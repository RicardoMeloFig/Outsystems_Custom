// ActionDetailProbe - find Action type names and their field structure
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

int pid = 0;
Console.WriteLine("Attaching to PID " + pid);
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

var actionTypes = new SortedSet<string>();
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn.Contains("Action", StringComparison.OrdinalIgnoreCase) &&
        !fn.Contains("Reference") &&
        !fn.Contains("Func`") &&
        !fn.Contains("+<>c") &&
        (fn.Contains("ServiceStudio.Model") || fn.Contains("OutSystems.Model")))
    {
        actionTypes.Add(fn);
    }
}

Console.WriteLine("=== ACTION TYPES (non-reference) ===");
foreach (var at in actionTypes)
    Console.WriteLine("  " + at);

// Now find objects of the ExecuteServerAction type and look at their full field structure
Console.WriteLine("\n=== ExecuteServerAction OBJECTS ===");
var found = 0;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (!fn.EndsWith("ExecuteServerAction")) continue;

    Console.WriteLine($"\n--- 0x{obj.Address:X16} ---");
    for (var bt = t; bt != null; bt = bt.BaseType)
    {
        Console.WriteLine($"  BASE: {bt.Name}");
        foreach (var f in bt.Fields)
            Console.WriteLine($"    {f.Name}: {f.Type.Name} (IsObj={f.IsObjectReference})");
    }

    if (++found >= 2) break;
}

// Also look for something with "ServiceAction" not "ServerAction"
Console.WriteLine("\n\n=== TYPES CONTAINING 'ServiceAction' ===");
var serviceTypes = new SortedSet<string>();
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn.Contains("ServiceAction") && !fn.Contains("Reference"))
        serviceTypes.Add(fn);
}
foreach (var st in serviceTypes)
    Console.WriteLine("  " + st);

return 0;