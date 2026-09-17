// OmlEditor - headless OutSystems .oml editor backend. Loads a .oml via
// Oml.LoadWithoutUpgrades(bytes, "") (empty productKey is fine), reads/writes
// XML fragments, regenerates signatures, and writes a valid .oml. No Service
// Studio UI, no running process, no productKey. Requires the SS 11 DLLs on disk
// (loaded via Assembly.LoadFrom from the SS install dir) - SS need not be running.
//
// Proven operations (verified end-to-end, IsValidOml=True, confirmed in SS):
//   - add an element to an existing fragment (service action, reference)
//   - CREATE a new fragment (GetFragmentXmlWriter(string) creates on write)
//   - clone an element + its flow fragment with node-key remapping
//   - regenerate signatures (SetNeedsSignatureRegeneration) -> content hashes
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OutSystemsMcpOmlEditor;

internal static class OmlEditor
{
    // SS install dir holds OutSystems.Model.Implementation.dll and its deps.
    // Override via the OSSS_DIR env var; otherwise the standard install path.
    static string SS_DIR =>
        Environment.GetEnvironmentVariable("OSSS_DIR") ??
        @"C:\Program Files\OutSystems\Service Studio 11\Service Studio";

    static bool _resolverAttached;
    static Assembly _implAssembly;

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

    static Assembly ImplAssembly()
    {
        EnsureResolver();
        if (_implAssembly == null)
            _implAssembly = Assembly.LoadFrom(Path.Combine(SS_DIR, "OutSystems.Model.Implementation.dll"));
        return _implAssembly;
    }

    static Type OmlType => ImplAssembly().GetType("OutSystems.Model.Implementation.Oml.Oml")
        ?? throw new Exception("OutSystems.Model.Implementation.Oml.Oml type not found");

    // Load a .oml file headless (empty productKey). Returns the Oml dynamic object.
    public static dynamic LoadOml(string omlPath)
    {
        var bytes = File.ReadAllBytes(omlPath);
        return OmlType.GetMethod("LoadWithoutUpgrades").Invoke(null, new object[] { bytes, "" });
    }

    public static string NewKey()
    {
        byte[] kb = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
        // 16 bytes -> 22-char base64, no padding, '/' -> '_' (matches OS ObjectKey charset)
        return Convert.ToBase64String(kb).TrimEnd('=').Replace('/', '_');
    }

    // Invoke Writer.Write(XElement). Works for existing fragments AND creates a
    // new fragment when the name does not yet exist (proven: flow fragment created).
    public static void WriteFragment(dynamic writer, XElement root)
    {
        var wt = ((object)writer).GetType();
        var writeM = wt.GetMethod("Write", new[] { typeof(XElement) })
            ?? wt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .First(m => { var ps = m.GetParameters(); return ps.Length == 1 && ps[0].ParameterType == typeof(XElement); });
        writeM.Invoke(writer, new object[] { root });
    }

    // ---- verification helpers ----
    public static bool IsValidOml(byte[] bytes) =>
        (bool)OmlType.GetMethod("IsValidOml", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });

    public static string HeaderValid(byte[] bytes)
    {
        try
        {
            var hdr = OmlType.GetMethod("GetOmlHeader", new[] { typeof(byte[]) }).Invoke(null, new object[] { bytes });
            var t = hdr.GetType();
            return $"Valid={t.GetProperty("Valid").GetValue(hdr)} IsValid={t.GetProperty("IsValid").GetValue(hdr)}";
        }
        catch (Exception e) { return "header-err: " + e.Message; }
    }

    // =====================================================================
    //  probe_oml: fragments, validity, productKey cache, signature, action XML
    // =====================================================================
    public static string Probe(string omlPath)
    {
        var sb = new StringBuilder();
        var bytes = File.ReadAllBytes(omlPath);
        dynamic oml = LoadOml(omlPath);
        sb.AppendLine($"=== {Path.GetFileName(omlPath)}  ({bytes.Length} bytes) ===");
        string pk = ""; try { pk = (string)oml.GetProductKeyCache(); } catch { }
        sb.AppendLine($"ProductKeyCache len={pk?.Length ?? -1}");
        sb.AppendLine($"IsValidOml={IsValidOml(bytes)}  {HeaderValid(bytes)}");
        var names = new List<string>();
        foreach (var n in (IEnumerable<string>)oml.DumpFragmentsNames()) names.Add(n.ToString());
        sb.AppendLine($"=== {names.Count} fragments ===");
        for (int i = 0; i < names.Count; i++) sb.AppendLine($"  [{i,2}] {names[i]}");
        return sb.ToString();
    }

    // =====================================================================
    //  get_fragment: dump a fragment's XML
    // =====================================================================
    public static string GetFragment(string omlPath, string fragment)
    {
        dynamic oml = LoadOml(omlPath);
        if (!((bool)oml.HasFragment(fragment))) return $"MISSING fragment: {fragment}";
        XElement xe = (XElement)((dynamic)oml.GetFragmentXmlReader(fragment)).ToXElement();
        return xe.ToString();
    }

    // =====================================================================
    //  scan_oml: list fragments whose XML contains needle
    // =====================================================================
    public static string Scan(string omlPath, string needle)
    {
        var sb = new StringBuilder();
        dynamic oml = LoadOml(omlPath);
        var names = new List<string>();
        foreach (var n in (IEnumerable<string>)oml.DumpFragmentsNames()) names.Add(n.ToString());
        sb.AppendLine($"scanning {names.Count} fragments for '{needle}'");
        int hits = 0;
        foreach (var nm in names)
        {
            try
            {
                XElement xe = (XElement)((dynamic)oml.GetFragmentXmlReader(nm)).ToXElement();
                if (xe.ToString().Contains(needle)) { hits++; sb.AppendLine($"  HIT {nm}  len={xe.ToString().Length}"); }
            }
            catch (Exception e) { sb.AppendLine($"  ERR {nm}: {e.Message}"); }
        }
        sb.AppendLine($"hits: {hits}");
        return sb.ToString();
    }

    // =====================================================================
    //  create_service_action: clone a template (or build from scratch if the
    //  module has no ServiceAPIMethods fragment yet). Regenerates + verifies.
    // =====================================================================
    public static string CreateServiceAction(string omlPath, string outOml, string name, string templateName)
    {
        var sb = new StringBuilder();
        dynamic oml = LoadOml(omlPath);
        string newKey = NewKey();
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        sb.AppendLine($"newKey={newKey} name={name}");

        bool hasSam = (bool)oml.HasFragment("ServiceAPIMethods");
        bool flowCreated = false;

        if (hasSam)
        {
            // --- clone path ---
            XElement samRoot = (XElement)((dynamic)oml.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
            int origCount = int.Parse(samRoot.Attribute("Count")?.Value ?? "0");
            var template = string.IsNullOrEmpty(templateName)
                ? samRoot.Elements().FirstOrDefault()
                : samRoot.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == templateName);
            if (template == null) { sb.AppendLine($"ERROR: template '{templateName}' not found"); return sb.ToString(); }
            string tmplKey = template.Attribute("Key").Value;
            sb.AppendLine($"template: Name={template.Attribute("Name")?.Value} Key={tmplKey}");

            // clone the flow fragment (remap node keys) if present
            string tmplFlow = "NodesNotShownInESpaceTree#" + tmplKey;
            string newFlow = "NodesNotShownInESpaceTree#" + newKey;
            if ((bool)oml.HasFragment(tmplFlow))
            {
                XElement flow = (XElement)((dynamic)oml.GetFragmentXmlReader(tmplFlow)).ToXElement();
                var oldKeys = new HashSet<string>();
                foreach (var el in flow.DescendantsAndSelf())
                { var k = el.Attribute("Key"); if (k != null && !string.IsNullOrEmpty(k.Value)) oldKeys.Add(k.Value); }
                var map = oldKeys.ToDictionary(k => k, k => NewKey());
                string xml = flow.ToString();
                foreach (var kv in map) xml = xml.Replace(kv.Key, kv.Value);
                XElement newFlowEl = XElement.Parse(xml);
                try { dynamic fw = oml.GetFragmentXmlWriter(newFlow); WriteFragment(fw, newFlowEl); try { ((IDisposable)fw).Dispose(); } catch { } flowCreated = true; sb.AppendLine($"flow cloned+created ({map.Count} keys remapped)"); }
                catch (Exception e) { sb.AppendLine($"flow clone FAILED: {e.Message} -> empty flow"); }
            }

            // clone the ServiceAPIMethod element
            XElement clone = XElement.Parse(template.ToString());
            clone.SetAttributeValue("Key", newKey);
            clone.SetAttributeValue("Name", name);
            clone.SetAttributeValue("Description", name);
            clone.SetAttributeValue("LastModifiedDate", now);
            clone.Attribute("GeneralHash")?.Remove();
            clone.Attribute("DebuggerHash")?.Remove();
            clone.Attribute("LastModifiedByCommand")?.Remove();
            var nns = clone.Element("NodesNotShownInESpaceTree");
            if (nns != null) { nns.RemoveAll(); nns.SetAttributeValue("HasChildren", flowCreated ? "Yes" : "No"); }
            samRoot.Add(clone);
            samRoot.SetAttributeValue("Count", (origCount + 1).ToString());
            dynamic w = oml.GetFragmentXmlWriter("ServiceAPIMethods"); WriteFragment(w, samRoot); try { ((IDisposable)w).Dispose(); } catch { }
        }
        else
        {
            // --- from-scratch path (module has no service actions yet) ---
            XElement es = (XElement)((dynamic)oml.GetFragmentXmlReader("eSpace")).ToXElement();
            var sam = es.Element("ServiceAPIMethods");
            if (sam == null) { sb.AppendLine("ERROR: eSpace has no <ServiceAPIMethods>"); return sb.ToString(); }
            sam.SetAttributeValue("HasChildren", "Yes");
            dynamic esW = oml.GetFragmentXmlWriter("eSpace"); WriteFragment(esW, es); try { ((IDisposable)esW).Dispose(); } catch { }

            var svc = new XElement("Flows.ServiceAPIMethod",
                new XAttribute("Key", newKey),
                new XAttribute("CreatedBy", "headless"),
                new XAttribute("LastModifiedBy", "headless"),
                new XAttribute("LastModifiedDate", now),
                new XAttribute("Name", name),
                new XAttribute("Description", name),
                new XAttribute("Public", "Yes"),
                new XElement("Image"),
                new XElement("TextResources"),
                new XElement("NodesShownInESpaceTree"),
                new XElement("NodesNotShownInESpaceTree", new XAttribute("HasChildren", "No")),
                new XElement("Metadata"),
                new XElement("LocalVariables"),
                new XElement("InputParameters"),
                new XElement("OutputParameters"));
            XElement frag = new XElement("eSpaceFragment", new XAttribute("Count", "1"), svc);
            dynamic fw = oml.GetFragmentXmlWriter("ServiceAPIMethods"); WriteFragment(fw, frag); try { ((IDisposable)fw).Dispose(); } catch { }
            sb.AppendLine("created ServiceAPIMethods fragment from scratch");
        }

        // regen + write
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        sb.AppendLine($"wrote {bytes.Length} bytes -> {outOml}");

        // verify
        dynamic oml2 = LoadOml(outOml);
        bool valid = IsValidOml(bytes);
        XElement sam2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("ServiceAPIMethods")).ToXElement();
        var found = sam2.Elements().FirstOrDefault(e => (e.Attribute("Name")?.Value ?? "") == name);
        sb.AppendLine($"RELOADED IsValidOml={valid} {HeaderValid(bytes)} ServiceAPIMethods children={sam2.Elements().Count()}");
        sb.AppendLine(found != null
            ? $"OK: created '{name}' Key={found.Attribute("Key").Value} Public={found.Attribute("Public")?.Value} flow={(flowCreated ? "cloned" : (hasSam ? "empty" : "none"))}"
            : "FAIL: element not found after reload");
        return sb.ToString();
    }

    // =====================================================================
    //  add_dependency: append a <Reference> to the References fragment, bump
    //  Count, regenerate. Proven (AddDep). referenceXml = a <Reference> element.
    // =====================================================================
    public static string AddDependency(string omlPath, string outOml, string referenceXml)
    {
        var sb = new StringBuilder();
        dynamic oml = LoadOml(omlPath);
        XElement root = (XElement)((dynamic)oml.GetFragmentXmlReader("References")).ToXElement();
        XElement newRef = XElement.Parse(referenceXml);
        string refName = newRef.Attribute("Name")?.Value ?? "?";
        int cnt = int.Parse(root.Attribute("Count")?.Value ?? "0");
        root.Add(newRef);
        root.SetAttributeValue("Count", (cnt + 1).ToString());
        dynamic w = oml.GetFragmentXmlWriter("References"); WriteFragment(w, root); try { ((IDisposable)w).Dispose(); } catch { }
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        dynamic oml2 = LoadOml(outOml);
        XElement root2 = (XElement)((dynamic)oml2.GetFragmentXmlReader("References")).ToXElement();
        bool found = root2.Elements().Any(e => (e.Attribute("Name")?.Value ?? "") == refName);
        sb.AppendLine($"wrote {bytes.Length} bytes -> {outOml}");
        sb.AppendLine($"RELOADED IsValidOml={IsValidOml(bytes)} References children={root2.Elements().Count()}");
        sb.AppendLine(found ? $"OK: reference '{refName}' present" : "FAIL: reference not found after reload");
        return sb.ToString();
    }

    // =====================================================================
    //  regen_and_write: load, regen signatures, write (round-trip / fixup).
    // =====================================================================
    public static string RegenAndWrite(string omlPath, string outOml)
    {
        var sb = new StringBuilder();
        dynamic oml = LoadOml(omlPath);
        oml.SetNeedsSignatureRegeneration();
        byte[] bytes = oml.GetBytes();
        File.WriteAllBytes(outOml, bytes);
        sb.AppendLine($"wrote {bytes.Length} bytes -> {outOml}");
        sb.AppendLine($"IsValidOml={IsValidOml(bytes)} {HeaderValid(bytes)}");
        return sb.ToString();
    }
}
