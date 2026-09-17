// ThemeProbe v3 - reconstruct CSS from LightweightExpression.lightweightElements
// and dump TextElement / LightweightReferenceElement schemas.
// Usage: dotnet run -c Release -- [PID]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

int pid;
if (args.Length > 0 && int.TryParse(args[0], out pid)) Console.WriteLine($"Using PID {pid}");
else
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) { Console.Error.WriteLine("Service Studio is not running."); return 1; }
    pid = procs[0].Id;
}

using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;
Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}\n");

string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }
string ReadStr(ClrObject o, string want)
{
    if (o.IsNull) return null;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
            { try { return f.ReadString(o.Address, false); } catch { } }
    return null;
}
ClrObject ReadRef(ClrObject o, string want)
{
    if (o.IsNull) return default;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Name == want)
            { try { var c = f.ReadObject(o.Address, false); if (!c.IsNull) return c; } catch { } }
    return default;
}
int ReadInt(ClrObject o, string want)
{
    if (o.IsNull) return 0;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (!f.IsObjectReference && f.Name == want)
            { try { return f.Read<int>(o.Address, false); } catch { } }
    return 0;
}
// Read a List<T>-style backing (_items array + _size) OR ISSCollection (array + size)
List<ClrObject> ReadList(ClrObject list)
{
    var result = new List<ClrObject>();
    if (list.IsNull) return result;
    var items = ReadRef(list, "_items");
    if (items.IsNull) items = ReadRef(list, "array");
    var size = ReadInt(list, "_size");
    if (size <= 0) size = ReadInt(list, "size");
    if (items.IsNull || !items.IsArray || size <= 0) return result;
    var arr = items.AsArray();
    for (int i = 0; i < Math.Min(size, arr.Length); i++)
    { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { } }
    return result;
}

// resolve a reference element's target name (try many field names + generic object-name walk)
string ResolveRefName(ClrObject refElem)
{
    if (refElem.IsNull) return null;
    foreach (var fn in new[] { "reference", "_reference", "referedObject", "_referedObject", "referencedObject", "_target", "target", "value", "_value" })
    {
        var r = ReadRef(refElem, fn);
        if (!r.IsNull)
        {
            var n = ReadStr(r, "_name") ?? ReadStr(r, "name") ?? ReadStr(r, "refName");
            if (!string.IsNullOrEmpty(n)) return n;
            // walk one level
            for (var t = r.Type; t != null; t = t.BaseType)
                foreach (var f in t.Fields)
                    if (f.IsObjectReference && f.Type?.Name != "System.String")
                    { try { var v = f.ReadObject(r.Address, false); if (!v.IsNull) { var vn = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(vn)) return vn; } } catch { } }
        }
    }
    return ReadStr(refElem, "_name") ?? ReadStr(refElem, "refName") ?? "?";
}

// shallow field dump (non-boilerplate)
var skip = new HashSet<string>(StringComparer.Ordinal) {
    "rawProperties","parent","_textResources","_isZombie","<IsToolbarObject>k__BackingField","isNew",
    "createdOnCommandGeneration","ownerESpace","ownerCompilationUnit","hashAggregator","invalidatingHashes",
    "compilationInterfaceHashNeverInvalidated","verifyCache","viewGeneration","viewGenerationOfLastReferersRefresh",
    "childCollectionsViewGeneration","scopeEntryChildrenGeneration","id","key","match","forcedMatch","saveCaches",
    "isLoadingOrImporting","isImporting","wasDeleted","hashesBeingSerialized","disableViewGenerationIncrease",
    "lastModifiedByCommand","isSettingName","nameClashPending","_collectionMetadata","insideOnBeforeNameChange",
    "scope","topLevelScopeEntriesCache","insideGetMergeId","objectsInPathCache","replacement","referenceElementDepth",
    "_metadata","<Order>k__BackingField","<ESpaceTreeFolderOrder>k__BackingField","<ESpaceTreeTabOrder>k__BackingField"
};
void DumpFields(ClrObject o, string label)
{
    if (o.IsNull) { Console.WriteLine($"  {label}: null"); return; }
    Console.WriteLine($"  {label}: [{o.Type?.Name ?? "?"}] @0x{o.Address:X}");
    var seen = new HashSet<string>();
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
        {
            if (!seen.Add(f.Name)) continue;
            if (skip.Contains(f.Name)) continue;
            var ftn = f.Type?.Name ?? "?";
            try
            {
                if (f.IsObjectReference)
                {
                    if (ftn == "System.String") { var s = f.ReadString(o.Address, false); Console.WriteLine($"    {f.Name} : string = {(s == null ? "null" : "\"" + (s.Length > 120 ? s.Substring(0, 120) + "..." : s) + "\"")}"); }
                    else { var v = f.ReadObject(o.Address, false); string vdesc = v.IsNull ? "null" : ("[" + Leaf(v.Type?.Name ?? "?") + "] @0x" + v.Address.ToString("X")); Console.WriteLine($"    {f.Name} : {Leaf(ftn)} = {vdesc}"); }
                }
                else if (f.IsValueType) { try { Console.WriteLine($"    {f.Name} : {Leaf(ftn)} = {f.Read<int>(o.Address, false)}"); } catch { } }
            }
            catch { }
        }
}

// ===== Reconstruct CSS from each Theme's _cssSource LightweightExpression =====
Console.WriteLine(new string('=', 70));
Console.WriteLine("CSS RECONSTRUCTION from LightweightExpression.lightweightElements");
Console.WriteLine(new string('=', 70));

bool dumpedTextElemSchema = false, dumpedRefElemSchema = false;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn != "ServiceStudio.Model.NewRuntime.Theme" && fn != "ServiceStudio.Model.Theme") continue;
    var themeName = ReadStr(obj, "_name") ?? "(unnamed)";
    Console.WriteLine($"\n### Theme: {themeName} ###");
    foreach (var ssField in new[] { "_styleSheet", "_invisibleStyleSheet" })
    {
        var ss = ReadRef(obj, ssField);
        if (ss.IsNull) continue;
        foreach (var ef in new[] { "_cssSource", "_finalCssSource", "_userCssSource", "_generatedCssSource" })
        {
            var expr = ReadRef(ss, ef);
            if (expr.IsNull) continue;
            var elements = ReadRef(expr, "lightweightElements");
            var list = ReadList(elements);
            if (list.Count == 0) continue;
            Console.WriteLine($"\n-- {ssField}.{ef}: {list.Count} elements --");
            var sb = new StringBuilder();
            int te = 0, re = 0, oe = 0;
            foreach (var el in list)
            {
                var leaf = Leaf(el.Type?.Name ?? "?");
                if (leaf == "TextElement")
                {
                    te++;
                    if (!dumpedTextElemSchema) { Console.WriteLine("\n  [TextElement schema sample:]"); DumpFields(el, "TextElement"); dumpedTextElemSchema = true; }
                    // try common text field names
                    var text = ReadStr(el, "value") ?? ReadStr(el, "_value") ?? ReadStr(el, "text") ?? ReadStr(el, "_text") ?? ReadStr(el, "Text") ?? ReadStr(el, "Value");
                    if (text == null)
                    {
                        // dump all string fields of this element
                        for (var tt = el.Type; tt != null && text == null; tt = tt.BaseType)
                            foreach (var f in tt.Fields)
                                if (f.IsObjectReference && f.Type?.Name == "System.String")
                                { try { var s = f.ReadString(el.Address, false); if (!string.IsNullOrEmpty(s)) { text = s; } } catch { } }
                    }
                    if (text != null) sb.Append(text);
                }
                else if (leaf == "LightweightReferenceElement" || leaf.Contains("Reference"))
                {
                    re++;
                    if (!dumpedRefElemSchema) { Console.WriteLine("\n  [LightweightReferenceElement schema sample:]"); DumpFields(el, "RefElement"); dumpedRefElemSchema = true; }
                    var nm = ResolveRefName(el);
                    sb.Append("{" + nm + "}");
                    // Always append suffix even if reference name failed (prevents mid-statement truncation)
                    var suffix = ReadStr(el, "suffix") ?? ReadStr(el, "_suffix");
                    if (suffix != null) sb.Append(suffix);
                }
                else { oe++; sb.Append($"/*[{leaf}]*/"); }
            }
            Console.WriteLine($"  element types: TextElement={te}, Reference={re}, other={oe}");
            var css = sb.ToString();
            Console.WriteLine($"  reconstructed CSS length: {css.Length} chars");
            Console.WriteLine("  --- CSS preview (first 1500 chars) ---");
            Console.WriteLine(css.Length > 1500 ? css.Substring(0, 1500) + "\n... (truncated)" : css);
        }
    }
}

Console.WriteLine("\nDone.");
return 0;
