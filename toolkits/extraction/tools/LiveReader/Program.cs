// LiveReader (structured) - reads the module open in a running Service Studio
// process via ClrMD and prints a clean tree: each Entity/Structure -> its own
// Attributes. Maps attributes to owners via the objects' .parent back-references
// (robust; no C5-collection internals). Auto-detects the ServiceStudio PID.
// No .oml, no productKey, no server.
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
    Console.WriteLine($"Auto-detected Service Studio PID {pid}" + (procs.Length > 1 ? $"  ({procs.Length} instances; using first)" : ""));
}

Console.WriteLine("Attaching (readonly)...");
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;
Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}\n");

string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }
bool InModel(string n) => n.StartsWith("ServiceStudio.Model.", StringComparison.Ordinal) || n.StartsWith("OutSystems.Model.", StringComparison.Ordinal);
bool IsSkip(string n) => n.Contains('[') || n.Contains("Reference") || n.Contains("Abstract") || n.Contains("Descriptor") || n.Contains("Signature") || n.Contains("Enumerator") || n.Contains("Iterator") || n.Contains("+<>c");

string? ReadStr(ClrObject o, string want)
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

// 1) gather top-level elements keyed by address
var elementByAddr = new Dictionary<ulong, (string name, string kind)>();
var actionNodes = new List<ClrObject>();
string? moduleName = null;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (IsSkip(fn)) continue;
    if (fn.EndsWith(".OmlHeader", StringComparison.Ordinal))
    {
        if (moduleName == null)
        {
            // name field is obfuscated -> read all string fields, pick a plausible module name
            for (var bt = t; bt != null && moduleName == null; bt = bt.BaseType)
                foreach (var f in bt.Fields)
                    if (f.IsObjectReference && f.Type?.Name == "System.String")
                    { try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && !s.Contains('|') && !s.Contains('{') && !s.Contains(';') && s.IndexOfAny(new[] { '/', '+', '=' }) < 0) { moduleName = s; break; } } catch { } }
        }
        continue;
    }
    if (!InModel(fn)) continue;
    var leaf = Leaf(fn);
    if (leaf == "Entity" || leaf == "Structure" || leaf == "SystemStructure" || leaf == "StaticEntity")
    {
        var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
        if (nm != null) elementByAddr[obj.Address] = (nm, leaf);
    }
    else if (leaf.Contains("ExecuteClientAction") || leaf.Contains("ExecuteServerAction")) actionNodes.Add(obj);
}

// 2) gather attributes and map each to its owning element via .parent chain
var attrsByOwner = new Dictionary<ulong, List<(string name, string? label, string attrKind)>>();
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (IsSkip(fn) || !InModel(fn)) continue;
    var leaf = Leaf(fn);
    if (leaf != "EntityAttribute" && leaf != "StructureAttribute") continue;
    var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
    if (nm == null) continue;
    // walk parent chain to find an owning element
    var cur = obj;
    ulong ownerAddr = 0;
    for (int hop = 0; hop < 8 && !cur.IsNull; hop++)
    {
        var p = ReadRef(cur, "parent"); if (p.IsNull) p = ReadRef(cur, "_parent"); if (p.IsNull) p = ReadRef(cur, "ownerESpace");
        if (p.IsNull) break;
        if (elementByAddr.ContainsKey(p.Address)) { ownerAddr = p.Address; break; }
        cur = p;
    }
    if (!attrsByOwner.ContainsKey(ownerAddr)) attrsByOwner[ownerAddr] = new List<(string, string?, string)>();
    attrsByOwner[ownerAddr].Add((nm, ReadStr(obj, "_label"), leaf));
}

Console.WriteLine($"MODULE: {moduleName ?? "(unknown)"}   (PID {pid})\n");

var entities = elementByAddr.Where(kv => kv.Value.kind == "Entity").OrderBy(kv => kv.Value.name).ToList();
var structures = elementByAddr.Where(kv => kv.Value.kind != "Entity").OrderBy(kv => kv.Value.name).ToList();

Console.WriteLine($"ENTITIES ({entities.Count}):");
foreach (var kv in entities)
{
    Console.WriteLine($"  - {kv.Value.name}");
    if (attrsByOwner.TryGetValue(kv.Key, out var attrs) && attrs.Count > 0)
    {
        foreach (var a in attrs.OrderBy(a => a.name))
            Console.WriteLine($"      . {a.name}" + (a.label != null && a.label != a.name ? $"  [{a.label}]" : ""));
    }
}
Console.WriteLine();

Console.WriteLine($"STRUCTURES ({structures.Count}):");
foreach (var kv in structures)
{
    var tag = kv.Value.kind == "SystemStructure" ? "  [system]" : (kv.Value.kind == "StaticEntity" ? "  [static entity]" : "");
    Console.WriteLine($"  - {kv.Value.name}{tag}");
    if (attrsByOwner.TryGetValue(kv.Key, out var attrs) && attrs.Count > 0)
    {
        foreach (var a in attrs.OrderBy(a => a.name))
            Console.WriteLine($"      . {a.name}" + (a.label != null && a.label != a.name ? $"  [{a.label}]" : ""));
    }
}
Console.WriteLine();

if (actionNodes.Count > 0)
{
    var names = actionNodes.DistinctBy(x => x.Address).Select(o => ReadStr(o, "_name") ?? ReadStr(o, "name"))
        .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    Console.WriteLine($"ACTION NODES ({names.Count}) [template/layout calls]:");
    foreach (var n in names) Console.WriteLine($"  - {n}");
}
Console.WriteLine("\nDone.");
return 0;
