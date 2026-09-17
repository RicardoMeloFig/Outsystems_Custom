using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Runtime;

class Program
{
    static void Main(string[] args)
    {
        int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
        Console.WriteLine($"Scanning PID {pid}...");

        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;

        Console.WriteLine("\n=== SCANNING FOR ACTION TYPES ===");
        var actionTypes = new HashSet<string>();
        foreach (var type in heap.EnumerateTypes())
        {
            var name = type.Name ?? "";
            if (name.Contains("Action") || name.Contains("Flow") || name.Contains("Parameter"))
                actionTypes.Add(name);
        }
        foreach (var t in actionTypes.OrderBy(x => x))
            Console.WriteLine($"  {t}");

        Console.WriteLine("\n=== DUMP ALL Action OBJECTS (first 20) ===");
        int count = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (fn.Contains("Action") && !fn.Contains("Reference"))
            {
                Console.WriteLine($"\n--- {fn} ---");
                var name = ReadStr(obj, "_name");
                Console.WriteLine($"  Name: {name ?? "(null)"}");
                DumpAllFields(obj, heap);
                count++;
                if (count >= 20) break;
            }
        }
    }

    static void DumpAllFields(ClrObject obj, ClrHeap heap, int indent = 0)
    {
        var prefix = new string(' ', indent);
        for (var t = obj.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                var ftype = f.Type?.Name ?? "null";
                try
                {
                    if (f.IsObjectReference)
                    {
                        var val = f.ReadObject(obj.Address, false);
                        if (!val.IsNull)
                        {
                            if (f.Name == "_name" || f.Name == "innerlist" || f.Name == "array" || f.Name.EndsWith("Parameter"))
                                Console.WriteLine($"{prefix}{f.Name} ({ftype}): 0x{val.Address:X}");
                        }
                        else
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): null");
                    }
                    else if (f.IsValueType && f.Type?.Name != "System.String")
                    {
                        try {
                            var val = f.Read<int>(obj.Address, false);
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): {val}");
                        } catch {}
                    }
                }
                catch {}
            }
        }
    }

    static string ReadStr(ClrObject o, string want)
    {
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
        return null;
    }
}