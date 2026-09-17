// LiveModelProbe - read-only reflection of the OutSystems live-model API surface.
// Loads the SS 11 model DLLs (net8 can host the netstandard2.0 SDK + net8 PluginAPI)
// via Assembly.LoadFrom + an AssemblyResolve handler (same pattern as OmlEditor),
// and prints ACCURATE signatures for the types that drive a live in-process editor:
//   - PluginAPI: PluginProvider (plugin discovery + ModelServices root), IDiscoverable,
//     IWorkspace (active module), PluginAttribute, the read-only plugin IEspace view.
//   - Model SDK: IESpace (sanctioned live mutation: CreateServerAction/CreateServerEntity/
//     AddDependency/Serialize/DeserializeInto/Lock...), IESpaceServices, IModelServices.
//   - Oml: accurate Oml/Writer/Reader signatures (corrects the earlier garbled parse).
//   - Model.Plugins / Model.Concurrency (plugin services + RW lock).
// Also attempts to invoke PluginProvider.PluginDirectoryPaths to print the real drop dirs.
//
// No SS process needed, no mutations, no file writes. Pure inspection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LiveModelProbe;

internal static class Program
{
    static string SS_DIR =>
        Environment.GetEnvironmentVariable("OSSS_DIR") ??
        @"C:\Program Files\OutSystems\Service Studio 11\Service Studio";

    static bool _resolverAttached;
    static void EnsureResolver()
    {
        if (_resolverAttached) return;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        _resolverAttached = true;
    }
    static Assembly ResolveSs(object s, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var path = Path.Combine(SS_DIR, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    static Assembly Load(string dll)
    {
        EnsureResolver();
        var path = Path.Combine(SS_DIR, dll);
        if (!File.Exists(path)) { Console.WriteLine($"NOT FOUND: {path}"); return null; }
        try { return Assembly.LoadFrom(path); }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; Console.WriteLine($"LOAD FAIL {dll}: {r.GetType().Name}: {r.Message}"); return null; }
    }

    static string Pretty(Type t)
    {
        if (t == null) return "?";
        if (t.IsGenericParameter) return t.Name;
        if (t.IsByRef) return "ref " + Pretty(t.GetElementType());
        if (t.IsArray) return Pretty(t.GetElementType()) + "[]";
        var n = t.IsGenericType ? t.Name.Split('`')[0] : t.Name;
        if (t.IsGenericType) n += "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">";
        return n;
    }

    static Type[] SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { var ok = rtle.Types.Where(x => x != null).ToArray(); Console.WriteLine($"  (partial load {asm.GetName().Name}: {rtle.LoaderExceptions.Length} loader errors, {ok.Length} ok)"); return ok; }
        catch (Exception e) { Console.WriteLine($"  GetTypes failed {asm.GetName().Name}: {e.Message}"); return Array.Empty<Type>(); }
    }

    static void DumpType(Type t)
    {
        if (t == null) { Console.WriteLine("  (type not found)"); return; }
        var kind = t.IsInterface ? "interface" : (t.IsAbstract && !t.IsInterface ? "abstract class" : (t.IsSealed ? "sealed class" : "class"));
        var bs = (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType)) ? " : " + t.BaseType.FullName : "";
        var genParams = t.IsGenericType ? "<" + string.Join(", ", t.GetGenericArguments().Select(a => a.Name)) + ">" : "";
        Console.WriteLine($"\n===== {kind}: {t.Namespace}.{t.Name}{genParams}{bs} =====");
        var ifaces = t.GetInterfaces().Where(i => i.IsPublic).Select(i => i.Name);
        if (ifaces.Any()) Console.WriteLine("  implements: " + string.Join(", ", ifaces));
        try
        {
            foreach (var at in t.GetCustomAttributesData())
            {
                var an = at.AttributeType.Name;
                if (an.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    an.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    an.IndexOf("Extension", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  [attr] " + at.AttributeType.FullName);
            }
        }
        catch { }
        var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                  .Where(m => !m.IsSpecialName).OrderBy(m => m.Name);
        foreach (var m in ms)
        {
            var gp = m.IsGenericMethod ? ("<" + string.Join(",", m.GetGenericArguments().Select(g => g.Name)) + ">") : "";
            var ps = string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name));
            var mods = (m.IsStatic ? "static " : "") + (m.IsAbstract ? "abstract " : "") + (m.IsVirtual ? "virtual " : "");
            Console.WriteLine($"  {mods}{Pretty(m.ReturnType)} {m.Name}{gp}({ps})");
        }
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(p => p.Name);
        foreach (var p in props)
            Console.WriteLine($"  prop {Pretty(p.PropertyType)} {p.Name} {{ {(p.GetMethod != null ? "get; " : "")}{(p.SetMethod != null ? "set; " : "")}}} {(p.GetMethod != null && p.GetMethod.IsStatic ? "(static)" : "")}");
        var evs = t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var ev in evs)
            Console.WriteLine($"  event {Pretty(ev.EventHandlerType)} {ev.Name}");
    }

    static void FindAndDump(Assembly asm, IEnumerable<string> simpleNames, string label)
    {
        if (asm == null) return;
        var ts = SafeGetTypes(asm);
        foreach (var sn in simpleNames)
        {
            var matches = ts.Where(t => t.Name == sn || t.Name == sn + "`1" || t.Name == sn + "`2").ToArray();
            if (matches.Length == 0) continue;
            Console.WriteLine($"\n--- {label}: found {string.Join(", ", matches.Select(m => m.FullName))} in {asm.GetName().Name} ---");
            foreach (var m in matches) DumpType(m);
        }
    }

    static void Pattern()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== PluginAPI types: interfaces / service / startup / provider / command / descriptor / registry ===");
        var pa = Load("ServiceStudio.PluginAPI.dll");
        if (pa != null)
        {
            var ts = SafeGetTypes(pa);
            foreach (var t in ts)
            {
                var n = t.Name;
                if (t.IsInterface || n.Contains("Service") || n.Contains("Startup") || n.Contains("Boot") || n.Contains("Initial") ||
                    n.Contains("Provider") || n.Contains("Command") || n.Contains("Module") || n.Contains("Plugin") ||
                    n.Contains("Descriptor") || n.Contains("Registry") || n.Contains("Context"))
                {
                    var bs = (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType)) ? " : " + t.BaseType.Name : "";
                    var ifc = string.Join(", ", t.GetInterfaces().Select(i => i.Name));
                    Console.WriteLine($"  {(t.IsInterface ? "[I] " : "")}{t.FullName}{bs}  [{ifc}]");
                }
            }
        }
        var pdir = Path.Combine(SS_DIR, "Plugins", "ServiceStudio");
        foreach (var f in new[] { "ServiceStudio.Plugin.DietAutomation.dll", "ServiceStudio.Plugin.SampleBlocks.dll" })
        {
            var fp = Path.Combine(pdir, f);
            if (!File.Exists(fp)) { Console.WriteLine($"\nNOT FOUND: {fp}"); continue; }
            try
            {
                var a = Assembly.LoadFrom(fp);
                Console.WriteLine($"\n=== {f} ===");
                var ts = SafeGetTypes(a);
                foreach (var t in ts.Where(t => t.Name.Contains("Provider") || t.Name.Contains("Descriptor")))
                    DumpType(t);
                // assembly-level attributes (plugin/export markers)
                Console.WriteLine("--- assembly attributes ---");
                foreach (var at in a.GetCustomAttributesData())
                {
                    var an = at.AttributeType.Name;
                    if (an.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        an.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        an.IndexOf("Extension", StringComparison.OrdinalIgnoreCase) >= 0)
                        Console.WriteLine("  [asm attr] " + at.AttributeType.FullName);
                }
            }
            catch (Exception e) { Console.WriteLine($"load err {f}: {e.Message}"); }
        }
        Console.WriteLine("\n=== pattern done ===");
    }

    // Research the "command" / IESpaceServices mechanism (mutation requires an open
    // command; ModelServices isn't IESpaceServices). Read-only, no SS needed.
    static void Command()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var dlls = new[] {
            "OutSystems.Model.V1.dll", "OutSystems.Model.V1.Internal.dll", "OutSystems.MetaModel.dll",
            "OutSystems.Model.Definition.dll", "OutSystems.Model.Implementation.dll",
            "OutSystems.Model.Implementation.Extensions.dll", "OutSystems.ModuleServices.dll",
            "ServiceStudio.PluginAPI.dll", "ServiceStudio.Common.dll", "ServiceStudio.Framework.dll",
            "ServiceStudio.Presenter.dll", "ServiceStudio.dll", "OutSystems.IDE.ContextualDispatcher.dll"
        };
        var loaded = new List<Assembly>();
        foreach (var d in dlls) { var a = Load(d); if (a != null) loaded.Add(a); }

        Type essType = null;
        foreach (var a in loaded)
        {
            essType = SafeGetTypes(a).FirstOrDefault(t => t.FullName == "OutSystems.Model.IESpaceServices");
            if (essType != null) break;
        }
        Console.WriteLine("IESpaceServices: " + (essType?.FullName ?? "NOT FOUND"));

        Console.WriteLine("\n=== concrete types implementing IESpaceServices ===");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
                try { if (essType != null && t.IsClass && !t.IsAbstract && essType.IsAssignableFrom(t)) Console.WriteLine("  " + t.FullName + "  (asm " + a.GetName().Name + ")"); } catch { }

        Console.WriteLine("\n=== methods/properties returning *ESpaceServices* ===");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
            {
                try
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        if (NameHas(m.ReturnType, "ESpaceServices"))
                            Console.WriteLine("  [" + t.FullName + "] " + Pretty(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
                    }
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (NameHas(p.PropertyType, "ESpaceServices"))
                            Console.WriteLine("  [" + t.FullName + "] prop " + Pretty(p.PropertyType) + " " + p.Name);
                }
                catch { }
            }

        Console.WriteLine("\n=== PrepareForCommandExecution declarers + signature ===");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
                try
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (m.Name == "PrepareForCommandExecution")
                            Console.WriteLine("  [" + t.FullName + "] " + Pretty(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
                }
                catch { }

        Console.WriteLine("\n=== IContext members (ServiceStudio.PluginAPI.Model.IContext) ===");
        Type ctxType = null;
        foreach (var a in loaded) { ctxType = SafeGetTypes(a).FirstOrDefault(t => t.FullName == "ServiceStudio.PluginAPI.Model.IContext"); if (ctxType != null) break; }
        if (ctxType != null) DumpType(ctxType); else Console.WriteLine("  NOT FOUND");

        Console.WriteLine("\n=== 'Command' interfaces/managers (Model SDK + PluginAPI) ===");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
                try { if ((t.IsInterface || t.Name.Contains("Manager")) && t.Name.Contains("Command") && !t.FullName.StartsWith("ServiceStudio.Plugin.NRWidgets")) Console.WriteLine("  " + t.FullName); } catch { }

        Console.WriteLine("\n=== IService implementers (GetPluginService<T> constraint) ===");
        Type isvcType = null;
        foreach (var a in loaded) { isvcType = SafeGetTypes(a).FirstOrDefault(t => t.FullName == "OutSystems.Model.IService"); if (isvcType != null) break; }
        if (isvcType != null)
            foreach (var a in loaded)
                foreach (var t in SafeGetTypes(a))
                    try { if (t.IsClass && !t.IsAbstract && isvcType.IsAssignableFrom(t) && t.Name.Contains("ESpace")) Console.WriteLine("  " + t.FullName); } catch { }

        Console.WriteLine("\n=== ESpaceServicesImplementation (ctors/statics/base) ===");
        var essImpl = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(t => t.FullName == "ServiceStudio.Model.API.V1.ESpaceServicesImplementation");
        if (essImpl != null)
        {
            Console.WriteLine("  base: " + (essImpl.BaseType?.FullName ?? "object"));
            Console.WriteLine("  interfaces: " + string.Join(", ", essImpl.GetInterfaces().Select(i => i.FullName)));
            foreach (var c in essImpl.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")" + (c.IsStatic ? " static" : "") + (c.IsPublic ? " public" : (c.IsPrivate ? " private" : " nonpublic")));
            foreach (var f in essImpl.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine("  field " + Pretty(f.FieldType) + " " + f.Name + (f.IsStatic ? " static" : ""));
            foreach (var p in essImpl.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine("  prop " + Pretty(p.PropertyType) + " " + p.Name + (p.GetGetMethod(true)?.IsStatic == true ? " static" : ""));
        }
        else Console.WriteLine("  NOT FOUND");

        Console.WriteLine("\n=== ParentDependentAction delegate ===");
        var pda = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(t => t.FullName != null && t.FullName.Contains("ParentDependentAction"));
        if (pda != null)
        {
            var inv = pda.GetMethod("Invoke");
            if (inv != null) Console.WriteLine("  " + pda.FullName + " : " + Pretty(inv.ReturnType) + " (" + string.Join(", ", inv.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
            else Console.WriteLine("  " + pda.FullName + " (no Invoke - not a delegate)");
        }
        else Console.WriteLine("  NOT FOUND");

        Console.WriteLine("\n=== ICommandServices / IInternalCommandServices ===");
        foreach (var tn in new[] { "OutSystems.Model.ICommandServices", "OutSystems.Model.Internal.IInternalCommandServices" })
        {
            var tt = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(x => x.FullName == tn);
            Console.WriteLine("-- " + tn + " --");
            if (tt != null) DumpType(tt); else Console.WriteLine("  NOT FOUND");
        }

        Console.WriteLine("\n=== GetPluginService<T> constraints ===");
        var ims = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(t => t.FullName == "OutSystems.Model.IModelServices");
        if (ims != null)
        {
            var gps = ims.GetMethods().FirstOrDefault(m => m.Name == "GetPluginService");
            if (gps != null && gps.IsGenericMethod)
            {
                var arg = gps.GetGenericArguments()[0];
                foreach (var c in arg.GetGenericParameterConstraints()) Console.WriteLine("  constraint: " + c.FullName);
                Console.WriteLine("  attrs: " + arg.GenericParameterAttributes);
            }
        }

        Console.WriteLine("\n=== command-BEGIN methods (ALL loaded assemblies) ===");
        var wantNames = new HashSet<string> { "BeginCommand", "OpenCommand", "StartCommand", "CreateCommand", "EndCommand", "CommitCommand", "UsingCommand", "ExecuteInCommand", "RunInCommand", "ExecuteCommand", "BeginUndoUnit", "OpenUndoUnit", "StartUndoUnit", "BeginTransaction", "OpenTransaction", "BeginModelChange", "BeginChange", "BeginUndoableChange", "BeginOperation", "StartOperation", "BeginMacro", "OpenMacro" };
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
            {
                try
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        if (!wantNames.Contains(m.Name)) continue;
                        Console.WriteLine("  [" + t.FullName + "] " + (m.IsStatic ? "static " : "") + Pretty(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
                    }
                }
                catch { }
            }
        Console.WriteLine("-- ICommandTarget + AggregatorPresenter command methods --");
        foreach (var tn in new[] { "ServiceStudio.Commands.ICommandTarget", "ServiceStudio.Commands.ICommand", "ServiceStudio.Presenter.AggregatorPresenter" })
        {
            Type tt = null;
            foreach (var a in loaded) { tt = SafeGetTypes(a).FirstOrDefault(x => x.FullName == tn); if (tt != null) break; }
            Console.WriteLine("-- " + tn + " --");
            if (tt != null)
            {
                foreach (var m in tt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (!m.IsSpecialName)
                        Console.WriteLine("  " + Pretty(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
            }
            else Console.WriteLine("  NOT FOUND");
        }

        Console.WriteLine("\n=== ServiceStudio.Undo types (UndoManager / ITrackableObject) ===");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
            {
                try
                {
                    if ((t.Namespace ?? "") == "ServiceStudio.Undo" && (t.IsInterface || t.Name.Contains("Manager") || t.Name.Contains("Command") || t.Name.Contains("Trackable") || t.Name.Contains("Scope") || t.Name.Contains("Unit")))
                        DumpType(t);
                }
                catch { }
            }
        Console.WriteLine("\n=== methods/props returning UndoManager ===");
        var umType = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(t => t.FullName == "ServiceStudio.Undo.UndoManager");
        if (umType != null)
            foreach (var a in loaded)
                foreach (var t in SafeGetTypes(a))
                {
                    try
                    {
                        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (!m.IsSpecialName && m.ReturnType == umType)
                                Console.WriteLine("  [" + t.FullName + "] " + m.Name + "() -> UndoManager");
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                            if (p.PropertyType == umType)
                                Console.WriteLine("  [" + t.FullName + "] prop UndoManager " + p.Name);
                    }
                    catch { }
                }

        Console.WriteLine("\n=== IBaseTopLevelPresenter (aggregator) ===");
        Type btlp = null;
        foreach (var a in loaded) { btlp = SafeGetTypes(a).FirstOrDefault(t => t.Name == "IBaseTopLevelPresenter"); if (btlp != null) break; }
        if (btlp != null) DumpType(btlp); else Console.WriteLine("  NOT FOUND");
        Console.WriteLine("-- methods/props returning *TopLevelPresenter* or named *Aggregator* --");
        foreach (var a in loaded)
            foreach (var t in SafeGetTypes(a))
            {
                try
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (!m.IsSpecialName && ((m.ReturnType == btlp) || (m.ReturnType.Name ?? "").Contains("TopLevelPresenter") || m.Name.Contains("Aggregator")))
                            Console.WriteLine("  [" + t.FullName + "] " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (p.PropertyType == btlp || (p.PropertyType.Name ?? "").Contains("TopLevelPresenter") || p.Name.Contains("Aggregator"))
                            Console.WriteLine("  [" + t.FullName + "] prop " + p.PropertyType.Name + " " + p.Name);
                }
                catch { }
            }

        Console.WriteLine("\n=== aggregator/presenter interfaces (command-opener?) ===");
        foreach (var tn in new[] { "ServiceStudio.Presenter.IBaseTopLevelPresenter", "ServiceStudio.Presenter.ITopLevelPresenter", "ServiceStudio.Presenter.IBaseAggregatorPresenter", "ServiceStudio.Presenter.IAggregatorPresenter", "ServiceStudio.Presenter.IPresenter", "ServiceStudio.Presenter.IAggregatorWindowPresenter" })
        {
            Type tt = null;
            foreach (var a in loaded) { tt = SafeGetTypes(a).FirstOrDefault(x => x.FullName == tn); if (tt != null) break; }
            Console.WriteLine("-- " + tn + " --");
            if (tt != null) DumpType(tt); else Console.WriteLine("  NOT FOUND");
        }
        // ServiceStudio.Runtime static methods (GetAggregator etc.)
        Console.WriteLine("\n=== ServiceStudio.Runtime static methods ===");
        var rt = loaded.SelectMany(a => SafeGetTypes(a)).FirstOrDefault(t => t.FullName == "ServiceStudio.Runtime");
        if (rt != null)
            foreach (var m in rt.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName)
                    Console.WriteLine("  " + Pretty(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name)) + ")");

        Console.WriteLine("\n=== command done ===");
    }

    static bool NameHas(Type t, string sub)
    {
        if (t == null) return false;
        if ((t.FullName ?? "").Contains(sub)) return true;
        return false;
    }

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 0 && args[0] == "pattern") { Pattern(); return; }
        if (args.Length > 0 && args[0] == "command") { Command(); return; }
        Console.WriteLine($"SS_DIR = {SS_DIR}");
        Console.WriteLine($"coreclr present: {File.Exists(Path.Combine(SS_DIR, "coreclr.dll"))}");
        Console.WriteLine($"Plugins/ServiceStudio dir: {Directory.Exists(Path.Combine(SS_DIR, "Plugins", "ServiceStudio"))}");

        // ---------- 1. PluginAPI ----------
        Console.WriteLine("\n################ PluginAPI ################");
        var pa = Load("ServiceStudio.PluginAPI.dll");
        Assembly ppAsm = pa;
        if (pa != null)
        {
            var pp = pa.GetType("ServiceStudio.PluginAPI.PluginProvider");
            Console.WriteLine("\n--- PluginProvider ---");
            if (pp != null)
            {
                Console.WriteLine("  (static class, base=" + (pp.BaseType?.FullName ?? "object") + ")");
                foreach (var p in pp.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    Console.WriteLine($"  prop {Pretty(p.PropertyType)} {p.Name} (static, get={p.GetMethod != null}, set={p.SetMethod != null})");
                foreach (var m in pp.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(px => Pretty(px.ParameterType) + " " + px.Name));
                    Console.WriteLine($"  static {Pretty(m.ReturnType)} {m.Name}({ps})");
                }
                // try to invoke PluginDirectoryPaths (private static getter)
                var pdp = pp.GetProperty("PluginDirectoryPaths", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (pdp != null && pdp.GetMethod != null)
                {
                    try
                    {
                        var val = pdp.GetValue(null, null);
                        Console.WriteLine($"\n  >> PluginDirectoryPaths invoked -> {val?.GetType().FullName ?? "null"}");
                        if (val is IEnumerable<string> strs) foreach (var s in strs) Console.WriteLine($"       PATH: {s}");
                        else if (val is IEnumerable en) { int n = 0; foreach (var x in en) Console.WriteLine($"       [{n++}] {x}"); }
                        else if (val != null) Console.WriteLine($"       value: {val}");
                    }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; Console.WriteLine($"       invoke failed: {r.GetType().Name}: {r.Message}"); }
                }
            }
            DumpType(pa.GetType("ServiceStudio.PluginAPI.IDiscoverable"));
            DumpType(pa.GetType("ServiceStudio.PluginAPI.IWorkspace"));
            DumpType(pa.GetType("ServiceStudio.PluginAPI.PluginAttribute"));
            DumpType(pa.GetType("ServiceStudio.PluginAPI.CustomCommandAttribute"));
            DumpType(pa.GetType("ServiceStudio.PluginAPI.Model.IEspace"));
            foreach (var nm in new[] {
                "ServiceStudio.PluginAPI.IServiceStudioApplication",
                "ServiceStudio.PluginAPI.Services.IServicesRegistry",
                "ServiceStudio.PluginAPI.Services.IService",
                "ServiceStudio.PluginAPI.IServiceStudioContext",
                "ServiceStudio.PluginAPI.IEditorContext" })
                DumpType(pa.GetType(nm));
        }

        // ---------- 2. Model SDK: IESpace / IESpaceServices / IModelServices ----------
        Console.WriteLine("\n################ Model SDK (IESpace etc.) ################");
        var modelAsms = new List<Assembly>();
        foreach (var d in new[] { "OutSystems.Model.V1.dll", "OutSystems.Model.V1.Internal.dll", "OutSystems.MetaModel.dll", "OutSystems.Model.Definition.dll" })
        { var a = Load(d); if (a != null) modelAsms.Add(a); }
        foreach (var a in modelAsms)
            FindAndDump(a, new[] { "IESpace", "IESpaceServices", "IModelServices", "IESpaceKey", "IModelObject", "IObjectKey" }, "Model SDK");

        // ---------- 3. Oml (accurate) ----------
        Console.WriteLine("\n################ Oml (accurate signatures) ################");
        var impl = Load("OutSystems.Model.Implementation.dll");
        if (impl != null)
        {
            DumpType(impl.GetType("OutSystems.Model.Implementation.Oml.Oml"));
            DumpType(impl.GetType("OutSystems.Model.Implementation.Oml.Writer"));
            DumpType(impl.GetType("OutSystems.Model.Implementation.XElementReader"));
            // also the OmlHeader type
            var ts = SafeGetTypes(impl);
            foreach (var t in ts.Where(t => t.Name == "OmlHeader")) DumpType(t);
        }

        // ---------- 4. Model.Plugins + Model.Concurrency ----------
        Console.WriteLine("\n################ Model.Plugins / Concurrency ################");
        var mp = Load("OutSystems.Model.Plugins.dll");
        if (mp != null)
        {
            var ts = SafeGetTypes(mp);
            Console.WriteLine($"  OutSystems.Model.Plugins.dll: {ts.Length} types; interfaces/plugin/feature types:");
            foreach (var t in ts.Where(t => t.IsInterface || t.Name.Contains("Plugin") || t.Name.Contains("Feature")))
                DumpType(t);
        }
        var conc = Load("OutSystems.Model.Concurrency.dll");
        if (conc != null)
        {
            var ts = SafeGetTypes(conc);
            foreach (var t in ts.Where(t => t.IsInterface || t.Name.Contains("Lock")))
                DumpType(t);
        }

        // ---------- 5. Scan for IDiscoverable implementers ----------
        Console.WriteLine("\n################ Scan: IDiscoverable implementers (across loaded asms) ################");
        var idisc = pa?.GetType("ServiceStudio.PluginAPI.IDiscoverable");
        if (idisc != null)
        {
            var allAsm = new[] { pa, impl, mp, conc }.Concat(modelAsms).Where(a => a != null).ToArray();
            int found = 0;
            foreach (var asm in allAsm)
                foreach (var t in SafeGetTypes(asm))
                {
                    try
                    {
                        if (t.IsClass && !t.IsAbstract && idisc.IsAssignableFrom(t))
                        { Console.WriteLine($"  IDiscoverable impl: {t.FullName}  (asm {asm.GetName().Name})"); found++; }
                    }
                    catch { }
                }
            // also peek at the real plugin DLLs in Plugins/ServiceStudio for IDiscoverable implementers
            Console.WriteLine($"  (in-probe asms: {found} implementer(s))");
            var pdir = Path.Combine(SS_DIR, "Plugins", "ServiceStudio");
            if (Directory.Exists(pdir))
            {
                Console.WriteLine("  -- scanning Plugins/ServiceStudio/*.dll for IDiscoverable --");
                foreach (var f in Directory.GetFiles(pdir, "ServiceStudio.Plugin.*.dll"))
                {
                    try
                    {
                        var a = Assembly.LoadFrom(f);
                        foreach (var t in SafeGetTypes(a))
                        {
                            try { if (t.IsClass && !t.IsAbstract && idisc.IsAssignableFrom(t)) Console.WriteLine($"    {Path.GetFileName(f)}: {t.FullName}"); }
                            catch { }
                        }
                    }
                    catch (Exception e) { Console.WriteLine($"    {Path.GetFileName(f)}: load err {e.Message}"); }
                }
            }
        }

        Console.WriteLine("\n=== done ===");
    }
}
