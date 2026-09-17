// ModuleAttributionProbe — investigates whether model objects (Screen, WebBlock,
// Entity, Action, Structure) can be attributed to their owning module (OmlHeader)
// via the `ownerESpace` back-reference field.
//
// Strategy:
//   1. Find ALL OmlHeader objects in the heap → record address + module name
//   2. Find sample objects of each model type (Screen, WebBlock, Entity, etc.)
//   3. Read each sample's `ownerESpace` field
//   4. Check if ownerESpace address matches any OmlHeader address (direct hit)
//   5. If not direct, follow ownerESpace's fields to find a path to OmlHeader
//   6. Also dump OmlHeader's object-reference fields (forward-pointers)
//
// Usage: ModuleAttributionProbe.exe <pid>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

class Program
{
    static ClrHeap g_heap;

    static void Main(string[] args)
    {
        int pid = args.Length > 0 ? int.Parse(args[0]) : 0;
        if (pid == 0) { Console.WriteLine("Usage: ModuleAttributionProbe <pid>"); return; }
        Console.WriteLine($"Attaching to PID {pid}...");

        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        g_heap = runtime.Heap;
        if (!g_heap.CanWalkHeap) { Console.WriteLine("ERROR: Cannot walk heap."); return; }

        // ---- Phase 1: Find ALL OmlHeader objects ----
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("PHASE 1: Find all OmlHeader objects");
        Console.WriteLine(new string('=', 70));
        var headers = new List<(ulong addr, string name, ClrObject obj)>();
        foreach (var obj in g_heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (!fn.EndsWith(".OmlHeader", StringComparison.Ordinal)) continue;
            var name = ExtractModuleName(obj);
            headers.Add((obj.Address, name, obj));
            Console.WriteLine($"  OmlHeader @ 0x{obj.Address:X} -> \"{name}\"");
        }
        Console.WriteLine($"\n  Total OmlHeader objects found: {headers.Count}");
        if (headers.Count == 0) { Console.WriteLine("No OmlHeader found. Aborting."); return; }

        var headerByAddr = headers.ToDictionary(h => h.addr, h => h);

        // ---- Phase 2: Dump OmlHeader's object-reference fields (forward pointers) ----
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("PHASE 2: OmlHeader object-reference fields (forward pointers)");
        Console.WriteLine(new string('=', 70));
        var firstHeader = headers[0].obj;
        Console.WriteLine($"\n  Examining first OmlHeader (\"{headers[0].name}\") @ 0x{firstHeader.Address:X}");
        Console.WriteLine("  Type: " + firstHeader.Type?.Name);
        var refFields = new List<(string name, string ftype, ulong addr, string targetTypeName)>();
        for (var t = firstHeader.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (!f.IsObjectReference) continue;
                try
                {
                    var val = f.ReadObject(firstHeader.Address, false);
                    if (val.IsNull) { Console.WriteLine($"    {f.Name} ({f.Type?.Name}): null"); continue; }
                    var ttn = val.Type?.Name ?? "?";
                    Console.WriteLine($"    {f.Name} ({f.Type?.Name}) -> 0x{val.Address:X} [{ttn}]");
                    refFields.Add((f.Name, f.Type?.Name ?? "?", val.Address, ttn));
                }
                catch (Exception ex) { Console.WriteLine($"    {f.Name}: ERROR reading: {ex.Message}"); }
            }
        }
        Console.WriteLine($"\n  Total object-reference fields on OmlHeader: {refFields.Count}");

        // ---- Phase 3: Find sample model objects and check ownerESpace ----
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("PHASE 3: Check ownerESpace on sample model objects");
        Console.WriteLine(new string('=', 70));

        string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }
        bool InModel(string n) => n.StartsWith("ServiceStudio.Model.", StringComparison.Ordinal) || n.StartsWith("OutSystems.Model.", StringComparison.Ordinal);
        bool IsSkip(string n) => n.Contains('[') || n.Contains("Reference") || n.Contains("Abstract") || n.Contains("Descriptor") || n.Contains("Signature") || n.Contains("Enumerator") || n.Contains("Iterator") || n.Contains("+<>c");

        var samplesByType = new Dictionary<string, List<ClrObject>>();
        void AddSample(string category, ClrObject obj)
        {
            if (!samplesByType.ContainsKey(category)) samplesByType[category] = new List<ClrObject>();
            if (samplesByType[category].Count < 5) samplesByType[category].Add(obj);
        }

        foreach (var obj in g_heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (IsSkip(fn) || !InModel(fn)) continue;
            var leaf = Leaf(fn);

            if (leaf == "WebScreen" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
                AddSample("Screen", obj);
            else if (leaf == "WebBlock" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
                AddSample("WebBlock", obj);
            else if (leaf == "Entity" || leaf == "Structure" || leaf == "SystemStructure" || leaf == "StaticEntity")
                AddSample("Entity/Structure", obj);
            else if (leaf == "ServerAction" || leaf == "Action")
                AddSample("ServerAction", obj);
            else if (leaf == "ClientAction")
                AddSample("ClientAction", obj);
            else if (leaf == "SiteProperty")
                AddSample("SiteProperty", obj);
            else if (fn == "ServiceStudio.Model.NewRuntime.Theme" || fn == "ServiceStudio.Model.Theme")
                AddSample("Theme", obj);
        }

        Console.WriteLine($"\n  Sample counts collected:");
        foreach (var kv in samplesByType)
            Console.WriteLine($"    {kv.Key}: {kv.Value.Count}");

        // ---- Phase 4: For each sample, read ownerESpace and check for OmlHeader match ----
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("PHASE 4: ownerESpace -> OmlHeader matching");
        Console.WriteLine(new string('=', 70));

        int directHits = 0, totalChecked = 0;
        var nonDirectPaths = new List<string>();

        foreach (var kv in samplesByType)
        {
            Console.WriteLine($"\n  --- {kv.Key} ---");
            foreach (var sample in kv.Value)
            {
                totalChecked++;
                var sName = ReadStr(sample, "_name") ?? ReadStr(sample, "name") ?? "(unnamed)";
                Console.WriteLine($"\n  Sample: {sName} @ 0x{sample.Address:X}  [{Leaf(sample.Type?.Name ?? "?")}]");

                // Try ownerESpace
                var ownerESpace = ReadRef(sample, "ownerESpace");
                if (ownerESpace.IsNull)
                {
                    Console.WriteLine("    ownerESpace: null (not present on this type)");
                    // Try parent chain
                    var parent = ReadRef(sample, "parent");
                    if (parent.IsNull) parent = ReadRef(sample, "_parent");
                    if (!parent.IsNull)
                    {
                        Console.WriteLine($"    parent -> 0x{parent.Address:X} [{parent.Type?.Name}]");
                        // Walk up the parent chain looking for ownerESpace
                        var cur = parent;
                        for (int hop = 0; hop < 10 && !cur.IsNull; hop++)
                        {
                            var oe = ReadRef(cur, "ownerESpace");
                            if (!oe.IsNull)
                            {
                                Console.WriteLine($"      hop {hop}: found ownerESpace on [{cur.Type?.Name}] @ 0x{cur.Address:X}");
                                Console.WriteLine($"        ownerESpace -> 0x{oe.Address:X} [{oe.Type?.Name}]");
                                CheckMatch(oe, headerByAddr, ref directHits, nonDirectPaths, sName, hop + 1);
                                break;
                            }
                            var next = ReadRef(cur, "parent");
                            if (next.IsNull) next = ReadRef(cur, "_parent");
                            if (next.IsNull || next.Address == cur.Address) { Console.WriteLine($"      hop {hop}: no parent (chain ends at [{cur.Type?.Name}])"); break; }
                            cur = next;
                        }
                    }
                    continue;
                }

                Console.WriteLine($"    ownerESpace -> 0x{ownerESpace.Address:X} [{ownerESpace.Type?.Name}]");
                CheckMatch(ownerESpace, headerByAddr, ref directHits, nonDirectPaths, sName, 0);
            }
        }

        // ---- Phase 5: Summary ----
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("PHASE 5: Summary");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine($"\n  OmlHeader objects found: {headers.Count}");
        Console.WriteLine($"  Total model objects checked: {totalChecked}");
        Console.WriteLine($"  Direct ownerESpace -> OmlHeader matches: {directHits}");
        Console.WriteLine($"  Non-direct (via parent chain) matches: {nonDirectPaths.Count}");
        if (directHits > 0)
        {
            Console.WriteLine("\n  *** SUCCESS: ownerESpace directly points to OmlHeader! ***");
            Console.WriteLine("  >>> Module attribution IS possible via ownerESpace field. <<<");
        }
        else if (nonDirectPaths.Count > 0)
        {
            Console.WriteLine("\n  *** PARTIAL: ownerESpace found via parent chain (not direct) ***");
            Console.WriteLine("  >>> Module attribution possible via parent chain walk. <<<");
            Console.WriteLine("\n  Non-direct paths found:");
            foreach (var p in nonDirectPaths.Take(10)) Console.WriteLine("    " + p);
        }
        else
        {
            Console.WriteLine("\n  *** FAILURE: ownerESpace does not link to OmlHeader ***");
            Console.WriteLine("  >>> Need to investigate OmlHeader forward-pointers or other fields. <<<");
        }
    }

    static void CheckMatch(ClrObject ownerESpace, Dictionary<ulong, (ulong addr, string name, ClrObject obj)> headerByAddr,
        ref int directHits, List<string> nonDirectPaths, string sampleName, int hopDepth)
    {
        // Direct match?
        if (headerByAddr.ContainsKey(ownerESpace.Address))
        {
            var h = headerByAddr[ownerESpace.Address];
            directHits++;
            Console.WriteLine($"      *** DIRECT MATCH to OmlHeader \"{h.name}\" ***");
            return;
        }

        // Is ownerESpace itself an OmlHeader (different address)?
        var oeType = ownerESpace.Type?.Name ?? "";
        if (oeType.EndsWith(".OmlHeader", StringComparison.Ordinal))
        {
            var name = ExtractModuleName(ownerESpace);
            Console.WriteLine($"      ownerESpace IS an OmlHeader but not in our dict (name=\"{name}\")");
            directHits++;
            return;
        }

        // Not a direct match — dump ownerESpace's type and fields to understand the chain
        Console.WriteLine($"      ownerESpace type: {oeType}");
        Console.WriteLine("      ownerESpace fields (object references):");
        for (var t = ownerESpace.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (!f.IsObjectReference) continue;
                try
                {
                    var val = f.ReadObject(ownerESpace.Address, false);
                    if (val.IsNull) continue;
                    var vtn = val.Type?.Name ?? "?";
                    Console.WriteLine($"        {f.Name} ({f.Type?.Name}) -> 0x{val.Address:X} [{vtn}]");

                    // Check if this field points to an OmlHeader
                    if (vtn.EndsWith(".OmlHeader", StringComparison.Ordinal))
                    {
                        var hName = ExtractModuleName(val);
                        Console.WriteLine($"          *** FOUND OmlHeader via ownerESpace.{f.Name}: \"{hName}\" ***");
                        nonDirectPaths.Add($"Sample \"{sampleName}\" -> ownerESpace.{f.Name} -> OmlHeader \"{hName}\" (hop {hopDepth})");
                    }
                    // Also check if the value's address is in our header dict
                    if (headerByAddr.ContainsKey(val.Address))
                    {
                        var h = headerByAddr[val.Address];
                        Console.WriteLine($"          *** MATCH via ownerESpace.{f.Name} -> OmlHeader \"{h.name}\" ***");
                        nonDirectPaths.Add($"Sample \"{sampleName}\" -> ownerESpace.{f.Name} -> OmlHeader \"{h.name}\" (hop {hopDepth})");
                    }
                }
                catch { }
            }
        }

        // Also try reading ownerESpace's own ownerESpace or parent (2-hop)
        var oe2 = ReadRef(ownerESpace, "ownerESpace");
        if (!oe2.IsNull)
        {
            var oe2tn = oe2.Type?.Name ?? "?";
            Console.WriteLine($"      ownerESpace.ownerESpace -> 0x{oe2.Address:X} [{oe2tn}]");
            if (headerByAddr.ContainsKey(oe2.Address))
            {
                var h = headerByAddr[oe2.Address];
                Console.WriteLine($"        *** MATCH via ownerESpace.ownerESpace -> OmlHeader \"{h.name}\" ***");
                nonDirectPaths.Add($"Sample \"{sampleName}\" -> ownerESpace.ownerESpace -> OmlHeader \"{h.name}\" (hop {hopDepth})");
            }
            if (oe2tn.EndsWith(".OmlHeader", StringComparison.Ordinal))
            {
                var hName = ExtractModuleName(oe2);
                Console.WriteLine($"        *** FOUND OmlHeader via ownerESpace.ownerESpace: \"{hName}\" ***");
                nonDirectPaths.Add($"Sample \"{sampleName}\" -> ownerESpace.ownerESpace -> OmlHeader \"{hName}\" (hop {hopDepth})");
            }
        }
    }

    static string ExtractModuleName(ClrObject headerObj)
    {
        for (var t = headerObj.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String")
                { try { var s = f.ReadString(headerObj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && s.IndexOfAny(new[] { '|', '{', ';', '/', '+', '=' }) < 0) return s; } catch { } }
        return null;
    }

    static string ReadStr(ClrObject o, string want)
    {
        if (o.IsNull) return null;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
        return null;
    }

    static ClrObject ReadRef(ClrObject o, string want)
    {
        if (o.IsNull) return default;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Name == want)
                { try { var c = f.ReadObject(o.Address, false); if (!c.IsNull) return c; } catch { } }
        return default;
    }
}
