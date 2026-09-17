// OutSystemsMcp - a self-contained MCP (Model Context Protocol) stdio server
// exposing OutSystems module-metadata tools. Speaks raw JSON-RPC 2.0 over stdio.
// Tools:
//   get_open_module   - list modules open in running Service Studio (from AutoSave cache)
//   parse_oml_header  - parse a .oml file's header (name, eSpaceKey, versions, ...)
//   live_reader       - read the open module's entities/structures/attributes live from memory (ClrMD)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Runtime;

while (Console.ReadLine() is string line && line != null)
{
    JsonNode msg;
    try { msg = JsonNode.Parse(line); } catch { continue; }
    var method = msg["method"]?.ToString();
    var id = msg["id"];
    if (method == "initialize")
        Respond(id, new JsonObject { ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "outsystems-tools", ["version"] = "1.0" } });
    else if (method == "notifications/initialized") { /* no response */ }
    else if (method == "tools/list") Respond(id, new JsonObject { ["tools"] = ToolsList() });
    else if (method == "tools/call")
    {
        var name = msg["params"]?["name"]?.ToString();
        var arguments = msg["params"]?["arguments"] as JsonObject;
        string result;
        try { result = Dispatch(name, arguments); }
        catch (Exception ex) { result = "ERROR: " + ex.Message; }
        Respond(id, new JsonObject { ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result } } });
    }
}

static void Respond(JsonNode id, JsonNode result)
{
    var resp = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
    if (id != null) resp["id"] = JsonNode.Parse(id.ToJsonString());
    Console.WriteLine(resp.ToJsonString());
    Console.Out.Flush();
}

static JsonArray ToolsList() => new()
{
    new JsonObject {
        ["name"] = "get_open_module",
        ["description"] = "List modules currently open in a running Service Studio instance, read from its AutoSave cache. No server or productKey needed. Returns module name, eSpaceKey, version, saved time per open module.",
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
    },
    new JsonObject {
        ["name"] = "parse_oml_header",
        ["description"] = "Parse the header of an OutSystems .oml file (magic OML, pipe-delimited header). Returns module name, eSpaceKey, IsExtension, SS/platform versions, description, saved time. Works offline, no DLLs.",
        ["inputSchema"] = new JsonObject { ["type"] = "object",
            ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Path to a .oml file" } },
            ["required"] = new JsonArray { "path" } }
    },
    new JsonObject {
        ["name"] = "live_reader",
        ["description"] = "Read the module open in Service Studio directly from process memory via ClrMD. Returns a structured tree: each Entity/Structure -> its own Attributes (mapped via .parent back-references). Bypasses the .oml productKey signature gate entirely. Auto-detects the ServiceStudio PID, or use the optional pid argument.",
        ["inputSchema"] = new JsonObject { ["type"] = "object",
            ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio process ID (auto-detected if omitted)" } } }
    },
};

static string Dispatch(string name, JsonObject arguments)
{
    return name switch
    {
        "get_open_module" => GetOpenModule(),
        "parse_oml_header" => ParseOmlHeader(arguments?["path"]?.ToString() ?? ""),
        "live_reader" => LiveReader(arguments != null && arguments.ContainsKey("pid") ? (int?)arguments["pid"].GetValue<int>() : null),
        _ => $"Unknown tool: {name}"
    };
}

// ---------- .oml header parser (offline) ----------
static string ParseOmlHeader(string path)
{
    if (!File.Exists(path)) return "File not found: " + path;
    var b = File.ReadAllBytes(path);
    if (b.Length < 11 || b[0] != (byte)'O' || b[1] != (byte)'M' || b[2] != (byte)'L') return "Not an OML file: " + path;
    int idx = -1;
    for (int i = 7; i < b.Length - 4; i++)
        if (b[i] == 0x77 && b[i + 1] == 0x67 && b[i + 2] == 0x7C && b[i + 3] == 0x2F) { idx = i; break; }
    if (idx < 0) return "OML body marker 'wg|/' not found.";
    var sb = new StringBuilder();
    for (int i = 7; i < idx; i++) sb.Append((char)b[i]);
    var f = sb.ToString().Split('|');
    return $"File        : {Path.GetFileName(path)}\nModuleName  : {f[6]}\nESpaceKey   : {f[3]}\nIsExtension : {f[5]}\nSSVersion   : {f[0]}\nPlatVersion : {f[1]}\nBuild       : {f[2]}\nDescription : {f[7]}\nSavedAt     : {f[8]}";
}

// ---------- AutoSave cache -> open module (with ClrMD fallback) ----------
// The AutoSave cache is unreliable: modules that are open but not recently
// saved (or whose AutoSave path label differs) produce zero files, so the old
// implementation falsely reported "no module open". The ClrMD fallback below
// enumerates ServiceStudio.Model.ESpace objects from process memory to list
// the actually-open modules when AutoSave yields nothing for a given PID.
static string GetOpenModule()
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) return "Service Studio is not running. Open a module first.";
    var autoSave = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OutSystems", "ServiceStudio 11 XPlatform Stable", "AutoSave");
    var sb = new StringBuilder();
    sb.AppendLine($"Service Studio running: PID(s) {string.Join(", ", procs.Select(p => p.Id))}\n");
    foreach (var p in procs)
    {
        string[] bak = Directory.Exists(autoSave) ? Directory.GetFiles(autoSave, $"{p.Id}_*") : Array.Empty<string>();
        if (bak.Length == 0)
        {
            // Fallback: read open modules live from process memory (ClrMD).
            var liveModules = ListModulesFromHeap(p.Id);
            if (liveModules.Count > 0)
            {
                sb.AppendLine($"PID {p.Id}: {liveModules.Count} module(s) open [detected via ClrMD; no AutoSave file]:");
                foreach (var m in liveModules) sb.AppendLine($"  - {m}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"PID {p.Id}: running, no AutoSave file and no modules detected in memory.");
            }
            continue;
        }
        foreach (var f in bak) { sb.AppendLine($"PID {p.Id}:"); sb.AppendLine(ParseOmlHeader(f)); sb.AppendLine(); }
    }
    return sb.ToString().TrimEnd();
}

// ClrMD fallback for GetOpenModule: enumerate ServiceStudio.Model.ESpace
// objects on the managed heap to list the names of modules currently loaded
// in a Service Studio process. Same heap-walk technique as LiveReader, but
// lightweight (names only). Returns an empty list on any failure.
static List<string> ListModulesFromHeap(int pid)
{
    var result = new List<string>();
    try
    {
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap) return result;
        string ReadStr(ClrObject o, string want)
        {
            for (var t = o.Type; t != null; t = t.BaseType)
                foreach (var f in t.Fields)
                    if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                    { try { var s = f.ReadString(o.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
            return null;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            if (t.Name != "ServiceStudio.Model.ESpace") continue;
            var name = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
                result.Add(name);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
    }
    catch { }
    return result;
}

// ---------- ClrMD live reader (structured, per-module) ----------
// Now with per-module attribution via ESpace.ownerESpace back-reference.
// Each model object (entity, structure, attribute) has an ownerESpace field
// pointing to its owning ESpace, which has a _name field = module name.
static string LiveReader(int? pidArg)
{
    int pid;
    if (pidArg.HasValue) pid = pidArg.Value;
    else
    {
        var procs = Process.GetProcessesByName("ServiceStudio");
        if (procs.Length == 0) return "Service Studio is not running.";
        pid = procs[0].Id;
    }

    using var target = DataTarget.AttachToProcess(pid, suspend: false);
    var runtime = target.ClrVersions[0].CreateRuntime();
    var heap = runtime.Heap;
    var sb = new StringBuilder();
    sb.AppendLine($"Attached to PID {pid}. CanWalkHeap={heap.CanWalkHeap}\n");

    string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }
    bool InModel(string n) => n.StartsWith("ServiceStudio.Model.", StringComparison.Ordinal) || n.StartsWith("OutSystems.Model.", StringComparison.Ordinal);
    bool IsSkip(string n) => n.Contains('[') || n.Contains("Reference") || n.Contains("Abstract") || n.Contains("Descriptor") || n.Contains("Signature") || n.Contains("Enumerator") || n.Contains("Iterator") || n.Contains("+<>c");

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

    // Phase 1: Build ESpace address -> module name map
    var espaceMap = new Dictionary<ulong, string>();
    foreach (var obj in heap.EnumerateObjects())
    {
        var t = obj.Type; if (t is null) continue;
        if (t.Name != "ServiceStudio.Model.ESpace") continue;
        var name = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
        if (!string.IsNullOrEmpty(name)) espaceMap[obj.Address] = name;
    }

    // Per-module data containers
    var byModule = new Dictionary<string, (Dictionary<ulong, (string name, string kind)> elements, List<ClrObject> actionNodes)>(StringComparer.Ordinal);
    (Dictionary<ulong, (string name, string kind)> elements, List<ClrObject> actionNodes) GetMod(string name)
    {
        if (name == null) name = "(unknown)";
        if (!byModule.ContainsKey(name)) byModule[name] = (new Dictionary<ulong, (string, string)>(), new List<ClrObject>());
        return byModule[name];
    }
    string ResolveModule(ClrObject obj)
    {
        var oe = ReadRef(obj, "ownerESpace");
        if (!oe.IsNull && espaceMap.TryGetValue(oe.Address, out var name)) return name;
        return null;
    }

    foreach (var obj in heap.EnumerateObjects())
    {
        var t = obj.Type; if (t is null) continue;
        var fn = t.Name ?? "";
        if (IsSkip(fn)) continue;
        if (fn.EndsWith(".OmlHeader", StringComparison.Ordinal)) continue;
        if (!InModel(fn)) continue;
        var leaf = Leaf(fn);
        if (leaf == "Entity" || leaf == "Structure" || leaf == "SystemStructure" || leaf == "StaticEntity")
        {
            var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name"); if (nm == null) continue;
            var mod = GetMod(ResolveModule(obj));
            mod.elements[obj.Address] = (nm, leaf);
        }
        else if (leaf.Contains("ExecuteClientAction") || leaf.Contains("ExecuteServerAction"))
        {
            var mod = GetMod(ResolveModule(obj));
            mod.actionNodes.Add(obj);
        }
    }

    // Phase 2: Attribute attributes to their owning entities/structures (per module)
    var moduleAttrs = new Dictionary<string, Dictionary<ulong, List<(string name, string label, string kind)>>>(StringComparer.Ordinal);
    foreach (var mkv in byModule)
    {
        var attrsByOwner = new Dictionary<ulong, List<(string name, string label, string kind)>>();
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (IsSkip(fn) || !InModel(fn)) continue;
            var leaf = Leaf(fn);
            if (leaf != "EntityAttribute" && leaf != "StructureAttribute") continue;
            var nm = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
            if (nm == null) continue;
            var cur = obj; ulong owner = 0;
            for (int hop = 0; hop < 8 && !cur.IsNull; hop++)
            {
                var p = ReadRef(cur, "parent"); if (p.IsNull) p = ReadRef(cur, "_parent"); if (p.IsNull) p = ReadRef(cur, "ownerESpace");
                if (p.IsNull) break;
                if (mkv.Value.elements.ContainsKey(p.Address)) { owner = p.Address; break; }
                cur = p;
            }
            if (!attrsByOwner.ContainsKey(owner)) attrsByOwner[owner] = new List<(string, string, string)>();
            attrsByOwner[owner].Add((nm, ReadStr(obj, "_label"), leaf));
        }
        moduleAttrs[mkv.Key] = attrsByOwner;
    }

    // Output per-module
    foreach (var mkv in byModule.OrderBy(m => m.Key))
    {
        var (elements, actionNodes) = mkv.Value;
        var attrsByOwner = moduleAttrs.TryGetValue(mkv.Key, out var ma) ? ma : new Dictionary<ulong, List<(string name, string label, string kind)>>();
        var entities = elements.Where(kv => kv.Value.kind == "Entity").OrderBy(kv => kv.Value.name).ToList();
        var structures = elements.Where(kv => kv.Value.kind != "Entity").OrderBy(kv => kv.Value.name).ToList();

        sb.AppendLine($"MODULE: {mkv.Key}   (PID {pid})\n");
        sb.AppendLine($"ENTITIES ({entities.Count}):");
        foreach (var kv in entities)
        {
            sb.AppendLine($"  - {kv.Value.name}");
            if (attrsByOwner.TryGetValue(kv.Key, out var attrs) && attrs.Count > 0)
                foreach (var a in attrs.OrderBy(a => a.name))
                    sb.AppendLine($"      . {a.name}" + (a.label != null && a.label != a.name ? $"  [{a.label}]" : ""));
        }
        sb.AppendLine();
        sb.AppendLine($"STRUCTURES ({structures.Count}):");
        foreach (var kv in structures)
        {
            var tag = kv.Value.kind == "SystemStructure" ? "  [system]" : (kv.Value.kind == "StaticEntity" ? "  [static entity]" : "");
            sb.AppendLine($"  - {kv.Value.name}{tag}");
            if (attrsByOwner.TryGetValue(kv.Key, out var attrs) && attrs.Count > 0)
                foreach (var a in attrs.OrderBy(a => a.name))
                    sb.AppendLine($"      . {a.name}" + (a.label != null && a.label != a.name ? $"  [{a.label}]" : ""));
        }
        sb.AppendLine();
        if (actionNodes.Count > 0)
        {
            var names = actionNodes.DistinctBy(x => x.Address).Select(o => ReadStr(o, "_name") ?? ReadStr(o, "name"))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            sb.AppendLine($"ACTION NODES ({names.Count}) [template/layout calls]:");
            foreach (var n in names) sb.AppendLine($"  - {n}");
            sb.AppendLine();
        }
        sb.AppendLine("================================================================\n");
    }
    return sb.ToString().TrimEnd();
}
