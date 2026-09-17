// OmlExtractor - loads an OutSystems .oml via the Service Studio DLLs and
// prints module header + entity/action names. No compile-time references to
// OutSystems assemblies: everything is resolved at runtime from the SS install
// folder via an AssemblyResolve handler (bulletproof against dependency chains).
//
// Verified API surface (Service Studio 11.0.415.100):
//   OutSystems.Model.Implementation.dll
//     namespace OutSystems.Model.Implementation.Oml
//       class Oml   (static loaders + per-instance XML access)
//         static Oml  LoadWithoutUpgrades(byte[] omlContent, string productKey)
//         static OmlHeader GetOmlHeader(string path)          // no productKey
//         static bool IsValidOml(Stream)
//         static Oml  DebugCompress(string path)
//         static void DebugDecompress(string path)            // dumps XML, no productKey
//         instance: GetESpaceXmlReader()  -> XElementReader   // main eSpace fragment XML
//                    ApplyToXmlFragments(Action<XElement>, bool saveChanges)
//                    DumpFragmentsNames() -> IEnumerable<string>
//                    GetBytes() / WriteTo(Stream)
//   OutSystems.Model.V1.dll  (netstandard2.0 - the public model interfaces)
//     OutSystems.Model.IModelServices  .LoadESpace(byte[], secretKey, productKey, upgradeInfo) -> IESpace
//     OutSystems.Model.IESpace         .Entities .ServerActions .ServiceActions .ClientActions
//                                       .Structures .Processes .SiteProperties .Timers
//     OutSystems.Model.Data.IEntity    .Name .Attributes .Description .Public
//     OutSystems.Model.Data.IEntityAttribute .Name .DataType .Label .IsAutoNumber
//     OutSystems.Model.Logic.IAction   .InputParameters .OutputParameters .Nodes
//
// productKey gate: full model loading (LoadWithoutUpgrades / LoadESpace) needs a
// productKey obtained from the platform server via the ServiceStudioSoap
// 'CanOpenOml'/'Handshake' calls (see sc-download.sh). GetOmlHeader and
// DebugDecompress do NOT need it. Try an empty productKey first - the
// "WithoutUpgrades" path sometimes loads read-only without validation.
//
// Usage:
//   dotnet run -- "<path-to-file.oml>" [productKey] [SS install dir]
//   dotnet run -- "C:\Users\ricar\Downloads\TestAIModules\AdminTool.oml"
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace OmlExtractor;

internal static class Program
{
    private static string _ssDir =
        @"C:\Program Files\OutSystems\Service Studio 11\Service Studio";

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: OmlExtractor <file.oml> [productKey] [ssDir]");
            return 1;
        }
        string omlPath = args[0];
        string productKey = args.Length > 1 ? args[1] : "";
        if (args.Length > 2) _ssDir = args[2];

        if (!File.Exists(omlPath)) { Console.Error.WriteLine("Not found: " + omlPath); return 1; }
        if (!Directory.Exists(_ssDir)) { Console.Error.WriteLine("SS dir not found: " + _ssDir); return 1; }

        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSsDir;

        // 1. Header - no productKey, works offline. Matches Parse-OmlHeader.ps1.
        PrintHeader(omlPath);

        // 2. Load the implementation assembly and locate the Oml loader type.
        Assembly impl = Assembly.LoadFrom(Path.Combine(_ssDir, "OutSystems.Model.Implementation.dll"));
        Type omlType = impl.GetType("OutSystems.Model.Implementation.Oml.Oml")
                       ?? throw new InvalidOperationException("Oml type not found");

        // 3. Try the full load (needs productKey). Fall back to DebugDecompress.
        dynamic oml = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(omlPath);
            oml = omlType.GetMethod("LoadWithoutUpgrades")
                        .Invoke(null, new object[] { bytes, productKey });
            Console.WriteLine("\n[OK] Oml.LoadWithoutUpgrades succeeded.");
        }
        catch (Exception ex)
        {
            Exception real = ex;
            while (real.InnerException != null) real = real.InnerException;
            Console.WriteLine("\n[!] LoadWithoutUpgrades failed.");
            Console.WriteLine("    Exception type: " + real.GetType().FullName);
            Console.WriteLine("    Message: " + real.Message);
            Console.WriteLine("    Stack: " + real.StackTrace?.TrimEnd());
            Console.WriteLine("    Falling back to Oml.DebugDecompress (no productKey)...");
            try
            {
                omlType.GetMethod("DebugDecompress").Invoke(null, new object[] { omlPath });
                Console.WriteLine("    Decompressed XML dumped next to the .oml. Inspect it for tags.");
            }
            catch (Exception ex2)
            {
                Exception real2 = ex2;
                while (real2.InnerException != null) real2 = real2.InnerException;
                Console.WriteLine("    DebugDecompress failed: " + real2.GetType().Name + ": " + real2.Message);
            }
            return 0;
        }

        // 4. Enumerate fragments and locate the eSpace fragment.
        Console.WriteLine("\n=== Fragments ===");
        foreach (var name in (IEnumerable)oml.DumpFragmentsNames())
            Console.WriteLine("  - " + name);

        // 5. Pull XML out of every fragment; the eSpace fragment holds entities/actions.
        //    ApplyToXmlFragments(Action<XElement>, bool) is the typed, reliable accessor.
        Console.WriteLine("\n=== Fragment XML roots ===");
        var fragments = new System.Collections.Generic.List<XElement>();
        oml.ApplyToXmlFragments(new Action<XElement>(xe =>
        {
            fragments.Add(xe);
            Console.WriteLine($"  <{xe.Name}>  attrs={xe.Attributes().Count()}  children={xe.Elements().Count()}");
        }), false);

        // 6. The eSpace fragment is the one whose root is the module/eSpace element.
        //    Save the full eSpace XML so the schema is visible, then extract names.
        XElement eSpace = fragments
            .OrderByDescending(xe => xe.ToString().Length)
            .FirstOrDefault()
            ?? fragments.FirstOrDefault();

        if (eSpace != null)
        {
            string xmlOut = Path.ChangeExtension(omlPath, ".espace.xml");
            eSpace.Save(xmlOut);
            Console.WriteLine("\nFull eSpace XML written to: " + xmlOut);

            ExtractMetadata(eSpace);
        }

        return 0;
    }

    private static void PrintHeader(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        string magic = "" + (char)b[0] + (char)b[1] + (char)b[2];
        Console.WriteLine("=== Header (offline, no productKey) ===");
        Console.WriteLine("Magic: " + magic);

        int idx = -1;
        for (int i = 7; i < b.Length - 4; i++)
            if (b[i] == 0x77 && b[i + 1] == 0x67 && b[i + 2] == 0x7C && b[i + 3] == 0x2F) { idx = i; break; }
        if (idx < 0) { Console.WriteLine("(body marker 'wg|/' not found)"); return; }

        string hdr = "";
        for (int i = 7; i < idx; i++) hdr += (char)b[i];
        string[] f = hdr.Split('|');
        Console.WriteLine($"SSVersion   : {f[0]}");
        Console.WriteLine($"PlatVersion : {f[1]}");
        Console.WriteLine($"ESpaceKey   : {f[3]}   <- use with ServiceCenter Download()");
        Console.WriteLine($"IsExtension : {f[5]}");
        Console.WriteLine($"ModuleName  : {f[6]}");
        Console.WriteLine($"Description : {f[7]}");
        Console.WriteLine($"SavedAt     : {f[8]}");
    }

    private static void ExtractMetadata(XElement eSpace)
    {
        Console.WriteLine("\n=== Entities ===");
        int eCount = 0;
        foreach (var ent in DescendantsNamed(eSpace, "Entity", "ServerEntity", "ClientEntity", "StaticEntity"))
        {
            string name = (string)ent.Attribute("Name") ?? ent.Name.LocalName;
            Console.WriteLine($"- {name}");
            foreach (var attr in DescendantsNamed(ent, "Attribute", "EntityAttribute"))
            {
                string an = (string)attr.Attribute("Name") ?? attr.Name.LocalName;
                string dt = (string)attr.Attribute("DataType") ?? (string)attr.Attribute("Type") ?? "";
                Console.WriteLine($"    . {an}  {dt}");
            }
            eCount++;
        }
        if (eCount == 0) Console.WriteLine("(no <Entity> elements - check .espace.xml for actual tag names)");

        Console.WriteLine("\n=== Actions ===");
        int aCount = 0;
        foreach (var act in DescendantsNamed(eSpace, "ServerAction", "ClientAction",
                                              "ServiceAction", "Action"))
        {
            string name = (string)act.Attribute("Name") ?? act.Name.LocalName;
            Console.WriteLine($"- [{act.Name.LocalName}] {name}");
            aCount++;
        }
        if (aCount == 0) Console.WriteLine("(no action elements - check .espace.xml for actual tag names)");

        Console.WriteLine("\n=== Structures ===");
        foreach (var st in DescendantsNamed(eSpace, "Structure"))
        {
            string name = (string)st.Attribute("Name") ?? st.Name.LocalName;
            Console.WriteLine($"- {name}");
        }
    }

    private static System.Collections.Generic.IEnumerable<XElement> DescendantsNamed(
        XElement root, params string[] localNames)
    {
        var set = new System.Collections.Generic.HashSet<string>(localNames);
        foreach (var e in root.Descendants())
            if (set.Contains(e.Name.LocalName))
                yield return e;
    }

    private static Assembly ResolveFromSsDir(object sender, ResolveEventArgs e)
    {
        string name = new AssemblyName(e.Name).Name + ".dll";
        string path = Path.Combine(_ssDir, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
