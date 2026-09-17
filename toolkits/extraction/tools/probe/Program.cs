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
        Console.WriteLine($"Scanning PID {pid}...");

        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;

        Console.WriteLine("\n=== DUMP EntityAttribute + AnonymousStructureAttribute + EntityIdentifierType ===");
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            if (tn == "ServiceStudio.Model.EntityAttribute" || tn == "ServiceStudio.Model.AnonymousStructureAttribute" || tn == "ServiceStudio.Model.EntityIdentifierType")
            {
                Console.WriteLine($"\n--- {tn} ---");
                var name = ReadStr(obj, "_name");
                Console.WriteLine($"  Name: {name ?? "(null)"}");
                DumpAllFields(obj, heap);
            }
        }

        Console.WriteLine("\n=== DUMP Named Structure objects (ServiceStudio.Model.Structure) ===");
        int count = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            if (tn == "ServiceStudio.Model.Structure" || tn == "ServiceStudio.Model.NamedStructure")
            {
                Console.WriteLine($"\n--- {tn} ---");
                var name = ReadStr(obj, "_name");
                Console.WriteLine($"  Name: {name ?? "(null)"}");
                DumpAllFields(obj, heap);
                count++;
            }
        }
        Console.WriteLine($"\nTotal named structures: {count}");

        Console.WriteLine("\n=== DUMP ALL Entity objects with attributes ===");
        count = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.Entity")
            {
                Console.WriteLine($"\n--- Entity ---");
                var name = ReadStr(obj, "_name");
                Console.WriteLine($"  Name: {name ?? "(null)"}");
                var attrsRef = ReadRef(obj, "_attributes");
                Console.WriteLine($"  _attributes: 0x{attrsRef.Address:X}");
                count++;
            }
        }
        Console.WriteLine($"\nTotal Entity objects: {count}");

        Console.WriteLine("\n=== READ Entity _attributes SEQUENCE ===");
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.Entity")
            {
                var name = ReadStr(obj, "_name");
                if (name == null) continue;
                Console.WriteLine($"\nEntity: {name}");
                var attrsSeq = ReadRef(obj, "_attributes");
                if (attrsSeq.IsNull) { Console.WriteLine("  (no attributes)"); continue; }
                var size = ReadInt(attrsSeq, "size");
                var arrObj = ReadRef(attrsSeq, "array");
                Console.WriteLine($"  size={size}, array=0x{arrObj.Address:X}");
                if (!arrObj.IsNull && arrObj.IsArray)
                {
                    var arr = arrObj.AsArray();
                    for (int i = 0; i < Math.Min(size, arr.Length); i++)
                    {
                        try
                        {
                            var ptr = arr.GetValue<IntPtr>(i);
                            var addr = ptr.ToInt64();
                            if (addr == 0) continue;
                            var elem = heap.GetObject((ulong)addr);
                            if (elem.IsNull) continue;
                            var eName = ReadStr(elem, "_name") ?? "(null)";
                            var eType = ReadRef(elem, "_type");
                            var eTypeName = ReadStr(eType, "_name") ?? (eType.IsNull ? "null" : eType.Type?.Name ?? "?");
                            var isKey = ReadBool(elem, "_isKey");
                            Console.WriteLine($"    [{i}] {eName} : {eTypeName} {(isKey ? "[KEY]" : "")}");
                        }
                        catch (Exception ex) { Console.WriteLine($"    [{i}] ERROR: {ex.Message}"); }
                    }
                }
            }
        }

        Console.WriteLine("\n=== READ AnonymousStructure attributes ===");
        count = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.AnonymousStructure")
            {
                var name = ReadStr(obj, "_name");
                if (name == null) continue;
                Console.WriteLine($"\nStructure: {name ?? "(null)"}");
                var attrsRef = ReadRef(obj, "_attributes");
                if (!attrsRef.IsNull)
                {
                    var arrObj = ReadRef(attrsRef, "_array");
                    var size = ReadInt(attrsRef, "size");
                    Console.WriteLine($"  collection size={size}");
                    if (!arrObj.IsNull && arrObj.IsArray)
                    {
                        var arr = arrObj.AsArray();
                        for (int i = 0; i < Math.Min(size, arr.Length); i++)
                        {
                            try
                            {
                                var ptr = arr.GetValue<IntPtr>(i);
                                var addr = ptr.ToInt64();
                                if (addr == 0) continue;
                                var elem = heap.GetObject((ulong)addr);
                                var eName = ReadStr(elem, "_name") ?? "(null)";
                                var eType = ReadRef(elem, "_type");
                                var eTypeName = ReadStr(eType, "_name") ?? (eType.IsNull ? "null" : eType.Type?.Name ?? "?");
                                Console.WriteLine($"    [{i}] {eName} : {eTypeName}");
                            }
                            catch (Exception ex) { Console.WriteLine($"    [{i}] ERROR: {ex.Message}"); }
                        }
                    }
                }
                count++;
            }
        }
        Console.WriteLine($"\nTotal AnonymousStructure: {count}");

        Console.WriteLine("\n=== READ SiteProperty details ===");
        count = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.Variables+SiteProperty")
            {
                var name = ReadStr(obj, "_name");
                if (name == null) continue;
                var desc = ReadStr(obj, "_description");
                var typeObj = ReadRef(obj, "_type");
                var typeName = ReadStr(typeObj, "_name") ?? (typeObj.IsNull ? "null" : typeObj.Type?.Name ?? "?");
                var isReadOnly = ReadBool(obj, "_isReadOnlySiteProperty");
                Console.WriteLine($"  SiteProperty: {name} | type: {typeName} | readOnly: {isReadOnly} | desc: {desc ?? ""}");
                count++;
            }
        }
        Console.WriteLine($"\nTotal SiteProperties: {count}");
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
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): 0x{val.Address:X}");
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

    static bool ReadBool(ClrObject o, string want)
    {
        return ReadInt(o, want) != 0;
    }
}