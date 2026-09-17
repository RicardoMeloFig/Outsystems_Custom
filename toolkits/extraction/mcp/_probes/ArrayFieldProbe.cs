// ArrayFieldProbe - probe ClrArray element access
using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.Runtime;

int pid = 0;
Console.WriteLine("Attaching to PID " + pid);
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;

int found = 0;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (!fn.Contains("CustomActionInput") && !fn.Contains("CustomActionOutput")) continue;

    Console.WriteLine($"\n=== {fn} @ 0x{obj.Address:X16} ===");
    for (var bt = obj.Type; bt != null; bt = bt.BaseType)
    {
        Console.WriteLine($"  Base: {bt.Name}");
        foreach (var f in bt.Fields)
            Console.WriteLine($"    Field: {f.Type.Name} {f.Name}");
    }

    if (++found >= 3) break;
}

return 0;