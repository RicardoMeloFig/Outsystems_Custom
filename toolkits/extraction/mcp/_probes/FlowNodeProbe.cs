using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

class Program
{
    static void Main(string[] args)
    {
        int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
        if (pid == 0)
        {
            var procs = Process.GetProcessesByName("ServiceStudio");
            if (procs.Length == 0) { Console.WriteLine("Service Studio not running"); return; }
            pid = procs[0].Id;
        }
        Console.WriteLine($"Attaching to PID {pid}...");

        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;

        // ============================================================
        // PART 1: Dump If nodes - discover the condition field
        // ============================================================
        Console.WriteLine("\n============================================================");
        Console.WriteLine("PART 1: If nodes (ServiceStudio.Model.Nodes+If)");
        Console.WriteLine("============================================================");
        int ifCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            if (tn == "ServiceStudio.Model.Nodes+If")
            {
                ifCount++;
                if (ifCount > 3) break; // just sample a few
                Console.WriteLine($"\n--- If node #{ifCount} at 0x{obj.Address:X} ---");
                DumpAllFields(obj, heap, 2);
            }
        }
        Console.WriteLine($"\nTotal If nodes found: {ifCount}+");

        // ============================================================
        // PART 2: Dump Switch nodes - discover the condition field
        // ============================================================
        Console.WriteLine("\n============================================================");
        Console.WriteLine("PART 2: Switch nodes (ServiceStudio.Model.Nodes+Switch)");
        Console.WriteLine("============================================================");
        int swCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            if (tn == "ServiceStudio.Model.Nodes+Switch")
            {
                swCount++;
                if (swCount > 2) break;
                Console.WriteLine($"\n--- Switch node #{swCount} at 0x{obj.Address:X} ---");
                DumpAllFields(obj, heap, 2);
            }
        }
        Console.WriteLine($"\nTotal Switch nodes found: {swCount}+");

        // ============================================================
        // PART 3: Dump Assign nodes and their _assignments structure
        // ============================================================
        Console.WriteLine("\n============================================================");
        Console.WriteLine("PART 3: Assign nodes + _assignments expression types");
        Console.WriteLine("============================================================");
        int asgCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            if (tn == "ServiceStudio.Model.Nodes+Assign")
            {
                asgCount++;
                if (asgCount > 3) break; // just sample a few
                Console.WriteLine($"\n--- Assign node #{asgCount} at 0x{obj.Address:X} ---");
                var assignsColl = ReadRef(obj, "_assignments");
                Console.WriteLine($"  _assignments collection at 0x{assignsColl.Address:X}");
                var assigns = ReadColl(assignsColl, heap);
                Console.WriteLine($"  _assignments count: {assigns.Count}");
                foreach (var a in assigns)
                {
                    Console.WriteLine($"  -- Assignment object at 0x{a.Address:X} (type: {a.Type?.Name}) --");
                    DumpAllFields(a, heap, 4);
                    var variable = ReadRef(a, "_variable");
                    var value = ReadRef(a, "_value");
                    Console.WriteLine($"    _variable: 0x{variable.Address:X} (type: {variable.Type?.Name})");
                    Console.WriteLine($"    _value: 0x{value.Address:X} (type: {value.Type?.Name})");
                    if (!variable.IsNull)
                    {
                        Console.WriteLine($"    >> _variable expression tree:");
                        DumpExpressionTree(variable, heap, 6, 2);
                    }
                    if (!value.IsNull)
                    {
                        Console.WriteLine($"    >> _value expression tree:");
                        DumpExpressionTree(value, heap, 6, 2);
                    }
                }
            }
        }
        Console.WriteLine($"\nTotal Assign nodes found: {asgCount}+");

        // ============================================================
        // PART 4: Census of all expression element types on the heap
        // ============================================================
        Console.WriteLine("\n============================================================");
        Console.WriteLine("PART 4: Census of ExpressionElement subtypes");
        Console.WriteLine("============================================================");
        var typeCounts = new Dictionary<string, int>();
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            // Expression elements live under ServiceStudio.Model.Expressions+
            if (tn.StartsWith("ServiceStudio.Model.Expressions+"))
            {
                var shortName = tn.Split('+').Last();
                if (!typeCounts.ContainsKey(shortName)) typeCounts[shortName] = 0;
                typeCounts[shortName]++;
            }
        }
        foreach (var kv in typeCounts.OrderBy(k => k.Key))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        Console.WriteLine($"Total expression types: {typeCounts.Count}");
    }

    static void DumpExpressionTree(ClrObject elem, ClrHeap heap, int indent, int maxDepth)
    {
        if (elem.IsNull || maxDepth <= 0) return;
        var prefix = new string(' ', indent);
        var tn = elem.Type?.Name ?? "?";
        var shortName = tn.Contains("+") ? tn.Split('+').Last() : tn.Contains(".") ? tn.Split('.').Last() : tn;
        Console.WriteLine($"{prefix}[{shortName}] (full: {tn}) at 0x{elem.Address:X}");
        // Dump key fields
        for (var t = elem.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (!f.IsObjectReference) continue;
                try
                {
                    var val = f.ReadObject(elem.Address, false);
                    if (val.IsNull) continue;
                    var valType = val.Type?.Name ?? "?";
                    if (f.Type?.Name == "System.String")
                    {
                        var s = f.ReadString(elem.Address, false);
                        if (!string.IsNullOrEmpty(s) && s.Length < 200)
                            Console.WriteLine($"{prefix}  {f.Name} (string) = \"{s}\"");
                    }
                    else
                    {
                        Console.WriteLine($"{prefix}  {f.Name} ({valType}) at 0x{val.Address:X}");
                    }
                }
                catch { }
            }
        }
    }

    static List<ClrObject> ReadColl(ClrObject coll, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (coll.IsNull) return result;
        var arrObj = ReadRef(coll, "array");
        var size = ReadInt(coll, "size");
        if (!arrObj.IsNull && arrObj.IsArray && size > 0)
        {
            var arr = arrObj.AsArray();
            for (int i = 0; i < Math.Min(size, arr.Length); i++)
            {
                try
                {
                    var ptr = arr.GetValue<IntPtr>(i);
                    if (ptr == IntPtr.Zero) continue;
                    var elem = heap.GetObject((ulong)ptr.ToInt64());
                    if (!elem.IsNull) result.Add(elem);
                }
                catch { }
            }
        }
        return result;
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
                            var valName = ReadStr(val, "_name") ?? ReadStr(val, "name");
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): 0x{val.Address:X}{(valName != null ? $" name=\"{valName}\"" : "")}");
                        }
                    }
                    else if (f.IsValueType && f.Type?.Name != "System.String")
                    {
                        try
                        {
                            var val = f.Read<int>(obj.Address, false);
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): {val}");
                        }
                        catch { }
                    }
                    else if (f.Type?.Name == "System.String")
                    {
                        var s = f.ReadString(obj.Address, false);
                        if (!string.IsNullOrEmpty(s) && s.Length < 300)
                            Console.WriteLine($"{prefix}{f.Name} (string): \"{s}\"");
                    }
                }
                catch { }
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

    static ClrObject ReadRef(ClrObject o, string want)
    {
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Name == want)
                { try { var c = f.ReadObject(o.Address, false); if (!c.IsNull) return c; } catch { } }
        return default;
    }

    static int ReadInt(ClrObject o, string want)
    {
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (!f.IsObjectReference && f.Name == want)
                { try { return f.Read<int>(o.Address, false); } catch { } }
        return 0;
    }
}
