// Quick probe - dump ParsedExpression fields to find expression text
using System;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

int pid = 0;
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

int found = 0;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn != "ServiceStudio.Model.ParsedExpression" && fn != "OutSystems.Model.ParsedExpression" &&
        !fn.EndsWith(".ParsedExpression")) continue;

    Console.WriteLine($"\n--- ParsedExpression 0x{obj.Address:X16} [{fn}] ---");
    for (var bt = t; bt != null; bt = bt.BaseType)
    {
        Console.WriteLine($"  BASE: {bt.Name}");
        foreach (var f in bt.Fields)
        {
            var ftype = f.Type?.Name ?? "?";
            try
            {
                if (f.IsObjectReference)
                {
                    if (ftype == "System.String")
                    {
                        var s = f.ReadString(obj.Address, false);
                        if (s != null && s.Length > 0 && s.Length < 500)
                            Console.WriteLine($"    {f.Name} (String): \"{s}\"");
                        else if (s != null)
                            Console.WriteLine($"    {f.Name} (String): [{s.Length} chars]");
                        else
                            Console.WriteLine($"    {f.Name} (String): null");
                    }
                    else
                    {
                        var val = f.ReadObject(obj.Address, false);
                        if (!val.IsNull)
                            Console.WriteLine($"    {f.Name} ({ftype}): 0x{val.Address:X} [{val.Type?.Name}]");
                    }
                }
                else
                {
                    if (ftype == "System.Boolean")
                        Console.WriteLine($"    {f.Name} ({ftype}): {f.Read<bool>(obj.Address, false)}");
                    else if (ftype == "System.Int32")
                        Console.WriteLine($"    {f.Name} ({ftype}): {f.Read<int>(obj.Address, false)}");
                }
            }
            catch { }
        }
    }
    if (++found >= 3) break;
}

// Also dump an AbstractLink to find source/destination fields
Console.WriteLine("\n\n=== ABSTRACT LINK FIELDS ===");
found = 0;
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn != "ServiceStudio.Model.Links+Sequence" && fn != "ServiceStudio.Model.Links+Condition") continue;

    Console.WriteLine($"\n--- {fn} 0x{obj.Address:X16} ---");
    for (var bt = t; bt != null; bt = bt.BaseType)
    {
        var leaf = bt.Name ?? "?";
        if (leaf.Contains("+")) leaf = leaf.Split('+').Last();
        if (leaf.Contains(".")) leaf = leaf.Split('.').Last();
        Console.WriteLine($"  BASE: {leaf}");
        foreach (var f in bt.Fields)
        {
            if (!f.IsObjectReference) continue;
            var ftype = f.Type?.Name ?? "?";
            var leafType = ftype.Contains("+") ? ftype.Split('+').Last() : ftype.Contains(".") ? ftype.Split('.').Last() : ftype;
            try
            {
                var val = f.ReadObject(obj.Address, false);
                if (val.IsNull) continue;
                var valName = ReadStr(val, "_name") ?? "";
                if (ftype == "System.String")
                {
                    var s = f.ReadString(obj.Address, false);
                    if (!string.IsNullOrEmpty(s)) Console.WriteLine($"    {f.Name} (String): \"{s}\"");
                }
                else
                    Console.WriteLine($"    {f.Name} ({leafType}): \"{valName}\" 0x{val.Address:X16}");
            }
            catch { }
        }
    }
    if (++found >= 2) break;
}

return 0;
