// ISSequenceProbe - probe ISSequence structure and UserAction params
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

// Find the UserAction's _inputParameters ISSequence object and dump it
Console.WriteLine("\n=== USERACTION INPUT PARAMETERS SEQUENCE DUMP ===");
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (!fn.EndsWith("Flows+UserAction")) continue;

    var inp = ReadRef(obj, "_inputParameters");
    if (inp.IsNull) { Console.WriteLine("  (null)"); continue; }

    Console.WriteLine($"\n--- ISSequence @ 0x{inp.Address:X16} [{inp.Type.Name}] ---");
    for (var bt = inp.Type; bt != null; bt = bt.BaseType)
    {
        Console.WriteLine($"  BASE: {bt.Name}");
        foreach (var f in bt.Fields)
            Console.WriteLine($"    {f.Name}: {f.Type?.Name ?? "?"} (obj={f.IsObjectReference})");
    }

    // Try to enumerate elements if it's an array
    if (inp.IsArray)
    {
        var arr = inp.AsArray();
        Console.WriteLine($"  Array length: {arr.Length}");
for (int i = 0; i < Math.Min(arr.Length, 10); i++)
        {
            var elem = (ClrObject)arr.GetValue<object>(i);
            if (!elem.IsNull)
            {
                var nm = ReadStr(elem, "_name");
                Console.WriteLine($"    [{i}]: 0x{elem.Address:X16} _name=\"{nm ?? "(null)"}\"");
            }
        }
    }
    else
    {
        // Try to find an enumerator or indexer
        Console.WriteLine("  Not an array, checking for _items or similar...");
        for (var bt = inp.Type; bt != null; bt = bt.BaseType)
        {
            foreach (var f in bt.Fields)
            {
                if (f.IsObjectReference && (f.Name.Contains("Item") || f.Name.Contains("item") || f.Name.Contains("Array") || f.Name.Contains("Buffer")))
                {
                    try {
                        var refObj = f.ReadObject(inp.Address, false);
                        if (!refObj.IsNull && refObj.IsArray)
                        {
                            var arr = refObj.AsArray();
                            Console.WriteLine($"    {f.Name}: array len={arr.Length}");
                            for (int i = 0; i < Math.Min(arr.Length, 10); i++)
                            {
                                var elem = (ClrObject)arr.GetValue<object>(i);
                                if (!elem.IsNull)
                                {
                                    var nm = ReadStr(elem, "_name");
                                    Console.WriteLine($"      [{i}]: 0x{elem.Address:X16} _name=\"{nm ?? "(null)"}\"");
                                }
                            }
                        }
                    } catch { }
                }
            }
        }
    }
    break;
}

// Also look for ISSequence type definition
Console.WriteLine("\n\n=== ISSequence TYPE FIELDS ===");
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn == "ServiceStudio.Model.ISSSequence`1" || fn.StartsWith("ServiceStudio.Model.ISSSequence"))
    {
        Console.WriteLine($"\n--- ISSequence @ 0x{obj.Address:X16} [{fn}] ---");
        for (var bt = t; bt != null; bt = bt.BaseType)
        {
            Console.WriteLine($"  BASE: {bt.Name}");
            foreach (var f in bt.Fields)
                Console.WriteLine($"    {f.Name}: {f.Type?.Name ?? "?"}");
        }
        break;
    }
}

return 0;