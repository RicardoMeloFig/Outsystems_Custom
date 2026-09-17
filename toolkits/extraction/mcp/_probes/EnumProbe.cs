// Quick probe - find BinaryOperator enum and list its values
using System;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

int pid;
var procs = System.Diagnostics.Process.GetProcessesByName("ServiceStudio");
if (procs.Length == 0) { Console.Error.WriteLine("SS not running"); return 1; }
pid = procs[0].Id;
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();

// Find the BinaryOperator enum type by scanning modules
ClrType enumType = null;
foreach (var mod in runtime.EnumerateModules())
{
    var t = mod.GetTypeByName("OutSystems.Model.Implementation.Expressions.BinaryOperator");
    if (t != null) { enumType = t; Console.WriteLine($"Found in module: {mod.Name}"); break; }
}

if (enumType == null)
{
    Console.WriteLine("BinaryOperator type not found via modules. Trying via field type...");
    // Alternative: find a BinaryOperation object and get the field type
    var heap = runtime.Heap;
    foreach (var obj in heap.EnumerateObjects())
    {
        var t = obj.Type; if (t is null) continue;
        if (t.Name != "ServiceStudio.Expressions.ExpressionElements.BinaryOperation") continue;
        foreach (var f in t.Fields)
            if (f.Name == "operator")
            {
                enumType = f.Type;
                Console.WriteLine($"Found via field: {enumType?.Name}");
                break;
            }
        break;
    }
}

if (enumType == null) { Console.WriteLine("Could not find BinaryOperator type!"); return 1; }

Console.WriteLine($"\nBinaryOperator enum type: {enumType.Name}");
Console.WriteLine($"IsEnum: {enumType.IsEnum}");
Console.WriteLine($"ElementType: {enumType.ElementType}");

// List all static fields (enum values)
Console.WriteLine("\nStatic fields:");
foreach (var f in enumType.StaticFields)
{
    Console.Write($"  {f.Name} (type={f.Type?.Name})");
    // Try to read the value
    try
    {
        foreach (var ad in runtime.AppDomains)
        {
            try
            {
                var val = f.Read<int>(ad);
                Console.WriteLine($" = {val}");
                break;
            }
            catch { }
        }
    }
    catch (Exception ex) { Console.WriteLine($" (read failed: {ex.Message})"); }
}

// Also try instance fields (the underlying value field)
Console.WriteLine("\nInstance fields:");
foreach (var f in enumType.Fields)
    Console.WriteLine($"  {f.Name} (type={f.Type?.Name})");

return 0;
