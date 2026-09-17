using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Runtime;

class Program
{
    static void Main()
    {
        while (Console.ReadLine() is string line && line != null)
        {
            JsonNode msg;
            try { msg = JsonNode.Parse(line); } catch { continue; }
            var method = msg["method"]?.ToString();
            var id = msg["id"];
            if (method == "initialize")
                Respond(id, new JsonObject {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "outsystems-logic", ["version"] = "1.0" }
                });
            else if (method == "notifications/initialized") { }
            else if (method == "tools/list") Respond(id, new JsonObject { ["tools"] = ToolsList() });
            else if (method == "tools/call")
            {
                var name = msg["params"]?["name"]?.ToString();
                var arguments = msg["params"]?["arguments"] as JsonObject;
                string result;
                try { result = Dispatch(name, arguments); }
                catch (Exception ex) { result = "ERROR: " + ex.Message + "\n" + ex.StackTrace; }
                Respond(id, new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result } } });
            }
        }
    }

    static void Respond(JsonNode id, JsonNode result)
    {
        var resp = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
        if (id != null) resp["id"] = JsonNode.Parse(id.ToJsonString());
        Console.WriteLine(resp.ToJsonString());
        Console.Out.Flush();
    }

    static JsonArray ToolsList()
    {
        var tools = new JsonArray();
        tools.Add(new JsonObject {
            ["name"] = "list_actions",
            ["description"] = "List Server Actions and Service Actions from the open module",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "list_client_actions",
            ["description"] = "List Client Actions from the open module",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "get_action_detail",
            ["description"] = "Get detailed info for a specific action",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Action name" },
                    ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" }
                },
                ["required"] = new JsonArray { "name" }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "list_entities",
            ["description"] = "List Entities from the open module with their attributes",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "list_structures",
            ["description"] = "List Named Structures and Anonymous Structures from the open module",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "list_site_properties",
            ["description"] = "List Site Properties from the open module",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        tools.Add(new JsonObject {
            ["name"] = "get_module_report",
            ["description"] = "Generate a full module report with link-traced action flows, expression text, entities, structures, and site properties",
            ["inputSchema"] = new JsonObject {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID" } }
            }
        });
        return tools;
    }

    static string Dispatch(string name, JsonObject arguments)
    {
        int? pid = arguments != null && arguments.ContainsKey("pid") && arguments["pid"] != null
            ? (int?)arguments["pid"].GetValue<int>() : null;
        return name switch
        {
            "list_actions" => ListActions(pid),
            "list_client_actions" => ListClientActions(pid),
            "get_action_detail" => GetActionDetail(arguments != null && arguments.ContainsKey("name") ? arguments["name"]?.ToString() ?? "" : "", pid),
            "list_entities" => ListEntities(pid),
            "list_structures" => ListStructures(pid),
            "list_site_properties" => ListSiteProperties(pid),
            "get_module_report" => GetModuleReport(pid),
            _ => "Unknown tool: " + name
        };
    }

    static string ListActions(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();
        var byModule = new Dictionary<string, List<(ActionInfo info, string kind)>>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            string kind = null;
            if (fn.EndsWith("Flows+UserAction")) kind = "ServerAction";
            else if (fn == "ServiceStudio.Model.Flows+ServiceAPIMethod") kind = "ServiceAction";
            if (kind == null) continue;
            var info = ReadActionInfo(obj, kind, heap);
            if (string.IsNullOrEmpty(info.name)) continue;
            var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
            if (!byModule.ContainsKey(mod)) byModule[mod] = new List<(ActionInfo, string)>();
            byModule[mod].Add((info, kind));
        }
        if (byModule.Count == 0) return "No Server/Service Actions found.";
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var serverActions = kv.Value.Where(x => x.kind == "ServerAction").GroupBy(x => x.info.name).Select(g => g.First().info).ToList();
            var serviceActions = kv.Value.Where(x => x.kind == "ServiceAction").GroupBy(x => x.info.name).Select(g => g.First().info).ToList();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {kv.Key}");
            sb.AppendLine("================================================================");
            if (serverActions.Count > 0) { sb.AppendLine($"SERVER ACTIONS ({serverActions.Count}):"); foreach (var a in serverActions.OrderBy(x => x.name)) sb.AppendLine(FormatAction(a)); sb.AppendLine(); }
            if (serviceActions.Count > 0) { sb.AppendLine($"SERVICE ACTIONS ({serviceActions.Count}):"); foreach (var a in serviceActions.OrderBy(x => x.name)) sb.AppendLine(FormatAction(a)); sb.AppendLine(); }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    static string ListClientActions(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();
        var byModule = new Dictionary<string, List<ActionInfo>>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (fn.EndsWith("NRFlows+ClientActionFlow") || fn.EndsWith("NRFlows+AbstractSystemClientAction"))
            {
                var info = ReadActionInfo(obj, "ClientAction", heap);
                if (string.IsNullOrEmpty(info.name)) continue;
                var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
                if (!byModule.ContainsKey(mod)) byModule[mod] = new List<ActionInfo>();
                byModule[mod].Add(info);
            }
        }
        if (byModule.Count == 0) return "No Client Actions found.";
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var actions = kv.Value.GroupBy(x => x.name).Select(g => g.First()).ToList();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {kv.Key}");
            sb.AppendLine("================================================================");
            sb.AppendLine($"CLIENT ACTIONS ({actions.Count}):");
            foreach (var a in actions.OrderBy(x => x.name))
                sb.AppendLine(FormatAction(a));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    static string GetActionDetail(string actionName, int? pidArg)
    {
        if (string.IsNullOrEmpty(actionName)) return "Action name required.";
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        ClrObject targetAction = default;
        string foundName = null, foundKind = null;
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            bool isTarget = fn.EndsWith("Flows+UserAction") || fn == "ServiceStudio.Model.Flows+ServiceAPIMethod" || fn.EndsWith("NRFlows+ClientActionFlow");
            if (!isTarget) continue;
            var nm = ReadStr(obj, "_name");
            if (nm == null || !nm.Contains(actionName, StringComparison.OrdinalIgnoreCase)) continue;
            targetAction = obj;
            foundName = nm;
            foundKind = fn.EndsWith("Flows+UserAction") ? "ServerAction" : (fn == "ServiceStudio.Model.Flows+ServiceAPIMethod") ? "ServiceAction" : "ClientAction";
            break;
        }
        if (targetAction.IsNull) return "Action '" + actionName + "' not found.";
        var info = ReadActionInfo(targetAction, foundKind, heap);
        info.name = foundName; info.kind = foundKind;
        var sb = new StringBuilder();
        sb.AppendLine("=== " + info.name + " (" + info.kind + ") ===");
        sb.AppendLine("Public: " + info.isPublic);
        if (!string.IsNullOrEmpty(info.description)) sb.AppendLine("Description: " + info.description);
        sb.AppendLine();
        sb.AppendLine("INPUT PARAMETERS (" + info.inputs.Count + "):");
        if (info.inputs.Count == 0) sb.AppendLine("  (none)");
        else foreach (var p in info.inputs) sb.AppendLine("  - " + p.name + " : " + p.type + (p.isMandatory ? " [required]" : ""));
        sb.AppendLine();
        sb.AppendLine("OUTPUT PARAMETERS (" + info.outputs.Count + "):");
        if (info.outputs.Count == 0) sb.AppendLine("  (none)");
        else foreach (var p in info.outputs) sb.AppendLine("  - " + p.name + " : " + p.type);
        return sb.ToString().TrimEnd();
    }

    static string ListEntities(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();
        var byModule = new Dictionary<string, List<EntityInfo>>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.Entity")
            {
                var name = ReadStr(obj, "_name");
                if (string.IsNullOrEmpty(name)) continue;
                var info = new EntityInfo { name = name };
                info.isPublic = ReadBool(obj, "_public");
                info.attributes = ReadEntityAttributes(obj, heap);
                var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
                if (!byModule.ContainsKey(mod)) byModule[mod] = new List<EntityInfo>();
                byModule[mod].Add(info);
            }
        }
        if (byModule.Count == 0) return "No Entities found.";
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var entities = kv.Value.GroupBy(e => e.name).Select(g => g.First()).ToList();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {kv.Key}");
            sb.AppendLine("================================================================");
            sb.AppendLine($"ENTITIES ({entities.Count}):");
            foreach (var e in entities.OrderBy(x => x.name))
            {
                sb.AppendLine();
                sb.AppendLine("=== " + e.name + " [" + (e.isPublic ? "public" : "private") + "] ===");
                if (e.attributes.Count == 0)
                    sb.AppendLine("  (no attributes)");
                else
                    foreach (var a in e.attributes)
                        sb.AppendLine("  - " + a.name + " : " + a.type + (a.isKey ? " [KEY]" : "") + (a.isMandatory ? " [required]" : ""));
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    static string ListStructures(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();
        var byModule = new Dictionary<string, List<StructureInfo>>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var tn = t.Name ?? "";
            var name = ReadStr(obj, "_name");
            if (string.IsNullOrEmpty(name)) continue;
            if (tn == "ServiceStudio.Model.Structure")
            {
                var info = new StructureInfo { name = name, isAnonymous = false };
                info.isPublic = ReadBool(obj, "_public");
                info.attributes = ReadStructureAttributes(obj, heap);
                var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
                if (!byModule.ContainsKey(mod)) byModule[mod] = new List<StructureInfo>();
                byModule[mod].Add(info);
            }
            else if (tn == "ServiceStudio.Model.AnonymousStructure")
            {
                var info = new StructureInfo { name = name, isAnonymous = true };
                info.attributes = ReadAnonStructureAttributes(obj, heap);
                var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
                if (!byModule.ContainsKey(mod)) byModule[mod] = new List<StructureInfo>();
                byModule[mod].Add(info);
            }
        }
        if (byModule.Count == 0) return "No Structures found.";
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var namedStructures = kv.Value.Where(s => !s.isAnonymous).GroupBy(s => s.name).Select(g => g.First()).ToList();
            var anonStructures = kv.Value.Where(s => s.isAnonymous).GroupBy(s => s.name).Select(g => g.First()).ToList();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {kv.Key}");
            sb.AppendLine("================================================================");
            if (namedStructures.Count > 0)
            {
                sb.AppendLine($"NAMED STRUCTURES ({namedStructures.Count}):");
                foreach (var s in namedStructures.OrderBy(x => x.name))
                {
                    sb.AppendLine();
                    sb.AppendLine("=== " + s.name + " [" + (s.isPublic ? "public" : "private") + "] ===");
                    if (s.attributes.Count == 0) sb.AppendLine("  (no attributes)");
                    else foreach (var a in s.attributes) sb.AppendLine("  - " + a.name + " : " + a.type);
                }
                sb.AppendLine();
            }
            if (anonStructures.Count > 0)
            {
                sb.AppendLine($"ANONYMOUS STRUCTURES ({anonStructures.Count}):");
                foreach (var s in anonStructures.OrderBy(x => x.name))
                {
                    sb.AppendLine();
                    sb.AppendLine("=== " + s.name + " ===");
                    if (s.attributes.Count == 0) sb.AppendLine("  (no attributes)");
                    else foreach (var a in s.attributes) sb.AppendLine("  - " + a.name + " : " + a.type);
                }
                sb.AppendLine();
            }
        }
        return sb.ToString().TrimEnd();
    }

    static string ListSiteProperties(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();
        var byModule = new Dictionary<string, List<SitePropertyInfo>>(StringComparer.Ordinal);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name == "ServiceStudio.Model.Variables+SiteProperty")
            {
                var name = ReadStr(obj, "_name");
                if (string.IsNullOrEmpty(name)) continue;
                var info = new SitePropertyInfo();
                info.name = name;
                info.type = ReadParamType(obj, heap);
                info.isReadOnly = ReadBool(obj, "_isReadOnlySiteProperty");
                info.description = ReadStr(obj, "_description") ?? "";
                var mod = ResolveModule(obj, espaceMap) ?? "(unknown)";
                if (!byModule.ContainsKey(mod)) byModule[mod] = new List<SitePropertyInfo>();
                byModule[mod].Add(info);
            }
        }
        if (byModule.Count == 0) return "No Site Properties found.";
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var props = kv.Value.GroupBy(p => p.name).Select(g => g.First()).ToList();
            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {kv.Key}");
            sb.AppendLine("================================================================");
            sb.AppendLine($"SITE PROPERTIES ({props.Count}):");
            foreach (var p in props.OrderBy(x => x.name))
                sb.AppendLine("  " + p.name + " : " + p.type + (p.isReadOnly ? " [read-only]" : "") + (!string.IsNullOrEmpty(p.description) ? " - " + p.description : ""));
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    struct ParamInfo { public string name, type; public bool isMandatory, isOutput, isKey; }
    struct ActionInfo { public string name, kind, description; public bool isPublic; public List<ParamInfo> inputs, outputs; }
    struct EntityAttrInfo { public string name, type; public bool isMandatory, isKey; }
    struct EntityInfo { public string name; public bool isPublic; public List<EntityAttrInfo> attributes; }
    struct SitePropertyInfo { public string name, type, description; public bool isReadOnly; }
    struct StructureInfo { public string name; public bool isPublic, isAnonymous; public List<EntityAttrInfo> attributes; }

    static ActionInfo ReadActionInfo(ClrObject obj, string kind, ClrHeap heap)
    {
        var info = new ActionInfo { name = ReadStr(obj, "_name") ?? "", kind = kind, inputs = new List<ParamInfo>(), outputs = new List<ParamInfo>() };
        info.description = ReadStr(obj, "_description") ?? "";
        info.isPublic = ReadBool(obj, "_public");
        info.inputs = ReadParams(obj, "_inputParameters", heap);
        info.outputs = ReadParams(obj, "_outputParameters", heap);
        return info;
    }

    static List<ParamInfo> ReadParams(ClrObject action, string fieldName, ClrHeap heap)
    {
        var result = new List<ParamInfo>();
        var seq = ReadRef(action, fieldName);
        if (seq.IsNull) return result;
        var size = ReadInt(seq, "size");
        var arrObj = ReadRef(seq, "array");
        if (arrObj.IsNull || !arrObj.IsArray) return result;
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        {
            try
            {
                var ptr = arr.GetValue<IntPtr>(i);
                var addr = ptr.ToInt64();
                if (addr == 0) continue;
                var elem = heap.GetObject((ulong)addr);
                if (elem.IsNull) continue;
                var p = new ParamInfo();
                p.name = ReadStr(elem, "_name") ?? "";
                p.isMandatory = ReadBool(elem, "_isMandatory");
                p.type = ReadParamType(elem, heap);
                result.Add(p);
            }
            catch { }
        }
        return result;
    }

    static List<EntityAttrInfo> ReadEntityAttributes(ClrObject entity, ClrHeap heap)
    {
        var result = new List<EntityAttrInfo>();
        var seq = ReadRef(entity, "_attributes");
        if (seq.IsNull) return result;
        var size = ReadInt(seq, "size");
        var arrObj = ReadRef(seq, "array");
        if (arrObj.IsNull || !arrObj.IsArray) return result;
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        {
            try
            {
                var ptr = arr.GetValue<IntPtr>(i);
                var addr = ptr.ToInt64();
                if (addr == 0) continue;
                var elem = heap.GetObject((ulong)addr);
                if (elem.IsNull) continue;
                var a = new EntityAttrInfo();
                a.name = ReadStr(elem, "_name") ?? "";
                a.isMandatory = ReadBool(elem, "_isMandatory");
                a.isKey = ReadBool(elem, "_isKey");
                a.type = ReadParamType(elem, heap);
                result.Add(a);
            }
            catch { }
        }
        return result;
    }

    static List<EntityAttrInfo> ReadStructureAttributes(ClrObject structure, ClrHeap heap)
    {
        var result = new List<EntityAttrInfo>();
        var seq = ReadRef(structure, "_attributes");
        if (seq.IsNull) return result;
        var size = ReadInt(seq, "size");
        var arrObj = ReadRef(seq, "array");
        if (arrObj.IsNull || !arrObj.IsArray) return result;
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        {
            try
            {
                var ptr = arr.GetValue<IntPtr>(i);
                var addr = ptr.ToInt64();
                if (addr == 0) continue;
                var elem = heap.GetObject((ulong)addr);
                if (elem.IsNull) continue;
                var a = new EntityAttrInfo();
                a.name = ReadStr(elem, "_name") ?? "";
                a.isMandatory = ReadBool(elem, "_isMandatory");
                a.type = ReadParamType(elem, heap);
                result.Add(a);
            }
            catch { }
        }
        return result;
    }

    static List<EntityAttrInfo> ReadAnonStructureAttributes(ClrObject anonStruct, ClrHeap heap)
    {
        var result = new List<EntityAttrInfo>();
        var coll = ReadRef(anonStruct, "_attributes");
        if (coll.IsNull) return result;
        var arrObj = ReadRef(coll, "array");
        var size = ReadInt(coll, "size");
        if (arrObj.IsNull || !arrObj.IsArray) return result;
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        {
            try
            {
                var ptr = arr.GetValue<IntPtr>(i);
                var addr = ptr.ToInt64();
                if (addr == 0) continue;
                var elem = heap.GetObject((ulong)addr);
                if (elem.IsNull) continue;
                var a = new EntityAttrInfo();
                a.name = ReadStr(elem, "_name") ?? "";
                a.type = ReadParamType(elem, heap);
                result.Add(a);
            }
            catch { }
        }
        return result;
    }

    static string ReadParamType(ClrObject param, ClrHeap heap)
    {
        var typeObj = ReadRef(param, "_type");
        if (typeObj.IsNull) return "Unknown";
        var typeName = ReadStr(typeObj, "_name");
        if (!string.IsNullOrEmpty(typeName)) return SimplifyTypeName(typeName);
        var fullName = typeObj.Type?.Name ?? "Unknown";
        return SimplifyTypeName(fullName);
    }

    static string SimplifyTypeName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return "Unknown";
        var last = fullName.Contains("+") ? fullName.Split('+').Last() : fullName.Contains(".") ? fullName.Split('.').Last() : fullName;
        if (last.StartsWith("BasicTypes\u002B")) last = last.Substring("BasicTypes\u002B".Length);
        return last;
    }

    static string FormatAction(ActionInfo a)
    {
        var inNames = a.inputs.Count > 0 ? string.Join(",", a.inputs.Select(p => p.name)) : "(none)";
        var outNames = a.outputs.Count > 0 ? string.Join(",", a.outputs.Select(p => p.name)) : "(none)";
        var pub = a.isPublic ? " [public]" : "";
        return $"  {a.name}{pub} | in: {inNames} | out: {outNames}";
    }

    static int GetPid(int? pidArg)
    {
        if (pidArg.HasValue) return pidArg.Value;
        var procs = Process.GetProcessesByName("ServiceStudio");
        if (procs.Length == 0) throw new Exception("Service Studio is not running.");
        return procs[0].Id;
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

    static bool ReadBool(ClrObject o, string want)
    {
        return ReadInt(o, want) != 0;
    }

    // ---- ESpace-based module attribution ----
    // Builds a map of ESpace object address -> module name. Each module open in
    // Service Studio has one ESpace object; every model object (entity, action,
    // structure, site property) has an ownerESpace field pointing to its ESpace.
    static Dictionary<ulong, string> BuildEspaceMap(ClrHeap heap)
    {
        var map = new Dictionary<ulong, string>();
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.ESpace") continue;
            var name = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
            if (!string.IsNullOrEmpty(name))
                map[obj.Address] = name;
        }
        return map;
    }

    static string ResolveModule(ClrObject obj, Dictionary<ulong, string> espaceMap)
    {
        var oe = ReadRef(obj, "ownerESpace");
        if (!oe.IsNull && espaceMap.TryGetValue(oe.Address, out var name))
            return name;
        return null;
    }

    // --- get_module_report support ---
    class NodeInfo { public string type, name, actionRef, details, condition; public ulong addr; public List<string> assignments = new(); public List<(ulong target, string linkType)> outgoing = new();
        // Aggregate (DataSet) details
        public List<string> aggSources = new();
        public List<string> aggJoins = new();
        public List<string> aggFilters = new();
        public List<string> aggSorts = new();
        public List<string> aggCalculated = new();
        public List<string> aggGroupBy = new();
        // SQL (AdvancedQuery) details
        public string sqlText;
        public List<string> sqlParameters = new();
        public List<string> sqlOutput = new();
    }

    static List<ClrObject> ReadColl(ClrObject coll, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (coll.IsNull) return result;
        // Pattern 1: C5/SSCollection (array + size) - used by most OutSystems model collections
        var arrObj = ReadRef(coll, "array");
        var size = ReadInt(coll, "size");
        if (!arrObj.IsNull && arrObj.IsArray && size > 0)
        {
            var arr = arrObj.AsArray();
            for (int i = 0; i < Math.Min(size, arr.Length); i++)
            { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { } }
        }
        if (result.Count > 0) return result;
        // Pattern 2: System.Collections.Generic.List<T> (_items + _size) - used by sqlElements etc.
        var itemsObj = ReadRef(coll, "_items");
        var itemsSize = ReadInt(coll, "_size");
        if (!itemsObj.IsNull && itemsObj.IsArray && itemsSize > 0)
        {
            var arr = itemsObj.AsArray();
            for (int i = 0; i < Math.Min(itemsSize, arr.Length); i++)
            { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { } }
        }
        return result;
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

    static string ReadElementText(ClrObject elem, ClrHeap heap, int depth = 0)
    {
        if (elem.IsNull || depth > 6) return "?";
        var elemType = SimplifyTypeName(elem.Type?.Name ?? "?");
        switch (elemType)
        {
            // ParsedExpression is a wrapper: the real expression tree is in its
            // "expressionElement" field. Unwrap and recurse before anything else.
            case "ParsedExpression":
            { var inner = ReadRef(elem, "expressionElement"); if (!inner.IsNull) return ReadElementText(inner, heap, depth + 1); break; }
            case "TextLiteral": return "\"" + (ReadStr(elem, "value") ?? "") + "\"";
            case "IntegerLiteral": return ReadStr(elem, "value") ?? "?";
            case "BooleanLiteral": return ReadStr(elem, "value") ?? "?";
            case "DecimalLiteral": return ReadStr(elem, "value") ?? "?";
            case "Identifier":
            { var r = ReadRef(elem, "reference"); if (!r.IsNull) { var n = ReadRefObjName(r); if (n != null) return n; } break; }
            case "CompoundIdentifier":
            {
                var parts = new List<string>();
                var r = ReadRef(elem, "reference"); if (!r.IsNull) { var n = ReadRefObjName(r); if (n != null) parts.Add(n); }
                var rest = ReadRef(elem, "rest"); int hops = 0;
                while (!rest.IsNull && hops < 8) { hops++; var restRef = ReadRef(rest, "reference"); if (!restRef.IsNull) { var n = ReadRefObjName(restRef); if (n != null) parts.Add(n); } var nextRest = ReadRef(rest, "rest"); if (nextRest.IsNull || nextRest.Address == rest.Address) break; rest = nextRest; }
                if (parts.Count > 0) return string.Join(".", parts);
                break;
            }
            case "BinaryOperation":
            {
                var left = ReadElementText(ReadRef(elem, "leftSide"), heap, depth + 1);
                var right = ReadElementText(ReadRef(elem, "rightSide"), heap, depth + 1);
                var opVal = ReadInt(elem, "operator");
                var op = opVal switch { 0 => "+", 1 => "-", 2 => "*", 3 => "/", 4 => "mod", 5 => "=", 6 => "<>", 7 => "<", 8 => "<=", 9 => ">", 10 => ">=", 11 => "and", 12 => "or", 13 => "&", _ => $"op{opVal}" };
                return $"{left} {op} {right}";
            }
            case "UnaryOperation":
            {
                var operand = ReadElementText(ReadRef(elem, "operand"), heap, depth + 1);
                var opVal = ReadInt(elem, "operator");
                var op = opVal switch { 0 => "+", 1 => "-", 2 => "not", _ => $"unary{opVal}" };
                return op == "not" ? $"not {operand}" : $"{op}{operand}";
            }
            case "CallFunction":
            {
                var r = ReadRef(elem, "reference"); var funcName = ""; if (!r.IsNull) funcName = ReadRefObjName(r) ?? "";
                var args = ReadRef(elem, "arguments"); var argTexts = new List<string>();
                if (!args.IsNull && args.IsArray) { var arr = args.AsArray(); for (int i = 0; i < arr.Length; i++) { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var argElem = heap.GetObject((ulong)ptr.ToInt64()); if (argElem.IsNull) continue; var inner = ReadRef(argElem, "argumentExpressionElement"); if (!inner.IsNull) argTexts.Add(ReadElementText(inner, heap, depth + 1)); } catch { } } }
                return $"{funcName}({string.Join(", ", argTexts)})";
            }
            case "NullLiteral": return "Null";
            case "DefaultLiteral": return "Default";
        }
        // Fallback 1: try string fields (but skip metadata/system fields)
        var skipStr = new HashSet<string> { "_name", "_customName", "name", "_text", "_description", "_createdBy", "_lastModifiedBy" };
        for (var t = elem.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && !skipStr.Contains(f.Name))
                { try { var s = f.ReadString(elem.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 500) return s; } catch { } }
        // Fallback 2: try object reference fields EXCLUDING ownerESpace/parent/etc
        // (those return the module name, which is misleading). Only return names
        // from fields that look like they hold a real model element.
        var skipObj = new HashSet<string> { "ownerESpace", "parent", "hashAggregator", "verifyCache", "key", "id", "_collectionMetadata", "scope", "references", "expressionElements", "expressionElement", "_links", "_metadata", "compilationInterfaceHashNeverInvalidated" };
        for (var t = elem.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name != "System.String" && f.Type?.Name?.StartsWith("System.") != true && !skipObj.Contains(f.Name))
                { try { var v = f.ReadObject(elem.Address, false); if (!v.IsNull) { var n = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(n)) return n; } } catch { } }
        return elemType;
    }

    static NodeInfo ReadNodeInfo(ClrObject obj, ClrHeap heap)
    {
        var info = new NodeInfo { addr = obj.Address, type = SimplifyTypeName(obj.Type?.Name ?? "?"), name = ReadStr(obj, "_name") ?? ReadStr(obj, "_customName") ?? "" };
        if (info.type == "ExecuteAction" || info.type == "ExecuteClientAction") { var a = ReadRef(obj, "_action"); if (!a.IsNull) info.actionRef = ReadStr(a, "_name") ?? ""; else { var ca = ReadRef(obj, "_clientAction"); if (!ca.IsNull) info.actionRef = ReadStr(ca, "_name") ?? ""; } }
        if (info.type == "Assign") { foreach (var a in ReadColl(ReadRef(obj, "_assignments"), heap)) { info.assignments.Add($"{ReadElementText(ReadRef(a, "_variable"), heap)} = {ReadElementText(ReadRef(a, "_value"), heap)}"); } }
        if (info.type == "If") { var cond = ReadRef(obj, "_condition"); if (!cond.IsNull) { var condText = ReadElementText(cond, heap); if (!string.IsNullOrEmpty(condText) && condText != "?") info.condition = condText; } }
        if (info.type == "Switch")
        {
            // Switch has _conditions collection of SwitchCondition objects; each has an _expressionElement
            var conds = ReadColl(ReadRef(obj, "_conditions"), heap);
            if (conds.Count > 0)
            {
                var parts = new List<string>();
                foreach (var c in conds) { var expr = ReadRef(c, "_expressionElement"); if (!expr.IsNull) { var t = ReadElementText(expr, heap); if (!string.IsNullOrEmpty(t) && t != "?") parts.Add(t); } }
                if (parts.Count > 0) info.condition = string.Join(" | ", parts);
            }
        }
        if (info.type == "Comment") { var text = ReadStr(obj, "_text"); if (!string.IsNullOrEmpty(text)) info.details = $"\"{text}\""; }
        // Aggregate (DataSet) detail extraction: source entity, joins, filters, sorts, group by, calculated attributes
        if (info.type == "DataSet") ReadAggregateDetails(obj, heap, info);
        // SQL (AdvancedQuery) detail extraction: SQL text, parameters, output structure
        if (info.type == "AdvancedQuery") ReadSqlQueryDetails(obj, heap, info);
        var skipStr = new HashSet<string> { "_name", "_customName", "name", "_text", "_description", "_createdBy", "_lastModifiedBy" };
        var extra = new List<string>();
        for (var t = obj.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields) { if (!f.IsObjectReference || f.Type?.Name != "System.String" || skipStr.Contains(f.Name)) continue; try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 300 && !s.Contains("@")) extra.Add($"{f.Name}=\"{s}\""); } catch { } }
        if (extra.Count > 0) info.details = string.IsNullOrEmpty(info.details) ? string.Join(", ", extra) : info.details + " | " + string.Join(", ", extra);
        return info;
    }

    // Extracts aggregate internals from a DataSet node's _table (DataTable).
    // DataTable._rootOperation (CombineSources or GroupBy) holds sources, joins,
    // filters, sorts, calculated attributes, and group-by attributes.
    static void ReadAggregateDetails(ClrObject dataSetNode, ClrHeap heap, NodeInfo info)
    {
        var table = ReadRef(dataSetNode, "_table");
        if (table.IsNull) return;

        // Source entity: from _masterSource (AddSource) -> _source (ReferenceEntity)
        var masterSource = ReadRef(table, "_masterSource");
        if (!masterSource.IsNull)
        {
            var src = ReadRef(masterSource, "_source");
            if (!src.IsNull)
            {
                var srcName = ReadStr(src, "_name") ?? ReadStr(src, "name");
                if (!string.IsNullOrEmpty(srcName)) info.aggSources.Add(srcName);
            }
        }

        // Root operation: CombineSources (with sources/joins/filters/sorts) or GroupBy
        var rootOp = ReadRef(table, "_rootOperation");
        if (!rootOp.IsNull) ReadAggregateOperation(rootOp, heap, info);

        // Also check _tableOperations for GroupBy or other operations not in root
        var tableOps = ReadColl(ReadRef(table, "_tableOperations"), heap);
        foreach (var op in tableOps)
        {
            var opType = SimplifyTypeName(op.Type?.Name ?? "?");
            if (opType == "GroupBy") ReadGroupByDetails(op, heap, info);
        }
    }

    static void ReadAggregateOperation(ClrObject op, ClrHeap heap, NodeInfo info)
    {
        var opType = SimplifyTypeName(op.Type?.Name ?? "?");

        if (opType == "CombineSources")
        {
            // Additional sources (joined entities)
            var sources = ReadColl(ReadRef(op, "_sources"), heap);
            foreach (var s in sources)
            {
                var sName = ReadStr(s, "_name");
                if (!string.IsNullOrEmpty(sName) && !info.aggSources.Contains(sName))
                    info.aggSources.Add(sName);
            }

            // Joins
            var joins = ReadColl(ReadRef(op, "_joins"), heap);
            foreach (var j in joins)
            {
                var leftSrc = ReadRef(j, "_leftSource");
                var rightSrc = ReadRef(j, "_rightSource");
                var leftName = ReadStr(leftSrc, "_name") ?? "?";
                var rightName = ReadStr(rightSrc, "_name") ?? "?";
                var joinType = ReadInt(j, "_joinType");
                var joinLabel = joinType switch { 0 => "Left", 1 => "Right", 2 => "Full", 3 => "Inner", _ => $"type{joinType}" };
                var cond = ReadRef(j, "_condition");
                var condText = !cond.IsNull ? ReadElementText(cond, heap) : "";
                info.aggJoins.Add($"[{joinLabel}] {leftName} with {rightName}{(!string.IsNullOrEmpty(condText) && condText != "?" ? $" ON {condText}" : "")}");
            }

            // Filters (WHERE conditions)
            var filters = ReadColl(ReadRef(op, "_filters"), heap);
            foreach (var f in filters)
            {
                var cond = ReadRef(f, "_condition");
                if (!cond.IsNull) { var t = ReadElementText(cond, heap); if (!string.IsNullOrEmpty(t) && t != "?") info.aggFilters.Add(t); }
            }

            // Sorts
            var sorts = ReadColl(ReadRef(op, "_sorts"), heap);
            foreach (var s in sorts)
            {
                var sortVal = ReadInt(s, "_sort");
                var sortLabel = sortVal switch { 0 => "Asc", 1 => "Desc", _ => $"sort{sortVal}" };
                var attr = ReadRef(s, "_originalAttribute");
                var attrText = !attr.IsNull ? ReadElementText(attr, heap) : "?";
                info.aggSorts.Add($"{attrText} [{sortLabel}]");
            }

            // Calculated attributes
            var calcAttrs = ReadColl(ReadRef(op, "_calculatedAttributes"), heap);
            foreach (var ca in calcAttrs)
            {
                var caName = ReadStr(ca, "_name") ?? "?";
                var caExpr = ReadRef(ca, "_expression");
                var caText = !caExpr.IsNull ? ReadElementText(caExpr, heap) : "";
                var caType = ReadParamType(ca, heap);
                info.aggCalculated.Add($"{caName} ({caType}){(!string.IsNullOrEmpty(caText) && caText != "?" ? $" = {caText}" : "")}");
            }
        }
        else if (opType == "GroupBy")
        {
            ReadGroupByDetails(op, heap, info);
        }
    }

    static void ReadGroupByDetails(ClrObject groupBy, ClrHeap heap, NodeInfo info)
    {
        // Group-by attributes
        var groupAttrs = ReadColl(ReadRef(groupBy, "_groupByAttributes"), heap);
        foreach (var ga in groupAttrs)
        {
            var gaName = ReadStr(ga, "_name") ?? "?";
            var gaOrig = ReadRef(ga, "_originalAttribute");
            var gaText = !gaOrig.IsNull ? ReadElementText(gaOrig, heap) : "";
            info.aggGroupBy.Add($"{gaName}{(!string.IsNullOrEmpty(gaText) && gaText != "?" ? $" = {gaText}" : "")}");
        }
        // Aggregate attributes (Count, Sum, etc.)
        var aggAttrs = ReadColl(ReadRef(groupBy, "_aggregateAttributes"), heap);
        foreach (var aa in aggAttrs)
        {
            var aaName = ReadStr(aa, "_name") ?? "?";
            var aggType = ReadInt(aa, "_aggregationType");
            var aggLabel = aggType switch { 0 => "None", 1 => "Count", 2 => "Sum", 3 => "Min", 4 => "Max", 5 => "Avg", _ => $"agg{aggType}" };
            var aaOrig = ReadRef(aa, "_originalAttribute");
            var aaText = !aaOrig.IsNull ? ReadElementText(aaOrig, heap) : "";
            var aaType = ReadParamType(aa, heap);
            info.aggCalculated.Add($"{aaName} ({aaType}) [{aggLabel}]{(!string.IsNullOrEmpty(aaText) && aaText != "?" ? $" of {aaText}" : "")}");
        }
    }

    // Extracts SQL Advanced Query internals: SQL text (reconstructed from the parsed
    // sqlElements tree), input parameters, and output structure.
    static void ReadSqlQueryDetails(ClrObject sqlNode, ClrHeap heap, NodeInfo info)
    {
        // Reconstruct SQL text from _sql.sqlElements
        var sql = ReadRef(sqlNode, "_sql");
        if (!sql.IsNull)
        {
            var sqlElements = ReadRef(sql, "sqlElements");
            if (!sqlElements.IsNull)
            {
                var elements = ReadColl(sqlElements, heap);
                var sb = new StringBuilder();
                foreach (var e in elements)
                {
                    var eType = SimplifyTypeName(e.Type?.Name ?? "?");
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
                    else sb.Append($"[{eType}]");
                }
                info.sqlText = sb.ToString().TrimEnd();
            }
        }

        // Input parameters: _queryParameters (sequence of AdvancedQueryParameter)
        var queryParams = ReadColl(ReadRef(sqlNode, "_queryParameters"), heap);
        foreach (var p in queryParams)
        {
            var pName = ReadStr(p, "_name") ?? "?";
            var pType = ReadParamType(p, heap);
            var expandInline = ReadBool(p, "_expandInline");
            info.sqlParameters.Add($"{pName} ({pType}){(expandInline ? " [expand inline]" : "")}");
        }

        // Output structure: _outputStructure (sequence of AdvancedQueryRecord)
        var outputStruct = ReadColl(ReadRef(sqlNode, "_outputStructure"), heap);
        foreach (var r in outputStruct)
        {
            var rName = ReadStr(r, "_name") ?? "?";
            var record = ReadRef(r, "_record");
            var recordName = "";
            if (!record.IsNull) recordName = ReadStr(record, "_name") ?? ReadStr(record, "name") ?? SimplifyTypeName(record.Type?.Name ?? "?");
            info.sqlOutput.Add($"{rName}{(!string.IsNullOrEmpty(recordName) ? $" -> {recordName}" : "")}");
        }
    }

    static string ExtractModuleName(ClrObject headerObj)
    {
        for (var t = headerObj.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String")
                { try { var s = f.ReadString(headerObj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && !s.Contains('|') && !s.Contains('{') && !s.Contains(';') && s.IndexOfAny(new[] { '/', '+', '=' }) < 0) return s; } catch { } }
        return null;
    }

    static string FormatParams(List<ParamInfo> ps) => ps.Count == 0 ? "(none)" : string.Join(", ", ps.Select(p => $"{p.name} ({p.type}){(p.isMandatory ? " [required]" : "")}"));

    // Per-module data container for GetModuleReport
    class ModuleData
    {
        public string Name;
        public Dictionary<ulong, (string name, string kind, bool isPublic, string desc, List<ParamInfo> inputs, List<ParamInfo> outputs, List<NodeInfo> nodes)> actions = new();
        public Dictionary<ulong, List<(ulong target, string linkType)>> linksBySource = new();
        public Dictionary<ulong, List<NodeInfo>> nodesByParent = new();
        public List<EntityInfo> entities = new();
        public List<StructureInfo> structures = new();
        public List<SitePropertyInfo> siteProps = new();
    }

    static string GetModuleReport(int? pidArg)
    {
        int pid = GetPid(pidArg);
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var espaceMap = BuildEspaceMap(heap);
        var sb = new StringBuilder();

        var byModule = new Dictionary<string, ModuleData>(StringComparer.Ordinal);
        ModuleData GetModule(string name)
        {
            if (name == null) name = "(unknown)";
            if (!byModule.ContainsKey(name)) byModule[name] = new ModuleData { Name = name };
            return byModule[name];
        }

        bool IsSkip(string fn) => fn.Contains('[') || fn.Contains("Reference") || fn.Contains("+<>c") || fn.Contains("Descriptor") || fn.Contains("Enumerator") || fn.Contains("Iterator") || fn.Contains("Abstract");

        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (IsSkip(fn)) continue;

            if (fn.EndsWith(".OmlHeader")) continue; // We use ESpace for module attribution now

            if (fn.EndsWith("Flows+UserAction") || fn == "ServiceStudio.Model.Flows+ServiceAPIMethod" || fn.EndsWith("NRFlows+ClientActionFlow") || fn.EndsWith("NRFlows+AbstractSystemClientAction"))
            {
                var nm = ReadStr(obj, "_name"); if (string.IsNullOrEmpty(nm)) continue;
                var kind = fn.EndsWith("Flows+UserAction") ? "ServerAction" : fn == "ServiceStudio.Model.Flows+ServiceAPIMethod" ? "ServiceAction" : "ClientAction";
                var mod = GetModule(ResolveModule(obj, espaceMap));
                mod.actions[obj.Address] = (nm, kind, ReadBool(obj, "_public"), ReadStr(obj, "_description") ?? "", ReadParams(obj, "_inputParameters", heap), ReadParams(obj, "_outputParameters", heap), new List<NodeInfo>());
                continue;
            }
            if (fn.StartsWith("ServiceStudio.Model.Nodes+") || fn.StartsWith("ServiceStudio.Model.NRNodes+") || fn.Contains(".Nodes+"))
            {
                var parent = ReadRef(obj, "parent"); if (parent.IsNull) continue;
                var info = ReadNodeInfo(obj, heap);
                // Attribute node to its parent action's module; if parent is an action, use its module.
                // NRNodes (New Runtime client-action nodes) may lack ownerESpace, so fall back to the
                // parent action's module to keep them in the same per-module bucket as their action.
                var modName = ResolveModule(obj, espaceMap) ?? ResolveModule(parent, espaceMap);
                var mod = GetModule(modName);
                if (!mod.nodesByParent.ContainsKey(parent.Address)) mod.nodesByParent[parent.Address] = new List<NodeInfo>();
                mod.nodesByParent[parent.Address].Add(info);
                continue;
            }
            if (fn.StartsWith("ServiceStudio.Model.Links+") || fn.Contains(".Links+"))
            {
                var src = ReadRef(obj, "parent"); var tgt = ReadRef(obj, "_targetNode"); if (src.IsNull || tgt.IsNull) continue;
                var mod = GetModule(ResolveModule(obj, espaceMap));
                var lt = SimplifyTypeName(fn);
                if (!mod.linksBySource.ContainsKey(src.Address)) mod.linksBySource[src.Address] = new List<(ulong, string)>();
                mod.linksBySource[src.Address].Add((tgt.Address, lt));
                continue;
            }
            if (fn == "ServiceStudio.Model.Entity")
            {
                var nm = ReadStr(obj, "_name"); if (string.IsNullOrEmpty(nm)) continue;
                var mod = GetModule(ResolveModule(obj, espaceMap));
                mod.entities.Add(new EntityInfo { name = nm, isPublic = ReadBool(obj, "_public"), attributes = ReadEntityAttributes(obj, heap) });
                continue;
            }
            if (fn == "ServiceStudio.Model.Structure" || fn == "ServiceStudio.Model.AnonymousStructure")
            {
                var nm = ReadStr(obj, "_name"); if (string.IsNullOrEmpty(nm)) continue;
                var mod = GetModule(ResolveModule(obj, espaceMap));
                mod.structures.Add(new StructureInfo { name = nm, isPublic = ReadBool(obj, "_public"), isAnonymous = fn == "ServiceStudio.Model.AnonymousStructure", attributes = fn.Contains("Anonymous") ? ReadAnonStructureAttributes(obj, heap) : ReadStructureAttributes(obj, heap) });
                continue;
            }
            if (fn == "ServiceStudio.Model.Variables+SiteProperty")
            {
                var nm = ReadStr(obj, "_name"); if (string.IsNullOrEmpty(nm)) continue;
                var mod = GetModule(ResolveModule(obj, espaceMap));
                var typeObj = ReadRef(obj, "_type");
                var typeName = ReadStr(typeObj, "_name") ?? SimplifyTypeName(typeObj.Type?.Name ?? "Unknown");
                mod.siteProps.Add(new SitePropertyInfo { name = nm, type = typeName, isReadOnly = ReadBool(obj, "_isReadOnlySiteProperty"), description = ReadStr(obj, "_description") ?? "" });
                continue;
            }
        }

        // Generate per-module report
        foreach (var kv in byModule.OrderBy(m => m.Key))
        {
            var md = kv.Value;

            // Match nodes to actions
            foreach (var nk in md.nodesByParent)
                if (md.actions.ContainsKey(nk.Key))
                { var a = md.actions[nk.Key]; a.nodes = nk.Value; md.actions[nk.Key] = a; }

            // Fallback: for actions whose nodes weren't caught by the parent-reference
            // scan (e.g. New Runtime client-action nodes, or any action whose flow nodes
            // live only in the _nodesShownInESpaceTree / _nodesNotShownInESpaceTree
            // collections), read those collections directly off the action object. The
            // outgoing links for these nodes were already captured globally (Links+), so
            // flow tracing still works once the node addresses are known.
            foreach (var ak in md.actions.Keys.ToList())
            {
                var a = md.actions[ak];
                if (a.nodes.Count > 0) continue;
                var actionObj = heap.GetObject(ak);
                if (actionObj.IsNull) continue;
                var fb = new List<NodeInfo>();
                foreach (var n in ReadColl(ReadRef(actionObj, "_nodesShownInESpaceTree"), heap)) fb.Add(ReadNodeInfo(n, heap));
                foreach (var n in ReadColl(ReadRef(actionObj, "_nodesNotShownInESpaceTree"), heap)) fb.Add(ReadNodeInfo(n, heap));
                if (fb.Count > 0) { a.nodes = fb; md.actions[ak] = a; }
            }

            var entities = md.entities.GroupBy(e => e.name).Select(g => g.First()).ToList();
            var structures = md.structures.GroupBy(s => s.name).Select(g => g.First()).ToList();
            var siteProps = md.siteProps.GroupBy(p => p.name).Select(g => g.First()).ToList();
            var serverActions = md.actions.Values.Where(a => a.kind == "ServerAction").GroupBy(a => a.name).Select(g => g.First()).ToList();
            var serviceActions = md.actions.Values.Where(a => a.kind == "ServiceAction").GroupBy(a => a.name).Select(g => g.First()).ToList();
            var clientActions = md.actions.Values.Where(a => a.kind == "ClientAction").GroupBy(a => a.name).Select(g => g.First()).ToList();

            sb.AppendLine("================================================================");
            sb.AppendLine($"  MODULE: {md.Name}");
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

            void TraceAction((string name, string kind, bool isPublic, string desc, List<ParamInfo> inputs, List<ParamInfo> outputs, List<NodeInfo> nodes) a)
            {
                sb.AppendLine(); sb.AppendLine($"  --- {a.name} ---");
                sb.AppendLine($"    Public:      {(a.isPublic ? "Yes" : "No")}");
                sb.AppendLine($"    Description: {(string.IsNullOrEmpty(a.desc) ? "(none)" : a.desc)}");
                sb.AppendLine($"    Inputs:      {FormatParams(a.inputs)}");
                sb.AppendLine($"    Outputs:     {FormatParams(a.outputs)}");
                if (a.nodes.Count > 0)
                {
                    var nodeDict = a.nodes.ToDictionary(n => n.addr, n => n);
                    var visited = new HashSet<ulong>();
                    var lines = new List<string>();
                    int counter = 1;
                    void Trace(ulong addr, int indent, string label)
                    {
                        if (visited.Contains(addr)) { if (nodeDict.TryGetValue(addr, out var vn)) lines.Add($"{new string(' ', indent)}    (loops back to [{vn.type}])"); return; }
                        visited.Add(addr);
                        if (!nodeDict.TryGetValue(addr, out var node)) return;
                        var prefix = new string(' ', indent);
                        var line = $"{prefix}{counter,2}. ";
                        if (!string.IsNullOrEmpty(label)) line += $"{label}: ";
                        line += $"[{node.type}]";
                        if (!string.IsNullOrEmpty(node.name)) line += $" {node.name}";
                        if (!string.IsNullOrEmpty(node.actionRef)) line += $" -> {node.actionRef}";
                        lines.Add(line); counter++;
                        foreach (var a2 in node.assignments) lines.Add($"{prefix}      {a2}");
                        if (!string.IsNullOrEmpty(node.condition)) lines.Add($"{prefix}      condition: {node.condition}");
                        if (!string.IsNullOrEmpty(node.details) && node.type == "Comment") lines.Add($"{prefix}      {node.details}");
                        // Aggregate (DataSet) details
                        if (node.aggSources.Count > 0) lines.Add($"{prefix}      Source: {string.Join(", ", node.aggSources)}");
                        if (node.aggJoins.Count > 0) { lines.Add($"{prefix}      Joins:"); foreach (var j in node.aggJoins) lines.Add($"{prefix}        - {j}"); }
                        if (node.aggFilters.Count > 0) { lines.Add($"{prefix}      Filters:"); foreach (var f in node.aggFilters) lines.Add($"{prefix}        - {f}"); }
                        if (node.aggSorts.Count > 0) { lines.Add($"{prefix}      Sort:"); foreach (var s in node.aggSorts) lines.Add($"{prefix}        - {s}"); }
                        if (node.aggGroupBy.Count > 0) { lines.Add($"{prefix}      Group By:"); foreach (var g in node.aggGroupBy) lines.Add($"{prefix}        - {g}"); }
                        if (node.aggCalculated.Count > 0) { lines.Add($"{prefix}      Calculated:"); foreach (var c in node.aggCalculated) lines.Add($"{prefix}        - {c}"); }
                        // SQL (AdvancedQuery) details
                        if (node.sqlParameters.Count > 0) lines.Add($"{prefix}      Parameters: {string.Join(", ", node.sqlParameters)}");
                        if (node.sqlOutput.Count > 0) lines.Add($"{prefix}      Output: {string.Join(", ", node.sqlOutput)}");
                        if (!string.IsNullOrEmpty(node.sqlText)) { lines.Add($"{prefix}      SQL:"); foreach (var sqlLine in node.sqlText.Replace("\r\n", "\n").Split('\n')) lines.Add($"{prefix}        {sqlLine}"); }
                        if (!md.linksBySource.TryGetValue(addr, out var links) || links.Count == 0) return;
                        var seq = links.Where(l => l.linkType == "Sequence").ToList();
                        var cycle = links.Where(l => l.linkType == "Cycle").ToList();
                        var cond = links.Where(l => l.linkType == "Condition").ToList();
                        var other = links.Where(l => l.linkType == "Otherwise").ToList();
                        var truel = links.Where(l => l.linkType == "True").ToList();
                        var falsel = links.Where(l => l.linkType == "False").ToList();
                        var comment = links.Where(l => l.linkType == "Comment").ToList();
                        foreach (var item in comment) if (nodeDict.TryGetValue(item.target, out var cn) && !string.IsNullOrEmpty(cn.details)) lines.Add($"{prefix}      Comment: {cn.details}");
                        foreach (var item in cycle) { lines.Add($"{prefix}      Cycle (loop body):"); Trace(item.target, indent + 8, ""); }
                        foreach (var item in cond) { if (nodeDict.TryGetValue(item.target, out var tn)) lines.Add($"{prefix}      Condition -> [{tn.type}]{(string.IsNullOrEmpty(tn.name) ? "" : " " + tn.name)}:"); Trace(item.target, indent + 8, ""); }
                        foreach (var item in other) { lines.Add($"{prefix}      Otherwise:"); Trace(item.target, indent + 8, ""); }
                        foreach (var item in truel) { lines.Add($"{prefix}      True:"); Trace(item.target, indent + 8, ""); }
                        foreach (var item in falsel) { lines.Add($"{prefix}      False:"); Trace(item.target, indent + 8, ""); }
                        foreach (var item in seq) Trace(item.target, indent, "");
                    }
                    var start = a.nodes.FirstOrDefault(n => n.type == "Start");
                    if (start != null) Trace(start.addr, 4, ""); else lines.Add("    (no Start node found)");
                    var ehs = a.nodes.Where(n => n.type == "ErrorHandler" && !visited.Contains(n.addr)).ToList();
                    if (ehs.Count > 0) { lines.Add(""); lines.Add("    Exception Handler:"); foreach (var eh in ehs) Trace(eh.addr, 8, ""); }
                    var unv = a.nodes.Where(n => !visited.Contains(n.addr)).ToList();
                    if (unv.Count > 0) { lines.Add(""); lines.Add("    Other nodes (unvisited):"); foreach (var n in unv) { var l = $"      [{n.type}]"; if (!string.IsNullOrEmpty(n.name)) l += $" {n.name}"; if (!string.IsNullOrEmpty(n.condition)) l += $" condition: {n.condition}"; if (n.aggSources.Count > 0) l += $" source: {string.Join(", ", n.aggSources)}"; if (n.aggFilters.Count > 0) l += $" filters: {string.Join("; ", n.aggFilters)}"; if (!string.IsNullOrEmpty(n.sqlText)) l += $" SQL: {n.sqlText.Replace("\n", " ").Substring(0, Math.Min(80, n.sqlText.Length))}{(n.sqlText.Length > 80 ? "..." : "")}"; lines.Add(l); } }
                    sb.AppendLine($"    Flow ({a.nodes.Count} nodes, traced by connections):");
                    foreach (var line in lines) sb.AppendLine(line);
                }
                else sb.AppendLine("    Flow: (no nodes found)");
            }

            if (serverActions.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SERVER ACTIONS ({serverActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in serverActions.OrderBy(x => x.name)) TraceAction(a); }
            if (serviceActions.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SERVICE ACTIONS ({serviceActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in serviceActions.OrderBy(x => x.name)) TraceAction(a); }
            if (clientActions.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  CLIENT ACTIONS ({clientActions.Count})"); sb.AppendLine("================================================================"); foreach (var a in clientActions.OrderBy(x => x.name)) TraceAction(a); }
            if (entities.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  ENTITIES ({entities.Count})"); sb.AppendLine("================================================================"); foreach (var e in entities.OrderBy(x => x.name)) { sb.AppendLine(); sb.AppendLine($"  --- {e.name} [{(e.isPublic ? "public" : "private")}] ---"); if (e.attributes.Count == 0) sb.AppendLine("    (no attributes)"); else { sb.AppendLine("    Attributes:"); foreach (var a in e.attributes) { var tags = new List<string>(); if (a.isKey) tags.Add("KEY"); if (a.isMandatory) tags.Add("required"); sb.AppendLine($"      - {a.name} : {a.type}{(tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "")}"); } } } }
            if (structures.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  STRUCTURES ({structures.Count})"); sb.AppendLine("================================================================"); foreach (var s in structures.OrderBy(x => x.name)) { sb.AppendLine(); sb.AppendLine($"  --- {s.name}{(s.isAnonymous ? " [anonymous]" : $" [{(s.isPublic ? "public" : "private")}]")} ---"); if (s.attributes.Count == 0) sb.AppendLine("    (no attributes)"); else { sb.AppendLine("    Attributes:"); foreach (var a in s.attributes) sb.AppendLine($"      - {a.name} : {a.type}"); } } }
            if (siteProps.Count > 0) { sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine($"  SITE PROPERTIES ({siteProps.Count})"); sb.AppendLine("================================================================"); foreach (var p in siteProps.OrderBy(x => x.name)) { sb.AppendLine(); var tags = new List<string>(); if (p.isReadOnly) tags.Add("read-only"); sb.AppendLine($"  {p.name} : {p.type}{(tags.Count > 0 ? $" [{string.Join(", ", tags)}]" : "")}{(!string.IsNullOrEmpty(p.description) ? $" - {p.description}" : "")}"); } }
            sb.AppendLine(); sb.AppendLine("================================================================"); sb.AppendLine("  END OF MODULE REPORT"); sb.AppendLine("================================================================");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
