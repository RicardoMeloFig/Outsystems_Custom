// ActionTypeProbe - scans Service Studio heap for all Action-related type names
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

int pid;
if (args.Length > 0 && int.TryParse(args[0], out pid))
{
    Console.WriteLine("Using PID " + pid);
}
else
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) { Console.Error.WriteLine("Service Studio is not running."); return 1; }
    pid = procs[0].Id;
    Console.WriteLine("Auto-detected PID " + pid);
}

Console.WriteLine("Attaching...");
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

var actionTypes = new SortedSet<string>();
var typeAddrs = new Dictionary<string, ulong>();

foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn.Contains("Action", StringComparison.OrdinalIgnoreCase) && !fn.Contains("+<>c"))
    {
        actionTypes.Add(fn);
        if (!typeAddrs.ContainsKey(fn)) typeAddrs[fn] = obj.Address;
    }
}

Console.WriteLine("\n=== ACTION TYPES FOUND (" + actionTypes.Count + ") ===");
foreach (var at in actionTypes)
{
    Console.WriteLine("  " + at);
}

// Now find objects of these types and show their names
Console.WriteLine("\n=== ACTION OBJECTS BY TYPE ===");
var actionsByType = new Dictionary<string, List<(ulong addr, string name)>>();

foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (!actionTypes.Contains(fn)) continue;

    string ReadStr(ClrObject o, string want)
    {
        for (var tt = o.Type; tt != null; tt = tt.BaseType)
            foreach (var f in tt.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
        return null;
    }

    var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
    if (!actionsByType.ContainsKey(fn)) actionsByType[fn] = new List<(ulong, string)>();
    actionsByType[fn].Add((obj.Address, nm ?? "(null)"));
}

foreach (var kvp in actionsByType.OrderBy(x => x.Key))
{
    Console.WriteLine("\n" + kvp.Key + " (" + kvp.Value.Count + "):");
    foreach (var item in kvp.Value.Take(20))
        Console.WriteLine("  0x" + item.addr.ToString("X16") + " : " + item.name);
    if (kvp.Value.Count > 20) Console.WriteLine("  ... and " + (kvp.Value.Count - 20) + " more");
}

return 0;