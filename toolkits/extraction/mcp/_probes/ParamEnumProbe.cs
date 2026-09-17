// ParamEnumProbe - enumerate input params via C5 GuardedList
using System;
using System.Collections;
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

int ReadInt(ClrObject o, string want)
{
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (!f.IsObjectReference && f.Name == want)
            { try { return f.Read<int>(o.Address, false); } catch { } }
    return 0;
}

ClrObject GetInnerList(ClrObject seqObj)
{
    if (seqObj.IsNull) return default;
    var innerlist = ReadRef(seqObj, "innerlist");
    if (!innerlist.IsNull) return innerlist;

    var underlying = ReadRef(seqObj, "underlying");
    return underlying;
}

var found = 0;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (!fn.EndsWith("Flows+UserAction")) continue;

    var inp = ReadRef(obj, "_inputParameters");
    if (inp.IsNull) { Console.WriteLine("No input params"); continue; }

    var inpName = ReadStr(obj, "_name");
    Console.WriteLine($"\n=== UserAction: {inpName} ===");

    // The ISSequence adapter wraps a GuardedList
    Console.WriteLine($"Input params type: {inp.Type?.Name}");

    // Get the GuardedList (innerlist)
    var innerlist = GetInnerList(inp);
    Console.WriteLine($"innerlist: 0x{innerlist.Address:X16} type={innerlist.Type?.Name}");

    if (!innerlist.IsNull)
    {
        // GuardedList has 'innerlist' too sometimes
        var underlying = ReadRef(innerlist, "innerlist");
        if (!underlying.IsNull) innerlist = underlying;

        // Try to find the array inside GuardedList
        Console.WriteLine($"Looking in GuardedList fields...");
        for (var bt = innerlist.Type; bt != null; bt = bt.BaseType)
        {
            Console.WriteLine($"  BASE: {bt.Name}");
            foreach (var f in bt.Fields)
                Console.WriteLine($"    {f.Name}: {f.Type?.Name} (obj={f.IsObjectReference})");
        }

        // Check if innerlist itself is an array
        if (innerlist.IsArray)
        {
            var arr = innerlist.AsArray();
            Console.WriteLine($"  innerlist IS ARRAY len={arr.Length}");
            for (int i = 0; i < arr.Length; i++)
            {
                try {
                    var ptr = arr.GetValue<IntPtr>(i);
                    var addr = ptr.ToInt64();
                    if (addr != 0)
                    {
                        var elem = heap.GetObject((ulong)addr);
                        if (!elem.IsNull)
                        {
                            var nm = ReadStr(elem, "_name");
                            Console.WriteLine($"    [{i}] 0x{addr:X16} _name=\"{nm}\"");
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"    [{i}] ERROR: {ex.Message}");
                }
            }
        }
    }

    if (++found >= 1) break;
}

return 0;