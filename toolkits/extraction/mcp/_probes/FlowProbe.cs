// FlowProbe - deep-dive into an action object to find flow/node structures
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
string targetName = args.Length > 1 ? args[1] : "";

Console.WriteLine($"Attaching to PID {pid}, looking for action '{targetName}'...");
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

// Find the action object
ClrObject actionObj = default;
string actionType = "";
foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (fn.EndsWith("Flows+UserAction") || fn == "ServiceStudio.Model.Flows+ServiceAPIMethod" || fn.EndsWith("NRFlows+ClientActionFlow"))
    {
        var nm = ReadStr(obj, "_name");
        if (nm != null && nm.Equals(targetName, StringComparison.OrdinalIgnoreCase))
        {
            actionObj = obj;
            actionType = fn;
            break;
        }
    }
}

if (actionObj.IsNull)
{
    Console.WriteLine($"Action '{targetName}' not found!");
    return 1;
}

Console.WriteLine($"\nFound: {targetName} [{actionType}] at 0x{actionObj.Address:X16}");

// Dump all fields recursively (depth-limited)
var visited = new HashSet<ulong>();
void DumpFields(ClrObject o, string prefix, int depth, int maxDepth)
{
    if (depth > maxDepth) return;
    if (o.IsNull) return;
    if (visited.Contains(o.Address)) { Console.WriteLine($"{prefix}(cycle back to 0x{o.Address:X})"); return; }
    visited.Add(o.Address);

    for (var t = o.Type; t != null; t = t.BaseType)
    {
        foreach (var f in t.Fields)
        {
            var fname = f.Name;
            var ftype = f.Type?.Name ?? "?";
            try
            {
                if (f.IsObjectReference)
                {
                    var val = f.ReadObject(o.Address, false);
                    if (val.IsNull)
                    {
                        Console.WriteLine($"{prefix}{fname} ({ftype}): null");
                    }
                    else if (ftype == "System.String")
                    {
                        try { var s = f.ReadString(o.Address, false); Console.WriteLine($"{prefix}{fname} (String): \"{s}\""); }
                        catch { Console.WriteLine($"{prefix}{fname} (String): (unreadable)"); }
                    }
                    else if (val.IsArray)
                    {
                        var arr = val.AsArray();
                        Console.WriteLine($"{prefix}{fname} ({ftype}[{arr.Length}]):");
                        for (int i = 0; i < Math.Min(arr.Length, 10); i++)
                        {
                            try
                            {
                                var ptr = arr.GetValue<IntPtr>(i);
                                if (ptr == IntPtr.Zero) continue;
                                var elem = heap.GetObject((ulong)ptr.ToInt64());
                                if (!elem.IsNull)
                                    Console.WriteLine($"{prefix}  [{i}] 0x{elem.Address:X} [{elem.Type?.Name}]");
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        var leafName = ftype.Contains("+") ? ftype.Split('+').Last() : ftype.Contains(".") ? ftype.Split('.').Last() : ftype;
                        Console.WriteLine($"{prefix}{fname} ({leafName}): 0x{val.Address:X}");
                        // Recurse into interesting types (skip common noise)
                        if (!ftype.Contains("Reference") && !ftype.Contains("Descriptor") &&
                            !ftype.Contains("Signature") && !ftype.Contains("Enumerator") &&
                            !ftype.Contains("Iterator") && !ftype.Contains("+<>c") &&
                            !ftype.StartsWith("System.") && !ftype.Contains("Wrapper"))
                        {
                            DumpFields(val, prefix + "  ", depth + 1, maxDepth);
                        }
                    }
                }
                else if (f.IsValueType && ftype != "System.String")
                {
                    try
                    {
                        if (ftype == "System.Boolean")
                        {
                            var val = f.Read<bool>(o.Address, false);
                            Console.WriteLine($"{prefix}{fname} ({ftype}): {val}");
                        }
                        else if (ftype == "System.Int32" || ftype == "System.Int64")
                        {
                            var val = f.Read<int>(o.Address, false);
                            Console.WriteLine($"{prefix}{fname} ({ftype}): {val}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}

Console.WriteLine("\n=== FULL FIELD DUMP (depth 3) ===\n");
DumpFields(actionObj, "", 0, 3);

// Now specifically look for collection-like fields that could hold flow nodes
Console.WriteLine("\n\n=== SEARCHING FOR NODE/FLOW COLLECTIONS ===");
visited.Clear();
for (var t = actionObj.Type; t != null; t = t.BaseType)
{
    foreach (var f in t.Fields)
    {
        if (!f.IsObjectReference) continue;
        var fname = f.Name.ToLowerInvariant();
        if (fname.Contains("node") || fname.Contains("flow") || fname.Contains("element") ||
            fname.Contains("sequence") || fname.Contains("child") || fname.Contains("step") ||
            fname.Contains("block") || fname.Contains("body") || fname.Contains("content"))
        {
            Console.WriteLine($"\n*** Candidate: {f.Name} ({f.Type?.Name}) ***");
            try
            {
                var val = f.ReadObject(actionObj.Address, false);
                if (!val.IsNull)
                {
                    Console.WriteLine($"  Address: 0x{val.Address:X}");
                    // Try to read as a sequence (C5/SCG)
                    var size = ReadInt(val, "size");
                    if (size > 0)
                    {
                        Console.WriteLine($"  size={size}");
                        var arrObj = ReadRef(val, "array");
                        if (!arrObj.IsNull && arrObj.IsArray)
                        {
                            var arr = arrObj.AsArray();
                            Console.WriteLine($"  array length={arr.Length}");
                            for (int i = 0; i < Math.Min(size, 20); i++)
                            {
                                try
                                {
                                    var ptr = arr.GetValue<IntPtr>(i);
                                    if (ptr == IntPtr.Zero) continue;
                                    var elem = heap.GetObject((ulong)ptr.ToInt64());
                                    if (!elem.IsNull)
                                    {
                                        var elemType = elem.Type?.Name ?? "?";
                                        var leaf = elemType.Contains("+") ? elemType.Split('+').Last() : elemType.Contains(".") ? elemType.Split('.').Last() : elemType;
                                        var elemName = ReadStr(elem, "_name") ?? ReadStr(elem, "name") ?? "";
                                        Console.WriteLine($"    [{i}] {leaf} \"{elemName}\"  0x{elem.Address:X}");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        // Try other count field names
                        foreach (var cntField in new[] { "Count", "count", "_count", "length", "_length", "_size" })
                        {
                            var c = ReadInt(val, cntField);
                            if (c > 0) { Console.WriteLine($"  {cntField}={c}"); break; }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  Error: {ex.Message}"); }
        }
    }
}

return 0;
