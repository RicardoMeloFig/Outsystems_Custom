// ModuleInfoExtractor v3 - link-traced flows + expression text extraction
// Usage: dotnet run -- [PID] [outputDir]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;

ClrHeap heap = null!;

// --- helpers ---
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
bool ReadBool(ClrObject o, string want)
{
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (!f.IsObjectReference && f.Name == want)
            { try { return f.Read<bool>(o.Address, false); } catch { } }
    return false;
}
string Leaf(string fn)
{
    if (string.IsNullOrEmpty(fn)) return "?";
    if (fn.Contains("+")) fn = fn.Split('+').Last();
    if (fn.Contains(".")) fn = fn.Split('.').Last();
    if (fn.StartsWith("BasicTypes\u002B")) fn = fn.Substring("BasicTypes\u002B".Length);
    return fn;
}
string ReadParamType(ClrObject param)
{
    var typeObj = ReadRef(param, "_type");
    if (typeObj.IsNull) return "Unknown";
    var typeName = ReadStr(typeObj, "_name");
    if (!string.IsNullOrEmpty(typeName)) return Leaf(typeName);
    return Leaf(typeObj.Type?.Name ?? "Unknown");
}
List<ClrObject> ReadCollection(ClrObject coll)
{
    var result = new List<ClrObject>();
    if (coll.IsNull) return result;
    var arrObj = ReadRef(coll, "array");
    var size = ReadInt(coll, "size");
    if (!arrObj.IsNull && arrObj.IsArray && size > 0)
    {
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        {
            try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { }
        }
    }
    return result;
}

// --- expression text extraction ---
// Follows ExpressionElementReference to find the model element's name
string ReadRefObjName(ClrObject refObj)
{
    if (refObj.IsNull) return null;
    // ExpressionElementReferenceByName has a refName field (e.g., "If", "List", "Current")
    var refName = ReadStr(refObj, "refName");
    if (refName != null) return refName;
    // ObjectElementReference has a referedObject field pointing to the model element
    var referedObj = ReadRef(refObj, "referedObject");
    if (!referedObj.IsNull)
    {
        var name = ReadStr(referedObj, "_name") ?? ReadStr(referedObj, "name");
        if (!string.IsNullOrEmpty(name)) return name;
    }
    // Generic fallback: try all object references
    for (var t = refObj.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name != "System.String" && f.Type?.Name?.StartsWith("System.") != true)
            {
                try { var val = f.ReadObject(refObj.Address, false); if (!val.IsNull) { var name = ReadStr(val, "_name") ?? ReadStr(val, "name"); if (!string.IsNullOrEmpty(name)) return name; } } catch { }
            }
    return null;
}

string ReadElementText(ClrObject elem, int depth = 0)
{
    if (elem.IsNull || depth > 6) return "?";
    var elemType = Leaf(elem.Type?.Name ?? "?");

    switch (elemType)
    {
        case "TextLiteral":
            return "\"" + (ReadStr(elem, "value") ?? "") + "\"";
        case "IntegerLiteral":
            return ReadStr(elem, "value") ?? "?";
        case "Identifier":
        {
            var r = ReadRef(elem, "reference");
            if (!r.IsNull) { var n = ReadRefObjName(r); if (n != null) return n; }
            break;
        }
        case "CompoundIdentifier":
        {
            var parts = new List<string>();
            var r = ReadRef(elem, "reference");
            if (!r.IsNull) { var n = ReadRefObjName(r); if (n != null) parts.Add(n); }
            // Follow rest chain (each rest can be Identifier or CompoundIdentifier with own reference + rest)
            var rest = ReadRef(elem, "rest");
            int hops = 0;
            while (!rest.IsNull && hops < 8)
            {
                hops++;
                var restRef = ReadRef(rest, "reference");
                if (!restRef.IsNull) { var n = ReadRefObjName(restRef); if (n != null) parts.Add(n); }
                var nextRest = ReadRef(rest, "rest");
                if (nextRest.IsNull || nextRest.Address == rest.Address) break;
                rest = nextRest;
            }
            if (parts.Count > 0) return string.Join(".", parts);
            break;
        }
        case "BinaryOperation":
        {
            var leftSide = ReadRef(elem, "leftSide");
            var rightSide = ReadRef(elem, "rightSide");
            var left = ReadElementText(leftSide, depth + 1);
            var right = ReadElementText(rightSide, depth + 1);
            // The operator is stored as a BinaryOperator enum field
            var opVal = ReadInt(elem, "operator");
            var op = opVal switch
            {
                0 => "+",    // Add (confirmed: Int + 1)
                1 => "-",
                2 => "*",
                3 => "/",
                4 => "mod",
                5 => "=",
                6 => "<>",
                7 => "<",
                8 => "<=",
                9 => ">",
                10 => ">=",  // GreaterThanOrEqual (confirmed: Int >= 1 in If)
                11 => "and",
                12 => "or",
                13 => "&",
                _ => $"op{opVal}"
            };
            return $"{left} {op} {right}";
        }
        case "CallFunction":
        {
            var r = ReadRef(elem, "reference");
            var funcName = "";
            if (!r.IsNull) funcName = ReadRefObjName(r) ?? "";
            var args = ReadRef(elem, "arguments");
            var argTexts = new List<string>();
            if (!args.IsNull && args.IsArray)
            {
                var arr = args.AsArray();
                for (int i = 0; i < arr.Length; i++)
                {
                    try
                    {
                        var ptr = arr.GetValue<IntPtr>(i);
                        if (ptr == IntPtr.Zero) continue;
                        var argElem = heap.GetObject((ulong)ptr.ToInt64());
                        if (argElem.IsNull) continue;
                        var inner = ReadRef(argElem, "argumentExpressionElement");
                        if (!inner.IsNull) argTexts.Add(ReadElementText(inner, depth + 1));
                    }
                    catch { }
                }
            }
            return $"{funcName}({string.Join(", ", argTexts)})";
        }
    }
    // Generic fallback: try ALL string fields
    for (var t = elem.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String")
            { try { var s = f.ReadString(elem.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 500) return s; } catch { } }
    // Try referenced objects
    for (var t = elem.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name != "System.String" && f.Type?.Name?.StartsWith("System.") != true)
            { try { var v = f.ReadObject(elem.Address, false); if (!v.IsNull) { var n = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(n)) return n; } } catch { } }
    return elemType;
}

string ReadExpressionText(ClrObject parsedExpr)
{
    if (parsedExpr.IsNull) return "?";
    var elem = ReadRef(parsedExpr, "expressionElement");
    if (elem.IsNull) return "?";
    return ReadElementText(elem);
}

// --- readers ---
ActionInfo ReadActionInfo(ClrObject obj, string kind)
{
    var info = new ActionInfo
    {
        name = ReadStr(obj, "_name") ?? "", kind = kind, description = ReadStr(obj, "_description") ?? "",
        isPublic = ReadBool(obj, "_public"), addr = obj.Address,
        inputs = new List<ParamInfo>(), outputs = new List<ParamInfo>(),
        nodes = new List<NodeInfo>(), actionLinks = new List<(ulong, string)>()
    };
    foreach (var p in ReadCollection(ReadRef(obj, "_inputParameters")))
        info.inputs.Add(new ParamInfo { name = ReadStr(p, "_name") ?? "", type = ReadParamType(p), isMandatory = ReadBool(p, "_isMandatory") });
    foreach (var p in ReadCollection(ReadRef(obj, "_outputParameters")))
        info.outputs.Add(new ParamInfo { name = ReadStr(p, "_name") ?? "", type = ReadParamType(p) });
    return info;
}

NodeInfo ReadNodeInfo(ClrObject obj)
{
    var info = new NodeInfo
    {
        addr = obj.Address, type = Leaf(obj.Type?.Name ?? "?"),
        name = ReadStr(obj, "_name") ?? ReadStr(obj, "_customName") ?? "",
        actionRef = "", details = "", assignments = new List<string>(),
        outgoing = new List<(ulong, string)>()
    };

    if (info.type == "ExecuteAction")
    {
        var action = ReadRef(obj, "_action");
        if (!action.IsNull) info.actionRef = ReadStr(action, "_name") ?? "";
    }

    if (info.type == "Assign")
    {
        foreach (var a in ReadCollection(ReadRef(obj, "_assignments")))
        {
            var varText = ReadExpressionText(ReadRef(a, "_variable"));
            var valText = ReadExpressionText(ReadRef(a, "_value"));
            info.assignments.Add($"{varText} = {valText}");
        }
    }

    if (info.type == "Comment")
    {
        var text = ReadStr(obj, "_text");
        if (!string.IsNullOrEmpty(text)) info.details = $"\"{text}\"";
    }

    // Collect extra string fields (skip noise)
    var skipStr = new HashSet<string> { "_name", "_customName", "name", "_text", "_description", "_createdBy", "_lastModifiedBy" };
    var extra = new List<string>();
    for (var t = obj.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
        {
            if (!f.IsObjectReference || f.Type?.Name != "System.String") continue;
            if (skipStr.Contains(f.Name)) continue;
            try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 300 && !s.Contains("@")) extra.Add($"{f.Name}=\"{s}\""); } catch { }
        }
    if (extra.Count > 0)
        info.details = string.IsNullOrEmpty(info.details) ? string.Join(", ", extra) : info.details + " | " + string.Join(", ", extra);

    // Collect key object references
    var skipRefs = new HashSet<string> { "parent", "ownerESpace", "hashAggregator", "verifyCache", "id", "key",
        "_metadata", "_links", "saveCaches", "match", "forcedMatch", "replacement", "objectsInPathCache",
        "scope", "topLevelScopeEntriesCache", "oldVisibleNodes", "visibleNodesCache", "visibleNodesCacheLockObj",
        "_folder", "_image", "_textResources", "ownerCompilationUnit", "_collectionMetadata",
        "_nodesShownInESpaceTree", "_nodesNotShownInESpaceTree", "elementKinds", "_localVariables",
        "_assignments", "_action", "_arguments", "_variable", "_value", "expressionElement", "expressionElements" };
    var refs = new List<string>();
    for (var t = obj.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
        {
            if (!f.IsObjectReference || f.Type?.Name == "System.String") continue;
            if (f.Type?.Name?.StartsWith("System.") == true || skipRefs.Contains(f.Name)) continue;
            if (f.Name.StartsWith("_collection") || f.Name.StartsWith("omi#")) continue;
            try { var v = f.ReadObject(obj.Address, false); if (!v.IsNull) { var n = ReadStr(v, "_name") ?? ""; if (!string.IsNullOrEmpty(n)) refs.Add($"{f.Name}={n}"); } } catch { }
        }
    if (refs.Count > 0)
        info.details = string.IsNullOrEmpty(info.details) ? string.Join(", ", refs) : info.details + " | " + string.Join(", ", refs);
    return info;
}

string ExtractModuleName(ClrObject headerObj)
{
    for (var t = headerObj.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String")
            { try { var s = f.ReadString(headerObj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && !s.Contains('|') && !s.Contains('{') && !s.Contains(';') && s.IndexOfAny(new[] { '/', '+', '=' }) < 0) return s; } catch { } }
    return null;
}

string FormatParams(List<ParamInfo> ps) => ps.Count == 0 ? "(none)" : string.Join(", ", ps.Select(p => $"{p.name} ({p.type}){(p.isMandatory ? " [required]" : "")}"));

// --- flow tracing ---
List<string> TraceFlow(ActionInfo action, Dictionary<ulong, NodeInfo> nodeDict, Dictionary<ulong, List<(ulong, string)>> allLinks)
{
    var visited = new HashSet<ulong>();
    var lines = new List<string>();
    int counter = 1;

    void Trace(ulong addr, int indent, string label)
    {
        if (visited.Contains(addr))
        {
            if (nodeDict.TryGetValue(addr, out var vn))
                lines.Add($"{new string(' ', indent)}    (loops back to [{vn.type}])");
            return;
        }
        visited.Add(addr);
        if (!nodeDict.TryGetValue(addr, out var node)) return;

        var prefix = new string(' ', indent);
        var line = $"{prefix}{counter,2}. ";
        if (!string.IsNullOrEmpty(label)) line += $"{label}: ";
        line += $"[{node.type}]";
        if (!string.IsNullOrEmpty(node.name)) line += $" {node.name}";
        if (!string.IsNullOrEmpty(node.actionRef)) line += $" -> {node.actionRef}";
        lines.Add(line);
        counter++;

        foreach (var a in node.assignments)
            lines.Add($"{prefix}      {a}");
        if (!string.IsNullOrEmpty(node.details) && node.type == "Comment")
            lines.Add($"{prefix}      {node.details}");

        if (!allLinks.TryGetValue(addr, out var links) || links.Count == 0) return;

        var seq = links.Where(l => l.Item2 == "Sequence").ToList();
        var cycle = links.Where(l => l.Item2 == "Cycle").ToList();
        var cond = links.Where(l => l.Item2 == "Condition").ToList();
        var other = links.Where(l => l.Item2 == "Otherwise").ToList();
        var truel = links.Where(l => l.Item2 == "True").ToList();
        var falsel = links.Where(l => l.Item2 == "False").ToList();
        var comment = links.Where(l => l.Item2 == "Comment").ToList();

        foreach (var item in comment)
            if (nodeDict.TryGetValue(item.Item1, out var cn) && !string.IsNullOrEmpty(cn.details))
                lines.Add($"{prefix}      Comment: {cn.details}");

        foreach (var item in cycle)
        { lines.Add($"{prefix}      Cycle (loop body):"); Trace(item.Item1, indent + 8, ""); }

        foreach (var item in cond)
        {
            if (nodeDict.TryGetValue(item.Item1, out var tn))
                lines.Add($"{prefix}      Condition -> [{tn.type}]{(string.IsNullOrEmpty(tn.name) ? "" : " " + tn.name)}:");
            Trace(item.Item1, indent + 8, "");
        }

        foreach (var item in other)
        { lines.Add($"{prefix}      Otherwise:"); Trace(item.Item1, indent + 8, ""); }

        foreach (var item in truel)
        { lines.Add($"{prefix}      True:"); Trace(item.Item1, indent + 8, ""); }

        foreach (var item in falsel)
        { lines.Add($"{prefix}      False:"); Trace(item.Item1, indent + 8, ""); }

        foreach (var item in seq)
            Trace(item.Item1, indent, "");
    }

    // Find Start node
    var start = action.nodes.FirstOrDefault(n => n.type == "Start");
    if (start != null)
        Trace(start.addr, 4, "");
    else
        lines.Add("    (no Start node found)");

    // Error handlers (unvisited)
    var errorHandlers = action.nodes.Where(n => n.type == "ErrorHandler" && !visited.Contains(n.addr)).ToList();
    if (errorHandlers.Count > 0)
    {
        lines.Add("");
        lines.Add("    Exception Handler:");
        foreach (var eh in errorHandlers)
            Trace(eh.addr, 8, "");
    }

    // Other unvisited nodes
    var unvisited = action.nodes.Where(n => !visited.Contains(n.addr)).ToList();
    if (unvisited.Count > 0)
    {
        lines.Add("");
        lines.Add("    Other nodes (unvisited):");
        foreach (var n in unvisited)
        {
            var line = $"      [{n.type}]";
            if (!string.IsNullOrEmpty(n.name)) line += $" {n.name}";
            lines.Add(line);
        }
    }
    return lines;
}

// --- output ---
string GenerateOutput(string moduleName, int pid,
    List<ActionInfo> serverActions, List<ActionInfo> serviceActions, List<ActionInfo> clientActions,
    List<EntityInfo> entities, List<StructureInfo> structures, List<SitePropertyInfo> siteProps,
    Dictionary<ulong, List<(ulong, string)>> allLinks)
{
    var sb = new StringBuilder();
    sb.AppendLine("================================================================");
    sb.AppendLine($"  MODULE: {moduleName}");
    sb.AppendLine("================================================================");
    sb.AppendLine($"  Generated:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"  SS PID:       {pid}");
    sb.AppendLine();
    sb.AppendLine("  SUMMARY");
    sb.AppendLine($"    Server Actions:  {serverActions.Count}");
    sb.AppendLine($"    Service Actions: {serviceActions.Count}");
    sb.AppendLine($"    Client Actions:  {clientActions.Count}");
    sb.AppendLine($"    Entities:        {entities.Count}");
    sb.AppendLine($"    Structures:      {structures.Count}");
    sb.AppendLine($"    Site Properties: {siteProps.Count}");

    void FormatAction(ActionInfo a)
    {
        sb.AppendLine();
        sb.AppendLine($"  --- {a.name} ---");
        sb.AppendLine($"    Public:      {(a.isPublic ? "Yes" : "No")}");
        sb.AppendLine($"    Description: {(string.IsNullOrEmpty(a.description) ? "(none)" : a.description)}");
        sb.AppendLine($"    Inputs:      {FormatParams(a.inputs)}");
        sb.AppendLine($"    Outputs:     {FormatParams(a.outputs)}");
        if (a.nodes != null && a.nodes.Count > 0)
        {
            var nodeDict = a.nodes.ToDictionary(n => n.addr, n => n);
            var flowLines = TraceFlow(a, nodeDict, allLinks);
            sb.AppendLine($"    Flow ({a.nodes.Count} nodes, traced by connections):");
            foreach (var line in flowLines)
                sb.AppendLine(line);
        }
        else
            sb.AppendLine("    Flow: (no nodes found)");
    }

    if (serverActions.Count > 0)
    { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SERVER ACTIONS ({serverActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in serverActions.OrderBy(x => x.name)) FormatAction(a); }
    if (serviceActions.Count > 0)
    { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SERVICE ACTIONS ({serviceActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in serviceActions.OrderBy(x => x.name)) FormatAction(a); }
    if (clientActions.Count > 0)
    { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  CLIENT ACTIONS ({clientActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in clientActions.OrderBy(x => x.name)) FormatAction(a); }
    if (entities.Count > 0)
    {
        sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  ENTITIES ({entities.Count})"); sb.AppendLine("================================================================");
        foreach (var e in entities.OrderBy(x => x.name))
        {
            sb.AppendLine(); sb.AppendLine($"  --- {e.name} [{(e.isPublic ? "public" : "private")}] ---");
            if (e.attributes.Count == 0) sb.AppendLine("    (no attributes)");
            else { sb.AppendLine("    Attributes:"); foreach (var a in e.attributes) { var tags = new List<string>(); if (a.isKey) tags.Add("KEY"); if (a.isMandatory) tags.Add("required"); sb.AppendLine($"      - {a.name} : {a.type}{(tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "")}"); } }
        }
    }
    if (structures.Count > 0)
    {
        sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  STRUCTURES ({structures.Count})"); sb.AppendLine("================================================================");
        foreach (var s in structures.OrderBy(x => x.name))
        {
            sb.AppendLine(); sb.AppendLine($"  --- {s.name}{(s.isAnonymous ? " [anonymous]" : $" [{(s.isPublic ? "public" : "private")}]")} ---");
            if (s.attributes.Count == 0) sb.AppendLine("    (no attributes)");
            else { sb.AppendLine("    Attributes:"); foreach (var a in s.attributes) sb.AppendLine($"      - {a.name} : {a.type}"); }
        }
    }
    if (siteProps.Count > 0)
    {
        sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SITE PROPERTIES ({siteProps.Count})"); sb.AppendLine("================================================================");
        foreach (var p in siteProps.OrderBy(x => x.name))
        {
            sb.AppendLine(); var tags = new List<string>(); if (p.isReadOnly) tags.Add("read-only");
            sb.AppendLine($"  {p.name} : {p.type}{(tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "")}{(!string.IsNullOrEmpty(p.description) ? $" - {p.description}" : "")}");
        }
    }
    sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine("  END OF REPORT"); sb.AppendLine("================================================================");
    return sb.ToString();
}

// --- main ---
int pid;
string outputDir = null;
if (args.Length > 0 && int.TryParse(args[0], out pid)) { }
else
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) { Console.Error.WriteLine("Service Studio is not running."); return 1; }
    pid = procs[0].Id;
    Console.WriteLine($"Auto-detected Service Studio PID {pid}" + (procs.Length > 1 ? $"  ({procs.Length} instances; using first)" : ""));
}
if (args.Length > 1) outputDir = args[1];

Console.WriteLine("Attaching (readonly)...");
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
heap = runtime.Heap;
Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}");
Console.WriteLine("Scanning heap...");

string moduleName = null;
var actions = new Dictionary<ulong, ActionInfo>();
var nodesByParent = new Dictionary<ulong, List<(ClrObject obj, NodeInfo info)>>();
var linksBySource = new Dictionary<ulong, List<(ulong targetAddr, string linkType)>>();
var entities = new List<EntityInfo>();
var structures = new List<StructureInfo>();
var siteProps = new List<SitePropertyInfo>();

bool IsSkip(string fn) => fn.Contains('[') || fn.Contains("Reference") || fn.Contains("+<>c") ||
    fn.Contains("Descriptor") || fn.Contains("Enumerator") || fn.Contains("Iterator") || fn.Contains("Abstract");

foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (IsSkip(fn)) continue;

    if (fn.EndsWith(".OmlHeader") && moduleName == null) { moduleName = ExtractModuleName(obj); continue; }
    if (fn.EndsWith("Flows+UserAction")) { var i = ReadActionInfo(obj, "ServerAction"); if (!string.IsNullOrEmpty(i.name)) actions[obj.Address] = i; continue; }
    if (fn == "ServiceStudio.Model.Flows+ServiceAPIMethod") { var i = ReadActionInfo(obj, "ServiceAction"); if (!string.IsNullOrEmpty(i.name)) actions[obj.Address] = i; continue; }
    if (fn.EndsWith("NRFlows+ClientActionFlow") || fn.EndsWith("NRFlows+AbstractSystemClientAction")) { var i = ReadActionInfo(obj, "ClientAction"); if (!string.IsNullOrEmpty(i.name)) actions[obj.Address] = i; continue; }

    if (fn.StartsWith("ServiceStudio.Model.Nodes+") || fn.Contains(".Nodes+"))
    {
        var parent = ReadRef(obj, "parent");
        if (!parent.IsNull)
        {
            var info = ReadNodeInfo(obj);
            if (!nodesByParent.ContainsKey(parent.Address)) nodesByParent[parent.Address] = new List<(ClrObject, NodeInfo)>();
            nodesByParent[parent.Address].Add((obj, info));
        }
        continue;
    }
    if (fn.StartsWith("ServiceStudio.Model.Links+") || fn.Contains(".Links+"))
    {
        var src = ReadRef(obj, "parent");
        var tgt = ReadRef(obj, "_targetNode");
        if (!src.IsNull && !tgt.IsNull)
        {
            var lt = Leaf(fn);
            if (!linksBySource.ContainsKey(src.Address)) linksBySource[src.Address] = new List<(ulong, string)>();
            linksBySource[src.Address].Add((tgt.Address, lt));
        }
        continue;
    }
    if (fn == "ServiceStudio.Model.Entity")
    { var i = new EntityInfo { name = ReadStr(obj, "_name") ?? "", isPublic = ReadBool(obj, "_public"), attributes = new List<EntityAttrInfo>() }; if (!string.IsNullOrEmpty(i.name)) { foreach (var a in ReadCollection(ReadRef(obj, "_attributes"))) i.attributes.Add(new EntityAttrInfo { name = ReadStr(a, "_name") ?? "", type = ReadParamType(a), isMandatory = ReadBool(a, "_isMandatory"), isKey = ReadBool(a, "_isKey") }); entities.Add(i); } continue; }
    if (fn == "ServiceStudio.Model.Structure" || fn == "ServiceStudio.Model.AnonymousStructure")
    { var i = new StructureInfo { name = ReadStr(obj, "_name") ?? "", isPublic = ReadBool(obj, "_public"), isAnonymous = fn == "ServiceStudio.Model.AnonymousStructure", attributes = new List<EntityAttrInfo>() }; if (!string.IsNullOrEmpty(i.name)) { foreach (var a in ReadCollection(ReadRef(obj, "_attributes"))) i.attributes.Add(new EntityAttrInfo { name = ReadStr(a, "_name") ?? "", type = ReadParamType(a), isMandatory = ReadBool(a, "_isMandatory") }); structures.Add(i); } continue; }
    if (fn == "ServiceStudio.Model.Variables+SiteProperty")
    { var i = new SitePropertyInfo { name = ReadStr(obj, "_name") ?? "", type = ReadParamType(obj), isReadOnly = ReadBool(obj, "_isReadOnlySiteProperty"), description = ReadStr(obj, "_description") ?? "" }; if (!string.IsNullOrEmpty(i.name)) siteProps.Add(i); continue; }
}

Console.WriteLine($"Found {actions.Count} actions, {nodesByParent.Values.Sum(l => l.Count)} nodes, {linksBySource.Values.Sum(l => l.Count)} links, {entities.Count} entities, {structures.Count} structures, {siteProps.Count} site properties");
Console.WriteLine("Matching nodes to actions...");
foreach (var kv in nodesByParent)
{
    if (!actions.ContainsKey(kv.Key)) continue;
    actions[kv.Key].nodes = kv.Value.Select(n => n.info).ToList();
}

var serverActions = actions.Values.Where(a => a.kind == "ServerAction").GroupBy(a => a.name).Select(g => g.First()).ToList();
var serviceActions = actions.Values.Where(a => a.kind == "ServiceAction").GroupBy(a => a.name).Select(g => g.First()).ToList();
var clientActions = actions.Values.Where(a => a.kind == "ClientAction").GroupBy(a => a.name).Select(g => g.First()).ToList();
entities = entities.GroupBy(e => e.name).Select(g => g.First()).ToList();
structures = structures.GroupBy(s => s.name).Select(g => g.First()).ToList();
siteProps = siteProps.GroupBy(p => p.name).Select(g => g.First()).ToList();

Console.WriteLine("Generating report...");
var output = GenerateOutput(moduleName ?? "(unknown)", pid, serverActions, serviceActions, clientActions, entities, structures, siteProps, linksBySource);

if (string.IsNullOrEmpty(outputDir)) outputDir = Path.Combine(Directory.GetCurrentDirectory(), "module-info");
Directory.CreateDirectory(outputDir);
string safeName = (moduleName ?? "Unknown").Replace(" ", "_");
string filePath = Path.Combine(outputDir, $"{safeName}_info.txt");
File.WriteAllText(filePath, output);
Console.WriteLine($"\nOutput written to: {filePath}");
Console.WriteLine("\n" + output);
return 0;

// --- data classes (must be after top-level statements) ---
class ParamInfo { public string name, type; public bool isMandatory; }
class ActionInfo { public string name, kind, description; public bool isPublic; public ulong addr; public List<ParamInfo> inputs, outputs; public List<NodeInfo> nodes; public List<(ulong target, string linkType)> actionLinks; }
class NodeInfo { public string type, name, actionRef, details; public ulong addr; public List<string> assignments; public List<(ulong target, string linkType)> outgoing; }
class EntityAttrInfo { public string name, type; public bool isMandatory, isKey; }
class EntityInfo { public string name; public bool isPublic; public List<EntityAttrInfo> attributes; }
class StructureInfo { public string name; public bool isPublic, isAnonymous; public List<EntityAttrInfo> attributes; }
class SitePropertyInfo { public string name, type, description; public bool isReadOnly; }
