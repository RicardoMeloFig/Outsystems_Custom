// CommandProbe - Mono.Cecil-based static analysis of the OutSystems SS DLLs.
// Purpose: find the guard that throws
//   "No command is open. The model cannot be changed outside the scope of a command!"
// dump its IL to see WHAT STATE it checks, then trace callers/setters to find the
// real command-open API. Read-only metadata analysis - no SS process needed.
//
// Modes:
//   (default)            scan all SS DLLs for "No command is open", dump IL, find callers
//   string <substring>   list methods whose IL has a ldstr containing <substring>
//   il <type> <method>   dump IL of a specific method (match by type FullName + method Name)
//   callers <type> <m>   list every method that calls <type>.<m>
//   calls <type> <m>     list every method that <type>.<m> itself calls (forward)
//   writes <type> <fld>  list methods that store to field <type>.<fld> (stfld/stsfld)
//   reads <type> <fld>   list methods that load field <type>.<fld> (ldfld/ldsfld)
//   type <type>          list all methods of a type (signatures only)
//
// <type> is a FullName like "ServiceStudio.Framework.SomeClass". Method/field names are
// simple names; when ambiguous the first declared match is used.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CommandProbe;

internal static class Program
{
    static string SS_DIR =>
        Environment.GetEnvironmentVariable("OSSS_DIR") ??
        @"C:\Program Files\OutSystems\Service Studio 11\Service Studio";

    static string PLUGINS_DIR => Path.Combine(SS_DIR, "Plugins", "ServiceStudio");

    static List<AssemblyDefinition> LoadAll()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(SS_DIR);
        resolver.AddSearchDirectory(PLUGINS_DIR);
        var ps = new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred };
        var dirs = new[] { SS_DIR, PLUGINS_DIR };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var asms = new List<AssemblyDefinition>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.dll"))
            {
                var name = Path.GetFileName(f);
                if (!seen.Add(name)) continue;
                try { asms.Add(AssemblyDefinition.ReadAssembly(f, ps)); }
                catch { /* native / not a managed dll - skip */ }
            }
        }
        return asms;
    }

    static IEnumerable<MethodDefinition> AllMethods(IEnumerable<AssemblyDefinition> asms)
    {
        foreach (var a in asms)
            foreach (var t in a.MainModule.Types.SelectMany(WalkTypes))
                foreach (var m in t.Methods)
                    if (m.HasBody) yield return m;
    }

    static IEnumerable<TypeDefinition> WalkTypes(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes.SelectMany(WalkTypes)) yield return n;
    }

    static string Sig(MethodDefinition m) =>
        $"{m.ReturnType.Name} {m.DeclaringType.FullName}::{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})";

    static string Sig(MethodReference m) =>
        $"{m.ReturnType.Name} {m.DeclaringType.FullName}::{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})";

    static bool NameEq(MethodReference a, MethodDefinition b) =>
        a.DeclaringType.FullName == b.DeclaringType.FullName && a.Name == b.Name;

    // ---------- scan for a ldstr substring ----------
    static void ScanString(List<AssemblyDefinition> asms, string sub)
    {
        Console.WriteLine($"=== methods whose IL has ldstr containing: \"{sub}\" ===");
        int hits = 0;
        foreach (var m in AllMethods(asms))
        {
            foreach (var ins in m.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s && s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits++;
                    Console.WriteLine($"\n[HIT {hits}] {Sig(m)}  (asm {m.DeclaringType.Module.Assembly.Name.Name})");
                    Console.WriteLine($"      string: \"{s}\"");
                    break;
                }
            }
        }
        Console.WriteLine($"\n=== {hits} method(s) matched ===");
    }

    static string InsStr(Instruction ins)
    {
        var op = ins.OpCode.Name;
        var operand = ins.Operand;
        string o;
        switch (operand)
        {
            case null: o = ""; break;
            case string s: o = "\"" + s.Replace("\n", "\\n").Replace("\r", "\\r") + "\""; break;
            case MethodReference mr: o = mr.DeclaringType.FullName + "::" + mr.Name; break;
            case FieldReference fr: o = fr.DeclaringType.FullName + "::" + fr.Name; break;
            case TypeReference tr: o = tr.FullName; break;
            case Instruction target: o = "IL_" + target.Offset.ToString("x4"); break;
            case Instruction[] targets: o = string.Join(", ", targets.Select(t => "IL_" + t.Offset.ToString("x4"))); break;
            default: o = operand.ToString(); break;
        }
        return $"IL_{ins.Offset.ToString("x4")}: {op} {o}";
    }

    static void DumpIl(MethodDefinition m)
    {
        Console.WriteLine($"--- IL: {Sig(m)} ---");
        if (!m.HasBody) { Console.WriteLine("  (no body)"); return; }
        foreach (var ins in m.Body.Instructions)
            Console.WriteLine("  " + InsStr(ins));
        if (m.Body.Variables.Count > 0)
        {
            Console.WriteLine("  locals:");
            for (int i = 0; i < m.Body.Variables.Count; i++)
                Console.WriteLine($"    V_{i}: {m.Body.Variables[i].VariableType.FullName}");
        }
    }

    static MethodDefinition FindMethod(List<AssemblyDefinition> asms, string typeFullName, string methodName)
    {
        foreach (var m in AllMethods(asms))
            if (m.DeclaringType.FullName == typeFullName && m.Name == methodName) return m;
        return null;
    }

    static void FindCallers(List<AssemblyDefinition> asms, string typeFullName, string methodName)
    {
        Console.WriteLine($"=== callers of {typeFullName}::{methodName} ===");
        var target = FindMethod(asms, typeFullName, methodName);
        if (target == null) { Console.WriteLine("  TARGET NOT FOUND"); return; }
        int n = 0;
        foreach (var m in AllMethods(asms))
        {
            bool calls = false;
            foreach (var ins in m.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) &&
                    ins.Operand is MethodReference mr && NameEq(mr, target))
                { calls = true; break; }
            }
            if (calls) { n++; Console.WriteLine($"  [{n}] {Sig(m)}  (asm {m.DeclaringType.Module.Assembly.Name.Name})"); }
        }
        Console.WriteLine($"=== {n} caller(s) ===");
    }

    static void FindCalls(List<AssemblyDefinition> asms, string typeFullName, string methodName)
    {
        Console.WriteLine($"=== methods called by {typeFullName}::{methodName} ===");
        var src = FindMethod(asms, typeFullName, methodName);
        if (src == null) { Console.WriteLine("  SOURCE NOT FOUND"); return; }
        var set = new HashSet<string>();
        foreach (var ins in src.Body.Instructions)
        {
            if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference mr)
            {
                var key = mr.DeclaringType.FullName + "::" + mr.Name;
                if (set.Add(key)) Console.WriteLine($"  {Sig(mr)}  (decl in {mr.DeclaringType.Module.Assembly.Name.Name})");
            }
        }
    }

    static void FindFieldOps(List<AssemblyDefinition> asms, string typeFullName, string fieldName, bool writes)
    {
        var want = writes ? "writes to" : "reads of";
        Console.WriteLine($"=== {want} field {typeFullName}::{fieldName} ===");
        int n = 0;
        var opcodes = writes
            ? new[] { OpCodes.Stfld, OpCodes.Stsfld }
            : new[] { OpCodes.Ldfld, OpCodes.Ldsfld, OpCodes.Ldflda, OpCodes.Ldsflda };
        foreach (var m in AllMethods(asms))
        {
            bool matched = false;
            foreach (var ins in m.Body.Instructions)
            {
                if (opcodes.Contains(ins.OpCode) && ins.Operand is FieldReference fr &&
                    fr.DeclaringType.FullName == typeFullName && fr.Name == fieldName)
                { matched = true; break; }
            }
            if (matched) { n++; Console.WriteLine($"  [{n}] {Sig(m)}  (asm {m.DeclaringType.Module.Assembly.Name.Name})"); }
        }
        Console.WriteLine($"=== {n} method(s) ===");
    }

    static void DumpType(List<AssemblyDefinition> asms, string typeFullName)
    {
        Console.WriteLine($"=== methods of {typeFullName} ===");
        TypeDefinition td = null;
        foreach (var a in asms)
        {
            td = a.MainModule.Types.SelectMany(WalkTypes).FirstOrDefault(t => t.FullName == typeFullName);
            if (td != null) break;
        }
        if (td == null) { Console.WriteLine("  TYPE NOT FOUND"); return; }
        Console.WriteLine($"  base: {td.BaseType?.FullName}");
        if (td.Interfaces.Count > 0)
            Console.WriteLine("  implements: " + string.Join(", ", td.Interfaces.Select(i => i.InterfaceType.FullName)));
        foreach (var f in td.Fields)
            Console.WriteLine($"  field {f.FieldType.Name} {f.Name}{(f.IsStatic ? " (static)" : "")}");
        foreach (var m in td.Methods.Where(x => !x.IsSpecialName).OrderBy(x => x.Name))
            Console.WriteLine($"  method {Sig(m)}{(m.IsStatic ? " (static)" : "")}");
        Console.WriteLine("=== end type ===");
    }

    static void FindMethodByName(List<AssemblyDefinition> asms, string methodName)
    {
        Console.WriteLine($"=== types declaring a method named '{methodName}' ===");
        int n = 0;
        foreach (var m in AllMethods(asms))
        {
            if (m.Name == methodName)
            {
                n++;
                Console.WriteLine($"  [{n}] {Sig(m)}{(m.IsStatic ? " (static)" : "")}  (asm {m.DeclaringType.Module.Assembly.Name.Name})");
            }
        }
        Console.WriteLine($"=== {n} declaration(s) ===");
    }

    static void FindType(List<AssemblyDefinition> asms, string nameSub)
    {
        Console.WriteLine($"=== types whose FullName contains '{nameSub}' ===");
        int n = 0;
        foreach (var a in asms)
            foreach (var t in a.MainModule.Types.SelectMany(WalkTypes))
            {
                if ((t.FullName ?? "").IndexOf(nameSub, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    n++;
                    Console.WriteLine($"  [{n}] {t.FullName}  (asm {a.MainModule.Assembly.Name.Name})");
                }
            }
        Console.WriteLine($"=== {n} type(s) ===");
    }

    static void DefaultRun()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"SS_DIR = {SS_DIR}");
        var asms = LoadAll();
        Console.WriteLine($"Loaded {asms.Count} assemblies");
        const string needle = "No command is open";
        Console.WriteLine($"\n############ scanning for \"{needle}\" ############");
        var guards = new List<MethodDefinition>();
        foreach (var m in AllMethods(asms))
        {
            foreach (var ins in m.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s && s.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    guards.Add(m);
                    Console.WriteLine($"\n>>>> GUARD: {Sig(m)}  (asm {m.DeclaringType.Module.Assembly.Name.Name})");
                    Console.WriteLine($"     string: \"{s}\"");
                    break;
                }
            }
        }
        Console.WriteLine($"\n{guards.Count} guard method(s) found. Dumping IL + callers for each.\n");
        foreach (var g in guards)
        {
            Console.WriteLine("\n========================================================");
            DumpIl(g);
            Console.WriteLine("-------- callers --------");
            int n = 0;
            foreach (var m in AllMethods(asms))
            {
                bool calls = false;
                foreach (var ins in m.Body.Instructions)
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) &&
                        ins.Operand is MethodReference mr && NameEq(mr, g)) { calls = true; break; }
                if (calls) { n++; Console.WriteLine($"  [{n}] {Sig(m)}  (asm {m.DeclaringType.Module.Assembly.Name.Name})"); }
            }
            Console.WriteLine($"-------- {n} caller(s) --------");
        }
        Console.WriteLine("\n=== default run done ===");
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0) { DefaultRun(); return; }
        var asms = LoadAll();
        Console.WriteLine($"Loaded {asms.Count} assemblies");
        switch (args[0])
        {
            case "string": ScanString(asms, args.Length > 1 ? args[1] : "No command"); break;
            case "il": DumpIlOrNotFound(asms, args); break;
            case "callers": FindCallers(asms, args[1], args[2]); break;
            case "calls": FindCalls(asms, args[1], args[2]); break;
            case "writes": FindFieldOps(asms, args[1], args[2], true); break;
            case "reads": FindFieldOps(asms, args[1], args[2], false); break;
            case "type": DumpType(asms, args[1]); break;
            case "find": FindMethodByName(asms, args[1]); break;
            case "findtype": FindType(asms, args[1]); break;
            case "ctors": DumpCtors(asms, args.Length > 1 ? args[1] : ""); break;
            default: Console.WriteLine("Unknown mode. Modes: string|il|callers|calls|writes|reads|type|find|findtype|ctors"); break;
        }
    }

    static void DumpCtors(List<AssemblyDefinition> asms, string frag)
    {
        if (string.IsNullOrEmpty(frag)) { Console.WriteLine("usage: ctors <typeNameFragment>"); return; }
        foreach (var a in asms)
            foreach (var t in a.MainModule.Types.SelectMany(WalkTypes))
            {
                if (t.FullName.IndexOf(frag, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.FullName.EndsWith("/Kind") || t.FullName.Contains("/Kind/")) continue;
                var ctors = t.Methods.Where(m => m.IsConstructor).ToList();
                if (ctors.Count == 0) continue;
                Console.WriteLine($"=== ctors of {t.FullName} (asm {a.Name.Name}) ===");
                foreach (var c in ctors)
                    Console.WriteLine($"  {(c.IsPublic ? "public" : "nonpublic")} .ctor({string.Join(", ", c.Parameters.Select(p => p.ParameterType.FullName + " " + p.Name))})");
            }
    }

    static void DumpIlOrNotFound(List<AssemblyDefinition> asms, string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: il <typeFullName> <methodName>"); return; }
        var m = FindMethod(asms, args[1], args[2]);
        if (m == null) Console.WriteLine("METHOD NOT FOUND: " + args[1] + "::" + args[2]);
        else DumpIl(m);
    }
}
