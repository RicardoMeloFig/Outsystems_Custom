using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Runtime;

// outsystems-ui MCP server: granular, read-only extraction of the OutSystems
// presentation layer (themes + CSS, screens, web blocks, widget tree, client
// actions) from a running Service Studio process via ClrMD. One tool per
// category, each writing its own split file(s) under docs/<ModuleName>/.
//
// Theme CSS is reconstructed from LightweightExpression.lightweightElements
// (TextElement.text + LightweightReferenceElement.reference), which the legacy
// UiExtractor missed (it read value/expressionElement, yielding empty CSS for
// NewRuntime themes). Theme grid/layout/themeValues are also captured.
class Program
{
    // ---- boilerplate AbstractObject fields to skip in generic dumps ----
    static readonly HashSet<string> SkipFields = new(StringComparer.Ordinal) {
        "rawProperties","parent","_textResources","_isZombie","<IsToolbarObject>k__BackingField","isNew",
        "createdOnCommandGeneration","ownerESpace","ownerCompilationUnit","hashAggregator","invalidatingHashes",
        "compilationInterfaceHashNeverInvalidated","verifyCache","viewGeneration","viewGenerationOfLastReferersRefresh",
        "childCollectionsViewGeneration","scopeEntryChildrenGeneration","id","key","match","forcedMatch","saveCaches",
        "isLoadingOrImporting","isImporting","wasDeleted","hashesBeingSerialized","disableViewGenerationIncrease",
        "lastModifiedByCommand","isSettingName","nameClashPending","_collectionMetadata","insideOnBeforeNameChange",
        "scope","topLevelScopeEntriesCache","insideGetMergeId","objectsInPathCache","replacement","referenceElementDepth",
        "_metadata","<Order>k__BackingField","<ESpaceTreeFolderOrder>k__BackingField","<ESpaceTreeTabOrder>k__BackingField",
        "_previousBaseThemeValue","_previousNormalPageLayoutValue","_previousHeaderWebBlockValue",
        "_previousMenuWebBlockValue","_previousFooterWebBlockValue","_finalCssSourceValueIsValid",
        "styleSuggestionsAreInvalid","lastStyleSuggestionsConsumer","lastStyleSuggestionsConsumerKind",
        "styleClassSuggestions","_comesFromElsewhere","settingGridProperties"
    };

    // ---- CSS diagnostics (written to css-diag.txt by WriteThemes) ----
    static StringBuilder _cssDiag = new();
    static int _diagElemCount, _diagArrLen, _diagSize;
    static string _diagListType, _diagLastLeaf;

    // ---- Manual string reading (bypasses ClrMD 3.0 ReadString 4096-char truncation) ----
    static ClrHeap _activeHeap;
    static int _stringLengthOffset = 8;  // default for 64-bit .NET Framework
    static int _firstCharOffset = 12;
    static bool _stringOffsetsInitialized;

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
                    ["serverInfo"] = new JsonObject { ["name"] = "outsystems-ui", ["version"] = "1.0" }
                });
            else if (method == "notifications/initialized") { }
            else if (method == "tools/list") Respond(id, new JsonObject { ["tools"] = ToolsList() });
            else if (method == "tools/call")
            {
                var name = msg["params"]?["name"]?.ToString();
                var args = msg["params"]?["arguments"] as JsonObject;
                string result;
                try { result = Dispatch(name, args); }
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

    static JsonObject ToolSchema(string name, string desc) => new()
    {
        ["name"] = name,
        ["description"] = desc,
        ["inputSchema"] = new JsonObject {
            ["type"] = "object",
            ["properties"] = new JsonObject {
                ["pid"] = new JsonObject { ["type"] = "number", ["description"] = "Service Studio PID (auto-detected if omitted). For multi-module, pass the PID of the single module open in that process." },
                ["outputDir"] = new JsonObject { ["type"] = "string", ["description"] = "Output base directory (default: docs). Files written to <outputDir>/<ModuleName>/" }
            }
        }
    };

    static JsonArray ToolsList() => new()
    {
        ToolSchema("extract_themes", "Extract themes: writes one <ThemeName>.css per theme (reconstructed CSS incl. LightweightExpression, tries all CSS sources and picks the longest) and theme-values.json (name, baseTheme, grid, layoutBlocks, themeValues) under docs/<Module>/. Read-only ClrMD."),
        ToolSchema("extract_screens", "Extract screen metadata (name, url, permissions, input/output params, local vars, events) to screens.json. No widget tree."),
        ToolSchema("extract_web_blocks", "Extract web block metadata to web-blocks.json. Same fields as screens."),
        ToolSchema("extract_ui_tree", "Write visual widget-tree maps: ui-tree-screens.txt and ui-tree-blocks.txt."),
        ToolSchema("extract_client_actions", "Write client action flows (link-traced, JS nodes, assignments) to client-actions-screens.json and client-actions-blocks.json."),
        ToolSchema("extract_ui", "Umbrella: run all of the above + write summary.json. Full UI extraction in split files.")
    };

    static string Dispatch(string name, JsonObject args)
    {
        int? pidArg = args != null && args.ContainsKey("pid") && args["pid"] != null ? (int?)args["pid"].GetValue<int>() : null;
        string outputDir = args != null && args.ContainsKey("outputDir") && args["outputDir"] != null ? args["outputDir"].ToString() : "docs";
        int pid = GetPid(pidArg);
        // Keep the DataTarget alive for the WHOLE dispatch (enumerate + all file
        // writes). If the target is disposed before the writers read the captured
        // ClrObjects, every field read returns garbage/empty. (outsystems-logic
        // keeps its target alive the same way, inside one method scope.)
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        _activeHeap = heap;
        if (!_stringOffsetsInitialized) { InitStringOffsets(heap); _stringOffsetsInitialized = true; }
        var contexts = CollectAllMulti(pid, heap);
        var sb = new StringBuilder();
        foreach (var ctx in contexts.OrderBy(c => c.ModuleName))
        {
            var moduleDir = Path.Combine(outputDir, SafeName(ctx.ModuleName));
            Directory.CreateDirectory(moduleDir);
            sb.AppendLine($"--- {ctx.ModuleName} ({ctx.Screens.Count} screens, {ctx.Blocks.Count} blocks, {ctx.Themes.Count} themes) ---");
            string result = name switch
            {
                "extract_themes" => WriteThemes(ctx, moduleDir),
                "extract_screens" => WriteScreens(ctx, moduleDir),
                "extract_web_blocks" => WriteWebBlocks(ctx, moduleDir),
                "extract_ui_tree" => WriteUiTree(ctx, moduleDir),
                "extract_client_actions" => WriteClientActions(ctx, moduleDir),
                "extract_ui" => WriteAll(ctx, moduleDir),
                _ => "Unknown tool: " + name
            };
            sb.AppendLine(result);
        }
        return sb.ToString().TrimEnd();
    }

    static string SafeName(string s) => string.IsNullOrEmpty(s) ? "UnknownModule" : string.Join("_", s.Split(Path.GetInvalidPathChars())).Trim();

    static int GetPid(int? pidArg)
    {
        if (pidArg.HasValue) return pidArg.Value;
        var procs = Process.GetProcessesByName("ServiceStudio");
        if (procs.Length == 0) throw new Exception("Service Studio is not running. Open a module first, or pass pid.");
        if (procs.Length > 1) throw new Exception(procs.Length + " Service Studio instances running. Pass pid explicitly to target one.");
        return procs[0].Id;
    }

    // ====================================================================
    //  Collection context
    // ====================================================================
    class Ctx
    {
        public int Pid; public string ModuleName; public ClrHeap Heap;
        public List<ClrObject> Themes = new(), Screens = new(), Blocks = new();
        public Dictionary<ulong, ClrObject> StyleSheets = new();
        public Dictionary<ulong, ClrObject> WidgetsByAddr = new();
        public Dictionary<ulong, List<ClrObject>> WidgetChildren = new(), EventsByWidget = new(), ClientActionsByParent = new(), NodesByParent = new();
        public Dictionary<ulong, List<(ulong target, string linkType)>> LinksBySource = new();
        public Dictionary<ulong, List<ClrObject>> StyleSheetsByParent = new(); // WebStyleSheet objects keyed by parent (block/screen) address
    }

    static Ctx CollectAll(int pid, ClrHeap heap)
    {
        var contexts = CollectAllMulti(pid, heap);
        return contexts.Count > 0 ? contexts[0] : new Ctx { Pid = pid, Heap = heap, ModuleName = "(unknown)" };
    }

    // ---- ESpace-based module attribution ----
    // Builds a map of ESpace object address -> module name by finding all
    // ServiceStudio.Model.ESpace objects in the heap and reading their _name field.
    // This is the key to per-module attribution in a merged heap: every model
    // object has an ownerESpace field pointing to its owning ESpace.
    static Dictionary<ulong, string> BuildEspaceMap(ClrHeap heap)
    {
        var map = new Dictionary<ulong, string>();
        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (fn != "ServiceStudio.Model.ESpace") continue;
            var name = ReadStr(obj, "_name") ?? ReadStr(obj, "name");
            if (!string.IsNullOrEmpty(name))
                map[obj.Address] = name;
        }
        return map;
    }

    // Resolves the module name for a model object by reading its ownerESpace
    // field and looking up the ESpace address in the map. Falls back to null
    // if the object has no ownerESpace or the ESpace is not in the map.
    static string ResolveModule(ClrObject obj, Dictionary<ulong, string> espaceMap)
    {
        var oe = ReadRef(obj, "ownerESpace");
        if (!oe.IsNull && espaceMap.TryGetValue(oe.Address, out var name))
            return name;
        return null;
    }

    static List<Ctx> CollectAllMulti(int pid, ClrHeap heap)
    {
        // Phase 1: Build ESpace address -> module name map
        var espaceMap = BuildEspaceMap(heap);

        // Phase 2: Collect all model objects, attributing each to its module
        // via ownerESpace. Child objects (widgets, events, nodes, links) that
        // don't have a direct ownerESpace are attributed via their parent
        // screen/block (traced through the parent chain).
        var contexts = new Dictionary<string, Ctx>(StringComparer.Ordinal);
        Ctx GetCtx(string moduleName)
        {
            if (moduleName == null) moduleName = "(unknown)";
            if (!contexts.ContainsKey(moduleName))
                contexts[moduleName] = new Ctx { Pid = pid, Heap = heap, ModuleName = moduleName };
            return contexts[moduleName];
        }

        bool IsSkip(string fn) => fn.Contains('[') || fn.Contains("+<>c") || fn.Contains("Enumerator") || fn.Contains("Iterator") || fn.Contains("Descriptor");
        bool IsWidget(string fn) => fn.StartsWith("ServiceStudio.Model.NRWebWidgets+", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.WebWidgets+", StringComparison.Ordinal);
        bool IsConcreteWidget(string leaf) => !leaf.StartsWith("Reference") && !leaf.StartsWith("From") && !leaf.StartsWith("Concrete_") && leaf != "Kind" && !leaf.Contains("Conversion");

        // Map of screen/block address -> module name (for child object attribution)
        var screenBlockModule = new Dictionary<ulong, string>();

        foreach (var obj in heap.EnumerateObjects())
        {
            var t = obj.Type; if (t is null) continue;
            var fn = t.Name ?? "";
            if (IsSkip(fn)) continue;
            var leaf = Leaf(fn);

            // Skip OmlHeader - we use ESpace for module attribution now
            if (fn.EndsWith(".OmlHeader", StringComparison.Ordinal)) continue;

            if (fn == "ServiceStudio.Model.NewRuntime.Theme" || fn == "ServiceStudio.Model.Theme")
            {
                var mod = ResolveModule(obj, espaceMap);
                var ctx = GetCtx(mod);
                ctx.Themes.Add(obj);
                continue;
            }
            if (leaf == "WebScreen" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
            {
                var mod = ResolveModule(obj, espaceMap);
                var ctx = GetCtx(mod);
                ctx.Screens.Add(obj);
                screenBlockModule[obj.Address] = ctx.ModuleName;
                continue;
            }
            if (leaf == "WebBlock" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
            {
                var mod = ResolveModule(obj, espaceMap);
                var ctx = GetCtx(mod);
                ctx.Blocks.Add(obj);
                screenBlockModule[obj.Address] = ctx.ModuleName;
                continue;
            }
            if (fn == "ServiceStudio.Model.WebStyleSheet" || fn == "ServiceStudio.Model.ReferenceWebStyleSheet" || fn == "ServiceStudio.Model.InvisibleWebStyleSheet" || fn == "ServiceStudio.Model.ReferenceInvisibleWebStyleSheet")
            {
                // StyleSheets are attributed via their parent theme's module
                var mod = ResolveModule(obj, espaceMap);
                var parent = ReadRef(obj, "parent");
                if (mod == null) { if (!parent.IsNull && screenBlockModule.TryGetValue(parent.Address, out var pm)) mod = pm; }
                var ctx = GetCtx(mod);
                if (!ctx.StyleSheets.ContainsKey(obj.Address)) ctx.StyleSheets[obj.Address] = obj;
                // Also store by parent (block/screen) for UI tree CSS attribution
                if (!parent.IsNull)
                {
                    if (!ctx.StyleSheetsByParent.ContainsKey(parent.Address)) ctx.StyleSheetsByParent[parent.Address] = new List<ClrObject>();
                    ctx.StyleSheetsByParent[parent.Address].Add(obj);
                }
                continue;
            }
            if (IsWidget(fn) && IsConcreteWidget(leaf))
            {
                // Widgets: try ownerESpace first, then trace parent chain to find screen/block
                var mod = ResolveModule(obj, espaceMap);
                if (mod == null)
                {
                    var parent = ReadRef(obj, "parent"); if (parent.IsNull) parent = ReadRef(obj, "owner");
                    if (!parent.IsNull)
                    {
                        // Walk up parent chain to find a known screen/block
                        var cur = parent;
                        for (int hop = 0; hop < 12 && !cur.IsNull; hop++)
                        {
                            if (screenBlockModule.TryGetValue(cur.Address, out var pm)) { mod = pm; break; }
                            var next = ReadRef(cur, "parent"); if (next.IsNull) next = ReadRef(cur, "owner");
                            if (next.IsNull || next.Address == cur.Address) break;
                            cur = next;
                        }
                        // Fallback: try ownerESpace on the parent
                        if (mod == null) mod = ResolveModule(parent, espaceMap);
                    }
                }
                var ctx = GetCtx(mod);
                ctx.WidgetsByAddr[obj.Address] = obj;
                var parent2 = ReadRef(obj, "parent"); if (parent2.IsNull) parent2 = ReadRef(obj, "owner");
                if (!parent2.IsNull) { if (!ctx.WidgetChildren.ContainsKey(parent2.Address)) ctx.WidgetChildren[parent2.Address] = new List<ClrObject>(); ctx.WidgetChildren[parent2.Address].Add(obj); }
                continue;
            }
            if (fn.StartsWith("ServiceStudio.Model.NRWebWidgetEvents+", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.WebWidgetEvents+", StringComparison.Ordinal))
            {
                if (leaf == "EventHandler" || leaf.Contains("OnClick"))
                {
                    var parent = ReadRef(obj, "parent");
                    if (!parent.IsNull)
                    {
                        // Find the module via parent widget's parent chain
                        var mod = ResolveModule(obj, espaceMap);
                        if (mod == null) { var cur = parent; for (int hop = 0; hop < 12 && !cur.IsNull; hop++) { if (screenBlockModule.TryGetValue(cur.Address, out var pm)) { mod = pm; break; } var next = ReadRef(cur, "parent"); if (next.IsNull || next.Address == cur.Address) break; cur = next; } }
                        if (mod == null) mod = ResolveModule(parent, espaceMap);
                        var ctx = GetCtx(mod);
                        if (!ctx.EventsByWidget.ContainsKey(parent.Address)) ctx.EventsByWidget[parent.Address] = new List<ClrObject>();
                        ctx.EventsByWidget[parent.Address].Add(obj);
                    }
                }
                continue;
            }
            if (fn == "ServiceStudio.Model.NRFlows+ClientScreenActionFlow" || fn == "ServiceStudio.Model.NRFlows+ClientActionFlow" || fn == "ServiceStudio.Model.NRFlows+DataScreenActionFlow")
            {
                var owner = ReadRef(obj, "parent");
                if (!owner.IsNull)
                {
                    var mod = ResolveModule(obj, espaceMap);
                    if (mod == null && screenBlockModule.TryGetValue(owner.Address, out var pm)) mod = pm;
                    var ctx = GetCtx(mod);
                    if (!ctx.ClientActionsByParent.ContainsKey(owner.Address)) ctx.ClientActionsByParent[owner.Address] = new List<ClrObject>();
                    ctx.ClientActionsByParent[owner.Address].Add(obj);
                }
                continue;
            }
            if (fn.StartsWith("ServiceStudio.Model.Nodes+", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.NRNodes+", StringComparison.Ordinal) || fn.Contains(".Nodes+"))
            {
                if (leaf == "Kind" || leaf.StartsWith("From") || leaf.Contains("Conversion")) continue;
                var parent = ReadRef(obj, "parent"); if (parent.IsNull) continue;
                var mod = ResolveModule(obj, espaceMap);
                if (mod == null) { var cur = parent; for (int hop = 0; hop < 12 && !cur.IsNull; hop++) { if (screenBlockModule.TryGetValue(cur.Address, out var pm)) { mod = pm; break; } var next = ReadRef(cur, "parent"); if (next.IsNull || next.Address == cur.Address) break; cur = next; } }
                if (mod == null) mod = ResolveModule(parent, espaceMap);
                var ctx = GetCtx(mod);
                if (!ctx.NodesByParent.ContainsKey(parent.Address)) ctx.NodesByParent[parent.Address] = new List<ClrObject>();
                ctx.NodesByParent[parent.Address].Add(obj);
                continue;
            }
            if (fn.StartsWith("ServiceStudio.Model.Links+", StringComparison.Ordinal) || fn.Contains(".Links+"))
            {
                var src = ReadRef(obj, "parent"); var tgt = ReadRef(obj, "_targetNode");
                if (src.IsNull || tgt.IsNull) continue;
                var mod = ResolveModule(obj, espaceMap);
                if (mod == null) { var cur = src; for (int hop = 0; hop < 12 && !cur.IsNull; hop++) { if (screenBlockModule.TryGetValue(cur.Address, out var pm)) { mod = pm; break; } var next = ReadRef(cur, "parent"); if (next.IsNull || next.Address == cur.Address) break; cur = next; } }
                if (mod == null) mod = ResolveModule(src, espaceMap);
                var ctx = GetCtx(mod);
                if (!ctx.LinksBySource.ContainsKey(src.Address)) ctx.LinksBySource[src.Address] = new List<(ulong, string)>();
                ctx.LinksBySource[src.Address].Add((tgt.Address, leaf));
                continue;
            }
        }
        // Filter out contexts with no meaningful content (e.g., system ESpaces with no screens/blocks/themes)
        var result = contexts.Values.Where(c => c.Screens.Count > 0 || c.Blocks.Count > 0 || c.Themes.Count > 0).ToList();
        return result;
    }

    // ====================================================================
    //  ClrMD helpers
    // ====================================================================
    static string Leaf(string f) { var i = f.LastIndexOf('.'); var l = i >= 0 ? f.Substring(i + 1) : f; var p = l.IndexOf('+'); return p >= 0 ? l.Substring(p + 1) : l; }

    // ClrMD 3.0's ReadString truncates strings at 4096 chars (likely a
    // ReadProcessMemory partial-read issue). These helpers read the .NET
    // string object directly from process memory, bypassing the bug.
    static void InitStringOffsets(ClrHeap heap)
    {
        try
        {
            // .NET string layout: [method-table-ptr (ptrSize bytes)] [_stringLength (4 bytes)] [chars...]
            // ClrMD's ClrField.Offset is relative to the data portion (after the method
            // table pointer), so we use ptrSize for the absolute offset from object address.
            int ptrSize = IntPtr.Size;
            _stringLengthOffset = ptrSize;      // 8 on 64-bit, 4 on 32-bit
            _firstCharOffset = ptrSize + 4;     // 12 on 64-bit, 8 on 32-bit
        }
        catch { }
    }
    static string ReadStrManual(ClrObject o, string want, ClrHeap heap)
    {
        if (o.IsNull) return null;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                {
                    try
                    {
                        var strObj = f.ReadObject(o.Address, false);
                        if (strObj.IsNull) return null;
                        var reader = heap.Runtime.DataTarget.DataReader;
                        var addr = strObj.Address;
                        if (!reader.Read<int>(addr + (ulong)_stringLengthOffset, out int length))
                            return null;
                        if (length <= 0 || length > 10_000_000) return null;
                        byte[] buf = new byte[length * 2];
                        int read = reader.Read(addr + (ulong)_firstCharOffset, buf.AsSpan());
                        if (read < 2) return null;
                        return Encoding.Unicode.GetString(buf, 0, read);
                    }
                    catch { }
                }
        return null;
    }

    static string ReadStr(ClrObject o, string want)
    {
        if (o.IsNull) return null;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
                { try {
                    var s = f.ReadString(o.Address, false);
                    // ClrMD 3.0 ReadString truncates strings >= 4096 chars.
                    // Fall back to manual memory reading when suspicious.
                    if (s != null && s.Length >= 4096 && _activeHeap != null)
                    {
                        var manual = ReadStrManual(o, want, _activeHeap);
                        if (manual != null && manual.Length > s.Length)
                        { _cssDiag.AppendLine($"  ReadStr: ClrMD truncated '{want}' ({s.Length} -> {manual.Length} chars via manual read)"); return manual; }
                    }
                    return s;
                } catch { } }
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
    static int ReadInt(ClrObject o, string want)
    {
        if (o.IsNull) return 0;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (!f.IsObjectReference && f.Name == want)
                { try { return f.Read<int>(o.Address, false); } catch { } }
        return 0;
    }
    static bool ReadBool(ClrObject o, string want) => ReadInt(o, want) != 0;
    static long? ReadNullableInt(ClrObject o, string want)
    {
        if (o.IsNull) return null;
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (!f.IsObjectReference && f.Name == want)
                {
                    try
                    {
                        var ft = f.Type;
                        if (ft != null && ft.Name == "System.Nullable<System.Int32>")
                        {
                            var hasValue = f.Read<int>(o.Address, false) != 0;
                            if (!hasValue) return null;
                            var fieldAddr = o.Address + (ulong)f.Offset;
                            foreach (var nf in ft.Fields)
                                if (nf.Name == "value" || nf.Name == "_value")
                                    try { return nf.Read<int>(fieldAddr, false); } catch { }
                            return null;
                        }
                        return f.Read<int>(o.Address, false);
                    }
                    catch { }
                }
        return null;
    }
    static List<ClrObject> ReadColl(ClrObject coll, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (coll.IsNull) return result;
        var arrObj = ReadRef(coll, "array"); var size = ReadInt(coll, "size");
        if (!arrObj.IsNull && arrObj.IsArray && size > 0)
        { var arr = arrObj.AsArray(); for (int i = 0; i < Math.Min(size, arr.Length); i++) { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { } } }
        return result;
    }
    // List<T>-style (_items array + _size) OR ISSCollection (array + size)
    static List<ClrObject> ReadList(ClrObject list, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (list.IsNull) return result;
        var items = ReadRef(list, "_items"); if (items.IsNull) items = ReadRef(list, "array");
        var size = ReadInt(list, "_size"); if (size <= 0) size = ReadInt(list, "size");
        if (items.IsNull || !items.IsArray || size <= 0) return result;
        var arr = items.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++) { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { } }
        return result;
    }
    // ReadListAll: reads ALL elements in the backing array (arr.Length), not just _size.
    // Used for LightweightExpression.lightweightElements where _size may be stale/wrong,
    // causing truncated CSS reconstruction. Also tries additional backing-array field names
    // in case the collection is an ISSequence (not a standard List<T>).
    static List<ClrObject> ReadListAll(ClrObject list, ClrHeap heap)
    {
        var result = new List<ClrObject>();
        if (list.IsNull) return result;
        var items = ReadRef(list, "_items");
        if (items.IsNull) items = ReadRef(list, "array");
        if (items.IsNull) items = ReadRef(list, "_data");
        if (items.IsNull) items = ReadRef(list, "_buffer");
        if (items.IsNull) items = ReadRef(list, "_elements");
        if (items.IsNull || !items.IsArray) return result;
        var arr = items.AsArray();
        for (int i = 0; i < arr.Length; i++) { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var e = heap.GetObject((ulong)ptr.ToInt64()); if (!e.IsNull) result.Add(e); } catch { } }
        return result;
    }
    static string ReadRefObjName(ClrObject refObj)
    {
        if (refObj.IsNull) return null;
        var refName = ReadStr(refObj, "refName"); if (refName != null) return refName;
        var referedObj = ReadRef(refObj, "referedObject");
        if (!referedObj.IsNull) { var n = ReadStr(referedObj, "_name") ?? ReadStr(referedObj, "name"); if (!string.IsNullOrEmpty(n)) return n; }
        for (var t = refObj.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name != "System.String" && f.Type?.Name?.StartsWith("System.") != true)
                { try { var v = f.ReadObject(refObj.Address, false); if (!v.IsNull) { var n = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(n)) return n; } } catch { } }
        return null;
    }

    // ====================================================================
    //  THEME: CSS reconstruction from LightweightExpression + structured values
    // ====================================================================
    static string ResolveLightweightRef(ClrObject refElem)
    {
        if (refElem.IsNull) return null;
        // Try direct name fields on the reference element itself first
        var directName = ReadStr(refElem, "_name") ?? ReadStr(refElem, "name") ?? ReadStr(refElem, "refName") ?? ReadStr(refElem, "_refName");
        if (!string.IsNullOrEmpty(directName)) return directName;
        // Try all known reference field names (ported from ThemeProbe)
        foreach (var fn in new[] { "reference", "_reference", "referedObject", "_referedObject", "referencedObject", "_target", "target", "value", "_value" })
        {
            var r = ReadRef(refElem, fn);
            if (r.IsNull) continue;
            var n = ReadStr(r, "_name") ?? ReadStr(r, "name") ?? ReadStr(r, "refName");
            if (!string.IsNullOrEmpty(n)) return n;
            // Generic object-name walk: iterate all object-reference fields on the referenced object
            for (var t = r.Type; t != null; t = t.BaseType)
            {
                foreach (var f in t.Fields)
                {
                    if (f.IsObjectReference && f.Type?.Name != "System.String")
                    {
                        try
                        {
                            var v = f.ReadObject(r.Address, false);
                            if (!v.IsNull)
                            {
                                var vn = ReadStr(v, "_name") ?? ReadStr(v, "name");
                                if (!string.IsNullOrEmpty(vn)) return vn;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        // Last resort: try reading any non-empty string field on the reference element
        for (var t = refElem.Type; t != null; t = t.BaseType)
        {
            foreach (var f in t.Fields)
            {
                if (f.IsObjectReference && f.Type?.Name == "System.String")
                {
                    try { var s = f.ReadString(refElem.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { }
                }
            }
        }
        return "?"; // placeholder — never return null (prevents mid-statement CSS truncation)
    }
    static string ReadLightweightCss(ClrObject expr, ClrHeap heap)
    {
        _diagElemCount = 0; _diagArrLen = 0; _diagSize = 0; _diagListType = ""; _diagLastLeaf = "";
        if (expr.IsNull) return null;
        var elements = ReadRef(expr, "lightweightElements");
        if (!elements.IsNull) _diagListType = elements.Type?.Name ?? "?";
        _diagSize = ReadInt(elements, "_size"); if (_diagSize <= 0) _diagSize = ReadInt(elements, "size");
        var items = ReadRef(elements, "_items"); if (items.IsNull) items = ReadRef(elements, "array");
        if (!items.IsNull && items.IsArray) _diagArrLen = items.AsArray().Length;
        var list = ReadListAll(elements, heap);
        _diagElemCount = list.Count;
        if (list.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var el in list)
        {
            var leaf = Leaf(el.Type?.Name ?? "?");
            _diagLastLeaf = leaf;
            if (leaf == "TextElement")
            {
                var text = ReadStr(el, "text") ?? ReadStr(el, "_text") ?? ReadStr(el, "value") ?? ReadStr(el, "_value") ?? ReadStr(el, "Text") ?? ReadStr(el, "Value");
                if (text == null)
                {
                    // Fallback: dump all string fields on this element (ported from ThemeProbe)
                    for (var tt = el.Type; tt != null && text == null; tt = tt.BaseType)
                        foreach (var f in tt.Fields)
                            if (f.IsObjectReference && f.Type?.Name == "System.String")
                            { try { var s = f.ReadString(el.Address, false); if (!string.IsNullOrEmpty(s)) { text = s; } } catch { } }
                }
                if (text != null) sb.Append(text);
            }
            else if (leaf.Contains("Reference"))
            {
                try { var nm = ResolveLightweightRef(el); sb.Append(string.IsNullOrEmpty(nm) ? "?" : nm); }
                catch { sb.Append("?"); }
                // Always append suffix even if reference name failed (prevents mid-statement truncation)
                var suffix = ReadStr(el, "suffix") ?? ReadStr(el, "_suffix");
                if (suffix != null) sb.Append(suffix);
            }
            else
            {
                // unknown element kind: try a text field
                var text = ReadStr(el, "text") ?? ReadStr(el, "value");
                if (text != null) sb.Append(text);
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
    static string ReadStyleSheetCss(ClrObject ss, ClrHeap heap)
    {
        if (ss.IsNull) return null;
        _cssDiag.AppendLine($"  WebStyleSheet type: {ss.Type?.Name ?? "?"}");
        _cssDiag.AppendLine($"    _finalCssSourceValueIsValid = {ReadBool(ss, "_finalCssSourceValueIsValid")}");
        // Pick the LONGEST non-empty result across CSS sources, but guard cached sources
        // that may be PARTIAL. _finalCssSource is a lazily-computed cache; when its
        // _finalCssSourceValueIsValid flag is false the list is incomplete and using it
        // causes mid-statement truncation (a longer-but-partial cache beats the complete
        // _cssSource under "pick longest"). Prefer the always-complete original sources.
        string best = null;
        string bestSrc = "(none)";
        // Traditional WebStyleSheet — full CSS string
        var dtv = ReadStr(ss, "designTimeValue");
        if (!string.IsNullOrEmpty(dtv)) { _cssDiag.AppendLine($"    designTimeValue: {dtv.Length} chars"); if (best == null || dtv.Length > best.Length) { best = dtv; bestSrc = "designTimeValue"; } }
        else _cssDiag.AppendLine($"    designTimeValue: null/empty");
        // Original LightweightExpression sources (always complete) — try FIRST
        foreach (var fn in new[] { "_cssSource", "_userCssSource" })
        {
            var expr = ReadRef(ss, fn);
            var css = ReadLightweightCss(expr, heap);
            var len = css?.Length ?? 0;
            _cssDiag.AppendLine($"    {fn}: exprNull={expr.IsNull} listType={_diagListType} _size={_diagSize} arrLen={_diagArrLen} elemCount={_diagElemCount} lastLeaf={_diagLastLeaf} cssLen={len}");
            if (!string.IsNullOrEmpty(css) && (best == null || css.Length > best.Length)) { best = css; bestSrc = fn; }
        }
        // Cached _finalCssSource — only trust if the validity flag is set
        if (ReadBool(ss, "_finalCssSourceValueIsValid"))
        {
            var expr = ReadRef(ss, "_finalCssSource");
            var css = ReadLightweightCss(expr, heap);
            var len = css?.Length ?? 0;
            _cssDiag.AppendLine($"    _finalCssSource (valid): listType={_diagListType} _size={_diagSize} arrLen={_diagArrLen} elemCount={_diagElemCount} lastLeaf={_diagLastLeaf} cssLen={len}");
            if (!string.IsNullOrEmpty(css) && (best == null || css.Length > best.Length)) { best = css; bestSrc = "_finalCssSource"; }
        }
        else _cssDiag.AppendLine($"    _finalCssSource: SKIPPED (validity=false)");
        // _generatedCssSource — no known validity flag, use as a last resort
        {
            var expr = ReadRef(ss, "_generatedCssSource");
            var genCss = ReadLightweightCss(expr, heap);
            var len = genCss?.Length ?? 0;
            _cssDiag.AppendLine($"    _generatedCssSource: exprNull={expr.IsNull} listType={_diagListType} _size={_diagSize} arrLen={_diagArrLen} elemCount={_diagElemCount} lastLeaf={_diagLastLeaf} cssLen={len}");
            if (!string.IsNullOrEmpty(genCss) && (best == null || genCss.Length > best.Length)) { best = genCss; bestSrc = "_generatedCssSource"; }
        }
        _cssDiag.AppendLine($"    => SELECTED: {bestSrc} ({best?.Length ?? 0} chars)");
        return best;
    }
    static string Cap(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        const int max = 500000;
        return s.Length <= max ? s : s.Substring(0, max) + $"\n/* ... truncated ({s.Length} chars total) ... */";
    }
    // Generic model-object -> JSON (bounded depth, skip boilerplate)
    static JsonNode ToJson(ClrObject o, ClrHeap heap, HashSet<ulong> seen, int depth, int maxDepth)
    {
        if (o.IsNull) return null;
        var tn = Leaf(o.Type?.Name ?? "?");
        var nm = ReadStr(o, "_name") ?? ReadStr(o, "name");
        if (depth >= maxDepth) return nm ?? tn;
        if (!seen.Add(o.Address)) return nm ?? tn;
        var obj = new JsonObject { ["_type"] = tn };
        if (nm != null) obj["_name"] = nm;
        var fieldSeen = new HashSet<string>();
        for (var t = o.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
            {
                if (!fieldSeen.Add(f.Name)) continue;
                if (SkipFields.Contains(f.Name)) continue;
                var ftn = f.Type?.Name ?? "?";
                try
                {
                    if (f.IsObjectReference)
                    {
                        if (ftn == "System.String") { var s = f.ReadString(o.Address, false); obj[f.Name] = s; }
                        else { var v = f.ReadObject(o.Address, false); obj[f.Name] = ToJson(v, heap, seen, depth + 1, maxDepth); }
                    }
                    else if (f.IsValueType) { try { obj[f.Name] = f.Read<int>(o.Address, false); } catch { } }
                }
                catch { }
            }
        seen.Remove(o.Address);
        return obj;
    }
    static JsonObject BuildThemeJson(ClrObject th, ClrHeap heap)
    {
        var o = new JsonObject { ["name"] = ReadStr(th, "_name") ?? "(unnamed)", ["description"] = ReadStr(th, "_description"), ["public"] = ReadBool(th, "_public") };
        var baseT = ReadRef(th, "_baseTheme");
        o["baseTheme"] = baseT.IsNull ? null : (ReadStr(baseT, "_name") ?? Leaf(baseT.Type?.Name ?? "?"));
        var grid = new JsonObject {
            ["useGrid"] = ReadInt(th, "_useGrid"),
            ["gridColumns"] = ReadInt(th, "_gridColumns"),
            ["columnWidth"] = ReadInt(th, "_columnWidth"),
            ["gutterWidth"] = ReadInt(th, "_gutterWidth"),
            ["gridWidth"] = ReadInt(th, "_gridWidth"),
            ["gutterPercentage"] = ReadInt(th, "_gutterPercentage"),
            ["minWidth"] = ReadNullableInt(th, "_minWidth"),
            ["maxWidth"] = ReadNullableInt(th, "_maxWidth")
        };
        o["grid"] = grid;
        var layout = new JsonObject();
        foreach (var lf in new[] { "_normalPageLayout", "_headerWebBlock", "_menuWebBlock", "_footerWebBlock" })
        { var lb = ReadRef(th, lf); layout[lf.Substring(1)] = lb.IsNull ? null : (ReadStr(lb, "_name") ?? ReadStr(lb, "name") ?? Leaf(lb.Type?.Name ?? "?")); }
        o["layoutBlocks"] = layout;
        var tvArr = new JsonArray();
        foreach (var tv in ReadColl(ReadRef(th, "_themeValues"), heap))
        { try { tvArr.Add(ToJson(tv, heap, new HashSet<ulong>(), 0, 3)); } catch { } }
        o["themeValues"] = tvArr;
        return o;
    }
    static string WriteThemes(Ctx ctx, string moduleDir)
    {
        var heap = ctx.Heap;
        _cssDiag.Clear();
        _cssDiag.AppendLine($"CSS Diagnostics for module: {ctx.ModuleName} (pid={ctx.Pid})");
        _cssDiag.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _cssDiag.AppendLine();
        // One <ThemeName>.css file per theme (main stylesheet + invisible stylesheet with separator).
        var files = new List<string>();
        foreach (var th in ctx.Themes)
        {
            var nm = ReadStr(th, "_name") ?? "(unnamed)";
            var baseT = ReadRef(th, "_baseTheme");
            var cssOut = new StringBuilder();
            _cssDiag.AppendLine($"=== Theme: {nm} ===");
            _cssDiag.AppendLine($"  -- _styleSheet (main) --");
            var mainCss = ReadStyleSheetCss(ReadRef(th, "_styleSheet"), heap);
            if (!string.IsNullOrEmpty(mainCss))
            {
                cssOut.AppendLine("/* ============================================================");
                cssOut.AppendLine($"   Theme: {nm}  (base: {(baseT.IsNull ? "(none)" : ReadStr(baseT, "_name") ?? "?")})");
                cssOut.AppendLine("   ============================================================ */");
                cssOut.AppendLine(Cap(mainCss));
                cssOut.AppendLine();
            }
            var iss = ReadRef(th, "_invisibleStyleSheet");
            _cssDiag.AppendLine($"  -- _invisibleStyleSheet --");
            var icss = ReadStyleSheetCss(iss, heap);
            if (!string.IsNullOrEmpty(icss))
            {
                cssOut.AppendLine("/* ============================================================");
                cssOut.AppendLine($"   Theme: {nm}  (invisible stylesheet)");
                cssOut.AppendLine("   ============================================================ */");
                cssOut.AppendLine(Cap(icss));
                cssOut.AppendLine();
            }
            if (cssOut.Length > 0)
            {
                var fileName = SafeName(nm) + ".css";
                File.WriteAllText(Path.Combine(moduleDir, fileName), cssOut.ToString());
                files.Add($"{fileName} ({cssOut.Length:N0} chars)");
            }
        }

        // theme-values.json
        var tv = new JsonObject { ["module"] = ctx.ModuleName, ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = ctx.Pid }, ["themes"] = new JsonArray(ctx.Themes.Select(th => BuildThemeJson(th, heap)).ToArray()) };
        WriteJson(Path.Combine(moduleDir, "theme-values.json"), tv);
        File.WriteAllText(Path.Combine(moduleDir, "css-diag.txt"), _cssDiag.ToString());
        return files.Count > 0
            ? $"extract_themes -> {moduleDir}\\{string.Join($", {moduleDir}\\", files)}; theme-values.json ({ctx.Themes.Count} theme(s))"
            : $"extract_themes -> no theme CSS found; theme-values.json ({ctx.Themes.Count} theme(s))";
    }

    // ====================================================================
    //  SCREENS / WEB BLOCKS metadata
    // ====================================================================
    static string ReadType(ClrObject obj)
    {
        var typeObj = ReadRef(obj, "_type"); if (typeObj.IsNull) return null;
        return ReadStr(typeObj, "_name") ?? Leaf(typeObj.Type?.Name ?? "Unknown");
    }
    static string ReadDestinationName(ClrObject dest)
    {
        if (dest.IsNull) return null;
        var n = ReadStr(dest, "_name") ?? ReadStr(dest, "name"); if (!string.IsNullOrEmpty(n)) return n;
        return ReadRefObjName(dest);
    }
    static JsonNode ReadParam(ClrObject p, string direction, ClrHeap heap) => new JsonObject
    {
        ["name"] = ReadStr(p, "_name") ?? "(unnamed)",
        ["dataType"] = ReadType(p),
        ["direction"] = direction,
        ["defaultValue"] = ReadExpressionText(ReadRef(p, "_defaultValue"), heap),
        ["isMandatory"] = ReadBool(p, "_isMandatory"),
        ["description"] = ReadStr(p, "_description")
    };
    static JsonArray ReadParams(ClrObject coll, string direction, ClrHeap heap) { var arr = new JsonArray(); foreach (var p in ReadColl(coll, heap)) arr.Add(ReadParam(p, direction, heap)); return arr; }
    static JsonArray ReadLocalVariables(ClrObject coll, ClrHeap heap)
    { var arr = new JsonArray(); foreach (var v in ReadColl(coll, heap)) arr.Add(new JsonObject { ["name"] = ReadStr(v, "_name") ?? "(unnamed)", ["dataType"] = ReadType(v), ["kind"] = Leaf(v.Type?.Name ?? "?") }); return arr; }
    static JsonArray ReadEvents(ClrObject sb, ClrHeap heap)
    {
        var evArr = new JsonArray();
        void AddLifecycle(string fieldName, string eventName) { var slot = ReadRef(sb, fieldName); if (slot.IsNull) return; var handler = ReadStr(slot, "_name") ?? ReadDestinationName(ReadRef(slot, "_destination")); evArr.Add(new JsonObject { ["name"] = eventName, ["kind"] = "System", ["handler"] = handler }); }
        AddLifecycle("_onInitialize", "OnInitialize"); AddLifecycle("_onReady", "OnReady"); AddLifecycle("_onRender", "OnRender"); AddLifecycle("_onDestroy", "OnDestroy"); AddLifecycle("_onParametersChanged", "OnParametersChanged");
        foreach (var ce in ReadColl(ReadRef(sb, "_customEvents"), heap))
            evArr.Add(new JsonObject { ["name"] = ReadStr(ce, "_name") ?? ReadStr(ce, "_eventName") ?? "(custom event)", ["kind"] = "Custom", ["handler"] = ReadDestinationName(ReadRef(ce, "_destination")) });
        return evArr;
    }
    static JsonObject BuildMetadataEntry(ClrObject sb, string kind, ClrHeap heap)
    {
        var o = new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["type"] = kind };
        o["description"] = ReadStr(sb, "_description");
        o["urlPath"] = ReadStr(sb, "_pageName") ?? ReadStr(sb, "_url");
        o["isPublic"] = ReadBool(sb, "_public");
        var permArr = new JsonArray();
        foreach (var perm in ReadColl(ReadRef(sb, "_permissions"), heap)) { var role = ReadRef(perm, "_role"); var rn = role.IsNull ? ReadRefObjName(ReadRef(perm, "_referedObject")) : (ReadStr(role, "_name") ?? ReadRefObjName(role)); if (!string.IsNullOrEmpty(rn)) permArr.Add(rn); }
        o["permissions"] = permArr;
        o["inputParameters"] = ReadParams(ReadRef(sb, "_inputParameters"), "In", heap);
        o["outputParameters"] = ReadParams(ReadRef(sb, "_outputParameters"), "Out", heap);
        o["localVariables"] = ReadLocalVariables(ReadRef(sb, "_localVariables"), heap);
        o["events"] = ReadEvents(sb, heap);
        return o;
    }
    static string WriteScreens(Ctx ctx, string moduleDir)
    {
        var arr = new JsonArray();
        foreach (var sb in ctx.Screens.OrderBy(x => ReadStr(x, "_name") ?? "")) { try { arr.Add(BuildMetadataEntry(sb, "Screen", ctx.Heap)); } catch (Exception ex) { arr.Add(new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["error"] = ex.Message }); } }
        var o = new JsonObject { ["module"] = ctx.ModuleName, ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = ctx.Pid }, ["screens"] = arr };
        WriteJson(Path.Combine(moduleDir, "screens.json"), o);
        return $"extract_screens -> {moduleDir}\\screens.json ({ctx.Screens.Count} screen(s))";
    }
    static string WriteWebBlocks(Ctx ctx, string moduleDir)
    {
        var arr = new JsonArray();
        foreach (var sb in ctx.Blocks.OrderBy(x => ReadStr(x, "_name") ?? "")) { try { arr.Add(BuildMetadataEntry(sb, "WebBlock", ctx.Heap)); } catch (Exception ex) { arr.Add(new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["error"] = ex.Message }); } }
        var o = new JsonObject { ["module"] = ctx.ModuleName, ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = ctx.Pid }, ["webBlocks"] = arr };
        WriteJson(Path.Combine(moduleDir, "web-blocks.json"), o);
        return $"extract_web_blocks -> {moduleDir}\\web-blocks.json ({ctx.Blocks.Count} web block(s))";
    }

    // ====================================================================
    //  UI TREE
    // ====================================================================
    static string WidgetTypeLabel(string leaf) => leaf switch { "CustomPlaceholderWidget" => "Container", "WebBlockInstance" => "Block", "PlaceholderArgument" => "Placeholder", "IfBranch" => "IfBranch", _ => leaf };
    static string WidgetDisplayName(ClrObject w, out string htmlClass)
    {
        var leaf = Leaf(w.Type?.Name ?? "?");
        var name = ReadStr(w, "_name") ?? ReadStr(w, "_customName");
        var type = WidgetTypeLabel(leaf);
        if (leaf == "PlaceholderArgument") { var ph = ReadRef(w, "_placeholder"); name = name ?? (ph.IsNull ? null : ReadStr(ph, "_name")); }
        else if (leaf == "WebBlockInstance") { var src = ReadRef(w, "_sourceWebBlock"); name = src.IsNull ? name : (ReadStr(src, "_name") ?? ReadRefObjName(src)); }
        htmlClass = ReadStr(w, "_customStyle");
        return string.IsNullOrEmpty(name) ? $"({type})" : $"{name} ({type})";
    }
    static string WidgetEventLabel(ClrObject ev)
    {
        var leaf = Leaf(ev.Type?.Name ?? "?");
        var evName = ReadStr(ev, "_name") ?? ReadStr(ev, "_eventName");
        if (string.IsNullOrEmpty(evName)) evName = leaf == "OnClick" ? "OnClick" : leaf;
        var handler = ReadDestinationName(ReadRef(ev, "_destination"));
        return string.IsNullOrEmpty(handler) ? evName : $"{evName}={handler}";
    }
    static void RenderWidgets(List<ClrObject> widgets, string prefix, StringBuilder sb, Ctx ctx)
    {
        for (int i = 0; i < widgets.Count; i++)
        {
            var last = i == widgets.Count - 1;
            var branch = last ? "\u2514\u2500 " : "\u251C\u2500 ";
            var label = WidgetDisplayName(widgets[i], out var cls);
            var classPart = string.IsNullOrEmpty(cls) ? "" : (cls.IndexOf(':') >= 0 || cls.IndexOf(';') >= 0 ? $" [style: {cls}]" : " ." + cls.Replace(" ", " ."));
            // Show widget events (OnClick, OnChange, etc.) from EventsByWidget
            var eventPart = "";
            if (ctx.EventsByWidget.TryGetValue(widgets[i].Address, out var evs) && evs.Count > 0)
            {
                var evLabels = evs.Select(e => { try { return WidgetEventLabel(e); } catch { return "?"; } }).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (evLabels.Length > 0) eventPart = " [" + string.Join(", ", evLabels) + "]";
            }
            sb.AppendLine(prefix + branch + label + classPart + eventPart);
            var kids = ctx.WidgetChildren.TryGetValue(widgets[i].Address, out var c) ? c : new List<ClrObject>();
            if (kids.Count == 0) kids = ReadColl(ReadRef(widgets[i], "_childWidgets"), ctx.Heap);
            RenderWidgets(kids.OrderBy(x => ReadStr(x, "_name") ?? "").ThenBy(x => Leaf(x.Type?.Name ?? "?")).ToList(), prefix + (last ? "   " : "\u2502  "), sb, ctx);
        }
    }
    static void RenderScreenOrBlock(ClrObject sb, string kind, StringBuilder treeOut, Ctx ctx)
    {
        var nm = ReadStr(sb, "_name") ?? "(unnamed)";
        treeOut.AppendLine(new string('=', 60)); treeOut.AppendLine($"{kind}: {nm}"); treeOut.AppendLine(new string('=', 60));
        // Lifecycle events
        var lifecycleEvents = new List<string>();
        foreach (var lf in new[] { ("_onInitialize", "OnInitialize"), ("_onReady", "OnReady"), ("_onRender", "OnRender"), ("_onDestroy", "OnDestroy"), ("_onParametersChanged", "OnParametersChanged") })
        {
            var slot = ReadRef(sb, lf.Item1);
            if (!slot.IsNull)
            {
                var handler = ReadStr(slot, "_name") ?? ReadDestinationName(ReadRef(slot, "_destination"));
                lifecycleEvents.Add(string.IsNullOrEmpty(handler) ? lf.Item2 : $"{lf.Item2}={handler}");
            }
        }
        // Custom events
        foreach (var ce in ReadColl(ReadRef(sb, "_customEvents"), ctx.Heap))
        {
            var ceName = ReadStr(ce, "_name") ?? ReadStr(ce, "_eventName") ?? "(custom event)";
            var handler = ReadDestinationName(ReadRef(ce, "_destination"));
            lifecycleEvents.Add(string.IsNullOrEmpty(handler) ? ceName : $"{ceName}={handler}");
        }
        if (lifecycleEvents.Count > 0) treeOut.AppendLine($"Events: {string.Join(", ", lifecycleEvents)}");
        // Block/screen's own stylesheet CSS
        if (ctx.StyleSheetsByParent.TryGetValue(sb.Address, out var styleSheets) && styleSheets.Count > 0)
        {
            var cssParts = new List<string>();
            foreach (var ss in styleSheets)
            {
                var css = ReadStyleSheetCss(ss, ctx.Heap);
                if (!string.IsNullOrEmpty(css)) cssParts.Add(css);
            }
            if (cssParts.Count > 0)
            {
                treeOut.AppendLine("CSS:");
                treeOut.AppendLine(string.Join("\n", cssParts));
            }
        }
        var top = ReadColl(ReadRef(sb, "_widgets"), ctx.Heap);
        if (top.Count == 0) treeOut.AppendLine("  (empty)"); else RenderWidgets(top, "", treeOut, ctx);
        treeOut.AppendLine();
    }
    static string WriteUiTree(Ctx ctx, string moduleDir)
    {
        var screens = new StringBuilder();
        foreach (var sb in ctx.Screens.OrderBy(x => ReadStr(x, "_name") ?? "")) RenderScreenOrBlock(sb, "SCREEN", screens, ctx);
        File.WriteAllText(Path.Combine(moduleDir, "ui-tree-screens.txt"), screens.ToString());
        var blocks = new StringBuilder();
        foreach (var sb in ctx.Blocks.OrderBy(x => ReadStr(x, "_name") ?? "")) RenderScreenOrBlock(sb, "WEB BLOCK", blocks, ctx);
        File.WriteAllText(Path.Combine(moduleDir, "ui-tree-blocks.txt"), blocks.ToString());
        return $"extract_ui_tree -> {moduleDir}\\ui-tree-screens.txt ({ctx.Screens.Count}), ui-tree-blocks.txt ({ctx.Blocks.Count})";
    }

    // ====================================================================
    //  CLIENT ACTIONS (flow tracing)
    // ====================================================================
    static string ReadElementText(ClrObject elem, ClrHeap heap, int depth = 0)
    {
        if (elem.IsNull || depth > 6) return "?";
        var elemType = Leaf(elem.Type?.Name ?? "?");
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
            case "NullLiteral": return "Null";
            case "DefaultLiteral": return "Default";
            case "Identifier": { var r = ReadRef(elem, "reference"); if (!r.IsNull) { var n = ReadRefObjName(r); if (n != null) return n; } break; }
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
                var left = ReadElementText(ReadRef(elem, "leftSide"), heap, depth + 1); var right = ReadElementText(ReadRef(elem, "rightSide"), heap, depth + 1);
                var opVal = ReadInt(elem, "operator"); var op = opVal switch { 0 => "+", 1 => "-", 2 => "*", 3 => "/", 4 => "mod", 5 => "=", 6 => "<>", 7 => "<", 8 => "<=", 9 => ">", 10 => ">=", 11 => "and", 12 => "or", 13 => "&", _ => $"op{opVal}" };
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
        }
        for (var t = elem.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name == "System.String")
                { try { var s = f.ReadString(elem.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 500) return s; } catch { } }
        return elemType;
    }
    static string ReadExpressionText(ClrObject parsedExpr, ClrHeap heap)
    {
        if (parsedExpr.IsNull) return null;
        var elem = ReadRef(parsedExpr, "expressionElement"); if (elem.IsNull) return null;
        return ReadElementText(elem, heap);
    }
    static JsonArray TraceFlow(ClrObject action, Ctx ctx)
    {
        var arr = new JsonArray();
        var heap = ctx.Heap;
        List<ClrObject> nodes;
        if (!ctx.NodesByParent.TryGetValue(action.Address, out nodes) || nodes.Count == 0) { nodes = new List<ClrObject>(); nodes.AddRange(ReadColl(ReadRef(action, "_nodesShownInESpaceTree"), heap)); nodes.AddRange(ReadColl(ReadRef(action, "_nodesNotShownInESpaceTree"), heap)); }
        if (nodes.Count == 0) return arr;
        var nodeDict = nodes.ToDictionary(n => n.Address, n => n);
        var visited = new HashSet<ulong>(); int counter = 1;
        string NType(ulong a) => nodeDict.TryGetValue(a, out var n) ? Leaf(n.Type?.Name ?? "?") : "?";
        string NName(ulong a) => nodeDict.TryGetValue(a, out var n) ? (ReadStr(n, "_name") ?? ReadStr(n, "_customName")) : null;
        string NAction(ulong a) { if (!nodeDict.TryGetValue(a, out var n)) return null; var nt = Leaf(n.Type?.Name ?? "?"); if (nt == "ExecuteAction" || nt == "ExecuteClientAction") { var ac = ReadRef(n, "_action"); if (!ac.IsNull) return ReadStr(ac, "_name") ?? ReadRefObjName(ac); var ca = ReadRef(n, "_clientAction"); return ca.IsNull ? null : (ReadStr(ca, "_name") ?? ReadRefObjName(ca)); } return null; }
        List<string> NAssign(ulong a) { var list = new List<string>(); if (!nodeDict.TryGetValue(a, out var n)) return list; if (Leaf(n.Type?.Name ?? "?") == "Assign") foreach (var asg in ReadColl(ReadRef(n, "_assignments"), heap)) list.Add($"{ReadExpressionText(ReadRef(asg, "_variable"), heap) ?? "?"} = {ReadExpressionText(ReadRef(asg, "_value"), heap) ?? "?"}"); return list; }
        string NDetails(ulong a) { if (!nodeDict.TryGetValue(a, out var n)) return null; var nt = Leaf(n.Type?.Name ?? "?"); if (nt == "Comment") return ReadStr(n, "_text"); if (nt == "JavascriptNode" || nt == "JSNode") return ReadStr(n, "_sourceCode") ?? ReadStr(n, "_javascript") ?? ReadStr(n, "_script") ?? ReadStr(n, "_body"); if (nt == "If") { var cond = ReadExpressionText(ReadRef(n, "_condition"), heap); return string.IsNullOrEmpty(cond) ? null : $"condition: {cond}"; } if (nt == "Switch") { var conds = ReadColl(ReadRef(n, "_conditions"), heap); var parts = new List<string>(); foreach (var c in conds) { var t = ReadExpressionText(ReadRef(c, "_expressionElement"), heap); if (!string.IsNullOrEmpty(t)) parts.Add(t); } return parts.Count == 0 ? null : $"condition: {string.Join(" | ", parts)}"; } return null; }
        void Trace(ulong addr, int indent, string label) { if (visited.Contains(addr)) { arr.Add(Step(counter++, indent, label, NType(addr), "(loops back)", null, null, null)); return; } visited.Add(addr); if (!nodeDict.TryGetValue(addr, out var node)) return; arr.Add(Step(counter++, indent, label, Leaf(node.Type?.Name ?? "?"), NName(addr), NAction(addr), NAssign(addr), NDetails(addr))); if (!ctx.LinksBySource.TryGetValue(addr, out var links) || links.Count == 0) return; foreach (var lk in links.Where(l => l.linkType == "Cycle")) { arr.Add(Step(counter++, indent, "Cycle", "Cycle", null, null, null, null)); Trace(lk.target, indent + 1, ""); } foreach (var lk in links.Where(l => l.linkType == "Condition")) { arr.Add(Step(counter++, indent, "Condition", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); } foreach (var lk in links.Where(l => l.linkType == "Otherwise")) { arr.Add(Step(counter++, indent, "Otherwise", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); } foreach (var lk in links.Where(l => l.linkType == "True")) { arr.Add(Step(counter++, indent, "True", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); } foreach (var lk in links.Where(l => l.linkType == "False")) { arr.Add(Step(counter++, indent, "False", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); } foreach (var lk in links.Where(l => l.linkType == "Sequence")) Trace(lk.target, indent, ""); }
        var start = nodes.FirstOrDefault(n => Leaf(n.Type?.Name ?? "?") == "Start");
        if (!start.IsNull) Trace(start.Address, 0, null); else arr.Add(Step(counter++, 0, null, "Start", "(no Start node found)", null, null, null));
        foreach (var n in nodes.Where(n => !visited.Contains(n.Address))) arr.Add(Step(counter++, 1, "Unvisited", Leaf(n.Type?.Name ?? "?"), NName(n.Address), NAction(n.Address), NAssign(n.Address), NDetails(n.Address)));
        return arr;
    }
    static JsonObject Step(int step, int indent, string label, string nodeType, string name, string actionRef, List<string> assignments, string details)
    {
        var o = new JsonObject { ["step"] = step, ["indent"] = indent, ["nodeType"] = nodeType };
        o["label"] = label; o["name"] = name; o["actionRef"] = actionRef;
        var aa = new JsonArray(); if (assignments != null) foreach (var a in assignments) aa.Add(a);
        o["assignments"] = aa; o["details"] = details; return o;
    }
    static JsonObject BuildClientActionEntry(ClrObject ca, Ctx ctx) => new()
    {
        ["name"] = ReadStr(ca, "_name") ?? "(unnamed)",
        ["description"] = ReadStr(ca, "_description"),
        ["inputs"] = ReadParams(ReadRef(ca, "_inputParameters"), "In", ctx.Heap),
        ["outputs"] = ReadParams(ReadRef(ca, "_outputParameters"), "Out", ctx.Heap),
        ["localVariables"] = ReadLocalVariables(ReadRef(ca, "_localVariables"), ctx.Heap),
        ["flow"] = TraceFlow(ca, ctx)
    };
    static JsonArray BuildClientActionsFor(ClrObject sb, Ctx ctx) { var arr = new JsonArray(); if (ctx.ClientActionsByParent.TryGetValue(sb.Address, out var owned)) foreach (var ca in owned.OrderBy(x => ReadStr(x, "_name") ?? "")) arr.Add(BuildClientActionEntry(ca, ctx)); return arr; }
    static JsonArray ActionsArray(List<ClrObject> list, Ctx ctx) { var arr = new JsonArray(); foreach (var sb in list.OrderBy(x => ReadStr(x, "_name") ?? "")) arr.Add(new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["clientActions"] = BuildClientActionsFor(sb, ctx) }); return arr; }
    static string WriteClientActions(Ctx ctx, string moduleDir)
    {
        var o = new JsonObject { ["module"] = ctx.ModuleName, ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = ctx.Pid }, ["screens"] = ActionsArray(ctx.Screens, ctx), ["webBlocks"] = ActionsArray(ctx.Blocks, ctx) };
        // split into two files
        var sc = new JsonObject { ["module"] = ctx.ModuleName, ["screens"] = ActionsArray(ctx.Screens, ctx) };
        WriteJson(Path.Combine(moduleDir, "client-actions-screens.json"), sc);
        var bl = new JsonObject { ["module"] = ctx.ModuleName, ["webBlocks"] = ActionsArray(ctx.Blocks, ctx) };
        WriteJson(Path.Combine(moduleDir, "client-actions-blocks.json"), bl);
        int total = ctx.ClientActionsByParent.Values.Sum(l => l.Count);
        return $"extract_client_actions -> client-actions-screens.json + client-actions-blocks.json ({total} client action(s))";
    }

    // ====================================================================
    //  Umbrella + summary
    // ====================================================================
    static string WriteAll(Ctx ctx, string moduleDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine(WriteThemes(ctx, moduleDir));
        sb.AppendLine(WriteScreens(ctx, moduleDir));
        sb.AppendLine(WriteWebBlocks(ctx, moduleDir));
        sb.AppendLine(WriteUiTree(ctx, moduleDir));
        sb.AppendLine(WriteClientActions(ctx, moduleDir));
        var summary = new JsonObject {
            ["module"] = ctx.ModuleName, ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = ctx.Pid },
            ["counts"] = new JsonObject { ["themes"] = ctx.Themes.Count, ["screens"] = ctx.Screens.Count, ["webBlocks"] = ctx.Blocks.Count, ["styleSheets"] = ctx.StyleSheets.Count, ["styleSheetsByParent"] = ctx.StyleSheetsByParent.Count, ["widgets"] = ctx.WidgetsByAddr.Count, ["widgetEvents"] = ctx.EventsByWidget.Values.Sum(l => l.Count), ["clientActions"] = ctx.ClientActionsByParent.Values.Sum(l => l.Count) } };
        WriteJson(Path.Combine(moduleDir, "summary.json"), summary);
        sb.AppendLine($"summary.json written. Output dir: {moduleDir}");
        return sb.ToString();
    }

    static void WriteJson(string path, JsonNode node)
    {
        using var ms = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            node.WriteTo(writer);
        File.WriteAllBytes(path, ms.ToArray());
    }
}
