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
        string targetAction = args.Length > 1 ? args[1] : null;
        Console.WriteLine($"Scanning PID {pid}... target action: {targetAction ?? "(all)"}");

        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;

        // ============================================================
        // PART A: Deep dive into one DataSet (Aggregate) DataTable tree
        // ============================================================
        Console.WriteLine("\n========== PART A: DataSet DataTable operation tree ==========");
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (Simplify(t.Name ?? "") != "DataSet") continue;
            var nodeName = ReadStr(obj, "_name") ?? "";
            if (targetAction != null && !ParentActionMatches(obj, targetAction)) continue;
            Console.WriteLine($"\n>>> DataSet [{nodeName}] @ 0x{obj.Address:X}");
            var table = ReadRef(obj, "_table");
            if (table.IsNull) { Console.WriteLine("  (no _table)"); continue; }
            DumpTableTree(table, heap, 0);
            break; // only first matching
        }

        // ============================================================
        // PART B: AggregateAttribute structure (source columns)
        // ============================================================
        Console.WriteLine("\n========== PART B: AggregateAttribute samples ==========");
        int aaCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.AggregateAttribute") continue;
            Console.WriteLine($"\n--- AggregateAttribute @ 0x{obj.Address:X} ---");
            DumpAllFields(obj, heap, 2);
            // Dump the _source attribute reference if any
            var src = ReadRef(obj, "_source");
            if (!src.IsNull)
            {
                Console.WriteLine($"  _source [{Simplify(src.Type?.Name ?? "?")}] deep:");
                DumpAllFields(src, heap, 4);
            }
            aaCount++;
            if (aaCount >= 4) { Console.WriteLine("  (stopping after 4)"); break; }
        }

        // ============================================================
        // PART C: JoinCondition structure
        // ============================================================
        Console.WriteLine("\n========== PART C: JoinCondition samples ==========");
        int jcCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.JoinCondition") continue;
            Console.WriteLine($"\n--- JoinCondition @ 0x{obj.Address:X} ---");
            DumpAllFields(obj, heap, 2);
            jcCount++;
            if (jcCount >= 4) { Console.WriteLine("  (stopping after 4)"); break; }
        }

        // ============================================================
        // PART D: All DataSetOperations subtypes
        // ============================================================
        Console.WriteLine("\n========== PART D: DataSetOperations subtypes in heap ==========");
        var opTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (fn.StartsWith("ServiceStudio.Model.DataSetOperations+"))
            {
                var simple = fn.Split('+').Last();
                if (!opTypeCounts.ContainsKey(simple)) opTypeCounts[simple] = 0;
                opTypeCounts[simple]++;
            }
        }
        foreach (var kv in opTypeCounts.OrderBy(x => x.Key))
            Console.WriteLine($"  {kv.Key,-40} {kv.Value}");

        // ============================================================
        // PART E: Deep dive into each DataSetOperations subtype
        // ============================================================
        Console.WriteLine("\n========== PART E: One sample of each DataSetOperations subtype ==========");
        var seenOps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (!fn.StartsWith("ServiceStudio.Model.DataSetOperations+")) continue;
            var simple = fn.Split('+').Last();
            if (!seenOps.Add(simple)) continue;
            Console.WriteLine($"\n--- {simple} @ 0x{obj.Address:X} ---");
            DumpAllFields(obj, heap, 2);
        }

        // ============================================================
        // PART F: SQL sqlElements - find the SQL text
        // ============================================================
        Console.WriteLine("\n========== PART F: SQL sqlElements exploration ==========");
        int sqlCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.Nodes+AdvancedQuery") continue;
            var nodeName = ReadStr(obj, "_name") ?? "(unnamed)";
            Console.WriteLine($"\n>>> AdvancedQuery [{nodeName}] @ 0x{obj.Address:X}");
            var sql = ReadRef(obj, "_sql");
            if (sql.IsNull) { Console.WriteLine("  (no _sql)"); continue; }
            var sqlElements = ReadRef(sql, "sqlElements");
            Console.WriteLine($"  sqlElements [{Simplify(sqlElements.Type?.Name ?? "?")}] @ 0x{sqlElements.Address:X}");
            // sqlElements is IList<SQLExpressionElement>. Try to read as array or list.
            var elements = ReadList(sqlElements, heap);
            Console.WriteLine($"  elements count = {elements.Count}");
            // Show distinct element types
            var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in elements)
            {
                var et = Simplify(e.Type?.Name ?? "?");
                if (!typeCounts.ContainsKey(et)) typeCounts[et] = 0;
                typeCounts[et]++;
            }
            Console.WriteLine("  element type counts:");
            foreach (var kv in typeCounts.OrderBy(x => x.Key))
                Console.WriteLine($"    {kv.Key,-40} {kv.Value}");

            // Dump first 8 elements in detail
            int i = 0;
            foreach (var e in elements)
            {
                if (i >= 8) { Console.WriteLine("    ... (more)"); break; }
                Console.WriteLine($"    [{i}] [{Simplify(e.Type?.Name ?? "?")}] @ 0x{e.Address:X}:");
                DumpAllFields(e, heap, 6);
                i++;
            }
            sqlCount++;
            if (sqlCount >= 1) break;
        }

        // ============================================================
        // PART G: SQLExpressionElement subtypes in heap
        // ============================================================
        Console.WriteLine("\n========== PART G: Filter, DataSource, AttributeSort, CalculatedAttribute samples ==========");

        Console.WriteLine("\n--- Filter samples ---");
        int fCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.Filter") continue;
            Console.WriteLine($"\n  Filter @ 0x{obj.Address:X}:");
            DumpAllFields(obj, heap, 4);
            // Resolve the condition expression
            var cond = ReadRef(obj, "_condition");
            if (!cond.IsNull) Console.WriteLine($"  (condition will be resolved by ReadElementText)");
            fCount++;
            if (fCount >= 3) break;
        }

        Console.WriteLine("\n--- DataSource samples ---");
        int dsCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.DataSource") continue;
            Console.WriteLine($"\n  DataSource @ 0x{obj.Address:X}:");
            DumpAllFields(obj, heap, 4);
            dsCount++;
            if (dsCount >= 2) break;
        }

        Console.WriteLine("\n--- AttributeSort samples ---");
        int asCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.AttributeSort") continue;
            Console.WriteLine($"\n  AttributeSort @ 0x{obj.Address:X}:");
            DumpAllFields(obj, heap, 4);
            asCount++;
            if (asCount >= 2) break;
        }

        Console.WriteLine("\n--- CalculatedAttribute samples ---");
        int caCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.CalculatedAttribute") continue;
            Console.WriteLine($"\n  CalculatedAttribute @ 0x{obj.Address:X}:");
            DumpAllFields(obj, heap, 4);
            caCount++;
            if (caCount >= 2) break;
        }

        // ============================================================
        // PART H: SQL element reference resolution (EntityElement, ParameterElement, AttributeElement)
        // ============================================================
        Console.WriteLine("\n========== PART H: SQL element reference resolution ==========");
        int eeCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var simple = Simplify(t.Name ?? "");
            if (simple != "EntityElement" && simple != "ParameterElement" && simple != "AttributeElement") continue;
            Console.WriteLine($"\n  {simple} @ 0x{obj.Address:X}:");
            DumpAllFields(obj, heap, 4);
            // Try to resolve the reference
            var r = ReadRef(obj, "reference");
            if (!r.IsNull)
            {
                Console.WriteLine($"    reference [{Simplify(r.Type?.Name ?? "?")}] @ 0x{r.Address:X}:");
                DumpAllFields(r, heap, 6);
                // Try referedObject
                var refered = ReadRef(r, "referedObject");
                if (!refered.IsNull)
                {
                    var rName = ReadStr(refered, "_name") ?? ReadStr(refered, "name");
                    var rType = Simplify(refered.Type?.Name ?? "?");
                    Console.WriteLine($"    referedObject [{rType}] name={rName}");
                }
            }
            eeCount++;
            if (eeCount >= 6) break;
        }

        // ============================================================
        // PART I: Reconstruct SQL text from sqlElements (proof of concept)
        // ============================================================
        Console.WriteLine("\n========== PART I: SQL text reconstruction ==========");
        int sqlReconCount = 0;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.Nodes+AdvancedQuery") continue;
            var nodeName = ReadStr(obj, "_name") ?? "(unnamed)";
            var sql = ReadRef(obj, "_sql");
            if (sql.IsNull) continue;
            var sqlElements = ReadRef(sql, "sqlElements");
            var elements = ReadList(sqlElements, heap);
            var sb = new StringBuilder();
            foreach (var e in elements)
            {
                var eType = Simplify(e.Type?.Name ?? "?");
                if (eType == "TextElement" || eType == "UnboundElement")
                {
                    var text = ReadStr(e, "text");
                    sb.Append(text ?? "");
                }
                else if (eType == "EntityElement")
                {
                    var r = ReadRef(e, "reference");
                    var name = ReadRefObjName(r);
                    sb.Append("{" + (name ?? "?") + "}");
                }
                else if (eType == "ParameterElement")
                {
                    var r = ReadRef(e, "reference");
                    var name = ReadRefObjName(r);
                    sb.Append("@" + (name ?? "?"));
                }
                else if (eType == "AttributeElement")
                {
                    var r = ReadRef(e, "reference");
                    var name = ReadRefObjName(r);
                    sb.Append("{" + (name ?? "?") + "}");
                }
                else
                {
                    sb.Append($"[{eType}]");
                }
            }
            Console.WriteLine($"\n>>> AdvancedQuery [{nodeName}] reconstructed SQL ({elements.Count} elements):");
            Console.WriteLine(sb.ToString());
            sqlReconCount++;
            if (sqlReconCount >= 2) break;
        }
    }

    static void DumpTableTree(ClrObject table, ClrHeap heap, int depth)
    {
        var prefix = new string(' ', depth * 2);
        if (table.IsNull) { Console.WriteLine($"{prefix}(null table)"); return; }
        var tType = Simplify(table.Type?.Name ?? "?");
        var tName = ReadStr(table, "_name") ?? ReadStr(table, "name");
        Console.WriteLine($"{prefix}[{tType}]{(tName != null ? " name=" + tName : "")} @ 0x{table.Address:X}");

        // Dump non-system fields
        var skip = new HashSet<string>(StringComparer.Ordinal) {
            "parent","ownerESpace","hashAggregator","verifyCache","id","key","scope",
            "_collectionMetadata","_metadata","topLevelScopeEntriesCache",
            "isLoadingOrImporting","isImporting","wasDeleted","hashesBeingSerialized",
            "disableViewGenerationIncrease","lastModifiedByCommand","isSettingName",
            "nameClashPending","insideOnBeforeNameChange","insideGetMergeId",
            "isInsideSimpleQuery","isInsideDataSet","isInsideParentWithSkipValidation",
            "isInsideClientSide","referenceElementDepth","_isZombie","<IsToolbarObject>k__BackingField",
            "isNew","createdOnCommandGeneration","invalidatingHashes",
            "compilationInterfaceHashNeverInvalidated","viewGeneration",
            "viewGenerationOfLastReferersRefresh","childCollectionsViewGeneration",
            "scopeEntryChildrenGeneration","_lastModifiedBy","_lastModifiedDate",
            "_lastMergedDate","isReducingOperationTree","_typeValueIsValid",
            "shouldDeferTypeRefresh","skipImplicitParametersRefresh"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = table.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (seen.Contains(f.Name)) continue;
                seen.Add(f.Name);
                if (skip.Contains(f.Name)) continue;
                try
                {
                    if (f.IsObjectReference)
                    {
                        var val = f.ReadObject(table.Address, false);
                        if (val.IsNull) continue;
                        var valType = Simplify(val.Type?.Name ?? "?");
                        var valName = ReadStr(val, "_name") ?? ReadStr(val, "name");
                        // For collections, show count
                        if (valType.Contains("SSSequence") || valType.Contains("SSCollection") || valType.Contains("Sequence") || valType.Contains("Collection"))
                        {
                            var items = ReadColl(val, heap);
                            Console.WriteLine($"{prefix}  {f.Name} [{valType}] count={items.Count}");
                            int i = 0;
                            foreach (var item in items)
                            {
                                if (i >= 5) { Console.WriteLine($"{prefix}    ... (more)"); break; }
                                var iType = Simplify(item.Type?.Name ?? "?");
                                var iName = ReadStr(item, "_name") ?? ReadStr(item, "name");
                                Console.WriteLine($"{prefix}    [{i}] [{iType}]{(iName != null ? " name=" + Truncate(iName, 60) : "")} @ 0x{item.Address:X}");
                                i++;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"{prefix}  {f.Name} [{valType}]{(valName != null ? " name=" + Truncate(valName, 60) : "")} @ 0x{val.Address:X}");
                        }
                    }
                    else if (f.IsValueType && f.Type?.Name != "System.String")
                    {
                        try { var v = f.Read<int>(table.Address, false); Console.WriteLine($"{prefix}  {f.Name} = {v}"); } catch { }
                    }
                    else if (f.Type?.Name == "System.String")
                    {
                        try { var s = f.ReadString(table.Address, false); if (!string.IsNullOrEmpty(s)) Console.WriteLine($"{prefix}  {f.Name} = \"{Truncate(s, 80)}\""); } catch { }
                    }
                }
                catch { }
            }
        }

        // Recurse into _rootOperation and _masterSource if this is a DataTable
        if (tType == "DataTable")
        {
            var rootOp = ReadRef(table, "_rootOperation");
            if (!rootOp.IsNull) { Console.WriteLine($"{prefix}  >> _rootOperation tree:"); DumpTableTree(rootOp, heap, depth + 2); }
            var masterSrc = ReadRef(table, "_masterSource");
            if (!masterSrc.IsNull && masterSrc.Address != rootOp.Address) { Console.WriteLine($"{prefix}  >> _masterSource tree:"); DumpTableTree(masterSrc, heap, depth + 2); }
            var tableOps = ReadRef(table, "_tableOperations");
            if (!tableOps.IsNull)
            {
                var items = ReadColl(tableOps, heap);
                Console.WriteLine($"{prefix}  >> _tableOperations count={items.Count}");
                int i = 0;
                foreach (var item in items) { if (i >= 10) break; Console.WriteLine($"{prefix}     [{i}]:"); DumpTableTree(item, heap, depth + 3); i++; }
            }
        }
        // For CombineSources, recurse into _sources and _joinConditions
        if (tType == "CombineSources")
        {
            var sources = ReadRef(table, "_sources");
            if (!sources.IsNull)
            {
                var items = ReadColl(sources, heap);
                Console.WriteLine($"{prefix}  >> _sources count={items.Count}");
                int i = 0;
                foreach (var item in items) { if (i >= 10) break; Console.WriteLine($"{prefix}     [{i}]:"); DumpTableTree(item, heap, depth + 3); i++; }
            }
            var joins = ReadRef(table, "_joinConditions");
            if (!joins.IsNull)
            {
                var items = ReadColl(joins, heap);
                Console.WriteLine($"{prefix}  >> _joinConditions count={items.Count}");
                int i = 0;
                foreach (var item in items) { if (i >= 10) break; Console.WriteLine($"{prefix}     [{i}] [{Simplify(item.Type?.Name ?? "?")}] @ 0x{item.Address:X}:"); DumpAllFields(item, heap, depth * 2 + 8); i++; }
            }
            var ops = ReadRef(table, "_operations");
            if (!ops.IsNull)
            {
                var items = ReadColl(ops, heap);
                Console.WriteLine($"{prefix}  >> _operations count={items.Count}");
                int i = 0;
                foreach (var item in items) { if (i >= 10) break; Console.WriteLine($"{prefix}     [{i}]:"); DumpTableTree(item, heap, depth + 3); i++; }
            }
        }
        // For AddSource, look at _source / _dataSource / _entity
        if (tType == "AddSource")
        {
            foreach (var fname in new[] { "_source", "_dataSource", "_entity", "_referedObject", "_reference" })
            {
                var src = ReadRef(table, fname);
                if (!src.IsNull)
                {
                    var srcType = Simplify(src.Type?.Name ?? "?");
                    var srcName = ReadStr(src, "_name") ?? ReadStr(src, "name");
                    Console.WriteLine($"{prefix}  >> {fname} [{srcType}]{(srcName != null ? " name=" + Truncate(srcName, 60) : "")} @ 0x{src.Address:X}");
                    // Try to resolve the entity reference
                    var refered = ReadRef(src, "referedObject");
                    if (!refered.IsNull)
                    {
                        var rName = ReadStr(refered, "_name") ?? ReadStr(refered, "name");
                        var rType = Simplify(refered.Type?.Name ?? "?");
                        Console.WriteLine($"{prefix}     referedObject [{rType}] name={rName}");
                    }
                }
            }
        }
    }

    static List<ClrObject> ReadList(ClrObject list, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (list.IsNull) return result;
        // Try as array
        if (list.IsArray)
        {
            var arr = list.AsArray();
            for (int i = 0; i < arr.Length; i++)
            {
                try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { }
            }
            return result;
        }
        // Try _items array (List<T> pattern)
        var items = ReadRef(list, "_items");
        var size = ReadInt(list, "_size");
        if (!items.IsNull && items.IsArray && size > 0)
        {
            var arr = items.AsArray();
            for (int i = 0; i < Math.Min(size, arr.Length); i++)
            {
                try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { }
            }
            if (result.Count > 0) return result;
        }
        // Try array+size (C5 pattern)
        var arrObj = ReadRef(list, "array");
        var c5size = ReadInt(list, "size");
        if (!arrObj.IsNull && arrObj.IsArray && c5size > 0)
        {
            var arr = arrObj.AsArray();
            for (int i = 0; i < Math.Min(c5size, arr.Length); i++)
            {
                try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { }
            }
        }
        return result;
    }

    static List<ClrObject> ReadColl(ClrObject coll, ClrHeap heap) => ReadList(coll, heap);

    static bool ParentActionMatches(ClrObject obj, string targetAction)
    {
        var parent = ReadRef(obj, "parent");
        if (parent.IsNull) return false;
        var parentName = ReadStr(parent, "_name");
        return parentName != null && parentName.Equals(targetAction, StringComparison.OrdinalIgnoreCase);
    }

    static string ReadRefObjName(ClrObject refObj)
    {
        if (refObj.IsNull) return null;
        var refName = ReadStr(refObj, "refName");
        if (refName != null) return refName;
        var referedObj = ReadRef(refObj, "referedObject");
        if (!referedObj.IsNull) { var n = ReadStr(referedObj, "_name") ?? ReadStr(referedObj, "name"); if (!string.IsNullOrEmpty(n)) return n; }
        for (var t = refObj.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name != "System.String" && f.Type?.Name?.StartsWith("System.") != true)
                { try { var v = f.ReadObject(refObj.Address, false); if (!v.IsNull) { var n = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(n)) return n; } } catch { } }
        return null;
    }

    static string Simplify(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return "?";
        var last = fullName.Contains("+") ? fullName.Split('+').Last() : fullName.Contains(".") ? fullName.Split('.').Last() : fullName;
        return last;
    }

    static void DumpAllFields(ClrObject obj, ClrHeap heap, int indent = 0)
    {
        var prefix = new string(' ', indent);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = obj.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (seen.Contains(f.Name)) continue;
                seen.Add(f.Name);
                var ftype = f.Type?.Name ?? "null";
                try
                {
                    if (f.IsObjectReference)
                    {
                        var val = f.ReadObject(obj.Address, false);
                        if (!val.IsNull)
                        {
                            var valName = ReadStr(val, "_name") ?? ReadStr(val, "name");
                            var valType = Simplify(val.Type?.Name ?? "?");
                            Console.WriteLine($"{prefix}{f.Name} ({ftype}): 0x{val.Address:X} [{valType}]{(valName != null ? " name=" + Truncate(valName, 60) : "")}");
                        }
                    }
                    else if (f.IsValueType && f.Type?.Name != "System.String")
                    {
                        try { var val = f.Read<int>(obj.Address, false); Console.WriteLine($"{prefix}{f.Name} ({ftype}): {val}"); } catch { }
                    }
                    else if (f.Type?.Name == "System.String")
                    {
                        try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s)) Console.WriteLine($"{prefix}{f.Name} ({ftype}): \"{Truncate(s, 120)}\""); } catch { }
                    }
                }
                catch { }
            }
        }
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return s.Length <= max ? s : s.Substring(0, max) + "...(" + s.Length + " chars)";
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
}
