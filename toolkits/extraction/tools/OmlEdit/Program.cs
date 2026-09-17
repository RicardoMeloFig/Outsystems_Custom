// OmlEdit - headless OML fragment inspector / editor (no productKey, no UI).
// Loads an .oml via Oml.LoadWithoutUpgrades(bytes, "") and exposes the XML
// fragment model. Subcommands:
//   dump <omlPath> [outDir]      - list every fragment, save each to outDir
//   getbytes <omlPath> <outOml>  - round-trip: load + re-serialize (validates WriteTo)
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace OmlEdit;

internal static class Program
{
    static string SS_DIR = @"C:\Program Files\OutSystems\Service Studio 11\Service Studio";

    static Assembly ResolveSs(object s, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name + ".dll";
        var path = Path.Combine(SS_DIR, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    static dynamic LoadOml(string omlPath)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml")
            ?? throw new Exception("Oml type not found");
        return omlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { File.ReadAllBytes(omlPath), "" });
    }

    static void Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0] : "";
        switch (cmd)
        {
            case "dump": Dump(args); break;
            case "getbytes": GetBytes(args); break;
            case "reflect": Reflect(args); break;
            case "fraginfo": FragInfo(args); break;
            case "fragxml": FragXml(args); break;
            case "editest": EditTest(args); break;
            case "scan": Scan(args); break;
            case "decompress": Decompress(args); break;
            case "hashtest": HashTest(args); break;
            case "adddep": AddDep(args); break;
            case "reflectdll": ReflectDll(args); break;
            case "pluginpaths": PluginPaths(args); break;
            case "probe": Probe(args); break;
            case "addsvc": AddSvc(args); break;
            case "mksvc": MkSvc(args); break;
            case "clonesvc": CloneSvc(args); break;
            default:
                Console.WriteLine("usage: OmlEdit ...|mksvc|clonesvc ...");
                break;
        }
    }

    static void Reflect(string[] a)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        foreach (var tname in new[] {
            "OutSystems.Model.Implementation.Oml.Oml",
            "OutSystems.Model.Implementation.XElementReader",
            "OutSystems.Model.Implementation.Oml.Writer" })
        {
            var t = impl.GetType(tname);
            if (t == null) { Console.WriteLine($"=== {tname} : NOT FOUND ==="); continue; }
            Console.WriteLine($"=== {tname} ===");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                var mods = (m.IsStatic ? "static " : "") + (m.IsPublic ? "public " : "");
                var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                Console.WriteLine($"  {mods}{m.ReturnType.Name} {m.Name}({ps})");
            }
        }
    }

    static void FragInfo(string[] a)
    {
        dynamic oml = LoadOml(a[1]);
        Console.WriteLine("=== GetFragmentsInfo ===");
        int i = 0;
        foreach (var fi in (IEnumerable)oml.GetFragmentsInfo())
        {
            Console.WriteLine($"  [{i}] {fi}");
            i++;
            if (i > 40) break;
        }
    }

    static void FragXml(string[] a)
    {
        var omlPath = a[1];
        var fragName = a[2];
        var outFile = a[3];
        dynamic oml = LoadOml(omlPath);
        Console.WriteLine($"HasFragment({fragName}) = {oml.HasFragment(fragName)}");
        dynamic reader = oml.GetFragmentXmlReader(fragName);
        Console.WriteLine($"reader type = {reader.GetType().FullName}");
        // try common extraction approaches
        string xml = null;
        try { xml = reader.ToString(); } catch (Exception e) { Console.WriteLine("ToString failed: " + e.Message); }
        try
        {
            // XElementReader may be enumerable or have a Read method returning XElement
            var t = reader.GetType();
            var readM = t.GetMethod("Read", BindingFlags.Public | BindingFlags.Instance);
            if (readM != null)
            {
                var xe = readM.Invoke(reader, null);
                xml = xe.ToString();
                Console.WriteLine("Read() returned: " + xe.GetType().Name);
            }
        }
        catch (Exception e) { Console.WriteLine("Read() failed: " + e.Message); }
        try
        {
            // try ReadOuterXml / ReadInnerXml
            var t = reader.GetType();
            foreach (var mn in new[] { "ReadOuterXml", "ReadInnerXml", "GetXml", "ToXElement", "GetXElement" })
            {
                var m = t.GetMethod(mn, BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { var r = m.Invoke(reader, null); xml = r?.ToString(); Console.WriteLine($"{mn}() -> {r?.GetType().Name}"); break; }
            }
        }
        catch { }
        if (xml != null)
        {
            File.WriteAllText(outFile, xml);
            Console.WriteLine($"wrote {xml.Length} chars -> {outFile}");
            // content flags
            foreach (var p in new[] { "ClientCreate2", "ClientCreate", "ExecuteAction", "ServiceAction", "ServerAction" })
                if (xml.Contains(p)) Console.WriteLine($"  contains: {p}");
        }
        else Console.WriteLine("could not extract XML from reader");
    }

    static void Scan(string[] a)
    {
        var omlPath = a[1];
        var needle = a.Length > 2 ? a[2] : "ExecuteAction";
        var outDir = a.Length > 3 ? a[3] : null;
        dynamic oml = LoadOml(omlPath);
        var names = new List<string>();
        foreach (var n in (IEnumerable<string>)oml.DumpFragmentsNames()) names.Add(n.ToString());
        Console.WriteLine($"scanning {names.Count} fragments for '{needle}'");
        int hits = 0;
        for (int i = 0; i < names.Count; i++)
        {
            var nm = names[i];
            try
            {
                var r = oml.GetFragmentXmlReader(nm);
                XElement xe = (XElement)r.ToXElement();
                var s = xe.ToString();
                if (s.Contains(needle))
                {
                    hits++;
                    Console.WriteLine($"  HIT [{i}] {nm}  len={s.Length}");
                    if (outDir != null)
                    {
                        Directory.CreateDirectory(outDir);
                        var safe = string.Concat(nm.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                        xe.Save(Path.Combine(outDir, $"hit_{i}_{safe}.xml"));
                    }
                }
            }
            catch (Exception e) { Console.WriteLine($"  ERR [{i}] {nm}: {e.Message}"); }
        }
        Console.WriteLine($"hits: {hits}");
    }

    static void EditTest(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        dynamic oml = LoadOml(omlPath);

        // 1. read UserActions fragment
        dynamic reader = oml.GetFragmentXmlReader("UserActions");
        XElement xe = (XElement)reader.ToXElement();
        var ua = xe.Elements().FirstOrDefault();
        if (ua == null) { Console.WriteLine("no UserAction element"); return; }
        var orig = ua.Attribute("LastModifiedDate")?.Value ?? "(none)";
        Console.WriteLine($"UserActions: {ua.Attribute("Name").Value}  LastModifiedDate={orig}");

        // 2. mutate
        ua.SetAttributeValue("LastModifiedDate", "2099-01-01 00:00:00");
        Console.WriteLine("mutated LastModifiedDate -> 2099-01-01 00:00:00");

        // 3. write back - discover Writer API
        dynamic writer = oml.GetFragmentXmlWriter("UserActions");
        var wt = ((object)writer).GetType();
        Console.WriteLine("Writer type: " + wt.FullName);
        foreach (var m in wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (!m.IsSpecialName) Console.WriteLine("  W: " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");

        bool written = false;
        // try Write(XElement)
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) });
        if (writeM != null) { writeM.Invoke(writer, new object[] { xe }); written = true; Console.WriteLine("-> called Write(XElement)"); }
        if (!written)
        {
            // try Write(XmlReader)
            var writeR = wt.GetMethod("Write", new[] { typeof(System.Xml.XmlReader) });
            if (writeR != null)
            {
                using var sr = System.Xml.XmlReader.Create(new StringReader(xe.ToString()));
                writeR.Invoke(writer, new object[] { sr }); written = true; Console.WriteLine("-> called Write(XmlReader)");
            }
        }
        if (!written)
        {
            // try a method named WriteObject/WriteFragment taking XElement, or any method taking XElement
            foreach (var m in wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var ps = m.GetParameters();
                if (ps.Length == 1 && (ps[0].ParameterType == typeof(XElement) || ps[0].ParameterType == typeof(System.Xml.XmlReader)))
                { m.Invoke(writer, new object[] { ps[0].ParameterType == typeof(XElement) ? xe : (object)System.Xml.XmlReader.Create(new StringReader(xe.ToString())) }); written = true; Console.WriteLine("-> called " + m.Name); break; }
            }
        }
        try { ((IDisposable)writer).Dispose(); } catch { }

        if (!written) { Console.WriteLine("Could not write fragment - no matching Writer method"); return; }

        // 4. signature regen
        try { oml.SetNeedsSignatureRegeneration(); } catch (Exception e) { Console.WriteLine("SetNeedsSignatureRegeneration: " + e.Message); }

        // 5. GetBytes
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        // 6. reload + verify
        dynamic oml2 = LoadOml(outOml);
        var reader2 = oml2.GetFragmentXmlReader("UserActions");
        XElement xe2 = (XElement)reader2.ToXElement();
        var ua2 = xe2.Elements().FirstOrDefault();
        Console.WriteLine("reloaded LastModifiedDate = " + (ua2?.Attribute("LastModifiedDate")?.Value ?? "(none)"));
        Console.WriteLine("RESULT: " + (ua2?.Attribute("LastModifiedDate")?.Value == "2099-01-01 00:00:00" ? "EDIT PERSISTED (hashes regenerated OK)" : "edit did NOT persist"));
    }

    static void Decompress(string[] a)
    {
        var omlPath = a[1];
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml");
        var m = omlType.GetMethod("DebugDecompress");
        Console.WriteLine($"DebugDecompress: static={m.IsStatic} params={m.GetParameters().Length}");
        try
        {
            object result;
            if (m.IsStatic) result = m.Invoke(null, new object[] { omlPath });
            else
            {
                dynamic oml = LoadOml(omlPath);
                result = m.Invoke(oml, new object[] { omlPath });
            }
            Console.WriteLine("DebugDecompress returned: " + (result ?? "(null)"));
        }
        catch (Exception ex)
        {
            var r = ex; while (r.InnerException != null) r = r.InnerException;
            Console.WriteLine($"FAILED: {r.GetType().Name}: {r.Message}");
        }
        // look for dumped xml next to the oml
        var dir = Path.GetDirectoryName(omlPath);
        foreach (var f in Directory.GetFiles(dir, "*.xml", SearchOption.TopDirectoryOnly))
            Console.WriteLine("  xml: " + f);
    }

    static void Dump(string[] a)
    {
        var omlPath = a[1];
        var outDir = a.Length > 2 ? a[2] : Path.Combine(Path.GetDirectoryName(omlPath), "frags_" + Path.GetFileNameWithoutExtension(omlPath));
        Directory.CreateDirectory(outDir);
        dynamic oml = LoadOml(omlPath);

        var names = new List<string>();
        foreach (var n in (IEnumerable<string>)oml.DumpFragmentsNames()) names.Add(n.ToString());
        Console.WriteLine($"=== ALL {names.Count} fragment names ===");
        for (int i = 0; i < names.Count; i++) Console.WriteLine($"  name[{i}] = {names[i]}");
        var frags = new List<XElement>();
        oml.ApplyToXmlFragments(new Action<XElement>(xe => frags.Add(xe)), false);
        Console.WriteLine($"=== ApplyToXmlFragments yielded={frags.Count} ===");

        string[] probe = { "ClientCreate2", "ClientCreate", "DoLogin" };
        for (int i = 0; i < frags.Count; i++)
        {
            var xe = frags[i];
            var nm = i < names.Count ? names[i] : "?";
            var s = xe.ToString();
            var flags = new List<string>();
            foreach (var p in probe) if (s.Contains(p)) flags.Add(p);
            if (s.Contains("<ServerAction")) flags.Add("ServerAction");
            if (s.Contains("<ServiceAction")) flags.Add("ServiceAction");
            if (s.Contains("ExecuteAction")) flags.Add("ExecuteAction");
            if (s.Contains("<Reference ")) flags.Add("Reference");
            if (s.Contains("<Entity ")) flags.Add("Entity");
            if (s.Contains("<Structure ")) flags.Add("Structure");
            var safe = string.Concat(nm.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            xe.Save(Path.Combine(outDir, $"frag_{i:D2}_{safe}.xml"));
            Console.WriteLine($"[{i,2}] {nm,-34} root={xe.Name.LocalName,-18} ch={xe.Elements().Count(),3} desc={xe.Descendants().Count(),4} len={s.Length,7} [{string.Join(",", flags)}]");
        }
        Console.WriteLine($"saved to: {outDir}");

        // Try GetESpaceXmlReader (main eSpace fragment XML)
        try
        {
            var reader = oml.GetESpaceXmlReader();
            var esXml = reader.Read();
            Console.WriteLine($"\nGetESpaceXmlReader root={esXml.Name} desc={esXml.Descendants().Count()}");
            esXml.Save(Path.Combine(outDir, "ESpaceXmlReader.xml"));
            Console.WriteLine("saved ESpaceXmlReader.xml");
        }
        catch (Exception ex) { Console.WriteLine("GetESpaceXmlReader failed: " + ex.Message); }
    }

    static void GetBytes(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        dynamic oml = LoadOml(omlPath);
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");
        dynamic oml2 = LoadOml(outOml);
        var n = 0; foreach (var _ in (IEnumerable<string>)oml2.DumpFragmentsNames()) n++;
        Console.WriteLine($"reloaded OK, fragments={n}");
    }

    // Does SetNeedsSignatureRegeneration()+GetBytes() recompute GeneralHash on a
    // modified element? Modify References fragment's System-ref LastModifiedDate,
    // regen, reload, compare GeneralHash before/after.
    static void HashTest(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        dynamic oml = LoadOml(omlPath);

        XElement xe = (XElement)((dynamic)oml.GetFragmentXmlReader("References")).ToXElement();
        var sysRef = xe.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == "(System)");
        if (sysRef == null) { Console.WriteLine("no (System) reference"); return; }
        string ghBefore = sysRef.Attribute("GeneralHash")?.Value ?? "(none)";
        string lmdBefore = sysRef.Attribute("LastModifiedDate")?.Value ?? "(none)";
        Console.WriteLine($"BEFORE  GeneralHash={ghBefore}  LastModifiedDate={lmdBefore}");

        sysRef.SetAttributeValue("LastModifiedDate", "2099-01-01 00:00:00");
        dynamic writer = oml.GetFragmentXmlWriter("References");
        var wt = ((object)writer).GetType();
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) })
            ?? wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .First(m => { var ps = m.GetParameters(); return ps.Length == 1 && ps[0].ParameterType == typeof(XElement); });
        writeM.Invoke(writer, new object[] { xe });
        try { ((IDisposable)writer).Dispose(); } catch { }

        try { oml.SetNeedsSignatureRegeneration(); } catch (Exception e) { Console.WriteLine("SetNeedsSignatureRegeneration: " + e.Message); }
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        dynamic oml2 = LoadOml(outOml);
        XElement xe2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("References")).ToXElement();
        var sysRef2 = xe2.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == "(System)");
        string ghAfter = sysRef2.Attribute("GeneralHash")?.Value ?? "(none)";
        string lmdAfter = sysRef2.Attribute("LastModifiedDate")?.Value ?? "(none)";
        Console.WriteLine($"AFTER   GeneralHash={ghAfter}  LastModifiedDate={lmdAfter}");
        Console.WriteLine("RESULT: " + (ghAfter != ghBefore
            ? "GeneralHash CHANGED (recomputed by regen) -> dummy GeneralHash OK for new refs"
            : "GeneralHash UNCHANGED (preserved as-is) -> must supply correct GeneralHash"));
    }

    // adddep <oml> <referenceXmlFile> <outOml>
    // Inserts the <Reference> from referenceXmlFile into the References fragment,
    // bumps the <eSpaceFragment Count>, regenerates signatures, writes outOml,
    // reloads + verifies the new reference is present.
    static void AddDep(string[] a)
    {
        var omlPath = a[1];
        var refXmlPath = a[2];
        var outOml = a[3];
        dynamic oml = LoadOml(omlPath);

        XElement root = (XElement)((dynamic)oml.GetFragmentXmlReader("References")).ToXElement();
        Console.WriteLine($"root={root.Name}  Count attr={(root.Attribute("Count")?.Value ?? "(none)")}  children={root.Elements().Count()}");

        XElement newRef = XElement.Load(refXmlPath);
        string refName = newRef.Attribute("Name")?.Value ?? "?";
        string refKey = newRef.Attribute("Key")?.Value ?? "?";
        string refRKey = newRef.Attribute("ReferenceKey")?.Value ?? "?";
        Console.WriteLine($"adding Reference Name={refName} Key={refKey} ReferenceKey={refRKey}");

        // append after existing references
        root.Add(newRef);
        // bump Count
        var cnt = root.Attribute("Count");
        if (cnt != null) { int n = int.Parse(cnt.Value); cnt.Value = (n + 1).ToString(); }

        // write back
        dynamic writer = oml.GetFragmentXmlWriter("References");
        var wt = ((object)writer).GetType();
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) })
            ?? wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .First(m => { var ps = m.GetParameters(); return ps.Length == 1 && ps[0].ParameterType == typeof(XElement); });
        writeM.Invoke(writer, new object[] { root });
        try { ((IDisposable)writer).Dispose(); } catch { }

        try { oml.SetNeedsSignatureRegeneration(); } catch (Exception e) { Console.WriteLine("SetNeedsSignatureRegeneration: " + e.Message); }
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        // reload + verify
        dynamic oml2 = LoadOml(outOml);
        XElement root2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("References")).ToXElement();
        Console.WriteLine($"RELOADED root Count={root2.Attribute("Count")?.Value}  children={root2.Elements().Count()}");
        foreach (var r in root2.Elements())
        {
            var nm = r.Attribute("Name")?.Value ?? "?";
            var rk = r.Attribute("ReferenceKey")?.Value ?? "?";
            var ra = r.Element("ReferenceActions")?.Elements().Count() ?? 0;
            var re = r.Element("ReferenceEntities")?.Elements().Count() ?? 0;
            Console.WriteLine($"  ref Name={nm} ReferenceKey={rk} actions={ra} entities={re}");
        }
        bool found = root2.Elements().Any(e => (e.Attribute("ReferenceKey")?.Value ?? "") == refRKey);
        Console.WriteLine("RESULT: " + (found ? "Diet_CS reference PRESENT after round-trip" : "Diet_CS reference MISSING"));
    }

    // reflectdll <dllName> [typeFilterRegex] - load a DLL from the SS dir and dump
    // assembly attributes + full signatures of public types whose simple name matches.
    // Default filter catches plugin/model/reference/esppace types.
    static void ReflectDll(string[] a)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var dllName = a[1];
        var filter = a.Length > 2 ? a[2] : "Plugin|Espace|ESpace|EditorContext|Model|Reference|Consume|ServiceAction|Dependency|Module|Flow";
        var rx = new System.Text.RegularExpressions.Regex(filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var path = Path.Combine(SS_DIR, dllName);
        if (!File.Exists(path)) { Console.WriteLine($"NOT FOUND: {path}"); return; }
        Console.WriteLine($"=== {dllName}  (filter={filter}) ===");
        Assembly asm;
        try { asm = Assembly.LoadFrom(path); }
        catch (Exception e) { Console.WriteLine("LoadFrom failed: " + e.Message); return; }

        // assembly attributes (look for plugin/export markers)
        Console.WriteLine("--- assembly attributes ---");
        try
        {
            foreach (var at in asm.GetCustomAttributesData())
            {
                var s = at.AttributeType.Name;
                if (s.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("Extension", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  ATTR: " + at.AttributeType.FullName + "  " + at.ToString());
            }
        }
        catch (Exception e) { Console.WriteLine("  attrs: " + e.Message); }

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null).ToArray(); Console.WriteLine("  (partial load: " + rtle.LoaderExceptions.Length + " loader errors)"); }
        catch (Exception e) { Console.WriteLine("GetTypes failed: " + e.Message); return; }

        Console.WriteLine($"--- {types.Length} types total, filtering ---");
        foreach (var t in types)
        {
            try
            {
                if (t == null) continue;
                if (!rx.IsMatch(t.Name) && !rx.IsMatch(t.FullName ?? "")) continue;
                var kind = t.IsInterface ? "interface" : (t.IsAbstract ? "abstract class" : "class");
                var baseStr = (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType)) ? " : " + t.BaseType.FullName : "";
                Console.WriteLine($"\nTYPE {kind}: {t.FullName}{baseStr}");
                var ifaces = t.GetInterfaces().Where(i => i.IsPublic).Select(i => i.Name);
                if (ifaces.Any()) Console.WriteLine("  : " + string.Join(", ", ifaces));
                // attributes on the type
                try
                {
                    foreach (var at in t.GetCustomAttributesData())
                    {
                        var n = at.AttributeType.Name;
                        if (n.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Export", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0)
                            Console.WriteLine("  [attr] " + at.AttributeType.FullName);
                    }
                }
                catch { }
                // methods (declared only)
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName) continue;
                    var gp = m.IsGenericMethod ? ("<" + string.Join(",", m.GetGenericArguments().Select(g => g.Name)) + ">") : "";
                    var ps = string.Join(", ", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name));
                    var mods = (m.IsStatic ? "static " : "") + (m.IsAbstract ? "abstract " : "");
                    Console.WriteLine($"  {mods}{Pretty(m.ReturnType)} {m.Name}{gp}({ps})");
                }
                // properties
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    Console.WriteLine($"  prop {Pretty(p.PropertyType)} {p.Name} {{ {(p.GetMethod != null ? "get; " : "")}{(p.SetMethod != null ? "set; " : "")}}}");
                // events
                foreach (var ev in t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    Console.WriteLine($"  event {Pretty(ev.EventHandlerType)} {ev.Name}");
            }
            catch (Exception e) { Console.WriteLine("  (type err: " + e.Message + ")"); }
        }
        Console.WriteLine("\n=== done ===");
    }

    // probe <omlPath> - productKey cache, validity, header, fragment list, and
    // dumps XML of any fragment whose name relates to actions/services.
    static void Probe(string[] a)
    {
        var omlPath = a[1];
        var bytes = File.ReadAllBytes(omlPath);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml");
        dynamic oml = omlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { bytes, "" });

        Console.WriteLine($"=== {omlPath}  ({bytes.Length} bytes) ===");

        // ProductKeyCache
        string pkCache = "";
        try { pkCache = (string)oml.GetProductKeyCache(); } catch (Exception e) { pkCache = "ERR:" + e.Message; }
        Console.WriteLine($"ProductKeyCache len={pkCache?.Length ?? -1}  val={(pkCache == null ? "(null)" : (pkCache.Length > 200 ? pkCache.Substring(0, 200) + "..." : pkCache))}");

        // IsValidOml
        try { Console.WriteLine("IsValidOml=" + omlType.GetMethod("IsValidOml", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes })); }
        catch (Exception e) { Console.WriteLine("IsValidOml ERR: " + e.Message); }

        // OmlHeader fields
        try
        {
            var hdr = omlType.GetMethod("GetOmlHeader", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
            Console.WriteLine("--- OmlHeader ---");
            var ht = hdr.GetType();
            foreach (var f in ht.GetFields())
            {
                try
                {
                    var v = f.GetValue(hdr);
                    var s = v?.ToString() ?? "(null)";
                    if (s.Length > 300) s = s.Substring(0, 300) + "...";
                    if (f.Name.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("Product", StringComparison.OrdinalIgnoreCase) >= 0 || f.FieldType == typeof(string))
                        Console.WriteLine($"  {f.Name} ({f.FieldType.Name}) = {s}");
                }
                catch { }
            }
            foreach (var p in ht.GetProperties())
            {
                try
                {
                    var v = p.GetValue(hdr);
                    var s = v?.ToString() ?? "(null)";
                    if (s.Length > 300) s = s.Substring(0, 300) + "...";
                    Console.WriteLine($"  prop {p.Name} ({p.PropertyType.Name}) = {s}");
                }
                catch { }
            }
        }
        catch (Exception e) { Console.WriteLine("OmlHeader ERR: " + e.Message); }

        // Signature
        try
        {
            dynamic sig = oml.GetSignature();
            string sigStr = sig?.ToString() ?? "(null)";
            if (sigStr.Length > 600) sigStr = sigStr.Substring(0, 600) + "...";
            Console.WriteLine("Signature: " + sigStr);
        }
        catch (Exception e) { Console.WriteLine("Signature ERR: " + e.Message); }

        // fragment names
        var names = new List<string>();
        foreach (var n in (IEnumerable<string>)oml.DumpFragmentsNames()) names.Add(n.ToString());
        Console.WriteLine($"\n=== {names.Count} fragments ===");
        for (int i = 0; i < names.Count; i++) Console.WriteLine($"  [{i,2}] {names[i]}");

        // dump action/service-related fragments
        Console.WriteLine("\n=== action/service-related fragment XML ===");
        foreach (var nm in names)
        {
            if (nm.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0 ||
                nm.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0 ||
                nm.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    dynamic r = oml.GetFragmentXmlReader(nm);
                    XElement xe = (XElement)r.ToXElement();
                    string s = xe.ToString();
                    if (s.Length > 2500) s = s.Substring(0, 2500) + "\n...[TRUNCATED " + (xe.ToString().Length) + " chars]";
                    Console.WriteLine($"\n----- FRAGMENT {nm}  root={xe.Name} children={xe.Elements().Count()} len={xe.ToString().Length} -----");
                    Console.WriteLine(s);
                }
                catch (Exception e) { Console.WriteLine($"  {nm} ERR: {e.Message}"); }
            }
        }
    }

    // addsvc <omlPath> <outOml> [newName] - headlessly create a Service Action by
    // cloning an existing Flows.ServiceAPIMethod (new Key/Name, empty params, empty
    // flow) into the ServiceAPIMethods fragment, regenerating signatures, and
    // verifying the result reloads as a valid OML with the new element present.
    static void AddSvc(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        var newName = a.Length > 3 ? a[3] : "TestServiceAction";
        dynamic oml = LoadOml(omlPath);

        // 1. read ServiceAPIMethods fragment
        XElement root = (XElement)((dynamic)oml.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
        int origCount = int.Parse(root.Attribute("Count")?.Value ?? "0");
        Console.WriteLine($"ServiceAPIMethods: Count={origCount} children={root.Elements().Count()}");

        var template = root.Elements().FirstOrDefault();
        if (template == null) { Console.WriteLine("No ServiceAPIMethod template to clone"); return; }
        Console.WriteLine($"template: Name={template.Attribute("Name")?.Value} Key={template.Attribute("Key")?.Value} Folder={template.Attribute("Folder")?.Value}");

        // 2. fresh ObjectKey (16 bytes -> base64, drop padding, '/'->'_'); matches observed key charset
        byte[] kb = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(kb);
        string newKey = Convert.ToBase64String(kb).TrimEnd('=').Replace('/', '_');
        Console.WriteLine($"newKey={newKey}");

        // 3. clone + modify
        XElement clone = XElement.Parse(template.ToString());
        clone.SetAttributeValue("Key", newKey);
        clone.SetAttributeValue("Name", newName);
        clone.SetAttributeValue("Description", newName);
        clone.SetAttributeValue("Public", "Yes");
        clone.SetAttributeValue("LastModifiedDate", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        clone.Attribute("GeneralHash")?.Remove();
        clone.Attribute("DebuggerHash")?.Remove();
        clone.Attribute("LastModifiedByCommand")?.Remove();
        // clear parameters and node containers
        foreach (var n in new[] { "InputParameters", "OutputParameters", "LocalVariables" })
        {
            var el = clone.Element(n);
            if (el != null) { el.RemoveAll(); el.SetAttributeValue("Count", "0"); }
        }
        var nns = clone.Element("NodesNotShownInESpaceTree");
        if (nns != null) { nns.RemoveAll(); nns.SetAttributeValue("HasChildren", "No"); }
        var nsh = clone.Element("NodesShownInESpaceTree");
        if (nsh != null) nsh.RemoveAll();

        // 4. append + bump Count
        root.Add(clone);
        root.SetAttributeValue("Count", (origCount + 1).ToString());

        // 5. write fragment back
        dynamic writer = oml.GetFragmentXmlWriter("ServiceAPIMethods");
        var wt = ((object)writer).GetType();
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) })
            ?? wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .First(m => { var ps = m.GetParameters(); return ps.Length == 1 && ps[0].ParameterType == typeof(XElement); });
        writeM.Invoke(writer, new object[] { root });
        try { ((IDisposable)writer).Dispose(); } catch { }

        // 6. regenerate signatures + serialize
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        // 7. reload + verify
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml");
        dynamic oml2 = omlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { bytes, "" });
        bool valid = (bool)omlType.GetMethod("IsValidOml", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
        XElement root2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
        Console.WriteLine($"RELOADED IsValidOml={valid}  ServiceAPIMethods Count={root2.Attribute("Count")?.Value} children={root2.Elements().Count()}");
        // header Valid/Signature
        try
        {
            var hdr = omlType.GetMethod("GetOmlHeader", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
            Console.WriteLine($"header Valid={hdr.GetType().GetProperty("Valid").GetValue(hdr)}  IsValid={hdr.GetType().GetProperty("IsValid").GetValue(hdr)}");
        } catch { }
        var found = root2.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == newName);
        if (found != null)
        {
            Console.WriteLine($"FOUND: Name={found.Attribute("Name").Value} Key={found.Attribute("Key").Value} Public={found.Attribute("Public")?.Value} Folder={found.Attribute("Folder")?.Value} HasChildren={found.Element("NodesNotShownInESpaceTree")?.Attribute("HasChildren")?.Value}");
            Console.WriteLine("RESULT: SERVICE ACTION CREATED HEADLESSLY (valid OML, element present after reload)");
        }
        else
            Console.WriteLine("RESULT: new element NOT found after reload - FAILED");
    }

    // mksvc <omlPath> <outOml> <newName> - create a Service Action in a module that
    // has NONE yet: marks eSpace <ServiceAPIMethods HasChildren="Yes"> and creates a
    // brand-new ServiceAPIMethods fragment (tests fragment creation) with one
    // root-level Flows.ServiceAPIMethod. Regenerates signatures, verifies reload.
    static void MkSvc(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        var newName = a.Length > 3 ? a[3] : "TestHeadlessSvc";
        dynamic oml = LoadOml(omlPath);

        // fresh ObjectKey
        byte[] kb = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(kb);
        string newKey = Convert.ToBase64String(kb).TrimEnd('=').Replace('/', '_');
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine($"newKey={newKey}  name={newName}");

        // 1. mark eSpace <ServiceAPIMethods HasChildren="Yes">
        XElement es = (XElement)((dynamic)oml.GetFragmentXmlReader("eSpace")).ToXElement();
        var sam = es.Element("ServiceAPIMethods");
        if (sam == null) { Console.WriteLine("eSpace has no <ServiceAPIMethods> element"); return; }
        bool wasEmpty = !sam.HasElements;
        sam.SetAttributeValue("HasChildren", "Yes");
        Console.WriteLine($"eSpace <ServiceAPIMethods> wasEmpty={wasEmpty} -> HasChildren=Yes");
        dynamic esWriter = oml.GetFragmentXmlWriter("eSpace");
        WriteFragment(esWriter, es);
        try { ((IDisposable)esWriter).Dispose(); } catch { }

        // 2. build a minimal root-level ServiceAPIMethod element
        var svc = new XElement("Flows.ServiceAPIMethod",
            new XAttribute("Key", newKey),
            new XAttribute("CreatedBy", "headless"),
            new XAttribute("LastModifiedBy", "headless"),
            new XAttribute("LastModifiedDate", now),
            new XAttribute("Name", newName),
            new XAttribute("Description", newName),
            new XAttribute("Public", "Yes"),
            new XElement("Image"),
            new XElement("TextResources"),
            new XElement("NodesShownInESpaceTree"),
            new XElement("NodesNotShownInESpaceTree", new XAttribute("HasChildren", "No")),
            new XElement("Metadata"),
            new XElement("LocalVariables"),
            new XElement("InputParameters"),
            new XElement("OutputParameters"));
        XElement fragRoot = new XElement("eSpaceFragment", new XAttribute("Count", "1"), svc);

        // 3. create the ServiceAPIMethods fragment (test: does GetFragmentXmlWriter create it?)
        Console.WriteLine("HasFragment(ServiceAPIMethods) before = " + oml.HasFragment("ServiceAPIMethods"));
        bool created = false;
        try
        {
            dynamic w = oml.GetFragmentXmlWriter("ServiceAPIMethods");
            WriteFragment(w, fragRoot);
            try { ((IDisposable)w).Dispose(); } catch { }
            created = true;
            Console.WriteLine("GetFragmentXmlWriter(ServiceAPIMethods).Write -> OK (fragment created/written)");
        }
        catch (Exception e)
        {
            var r = e; while (r.InnerException != null) r = r.InnerException;
            Console.WriteLine("GetFragmentXmlWriter(ServiceAPIMethods) FAILED: " + r.GetType().Name + ": " + r.Message);
            Console.WriteLine("-> fragment creation via string overload not supported; aborting.");
            return;
        }

        // 4. regenerate signatures + serialize
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        // 5. reload + verify
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml");
        dynamic oml2 = omlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { bytes, "" });
        bool valid = (bool)omlType.GetMethod("IsValidOml", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
        bool hasFrag = (bool)oml2.HasFragment("ServiceAPIMethods");
        Console.WriteLine($"RELOADED IsValidOml={valid}  HasFragment(ServiceAPIMethods)={hasFrag}");
        if (hasFrag)
        {
            XElement r2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
            var found = r2.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == newName);
            Console.WriteLine($"ServiceAPIMethods children={r2.Elements().Count()}");
            if (found != null)
            {
                Console.WriteLine($"FOUND: Name={found.Attribute("Name").Value} Key={found.Attribute("Key").Value} Public={found.Attribute("Public")?.Value} Folder={found.Attribute("Folder")?.Value ?? "(root)"}");
                Console.WriteLine("RESULT: SERVICE ACTION CREATED (new fragment + element, valid OML)");
            }
            else Console.WriteLine("RESULT: fragment present but element NOT found - PARTIAL");
        }
        else Console.WriteLine("RESULT: fragment NOT present after reload - FAILED");
    }

    // clonesvc <omlPath> <outOml> <newName> - clone the first Service Action (and its
    // flow fragment) into a new one with a fresh Key/Name. Remaps all node keys in
    // the cloned flow. Tests creating a new fragment; falls back to empty flow
    // (HasChildren=No) if fragment creation is unsupported. Regenerates + verifies.
    static void CloneSvc(string[] a)
    {
        var omlPath = a[1];
        var outOml = a[2];
        var newName = a.Length > 3 ? a[3] : "TestHeadlessSvc";
        dynamic oml = LoadOml(omlPath);

        string NewKey()
        {
            byte[] kb = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(kb);
            return Convert.ToBase64String(kb).TrimEnd('=').Replace('/', '_');
        }
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. read ServiceAPIMethods + template element
        XElement samRoot = (XElement)((dynamic)oml.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
        int origCount = int.Parse(samRoot.Attribute("Count")?.Value ?? "0");
        var template = samRoot.Elements().FirstOrDefault();
        if (template == null) { Console.WriteLine("no ServiceAPIMethod to clone"); return; }
        string tmplKey = template.Attribute("Key").Value;
        Console.WriteLine($"template: Name={template.Attribute("Name")?.Value} Key={tmplKey}  HasChildren={template.Element("NodesNotShownInESpaceTree")?.Attribute("HasChildren")?.Value}");

        string newActionKey = NewKey();
        string newFlowFrag = "NodesNotShownInESpaceTree#" + newActionKey;

        // 2. clone the flow fragment (remap node/link keys) - read from ORIGINAL oml state
        bool flowCreated = false;
        XElement newFlow = null;
        string tmplFlowFrag = "NodesNotShownInESpaceTree#" + tmplKey;
        if ((bool)oml.HasFragment(tmplFlowFrag))
        {
            XElement flow = (XElement)((dynamic)oml.GetFragmentXmlReader(tmplFlowFrag)).ToXElement();
            var oldKeys = new HashSet<string>();
            foreach (var el in flow.DescendantsAndSelf())
            {
                var k = el.Attribute("Key");
                if (k != null && !string.IsNullOrEmpty(k.Value)) oldKeys.Add(k.Value);
            }
            var keyMap = oldKeys.ToDictionary(k => k, k => NewKey());
            string flowXml = flow.ToString();
            foreach (var kv in keyMap) flowXml = flowXml.Replace(kv.Key, kv.Value);
            newFlow = XElement.Parse(flowXml);
            Console.WriteLine($"flow: {tmplFlowFrag} -> {newFlowFrag}  ({keyMap.Count} keys remapped, {newFlow.Elements().Count()} nodes)");

            // try to CREATE the new flow fragment
            try
            {
                dynamic fw = oml.GetFragmentXmlWriter(newFlowFrag);
                WriteFragment(fw, newFlow);
                try { ((IDisposable)fw).Dispose(); } catch { }
                flowCreated = true;
                Console.WriteLine("  flow fragment CREATED via GetFragmentXmlWriter(string)");
            }
            catch (Exception e)
            {
                var r = e; while (r.InnerException != null) r = r.InnerException;
                Console.WriteLine("  flow fragment creation FAILED: " + r.GetType().Name + ": " + r.Message);
                Console.WriteLine("  -> falling back to empty flow (HasChildren=No)");
            }
        }
        else Console.WriteLine("template has no flow fragment; will create action with empty flow");

        // 3. clone the ServiceAPIMethod element
        XElement clone = XElement.Parse(template.ToString());
        clone.SetAttributeValue("Key", newActionKey);
        clone.SetAttributeValue("Name", newName);
        clone.SetAttributeValue("Description", newName);
        clone.SetAttributeValue("LastModifiedDate", now);
        clone.Attribute("GeneralHash")?.Remove();
        clone.Attribute("DebuggerHash")?.Remove();
        clone.Attribute("LastModifiedByCommand")?.Remove();
        var nns = clone.Element("NodesNotShownInESpaceTree");
        if (nns != null) { nns.RemoveAll(); nns.SetAttributeValue("HasChildren", flowCreated ? "Yes" : "No"); }
        samRoot.Add(clone);
        samRoot.SetAttributeValue("Count", (origCount + 1).ToString());
        dynamic samWriter = oml.GetFragmentXmlWriter("ServiceAPIMethods");
        WriteFragment(samWriter, samRoot);
        try { ((IDisposable)samWriter).Dispose(); } catch { }

        // 4. regenerate + serialize
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        Console.WriteLine($"wrote {bytes.Length} bytes -> {outOml}");

        // 5. verify
        var impl = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        var omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml");
        dynamic oml2 = omlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { bytes, "" });
        bool valid = (bool)omlType.GetMethod("IsValidOml", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
        bool hasFlow = (bool)oml2.HasFragment(newFlowFrag);
        XElement sam2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
        var found = sam2.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == newName);
        Console.WriteLine($"RELOADED IsValidOml={valid}  HasFragment(flow)={hasFlow}  ServiceAPIMethods children={sam2.Elements().Count()} (orig {origCount})");
        if (found != null)
        {
            Console.WriteLine($"FOUND: Name={found.Attribute("Name").Value} Key={found.Attribute("Key").Value} Public={found.Attribute("Public")?.Value} flow={(hasFlow?"cloned":"empty")}");
            Console.WriteLine("RESULT: SERVICE ACTION CLONED HEADLESSLY (valid OML)");
        }
        else Console.WriteLine("RESULT: element NOT found after reload - FAILED");
    }

    static void WriteFragment(dynamic writer, XElement root)
    {
        var wt = ((object)writer).GetType();
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) })
            ?? wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .First(m => { var ps = m.GetParameters(); return ps.Length == 1 && ps[0].ParameterType == typeof(XElement); });
        writeM.Invoke(writer, new object[] { root });
    }

    static string Pretty(Type t)
    {
        if (t == null) return "?";
        var n = t.IsGenericType ? t.Name.Split('`')[0] : t.Name;
        if (t.IsGenericType)
        {
            var args = t.GetGenericArguments().Select(Pretty);
            n += "<" + string.Join(", ", args) + ">";
        }
        return n;
    }

    // pluginpaths - load PluginAPI and print all directories SS scans for plugins.
    static void PluginPaths(string[] a)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveSs;
        var asm = Assembly.LoadFrom(Path.Combine(SS_DIR, "ServiceStudio.PluginAPI.dll"));
        var t = asm.GetType("ServiceStudio.PluginAPI.PluginProvider");
        if (t == null) { Console.WriteLine("PluginProvider not found"); return; }
        // try static property PluginDirectoryPaths
        var prop = t.GetProperty("PluginDirectoryPaths", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
        if (prop == null)
        {
            // list all static properties/getters to find the right name
            Console.WriteLine("No public static PluginDirectoryPaths. Static props:");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                Console.WriteLine("  " + p.PropertyType.Name + " " + p.Name);
            return;
        }
        var val = prop.GetValue(null, null);
        Console.WriteLine("PluginDirectoryPaths -> " + (val?.GetType().FullName ?? "null"));
        if (val is IEnumerable<string> strs)
            foreach (var p in strs) Console.WriteLine("  PATH: " + p);
        else if (val is IEnumerable en)
            foreach (var p in en) Console.WriteLine("  PATH: " + p);
    }
}
