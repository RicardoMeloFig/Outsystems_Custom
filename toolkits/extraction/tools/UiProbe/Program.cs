// UiProbe - discovery tool. Scans the running Service Studio heap and reports
// every ServiceStudio.Model.* / OutSystems.Model.* type with counts, plus field
// names and a sample _name for UI-relevant types. Used to discover the exact
// model schema before writing the generic UiExtractor.
// Usage: dotnet run -c Release -- [PID]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

int pid;
if (args.Length > 0 && int.TryParse(args[0], out pid)) Console.WriteLine($"Using PID {pid}");
else
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) { Console.Error.WriteLine("Service Studio is not running."); return 1; }
    pid = procs[0].Id;
    Console.WriteLine($"Auto-detected Service Studio PID {pid}");
}

using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;
Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}\n");

string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }
bool InModel(string n) => n.StartsWith("ServiceStudio.Model.", StringComparison.Ordinal) || n.StartsWith("OutSystems.Model.", StringComparison.Ordinal);
bool IsSkip(string n) => n.Contains('[') || n.Contains("+<>c") || n.Contains("Enumerator") || n.Contains("Iterator");

string ReadStr(ClrObject o, string want)
{
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
            { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
    return null;
}
ClrObject ReadRef(ClrObject o, string want)
{
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Name == want)
            { try { var c = f.ReadObject(o.Address, false); if (!c.IsNull) return c; } catch { } }
    return default;
}

// module name
string moduleName = null;
var typeCounts = new Dictionary<string, int>();
var typeSamples = new Dictionary<string, ClrObject>();
string[] uiKeywords = { "Screen", "WebBlock", "Block", "Theme", "Widget", "Css", "Style",
    "Container", "Form", "Input", "Button", "Expression", "Link", "Image", "Event",
    "Handler", "Flow", "Menu", "Popup", "BlockInstance", "Placeholder", "WebFlow" };

foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (IsSkip(fn)) continue;
    if (fn.EndsWith(".OmlHeader", StringComparison.Ordinal) && moduleName == null)
    {
        for (var bt = t; bt != null && moduleName == null; bt = bt.BaseType)
            foreach (var f in bt.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String")
                { try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && s.IndexOfAny(new[] { '|', '{', ';', '/', '+', '=' }) < 0) { moduleName = s; break; } } catch { } }
        continue;
    }
    if (!InModel(fn)) continue;
    if (!typeCounts.ContainsKey(fn)) { typeCounts[fn] = 0; typeSamples[fn] = obj; }
    typeCounts[fn]++;
}

Console.WriteLine($"MODULE: {moduleName ?? "(unknown)"}   (PID {pid})\n");

Console.WriteLine("=== FULL TYPE CENSUS (ServiceStudio.Model.* / OutSystems.Model.*) ===");
foreach (var kv in typeCounts.OrderBy(x => x.Key))
    Console.WriteLine($"  {kv.Value,5}  {kv.Key}");

Console.WriteLine("\n=== UI-RELEVANT TYPES (field dump + sample name) ===");
foreach (var kv in typeCounts.OrderBy(x => x.Key))
{
    var leaf = Leaf(kv.Key);
    bool uiMatch = uiKeywords.Any(k => leaf.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
    if (!uiMatch) continue;
    var sample = typeSamples[kv.Key];
    var nm = ReadStr(sample, "_name") ?? ReadStr(sample, "name") ?? "(no name)";
    Console.WriteLine($"\n--- {kv.Key}  (count={kv.Value})  sampleName={nm} ---");
    var fields = new HashSet<string>();
    for (var t = sample.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
        {
            if (fields.Contains(f.Name)) continue;
            fields.Add(f.Name);
            var ftn = f.Type?.Name ?? "?";
            string val = "";
            try
            {
                if (f.IsObjectReference)
                {
                    if (ftn == "System.String") { var s = f.ReadString(sample.Address, false); val = s != null ? $"=\"{s.Substring(0, Math.Min(s.Length, 60))}\"" : "=null"; }
                    else { var v = f.ReadObject(sample.Address, false); val = v.IsNull ? "=null" : $"=0x{v.Address:X}"; }
                }
                else if (f.IsValueType) { try { val = "=" + f.Read<int>(sample.Address, false); } catch { } }
            }
            catch { }
            Console.WriteLine($"    {f.Name} : {ftn} {val}");
        }
}

Console.WriteLine("\nDone.");
return 0;
