// HeapProbe - read-only ClrMD attach to a running Service Studio process.
// Reports which plugin types are actually INSTANTIATED on the heap (so we learn
// which IDiscoverable base class SS eagerly constructs) and confirms whether our
// OsLiveBridge types are loaded/instantiated. No mutation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

var procs = Process.GetProcessesByName("ServiceStudio");
if (procs.Length == 0) { Console.WriteLine("SS not running"); return 0; }
if (procs.Length > 1) { Console.WriteLine("Multiple SS: " + string.Join(", ", procs.Select(p => p.Id))); }
int pid = procs[0].Id;
Console.WriteLine("Attaching (readonly) to SS pid " + pid);
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

string ReadStr(ClrObject o, string want)
{
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
            { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
    return null;
}

var svcActions = new List<string>();
var counts = new Dictionary<string, int>();
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t == null) continue;
    var name = t.Name ?? "";
    if (name == "ServiceAPIMethod" || (t.Name ?? "").EndsWith("ServiceAPIMethod"))
    {
        var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name") ?? "?";
        svcActions.Add(nm);
    }
    if (name.Contains("DietAutomation") || name.Contains("SampleBlocks") ||
        name.Contains("OsLiveBridge") || name.Contains("ToolEntriesProvider") ||
        name.Contains("PluginDescriptor") || name.Contains("SuggestionsProvider") ||
        name.Contains("ModelFeatures") || name.Contains("CustomCommand"))
    {
        counts.TryGetValue(name, out var c); counts[name] = c + 1;
    }
}
Console.WriteLine("\n=== ServiceAPIMethod instances (live) ===");
Console.WriteLine($"  count={svcActions.Count}: {string.Join(", ", svcActions)}");

Console.WriteLine("\n=== instantiated types on heap (plugin/provider/descriptor) ===");
if (counts.Count == 0) Console.WriteLine("  (none matched)");
foreach (var kv in counts.OrderBy(k => k.Key))
    Console.WriteLine($"  {kv.Value,5}  {kv.Key}");

Console.WriteLine("\n=== loaded assemblies (OsLiveBridge / DietAutomation / SampleBlocks) ===");
foreach (var mod in runtime.EnumerateModules())
{
    var n = mod.Name ?? "";
    if (n.Contains("OsLiveBridge") || n.Contains("DietAutomation") || n.Contains("SampleBlocks"))
        Console.WriteLine("  " + n);
}
return 0;
