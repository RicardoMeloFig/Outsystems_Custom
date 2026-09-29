// BridgeHost - the in-process named-pipe server. Started once from the plugin
// descriptor ctor. Protocol: newline-delimited JSON (one request per line, one
// response per line). Commands (spike):
//   ping                                   -> pong + pid + pipe name
//   list_modules                           -> names from PluginProvider.ModelServices.LoadedESpaces
//   module_info {module}                   -> Name/Kind/counts (proves deep live read)
//   try_create_service_action {module,name}-> 2A mutation probe: IESpace.CreateServiceAction(name, NewKey())
// All model access is via reflection->dynamic so the plugin binds to the EXACT live
// types in SS's load context (no compile-time reference to OutSystems.Model.V1 needed,
// avoiding ALC/type-identity pitfalls). Read/mutate are wrapped so exceptions are
// reported back to the client (the spike learns the required lock sequence from them).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using ServiceStudio.PluginAPI;

namespace OsLiveBridge;

internal static class BridgeHost
{
    static int _started;
    static readonly string PipeName = "OsLiveBridge-" + Process.GetCurrentProcess().Id;

    public static void EnsureStarted()
    {
        Log("EnsureStarted called (thread=" + Thread.CurrentThread.ManagedThreadId + " isBg=" + Thread.CurrentThread.IsBackground + ")");
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) { Log("EnsureStarted: already started"); return; }
        var t = new Thread(Run) { IsBackground = true, Name = "OsLiveBridge-pipe" };
        t.Start();
        Log("EnsureStarted: pipe thread started");
    }

    static void Run()
    {
        Log("bridge starting; pipe=" + PipeName + " pid=" + Process.GetCurrentProcess().Id);
        while (true)
        {
            NamedPipeServerStream server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                server.WaitForConnection();
                Log("connection accepted");
                HandleClient(server);
            }
            catch (Exception e) { Log("server loop err: " + e); Thread.Sleep(500); }
            finally { try { server?.Dispose(); Log("server disposed"); } catch { } }
        }
    }

    static void HandleClient(NamedPipeServerStream s)
    {
        var enc = new UTF8Encoding(false);
        using var reader = new StreamReader(s, enc, false);
        using var writer = new StreamWriter(s, enc) { AutoFlush = true };
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            Log("read: " + (line.Length > 200 ? line.Substring(0, 200) + "..." : line));
            string resp;
            try { resp = Dispatch(line); }
            catch (Exception e) { resp = Json(new { ok = false, error = e.GetType().Name + ": " + e.Message }); }
            try { writer.WriteLine(resp); writer.Flush(); Log("wrote resp len=" + resp.Length); }
            catch (Exception we) { Log("write err: " + we.Message); break; }
        }
        Log("HandleClient exit (client disconnected)");
    }

    static string Dispatch(string reqLine)
    {
        using var doc = JsonDocument.Parse(reqLine);
        var root = doc.RootElement;
        string cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() : "";
        switch (cmd)
        {
            case "ping": return Json(new { ok = true, pong = "OsLiveBridge v0.1-spike", pid = Process.GetCurrentProcess().Id, pipe = PipeName });
            case "list_modules": return ListModules();
            case "module_info": return ModuleInfo(GetStr(root, "module"));
            // The command-opener: Command.ExecuteFromAsyncCode (found by decompiling
            // ServiceStudio.Framework.dll). UndoManager guards check GetCurrentCommand(espace)==null;
            // Command.Execute opens a command; ExecuteInContext does NOT.
            case "try_create_v7": return TryCreateV7(GetStr(root, "module"), GetStr(root, "name"), root.TryGetProperty("withSkeleton", out var ws) && ws.GetBoolean());
            case "try_create_server_action": return TryCreateServerAction(GetStr(root, "module"), GetStr(root, "name"));
            case "try_create_client_action": return TryCreateClientAction(GetStr(root, "module"), GetStr(root, "name"));
            case "try_create_screen_client_action": return TryCreateScreenClientAction(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"));
            case "try_clone_v8": return TryCloneV8(GetStr(root, "module"), GetStr(root, "source"), GetStr(root, "name"));
            case "clone_server_action": return CloneServerAction(GetStr(root, "module"), GetStr(root, "source"), GetStr(root, "name"));
            case "clone_client_action": return CloneClientAction(GetStr(root, "module"), GetStr(root, "source"), GetStr(root, "name"));
            // Generic live flow-editing primitives (compose any edit from the MCP - no recompile/restart):
            case "list_flow": return ListFlow(GetStr(root, "module"), GetStr(root, "action"));
            case "set_assign": return SetAssign(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "matchValue"), GetStr(root, "newValue"), GetStr(root, "matchVar"));
            case "add_output": return AddOutput(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "type"));
            case "add_assign": return AddAssign(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "var"), GetStr(root, "value"), GetStr(root, "where"), GetStr(root, "anchorVar"), GetStr(root, "anchorValue"), root.TryGetProperty("afterNodeIndex", out var aai) && aai.ValueKind == JsonValueKind.Number ? aai.GetInt32() : -1);
            case "del_node": return DelNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "matchVar"), GetStr(root, "matchValue"));
            case "del_node_by_index": { var idx = root.GetProperty("nodeIndex").GetInt32(); return DelNodeByIndex(GetStr(root, "module"), GetStr(root, "action"), idx); }
            case "set_node_target": { var ni = root.GetProperty("nodeIndex").GetInt32(); var ti = root.GetProperty("targetIndex").GetInt32(); return SetNodeTarget(GetStr(root, "module"), GetStr(root, "action"), ni, ti); }
            case "set_node_prop": return SetNodeProp(GetStr(root, "module"), GetStr(root, "action"), root.GetProperty("nodeIndex").GetInt32(), GetStr(root, "propName"), GetStr(root, "propValue"));
            case "add_input": return AddInput(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "type"));
            case "add_local_variable": return AddLocalVariable(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "type"));
            case "add_end_node": return AddEndNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "where"));
            case "set_exception_handler": return SetExceptionHandler(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "endVar"), GetStr(root, "endValue"), GetStr(root, "handlerVar"), GetStr(root, "handlerValue"));
            case "set_start_exception_handler": return SetStartExceptionHandler(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "handlerVar"), GetStr(root, "handlerValue"));
            case "probe_node_types": return ProbeNodeTypes(GetStr(root, "module"), GetStr(root, "action"));
            case "probe_all_node_types": return ProbeAllNodeTypes();
            case "debug_create_node": return DebugCreateNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "nodeInterface"));
            case "add_action_call": return AddActionCall(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "where"), GetStr(root, "anchorVar"), GetStr(root, "anchorValue"), GetStr(root, "serverActionName"), GetStr(root, "producerModule"), root.TryGetProperty("afterNodeIndex", out var ani) && ani.ValueKind == JsonValueKind.Number ? ani.GetInt32() : -1);
            case "set_action_call": { var idx = root.GetProperty("nodeIndex").GetInt32(); return SetActionCall(GetStr(root, "module"), GetStr(root, "action"), idx, GetStr(root, "serverActionName"), GetStr(root, "producerModule")); }
            case "add_refresh_node": return AddRefreshNode2(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "block"), GetStr(root, "screen"), GetStr(root, "aggregateName"), GetStr(root, "where"), root.TryGetProperty("afterNodeIndex", out var ani2) && ani2.ValueKind == JsonValueKind.Number ? ani2.GetInt32() : -1);
            case "debug_server_actions": return DebugServerActions(GetStr(root, "module"));
            case "debug_find_action": return DebugFindAction(GetStr(root, "module"), GetStr(root, "name"));
            case "debug_node_props": return DebugNodeProps(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "nodeIndex"));
            case "debug_action_ref": return DebugActionRef(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "serverActionName"), GetStr(root, "producerModule"));
            case "debug_eSpace_collections": return DebugESpaceCollections(GetStr(root, "module"));
            case "debug_eSpace_collection_items": return DebugESpaceCollectionItems(GetStr(root, "module"), GetStr(root, "collection"));
            case "debug_reference": return DebugReference(GetStr(root, "module"), GetStr(root, "referenceName"));
            case "debug_reference_extended": return DebugReferenceExtended(GetStr(root, "module"), GetStr(root, "referenceName"));
            case "debug_eSpace_from_reference": return DebugESpaceFromReference(GetStr(root, "module"), GetStr(root, "referenceName"));
            case "debug_sentinel": return DebugSentinel(GetStr(root, "module"), GetStr(root, "referenceName"));
            case "debug_sentinel_collections": return DebugSentinelCollections(GetStr(root, "module"), GetStr(root, "referenceName"));
            case "debug_sentinel_collection_items": return DebugSentinelCollectionItems(GetStr(root, "module"), GetStr(root, "referenceName"), GetStr(root, "collection"));
            // Live dependency management: list consumable elements + consume (add references).
            case "list_consumable_elements": return ListConsumableElements(GetStr(root, "module"));
            case "consume_elements": return ConsumeElements(GetStr(root, "consumer"), GetStr(root, "producer"), GetStr(root, "what"));
            case "remove_dependency": return RemoveDependency(GetStr(root, "module"), GetStr(root, "producer"));
            case "publish_module": return PublishModule(GetStr(root, "module"), GetStr(root, "commitMessage"), !root.TryGetProperty("wait", out var wp) || wp.GetBoolean(), root.TryGetProperty("timeoutSec", out var ts) ? ts.GetInt32() : 600);
            case "debug_publish_surface": return DebugPublishSurface(GetStr(root, "module"));
            case "debug_publish_state": return DebugPublishState(GetStr(root, "module"));
            case "save_module": return SaveModule(GetStr(root, "module"));
            case "open_producer_module": return OpenProducerModule(GetStr(root, "module"), GetStr(root, "reference"));
            case "clone_service_action_from": return CloneServiceActionFrom(GetStr(root, "consumer"), GetStr(root, "producer"), GetStr(root, "source"), GetStr(root, "name"));
            case "clone_element": return CloneElement(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "newName"));
            case "debug_object_prop_surface": return DebugObjectPropSurface(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "entity"), GetStr(root, "name"));
            case "set_object_prop_deep": return SetObjectPropDeep(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "entity"), GetStr(root, "name"), GetStr(root, "propName"), GetStr(root, "value"));
            case "upload_image": return UploadImage(GetStr(root, "module"), GetStr(root, "name"), GetStr(root, "base64Data"), GetStr(root, "description"));
            case "upload_resource": return UploadResource(GetStr(root, "module"), GetStr(root, "name"), GetStr(root, "base64Data"));
            case "delete_structure": return DeleteStructure(GetStr(root, "module"), GetStr(root, "name"));
            case "remove_unused_dependencies": return RemoveUnusedDependencies(GetStr(root, "module"));
            case "add_entity_input": return AddEntityInput(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "add_entity_identifier_input": return AddEntityIdentifierInput(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "set_output_param_type": return SetOutputParamType(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "paramName"), GetStr(root, "typeName"), GetStr(root, "producerModule"));
            case "set_input_param_type": return SetInputParamType(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "paramName"), GetStr(root, "typeName"), GetStr(root, "producerModule"), GetStr(root, "entityName"));
            case "add_entity_output": return AddEntityOutput(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "delete_service_action": return DeleteServiceAction(GetStr(root, "module"), GetStr(root, "action"));
            case "try_create_folder": return TryCreateFolder(GetStr(root, "module"), GetStr(root, "folderName"), GetStr(root, "parentFolder"));
            case "move_to_folder": return MoveToFolder(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "folderName"));
            case "add_assignment_to_node": { var idx = root.GetProperty("nodeIndex").GetInt32(); return AddAssignmentToNode(GetStr(root, "module"), GetStr(root, "action"), idx, GetStr(root, "var"), GetStr(root, "value")); }
            case "remove_assignment": { var idx = root.GetProperty("nodeIndex").GetInt32(); return RemoveAssignment(GetStr(root, "module"), GetStr(root, "action"), idx, GetStr(root, "var")); }
            case "set_node_prop_to_node": { var ni = root.GetProperty("nodeIndex").GetInt32(); var ti = root.GetProperty("targetNodeIndex").GetInt32(); return SetNodePropToNode(GetStr(root, "module"), GetStr(root, "action"), ni, GetStr(root, "propName"), ti); }
            case "set_prop_force": { var ni = root.GetProperty("nodeIndex").GetInt32(); var ti = root.GetProperty("targetNodeIndex").GetInt32(); return SetPropForceCmd(GetStr(root, "module"), GetStr(root, "action"), ni, GetStr(root, "propName"), ti); }
            case "debug_find_exc_handler": return DebugFindExcHandler(GetStr(root, "module"), GetStr(root, "action"));
            case "set_error_handler_exception": { var idx = root.GetProperty("nodeIndex").GetInt32(); var excName = GetStr(root, "exceptionName"); return SetErrorHandlerException(GetStr(root, "module"), GetStr(root, "action"), idx, excName); }
            case "map_action_inputs": { var idx = root.GetProperty("nodeIndex").GetInt32(); return MapActionInputs(GetStr(root, "module"), GetStr(root, "action"), idx, GetStr(root, "inputParamName")); }
            case "set_action_arg": { var idx = root.GetProperty("nodeIndex").GetInt32(); return SetActionArg(GetStr(root, "module"), GetStr(root, "action"), idx, GetStr(root, "argName"), GetStr(root, "value")); }
            case "remove_input_param": return RemoveInputParam(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "paramName"));
            case "remove_output_param": return RemoveOutputParam(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "paramName"));
            case "debug_action_args": { var idx = root.GetProperty("nodeIndex").GetInt32(); return DebugActionArgs(GetStr(root, "module"), GetStr(root, "action"), idx); }
            case "layout_flow": return LayoutFlow(GetStr(root, "module"), GetStr(root, "action"));
            case "get_node_positions": return GetNodePositions(GetStr(root, "module"), GetStr(root, "action"));
            case "set_node_position": { var ni = root.GetProperty("nodeIndex").GetInt32(); var x = root.GetProperty("x").GetDouble(); var y = root.GetProperty("y").GetDouble(); return SetNodePosition(GetStr(root, "module"), GetStr(root, "action"), ni, x, y); }
            // ---- UI / screen / widget / CSS tools (live) ----
            case "debug_espace_creates": return DebugESpaceCreates(GetStr(root, "module"));
            case "probe_theme_factories": return ProbeThemeFactories(GetStr(root, "module"));
            case "create_web_flow": return CreateWebFlow(GetStr(root, "module"), GetStr(root, "name"));
            case "create_flow_scratch": return CreateFlowScratch(GetStr(root, "module"), GetStr(root, "name"));
            case "delete_web_flow": return DeleteWebFlow(GetStr(root, "module"), GetStr(root, "flow"));
            case "create_web_screen": return CreateWebScreen(GetStr(root, "module"), GetStr(root, "webFlow"), GetStr(root, "name"));
            case "create_web_block": return CreateWebBlock(GetStr(root, "module"), GetStr(root, "flow"), GetStr(root, "name"));
            case "delete_screen_from_flow": return DeleteScreenFromFlow(GetStr(root, "module"), GetStr(root, "flow"), GetStr(root, "screen"));
            case "list_web_flows": return ListWebFlows(GetStr(root, "module"));
            case "add_container": return AddContainer(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "styleClass"));
            case "add_expression": return AddExpressionWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "value"), GetStr(root, "styleClass"));
            case "add_text": return AddTextWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "styleClass"));
            case "add_link": return AddLinkWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "targetScreen"), GetStr(root, "styleClass"));
            case "add_html_element": return AddHtmlElement(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "tag"), GetStr(root, "styleClass"));
            case "add_html_text": return AddHtmlText(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "tag"), GetStr(root, "styleClass"), GetStr(root, "text"));
            case "delete_widget": return DeleteWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"));
            case "set_widget_property": return SetWidgetProperty(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "propName"), GetStr(root, "propValue"));
            case "probe_widget_members": return ProbeWidgetMembers(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"));
            case "move_web_block_to_flow": return MoveWebBlockToFlow(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "targetFlow"));
            case "set_style_class": return SetStyleClass(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "styleClass"));
            case "set_module_css": return SetModuleCss(GetStr(root, "module"), GetStr(root, "css"));
            case "set_user_css": return SetUserModuleCss(GetStr(root, "module"), GetStr(root, "css"));
            case "probe_style_prop": return ProbeStyleProp(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"));
            case "probe_block_widget": return ProbeBlockWidget(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"));
            case "set_style_cp": return SetStyleCp(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "styleClass"));
            case "read_theme_css": return ReadThemeCss(GetStr(root, "module"));
            case "read_user_css_text": return ReadUserCssText(GetStr(root, "module"), GetStr(root, "themeName"));
            case "probe_sheet": return ProbeSheet(GetStr(root, "module"));
            case "set_screen_title": return SetScreenTitle(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "title"));
            case "create_theme": return CreateTheme(GetStr(root, "module"), GetStr(root, "sourceName"), GetStr(root, "newName"), GetStr(root, "css"));
            case "set_theme_css": return SetThemeCss(GetStr(root, "module"), GetStr(root, "themeName"), GetStr(root, "css"));
            case "set_screen_theme": return SetScreenTheme(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "themeName"));
            case "set_flow_theme": return SetFlowTheme(GetStr(root, "module"), GetStr(root, "flow"), GetStr(root, "themeName"));
            case "create_theme_scratch": return CreateThemeScratch(GetStr(root, "module"), GetStr(root, "name"), GetStr(root, "css"), GetStr(root, "baseTheme"));
            case "create_theme_v2": return CreateThemeV2(GetStr(root, "module"), GetStr(root, "name"), GetStr(root, "css"), GetStr(root, "baseTheme"));
            case "clone_web_block": return CloneWebBlock(GetStr(root, "module"), GetStr(root, "sourceName"), GetStr(root, "newName"));
            case "set_theme_layout": return SetThemeLayout(GetStr(root, "module"), GetStr(root, "themeName"), GetStr(root, "layoutBlock"), GetStr(root, "menuBlock"));
            case "probe_theme": return ProbeTheme(GetStr(root, "module"), GetStr(root, "themeName"));
            case "list_widgets": return ListWidgets(GetStr(root, "module"), GetStr(root, "screen"));
            case "probe_widget_api": return ProbeWidgetApi(GetStr(root, "module"), GetStr(root, "screen"));
            case "probe_create_methods": return ProbeCreateMethods(GetStr(root, "module"), GetStr(root, "screen"));
            case "dump_widget_concretes": return DumpWidgetConcretes();
                    case "probe_descriptors": return ProbeDescriptors();
                    case "probe_type": return ProbeType(GetStr(root, "typeName"));
                    case "probe_references": return ProbeReferences(GetStr(root, "module"));
            case "probe_link_widget": return ProbeLinkWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"));
            case "create_widget_descriptor": return CreateWidgetDescriptor(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "parent"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "styleClass"));
            case "add_button": return AddButton(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "placeholder"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "styleClass"));
            case "set_button_onclick": return SetButtonOnClick(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "button"), GetStr(root, "actionName"));
            case "wire_button_to_screen": return WireButtonToScreen(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "button"), GetStr(root, "targetScreen"), GetStr(root, "params"));
            case "list_placeholders": return ListPlaceholders(GetStr(root, "module"), GetStr(root, "screen"));
            case "probe_layout_ref": return ProbeLayoutRef(GetStr(root, "module"), GetStr(root, "screen"));
            case "set_screen_layout": return SetScreenLayout(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "layoutBlock"));
            case "add_to_placeholder": return AddToPlaceholder(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "placeholder"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "targetScreen"), GetStr(root, "styleClass"));
            case "add_inside_placeholder": return AddInsidePlaceholder(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "placeholder"), GetStr(root, "parent"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "targetScreen"), GetStr(root, "styleClass"));
            case "add_link_to_block": return AddLinkToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "targetScreen"), GetStr(root, "styleClass"));
            case "add_placeholder_to_block": return AddPlaceholderToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "name"));
            case "add_widget_to_block": return AddWidgetToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "value"), GetStr(root, "styleClass"));
            case "delete_widget_from_block": return DeleteWidgetFromBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
                    case "delete_anon_block_widgets": return DeleteAnonBlockWidgets(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "typeContains"), root.TryGetProperty("recursive", out var rec) && rec.GetBoolean());
            case "dump_block_widget_types": return DumpBlockWidgetTypes(GetStr(root, "module"), GetStr(root, "block"));
            case "add_variable_to_block": return AddVariableToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "add_variable_to_screen": return AddVariableToScreen(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "type"), GetStr(root, "defaultValue"));
            case "set_block_widget_property": return SetBlockWidgetProperty(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widgetName"), GetStr(root, "propName"), GetStr(root, "propValue"));
            case "list_block_widgets": return ListBlockWidgets(GetStr(root, "module"), GetStr(root, "block"));
            case "add_button_to_block": return AddButtonToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "text"), GetStr(root, "styleClass"));
            case "set_block_button_onclick": return SetBlockButtonOnClick(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "button"), GetStr(root, "actionName"));
            case "list_block_input_params": return ListBlockInputParams(GetStr(root, "module"), GetStr(root, "block"));
            case "add_input_param_to_block": return AddInputParamToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "type"));
            case "set_block_input_param_type": return SetBlockInputParamType(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "paramName"), GetStr(root, "typeName"), GetStr(root, "producerModule"), GetStr(root, "entityName"));
            case "remove_input_param_from_block": return RemoveInputParamFromBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "paramName"));
            case "remove_screen_input_param": return RemoveScreenInputParam(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "paramName"));
            case "add_entity_input_to_block": return AddEntityInputToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "add_entity_identifier_input_to_block": return AddEntityIdentifierInputToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "add_if_node": return AddIfNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "condition"), root.TryGetProperty("afterNodeIndex", out var ini) && ini.ValueKind == JsonValueKind.Number ? ini.GetInt32() : -1);
            case "add_switch_node": return AddSwitchNode(GetStr(root, "module"), GetStr(root, "action"), root.TryGetProperty("afterNodeIndex", out var sni) && sni.ValueKind == JsonValueKind.Number ? sni.GetInt32() : -1);
            case "probe_node_connectors": { var pidx = root.GetProperty("nodeIndex").GetInt32(); return ProbeNodeConnectors(GetStr(root, "module"), GetStr(root, "action"), pidx); }
            case "probe_node_conditions": { var pnci = root.GetProperty("nodeIndex").GetInt32(); return ProbeNodeConditions(GetStr(root, "module"), GetStr(root, "action"), pnci); }
            case "set_switch_case": { var sswi = root.GetProperty("nodeIndex").GetInt32(); var sti = root.GetProperty("targetIndex").GetInt32(); return SetSwitchCase(GetStr(root, "module"), GetStr(root, "action"), sswi, GetStr(root, "value"), sti); }
            case "set_connector_target": { var cni = root.GetProperty("nodeIndex").GetInt32(); var cti = root.GetProperty("targetIndex").GetInt32(); return SetConnectorTarget(GetStr(root, "module"), GetStr(root, "action"), cni, GetStr(root, "propName"), cti); }
            case "probe_block_members": return ProbeBlockMembers(GetStr(root, "module"), GetStr(root, "block"));
            case "add_event_to_block": return AddEventToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "delete_block_client_action": return DeleteBlockClientAction(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "remove_event_from_block": return RemoveEventFromBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "add_event_param_to_block": return AddEventParamToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "event"), GetStr(root, "name"), GetStr(root, "type"));
            case "set_block_handler": return SetBlockHandler(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "handler"), GetStr(root, "actionName"));
            case "set_screen_handler": return SetScreenHandler(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "handler"), GetStr(root, "actionName"));
            case "add_raise_event_node": return AddRaiseEventNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "eventName"), root.TryGetProperty("afterNodeIndex", out var reni) && reni.ValueKind == JsonValueKind.Number ? reni.GetInt32() : -1);
            case "probe_widget_deep": return ProbeWidgetDeep(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widgetName"));
            case "probe_data_sources": return ProbeDataSources(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "screen"));
            case "probe_widget_kinds": return ProbeWidgetKinds();
            case "add_nr_widget": return AddNRWidget(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "styleClass"), GetStr(root, "text"));
            case "set_widget_handler": return SetWidgetHandler(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "event"), GetStr(root, "actionName"));
            case "set_element_description": return SetElementDescription(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "description"));
            case "add_aggregate_to_block": return AddAggregateToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "add_aggregate_to_screen": return AddAggregateToScreen(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "add_data_action_to_block": return AddDataActionToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "add_data_action_to_screen": return AddDataActionToScreen(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"));
            case "delete_data_action": return DeleteDataAction(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "screen"), GetStr(root, "name"));
            case "delete_aggregate": return DeleteAggregate(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "screen"), GetStr(root, "name"));
            case "delete_from_placeholder": return DeleteFromPlaceholder(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "placeholder"), GetStr(root, "name"));
            case "delete_layout": return DeleteLayout(GetStr(root, "module"), GetStr(root, "screen"));
            case "set_html_attr": return SetHtmlAttr(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "attrName"), GetStr(root, "attrValue"));
                    case "set_block_variable_type": return SetBlockVariableType(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "var"), GetStr(root, "type"), GetStr(root, "default"), GetStr(root, "producerModule"), root.TryGetProperty("identifier", out var idf) && idf.GetBoolean(), GetStr(root, "entityName"));
            case "set_block_aggregate_source": return SetBlockAggregateSource(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "entityName"), GetStr(root, "producerModule"));
            case "set_block_aggregate_filter": return SetBlockAggregateFilter(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "filter"));
            case "add_if_widget_to_block": return AddIfWidgetToBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "parent"), GetStr(root, "name"), GetStr(root, "condition"));
            case "move_widget_in_block": return MoveWidgetInBlock(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "newParent"));
            case "move_widget": return MoveWidgetInScreen(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "newParent"));
                    case "set_block_cp_expression": return SetBlockCpExpression(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
                    case "set_block_cp_parsed": return SetBlockCpParsed(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
            case "set_screen_cp_parsed": return SetScreenCpParsed(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
            case "set_screen_cp_text": return SetScreenCpText(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
                    case "set_block_cp_value_attr": return SetBlockCpValueAttr(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
                    case "set_block_cp_image": return SetBlockCpImage(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "imageName"));
                    case "set_block_cp_text": return SetBlockCpText(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"), GetStr(root, "value"));
            case "probe_block_cp": return ProbeBlockCp(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "propName"));
            case "dump_flow_nodes": return DumpFlowNodes(GetStr(root, "module"));
            case "create_block_client_action": return CreateBlockClientAction(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "name"));
            case "create_structure": return CreateStructure(GetStr(root, "module"), GetStr(root, "name"));
            case "add_structure_attribute": return AddStructureAttribute(GetStr(root, "module"), GetStr(root, "structure"), GetStr(root, "attrName"), GetStr(root, "type"));
            case "set_structure_attribute_type": return SetStructureAttributeType(GetStr(root, "module"), GetStr(root, "structure"), GetStr(root, "attrName"), GetStr(root, "type"));
            case "create_entity": return CreateEntity(GetStr(root, "module"), GetStr(root, "name"));
            case "add_entity_attribute": return AddEntityAttribute(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "attrName"), GetStr(root, "type"), GetStr(root, "isMandatory"), GetStr(root, "defaultValue"));
            case "set_entity_attribute_type": return SetEntityAttributeType(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "attrName"), GetStr(root, "type"));
            case "set_entity_attribute_name": return SetEntityAttributeName(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "attrName"), GetStr(root, "newName"));
            case "set_entity_prop": return SetEntityProp(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "prop"), GetStr(root, "value"));
            case "set_structure_prop": return SetStructureProp(GetStr(root, "module"), GetStr(root, "structure"), GetStr(root, "prop"), GetStr(root, "value"));
            case "set_server_action_prop": return SetServerActionProp(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "prop"), GetStr(root, "value"));
            case "set_entity_identifier": return SetEntityIdentifier(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "attrName"));
            case "probe_entity_actions": return ProbeEntityActions(GetStr(root, "module"), GetStr(root, "entity"));
            case "create_entity_action_node": return CreateEntityActionNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "entity"), GetStr(root, "entityAction"), GetStr(root, "where"), GetStr(root, "anchorVar"), GetStr(root, "anchorValue"), root.TryGetProperty("afterNodeIndex", out var eani) && eani.ValueKind == JsonValueKind.Number ? eani.GetInt32() : -1);
            case "delete_entity_attribute": return DeleteEntityAttribute(GetStr(root, "module"), GetStr(root, "entity"), GetStr(root, "attrName"));
            case "delete_entity": return DeleteEntity(GetStr(root, "module"), GetStr(root, "name"));
            case "get_verify_errors": return GetVerifyErrors(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "name"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "verbose"));
            case "set_widget_source": return SetWidgetSource(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "source"));
            case "set_aggregate_paging": return SetAggregatePaging(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "maxRecords"), GetStr(root, "startIndex"));
            case "add_aggregate_sort": return AddAggregateSort(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "attr"), GetStr(root, "ascending"));
            case "remove_aggregate_sort": return RemoveAggregateSort(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "attr"));
            case "add_aggregate_dynamic_sort": return AddAggregateDynamicSort(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "varName"));
            case "set_input_param_mandatory": return SetInputParamMandatory(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "paramName"), GetStr(root, "mandatory"));
            case "set_widget_handler_arg": return SetWidgetHandlerArg(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "event"), GetStr(root, "argName"), GetStr(root, "value"));
            case "probe_system_actions": return ProbeSystemActions(GetStr(root, "module"));
            case "consume_system_client_action": return ConsumeSystemClientAction(GetStr(root, "module"), GetStr(root, "name"));
            case "add_client_action_call": return AddClientActionCall(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "systemAction"), GetStr(root, "where"), GetStr(root, "anchorVar"), GetStr(root, "anchorValue"), root.TryGetProperty("args", out var aj) ? aj.ToString() : null);
            case "add_screen_aggregate_filter": return AddScreenAggregateFilter(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "filter"));
            case "add_aggregate_calculated_attr": return AddAggregateCalculatedAttr(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "attrName"), GetStr(root, "type"));
            case "add_foreach_node": return AddForeachNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "recordList"), GetStr(root, "maxIterations"), GetStr(root, "startIndex"));
            case "add_screen_input_param": return AddScreenInputParam(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "type"));
            case "create_role": return CreateRole(GetStr(root, "module"), GetStr(root, "name"));
            case "set_screen_permissions": return SetScreenPermissions(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "roles"), GetStr(root, "isPublic"));
            case "create_site_property": return CreateSiteProperty(GetStr(root, "module"), GetStr(root, "name"), GetStr(root, "type"), GetStr(root, "shared"), GetStr(root, "defaultValue"));
            case "create_timer": return CreateTimer(GetStr(root, "module"), GetStr(root, "name"));
            case "fix_style_literal": return FixStyleLiteral(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "cssClass"));
            case "set_aggregate_calc_formula": return SetAggregateCalcFormula(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "attrName"), GetStr(root, "formula"));
            case "set_aggregate_param_type": return SetAggregateParamType(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "paramName"), GetStr(root, "type"));
            case "probe_collection": return ProbeCollection(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "collection"));
            case "read_aggregate_sorts": return ReadAggregateSorts(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"));
            case "read_aggregate_calcs": return ReadAggregateCalcs(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"));
            case "read_aggregate_params": return ReadAggregateParams(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"));
            case "delete_aggregate_sort": { int di = -1; try { di = root.GetProperty("index").GetInt32(); } catch { } return DeleteAggregateSort(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), di); }
            case "delete_aggregate_filter": { int fi = -1; try { fi = root.GetProperty("index").GetInt32(); } catch { } return DeleteAggregateFilter(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), fi); }
            case "grant_screen_permission": return GrantScreenPermission(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "roleName"));
            case "remove_screen_permission": return RemoveScreenPermission(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "roleName"));
            case "read_screen_permissions": return ReadScreenPermissions(GetStr(root, "module"), GetStr(root, "screen"));
            case "add_aggregate_source": return AddAggregateSource(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "entityName"));
            case "add_aggregate_join": return AddAggregateJoin(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "block"), GetStr(root, "name"), GetStr(root, "condition"), GetStr(root, "leftIndex"), GetStr(root, "rightIndex"));
            case "set_link_params": return SetLinkParams(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "widget"), GetStr(root, "params"));
            case "debug_create_surface": return DebugCreateSurface(GetStr(root, "module"));
            case "set_timer_action": return SetTimerAction(GetStr(root, "module"), GetStr(root, "timer"), GetStr(root, "action"));
            case "set_block_html_attr": return SetBlockHtmlAttr(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "attrName"), GetStr(root, "attrValue"));
            case "delete_block_html_attr": return DeleteBlockHtmlAttr(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"), GetStr(root, "attrName"));
            case "probe_block_extended_props": return ProbeBlockExtendedProps(GetStr(root, "module"), GetStr(root, "block"), GetStr(root, "widget"));
            case "probe_flow_node_classes": return ProbeFlowNodeClasses();
            case "set_raise_event_arg": return SetRaiseEventArg(GetStr(root, "module"), GetStr(root, "action"), root.TryGetProperty("nodeIndex", out var rei) && rei.ValueKind == JsonValueKind.Number ? rei.GetInt32() : -1, GetStr(root, "argName"), GetStr(root, "value"));
            case "set_action_name": return SetActionName(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "newName"));
            case "delete_action": return DeleteAction(GetStr(root, "module"), GetStr(root, "action"));
            case "delete_screen_client_action": return DeleteScreenClientAction(GetStr(root, "module"), GetStr(root, "screen"), GetStr(root, "action"));
            case "probe_obj": return ProbeObj(GetStr(root, "module"), GetStr(root, "kind"), GetStr(root, "block"), GetStr(root, "screen"), GetStr(root, "name"), GetStr(root, "sub"));
            case "add_js_node": return AddJsNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "js"), GetStr(root, "nodeName"), root.TryGetProperty("afterNodeIndex", out var jsni) && jsni.ValueKind == JsonValueKind.Number ? jsni.GetInt32() : -1);
            case "add_message_node": return AddMessageNode(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "message"), GetStr(root, "kind"), root.TryGetProperty("afterNodeIndex", out var mni) && mni.ValueKind == JsonValueKind.Number ? mni.GetInt32() : -1);
            case "add_lifecycle_assign": return AddLifecycleAssign(GetStr(root, "module"), GetStr(root, "action"), GetStr(root, "var"), GetStr(root, "value"));
            default: return Json(new { ok = false, error = "unknown cmd: " + cmd });
        }
    }

    // ---- live-model access (reflection) ----
    static object ModelServices()
    {
        var pp = typeof(PluginProvider).GetProperty("ModelServices", BindingFlags.Public | BindingFlags.Static);
        return pp?.GetValue(null, null);
    }

    static List<object> LoadedESpaces()
    {
        var list = new List<object>();
        var ms = ModelServices();
        if (ms == null) return list;
        var loaded = ms.GetType().GetProperty("LoadedESpaces")?.GetValue(ms, null) as IEnumerable;
        if (loaded == null) return list;
        foreach (var es in loaded) list.Add(es);
        return list;
    }

    static object FindEspace(string name)
    {
        // Prefer the entry with a LIVE aggregator context: kills/restarts can leave
        // multiple same-name entries (stale ones read fine but refuse writes with
        // "aggregator is null"). Fall back to the first name match.
        object fallback = null;
        foreach (var es in LoadedESpaces())
        {
            string nm = null;
            try { nm = GetProp(es, "Name") as string; } catch { continue; }
            if (nm != name) continue;
            if (fallback == null) fallback = es;
            try { if (GetContext(es) != null) return es; } catch { }
        }
        return fallback;
    }

    static string ListModules()
    {
        var names = new List<string>();
        foreach (var es in LoadedESpaces())
        {
            try { names.Add(GetProp(es, "Name") as string); }
            catch (Exception e) { names.Add("<err:" + e.GetType().Name + ">"); }
        }
        return Json(new { ok = true, count = names.Count, modules = names });
    }

    static string ModuleInfo(string name)
    {
        var es = FindEspace(name);
        if (es == null) return Json(new { ok = false, error = "module not found: " + name });
        var info = new Dictionary<string, object> { ["ok"] = true };
        info["name"] = GetProp(es, "Name");
        info["kind"] = GetProp(es, "Kind");
        info["entities"] = CountProp(es, "Entities");
        info["clientActions"] = CountProp(es, "ClientActions");
        info["serviceActions"] = CountProp(es, "ServiceActions");
        info["serverActions"] = CountProp(es, "ServerActions");
        info["anonymousStructures"] = CountProp(es, "AnonymousStructures");
        info["type"] = es.GetType().FullName;
        return Json(info);
    }

    static int CountProp(object es, string prop)
    {
        try
        {
            var val = GetProp(es, prop) as IEnumerable;
            if (val == null) return -1;
            int n = 0; foreach (var _ in val) n++;
            return n;
        }
        catch (Exception) { return -2; }
    }

    // v8: DEEP-CLONE a service action via IModelServices.Duplicate(source, parent) inside a
    // command, then rename. Duplicate serializes+deserializes the element (the same mechanism
    // SS copy/paste uses) so the clone is exactly equal (params, flow, metadata) with a fresh key.
    // The returned IObjectSignature IS the new ServiceAPIMethod object (implements IServiceAction,
    // which has a Name setter) - rename it to newName.
    static object FindServiceAction(object es, string name)
    {
        var coll = GetProp(es, "ServiceActions") as IEnumerable;
        if (coll == null) return null;
        foreach (var item in coll)
        {
            try { if ((GetProp(item, "Name") as string) == name) return item; } catch { }
        }
        return null;
    }

    // FindAction: search ALL action-type collections on the eSpace by name.
    // Used by flow-editing tools (LiveEdit, ListFlow, DebugNodeProps, etc.) so they
    // work on service actions, server actions (UserActions), AND client actions.
    // The flow-editing primitives are IAction-level (generic), so once the action
    // is found, all downstream mutations (CreateNode, CreateOutputParameter, etc.)
    // work regardless of action type.
    static object FindAction(object es, string name)
    {
        // Try each action-type collection. ServiceActions first (most common),
        // then UserActions (server actions), then ClientActions.
        foreach (var collName in new[] { "ServiceActions", "UserActions", "ServerActions", "ClientActions" })
        {
            var coll = GetProp(es, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var item in coll)
            {
                try { if ((GetProp(item, "Name") as string) == name) return item; } catch { }
            }
        }
        // Fallback: screen-level Client Actions (ClientScreenActionFlow) live on each
        // web-flow screen's ClientActions collection, not on the eSpace. Search all
        // screens so flow-editing tools work on screen client actions too.
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (!IsScreen(node)) continue;
                var found = FindActionInCollection(node, "ClientActions", name);
                if (found != null) return found;
            }
        }
        // Web-block-scoped targets: block-level ClientActions AND the lifecycle-handler
        // children (OnInitialize / OnReady / OnRender / OnParametersChanged / OnDestroy, which
        // are NREvents.* objects that own a flow in Reactive). This lets ALL the flow-editing
        // tools (live_list_flow, live_add_if_node, live_add_raise_event_node, assigns, ...)
        // edit a block handler's flow just by using the handler's name (e.g.
        // live_list_flow on "OnParametersChanged").
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (IsScreen(node)) continue;
                var foundCa = FindActionInCollection(node, "ClientActions", name);
                if (foundCa != null) return foundCa;
                foreach (var lifeName in new[] { "OnInitialize", "OnReady", "OnRender", "OnParametersChanged", "OnDestroy", "OnParametersChange" })
                {
                    object lf = null;
                    try { lf = GetProp(node, lifeName); } catch { }
                    if (lf != null && string.Equals(lifeName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        // NREvents lifecycle wrappers (OnParametersChanged etc.) hold the real
                        // flow in their .Destination (a ClientScreenActionFlow) - unwrap it so
                        // flow-editing tools (CreateNode, list_flow, ...) work on the actual flow.
                        try
                        {
                            var dest = GetProp(lf, "Destination");
                            if (dest != null && dest.GetType().Name.IndexOf("Flow", StringComparison.Ordinal) >= 0)
                                return dest;
                        }
                        catch { }
                        return lf;
                    }
                }
            }
        }
        // Screen lifecycle children (OnInitialize / OnReady / OnRender / OnDestroy) own flows the
        // same way (NREvents.* wrappers whose Destination is the screen's ClientScreenActionFlow).
        // Reflection-verified on SS 11.55.83: ServiceStudio.Model.NRNodes/WebScreen implements
        // IMobileScreen, whose lifecycle props are read-only IUILifeCycleEvent children - screens
        // have NO assignable action-reference prop, so unwrapping the flow for the flow-editing
        // tools is the screen counterpart of the block handling above.
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (!IsScreen(node)) continue;
                foreach (var lifeName in new[] { "OnInitialize", "OnReady", "OnRender", "OnDestroy" })
                {
                    object lf = null;
                    try { lf = GetProp(node, lifeName); } catch { }
                    if (lf != null && string.Equals(lifeName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var dest = GetProp(lf, "Destination");
                            if (dest != null && dest.GetType().Name.IndexOf("Flow", StringComparison.Ordinal) >= 0)
                                return dest;
                        }
                        catch { }
                        return lf;
                    }
                }
            }
        }
        // Custom events on web blocks (WebBlockCustomEvent implements a flow-like target that
        // SetBlockHandler-style wiring / raise-event binding can address by name).
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (IsScreen(node)) continue;
                var foundEv = FindActionInCollection(node, "CustomEvents", name);
                if (foundEv != null) return foundEv;
            }
        }
        // Web-block DATA actions (DataScreenActionFlow) and screen AGGREGATES / data sets
        // (WebScreenDataSet / ScreenAggregate) also own flows with nodes + output params. Searching
        // them lets the flow-editing tools (live_list_flow, live_add_assign_node, ...) and
        // live_set_output_param_type work on a block's FetchUserStats / GetUserById directly.
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (IsScreen(node)) continue;
                var foundDa = FindActionInCollection(node, "DataActions", name);
                if (foundDa != null) return foundDa;
                var foundDs = FindActionInCollection(node, "ScreenDataSets", name);
                if (foundDs != null) return foundDs;
                foreach (var dsColl in new[] { "ScreenAggregates", "DataSets" })
                {
                    var foundDs2 = FindActionInCollection(node, dsColl, name);
                    if (foundDs2 != null) return foundDs2;
                }
            }
        }
        return null;
    }

    // Enumerate ALL actions in an ESpace across every action-type collection
    // (ServiceActions, UserActions/ServerActions, ClientActions). Used by
    // SetErrorHandlerException Strategy 1 to copy an Exception object from any
    // existing exception handler in the module � works in any project, not just
    // ones with a hardcoded "ClientCreate_BL_Manually" reference action.
    static IEnumerable<object> AllActions(object es)
    {
        foreach (var collName in new[] { "ServiceActions", "UserActions", "ServerActions", "ClientActions" })
        {
            var coll = GetProp(es, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var item in coll) yield return item;
        }
    }

    static void SetProp(object obj, string name, object value)
    {
        if (obj == null) throw new Exception("SetProp: obj null");
        for (var t = obj.GetType(); t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, value, null); return; }
        }
        foreach (var iface in obj.GetType().GetInterfaces())
        {
            var p = iface.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, value, null); return; }
        }
        throw new Exception("property not settable: " + name + " on " + obj.GetType().FullName);
    }

    // SetPropTyped: like SetProp but converts the value to the property's declared type
    // via Convert.ChangeType. Needed for numeric properties like X/Y that may be int,
    // double, or float depending on the SS build � SetProp passes the value as-is and
    // would throw when assigning a double to an int property (or vice-versa).
    static void SetPropTyped(object obj, string name, object value)
    {
        if (obj == null) throw new Exception("SetPropTyped: obj null");
        for (var t = obj.GetType(); t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType), null); return; }
        }
        foreach (var iface in obj.GetType().GetInterfaces())
        {
            var p = iface.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType), null); return; }
        }
        throw new Exception("property not settable: " + name + " on " + obj.GetType().FullName);
    }

    // SetPropForce: aggressive reflection to set a property that SetProp can't handle
    // (read-only properties, explicit interface impls with non-public setters, etc.)
    // Tries: normal SetProp -> NonPublic SetMethod -> backing field -> Set<Name> method.
    // Returns a description of which strategy worked, or throws with a full diagnostic.
    static string SetPropForce(object obj, string name, object value)
    {
        if (obj == null) throw new Exception("SetPropForce: obj null");
        var diag = new StringBuilder();

        // Strategy 1: Normal SetProp
        try { SetProp(obj, name, value); return "SetProp succeeded"; }
        catch (Exception e) { diag.AppendLine("s1(SetProp): " + e.Message); }

        // Strategy 2: Find property (any access) and invoke its SetMethod (include non-public)
        foreach (var t in AllTypes(obj.GetType()))
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null) continue;
            diag.AppendLine("s2: found prop " + name + " on " + t.Name + " CanWrite=" + p.CanWrite);
            var setMethod = p.GetSetMethod(true); // true = include non-public
            if (setMethod != null)
            {
                diag.AppendLine("  SetMethod=" + setMethod.Name + " IsPublic=" + setMethod.IsPublic);
                try { setMethod.Invoke(obj, new object[] { value }); return "s2: SetMethod " + setMethod.Name + " invoked OK"; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag.AppendLine("  SetMethod invoke err: " + r.Message); }
            }
        }

        // Strategy 3: Backing field (compiler-generated: <Name>k__BackingField)
        foreach (var t in AllTypes(obj.GetType()))
        {
            foreach (var fn in new[] { "<" + name + ">k__BackingField", name, "_" + name, "m_" + name })
            {
                var f = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    diag.AppendLine("s3: found field " + fn + " on " + t.Name);
                    try { f.SetValue(obj, value); return "s3: field " + fn + " set OK"; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag.AppendLine("  field set err: " + r.Message); }
                }
            }
        }

        // Strategy 4: Method like Set<Name> or set_<Name> with 1 param
        foreach (var t in AllTypes(obj.GetType()))
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if ((m.Name == "Set" + name || m.Name == "set_" + name) && m.GetParameters().Length == 1)
                {
                    diag.AppendLine("s4: found method " + m.Name + " on " + t.Name);
                    try { m.Invoke(obj, new object[] { value }); return "s4: method " + m.Name + " invoked OK"; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag.AppendLine("  method invoke err: " + r.Message); }
                }
            }
        }

        // Diagnostic: list all props + methods containing the name
        diag.AppendLine("--- props containing '" + name + "' ---");
        foreach (var t in AllTypes(obj.GetType()))
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (p.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    diag.AppendLine("  [" + t.Name + "] " + p.PropertyType.Name + " " + p.Name + " CanWrite=" + p.CanWrite + " SetMethod=" + (p.GetSetMethod(true) != null));
        diag.AppendLine("--- methods containing '" + name + "' ---");
        foreach (var t in AllTypes(obj.GetType()))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (!m.IsSpecialName && m.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && m.GetParameters().Length <= 2)
                    diag.AppendLine("  [" + t.Name + "] " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");

        throw new Exception("SetPropForce failed: " + name + " on " + obj.GetType().FullName + "\n" + diag.ToString());
    }

    static string TryCloneV8(string moduleName, string sourceName, string newName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var source = FindServiceAction(es, sourceName);
        if (source == null) return Json(new { ok = false, error = "source service action not found: " + sourceName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(es, "ServiceActions");
        // find Duplicate(IObjectSignature, IObject) - the (IEnumerable<IObjectSignature>, IObject)
        // overload is disambiguated by param[0] being assignable from a single service action.
        MethodInfo dupMethod = null;
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType())) { dupMethod = m; break; }
        }
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) method not found" });

        object created = null; string err = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, es });
                // dup is the new ServiceAPIMethod (IObjectSignature + IServiceAction). Rename it.
                try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + sourceName + ",es)->rename";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: clone service action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, "ServiceActions");
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), serviceActionsBefore = before, serviceActionsAfter = after, error = err });
    }

    // clone_server_action / clone_client_action: DEEP-CLONE a Server Action (UserActions /
    // ServerActions) or Client Action (ClientActions) via IModelServices.Duplicate + rename -
    // the same mechanism as try_clone_v8 (service actions), generalized by collection.
    static string CloneServerAction(string moduleName, string sourceName, string newName)
        => CloneActionCore(moduleName, sourceName, newName, "server", new[] { "UserActions", "ServerActions" });

    static string CloneClientAction(string moduleName, string sourceName, string newName)
        => CloneActionCore(moduleName, sourceName, newName, "client", new[] { "ClientActions" });

    static string CloneActionCore(string moduleName, string sourceName, string newName, string kind, string[] collNames)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        string collUsed = null;
        object source = null;
        foreach (var cn in collNames)
        {
            source = FindActionInCollection(es, cn, sourceName);
            if (source != null) { collUsed = cn; break; }
        }
        if (source == null) return Json(new { ok = false, error = "source " + kind + " action not found: " + sourceName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(es, collUsed);
        MethodInfo dupMethod = null;
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType())) { dupMethod = m; break; }
        }
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) method not found" });
        object created = null; string err = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, es });
                try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + sourceName + ",es)->rename";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: clone " + kind + " action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, collUsed);
        return Json(new { ok = created != null, kind = kind, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), collection = collUsed, before = before, after = after, error = err });
    }

    // remove_dependency: remove a module dependency (a Reference) by producer module name,
    // live, inside a real SS command (undo unit). Finds the Reference in es.References by
    // Name and deletes it (Delete() then Delete(false) fallback). Use after consuming the
    // wrong module or when a producer is no longer needed.
    static string RemoveDependency(string moduleName, string producerName)
    {
        if (string.IsNullOrWhiteSpace(producerName)) return Json(new { ok = false, error = "producer required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object target = null;
        foreach (var r in refs)
        {
            string nm = null;
            try { nm = GetProp(r, "Name") as string; } catch { }
            if (nm == producerName) { target = r; break; }
        }
        if (target == null) return Json(new { ok = false, error = "dependency not found: " + producerName });
        string err = null; string via = null; string typeName = target.GetType().FullName;
        Action mutate = () =>
        {
            try { CallMethod(target, "Delete", null, 0); via = "Delete()"; }
            catch (Exception e1)
            {
                try { CallMethod(target, "Delete", new object[] { false }, 1); via = "Delete(false)"; }
                catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = "Delete() " + FirstMsg(e1) + " ; Delete(false) " + r.GetType().Name + ": " + r.Message; }
            }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: remove dependency", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        bool stillThere = false;
        var refs2 = GetProp(es, "References") as IEnumerable;
        if (refs2 != null)
            foreach (var r in refs2)
            {
                try { if ((GetProp(r, "Name") as string) == producerName) { stillThere = true; break; } } catch { }
            }
        return Json(new { ok = err == null && !stillThere, removed = producerName, via = via, referenceType = typeName, stillPresent = stillThere, error = err });
    }

    // FindServerProcessFor: the ServerProcess registered for this aggregator (Pending first,
    // then Started), or null. Also reports which registry held it.
    static object FindServerProcessFor(object agg, out bool inPending, out bool inStarted)
    {
        inPending = false; inStarted = false;
        var spType = FindType("ServiceStudio.Presenter.Commands.ServerProcess");
        if (spType == null) return null;
        foreach (var (dictName, isPending) in new[] { ("ProcessesPending", true), ("ProcessesStarted", false) })
        {
            var f = spType.GetField(dictName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) continue;
            var lazy = f.GetValue(null);
            var dict = lazy?.GetType().GetProperty("Value")?.GetValue(lazy, null);
            if (dict == null) continue;
            try
            {
                var tryGet = dict.GetType().GetMethod("TryGetValue");
                var args = new object[] { agg, null };
                if ((bool)tryGet.Invoke(dict, args) && args[1] != null)
                {
                    if (isPending) inPending = true; else inStarted = true;
                    return args[1];
                }
            }
            catch { }
        }
        return null;
    }

    // publish_module: in-process 1-Click Publish of the OPEN module - the SAME code path as
    // the F5 button (ServiceStudio.Presenter.Commands.Publish, PublishCommand<Publish,
    // IAggregatorPresenter>). No UI automation, no vision. Decompiled AutoRegistryCommand
    // .Execute: it calls Command.CheckAccess, then TelemetryUtils.Command(desc, () =>
    // InnerExecute(...)) which opens its OWN command via targetPresenter.GetPresenterContext()
    // and routes through ServiceStudio.Commands.Command (the concurrency machinery) - so we
    // must NOT wrap it in Command.ExecuteFromAsyncCode (nested command), and calling it from
    // the pipe thread is exactly the path the button uses. Optional commitMessage bypasses
    // the Shift+F5 dialog by calling the internal Publish.prs#lislasrz(agg, presenter, msg).
    static string PublishModule(string moduleName, string commitMessage, bool wait, int timeoutSec)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        var cmdType = FindType("ServiceStudio.Presenter.Commands.Publish");
        if (cmdType == null) return Json(new { ok = false, error = "ServiceStudio.Presenter.Commands.Publish not loaded (SS version?)" });
        // SS registers every [Command] once (AutoRegistryType<Concrete>.Instance); the UI itself
        // uses the singleton (see PublishWithCommitMessage: AutoRegistryType<Publish>.Instance).
        // Activator.CreateInstance on Publish throws "key already added" because the registry
        // dict already holds it - so resolve the singleton first, fall back to the ctor.
        object cmd = null; string cmdVia = null;
        try
        {
            var regOpen = FindType("ServiceStudio.AutoRegistryType`1");
            var regClosed = regOpen?.MakeGenericType(cmdType);
            cmd = regClosed?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            cmdVia = "AutoRegistryType<Publish>.Instance";
        }
        catch { }
        if (cmd == null)
        {
            try { cmd = Activator.CreateInstance(cmdType); cmdVia = "Activator.CreateInstance"; }
            catch (Exception e) { return Json(new { ok = false, error = "Publish ctor: " + FirstMsg(e) }); }
        }

        MethodInfo exec = null; string execVia = null;
        foreach (var m in cmdType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (string.IsNullOrEmpty(commitMessage))
            {
                if (m.Name == "Execute" && m.GetParameters().Length == 2 && m.IsPublic)
                { exec = m; execVia = "Execute(ICommandTarget,IPresenter)"; break; }
            }
            else if (m.Name == "prs#lislasrz" && m.GetParameters().Length == 3 && m.IsAssembly)
            { exec = m; execVia = "prs#lislasrz(agg,presenter,message)"; break; }
        }
        if (exec == null) return Json(new { ok = false, error = "no usable Execute surface found on Publish" });

        object result = null; string err = null;
        var sw = Stopwatch.StartNew();
        try
        {
            result = string.IsNullOrEmpty(commitMessage)
                ? exec.Invoke(cmd, new object[] { agg, agg })
                : exec.Invoke(cmd, new object[] { agg, agg, commitMessage });
        }
        catch (Exception e) { err = "execute: " + FirstMsg(e); }
        sw.Stop();

        // Await the ServerProcess: Execute returns null immediately (async) - the real publish
        // runs in a ServerProcess registered in ProcessesPending/ProcessesStarted, which
        // removes itself when finished. Poll until gone so the tool can report the outcome.
        bool processSeen = false; bool stillRunning = false;
        string lastState = null, lastInner = null; long waitMs = 0;
        if (wait)
        {
            var waitSw = Stopwatch.StartNew();
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(10, timeoutSec));
            while (DateTime.UtcNow < deadline)
            {
                var sp = FindServerProcessFor(agg, out bool inPending, out bool inStarted);
                if (sp == null) { stillRunning = false; break; }
                processSeen = true; stillRunning = true;
                lastState = SafeGetProp(sp, "CurrentState")?.ToString() ?? lastState;
                lastInner = SafeGetProp(sp, "InnerState")?.ToString() ?? lastInner;
                Thread.Sleep(1500);
            }
            waitMs = waitSw.ElapsedMilliseconds;
        }

        // Pre-flight diagnostics whenever Execute yielded no CommandResult: the decompiled
        // PublishCommand returns null for access-denied, already-publishing, debug session,
        // or a declined confirm dialog (e.g. "publish to Production?"). Surface the conditions.
        var diag = new Dictionary<string, object>();
        try
        {
            var canExec = FindMethod(cmd, "CanExecute", 2)?.Invoke(cmd, new object[] { agg, agg });
            diag["CanExecute"] = canExec?.ToString() ?? "null";
        }
        catch (Exception e) { diag["CanExecute_err"] = FirstMsg(e); }
        try
        {
            var baseType = FindType("ServiceStudio.Presenter.Commands.PublishCommand`2");
            if (baseType != null)
            {
                Type aggIface = agg.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IAggregatorPresenter") ?? agg.GetType();
                var closed = baseType.MakeGenericType(cmdType, aggIface);
                foreach (var m in closed.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    if (m.Name == "ESpaceCanBePublished")
                        diag["ESpaceCanBePublished"] = m.Invoke(null, new object[] { agg })?.ToString() ?? "null";
            }
        }
        catch (Exception e) { diag["ESpaceCanBePublished_err"] = FirstMsg(e); }
        // Guard-value reads (why ExecutePublish returns null):
        diag["HasOpenedActiveESpace"] = SafeGetProp(agg, "HasOpenedActiveESpace")?.ToString() ?? "null";
        var activeEs = SafeGetProp(agg, "ActiveESpace");
        diag["ActiveESpace"] = activeEs != null ? (SafeGetProp(activeEs, "Name")?.ToString() ?? activeEs.GetType().Name) : "null";
        diag["ActiveESpace.LastSavePath"] = activeEs != null ? (SafeGetProp(activeEs, "LastSavePath")?.ToString() ?? "null") : "null";
        diag["ActiveESpace.PublishRetryCount"] = activeEs != null ? (SafeGetProp(activeEs, "PublishRetryCount")?.ToString() ?? "null") : "null";
        var sscp = SafeGetProp(agg, "ServerCommunicationsProvider");
        diag["InstallationKind"] = sscp != null ? (SafeGetProp(sscp, "InstallationKind")?.ToString() ?? "null") : "null";
        try
        {
            var dm = FindType("ServiceStudio.Presenter.Debugger.DebuggerManager");
            if (dm != null)
                foreach (var m in dm.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    if (m.Name == "HasOpenDebugSession" && m.GetParameters().Length == 3)
                        diag["HasOpenDebugSession"] = m.Invoke(null, new object[] { agg, activeEs, false })?.ToString() ?? "null";
        }
        catch (Exception e) { diag["HasOpenDebugSession_err"] = FirstMsg(e); }
        try
        {
            var spType = FindType("ServiceStudio.Presenter.Commands.ServerProcess");
            if (spType != null)
            {
                foreach (var m in spType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    if (m.Name == "prs#sifwvgpc" && m.GetParameters().Length == 1)
                    {
                        var sp = m.Invoke(null, new object[] { agg });
                        diag["ServerProcessActive"] = sp != null ? (SafeGetProp(sp, "CurrentState")?.ToString() ?? sp.GetType().Name) : "null";
                        if (sp != null)
                        {
                            diag["ServerProcessType"] = sp.GetType().FullName;
                            diag["ServerProcess.Name"] = SafeGetProp(sp, "Name")?.ToString() ?? "null";
                            diag["ServerProcess.Description"] = SafeGetProp(sp, "Description")?.ToString() ?? "null";
                            foreach (var p in sp.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (p.GetIndexParameters().Length != 0) continue;
                                if (!(p.Name.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      p.Name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      p.Name.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                                try { diag["ServerProcess." + p.Name] = p.GetValue(sp, null)?.ToString() ?? "null"; } catch { }
                            }
                        }
                    }
            }
        }
        catch (Exception e) { diag["ServerProcess_err"] = FirstMsg(e); }
        try
        {
            var spType = FindType("ServiceStudio.Presenter.Commands.ServerProcess");
            if (spType != null)
            {
                foreach (var dictName in new[] { "ProcessesPending", "ProcessesStarted" })
                {
                    var f = spType.GetField(dictName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f == null) continue;
                    var lazy = f.GetValue(null);
                    var dict = lazy?.GetType().GetProperty("Value")?.GetValue(lazy, null);
                    if (dict == null) { diag["SP." + dictName] = "null"; continue; }
                    var tryGet = dict.GetType().GetMethod("TryGetValue");
                    var args = new object[] { agg, null };
                    bool has = (bool)tryGet.Invoke(dict, args);
                    diag["SP." + dictName + ".HasThisAgg"] = has.ToString();
                    diag["SP." + dictName + ".Count"] = dict.GetType().GetProperty("Count")?.GetValue(dict, null)?.ToString() ?? "?";
                    if (has && args[1] != null)
                    {
                        diag["SP." + dictName + ".Type"] = args[1].GetType().FullName;
                        diag["SP." + dictName + ".State"] = SafeGetProp(args[1], "CurrentState")?.ToString() ?? "null";
                    }
                }
            }
        }
        catch (Exception e) { diag["SP_dict_err"] = FirstMsg(e); }
        try
        {
            var rt = FindType("ServiceStudio.Presenter.Runtime");
            var inst = rt?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            if (inst != null)
            {
                foreach (var p in inst.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (p.GetIndexParameters().Length != 0) continue;
                    if (!(p.Name.IndexOf("ublish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          p.Name.IndexOf("Executing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          p.Name.IndexOf("nattended", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          p.Name.IndexOf("RunningUnitTests", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    try { diag["Runtime." + p.Name] = p.GetValue(inst, null)?.ToString() ?? "null"; } catch { }
                }
                foreach (var f in inst.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (!(f.Name.IndexOf("ublish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          f.Name.IndexOf("Executing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          f.Name.IndexOf("nattended", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    try { diag["RuntimeField." + f.Name] = f.GetValue(inst)?.ToString() ?? "null"; } catch { }
                }
            }
        }
        catch { }

        var props = new Dictionary<string, object>();
        if (result != null)
        {
            foreach (var p in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    var v = p.GetValue(result, null);
                    if (v == null) props[p.Name] = "null";
                    else if (v is string || v.GetType().IsPrimitive || v is bool || v is Guid) props[p.Name] = v.ToString();
                    else if (v is IEnumerable) { int n = 0; foreach (var _ in (IEnumerable)v) n++; props[p.Name] = v.GetType().Name + "[" + n + "]"; }
                    else props[p.Name] = v.GetType().Name + " (" + v + ")";
                }
                catch { props[p.Name] = "<unreadable>"; }
            }
        }
        bool finished = processSeen && !stillRunning;
        return Json(new
        {
            ok = err == null && (result != null || finished),
            module = moduleName,
            via = execVia,
            commitMessageWarning = string.IsNullOrEmpty(commitMessage) ? null : "message path (prs#lislasrz) has been observed leaving the publish process STUCK at Uploading - prefer no commitMessage",
            elapsedMs = sw.ElapsedMilliseconds,
            wait = wait,
            processSeen = processSeen,
            finished = finished,
            stillRunning = stillRunning,
            lastState = lastState,
            lastInner = lastInner,
            waitMs = waitMs,
            resultType = result?.GetType().FullName,
            resultProps = props,
            diag = diag,
            error = err
        });
    }

    // debug_publish_surface: read-only dump of the publish API surface - command type load,
    // ctor access, Execute candidates, aggregator IsUnattended (True = no confirm dialogs),
    // and whether ExecuteInContext(Action,bool) exists. Use BEFORE publish_module on a new
    // SS build to confirm the surface is unchanged.
    static string DebugPublishSurface(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        var cmdType = FindType("ServiceStudio.Presenter.Commands.Publish");
        if (cmdType == null) return Json(new { ok = false, error = "Publish type not loaded" });
        var methods = new List<object>();
        foreach (var m in cmdType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            methods.Add(new { name = m.Name, access = m.IsPublic ? "public" : (m.IsAssembly ? "internal" : "private"), paramsCount = m.GetParameters().Length, ret = m.ReturnType.Name });
        bool? unattended = null;
        foreach (var p in agg.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.Name == "IsUnattended") { try { unattended = (bool?)p.GetValue(agg, null); } catch { } }
        bool hasExecInCtx = false;
        foreach (var m in agg.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (m.Name == "ExecuteInContext" && m.GetParameters().Length == 2) { hasExecInCtx = true; break; }
        return Json(new { ok = true, module = moduleName, commandType = cmdType.FullName, methods = methods, aggregatorIsUnattended = unattended, aggregatorHasExecuteInContext2 = hasExecInCtx });
    }

    // debug_publish_state: read-only monitor for an in-flight in-process publish. Reports
    // whether a ServerProcess is registered for the aggregator (Pending/Started), its type,
    // CurrentState + InnerState. Does NOT start anything - use to poll a publish_module call.
    static string DebugPublishState(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        var d = new Dictionary<string, object>();
        var spType = FindType("ServiceStudio.Presenter.Commands.ServerProcess");
        if (spType == null) return Json(new { ok = false, error = "ServerProcess type not loaded" });
        foreach (var dictName in new[] { "ProcessesPending", "ProcessesStarted" })
        {
            var f = spType.GetField(dictName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) continue;
            var lazy = f.GetValue(null);
            var dict = lazy?.GetType().GetProperty("Value")?.GetValue(lazy, null);
            if (dict == null) { d[dictName] = "null"; continue; }
            var tryGet = dict.GetType().GetMethod("TryGetValue");
            var args = new object[] { agg, null };
            bool has = false;
            try { has = (bool)tryGet.Invoke(dict, args); } catch { }
            if (has && args[1] != null)
            {
                d[dictName] = new
                {
                    type = args[1].GetType().FullName,
                    currentState = SafeGetProp(args[1], "CurrentState")?.ToString() ?? "null",
                    innerState = SafeGetProp(args[1], "InnerState")?.ToString() ?? "null"
                };
            }
            else d[dictName] = "none";
        }
        return Json(new { ok = true, module = moduleName, serverProcess = d });
    }

    // UnwrapWritable: ReadOnlySSCollectionAdapter wraps the real ISSCollection in a private
    // field - walk down (max 3 levels) to find a collection object whose declared type is NOT
    // a ReadOnly adapter, and return it. Returns the input when nothing to unwrap.
    static object UnwrapWritable(object coll, int depth)
    {
        if (coll == null || depth > 3) return coll;
        var t = coll.GetType();
        var isReadOnlyAdapter = false;
        for (var bt = t; bt != null; bt = bt.BaseType)
            if (bt.Name.StartsWith("ReadOnlySSCollectionAdapter") || bt.Name.StartsWith("ReadOnlySSSequenceAdapter")) { isReadOnlyAdapter = true; break; }
        if (!isReadOnlyAdapter) return coll;
        foreach (var tt in AllTypes(t))
            foreach (var f in tt.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.FieldType.IsGenericType) continue;
                var defName = f.FieldType.GetGenericTypeDefinition().Name;
                if (defName != "ISSCollection`1" && defName != "ISSSequence`1" && !f.FieldType.Name.StartsWith("ISSCollection")) continue;
                try { var v = f.GetValue(coll); if (v != null && !object.ReferenceEquals(v, coll)) return UnwrapWritable(v, depth + 1); } catch { }
            }
        return coll;
    }

    // FindWritableCollection(s): the WRITABLE ISSCollection<T> for an element type. GetProp on
    // the ESpace returns READ-ONLY adapters (ReadOnlySSCollectionAdapter) with no Create/Add;
    // the writable ISSCollection lives in a private ESpace field. Scan all instance fields for
    // an ISSCollection`1 / ISSSequence`1 whose element type name matches.
    static List<object> FindWritableCollections(object es, string elementTypeName)
    {
        var result = new List<object>();
        foreach (var t in AllTypes(es.GetType()))
        {
            foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                if (ft == null || !ft.IsGenericType) continue;
                var defName = ft.GetGenericTypeDefinition().Name;
                if (defName != "ISSCollection`1" && defName != "ISSSequence`1") continue;
                if (ft.GetGenericArguments()[0].Name != elementTypeName) continue;
                try { var v = f.GetValue(es); if (v != null && !result.Contains(v)) result.Add(v); } catch { }
            }
        }
        return result;
    }

    // TryRunOnUIThread: marshal an Action to SS's UI thread - WPF Application.Current.Dispatcher
    // first, then Avalonia Dispatcher.UIThread. Returns false when neither dispatcher exists.
    // Needed because synchronous UI-bound commands (Save) deadlock the pipe when run directly
    // on the pipe thread (observed 2026-09-25: save_module wedged the bridge).
    static bool TryRunOnUIThread(Action work, out string usedDispatcher, out string err)
    {
        usedDispatcher = null; err = null;
        var errs = new List<string>();
        // WPF
        try
        {
            var appType = FindType("System.Windows.Application");
            var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            var disp = app != null ? appType.GetProperty("Dispatcher")?.GetValue(app, null) : null;
            if (disp != null)
            {
                var inv = disp.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Invoke" && m.GetParameters().Length >= 1 && typeof(Delegate).IsAssignableFrom(m.GetParameters()[0].ParameterType) && !m.IsGenericMethod);
                if (inv != null)
                {
                    var args = new List<object> { (Delegate)work };
                    foreach (var p in inv.GetParameters().Skip(1)) args.Add(p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                    inv.Invoke(disp, args.ToArray());
                    usedDispatcher = "wpf";
                    return true;
                }
            }
        }
        catch (Exception e) { errs.Add("wpf: " + FirstMsg(e)); }
        // Avalonia
        try
        {
            var dt = FindType("Avalonia.Threading.Dispatcher");
            var uit = dt?.GetProperty("UIThread", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
            if (uit != null)
            {
                var inv = uit.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Invoke" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Action) && !m.IsGenericMethod);
                if (inv != null) { inv.Invoke(uit, new object[] { work }); usedDispatcher = "avalonia"; return true; }
            }
        }
        catch (Exception e) { errs.Add("avalonia: " + FirstMsg(e)); }
        err = string.Join(" ; ", errs);
        return false;
    }

    // save_module: Ctrl+S equivalent in-process - invokes the registered Save command
    // (ServiceStudio.Presenter.Commands.ESpaceCommands/Save) via AutoRegistryType<Save>.Instance
    // .Execute(agg, agg). Save is UI-bound (unlike Publish's async ServerProcess): it MUST run
    // on the UI dispatcher - direct pipe-thread invocation wedges the bridge (observed).
    static string SaveModule(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        var saveType = FindType("ServiceStudio.Presenter.Commands.ESpaceCommands+Save");
        if (saveType == null) return Json(new { ok = false, error = "ESpaceCommands+Save type not loaded" });
        object cmd = null; string via = null;
        try
        {
            var regOpen = FindType("ServiceStudio.AutoRegistryType`1");
            var regClosed = regOpen?.MakeGenericType(saveType);
            cmd = regClosed?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            via = "AutoRegistryType<Save>.Instance";
        }
        catch { }
        if (cmd == null)
        {
            try { cmd = Activator.CreateInstance(saveType); via = "Activator.CreateInstance"; }
            catch (Exception e) { return Json(new { ok = false, error = "Save ctor: " + FirstMsg(e) }); }
        }
        MethodInfo exec = null;
        foreach (var m in cmd.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (m.Name == "Execute" && m.GetParameters().Length == 2) { exec = m; break; }
        if (exec == null) return Json(new { ok = false, error = "Execute(ICommandTarget,IPresenter) not found on Save" });
        object result = null; string err = null; string via2 = null;
        var sw = Stopwatch.StartNew();
        Action work = () => { result = exec.Invoke(cmd, new object[] { agg, agg }); };
        if (TryRunOnUIThread(work, out var dispatcher, out var uiErr)) via2 = "ui-dispatcher:" + dispatcher;
        else
        {
            if (!string.IsNullOrEmpty(uiErr)) Log("save_module: no UI dispatcher found (" + uiErr + ") - running on pipe thread");
            try { work(); via2 = "pipe-thread"; }
            catch (Exception e) { err = "execute: " + FirstMsg(e); }
        }
        sw.Stop();
        return Json(new { ok = err == null, module = moduleName, via = via + " / " + via2, elapsedMs = sw.ElapsedMilliseconds, resultType = result?.GetType().FullName, resultNull = result == null, error = err });
    }

    // open_producer_module: open a consumed producer module in a NEW SS tab, in-process.
    // Resolves the Reference's ReferenceKey (ObjectKey of the producer espace) and calls the
    // UI's own open engine AcrossTabCommands.prs#nexcffly(agg, key, name, null, true, null) -
    // the same static the "Open Producer" context-menu command uses.
    static string OpenProducerModule(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object target = null;
        foreach (var r in refs)
        {
            string nm = null;
            try { nm = GetProp(r, "Name") as string; } catch { }
            if (nm == referenceName) { target = r; break; }
        }
        if (target == null)
        {
            var known = new List<string>();
            foreach (var r in refs) { try { known.Add(GetProp(r, "Name") as string ?? "?"); } catch { } }
            return Json(new { ok = false, error = "reference not found: " + referenceName, references = known });
        }
        var key = GetProp(target, "ReferenceKey");
        if (key == null) return Json(new { ok = false, error = "ReferenceKey is null on reference " + referenceName });
        var at = FindType("ServiceStudio.Presenter.Commands.AcrossTabCommands");
        if (at == null) return Json(new { ok = false, error = "AcrossTabCommands type not loaded" });
        MethodInfo open = null;
        foreach (var m in at.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
            if (m.Name == "prs#nexcffly" && m.GetParameters().Length == 6) { open = m; break; }
        if (open == null) return Json(new { ok = false, error = "open engine prs#nexcffly not found" });
        string err = null;
        var sw = Stopwatch.StartNew();
        try { open.Invoke(null, new object[] { agg, key, referenceName, null, true, null }); }
        catch (Exception e) { err = "open: " + FirstMsg(e); }
        sw.Stop();
        return Json(new { ok = err == null, opened = referenceName, elapsedMs = sw.ElapsedMilliseconds, error = err });
    }

    // clone_service_action_from: create a REAL local Service Action in the consumer by
    // consuming + IModelServices.Duplicate from an OPEN producer module - the SS copy/paste
    // mechanism, which bypasses the "Service Action objects can't be children of Module
    // objects" create-from-scratch validation. The clone is an exact copy with a fresh key,
    // renamed to newName, living in consumer.ServiceActions.
    static string CloneServiceActionFrom(string consumerName, string producerName, string sourceName, string newName)
    {
        var consumerEs = FindEspace(consumerName);
        if (consumerEs == null) return Json(new { ok = false, error = "consumer module not found: " + consumerName });
        var producerEs = FindEspace(producerName);
        if (producerEs == null) return Json(new { ok = false, error = "producer module not found (open it first): " + producerName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var source = FindServiceAction(producerEs, sourceName);
        if (source == null) return Json(new { ok = false, error = "service action not found in producer: " + sourceName });
        var agg = GetContext(consumerEs);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        MethodInfo dupMethod = null;
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType())) { dupMethod = m; break; }
        }
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) not found" });
        MethodInfo dupMany = null;
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2 || ps[0].ParameterType.Name.StartsWith("IObjectSignature")) continue;
            if (ps[0].ParameterType.IsGenericType && ps[0].ParameterType.GetGenericArguments().Length == 1
                && ps[0].ParameterType.GetGenericArguments()[0].IsAssignableFrom(source.GetType())) dupMany = m;
        }
        int before = CountProp(consumerEs, "ServiceActions");
        object created = null; string err = null; string via = null;
        var diag = new List<string>();
        var routeErrs = new List<string>();
        // Paste targets: the model routes cross-espace Duplicate with the ESPACE as target
        // (decompiled prs#miuspfqw::Duplicate -> DeserializeInto((IPasteTarget)target, ...)).
        // NOTE: "ServiceActions" GetProp is a lazy OfType iterator, NOT the collection - never use it as target.
        object pasteTarget = consumerEs;
        diag.Add("pasteTarget=" + pasteTarget.GetType().Name + " isEspace=" + object.ReferenceEquals(pasteTarget, consumerEs));
        Action mutate = () =>
        {
            try
            {
                // Route A: single Duplicate. Route B: many Duplicate. Route C: manual
                // ClipboardManager.CopyToClipboard + DeserializeInto (the UI's own path).
                try
                {
                    var dup = dupMethod.Invoke(ms, new object[] { source, pasteTarget });
                    try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                    created = dup;
                    via = "Duplicate(producer.ServiceAction, ServiceActions->rename";
                    return;
                }
                catch (Exception eA) { routeErrs.Add("A: " + FullMsg(eA)); }
                if (dupMany != null)
                {
                    try
                    {
                        var items = (IEnumerable)Activator.CreateInstance(typeof(List<>).MakeGenericType(source.GetType()));
                        ((System.Collections.IList)items).Add(source);
                        var dups = dupMany.Invoke(ms, new object[] { items, pasteTarget });
                        object dup = null;
                        foreach (var o in (IEnumerable)dups) { dup = o; break; }
                        if (dup == null) throw new Exception("many Duplicate returned no items");
                        try { SetProp(dup, "Name", newName); } catch (Exception e2) { var rr = e2; while (rr.InnerException != null) rr = rr.InnerException; err = "rename: " + rr.GetType().Name + ": " + rr.Message; }
                        created = dup;
                        via = "Duplicate([]producer.ServiceAction, consumerEs)->rename (fallback)";
                        return;
                    }
                    catch (Exception eB) { routeErrs.Add("B: " + FullMsg(eB)); }
                }
                // Route D: the EXACT recipe decompiled from prs#miuspfqw::Duplicate/Serialize/
                // DeserializeInto - MockPresenter(MockAggregatorPresenter) + static
                // ClipboardManager.Serialize(IEnumerable<AbstractObject>, IPresenter, CT) then
                // DeserializeInto(bytes, (IPasteTarget)espace, espace, mockPresenter).
                try
                {
                    var aoType = FindType("ServiceStudio.Model.AbstractObject");
                    var aggCtorType = FindType("ServiceStudio.Presenter.Commands.MockAggregatorPresenter");
                    var mockPType = FindType("ServiceStudio.Presenter.Commands.MockPresenter");
                    var cm = FindType("ServiceStudio.CopyPaste.ClipboardManager");
                    if (aoType != null && aggCtorType != null && mockPType != null && cm != null)
                    {
                        var listType = typeof(List<>).MakeGenericType(aoType);
                        var aoList = (IEnumerable)Activator.CreateInstance(listType);
                        ((System.Collections.IList)aoList).Add(source);
                        ConstructorInfo aggCtor = null;
                        try { aggCtor = aggCtorType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(c => c.GetParameters().Length).FirstOrDefault(); } catch { }
                        object mockAgg = null, mockP = null;
                        object realProvider = null;
                        try { realProvider = SafeGetProp(agg, "ServerCommunicationsProvider"); } catch { }
                        try
                        {
                            if (aggCtor != null)
                            {
                                var ps = aggCtor.GetParameters();
                                var args = new object[ps.Length];
                                for (int ai = 0; ai < ps.Length; ai++)
                                {
                                    var pt = StripNullable(ps[ai].ParameterType);
                                    if (ps[ai].ParameterType.Name.Contains("ServerCommunicationsProvider") && realProvider != null) { args[ai] = realProvider; continue; }
                                    if (ps[ai].ParameterType.Name.Contains("ESpace") && ps[ai].Name == "activeESpace") { args[ai] = consumerEs; continue; }
                                    if (pt.IsValueType) args[ai] = Activator.CreateInstance(pt);
                                    else args[ai] = null;
                                    if (ps[ai].Name == "registerAsStaticInstance") args[ai] = false;
                                }
                                mockAgg = aggCtor.Invoke(args);
                            }
                        }
                        catch (Exception eCtor) { routeErrs.Add("D-ctor: " + FirstMsg(eCtor)); }
                        if (mockAgg != null)
                        {
                            ConstructorInfo mockCtor = null;
                            try
                            {
                                foreach (var c in mockPType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    var ps = c.GetParameters();
                                    if (ps.Length != 1) continue;
                                    try { if (ps[0].ParameterType.IsInstanceOfType(mockAgg)) { mockCtor = c; break; } } catch { }
                                }
                            }
                            catch { }
                            if (mockCtor == null)
                                try { mockCtor = mockPType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == 1); } catch { }
                            if (mockCtor != null) mockP = mockCtor.Invoke(new object[] { mockAgg });
                        }
                        MethodInfo ser = null, deser = null;
                        foreach (var m in cm.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        {
                            if (m.Name == "Serialize" && m.GetParameters().Length == 3) ser = m;
                            if (m.Name == "DeserializeInto" && m.GetParameters().Length == 4) deser = m;
                        }
                        diag.Add("D: aggType=" + (aggCtorType?.FullName ?? "null") + " aggCtorFound=" + (aggCtor != null) + " mockAgg=" + (mockAgg != null) + " mockP=" + (mockP != null) + " mockAss=" + (mockPType?.FullName ?? "null"));
                        if (ser != null && deser != null && mockP != null)
                        {
                            // Serialize with the REAL aggregator presenter (UI behaviour:
                            // SourceSelection needs a real presenter to emit objects), then
                            // deserialize with the mocks (all paste gates pass there).
                            object serializePresenter = agg;
                            if (!(agg is object)) serializePresenter = mockP;
                            byte[] bytes = ser.Invoke(null, new object[] { aoList, serializePresenter, CancellationToken.None }) as byte[];
                            var consumerCode = SafeGetProp(consumerEs, "ActivationCode") as string;
                            var producerCode = SafeGetProp(producerEs, "ActivationCode") as string;
                            var patched = false;
                            if (string.IsNullOrEmpty(consumerCode) || consumerCode != producerCode)
                            {
                                bytes = PatchActivationCode(bytes, consumerCode);
                                patched = true;
                            }
                            diag.Add("D: patched=" + patched + " consumerCode=" + (consumerCode ?? "<null>") + " producerCode=" + (producerCode ?? "<null>"));
                            string patchedAttr = "?";
                            try
                            {
                                var omlType2 = FindType("OutSystems.Model.Implementation.Oml.Oml");
                                var rm2 = omlType2.GetMethod("CreateXmlReader", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                                using (var ms2 = new MemoryStream(bytes))
                                using (var rd2 = (XmlReader)rm2.Invoke(null, new object[] { ms2 }))
                                {
                                    var doc2 = XDocument.Load(rd2);
                                    patchedAttr = (doc2.Root?.Attribute("ActivationCode")?.Value ?? "<none>") + " root=" + (doc2.Root?.Name.LocalName ?? "?");
                                }
                            }
                            catch (Exception ep) { patchedAttr = "decode-err:" + FirstMsg(ep); }
                            diag.Add("D: patchedAttr=" + patchedAttr + " patchedBytes=" + (bytes != null ? bytes.Length : -1) + " realProvider=" + (realProvider != null));
                            diag.Add("D: consumerAC=" + (SafeGetProp(consumerEs, "ActivationCode") as string) + " producerAC=" + (SafeGetProp(producerEs, "ActivationCode") as string));
                            try
                            {
                                var licType = FindType("ServiceStudio.ServerCommunications.ServerLicensingInfo");
                                var licMethod = licType?.GetMethod("CheckLocalForeignCodes", BindingFlags.Public | BindingFlags.Static);
                                var ctType = FindType("ServiceStudio.ServerCommunications.ServerLicensingInfoCheckType");
                                object checkType = ctType != null ? Enum.Parse(ctType, "CopyPaste") : null;
                                if (licMethod != null && checkType != null)
                                {
                                    var licRes = licMethod.Invoke(null, new object[] { mockAgg, SafeGetProp(consumerEs, "ActivationCode"), patchedAttr.Split(' ')[0], checkType, consumerEs });
                                    diag.Add("D: lic=" + licRes);
                                }
                                else diag.Add("D: lic-missing licM=" + (licMethod != null) + " ct=" + (checkType != null));
                            }
                            catch (Exception el) { diag.Add("D: lic-err=" + FirstMsg(el)); }
                            if (bytes != null && bytes.Length > 0)
                            {
                                try
                                {
                                    var forM = cm.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static);
                                    object mgr = forM?.Invoke(null, new object[] { bytes });
                                    if (mgr != null)
                                    {
                                        MethodInfo canM = null;
                                        foreach (var m in mgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                                            if (m.Name == "CanPasteObjectsInto" && (m.GetParameters().Length == 2 || m.GetParameters().Length == 3)) { canM = m; break; }
                                        if (canM == null)
                                            foreach (var m in mgr.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                                                if (m.Name == "CanPasteObjectsInto" && m.GetParameters().Length == 3) { canM = m; break; }
                                        if (canM != null)
                                        {
                                            var canArgs = new object[] { consumerEs, mockP, false };
                                            var canRes = canM.Invoke(mgr, canArgs);
                                            diag.Add("D: canPaste=" + canRes + " usesConv=" + (canArgs[2]?.ToString() ?? "null"));
                                            try
                                            {
                                                var cik = consumerEs.GetType().GetMethod("CanImportKind", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                                if (cik != null)
                                                {
                                                    var kindObj = SafeGetProp(source, "AbstractObjectKind");
                                                    var cikArgs = new object[] { kindObj, null, null };
                                                    var cikRes = cik.Invoke(consumerEs, cikArgs);
                                                    diag.Add("D: CanImportKind=" + cikRes + " kind=" + (kindObj?.GetType().Name ?? "null"));
                                                }
                                                else diag.Add("D: CanImportKind method not found");
                                            }
                                            catch (Exception ecik) { diag.Add("D: CanImportKind-err=" + FirstMsg(ecik)); }
                                        }
                                        else diag.Add("D: CanPasteObjectsInto not found");
                                    }
                                    else diag.Add("D: For returned null");
                                }
                                catch (Exception ecp) { diag.Add("D: canPaste-err=" + FirstMsg(ecp)); }
                                var objs = deser.Invoke(null, new object[] { bytes, consumerEs, null, mockP }) as IEnumerable;
                                object dup = null; foreach (var o in objs) { dup = o; break; }
                                if (dup != null)
                                {
                                    try { SetProp(dup, "Name", newName); } catch (Exception e2) { var rr = e2; while (rr.InnerException != null) rr = rr.InnerException; err = "rename: " + rr.GetType().Name + ": " + rr.Message; }
                                    created = dup;
                                    via = "clipboard.Serialize+DeserializeInto(MockPresenter) [UI recipe]";
                                    return;
                                }
                                routeErrs.Add("D: deserialize returned no objects");
                            }
                            else routeErrs.Add("D: serialize returned " + (bytes?.Length ?? -1).ToString() + " bytes");
                        }
                        else routeErrs.Add("D: missing methods ser=" + (ser != null) + " deser=" + (deser != null) + " mockP=" + (mockP != null));
                    }
                    else routeErrs.Add("D: type missing ao=" + (aoType != null) + " agg=" + (aggCtorType != null) + " mock=" + (mockPType != null) + " cm=" + (cm != null));
                }
                catch (Exception eD2) { routeErrs.Add("D: " + FullMsg(eD2)); }
                // Route C: the UI's own copy->paste against the consumer aggs.
                try
                {
                    var cm = FindType("ServiceStudio.CopyPaste.ClipboardManager");
                    var copyM = cm.GetMethod("CopyToClipboard", BindingFlags.Public | BindingFlags.Static);
                    var pasteM = cm.GetMethod("DeserializeInto", BindingFlags.Public | BindingFlags.Static);
                    var rt = FindType("ServiceStudio.Runtime") ?? FindType("ServiceStudio.RuntimeCommon.Runtime");
                    object aggP = null;
                    bool aggIsPresenter = false;
                    try { foreach (var i in agg.GetType().GetInterfaces()) if (i.Name == "IPresenter") { aggIsPresenter = true; break; } } catch { }
                    try { aggP = GetProp(agg, "Presenter"); } catch { }
                    if (aggP == null && aggIsPresenter) aggP = agg;
                    diag.Add("aggIsIPresenter=" + aggIsPresenter + " aggPresenter=" + (aggP == null ? "null" : aggP.GetType().Name));
                    if (copyM != null && pasteM != null && aggP != null)
                    {
                        var aoType = FindType("ServiceStudio.Model.AbstractObject");
                        var listType = typeof(List<>).MakeGenericType(aoType ?? source.GetType());
                        var items = (IEnumerable)Activator.CreateInstance(listType);
                        ((System.Collections.IList)items).Add(source);
                        copyM.Invoke(null, new object[] { items, aggP, CancellationToken.None });
                        byte[] bytes = null;
                        if (rt != null)
                        {
                            var gi = rt.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                            object inst = gi?.GetValue(null, null);
                            if (inst != null)
                            {
                                var gcm = inst.GetType().GetMethod("GetClipboardOmlData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (gcm != null) bytes = gcm.Invoke(inst, null) as byte[];
                            }
                        }
                        if (bytes != null)
                        {
                            bytes = PatchActivationCode(bytes, SafeGetProp(consumerEs, "ActivationCode") as string);
                            var objs = pasteM.Invoke(null, new object[] { bytes, pasteTarget, null, aggP }) as IEnumerable;
                            object dup = null; foreach (var o in objs) { dup = o; break; }
                            if (dup != null)
                            {
                                try { SetProp(dup, "Name", newName); } catch (Exception e2) { var rr = e2; while (rr.InnerException != null) rr = rr.InnerException; err = "rename: " + rr.GetType().Name + ": " + rr.Message; }
                                created = dup;
                                via = "ClipboardManager.CopyToClipboard+DeserializeInto (UI path)";
                                return;
                            }
                            routeErrs.Add("C: paste returned no objects");
                        }
                        else routeErrs.Add("C: GetClipboardOmlData null");
                    }
                    else routeErrs.Add("C: missing methods/presenter copy=" + (copyM != null) + " paste=" + (pasteM != null) + " aggP=" + (aggP != null));
                }
                catch (Exception eC) { routeErrs.Add("C: " + FullMsg(eC)); }
            }
            catch (Exception e) { err = "mutate: " + e.ToString().Replace("\r", " ").Replace("\n", " | "); }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: clone service action from producer", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(consumerEs, "ServiceActions");
        try
        {
            var cmdSvcsField = ms.GetType().GetField("commandServicesInstance", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            object cmdSvcs = cmdSvcsField?.GetValue(ms);
            diag.Add("commandServices=" + (cmdSvcs == null ? "null" : cmdSvcs.GetType().FullName));
            object pv = null; bool hasP = false;
            try { hasP = TryGetProp(agg, "Presenter", out pv); } catch { }
            diag.Add("agg=" + agg.GetType().FullName + " hasPresenterProp=" + hasP + " presenter=" + (pv == null ? "null" : pv.GetType().Name));
            diag.Add("routes=" + string.Join(" ; ", routeErrs));
        }
        catch (Exception eD) { diag.Add("diag: " + FirstMsg(eD)); }
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), before = before, after = after, error = err, diag = diag });
    }

    // PatchActivationCode: the clipboard XML carries the PRODUCER's ActivationCode;
    // IPPCanPaste -> ServerLicensingInfo.CheckLocalForeignCodes blocks foreign codes
    // (the UI's "copied from another environment" dialog). Rewrite the attribute to the
    // consumer's code so the model API path pastes like a same-environment copy.
    static byte[] PatchActivationCode(byte[] bytes, string code)
    {
        try
        {
            var omlType = FindType("OutSystems.Model.Implementation.Oml.Oml");
            if (omlType == null) return bytes;
            var rm = omlType.GetMethod("CreateXmlReader", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var wm = omlType.GetMethod("CreateXmlWriter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (rm == null || wm == null) return bytes;
            XDocument doc;
            using (var msIn = new MemoryStream(bytes))
            using (var reader = (XmlReader)rm.Invoke(null, new object[] { msIn }))
            {
                doc = XDocument.Load(reader);
            }
            if (doc.Root == null) return bytes;
            var att = doc.Root.Attribute("ActivationCode");
            if (att == null) { att = new XAttribute("ActivationCode", ""); doc.Root.Add(att); }
            att.Value = code ?? "";
            using (var msOut = new MemoryStream())
            {
                using (var writer = (XmlWriter)wm.Invoke(null, new object[] { msOut }))
                {
                    doc.Save(writer);
                    writer.Flush();
                }
                return msOut.ToArray();
            }
        }
        catch { return bytes; }
    }

    // FullMsg: deepest exception plus up to 3 stack frames for diagnostics.
    static string FullMsg(Exception e)
    {
        var r = e;
        while (r.InnerException != null) r = r.InnerException;
        var sb = new StringBuilder();
        sb.Append(r.GetType().Name).Append(": ").Append(r.Message);
        if (r.StackTrace != null)
        {
            foreach (var line in r.StackTrace.Split('\n'))
            {
                if (line.Trim().StartsWith("at ")) { sb.Append(" | ").Append(line.Trim()); }
            }
        }
        return sb.ToString();
    }

    // clone_element: same-module generic clone of a model element (entity / structure /
    // serviceaction / serveraction / clientaction) via IModelServices.Duplicate + rename.
    static string CloneElement(string moduleName, string kind, string name, string newName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        object source = null; string collUsed = null;
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "serviceaction": case "service_action": case "service":
                source = FindServiceAction(es, name); collUsed = "ServiceActions"; break;
            case "serveraction": case "server":
                foreach (var cn in new[] { "UserActions", "ServerActions" }) { source = FindActionInCollection(es, cn, name); if (source != null) { collUsed = cn; break; } }
                break;
            case "clientaction": case "client":
                source = FindActionInCollection(es, "ClientActions", name); collUsed = "ClientActions"; break;
            case "entity": source = FindEntity(es, name); collUsed = "Entities"; break;
            case "structure": source = FindModelObject(es, "structure", null, name); collUsed = "Structures"; break;
            default: return Json(new { ok = false, error = "unknown kind: " + kind + " (entity|structure|serviceaction|serveraction|clientaction)" });
        }
        if (source == null) return Json(new { ok = false, error = kind + " not found: " + name });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(es, collUsed);
        MethodInfo dupMethod = null;
        try { dupMethod = FindDupMethod(ms, source); }
        catch (Exception e) { return Json(new { ok = false, error = "Duplicate method not found for " + source.GetType().Name + ": " + FirstMsg(e) }); }
        object created = null; string err = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, es });
                try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + name + ",es)->rename";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.ToString().Replace("\r", " ").Replace("\n", " | "); }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: clone " + kind, mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, collUsed);
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), before = before, after = after, error = err });
    }

    // debug_object_prop_surface: dump an object's settable surface for live_set_object_prop_deep:
    // public properties, private backing fields (_prop), static setter delegates
    // (_propSetter), static property-grid descriptors (PropPropertyDescriptor). Read-only.
    static string DebugObjectPropSurface(string moduleName, string kind, string entityName, string name)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var obj = FindModelObject(es, kind, entityName, name);
        if (obj == null) return Json(new { ok = false, error = "object not found: kind=" + kind + " entity=" + entityName + " name=" + name });
        var props = new List<object>();
        foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            object v = null; try { v = p.GetValue(obj, null); } catch { }
            props.Add(new { name = p.Name, type = p.PropertyType.Name, value = v?.ToString(), settable = p.CanWrite });
        }
        var fields = new List<object>();
        foreach (var f in obj.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            if (f.Name.StartsWith("_")) fields.Add(new { name = f.Name, type = f.FieldType.Name });
        var statics = new List<object>();
        foreach (var t in AllTypes(obj.GetType()))
        {
            foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
            {
                if (f.Name.EndsWith("Setter") || f.Name.EndsWith("PropertyDescriptor"))
                    statics.Add(new { name = f.Name, type = f.FieldType.Name, declaring = t.Name });
            }
        }
        return Json(new { ok = true, kind = kind, name = name, objectType = obj.GetType().FullName, properties = props, backingFields = fields.Take(40), staticSurfaces = statics });
    }

    // Finder for set_object_prop_deep / debug_object_prop_surface.
    static object FindModelObject(object es, string kind, string entityName, string name)
    {
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "entity": return FindEntity(es, name);
            case "attribute":
                var ent = FindEntity(es, entityName);
                if (ent == null) return null;
                var attrs = GetProp(ent, "Attributes") as IEnumerable;
                if (attrs == null) return null;
                foreach (var a in attrs) try { if ((GetProp(a, "Name") as string) == name) return a; } catch { }
                return null;
            case "structure":
                var structs = GetProp(es, "Structures") as IEnumerable;
                if (structs != null) foreach (var s in structs) try { if ((GetProp(s, "Name") as string) == name) return s; } catch { }
                return null;
            case "structureattribute":
                object st = null;
                var structs2 = GetProp(es, "Structures") as IEnumerable;
                if (structs2 != null) foreach (var s in structs2) try { if ((GetProp(s, "Name") as string) == entityName) { st = s; break; } } catch { }
                if (st == null) return null;
                var sattrs = GetProp(st, "Attributes") as IEnumerable;
                if (sattrs != null) foreach (var a in sattrs) try { if ((GetProp(a, "Name") as string) == name) return a; } catch { }
                return null;
            case "timer":
                foreach (var collName in new[] { "Timers", "Processes" })
                {
                    var coll = GetProp(es, collName) as IEnumerable;
                    if (coll == null) continue;
                    foreach (var t in coll) try { if ((GetProp(t, "Name") as string) == name) return t; } catch { }
                }
                return null;
            case "siteproperty":
                var sps = GetProp(es, "SiteProperties") as IEnumerable;
                if (sps != null) foreach (var s in sps) try { if ((GetProp(s, "Name") as string) == name) return s; } catch { }
                return null;
            case "role":
                foreach (var collName in new[] { "Roles", "SystemRoles" })
                {
                    var coll = GetProp(es, collName) as IEnumerable;
                    if (coll == null) continue;
                    foreach (var r in coll) try { if ((GetProp(r, "Name") as string) == name) return r; } catch { }
                }
                return null;
            default: return null;
        }
    }

    // ConvertValueFor: parse the text value into the member's target type (string, int/long,
    // double, bool, enums, Nullable<>). Returns ok=false when parsing failed (caller then
    // falls back to the raw string - descriptors parse text themselves).
    static object ConvertValueFor(Type targetType, string value, out bool ok)
    {
        ok = true;
        try
        {
            var t = targetType;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) t = t.GetGenericArguments()[0];
            if (t == typeof(string)) return value;
            if (t == typeof(int)) return int.Parse(value);
            if (t == typeof(long)) return long.Parse(value);
            if (t == typeof(double)) return double.Parse(value);
            if (t == typeof(bool)) return bool.Parse(value);
            if (t.IsEnum) return Enum.Parse(t, value, true);
            return value;
        }
        catch { ok = false; return value; }
    }

    static Type StripNullable(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)
        ? t.GetGenericArguments()[0] : t;

    // set_object_prop_deep: the Swiss-army setter for property-grid surfaces that lack a
    // public setter. Tries, in order: public property (type-aware value) -> static
    // "PropPropertyDescriptor" (reflection SetValue - the property-grid machinery parses the
    // text itself, e.g. DefaultValue's expression parser) -> static "_propSetter" delegate
    // (type-aware DynamicInvoke) -> raw backing field "_prop" (+revalidate). Field lookups are
    // CASE-INSENSITIVE (delegate fields are camelCase: _defaultValueSetter).
    static string SetObjectPropDeep(string moduleName, string kind, string entityName, string name, string propName, string value)
    {
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        return RunCmd(moduleName, "set object prop deep", es2 =>
        {
            var obj = FindModelObject(es2, kind, entityName, name);
            if (obj == null) throw new Exception("object not found: kind=" + kind + " entity=" + entityName + " name=" + name);
            var tried = new List<string>();
            // 1. public property setter (type-aware)
            foreach (var t in AllTypes(obj.GetType()))
            {
                var p = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (p == null || !p.CanWrite || p.GetIndexParameters().Length != 0) continue;
                try
                {
                    var v = ConvertValueFor(p.PropertyType, value, out bool ok);
                    p.SetValue(obj, v, null);
                    return "set " + propName + " via public property" + (ok ? "" : " (string fallback)");
                }
                catch (Exception e) { tried.Add("property: " + FirstMsg(e)); }
            }
            // 2. static property-grid descriptor (reflection SetValue - no System.ComponentModel cast;
            // these are ServiceStudio.Model.PropertyDescriptors.PropertyDescriptor subclasses that
            // parse the text themselves, e.g. DefaultValuePropertyDescriptor -> AbstractExpression)
            foreach (var t in AllTypes(obj.GetType()))
            {
                var pdf = t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(f => f.Name.Equals(propName + "PropertyDescriptor", StringComparison.OrdinalIgnoreCase));
                if (pdf == null) continue;
                try
                {
                    var pd = pdf.GetValue(null);
                    if (pd == null) continue;
                    var setValue = pd.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "SetValue" && m.GetParameters().Length == 2);
                    if (setValue == null) continue;
                    setValue.Invoke(pd, new object[] { obj, value });
                    try { CallMethod(obj, "ForceValidate", null, 0); } catch { }
                    try { CallMethod(obj, "InvalidateSelfVerifyCache", null, 0); } catch { }
                    return "set " + propName + " via PropertyDescriptor " + pd.GetType().Name;
                }
                catch (Exception e) { tried.Add("descriptor: " + FirstMsg(e)); }
            }
            // 3. static delegate _propSetter (case-insensitive; type-aware DynamicInvoke)
            foreach (var t in AllTypes(obj.GetType()))
            {
                var df = t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(f => f.Name.Equals("_" + propName + "Setter", StringComparison.OrdinalIgnoreCase));
                if (df == null) continue;
                try
                {
                    var dlg = df.GetValue(null) as Delegate;
                    if (dlg == null) continue;
                    var paramType = dlg.Method.GetParameters().Length == 2 ? dlg.Method.GetParameters()[1].ParameterType : typeof(string);
                    var v = ConvertValueFor(paramType, value, out bool ok);
                    dlg.DynamicInvoke(obj, v);
                    return "set " + propName + " via static delegate" + (ok ? "" : " (string fallback)");
                }
                catch (Exception e) { tried.Add("delegate: " + FirstMsg(e)); }
            }
            // 4. raw backing field (_prop, case-insensitive) + revalidate
            foreach (var t in AllTypes(obj.GetType()))
            {
                var f = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(x => x.Name.Equals("_" + propName, StringComparison.OrdinalIgnoreCase)
                                      || x.Name.Equals("_" + char.ToLowerInvariant(propName[0]) + propName.Substring(1), StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                try
                {
                    var v = ConvertValueFor(StripNullable(f.FieldType), value, out bool ok);
                    f.SetValue(obj, v);
                    try { CallMethod(obj, "ForceValidate", null, 0); } catch { }
                    try { CallMethod(obj, "InvalidateSelfVerifyCache", null, 0); } catch { }
                    return "set " + propName + " via raw field " + f.Name + " (+revalidate)";
                }
                catch (Exception e) { tried.Add("field: " + FirstMsg(e)); }
            }
            throw new Exception("no settable surface for " + propName + ": " + string.Join(" | ", tried));
        });
    }

    // upload_image: create a module Image from a base64 payload via the public
    // IESpace.CreateImage(Byte[], String, String, IKey). No file dialog, no SS UI.
    static string UploadImage(string moduleName, string name, string base64Data, string description)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(base64Data)) return Json(new { ok = false, error = "name and base64Data required" });
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64Data); }
        catch (Exception e) { return Json(new { ok = false, error = "base64Data invalid: " + FirstMsg(e) }); }
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        object created = null; string err = null; string via = null;
        var errs = new List<string>();
        Action mutate = () =>
        {
            // Path 0: the UI's own static factory - ResourceCommands.CreateImage calls
            // ServiceStudio.Model.Image.Create(es, bytes, name, null, null) (decompiled).
            try
            {
                var imgType = FindType("ServiceStudio.Model.Image");
                if (imgType != null)
                    foreach (var m in imgType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (m.Name != "Create") continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 5 && ps[0].ParameterType.Name == "ESpace" && ps[1].ParameterType == typeof(byte[]))
                        {
                            created = m.Invoke(null, new object[] { es, bytes, name, null, null });
                            via = "Image.Create(es,bytes,name,null,null)";
                            break;
                        }
                    }
            }
            catch (Exception eImg) { errs.Add("Image.Create: " + FirstMsg(eImg)); }
            if (created != null) return;
            var key = CallMethod(ms, "NewKey", null, 0);
            foreach (var coll0 in FindWritableCollections(es, "Image"))
            {
                if (created != null) break;
                var coll = UnwrapWritable(coll0, 0);
                foreach (var (methodName, args) in new (string, object[])[] {
                    ("Create", new object[] { bytes, name, key }),
                    ("Add",    new object[] { bytes, name, key }),
                    ("Create", new object[] { bytes, name, description ?? "", key }),
                    ("Add",    new object[] { bytes, name, description ?? "", key }),
                    ("Create", new object[] { name, key }),
                    ("Add",    new object[] { name, key }) })
                {
                    try
                    {
                        created = CallMethod(coll, methodName, args, args.Length);
                        if (created != null) { via = "writableImages." + methodName + "/" + args.Length; break; }
                    }
                    catch (Exception e0) { errs.Add("writableImages." + methodName + "/" + args.Length + ": " + FirstMsg(e0)); }
                }
            }
            // Path 1b: the es.Images read-only adapter (kept - some builds expose Create on it).
            foreach (var collName in new[] { "Images", "ResourceImages" })
            {
                if (created != null) break;
                var coll = GetProp(es, collName);
                if (coll == null) continue;
                foreach (var (methodName, args) in new (string, object[])[] {
                    ("Create", new object[] { bytes, name, key }),
                    ("Create", new object[] { bytes, name, description ?? "", key }),
                    ("Add",    new object[] { bytes, name, key }),
                    ("Add",    new object[] { bytes, name, description ?? "", key }),
                    ("Create", new object[] { name, key }),
                    ("Add",    new object[] { name, key }) })
                {
                    try
                    {
                        created = CallMethod(coll, methodName, args, args.Length);
                        if (created != null) { via = collName + "." + methodName + "/" + args.Length; break; }
                    }
                    catch (Exception e1) { errs.Add(collName + "." + methodName + "/" + args.Length + ": " + FirstMsg(e1)); }
                }
            }
            // Path 2: IESpace.CreateImage (kept for older builds where it works)
            if (created == null)
            {
                try { created = CallMethod(es, "CreateImage", new object[] { bytes, name, description ?? "", key }, 4); via = "es.CreateImage/4"; }
                catch (Exception e1) { errs.Add("es.CreateImage/4: " + FirstMsg(e1)); }
            }
            // Path 3: the UI's own static ResourceCommands.CreateImage(key?, es, bytes, name)
            // (may be internal - include NonPublic; try null and real key for the first param)
            if (created == null)
            {
                var rc = FindType("ServiceStudio.Commands.ResourceCommands");
                if (rc != null)
                    foreach (var m in rc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (m.Name != "CreateImage") continue;
                        foreach (var first in new object[] { null, key })
                        {
                            try
                            {
                                created = m.Invoke(null, new object[] { first, es, bytes, name });
                                if (created != null) { via = "ResourceCommands.CreateImage"; break; }
                            }
                            catch (Exception e2) { errs.Add("ResourceCommands.CreateImage: " + FirstMsg(e2)); }
                        }
                        if (created != null) break;
                    }
            }
            if (created == null) err = "all image factories failed: " + string.Join(" ; ", errs.Take(6));
        };
        RunInCommand(es, "upload image", mutate);
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), bytes = bytes.Length, error = err });
    }

    // upload_resource: create a module Resource from a base64 payload via
    // IESpace.CreateResource(Byte[], String, IKey).
    static string UploadResource(string moduleName, string name, string base64Data)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(base64Data)) return Json(new { ok = false, error = "name and base64Data required" });
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64Data); }
        catch (Exception e) { return Json(new { ok = false, error = "base64Data invalid: " + FirstMsg(e) }); }
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        object created = null; string err = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                created = CallMethod(es, "CreateResource", new object[] { bytes, name, key }, 3);
                via = "es.CreateResource(bytes,name,key)";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        RunInCommand(es, "upload resource", mutate);
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), bytes = bytes.Length, error = err });
    }

    // Shared: run an Action inside Command.ExecuteFromAsyncCode for the module's espace.
    static void RunInCommand(object es, string desc, Action mutate)
    {
        var agg = GetContext(es);
        if (agg == null) throw new Exception("aggregator (GetContext) is null");
        var pc = BuildPresenterContext(agg);
        if (pc == null) throw new Exception("PresenterContext null");
        var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
        if (exec == null) throw new Exception("Command.ExecuteFromAsyncCode not found");
        exec.Invoke(null, new object[] { pc, "OsLiveBridge: " + desc, mutate });
    }

    // delete_structure: delete a Structure by name (Delete inside a real SS command).
    static string DeleteStructure(string moduleName, string name)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        object target = null;
        var structs = GetProp(es, "Structures") as IEnumerable;
        if (structs != null)
            foreach (var s in structs) try { if ((GetProp(s, "Name") as string) == name) { target = s; break; } } catch { }
        if (target == null) return Json(new { ok = false, error = "structure not found: " + name });
        int before = CountProp(es, "Structures");
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(target, "Delete", null, 0); }
            catch (Exception e1)
            {
                try { CallMethod(target, "Delete", new object[] { false }, 1); }
                catch (Exception e2) { err = "Delete() " + FirstMsg(e1) + " ; Delete(false) " + FirstMsg(e2); }
            }
        };
        try { RunInCommand(es, "delete structure", mutate); }
        catch (Exception e) { err = err ?? FirstMsg(e); }
        int after = CountProp(es, "Structures");
        return Json(new { ok = err == null, deleted = name, before = before, after = after, error = err });
    }

    // remove_unused_dependencies: IESpace.RemoveUnusedDependencies() inside a command.
    static string RemoveUnusedDependencies(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        int before = CountProp(es, "References");
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(es, "RemoveUnusedDependencies", null, 0); }
            catch (Exception e) { err = FirstMsg(e); }
        };
        try { RunInCommand(es, "remove unused dependencies", mutate); }
        catch (Exception e) { err = err ?? FirstMsg(e); }
        int after = CountProp(es, "References");
        return Json(new { ok = err == null, referencesBefore = before, referencesAfter = after, removed = Math.Max(0, before - after), error = err });
    }

    // delete_service_action: delete a ServiceAPIMethod by name
    static string DeleteServiceAction(string moduleName, string name)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindServiceAction(es, name);
        if (action == null) return Json(new { ok = false, error = "service action not found: " + name });
        int before = CountProp(es, "ServiceActions");
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(action, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: delete service action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, "ServiceActions");
        return Json(new { ok = err == null, deleted = name, serviceActionsBefore = before, serviceActionsAfter = after, error = err });
    }

    // set_action_name: rename an action by its current name (module-level OR block-scoped
    // ClientScreenActionFlow). The ctor auto-suffixes names (SaveProfile2) to avoid colliding
    // with module-level actions; once the module-level duplicates are deleted, rename to the
    // clean name so widget handlers / flow tools resolve by the spec name. Uses SetName (public
    // method on ClientScreenActionFlow) inside a real SS command (undo unit).
    static string SetActionName(string moduleName, string action, string newName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (string.IsNullOrEmpty(newName)) return Json(new { ok = false, error = "newName required" });
        var found = FindAction(es, action);
        if (found == null) return Json(new { ok = false, error = "action not found: " + action });
        string err = null; string via = null;
        Action mutate = () =>
        {
            try { CallMethod(found, "SetName", new object[] { newName }, 1); via = "SetName"; }
            catch (Exception e1)
            {
                try { SetProp(found, "Name", newName); via = "SetProp(Name)"; }
                catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = "SetName: " + FirstMsg(e1) + " ; SetProp: " + r.GetType().Name + ": " + r.Message; }
            }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: set action name", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = err == null, action = action, newName = newName, via = via, error = err });
    }

    static string FirstMsg(Exception e)
    {
        var r = e; while (r.InnerException != null) r = r.InnerException;
        return r.GetType().Name + ": " + r.Message;
    }

    // delete_action: delete a module-level Service/Server/Client action by name (inside a real SS
    // command so it's an undo unit). Use to remove module-level duplicates (e.g. the module-level
    // SaveProfile / RoleSelection_OnChange) so FindAction falls through to the block-scoped
    // ClientScreenActionFlow versions. NOTE: this deletes ONLY the module-level action (searches
    // es.ServiceActions/UserActions/ServerActions/ClientActions), never a block-scoped one.
    static string DeleteAction(string moduleName, string name)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        object action = null; string collUsed = null;
        foreach (var collName in new[] { "ServiceActions", "UserActions", "ServerActions", "ClientActions" })
        {
            var found = FindActionInCollection(es, collName, name);
            if (found != null) { action = found; collUsed = collName; break; }
        }
        if (action == null) return Json(new { ok = false, error = "module-level action not found: " + name });
        int before = CountProp(es, collUsed);
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(action, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: delete action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, collUsed);
        return Json(new { ok = err == null, deleted = name, collection = collUsed, before = before, after = after, error = err });
    }

    // ============ UI / screen / widget / CSS tools (live) ============
    // All mutations wrap in Command.ExecuteFromAsyncCode via RunCmd (one undo unit).
    // Widget addressing is by NAME (every widget created gets a name; FindWidget walks
    // the tree). parent="" or null means "add to the screen's top-level Widgets".
    // Reactive vs Traditional: CreateWebFlow/CreateScreen/CreateWidget<T> dispatch to
    // the right concrete type per module kind; flows are searched across both the
    // WebFlows and NRWebFlows collections so this works for either kind.

    // delete_screen_client_action: delete a SCREEN-level Client Action (ClientScreenActionFlow)
    // from a screen's own ClientActions collection. Module-level delete_action does NOT cover
    // these (they live on the screen, not es.ClientActions).
    // delete_block_client_action: delete a client action that lives on a WEB BLOCK
    // (module-level delete_action does not see block-owned actions). Same Delete()
    // in-command pattern as DeleteScreenClientAction. Undo unit (Ctrl+Z).
    static string DeleteBlockClientAction(string moduleName, string blockName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var blk = FindBlock(es, blockName);
        if (blk == null) return Json(new { ok = false, error = "web block not found: " + blockName });
        object action = null;
        var coll = GetProp(blk, "ClientActions") as IEnumerable;
        if (coll != null)
            foreach (var a in coll)
                try { if ((GetProp(a, "Name") as string) == actionName) { action = a; break; } } catch { }
        if (action == null) return Json(new { ok = false, error = "block client action not found: " + actionName });
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(action, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: delete block client action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = err == null, error = err, report = err == null ? "deleted block client action '" + actionName + "' on block '" + blockName + "'" : null });
    }

    // remove_event_from_block: delete a custom event (WebBlockCustomEvent) from a web
    // block. Inverse of add_event_to_block. Same in-command Delete() pattern. Undo unit.
    static string RemoveEventFromBlock(string moduleName, string blockName, string eventName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var blk = FindBlock(es, blockName);
        if (blk == null) return Json(new { ok = false, error = "web block not found: " + blockName });
        object evt = null;
        var evts = GetProp(blk, "CustomEvents") as IEnumerable ?? GetProp(blk, "Events") as IEnumerable;
        if (evts != null)
            foreach (var v in evts)
                try { if ((GetProp(v, "Name") as string) == eventName) { evt = v; break; } } catch { }
        if (evt == null) return Json(new { ok = false, error = "block event not found: " + eventName });
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(evt, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: remove block event", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = err == null, error = err, report = err == null ? "removed event '" + eventName + "' from block '" + blockName + "'" : null });
    }

    static string DeleteScreenClientAction(string moduleName, string screenName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var sc = FindScreen(es, screenName);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screenName });
        object action = null;
        try
        {
            var coll = GetProp(sc, "ClientActions") as IEnumerable;
            if (coll != null)
                foreach (var a in coll)
                {
                    try { if ((GetProp(a, "Name") as string) == actionName) { action = a; break; } } catch { }
                }
        }
        catch (Exception e) { return Json(new { ok = false, error = "screen ClientActions read failed: " + e.Message }); }
        if (action == null) return Json(new { ok = false, error = "screen client action not found: " + actionName });
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(action, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: delete screen client action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = err == null, deleted = actionName, screen = screenName, error = err });
    }

    static string RunCmd(string moduleName, string desc, Func<object, string> mutate)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        string report = null, err = null;
        Action body = () =>
        {
            try { report = mutate(es); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; report = (report ?? "") + "\nEXC: " + err; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "exec not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: " + desc, body });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        // Note: do NOT call UndoManager.Undo on failure. Command.ExecuteFromAsyncCode
        // auto-rolls-back an exception-propagated command; our body catches for error
        // detail, but our operations are single/clean (throw before mutating if invalid),
        // so there is no partial state to undo. Calling Undo repeatedly crashed SS before.
        return Json(new { ok = err == null, error = err, report = report });
    }

    // Combine WebFlows + NRWebFlows so we find flows regardless of module kind.
    static List<object> WebFlowsOf(object es)
    {
        var result = new List<object>();
        foreach (var propName in new[] { "WebFlows", "NRWebFlows" })
        {
            var coll = GetProp(es, propName) as IEnumerable;
            if (coll != null) foreach (var f in coll) result.Add(f);
        }
        return result;
    }

    static object FindWebFlow(object es, string name)
    {
        foreach (var f in WebFlowsOf(es))
            try { if ((GetProp(f, "Name") as string) == name) return f; } catch { }
        return null;
    }

    // Count the screens (IScreen nodes) in a web flow. Used by CreateWebFlow to prefer a
    // screen-less source flow so a cloned flow doesn't inherit the source's screens.
    static int FlowScreenCount(object flow)
    {
        int n = 0;
        try
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes != null) foreach (var node in nodes) if (IsScreen(node)) n++;
        }
        catch { }
        return n;
    }

    // Combine NRThemes + WebThemes so we find themes regardless of module kind.
    static List<object> ThemesOf(object es)
    {
        var result = new List<object>();
        foreach (var propName in new[] { "NRThemes", "WebThemes" })
        {
            var coll = GetProp(es, propName) as IEnumerable;
            if (coll != null) foreach (var t in coll) result.Add(t);
        }
        return result;
    }

    static object FindTheme(object es, string name)
    {
        foreach (var t in ThemesOf(es))
            try { if ((GetProp(t, "Name") as string) == name) return t; } catch { }
        return null;
    }

    static int CountThemes(object es) => ThemesOf(es).Count;

    static object FindScreen(object es, string name)
    {
        foreach (var flow in WebFlowsOf(es))
        {
            // IUIFlow exposes screens (and blocks/emails) via Nodes, not Screens.
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (!IsScreen(node)) continue;
                try { if ((GetProp(node, "Name") as string) == name) return node; } catch { }
            }
        }
        return null;
    }

    // A UI flow node is a screen if it implements IScreen (NRWebScreen / WebScreen do;
    // blocks/emails do not). Match by interface short name to avoid namespace assumptions.
    static bool IsScreen(object node)
    {
        try { foreach (var i in node.GetType().GetInterfaces()) if (i.Name == "IScreen") return true; } catch { }
        return false;
    }

    // Find a web block by name across all web flows (blocks are flow nodes that are NOT screens).
    static object FindBlock(object es, string name)
    {
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (IsScreen(node)) continue;
                try { if ((GetProp(node, "Name") as string) == name) return node; } catch { }
            }
        }
        return null;
    }

    // Find a web block AND the flow that contains it (needed as the Duplicate parent,
    // since web blocks live inside a WebFlow's Nodes, not on the eSpace directly).
    static object FindBlockWithFlow(object es, string name, out object flowOut)
    {
        flowOut = null;
        foreach (var flow in WebFlowsOf(es))
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes == null) continue;
            foreach (var node in nodes)
            {
                if (IsScreen(node)) continue;
                try { if ((GetProp(node, "Name") as string) == name) { flowOut = flow; return node; } } catch { }
            }
        }
        return null;
    }

    // Recursively find a widget by name under a screen or container (depth-first).
    static object FindWidget(object root, string name)
    {
        var widgets = GetProp(root, "Widgets") as IEnumerable;
        if (widgets == null) return null;
        return FindWidgetRecursive(widgets, name);
    }

    static object FindWidgetRecursive(IEnumerable widgets, string name)
    {
        foreach (var w in widgets)
        {
            try { if ((GetProp(w, "Name") as string) == name) return w; } catch { }
            var childWidgets = GetProp(w, "Widgets") as IEnumerable;
            if (childWidgets != null)
            {
                var found = FindWidgetRecursive(childWidgets, name);
                if (found != null) return found;
            }
            // Reactive containers keep children inside a 'content' placeholder (CustomPlaceholderWidget),
            // not in Widgets. Descend into every placeholder's Widgets too.
            var phColl = GetProp(w, "Placeholders") as IEnumerable;
            if (phColl != null)
            {
                foreach (var ph in phColl)
                {
                    var phWidgets = GetProp(ph, "Widgets") as IEnumerable;
                    if (phWidgets == null) continue;
                    var found = FindWidgetRecursive(phWidgets, name);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    // Resolve the parent for add_* tools: empty parent = the screen itself; otherwise
    // a named widget (container/link) found recursively under the screen.
    static object ResolveParent(object screen, string parentName)
    {
        if (string.IsNullOrEmpty(parentName)) return screen;
        var dotted = ResolveDottedContent(screen, parentName);
        if (dotted != null) return dotted;
        return FindWidget(screen, parentName);
    }

    // Resolve "Table.Row" / "Table.HeaderRow" (/Item) content hosts for column/item templates.
    // Row/HeaderRow are IContent (not FindWidget-addressable); resolve the host widget first,
    // then its Row/HeaderRow/Widgets member as the creation host. Returns null when N/A.
    static object ResolveDottedContent(object root, string parentName)
    {
        if (string.IsNullOrEmpty(parentName)) return null;
        var dot = parentName.LastIndexOf('.');
        if (dot <= 0 || dot == parentName.Length - 1) return null;
        var hostName = parentName.Substring(0, dot);
        var part = parentName.Substring(dot + 1);
        string[] propCandidates = null;
        if (part.Equals("Row", StringComparison.OrdinalIgnoreCase)) propCandidates = new[] { "Row" };
        else if (part.Equals("HeaderRow", StringComparison.OrdinalIgnoreCase) || part.Equals("Header", StringComparison.OrdinalIgnoreCase)) propCandidates = new[] { "HeaderRow" };
        else if (part.Equals("Item", StringComparison.OrdinalIgnoreCase)) propCandidates = new[] { "Widgets", "Item", "Row" };
        else return null;
        var host = FindWidget(root, hostName);
        if (host == null) return null;
        foreach (var pn in propCandidates)
        {
            try { var sub = GetProp(host, pn); if (sub != null) return sub; } catch { }
        }
        return null;
    }

    // Candidate widget interfaces per kind. Reactive (NRWebScreen) widgets implement
    // Mobile/IContent/nested NRWebWidgetInterfaces interfaces (NOT the Traditional
    // Web.Widgets.I*Widget set), so we try a list and use the first the factory accepts.
    static readonly Dictionary<string, string[]> WidgetKindCandidates = new Dictionary<string, string[]>
    {
        ["container"] = new[] {
            "ServiceStudio.Plugin.NRWidgets.IContainer",                            // Reactive container (Container) - implements IMobileWidget
            "OutSystems.Model.UI.Mobile.IContent",                                  // Reactive container (CustomPlaceholderWidget)
            "ServiceStudio.Model.Interfaces.NRWebWidgetInterfaces+ICustomPlaceholderWidget",
            "ServiceStudio.Model.Interfaces.IWebCustomPlaceholderWidget",
            "ServiceStudio.PluginAPI.Model.UI.IPlaceholderWidget",
            "OutSystems.Model.UI.Web.Widgets.IContainerWidget" },                   // Traditional fallback
        ["text"] = new[] {
            "OutSystems.Model.UI.Mobile.Widgets.ITextWidget",                       // Reactive text
            "OutSystems.Model.UI.Web.Widgets.ITextWidget" },                        // Traditional
        ["expression"] = new[] {
            "ServiceStudio.Plugin.NRWidgets.IExpression",                       // Reactive expression (NRWidgets plugin -> real [Expression])
            "OutSystems.Plugin.NRWidgets.IExpression",
            "OutSystems.Model.UI.Web.Widgets.IExpressionWidget",
            "OutSystems.Model.UI.Mobile.Widgets.ITextWidget" },                 // last-resort fallback
        ["link"] = new[] {
            "OutSystems.Model.UI.Mobile.Widgets.ILinkWidget",                       // Reactive link
            "ServiceStudio.Model.Interfaces.NRWebWidgetInterfaces+ILinkWidget",
            "ServiceStudio.Model.Interfaces.ILinkWidget",
            "OutSystems.Model.UI.Web.Widgets.ILinkWidget" },                        // Traditional fallback
        ["html"] = new[] {
            "ServiceStudio.Model.Interfaces.IHtmlElementWidget",                    // Reactive HTML element (CustomWidget)
            "ServiceStudio.Model.Interfaces.NRWebWidgetInterfaces+ICustomWidget",
            "ServiceStudio.Model.Interfaces.ICustomWidget",
            "ServiceStudio.PluginAPI.Model.UI.ICustomWidget" },
        ["placeholder"] = new[] {
            "OutSystems.Model.UI.Mobile.IPlaceholderWidget",                   // Reactive placeholder (CustomPlaceholderWidget)
            "ServiceStudio.Model.Interfaces.NRWebWidgetInterfaces+ICustomPlaceholderWidget",
            "ServiceStudio.Model.Interfaces.IWebCustomPlaceholderWidget",
            "ServiceStudio.PluginAPI.Model.UI.IPlaceholderWidget",
            "OutSystems.Model.UI.Web.Widgets.IPlaceholderWidget" },            // Traditional fallback
        ["if"] = new[] {
            "OutSystems.Model.UI.Mobile.Widgets.IIfWidget",                         // Reactive If
            "ServiceStudio.Model.Interfaces.IIfWidget",
            "OutSystems.Model.UI.Web.Widgets.IIfWidget" },                          // Traditional fallback
    };

    // Create a widget of the given kind inside parent (screen or container). Tries each
    // candidate interface across every CreateWidget<T>(name,key) method on the parent
    // (IScreen.CreateWidget<T> and IMobileScreen.CreateWidget<T>); returns the first that
    // the factory accepts. Failed attempts throw cleanly (no widget created) so retry is safe.
    static object CreateWidgetByKind(object parent, string kind, string name)
    {
        if (!WidgetKindCandidates.TryGetValue(kind, out var candidates))
            throw new Exception("unknown widget kind: " + kind);
        var createMethods = new List<MethodInfo>();
        foreach (var iface in parent.GetType().GetInterfaces())
            foreach (var m in iface.GetMethods())
                if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2 && !createMethods.Contains(m))
                    createMethods.Add(m);
        for (var t = parent.GetType(); t != null; t = t.BaseType)
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2 && !createMethods.Contains(m))
                    createMethods.Add(m);
        if (createMethods.Count == 0) throw new Exception("CreateWidget<T> not found on " + parent.GetType().FullName);
        var ms = ModelServices();
        var errs = new List<string>();
        foreach (var ifaceName in candidates)
        {
            var widgetType = FindType(ifaceName);
            if (widgetType == null) { errs.Add(ifaceName + ": type not found"); continue; }
            foreach (var cm in createMethods)
            {
                try
                {
                    var gen = cm.MakeGenericMethod(widgetType);
                    var key = CallMethod(ms, "NewKey", null, 0);
                    var w = gen.Invoke(parent, new object[] { name, key });
                    Log("CreateWidgetByKind(" + kind + "," + name + ") via " + ifaceName + "/" + cm.DeclaringType?.Name + " => " + (w?.GetType().Name ?? "null"));
                    return w;
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(ifaceName + "/" + cm.DeclaringType?.Name + ": " + r.Message); }
            }
        }
        // Brute-force fallback: try EVERY I*<Kind>Widget interface in ANY namespace (the
        // explicit candidate list only covers some namespaces and missed the Reactive link
        // interface). Only interfaces assignable to each CreateWidget<T>'s generic constraint
        // are attempted, so constraint-violating interfaces are skipped cleanly.
        var kindLower = kind;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] alltypes; try { alltypes = asm.GetTypes(); } catch { continue; }
            foreach (var ct in alltypes)
            {
                try
                {
                    if (!ct.IsInterface) continue;
                    if (!ct.Name.StartsWith("I", StringComparison.Ordinal)) continue;
                    if (ct.Name.IndexOf(kindLower, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (candidates.Contains(ct.FullName) || candidates.Contains(ct.Name)) continue;
                    foreach (var cm in createMethods)
                    {
                        var arg = cm.GetGenericArguments()[0];
                        var ccons = arg.GetGenericParameterConstraints();
                        if (ccons.Length > 0 && !ccons[0].IsAssignableFrom(ct)) continue;
                        try
                        {
                            var key = CallMethod(ms, "NewKey", null, 0);
                            var gen = cm.MakeGenericMethod(ct);
                            var w = gen.Invoke(parent, new object[] { name, key });
                            if (w != null) { Log("CreateWidgetByKind(" + kind + "," + name + ") via brute " + ct.FullName); return w; }
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add("brute " + ct.FullName + "/" + cm.DeclaringType?.Name + ": " + r.Message); }
                    }
                }
                catch { }
            }
        }
        throw new Exception("CreateWidgetByKind failed for '" + kind + "': " + string.Join(" | ", errs));
    }

    // Read-only diagnostic: dump a parent widget's CreateWidget<T> constraints plus every
    // Link-related interface in loaded assemblies (incl. ServiceStudio.Model.Interfaces.*).
    static string ProbeLinkWidget(string module, string screen, string parentName)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var parent = ResolveParent(sc, parentName);
        if (parent == null) return Json(new { ok = false, error = "parent not found: " + parentName });
        var pt = parent.GetType();
        var cwMethods = new List<object>();
        foreach (var iface in pt.GetInterfaces())
            foreach (var m in iface.GetMethods())
                if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2)
                {
                    var arg = m.GetGenericArguments()[0];
                    var cs = arg.GetGenericParameterConstraints();
                    cwMethods.Add(new { iface = iface.FullName, method = m.DeclaringType?.FullName, constraint = cs.Length > 0 ? cs[0].FullName : "(none)" });
                }
        var imw = FindType("OutSystems.Model.UI.Mobile.Widgets.IMobileWidget");
        var linkIfaces = new List<object>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
                try { if (t.IsInterface && t.Name.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0) linkIfaces.Add(new { type = t.FullName, isWidget = t.Name.EndsWith("Widget", StringComparison.OrdinalIgnoreCase), assignableToIMobileWidget = imw != null ? imw.IsAssignableFrom(t) : false }); } catch { }
        }
        return Json(new { ok = true, parentType = pt.FullName, parentImplements = pt.GetInterfaces().Select(i => i.FullName), createWidgetMethods = cwMethods, imobileWidget = imw?.FullName ?? "(not found)", linkInterfaces = linkIfaces });
    }

    // Set the StyleClasses property on a widget. For Reactive CONTAINERS the class list
    // must live in the 'Style' CustomProperty's ValueExpression (that is what SS serializes
    // as <CustomProperty PropertyName="Style"><ValueExpression><ParsedExpression><Text
    // Value="..."/>) � a bare CustomStyle= attribute is NOT the container class surface.
    // Links/Images carry classes differently (CustomStyle attr for Links). Fallback order:
    // Style CustomProperty (containers) -> StyleClasses prop -> CustomStyle prop -> force.
    static void SetStyleClassesOn(object widget, string styleClass)
    {
        if (string.IsNullOrEmpty(styleClass)) return;
        var tn = widget?.GetType().FullName ?? "";
        if (tn.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0)
            if (TrySetStyleCustomProperty(widget, styleClass)) return;
        try { SetProp(widget, "StyleClasses", styleClass); return; }
        catch { }
        try { SetProp(widget, "CustomStyle", styleClass); return; }
        catch { }
        SetPropForce(widget, "StyleClasses", styleClass);
    }

    // Write the class list into the widget's 'Style' CustomProperty ValueExpression and
    // clear any stray raw-CSS CustomStyle value so it doesn't serialize a bogus attribute.
    // Returns false if the widget has no 'Style' CustomProperty or every setter fails.
    // MUST be called inside a command scope (RunCmd) � SS rejects model mutations otherwise.
    static bool TrySetStyleCustomProperty(object widget, string styleClass)
    {
        try
        {
            object cps = null;
            try { cps = GetProp(widget, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(widget, "_customProperties"); } catch { } }
            if (!(cps is IEnumerable coll)) return false;
            foreach (var cp in coll)
            {
                var name = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                if (name != "Style") continue;
                bool set = false;
                // SetValueExpression(String) materializes the _valueExpression ParsedExpression
                // (the genuine serialization surface for container style classes). ⚠ It stores the
                // string VERBATIM as a <Text> literal - quotes are NOT expression syntax and would
                // become part of the class name (proven: Style Classes showed "header" with quotes).
                // Store the RAW class string; fall back to quoted only if raw throws.
                var quoted = "\"" + styleClass.Replace("\"", "\"\"") + "\"";
                foreach (var candidate in new[] { styleClass, quoted })
                {
                    try { CallMethodTyped(cp, "SetValueExpression", new object[] { candidate }); set = true; break; }
                    catch { }
                }
                if (!set)
                    try { SetProp(cp, "Value", styleClass); set = true; } catch { }
                if (!set)
                {
                    var ve = GetProp(cp, "ValueExpression");
                    if (ve != null)
                    {
                        try { CallMethod(ve, "SetValue", new object[] { styleClass }, 1); set = true; }
                        catch { try { SetProp(ve, "Text", styleClass); set = true; } catch { } }
                    }
                }
                try { SetProp(widget, "CustomStyle", ""); } catch { }
                return true;
            }
        }
        catch { }
        return false;
    }

    static string CreateWebFlow(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        if (FindWebFlow(es, name) != null) return Json(new { ok = false, error = "web flow already exists: " + name });
        var flows = WebFlowsOf(es);
        var source = flows.FirstOrDefault(f => FlowScreenCount(f) == 0) ?? flows.FirstOrDefault();
        if (source == null) return Json(new { ok = false, error = "no source flow to clone" });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var dupMethod = FindDuplicateMethod(ms, source);
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) method not found" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = flows.Count;
        object created = null; string err = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, es });
                try { SetProp(dup, "Name", name); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + GetProp(source, "Name") + ",es)->rename";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create web flow", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = WebFlowsOf(es).Count;
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), flowsBefore = before, flowsAfter = after, error = err });
    }

    // From-scratch web flow creation via the IESpace.CreateWebFlow(String, IKey) factory.
    // NO Duplicate/cloning. The concrete type follows the module kind.
    static string CreateFlowScratch(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        if (FindWebFlow(es, name) != null) return Json(new { ok = false, error = "web flow already exists: " + name });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var key = CallMethod(ms, "NewKey", null, 0);
        if (key == null) return Json(new { ok = false, error = "NewKey failed" });
        int before = WebFlowsOf(es).Count;
        object created = null; string err = null;
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            Action mutate = () =>
            {
                try
                {
                    created = CallMethod(es, "CreateWebFlow", new object[] { name, key }, 2);
                    if (created == null) throw new Exception("CreateWebFlow returned null");
                }
                catch (Exception e1)
                {
                    var r = e1; while (r.InnerException != null) r = r.InnerException;
                    var firstErr = "CreateWebFlow: " + r.GetType().Name + ": " + r.Message;
                    try
                    {
                        created = CallMethod(es, "CreateUIFlow", new object[] { name, key }, 2);
                        if (created == null) throw new Exception("CreateUIFlow returned null");
                    }
                    catch (Exception e2)
                    {
                        r = e2; while (r.InnerException != null) r = r.InnerException;
                        err = firstErr + " | CreateUIFlow: " + r.GetType().Name + ": " + r.Message;
                    }
                }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create web flow (scratch)", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = WebFlowsOf(es).Count;
        return Json(new { ok = created != null, via = "es.CreateWebFlow/CreateUIFlow(name,key)", createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), flowsBefore = before, flowsAfter = after, error = err });
    }

    // Delete a web flow by name (CallMethod(flow, "Delete")). Removes the flow from
    // whichever collection holds it (WebFlows / NRWebFlows).
    static string DeleteWebFlow(string module, string flow)
    {
        if (string.IsNullOrWhiteSpace(flow)) return Json(new { ok = false, error = "flow required" });
        return RunCmd(module, "delete web flow", es =>
        {
            var wf = FindWebFlow(es, flow);
            if (wf == null) throw new Exception("web flow not found: " + flow);
            string typeName = wf.GetType().FullName;
            string via = "Delete";
            try { CallMethod(wf, "Delete", new object[] { false }, 1); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; via = "Delete(false) failed: " + r.GetType().Name + ": " + r.Message; CallMethod(wf, "Delete", null, 0); }
            return "deleted flow '" + flow + "' (" + typeName + ") via " + via + "; remaining flows: " + WebFlowsOf(es).Count;
        });
    }

    static string CreateWebScreen(string module, string webFlow, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "create web screen", es =>
        {
            var wf = FindWebFlow(es, webFlow);
            if (wf == null) throw new Exception("web flow not found: " + webFlow);
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            var screen = CallMethod(wf, "CreateScreen", new object[] { name, key }, 2);
            return "created screen '" + name + "' in flow '" + webFlow + "' (" + (screen?.GetType().Name ?? "?") + ")";
        });
    }

    // From-scratch web block creation via a Create*WebBlock* factory on the eSpace or the
    // target WebFlow (NO Duplicate/cloning). Hunts for a factory method that returns a web
    // block node, invokes it inside Command.ExecuteFromAsyncCode (undo unit, live tree), and
    // adds the node to the flow's Nodes if the factory didn't already. Returns the created
    // type/name/flow. If no from-scratch factory exists the error lists the candidates found.
    static string CreateWebBlock(string module, string flowName, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        if (FindBlock(es, name) != null) return Json(new { ok = false, error = "web block already exists: " + name });
        var flow = FindWebFlow(es, flowName);
        if (flow == null) return Json(new { ok = false, error = "web flow not found: " + flowName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var key = CallMethod(ms, "NewKey", null, 0);
        if (key == null) return Json(new { ok = false, error = "NewKey failed" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = FlowBlockCount(flow);
        object created = null; string err = null; string via = null; string add = null; string diag = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            Action mutate = () =>
            {
                object target; var m = FindWebBlockFactory(es, flow, out target);
                if (m == null)
                {
                    diag = WebBlockFactoryCandidates(es, flow);
                    throw new Exception("no from-scratch web-block factory found; candidates: " + diag);
                }
                var args = m.GetParameters().Length == 2 ? new object[] { name, key } : new object[] { name };
                created = m.Invoke(target, args);
                if (created == null) throw new Exception(m.Name + " returned null");
                via = m.DeclaringType.Name + "." + m.Name;
                try { SetProp(created, "Name", name); } catch { }
                var nodes = GetProp(flow, "Nodes");
                if (nodes != null)
                {
                    bool present = false;
                    foreach (var n in (IEnumerable)nodes) if (ReferenceEquals(n, created)) { present = true; break; }
                    if (!present) { CallMethod(nodes, "Add", new object[] { created }, 1); add = "added to flow Nodes"; }
                    else add = "already in flow Nodes";
                }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create web block", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = FlowBlockCount(flow);
        return Json(new { ok = created != null && err == null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), flow = flowName, flowBlocksBefore = before, flowBlocksAfter = after, add = add, factoryCandidates = diag, error = err });
    }

    // Count the non-screen (web block) nodes in a web flow.
    static int FlowBlockCount(object flow)
    {
        int n = 0;
        try
        {
            var nodes = GetProp(flow, "Nodes") as IEnumerable;
            if (nodes != null) foreach (var node in nodes) if (!IsScreen(node)) n++;
        }
        catch { }
        return n;
    }

    // Hunt for a from-scratch web-block factory on the eSpace and the target WebFlow.
    // Prefers Create*/New* methods whose return type looks like a web block, then falls
    // back to a factory literally named CreateWebBlock/CreateBlock/NewWebBlock.
    static MethodInfo FindWebBlockFactory(object es, object flow, out object target)
    {
        target = null;
        foreach (var candidate in new[] { es, flow })
        {
            foreach (var m in AllCreateMethods(candidate))
            {
                if (LooksLikeWebBlockType(m.ReturnType)) { target = candidate; return m; }
            }
        }
        foreach (var candidate in new[] { es, flow })
        {
            foreach (var mName in new[] { "CreateWebBlock", "CreateBlock", "NewWebBlock" })
            {
                var m = FindMethod(candidate, mName, 2) ?? FindMethod(candidate, mName, 1);
                if (m != null) { target = candidate; return m; }
            }
        }
        return null;
    }

    // Enumerate all Create*/New*/Add* public instance methods (interfaces + concrete) on a target.
    static IEnumerable<MethodInfo> AllCreateMethods(object target)
    {
        var seen = new HashSet<string>();
        foreach (var iface in target.GetType().GetInterfaces())
        {
            foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("New") || m.Name.StartsWith("Add"))) continue;
                if (m.GetParameters().Length < 1 || m.GetParameters().Length > 2) continue;
                if (seen.Add(m.Name + m.GetParameters().Length)) yield return m;
            }
        }
        foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("New") || m.Name.StartsWith("Add"))) continue;
            if (m.GetParameters().Length < 1 || m.GetParameters().Length > 2) continue;
            if (seen.Add(m.Name + m.GetParameters().Length)) yield return m;
        }
    }

    // A Create* return type is a web block if its name mentions WebBlock or it implements IWebBlock.
    static bool LooksLikeWebBlockType(Type t)
    {
        if (t == null) return false;
        if (t.Name.IndexOf("WebBlock", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (t.Name == "Object" || t.Name == "IObjectSignature" || t.IsGenericParameter) return false;
        try { foreach (var i in t.GetInterfaces()) if (i.Name == "IWebBlock") return true; } catch { }
        return false;
    }

    // Diagnostic: dump all Create*/New*/Add* method signatures on eSpace + flow (for debugging when no factory is found).
    static string WebBlockFactoryCandidates(object es, object flow)
    {
        var list = new List<string>();
        foreach (var candidate in new[] { es, flow })
            foreach (var m in AllCreateMethods(candidate))
                list.Add(candidate.GetType().Name + "." + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")->" + m.ReturnType.Name);
        return string.Join(" | ", list);
    }

    // Delete a screen node from a web flow's Nodes. Removes the named IScreen node and
    // reports remaining screen count. Uses CallMethod(node, "Delete") inside a real command.
    static string DeleteScreenFromFlow(string module, string flow, string screen)
    {
        if (string.IsNullOrWhiteSpace(flow)) return Json(new { ok = false, error = "flow required" });
        if (string.IsNullOrWhiteSpace(screen)) return Json(new { ok = false, error = "screen required" });
        return RunCmd(module, "delete screen from flow", es =>
        {
            var wf = FindWebFlow(es, flow);
            if (wf == null) throw new Exception("web flow not found: " + flow);
            object target = null;
            var nodes = GetProp(wf, "Nodes") as IEnumerable;
            if (nodes != null)
                foreach (var node in nodes)
                {
                    if (!IsScreen(node)) continue;
                    try { if ((GetProp(node, "Name") as string) == screen) { target = node; break; } } catch { }
                }
            if (target == null) throw new Exception("screen '" + screen + "' not found in flow '" + flow + "'");
            CallMethod(target, "Delete", null, 0);
            int remaining = 0;
            nodes = GetProp(wf, "Nodes") as IEnumerable;
            if (nodes != null) foreach (var node in nodes) if (IsScreen(node)) remaining++;
            return "deleted screen '" + screen + "' from flow '" + flow + "' (remaining screens: " + remaining + ")";
        });
    }

    static string AddContainer(string module, string screen, string parent, string name, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add container", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "container", name);
            SetStyleClassesOn(w, styleClass);
            return "created container '" + name + "' in " + (string.IsNullOrEmpty(parent) ? "screen" : parent) + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    static string AddExpressionWidget(string module, string screen, string parent, string name, string value, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add expression", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "expression", name);
            if (!string.IsNullOrEmpty(value)) { try { CallMethod(w, "SetValue", new object[] { value }, 1); } catch { try { SetProp(w, "Text", value); } catch { } } }
            SetStyleClassesOn(w, styleClass);
            return "created expression '" + name + "' = " + (value ?? "");
        });
    }

    static string AddTextWidget(string module, string screen, string parent, string name, string text, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add text", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "text", name);
            if (text != null) { try { SetProp(w, "Text", text); } catch { } }
            SetStyleClassesOn(w, styleClass);
            return "created text '" + name + "' = " + (text ?? "");
        });
    }

    static string AddLinkWidget(string module, string screen, string parent, string name, string text, string targetScreen, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add link", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "link", name);
            // Destination first (best-effort, non-fatal).
            if (!string.IsNullOrEmpty(targetScreen))
            {
                var tgt = FindScreen(es, targetScreen);
                if (tgt != null)
                {
                    try
                    {
                        var onClick = GetProp(w, "OnClick");
                        if (onClick == null) onClick = CallMethod(w, "CreateOnClick", null, 0);
                        SetProp(onClick, "Destination", tgt);
                    }
                    catch (Exception e) { Log("link destination set failed: " + e.Message); }
                }
            }
            // Label (best-effort): NRWeb Link caption is the inner Text child's Text;
            // also try common direct properties/methods (SetTitle exists on Traditional Link only).
            if (!string.IsNullOrEmpty(text)) SetWidgetLinkLabel(w, text);
            SetStyleClassesOn(w, styleClass);
            return "created link '" + name + "' (text=" + (text ?? "") + ", target=" + (targetScreen ?? "(none)") + ")";
        });
    }

    // Best-effort label setter for a Reactive Link (which has no SetTitle method). The
    // caption lives on the link's inner Text child; also try direct Text/Caption/Value
    // props and Set-title-like methods.
    static void SetWidgetLinkLabel(object link, string text)
    {
        try { SetProp(link, "Text", text); } catch { }
        try { SetProp(link, "Caption", text); } catch { }
        try { SetProp(link, "Value", text); } catch { }
        try { CallMethod(link, "SetTitle", new object[] { text }, 1); } catch { }
        try { CallMethod(link, "SetValue", new object[] { text }, 1); } catch { }
        try
        {
            var children = GetProp(link, "Widgets") as IEnumerable;
            if (children != null)
            {
                foreach (var c in children.Cast<object>())
                {
                    try { if (c.GetType().Name.IndexOf("Text", StringComparison.Ordinal) >= 0) { SetProp(c, "Text", text); break; } } catch { }
                }
            }
        }
        catch { }
    }

    static string SetWidgetProperty(string module, string screen, string widgetName, string propName, string propValue)
    {
        if (string.IsNullOrEmpty(widgetName)) return Json(new { ok = false, error = "widgetName required" });
        return RunCmd(module, "set widget property", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widgetName);
            if (w == null) throw new Exception("widget not found: " + widgetName);
            SetProp(w, propName, propValue);
            return "set " + propName + " = " + (propValue ?? "") + " on '" + widgetName + "' (" + w.GetType().Name + ")";
        });
    }

    // Read-only diagnostic: dump a widget's type, its Set*/Text*/Caption*/Navigate methods,
    // its string properties, and (if present) its OnClick object + OnClick properties.
    static string ProbeWidgetMembers(string module, string screen, string widgetName)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var w = FindWidget(sc, widgetName);
        if (w == null) return Json(new { ok = false, error = "widget not found: " + widgetName });
        var t = w.GetType();
        var methodNames = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Set", StringComparison.Ordinal) || n.IndexOf("Text", StringComparison.Ordinal) >= 0 || n.IndexOf("Caption", StringComparison.Ordinal) >= 0 || n.IndexOf("Navigate", StringComparison.Ordinal) >= 0 || n == "CreateOnClick")
            .Distinct().ToList();
        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType == typeof(string) || (p.PropertyType?.FullName ?? "").Contains("Destination") || (p.PropertyType?.FullName ?? "").IndexOf("Navigate", StringComparison.Ordinal) >= 0)
            .Select(p => p.Name).ToList();
        object onClick = null; try { onClick = GetProp(w, "OnClick"); } catch { }
        var onClickProps = new List<string>();
        if (onClick != null) { try { onClickProps = onClick.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList(); } catch { } }
        // Read child widgets + their Text value, and the OnClick.Destination screen name,
        // so we can VERIFY link labels and destinations were actually written.
        var children = new List<object>();
        try {
            var cw = GetProp(w, "Widgets") as IEnumerable;
            if (cw != null) foreach (var c in cw.Cast<object>()) {
                var ctn = c?.GetType();
                var tv = "";
                try { if (c != null) { var tvObj = GetProp(c, "Text"); if (tvObj != null) tv = tvObj.ToString(); } } catch { }
                children.Add(new { name = (c != null ? GetProp(c, "Name") as string : null), type = ctn?.FullName, text = tv });
            }
        } catch { }
        var dest = "";
        try { if (onClick != null) { var d = GetProp(onClick, "Destination"); if (d != null) dest = d.ToString(); } } catch { }
        return Json(new { ok = true, widget = widgetName, type = t.FullName, setTextMethods = methodNames, stringProps = props, onClickType = onClick?.GetType().FullName ?? "(none)", onClickProps = onClickProps, onClickDestination = dest, children = children });
    }

    // Deep probe: find a widget ANYWHERE on a screen including inside layout placeholder fills
    // (Header/MainContent/Footer/...) and dump its full public property list + all methods whose
    // names mention Style/Css/Class/Set. Reveals the exact surface to set style classes on
    // Reactive containers (StyleClasses may be exposed via an interface property or a method).
    static string ProbeWidgetDeep(string module, string screen, string widgetName)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var w = FindWidgetDeep(sc, widgetName);
        if (w == null) return Json(new { ok = false, error = "widget not found anywhere on screen: " + widgetName });
        var t = w.GetType();
        var props = new List<object>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.Name.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("Css", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name == "Name")
            {
                string cur = null;
                try { var v = p.GetValue(w, null); cur = v?.ToString(); } catch { }
                props.Add(new { name = p.Name, type = p.PropertyType.FullName, canWrite = p.CanWrite, current = cur });
            }
        var methods = new List<string>();
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (m.Name.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Css", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Set", StringComparison.OrdinalIgnoreCase) == 0)
                methods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
        // Also list all interfaces containing 'Style' or 'Css'
        var ifaces = new List<string>();
        foreach (var i in t.GetInterfaces())
            if (i.Name.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 || i.Name.IndexOf("Css", StringComparison.OrdinalIgnoreCase) >= 0 || i.Name.IndexOf("Format", StringComparison.OrdinalIgnoreCase) >= 0)
                ifaces.Add(i.FullName);
        return Json(new { ok = true, type = t.FullName, styleProps = props, styleMethods = methods, styleInterfaces = ifaces });
    }

    // Probe the DATA SOURCING collections (DataActions / ScreenAggregates) on a web block OR a
    // screen in an OPEN module � read-only diagnostic. Dumps each collection's concrete type,
    // its factory methods (Create*/Add*/New* on the collection AND on the block/screen), and �
    // if any items exist � each item's type + key properties. Use to discover the correct
    // factory (CreateAggregate? CreateDataAction? Add?) and the aggregate's settable surface before
    // implementing add_aggregate_to_block / add_data_action_to_block.
    static string ProbeDataSources(string module, string block, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object target = null; string targetKind = null;
        if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; }
        else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen"; }
        else return Json(new { ok = false, error = "pass block=<block> or screen=<screen>" });
        if (target == null) return Json(new { ok = false, error = targetKind + " not found" });
        var cols = new List<object>();
        foreach (var collName in new[] { "DataActions", "ScreenAggregates" })
        {
            IEnumerable coll = null; try { coll = GetProp(target, collName) as IEnumerable; } catch { }
            if (coll == null) { cols.Add(new { name = collName, present = false }); continue; }
            int count = 0; var items = new List<object>();
            foreach (var it in coll) { count++; if (items.Count < 5) items.Add(new { type = it.GetType().FullName, name = SafeGet(it, "Name") }); }
            var factoryMethods = new List<string>();
            var seen = new HashSet<string>();
            foreach (var t in AllTypes(coll.GetType()))
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
                    if (m.GetParameters().Length > 2) continue;
                    if (!seen.Add(m.Name + m.GetParameters().Length + m.ReturnType.Name)) continue;
                    factoryMethods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")->" + m.ReturnType.Name);
                }
            var targetFactories = new List<string>();
            var seen2 = new HashSet<string>();
            foreach (var t in AllTypes(target.GetType()))
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New"))) continue;
                    if (m.Name.IndexOf("DataAction", StringComparison.OrdinalIgnoreCase) < 0 && m.Name.IndexOf("Aggregate", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (m.GetParameters().Length > 3) continue;
                    if (!seen2.Add(m.Name + m.GetParameters().Length)) continue;
                    targetFactories.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")->" + m.ReturnType.Name);
                }
            cols.Add(new { name = collName, present = true, collType = coll.GetType().FullName, count = count, items = items, collectionFactories = factoryMethods, targetFactories = targetFactories });
        }
        return Json(new { ok = true, module = module, target = targetKind, name = SafeGet(target, "Name"), type = target.GetType().FullName, collections = cols });
    }

    static string SafeGet(object o, string prop)
    {
        try { var v = GetProp(o, prop); return v?.ToString(); } catch { return null; }
    }

    // Map a human widget kind to its NRWidgets plugin concrete class name (the class with a
    // nested Kind exposing Instance.Descriptor, proven live via probe_widget_kinds). All Reactive
    // widgets are plugin CustomWidgets created via CreateWidget(descriptor), NOT CreateWidget<T>.
    // headercell/rowcell: Reactive table cells. Reflection-verified on SS 11.55.83 -
    // ServiceStudio.Plugin.NRWidgets.HeaderCell and .RowCell follow the exact Button pattern
    // (nested Kind with static Instance field + Descriptor -> CustomObjectDescriptor; base
    // ServiceStudio.Model.NRWebWidgets/CustomWidget, same as Button). The TableRecords class has
    // NO column/cell factory methods - cells are plain widgets created under the table's
    // HeaderRow/Row IContent hosts (their only concrete implementation is CustomPlaceholderWidget,
    // the same host type as containers' 'content' placeholder), addressed by add_nr_widget's
    // dotted parent syntax: kind=headercell parent="Table.HeaderRow", kind=rowcell parent="Table.Row".
    static readonly Dictionary<string, string> NRWidgetKindClasses = new Dictionary<string, string>
    {
        ["button"] = "Button",
        ["label"] = "Label",
        ["input"] = "Input",
        ["textarea"] = "TextArea",
        ["checkbox"] = "Checkbox",
        ["dropdown"] = "Dropdown",
        ["radio"] = "RadioButton",
        ["radio-group"] = "RadioGroup",
        ["switch"] = "Switch",
        ["list"] = "List",
        ["list-item"] = "ListItem",
        ["table"] = "TableRecords",
        ["headercell"] = "HeaderCell",
        ["rowcell"] = "RowCell",
        ["image"] = "Image",
        ["icon"] = "Icon",
        ["form"] = "Form",
        ["button-group"] = "ButtonGroup",
        ["container"] = "Container",
        ["expression"] = "Expression",
        ["link"] = "Link",
        ["html"] = "AdvancedHtml",
        ["upload"] = "Upload",
        ["popover"] = "Popover",
        ["popup"] = "Popup",
        ["list-item-action"] = "ListItemAction",
    };

    // add_nr_widget: create ANY Reactive custom widget (Label/Input/TextArea/Checkbox/Dropdown/
    // Radio/Switch/List/Table/Image/Icon/Form/ButtonGroup/...) on a screen OR web block (and inside
    // a named container). Uses the Button-proven descriptor pattern: <KindClassName>+Kind.Instance
    // -> Descriptor -> CreateWidget(descriptor) -> name+style. Kind -> class via NRWidgetKindClasses.
    static string AddNRWidget(string module, string screen, string block, string parent, string kind, string name, string styleClass, string text)
    {
        if (string.IsNullOrEmpty(kind)) return Json(new { ok = false, error = "kind required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (!NRWidgetKindClasses.TryGetValue(kind.ToLowerInvariant(), out var classSimple))
            return Json(new { ok = false, error = "unknown widget kind: " + kind + " (valid: " + string.Join(", ", NRWidgetKindClasses.Keys) + ")" });
        string fullName = "ServiceStudio.Plugin.NRWidgets." + classSimple;
        return RunCmd(module, "add nr widget", es =>
        {
            object target = null; string targetKind = null;
            if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen '" + screen + "'"; }
            else if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block '" + block + "'"; }
            if (target == null) throw new Exception("screen or block required");
            object host = target;
            if (!string.IsNullOrEmpty(parent))
            {
                object par = null;
                var colonIdx = parent.LastIndexOf(':');
                var suffix = colonIdx > 0 ? parent.Substring(colonIdx + 1) : null;
                var isBranchSyntax = colonIdx > 0 && (string.Equals(suffix, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(suffix, "False", StringComparison.OrdinalIgnoreCase));
                if (isBranchSyntax)
                {
                    // If-branch parent syntax "IfName:True" / "IfName:False" - children of an If live in
                    // its Branches[0]/[1] (IfBranch.ChildWidgets), not in a 'content' placeholder.
                    var ifName = parent.Substring(0, colonIdx);
                    object ifWidget = null;
                    if (!string.IsNullOrEmpty(screen)) ifWidget = FindWidgetDeep(target, ifName);
                    else ifWidget = FindWidget(target, ifName);
                    if (ifWidget == null) throw new Exception("If widget not found: " + ifName);
                    var branches = GetProp(ifWidget, "Branches") as IEnumerable;
                    if (branches == null) throw new Exception("widget '" + ifName + "' has no Branches collection");
                    int idx = string.Equals(suffix, "True", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    object branch = null;
                    int i = 0;
                    foreach (var b in branches) { if (i == idx) { branch = b; break; } i++; }
                    if (branch == null) throw new Exception("branch index " + idx + " not found on '" + ifName + "'");
                    par = branch;
                    host = branch; // IfBranch.ChildWidgets is the widget-tree surface; no content placeholder step
                }
                else
                {
                    var widgetPart = colonIdx > 0 ? parent.Substring(0, colonIdx) : parent;
                    var placeholderPart = colonIdx > 0 ? suffix : null;
                    // Dotted content syntax "Table.Row" / "Table.HeaderRow": Row/HeaderRow are
                    // IContent (not FindWidget-addressable); resolve the content object as host.
                    var dotted = ResolveDottedContent(target, parent);
                    if (dotted != null) { par = dotted; host = dotted; }
                    else
                    {
                    if (!string.IsNullOrEmpty(screen)) par = FindWidgetDeep(target, widgetPart);
                    else par = FindWidget(target, widgetPart);
                    if (par == null) throw new Exception("parent widget not found: " + widgetPart);
                    host = par;
                    }
                    // Named-placeholder syntax "Widget:placeholderName" (e.g. "DetailsListItem:rightActions")
                    // targets a NAMED placeholder on the widget instead of the default 'content' one.
                    IEnumerable phCollForNamed = null;
                    try { phCollForNamed = GetProp(par, "Placeholders") as IEnumerable; } catch { }
                    if (phCollForNamed != null)
                    {
                        foreach (var ph in phCollForNamed)
                        {
                            var phName = GetProp(ph, "Name") as string ?? "";
                            if (placeholderPart != null && string.Equals(phName, placeholderPart, StringComparison.OrdinalIgnoreCase)) { host = ph; par = ph; break; }
                        }
                    }
                    if (ReferenceEquals(host, par))
                    {
                        // A container's children live on its 'content' CustomPlaceholderWidget
                        // (RADIO/radio-group/dropdown/list/container all nest there in Reactive).
                        // Without this, CreateWidget(descriptor) on the container itself throws
                        // "CreateWidget(descriptor) not found on ...Container" (the widget tree
                        // surface is the content placeholder, not the container node).
                        object contentHolder = null;
                        IEnumerable parPhColl = null;
                        try { parPhColl = GetProp(par, "Placeholders") as IEnumerable; } catch { }
                        if (parPhColl != null)
                        {
                            foreach (var ph in parPhColl)
                            {
                                var phName = GetProp(ph, "Name") as string ?? "";
                                if (string.Equals(phName, "content", StringComparison.OrdinalIgnoreCase)) { contentHolder = ph; break; }
                            }
                        }
                        if (contentHolder != null) { host = contentHolder; par = contentHolder; }
                    }
                }
            }
            object w = null;
            try
            {
                w = CreateNRCustomWidgetOn(host, fullName, name, styleClass);
            }
            catch
            {
                // Fallback for hosts without a CreateWidget surface (e.g. IfBranch, Popup body):
                // create on the block/screen root, then ChangeParent onto the desired host.
                if (!ReferenceEquals(host, target))
                {
                    w = CreateNRCustomWidgetOn(target, fullName, name, styleClass);
                    CallMethod(w, "ChangeParent", new object[] { host }, 1);
                }
                else throw;
            }
            if (w == null) throw new Exception("CreateNRCustomWidgetOn returned null for kind=" + kind + " host=" + host.GetType().Name);
            string extra = "";
            if (!string.IsNullOrEmpty(text) && (kind.ToLowerInvariant() == "label" || kind.ToLowerInvariant() == "input" || kind.ToLowerInvariant() == "textarea"))
            {
                try
                {
                    var vp = w.GetType().GetProperty("Text");
                    if (vp != null && vp.GetSetMethod() != null) vp.SetValue(w, text);
                    else if (kind.ToLowerInvariant() == "label") SetWidgetLinkLabel(w, text);
                    extra = "; text=" + text;
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; extra = "; text set failed: " + r.Message; }
            }
            return "created " + kind + " '" + name + "' in " + targetKind + " via descriptor (" + (w?.GetType().FullName ?? "?") + ")" + extra;
        });
    }

    // Generic: create an NRWidgets plugin CustomWidget by its concrete class fullName (e.g.
    // ServiceStudio.Plugin.NRWidgets.Label). Resolves via the nested Kind.Instance.Descriptor
    // (Button-proven), falls back to <class>Descriptor ctor, then CreateWidget(descriptor).
    static object CreateNRCustomWidgetOn(object target, string classFullName, string name, string styleClass)
    {
        object descriptor = null; string descVia = null;
        Type kindType = FindType(classFullName + "+Kind");
        if (kindType == null) kindType = FindTypeLoaded(classFullName + "+Kind");
        object kindInst = null;
            if (kindType != null)
            {
                foreach (var f in kindType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    if (f.Name == "Instance") { try { kindInst = f.GetValue(null); } catch { } }
                if (kindInst == null)
                    foreach (var p in kindType.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        if (p.Name == "Instance") { try { kindInst = p.GetValue(null, null); } catch { } }
                // Heuristic: some model-widget Kinds (e.g. NRWebWidgets+If+Kind) don't expose a
                // member literally named "Instance" - find ANY static member whose value is
                // assignable to the Kind type itself (a self-typed singleton).
                if (kindInst == null)
                {
                    foreach (var f in kindType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        try { var v = f.GetValue(null); if (v != null && kindType.IsInstanceOfType(v)) { kindInst = v; descVia = classFullName + "+Kind." + f.Name + "(heuristic)"; break; } } catch { }
                    if (kindInst == null)
                        foreach (var p in kindType.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                            try { var v = p.GetValue(null, null); if (v != null && kindType.IsInstanceOfType(v)) { kindInst = v; descVia = classFullName + "+Kind." + p.Name + "(heuristic)"; break; } } catch { }
                }
                if (kindInst != null && descriptor == null)
                {
                    descriptor = GetProp(kindInst, "Descriptor");
                    if (descriptor == null)
                    {
                        // Heuristic: any instance property whose value looks like a widget descriptor.
                        foreach (var p in kindInst.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (p.GetIndexParameters().Length > 0) continue;
                            try
                            {
                                var v = p.GetValue(kindInst, null);
                                if (v != null && (v.GetType().Name.Contains("Descriptor") || v.GetType().Name.Contains("Widget"))) { descriptor = v; descVia = (descVia ?? classFullName + "+Kind") + "." + p.Name + "(heuristic)"; break; }
                            }
                            catch { }
                        }
                    }
                    if (descriptor == null) descVia = classFullName + "+Kind(resolved, no descriptor prop)";
                }
            }
        if (descriptor == null)
        {
            Type descType = FindType(classFullName + "Descriptor");
            if (descType != null)
            {
                try { descriptor = Activator.CreateInstance(descType); descVia = "new " + descType.Name + "()"; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception(classFullName + " descriptor fallback failed: " + r.Message); }
            }
        }
        if (descriptor == null) throw new Exception("no descriptor for " + classFullName + " (looked for +Kind.Instance.Descriptor and Descriptor ctor)");

        MethodInfo cwMethod = null;
        foreach (var iface in target.GetType().GetInterfaces())
            foreach (var m in iface.GetMethods())
                if (m.Name == "CreateWidget" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(descriptor.GetType()))
                    cwMethod = m;
        if (cwMethod == null)
            foreach (var iface in target.GetType().GetInterfaces())
                foreach (var m in iface.GetMethods())
                    if (m.Name == "CreateWidget" && !m.IsGenericMethod && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.IsAssignableFrom(descriptor.GetType()))
                        cwMethod = m;
        if (cwMethod == null) throw new Exception("CreateWidget(descriptor) not found on " + target.GetType().FullName + " for " + descriptor.GetType().Name);

        object w;
        try { w = cwMethod.GetParameters().Length == 1 ? cwMethod.Invoke(target, new object[] { descriptor }) : cwMethod.Invoke(target, new object[] { descriptor, null }); }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception("CreateWidget(descriptor) invoke failed: " + r.Message); }
        if (w == null) throw new Exception("CreateWidget(descriptor) returned null for " + classFullName);

        try { SetProp(w, "Name", name); } catch { }
        try { SetStyleClassesOn(w, styleClass); } catch (Exception e) { Log("style set failed for " + classFullName + ": " + e.Message); }
        Log("CreateNRCustomWidgetOn(" + classFullName + "," + name + ") via " + descVia + " => " + (w?.GetType().Name ?? "null"));
        return w;
    }

    // Read-only diagnostic: enumerate all widget KINDS at runtime — concrete classes (or nested
    // Kind classes) whose name matches a widget-family name (Label/Input/Checkbox/Dropdown/...)
    // and that expose a Descriptor (static Instance → Descriptor, like Button+Kind). This reveals
    // the exact NRWidgets CustomWidget descriptor objects needed to extend WidgetKindCandidates.
    static string ProbeWidgetKinds()
    {
        var families = new[] { "Label", "Input", "TextArea", "Checkbox", "Dropdown", "Radio", "Switch", "Icon", "Image", "List", "Table", "Form", "ButtonGroup", "Button", "Container", "Expression", "Link", "Html", "Placeholder" };
        var result = new List<object>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                try
                {
                    var name = t.Name;
                    if (name == "Kind") continue;
                    if (!families.Any(f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var nestedKinds = new List<string>();
                    foreach (var n in t.GetNestedTypes())
                    {
                        if (n.Name != "Kind") continue;
                        object inst = null;
                        foreach (var f in n.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                            if (f.Name == "Instance") { try { inst = f.GetValue(null); } catch { } }
                        if (inst == null)
                            foreach (var p in n.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                                if (p.Name == "Instance") { try { inst = p.GetValue(null, null); } catch { } }
                        string descType = null;
                        if (inst != null)
                        {
                            try { var d = GetProp(inst, "Descriptor"); descType = d?.GetType().FullName; } catch { }
                            nestedKinds.Add(n.FullName + " [Descriptor->" + (descType ?? "?") + "]");
                        }
                        else nestedKinds.Add(n.FullName + " [no Instance]");
                    }
                    var descFields = new List<string>();
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        if (f.Name.IndexOf("Descriptor", StringComparison.OrdinalIgnoreCase) >= 0)
                            descFields.Add(f.Name + ":" + f.FieldType.Name);
                    if (nestedKinds.Count == 0 && descFields.Count == 0) continue;
                    result.Add(new { type = t.FullName, nestedKinds = nestedKinds, descriptorFields = descFields });
                }
                catch { }
            }
        }
        return Json(new { ok = true, count = result.Count, kinds = result });
    }

    // add_aggregate_to_block: create a SCREEN AGGREGATE on a web block (the block's
    // ScreenAggregates/DataActions sourcing) with a source entity from a consumed reference.
    // Factory proven live via probe_data_sources: block.CreateScreenAggregate(Boolean,String,IKey)
    // -> IScreenAggregate. Tries the factory by interface then concrete type, then sets the source
    // entity (C_AGGREGATE CreateSource/CreateAddSourceOperation or Source property on RootOperation).
    static string AddAggregateToTarget(string module, string block, string screen, string name, string entityName, string producerModule)
    {
        return RunCmd(module, "add aggregate", es =>
        {
            object target = null; string targetKind = null;
            if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; }
            else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen"; }
            if (target == null) throw new Exception(targetKind + " not found");
            object entity = null;
            if (!string.IsNullOrEmpty(entityName))
            {
                entity = FindEntityInReferences(es, entityName, producerModule);
                // Fallback: local entity in the same module (e.g. ScratchOrder in FitnessManager).
                if (entity == null) entity = FindEntity(es, entityName);
                if (entity == null) throw new Exception("entity '" + entityName + "' not found in references" + (string.IsNullOrEmpty(producerModule) ? " (pass producerModule)" : " of " + producerModule) + " nor as a local entity (check spelling)");
            }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object agg = null; string via = null; var errs = new List<string>();
            var methods = new List<MethodInfo>();
            foreach (var t in AllTypes(target.GetType()))
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (m.Name == "CreateScreenAggregate" && m.GetParameters().Length == 3)
                        methods.Add(m);
            if (methods.Count == 0) methods.Add(FindMethod(target, "CreateScreenAggregate", 3));
            foreach (var m in methods)
            {
                // CreateScreenAggregate(isClientSide, name, key): isClientSide MUST be false.
                // true creates a client-side/local aggregate which corrupts the module.
                try { agg = m.Invoke(target, new object[] { false, name, key }); via = target.GetType().Name + "." + m.Name; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
            }
            if (agg == null) throw new Exception("no CreateScreenAggregate factory worked on " + target.GetType().Name + " | " + string.Join(" ; ", errs));
            if (entity != null)
            {
                string srcReport = SetAggregateSource(agg, entity);
                return "created aggregate '" + name + "' in " + targetKind + " '" + ((string.IsNullOrEmpty(block) ? screen : block)) + "' via " + via + " (" + agg.GetType().Name + "); source: " + srcReport;
            }
            return "created aggregate '" + name + "' in " + targetKind + " '" + ((string.IsNullOrEmpty(block) ? screen : block)) + "' via " + via + " (" + agg.GetType().Name + ")";
        });
    }

    static string AddAggregateToBlock(string module, string block, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return AddAggregateToTarget(module, block, null, name, entityName, producerModule);
    }

    static string AddAggregateToScreen(string module, string screen, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return AddAggregateToTarget(module, null, screen, name, entityName, producerModule);
    }

    static string AddDataActionToTarget(string module, string block, string screen, string name)
    {
        return RunCmd(module, "add data action", es =>
        {
            object target = null; string targetKind = null;
            if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; }
            else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen"; }
            if (target == null) throw new Exception(targetKind + " not found");
            var das = GetProp(target, "DataActions") as IEnumerable;
            if (das != null)
                foreach (var d in das)
                    try { if ((GetProp(d, "Name") as string) == name) throw new Exception("data action already exists: " + name); } catch (Exception e) { if (e.Message.Contains("already exists")) throw; }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object da = null; string via = null; var errs = new List<string>();
            var methods = new List<MethodInfo>();
            foreach (var t in AllTypes(target.GetType()))
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (m.Name == "CreateDataAction" && m.GetParameters().Length == 2)
                        methods.Add(m);
            if (methods.Count == 0) methods.Add(FindMethod(target, "CreateDataAction", 2));
            foreach (var m in methods)
            {
                try { da = m.Invoke(target, new object[] { name, key }); via = target.GetType().Name + "." + m.Name; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
            }
            if (da == null) throw new Exception("no CreateDataAction factory worked on " + target.GetType().Name + " | " + string.Join(" ; ", errs));
            return "created data action '" + name + "' in " + targetKind + " '" + ((string.IsNullOrEmpty(block) ? screen : block)) + "' via " + via + " (" + da.GetType().Name + ")";
        });
    }

    static string AddDataActionToBlock(string module, string block, string name)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return AddDataActionToTarget(module, block, null, name);
    }

    static string AddDataActionToScreen(string module, string screen, string name)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return AddDataActionToTarget(module, null, screen, name);
    }

    static string SetAggregateSource(object agg, object entity)
    {
        var tried = new List<string>(); var errors = new List<string>();
        // path 1: IDatabaseAggregate.CreateSource(IDataSource dataSource, string name = null)
        try { var src = FindMethod(agg, "CreateSource", 1); if (src != null) { var source = src.Invoke(agg, new[] { entity }); if (source != null) { tried.Add("CreateSource(source) -> " + source.GetType().Name); return "via CreateSource: " + source.GetType().Name; } } } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errors.Add("CreateSource(1): " + r.Message); }
        // path 2: RootOperation = CreateAddSourceOperation(entity)
        try { var addOp = FindMethod(agg, "CreateAddSourceOperation", 1); if (addOp != null) { var op = addOp.Invoke(agg, new[] { entity }); if (op != null) { var setP = op.GetType().GetProperty("Source"); if (setP != null && setP.CanWrite) setP.SetValue(op, entity); tried.Add("CreateAddSourceOperation+Source"); return "via CreateAddSourceOperation+Source: " + op.GetType().Name; } } } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errors.Add("CreateAddSourceOperation: " + r.Message); }
        // path 3: Sources.Add(CreateSource(entity))
        try { var src2 = FindMethod(agg, "CreateSource", 2); if (src2 != null) { var source = src2.Invoke(agg, new[] { entity, nameof(entity) }); if (source != null) { var coll = GetProp(agg, "Sources") as IEnumerable; if (coll != null) { var add = FindMethod(coll, "Add", 1); if (add != null) { add.Invoke(coll, new[] { source }); tried.Add("CreateSource(name)+Add"); return "via Sources.Add: " + source.GetType().Name; } } } } } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errors.Add("CreateSource(2)+Add: " + r.Message); }
        return "no source path succeeded; " + (tried.Count > 0 ? string.Join(",", tried) : "errors: " + string.Join(" ; ", errors));
    }

    // Delete a data action (DataScreenActionFlow) from a block or screen by name (CallMethod Delete).
    static string DeleteDataAction(string module, string block, string screen, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "delete data action", es =>
        {
            object target = null; string targetKind = null;
            if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; }
            else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen"; }
            if (target == null) throw new Exception(targetKind + " not found");
            var das = GetProp(target, "DataActions") as IEnumerable;
            object da = null;
            if (das != null)
                foreach (var d in das)
                    try { if ((GetProp(d, "Name") as string) == name) { da = d; break; } } catch { }
            if (da == null) throw new Exception("data action not found: " + name);
            CallMethod(da, "Delete", null, 0);
            return "deleted data action '" + name + "' from " + targetKind + " '" + ((string.IsNullOrEmpty(block) ? screen : block)) + "'";
        });
    }

    // Delete an aggregate (WebScreenDataSet/ScreenAggregate) from a block or screen by name.
    static string DeleteAggregate(string module, string block, string screen, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "delete aggregate", es =>
        {
            object target = null; string targetKind = null;
            if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; }
            else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); targetKind = "screen"; }
            if (target == null) throw new Exception(targetKind + " not found");
            var aggs = GetProp(target, "ScreenAggregates") as IEnumerable;
            object agg = null;
            if (aggs != null)
                foreach (var a in aggs)
                    try { if ((GetProp(a, "Name") as string) == name) { agg = a; break; } } catch { }
            if (agg == null) throw new Exception("aggregate not found: " + name);
            CallMethod(agg, "Delete", null, 0);
            return "deleted aggregate '" + name + "' from " + targetKind + " '" + ((string.IsNullOrEmpty(block) ? screen : block)) + "'";
        });
    }

    // SetAggregateSource: try multiple proven surface paths to attach a source entity to an
    // aggregate (IDatabaseAggregate.CreateSource / FullAggregate RootOperation / Sources.Add).

    // Find a widget anywhere on a screen including inside layout placeholder fills and inside
    // nested placeholders (containers keep children in a 'content' CustomPlaceholderWidget).
    static object FindWidgetDeep(object root, string name)
    {
        var widgets = GetProp(root, "Widgets") as IEnumerable;
        if (widgets == null) return null;
        return FindWidgetRecursiveDeep(widgets, name);
    }

    static object FindWidgetRecursiveDeep(IEnumerable widgets, string name)
    {
        foreach (var w in widgets)
        {
            try { if ((GetProp(w, "Name") as string) == name) return w; } catch { }
            var childWidgets = GetProp(w, "Widgets") as IEnumerable;
            if (childWidgets != null)
            {
                var found = FindWidgetRecursiveDeep(childWidgets, name);
                if (found != null) return found;
            }
            // Layout WebBlockInstance: children live under the instance's Placeholders fills.
            IEnumerable phColl = null;
            try { phColl = GetProp(w, "Placeholders") as IEnumerable; } catch { }
            if (phColl == null)
            {
                object inst = null;
                try { inst = GetProp(w, "Instance"); } catch { }
                if (inst != null) { try { phColl = GetProp(inst, "Placeholders") as IEnumerable; } catch { } }
            }
            if (phColl != null)
            {
                foreach (var ph in phColl)
                {
                    var phWidgets = GetProp(ph, "Widgets") as IEnumerable;
                    if (phWidgets == null) continue;
                    var found = FindWidgetRecursiveDeep(phWidgets, name);
                    if (found != null) return found;
                }
            }
            // Containers keep children inside a 'content' placeholder.
            var phColl2 = GetProp(w, "Placeholders") as IEnumerable;
            if (phColl2 != null)
            {
                foreach (var ph in phColl2)
                {
                    var phWidgets = GetProp(ph, "Widgets") as IEnumerable;
                    if (phWidgets == null) continue;
                    var found = FindWidgetRecursiveDeep(phWidgets, name);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    // Delete a widget (and recursively its children) from a screen. Useful for clearing a
    // half-built widget so the screen can be rebuilt cleanly.
    static string DeleteWidget(string module, string screen, string name)
    {
        return RunCmd(module, "delete widget", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, name);
            if (w == null) throw new Exception("widget not found: " + name);
            DeleteWidgetRecursive(w);
            return "deleted widget '" + name + "' (and children)";
        });
    }
    static void DeleteWidgetRecursive(object w)
    {
        IEnumerable children = GetProp(w, "Widgets") as IEnumerable;
        List<object> snapshot = null;
        if (children != null) snapshot = children.Cast<object>().ToList();
        if (snapshot != null) foreach (var c in snapshot) try { DeleteWidgetRecursive(c); } catch { }
        try { CallMethod(w, "Delete", new object[] { true }, 1); }
        catch { try { CallMethod(w, "Delete", new object[] { false }, 1); } catch { try { CallMethod(w, "Delete", null, 0); } catch { } } }
    }

    static string AddHtmlElement(string module, string screen, string parent, string name, string tag, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add html element", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "html", name);
            if (!string.IsNullOrEmpty(tag)) { try { SetProp(w, "Tag", tag); } catch { try { SetProp(w, "HtmlTag", tag); } catch { } } }
            SetStyleClassesOn(w, styleClass);
            return "created html element '" + name + "' (tag=" + (tag ?? "div") + ") in " + (string.IsNullOrEmpty(parent) ? "screen" : parent) + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // AddHtmlText: create an HTML element widget (real <h1>/<h2>/<p>... via its Tag) inside
    // parent (screen or container), set its style class, and place a Text widget with the given
    // text INSIDE the element's content placeholder. This produces real element markup
    // (<h1><span>...</span></h1>) so CSS element selectors (.zombie-hero h1, .zombie-panel h2,
    // .zombie-stat .num, .zombie-hero .tagline) actually match. Wrapped in RunCmd (undo unit).
    static string AddHtmlText(string module, string screen, string parent, string name, string tag, string styleClass, string text)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add html element with text", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            var w = CreateWidgetByKind(par, "html", name);
            if (!string.IsNullOrEmpty(tag)) { try { SetProp(w, "Tag", tag); } catch { try { SetProp(w, "HtmlTag", tag); } catch { } } }
            SetStyleClassesOn(w, styleClass);
            string childName = name + "Text";
            // Place the Text widget inside the html element's content placeholder (like
            // AddInsidePlaceholder does for containers); fall back to the element itself.
            object host = w;
            IEnumerable phColl = null;
            try { phColl = GetProp(w, "Placeholders") as IEnumerable; } catch { }
            if (phColl != null)
            {
                foreach (var ph in phColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, "content", StringComparison.OrdinalIgnoreCase)) { host = ph; break; }
                }
            }
            var t = CreateWidgetByKind(host, "text", childName);
            if (!string.IsNullOrEmpty(text)) { try { SetProp(t, "Text", text); } catch { try { CallMethod(t, "SetValue", new object[] { text }, 1); } catch { } } }
            return "created html element '" + name + "' (tag=" + (tag ?? "div") + ", class=" + (styleClass ?? "-") + ") with text '" + text + "' in " + (string.IsNullOrEmpty(parent) ? "screen" : parent) + " (" + (w?.GetType().Name ?? "?") + " + " + (t?.GetType().Name ?? "?") + ")";
        });
    }

    static string SetStyleClass(string module, string screen, string widget, string styleClass)    {
        return RunCmd(module, "set style class", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget);
            SetStyleClassesOn(w, styleClass);
            return "set StyleClasses='" + (styleClass ?? "") + "' on '" + widget + "'";
        });
    }

    // The module CSS. For a Reactive module with no local themes, this is the eSpace's
    // InvisibleStyleSheet (a stylesheet object). We try a cascade of string setters on it;
    // if none work we fall back to a local theme's StyleSheet (a confirmed settable string),
    // creating one if needed. On total failure we return a diagnostic dump of the sheet's
    // setters so the correct API can be wired next.
    static string SetModuleCss(string module, string css)
    {
        return RunCmd(module, "set module css", es =>
        {
            var diag = new List<string>();
            // 1) Existing local themes (NRThemes then WebThemes) - StyleSheet is a settable
            //    string and is the CSS widget StyleClasses resolve against. Preferred.
            foreach (var propName in new[] { "NRThemes", "WebThemes" })
            {
                var themes = GetProp(es, propName) as IEnumerable;
                if (themes == null) continue;
                foreach (var t in themes)
                {
                    // Theme.StyleSheet is a WebStyleSheet OBJECT, not a string. Set its Value
                    // property (the user-editable CSS) when present; fall back to StyleSheet=string.
                    try
                    {
                        var tSheet = GetProp(t, "StyleSheet");
                        if (tSheet != null && TrySetCssOnSheet(tSheet, css ?? "", diag))
                            return "set StyleSheet.Value on " + propName + " '" + (GetProp(t, "Name") ?? "?") + "' (" + (css?.Length ?? 0) + " chars) via " + diag[diag.Count - 1];
                    }
                    catch (Exception e) { diag.Add(propName + " StyleSheet.Value failed: " + e.Message); }
                    try { SetProp(t, "StyleSheet", css ?? ""); return "set StyleSheet on " + propName + " '" + (GetProp(t, "Name") ?? "?") + "' (" + (css?.Length ?? 0) + " chars)"; }
                    catch (Exception e) { diag.Add(propName + " StyleSheet set failed: " + e.Message); }
                }
            }
            // 2) The eSpace's InvisibleStyleSheet (a stylesheet object) - try a cascade of
            //    string setters; diagnostic on failure.
            var sheet = GetProp(es, "InvisibleStyleSheet");
            if (sheet != null)
            {
                if (TrySetCssOnSheet(sheet, css, diag)) return "set InvisibleStyleSheet CSS (" + (css?.Length ?? 0) + " chars) via " + diag[diag.Count - 1];
                diag.Add("InvisibleStyleSheet type: " + sheet.GetType().FullName);
                diag.Add(DumpCssSetters(sheet));
            }
            else diag.Add("es.InvisibleStyleSheet is null");
            // 3) Last resort: create a local theme with the CSS and make it the default.
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object newTheme = null;
            try { newTheme = CallMethod(es, "CreateWebTheme", new object[] { "FitnessTheme", key }, 2); }
            catch (Exception e) { diag.Add("CreateWebTheme failed: " + e.Message); }
            if (newTheme != null)
            {
                try
                {
                    var tSheet = GetProp(newTheme, "StyleSheet");
                    if (tSheet != null && TrySetCssOnSheet(tSheet, css ?? "", diag)) { }
                    else try { SetProp(newTheme, "StyleSheet", css ?? ""); } catch { }
                }
                catch { }
                try { SetProp(es, "DefaultWebTheme", newTheme); } catch (Exception e) { diag.Add("DefaultWebTheme set failed: " + e.Message); }
                return "created theme 'FitnessTheme' with CSS (" + (css?.Length ?? 0) + " chars) + set as default";
            }
            return "FAILED to set CSS. Diagnostic:\n" + string.Join("\n", diag);
        });
    }

    // Diagnostic: report the theme stylesheet's CSS source field lengths (user/css/generated/final).
    static string ReadThemeCss(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var themes = new List<object>();
        foreach (var propName in new[] { "NRThemes", "WebThemes" })
        {
            var ts = GetProp(es, propName) as IEnumerable;
            if (ts == null) continue;
            foreach (var t in ts) themes.Add(t);
        }
        var outList = new List<object>();
        foreach (var t in themes)
        {
            var name = GetProp(t, "Name") as string;
            object sheet = null;
            try { sheet = GetProp(t, "StyleSheet"); } catch { }
            var fields = new Dictionary<string, object>();
            if (sheet != null)
            {
                foreach (var fn in new[] { "_cssSource", "_userCssSource", "_generatedCssSource", "_finalCssSource" })
                {
                    var expr = GetField(sheet, fn);
                    var txt = expr == null ? null : ReadLightweightExpressionText(expr);
                    fields[fn] = txt == null ? null : txt.Length;
                }
                try { fields["designTimeValue"] = GetFieldStr(sheet, "designTimeValue")?.Length; } catch { }
            }
            outList.Add(new { name = name, styleSheet = sheet?.GetType().FullName, fields = fields });
        }
        return Json(new { ok = true, themes = outList });
    }

    // Read the actual text content of a theme's _userCssSource field.
    static string ReadUserCssText(string module, string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return Json(new { ok = false, error = "themeName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var theme = FindTheme(es, themeName);
        if (theme == null) return Json(new { ok = false, error = "theme not found: " + themeName });
        var sheet = GetProp(theme, "StyleSheet");
        if (sheet == null) return Json(new { ok = false, error = "theme '" + themeName + "' has no StyleSheet" });
        var expr = GetField(sheet, "_userCssSource");
        var text = expr == null ? null : ReadLightweightExpressionText(expr);
        return Json(new { ok = true, theme = themeName, cssText = text, length = text?.Length ?? 0 });
    }

    // Diagnostic: dump a theme stylesheet's type + full public API (methods, settable props),
    // plus the populated _cssSource expression's concrete type, Clone support, and methods.
    // Use to choose the correct surface for writing the user CSS source.
    static string ProbeSheet(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var outList = new List<object>();
        foreach (var propName in new[] { "NRThemes", "WebThemes" })
        {
            var ts = GetProp(es, propName) as IEnumerable;
            if (ts == null) continue;
            foreach (var t in ts)
            {
                var name = GetProp(t, "Name") as string;
                object sheet = null;
                try { sheet = GetProp(t, "StyleSheet"); } catch { }
                if (sheet == null) { outList.Add(new { theme = name, sheet = "null" }); continue; }
                var methods = new SortedSet<string>();
                foreach (var m in sheet.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName) continue;
                    var ps = string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                    methods.Add(m.Name + "(" + ps + ")");
                }
                var props = new List<object>();
                foreach (var p in sheet.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    props.Add(new { name = p.Name, canWrite = p.CanWrite, type = p.PropertyType.Name });
                object css = null, user = null;
                try { css = GetField(sheet, "_cssSource"); } catch { }
                try { user = GetField(sheet, "_userCssSource"); } catch { }
                var cssMethods = new SortedSet<string>();
                if (css != null)
                    foreach (var m in css.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        var ps = string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                        cssMethods.Add(m.Name + "(" + ps + ")");
                    }
                object ms = ModelServices();
                var msMethods = new SortedSet<string>();
                if (ms != null)
                    foreach (var m in ms.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        var ps = string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                        msMethods.Add(m.Name + "(" + ps + ")");
                    }
                outList.Add(new
                {
                    theme = name,
                    sheetType = sheet.GetType().FullName,
                    sheetMethods = methods,
                    sheetProps = props,
                    cssSourceType = css?.GetType().FullName,
                    userCssSourceType = user?.GetType().FullName,
                    cssSourceCloneable = css is ICloneable,
                    cssSourceMethods = cssMethods,
                    modelServicesMethods = msMethods
                });
            }
        }
        return Json(new { ok = true, sheets = outList });
    }

    // Set the theme stylesheet's USER CSS source (_userCssSource), which is what SS's theme
    // CSS editor displays. Writing _cssSource/CssSource (the previous target) leaves the
    // editor empty because the editor reads the user source. Mirrors ReadLightweightExpressionText.
    static string SetUserModuleCss(string module, string css)
    {
        return RunCmd(module, "set user css", es =>
        {
            var diag = new List<string>();
            var cssStr = css ?? "";
            foreach (var propName in new[] { "NRThemes", "WebThemes" })
            {
                var themes = GetProp(es, propName) as IEnumerable;
                if (themes == null) continue;
                foreach (var t in themes)
                {
                    try
                    {
                        var tSheet = GetProp(t, "StyleSheet");
                        if (tSheet != null && TrySetUserCssOnSheet(tSheet, cssStr, diag))
                            return "set UserCssSource on " + propName + " '" + (GetProp(t, "Name") ?? "?") + "' (" + cssStr.Length + " chars) via " + diag[diag.Count - 1];
                    }
                    catch (Exception e) { diag.Add(propName + " failed: " + e.Message); }
                }
            }
            var sheet = GetProp(es, "InvisibleStyleSheet");
            if (sheet != null)
            {
                try
                {
                    if (TrySetUserCssOnSheet(sheet, cssStr, diag))
                        return "set InvisibleStyleSheet UserCssSource (" + cssStr.Length + " chars) via " + diag[diag.Count - 1];
                }
                catch (Exception e) { diag.Add("InvisibleStyleSheet try failed: " + e.Message); }
            }
            diag.Add("InvisibleStyleSheet: " + (sheet == null ? "null" : "no user css setter"));
            return "FAILED to set user CSS. Diagnostic:\n" + string.Join("\n", diag);
        });
    }

    static bool TrySetUserCssOnSheet(object sheet, string css, List<string> diag)
    {
        // 1) Genuine public API on WebStyleSheet: SetUserCssSource(String) is what SS's
        //    theme CSS editor itself uses. Probe confirmed it exists on the sheet type.
        foreach (var meth in new[] { "SetUserCssSource", "SetCssSource", "SetCorrectCssSource" })
        {
            try { CallMethod(sheet, meth, new object[] { css }, 1); diag.Add("ok:CallMethod " + meth); return true; }
            catch (Exception e) { diag.Add(meth + " failed: " + e.Message); }
        }
        // 2) Existing _userCssSource expression object � clear + fill its elements.
        var expr = GetField(sheet, "_userCssSource");
        if (expr != null)
        {
            if (SetLightweightExpressionText(expr, css)) { diag.Add("ok:filled existing _userCssSource"); return true; }
            diag.Add("_userCssSource present but could not fill elements");
        }
        else diag.Add("_userCssSource field null");
        // 3) Create a fresh expression of the same concrete type as another populated source
        //    (_cssSource/_generatedCssSource) and assign it to the _userCssSource field.
        var proto = GetField(sheet, "_cssSource") ?? GetField(sheet, "_generatedCssSource");
        var newExpr = CreateLightweightExpressionWithText(proto, css);
        if (newExpr != null && SetField(sheet, "_userCssSource", newExpr)) { diag.Add("ok:created+assigned _userCssSource"); return true; }
        diag.Add("create+assign _userCssSource failed");
        // 4) Genuine property setters as a last resort.
        foreach (var prop in new[] { "UserCssSource", "CssSource", "EffectiveValue", "UserEffectiveValue" })
        {
            try { SetProp(sheet, prop, css); diag.Add("ok:SetProp " + prop); return true; } catch { }
        }
        return false;
    }

    // Replace a LightweightExpression's lightweightElements with a single TextElement holding text.
    static bool SetLightweightExpressionText(object expr, string text)
    {
        try
        {
            object elements = null;
            try { elements = GetField(expr, "lightweightElements"); } catch { }
            if (elements == null) { try { elements = GetProp(expr, "lightweightElements"); } catch { } }
            var list = elements as IList;
            if (list == null) list = GetField(expr, "_items") as IList ?? GetField(expr, "array") as IList ?? GetField(expr, "_array") as IList;
            if (list == null) return false;
            list.Clear();
            var el = CreateTextElement(expr, text);
            if (el == null) return false;
            list.Add(el);
            return true;
        }
        catch { return false; }
    }

    // Clone the concrete expression type from a populated prototype and fill it with text.
    static object CreateLightweightExpressionWithText(object protoExpr, string text)
    {
        try
        {
            var t = protoExpr?.GetType();
            if (t == null) return null;
            var expr = CreateInstanceBestEffort(t);
            if (expr == null || !SetLightweightExpressionText(expr, text)) return null;
            return expr;
        }
        catch { return null; }
    }

    // Build a TextElement of the same concrete type as the prototype expression's elements.
    static object CreateTextElement(object protoExpr, string text)
    {
        try
        {
            Type elType = null;
            object elements = null;
            try { elements = GetField(protoExpr, "lightweightElements"); } catch { }
            if (elements == null) { try { elements = GetProp(protoExpr, "lightweightElements"); } catch { } }
            if (elements is IEnumerable coll)
                foreach (var el in coll) { elType = el.GetType(); break; }
            if (elType == null) return null;
            var textEl = CreateInstanceBestEffort(elType);
            if (textEl == null) return null;
            if (!SetField(textEl, "_text", text))
            {
                try { SetProp(textEl, "Text", text); } catch { return null; }
            }
            return textEl;
        }
        catch { return null; }
    }

    static object CreateInstanceBestEffort(Type t)
    {
        try
        {
            var ctor = t.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                ctor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return ctor?.Invoke(null);
        }
        catch { return null; }
    }

    // Set a private backing field across the whole type hierarchy. Returns false if missing.
    static bool SetField(object obj, string name, object value)
    {
        if (obj == null) return false;
        try
        {
            foreach (var t in AllTypes(obj.GetType()))
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { f.SetValue(obj, value); return true; }
            }
        }
        catch { }
        return false;
    }

    // Diagnostic: dump a widget's CustomProperties (esp. 'Style') and its Style/Set methods,
    // to confirm the exact surface for writing container style classes.
    static string ProbeStyleProp(string module, string screen, string widgetName)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var w = FindWidget(sc, widgetName);
        if (w == null) return Json(new { ok = false, error = "widget not found: " + widgetName });
        return Json(DumpWidgetStyleInfo(w, widgetName));
    }

    // Same deep dump but for a widget inside a top-level WEB BLOCK (no layout recursion, safe).
    static string ProbeBlockWidget(string module, string block, string widgetName)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var w = FindWidget(blk, widgetName);
        if (w == null) return Json(new { ok = false, error = "widget not found in block: " + widgetName });
        return Json(DumpWidgetStyleInfo(w, widgetName));
    }

    static Dictionary<string, object> DumpWidgetStyleInfo(object w, string widgetName)
    {
        var t = w.GetType();
        var cps = new List<object>();
        object cpsColl = null;
        try { cpsColl = GetProp(w, "CustomProperties"); } catch { }
        if (cpsColl == null) { try { cpsColl = GetField(w, "_customProperties"); } catch { } }
        if (cpsColl is IEnumerable ce)
        {
            foreach (var cp in ce)
            {
                var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                var ve = GetProp(cp, "ValueExpression");
                var setValueOk = false;
                try { var m = FindMethod(cp, "SetValue", 1); setValueOk = m != null; } catch { }
                cps.Add(new { propertyName = pn, type = cp.GetType().FullName, valueExpressionType = ve?.GetType().FullName, hasSetValue = setValueOk });
            }
        }
        var styleMethods = new List<string>();
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (m.Name.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Set", StringComparison.OrdinalIgnoreCase) == 0)
                styleMethods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
        object stylePropVal = null;
        try { stylePropVal = GetProp(w, "Style"); } catch { }
        var stylePropObj = new List<object>();
        if (stylePropVal != null)
        {
            stylePropObj.Add(new { type = stylePropVal.GetType().FullName });
            foreach (var f in stylePropVal.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                try { stylePropObj.Add(new { f = f.Name + ":" + f.FieldType.Name, v = (f.GetValue(stylePropVal)?.ToString() ?? "null").Substring(0, Math.Min(60, (f.GetValue(stylePropVal)?.ToString() ?? "null").Length)) }); } catch { }
        }
        var styleCpDump = new List<object>();
        if (cpsColl is IEnumerable ce3)
            foreach (var cp in ce3)
            {
                var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                if (pn != "Style") continue;
                foreach (var f in cp.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    string v = null;
                    try { var fv = f.GetValue(cp); v = fv == null ? "null" : fv.ToString(); if (v != null && v.Length > 60) v = v.Substring(0, 60) + "..."; } catch { v = "(err)"; }
                    styleCpDump.Add(new { f = f.Name + ":" + f.FieldType.Name, v = v });
                }
                break;
            }
        // Widget fields whose name mentions Style/Class/Custom
        var widgetStyleFields = new List<object>();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (f.Name.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string v = null;
                try { var fv = f.GetValue(w); v = fv == null ? "null" : fv.ToString(); if (v != null && v.Length > 60) v = v.Substring(0, 60) + "..."; } catch { v = "(err)"; }
                widgetStyleFields.Add(new { f = f.Name + ":" + f.FieldType.Name, v = v });
            }
        return new Dictionary<string, object>
        {
            ["ok"] = true, ["widget"] = widgetName, ["type"] = t.FullName,
            ["customProps"] = cps, ["styleMethods"] = styleMethods,
            ["stylePropObj"] = stylePropObj, ["styleCpDump"] = styleCpDump,
            ["widgetStyleFields"] = widgetStyleFields
        };
    }

    // Test setter for container style classes � tries several surfaces and reports which one
    // changed the in-memory state. Runs inside a command scope (SS requires it for mutations).
    static string SetStyleCp(string module, string screen, string widgetName, string styleClass)
    {
        return RunCmd(module, "set style cp", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widgetName);
            if (w == null) throw new Exception("widget not found: " + widgetName);
            var report = new List<string>();
            object styleCp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var cp in coll)
                {
                    var name = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                    if (name == "Style") { styleCp = cp; break; }
                }
            if (styleCp != null)
            {
                // Raw first - SetValueExpression stores the string VERBATIM (quotes would become
                // part of the class name). See SetBlockCpExpression for the full explanation.
                var quoted = "\"" + styleClass.Replace("\"", "\"\"") + "\"";
                foreach (var candidate in new[] { styleClass, quoted })
                {
                    try { CallMethodTyped(styleCp, "SetValueExpression", new object[] { candidate }); report.Add("ok:SetValueExpression(" + candidate + ")"); break; }
                    catch (Exception e) { report.Add("SetValueExpression(" + candidate + ") failed: " + e.Message); }
                }
                try { SetProp(styleCp, "Value", styleClass); report.Add("ok:CP Value"); }
                catch (Exception e) { report.Add("CP Value failed: " + e.Message); }
            }
            else report.Add("no Style CustomProperty found");
            try { SetProp(w, "CustomStyle", styleClass); report.Add("ok:CustomStyle prop"); } catch (Exception e) { report.Add("CustomStyle prop failed: " + e.Message); }
            var state = DumpWidgetStyleInfo(w, widgetName);
            return "set_style_cp report:\n" + string.Join("\n", report) + "\nstate: " + Json(state);
        });
    }

    // Try a cascade of genuine string setters on a stylesheet object. No force-set
    // (avoid writing a string into a non-string field). Returns true on first success.
    static bool TrySetCssOnSheet(object sheet, string css, List<string> diag)
    {
        foreach (var prop in new[] { "Value", "Source", "CssText", "UserCssSource", "Text", "DesignTimeValue" })
        {
            try { SetProp(sheet, prop, css); diag.Add("ok:SetProp " + prop); return true; } catch { }
        }
        foreach (var meth in new[] { "SetSource", "SetCss", "SetCssSource", "SetUserCssSource", "SetDesignTimeValue" })
        {
            try { CallMethod(sheet, meth, new object[] { css }, 1); diag.Add("ok:CallMethod " + meth); return true; } catch { }
        }
        return false;
    }

    // Diagnostic: dump a stylesheet object's string-typed settable props + Set*(string) methods.
    static string DumpCssSetters(object sheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- props (Css/Source/Text/Value) --");
        foreach (var t in AllTypes(sheet.GetType()))
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var n = p.Name;
                if (n.IndexOf("Css", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n == "Text" || n == "Value" || n.IndexOf("DesignTime", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("  [" + t.Name + "] " + p.PropertyType.Name + " " + n + " CanWrite=" + p.CanWrite);
            }
        sb.AppendLine("-- methods (Set*/Css/Source, 1 string param) --");
        foreach (var t in AllTypes(sheet.GetType()))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.IsSpecialName) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                if (m.Name.StartsWith("Set") || m.Name.IndexOf("Css", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("  [" + t.Name + "] " + m.ReturnType.Name + " " + m.Name + "(string)");
            }
        return sb.ToString();
    }

    static string SetScreenTitle(string module, string screen, string title)
    {
        return RunCmd(module, "set screen title", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            CallMethod(sc, "SetTitle", new object[] { title }, 1);
            return "set title='" + title + "' on screen '" + screen + "'";
        });
    }

    // ---- theme / layout tools (live) ----

    // Find the Duplicate(IObjectSignature, IObject) method on ModelServices where param[0]
    // is assignable from the source element type. Reused by CloneWebBlock.
    static MethodInfo FindDuplicateMethod(object ms, object source)
    {
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType())) return m;
        }
        return null;
    }

    // Create a theme by deep-cloning an existing one (e.g. FitnessManager -> ZombieTheme).
    // Duplicate(sourceTheme, es) inside a real SS command, then rename. Optionally writes
    // the given CSS to the new theme's StyleSheet via TrySetUserCssOnSheet (the user-CSS
    // source the theme CSS editor displays). Returns before/after theme counts.
    static string CreateTheme(string moduleName, string sourceName, string newName, string css)
    {
        if (string.IsNullOrWhiteSpace(newName)) return Json(new { ok = false, error = "newName required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (FindTheme(es, newName) != null) return Json(new { ok = false, error = "theme already exists: " + newName });
        var source = FindTheme(es, sourceName);
        if (source == null) return Json(new { ok = false, error = "source theme not found: " + sourceName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var dupMethod = FindDuplicateMethod(ms, source);
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) method not found" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountThemes(es);
        object created = null; string err = null; string via = null; string cssReport = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, es });
                try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + sourceName + ",es)->rename";
                if (!string.IsNullOrEmpty(css))
                {
                    var diag = new List<string>();
                    var sheet = GetProp(dup, "StyleSheet");
                    if (sheet != null && TrySetUserCssOnSheet(sheet, css, diag)) cssReport = "css via " + diag[diag.Count - 1];
                    else cssReport = "css failed: " + string.Join(" | ", diag);
                }
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create theme", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountThemes(es);
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), themesBefore = before, themesAfter = after, css = cssReport, error = err });
    }

    // CreateThemeScratch: create a theme FROM SCRATCH (no Duplicate) via the eSpace factory
    // es.CreateWebTheme(name, IKey). Optionally sets the CSS on its StyleSheet (user CSS source)
    // and BaseTheme. Returns the created type + which collection it landed in (NRThemes vs
    // WebThemes), so we can confirm the module kind accepted it.
    static string CreateThemeScratch(string moduleName, string name, string css, string baseTheme)
    {
        if (string.IsNullOrWhiteSpace(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (FindTheme(es, name) != null) return Json(new { ok = false, error = "theme already exists: " + name });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountThemes(es);
        object created = null; string err = null; string cssReport = null; string baseReport = null; string collection = null;
        Action mutate = () =>
        {
            try
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                created = CallMethod(es, "CreateWebTheme", new object[] { name, key }, 2);
                var inNRColl = false;
                var nrThemes = GetProp(es, "NRThemes") as IEnumerable;
                if (nrThemes != null) foreach (var t in nrThemes) if (ReferenceEquals(t, created)) inNRColl = true;
                collection = inNRColl ? "NRThemes" : "WebThemes";
                if (!string.IsNullOrEmpty(baseTheme))
                {
                    var bt = FindTheme(es, baseTheme);
                    if (bt == null) { baseReport = "base theme not found: " + baseTheme; }
                    else
                    {
                        try { SetProp(created, "BaseTheme", GetProp(bt, "BaseTheme")); baseReport = "BaseTheme copied from '" + baseTheme + "' (reference, not clone)"; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; baseReport = "BaseTheme set failed: " + r.GetType().Name + ": " + r.Message; }
                    }
                }
                if (!string.IsNullOrEmpty(css))
                {
                    var diag = new List<string>();
                    var sheet = GetProp(created, "StyleSheet");
                    if (sheet != null && TrySetUserCssOnSheet(sheet, css, diag)) cssReport = "css via " + diag[diag.Count - 1];
                    else cssReport = "css failed: " + string.Join(" | ", diag);
                }
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create theme from scratch", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountThemes(es);
        return Json(new { ok = created != null, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), collection = collection, themesBefore = before, themesAfter = after, css = cssReport, baseTheme = baseReport, error = err });
    }

    // CreateThemeV2: like CreateThemeScratch but the creation uses the NewRuntime.Theme
    // ctor path instead of IESpace.CreateWebTheme. On CrossDevice modules CreateWebTheme
    // throws "Theme objects can't be children of Module objects" (the same failure
    // CreateWebFlow had � where CreateUIFlow was the fix). The NewRuntime.Theme ctor
    // (ESpace, String) is the UI-flow-style factory; we fall back from CreateWebTheme to
    // it so from-scratch theme creation works on every module kind.
    static string CreateThemeV2(string moduleName, string name, string css, string baseTheme)
    {
        if (string.IsNullOrWhiteSpace(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (FindTheme(es, name) != null) return Json(new { ok = false, error = "theme already exists: " + name });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountThemes(es);
        object created = null; string err = null; string cssReport = null; string baseReport = null; string collection = null; string via = null;
        Action mutate = () =>
        {
            try
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                via = "es.CreateWebTheme(name,key)";
                try
                {
                    created = CallMethod(es, "CreateWebTheme", new object[] { name, key }, 2);
                }
                catch (Exception e1)
                {
                    var r = e1; while (r.InnerException != null) r = r.InnerException;
                    var firstErr = "CreateWebTheme: " + r.GetType().Name + ": " + r.Message;
                    try
                    {
                        var themeType = FindType("ServiceStudio.Model.NewRuntime.Theme");
                        if (themeType == null) throw new Exception("NewRuntime.Theme type not found");
                        var ctor = themeType.GetConstructors().FirstOrDefault(c =>
                        {
                            var ps = c.GetParameters();
                            return ps.Length == 2 && ps[1].ParameterType == typeof(string);
                        });
                        if (ctor == null) throw new Exception("NewRuntime.Theme(ESpace, String) ctor not found");
                        created = ctor.Invoke(new object[] { es, name });
                        via = "new NewRuntime.Theme(es, name)";
                    }
                    catch (Exception e2)
                    {
                        r = e2; while (r.InnerException != null) r = r.InnerException;
                        err = firstErr + " | NewRuntime.Theme ctor: " + r.GetType().Name + ": " + r.Message;
                        return;
                    }
                }
                var inNRColl = false;
                var nrThemes = GetProp(es, "NRThemes") as IEnumerable;
                if (nrThemes != null) foreach (var t in nrThemes) if (ReferenceEquals(t, created)) inNRColl = true;
                collection = inNRColl ? "NRThemes" : "WebThemes";
                if (!string.IsNullOrEmpty(baseTheme))
                {
                    var bt = FindTheme(es, baseTheme);
                    if (bt == null) { baseReport = "base theme not found: " + baseTheme; }
                    else
                    {
                        try { SetProp(created, "BaseTheme", GetProp(bt, "BaseTheme")); baseReport = "BaseTheme copied from '" + baseTheme + "' (reference, not clone)"; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; baseReport = "BaseTheme set failed: " + r.GetType().Name + ": " + r.Message; }
                    }
                }
                if (!string.IsNullOrEmpty(css))
                {
                    var diag = new List<string>();
                    var sheet = GetProp(created, "StyleSheet");
                    if (sheet != null && TrySetUserCssOnSheet(sheet, css, diag)) cssReport = "css via " + diag[diag.Count - 1];
                    else cssReport = "css failed: " + string.Join(" | ", diag);
                }
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create theme v2 (from scratch)", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountThemes(es);
        return Json(new { ok = created != null && err == null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), collection = collection, themesBefore = before, themesAfter = after, css = cssReport, baseTheme = baseReport, error = err });
    }
    // first theme). Writes BOTH _userCssSource (via SetUserCssSource) AND _cssSource (via
    // SetCssSource) so SS's theme CSS editor displays the stylesheet. The editor binds to
    // the _cssSource surface (and DesignTimeValue), not _userCssSource alone � writing both
    // matches the FitnessManager theme which displays correctly.
    static string SetThemeCss(string moduleName, string themeName, string css)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return Json(new { ok = false, error = "themeName required" });
        return RunCmd(moduleName, "set theme css", es =>
        {
            var theme = FindTheme(es, themeName);
            if (theme == null) throw new Exception("theme not found: " + themeName);
            var diag = new List<string>();
            var sheet = GetProp(theme, "StyleSheet");
            if (sheet == null) throw new Exception("theme '" + themeName + "' has no StyleSheet");
            var cssStr = css ?? "";
            if (!TrySetUserCssOnSheet(sheet, cssStr, diag))
                throw new Exception("SetUserCssSource failed on theme '" + themeName + "': " + string.Join(" | ", diag));
            // Also write _cssSource (the editor-visible surface) and DesignTimeValue.
            CallMethod(sheet, "SetCssSource", new object[] { cssStr }, 1);
            diag.Add("ok:CallMethod SetCssSource (" + cssStr.Length + " chars)");
            return "set UserCssSource+CSSource on theme '" + themeName + "' (" + cssStr.Length + " chars) via " + diag[diag.Count - 1];
        });
    }

    // Set the Theme on a screen to a named theme (e.g. ZombieGame -> ZombieTheme).
    // Reactive screens expose the theme through 'ThemeNewRuntime' (the Theme property SS
    // serializes as the screen's theme); try the common property names, fall back to
    // SetPropForce, and report which one landed.
    static string SetScreenTheme(string moduleName, string screen, string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return Json(new { ok = false, error = "themeName required" });
        return RunCmd(moduleName, "set screen theme", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var theme = FindTheme(es, themeName);
            if (theme == null) throw new Exception("theme not found: " + themeName);
            var diag = new List<string>();
            foreach (var propName in new[] { "ThemeNewRuntime", "Theme", "WebTheme" })
            {
                try { SetProp(sc, propName, theme); return "set '" + propName + "' on screen '" + screen + "' -> theme '" + themeName + "'"; }
                catch (Exception e) { diag.Add(propName + ": " + e.Message); }
            }
            try { SetPropForce(sc, "Theme", theme); return "set Theme (force) on screen '" + screen + "' -> theme '" + themeName + "'"; }
            catch (Exception e) { diag.Add("force Theme: " + e.Message); }
            throw new Exception("could not set theme on screen. Diagnostic:\n" + string.Join("\n", diag));
        });
    }

    // Set the Theme on a web flow (screens inherit the theme from their flow; the screen's
    // own theme property is read-only). Try common property names, fall back to SetPropForce.
    static string SetFlowTheme(string moduleName, string flow, string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return Json(new { ok = false, error = "themeName required" });
        if (string.IsNullOrWhiteSpace(flow)) return Json(new { ok = false, error = "flow required" });
        return RunCmd(moduleName, "set flow theme", es =>
        {
            var wf = FindWebFlow(es, flow);
            if (wf == null) throw new Exception("web flow not found: " + flow);
            var theme = FindTheme(es, themeName);
            if (theme == null) throw new Exception("theme not found: " + themeName);
            var diag = new List<string>();
            foreach (var propName in new[] { "Theme", "WebTheme", "ThemeNewRuntime" })
            {
                try { SetProp(wf, propName, theme); return "set '" + propName + "' on flow '" + flow + "' -> theme '" + themeName + "'"; }
                catch (Exception e) { diag.Add(propName + ": " + e.Message); }
            }
            try { SetPropForce(wf, "Theme", theme); return "set Theme (force) on flow '" + flow + "' -> theme '" + themeName + "'"; }
            catch (Exception e) { diag.Add("force Theme: " + e.Message); }
            throw new Exception("could not set theme on flow. Diagnostic:\n" + string.Join("\n", diag));
        });
    }

    // Deep-clone a web block (e.g. the Layout or Menu block) into a new one via
    // IModelServices.Duplicate(sourceBlock, es) + rename. Same mechanism as the proven
    // service-action clone. Returns the created block type/name + flow it landed in.
    static string CloneWebBlock(string moduleName, string sourceName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return Json(new { ok = false, error = "newName required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (FindBlock(es, newName) != null) return Json(new { ok = false, error = "web block already exists: " + newName });
        object srcFlow;
        var source = FindBlockWithFlow(es, sourceName, out srcFlow);
        if (source == null) return Json(new { ok = false, error = "source web block not found: " + sourceName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        // The Duplicate parent must be the containing WebFlow (web blocks live in a flow's Nodes),
        // not the eSpace. Find a Duplicate overload whose param[1] accepts the flow type.
        MethodInfo dupMethod = null;
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType()) && srcFlow != null && ps[1].ParameterType.IsInstanceOfType(srcFlow)) { dupMethod = m; break; }
        }
        if (dupMethod == null) dupMethod = FindDuplicateMethod(ms, source);
        if (dupMethod == null) return Json(new { ok = false, error = "Duplicate(IObjectSignature,IObject) method not found" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        object created = null; string err = null; string via = null; string flowName = null;
        Action mutate = () =>
        {
            try
            {
                var dup = dupMethod.Invoke(ms, new object[] { source, srcFlow ?? es });
                try { SetProp(dup, "Name", newName); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "rename: " + r.GetType().Name + ": " + r.Message; }
                created = dup;
                via = "Duplicate(" + sourceName + "," + (srcFlow != null ? "flow" : "es") + ")->rename";
                foreach (var f in WebFlowsOf(es))
                {
                    var nodes = GetProp(f, "Nodes") as IEnumerable;
                    if (nodes == null) continue;
                    foreach (var n in nodes)
                        if (ReferenceEquals(n, dup)) { flowName = GetProp(f, "Name") as string; break; }
                }
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: clone web block", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, createdName = GetProp(created, "Name"), flow = flowName, error = err });
    }

    // Move an existing web block from its current WebFlow into a different WebFlow
    // (both flows live in the same eSpace). Required because CloneWebBlock places
    // the clone in the SOURCE flow's Nodes, but a block may need to land in a NEW
    // flow (e.g. Header cloned from Menu in Common, but consumed in zombieGame).
    static string MoveWebBlockToFlow(string moduleName, string blockName, string targetFlowName)
    {
        if (string.IsNullOrWhiteSpace(blockName)) return Json(new { ok = false, error = "blockName required" });
        if (string.IsNullOrWhiteSpace(targetFlowName)) return Json(new { ok = false, error = "targetFlow required" });
        return RunCmd(moduleName, "move web block to flow", es =>
        {
            object srcFlow;
            var blk = FindBlockWithFlow(es, blockName, out srcFlow);
            if (blk == null) throw new Exception("web block not found: " + blockName);
            var targetFlow = FindWebFlow(es, targetFlowName);
            if (targetFlow == null) throw new Exception("target flow not found: " + targetFlowName);
            if (ReferenceEquals(srcFlow, targetFlow)) return "block '" + blockName + "' already in flow '" + targetFlowName + "' (no move needed)";
            var srcNodes = GetProp(srcFlow, "Nodes") as IEnumerable;
            if (srcNodes == null) throw new Exception("source flow '" + GetProp(srcFlow, "Name") + "' has no Nodes");
            var tgtNodes = GetProp(targetFlow, "Nodes");
            if (tgtNodes == null) throw new Exception("target flow '" + GetProp(targetFlow, "Name") + "' has no Nodes");
            // OutSystems model collections (Nodes) aren't plain IList; they expose typed
            // Add/Remove methods (e.g. Add(IWebBlock)). Use CallMethod to find any
            // Add/Remove with exactly 1 parameter, regardless of the param type, and invoke
            // with the block instance.
            var removed = false;
            foreach (var n in srcNodes) { if (ReferenceEquals(n, blk)) { removed = true; break; } }
            if (!removed) throw new Exception("block '" + blockName + "' not found in source flow '" + GetProp(srcFlow, "Name") + "' Nodes");
            CallMethod(srcNodes, "Remove", new object[] { blk }, 1);
            CallMethod(tgtNodes, "Add", new object[] { blk }, 1);
            return "moved web block '" + blockName + "' from flow '" + GetProp(srcFlow, "Name") + "' to '" + GetProp(targetFlow, "Name") + "'";
        });
    }

    // Wire a theme's layout web block (NormalPageLayout) and/or menu web block (MenuWebBlock)
    // to specific cloned web blocks. The theme props hold web-block references; we try setting
    // the reference by block object. Fall back to SetPropForce with diagnostics.
    static string SetThemeLayout(string moduleName, string themeName, string layoutBlock, string menuBlock)
    {
        if (string.IsNullOrWhiteSpace(themeName)) return Json(new { ok = false, error = "themeName required" });
        return RunCmd(moduleName, "set theme layout", es =>
        {
            var theme = FindTheme(es, themeName);
            if (theme == null) throw new Exception("theme not found: " + themeName);
            var report = new List<string>();
            if (!string.IsNullOrEmpty(layoutBlock))
            {
                var blk = FindBlock(es, layoutBlock);
                if (blk == null) throw new Exception("layout web block not found: " + layoutBlock);
                bool set = false;
                foreach (var propName in new[] { "NormalPageLayout", "DefaultLayout" })
                {
                    try { SetProp(theme, propName, blk); report.Add(propName + "=" + layoutBlock + " (SetProp)"); set = true; break; }
                    catch (Exception e) { report.Add(propName + " SetProp failed: " + e.Message); }
                }
                if (!set)
                {
                    try { SetPropForce(theme, "NormalPageLayout", blk); report.Add("NormalPageLayout=" + layoutBlock + " (force)"); set = true; }
                    catch (Exception e) { report.Add("NormalPageLayout force failed: " + e.Message); }
                }
            }
            if (!string.IsNullOrEmpty(menuBlock))
            {
                var blk = FindBlock(es, menuBlock);
                if (blk == null) throw new Exception("menu web block not found: " + menuBlock);
                bool set = false;
                foreach (var propName in new[] { "MenuWebBlock", "MenuBlock" })
                {
                    try { SetProp(theme, propName, blk); report.Add(propName + "=" + menuBlock + " (SetProp)"); set = true; break; }
                    catch (Exception e) { report.Add(propName + " SetProp failed: " + e.Message); }
                }
                if (!set)
                {
                    try { SetPropForce(theme, "MenuWebBlock", blk); report.Add("MenuWebBlock=" + menuBlock + " (force)"); set = true; }
                    catch (Exception e) { report.Add("MenuWebBlock force failed: " + e.Message); }
                }
            }
            return "theme '" + themeName + "' layout wiring: " + string.Join(" | ", report);
        });
    }

    // Read-only diagnostic: dump a theme's settable properties + StyleSheet source lengths.
    // Use to discover the correct property names for NormalPageLayout/MenuWebBlock wiring.
    static string ProbeTheme(string moduleName, string themeName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var themes = string.IsNullOrEmpty(themeName) ? ThemesOf(es) : new List<object> { FindTheme(es, themeName) };
        if (themes.Count == 0) return Json(new { ok = false, error = "theme not found: " + themeName });
        var outList = new List<object>();
        foreach (var t in themes)
        {
            if (t == null) continue;
            var props = new List<object>();
            foreach (var p in t.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object val = null; string valDesc = null;
                try { val = p.GetValue(t, null); if (val != null) { var nm = GetProp(val, "Name"); if (nm != null) valDesc = nm.ToString(); else valDesc = val.GetType().Name; } else valDesc = "(null)"; } catch { valDesc = "(err)"; }
                props.Add(new { name = p.Name, canWrite = p.CanWrite, type = p.PropertyType.Name, value = valDesc });
            }
            object sheet = null;
            try { sheet = GetProp(t, "StyleSheet"); } catch { }
            var fields = new Dictionary<string, object>();
            if (sheet != null)
                foreach (var fn in new[] { "_cssSource", "_userCssSource", "_generatedCssSource", "_finalCssSource" })
                {
                    var expr = GetField(sheet, fn);
                    var txt = expr == null ? null : ReadLightweightExpressionText(expr);
                    fields[fn] = txt == null ? null : txt.Length;
                }
            outList.Add(new { name = GetProp(t, "Name") as string, type = t.GetType().FullName, props = props, sheetFields = fields });
        }
        return Json(new { ok = true, themes = outList });
    }

    static string ListWebFlows(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var flowsInfo = new List<object>();
        foreach (var f in WebFlowsOf(es))
        {
            var fName = GetProp(f, "Name") as string ?? "?";
            var screens = new List<string>();
            var nodes = GetProp(f, "Nodes") as IEnumerable;
            if (nodes != null) foreach (var s in nodes) { if (!IsScreen(s)) continue; try { screens.Add(GetProp(s, "Name") as string ?? "?"); } catch { } }
            flowsInfo.Add(new { name = fName, type = f.GetType().Name, screens = screens });
        }
        var themesInfo = new List<object>();
        foreach (var propName in new[] { "NRThemes", "WebThemes" })
        {
            var themes = GetProp(es, propName) as IEnumerable;
            if (themes == null) continue;
            foreach (var t in themes)
                try
                {
                    // StyleSheet is a WebStyleSheet OBJECT; read its CssSource text (or fall back
                    // to any string-typed property). StyleSheet as string would always be empty.
                    int cssLen = 0;
                    var sheet = GetProp(t, "StyleSheet");
                    if (sheet != null) cssLen = (ReadCssText(sheet) ?? "").Length;
                    themesInfo.Add(new { name = GetProp(t, "Name") as string ?? "?", type = t.GetType().Name, styleSheetLen = cssLen });
                }
                catch { }
        }
        var kindVal = GetProp(es, "Kind");
        return Json(new { ok = true, module = module, kind = kindVal, kindName = kindVal?.ToString(), flows = flowsInfo, themes = themesInfo });
    }

    static string ListWidgets(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var tree = new List<object>();
        var widgets = GetProp(sc, "Widgets") as IEnumerable;
        if (widgets != null) BuildWidgetTree(widgets, tree);
        return Json(new { ok = true, module = module, screen = screen, screenType = sc.GetType().Name, widgets = tree });
    }

    static void BuildWidgetTree(IEnumerable widgets, List<object> outList, int depth = 0)
    {
        if (depth > 12) return;
        foreach (var w in widgets)
        {
            string name = null, styleClasses = null;
            try { name = GetProp(w, "Name") as string; } catch { }
            // Style classes are stored in the _customStyle field (ClrMD reads it as such).
            // The StyleClasses property may be null/unreadable, so prefer the field, falling
            // back to the property.
            try { styleClasses = GetFieldStr(w, "_customStyle"); } catch { }
            if (string.IsNullOrEmpty(styleClasses)) { try { styleClasses = GetProp(w, "StyleClasses") as string; } catch { } }
            string concreteType = w.GetType().Name;
            var ifaces = new List<string>();
            try
            {
                foreach (var i in w.GetType().GetInterfaces())
                {
                    var n = i.Name;
                    if (n.Contains("Widget") || n.Contains("Container") || n.Contains("Expression") || n.Contains("Link") || n.Contains("Placeholder") || n.Contains("Content") || n == "IText")
                        ifaces.Add(i.FullName);
                }
            }
            catch { }
            var children = new List<object>();
            var childWidgets = GetProp(w, "Widgets") as IEnumerable;
            if (childWidgets != null) BuildWidgetTree(childWidgets, children, depth + 1);
            // Placeholder content (layout WebBlockInstance): walk the instance's Placeholders
            // collection and recurse into each placeholder's child widgets so placeholder fills
            // (Header/MainContent/Footer/...) are visible in the tree.
            var placeholders = new List<object>();
            IEnumerable phColl = null;
            try { phColl = GetProp(w, "Placeholders") as IEnumerable; } catch { }
            if (phColl == null)
            {
                object inst = null;
                try { inst = GetProp(w, "Instance"); } catch { }
                if (inst != null) { try { phColl = GetProp(inst, "Placeholders") as IEnumerable; } catch { } }
            }
            if (phColl != null)
            {
                foreach (var ph in phColl)
                {
                    var phName = "";
                    try { phName = GetProp(ph, "Name") as string ?? ""; } catch { }
                    var phChildren = new List<object>();
                    IEnumerable phWidgets = null;
                    try { phWidgets = GetProp(ph, "Widgets") as IEnumerable; } catch { }
                    if (phWidgets != null) BuildWidgetTree(phWidgets, phChildren, depth + 1);
                    placeholders.Add(new { name = phName, type = ph.GetType().Name, children = phChildren });
                }
            }
            outList.Add(new { name = name, concreteType = concreteType, interfaces = ifaces, styleClasses = styleClasses, children = children, placeholders = placeholders });
        }
    }

    // ---- placeholder tools (live layout placeholders: Header/Menu/Content/Footer) ----

    // HasInterface: check if an object implements an interface by short name (e.g. "IWebBlockInstanceWidget").
    static bool HasInterface(object obj, string shortName)
    {
        if (obj == null) return false;
        try { foreach (var i in obj.GetType().GetInterfaces()) if (i.Name == shortName) return true; } catch { }
        return false;
    }

    // FindLayoutInstance: first child widget of a screen implementing IWebBlockInstanceWidget
    // (the layout web block instance injected by the screen's layout). This is how we reach
    // the layout's named placeholders (Header/Menu/Content/Footer).
    static object FindLayoutInstance(object screen)
    {
        if (screen == null) return null;
        var widgets = GetProp(screen, "Widgets") as IEnumerable;
        if (widgets == null) return null;
        foreach (var w in widgets)
            if (HasInterface(w, "IWebBlockInstanceWidget")) return w;
        // Broader fallback: any interface whose name contains "WebBlockInstance"
        foreach (var w in widgets)
            try { foreach (var i in w.GetType().GetInterfaces()) if (i.Name.IndexOf("WebBlockInstance", StringComparison.OrdinalIgnoreCase) >= 0) return w; } catch { }
        return null;
    }

    // ListPlaceholders (READ-ONLY, no RunCmd): for targets [layoutInstance, layoutInstance.Instance],
    // read Placeholders (trying Placeholders/PlaceholderInstances/PlaceholderWidgets), report each
    // placeholder's Name + type + interfaces + CreateWidget method signatures (+ whether it has a
    // 2-param generic CreateWidget<T>(string,IKey)). Used to discover placeholder names and the
    // correct widget-creation accessor before calling AddToPlaceholder.
    static string ListPlaceholders(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var layoutInstance = FindLayoutInstance(sc);
        if (layoutInstance == null)
            return Json(new { ok = false, error = "layout WebBlockInstanceWidget not found on screen '" + screen + "'. Apply a layout (e.g. LayoutTopMenu) to the screen first." });

        var placeholders = new List<object>();
        var targetInfos = new List<object>();
        var targets = new List<object> { layoutInstance };
        object instance = null;
        try { instance = GetProp(layoutInstance, "Instance"); } catch { }
        if (instance != null) targets.Add(instance);

        var diag = new StringBuilder();
        foreach (var tgt in targets)
        {
            diag.AppendLine("probing " + tgt.GetType().FullName);
            string phPropUsed = null;
            IEnumerable phColl = GetProp(tgt, "Placeholders") as IEnumerable;
            if (phColl != null) phPropUsed = "Placeholders";
            else
            {
                phColl = GetProp(tgt, "PlaceholderInstances") as IEnumerable;
                if (phColl != null) phPropUsed = "PlaceholderInstances";
                else
                {
                    phColl = GetProp(tgt, "PlaceholderWidgets") as IEnumerable;
                    if (phColl != null) phPropUsed = "PlaceholderWidgets";
                }
            }
            if (phColl == null) { diag.AppendLine("  no Placeholders* prop on " + tgt.GetType().Name); continue; }
            int cnt = 0;
            foreach (var ph in phColl)
            {
                cnt++;
                var phName = GetProp(ph, "Name") as string ?? "";
                var ifaces = new List<string>();
                foreach (var i in ph.GetType().GetInterfaces()) ifaces.Add(i.FullName);
                bool hasCreateWidget2Param = false;
                var cwMethods = new List<object>();
                foreach (var iface in ph.GetType().GetInterfaces())
                    foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2)
                        { hasCreateWidget2Param = true; cwMethods.Add(new { iface = iface.FullName, declaringType = m.DeclaringType?.FullName }); }
                for (var t = ph.GetType(); t != null; t = t.BaseType)
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2 && !cwMethods.Any(x => true))
                            cwMethods.Add(new { iface = "(concrete:" + t.Name + ")", declaringType = m.DeclaringType?.FullName });
                placeholders.Add(new { name = phName, sourceType = tgt.GetType().FullName, type = ph.GetType().Name, fullType = ph.GetType().FullName, interfaces = ifaces, hasCreateWidget2Param = hasCreateWidget2Param, createWidgetMethods = cwMethods });
                diag.AppendLine("  placeholder: '" + phName + "' type=" + ph.GetType().Name + " hasCreateWidget2Param=" + hasCreateWidget2Param);
            }
            diag.AppendLine("  found via " + phPropUsed + " (" + cnt + " items)");
            targetInfos.Add(new { type = tgt.GetType().FullName, phProp = phPropUsed, count = cnt });
        }

        return Json(new { ok = true, module = module, screen = screen, layoutInstanceType = layoutInstance.GetType().FullName, instanceType = instance?.GetType()?.FullName ?? "(null)", hasInstance = instance != null, targets = targetInfos, placeholders = placeholders, diagnostic = diag.ToString() });
    }

    // ProbeLayoutRef (READ-ONLY): enumerate the screen's layout WebBlockInstance properties to
    // find which web block the layout references. The block reference is usually an object with
    // a Name / GetName that equals the layout block name (ZombieLayout vs LayoutTopMenu). Dump
    // every property whose value is a non-null object, reporting type + Name when present.
    static string ProbeLayoutRef(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var layoutInstance = FindLayoutInstance(sc);
        if (layoutInstance == null) return Json(new { ok = false, error = "layout WebBlockInstanceWidget not found on screen '" + screen + "'" });

        var props = new List<object>();
        var blockCandidates = new List<string>();
        foreach (var p in layoutInstance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object val = null;
            try { val = p.GetValue(layoutInstance, null); } catch { continue; }
            if (val == null) { props.Add(new { prop = p.Name, type = p.PropertyType.Name, value = "(null)" }); continue; }
            string name = null;
            try { name = GetProp(val, "Name") as string; } catch { }
            string fullName = null;
            try { fullName = GetProp(val, "FullName") as string; } catch { }
            string kind = val.GetType().Name;
            if (p.PropertyType.Name.Contains("Block")) blockCandidates.Add(p.Name);
            if (val is string || val is int || val is bool || val is long || val is double)
                props.Add(new { prop = p.Name, type = p.PropertyType.Name, value = val.ToString() });
            else if (val is IEnumerable && !(val is string))
                props.Add(new { prop = p.Name, type = p.PropertyType.Name, value = "(collection)" });
            else
                props.Add(new { prop = p.Name, type = p.PropertyType.Name, value = kind, name = name, fullName = fullName });
        }

        // Also probe the block reference candidates: properties whose value type name contains
        // "WebBlock" or "Block", plus try Instance's props.
        var targetInfos = new List<object>();
        foreach (var tgt in new[] { layoutInstance })
        {
            foreach (var cand in blockCandidates)
            {
                object v = null;
                try { v = GetProp(tgt, cand); } catch { }
                if (v == null) continue;
                object blk = null;
                try { blk = v; } catch { }
                string blkName = null;
                try { blkName = GetProp(blk, "Name") as string ?? (GetProp(blk, "FullName") as string); } catch { }
                string blkType = blk != null ? blk.GetType().FullName : "(null)";
                bool isScreen = blk != null && HasInterface(blk, "IScreen");
                targetInfos.Add(new { prop = cand, propType = v.GetType().FullName, blockName = blkName, blockType = blkType, isScreen = isScreen });
            }
        }
        return Json(new { ok = true, module = module, screen = screen, layoutInstanceType = layoutInstance.GetType().FullName, blockProps = blockCandidates, props = props, resolved = targetInfos });
    }

    // SetScreenLayout: repoint the screen's layout WebBlockInstance SourceWebBlock to a
    // different layout block (e.g. LayoutTopMenu -> ZombieLayout). The layout block must exist
    // in the module (FindBlock). Wrapped in RunCmd (undo unit). Placeholder fills are keyed by
    // placeholder NAME, so content placed in MainContent/Header/... survives the switch as long
    // as the target layout exposes the same placeholder names.
    static string SetScreenLayout(string module, string screen, string layoutBlock)
    {
        if (string.IsNullOrWhiteSpace(layoutBlock)) return Json(new { ok = false, error = "layoutBlock required" });
        return RunCmd(module, "set screen layout", es =>
        {
            var blk = FindBlock(es, layoutBlock);
            if (blk == null) throw new Exception("layout web block not found: " + layoutBlock);
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var layoutInstance = FindLayoutInstance(sc);
            if (layoutInstance == null) throw new Exception("layout WebBlockInstanceWidget not found on screen '" + screen + "'");
            var old = GetProp(layoutInstance, "SourceWebBlock");
            var oldName = old != null ? (GetProp(old, "Name") as string ?? "?") : "(none)";
            SetProp(layoutInstance, "SourceWebBlock", blk);
            var now = GetProp(layoutInstance, "SourceWebBlock");
            var nowName = now != null ? (GetProp(now, "Name") as string ?? "?") : "(none)";
            return "screen '" + screen + "' layout: " + oldName + " -> " + nowName + " (via SourceWebBlock SetProp)";
        });
    }

    // AddToPlaceholder: locate a placeholder by Name (case-insensitive) on the screen's layout
    // instance (or its Instance), then CreateWidgetByKind(placeholder, kind, name) to create a
    // widget inside that placeholder, then set label/destination/styleClass. Wrapped in RunCmd
    // (Command.ExecuteFromAsyncCode) so it is a single undo unit.
    static string AddToPlaceholder(string module, string screen, string placeholder, string kind, string name, string text, string targetScreen, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(kind)) return Json(new { ok = false, error = "kind required" });
        if (string.IsNullOrEmpty(placeholder)) return Json(new { ok = false, error = "placeholder required" });
        return RunCmd(module, "add to placeholder", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var layoutInstance = FindLayoutInstance(sc);
            if (layoutInstance == null) throw new Exception("layout WebBlockInstanceWidget not found on screen '" + screen + "'");
            object instance = null;
            try { instance = GetProp(layoutInstance, "Instance"); } catch { }
            object foundPlaceholder = null;
            foreach (var tgt in new object[] { layoutInstance, instance }.Where(t => t != null))
            {
                IEnumerable phColl = GetProp(tgt, "Placeholders") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderInstances") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderWidgets") as IEnumerable;
                if (phColl == null) continue;
                foreach (var ph in phColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, placeholder, StringComparison.OrdinalIgnoreCase)) { foundPlaceholder = ph; break; }
                }
                if (foundPlaceholder != null) break;
            }
            if (foundPlaceholder == null) throw new Exception("placeholder not found: " + placeholder + " (on layout or its Instance)");

            // Create the widget via the placeholder's CreateWidget<T>(name, IKey)
            var w = CreateWidgetByKind(foundPlaceholder, kind, name);
            if (w == null) throw new Exception("CreateWidgetByKind returned null for kind=" + kind);

            // Set widget properties (mirrors AddTextWidget / AddLinkWidget / AddContainer)
            if (kind == "text" && !string.IsNullOrEmpty(text)) { try { SetProp(w, "Text", text); } catch { } }
            if (kind == "expression" && !string.IsNullOrEmpty(text)) { try { CallMethod(w, "SetValue", new object[] { text }, 1); } catch { try { SetProp(w, "Text", text); } catch { } } }
            if (!string.IsNullOrEmpty(styleClass)) SetStyleClassesOn(w, styleClass);
            if (kind == "link")
            {
                if (!string.IsNullOrEmpty(text)) SetWidgetLinkLabel(w, text);
                if (!string.IsNullOrEmpty(targetScreen))
                {
                    var tgt = FindScreen(es, targetScreen);
                    if (tgt != null)
                    {
                        try
                        {
                            var onClick = GetProp(w, "OnClick");
                            if (onClick == null) onClick = CallMethod(w, "CreateOnClick", null, 0);
                            SetProp(onClick, "Destination", tgt);
                        }
                        catch (Exception e) { Log("link destination set failed: " + e.Message); }
                    }
                }
            }
            return "created " + kind + " '" + name + "' in placeholder '" + placeholder + "' (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // AddButton: create a REAL Button widget (the NRWidgets plugin's custom widget,
    // serialized as <NRWebWidgets.CustomWidget-OutSystems.Plugin.NRWidgets.Button>).
    // The Button is a CustomWidget made via CreateWidget(CustomObjectDescriptor) � NOT
    // CreateWidget<T> (no creatable widget interface). The descriptor is the plugin's
    // ButtonDescriptor singleton; instantiate it inside SS (its ctor needs the WidgetFactory
    // service that only SS's ServicesRegistry provides). Then set Name + Style class, and add
    // a Text label child into the button's 'content' CustomPlaceholderWidget (the label that
    // renders on the button). Target: placeholder (layout Action/other) or parent container
    // or the screen itself.
    static string AddButton(string module, string screen, string placeholder, string parent, string name, string text, string styleClass)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add button", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);

            // Resolve target: placeholder on the layout instance first, else parent/screen.
            object target = null; string where = null;
            if (!string.IsNullOrEmpty(placeholder))
            {
                var layoutInstance = FindLayoutInstance(sc);
                if (layoutInstance == null) throw new Exception("layout WebBlockInstanceWidget not found on screen '" + screen + "'");
                object instance = null;
                try { instance = GetProp(layoutInstance, "Instance"); } catch { }
                object foundPlaceholder = null;
                foreach (var tgt in new object[] { layoutInstance, instance }.Where(t => t != null))
                {
                    IEnumerable phColl = GetProp(tgt, "Placeholders") as IEnumerable;
                    if (phColl == null) phColl = GetProp(tgt, "PlaceholderInstances") as IEnumerable;
                    if (phColl == null) phColl = GetProp(tgt, "PlaceholderWidgets") as IEnumerable;
                    if (phColl == null) continue;
                    foreach (var ph in phColl)
                    {
                        var phName = GetProp(ph, "Name") as string ?? "";
                        if (string.Equals(phName, placeholder, StringComparison.OrdinalIgnoreCase)) { foundPlaceholder = ph; break; }
                    }
                    if (foundPlaceholder != null) break;
                }
                if (foundPlaceholder == null) throw new Exception("placeholder not found: " + placeholder);
                target = foundPlaceholder; where = "placeholder '" + placeholder + "'";
            }
            else
            {
                target = ResolveParent(sc, parent);
                if (target == null) throw new Exception("parent widget not found: " + parent);
                where = string.IsNullOrEmpty(parent) ? "screen" : parent;
            }

            return CreateButtonOn(target, where, name, text, styleClass);
        });
    }

    // Create a REAL Button (NRWidgets plugin CustomWidget) on an arbitrary widget host
    // (works for screens, containers, AND web blocks � any IObjectWithWidgets). Resolves the
    // Button's CustomObjectDescriptor, creates via CreateWidget(descriptor), names it, styles it,
    // and sets the label Text inside the 'content' placeholder. Returns a report. Called from
    // AddButton (screen) and AddButtonToBlock (web block).
    static string CreateButtonOn(object target, string where, string name, string text, string styleClass)
    {
        object descriptor = null; string descVia = null;
        Type kindType = FindType("ServiceStudio.Plugin.NRWidgets.Button+Kind")
                    ?? FindTypeByShortName("Kind");
        if (kindType != null)
        {
            try
            {
                var instField = kindType.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                var kindInst = instField?.GetValue(null);
                if (kindInst != null) { descriptor = GetProp(kindInst, "Descriptor"); descVia = "Button+Kind.Instance.Descriptor"; }
            }
            catch (Exception e) { Log("Button+Kind.Instance.Descriptor failed: " + e.Message); }
        }
        if (descriptor == null)
        {
            Type btnDescType = FindType("ServiceStudio.Plugin.NRWidgets.ButtonDescriptor");
            if (btnDescType != null)
            {
                try { descriptor = Activator.CreateInstance(btnDescType); descVia = "new ButtonDescriptor()"; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception("Button descriptor resolution failed: " + r.Message); }
            }
        }
        if (descriptor == null) throw new Exception("ButtonDescriptor not found in loaded assemblies");

        MethodInfo cwMethod = null;
        foreach (var iface in target.GetType().GetInterfaces())
            foreach (var m in iface.GetMethods())
                if (m.Name == "CreateWidget" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(descriptor.GetType()))
                    cwMethod = m;
        if (cwMethod == null)
            foreach (var iface in target.GetType().GetInterfaces())
                foreach (var m in iface.GetMethods())
                    if (m.Name == "CreateWidget" && !m.IsGenericMethod && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.IsAssignableFrom(descriptor.GetType()))
                        cwMethod = m;
        if (cwMethod == null) throw new Exception("CreateWidget(descriptor) not found on " + target.GetType().FullName);
        object w;
        try { w = cwMethod.GetParameters().Length == 1 ? cwMethod.Invoke(target, new object[] { descriptor }) : cwMethod.Invoke(target, new object[] { descriptor, null }); }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception("CreateWidget(descriptor) invoke failed: " + r.Message); }
        if (w == null) throw new Exception("CreateWidget(descriptor) returned null");

        try { SetProp(w, "Name", name); } catch { }
        SetStyleClassesOn(w, styleClass);

        string labelType = "unset";
        object contentHolder = w;
        IEnumerable phColl2 = null;
        try { phColl2 = GetProp(w, "Placeholders") as IEnumerable; } catch { }
        if (phColl2 != null)
        {
            foreach (var ph in phColl2)
            {
                var phName = GetProp(ph, "Name") as string ?? "";
                if (string.Equals(phName, "content", StringComparison.OrdinalIgnoreCase)) { contentHolder = ph; break; }
            }
        }
        object label = null;
        if (!string.IsNullOrEmpty(text))
        {
            IEnumerable contentChildren = null;
            try { contentChildren = GetProp(contentHolder, "Widgets") as IEnumerable; } catch { }
            object existingText = null;
            if (contentChildren != null)
                foreach (var c in contentChildren)
                    try { if (c.GetType().Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0) { existingText = c; break; } } catch { }
            if (existingText != null)
            {
                label = existingText; labelType = "existing " + label.GetType().Name;
                try { SetProp(label, "Text", text); } catch { try { CallMethod(label, "SetValue", new object[] { text }, 1); } catch { } }
            }
            else
            {
                try { contentChildren = GetProp(contentHolder, "Widgets") as IEnumerable; } catch { }
                label = CreateWidgetByKind(contentHolder, "text", name + "Label");
                if (label != null)
                {
                    labelType = label.GetType().Name;
                    try { SetProp(label, "Text", text); } catch { try { CallMethod(label, "SetValue", new object[] { text }, 1); } catch { } }
                }
            }
        }
        return "created button '" + name + "' (" + (w?.GetType().Name ?? "?") + ") in " + where + " via " + descVia + (label != null ? " + label '" + text + "' (" + labelType + ")" : "");
    }

    // SetButtonOnClick: wire a Button's OnClick event handler to an action in the module
    // (Client Action for Reactive) by setting the handler's Destination. The Button is a
    // plugin CustomWidget exposing an OnClick EventHandler (ServiceStudio.Model.NRWebWidgets+
    // EventHandler) whose Destination is the action to run when clicked. If the button has no
    // OnClick yet, create it via CreateOnClick (best-effort). The action is resolved across
    // ClientActions/UserActions (server) by name via FindAction.
    static string SetButtonOnClick(string module, string screen, string button, string actionName)
    {
        if (string.IsNullOrEmpty(button)) return Json(new { ok = false, error = "button (widget name) required" });
        if (string.IsNullOrEmpty(actionName)) return Json(new { ok = false, error = "actionName required" });
        return RunCmd(module, "set button onclick", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, button);
            if (w == null) throw new Exception("button widget not found: " + button);
            return WireButtonToAction(es, w, button, actionName, "screen");
        });
    }

    // Wire a Button's OnClick EventHandler Destination to an action, on ANY widget host
    // (screen, block � the OnClick surface is the same plugin CustomWidget). The action is
    // resolved on the host's ClientActions collection first (screen-level
    // ClientScreenActionFlow / block ClientActionFlow), then the module's actions. Shared by
    // SetButtonOnClick (screen) and SetBlockButtonOnClick (web block).
    static string WireButtonToAction(object es, object w, string button, string actionName, string where)
    {
        var action = FindActionInCollection(w, "ClientActions", actionName) ?? FindAction(es, actionName);
        if (action == null) throw new Exception("action not found: " + actionName);
        var actionName2 = "?"; try { actionName2 = GetProp(action, "Name") as string ?? "?"; } catch { }

        object handler = null; try { handler = GetProp(w, "OnClick"); } catch { }
        if (handler == null) { try { handler = CallMethod(w, "CreateOnClick", null, 0); } catch { } }
        if (handler == null)
        {
            var evHandlers = GetProp(w, "EventHandlers") as IEnumerable;
            if (evHandlers != null)
                foreach (var eh in evHandlers)
                    try { if ((GetProp(eh, "EventName") as string ?? "") == "OnClick") { handler = eh; break; } } catch { }
        }
        if (handler == null) throw new Exception("OnClick handler not found and could not be created on " + button);

        bool done = false; string via = "";
        var destProp = handler.GetType().GetProperty("Destination");
        if (destProp != null && destProp.PropertyType.IsAssignableFrom(action.GetType()))
        {
            SetProp(handler, "Destination", action);
            done = true; via = "handler.Destination";
        }
        if (!done)
        {
            object descriptor = null;
            try
            {
                Type kindType = FindType("ServiceStudio.Plugin.NRWidgets.Button+Kind");
                var instField = kindType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                var kindInst = instField?.GetValue(null);
                if (kindInst != null) descriptor = GetProp(kindInst, "Descriptor");
            }
            catch (Exception e) { Log("SetButtonOnClick descriptor resolve failed: " + e.Message); }
            if (descriptor != null)
            {
                var soh = descriptor.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "SetOnClickHandler" && m.GetParameters().Length == 2);
                if (soh != null)
                {
                    soh.Invoke(descriptor, new object[] { w, action });
                    done = true; via = "SetOnClickHandler";
                }
            }
        }
        if (!done) throw new Exception("could not wire OnClick: Destination not assignable and no SetOnClickHandler found");
        return "set OnClick of button '" + button + "' -> action '" + actionName2 + "' via " + via + " (" + where + ")";
    }

    // wire_button_to_screen: set a Button's OnClick Destination DIRECTLY to a screen (click =
    // navigate), with optional input-parameter mapping mirroring set_link_params. Reflection-
    // verified on SS 11.55.83: ServiceStudio.Model.NRWebWidgetEvents+EventHandler (the Button's
    // OnClick object) inherits ServiceStudio.Model.NREvents+AbstractClientSideEvent, whose PUBLIC
    // Destination property is typed ServiceStudio.Model.Interfaces.IClientSideDestination, and
    // ServiceStudio.Model.NRNodes/WebScreen (the Reactive screen) IMPLEMENTS
    // IClientSideDestination - so the screen object is assignable to handler.Destination exactly
    // like a screen-level ClientScreenActionFlow is. params = "Name=Value,..." for the target
    // screen's input parameters.
    static string WireButtonToScreen(string module, string screen, string button, string targetScreen, string paramsCsv)
    {
        if (string.IsNullOrEmpty(button)) return Json(new { ok = false, error = "button (widget name) required" });
        if (string.IsNullOrEmpty(targetScreen)) return Json(new { ok = false, error = "targetScreen required" });
        return RunCmd(module, "wire button to screen", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, button) ?? FindWidgetDeep(sc, button);
            if (w == null) throw new Exception("button widget not found: " + button);
            var tgt = FindScreen(es, targetScreen);
            if (tgt == null) throw new Exception("target screen not found: " + targetScreen);
            object handler = null; try { handler = GetProp(w, "OnClick"); } catch { }
            if (handler == null) { try { handler = CallMethod(w, "CreateOnClick", null, 0); } catch { } }
            if (handler == null)
            {
                var evHandlers = GetProp(w, "EventHandlers") as IEnumerable;
                if (evHandlers != null)
                    foreach (var eh in evHandlers)
                        try { if ((GetProp(eh, "EventName") as string ?? "") == "OnClick") { handler = eh; break; } } catch { }
            }
            if (handler == null) throw new Exception("OnClick handler not found and could not be created on " + button);
            var destProp = handler.GetType().GetProperty("Destination");
            if (destProp == null) throw new Exception("OnClick handler has no Destination property (" + handler.GetType().FullName + ")");
            if (!destProp.PropertyType.IsInstanceOfType(tgt) && !destProp.PropertyType.IsAssignableFrom(tgt.GetType()))
                throw new Exception("screen '" + targetScreen + "' (" + tgt.GetType().Name + ") is not assignable to OnClick Destination (" + destProp.PropertyType.FullName + ") - use a Link with targetScreen as fallback");
            SetProp(handler, "Destination", tgt);
            // Optional input-parameter mapping (mirrors set_link_params): once Destination is
            // set, the handler's Arguments carry one slot per destination-screen input param.
            var paramReport = "";
            if (!string.IsNullOrEmpty(paramsCsv))
            {
                var args = GetProp(handler, "Arguments") as IEnumerable;
                if (args == null) paramReport = " | params NOT set: OnClick has no Arguments collection (destination may have no input params)";
                else
                {
                    var argList = new List<object>();
                    foreach (var arg in args) argList.Add(arg);
                    var report = new List<string>();
                    foreach (var pair in paramsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var eq = pair.IndexOf('=');
                        if (eq < 0) { report.Add(pair.Trim() + ":SKIP(no =)"); continue; }
                        var an = pair.Substring(0, eq).Trim();
                        var av = pair.Substring(eq + 1).Trim();
                        object target = null;
                        foreach (var arg in argList)
                        {
                            var param = GetProp(arg, "Parameter");
                            var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                            if (string.Equals(paramName, an, StringComparison.OrdinalIgnoreCase)) { target = arg; break; }
                        }
                        if (target == null) { report.Add(an + ":NO-SLOT(" + argList.Count + " args present)"); continue; }
                        bool mapped = false; string how = null;
                        try { CallMethod(target, "SetValue", new object[] { av }, 1); mapped = true; how = "SetValue"; }
                        catch { }
                        if (!mapped)
                        {
                            try { var ve = GetProp(target, "Value"); if (ve != null) { SetProp(ve, "Text", av); mapped = true; how = "Value.Text"; } } catch { }
                        }
                        report.Add(an + "=" + av + (mapped ? ":OK(" + how + ")" : ":FAILED"));
                    }
                    paramReport = " | params: " + string.Join(";", report);
                }
            }
            return "set OnClick of button '" + button + "' -> screen '" + targetScreen + "' (" + tgt.GetType().Name + ") via handler.Destination" + paramReport;
        });
    }

    // add_button_to_block: create a REAL Button inside a WEB BLOCK's widget tree (or a named
    // container inside it). Same mechanism as add_button on screens (NRWidgets plugin CustomWidget
    // via ButtonDescriptor -> CreateWidget(descriptor)); the only difference is the target is found via
    // FindBlock instead of FindScreen (blocks are non-IScreen flow nodes).
    static string AddButtonToBlock(string module, string block, string parent, string name, string text, string styleClass)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add button to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var target = ResolveParent(blk, parent);
            if (target == null) throw new Exception("parent widget not found: " + parent + " in block '" + block + "'");
            return CreateButtonOn(target, "block '" + block + "'" + (string.IsNullOrEmpty(parent) ? "" : " container '" + parent + "'"), name, text, styleClass);
        });
    }

    // set_block_button_onclick: wire a Button's OnClick inside a web block to an action. The
    // action is looked up on the block's own ClientActions first (block-level ClientActionFlow),
    // then the module's actions. Same wiring surface as screens (shared WireButtonToAction).
    static string SetBlockButtonOnClick(string module, string block, string button, string actionName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(button)) return Json(new { ok = false, error = "button (widget name) required" });
        if (string.IsNullOrEmpty(actionName)) return Json(new { ok = false, error = "actionName required" });
        return RunCmd(module, "set block button onclick", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, button);
            if (w == null) throw new Exception("button widget not found in block: " + button);
            return WireButtonToAction(es, w, button, actionName, "block '" + block + "'");
        });
    }

    // set_widget_handler: wire ANY widget event handler (OnChange/OnFocus/OnClick/...) on a
    // screen or block widget to a client action. Mirrors WireButtonToAction but driven by the
    // event's EventName (OnClick = the button case; for Input/Dropdown/etc the interactive events
    // are OnChange/OnBlur/OnFocus...). The handler lives on the widget's EventHandlers collection.
    static string SetWidgetHandler(string module, string screen, string block, string widget, string eventName, string actionName)
    {
        if (string.IsNullOrEmpty(widget)) return Json(new { ok = false, error = "widget required" });
        if (string.IsNullOrEmpty(eventName)) return Json(new { ok = false, error = "event required" });
        if (string.IsNullOrEmpty(actionName)) return Json(new { ok = false, error = "actionName required" });
        return RunCmd(module, "set widget handler", es =>
        {
            object host = null; string where = null;
            if (!string.IsNullOrEmpty(screen)) { host = FindScreen(es, screen); where = "screen '" + screen + "'"; }
            else if (!string.IsNullOrEmpty(block)) { host = FindBlock(es, block); where = "block '" + block + "'"; }
            if (host == null) throw new Exception("screen or block required");
            object w = null;
            if (!string.IsNullOrEmpty(screen)) { w = FindWidget(host, widget) ?? FindWidgetDeep(host, widget); }
            else w = FindWidget(host, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in " + where);
            var action = FindActionInCollection(w, "ClientActions", actionName) ?? FindAction(es, actionName);
            if (action == null) throw new Exception("action not found: " + actionName);
            var actionName2 = "?"; try { actionName2 = GetProp(action, "Name") as string ?? "?"; } catch { }
            object handler = null; string handlerVia = null;
            IEnumerable ehs = null; try { ehs = GetProp(w, "EventHandlers") as IEnumerable; } catch { }
            // Builtin-event property (e.g. Input.OnChange returns an IBuiltinEvent with settable Destination)
            if (handler == null)
            {
                try
                {
                    var bp = w.GetType().GetProperty(eventName, BindingFlags.Public | BindingFlags.Instance);
                    if (bp != null)
                    {
                        var be = bp.GetValue(w, null);
                        if (be != null)
                        {
                            var bd = be.GetType().GetProperty("Destination");
                            if (bd != null && bd.CanWrite) { handler = be; handlerVia = "builtin " + eventName + " property"; }
                        }
                    }
                }
                catch (Exception e) { var rr = e; while (rr.InnerException != null) rr = rr.InnerException; Log("builtin " + eventName + " prop failed: " + rr.Message); }
            }
            if (ehs != null)
                foreach (var eh in ehs)
                    try { if ((GetProp(eh, "EventName") as string ?? "") == eventName) { handler = eh; handlerVia = "existing EventHandlers." + eventName; break; } } catch { }
            if (handler == null && string.Equals(eventName, "OnClick", StringComparison.OrdinalIgnoreCase))
                try { handler = CallMethod(w, "CreateOnClick", null, 0); handlerVia = "CreateOnClick"; } catch { }
            if (handler == null && ehs != null)
            {
                foreach (var mName in new[] { "CreateEventHandler", "Add", "Create" })
                {
                    var m = FindMethod(ehs, mName, 1);
                    if (m == null) continue;
                    try { var eh = m.Invoke(ehs, new object[] { eventName }); if (eh != null) { handler = eh; handlerVia = "EventHandlers." + mName + "('" + eventName + "')"; break; } }
                    catch (Exception e) { var rr = e; while (rr.InnerException != null) rr = rr.InnerException; Log("handler create " + mName + " failed: " + rr.Message); }
                }
            }
            if (handler == null) throw new Exception("no handler for event '" + eventName + "' on " + widget + " (looked in EventHandlers, CreateOnClick, EventHandlers.Create*)");
            bool done = false; string via = handlerVia ?? "";
            var destProp = handler.GetType().GetProperty("Destination");
            if (destProp != null && destProp.PropertyType.IsAssignableFrom(action.GetType()))
            {
                SetProp(handler, "Destination", action);
                done = true; via = (via.Length > 0 ? via + " + " : "") + "handler.Destination";
            }
            if (!done && string.Equals(eventName, "OnClick", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Type kindType = FindType("ServiceStudio.Plugin.NRWidgets.Button+Kind");
                    var instField = kindType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    var kindInst = instField?.GetValue(null);
                    var descriptor = kindInst != null ? GetProp(kindInst, "Descriptor") : null;
                    if (descriptor != null)
                    {
                        var soh = descriptor.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "SetOnClickHandler" && m.GetParameters().Length == 2);
                        if (soh != null) { soh.Invoke(descriptor, new object[] { w, action }); done = true; via += " + SetOnClickHandler"; }
                    }
                }
                catch (Exception e) { Log("SetOnClickHandler fallback failed: " + e.Message); }
            }
            if (!done) throw new Exception("could not wire '" + eventName + "' on " + widget + " to " + actionName2 + ": Destination not assignable");
            return "set '" + eventName + "' of widget '" + widget + "' -> action '" + actionName2 + "' via " + via + " (" + where + ")";
        });
    }

    // set_element_description: set the Description property on an element (web block or action) in
    // an OPEN module — LIVE, undo unit. Description is writable (confirmed via probe_block_members).
    // kind: block | action (any action type) | screen. name = element name. Clean way to document.
    static string SetElementDescription(string module, string kind, string name, string description)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "set element description", es =>
        {
            object el = null; string elKind = null;
            if (kind == "block") { el = FindBlock(es, name); elKind = "block '" + name + "'"; }
            else if (kind == "screen") { el = FindScreen(es, name); elKind = "screen '" + name + "'"; }
            else if (kind == "action") { el = FindAction(es, name); elKind = "action '" + name + "'"; }
            if (el == null) throw new Exception(kind + " not found: " + name);
            var dp = el.GetType().GetProperty("Description");
            if (dp == null || dp.GetSetMethod() == null) throw new Exception("Description not settable on " + el.GetType().Name);
            dp.SetValue(el, description ?? "");
            return "set Description on " + elKind + " = '" + (description ?? "") + "'";
        });
    }

    // list_block_input_params: read-only dump of a web block's InputParameters (name + type).
    // Web block input parameters are how parent screens/blocks pass data INTO the block.
    static string ListBlockInputParams(string module, string block)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var items = new List<object>();
        var inputs = GetProp(blk, "InputParameters") as IEnumerable;
        if (inputs != null)
            foreach (var p in inputs)                items.Add(new { name = GetProp(p, "Name") ?? "?", type = TypeLabel(GetProp(p, "DataType")) });
        return Json(new { ok = true, block = block, inputParams = items });
    }

    // add_input_param_to_block: create an INPUT PARAMETER on a web block (the block's
    // InputParameters collection) so parent screens/blocks can pass data in. Mirrors the factory-hunt
    // pattern of AddVariableToBlock. Sets the DataType from a basic type (es.<Type>Type) or a
    // Structure by unique name. Undo unit.
    static string AddInputParamToBlock(string module, string block, string name, string type)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        return RunCmd(module, "add input param to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var inputs = GetProp(blk, "InputParameters") as IEnumerable;
            if (inputs != null)
                foreach (var v in inputs)
                    try { if ((GetProp(v, "Name") as string) == name) throw new Exception("input param already exists: " + name); } catch (Exception e) { if (e.Message.Contains("already exists")) throw; }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null; var errs = new List<string>();
            if (inputs != null)
            {
                foreach (var mName in new[] { "CreateInputParameter", "CreateParameter", "Add", "Create" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(inputs, mName, pc);
                        if (m == null) continue;
                        try
                        {
                            created = m.Invoke(inputs, pc == 2 ? new object[] { name, key } : new object[] { name });
                            via = "InputParameters." + m.Name;
                            break;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
            {
                foreach (var mName in new[] { "CreateInputParameter", "CreateParameter", "AddInputParameter" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(blk, mName, pc);
                        if (m == null) continue;
                        try
                        {
                            created = m.Invoke(blk, pc == 2 ? new object[] { name, key } : new object[] { name });
                            via = blk.GetType().Name + "." + m.Name;
                            break;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
                throw new Exception("no input-param factory worked; block InputParameters: " + (inputs?.GetType().FullName ?? "null") + " | " + string.Join(" ; ", errs));
            object dataType = null;
            if (TryGetProp(es, type + "Type", out dataType) || TryGetProp(es, type, out dataType)) { }
            else
            {
                var structures = GetProp(es, "Structures") as IEnumerable;
                if (structures != null)
                    foreach (var s in structures)
                        try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
            }
            if (dataType != null) { try { SetProp(created, "DataType", dataType); } catch { } }
            return "created input param '" + name + "' in block '" + block + "' via " + via + " (" + created.GetType().Name + ")" + (dataType != null ? " : " + TypeLabel(dataType) : "");
        });
    }

    // set_block_input_param_type: set the DataType of an existing web-block input parameter.
    // Same resolution as SetInputParamType (basic types, Structures, entity records via
    // producerModule, entity IdentifierType via entityName).
    static string SetBlockInputParamType(string module, string block, string paramName, string typeName, string producerModule, string entityName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "paramName required" });
        if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(entityName)) return Json(new { ok = false, error = "typeName or entityName required" });
        return RunCmd(module, "set block input param type", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object inP = null;
            var ins = GetProp(blk, "InputParameters") as IEnumerable;
            if (ins != null) foreach (var p in ins) { try { if ((GetProp(p, "Name") as string) == paramName) { inP = p; break; } } catch { } }
            if (inP == null) throw new Exception("input param not found: " + paramName + " in block '" + block + "'");
            object dataType = null;
            if (!string.IsNullOrEmpty(entityName))
            {
                if (string.IsNullOrEmpty(producerModule)) throw new Exception("producerModule required when entityName is provided");
                var entity = FindEntityInReferences(es, entityName, producerModule);
                if (entity == null) throw new Exception("entity not found: " + entityName + " in " + producerModule);
                dataType = GetProp(entity, "IdentifierType");
                if (dataType == null) throw new Exception("could not get IdentifierType from entity " + entityName);
            }
            else
            {
                if (TryGetProp(es, typeName + "Type", out dataType) || TryGetProp(es, typeName, out dataType)) { }
                else
                {
                    var structures = GetProp(es, "Structures") as IEnumerable;
                    if (structures != null)
                        foreach (var s in structures)
                            try { if ((GetProp(s, "Name") as string) == typeName) { dataType = s; break; } } catch { }
                }
                if (dataType == null && !string.IsNullOrEmpty(producerModule))
                    dataType = FindEntityInReferences(es, typeName, producerModule);
            }
            if (dataType == null) throw new Exception("type not found: " + typeName + (producerModule != null ? " (producer: " + producerModule + ")" : ""));
            SetProp(inP, "DataType", dataType);
            return "set " + paramName + " DataType -> " + (entityName ?? typeName) + " (" + TypeLabel(dataType) + ") in block '" + block + "'";
        });
    }

    // remove_input_param_from_block: remove a web-block input parameter by name.
    // remove_screen_input_param: delete an input parameter from a SCREEN (the revert path
    // when an input added for testing breaks existing callers — every link to that screen
    // then requires the argument). Mirrors RemoveInputParamFromBlock. Undo unit.
    static string RemoveScreenInputParam(string module, string screen, string paramName)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "paramName required" });
        return RunCmd(module, "remove screen input param", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var inputs = GetProp(sc, "InputParameters") as IEnumerable;
            if (inputs == null) throw new Exception("InputParameters collection is null");
            object toRemove = null;
            foreach (var p in inputs)
            {
                try { if ((GetProp(p, "Name") as string) == paramName) { toRemove = p; break; } } catch { }
            }
            if (toRemove == null) throw new Exception("input param not found: " + paramName);
            CallMethod(toRemove, "Delete", null, 0);
            return "removed input param: " + paramName + " from screen '" + screen + "'";
        });
    }

    static string RemoveInputParamFromBlock(string module, string block, string paramName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "paramName required" });
        return RunCmd(module, "remove input param from block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var inputs = GetProp(blk, "InputParameters") as IEnumerable;
            if (inputs == null) throw new Exception("InputParameters collection is null");
            object toRemove = null;
            foreach (var p in inputs)
            {
                try { if ((GetProp(p, "Name") as string) == paramName) { toRemove = p; break; } } catch { }
            }
            if (toRemove == null) throw new Exception("input param not found: " + paramName);
            CallMethod(toRemove, "Delete", null, 0);
            return "removed input param: " + paramName + " from block '" + block + "'";
        });
    }

    // add_entity_input_to_block: add a web-block input parameter typed as an entity record from a
    // consumed reference (producerModule). Mirrors AddEntityInput for blocks.
    static string AddEntityInputToBlock(string module, string block, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName)) return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(producerModule)) return Json(new { ok = false, error = "producerModule required for entity input params" });
        return RunCmd(module, "add entity input param to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object entityType = FindEntityInReferences(es, entityName, producerModule);
            if (entityType == null) throw new Exception("entity not found: " + entityName + " in " + producerModule + " (check that the module is consumed and the entity is public)");
            object created = null; string via = null;
            var inputs = GetProp(blk, "InputParameters") as IEnumerable;
            foreach (var mName in new[] { "CreateInputParameter", "CreateParameter" })
            {
                foreach (var pc in new[] { 2, 1 })
                {
                    var m = FindMethod(inputs, mName, pc);
                    if (m == null) continue;
                    try { created = m.Invoke(inputs, pc == 2 ? new object[] { name, null } : new object[] { name }); via = "InputParameters." + m.Name; break; }
                    catch { }
                }
                if (created != null) break;
            }
            if (created == null) { created = CallMethod(blk, "CreateInputParameter", new object[] { name, null }, 2); via = blk.GetType().Name + ".CreateInputParameter"; }
            SetProp(created, "DataType", entityType);
            return "added entity input " + name + " : " + entityName + " (from " + producerModule + ") in block '" + block + "' via " + via;
        });
    }

    // add_entity_identifier_input_to_block: add a web-block input parameter typed as an entity's
    // Identifier type from a consumed reference. Mirrors AddEntityIdentifierInput for blocks.
    static string AddEntityIdentifierInputToBlock(string module, string block, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName)) return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(producerModule)) return Json(new { ok = false, error = "producerModule required for entity identifier input params" });
        return RunCmd(module, "add entity identifier input param to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var inputs = GetProp(blk, "InputParameters") as IEnumerable;
            if (inputs != null)
                foreach (var p in inputs)
                    try { if ((GetProp(p, "Name") as string) == name) throw new Exception("input param already exists: " + name); } catch (Exception ex) { if (ex.Message.Contains("already exists")) throw; }
            var entity = FindEntityInReferences(es, entityName, producerModule);
            if (entity == null) throw new Exception("entity not found: " + entityName + " in " + producerModule);
            object identifierType = GetProp(entity, "IdentifierType");
            if (identifierType == null) throw new Exception("could not get IdentifierType from entity " + entityName);
            object created = null;
            var inputs2 = GetProp(blk, "InputParameters") as IEnumerable;
            foreach (var mName in new[] { "CreateInputParameter", "CreateParameter" })
            {
                foreach (var pc in new[] { 2, 1 })
                {
                    var m = FindMethod(inputs2, mName, pc);
                    if (m == null) continue;
                    try { created = m.Invoke(inputs2, pc == 2 ? new object[] { name, null } : new object[] { name }); break; }
                    catch { }
                }
                if (created != null) break;
            }
            if (created == null) created = CallMethod(blk, "CreateInputParameter", new object[] { name, null }, 2);
            SetProp(created, "DataType", identifierType);
            return "added entity identifier input " + name + " : " + entityName + " Identifier (from " + producerModule + ") in block '" + block + "'";
        });
    }

    // Find an action by name within a single named collection on a parent object (e.g. a
    // screen's ClientActions holding ClientScreenActionFlow).
    static object FindActionInCollection(object parent, string collectionName, string name)
    {
        var coll = GetProp(parent, collectionName) as IEnumerable;
        if (coll == null) return null;
        foreach (var item in coll)
        {
            try { if ((GetProp(item, "Name") as string) == name) return item; } catch { }
        }
        return null;
    }

    // AddInsidePlaceholder: locate a placeholder by Name on the screen's layout instance, then
    // find a named parent widget (container/link) inside that placeholder's child widgets, then
    // create a widget INSIDE the parent. The parent container in Reactive is a
    // CustomPlaceholderWidget exposing a 'content' CustomPlaceholderWidget placeholder; we create
    // the new widget on that content holder so it lands as a child of the container (needed to
    // nest text/links/cards inside a container dropped into a layout placeholder). Wrapped in
    // RunCmd (undo unit).
    static string AddInsidePlaceholder(string module, string screen, string placeholder, string parent, string kind, string name, string text, string targetScreen, string styleClass)
    {
        if (string.IsNullOrEmpty(parent)) return Json(new { ok = false, error = "parent (container name inside placeholder) required" });
        if (string.IsNullOrEmpty(kind)) return Json(new { ok = false, error = "kind required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add inside placeholder container", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var layoutInstance = FindLayoutInstance(sc);
            if (layoutInstance == null) throw new Exception("layout WebBlockInstanceWidget not found on screen '" + screen + "'");
            object instance = null;
            try { instance = GetProp(layoutInstance, "Instance"); } catch { }
            object foundPlaceholder = null;
            foreach (var tgt in new object[] { layoutInstance, instance }.Where(t => t != null))
            {
                IEnumerable phColl = GetProp(tgt, "Placeholders") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderInstances") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderWidgets") as IEnumerable;
                if (phColl == null) continue;
                foreach (var ph in phColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, placeholder, StringComparison.OrdinalIgnoreCase)) { foundPlaceholder = ph; break; }
                }
                if (foundPlaceholder != null) break;
            }
            if (foundPlaceholder == null) throw new Exception("placeholder not found: " + placeholder);

            // Find the named parent widget inside the placeholder (recursively through Widgets).
            object parentWidget = null;
            IEnumerable phWidgets = GetProp(foundPlaceholder, "Widgets") as IEnumerable;
            if (phWidgets != null) parentWidget = FindWidgetRecursive(phWidgets, parent);
            if (parentWidget == null) throw new Exception("parent widget not found: " + parent + " in placeholder '" + placeholder + "'");

            // The container is a CustomPlaceholderWidget with a 'content' CustomPlaceholderWidget
            // placeholder; create the new widget on that content holder so it nests inside.
            object contentHolder = null;
            IEnumerable parPhColl = GetProp(parentWidget, "Placeholders") as IEnumerable;
            if (parPhColl != null)
            {
                foreach (var ph in parPhColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, "content", StringComparison.OrdinalIgnoreCase)) { contentHolder = ph; break; }
                }
            }
            object host = contentHolder ?? parentWidget;

            var w = CreateWidgetByKind(host, kind, name);
            if (w == null) throw new Exception("CreateWidgetByKind returned null for kind=" + kind + " host=" + host.GetType().Name);

            if (kind == "text" && !string.IsNullOrEmpty(text)) { try { SetProp(w, "Text", text); } catch { } }
            if (kind == "expression" && !string.IsNullOrEmpty(text)) { try { CallMethod(w, "SetValue", new object[] { text }, 1); } catch { try { SetProp(w, "Text", text); } catch { } } }
            if (!string.IsNullOrEmpty(styleClass)) SetStyleClassesOn(w, styleClass);
            if (kind == "link")
            {
                if (!string.IsNullOrEmpty(text)) SetWidgetLinkLabel(w, text);
                if (!string.IsNullOrEmpty(targetScreen))
                {
                    var tgt = FindScreen(es, targetScreen);
                    if (tgt != null)
                    {
                        try
                        {
                            var onClick = GetProp(w, "OnClick");
                            if (onClick == null) onClick = CallMethod(w, "CreateOnClick", null, 0);
                            SetProp(onClick, "Destination", tgt);
                        }
                        catch (Exception e) { Log("link destination set failed: " + e.Message); }
                    }
                }
            }
            return "created " + kind + " '" + name + "' inside '" + parent + "' in placeholder '" + placeholder + "' (host=" + host.GetType().Name + ", " + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // DeleteFromPlaceholder: locate a placeholder by Name (case-insensitive) on the screen's
    // layout instance (or its Instance), find a widget by name inside that placeholder's child
    // widgets (recursively), and delete it. Wrapped in RunCmd (undo unit). Use to remove a
    // placeholder fill that was added with the wrong kind/label before re-adding it correctly.
    static string DeleteFromPlaceholder(string module, string screen, string placeholder, string name)
    {
        if (string.IsNullOrEmpty(placeholder)) return Json(new { ok = false, error = "placeholder required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "delete from placeholder", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var layoutInstance = FindLayoutInstance(sc);
            if (layoutInstance == null) throw new Exception("layout WebBlockInstanceWidget not found on screen '" + screen + "'");
            object instance = null;
            try { instance = GetProp(layoutInstance, "Instance"); } catch { }
            object foundPlaceholder = null;
            foreach (var tgt in new object[] { layoutInstance, instance }.Where(t => t != null))
            {
                IEnumerable phColl = GetProp(tgt, "Placeholders") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderInstances") as IEnumerable;
                if (phColl == null) phColl = GetProp(tgt, "PlaceholderWidgets") as IEnumerable;
                if (phColl == null) continue;
                foreach (var ph in phColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, placeholder, StringComparison.OrdinalIgnoreCase)) { foundPlaceholder = ph; break; }
                }
                if (foundPlaceholder != null) break;
            }
            if (foundPlaceholder == null) throw new Exception("placeholder not found: " + placeholder);
            var phWidgets = GetProp(foundPlaceholder, "Widgets") as IEnumerable;
            if (phWidgets == null) throw new Exception("placeholder '" + placeholder + "' has no Widgets collection");
            var w = FindWidgetRecursive(phWidgets, name);
            if (w == null) throw new Exception("widget not found: " + name + " in placeholder '" + placeholder + "'");
            DeleteWidgetRecursive(w);
            return "deleted widget '" + name + "' from placeholder '" + placeholder + "'";
        });
    }

    // DeleteLayout: remove the screen's layout web block instance (the first IWebBlockInstanceWidget
    // child of the screen) entirely, leaving the screen with no layout (a full-bleed custom page).
    // Use to strip a screen's LayoutTopMenu / layout chrome before building standalone content.
    // Wrapped in RunCmd (undo unit).
    static string DeleteLayout(string module, string screen)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        return RunCmd(module, "delete layout", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var layoutInstance = FindLayoutInstance(sc);
            if (layoutInstance == null) return "screen '" + screen + "' has no layout web block instance (nothing to delete)";
            string removedName = "(unnamed)";
            try { var sb = GetProp(layoutInstance, "SourceWebBlock"); if (sb != null) removedName = (GetProp(sb, "Name") as string) ?? "(unnamed)"; } catch { }
            DeleteWidgetRecursive(layoutInstance);
            int remaining = 0;
            var widgets = GetProp(sc, "Widgets") as IEnumerable;
            if (widgets != null) foreach (var w in widgets) remaining++;
            return "deleted layout web block '" + removedName + "' from screen '" + screen + "' (remaining top-level widgets: " + remaining + ")";
        });
    }

    // SetHtmlAttr: write an HTML attribute (e.g. src on an iframe, or allowfullscreen) onto an
    // existing HTML-element widget (CustomWidget / IHtmlElementWidget). Tries the widget's
    // ExtendedProperties collection (the surface SS uses for arbitrary HTML attrs) first, then
    // CustomProperties (PropertyName/Value), then a SetPropertyValue(name, value) call. Wrapped in
    // RunCmd (undo unit). Reads back the value to confirm.
    static string SetHtmlAttr(string module, string screen, string widget, string attrName, string attrValue)
    {
        if (string.IsNullOrEmpty(widget)) return Json(new { ok = false, error = "widget required" });
        if (string.IsNullOrEmpty(attrName)) return Json(new { ok = false, error = "attrName required" });
        return RunCmd(module, "set html attr", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget);
            string usedSurface = null;
            // 1) ExtendedProperties (ExtendedProperty entries - Name + Value).
            foreach (var collName in new[] { "ExtendedProperties", "Attributes" })
            {
                try
                {
                    var coll = GetProp(w, collName) as IEnumerable;
                    if (coll == null) continue;
                    object target = null;
                    foreach (var e in coll)
                    {
                        string en = null;
                        try { en = GetProp(e, "Name") as string; } catch { }
                        if (string.Equals(en, attrName, StringComparison.OrdinalIgnoreCase)) { target = e; break; }
                    }
                    if (target == null)
                    {
                        // Create a new entry: try CreateExtendedProperty(name, key) / Add(name)
                        var ms = ModelServices();
                        object key = null;
                        try { key = CallMethod(ms, "NewKey", null, 0); } catch { }
                        var created = CreateExtendedPropertyOn(w, attrName, key);
                        if (created == null) created = CreateExtendedPropertyOn(w, attrName, null);
                        if (created != null)
                        {
                            coll = GetProp(w, collName) as IEnumerable;
                            if (coll != null) foreach (var e in coll) { string en = null; try { en = GetProp(e, "Name") as string; } catch { } if (string.Equals(en, attrName, StringComparison.OrdinalIgnoreCase)) { target = e; break; } }
                        }
                    }
                    if (target != null)
                    {
                        SetPropOrValue(target, attrValue);
                        usedSurface = collName + ":" + target.GetType().Name;
                        break;
                    }
                }
                catch (Exception e) { Log("SetHtmlAttr " + collName + " failed: " + e.Message); }
            }
            // 2) CustomProperties with PropertyName == attrName.
            if (usedSurface == null)
            {
                try
                {
                    var cps = GetProp(w, "CustomProperties") as IEnumerable;
                    if (cps != null)
                    {
                        object target = null;
                        foreach (var cp in cps)
                        {
                            var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                            if (string.Equals(pn, attrName, StringComparison.OrdinalIgnoreCase)) { target = cp; break; }
                        }
                        if (target != null)
                        {
                            SetPropOrValue(target, attrValue);
                            usedSurface = "CustomProperties:" + target.GetType().Name;
                        }
                    }
                }
                catch (Exception e) { Log("SetHtmlAttr CustomProperties failed: " + e.Message); }
            }
            // 3) Generic SetPropertyValue / SetAttributeValue call.
            if (usedSurface == null)
            {
                foreach (var m in new[] { "SetPropertyValue", "SetAttributeValue" })
                {
                    try { CallMethod(w, m, new object[] { attrName, attrValue }, 2); usedSurface = m; break; } catch { }
                }
            }
            if (usedSurface == null) throw new Exception("no attribute surface found for '" + attrName + "' on " + w.GetType().Name);
            string readBack = null;
            try
            {
                var coll = GetProp(w, "ExtendedProperties") as IEnumerable;
                if (coll != null) foreach (var e in coll) { string en = null; try { en = GetProp(e, "Name") as string; } catch { } if (string.Equals(en, attrName, StringComparison.OrdinalIgnoreCase)) { readBack = (GetProp(e, "Value") as string); break; } }
            }
            catch { }
            return "set attr '" + attrName + "' = '" + (attrValue ?? "") + "' on '" + widget + "' (" + usedSurface + (readBack != null ? ", read-back='" + readBack + "'" : "") + ")";
        });
    }

    // Create an ExtendedProperty entry on a widget, returning it (or null on failure). Tries the
    // common model factories: IParent<ExtendedProperty>.CreateExtendedProperty(name,key),
    // ExtendedProperty(name,key) ctor via NewKey, or an 'Add' method that takes a name.
    static object CreateExtendedPropertyOn(object w, string name, object key)
    {
        try { var m = FindMethod(w, "CreateExtendedProperty", 2); if (m != null) return m.Invoke(w, new object[] { name, key ?? ModelServicesNewKey() }); } catch (Exception e) { Log("CreateExtendedProperty(2) failed: " + e.Message); }
        try { var m = FindMethod(w, "CreateExtendedProperty", 1); if (m != null) return m.Invoke(w, new object[] { name }); } catch (Exception e) { Log("CreateExtendedProperty(1) failed: " + e.Message); }
        try { var m = FindMethod(w, "CreateExtendedProperty", 0); if (m != null) return m.Invoke(w, null); } catch (Exception e) { Log("CreateExtendedProperty(0) failed: " + e.Message); }
        try { var m = FindMethod(w, "Add", 1); if (m != null) return m.Invoke(w, new object[] { name }); } catch (Exception e) { Log("Add(name) failed: " + e.Message); }
        return null;
    }

    static object ModelServicesNewKey()
    {
        try { var ms = ModelServices(); if (ms == null) return null; var m = FindMethod(ms, "NewKey", 0); if (m != null) return m.Invoke(ms, null); } catch { }
        return null;
    }

    static void SetPropOrValue(object target, string value)
    {
        try { SetProp(target, "Value", value); return; } catch { }
        try { SetProp(target, "Text", value); return; } catch { }
        try { CallMethod(target, "SetValue", new object[] { value }, 1); } catch { }
    }

    // AddLinkToBlock: find a web block by name (e.g. Menu), locate a parent container within its
    // widget tree (e.g. PageLinks), and create a Link widget inside it. Used to put nav links
    // (Home/Stats/Challenges/Settings) into the Menu block's PageLinks so every screen using the
    // layout gets the same navigation. Wrapped in RunCmd (undo unit).
    static string AddLinkToBlock(string module, string block, string parent, string name, string text, string targetScreen, string styleClass)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add link to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object host = blk;
            if (!string.IsNullOrEmpty(parent))
            {
                var par = FindWidget(blk, parent);
                if (par == null) throw new Exception("container not found: " + parent + " in block '" + block + "'");
                host = par;
            }
            var w = CreateWidgetByKind(host, "link", name);
            if (w == null) throw new Exception("CreateWidgetByKind returned null for kind=link host=" + host.GetType().Name);
            if (!string.IsNullOrEmpty(text)) SetWidgetLinkLabel(w, text);
            if (!string.IsNullOrEmpty(styleClass)) SetStyleClassesOn(w, styleClass);
            if (!string.IsNullOrEmpty(targetScreen))
            {
                var tgt = FindScreen(es, targetScreen);
                if (tgt != null)
                {
                    try
                    {
                        var onClick = GetProp(w, "OnClick");
                        if (onClick == null) onClick = CallMethod(w, "CreateOnClick", null, 0);
                        SetProp(onClick, "Destination", tgt);
                    }
                    catch (Exception e) { Log("link destination set failed: " + e.Message); }
                }
            }
            return "created link '" + name + "' in block '" + block + "'" + (string.IsNullOrEmpty(parent) ? "" : " container '" + parent + "'") + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // Create a Placeholder widget in a web block's widget tree (or inside a named container in it).
    // Mirrors AddLinkToBlock but creates a placeholder (kind "placeholder" in WidgetKindCandidates).
    // Placeholders let content be filled when the block is used; each gets its own Name in the tree.
    static string AddPlaceholderToBlock(string module, string block, string parent, string name)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add placeholder to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object host = blk;
            if (!string.IsNullOrEmpty(parent))
            {
                var par = FindWidget(blk, parent);
                if (par == null) throw new Exception("container not found: " + parent + " in block '" + block + "'");
                host = par;
            }
            var w = CreateWidgetByKind(host, "placeholder", name);
            if (w == null) throw new Exception("CreateWidgetByKind returned null for kind=placeholder host=" + host.GetType().Name);
            return "created placeholder '" + name + "' in block '" + block + "'" + (string.IsNullOrEmpty(parent) ? "" : " container '" + parent + "'") + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // Create any widget kind (container/text/expression/link/html/placeholder) in a web block's
    // widget tree (or a named container inside it). Kind maps to WidgetKindCandidates; value only
    // applies to text/expression (SetValue / Text). Mirrors AddLinkToBlock/AddPlaceholderToBlock.
    static string AddWidgetToBlock(string module, string block, string parent, string kind, string name, string value, string styleClass)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(kind)) return Json(new { ok = false, error = "kind required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add widget to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object host = blk;
            if (!string.IsNullOrEmpty(parent))
            {
                var par = FindWidget(blk, parent);
                if (par == null) throw new Exception("container not found: " + parent + " in block '" + block + "'");
                host = par;
            }
            var w = CreateWidgetByKind(host, kind, name);
            if (w == null) throw new Exception("CreateWidgetByKind returned null for kind=" + kind + " host=" + host.GetType().Name);
            if (!string.IsNullOrEmpty(value))
            {
                if (kind == "expression")
                {
                    bool set = false;
                    try { CallMethod(w, "SetValue", new object[] { value }, 1); set = true; } catch { }
                    if (!set) { try { SetProp(w, "Value", value); set = true; } catch { } }
                    if (!set) { try { SetProp(w, "Text", value); } catch { } }
                }
                else if (kind == "text")
                {
                    try { CallMethod(w, "SetValue", new object[] { value }, 1); }
                    catch { try { SetProp(w, "Text", value); } catch { } }
                }
            }
            if (!string.IsNullOrEmpty(styleClass)) SetStyleClassesOn(w, styleClass);
            return "created " + kind + " '" + name + "' in block '" + block + "'" + (string.IsNullOrEmpty(parent) ? "" : " container '" + parent + "'") + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // Delete a widget by name from a web block's widget tree (recursively removes children
    // too). Mirrors DeleteWidget on screens. Undo unit.
    static string DeleteWidgetFromBlock(string module, string block, string name)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "delete widget from block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, name);
            if (w == null) throw new Exception("widget not found in block: " + name);
            DeleteWidgetRecursive(w);
            return "deleted widget '" + name + "' (and children) from block '" + block + "'";
        });
    }

    // delete_anon_block_widgets: delete UNNAMED (anonymous) widgets from a web block's TOP-LEVEL
    // widget tree, filtered by concrete-type substring (e.g. "NRWidgets.Image"). Anon widgets have
    // a null/empty Name, so FindWidget-by-name (delete_widget_from_block) cannot address them.
    // Named widgets are never touched. Undo unit per RunCmd.
    static string DeleteAnonBlockWidgets(string module, string block, string typeContains, bool recursive = false)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(typeContains)) return Json(new { ok = false, error = "typeContains required" });
        return RunCmd(module, "delete anon block widgets", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var deleted = new List<string>();
            DeleteAnonWidgetsRec(blk, typeContains, recursive, deleted);
            int remaining = 0;
            var widgets = GetProp(blk, "Widgets") as IEnumerable;
            if (widgets != null) foreach (var w in widgets) remaining++;
            return "deleted " + deleted.Count + " anon widget(s) matching '" + typeContains + "' from block '" + block + "' [" + string.Join(", ", deleted) + "] (remaining top-level widgets: " + remaining + ")";
        });
    }

    // Walk an owner (block/widget/placeholder) collecting UNNAMED widgets whose type matches,
    // optionally recursing into child widgets and placeholders. Deletes collected targets at the
    // end so collection enumeration isn't mutated mid-walk.
    static void DeleteAnonWidgetsRec(object owner, string typeContains, bool recursive, List<string> deleted)
    {
        var targets = new List<object>();
        CollectAnonWidgets(owner, typeContains, recursive, targets, deleted, 0);
        foreach (var t in targets)
        {
            deleted.Add(t.GetType().Name);
            DeleteWidgetRecursive(t);
        }
    }

    static void CollectAnonWidgets(object owner, string typeContains, bool recursive, List<object> targets, List<string> deleted, int depth)
    {
        if (depth > 25) return;
        var widgets = GetProp(owner, "Widgets") as IEnumerable;
        if (widgets != null)
            foreach (var w in widgets)
            {
                string nm = null;
                try { nm = GetProp(w, "Name") as string; } catch { }
                var tn = w.GetType().FullName ?? "";
                if (string.IsNullOrEmpty(nm) && tn.IndexOf(typeContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    targets.Add(w);
                else if (recursive) CollectAnonWidgets(w, typeContains, true, targets, deleted, depth + 1);
            }
        var phs = GetProp(owner, "Placeholders") as IEnumerable;
        if (phs != null)
            foreach (var ph in phs)
                if (recursive) CollectAnonWidgets(ph, typeContains, true, targets, deleted, depth + 1);
    }

    // Read-only diagnostic: dump every widget in a web block with its concrete type AND all its
    // interfaces, including anonymous widgets (which list_block_widgets shows as (anon)). Use to
    // discover the exact concrete type/interface of widgets SS creates (e.g. a real [Expression]).
    static string DumpBlockWidgetTypes(string module, string block)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var list = new List<object>();
        DumpBlockWidgetTypesRec(blk, list, "");
        return Json(new { ok = true, block = block, widgets = list });
    }

    static void DumpBlockWidgetTypesRec(object w, List<object> list, string path)
    {
        IEnumerable widgets = GetProp(w, "Widgets") as IEnumerable;
        if (widgets == null) return;
        foreach (var c in widgets)
        {
            string nm; try { nm = GetProp(c, "Name") as string ?? ""; } catch { nm = ""; }
            var ifaces = new List<string>();
            try { foreach (var i in c.GetType().GetInterfaces()) ifaces.Add(i.FullName); } catch { }
            var cpath = path + (string.IsNullOrEmpty(nm) ? "(anon)" : nm);
            list.Add(new { name = cpath, type = c.GetType().FullName, interfaces = ifaces });
            DumpBlockWidgetTypesRec(c, list, cpath + "/");
        }
    }

    // Create a Local Variable in a web block (NRWebBlock.Variables) from scratch. Hunts a
    // factory on the Variables collection (CreateLocalVariable/CreateVariable/Create/Add) or the
    // block itself, invokes inside RunCmd (undo unit). Returns the created variable type.
    // ============ Acceptance batch: screen variables + screen-aggregate refresh ============
    // add_screen_variable: create a LOCAL VARIABLE on a screen (page vars for pagination:
    // PageNumber, PageSize, SearchText...). Mirrors AddVariableToBlock; optional type +
    // default applied best-effort (basic types via ResolveAttrDataType). Undo unit.
    static string AddVariableToScreen(string module, string screen, string name, string type, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrWhiteSpace(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add variable to screen", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var vars = GetProp(sc, "LocalVariables") as IEnumerable;
            if (vars == null) vars = GetProp(sc, "Variables") as IEnumerable;
            if (vars != null)
                foreach (var v in vars)
                    try { if ((GetProp(v, "Name") as string) == name) throw new Exception("variable already exists: " + name); } catch (Exception e) { throw; }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null; var errs = new List<string>();
            foreach (var host in new object[] { vars, sc }.Where(h => h != null))
            {
                if (created != null) break;
                foreach (var mName in new[] { "CreateLocalVariable", "CreateVariable", "Add", "Create" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(host, mName, pc);
                        if (m == null) continue;
                        try
                        {
                            created = m.Invoke(host, pc == 2 ? new object[] { name, key } : new object[] { name });
                            via = host.GetType().Name + "." + m.Name; break;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(mName + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
                throw new Exception("no variable factory worked: " + string.Join(" ; ", errs));
            var flags = new List<string>();
            if (!string.IsNullOrEmpty(type))
            {
                object dt = ResolveAttrDataType(es, type);
                if (dt == null) flags.Add("type NOT resolved: " + type);
                else { try { SetProp(created, "DataType", dt); flags.Add("type=" + type); } catch (Exception e) { flags.Add("type NOT set: " + FirstMsg(e)); } }
            }
            if (!string.IsNullOrEmpty(defaultValue))
            {
                try { SetProp(created, "DefaultValue", defaultValue); flags.Add("default=" + defaultValue); }
                catch (Exception e) { flags.Add("default NOT set: " + FirstMsg(e)); }
            }
            return "created variable '" + name + "' on screen '" + screen + "' via " + via + (flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "");
        });
    }

    static string AddVariableToBlock(string module, string block, string name)
    {
        if (string.IsNullOrWhiteSpace(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrWhiteSpace(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add variable to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var vars = GetProp(blk, "Variables") as IEnumerable;
            if (vars != null)
                foreach (var v in vars)
                    try { if ((GetProp(v, "Name") as string) == name) throw new Exception("variable already exists: " + name); } catch (Exception e) { throw; }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null; var errs = new List<string>();
            if (vars != null)
            {
                foreach (var mName in new[] { "CreateLocalVariable", "CreateVariable", "Add", "Create" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(vars, mName, pc);
                        if (m == null) continue;
                        try
                        {
                            created = m.Invoke(vars, pc == 2 ? new object[] { name, key } : new object[] { name });
                            via = "Variables." + m.Name;
                            break;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
            {
                foreach (var mName in new[] { "CreateLocalVariable", "CreateVariable" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(blk, mName, pc);
                        if (m == null) continue;
                        try
                        {
                            created = m.Invoke(blk, pc == 2 ? new object[] { name, key } : new object[] { name });
                            via = blk.GetType().Name + "." + m.Name;
                            break;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
                throw new Exception("no variable factory worked; block Variables collection: " + (vars?.GetType().FullName ?? "null") + " | " + string.Join(" ; ", errs));
            return "created variable '" + name + "' in block '" + block + "' via " + via + " (" + created.GetType().Name + ")";
        });
    }

    // Set any string property on a widget inside a web block (mirror of SetWidgetProperty on
    // screens). For an Expression's value, use propName "Value" (or "Text"); pass the expression
    // reference text as-is (e.g. the variable name Test2Variable). Undo unit.
    static string SetBlockWidgetProperty(string module, string block, string widgetName, string propName, string propValue)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widgetName)) return Json(new { ok = false, error = "widgetName required" });
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        return RunCmd(module, "set block widget property", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widgetName);
            if (w == null) throw new Exception("widget not found: " + widgetName + " in block '" + block + "'");
            bool set = false; string via = propName; string setValueDiag = "";
            if (propName == "Value" || propName == "ValueExpression" || propName == "Text")
            {
                try { CallMethodTyped(w, "SetValue", new object[] { propValue }); set = true; via = propName + " (SetValue)"; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; setValueDiag = "SetValue err: " + r.GetType().Name + ": " + r.Message; }
            }
            if (!set) { try { SetProp(w, propName, propValue); set = true; } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception("set " + propName + " failed: " + r.Message + (string.IsNullOrEmpty(setValueDiag) ? "" : " [" + setValueDiag + "]")); } }
            return "set " + propName + " = " + (propValue ?? "") + " on '" + widgetName + "' in block '" + block + "' via " + via + " (" + w.GetType().Name + ")" + (string.IsNullOrEmpty(setValueDiag) ? "" : " [" + setValueDiag + "]");
        });
    }

    // Read-only diagnostic: dump the widget tree of a web block (screens use FindScreen; blocks
    // use FindBlock). Lists each widget name + concrete type recursively.
    static string ListBlockWidgets(string module, string block)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BLOCK: " + block + " (" + blk.GetType().Name + ")");
        var widgets = GetProp(blk, "Widgets") as IEnumerable;
        DumpWidgetTree(widgets, sb, "");
        return Json(new { ok = true, block = block, blockType = blk.GetType().Name, tree = sb.ToString() });
    }

    static void DumpWidgetTree(IEnumerable widgets, System.Text.StringBuilder sb, string indent)
    {
        if (widgets == null) return;
        foreach (var w in widgets)
        {
            string nm; try { nm = GetProp(w, "Name") as string ?? ""; } catch { nm = ""; }
            sb.AppendLine(indent + (string.IsNullOrEmpty(nm) ? "(anon)" : nm) + " [" + w.GetType().Name + "]");
            var ch = GetProp(w, "Widgets") as IEnumerable;
            if (ch != null) DumpWidgetTree(ch, sb, indent + "  ");
            var phColl = GetProp(w, "Placeholders") as IEnumerable;
            if (phColl != null)
            {
                foreach (var ph in phColl)
                {
                    string phn; try { phn = GetProp(ph, "Name") as string ?? ""; } catch { phn = ""; }
                    sb.AppendLine(indent + "  {ph:" + phn + " [" + ph.GetType().Name + "]}");
                    var phw = GetProp(ph, "Widgets") as IEnumerable;
                    if (phw != null) DumpWidgetTree(phw, sb, indent + "    ");
                }
            }
        }
    }

    // Diagnostic: dump every concrete widget class in loaded assemblies (NRWebWidgets /
    // WebWidgets / MobileWidgets) with its relevant interfaces. Reveals the interface
    // names Reactive containers/expressions/links implement (which aren't I*Widget-named).
    static string DumpWidgetConcretes()
    {
        var result = new List<object>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                try
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    var ns = t.Namespace ?? "";
                    if (!(ns.Contains("NRWebWidget") || ns.Contains("WebWidget") || ns.Contains("MobileWidget") || (ns.Contains("Model") && t.Name.EndsWith("Widget")))) continue;
                    var ifaces = t.GetInterfaces().Where(i =>
                    {
                        var n = i.Name;
                        return n.Contains("Widget") || n.Contains("Container") || n.Contains("Expression") || n.Contains("Link") || n.Contains("Placeholder") || n.Contains("Content") || n == "IText";
                    }).Select(i => i.FullName).ToList();
                    if (ifaces.Count == 0) continue;
                    result.Add(new { type = t.FullName, ifaces = ifaces });
                }
                catch { }
            }
        }
        return Json(new { ok = true, count = result.Count, widgets = result });
    }

    // Read-only diagnostic: find widget descriptor catalog entries (static fields/properties
    // whose name matches a widget kind and whose type contains "Descriptor"). Reveals how to
    // obtain a CustomObjectDescriptor for Container/Text/Expression/Link/Html in Reactive.
    static string ProbeDescriptors()
    {
        var descriptorTypes = new List<string>();
        var entries = new List<object>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                try
                {
                    if (t.Name.Contains("Descriptor") && (t.Namespace?.Contains("Descriptor") == true || t.Name.Contains("WidgetDescriptor") || t.Name == "CustomObjectDescriptor"))
                        descriptorTypes.Add(t.FullName);
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        var n = f.Name;
                        if ((n.Contains("Container") || n.Contains("Text") || n.Contains("Expression") || n.Contains("Link") || n.Contains("Html") || n.Contains("Button") || n.Contains("IfWidget")) && f.FieldType.Name.Contains("Descriptor"))
                            entries.Add(new { kind = "field", type = t.FullName, name = n, fieldType = f.FieldType.Name });
                    }
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        var n = p.Name;
                        if ((n.Contains("Container") || n.Contains("Text") || n.Contains("Expression") || n.Contains("Link") || n.Contains("Html") || n.Contains("Button") || n.Contains("IfWidget")) && p.PropertyType.Name.Contains("Descriptor"))
                            entries.Add(new { kind = "prop", type = t.FullName, name = n, propType = p.PropertyType.Name });
                    }
                }
                catch { }
            }
        }
        return Json(new { ok = true, descriptorTypes = descriptorTypes, entries = entries });
    }

    // Create a widget via IObjectWithWidgets.CreateWidget(CustomObjectDescriptor), finding
    // the descriptor by kind name (a static field/prop whose name contains the kind and whose
    // type contains "Descriptor"). This is how Reactive containers/html-elements are created
    // (CreateWidget<T> can't make them - no IMobileWidget sub-interface). Single clean creation.
    static string CreateWidgetDescriptor(string module, string screen, string parent, string kind, string name, string styleClass)
    {
        return RunCmd(module, "create widget by descriptor", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var par = ResolveParent(sc, parent);
            if (par == null) throw new Exception("parent widget not found: " + parent);
            // Find a widget descriptor: a static field/prop whose NAME contains the kind
            // (e.g. "Container" -> "ContainerDescriptor") and whose TYPE contains
            // "WidgetDescriptor" (filters out unrelated *PropertyDescriptor fields like
            // ExpressionPropertyDescriptor that happen to contain the kind word).
            object descriptor = null; string via = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (descriptor != null) break;
                Type[] types; try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (descriptor != null) break;
                    try
                    {
                        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        {
                            if (f.FieldType.Name.IndexOf("WidgetDescriptor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (f.Name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            try { descriptor = f.GetValue(null); via = "field " + t.FullName + "." + f.Name; } catch { }
                            if (descriptor != null) break;
                        }
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        {
                            if (p.PropertyType.Name.IndexOf("WidgetDescriptor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (p.Name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            try { descriptor = p.GetValue(null, null); via = "prop " + t.FullName + "." + p.Name; } catch { }
                            if (descriptor != null) break;
                        }
                    }
                    catch { }
                }
            }
            if (descriptor == null) throw new Exception("no widget descriptor found for kind: " + kind);
            MethodInfo cwMethod = null;
            foreach (var iface in par.GetType().GetInterfaces())
                foreach (var m in iface.GetMethods())
                    if (m.Name == "CreateWidget" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(descriptor.GetType()))
                        cwMethod = m;
            if (cwMethod == null) throw new Exception("CreateWidget(descriptor) not found for descriptor " + descriptor.GetType().FullName);
            var w = cwMethod.Invoke(par, new object[] { descriptor });
            try { SetProp(w, "Name", name); } catch { }
            SetStyleClassesOn(w, styleClass);
            return "created " + kind + " '" + name + "' via " + via + " (" + (w?.GetType().Name ?? "?") + ")";
        });
    }

    // Diagnostic: discover which widget interfaces a screen's CreateWidget<T> accepts.
    // Dumps the screen type, its Screen/Widget/Flow interfaces, every CreateWidget<T>
    // method's generic constraint, then brute-force tries CreateWidget for every I*Widget
    // interface in loaded assemblies (namespace contains "Widget"), creating+deleting each.
    // Reveals the correct widget interface namespace for the module kind (e.g. Reactive).
    static string ProbeWidgetApi(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var st = sc.GetType();
        var ifaces = st.GetInterfaces()
            .Where(i => i.Name.Contains("Screen") || i.Name.Contains("Widget") || i.Name.Contains("Flow"))
            .Select(i => i.FullName).Where(f => f != null).ToList();
        var cwMethods = new List<object>();
        MethodInfo createWidgetMethod = null;
        foreach (var iface in st.GetInterfaces())
        {
            foreach (var m in iface.GetMethods())
            {
                if (m.Name == "CreateWidget" && m.IsGenericMethod && m.GetParameters().Length == 2)
                {
                    var args = m.GetGenericArguments();
                    var cs = args.Length == 1 ? args[0].GetGenericParameterConstraints() : new Type[0];
                    cwMethods.Add(new { iface = iface.FullName, constraint = cs.Length > 0 ? cs[0].FullName : "(none)" });
                    if (createWidgetMethod == null) createWidgetMethod = m;
                }
            }
        }
        // Collect every I*Widget interface in loaded assemblies (namespace contains "Widget").
        var candidates = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                try
                {
                    if (!t.IsInterface) continue;
                    if (!(t.Name.StartsWith("I") && t.Name.EndsWith("Widget"))) continue;
                    if (t.Namespace == null || !t.Namespace.Contains("Widget")) continue;
                    candidates.Add(t);
                }
                catch { }
            }
        }
        var tried = new List<object>();
        string probeError = null;
        var agg = GetContext(es);
        if (createWidgetMethod != null && agg != null)
        {
            Action mutate = () =>
            {
                var ms = ModelServices();
                foreach (var t in candidates)
                {
                    try
                    {
                        var key = CallMethod(ms, "NewKey", null, 0);
                        var gen = createWidgetMethod.MakeGenericMethod(t);
                        var w = gen.Invoke(sc, new object[] { "ProbeWidget", key });
                        try { CallMethod(w, "Delete", new object[] { false }, 1); } catch { try { CallMethod(w, "Delete", null, 0); } catch { } }
                        tried.Add(new { iface = t.FullName, ok = true });
                    }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; tried.Add(new { iface = t.FullName, ok = false, err = r.Message }); }
                }
            };
            try
            {
                var pc = BuildPresenterContext(agg);
                var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
                exec.Invoke(null, new object[] { pc, "OsLiveBridge: probe widget api", mutate });
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; probeError = r.GetType().Name + ": " + r.Message; }
        }
        return Json(new { ok = true, screenType = st.FullName, interfaces = ifaces, createWidgetMethods = cwMethods, candidateCount = candidates.Count, tried = tried, probeError = probeError });
    }

    // Diagnostic: dump every Create*/Add*/New*/Insert* method across all interfaces the
    // screen implements, with signatures (generic args + constraints + param types).
    // Reveals the container/expression/link creation API for the module kind (Reactive
    // has no IContainerWidget; the screen may expose CreateContainer/etc. directly).
    static string ProbeCreateMethods(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var st = sc.GetType();
        var methods = new List<object>();
        var seen = new HashSet<string>();
        foreach (var iface in st.GetInterfaces())
        {
            foreach (var m in iface.GetMethods())
            {
                if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
                var key = iface.Name + "." + m.Name + m.GetParameters().Length;
                if (!seen.Add(key)) continue;
                var ga = m.GetGenericArguments();
                var constraints = ga.Select(g => { var c = g.GetGenericParameterConstraints(); return c.Length > 0 ? c[0].Name : "(none)"; }).ToList();
                methods.Add(new
                {
                    iface = iface.FullName,
                    method = m.Name,
                    genericArgs = ga.Length,
                    constraints = constraints,
                    @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))
                });
            }
        }
        return Json(new { ok = true, screenType = st.FullName, methods = methods });
    }

    // Diagnostic: dump all Create*/New*/Add* factory methods on the eSpace concrete type
    // AND its interfaces (deduped). Use to discover the correct web-flow factory for a
    // CrossDevice module (CreateNRWebFlow? CreateWebFlow? a collection-level factory?).
    static string DebugESpaceCreates(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var st = es.GetType();        var methods = new List<object>();
        var seen = new HashSet<string>();
        foreach (var iface in st.GetInterfaces())
        {
            foreach (var m in iface.GetMethods())
            {
                if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
                var key = iface.Name + "." + m.Name + m.GetParameters().Length;
                if (!seen.Add(key)) continue;
                methods.Add(new
                {
                    source = "iface:" + iface.Name,
                    method = m.Name,
                    @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)),
                    ret = m.ReturnType.Name
                });
            }
        }
        foreach (var m in st.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
            var key = "concrete." + m.Name + m.GetParameters().Length;
            if (!seen.Add(key)) continue;
            methods.Add(new
            {
                source = "concrete",
                method = m.Name,
                @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)),
                ret = m.ReturnType.Name
            });
        }
        // Also dump the NRWebFlows collection concrete type + its methods (flows live there).
        var flowColl = GetProp(es, "NRWebFlows") as IEnumerable;
        string flowCollType = null; var flowCollMethods = new List<object>();
        if (flowColl != null)
        {
            flowCollType = flowColl.GetType().FullName;
            var seen2 = new HashSet<string>();
            foreach (var m in flowColl.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
                var key = m.Name + m.GetParameters().Length;
                if (!seen2.Add(key)) continue;
                flowCollMethods.Add(new { method = m.Name, @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)), ret = m.ReturnType.Name });
            }
        }
        // Also dump the NRThemes collection concrete type + its methods (themes live there).
        var themeColl = GetProp(es, "NRThemes") as IEnumerable;
        string themeCollType = null; var themeCollMethods = new List<object>();
        if (themeColl != null)
        {
            themeCollType = themeColl.GetType().FullName;
            var seen3 = new HashSet<string>();
            foreach (var m in themeColl.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!(m.Name.StartsWith("Create") || m.Name.StartsWith("Add") || m.Name.StartsWith("New") || m.Name.StartsWith("Insert"))) continue;
                var key = m.Name + m.GetParameters().Length;
                if (!seen3.Add(key)) continue;
                themeCollMethods.Add(new { method = m.Name, @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)), ret = m.ReturnType.Name });
            }
        }
        // Search every interface + concrete type for ANY method whose name mentions Theme (factory hunt).
        var themeFactories = new List<object>();
        var seen4 = new HashSet<string>();
        var allTypes = new List<Type> { st };
        allTypes.AddRange(st.GetInterfaces());
        foreach (var iface in allTypes)
        {
            foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!m.Name.Contains("Theme")) continue;
                var key = m.Name + m.GetParameters().Length + ":" + iface.Name;
                if (!seen4.Add(key)) continue;
                themeFactories.Add(new { source = iface.Name, method = m.Name, @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)), ret = m.ReturnType.Name });
            }
        }
        return Json(new { ok = true, eSpaceType = st.FullName, methods = methods, nrWebFlowsCollType = flowCollType, nrWebFlowsCollMethods = flowCollMethods, nrThemesCollType = themeCollType, nrThemesCollMethods = themeCollMethods, themeFactories = themeFactories });
    }

    // Scan loaded ServiceStudio.* assemblies for static/instance factory methods that could
    // create a NewRuntime.Theme (e.g. NRFlows-style factories, ThemeCollection.Create...).
    static string ProbeThemeFactories(string module)
    {
        var found = new List<object>();
        var seen = new HashSet<string>();
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!(asm.FullName.StartsWith("ServiceStudio") || asm.FullName.StartsWith("OutSystems"))) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsGenericTypeDefinition || t.FullName == null) continue;
                    if (!(t.FullName.Contains("NewRuntime") || t.FullName.Contains("NRFlows") || t.FullName.Contains("NRNodes") || t.FullName.Contains(".Theme"))) continue;
                    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                    {
                        var key = t.FullName + "..ctor" + c.GetParameters().Length + (c.IsPublic ? ":pub" : ":nonpub");
                        if (!seen.Add(key)) continue;
                        found.Add(new { type = t.FullName, method = ".ctor", isStatic = false, @params = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)), ret = t.FullName });
                    }
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        var key = t.FullName + "." + m.Name + m.GetParameters().Length + (m.IsStatic ? ":s" : ":i");
                        if (!seen.Add(key)) continue;
                        found.Add(new { type = t.FullName, method = m.Name, isStatic = m.IsStatic, @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)), ret = (m.ReturnType.FullName ?? m.ReturnType.Name) });
                    }
                }
            }
            foreach (var iface in FindEspace(module).GetType().GetInterfaces())
            {
                foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var key = iface.Name + "." + m.Name + m.GetParameters().Length;
                    if (!seen.Add(key)) continue;
                    found.Add(new { type = iface.Name, method = m.Name, isStatic = false, @params = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)), ret = m.ReturnType.FullName ?? m.ReturnType.Name });
                }
            }
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return Json(new { ok = false, error = r.GetType().Name + ": " + r.Message, found = found }); }
        return Json(new { ok = true, found = found });
    }

        // v9: edit the AutomaticTest flow live (3 changes in one command):    //  1. assignment value "teste var here" -> "my new one"
    //  2. add output parameter "Id" of type LongInteger (es.LongIntegerType)
    //  3. add an Assign node at the end (before End) with assignment Id = 1
    // Uses IAction.Nodes / CreateOutputParameter / CreateNode<IAssignNode> / IAssignment.SetValue
    // / IParameter.DataType / IAssignNode.Target - all inside Command.ExecuteFromAsyncCode.
    static List<object> NodesOfType(object action, string ifaceShortName)
    {
        var result = new List<object>();
        var nodes = GetProp(action, "Nodes") as IEnumerable;
        if (nodes == null) return result;
        foreach (var n in nodes)
        {
            foreach (var i in n.GetType().GetInterfaces())
                if (i.Name == ifaceShortName) { result.Add(n); break; }
        }
        return result;
    }

    static object CreateNodeGeneric(object action, string ifaceFullName)
    {
        var nodeType = FindType(ifaceFullName);
        if (nodeType == null) throw new Exception("node type not found: " + ifaceFullName);
        // P5 GUARD: creating nodes on an EMPTY lifecycle-child flow (NREvents.*) deadlocks
        // the pipe (SS restart to recover). Refuse with a clear error instead of hanging.
        // Skeletonized (non-empty) lifecycle flows are allowed.
        try
        {
            object cur = action;
            for (int depth = 0; depth < 8 && cur != null; depth++)
            {
                string tn = cur.GetType().FullName ?? "";
                if (tn.Contains("NREvents"))
                {
                    int n = -1;
                    try { n = NodeList(action).Count; } catch { }
                    if (n == 0) throw new Exception("refusing node creation on the EMPTY lifecycle flow (" + tn + ") - it deadlocks the bridge. Manual in SS: add Start+End first, then retry (or use live_add_lifecycle_assign for defaults).");
                    break;
                }
                object next = null;
                try { next = GetProp(cur, "Parent"); } catch { break; }
                if (next == null || ReferenceEquals(next, cur)) break;
                cur = next;
            }
        }
        catch (Exception g) { if (g.Message.StartsWith("refusing node creation")) throw; }
        // Capture existing node count BEFORE creation so we can auto-position the new
        // node below existing ones (prevents all-new-nodes-stacked-at-0,0 syndrome).
        int existingCount = NodeList(action).Count;
        MethodInfo createNode = null;
        // Search interfaces FIRST - this is where IAction.CreateNode<T> lives (explicit impl).
        // Searching the concrete type first can find wrong overloads (e.g. Comment.CreateNode).
        foreach (var iface in action.GetType().GetInterfaces())
        {
            foreach (var m in iface.GetMethods())
            {
                if (m.Name == "CreateNode" && m.IsGenericMethod && m.GetParameters().Length == 2)
                {
                    createNode = m; break;
                }
            }
            if (createNode != null) break;
        }
        // Fallback: search concrete type hierarchy (some types may have public CreateNode)
        if (createNode == null)
        {
            for (var t = action.GetType(); t != null && createNode == null; t = t.BaseType)
            {
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Skip methods on Comment-like types - they have a public CreateNode(Name,Key)
                    // overload that creates Comment nodes, not the intended flow node type
                    if (m.Name == "CreateNode" && m.IsGenericMethod && m.GetParameters().Length == 2)
                    {
                        var declaringTypeName = m.DeclaringType?.Name ?? "";
                        if (declaringTypeName == "Comment" || declaringTypeName == "CommentNode") continue;
                        createNode = m; break;
                    }
                }
            }
        }
        if (createNode == null) throw new Exception("CreateNode<T> method not found");
        var result = createNode.MakeGenericMethod(nodeType).Invoke(action, new object[] { null, null });
        // Auto-position: assign a Y based on existing node count so new nodes don't stack
        // on top of each other at (0,0). ExceptionHandler nodes go to the right column
        // (X=12800); everything else to the left (X=3200). This is a rough heuristic �
        // call live_layout_flow afterwards for a proper topology-based layout.
        try
        {
            bool isHandler = ifaceFullName.Contains("ExceptionHandler");
            double x = isHandler ? 12800 : 3200;
            double y = 914 + existingCount * 2000;
            SetPropTyped(result, "X", x);
            SetPropTyped(result, "Y", y);
        }
        catch { } // non-fatal � position is cosmetic
        Log("CreateNodeGeneric(" + ifaceFullName + ") => " + (result?.GetType().Name ?? "null"));
        return result;
    }

    static string TypeLabel(object t)
    {
        if (t == null) return "?";
        var x = GetProp(t, "XifType") as string; if (!string.IsNullOrEmpty(x)) return x;
        var n = GetProp(t, "Name") as string; if (!string.IsNullOrEmpty(n)) return n;
        return t.GetType().Name;
    }

    // ---- Generic live flow-editing primitives (compose any edit from the MCP, no recompile) ----
    // Runs a mutation inside Command.ExecuteFromAsyncCode; mutate returns a report string.
    // If mutate throws, the command is rolled back via UndoManager.Uso that phantom nodes
    // (created before the exception) are removed from the live tree.
    static string LiveEdit(string moduleName, string actionName, string desc, Func<object, object, string> mutate)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        string report = null, err = null;
        Action body = () =>
        {
            try { report = mutate(action, es); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; report = (report ?? "") + "\nEXC: " + err; }
        };
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "exec not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: " + desc, body });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        // Roll back the command if mutate threw � prevents phantom nodes from partial mutations.
        if (err != null)
        {
            var undoErr = UndoLastCommand(agg);
            if (undoErr != null) report = (report ?? "") + "\nWARNING: rollback failed: " + undoErr;
            else report = (report ?? "") + "\nRolled back command (phantom nodes removed).";
        }
        return Json(new { ok = err == null, error = err, report = report });
    }

    // Undo the last command on an aggregator via UndoManager.Undo(aggregator).
    // Used by LiveEdit to roll back partial mutations when mutate throws.
    // Returns null on success, or an error message on failure.
    static string UndoLastCommand(object aggregator)
    {
        try
        {
            var umType = FindType("ServiceStudio.Undo.UndoManager");
            if (umType == null) return "UndoManager type not found";
            // Find Undo method that takes 1 parameter (the aggregator / IBaseTopLevelPresenter)
            // Find Undo(aggregator, int) - the 1-param overload doesn't exist (count has a default).
            MethodInfo undoMethod = null;
            foreach (var m in umType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Undo") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[1].ParameterType == typeof(int)) { undoMethod = m; break; }
            }
            if (undoMethod == null) return "Undo(aggregator,int) method not found on UndoManager";
            undoMethod.Invoke(null, new object[] { aggregator, 1 });
            return null; // success
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return r.GetType().Name + ": " + r.Message; }
    }

    static List<object> NodeList(object action)
    {
        var nodes = new List<object>();
        var ne = GetProp(action, "Nodes") as IEnumerable;
        if (ne != null) foreach (var n in ne) nodes.Add(n);
        return nodes;
    }

    static string ShortName(object n)
    {
        var ifaces = n.GetType().GetInterfaces();
        // Prefer specific node interfaces � check these first so we don't return
        // the generic IFlowNode/IActionNode/IProcessNode that every node implements.
        var preferred = new[] {
            "IStartNode", "IEndNode", "IAssignNode", "IExecuteServerActionNode",
            "IExceptionHandlerNode", "ICommentNode", "IIfNode", "ISwitchNode",
            "IForEachNode", "IBreakNode", "IExitNode", "IRaiseExceptionNode",
            "IStartNode", "IEndNode"
        };
        foreach (var name in preferred)
            foreach (var i in ifaces)
                if (i.Name == name) return i.Name;
        // Fallback: any I*Node, skipping generic base interfaces
        var skip = new HashSet<string> {
            "IFlowNode", "IActionNode", "IProcessNode", "IUIFlowNode",
            "IWebFlowNode", "IMobileFlowNode", "INode", "IFlowNodeSignature",
            "IProcessNodeSignature", "IUIFlowNodeSignature", "IWebFlowNodeSignature",
            "IMobileFlowNodeSignature"
        };
        foreach (var i in ifaces)
            if (i.Name.EndsWith("Node") && i.Name.StartsWith("I") && !skip.Contains(i.Name))
                return i.Name;
        return n.GetType().Name;
    }

    static string AssignmentText(object a)
    {
        var v = (GetProp(GetProp(a, "Variable"), "Text") as string) ?? "?";
        var val = (GetProp(GetProp(a, "Value"), "Text") as string) ?? "?";
        return v + " = " + val;
    }

    static object FindAssignNode(object action, string varContains, string valueContains)
    {
        foreach (var an in NodesOfType(action, "IAssignNode"))
        {
            var assigns = GetProp(an, "Assignments") as IEnumerable;
            if (assigns == null) continue;
            foreach (var a in assigns)
            {
                var vt = (GetProp(GetProp(a, "Variable"), "Text") as string) ?? "";
                var valt = (GetProp(GetProp(a, "Value"), "Text") as string) ?? "";
                if (vt.Contains(varContains) && valt.Contains(valueContains)) return an;
            }
        }
        return null;
    }

    static string DumpFlowGraph(object action)
    {
        var nodes = NodeList(action);
        var sb = new StringBuilder();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var line = "[" + i + "] " + ShortName(n);
            if (n.GetType().GetInterfaces().Any(x => x.Name == "IAssignNode"))
            {
                var assigns = GetProp(n, "Assignments") as IEnumerable;
                int ai = 0;
                if (assigns != null) foreach (var a in assigns) { line += "  [" + ai + "] " + AssignmentText(a); ai++; }
            }
            var tgt = GetProp(n, "Target");
            if (tgt != null) { int ti = nodes.IndexOf(tgt); line += "  -> [" + (ti >= 0 ? ti.ToString() : "?") + "]"; }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    static string ListFlow(string moduleName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });
        return Json(new { ok = true, report = DumpFlowGraph(action) });
    }

    // Layout constants � derived from the user's manually-positioned DietCreate_BL_FIXED
    // reference: main flow at X=3200, exception flow at X=12800, Y starts at 914 and
    // increments ~2000 per node going downward.
        const double LAYOUT_MAIN_X = 3200;
        const double LAYOUT_EXC_X = 12800;
        const double LAYOUT_IF_TRUE_X = 6400;
        const double LAYOUT_IF_FALSE_X = 9600;
        const double LAYOUT_SWITCH_OTHERWISE_X = 12800;
    const double LAYOUT_Y_START = 914;
    const double LAYOUT_Y_STEP = 2000;

    // layout_flow: analyze the flow topology and auto-position every node so they don't
    // stack on top of each other. Main flow (Start ? ... ? End) goes in the left column;
    // exception flows (ErrorHandler ? ... ? End) go in the right column. Call this AFTER
    // the flow is fully built and linked. Undo unit (Ctrl+Z).
    static string LayoutFlow(string moduleName, string actionName)
    {
        return LiveEdit(moduleName, actionName, "layout flow", (act, es) =>
        {
            var nodes = NodeList(act);
            var sb = new StringBuilder();
            var visited = new HashSet<object>();

            // --- Main flow: traverse from Start following Target links ---
            var mainFlow = new List<object>();
            object start = null;
            foreach (var n in nodes)
                if (n.GetType().GetInterfaces().Any(i => i.Name == "IStartNode")) { start = n; break; }
            if (start != null)
            {
                var current = start;
                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);
                    mainFlow.Add(current);
                    current = GetProp(current, "Target");
                }
            }
            for (int i = 0; i < mainFlow.Count; i++)
            {
                double y = LAYOUT_Y_START + i * LAYOUT_Y_STEP;
                SetPropTyped(mainFlow[i], "X", LAYOUT_MAIN_X);
                SetPropTyped(mainFlow[i], "Y", y);
                sb.AppendLine("main[" + i + "] " + ShortName(mainFlow[i]) + "  ->  X=" + LAYOUT_MAIN_X + " Y=" + y);
            }

            // --- Exception flows: each ExceptionHandler starts a right-column branch ---
            int excYield = 0;
            foreach (var n in nodes)
            {
                if (visited.Contains(n)) continue;
                if (!n.GetType().GetInterfaces().Any(i => i.Name == "IExceptionHandlerNode")) continue;
                var excFlow = new List<object>();
                var current = n;
                while (current != null && !visited.Contains(current))
                {
                    visited.Add(current);
                    excFlow.Add(current);
                    current = GetProp(current, "Target");
                }
                for (int i = 0; i < excFlow.Count; i++)
                {
                    double y = LAYOUT_Y_START + (excYield + i) * LAYOUT_Y_STEP;
                    SetPropTyped(excFlow[i], "X", LAYOUT_EXC_X);
                    SetPropTyped(excFlow[i], "Y", y);
                    sb.AppendLine("exc[" + excYield + "+" + i + "] " + ShortName(excFlow[i]) + "  ->  X=" + LAYOUT_EXC_X + " Y=" + y);
                }
                excYield += excFlow.Count;
            }

            // --- Decision branches: If (TrueTarget/FalseTarget) and Switch (OtherwiseTarget) ---
            // Each branch is laid out as its own column (True -> left of otherwise, etc.).
            var branchColumns = new Dictionary<string, double>
            {
                ["TrueTarget"] = LAYOUT_IF_TRUE_X,
                ["FalseTarget"] = LAYOUT_IF_FALSE_X,
                ["OtherwiseTarget"] = LAYOUT_SWITCH_OTHERWISE_X
            };
            foreach (var n in nodes)
            {
                if (!n.GetType().GetInterfaces().Any(i => i.Name == "IIfNode" || i.Name == "ISwitchNode")) continue;
                foreach (var kv in branchColumns)
                {
                    object branchStart = null;
                    try { branchStart = GetProp(n, kv.Key); } catch { }
                    if (branchStart == null || visited.Contains(branchStart)) continue;
                    var branch = new List<object>();
                    var cur = branchStart;
                    while (cur != null && !visited.Contains(cur))
                    {
                        visited.Add(cur);
                        branch.Add(cur);
                        cur = GetProp(cur, "Target");
                    }
                    for (int i = 0; i < branch.Count; i++)
                    {
                        double y = LAYOUT_Y_START + i * LAYOUT_Y_STEP;
                        SetPropTyped(branch[i], "X", kv.Value);
                        SetPropTyped(branch[i], "Y", y);
                        sb.AppendLine("if/" + kv.Key + "[" + i + "] " + ShortName(branch[i]) + "  ->  X=" + kv.Value + " Y=" + y);
                    }
                }
            }

            // --- Unvisited non-Comment nodes: place below main flow (shouldn't normally happen) ---
            int extraIdx = 0;
            foreach (var n in nodes)
            {
                if (visited.Contains(n)) continue;
                if (n.GetType().GetInterfaces().Any(i => i.Name == "ICommentNode")) continue;
                double y = LAYOUT_Y_START + (mainFlow.Count + extraIdx) * LAYOUT_Y_STEP;
                SetPropTyped(n, "X", LAYOUT_MAIN_X);
                SetPropTyped(n, "Y", y);
                sb.AppendLine("extra[" + extraIdx + "] " + ShortName(n) + "  ->  X=" + LAYOUT_MAIN_X + " Y=" + y);
                extraIdx++;
            }

            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // get_node_positions: read-only diagnostic � returns X/Y for every node in the flow.
    // Use to verify a layout or inspect how SS positioned nodes.
    static string GetNodePositions(string moduleName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });
        var nodes = NodeList(action);
        var sb = new StringBuilder();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var x = GetProp(n, "X");
            var y = GetProp(n, "Y");
            sb.AppendLine("[" + i + "] " + ShortName(n) + "  X=" + (x ?? "?") + "  Y=" + (y ?? "?"));
        }
        return Json(new { ok = true, report = sb.ToString() });
    }

    // set_node_position: set X/Y on a single node by index. Handles numeric type
    // conversion (int/double) via SetPropTyped. Undo unit (Ctrl+Z).
    static string SetNodePosition(string moduleName, string actionName, int nodeIndex, double x, double y)
    {
        return LiveEdit(moduleName, actionName, "set node position", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            SetPropTyped(node, "X", x);
            SetPropTyped(node, "Y", y);
            return "set node[" + nodeIndex + "] (" + ShortName(node) + ") X=" + x + " Y=" + y;
        });
    }

    static string SetAssign(string moduleName, string actionName, string matchValue, string newValue, string matchVar)
    {
        if (string.IsNullOrEmpty(matchValue) || newValue == null) return Json(new { ok = false, error = "matchValue and newValue required" });
        return LiveEdit(moduleName, actionName, "set assign value", (act, es) =>
        {
            var sb = new StringBuilder(); bool found = false;
            foreach (var an in NodesOfType(act, "IAssignNode"))
            {
                var assigns = GetProp(an, "Assignments") as IEnumerable;
                if (assigns == null) continue;
                foreach (var a in assigns)
                {
                    var valText = (GetProp(GetProp(a, "Value"), "Text") as string) ?? "";
                    var varText = (GetProp(GetProp(a, "Variable"), "Text") as string) ?? "";
                    if (valText.Contains(matchValue) && (string.IsNullOrEmpty(matchVar) || varText.Contains(matchVar)))
                    {
                        string newText = newValue;
                        CallMethod(a, "SetValue", new object[] { newText }, 1);
                        sb.AppendLine("set: " + varText + " = " + valText + "  ->  " + newText);
                        found = true;
                    }
                }
            }
            if (!found) sb.AppendLine("NOT FOUND: matchValue='" + matchValue + "'");
            return sb.ToString();
        });
    }

    static string AddOutput(string moduleName, string actionName, string name, string type)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        return LiveEdit(moduleName, actionName, "add output param", (act, es) =>
        {
            var outP = CallMethod(act, "CreateOutputParameter", new object[] { name, null }, 2);
            SetProp(outP, "DataType", GetProp(es, type + "Type"));
            return "added output " + name + " : " + TypeLabel(GetProp(outP, "DataType"));
        });
    }

    // remove_input_param: remove an input parameter by name from a service/server/client action.
    // Finds the parameter in InputParameters by name and calls Delete on it. Useful when cloning
    // an action and replacing a parameter with a different type (e.g. removing DietId before
    // adding ProgressId). Undo unit (Ctrl+Z).
    static string RemoveInputParam(string moduleName, string actionName, string paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "paramName required" });
        return LiveEdit(moduleName, actionName, "remove input param", (act, es) =>
        {
            var inputs = GetProp(act, "InputParameters") as IEnumerable;
            if (inputs == null) throw new Exception("InputParameters collection is null");
            object toRemove = null;
            foreach (var p in inputs)
            {
                try { if ((GetProp(p, "Name") as string) == paramName) { toRemove = p; break; } } catch { }
            }
            if (toRemove == null) throw new Exception("input param not found: " + paramName);
            CallMethod(toRemove, "Delete", null, 0);
            return "removed input param: " + paramName;
        });
    }

    // remove_output_param: remove an output parameter by name from a service/server/client action.
    // Same pattern as RemoveInputParam but for OutputParameters.
    static string RemoveOutputParam(string moduleName, string actionName, string paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "paramName required" });
        return LiveEdit(moduleName, actionName, "remove output param", (act, es) =>
        {
            var outputs = GetProp(act, "OutputParameters") as IEnumerable;
            if (outputs == null) throw new Exception("OutputParameters collection is null");
            object toRemove = null;
            foreach (var p in outputs)
            {
                try { if ((GetProp(p, "Name") as string) == paramName) { toRemove = p; break; } } catch { }
            }
            if (toRemove == null) throw new Exception("output param not found: " + paramName);
            CallMethod(toRemove, "Delete", null, 0);
            return "removed output param: " + paramName;
        });
    }

    static string AddAssign(string moduleName, string actionName, string varName, string value, string where, string anchorVar, string anchorValue, int afterNodeIndex = -1)
    {
        if (string.IsNullOrEmpty(varName) || value == null) return Json(new { ok = false, error = "var and value required" });
        return LiveEdit(moduleName, actionName, "add assign node", (act, es) =>
        {
            var newAssign = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IAssignNode");
            var a = CallMethod(newAssign, "CreateAssignment", null, 0);
            CallMethod(a, "SetVariable", new object[] { varName }, 1);
            CallMethod(a, "SetValue", new object[] { value }, 1);
            var sb = new StringBuilder();
            if (where == "afterNode" && afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex + " (count=" + nodes.Count + ")");
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newAssign, "Target", anchorTarget);
                SetProp(anchor, "Target", newAssign);
                sb.AppendLine("inserted " + varName + "=" + value + " after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            else if (where == "afterAnchor")
            {
                var anchor = FindAssignNode(act, anchorVar ?? "", anchorValue ?? "");
                if (anchor == null) throw new Exception("anchor not found: " + anchorVar + "=" + anchorValue);
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newAssign, "Target", anchorTarget);
                SetProp(anchor, "Target", newAssign);
                sb.AppendLine("inserted " + varName + "=" + value + " after anchor (" + anchorVar + "=" + anchorValue + ")");
            }
            else // beforeEnd (first End node)
            {
                var nodes = NodeList(act);
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                {
                    foreach (var n in nodes)
                    {
                        var tgt = GetProp(n, "Target");
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                    }
                }
                // Auto-link Start?End if no node points to End yet (Issue 3 fix).
                if (endNode != null && prev == null)
                {
                    var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
                    if (startNode != null && GetProp(startNode, "Target") == null)
                    {
                        SetProp(startNode, "Target", endNode);
                        prev = startNode;
                        sb.AppendLine("auto-linked Start?End");
                    }
                }
                if (endNode == null || prev == null) throw new Exception("End/prev not found (endNode=" + (endNode != null) + " prev=" + (prev != null) + "). Create Start+End nodes and link them via live_set_node_target before using where='beforeEnd'.)");
                SetProp(newAssign, "Target", endNode);
                SetProp(prev, "Target", newAssign);
                sb.AppendLine("inserted " + varName + "=" + value + " before End");
            }
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    static string DelNode(string moduleName, string actionName, string matchVar, string matchValue)
    {
        if (string.IsNullOrEmpty(matchVar) || matchValue == null) return Json(new { ok = false, error = "matchVar and matchValue required" });
        return LiveEdit(moduleName, actionName, "delete node", (act, es) =>
        {
            var target = FindAssignNode(act, matchVar, matchValue);
            if (target == null) throw new Exception("node not found: " + matchVar + "=" + matchValue);
            var targetNext = GetProp(target, "Target");
            foreach (var n in NodeList(act))
            {
                var tgt = GetProp(n, "Target");
                if (tgt != null && object.ReferenceEquals(tgt, target)) { SetProp(n, "Target", targetNext); break; }
            }
            CallMethod(target, "Delete", null, 0);
            var sb = new StringBuilder();
            sb.AppendLine("deleted node " + matchVar + "=" + matchValue);
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // del_node_by_index: delete a node by its index in the NodeList (works for any node type)
    static string DelNodeByIndex(string moduleName, string actionName, int nodeIndex)
    {
        return LiveEdit(moduleName, actionName, "delete node by index", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex + " (count=" + nodes.Count + ")");
            var target = nodes[nodeIndex];
            var targetNext = GetProp(target, "Target");
            foreach (var n in NodeList(act))
            {
                var tgt = GetProp(n, "Target");
                if (tgt != null && object.ReferenceEquals(tgt, target)) { SetProp(n, "Target", targetNext); }
            }
            CallMethod(target, "Delete", null, 0);
            var sb = new StringBuilder();
            sb.AppendLine("deleted node[" + nodeIndex + "] type=" + target.GetType().Name);
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // set_node_target: set the Target property of a node at nodeIndex to the node at targetIndex
    static string SetNodeTarget(string moduleName, string actionName, int nodeIndex, int targetIndex)
    {
        return LiveEdit(moduleName, actionName, "set node target", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            if (targetIndex < 0 || targetIndex >= nodes.Count) throw new Exception("targetIndex out of range: " + targetIndex);
            var node = nodes[nodeIndex];
            var target = nodes[targetIndex];
            SetProp(node, "Target", target);
            return "set node[" + nodeIndex + "].Target = node[" + targetIndex + "] (" + node.GetType().Name + " -> " + target.GetType().Name + ")\n" + DumpFlowGraph(act);
        });
    }

    // set_node_prop: set any property on a node by name (string properties only)
    static string SetNodeProp(string moduleName, string actionName, int nodeIndex, string propName, string propValue)
    {
        return LiveEdit(moduleName, actionName, "set node prop", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            SetProp(node, propName, propValue);
            return "set node[" + nodeIndex + "]." + propName + " = " + propValue + "\n" + DumpFlowGraph(act);
        });
    }

    // add_assignment_to_node: add a new assignment (var=value) to an EXISTING IAssignNode
    // identified by its index in the NodeList. Unlike add_assign (which creates a NEW node
    // with one assignment), this operates on an existing node, enabling multi-assignment nodes
    // (e.g. Result.IsError=False AND Result.Message="Success" on a single Assign node).
    // Uses the same CreateAssignment -> SetVariable -> SetValue pattern as AddAssign.
    // The value is passed as-is to SetValue: text literals must include double quotes
    // (e.g. "\"Success\""), booleans/identifiers are bare (e.g. "False", "AllExceptions.ExceptionMessage").
    // Do NOT wrap the value in single quotes � pass "Success" (with double quotes), NOT '"Success"'.
    static string AddAssignmentToNode(string moduleName, string actionName, int nodeIndex, string varName, string value)
    {
        if (string.IsNullOrEmpty(varName) || value == null) return Json(new { ok = false, error = "var and value required" });
        return LiveEdit(moduleName, actionName, "add assignment to node", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex + " (count=" + nodes.Count + ")");
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IAssignNode"))
                throw new Exception("node[" + nodeIndex + "] is not an IAssignNode (type=" + ShortName(node) + ")");
            var a = CallMethod(node, "CreateAssignment", null, 0);
            CallMethod(a, "SetVariable", new object[] { varName }, 1);
            CallMethod(a, "SetValue", new object[] { value }, 1);
            return "added assignment " + varName + " = " + value + " to node[" + nodeIndex + "] (" + ShortName(node) + ")\n" + DumpFlowGraph(act);
        });
    }

    // remove_assignment: remove a single assignment (by variable name) from an EXISTING
    // IAssignNode identified by its index. Useful for cleaning up leftover dummy/placeholder
    // assignments (e.g. Temp=False) without deleting the entire node. The Assignments
    // collection is an IList; we find the matching assignment by Variable.Text and call Remove.
    static string RemoveAssignment(string moduleName, string actionName, int nodeIndex, string varName)
    {
        if (string.IsNullOrEmpty(varName)) return Json(new { ok = false, error = "varName required" });
        return LiveEdit(moduleName, actionName, "remove assignment", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex + " (count=" + nodes.Count + ")");
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IAssignNode"))
                throw new Exception("node[" + nodeIndex + "] is not an IAssignNode (type=" + ShortName(node) + ")");
            var assigns = GetProp(node, "Assignments") as IList;
            if (assigns == null) throw new Exception("Assignments collection is null or not IList on node[" + nodeIndex + "]");
            object toRemove = null;
            for (int i = 0; i < assigns.Count; i++)
            {
                var a = assigns[i];
                var vt = (GetProp(GetProp(a, "Variable"), "Text") as string) ?? "";
                if (vt == varName || vt.Contains(varName)) { toRemove = a; break; }
            }
            if (toRemove == null) throw new Exception("assignment not found on node[" + nodeIndex + "]: " + varName);
            assigns.Remove(toRemove);
            return "removed assignment " + varName + " from node[" + nodeIndex + "] (" + ShortName(node) + ")\n" + DumpFlowGraph(act);
        });
    }

    // set_node_prop_to_node: set a node-reference property (e.g. ExceptionHandler) on a node
    // at nodeIndex to the node at targetNodeIndex. Unlike set_node_prop (which sets STRING
    // properties via SetProp with a string value), this sets properties whose value is a node
    // reference — like Start.ExceptionHandler = ErrorHandler. Uses SetProp which searches
    // the type hierarchy + interfaces for a settable property by name.
    static string SetNodePropToNode(string moduleName, string actionName, int nodeIndex, string propName, int targetNodeIndex)
    {
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        return LiveEdit(moduleName, actionName, "set node prop to node", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            if (targetNodeIndex < 0 || targetNodeIndex >= nodes.Count) throw new Exception("targetNodeIndex out of range: " + targetNodeIndex);
            var node = nodes[nodeIndex];
            var target = nodes[targetNodeIndex];
            SetProp(node, propName, target);
            return "set node[" + nodeIndex + "](" + ShortName(node) + ")." + propName + " = node[" + targetNodeIndex + "](" + ShortName(target) + ")\n" + DumpFlowGraph(act);
        });
    }

    // set_prop_force: like set_node_prop_to_node but uses SetPropForce (aggressive reflection)
    // to set node-reference properties that are read-only via normal SetProp (e.g.
    // Start.ExceptionHandler). Tries NonPublic setters, backing fields, and Set<Name> methods.
    static string SetPropForceCmd(string moduleName, string actionName, int nodeIndex, string propName, int targetNodeIndex)
    {
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        return LiveEdit(moduleName, actionName, "set prop force", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            if (targetNodeIndex < 0 || targetNodeIndex >= nodes.Count) throw new Exception("targetNodeIndex out of range: " + targetNodeIndex);
            var node = nodes[nodeIndex];
            var target = nodes[targetNodeIndex];
            string result = SetPropForce(node, propName, target);
            return result + " | node[" + nodeIndex + "](" + ShortName(node) + ")." + propName + " = node[" + targetNodeIndex + "](" + ShortName(target) + ")\n" + DumpFlowGraph(act);
        });
    }

    // add_input: add an INPUT parameter to a service action. Supports basic types
    // (LongInteger, Integer, Text, Decimal, Boolean, DateTime, Date, Time, PhoneNumber,
    // Email, BinaryData, Currency, TextIdentifier, IntegerIdentifier, LongIntegerIdentifier)
    // and Structure types (exact name match on es.Structures, unique per module).
    static string AddInput(string moduleName, string actionName, string name, string type)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        return LiveEdit(moduleName, actionName, "add input param", (act, es) =>
        {
            // Idempotency check: prevent creating a duplicate input parameter with the same name.
            var existingInputs = GetProp(act, "InputParameters") as IEnumerable;
            if (existingInputs != null)
                foreach (var p in existingInputs)
                    try { if ((GetProp(p, "Name") as string) == name) throw new Exception("input param already exists: " + name); } catch (Exception ex) { if (ex.Message.Contains("already exists")) throw; }
            var inP = CallMethod(act, "CreateInputParameter", new object[] { name, null }, 2);
            object dataType = ResolveAttrDataType(es, type);
            if (dataType == null)
            {
                if (TryGetProp(es, type + "Type", out dataType) || TryGetProp(es, type, out dataType)) { }
                else
                {
                    var structures = GetProp(es, "Structures") as IEnumerable;
                    if (structures != null)
                        foreach (var s in structures)
                        {
                            try
                            {
                                var sname = GetProp(s, "Name") as string;
                                if (sname == type) { dataType = s; break; }
                            }
                            catch { }
                        }
                }
            }
            if (dataType != null) SetProp(inP, "DataType", dataType);
            return "added input " + name + " : " + TypeLabel(GetProp(inP, "DataType"));
        });
    }

    // add_local_variable: add a LOCAL VARIABLE to an action (service/server/client).
    // Supports basic types and Structure types (same resolution as AddInput).
    // Local variables are action-scoped (visible in the flow but not exposed as
    // input/output parameters). Uses IAction.CreateLocalVariable.
    static string AddLocalVariable(string moduleName, string actionName, string name, string type)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        return LiveEdit(moduleName, actionName, "add local variable", (act, es) =>
        {
            var lv = CallMethod(act, "CreateLocalVariable", new object[] { name, null }, 2);
            object dataType = ResolveAttrDataType(es, type);
            if (dataType == null)
            {
                if (TryGetProp(es, type + "Type", out dataType) || TryGetProp(es, type, out dataType)) { }
                else
                {
                    var structures = GetProp(es, "Structures") as IEnumerable;
                    if (structures != null)
                        foreach (var s in structures)
                        {
                            try
                            {
                                var sname = GetProp(s, "Name") as string;
                                if (sname == type) { dataType = s; break; }
                            }
                            catch { }
                        }
                }
            }
            if (dataType != null) SetProp(lv, "DataType", dataType);
            return "added local variable " + name + " : " + TypeLabel(GetProp(lv, "DataType"));
        });
    }

    // add_end_node: create a new End node in a service action's flow. Use cases:
    //   "beforeEnd"  - insert before the existing End node (existing flow end becomes the new node's Target)
    //   "atEnd"      - append after the last node in the flow
    //   "exceptionPath" - create a standalone End node for an exception handler branch (no auto-linking)
    static string AddEndNode(string moduleName, string actionName, string where)
    {
        if (string.IsNullOrEmpty(where)) return Json(new { ok = false, error = "where required (beforeEnd|atEnd|exceptionPath)" });
        return LiveEdit(moduleName, actionName, "add end node", (act, es) =>
        {
            var newEnd = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IEndNode");
            var sb = new StringBuilder();
            if (where == "beforeEnd")
            {
                var nodes = NodeList(act);
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                {
                    foreach (var n in nodes)
                    {
                        var tgt = GetProp(n, "Target");
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                    }
                }
                if (endNode == null || prev == null) throw new Exception("End/prev not found");
                SetProp(newEnd, "Target", endNode);
                SetProp(prev, "Target", newEnd);
                sb.AppendLine("inserted End before existing End");
            }
            else if (where == "atEnd")
            {
                var nodes = NodeList(act);
                object last = nodes.LastOrDefault();
                if (last == null) throw new Exception("no nodes in flow");
                SetProp(last, "Target", newEnd);
                sb.AppendLine("appended End after last node");
            }
            else if (where == "exceptionPath")
            {
                sb.AppendLine("created exception-path End node (wire ExceptionHandler manually)");
            }
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // set_exception_handler: wire an exception handler (catch node or other handler) to an
    // End node's ExceptionHandler property. Both nodes are found by assignment var+value
    // substrings on the End node's normal assignments (to disambiguate when multiple similar
    // End nodes exist). The handler node is found by type ITryNode or by assignment pattern.
    static string SetExceptionHandler(string moduleName, string actionName, string endVar, string endValue, string handlerVar, string handlerValue)
    {
        if (string.IsNullOrEmpty(endVar) || string.IsNullOrEmpty(endValue)) return Json(new { ok = false, error = "endVar and endValue required" });
        if (string.IsNullOrEmpty(handlerVar) || string.IsNullOrEmpty(handlerValue)) return Json(new { ok = false, error = "handlerVar and handlerValue required" });
        return LiveEdit(moduleName, actionName, "set exception handler", (act, es) =>
        {
            var endNode = FindAssignNode(act, endVar, endValue);
            if (endNode == null) throw new Exception("end node not found: " + endVar + "=" + endValue);
            object handlerNode = null;
            foreach (var n in NodesOfType(act, "IAssignNode"))
            {
                var assigns = GetProp(n, "Assignments") as IEnumerable;
                if (assigns == null) continue;
                foreach (var a in assigns)
                {
                    var vt = (GetProp(GetProp(a, "Variable"), "Text") as string) ?? "";
                    var valt = (GetProp(GetProp(a, "Value"), "Text") as string) ?? "";
                    if (vt.Contains(handlerVar) && valt.Contains(handlerValue)) { handlerNode = n; break; }
                }
                if (handlerNode != null) break;
            }
            if (handlerNode == null) throw new Exception("handler node not found: " + handlerVar + "=" + handlerValue);
            SetProp(endNode, "ExceptionHandler", handlerNode);
            return "wired ExceptionHandler: " + handlerVar + "=" + handlerValue + " -> end(" + endVar + "=" + endValue + ")\n" + DumpFlowGraph(act);
        });
    }

    // set_start_exception_handler: wire the exception handler branch to the Start node's
    // ExceptionHandler property. This is what makes exceptions from the normal flow route
    // to the catch/exception path. The handler node (catch/handler end) is found by matching
    // an assignment var+value on it (typically the catch End node itself, or a catch Assign
    // that feeds into it). Finds the IStartNode, sets its ExceptionHandler = handlerNode.
    static string SetStartExceptionHandler(string moduleName, string actionName, string handlerVar, string handlerValue)
    {
        if (string.IsNullOrEmpty(handlerVar) || string.IsNullOrEmpty(handlerValue)) return Json(new { ok = false, error = "handlerVar and handlerValue required" });
        return LiveEdit(moduleName, actionName, "set start exception handler", (act, es) =>
        {
            var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
            if (startNode == null) throw new Exception("Start node not found");
            object handlerNode = FindAssignNode(act, handlerVar, handlerValue);
            if (handlerNode == null) throw new Exception("handler node not found: " + handlerVar + "=" + handlerValue);
            SetProp(startNode, "ExceptionHandler", handlerNode);
            return "wired Start.ExceptionHandler: " + handlerVar + "=" + handlerValue + "\n" + DumpFlowGraph(act);
        });
    }

    // probe_node_types: enumerate all node interface types in an action's Nodes collection
    // (including types not yet used). Useful for discovering the exact interface name for
    // call-server-action nodes, try/catch nodes, etc. at runtime.
    static string ProbeNodeTypes(string moduleName, string actionName)
    {
        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(actionName))
            return Json(new { ok = false, error = "module and action required" });
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });

        var found = new Dictionary<string, bool>();
        var nodes = GetProp(action, "Nodes") as IEnumerable;
        if (nodes != null)
            foreach (var n in nodes)
                foreach (var i in n.GetType().GetInterfaces())
                    found[i.Name] = true;

        return Json(new { ok = true, module = moduleName, action = actionName,
            nodeInterfaces = found.Keys.OrderBy(k => k).ToList() });
    }

    // debug_create_node: test CreateNodeGeneric for a given node interface name.
    // Wraps in LiveEdit so CreateNode is called inside a command scope.
    static string DebugCreateNode(string moduleName, string actionName, string nodeInterface)
    {
        if (string.IsNullOrEmpty(nodeInterface)) return Json(new { ok = false, error = "nodeInterface required" });
        return LiveEdit(moduleName, actionName, "debug create node", (act, es) =>
        {
            object result = null;
            try { result = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes." + nodeInterface); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; throw new Exception(r.GetType().Name + ": " + r.Message); }
            return "created: " + (result?.GetType().Name ?? "null");
        });
    }

    // Set an expression-like value on a flow node defensively: tries common surfaces used by
    // decision nodes (If's condition etc.): SetValue(1-param), SetValue(2-param),
    // SetProp Cond/Condition/Value/ScalarValue/Expression. Returns how it was set, or "" if
    // none matched. Non-fatal for structure nodes (Switch sets per-case).
    static string SetNodeExpression(object node, string expr)
    {
        if (string.IsNullOrEmpty(expr)) return "";
        try { CallMethod(node, "SetCondition", new object[] { expr }, 1); return "SetCondition"; } catch { }
        string[] propCandidates = { "Cond", "Condition", "Value", "ScalarValue", "Expression" };
        try { CallMethod(node, "SetValue", new object[] { expr }, 1); return "SetValue"; } catch { }
        try { CallMethod(node, "SetValue", new object[] { expr, null }, 2); return "SetValue(2)"; } catch { }
        foreach (var pn in propCandidates)
        {
            try { if (GetProp(node, pn) != null) { SetProp(node, pn, expr); return "SetProp " + pn; } } catch { }
        }
        return "";
    }

    // probe_node_connectors: read-only � dump a flow node's "connector-like" settable props
    // (Target, FalseTarget, FalseConnector, DefaultTarget, etc.) with the type name and whether it
    // currently points at another node. Use before set_connector_target to find the exact property
    // name for the second branch of an If/Switch.
    static string ProbeNodeConnectors(string moduleName, string actionName, int nodeIndex)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });
        var nodes = NodeList(action);
        if (nodeIndex < 0 || nodeIndex >= nodes.Count) return Json(new { ok = false, error = "nodeIndex out of range: " + nodeIndex + " (count=" + nodes.Count + ")" });
        var n = nodes[nodeIndex];
        var props = new List<object>();
        foreach (var p in n.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (!p.Name.Contains("Target") && !p.Name.Contains("Connector") && !p.Name.Contains("Case") && p.Name != "Next") continue;
            object val = null; string type = "?";
            try { val = p.GetValue(n, null); type = p.PropertyType.Name; } catch { }
            bool isNode = val != null && val.GetType().GetInterfaces().Any(i => i.Name == "IFlowNode");
            props.Add(new { prop = p.Name, type = type, isNode = isNode, targetName = (isNode && val != null) ? (GetProp(val, "Name") ?? "?") : null });
        }
        return Json(new { ok = true, nodeIndex = nodeIndex, nodeType = n.GetType().Name, connectors = props });
    }

    // set_connector_target: set a NAMED target-ish property (e.g. "FalseTarget") on a node at
    // nodeIndex to point at nodes[targetIndex]. Use for an If's false branch / Switch's case
    // target, which live_set_node_target (only "Target") cannot reach.
    static string SetConnectorTarget(string moduleName, string actionName, int nodeIndex, string propName, int targetIndex)
    {
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        return LiveEdit(moduleName, actionName, "set connector target", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            if (targetIndex < 0 || targetIndex >= nodes.Count) throw new Exception("targetIndex out of range: " + targetIndex);
            var src = nodes[nodeIndex];
            var tgt = nodes[targetIndex];
            try { SetProp(src, propName, tgt); }
            catch (Exception e)
            {
                var r = e; while (r.InnerException != null) r = r.InnerException;
                throw new Exception("property '" + propName + "' not settable on " + src.GetType().Name + " (use probe_node_connectors to list them): " + r.Message);
            }
            return "set " + ShortName(src) + ".[" + nodeIndex + "]." + propName + " -> node[" + targetIndex + "] (" + ShortName(tgt) + ")";
        });
    }

    // probe_node_conditions: dump a Switch node's Conditions collection - each condition's
    // concrete type, settable properties, and current values. Read-only diagnostic. Use to
    // discover how to add/set switch cases (per-case value + target) before editing.
    static string ProbeNodeConditions(string moduleName, string actionName, int nodeIndex)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found: " + actionName });
        var nodes = NodeList(action);
        if (nodeIndex < 0 || nodeIndex >= nodes.Count) return Json(new { ok = false, error = "nodeIndex out of range: " + nodeIndex + " (count=" + nodes.Count + ")" });
        var n = nodes[nodeIndex];
        var conds = GetProp(n, "Conditions") as IEnumerable;
        var items = new List<object>();
        if (conds != null)
            foreach (var c in conds)
            {
                if (c == null) { items.Add(new { type = "null", props = new List<object>() }); continue; }
                var props = new List<object>();
                try
                {
                    foreach (var t in AllTypes(c.GetType()))
                    {
                        System.Reflection.PropertyInfo[] plist = null;
                        try { plist = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); } catch { plist = null; }
                        if (plist == null) continue;
                        foreach (var p in plist)
                        {
                            object val = null; string vstr = "?";
                            try { val = p.GetValue(c, null); if (val != null) vstr = val.ToString(); } catch { }
                            props.Add(new { name = p.Name, type = p.PropertyType.Name, value = vstr.Length > 120 ? vstr.Substring(0, 120) : vstr });
                        }
                    }
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; props.Add(new { name = "PROBE_ERR", type = r.GetType().Name, value = r.Message }); }
                var methods = new List<string>();
                foreach (var t in AllTypes(c.GetType()))
                {
                    System.Reflection.MethodInfo[] mlist = null;
                    try { mlist = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); } catch { mlist = null; }
                    if (mlist == null) continue;
                    foreach (var m in mlist)
                        if (!m.IsSpecialName && (m.Name.Contains("Value") || m.Name.Contains("Expression") || m.Name.Contains("Condition") || m.Name.Contains("Set")))
                            try { methods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")"); } catch { }
                }
                items.Add(new { type = c.GetType().FullName, methods = methods.Distinct().ToList(), props = props });
            }
        return Json(new { ok = true, nodeType = n.GetType().Name, conditionsCount = items.Count, conditions = items });
    }

    // set_switch_case: add or update a Switch condition. If a case whose Value/expression text
    // matches existing is found, update its Target; otherwise create a new condition via the
    // Conditions collection's Create/Add factory (hunted reflectively) or the node's
    // CreateCondition/CreateCase method. value is the case expression (e.g. "Admin"), targetIndex
    // the node to route to. Otherwise branch is set separately with set_connector_target.
    static string SetSwitchCase(string moduleName, string actionName, int nodeIndex, string value, int targetIndex)
    {
        return LiveEdit(moduleName, actionName, "set switch case", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            if (targetIndex < 0 || targetIndex >= nodes.Count) throw new Exception("targetIndex out of range: " + targetIndex);
            var n = nodes[nodeIndex];
            var tgt = nodes[targetIndex];
            var conds = GetProp(n, "Conditions") as IEnumerable;
            var sb = new StringBuilder();
            object found = null; object firstEmpty = null;
            if (conds != null)
                foreach (var c in conds)
                {
                    var valText = GetProp(c, "Value")?.ToString() ?? GetProp(c, "Condition")?.ToString() ?? "";
                    var valExpr = GetProp(c, "Value");
                    if (valExpr != null) try { valText = GetProp(valExpr, "Text") as string ?? valText; } catch { }
                    sb.AppendLine("existing case: " + valText);
                    if (!string.IsNullOrEmpty(value) && valText.Trim() == value.Trim()) found = c;
                    if (string.IsNullOrWhiteSpace(valText) && firstEmpty == null) firstEmpty = c;
                }
            if (found == null && firstEmpty != null) { found = firstEmpty; sb.AppendLine("reusing first empty case"); }
            if (found == null)
            {
                var created = false;
                foreach (var t in AllTypes(n.GetType()))
                {
                    var m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(mm => mm.Name == "CreateCondition" || mm.Name == "CreateCase" || mm.Name == "AddCondition" || mm.Name == "AddCase").FirstOrDefault();
                    if (m != null)
                    {
                        try
                        {
                            var ps = m.GetParameters();
                            var args = new object[ps.Length];
                            if (ps.Length >= 1 && ps[0].ParameterType == typeof(string)) args[0] = value;
                            found = m.Invoke(n, args); sb.AppendLine("created via " + m.Name + "(" + string.Join(",", ps.Select(p => p.ParameterType.Name)) + ")"); created = true;
                        }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  " + m.Name + " err: " + r.Message); }
                        if (created) break;
                    }
                }
                if (!created && conds != null)
                {
                    foreach (var t in AllTypes(conds.GetType()))
                    {
                        var m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(mm => mm.Name == "Add" || mm.Name == "Create" || mm.Name == "AddNew").FirstOrDefault();
                        if (m != null)
                        {
                            try
                            {
                                var ps = m.GetParameters();
                                var args = new object[ps.Length];
                                found = m.Invoke(conds, args); sb.AppendLine("created via Conditions." + m.Name + "(" + string.Join(",", ps.Select(p => p.ParameterType.Name)) + ")"); created = true;
                            }
                            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  Conditions." + m.Name + " err: " + r.Message); }
                            if (created) break;
                        }
                    }
                }
                if (found == null && !created) throw new Exception("could not create switch condition (see report)");
            }
            else sb.AppendLine("reusing existing case");
            bool valueSet = false;
            foreach (var pn in new[] { "Value", "Condition", "Expression", "ConditionExpression", "ValueExpression" })
            {
                var vExpr = GetProp(found, pn);
                if (vExpr == null) continue;
                try
                {
                    var txtProp = vExpr.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                    if (txtProp != null && txtProp.CanWrite) { txtProp.SetValue(vExpr, value); sb.AppendLine("set " + pn + ".Text=\"" + value + "\""); valueSet = true; break; }
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  " + pn + ".Text err: " + r.Message); }
                try { CallMethod(vExpr, "SetText", new object[] { value }, 1); sb.AppendLine("set " + pn + ".SetText(\"" + value + "\")"); valueSet = true; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  " + pn + ".SetText err: " + r.Message); }
            }
            if (!valueSet)
            {
                // direct SetValue on the condition itself
                try { CallMethod(found, "SetValue", new object[] { value }, 1); sb.AppendLine("condition.SetValue(\"" + value + "\") OK"); valueSet = true; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  condition.SetValue err: " + r.Message); }
            }
            if (!valueSet) sb.AppendLine("WARN: could not set condition value text");
            bool targetSet = false;
            if (targetIndex >= 0)
            {
                try { SetProp(found, "Target", tgt); sb.AppendLine("set case Target -> node[" + targetIndex + "]"); targetSet = true; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("case Target err: " + r.Message); }
                if (!targetSet)
                {
                    foreach (var pn in new[] { "Node", "Target", "CaseTarget", "Destination", "ConnectorTarget" })
                    {
                        try { var p = found.GetType().GetProperty(pn, BindingFlags.Public | BindingFlags.Instance); if (p != null && p.CanWrite) { p.SetValue(found, tgt); sb.AppendLine("set " + pn + " -> node[" + targetIndex + "]"); targetSet = true; break; } } catch { }
                    }
                }
            }
            else sb.AppendLine("target skipped (targetIndex < 0 - value-only)");
            if (!targetSet && targetIndex >= 0) sb.AppendLine("WARN: could not set case target");
            return sb.ToString();
        });
    }

    // add_if_node: create an IIfNode decision node in an action flow, set its condition
    // expression (defensively across common surfaces), and optionally insert it after node[afterNodeIndex]
    // (its previous target is chained; the If node keeps the anchor's old target on its true "Target",
    // the false branch is left unwired � use set_connector_target with FalseTarget/FalseConnector).
    static string AddIfNode(string moduleName, string actionName, string condition, int afterNodeIndex = -1)
    {
        return LiveEdit(moduleName, actionName, "add if node", (act, es) =>
        {
            var ifNode = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IIfNode");
            var viaExpr = SetNodeExpression(ifNode, condition);
            var idx = NodeList(act).IndexOf(ifNode);
            var sb = new StringBuilder("created If node [" + idx + "] (" + ifNode.GetType().Name + ")");
            if (!string.IsNullOrEmpty(condition)) sb.Append(" condition via " + viaExpr);
            if (afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                if (anchorTarget != null)
                {
                    try { SetProp(ifNode, "TrueTarget", anchorTarget); } catch { try { SetProp(ifNode, "Target", anchorTarget); } catch { } }
                }
                try { SetProp(anchor, "Target", ifNode); } catch { }
                sb.Append(" | inserted after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            sb.AppendLine();
            sb.AppendLine("NOTE: if the true/false branch props are not 'Target'/'FalseTarget', run probe_node_connectors and wire with set_connector_target.");
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // add_switch_node: create an ISwitchNode in an action flow. Switch conditions live per-case
    // (not settable via a single expression), so this only creates + optionally inserts it at node[afterNodeIndex].
    static string AddSwitchNode(string moduleName, string actionName, int afterNodeIndex = -1)
    {
        return LiveEdit(moduleName, actionName, "add switch node", (act, es) =>
        {
            var sw = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.ISwitchNode");
            var idx = NodeList(act).IndexOf(sw);
            var sb = new StringBuilder("created Switch node [" + idx + "] (" + sw.GetType().Name + ")");
            if (afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                try { if (anchorTarget != null) SetProp(sw, "Target", anchorTarget); } catch { }
                try { SetProp(anchor, "Target", sw); } catch { }
                sb.Append(" | inserted after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            sb.AppendLine();
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // ---- Phase 3: web-block Events + lifecycle handlers (probe first, then create) ----

    // probe_block_members: read-only � dump a web block's properties AND its collections
    // (InputParameters, Variables, Events, ClientActions, Widgets, Placeholders) with counts and
    // first-item concrete types. Use to discover the Events surface and lifecycle-handler
    // property names before add_event_to_block / set_block_handler.
    static string ProbeBlockMembers(string module, string block)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var props = new List<object>();
        var collections = new List<object>();
        foreach (var t in AllTypes(blk.GetType()))
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    object v = null; try { v = p.GetValue(blk, null); } catch { }
                    bool isEnum = v is IEnumerable && !(v is string);
                    if (isEnum)
                    {
                        int cnt = 0; string first = null;
                        foreach (var it in (IEnumerable)v) { if (first == null) first = it?.GetType().Name; cnt++; }
                        collections.Add(new { prop = p.Name, type = p.PropertyType.Name, count = cnt, firstItem = first });
                    }
                    else
                    {
                        props.Add(new { prop = p.Name, type = p.PropertyType.Name, writable = p.CanWrite, value = (v?.GetType().Name ?? "null") });
                    }
                }
                catch { }
            }
        }
        return Json(new { ok = true, block = block, blockType = blk.GetType().FullName, properties = props, collections = collections });
    }

    // add_event_to_block: create a custom EVENT on a web block (the block's Events collection) �
    // how a block signals data/state UP to parent screens. Factory-hunt pattern (like
    // AddVariableToBlock): Events.CreateEvent/Create/Add (2/1 arity), then block-level.
    static string AddEventToBlock(string module, string block, string name)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        return RunCmd(module, "add event to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var evts = GetProp(blk, "Events") as IEnumerable;
            if (evts != null)
                foreach (var v in evts)
                    try { if ((GetProp(v, "Name") as string) == name) throw new Exception("event already exists: " + name); } catch (Exception e) { if (e.Message.Contains("already exists")) throw; }
            var ms = ModelServices();
            if (ms == null) throw new Exception("ModelServices is null");
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null; var errs = new List<string>();
            if (evts != null)
            {
                foreach (var mName in new[] { "CreateEvent", "Create", "Add" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(evts, mName, pc);
                        if (m == null) continue;
                        try { created = m.Invoke(evts, pc == 2 ? new object[] { name, key } : new object[] { name }); via = "Events." + m.Name; break; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
            {
                foreach (var mName in new[] { "CreateEvent", "AddEvent" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(blk, mName, pc);
                        if (m == null) continue;
                        try { created = m.Invoke(blk, pc == 2 ? new object[] { name, key } : new object[] { name }); via = blk.GetType().Name + "." + m.Name; break; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
                throw new Exception("no event factory worked; block Events: " + (evts?.GetType().FullName ?? "null") + " | " + string.Join(" ; ", errs));
            return "created event '" + name + "' in block '" + block + "' via " + via + " (" + created.GetType().Name + ")";
        });
    }

    // add_event_param_to_block: add a payload parameter to an existing web-block Event (the
    // data the block passes to the parent when it raises the event). Factory hunt on the event's
    // OutputParameters/CreateEventParameter/etc. Sets the DataType (basic type or Structure).
    static string AddEventParamToBlock(string module, string block, string eventName, string name, string type)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(eventName)) return Json(new { ok = false, error = "event required" });
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        return RunCmd(module, "add event param to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var evts = GetProp(blk, "Events") as IEnumerable;
            object evt = null;
            if (evts != null)
                foreach (var v in evts)
                    try { if ((GetProp(v, "Name") as string) == eventName) { evt = v; break; } } catch { }
            if (evt == null) throw new Exception("event not found: " + eventName + " in block '" + block + "'");
            object created = null; string via = null; var errs = new List<string>();
            var outs = GetProp(evt, "OutputParameters") as IEnumerable;
            object dataType = null;
            if (TryGetProp(es, type + "Type", out dataType) || TryGetProp(es, type, out dataType)) { }
            else
            {
                var structures = GetProp(es, "Structures") as IEnumerable;
                if (structures != null)
                    foreach (var s in structures)
                        try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
            }
            // 1) create-then-add: some surfaces expose CreateOutputParameter() (0-arg) returning
            //    the parameter, then Add(object). The 1-arg Create* overloads take an
            //    AbstractOutputParameter object, NOT a name.
            foreach (var host in new object[] { evt, outs }.Where(h => h != null))
            {
                if (created != null) break;
                foreach (var mName in new[] { "CreateOutputParameter", "CreateParameter", "CreateInputParameter" })
                {
                    var m0 = FindMethod(host, mName, 0);
                    if (m0 == null) continue;
                    object p = null;
                    try { p = m0.Invoke(host, null); } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(host.GetType().Name + "." + mName + "() : " + r.Message); }
                    if (p == null) continue;
                    try { SetProp(p, "Name", name); } catch { }
                    if (dataType != null) { try { SetProp(p, "DataType", dataType); } catch { } }
                    try { CallMethod(host, "Add", new object[] { p }, 1); created = p; via = host.GetType().Name + "." + mName + "() + Add"; break; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(host.GetType().Name + "." + mName + "()+Add : " + r.Message); }
                }
            }
            if (created == null && outs != null)
            {
                foreach (var mName in new[] { "CreateOutputParameter", "CreateParameter", "CreateInputParameter" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(outs, mName, pc);
                        if (m == null) continue;
                        try { created = m.Invoke(outs, pc == 2 ? new object[] { name, null } : new object[] { name }); via = "Event.OutputParameters." + m.Name; break; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            }
            if (created == null)
                foreach (var mName in new[] { "CreateOutputParameter", "CreateParameter", "AddEventParameter", "CreateInputParameter" })
                {
                    foreach (var pc in new[] { 2, 1 })
                    {
                        var m = FindMethod(evt, mName, pc);
                        if (m == null) continue;
                        try { created = m.Invoke(evt, pc == 2 ? new object[] { name, null } : new object[] { name }); via = "Event." + m.Name; break; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(via + ": " + r.Message); }
                    }
                    if (created != null) break;
                }
            if (created == null)
                throw new Exception("no event-param factory worked on event '" + eventName + "': " + string.Join(" ; ", errs));
            if (dataType != null) { try { SetProp(created, "DataType", dataType); } catch { } }
            return "created event param '" + name + "' on event '" + eventName + "' in block '" + block + "' via " + via + (dataType != null ? " : " + TypeLabel(dataType) : "");
        });
    }

    // set_block_handler: assign a lifecycle/event-handler property of a web block
    // (OnInitialize / OnReady / OnRender / OnParametersChange / OnDestroy � exact prop name is
    // found case-insensitively, confirm with probe_block_members) to a Client Action. The action
    // is looked up on the block's ClientActions first, then the module. Undo unit.
    static string SetBlockHandler(string module, string block, string handler, string actionName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(handler)) return Json(new { ok = false, error = "handler required" });
        if (string.IsNullOrEmpty(actionName)) return Json(new { ok = false, error = "actionName required" });
        return RunCmd(module, "set block handler", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            string setVia = null; string valueType = null;
            foreach (var t in AllTypes(blk.GetType()))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.Name.Equals(handler, StringComparison.OrdinalIgnoreCase)) continue;
                    valueType = p.PropertyType.Name;
                    if (valueType.IndexOf("NREvents", StringComparison.Ordinal) >= 0 || valueType.StartsWith("On"))
                        return "handler '" + p.Name + "' on block '" + block + "' is a lifecycle child (" + valueType + ") that owns its own flow. Edit it with the flow tools by name (e.g. list_flow action='" + p.Name + "') � FindAction now resolves block lifecycle children.";
                    if (!p.CanWrite) throw new Exception("handler property '" + p.Name + "' is read-only on " + blk.GetType().Name);
                    setVia = p.Name; // real action-reference prop -> set below
                    break;
                }
                if (setVia != null) break;
            }
            if (setVia == null) throw new Exception("no property named '" + handler + "' found on block '" + block + "' (run probe_block_members)");
            var action = FindActionInCollection(blk, "ClientActions", actionName) ?? FindAction(es, actionName);
            if (action == null) throw new Exception("action not found: " + actionName);
            foreach (var t in AllTypes(blk.GetType()))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.Name.Equals(setVia, StringComparison.OrdinalIgnoreCase)) continue;
                    p.SetValue(blk, action, null);
                    break;
                }
            }
            return "set handler " + setVia + " of block '" + block + "' -> action '" + actionName + "'";
        });
    }

    // set_screen_handler: mirror of set_block_handler for SCREEN lifecycle handlers
    // (OnInitialize / OnReady / OnRender / OnDestroy). Reflection-verified on SS 11.55.83:
    // ServiceStudio.Model.NRNodes/WebScreen implements IMobileScreen, whose lifecycle props are
    // READ-ONLY IUILifeCycleEvent children (ServiceStudio.Model.NREvents.* objects owning their
    // own flow via Destination) - screens have NO assignable action-reference lifecycle prop.
    // So when the resolved handler is a lifecycle child we do NOT force an assignment (same
    // do-not-force rule as set_block_handler): instead we insert an ExecuteAction
    // (IExecuteClientActionNode) node INTO the lifecycle flow calling the named screen Client
    // Action (screen ClientActions first, then module actions). If a writable
    // action-reference property is ever found, it is assigned directly like set_block_handler.
    static string SetScreenHandler(string module, string screen, string handler, string actionName)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(handler)) return Json(new { ok = false, error = "handler required" });
        if (string.IsNullOrEmpty(actionName)) return Json(new { ok = false, error = "actionName required" });
        return RunCmd(module, "set screen handler", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            // Case-insensitive prop lookup: class hierarchy first (set_block_handler's scan),
            // then the interface map - screens expose the lifecycle props ONLY through explicit
            // IMobileScreen implementations (get-only), which the class scan cannot see.
            System.Reflection.PropertyInfo found = null; string foundVia = null;
            foreach (var t in AllTypes(sc.GetType()))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.Name.Equals(handler, StringComparison.OrdinalIgnoreCase)) continue;
                    found = p; foundVia = "class " + t.Name; break;
                }
                if (found != null) break;
            }
            if (found == null)
            {
                foreach (var iface in sc.GetType().GetInterfaces())
                {
                    foreach (var p in iface.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!p.Name.Equals(handler, StringComparison.OrdinalIgnoreCase)) continue;
                        found = p; foundVia = "interface " + iface.Name; break;
                    }
                    if (found != null) break;
                }
            }
            if (found == null) throw new Exception("no property named '" + handler + "' found on screen '" + screen + "'");
            var ptName = found.PropertyType.Name;
            var ptFull = found.PropertyType.FullName ?? ptName;
            bool isLifecycle = ptFull.IndexOf("NREvents", StringComparison.Ordinal) >= 0
                || ptName.StartsWith("On", StringComparison.Ordinal)
                || ptName.IndexOf("LifeCycleEvent", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isLifecycle && found.CanWrite)
            {
                // Writable action-reference property (the set_block_handler shape): assign.
                var action = FindActionInCollection(sc, "ClientActions", actionName) ?? FindAction(es, actionName);
                if (action == null) throw new Exception("action not found: " + actionName);
                if (!found.PropertyType.IsInstanceOfType(action))
                    throw new Exception("action '" + actionName + "' (" + action.GetType().Name + ") is not assignable to handler property '" + found.Name + "' (" + ptName + ")");
                found.SetValue(sc, action, null);
                return "set handler " + found.Name + " of screen '" + screen + "' -> action '" + actionName + "' (via " + foundVia + ")";
            }
            if (!isLifecycle) throw new Exception("handler property '" + found.Name + "' on screen '" + screen + "' is read-only (" + ptName + ") and not a lifecycle child - cannot assign");
            // Lifecycle child: report (do not force) + wire by inserting a call node into its flow.
            var lf = GetProp(sc, found.Name);
            if (lf == null) throw new Exception("lifecycle child '" + found.Name + "' not readable on screen '" + screen + "'");
            var flow = GetProp(lf, "Destination");
            if (flow == null) throw new Exception("lifecycle child '" + found.Name + "' on screen '" + screen + "' has no flow (Destination is null) - open the flow once in SS so it is created, then retry");
            if (NodeList(flow).Count == 0) throw new Exception("lifecycle flow '" + found.Name + "' on screen '" + screen + "' is EMPTY - node creation on an empty lifecycle flow deadlocks the bridge. Open the flow once in SS (Start/End skeleton) or seed it via add_lifecycle_assign, then retry");
            var act = FindActionInCollection(sc, "ClientActions", actionName) ?? FindAction(es, actionName);
            if (act == null) throw new Exception("action not found: " + actionName + " (screen ClientActions searched first, then module actions)");
            var iasType = FindType("OutSystems.Model.Logic.IActionSignature");
            if (iasType != null && !iasType.IsInstanceOfType(act))
                throw new Exception("action '" + actionName + "' (" + act.GetType().Name + ") does not implement IActionSignature - not callable from a flow node");
            // Resolve End/prev BEFORE creating anything so a wiring miss cannot leave a phantom node.
            var endNode = NodesOfType(flow, "IEndNode").FirstOrDefault();
            object prev = null;
            if (endNode != null)
                foreach (var n in NodeList(flow))
                {
                    var tgt = GetProp(n, "Target");
                    if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                }
            if (endNode == null || prev == null) throw new Exception("End/prev not found in lifecycle flow '" + found.Name + "' on screen '" + screen + "'");
            var node = CreateNodeGeneric(flow, "OutSystems.Model.Logic.Nodes.IExecuteClientActionNode");
            SetProp(node, "Action", act);
            SetProp(node, "Target", endNode);
            SetProp(prev, "Target", node);
            return "handler '" + found.Name + "' on screen '" + screen + "' is a lifecycle child (" + lf.GetType().Name + ") that owns its own flow - no reference assignment forced (screens have none). Inserted an ExecuteAction node calling '" + actionName + "' (" + act.GetType().Name + ") before End instead. The flow tools also address this flow by name (FindAction resolves screen lifecycle children).\n" + DumpFlowGraph(flow);
        });
    }

    // add_raise_event_node: create a Raise-Event node in an action/client-action flow and
    // (best-effort) bind it to an event by name on the containing block. Tries candidate node
    // interfaces (Render/Reactive RaiseEvent surfaces), then SetEvent/EventName/SetEventHandler.
    // Optional afterNodeIndex inserts it into the flow (chains prev.Target).
    static string AddRaiseEventNode(string moduleName, string actionName, string eventName, int afterNodeIndex)
    {
        return LiveEdit(moduleName, actionName, "add raise event node", (act, es) =>
        {
            object node = null; string via = null; var errs = new List<string>();
            foreach (var cand in new[] { "OutSystems.Model.Logic.Nodes.IRaiseEventNode", "OutSystems.Model.Logic.Nodes.ITriggerNode", "OutSystems.Model.Logic.Nodes.ISendEventNode", "ServiceStudio.Plugin.NRFlows.IRaiseEventNode" })
            {
                try { node = CreateNodeGeneric(act, cand); via = cand; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(cand + ": " + r.Message); }
            }
            if (node == null) throw new Exception("no RaiseEvent node interface found: " + string.Join(" ; ", errs));
            // CRASH FIX: the TriggerEvent node's concrete type decompiles with
            // AutoOpenOnCreateByDrop=true and DefaultPropertyForKind=EventPropertyDescriptor. An
            // UNBOUND TriggerEvent makes SS auto-open the "Select Event" modal on the UI thread,
            // which deadlocks the pipe command and freezes/crashes SS. The event property is typed
            // WebBlockCustomEvent (NOT string!) - we MUST resolve the block's custom event object
            // and assign it in the SAME command so SS never auto-opens the dialog.
            string bindVia = "";
            object srcBlock = null;
            // Find the web block that owns this action (its CustomEvents define the event scope).
            foreach (var flow in WebFlowsOf(es))
            {
                var nodes = GetProp(flow, "Nodes") as IEnumerable;
                if (nodes == null) continue;
                foreach (var n in nodes)
                {
                    if (IsScreen(n)) continue;
                    var cas = GetProp(n, "ClientActions") as IEnumerable;
                    bool owner = false;
                    if (cas != null)
                        foreach (var ca in cas)
                            try { if (object.ReferenceEquals(ca, act)) { owner = true; break; } } catch { }
                    if (!owner)
                        try { var das = GetProp(n, "DataActions") as IEnumerable; if (das != null) foreach (var da in das) { try { if (object.ReferenceEquals(da, act)) { owner = true; break; } } catch { } } } catch { }
                    if (owner) { srcBlock = n; break; }
                }
                if (srcBlock != null) break;
            }
            if (!string.IsNullOrEmpty(eventName))
            {
                object evt = null;
                var evts = srcBlock != null ? (GetProp(srcBlock, "CustomEvents") as IEnumerable ?? GetProp(srcBlock, "Events") as IEnumerable) : null;
                if (evts != null)
                    foreach (var v in evts)
                        try { if ((GetProp(v, "Name") as string) == eventName) { evt = v; break; } } catch { }
                if (evt == null)
                {
                    // Last chance: search the whole module's web-block custom events by name.
                    foreach (var flow in WebFlowsOf(es))
                    {
                        var nodes = GetProp(flow, "Nodes") as IEnumerable;
                        if (nodes == null) continue;
                        foreach (var n in nodes)
                        {
                            if (IsScreen(n)) continue;
                            var evts2 = GetProp(n, "CustomEvents") as IEnumerable;
                            if (evts2 == null) continue;
                            foreach (var v in evts2)
                                try { if ((GetProp(v, "Name") as string) == eventName) { evt = v; srcBlock = n; break; } } catch { }
                            if (evt != null) break;
                        }
                        if (evt != null) break;
                    }
                }
                if (evt != null)
                {
                    try { SetProp(node, "Event", evt); bindVia = " Event=" + eventName + " (typed WebBlockCustomEvent, block='" + (srcBlock != null ? (GetProp(srcBlock, "Name") as string ?? "?") : "?") + "')"; }
                    catch (Exception ee) { var r = ee; while (r.InnerException != null) r = r.InnerException; bindVia = " Event set FAILED: " + r.Message; }
                }
                else bindVia = " WARN: event '" + eventName + "' not found in any block's CustomEvents - node left unbound (may open dialog!)";
            }
            var sb = new StringBuilder();
            sb.Append("created RaiseEvent node (" + node.GetType().Name + ") via " + via + bindVia);
            if (afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                try { SetProp(node, "Target", anchorTarget); } catch { }
                SetProp(anchor, "Target", node);
                sb.Append(" | inserted after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            sb.AppendLine();
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // set_raise_event_arg: set the value of a RaiseEvent (TriggerEvent) node's payload argument.
    // The TriggerEvent holds ISSCollection<Argument> (same Argument type as ExecuteAction nodes);
    // when the Event is bound, each input parameter of the WebBlockCustomEvent gets a matching
    // Argument. We locate the Argument whose Parameter.Name == argName and SetValue(expr).
    static string SetRaiseEventArg(string moduleName, string actionName, int nodeIndex, string argName, string value)
    {
        return LiveEdit(moduleName, actionName, "set raise event arg", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "ITriggerNode"))
                throw new Exception("node[" + nodeIndex + "] is not ITriggerNode (type=" + ShortName(node) + ")");
            var args = GetProp(node, "Arguments") as IEnumerable;
            if (args == null) throw new Exception("Arguments collection is null on TriggerEvent node");
            var sb = new StringBuilder();
            int count = 0; object target = null;
            foreach (var arg in args)
            {
                count++;
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                sb.AppendLine("arg[" + (count - 1) + "] param=" + paramName);
                if (string.Equals(paramName, argName, StringComparison.OrdinalIgnoreCase)) target = arg;
            }
            if (target == null) throw new Exception("argument not found for param '" + argName + "' (" + count + " args present)");
            bool mapped = false;
            try { CallMethod(target, "SetValue", new object[] { value }, 1); sb.AppendLine("SetValue(\"" + value + "\") OK"); mapped = true; }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("SetValue err: " + r.Message); }
            if (!mapped) try { CallMethod(target, "SetVariable", new object[] { value }, 1); sb.AppendLine("SetVariable(\"" + value + "\") OK"); mapped = true; }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("SetVariable err: " + r.Message); }
            if (!mapped) try
            {
                var valExpr = GetProp(target, "Value");
                if (valExpr != null) { SetProp(valExpr, "Text", value); sb.AppendLine("Value.Text=\"" + value + "\" OK"); mapped = true; }
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("Value.Text err: " + r.Message); }
            if (!mapped) throw new Exception("could not set argument value for '" + argName + "'");
            return sb.ToString();
        });
    }

    // set_block_variable_type: set DataType (and optional default value) on an existing
    // web-block LocalVariable (block.Variables, created by add_variable_to_block). Mirrors
    // SetBlockInputParamType's type resolution but on variables: basic types via
    // es.<type>Type, Structures by unique name in es.Structures, entity records via
    // producerModule. The default value (default param) is pushed through the variable's
    // DefaultValue/Value SetValue-style surface if present.
    static string SetBlockVariableType(string module, string block, string var, string type, string defaultValue, string producerModule, bool identifier = false, string entityName = null)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(var)) return Json(new { ok = false, error = "var required" });
        if (string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "type required" });
        return RunCmd(module, "set block variable type", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var vars = GetProp(blk, "Variables") as IEnumerable ?? GetProp(blk, "LocalVariables") as IEnumerable;
            object v = null;
            if (vars != null)
                foreach (var it in vars)
                    try { if ((GetProp(it, "Name") as string) == var) { v = it; break; } } catch { }
            if (v == null) throw new Exception("variable not found: " + var + " in block '" + block + "'");
            object dataType = null;
            if (TryGetProp(es, type + "Type", out dataType) || TryGetProp(es, type, out dataType)) { }
            else
            {
                var structures = GetProp(es, "Structures") as IEnumerable;
                if (structures != null)
                    foreach (var s in structures)
                        try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
            }
            if (dataType == null)
            {
                // List types (e.g. "User Record List", "Attachment List") live in es.ListTypes.
                var listTypes = GetProp(es, "ListTypes") as IEnumerable;
                if (listTypes != null)
                    foreach (var lt in listTypes)
                        try { if ((GetProp(lt, "Name") as string) == type) { dataType = lt; break; } } catch { }
            }
            var entName = !string.IsNullOrEmpty(entityName) ? entityName : type;
            if (dataType == null && !string.IsNullOrEmpty(producerModule))
            {
                var entity = FindEntityInReferences(es, entName, producerModule);
                if (entity != null)
                    dataType = identifier ? GetProp(entity, "IdentifierType") ?? GetProp(entity, "Identifier") : entity;
            }
            if (dataType == null) throw new Exception("type not found: " + type + (identifier ? " (identifier of " + entName + ")" : ""));
            SetProp(v, "DataType", dataType);
            string defaultVia = "";
            if (defaultValue != null)
            {
                // Default values on block variables are expression text; text literals come
                // pre-quoted from the caller (e.g. "\"Viewer\""), booleans/numbers bare.
                try { CallMethod(v, "SetValue", new object[] { defaultValue }, 1); defaultVia = " SetValue(" + defaultValue + ")"; }
                catch { try { SetProp(v, "DefaultValue", defaultValue); defaultVia = " DefaultValue=" + defaultValue; } catch { try { SetProp(v, "Value", defaultValue); defaultVia = " Value=" + defaultValue; } catch { } } }
            }
            return "set variable '" + var + "' in block '" + block + "' DataType -> " + type + " (" + TypeLabel(dataType) + ")" + defaultVia;
        });
    }

    // set_block_aggregate_source: attach a source entity to an existing SCREEN AGGREGATE on a
    // web block's ScreenAggregates collection (mirror of SetAggregateSource on blocks).
    static string SetBlockAggregateSource(string module, string block, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(entityName)) return Json(new { ok = false, error = "entityName required" });
        return RunCmd(module, "set block aggregate source", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var aggs = GetProp(blk, "ScreenAggregates") as IEnumerable;
            object agg = null;
            if (aggs != null)
                foreach (var a in aggs)
                    try { if ((GetProp(a, "Name") as string) == name) { agg = a; break; } } catch { }
            if (agg == null) throw new Exception("aggregate not found: " + name + " in block '" + block + "'");
            object entity = null;
            // Platform entities (e.g. the platform "User") can be consumed from the producer
            // module; try the named producer first, then any reference exposing the entity.
            if (string.IsNullOrEmpty(producerModule))
            {
                var refs = GetProp(blk, "References") as IEnumerable ?? GetProp(es, "References") as IEnumerable;
                if (refs != null)
                    foreach (var r in refs)
                    {
                        var nm = GetProp(r, "Name") as string;
                        if (nm == null) continue;
                        entity = FindEntityInReferences(es, entityName, nm);
                        if (entity != null) { producerModule = nm; break; }
                    }
            }
            else entity = FindEntityInReferences(es, entityName, producerModule);
            // Fallback: local entity in the same module (e.g. ScratchOrder in FitnessManager).
            if (entity == null) entity = FindEntity(es, entityName);
            if (entity == null) throw new Exception("entity not found: " + entityName + (string.IsNullOrEmpty(producerModule) ? " (pass producerModule, consume it, or create it locally)" : " in " + producerModule + " nor locally"));
            string srcReport = SetAggregateSource(agg, entity);
            return "set source of aggregate '" + name + "' in block '" + block + "' -> " + entityName + " (" + producerModule + "): " + srcReport;
        });
    }

    // set_block_aggregate_filter: add a filter expression to an existing SCREEN AGGREGATE on a
    // web block. The filter (e.g. "User.Id = TextToIdentifier(UserId)") is pushed through the
    // aggregate's filter/condition surface defensively (WebScreenDataSet.Filters/Condition).
    static string SetBlockAggregateFilter(string module, string block, string name, string filter)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(filter)) return Json(new { ok = false, error = "filter required" });
        return RunCmd(module, "set block aggregate filter", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var aggs = GetProp(blk, "ScreenAggregates") as IEnumerable;
            object agg = null;
            if (aggs != null)
                foreach (var a in aggs)
                    try { if ((GetProp(a, "Name") as string) == name) { agg = a; break; } } catch { }
            if (agg == null) throw new Exception("aggregate not found: " + name + " in block '" + block + "'");
            string via = null; var errs = new List<string>();
            // Path 1: Condition / Filter settable expression surface.
            foreach (var pn in new[] { "Filter", "Condition", "Where", "WhereCondition" })
            {
                try
                {
                    object cur = null; try { cur = GetProp(agg, pn); } catch { cur = null; }
                    if (cur != null) { SetNodeExpression(cur, filter); via = pn + " (SetNodeExpression)"; break; }
                }
                catch { errs.Add(pn); }
            }
            if (via == null)
            {
                // Path 2: Filters collection with an Add/Create filter factory that owns a condition.
                try
                {
                    var filters = GetProp(agg, "Filters") as IEnumerable;
                    if (filters != null)
                    {
                        foreach (var mName in new[] { "Add", "CreateFilter", "Create" })
                        {
                            var m = FindMethod(filters, mName, 1);
                            if (m == null) continue;
                            try { var f = m.Invoke(filters, new object[] { filter }); if (f != null) { via = "Filters." + mName + "(" + filter + ")"; break; } } catch { }
                        }
                        if (via == null) foreach (var mName in new[] { "AddFilter", "AddCondition", "AddWhere" }) { var m = FindMethod(agg, mName, 1); if (m != null) { try { var f = m.Invoke(agg, new object[] { filter }); if (f != null) { via = agg.GetType().Name + "." + mName; break; } } catch { } } }
                    }
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add("Filters: " + r.Message); }
            }
            if (via == null)
            {
                // Path 3 (PROVEN via probe_obj): the aggregate's DataTable exposes
                // AddFilter(String) / AddGroupByFilter(String) - the REAL surface SS uses to add a
                // screen-aggregate filter. Walk agg.Table -> AddFilter(condition).
                try
                {
                    var tbl = GetProp(agg, "Table");
                    if (tbl != null)
                    {
                        var m = FindMethod(tbl, "AddFilter", 1);
                        if (m != null)
                        {
                            m.Invoke(tbl, new object[] { filter });
                            via = "Table.AddFilter(" + filter + ")";
                        }
                        else errs.Add("Table.AddFilter(String) not found");
                    }
                    else errs.Add("aggregate.Table null");
                }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add("Table.AddFilter: " + r.Message); }
            }
            if (via == null) return "aggregate '" + name + "' found, but no filter surface succeeded | " + string.Join(" ; ", errs) + " (filter may need manual set)";
            return "set filter '" + filter + "' on aggregate '" + name + "' in block '" + block + "' via " + via;
        });
    }

    // add_if_widget_to_block: create an "If" widget with a condition inside a web block (or a
    // named container inside it). Uses the NRWidgets If family via CreateNRCustomWidgetOn with a
    // condition pushed through SetCondition/condition CustomProperty surfaces.
    static string AddIfWidgetToBlock(string module, string block, string parent, string name, string condition)
    {
        if (string.IsNullOrEmpty(block) || string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "block and name required" });
        return RunCmd(module, "add if widget to block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            object host = blk;
            if (!string.IsNullOrEmpty(parent))
            {
                object par = FindWidget(blk, parent);
                if (par == null) throw new Exception("parent widget not found: " + parent);
                host = par;
            }
            // If is a MODEL widget created via the parent's generic CreateWidget<T>(name,key) with
            // the Reactive IIfWidget interface (same pattern as ITextWidget for Text) - the
            // Kind-descriptor patterns (NRWidgets.If / NRWebWidgets+If+Kind) have no Instance
            // singleton on SS 11.55.83. Falls back to the Kind-descriptor attempts for other builds.
            object w = null; string via = "";
            try { w = CreateWidgetByKind(host, "if", name); via = "CreateWidget<IIfWidget>"; } catch (Exception e0) { via = "CreateWidget<IIfWidget> failed: " + e0.Message; }
            if (w == null)
            {
                try { w = CreateNRCustomWidgetOn(host, "ServiceStudio.Model.NRWebWidgets+If", name, null); via += " | model Kind"; }
                catch (Exception e1) { try { w = CreateNRCustomWidgetOn(host, "ServiceStudio.Plugin.NRWidgets.If", name, null); via += " | plugin Kind"; } catch { via += " | all factories failed"; } }
            }
            if (w == null) throw new Exception("If creation failed. " + via);
            string condVia = "";
            if (!string.IsNullOrEmpty(condition))
            {
                try { CallMethod(w, "SetCondition", new object[] { condition }, 1); condVia = "SetCondition"; }
                catch { try { SetProp(w, "Condition", condition); condVia = "Condition"; } catch { try { SetProp(w, "Value", condition); condVia = "Value"; } catch { } } }
            }
            return "created If widget '" + name + "' in block '" + block + "'" + (string.IsNullOrEmpty(parent) ? "" : " inside '" + parent + "'") + (condVia.Length > 0 ? " condition via " + condVia : "") + " (" + w.GetType().Name + ")";
        });
    }

    // move_widget_in_block: reparent a web-block widget (remove/rename approach: create is not
    // needed - we locate the widget and move it onto a new parent widget's content placeholder
    // collection, mirroring how add_* nests children into containers).
    static string MoveWidgetInBlock(string module, string block, string widget, string newParent)
    {
        if (string.IsNullOrEmpty(block) || string.IsNullOrEmpty(widget)) return Json(new { ok = false, error = "block and widget required" });
        return RunCmd(module, "move widget in block", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object newHost = null;
            if (!string.IsNullOrEmpty(newParent))
            {
                object par = FindWidget(blk, newParent);
                if (par == null) throw new Exception("new parent not found: " + newParent);
                newHost = par;
            }
            else newHost = blk;
            // Locate the widget in its current parent's Widgets/Placeholders and attempt a Move
            // (remove from old, add to new). If no Move api, fall back to reporting the widget
            // (the caller re-creates it under the desired parent via add_*_to_block).
            string via = null;
            try { CallMethod(w, "Move", new object[] { newHost }, 1); via = "Move(parent)"; }
            catch
            {
                try { CallMethod(w, "MoveTo", new object[] { newHost }, 1); via = "MoveTo(parent)"; }
                catch { via = null; }
            }
            if (via != null) return "moved widget '" + widget + "' in block '" + block + "' onto '" + (string.IsNullOrEmpty(newParent) ? "<block root>" : newParent) + "' via " + via + " (" + w.GetType().Name + ")";
            return "no Move api found for " + w.GetType().Name + " - widget '" + widget + "' is addressable; re-create under '" + newParent + "' with add_widget_to_block/add_nr_widget instead";
        });
    }

    // Resolve the destination host for move_widget on a SCREEN. Mirrors the parent-resolution
    // logic of AddNRWidget: If-branch syntax "IfName:True"/"IfName:False", dotted content
    // "Table.Row"/"Table.HeaderRow" (ResolveDottedContent), named placeholder
    // "LayoutName:MainContent"/"Widget:placeholderName", and the default 'content'
    // CustomPlaceholderWidget of containers. Also descends into a layout WebBlockInstance's
    // Instance.Placeholders (screens host layout fills there). Empty newParent = screen root.
    static object ResolveScreenWidgetHost(object screen, string newParent)
    {
        if (string.IsNullOrEmpty(newParent)) return screen;
        var colonIdx = newParent.LastIndexOf(':');
        var suffix = colonIdx > 0 ? newParent.Substring(colonIdx + 1) : null;
        var isBranchSyntax = colonIdx > 0 && (string.Equals(suffix, "True", StringComparison.OrdinalIgnoreCase) || string.Equals(suffix, "False", StringComparison.OrdinalIgnoreCase));
        if (isBranchSyntax)
        {
            // Children of an If live in its Branches[0]/[1] (IfBranch), not a content placeholder.
            var ifName = newParent.Substring(0, colonIdx);
            var ifWidget = FindWidgetDeep(screen, ifName);
            if (ifWidget == null) throw new Exception("If widget not found: " + ifName);
            var branches = GetProp(ifWidget, "Branches") as IEnumerable;
            if (branches == null) throw new Exception("widget '" + ifName + "' has no Branches collection");
            int idx = string.Equals(suffix, "True", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            object branch = null;
            int i = 0;
            foreach (var b in branches) { if (i == idx) { branch = b; break; } i++; }
            if (branch == null) throw new Exception("branch index " + idx + " not found on '" + ifName + "'");
            return branch;
        }
        var widgetPart = colonIdx > 0 ? newParent.Substring(0, colonIdx) : newParent;
        var placeholderPart = colonIdx > 0 ? suffix : null;
        // Dotted content syntax "Table.Row" / "Table.HeaderRow": Row/HeaderRow are IContent
        // (not FindWidget-addressable); resolve the content object as the host.
        var dotted = ResolveDottedContent(screen, newParent);
        if (dotted != null) return dotted;
        var par = FindWidgetDeep(screen, widgetPart);
        if (par == null) throw new Exception("new parent not found: " + widgetPart);
        IEnumerable GetPhColl()
        {
            IEnumerable phColl = null;
            try { phColl = GetProp(par, "Placeholders") as IEnumerable; } catch { }
            if (phColl == null)
            {
                // Layout WebBlockInstance widgets keep their placeholders on the Instance.
                object inst = null;
                try { inst = GetProp(par, "Instance"); } catch { }
                if (inst != null) { try { phColl = GetProp(inst, "Placeholders") as IEnumerable; } catch { } }
            }
            return phColl;
        }
        var phColl = GetPhColl();
        if (phColl != null)
        {
            // Named placeholder "Widget:placeholderName" (e.g. "LayoutName:MainContent").
            if (placeholderPart != null)
            {
                foreach (var ph in phColl)
                {
                    var phName = GetProp(ph, "Name") as string ?? "";
                    if (string.Equals(phName, placeholderPart, StringComparison.OrdinalIgnoreCase)) return ph;
                }
            }
            // A container's children live on its 'content' CustomPlaceholderWidget.
            foreach (var ph in phColl)
            {
                var phName = GetProp(ph, "Name") as string ?? "";
                if (string.Equals(phName, "content", StringComparison.OrdinalIgnoreCase)) return ph;
            }
        }
        return par;
    }

    // move_widget: reparent a widget on a SCREEN - LIVE tree, undo unit. Uses the widget's
    // ChangeParent method (the API AddNRWidget's fallback uses for IfBranch/Popup hosts).
    // newParent supports the AddNRWidget parent syntax: "" (screen root) | container name |
    // "IfName:True"/"IfName:False" | "LayoutName:MainContent" (named placeholder) |
    // "Table.Row" / "Table.HeaderRow" (dotted content hosts).
    static string MoveWidgetInScreen(string module, string screen, string widget, string newParent)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(widget)) return Json(new { ok = false, error = "screen and widget required" });
        return RunCmd(module, "move widget", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidgetDeep(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " on screen '" + screen + "'");
            var newHost = ResolveScreenWidgetHost(sc, newParent);
            CallMethod(w, "ChangeParent", new object[] { newHost }, 1);
            return "moved widget '" + widget + "' on screen '" + screen + "' onto '" + (string.IsNullOrEmpty(newParent) ? "<screen root>" : newParent) + "' via ChangeParent (" + w.GetType().Name + " -> " + newHost.GetType().Name + ")";
        });
    }

    // set_block_cp_expression: set ANY named CustomProperty on a web-block widget via
    // CustomProperty.SetValueExpression (the general form of the proven Style path at the top of
    // this file). Used to bind Variable/List/Labels/Values/Source etc. on Reactive widgets.
    static string SetBlockCpExpression(string module, string block, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set block cp expression", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (!(cps is IEnumerable coll)) return "no CustomProperties collection on " + w.GetType().Name;
            foreach (var cp in coll)
            {
                var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                if (pn != propName) continue;
                bool set = false; string diag = "";
                // ⚠ SetValueExpression(String) stores the string VERBATIM as a <Text> literal - it
                // does NOT treat quotes as expression syntax. Empirically proven: SetValueExpression(
                // "\"header\"") yields a Style class literally named "header" (quotes included),
                // while the working PageLinks container stores Value=app-menu-links (raw). So try
                // the RAW string first; only fall back to quoted if raw throws.
                var quoted = "\"" + value.Replace("\"", "\"\"") + "\"";
                foreach (var candidate in new[] { value, quoted })
                {
                    try { CallMethodTyped(cp, "SetValueExpression", new object[] { candidate }); set = true; break; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "SetValueExpression(" + candidate + "): " + r.GetType().Name + ": " + r.Message + " | "; }
                }
                if (!set) try { SetProp(cp, "Value", value); set = true; } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "SetProp(Value): " + r.Message + " | "; }
                if (!set) { var ve = GetProp(cp, "ValueExpression"); if (ve != null) { try { CallMethodTyped(ve, "SetValue", new object[] { value }); set = true; } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "ValueExpression.SetValue: " + r.Message + " | "; } if (!set) try { SetProp(ve, "Text", value); set = true; } catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "ValueExpression.Text: " + r.Message + " | "; } } }
                return "set CP '" + propName + "' = " + value + " on '" + widget + "' in block '" + block + "' (set=" + set + ")" + (set ? "" : " DIAG: " + diag);
            }
            return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";
        });
    }

    // set_block_cp_parsed: set a CustomProperty so the value is a PARSED expression (real
    // Identifier/Boolean/Number elements with references), NOT a single-Text literal. This is
    // the correct way to bind Variable/List/Labels/Values/Visible/Enabled/Mandatory/Source etc.
    // SetValueExpression(String) alone stores <Text Value="..."> (a string literal), which is
    // why bindings set through it fail validation ("Unknown variable in 'X'"). Strategies, in
    // order, each VERIFIED by re-reading the expression element:
    //   1. widget typed setter Set<PropName>(String)  (SetVariable/SetList/SetLabels/... - the
    //      plugin-generated setters the SS property grid uses)
    //   2. CP.SetPropertyValue("Value", value)        (model-level setter, converts strings)
    //   3. SetProp(cp, "Value", value)                (public string Value property setter)
    //   4. SetValueExpression (literal fallback - reported as such)
    // set_screen_cp_parsed: screen twin of SetBlockCpParsed (same 4 strategies +
    // CpLooksParsed verification). Binds screen widget CustomProperties as PARSED
    // expressions (Variable/Source/Visible...), not literals. Undo unit.
    static string SetScreenCpParsed(string module, string screen, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set screen cp parsed", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " on screen '" + screen + "'");
            object cp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var c in coll)
                {
                    var pn = GetProp(c, "PropertyName") as string ?? GetFieldStr(c, "_propertyName");
                    if (pn == propName) { cp = c; break; }
                }
            if (cp == null) return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";

            string diag = "";
            try
            {
                var m = FindMethod(w, "Set" + propName, 1);
                if (m != null && m.GetParameters()[0].ParameterType == typeof(string))
                {
                    m.Invoke(w, new object[] { value });
                    if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via Set" + propName + " (parsed OK)";
                    diag += "Set" + propName + " stored literal; ";
                }
                else diag += "no Set" + propName + "(String) method; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "Set" + propName + ": " + r.Message + "; "; }

            try
            {
                CallMethodTyped(cp, "SetPropertyValue", new object[] { "Value", value });
                if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via CP.SetPropertyValue (parsed OK)";
                diag += "CP.SetPropertyValue stored literal; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "CP.SetPropertyValue: " + r.Message + "; "; }

            try
            {
                SetProp(cp, "Value", value);
                if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via CP.Value setter (parsed OK)";
                diag += "CP.Value setter stored literal; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "CP.Value: " + r.Message + "; "; }

            try { CallMethodTyped(cp, "SetValueExpression", new object[] { value }); return "set CP '" + propName + "' = " + value + " on '" + widget + "' via SetValueExpression (LITERAL fallback) DIAG: " + diag; }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return "FAILED: " + r.Message + " DIAG: " + diag; }
        });
    }

    static string SetBlockCpParsed(string module, string block, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set block cp parsed", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object cp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var c in coll)
                {
                    var pn = GetProp(c, "PropertyName") as string ?? GetFieldStr(c, "_propertyName");
                    if (pn == propName) { cp = c; break; }
                }
            if (cp == null) return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";

            string diag = "";
            // Strategy 1: widget typed setter Set<PropName>(String).
            try
            {
                var m = FindMethod(w, "Set" + propName, 1);
                if (m != null && m.GetParameters()[0].ParameterType == typeof(string))
                {
                    m.Invoke(w, new object[] { value });
                    if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via Set" + propName + " (parsed OK)";
                    diag += "Set" + propName + " stored literal; ";
                }
                else diag += "no Set" + propName + "(String) method; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "Set" + propName + ": " + r.Message + "; "; }

            // Strategy 2: CP.SetPropertyValue("Value", value).
            try
            {
                CallMethodTyped(cp, "SetPropertyValue", new object[] { "Value", value });
                if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via CP.SetPropertyValue (parsed OK)";
                diag += "CP.SetPropertyValue stored literal; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "CP.SetPropertyValue: " + r.Message + "; "; }

            // Strategy 3: public string Value property setter.
            try
            {
                SetProp(cp, "Value", value);
                if (CpLooksParsed(cp, value)) return "set CP '" + propName + "' = " + value + " on '" + widget + "' via CP.Value setter (parsed OK)";
                diag += "CP.Value setter stored literal; ";
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; diag += "CP.Value: " + r.Message + "; "; }

            // Strategy 4: literal fallback so the property is at least deterministically set.
            try { CallMethodTyped(cp, "SetValueExpression", new object[] { value }); return "set CP '" + propName + "' = " + value + " on '" + widget + "' via SetValueExpression (LITERAL fallback) DIAG: " + diag; }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return "FAILED: " + r.Message + " DIAG: " + diag; }
        });
    }

    // CpLooksParsed: verify the CP's ValueExpression actually parsed. Reads the expressionElement
    // dump (same text probe_block_cp shows). Rules:
    //   quoted source ("...")  -> a Text element is CORRECT.
    //   True / False           -> a Boolean element is CORRECT.
    //   number                 -> a numeric (non-Text) element is CORRECT.
    //   otherwise              -> parsed = element carries [Reference:...] or its [Value:] differs
    //                             from the raw source (a bare Text equal to the raw source means
    //                             the string was stored as a literal, i.e. NOT parsed).
    static bool CpLooksParsed(object cp, string source)
    {
        try
        {
            var ve = GetProp(cp, "ValueExpression");
            if (ve == null) return false;
            var el = GetField(ve, "expressionElement");
            var s = el?.ToString() ?? "";
            var src = (source ?? "").Trim();
            bool isQuoted = src.StartsWith("\"") && src.EndsWith("\"") && src.Length >= 2;
            bool isBool = src == "True" || src == "False";
            double num; bool isNum = double.TryParse(src, out num);
            if (isQuoted) return s.Contains("[Type: Text]");
            if (isBool) return s.Contains("[Type: Boolean]");
            if (isNum) return s.Contains("[Type:") && !s.Contains("[Type: Text]");
            if (s.Contains("[Reference:")) return true;
            int i = s.IndexOf("[Value: ");
            var v = i >= 0 ? s.Substring(i + 8).TrimEnd(']').Trim() : null;
            bool textEl = s.Contains("[Type: Text]");
            if (textEl && v != null && v == src) return false; // raw source stored as a literal
            return !textEl || v == null;
        }
        catch { return false; }
    }

    // set_block_cp_value_attr: set a CustomProperty's plain Value ATTRIBUTE (e.g.
    // <CustomProperty PropertyName="Image" Value="Image:/Images.<key>">) instead of a
    // ValueExpression. This is how NRWidgets.Image stores a LOCAL image reference (proven by the
    // working AppLogo: PropertyName="Image" Value="Image:/Images.l+OY1Q2+WEeVPvSNAcnTiQ").
    // SetValueExpression on the Image CP produces a broken <Text Value="GetUserById..."> instead.
    // ⚠ CAUTION: the XML Value="Image:/Images.<key>" is the SERIALIZED (saved .oml) form. Writing
    // that STRING into the live _value field does NOT create a valid reference - SS's validator
    // then reports "Object data type required instead of 'Text'" / "'Value' must be set in Image".
    // For live image CPs use set_block_cp_image (passes the actual Image object).
    static string SetBlockCpValueAttr(string module, string block, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set block cp value attr", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (!(cps is IEnumerable coll)) return "no CustomProperties collection on " + w.GetType().Name;
            foreach (var cp in coll)
            {
                var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                if (pn != propName) continue;
                // Clear any ValueExpression first so the Value attribute is the source of truth.
                try
                {
                    var ve = GetProp(cp, "ValueExpression");
                    if (ve != null) { try { CallMethod(ve, "SetValue", new object[] { "" }, 1); } catch { } }
                }
                catch { }
                // Null out the _valueExpression backing field entirely (SS renders the Value attr
                // only when ValueExpression is truly absent - matches the working AppLogo OML where
                // Image CP has Value="Image:/Images..." and an EMPTY <ValueExpression>).
                try { SetField(cp, "_valueExpression", null); } catch { }
                // The XML Value="..." attribute serializes from the _value backing field (string).
                // The public Value property is typed AbstractObject on this model version, so a
                // plain string SetProp fails - write the backing field directly.
                if (SetField(cp, "_value", value))
                {
                    // Raw field writes bypass SS's change notifications - the widget's verify state
                    // (and SS's error list) stays stale until revalidation. Force it.
                    try { CallMethod(w, "ForceValidate", null, 0); } catch { }
                    try { CallMethod(w, "InvalidateSelfVerifyCache", null, 0); } catch { }
                    return "set CP '" + propName + "' Value-attr = '" + value + "' on '" + widget + "' (_value field, ok, revalidated)";
                }
                try { SetProp(cp, "Value", value); return "set CP '" + propName + "' Value-attr = '" + value + "' on '" + widget + "' (Value prop, ok)"; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return "set CP '" + propName + "' Value-attr failed: " + r.Message; }
            }
            return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";
        });
    }

    // set_block_cp_image: set a CustomProperty that expects an IMAGE reference (e.g. NRWidgets.Image
    // 'Image') with the ACTUAL Image object from es.Images - NOT a string. SS stores the reference
    // object (ToString() -> "Name (Image:<key>)") and validates it as an Image/Object type; writing a
    // string (SetValueExpression(String), the _value field, or "Image:/Images.<key>") yields "Object
    // data type required instead of 'Text'" + "'Value' must be set in Image". Strategies, each
    // VERIFIED by re-reading the _value field's runtime type (must be a non-String reference):
    //   1. CP.SetValueExpression(IImageSignature)  - the SS property-grid mechanism (exact overload)
    //   2. CP.SetPropertyValue("Value", img)        - model-level setter (takes System.Object)
    //   3. widget.SetPropertyValue(propName, img)   - widget-level setter
    static string SetBlockCpImage(string module, string block, string widget, string propName, string imageName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(imageName)) return Json(new { ok = false, error = "widget, propName and imageName required" });
        return RunCmd(module, "set block cp image", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object img = null; string imgLabel = "?";
            var imgs = GetProp(es, "Images") as IEnumerable;
            if (imgs != null)
                foreach (var i in imgs)
                {
                    string nm = null, key = null;
                    try { nm = GetProp(i, "Name") as string; } catch { }
                    try { key = GetProp(i, "Key") as string; } catch { }
                    if (nm == imageName || key == imageName) { img = i; imgLabel = nm ?? key; break; }
                }
            if (img == null) throw new Exception("image not found in module Images: " + imageName);
            object cp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var c in coll)
                {
                    var pn = GetProp(c, "PropertyName") as string ?? GetFieldStr(c, "_propertyName");
                    if (pn == propName) { cp = c; break; }
                }
            if (cp == null) return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";
            var results = new List<string>();
            // Strategy 1: CP.SetValueExpression(IImageSignature).
            try
            {
                CallMethodTyped(cp, "SetValueExpression", new object[] { img });
                if (CpValueIsImageRef(cp)) return SetBlockCpImageSuccess(cp, propName, imgLabel, widget, "CP.SetValueExpression(IImageSignature)");
                results.Add("SetValueExpression(img): _value still " + CpValueTypeName(cp));
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; results.Add("SetValueExpression(img) FAILED: " + r.Message); }
            // Strategy 2: CP.SetPropertyValue("Value", img).
            try
            {
                CallMethodTyped(cp, "SetPropertyValue", new object[] { "Value", img });
                if (CpValueIsImageRef(cp)) return SetBlockCpImageSuccess(cp, propName, imgLabel, widget, "CP.SetPropertyValue(Value,img)");
                results.Add("CP.SetPropertyValue: _value still " + CpValueTypeName(cp));
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; results.Add("CP.SetPropertyValue FAILED: " + r.Message); }
            // Strategy 3: widget.SetPropertyValue(propName, img).
            try
            {
                CallMethodTyped(w, "SetPropertyValue", new object[] { propName, img });
                if (CpValueIsImageRef(cp)) return SetBlockCpImageSuccess(cp, propName, imgLabel, widget, "widget.SetPropertyValue(" + propName + ",img)");
                results.Add("widget.SetPropertyValue: _value still " + CpValueTypeName(cp));
            }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; results.Add("widget.SetPropertyValue FAILED: " + r.Message); }
            return "FAILED to set image '" + imgLabel + "' on '" + widget + "' CP '" + propName + "'. " + string.Join(" | ", results);
        });
    }

    // Clear any stale ValueExpression on an image CP after _value holds the image reference.
    // SS-created Image CPs (e.g. the working AppLogo) have _value = Image object and NO
    // ValueExpression (null). A leftover ParsedExpression (e.g. [Type: Text]) on the CP makes the
    // validator report "'Object' data type required instead of 'Text'" even with a valid _value.
    static string SetBlockCpImageSuccess(object cp, string propName, string imgLabel, string widget, string via)
    {
        var notes = new List<string>();
        try
        {
            var ve = GetProp(cp, "ValueExpression");
            if (ve != null)
            {
                try { CallMethod(ve, "SetValue", new object[] { "" }, 1); notes.Add("VE.SetValue(\"\")"); } catch (Exception e2) { notes.Add("VE.SetValue failed: " + e2.Message); }
            }
        }
        catch (Exception e) { notes.Add("VE lookup failed: " + e.Message); }
        try { SetField(cp, "_valueExpression", null); notes.Add("_valueExpression=null"); } catch (Exception e) { notes.Add("null _valueExpression failed: " + e.Message); }
        // Re-read final state for verification.
        var veFinal = "null";
        try { veFinal = GetProp(cp, "ValueExpression")?.GetType().FullName ?? "null"; } catch { }
        return "set CP '" + propName + "' image '" + imgLabel + "' on '" + widget + "' via " + via +
               " [_value=" + CpValueTypeName(cp) + ", _valueExpression=" + veFinal + "]" +
               (notes.Count > 0 ? " (cleared: " + string.Join(", ", notes) + ")" : "");
    }

    static bool CpValueIsImageRef(object cp)
    {
        try
        {
            var v = GetField(cp, "_value");
            if (v == null) return false;
            var tn = v.GetType().FullName ?? "";
            if (tn.Contains("System.String")) return false;
            return tn.Contains("Image") || tn.Contains("Signature") || tn.Contains("Reference");
        }
        catch { return false; }
    }

    static string CpValueTypeName(object cp)
    {
        try { var v = GetField(cp, "_value"); return v == null ? "null" : v.GetType().FullName; }
        catch { return "?"; }
    }

    // set_block_cp_text: set a TEXT-LITERAL CustomProperty (e.g. NRWidgets Container 'Style'
    // classes) to a plain string, producing a Text element whose value is EXACTLY the string
    // (no quotes, no expression parsing) - matching SS-created widgets like PageLinks
    // (Style=app-menu-links).
    // Why not the direct setters: SetValueExpression / CP.SetPropertyValue both PARSE the string
    // as an expression - raw 'header' becomes an invalid identifier reference, and quoted
    // '"header"' stores a Text element whose value RETAINS the quote characters (both proven on
    // SS 11.55.83). So: set the quoted literal, then rewrite the element's string field(s) that
    // hold the verbatim quoted value to the bare value.
    static string SetBlockCpText(string module, string block, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set block cp text", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object cp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var c in coll)
                {
                    var pn = GetProp(c, "PropertyName") as string ?? GetFieldStr(c, "_propertyName");
                    if (pn == propName) { cp = c; break; }
                }
            if (cp == null) return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";
            var quoted = "\"" + value + "\"";
            CallMethodTyped(cp, "SetValueExpression", new object[] { quoted });
            // Surgical fix: the parsed Text element carries the quoted string verbatim; rewrite
            // every string field/property on the element holding the quoted value to the bare one.
            var fixes = new List<string>();
            try
            {
                var ve = GetProp(cp, "ValueExpression");
                var el = ve != null ? GetField(ve, "expressionElement") : null;
                if (el != null)
                {
                    foreach (var t in AllTypes(el.GetType()))
                    {
                        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (f.FieldType != typeof(string)) continue;
                            try { if ((string)f.GetValue(el) == quoted) { f.SetValue(el, value); fixes.Add(t.Name + "." + f.Name); } } catch { }
                        }
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (p.PropertyType != typeof(string) || !p.CanWrite) continue;
                            try { if ((string)p.GetValue(el, null) == quoted) { p.SetValue(el, value, null); fixes.Add(t.Name + "." + p.Name + "(prop)"); } } catch { }
                        }
                    }
                }
            }
            catch (Exception e) { fixes.Add("fix failed: " + e.Message); }
            // Verify: dump the resulting expression element + verify messages.
            string elementDump = "null"; string messages = "";
            try
            {
                var ve = GetProp(cp, "ValueExpression");
                if (ve != null)
                {
                    var el = GetField(ve, "expressionElement");
                    elementDump = el?.ToString() ?? "null";
                    var vc = GetField(ve, "verifyCache");
                    messages = vc?.ToString() ?? "";
                }
            }
            catch (Exception e) { elementDump = "dump failed: " + e.Message; }
            return "set CP '" + propName + "' = '" + value + "' on '" + widget + "'. Element: " + elementDump + " | Verify: " + messages + (fixes.Count > 0 ? " | fixed fields: " + string.Join(", ", fixes) : " | NO quoted-value fields found to fix");
        });
    }

    // set_screen_cp_text: SCREEN twin of set_block_cp_text. Sets a text-literal CustomProperty
    // (e.g. NRWidgets Container 'Style' classes) on a SCREEN widget with the BARE value (no
    // quotes, no expression parsing), then forces revalidation so SS's error list is not stale.
    static string SetScreenCpText(string module, string screen, string widget, string propName, string value)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(propName) || value == null) return Json(new { ok = false, error = "widget, propName and value required" });
        return RunCmd(module, "set screen cp text", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " on screen '" + screen + "'");
            object cp = null;
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (cps is IEnumerable coll)
                foreach (var c in coll)
                {
                    var pn = GetProp(c, "PropertyName") as string ?? GetFieldStr(c, "_propertyName");
                    if (pn == propName) { cp = c; break; }
                }
            if (cp == null) return "no CustomProperty named '" + propName + "' on " + w.GetType().Name + " '" + widget + "'";
            var quoted = "\"" + value + "\"";
            CallMethodTyped(cp, "SetValueExpression", new object[] { quoted });
            var fixes = new List<string>();
            try
            {
                var ve = GetProp(cp, "ValueExpression");
                var el = ve != null ? GetField(ve, "expressionElement") : null;
                if (el != null)
                {
                    foreach (var t in AllTypes(el.GetType()))
                    {
                        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (f.FieldType != typeof(string)) continue;
                            try { if ((string)f.GetValue(el) == quoted) { f.SetValue(el, value); fixes.Add(t.Name + "." + f.Name); } } catch { }
                        }
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (p.PropertyType != typeof(string) || !p.CanWrite) continue;
                            try { if ((string)p.GetValue(el, null) == quoted) { p.SetValue(el, value, null); fixes.Add(t.Name + "." + p.Name + "(prop)"); } } catch { }
                        }
                    }
                }
            }
            catch (Exception e) { fixes.Add("fix failed: " + e.Message); }
            // Raw backing-field writes bypass SS change notifications - clear the stale verification.
            try { CallMethod(w, "InvalidateSelfVerifyCache", null, 0); } catch { }
            try { CallMethod(w, "ForceValidate", null, 0); } catch { }
            string elementDump = "null"; string messages = "";
            try
            {
                var ve = GetProp(cp, "ValueExpression");
                if (ve != null)
                {
                    var el = GetField(ve, "expressionElement");
                    elementDump = el?.ToString() ?? "null";
                    var vc = GetField(ve, "verifyCache");
                    messages = vc?.ToString() ?? "";
                }
            }
            catch (Exception e) { elementDump = "dump failed: " + e.Message; }
            return "set CP '" + propName + "' = '" + value + "' on '" + widget + "'. Element: " + elementDump + " | Verify: " + messages + (fixes.Count > 0 ? " | fixed fields: " + string.Join(", ", fixes) : " | NO quoted-value fields found to fix");
        });
    }

    // probe_block_cp: dump a single CustomProperty's internals - its methods (full signatures),
    // the ValueExpression object's type/methods/fields, and the _value/_valueExpression fields.
    // Diagnostic: understand WHY SetValueExpression fails (IImageSignature overload confusion).
    static string ProbeBlockCp(string module, string block, string widget, string propName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(propName)) return Json(new { ok = false, error = "propName required" });
        return RunCmd(module, "probe block cp", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object cps = null;
            try { cps = GetProp(w, "CustomProperties"); } catch { }
            if (cps == null) { try { cps = GetField(w, "_customProperties"); } catch { } }
            if (!(cps is IEnumerable coll)) return Json(new { ok = false, error = "no CustomProperties on " + w.GetType().Name });
            foreach (var cp in coll)
            {
                var pn = GetProp(cp, "PropertyName") as string ?? GetFieldStr(cp, "_propertyName");
                if (pn != propName) continue;
                var cpMethods = new List<string>();
                foreach (var t in AllTypes(cp.GetType()))
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (!m.IsSpecialName && (m.Name.Contains("Value") || m.Name.Contains("Expression")))
                            cpMethods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")");
                var ve = GetProp(cp, "ValueExpression");
                var veInfo = new List<string>();
                string veType = ve?.GetType().FullName ?? "null";
                if (ve != null)
                {
                    foreach (var t in AllTypes(ve.GetType()))
                    {
                        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                            if (!m.IsSpecialName && (m.Name.Contains("Value") || m.Name.Contains("Text") || m.Name.Contains("Set")))
                                veInfo.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")");
                        foreach (var fld in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            veInfo.Add("field " + fld.Name + ":" + fld.FieldType.Name + "=" + (SafeGetFieldValue(fld, ve) ?? "?"));
                    }
                }
                return Json(new { ok = true, block = block, widget = widget, propName = propName,
                    cpType = cp.GetType().FullName, cpMethods = cpMethods,
                    valueExpressionType = veType, valueExpressionFields = veInfo,
                    _value = GetField(cp, "_value")?.ToString(), _valueType = GetField(cp, "_value")?.GetType().FullName, _valueExpression = GetField(cp, "_valueExpression")?.GetType().FullName });
            }
            return Json(new { ok = false, error = "no CustomProperty named '" + propName + "' on " + w.GetType().Name });
        });
    }

    static string SafeGetFieldValue(System.Reflection.FieldInfo f, object o)
    {
        try { var v = f.GetValue(o); return v == null ? "null" : v.ToString(); } catch { return "?"; }
    }

    // dump_flow_nodes: enumerate every web flow and its Nodes - names, concrete types, and
    // which are screens vs blocks. Diagnostic for "block not found" when FindBlock fails.
    static string DumpFlowNodes(string module)
    {
        if (string.IsNullOrEmpty(module)) return Json(new { ok = false, error = "module required" });
        return RunCmd(module, "dump flow nodes", es =>
        {
            var flows = new List<object>();
            foreach (var flow in WebFlowsOf(es))
            {
                var entry = new Dictionary<string, object>();
                entry["flow"] = GetProp(flow, "Name");
                var nodes = GetProp(flow, "Nodes") as IEnumerable;
                if (nodes == null) { entry["nodesProp"] = "null"; flows.Add(entry); continue; }
                var nodeList = new List<object>();
                foreach (var node in nodes)
                {
                    object nm = null; try { nm = GetProp(node, "Name"); } catch { }
                    nodeList.Add(new { name = nm?.ToString() ?? "?", type = node.GetType().Name, isScreen = IsScreen(node) });
                }
                entry["count"] = nodeList.Count;
                entry["nodes"] = nodeList;
                flows.Add(entry);
            }
            return Json(new { ok = true, flows = flows });
        });
    }

    // create_block_client_action: create a ClientActionFlow on a WEB BLOCK's ClientActions
    // collection (block-scoped client action - the right type for radio OnChange / button
    // handlers that must resolve against the block, not a module-level ClientActionFlow). Mirrors
    // TryCreateScreenClientAction's factory + fallback-ctor pattern.
    static string CreateBlockClientAction(string module, string block, string name)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var blk = FindBlock(es, block);
        if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        int before = CountProp(blk, "ClientActions");
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                try { created = CallMethod(blk, "CreateClientAction", new object[] { name, key }, 2); via += "/block.CreateClientAction"; }
                catch (Exception e1)
                {
                    // Decompiled: the WEB BLOCK's ClientActions is ISSCollection<NRFlows.ClientScreenActionFlow>
                    // (NOT ClientActionFlow). ClientScreenActionFlow ctor: (NRNodes.AbstractWebInteractiveContentNode
                    // parent, string name) or (IParent<ClientScreenActionFlow>, string). Implements
                    // IClientSideDestination - the ONLY action type a widget EventHandler.Destination accepts
                    // (module-level ClientActionFlow does NOT implement IClientSideDestination, so radio OnChange
                    // fails with "Destination not assignable").
                    var cafType = FindType("ServiceStudio.Model.NRFlows+ClientScreenActionFlow");
                    if (cafType == null) { err = e1.Message + "; ClientScreenActionFlow type not found"; return; }
                    // Try (IParent<ClientScreenActionFlow>, string) first, then the abstract-node ctor, then
                    // (ESpace, string). Binding against the concrete blk finds whichever holds.
                    foreach (var ctor in cafType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var ps = ctor.GetParameters();
                        if (ps.Length != 2 || ps[1].ParameterType != typeof(string)) continue;
                        try { created = ctor.Invoke(new object[] { blk, name }); via += "/" + ctor.GetParameters()[0].ParameterType.Name + "-ctor"; break; }
                        catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = err == null ? (e2.GetType().Name + ": " + r.Message) : err + " | " + r.GetType().Name + ": " + r.Message; }
                    }
                    if (created == null && err == null) err = "no matching ClientScreenActionFlow ctor";
                }
                if (err == null && created == null) err = "CreateClientAction returned null";
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create block client action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(blk, "ClientActions");
        return Json(new { ok = created != null || (before > 0 && after == before), via = via, createdType = created?.GetType().FullName, clientActionsBefore = before, clientActionsAfter = after, error = err });
    }

    // create_structure: create a NAMED Structure (e.g. UserStats) on the eSpace via
    // IESpace.CreateStructure(name, key) (factory hunt; fallback to Structures.Create/Add).
    static string CreateStructure(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var structs = GetProp(es, "Structures") as IEnumerable;
        if (structs != null)
            foreach (var s in structs)
                try { if ((GetProp(s, "Name") as string) == name) return Json(new { ok = false, error = "structure already exists: " + name }); } catch { }
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                try { created = CallMethod(es, "CreateStructure", new object[] { name, key }, 2); via += "/es.CreateStructure"; }
                catch (Exception e1)
                {
                    try { created = CallMethod(structs, "Add", new object[] { name, key }, 2); via += "/Structures.Add"; }
                    catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = e1.Message + "; Structures.Add: " + r.Message; }
                }
                if (err == null && created == null) err = "CreateStructure returned null";
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create structure", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, error = err });
    }

    // add_structure_attribute: add an ATTRIBUTE to a named Structure (e.g. TotalLogins Integer
    // on UserStats). Uses the structure's CreateAttribute/Add factory (2-arity name+key), then
    // sets DataType by resolving es.<type>Type / structure type.
    static string AddStructureAttribute(string module, string structure, string attrName, string type)
    {
        if (string.IsNullOrEmpty(structure) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "structure, attrName and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var structs = GetProp(es, "Structures") as IEnumerable;
        object st = null;
        if (structs != null)
            foreach (var s in structs)
                try { if ((GetProp(s, "Name") as string) == structure) { st = s; break; } } catch { }
        if (st == null) return Json(new { ok = false, error = "structure not found: " + structure });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        object created = null; string err = null; string via = null;
        object dataType = null;
        TryGetProp(es, type + "Type", out dataType);
        // List types live in es.ListTypes (e.g. "ActivityTag List"); match by Name. Fall back to
        // named structures (es.Structures) so "UserStats" resolves too.
        if (dataType == null)
        {
            var listTypes = GetProp(es, "ListTypes") as IEnumerable;
            if (listTypes != null)
                foreach (var lt in listTypes)
                    try { if ((GetProp(lt, "Name") as string) == type) { dataType = lt; break; } } catch { }
        }
        if (dataType == null)
        {
            var structs2 = GetProp(es, "Structures") as IEnumerable;
            if (structs2 != null)
                foreach (var s in structs2)
                    try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
        }
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                try { created = CallMethod(st, "CreateAttribute", new object[] { attrName, key }, 2); via += "/st.CreateAttribute"; }
                catch (Exception e1)
                {
                    var attrs = GetProp(st, "Attributes") as IEnumerable;
                    var m = attrs != null ? FindMethod(attrs, "Add", 2) : null;
                    if (m != null) { try { created = m.Invoke(attrs, new object[] { attrName, key }); via += "/Attributes.Add"; } catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = e2.GetType().Name + ": " + r.Message; } }
                    else err = e1.Message;
                }
                if (err == null && created == null) err = "CreateAttribute returned null";
                if (created != null && dataType != null) { bool dt = false; try { SetProp(created, "DataType", dataType); dt = true; } catch { } via += " DataType=" + type + (dt ? "" : "(not set!)"); }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: add structure attribute", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, error = err });
    }

    // set_structure_attribute_type: set the DataType on an EXISTING structure attribute (e.g.
    // RecentActivityTags = "ActivityTag List"). Resolves type like AddStructureAttribute: basic
    // es.<type>Type, then es.ListTypes by Name, then es.Structures by Name. Undo unit.
    static string SetStructureAttributeType(string module, string structure, string attrName, string type)
    {
        if (string.IsNullOrEmpty(structure) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "structure, attrName and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var structs = GetProp(es, "Structures") as IEnumerable;
        object st = null;
        if (structs != null)
            foreach (var s in structs)
                try { if ((GetProp(s, "Name") as string) == structure) { st = s; break; } } catch { }
        if (st == null) return Json(new { ok = false, error = "structure not found: " + structure });
        object attr = null;
        var attrs = GetProp(st, "Attributes") as IEnumerable;
        if (attrs != null)
            foreach (var a in attrs)
                try { if ((GetProp(a, "Name") as string) == attrName) { attr = a; break; } } catch { }
        if (attr == null) return Json(new { ok = false, error = "attribute not found: " + attrName + " on " + structure });
        object dataType = null;
        TryGetProp(es, type + "Type", out dataType);
        if (dataType == null)
        {
            var listTypes = GetProp(es, "ListTypes") as IEnumerable;
            if (listTypes != null)
                foreach (var lt in listTypes)
                    try { if ((GetProp(lt, "Name") as string) == type) { dataType = lt; break; } } catch { }
        }
        if (dataType == null)
        {
            var structs2 = GetProp(es, "Structures") as IEnumerable;
            if (structs2 != null)
                foreach (var s in structs2)
                    try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
        }
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "set structure attribute type", es2 =>
        {
            SetProp(attr, "DataType", dataType);
            return "set " + structure + "." + attrName + " DataType -> " + type + " (" + TypeLabel(dataType) + ")";
        });
    }

    // ============ P2: entities (live data model) + verify readback ============
    // Resolve an attribute DataType the same way AddStructureAttribute does (basic
    // es.<type>Type, then es.ListTypes by Name, then es.Structures by Name), plus
    // es.Entities by Name so reference attributes resolve too.
    // FK Identifier syntax: "<Entity> Identifier" (e.g. "Wave Identifier") strips the suffix
    // (case-insensitive), resolves the entity in LOCAL es.Entities and returns its
    // IdentifierType (ServiceStudio.Model.EntityIdentifierType - an AbstractType, i.e. a real
    // DataType, reflection-verified: EntityIdentifierType : AbstractConstantDBType : ... :
    // AbstractType; AbstractEntity.IdentifierType : EntityIdentifierType). Throws naming the
    // entity when it is not found or has no IdentifierType. All other type strings keep the
    // pre-existing resolution behavior. Extends set_entity_attribute_type / add_entity_attribute.
    static object ResolveAttrDataType(object es, string type)
    {
        if (!string.IsNullOrEmpty(type))
        {
            const string idSuffix = " Identifier";
            if (type.Length > idSuffix.Length && type.EndsWith(idSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var entityName = type.Substring(0, type.Length - idSuffix.Length).TrimEnd();
                var ent = FindEntity(es, entityName);
                if (ent == null) throw new Exception("entity '" + entityName + "' not found in module (resolving FK Identifier type '" + type + "') - only LOCAL entities resolve for Identifier types; check spelling");
                var idType = GetProp(ent, "IdentifierType");
                if (idType == null) throw new Exception("entity '" + entityName + "' has no IdentifierType (resolving FK Identifier type '" + type + "') - wire its identifier attribute first (set_entity_identifier)");
                return idType;
            }
        }
        object dataType = null;
        TryGetProp(es, type + "Type", out dataType);
        if (dataType == null)
        {
            var listTypes = GetProp(es, "ListTypes") as IEnumerable;
            if (listTypes != null)
                foreach (var lt in listTypes)
                    try { if ((GetProp(lt, "Name") as string) == type) { dataType = lt; break; } } catch { }
        }
        if (dataType == null)
        {
            var structs = GetProp(es, "Structures") as IEnumerable;
            if (structs != null)
                foreach (var s in structs)
                    try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
        }
        if (dataType == null)
        {
            // Entity record types live as anonymous singleton structures (e.g. "SurvivorType Record").
            var anonStructs = GetProp(es, "AnonymousStructures") as IEnumerable;
            if (anonStructs != null)
                foreach (var s in anonStructs)
                    try { if ((GetProp(s, "Name") as string) == type) { dataType = s; break; } } catch { }
        }
        if (dataType == null)
        {
            var ents = GetProp(es, "Entities") as IEnumerable;
            if (ents != null)
                foreach (var e in ents)
                    try { if ((GetProp(e, "Name") as string) == type) { dataType = e; break; } } catch { }
        }
        return dataType;
    }

    static object FindEntity(object es, string name)
    {
        var ents = GetProp(es, "Entities") as IEnumerable;
        if (ents == null) return null;
        foreach (var e in ents)
            try { if ((GetProp(e, "Name") as string) == name) return e; } catch { }
        return null;
    }

    // create_entity: create a SERVER Entity on the eSpace via IESpace.CreateServerEntity
    // (name, key). Rejects duplicates. Undo unit.
    static string CreateEntity(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        if (FindEntity(es, name) != null) return Json(new { ok = false, error = "entity already exists: " + name });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator is null" });
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                try { created = CallMethod(es, "CreateServerEntity", new object[] { name, key }, 2); via += "/es.CreateServerEntity"; }
                catch (Exception e1)
                {
                    try
                    {
                        var ents = GetProp(es, "Entities");
                        created = CallMethod(ents, "Add", new object[] { name, key }, 2); via += "/Entities.Add";
                    }
                    catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = FirstMsg(e1) + " ; Entities.Add: " + r.GetType().Name + ": " + r.Message; }
                }
                if (err == null && created == null) err = "CreateServerEntity returned null";
                // Auto-Id: SS-created entities always carry an Id identifier attribute; the raw
                // CreateServerEntity factory does not add one. Create `Id : LongInteger,
                // mandatory` in the SAME command so the entity is runtime-valid immediately.
                string idVia = null;
                if (err == null && created != null)
                {
                    try
                    {
                        bool hasId = false;
                        var attrs0 = GetProp(created, "Attributes") as IEnumerable;
                        if (attrs0 != null) foreach (var a in attrs0) try { if ((GetProp(a, "Name") as string) == "Id") { hasId = true; break; } } catch { }
                        if (!hasId)
                        {
                            var key2 = CallMethod(ms, "NewKey", null, 0);
                            object idAttr = null;
                            try { idAttr = CallMethod(created, "CreateAttribute", new object[] { "Id", key2 }, 2); idVia = "CreateAttribute"; }
                            catch
                            {
                                var attrs = GetProp(created, "Attributes");
                                idAttr = CallMethod(attrs, "Add", new object[] { "Id", key2 }, 2);
                                idVia = "Attributes.Add";
                            }
                            if (idAttr != null)
                            {
                                try { SetProp(idAttr, "DataType", GetProp(es, "LongIntegerType")); } catch { }
                                try { SetProp(idAttr, "IsMandatory", true); } catch { }
                                try { SetProp(idAttr, "Label", "Id"); } catch { }
                                // Wire it as THE identifier so entity actions regenerate (SS-created
                                // entities have Identifier=Id from birth).
                                try { SetProp(created, "Identifier", idAttr); CallMethod(created, "RefreshEntityActions", null, 0); idVia += "+identifier"; } catch { }
                                via += " +autoId(" + idVia + ")";
                            }
                        }
                        else via += " (Id already present)";
                    }
                    catch (Exception e3) { via += " +autoId FAILED: " + FirstMsg(e3); }
                }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create entity", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, error = err });
    }

    // add_entity_attribute: add an ATTRIBUTE to a server Entity (e.g. Title Text on
    // ScratchOrder). entity.CreateAttribute(name, key) then DataType; best-effort
    // IsMandatory (bool) + DefaultValue (string) when provided. Undo unit.
    static string AddEntityAttribute(string module, string entity, string attrName, string type, string isMandatory, string defaultValue)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "entity, attrName and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "add entity attribute", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null;
            try { created = CallMethod(ent, "CreateAttribute", new object[] { attrName, key }, 2); via = "ent.CreateAttribute"; }
            catch (Exception e1)
            {
                var attrs = GetProp(ent, "Attributes");
                var m = attrs != null ? FindMethod(attrs, "Add", 2) : null;
                if (m == null) throw new Exception("no attribute factory: " + FirstMsg(e1));
                created = m.Invoke(attrs, new object[] { attrName, key }); via = "Attributes.Add";
            }
            SetProp(created, "DataType", dataType);
            var flags = new List<string>();
            if (!string.IsNullOrEmpty(isMandatory) && bool.TryParse(isMandatory, out var mand))
            { try { SetProp(created, "IsMandatory", mand); flags.Add("IsMandatory=" + mand); } catch { flags.Add("IsMandatory(not set)"); } }
            if (!string.IsNullOrEmpty(defaultValue))
            { try { SetProp(created, "DefaultValue", defaultValue); flags.Add("DefaultValue=" + defaultValue); } catch { flags.Add("DefaultValue(not set)"); } }
            return "added " + entity + "." + attrName + " : " + type + " (" + TypeLabel(dataType) + ") via " + via + (flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "");
        });
    }

    // set_entity_attribute_type: set DataType on an EXISTING entity attribute. Undo unit.
    static string SetEntityAttributeType(string module, string entity, string attrName, string type)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "entity, attrName and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        object attr = null;
        var attrs = GetProp(ent, "Attributes") as IEnumerable;
        if (attrs != null)
            foreach (var a in attrs)
                try { if ((GetProp(a, "Name") as string) == attrName) { attr = a; break; } } catch { }
        if (attr == null) return Json(new { ok = false, error = "attribute not found: " + attrName + " on " + entity });
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "set entity attribute type", es2 =>
        {
            SetProp(attr, "DataType", dataType);
            return "set " + entity + "." + attrName + " DataType -> " + type + " (" + TypeLabel(dataType) + ")";
        });
    }

    // set_entity_attribute_name: rename an EXISTING entity attribute. Same resolution
    // pattern as set_entity_attribute_type (entity by name -> attribute by name), then
    // SetProp(attr, "Name", newName) inside a real SS command (undo unit). The report
    // carries the old name and a read-back of the new Name for verification.
    static string SetEntityAttributeName(string module, string entity, string attrName, string newName)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(newName)) return Json(new { ok = false, error = "entity, attrName and newName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        object attr = null;
        var attrs = GetProp(ent, "Attributes") as IEnumerable;
        if (attrs != null)
            foreach (var a in attrs)
                try { if ((GetProp(a, "Name") as string) == attrName) { attr = a; break; } } catch { }
        if (attr == null) return Json(new { ok = false, error = "attribute not found: " + attrName + " on " + entity });
        if (newName == attrName) return Json(new { ok = false, error = "newName equals current name '" + attrName + "' - nothing to change" });
        return RunCmd(module, "set entity attribute name", es2 =>
        {
            SetProp(attr, "Name", newName);
            string readBack = null;
            try { readBack = GetProp(attr, "Name") as string; } catch { }
            return "renamed " + entity + "." + attrName + " Name -> " + newName
                + (readBack != null ? " (read-back: " + readBack + ")" : " (read-back: n/a)");
        });
    }

    // set_entity_prop: set a settable string/bool/int property on a server Entity
    // (Public, ExposeReadOnly, Description, PrettyName, IsStaticEntity, UpdateBehavior, ...).
    // ExposeCreateAndChangeActions / IdentifierType / EntityActions are read-only (computed)
    // and throw a clean error here. Undo unit. Verified by read-back.
    static string SetEntityProp(string module, string entity, string prop, string value)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(prop)) return Json(new { ok = false, error = "entity and prop required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        string before = null;
        try { var b = GetProp(ent, prop); if (b != null) before = b.ToString(); } catch { }
        return RunCmd(module, "set entity prop " + prop, es2 =>
        {
            object val = value;
            if (bool.TryParse(value, out var b)) val = b;
            else if (int.TryParse(value, out var i)) val = i;
            else if (long.TryParse(value, out var l)) val = l;
            SetProp(ent, prop, val); // throws -> RunCmd marks error (read-only props land here)
            string after = null;
            try { var a = GetProp(ent, prop); if (a != null) after = a.ToString(); } catch { }
            return "set " + entity + "." + prop + " = " + value
                + (before != null ? " (was " + before + ")" : "")
                + (after != null ? " -> now " + after : "");
        });
    }

    // set_structure_prop / set_server_action_prop: set a settable bool/string/int property on
    // a Structure or a Server/Service Action (e.g. Public=True). Same pattern as SetEntityProp.
    static string SetStructureProp(string module, string structure, string prop, string value)
    {
        if (string.IsNullOrEmpty(structure) || string.IsNullOrEmpty(prop)) return Json(new { ok = false, error = "structure and prop required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object obj = null;
        var structs = GetProp(es, "Structures") as IEnumerable;
        if (structs != null)
            foreach (var s in structs)
                try { if ((GetProp(s, "Name") as string) == structure) { obj = s; break; } } catch { }
        if (obj == null) return Json(new { ok = false, error = "structure not found: " + structure });
        return SetNamedObjectProp(module, structure, prop, value, obj);
    }

    static string SetServerActionProp(string module, string action, string prop, string value)
    {
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(prop)) return Json(new { ok = false, error = "action and prop required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var obj = FindAction(es, action);
        if (obj == null) return Json(new { ok = false, error = "action not found: " + action });
        return SetNamedObjectProp(module, action, prop, value, obj);
    }

    static string SetNamedObjectProp(string module, string name, string prop, string value, object obj)
    {
        string before = null;
        try { var b = GetProp(obj, prop); if (b != null) before = b.ToString(); } catch { }
        return RunCmd(module, "set " + prop, es2 =>
        {
            object val = value;
            if (bool.TryParse(value, out var b)) val = b;
            else if (int.TryParse(value, out var i)) val = i;
            else if (long.TryParse(value, out var l)) val = l;
            SetProp(obj, prop, val);
            string after = null;
            try { var a = GetProp(obj, prop); if (a != null) after = a.ToString(); } catch { }
            return "set " + name + "." + prop + " = " + value
                + (before != null ? " (was " + before + ")" : "")
                + (after != null ? " -> now " + after : "");
        });
    }

    // set_entity_identifier: wire an existing attribute as the entity's Identifier (primary
    // key). The settable surface is entity.Identifier (public setter on AbstractCoreEntity);
    // IsIdentifierAttribute on the attribute is read-only (computed). Then RefreshEntityActions()
    // regenerates the auto entity actions against the new schema. Verified by re-read.
    static string SetEntityIdentifier(string module, string entity, string attrName)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(attrName)) return Json(new { ok = false, error = "entity and attrName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        object attr = null;
        var attrs = GetProp(ent, "Attributes") as IEnumerable;
        if (attrs != null)
            foreach (var a in attrs)
                try { if ((GetProp(a, "Name") as string) == attrName) { attr = a; break; } } catch { }
        if (attr == null) return Json(new { ok = false, error = "attribute not found: " + attrName + " on " + entity });
        return RunCmd(module, "set entity identifier", es2 =>
        {
            SetProp(ent, "Identifier", attr);
            CallMethod(ent, "RefreshEntityActions", null, 0);
            var id = GetProp(ent, "Identifier");
            var idType = GetProp(ent, "IdentifierType");
            var idName = id != null ? (GetProp(id, "Name") as string ?? "<unnamed>") : "<null>";
            return entity + ".Identifier = " + idName + " ; IdentifierType = " + (idType != null ? idType.GetType().Name : "<null>");
        });
    }

    // probe_entity_actions: list the auto-generated entity actions (type short name, Name,
    // input/output parameter names + types). Read-only. Call after set_entity_identifier so the
    // actions are regenerated (RefreshEntityActions).
    static string ProbeEntityActions(string module, string entity)
    {
        if (string.IsNullOrEmpty(entity)) return Json(new { ok = false, error = "entity required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        var actions = GetProp(ent, "EntityActions") as IEnumerable;
        var sb = new StringBuilder();
        sb.AppendLine("entity " + entity + " EntityActions:");
        int n = 0;
        if (actions != null)
            foreach (var actObj in actions)
            {
                n++;
                sb.AppendLine("  [" + n + "] type=" + ShortName(actObj) + " name=" + SafeToString(GetProp(actObj, "Name")));
                var ins = GetProp(actObj, "InputParameters") as IEnumerable;
                if (ins != null) foreach (var p in ins)
                    sb.AppendLine("      in : " + SafeToString(GetProp(p, "Name")) + " : " + SafeToString(GetProp(p, "DataType")));
                var outs = GetProp(actObj, "OutputParameters") as IEnumerable;
                if (outs != null) foreach (var p in outs)
                    sb.AppendLine("      out: " + SafeToString(GetProp(p, "Name")));
            }
        if (n == 0) sb.AppendLine("  (none - regenerate via set_entity_identifier or check entity)");
        return Json(new { ok = true, report = sb.ToString() });
    }

    // Find an entity action object by name: exact Name match, then exact type short name
    // (e.g. "CreateEntity"), then prefix match (e.g. "Create" -> "CreateEntity").
    static object FindEntityAction(object ent, string name)
    {
        var actions = GetProp(ent, "EntityActions") as IEnumerable;
        if (actions == null || string.IsNullOrEmpty(name)) return null;
        object prefixMatch = null;
        foreach (var a in actions)
        {
            var nm = GetProp(a, "Name") as string;
            if (string.Equals(nm, name, StringComparison.OrdinalIgnoreCase)) return a;
            var tn = ShortName(a);
            if (string.Equals(tn, name, StringComparison.OrdinalIgnoreCase)) return a;
            if (prefixMatch == null && tn != null && tn.StartsWith(name, StringComparison.OrdinalIgnoreCase)) prefixMatch = a;
            if (prefixMatch == null && nm != null && nm.StartsWith(name, StringComparison.OrdinalIgnoreCase)) prefixMatch = a;
        }
        return prefixMatch;
    }

    // Map an ExecuteAction's Arguments by name against the flow action's scope
    // (input parameters + local variables). Same SetValue(str) mechanism as MapActionInputs.
    static string MapArgumentsByName(object act, object node)
    {
        var sb = new StringBuilder();
        var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collName in new[] { "InputParameters", "Variables", "LocalVariables" })
        {
            var coll = GetProp(act, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var p in coll) { var nm = GetProp(p, "Name") as string; if (nm != null) scope.Add(nm); }
        }
        var args = GetProp(node, "Arguments") as IEnumerable;
        if (args == null) { sb.AppendLine("  (no Arguments on node)"); return sb.ToString(); }
        foreach (var arg in args)
        {
            var param = GetProp(arg, "Parameter");
            var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
            if (scope.Contains(paramName))
            {
                bool mapped = false;
                try { CallMethod(arg, "SetValue", new object[] { paramName }, 1); mapped = true; } catch { }
                if (!mapped) try { CallMethod(arg, "SetVariable", new object[] { paramName }, 1); mapped = true; } catch { }
                sb.AppendLine("  arg " + paramName + " -> " + (mapped ? "mapped (=" + paramName + ")" : "SetValue FAILED"));
            }
            else sb.AppendLine("  arg " + paramName + " -> no scope match (set manually)");
        }
        return sb.ToString();
    }

    // create_entity_action_node: insert an ExecuteAction node calling an ENTITY action
    // (CreateEntity/Create, UpdateEntity/Update, DeleteEntity/Delete, GetEntity/Get,
    // CreateOrUpdateEntity/CreateOrUpdate...). Entity actions are IActionSignature objects on
    // entity.EntityActions; the flow node is the standard IExecuteServerActionNode with
    // Action = that object. where: "beforeEnd" | "afterAnchor" (anchorVar/anchorValue) |
    // "afterNode" (afterNodeIndex). Auto-maps arguments by name. Undo unit.
    static string CreateEntityActionNode(string moduleName, string actionName, string entityName, string entityActionName,
        string where, string anchorVar, string anchorValue, int afterNodeIndex = -1)
    {
        if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(entityActionName))
            return Json(new { ok = false, error = "entity and entityAction required" });
        return LiveEdit(moduleName, actionName, "create entity action node", (act, es) =>
        {
            var ent = FindEntity(es, entityName);
            if (ent == null) throw new Exception("entity not found: " + entityName);
            var ea = FindEntityAction(ent, entityActionName);
            if (ea == null) throw new Exception("entity action not found: " + entityActionName + " on " + entityName + " (probe_entity_actions lists them)");
            var newNode = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IExecuteServerActionNode");
            SetProp(newNode, "Action", ea);
            var sb = new StringBuilder();
            if (where == "afterNode" && afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex + " (count=" + nodes.Count + ")");
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted entity action " + entityActionName + " after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            else if (where == "afterAnchor")
            {
                var anchor = FindAssignNode(act, anchorVar ?? "", anchorValue ?? "");
                if (anchor == null) throw new Exception("anchor not found: " + anchorVar + "=" + anchorValue);
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted entity action " + entityActionName + " after anchor (" + anchorVar + "=" + anchorValue + ")");
            }
            else // beforeEnd (first End node)
            {
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                    foreach (var n in NodeList(act))
                    {
                        var tgt = GetProp(n, "Target");
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                    }
                if (endNode != null && prev == null)
                {
                    var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
                    if (startNode != null && GetProp(startNode, "Target") == null)
                    {
                        SetProp(startNode, "Target", endNode);
                        prev = startNode;
                        sb.AppendLine("auto-linked Start?End");
                    }
                }
                if (endNode == null || prev == null) throw new Exception("End/prev not found (create Start+End nodes and link them via live_set_node_target before using where='beforeEnd')");
                SetProp(newNode, "Target", endNode);
                SetProp(prev, "Target", newNode);
                sb.AppendLine("inserted entity action " + entityActionName + " before End");
            }
            sb.AppendLine(MapArgumentsByName(act, newNode));
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // delete_entity_attribute: remove one attribute from a server Entity. Undo unit.
    static string DeleteEntityAttribute(string module, string entity, string attrName)
    {
        if (string.IsNullOrEmpty(entity) || string.IsNullOrEmpty(attrName)) return Json(new { ok = false, error = "entity and attrName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, entity);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + entity });
        object attr = null;
        var attrs = GetProp(ent, "Attributes") as IEnumerable;
        if (attrs != null)
            foreach (var a in attrs)
                try { if ((GetProp(a, "Name") as string) == attrName) { attr = a; break; } } catch { }
        if (attr == null) return Json(new { ok = false, error = "attribute not found: " + attrName + " on " + entity });
        return RunCmd(module, "delete entity attribute", es2 =>
        {
            CallMethod(attr, "Delete", null, 0);
            return "deleted attribute " + attrName + " from entity " + entity;
        });
    }

    // delete_entity: remove a whole server Entity by name. Undo unit (Ctrl+Z restores).
    static string DeleteEntity(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var ent = FindEntity(es, name);
        if (ent == null) return Json(new { ok = false, error = "entity not found: " + name });
        int before = CountProp(es, "Entities");
        string err = null;
        Action mutate = () =>
        {
            try { CallMethod(ent, "Delete", null, 0); }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        };
        try
        {
            var pc = BuildPresenterContext(GetContext(es));
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: delete entity", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(es, "Entities");
        return Json(new { ok = err == null, deleted = name, entitiesBefore = before, entitiesAfter = after, error = err });
    }

    // get_verify_errors: read-only validation readback for an action, screen, block, or
    // single widget. kind=widget needs screen or block to scope FindWidget.
    // verbose=true adds a first-level property dump per message (to discover the culprit
    // shape). Returns up to 50 messages; empty list = clean.
    static string GetVerifyErrors(string module, string kind, string name, string screen, string block, string verbose)
    {
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "kind (action|screen|block|widget) and name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object target = null;
        if (kind.Equals("action", StringComparison.OrdinalIgnoreCase)) target = FindAction(es, name);
        else if (kind.Equals("screen", StringComparison.OrdinalIgnoreCase)) target = FindScreen(es, name);
        else if (kind.Equals("block", StringComparison.OrdinalIgnoreCase)) target = FindBlock(es, name);
        else if (kind.Equals("widget", StringComparison.OrdinalIgnoreCase))
        {
            object root = null;
            if (!string.IsNullOrEmpty(block)) root = FindBlock(es, block);
            else if (!string.IsNullOrEmpty(screen)) root = FindScreen(es, screen);
            if (root == null) return Json(new { ok = false, error = "widget kind needs screen or block" });
            target = FindWidget(root, name);
        }
        else return Json(new { ok = false, error = "kind must be action|screen|block|widget" });
        if (target == null) return Json(new { ok = false, error = kind + " not found: " + name });
        var msgs = new List<string>();
        var detail = new List<object>();
        bool wantVerbose = (verbose ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);
        try
        {
            object res = null;
            try { res = CallMethod(target, "GetVerifyErrors", null, 0); }
            catch { res = CallMethod(target, "GetValidationMessages", new object[] { true }, 1); }
            var en = res as IEnumerable;
            if (en != null)
                foreach (var m in en)
                {
                    if (msgs.Count >= 50) break;
                    string text = null;
                    try { text = GetProp(m, "Message") as string; } catch { }
                    if (text == null) { try { text = GetProp(m, "Text") as string; } catch { } }
                    string owner = "";
                    try
                    {
                        // The culprit is NOT the message: resolve the model object the message
                        // points at (Object/Element/Owner/Widget/Target props), then Name it +
                        // walk its Parent chain.
                        object culprit = null;
                        foreach (var pn in new[] { "Object", "Element", "Owner", "Widget", "Target", "Source" })
                        {
                            try { culprit = GetProp(m, pn); } catch { culprit = null; }
                            if (culprit != null && !(culprit is string)) break;
                            culprit = null;
                        }
                        if (culprit != null)
                        {
                            var chain = new List<string>();
                            object cur = culprit;
                            for (int i = 0; i < 4 && cur != null; i++)
                            {
                                string nm = null;
                                try { nm = GetProp(cur, "Name") as string; } catch { }
                                chain.Add(cur.GetType().Name + (nm != null ? ":" + nm : ""));
                                object next = null;
                                try { next = GetProp(cur, "Parent"); } catch { break; }
                                if (next == null || ReferenceEquals(next, cur)) break;
                                cur = next;
                            }
                            owner = string.Join(">", chain);
                        }
                    }
                    catch { }
                    msgs.Add((text ?? (m?.ToString() ?? "?")) + (owner != "" ? " @ " + owner : ""));
                    if (wantVerbose && detail.Count < 50)
                    {
                        var pd = new Dictionary<string, string>();
                        try
                        {
                            foreach (var p in m.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (pd.Count >= 40) break;
                                if (p.GetIndexParameters().Length > 0) continue;
                                object v = null;
                                try { v = p.GetValue(m, null); } catch (Exception e) { pd[p.Name] = "!ERR " + e.GetType().Name; continue; }
                                string vs = null;
                                try { vs = v == null ? "null" : v.ToString(); } catch { vs = "!TOSTR-ERR"; }
                                if (vs != null && vs.Length > 120) vs = vs.Substring(0, 120) + "...";
                                pd[p.Name] = vs;
                            }
                        }
                        catch { }
                        pd["__type"] = m.GetType().FullName;
                        detail.Add(new { message = text, props = pd });
                    }
                }
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return Json(new { ok = false, error = "verify readback failed: " + r.GetType().Name + ": " + r.Message }); }
        if (wantVerbose) return Json(new { ok = true, kind = kind, name = name, errorCount = msgs.Count, errors = msgs.ToArray(), detail = detail.ToArray() });
        return Json(new { ok = true, kind = kind, name = name, errorCount = msgs.Count, errors = msgs.ToArray() });
    }

    // ============ P3: tables/lists/paging/sort/calc/foreach/screen-inputs ============
    static object FindScreenAggregate(object es, string screen, string block, string name)
    {
        object target = null;
        if (!string.IsNullOrEmpty(block)) target = FindBlock(es, block);
        else if (!string.IsNullOrEmpty(screen)) target = FindScreen(es, screen);
        if (target == null) return null;
        var aggs = GetProp(target, "ScreenAggregates") as IEnumerable;
        if (aggs == null) return null;
        foreach (var a in aggs)
            try { if ((GetProp(a, "Name") as string) == name) return a; } catch { }
        return null;
    }

    // set_widget_source: bind Source on a Table/List/Form widget (SetSource(String) typed
    // setter, CP fallback). Screen OR block addressing. Undo unit.
    static string SetWidgetSource(string module, string screen, string block, string widget, string source)
    {
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(source)) return Json(new { ok = false, error = "widget and source required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        return RunCmd(module, "set widget source", es =>
        {
            object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
            if (root == null) throw new Exception(!string.IsNullOrEmpty(block) ? "web block not found: " + block : "screen not found: " + screen);
            var w = FindWidget(root, widget);
            if (w == null) throw new Exception("widget not found: " + widget);
            var errs = new List<string>();
            try
            {
                var m = FindMethod(w, "SetSource", 1);
                if (m != null) { m.Invoke(w, new object[] { source }); return "set Source = " + source + " on '" + widget + "' via SetSource (" + w.GetType().Name + ")"; }
                errs.Add("no SetSource(String)");
            }
            catch (Exception e) { errs.Add("SetSource: " + FirstMsg(e)); }
            try
            {
                var cps = GetProp(w, "CustomProperties") as IEnumerable;
                if (cps != null)
                    foreach (var cp in cps)
                    {
                        string pn = null;
                        try { pn = GetProp(cp, "PropertyName") as string; } catch { }
                        if (pn != "Source") continue;
                        try { CallMethodTyped(cp, "SetValueExpression", new object[] { source }); return "set Source = " + source + " on '" + widget + "' via CP.SetValueExpression"; }
                        catch (Exception e) { errs.Add("CP.SetValueExpression: " + FirstMsg(e)); }
                        try { SetProp(cp, "Value", source); return "set Source = " + source + " on '" + widget + "' via CP.Value"; }
                        catch (Exception e) { errs.Add("CP.Value: " + FirstMsg(e)); }
                    }
                errs.Add("no Source CustomProperty");
            }
            catch (Exception e) { errs.Add("CP hunt: " + FirstMsg(e)); }
            throw new Exception("no Source surface on " + w.GetType().Name + ": " + string.Join(" ; ", errs));
        });
    }

    // set_aggregate_paging: SetMaxRecords/SetStartIndex (expression text) on a screen/block
    // aggregate. E.g. maxRecords="10", startIndex="(PageNumber-1)*PageSize". Undo unit.
    static string SetAggregatePaging(string module, string screen, string block, string name, string maxRecords, string startIndex)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(maxRecords) && string.IsNullOrEmpty(startIndex)) return Json(new { ok = false, error = "maxRecords or startIndex required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        return RunCmd(module, "set aggregate paging", es2 =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(maxRecords)) { CallMethod(agg, "SetMaxRecords", new object[] { maxRecords }, 1); parts.Add("MaxRecords=" + maxRecords); }
            if (!string.IsNullOrEmpty(startIndex)) { CallMethod(agg, "SetStartIndex", new object[] { startIndex }, 1); parts.Add("StartIndex=" + startIndex); }
            return "set paging on '" + name + "': " + string.Join(", ", parts);
        });
    }

    // add_aggregate_sort: replicate the UI presenter bytecode EXACTLY (decompiled 2026-09-16):
    //   new AttributeSort() + set_Sort + set_IsDynamic(false) +
    //   SetOriginalAttribute(AttributeReference.FullNameInScope == "<Source>.<Attr>") +
    //   ADD to the query CombineSources.Sorts (proper parenting = working scope).
    // agg.CreateSort()/AddOrderBy leave scope broken. Tries scope forms, keeps delta-winner.
    static string AddAggregateSort(string module, string screen, string block, string name, string attr, string ascending)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(attr)) return Json(new { ok = false, error = "name and attr required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        bool asc = !(ascending ?? "").Equals("false", StringComparison.OrdinalIgnoreCase);
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        int before = CountVerify(root);
        var log = new List<string>();
        string winner = null;
        string bare = attr.Contains(".") ? attr.Substring(attr.LastIndexOf('.') + 1) : attr;
        foreach (var form in new[] { attr, bare })
        {
            string r = RunCmd(module, "add aggregate sort " + form, es2 =>
            {
                var a2 = FindScreenAggregate(es2, screen, block, name);
                if (a2 == null) throw new Exception("aggregate not found: " + name);
                var t = FindType("ServiceStudio.Model.AttributeSort");
                if (t == null) throw new Exception("AttributeSort type not found");
                object table0 = GetProp(a2, "Table");
                if (table0 == null) throw new Exception("aggregate has no Table");
                object combine0 = null;
                foreach (var pn in new[] { "RootOperation", "DetailsRootOperation" })
                {
                    try { combine0 = GetProp(table0, pn); } catch { }
                    if (combine0 != null) break;
                }
                if (combine0 == null) throw new Exception("no CombineSources (RootOperation/DetailsRootOperation)");
                // UI calls .ctor(CombineSources) (decompiled presenter IL). The parameterless
                // ctor is nonpublic and refuses Invoke — try parent-typed ctors first.
                object sort = null; string ctorUsed = null; var ctorErrs = new List<string>();
                foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ps = ctor.GetParameters();
                    if (ps.Length == 1)
                    {
                        try
                        {
                            if (ps[0].ParameterType.IsAssignableFrom(combine0.GetType()))
                            { sort = ctor.Invoke(new object[] { combine0 }); ctorUsed = "(CombineSources)"; break; }
                        }
                        catch (Exception e) { ctorErrs.Add("(parent): " + FirstMsg(e)); }
                    }
                    if (ps.Length == 0)
                    {
                        try { sort = ctor.Invoke(null); ctorUsed = "()"; break; }
                        catch (Exception e) { ctorErrs.Add("(): " + FirstMsg(e)); }
                    }
                }
                if (sort == null) throw new Exception("no AttributeSort ctor worked (" + string.Join(" ; ", ctorErrs) + ")");
                try
                {
                    var sortProp = t.GetProperty("Sort");
                    if (sortProp != null && sortProp.CanWrite)
                    {
                        object enumVal = null;
                        try { enumVal = Enum.Parse(sortProp.PropertyType, asc ? "Ascending" : "Descending"); } catch { }
                        if (enumVal == null) { try { enumVal = Enum.Parse(sortProp.PropertyType, asc ? "0" : "1"); } catch { } }
                        if (enumVal != null) sortProp.SetValue(sort, enumVal, null);
                    }
                }
                catch { }
                try { SetProp(sort, "IsDynamic", false); } catch { }
                try { CallMethod(sort, "SetOriginalAttribute", new object[] { form }, 1); }
                catch (Exception e) { throw new Exception("SetOriginalAttribute(" + form + ") failed: " + FirstMsg(e)); }
                object sorts = GetProp(combine0, "Sorts");
                if (sorts == null) throw new Exception("CombineSources has no Sorts collection");
                CallMethod(sorts, "Add", new object[] { sort }, 1);
                try { CallMethod(table0, "MakeAttributesInUseVisible", null, 0); } catch { }
                return "new AttributeSort" + ctorUsed + " + SetOriginalAttribute(" + form + ") + Sorts.Add";
            });
            log.Add(form + ":" + r);
            var es3 = FindEspace(module);
            var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
            int after = CountVerify(root3);
            // Accept the first trial that does not WORSEN the count (green from a clean
            // baseline shows after==before). This bounds junk trials to at most one extra.
            if (after >= 0 && after <= before) { winner = form + " (errors " + before + "->" + after + ")"; break; }
        }
        return Json(new { ok = winner != null, winner = winner, errorsBefore = before, log = log.ToArray() });
    }

    // remove_aggregate_sort: UI RemoveDefaultSort equivalent (decompiled
    // ClientScreenActionFlow.RemoveDefaultSort, SS 11.55.83): delete the AttributeSort(s)
    // on the aggregate's CombineSources.Sorts whose display matches attr
    // ("<Entity>.<Attr>" or bare "<Attr>"). Empty attr removes ALL sorts.
    // Delta-verified: refuses to worsen the verify count. Undo unit.
    static string RemoveAggregateSort(string module, string screen, string block, string name, string attr)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        int before = CountVerify(root);
        string bare = string.IsNullOrEmpty(attr) ? null : (attr.Contains(".") ? attr.Substring(attr.LastIndexOf('.') + 1) : attr);
        string r = RunCmd(module, "remove aggregate sort " + (attr ?? "<all>"), es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            object table0 = GetProp(a2, "Table");
            if (table0 == null) throw new Exception("aggregate has no Table");
            object combine0 = null;
            foreach (var pn in new[] { "RootOperation", "DetailsRootOperation" })
            {
                try { combine0 = GetProp(table0, pn); } catch { }
                if (combine0 != null) break;
            }
            if (combine0 == null) throw new Exception("no CombineSources (RootOperation/DetailsRootOperation)");
            object sorts = GetProp(combine0, "Sorts");
            if (sorts == null) throw new Exception("CombineSources has no Sorts collection");
            var items = new List<object>();
            foreach (var s in (sorts as IEnumerable)) items.Add(s);
            int removed = 0; var kept = new List<string>();
            foreach (var s in items)
            {
                string disp = null;
                try { disp = GetProp(s, "DisplayName") as string; } catch { }
                bool match = string.IsNullOrEmpty(attr)
                    || (!string.IsNullOrEmpty(disp) && (disp.Contains(attr) || (bare != null && disp.Contains(bare))));
                if (!match) { if (disp != null) kept.Add(disp); continue; }
                bool deleted = false;
                try { CallMethod(s, "Delete", null, 0); deleted = true; }
                catch (Exception e1)
                {
                    try { CallMethod(sorts, "Remove", new object[] { s }, 1); deleted = true; }
                    catch (Exception e2) { throw new Exception("cannot remove sort '" + disp + "': " + FirstMsg(e1) + " / " + FirstMsg(e2)); }
                }
                if (deleted) removed++;
            }
            try { CallMethod(table0, "MakeAttributesInUseVisible", null, 0); } catch { }
            return "removed " + removed + " sort(s), kept [" + string.Join(",", kept.ToArray()) + "]";
        });
        var es3 = FindEspace(module);
        var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
        int after = CountVerify(root3);
        bool ok = after >= 0 && after <= before;
        return Json(new { ok = ok, report = r, errorsBefore = before, errorsAfter = after });
    }

    // add_aggregate_dynamic_sort: UI CreateStandardNodesForOnSort sort equivalent
    // (decompiled SS 11.55.83): CombineSources.AddOrderBy(varDisplayName, isDynamic: true)
    // parents the AttributeSort, then MoveChildTo(sort, 0) puts it first.
    // varName is a screen/block variable holding the sort expression text
    // (e.g. "TableSort", toggled "col" / "col DESC" by the OnSort handler).
    // Delta-verified: refuses to worsen the verify count. Undo unit.
    static string AddAggregateDynamicSort(string module, string screen, string block, string name, string varName)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(varName)) return Json(new { ok = false, error = "name and varName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        int before = CountVerify(root);
        string r = RunCmd(module, "add aggregate dynamic sort " + varName, es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            object table0 = GetProp(a2, "Table");
            if (table0 == null) throw new Exception("aggregate has no Table");
            object combine0 = null;
            foreach (var pn in new[] { "RootOperation", "DetailsRootOperation" })
            {
                try { combine0 = GetProp(table0, pn); } catch { }
                if (combine0 != null) break;
            }
            if (combine0 == null) throw new Exception("no CombineSources (RootOperation/DetailsRootOperation)");
            object dynSort = null;
            try { dynSort = CallMethod(combine0, "AddOrderBy", new object[] { varName, true }, 2); }
            catch (Exception e) { throw new Exception("AddOrderBy(" + varName + ", true) failed: " + FirstMsg(e)); }
            if (dynSort == null) throw new Exception("AddOrderBy returned null");
            // Ordering is BEST-EFFORT (canon path removes statics first, so the dynamic
            // sort lands alone at index 0 anyway). Try Parent.MoveChildTo(sort,0,false)
            // via instance-or-extension scan, then sort.MoveToNewAbsoluteIndex(0).
            string moveNote = "skipped";
            try
            {
                object parent = GetProp(dynSort, "Parent");
                if (parent != null)
                {
                    CallMethodExt(parent, "MoveChildTo", new object[] { dynSort, 0, false }, 3, out var via1);
                    moveNote = "MoveChildTo via " + via1;
                }
            }
            catch
            {
                try { CallMethodExt(dynSort, "MoveToNewAbsoluteIndex", new object[] { 0 }, 1, out var via2); moveNote = "MoveToNewAbsoluteIndex via " + via2; }
                catch { }
            }
            try { CallMethod(table0, "MakeAttributesInUseVisible", null, 0); } catch { }
            return "AddOrderBy(" + varName + ", dynamic) + " + moveNote;
        });
        var es3 = FindEspace(module);
        var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
        int after = CountVerify(root3);
        bool ok = after >= 0 && after <= before;
        return Json(new { ok = ok, report = r, errorsBefore = before, errorsAfter = after });
    }

    // set_input_param_mandatory: flip an action input between mandatory and optional
    // by setting ActivityInput (YesMandatory / YesOptional). IsMandatory's setter is a
    // no-op (decompiled GenericInputParameter, SS 11.55.83) - ActivityInput is the real
    // surface. Needed for event-handler payload inputs (e.g. OnSort SortBy), which the
    // platform generates OPTIONAL (raw GenericInputParameter ctor), while the
    // CreateInputParameter factory defaults to YesMandatory. Undo unit.
    static string SetInputParamMandatory(string module, string action, string paramName, string mandatory)
    {
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(paramName)) return Json(new { ok = false, error = "action and paramName required" });
        return LiveEdit(module, action, "set input param mandatory", (act, es) =>
        {
            var inputs = GetProp(act, "InputParameters") as IEnumerable;
            if (inputs == null) throw new Exception("action has no InputParameters");
            object target = null;
            foreach (var p in inputs)
                try { if ((GetProp(p, "Name") as string) == paramName) { target = p; break; } } catch { }
            if (target == null) throw new Exception("input param not found: " + paramName);
            bool mand = !(mandatory ?? "").Equals("false", StringComparison.OrdinalIgnoreCase);
            var t = FindType("OutSystems.Model.Implementation.Enumerations.ActivityInputType");
            if (t == null) throw new Exception("ActivityInputType not found");
            object enumVal = null;
            try { enumVal = Enum.Parse(t, mand ? "YesMandatory" : "YesOptional"); }
            catch (Exception e) { throw new Exception("ActivityInput enum parse failed: " + FirstMsg(e)); }
            string via = SetPropForce(target, "ActivityInput", enumVal);
            return "set input '" + paramName + "' " + (mand ? "mandatory (YesMandatory)" : "optional (YesOptional)") + " via " + via;
        });
    }

    // set_widget_handler_arg: set a call-site argument value on a widget event handler
    // (e.g. ProbeTable OnSort -> SortBy = ClickedColumn). Mirrors SetRaiseEventArg's
    // arg access (Arguments -> Parameter.Name match -> SetValue) but on the widget's
    // EventHandler (NREvents IBuiltinEvent.Arguments) instead of a TriggerEvent node.
    // Completes the platform's OnSort contract: the table raises OnSort(ClickedColumn)
    // and the handler action's SortBy input is fed from it. Undo unit.
    static string SetWidgetHandlerArg(string module, string screen, string block, string widget, string eventName, string argName, string value)
    {
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(argName)) return Json(new { ok = false, error = "widget, event and argName required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        return RunCmd(module, "set widget handler arg", es =>
        {
            object host = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
            if (host == null) throw new Exception(!string.IsNullOrEmpty(block) ? "web block not found: " + block : "screen not found: " + screen);
            var w = FindWidget(host, widget);
            if (w == null) { try { w = FindWidgetDeep(host, widget); } catch { } }
            if (w == null) throw new Exception("widget not found: " + widget);
            object handler = null;
            IEnumerable ehs = null; try { ehs = GetProp(w, "EventHandlers") as IEnumerable; } catch { }
            if (ehs != null)
                foreach (var eh in ehs)
                    try { if ((GetProp(eh, "EventName") as string ?? "") == eventName) { handler = eh; break; } } catch { }
            if (handler == null) throw new Exception("no handler for event '" + eventName + "' on " + widget);
            var args = GetProp(handler, "Arguments") as IEnumerable;
            if (args == null) throw new Exception("Arguments collection is null on handler for '" + eventName + "'");
            var sb = new StringBuilder();
            int count = 0; object target = null;
            foreach (var arg in args)
            {
                count++;
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                sb.AppendLine("arg[" + (count - 1) + "] param=" + paramName);
                if (string.Equals(paramName, argName, StringComparison.OrdinalIgnoreCase)) target = arg;
            }
            if (target == null) throw new Exception("argument not found for param '" + argName + "' (" + count + " args present)");
            bool mapped = false;
            try { CallMethod(target, "SetValue", new object[] { value }, 1); sb.AppendLine("SetValue(\"" + value + "\") OK"); mapped = true; }
            catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("SetValue err: " + r.Message); }
            if (!mapped) throw new Exception("could not set argument value: " + sb.ToString());
            return sb.ToString();
        });
    }

    // add_screen_aggregate_filter: DataTable.AddFilter(filter) on a SCREEN aggregate
    // (screen twin of the block-side add_aggregate_filter). Undo unit.
    static string AddScreenAggregateFilter(string module, string screen, string name, string filter)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(filter)) return Json(new { ok = false, error = "screen, name and filter required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, null, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name + " on screen " + screen });
        return RunCmd(module, "add screen aggregate filter", es2 =>
        {
            var table = GetProp(agg, "Table");
            if (table == null) throw new Exception("aggregate has no Table");
            CallMethod(table, "AddFilter", new object[] { filter }, 1);
            return "added filter '" + filter + "' on '" + name + "' via Table.AddFilter";
        });
    }

    // add_aggregate_calculated_attr: CreateCalculatedAttribute(name, key) + DataType on a
    // screen/block aggregate. Undo unit.
    static string AddAggregateCalculatedAttr(string module, string screen, string block, string name, string attrName, string type)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name, attrName and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "add aggregate calculated attr", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = CallMethod(agg, "CreateCalculatedAttribute", new object[] { attrName, key }, 2);
            if (created == null) throw new Exception("CreateCalculatedAttribute returned null");
            try { SetProp(created, "DataType", dataType); } catch { }
            return "added calculated attribute " + attrName + " : " + type + " on '" + name + "'";
        });
    }

    // add_foreach_node: create an IForEachNode in an action flow + SetRecordList (and optional
    // maxIterations/startIndex). Wire Target (body entry) / CycleTarget (exit) afterwards with
    // live_set_connector_target after live_probe_node_connectors.
    static string AddForeachNode(string module, string action, string recordList, string maxIterations, string startIndex)
    {
        if (string.IsNullOrEmpty(recordList)) return Json(new { ok = false, error = "recordList required" });
        return LiveEdit(module, action, "add foreach node", (act, es) =>
        {
            object node;
            try { node = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IForEachNode"); }
            catch (Exception e) { throw new Exception("CreateNode IForEachNode failed: " + FirstMsg(e)); }
            CallMethod(node, "SetRecordList", new object[] { recordList }, 1);
            var extra = new List<string>();
            if (!string.IsNullOrEmpty(maxIterations)) { try { CallMethod(node, "SetMaximumIterations", new object[] { maxIterations }, 1); extra.Add("Max=" + maxIterations); } catch (Exception e) { extra.Add("Max(not set:" + FirstMsg(e) + ")"); } }
            if (!string.IsNullOrEmpty(startIndex)) { try { CallMethod(node, "SetStartIndex", new object[] { startIndex }, 1); extra.Add("Start=" + startIndex); } catch (Exception e) { extra.Add("Start(not set:" + FirstMsg(e) + ")"); } }
            return "created ForEach List=" + recordList + " (" + node.GetType().Name + ")" + (extra.Count > 0 ? " [" + string.Join(",", extra) + "]" : "") + " - wire Target/CycleTarget next";
        });
    }

    // add_screen_input_param: create an INPUT PARAMETER on a screen (receiving side of
    // navigation params, e.g. OrderId). Factory hunt: screen.CreateInputParameter, then
    // InputParameters collection factories. Undo unit.
    static string AddScreenInputParam(string module, string screen, string name, string type)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "screen, name and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "add screen input param", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = null; string via = null; var errs = new List<string>();
            try { created = CallMethod(sc, "CreateInputParameter", new object[] { name, key }, 2); via = "screen.CreateInputParameter"; }
            catch (Exception e) { errs.Add("screen.CreateInputParameter: " + FirstMsg(e)); }
            if (created == null)
            {
                var coll = GetProp(sc, "InputParameters");
                if (coll != null)
                    foreach (var mName in new[] { "Create", "Add", "CreateInputParameter" })
                    {
                        var m = FindMethod(coll, mName, 2);
                        if (m == null) continue;
                        try { created = m.Invoke(coll, new object[] { name, key }); via = "InputParameters." + mName; break; }
                        catch (Exception e) { errs.Add("InputParameters." + mName + ": " + FirstMsg(e)); }
                    }
            }
            if (created == null) throw new Exception("no input-param factory worked: " + string.Join(" ; ", errs));
            try { SetProp(created, "DataType", dataType); } catch (Exception e) { throw new Exception("created but DataType not settable: " + FirstMsg(e)); }
            return "added screen input '" + name + "' : " + type + " on '" + screen + "' via " + via;
        });
    }

    // ============ Deploy-5 fixers (delta-verified: compare verify count before/after) ============
    static int CountVerify(object target)
    {
        try
        {
            object res = null;
            try { res = CallMethod(target, "GetVerifyErrors", null, 0); }
            catch { res = CallMethod(target, "GetValidationMessages", new object[] { true }, 1); }
            int n = 0;
            var en = res as IEnumerable;
            if (en != null) foreach (var m in en) n++;
            return n;
        }
        catch { return -1; }
    }

    // fix_style_literal: store a widget Style class as a QUOTED text literal in the Style
    // CustomProperty (ParsedExpression <Text Value="class"/>). Tries strategies in order and
    // keeps the FIRST that reduces the parent screen/block verify count. Screen OR block.
    static string FixStyleLiteral(string module, string screen, string block, string widget, string cssClass)
    {
        if (string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(cssClass)) return Json(new { ok = false, error = "widget and cssClass required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        if (root == null) return Json(new { ok = false, error = !string.IsNullOrEmpty(block) ? "web block not found: " + block : "screen not found: " + screen });
        var w = FindWidget(root, widget);
        if (w == null) return Json(new { ok = false, error = "widget not found: " + widget });
        string quoted = "\"" + cssClass + "\"";
        int before = CountVerify(root);
        var log = new List<string>();
        string winner = null;
        foreach (var strat in new[] { "quoted-expr", "propvalue", "setprop", "veset" })
        {
            string r = RunCmd(module, "fix style literal " + strat, es2 =>
            {
                var w2 = FindWidget(!string.IsNullOrEmpty(block) ? FindBlock(es2, block) : FindScreen(es2, screen), widget);
                if (w2 == null) throw new Exception("widget not found: " + widget);
                var cps = GetProp(w2, "CustomProperties") as IEnumerable;
                if (cps == null) throw new Exception("no CustomProperties on " + w2.GetType().Name);
                object cp = null;
                foreach (var c in cps)
                {
                    string pn = null;
                    try { pn = GetProp(c, "PropertyName") as string; } catch { }
                    if (pn == "Style") { cp = c; break; }
                }
                if (cp == null) throw new Exception("no Style CustomProperty on " + widget);
                if (strat == "quoted-expr") CallMethodTyped(cp, "SetValueExpression", new object[] { quoted });
                else if (strat == "propvalue") CallMethod(cp, "SetPropertyValue", new object[] { "Value", quoted }, 2);
                else if (strat == "setprop") SetProp(cp, "Value", quoted);
                else { var ve = GetProp(cp, "ValueExpression"); if (ve == null) throw new Exception("no ValueExpression"); CallMethodTyped(ve, "SetValue", new object[] { quoted }); }
                return strat + " applied";
            });
            log.Add(strat + ":" + r);
            var w3 = FindWidget(!string.IsNullOrEmpty(block) ? FindBlock(FindEspace(module), block) : FindScreen(FindEspace(module), screen), widget);
            int after = CountVerify(!string.IsNullOrEmpty(block) ? FindBlock(FindEspace(module), block) : FindScreen(FindEspace(module), screen));
            if (after >= 0 && after < before) { winner = strat + " (errors " + before + "->" + after + ")"; break; }
        }
        return Json(new { ok = winner != null, winner = winner, errorsBefore = before, log = log.ToArray() });
    }

    // set_aggregate_calc_formula: set the formula Value on a calculated attribute of a
    // screen/block aggregate. Tries Value/Expression/Formula/Definition props + nested
    // ValueExpression.SetValue; keeps the first that reduces parent verify count.
    static string SetAggregateCalcFormula(string module, string screen, string block, string name, string attrName, string formula)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(formula)) return Json(new { ok = false, error = "name, attrName and formula required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        int before = CountVerify(root);
        var log = new List<string>();
        string winner = null;
        // SetValue(String) is the proven surface (CalculatedAttribute.Value is an
        // AbstractExpression object, not a settable string prop).
        string r0 = RunCmd(module, "set calc formula setvalue", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            var calcs = GetProp(a2, "CalculatedAttributes") as IEnumerable;
            if (calcs == null) throw new Exception("no CalculatedAttributes collection");
            object attr = null;
            foreach (var c in calcs)
                try { if ((GetProp(c, "Name") as string) == attrName) { attr = c; break; } } catch { }
            if (attr == null) throw new Exception("calculated attribute not found: " + attrName);
            CallMethod(attr, "SetValue", new object[] { formula }, 1);
            return "SetValue applied";
        });
        log.Add("setvalue:" + r0);
        {
            var es3 = FindEspace(module);
            var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
            int after = CountVerify(root3);
            if (after >= 0 && after < before) winner = "setvalue (errors " + before + "->" + after + ")";
        }
        foreach (var strat in new[] { "Value", "Expression", "Formula", "Definition", "veset" })
        {
            if (winner != null) break;
            string r = RunCmd(module, "set calc formula " + strat, es2 =>
            {
                var a2 = FindScreenAggregate(es2, screen, block, name);
                if (a2 == null) throw new Exception("aggregate not found: " + name);
                var calcs = GetProp(a2, "CalculatedAttributes") as IEnumerable;
                if (calcs == null) throw new Exception("no CalculatedAttributes collection");
                object attr = null;
                foreach (var c in calcs)
                    try { if ((GetProp(c, "Name") as string) == attrName) { attr = c; break; } } catch { }
                if (attr == null) throw new Exception("calculated attribute not found: " + attrName);
                if (strat == "veset")
                {
                    var ve = GetProp(attr, "ValueExpression");
                    if (ve == null) throw new Exception("no ValueExpression");
                    CallMethodTyped(ve, "SetValue", new object[] { formula });
                }
                else SetProp(attr, strat, formula);
                return strat + " applied";
            });
            log.Add(strat + ":" + r);
            var es3 = FindEspace(module);
            var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
            int after = CountVerify(root3);
            if (after >= 0 && after < before) { winner = strat + " (errors " + before + "->" + after + ")"; break; }
        }
        return Json(new { ok = winner != null, winner = winner, errorsBefore = before, log = log.ToArray() });
    }

    // set_aggregate_param_type: set DataType on an implicit/query parameter of a screen/block
    // aggregate (e.g. the DataSetImplicitParameter created by a filter/sort). Searches
    // ImplicitParameters + Parameters + Arguments collections by name.
    static string SetAggregateParamType(string module, string screen, string block, string name, string paramName, string type)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name, paramName and type required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        return RunCmd(module, "set aggregate param type", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            foreach (var collName in new[] { "ImplicitParameters", "Parameters", "Arguments" })
            {
                object coll = null;
                try { coll = GetProp(a2, collName); } catch { continue; }
                var en = coll as IEnumerable;
                if (en == null) continue;
                var items = new List<object>();
                foreach (var p in en) items.Add(p);
                object pick = null;
                foreach (var p in items)
                {
                    string pn = null;
                    try { pn = GetProp(p, "Name") as string; } catch { continue; }
                    if (pn == paramName) { pick = p; break; }
                }
                // Fallback: single (usually unnamed "?") item + empty paramName = that item.
                if (pick == null && items.Count == 1 && string.IsNullOrEmpty(paramName)) pick = items[0];
                if (pick == null) continue;
                SetProp(pick, "DataType", dataType);
                return "set " + name + "." + paramName + " DataType -> " + type + " (via " + collName + (items.Count == 1 ? ", single-item fallback" : "") + ")";
            }
            throw new Exception("parameter not found: " + paramName + " (checked ImplicitParameters/Parameters/Arguments)");
        });
    }

    // set_timer_action: point a Timer's wake Action at a server action
    // (DefaultProperty Action:IGlobalExecuteActionTarget). Resolves the action via
    // FindAction (server/client actions). Undo unit.
    static string SetTimerAction(string module, string timer, string action)
    {
        if (string.IsNullOrEmpty(timer) || string.IsNullOrEmpty(action)) return Json(new { ok = false, error = "timer and action required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object tm = null;
        var timers = GetProp(es, "Timers") as IEnumerable;
        if (timers != null)
            foreach (var t in timers)
                try { if ((GetProp(t, "Name") as string) == timer) { tm = t; break; } } catch { }
        if (tm == null) return Json(new { ok = false, error = "timer not found: " + timer });
        var act = FindAction(es, action);
        if (act == null) return Json(new { ok = false, error = "action not found: " + action });
        return RunCmd(module, "set timer action", es2 =>
        {
            var timers2 = GetProp(es2, "Timers") as IEnumerable;
            object target = null;
            if (timers2 != null)
                foreach (var t in timers2)
                    try { if ((GetProp(t, "Name") as string) == timer) { target = t; break; } } catch { }
            if (target == null) throw new Exception("timer not found: " + timer);
            var act2 = FindAction(es2, action);
            if (act2 == null) throw new Exception("action not found: " + action);
            try { SetProp(target, "Action", act2); }
            catch (Exception e) { throw new Exception("Action not settable: " + FirstMsg(e)); }
            return "set timer '" + timer + "' wake action -> '" + action + "'";
        });
    }

    // grant_screen_permission: grant a role on a screen by DUPLICATING an existing Permission
    // entry (constructors refuse direct invocation) + repointing its Role. Undo unit.
    static string GrantScreenPermission(string module, string screen, string roleName)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(roleName)) return Json(new { ok = false, error = "screen and roleName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        object role = null;
        foreach (var collName in new[] { "Roles", "SystemRoles" })
        {
            var coll = GetProp(es, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var r in coll)
                try { if ((GetProp(r, "Name") as string) == roleName) { role = r; break; } } catch { }
            if (role != null) break;
        }
        if (role == null) return Json(new { ok = false, error = "role not found: " + roleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        return RunCmd(module, "grant screen permission", es2 =>
        {
            var sc2 = FindScreen(es2, screen);
            if (sc2 == null) throw new Exception("screen not found: " + screen);
            var permColl = GetProp(sc2, "Permissions") as IEnumerable;
            if (permColl == null) throw new Exception("screen has no Permissions collection");
            object template = null;
            foreach (var p in permColl) { template = p; break; }
            string grantVia = null;
            object role2 = null;
            foreach (var collName in new[] { "Roles", "SystemRoles" })
            {
                var coll = GetProp(es2, collName) as IEnumerable;
                if (coll == null) continue;
                foreach (var r in coll)
                    try { if ((GetProp(r, "Name") as string) == roleName) { role2 = r; break; } } catch { }
                if (role2 != null) break;
            }
            if (role2 == null) throw new Exception("role not found: " + roleName);
            int permsBefore = 0;
            foreach (var p in permColl) permsBefore++;
            object clone = null;
            if (template != null)
            {
                // Path 1: clone an existing Permission on THIS screen, then re-target Role.
                var dupMethod = FindDupMethod(ms, template);
                clone = dupMethod.Invoke(ms, new object[] { template, sc2 });
                grantVia = "Duplicate(Permission)+Role";
            }
            else
            {
                // Path 2: no Permission on this screen - clone from ANY other screen in the module.
                foreach (var flow in WebFlowsOf(es2))
                {
                    var nodes = GetProp(flow, "Nodes") as IEnumerable;
                    if (nodes == null) continue;
                    foreach (var n in nodes)
                    {
                        if (!IsScreen(n) || object.ReferenceEquals(n, sc2)) continue;
                        var perms2 = GetProp(n, "Permissions") as IEnumerable;
                        if (perms2 == null) continue;
                        foreach (var p2 in perms2) { template = p2; break; }
                        if (template != null) break;
                    }
                    if (template != null) break;
                }
                if (template != null)
                {
                    var dupMethod = FindDupMethod(ms, template);
                    clone = dupMethod.Invoke(ms, new object[] { template, sc2 });
                    grantVia = "Duplicate(Permission from other screen)+Role";
                }
            }
            if (clone == null)
            {
                // Path 3: no template anywhere - hunt a factory on the Permissions collection
                // object itself (Create()/Create(IKey)), then the Permission ctor.
                var collObj = GetProp(sc2, "Permissions");
                var tried = new List<string>();
                foreach (var m in collObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "Create" && m.Name != "Add") continue;
                    var ps = m.GetParameters();
                    if (ps.Length > 1) continue;
                    try
                    {
                        clone = ps.Length == 0 ? m.Invoke(collObj, null)
                              : m.Invoke(collObj, new object[] { CallMethod(ms, "NewKey", null, 0) });
                        if (clone != null) { grantVia = "Permissions." + m.Name + "(" + ps.Length + " args)"; break; }
                    }
                    catch (Exception e) { tried.Add(m.Name + "/" + ps.Length + ": " + FirstMsg(e)); }
                }
                if (clone == null)
                {
                    var permType = FindType("ServiceStudio.Model.Permission");
                    foreach (var ctor in permType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var ps = ctor.GetParameters();
                        object[] args = null;
                        if (ps.Length == 0) args = new object[0];
                        else if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(sc2.GetType())) args = new object[] { sc2 };
                        else if (ps.Length == 2 && ps[0].ParameterType.IsAssignableFrom(sc2.GetType()) && ps[1].ParameterType == typeof(string)) args = new object[] { sc2, roleName };
                        if (args == null) continue;
                        try { clone = ctor.Invoke(args); grantVia = "ctor(" + ps.Length + ")"; break; }
                        catch (Exception e) { tried.Add("ctor/" + ps.Length + ": " + FirstMsg(e)); }
                    }
                }
                if (clone == null) throw new Exception("no way to create a Permission: " + string.Join(" | ", tried));
            }
            try { SetProp(clone, "Role", role2); }
            catch (Exception e)
            {
                // Role via the static _roleSetter delegate fallback.
                bool okRole = false;
                foreach (var t in AllTypes(clone.GetType()))
                {
                    var df = t.GetField("_roleSetter", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (df != null)
                    {
                        try { (df.GetValue(null) as Delegate)?.DynamicInvoke(clone, role2); okRole = true; break; }
                        catch (Exception e2) { throw new Exception("Role set failed (prop + delegate): " + FirstMsg(e) + " / " + FirstMsg(e2)); }
                    }
                }
                if (!okRole) throw new Exception("Role not settable on cloned Permission: " + FirstMsg(e));
            }
            try { CallMethod(clone, "AddDependentPermissions", null, 0); } catch { }
            // AddDependentPermissions can add a second Permission for the same role - dedupe so a
            // grant never yields two entries for one role on one screen (UI-consistent).
            int dedupeRemoved = 0; string dedupeErr = null;
            try
            {
                var pc = GetProp(sc2, "Permissions") as IEnumerable;
                if (pc != null)
                {
                    var seen = new HashSet<string>();
                    var extras = new List<object>();
                    foreach (var p in pc)
                    {
                        string rn = null;
                        try { var r = GetProp(p, "Role"); if (r != null) rn = GetProp(r, "Name") as string; } catch { }
                        if (rn == null) continue;
                        if (!seen.Add(rn)) extras.Add(p);
                    }
                    MethodInfo rm = null;
                    foreach (var m in pc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name != "Remove") continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 1) continue;
                        try { if (ps[0].ParameterType.IsAssignableFrom(extras.Count > 0 ? extras[0].GetType() : typeof(object))) { rm = m; break; } } catch { }
                    }
                    foreach (var p in extras)
                    {
                        try { if (rm != null) rm.Invoke(pc, new object[] { p }); else CallMethod(pc, "Remove", new object[] { p }, 1); dedupeRemoved++; }
                        catch (Exception e) { dedupeErr = dedupeErr ?? FirstMsg(e); }
                    }
                }
            }
            catch (Exception e) { dedupeErr = "dedupe loop: " + FirstMsg(e); }
            int permsAfter = 0;
            var permColl2 = GetProp(sc2, "Permissions") as IEnumerable;
            if (permColl2 != null) foreach (var p in permColl2) permsAfter++;
            string grantedRole = null;
            try { grantedRole = GetProp(GetProp(clone, "Role"), "Name") as string; } catch { }
            if (grantedRole != roleName) throw new Exception("read-back mismatch: Permission.Role=" + (grantedRole ?? "null"));
            return "granted role '" + roleName + "' on '" + screen + "' via " + grantVia + " (Permissions " + permsBefore + "->" + permsAfter + ") dedupeRemoved=" + dedupeRemoved + (dedupeErr != null ? " dedupeErr=" + dedupeErr : "");
        });
    }

    // FindDupMethod: the IModelServices.Duplicate overload assignable from source's type.
    static MethodInfo FindDupMethod(object ms, object source)
    {
        foreach (var m in ms.GetType().GetMethods())
        {
            if (m.Name != "Duplicate") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType.IsAssignableFrom(source.GetType())) return m;
        }
        throw new Exception("Duplicate method not found");
    }
    static string RemoveScreenPermission(string module, string screen, string roleName)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(roleName)) return Json(new { ok = false, error = "screen and roleName required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        return RunCmd(module, "remove screen permission", es2 =>
        {
            var sc2 = FindScreen(es2, screen);
            if (sc2 == null) throw new Exception("screen not found: " + screen);
            var permColl = GetProp(sc2, "Permissions");
            if (permColl == null) throw new Exception("screen has no Permissions collection");
            var en = permColl as IEnumerable;
            if (en == null) throw new Exception("Permissions not enumerable");
            var doomed = new List<object>();
            foreach (var p in en)
            {
                string rn = null;
                try
                {
                    var r = GetProp(p, "Role");
                    if (r != null) rn = GetProp(r, "Name") as string;
                }
                catch { }
                if (rn == roleName) doomed.Add(p);
            }
            if (doomed.Count == 0) return "no Permission for role '" + roleName + "' on '" + screen + "' (nothing removed)";
            int removed = 0; var errs = new List<string>();
            // Resolve Remove(Permission)-shaped overload by assignability (NOT Remove(ObjectKey)).
            MethodInfo rmMethod = null;
            foreach (var m in permColl.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Remove") continue;
                var ps = m.GetParameters();
                if (ps.Length != 1) continue;
                try { if (ps[0].ParameterType.IsAssignableFrom(doomed[0].GetType())) { rmMethod = m; break; } } catch { }
            }
            foreach (var d in doomed)
            {
                try
                {
                    if (rmMethod != null) rmMethod.Invoke(permColl, new object[] { d });
                    else CallMethod(permColl, "Remove", new object[] { d }, 1);
                    removed++;
                }
                catch (Exception e) { errs.Add(FirstMsg(e)); }
            }
            return "removed " + removed + "/" + doomed.Count + " Permission(s) for '" + roleName + "'" + (errs.Count > 0 ? " errs: " + string.Join(";", errs) : "");
        });
    }

    // read_screen_permissions: list each Permission entry's role name (read-only).
    static string ReadScreenPermissions(string module, string screen)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        var out_ = new List<object>();
        try
        {
            var permColl = GetProp(sc, "Permissions") as IEnumerable;
            if (permColl == null) return Json(new { ok = false, error = "no Permissions collection" });
            int i = 0;
            foreach (var p in permColl)
            {
                string rn = null; string via = null;
                try
                {
                    var r = GetProp(p, "Role");
                    if (r != null)
                    {
                        try { rn = GetProp(r, "Name") as string; via = "Role.Name"; } catch { }
                        if (rn == null) { try { rn = r.ToString(); via = "Role.ToString"; } catch { } }
                    }
                }
                catch (Exception e) { via = "Role-err:" + FirstMsg(e); }
                out_.Add(new { index = i, role = rn, via = via, itemType = p.GetType().Name });
                i++;
            }
        }
        catch (Exception e) { return Json(new { ok = false, error = "read failed: " + FirstMsg(e) }); }
        return Json(new { ok = true, screen = screen, count = out_.Count, grants = out_.ToArray() });
    }

    // ============ Readback micro-commands (ground truth from UI-created reference) ============
    // delete_aggregate_filter: remove one filter by index from a screen/block aggregate's
    // query Filters. Undo unit. Index from readback (no filter reader exists — indices follow
    // creation order). Needed for dynamic search (replace filter text).
    static string DeleteAggregateFilter(string module, string screen, string block, string name, int index)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        return RunCmd(module, "delete aggregate filter", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            var table = GetProp(a2, "Table");
            if (table == null) throw new Exception("aggregate has no Table");
            object combine = null;
            foreach (var pn in new[] { "RootOperation", "DetailsRootOperation" })
            {
                try { combine = GetProp(table, pn); } catch { }
                if (combine != null) break;
            }
            if (combine == null) throw new Exception("no CombineSources");
            var filters = GetProp(combine, "Filters") as IEnumerable;
            if (filters == null) throw new Exception("no Filters collection");
            var items = new List<object>();
            foreach (var f in filters) items.Add(f);
            if (index < 0 || index >= items.Count) throw new Exception("filter index " + index + " out of range (count=" + items.Count + ")");
            CallMethod(items[index], "Delete", null, 0);
            return "deleted filter[" + index + "] from '" + name + "' (" + items.Count + "->" + (items.Count - 1) + ")";
        });
    }

    // add_aggregate_source: add an ADDITIONAL source entity to a screen/block aggregate
    // (for joins; the first source comes from create/set-source). Reuses SetAggregateSource
    // paths; reports Sources count before/after to prove append-vs-replace.
    static string AddAggregateSource(string module, string screen, string block, string name, string entityName)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName)) return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        object entity = FindEntity(es, entityName);
        if (entity == null) return Json(new { ok = false, error = "local entity not found: " + entityName + " (joins currently support local entities only)" });
        int before = -1;
        try
        {
            var srcs = GetProp(agg, "Sources") as IEnumerable;
            if (srcs != null) { before = 0; foreach (var s in srcs) before++; }
        }
        catch { }
        string rep = null;
        string r = RunCmd(module, "add aggregate source", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            var ents = GetProp(es2, "Entities") as IEnumerable;
            object ent = null;
            if (ents != null)
                foreach (var e in ents)
                    try { if ((GetProp(e, "Name") as string) == entityName) { ent = e; break; } } catch { }
            if (ent == null) throw new Exception("entity not found: " + entityName);
            var table = GetProp(a2, "Table");
            var tried = new List<string>();
            // Path A: table.GetAddSourceOperation(entity) — get-or-create with registration.
            if (table != null)
            {
                try
                {
                    var g = FindMethod(table, "GetAddSourceOperation", 1);
                    if (g != null) { var op = g.Invoke(table, new object[] { ent }); tried.Add("GetAddSourceOperation->" + (op?.GetType().Name ?? "null")); }
                    else tried.Add("GetAddSourceOperation:missing");
                }
                catch (Exception e) { tried.Add("GetAddSourceOperation:" + FirstMsg(e)); }
                // Path B: table.AddSource(entity) directly.
                try { CallMethod(table, "AddSource", new object[] { ent }, 1); tried.Add("AddSource:ok"); }
                catch (Exception e) { tried.Add("AddSource:" + FirstMsg(e)); }
            }
            // Path C: legacy SetAggregateSource paths (proven for first source).
            string legacy = null;
            try { legacy = SetAggregateSource(a2, ent); tried.Add("legacy:" + legacy); }
            catch (Exception e) { tried.Add("legacy:" + FirstMsg(e)); }
            rep = string.Join(" | ", tried);
            return rep;
        });
        int after = -1;
        try
        {
            var es3 = FindEspace(module);
            var a3 = FindScreenAggregate(es3, screen, block, name);
            var srcs = GetProp(a3, "Sources") as IEnumerable;
            if (srcs != null) { after = 0; foreach (var s in srcs) after++; }
        }
        catch { }
        return Json(new { ok = true, report = r, sourcesBefore = before, sourcesAfter = after });
    }

    // add_aggregate_join: CreateJoin() on a screen/block aggregate, wire LeftSource/
    // RightSource to source operations by index, SetCondition(join expression), delta-verify.
    static string AddAggregateJoin(string module, string screen, string block, string name, string condition, string leftIndex, string rightIndex)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(condition)) return Json(new { ok = false, error = "name and condition required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        int li = 0, ri = 1;
        int.TryParse(leftIndex ?? "", out li);
        if (!int.TryParse(rightIndex ?? "", out ri)) ri = 1;
        object root = !string.IsNullOrEmpty(block) ? FindBlock(es, block) : FindScreen(es, screen);
        int before = CountVerify(root);
        var log = new List<string>();
        string winner = null;
        string r = RunCmd(module, "add aggregate join", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            object join = CallMethod(a2, "CreateJoin", null, 0);
            if (join == null) throw new Exception("CreateJoin returned null");
            var srcs = GetProp(a2, "Sources") as IEnumerable;
            var items = new List<object>();
            if (srcs != null) foreach (var s in srcs) items.Add(s);
            if (li < 0 || li >= items.Count) throw new Exception("leftIndex " + li + " out of range (sources=" + items.Count + ")");
            if (ri < 0 || ri >= items.Count) throw new Exception("rightIndex " + ri + " out of range (sources=" + items.Count + ")");
            try { SetProp(join, "LeftSource", items[li]); } catch (Exception e) { throw new Exception("LeftSource not settable: " + FirstMsg(e)); }
            try { SetProp(join, "RightSource", items[ri]); } catch (Exception e) { throw new Exception("RightSource not settable: " + FirstMsg(e)); }
            try { CallMethod(join, "SetCondition", new object[] { condition }, 1); }
            catch (Exception e) { throw new Exception("SetCondition failed: " + FirstMsg(e)); }
            // Attach to Joins collection if CreateJoin didn't (check count delta).
            int jc0 = -1, jc1 = -1;
            try { var jc = GetProp(a2, "Joins") as IEnumerable; if (jc != null) { jc0 = 0; foreach (var j in jc) jc0++; } } catch { }
            if (jc0 >= 0)
            {
                try
                {
                    var jc = GetProp(a2, "Joins");
                    var addM = FindMethod(jc, "Add", 1);
                    if (addM != null) { try { addM.Invoke(jc, new object[] { join }); } catch { } }
                    var jc2 = GetProp(a2, "Joins") as IEnumerable;
                    if (jc2 != null) { jc1 = 0; foreach (var j in jc2) jc1++; }
                }
                catch { }
            }
            return "CreateJoin Left=" + li + " Right=" + ri + " Condition=" + condition + " (joins " + jc0 + "->" + jc1 + ")";
        });
        log.Add(r);
        var es3 = FindEspace(module);
        var root3 = !string.IsNullOrEmpty(block) ? FindBlock(es3, block) : FindScreen(es3, screen);
        int after = CountVerify(root3);
        if (after >= 0 && after <= before) winner = "applied (errors " + before + "->" + after + ")";
        return Json(new { ok = winner != null, winner = winner, errorsBefore = before, log = log.ToArray() });
    }

    // set_link_params: set navigation parameter values on a Link widget's OnClick destination
    // (e.g. passing OrderId to a Detail screen). Mirrors SetRaiseEventArg: locate each
    // Argument whose Parameter.Name matches, SetValue(expr). params = "Name=Value,...".
    // The destination screen must already have the input params (add_screen_input_param).
    static string SetLinkParams(string module, string screen, string widget, string paramsCsv)
    {
        if (string.IsNullOrEmpty(screen) || string.IsNullOrEmpty(widget) || string.IsNullOrEmpty(paramsCsv)) return Json(new { ok = false, error = "screen, widget and params required" });
        return RunCmd(module, "set link params", es =>
        {
            var sc = FindScreen(es, screen);
            if (sc == null) throw new Exception("screen not found: " + screen);
            var w = FindWidget(sc, widget);
            if (w == null) throw new Exception("widget not found: " + widget);
            var onClick = GetProp(w, "OnClick");
            if (onClick == null)
            {
                var coc = FindMethod(w, "CreateOnClick", 0);
                if (coc == null) throw new Exception("link has no OnClick and no CreateOnClick");
                onClick = coc.Invoke(w, null);
            }
            var args = GetProp(onClick, "Arguments") as IEnumerable;
            if (args == null) throw new Exception("OnClick has no Arguments collection (destination may have no input params)");
            var sb = new StringBuilder();
            int count = 0;
            var argList = new List<object>();
            foreach (var arg in args)
            {
                count++;
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                sb.AppendLine("arg[" + (count - 1) + "] param=" + paramName);
                argList.Add(arg);
            }
            var report = new List<string>();
            foreach (var pair in paramsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) { report.Add(pair.Trim() + ":SKIP(no =)"); continue; }
                var an = pair.Substring(0, eq).Trim();
                var av = pair.Substring(eq + 1).Trim();
                object target = null;
                foreach (var arg in argList)
                {
                    var param = GetProp(arg, "Parameter");
                    var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                    if (string.Equals(paramName, an, StringComparison.OrdinalIgnoreCase)) { target = arg; break; }
                }
                if (target == null) { report.Add(an + ":NO-SLOT(" + count + " args present)"); continue; }
                bool mapped = false; string how = null;
                try { CallMethod(target, "SetValue", new object[] { av }, 1); mapped = true; how = "SetValue"; }
                catch { }
                if (!mapped)
                {
                    try { var ve = GetProp(target, "Value"); if (ve != null) { SetProp(ve, "Text", av); mapped = true; how = "Value.Text"; } } catch { }
                }
                report.Add(an + "=" + av + (mapped ? ":OK(" + how + ")" : ":FAILED"));
            }
            sb.Append(string.Join(";", report));
            return sb.ToString();
        });
    }

    // debug_create_surface: read-only diagnostics for the service-action creation puzzle.
    // Reports reflection-visible ctors, folder resolution, and collection concrete types.
    static string DebugCreateSurface(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var t = FindType("ServiceStudio.Model.Flows+ServiceAPIMethod");
        var ctors = new List<string>();
        if (t != null)
            foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var ps = ctor.GetParameters();
                var plist = new List<string>();
                foreach (var p in ps) plist.Add(p.ParameterType.FullName + " " + p.Name);
                ctors.Add((ctor.IsPublic ? "public" : "nonpublic") + " .ctor(" + string.Join(",", plist) + ") [declaring:" + ctor.DeclaringType.FullName + "]");
            }
        if (t != null) ctors.Add("REFLECTED-TYPE-ASM:" + t.Assembly.FullName);
        var folders = new List<string>();
        try
        {
            var fcoll = GetProp(es, "Folders") as IEnumerable;
            if (fcoll != null)
                foreach (var f in fcoll)
                {
                    string nm = null; object sa = null; object ef = null;
                    try { nm = GetProp(f, "Name") as string; } catch { }
                    try { sa = GetProp(f, "IsAServerActionsFolder"); } catch { }
                    try { ef = GetProp(f, "ESpaceTreeFolder"); } catch { }
                    folders.Add((nm ?? "?") + " IsA=" + (sa?.ToString() ?? "?") + " Tree=" + (ef?.ToString() ?? "?") + " type=" + f.GetType().FullName);
                }
        }
        catch (Exception e) { folders.Add("ERR:" + FirstMsg(e)); }
        string adapterType = null, actionsPropType = null;
        try { var a = GetProp(es, "ServiceAPIMethods"); if (a != null) adapterType = a.GetType().FullName; } catch (Exception e) { adapterType = "ERR:" + FirstMsg(e); }
        try { var a = GetProp(es, "ServiceActions"); if (a != null) actionsPropType = a.GetType().FullName; } catch (Exception e) { actionsPropType = "ERR:" + FirstMsg(e); }
        return Json(new { ok = true, ctors = ctors.ToArray(), folders = folders.ToArray(), adapterType = adapterType, actionsPropType = actionsPropType });
    }

    // delete_aggregate_sort: remove one sort by index (see read_aggregate_sorts) from a
    // screen/block aggregate's query Sorts. Undo unit. Needed to clean up failed trials.
    static string DeleteAggregateSort(string module, string screen, string block, string name, int index)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen or block required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        return RunCmd(module, "delete aggregate sort", es2 =>
        {
            var a2 = FindScreenAggregate(es2, screen, block, name);
            if (a2 == null) throw new Exception("aggregate not found: " + name);
            var table = GetProp(a2, "Table");
            if (table == null) throw new Exception("aggregate has no Table");
            object combine = null;
            foreach (var pn in new[] { "RootOperation", "DetailsRootOperation" })
            {
                try { combine = GetProp(table, pn); } catch { }
                if (combine != null) break;
            }
            if (combine == null) throw new Exception("no CombineSources");
            var sorts = GetProp(combine, "Sorts") as IEnumerable;
            if (sorts == null) throw new Exception("no Sorts collection");
            var items = new List<object>();
            foreach (var s in sorts) items.Add(s);
            if (index < 0 || index >= items.Count) throw new Exception("sort index " + index + " out of range (count=" + items.Count + ")");
            CallMethod(items[index], "Delete", null, 0);
            return "deleted sort[" + index + "] from '" + name + "' (" + items.Count + "->" + (items.Count - 1) + ")";
        });
    }

    static string ExprText(object expr)
    {
        if (expr == null) return null;
        if (expr is string s) return s;
        try { var t = GetProp(expr, "Text") as string; if (t != null) return t; } catch { }
        try { return expr.ToString(); } catch { return "?"; }
    }

    static string ReadAggregateSorts(string module, string screen, string block, string name)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        var out_ = new List<object>();
        try
        {
            var sorts = GetProp(agg, "Sorts") as IEnumerable;
            if (sorts != null)
            {
                int i = 0;
                foreach (var s in sorts)
                {
                    object orig = null, dir = null, dyn = null;
                    try { orig = GetProp(s, "OriginalAttribute"); } catch { }
                    try { dir = GetProp(s, "Sort"); } catch { }
                    try { dyn = GetProp(s, "IsDynamic"); } catch { }
                    out_.Add(new { index = i, type = s.GetType().Name, originalAttribute = ExprText(orig), direction = dir?.ToString(), isDynamic = dyn?.ToString() });
                    i++;
                }
            }
        }
        catch (Exception e) { return Json(new { ok = false, error = "read sorts failed: " + FirstMsg(e) }); }
        return Json(new { ok = true, aggregate = name, sortCount = out_.Count, sorts = out_.ToArray() });
    }

    static string ReadAggregateCalcs(string module, string screen, string block, string name)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        var out_ = new List<object>();
        try
        {
            var calcs = GetProp(agg, "CalculatedAttributes") as IEnumerable;
            if (calcs != null)
                foreach (var c in calcs)
                {
                    string nm = null; object val = null; object typ = null;
                    try { nm = GetProp(c, "Name") as string; } catch { }
                    try { val = GetProp(c, "Value"); } catch { }
                    try { typ = GetProp(c, "Type"); } catch { }
                    out_.Add(new { name = nm, type = TypeLabel(typ), value = ExprText(val) });
                }
        }
        catch (Exception e) { return Json(new { ok = false, error = "read calcs failed: " + FirstMsg(e) }); }
        return Json(new { ok = true, aggregate = name, calcCount = out_.Count, calcs = out_.ToArray() });
    }

    static string ReadAggregateParams(string module, string screen, string block, string name)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var agg = FindScreenAggregate(es, screen, block, name);
        if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
        var out_ = new List<object>();
        try
        {
            foreach (var collName in new[] { "ImplicitParameters", "Parameters", "Arguments" })
            {
                object coll = null;
                try { coll = GetProp(agg, collName); } catch { continue; }
                var en = coll as IEnumerable;
                if (en == null) continue;
                int i = 0;
                foreach (var p in en)
                {
                    string nm = null; object dt = null; object val = null;
                    try { nm = GetProp(p, "Name") as string; } catch { }
                    try { dt = GetProp(p, "DataType"); } catch { }
                    try { val = GetProp(p, "Value"); } catch { }
                    if (val == null) { try { val = GetProp(p, "DefaultValue"); } catch { } }
                    out_.Add(new { collection = collName, index = i, name = nm, dataType = TypeLabel(dt), value = ExprText(val), itemType = p.GetType().Name });
                    i++;
                }
            }
        }
        catch (Exception e) { return Json(new { ok = false, error = "read params failed: " + FirstMsg(e) }); }
        return Json(new { ok = true, aggregate = name, paramCount = out_.Count, @params = out_.ToArray() });
    }

    // ============ Deploy-6: sort rework, calc SetValue, param single-fallback, collection probe ============
    // probe_collection: read-only factory discovery on any es/screen/block collection
    // (methods with Create/Add/New/Remove/Delete + item count + first item types).
    // Use to discover Permission construction, ServiceAPIMethods factories, etc.
    static string ProbeCollection(string module, string screen, string block, string collection)
    {
        if (string.IsNullOrEmpty(collection)) return Json(new { ok = false, error = "collection required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object target = es;
        if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); if (target == null) return Json(new { ok = false, error = "web block not found: " + block }); }
        else if (!string.IsNullOrEmpty(screen)) { target = FindScreen(es, screen); if (target == null) return Json(new { ok = false, error = "screen not found: " + screen }); }
        object coll = null;
        try { coll = GetProp(target, collection); } catch (Exception e) { return Json(new { ok = false, error = "no collection '" + collection + "' on " + target.GetType().Name + ": " + FirstMsg(e) }); }
        if (coll == null) return Json(new { ok = false, error = "collection '" + collection + "' is null" });
        var methods = new List<string>();
        foreach (var m in coll.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!(m.Name.Contains("Create") || m.Name.Contains("Add") || m.Name.Contains("New") || m.Name.Contains("Insert") || m.Name.Contains("Remove") || m.Name.Contains("Delete") || m.Name == "Count" || m.Name.Contains("Factory"))) continue;
            var ps = m.GetParameters();
            var plist = new List<string>();
            foreach (var p in ps) plist.Add(p.ParameterType.Name + " " + p.Name);
            methods.Add(m.Name + "(" + string.Join(",", plist) + ")->" + m.ReturnType.Name);
        }
        var items = new List<string>();
        int count = 0;
        try
        {
            var en = coll as IEnumerable;
            if (en != null)
                foreach (var it in en)
                {
                    count++;
                    if (items.Count < 8)
                    {
                        string nm = null;
                        try { nm = GetProp(it, "Name") as string; } catch { }
                        items.Add(it.GetType().Name + (nm != null ? ":" + nm : ""));
                    }
                }
        }
        catch { }
        return Json(new { ok = true, collection = collection, collType = coll.GetType().FullName, methods = methods.ToArray(), itemCount = count, items = items.ToArray() });
    }

    // ============ P5: roles / screen security / site props / timers + lifecycle guard ============
    // create_role: IESpace.CreateRole(name, key). Dup-checked. Undo unit.
    static string CreateRole(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var roles = GetProp(es, "Roles") as IEnumerable;
        if (roles != null)
            foreach (var r in roles)
                try { if ((GetProp(r, "Name") as string) == name) return Json(new { ok = false, error = "role already exists: " + name }); } catch { }
        return RunCmd(module, "create role", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = CallMethod(es2, "CreateRole", new object[] { name, key }, 2);
            if (created == null) throw new Exception("CreateRole returned null");
            return "created role '" + name + "' (" + created.GetType().Name + ")";
        });
    }

    // set_screen_permissions: Public flag + role gating on a screen. roles = comma-separated
    // role names (must exist; create with create_role first). Best-effort per role, reported.
    static string SetScreenPermissions(string module, string screen, string roles, string isPublic)
    {
        if (string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "screen required" });
        if (string.IsNullOrEmpty(roles) && string.IsNullOrEmpty(isPublic)) return Json(new { ok = false, error = "roles or isPublic required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sc = FindScreen(es, screen);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
        return RunCmd(module, "set screen permissions", es2 =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(isPublic) && bool.TryParse(isPublic, out var pub))
            { SetProp(sc, "Public", pub); parts.Add("Public=" + pub); }
            if (!string.IsNullOrEmpty(roles))
            {
                var permColl = GetProp(sc, "Permissions");
                if (permColl == null) throw new Exception("screen has no Permissions collection");
                foreach (var rn in roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rname = rn.Trim();
                    object role = null;
                    foreach (var collName in new[] { "Roles", "SystemRoles" })
                    {
                        var coll = GetProp(es2, collName) as IEnumerable;
                        if (coll == null) continue;
                        foreach (var r in coll)
                            try { if ((GetProp(r, "Name") as string) == rname) { role = r; break; } } catch { }
                        if (role != null) break;
                    }
                    if (role == null) { parts.Add(rname + ":ROLE-NOT-FOUND"); continue; }
                    // Permissions holds Permission wrappers (SSCollection<Permission>.Add(Permission)).
                    // Construct via ctor hunt, set Role reference, Add. Report the winning shape.
                    bool added = false; string how = null;
                    try
                    {
                        var t = FindType("ServiceStudio.Model.Permission");
                        if (t == null) throw new Exception("Permission type not found");
                        // Proven ctor shape (decompiled): .ctor(WebScreen|NRNodes.WebScreen|IParent, AbstractRole).
                        // Pass the SCREEN object itself as parent + the role.
                        var sc0 = FindScreen(es, screen);
                        if (sc0 == null) throw new Exception("screen not found: " + screen);
                        object wrap = null;
                        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            var ps = ctor.GetParameters();
                            if (ps.Length != 2) continue;
                            try
                            {
                                if (ps[0].ParameterType.IsAssignableFrom(sc0.GetType()) && ps[1].ParameterType.IsAssignableFrom(role.GetType()))
                                { wrap = ctor.Invoke(new object[] { sc0, role }); how = "(" + ps[0].ParameterType.Name + ",Role)"; break; }
                            }
                            catch (Exception e) { how = "ctor-err: " + FirstMsg(e); }
                        }
                        if (wrap == null) throw new Exception("no (parent,Role) Permission ctor worked" + (how != null ? ": " + how : ""));
                        CallMethod(permColl, "Add", new object[] { wrap }, 1);
                        added = true; how += "+Add";
                    }
                    catch (Exception e) { parts.Add(rname + ":NOT-ADDED(" + FirstMsg(e) + ")"); continue; }
                    parts.Add(rname + (added ? ":added(" + how + ")" : ":NOT-ADDED(unknown)"));
                }
            }
            return "permissions on '" + screen + "': " + string.Join(", ", parts);
        });
    }

    // create_site_property: IESpace.CreateSiteProperty(shared, name, key) + DataType (+
    // best-effort default value). shared defaults true. Undo unit.
    static string CreateSiteProperty(string module, string name, string type, string shared, string defaultValue)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) return Json(new { ok = false, error = "name and type required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var props = GetProp(es, "SiteProperties") as IEnumerable;
        if (props != null)
            foreach (var p in props)
                try { if ((GetProp(p, "Name") as string) == name) return Json(new { ok = false, error = "site property already exists: " + name }); } catch { }
        object dataType = ResolveAttrDataType(es, type);
        if (dataType == null) return Json(new { ok = false, error = "type not found: " + type });
        bool isShared = !(shared ?? "").Equals("false", StringComparison.OrdinalIgnoreCase);
        return RunCmd(module, "create site property", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = CallMethod(es2, "CreateSiteProperty", new object[] { isShared, name, key }, 3);
            if (created == null) throw new Exception("CreateSiteProperty returned null");
            try { SetProp(created, "DataType", dataType); } catch (Exception e) { throw new Exception("created but DataType not settable: " + FirstMsg(e)); }
            string dv = "";
            if (!string.IsNullOrEmpty(defaultValue))
            {
                foreach (var pn in new[] { "Value", "DefaultValue" })
                {
                    try { SetProp(created, pn, defaultValue); dv = " " + pn + "=" + defaultValue; break; }
                    catch { }
                }
                if (dv == "") dv = " (default NOT set - set manually)";
            }
            return "created site property '" + name + "' : " + type + " shared=" + isShared + dv;
        });
    }

    // create_timer: IESpace.CreateTimer(name, key). Dup-checked. Undo unit. (G14)
    static string CreateTimer(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var timers = GetProp(es, "Timers") as IEnumerable;
        if (timers != null)
            foreach (var t in timers)
                try { if ((GetProp(t, "Name") as string) == name) return Json(new { ok = false, error = "timer already exists: " + name }); } catch { }
        return RunCmd(module, "create timer", es2 =>
        {
            var ms = ModelServices();
            var key = CallMethod(ms, "NewKey", null, 0);
            object created = CallMethod(es2, "CreateTimer", new object[] { name, key }, 2);
            if (created == null) throw new Exception("CreateTimer returned null");
            return "created timer '" + name + "' (" + created.GetType().Name + ") - set schedule + action in SS";
        });
    }

    // set_block_html_attr: set an HTML attribute on a widget INSIDE a web block (mirror of
    // SetHtmlAttr but FindBlock/FindWidget on the block's own tree instead of a screen).
    // IMPORTANT (learned the hard way): ExtendedProperty entries are keyed by 'AttributeName'
    // (NOT 'Name' - GetProp(e,"Name") returns null, so the old code NEVER found an existing entry
    // and AddExtendedProperty(String,String) always ADDED a second entry -> duplicates).
    // Correct behavior: 1) look up existing by AttributeName, EDIT its Value expression in place;
    // 2) only if absent, AddExtendedProperty (which creates a NEW entry).
    static string SetBlockHtmlAttr(string module, string block, string widget, string attrName, string attrValue)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(widget)) return Json(new { ok = false, error = "widget required" });
        if (string.IsNullOrEmpty(attrName)) return Json(new { ok = false, error = "attrName required" });
        return RunCmd(module, "set block html attr", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            // Strategy 1: find existing ExtendedProperty by AttributeName and edit it.
            object existing = null; string usedSurface = null;
            foreach (var collName in new[] { "ExtendedProperties", "Attributes" })
            {
                try
                {
                    var coll = GetProp(w, collName) as IEnumerable;
                    if (coll == null) continue;
                    foreach (var e in coll)
                    {
                        string en = null;
                        try { en = GetProp(e, "AttributeName") as string; } catch { }
                        if (en == null) { try { en = GetProp(e, "Name") as string; } catch { } }
                        if (string.Equals(en, attrName, StringComparison.OrdinalIgnoreCase)) { existing = e; break; }
                    }
                    if (existing != null) { usedSurface = collName + ":edit:" + existing.GetType().Name; break; }
                }
                catch (Exception e) { Log("SetBlockHtmlAttr " + collName + " lookup failed: " + e.Message); }
            }
            if (existing == null)
            {
                // Strategy 2: no existing entry -> add one (AddExtendedProperty creates a new entry).
                try
                {
                    var addM = FindMethod(w, "AddExtendedProperty", 2);
                    if (addM != null) { addM.Invoke(w, new object[] { attrName, attrValue ?? "" }); usedSurface = "AddExtendedProperty(" + attrName + ")"; }
                }
                catch (Exception e) { Log("SetBlockHtmlAttr AddExtendedProperty(2) failed: " + e.Message); }
                if (usedSurface == null)
                {
                    object key = null; try { key = CallMethod(ModelServices(), "NewKey", null, 0); } catch { }
                    var created = CreateExtendedPropertyOn(w, attrName, key);
                    if (created == null) created = CreateExtendedPropertyOn(w, attrName, null);
                    if (created != null)
                    {
                        existing = created;
                        usedSurface = "CreateExtendedProperty:" + created.GetType().Name;
                    }
                }
            }
            if (existing != null)
            {
                // Edit in place: set the Value expression (quoted text literal first - matches how SS
                // stores string attrs as <Text Value="...">, e.g. data-testid="profile-card").
                SetExtendedPropertyValue(existing, attrValue);
                if (usedSurface == null) usedSurface = "ExtendedProperties:edit:" + existing.GetType().Name;
            }
            if (usedSurface == null) throw new Exception("no attribute surface found for '" + attrName + "' on " + w.GetType().Name);
            return "set attr '" + attrName + "' = '" + (attrValue ?? "") + "' on '" + widget + "' in block '" + block + "' (" + usedSurface + ")";
        });
    }

    // delete_block_html_attr: remove an ExtendedProperty entry by its AttributeName from a
    // widget inside a web block (used to clean up duplicates created by the old buggy path).
    static string DeleteBlockHtmlAttr(string module, string block, string widget, string attrName)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        if (string.IsNullOrEmpty(attrName)) return Json(new { ok = false, error = "attrName required" });
        return RunCmd(module, "delete block html attr", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            object target = null;
            foreach (var collName in new[] { "ExtendedProperties", "Attributes" })
            {
                try
                {
                    var coll = GetProp(w, collName) as IEnumerable;
                    if (coll == null) continue;
                    foreach (var e in coll)
                    {
                        string en = null;
                        try { en = GetProp(e, "AttributeName") as string; } catch { }
                        if (en == null) { try { en = GetProp(e, "Name") as string; } catch { } }
                        if (string.Equals(en, attrName, StringComparison.OrdinalIgnoreCase)) { target = e; break; }
                    }
                }
                catch { }
                if (target != null) break;
            }
            if (target == null) return "no ExtendedProperty named '" + attrName + "' found on '" + widget + "'";
            bool removed = false;
            try { CallMethod(w, "RemoveChild", new object[] { target, true }, 2); removed = true; }
            catch { try { CallMethod(w, "RemoveChild", new object[] { target }, 1); removed = true; } catch { } }
            if (!removed) try { CallMethod(target, "Delete", null, 0); removed = true; } catch { }
            return "deleted ExtendedProperty '" + attrName + "' on '" + widget + "' (removed=" + removed + ")";
        });
    }

    // probe_block_extended_props: dump every ExtendedProperty on a block widget: AttributeName +
    // the Value expression text (via GetExtendedPropertyValue(...).Text or the Value expr surface).
    static string ProbeBlockExtendedProps(string module, string block, string widget)
    {
        if (string.IsNullOrEmpty(block)) return Json(new { ok = false, error = "block required" });
        return RunCmd(module, "probe block extended props", es =>
        {
            var blk = FindBlock(es, block);
            if (blk == null) throw new Exception("web block not found: " + block);
            var w = FindWidget(blk, widget);
            if (w == null) throw new Exception("widget not found: " + widget + " in block '" + block + "'");
            var items = new List<object>();
            foreach (var collName in new[] { "ExtendedProperties", "Attributes" })
            {
                try
                {
                    var coll = GetProp(w, collName) as IEnumerable;
                    if (coll == null) continue;
                    foreach (var e in coll)
                    {
                        string en = null;
                        try { en = GetProp(e, "AttributeName") as string; } catch { }
                        if (en == null) { try { en = GetProp(e, "Name") as string; } catch { } }
                        string val = null;
                        try
                        {
                            var gv = FindMethod(w, "GetExtendedPropertyValue", 1);
                            if (gv != null) { var expr = gv.Invoke(w, new object[] { en }); val = GetExprText(expr); }
                        }
                        catch { }
                        if (val == null) { try { var ve = GetProp(e, "Value"); val = GetExprText(ve); } catch { } }
                        if (val == null) { try { val = GetProp(e, "Value") as string; } catch { } }
                        items.Add(new { attrName = en, value = val, type = e.GetType().Name });
                    }
                }
                catch (Exception e) { Log("ProbeBlockExtendedProps " + collName + " failed: " + e.Message); }
            }
            return Json(new { ok = true, block = block, widget = widget, extendedProperties = items });
        });
    }

    // SetExtendedPropertyValue: set an ExtendedProperty's Value to a string, preferring the
    // expression surfaces so text literals store as <Text Value="..."> (matches SS serialization).
    static void SetExtendedPropertyValue(object ep, string value)
    {
        if (ep == null || value == null) return;
        // 1) If the entry exposes SetValue(String) directly (ExtendedProperty model surface).
        try { var m = FindMethod(ep, "SetValue", 1); if (m != null) { m.Invoke(ep, new object[] { value }); return; } } catch { }
        // 2) Value property -> expression text.
        try
        {
            var ve = GetProp(ep, "Value");
            if (ve != null)
            {
                try { CallMethod(ve, "SetValue", new object[] { value }, 1); return; } catch { }
                try { SetProp(ve, "Text", value); return; } catch { }
            }
        }
        catch { }
        // 3) Plain Value property as string (non-expression surface).
        try { SetProp(ep, "Value", value); return; } catch { }
        try { SetProp(ep, "Text", value); } catch { }
    }

    // GetExprText: best-effort extraction of an expression's text (ParsedExpression .Text / Value
    // / ToString) for diagnostics. Returns null if none match.
    static string GetExprText(object expr)
    {
        if (expr == null) return null;
        try { var t = GetProp(expr, "Text"); if (t is string ts && !string.IsNullOrEmpty(ts)) return ts; } catch { }
        try { var v = GetProp(expr, "Value"); if (v is string vs && !string.IsNullOrEmpty(vs)) return vs; } catch { }
        try { var t2 = GetProp(expr, "_valueExpression"); if (t2 != null && !object.ReferenceEquals(t2, expr)) { var inner = GetExprText(t2); if (inner != null) return inner; } } catch { }
        try { return expr.ToString(); } catch { }
        return null;
    }

    // probe_obj: dump the public methods+props of a named live object (block widget / aggregate /
    // data action / structure / client action / block event). kind: widget|aggregate|dataaction|
    // structure|clientaction|blockevent|screenwidget. Returns full reflective surface so callers
    // can discover the EXACT API (Filter/Condition/how-they-create-if) without guessing.
    static string ProbeObj(string module, string kind, string block, string screen, string name, string sub)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object target = null; string targetLabel = name ?? kind;
        try
        {
            if (kind == "widget" || kind == "blockwidget")
            {
                var blk = FindBlock(es, block);
                if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
                target = FindWidget(blk, name);
                if (target == null) return Json(new { ok = false, error = "widget not found: " + name });
            }
            else if (kind == "screenwidget")
            {
                var sc = FindScreen(es, screen);
                if (sc == null) return Json(new { ok = false, error = "screen not found: " + screen });
                target = FindWidget(sc, name) ?? FindWidgetDeep(sc, name);
                if (target == null) return Json(new { ok = false, error = "widget not found: " + name });
            }
            else if (kind == "aggregate")
            {
                var blk = FindBlock(es, block);
                if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
                var aggs = GetProp(blk, "ScreenAggregates") as IEnumerable;
                object agg = null;
                if (aggs != null)
                    foreach (var a in aggs)
                        try { if ((GetProp(a, "Name") as string) == name) { agg = a; break; } } catch { }
                if (agg == null) return Json(new { ok = false, error = "aggregate not found: " + name });
                target = agg;
                if (!string.IsNullOrEmpty(sub))
                {
                    // walk into sub-collection (e.g. Table.TableOperations / Table / Filters / RootOperation)
                    object cur = agg;
                    foreach (var step in sub.Split('.'))
                    {
                        object next = null;
                        try { next = GetProp(cur, step); } catch { next = null; }
                        if (next is IEnumerable coll)
                        {
                            var items = new List<object>();
                            foreach (var it in coll)
                            {
                                try { items.Add(new { type = it.GetType().FullName, name = (GetProp(it, "Name") as string ?? "?"), methods = OverloadSummary(it) }); }
                                catch { items.Add(new { type = it.GetType().FullName }); }
                            }
                            return Json(new { ok = true, kind = kind, name = name, step = step, collectionCount = items.Count, items = items });
                        }
                        if (next == null) return Json(new { ok = false, error = "sub-step not found: " + step + " on " + targetLabel });
                        cur = next;
                    }
                    return Json(new { ok = true, kind = kind, objType = cur.GetType().FullName, methods = MethodSummary(cur) });
                }
            }
            else if (kind == "structure")
            {
                var structs = GetProp(es, "Structures") as IEnumerable;
                object st = null;
                if (structs != null)
                    foreach (var s in structs)
                        try { if ((GetProp(s, "Name") as string) == name) { st = s; break; } } catch { }
                if (st == null) return Json(new { ok = false, error = "structure not found: " + name });
                target = st;
            }
            else if (kind == "clientaction")
            {
                target = FindAction(es, name);
                if (target == null) return Json(new { ok = false, error = "action not found: " + name });
            }
            else if (kind == "blockevent")
            {
                var blk = FindBlock(es, block);
                if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
                var evts = GetProp(blk, "CustomEvents") as IEnumerable ?? GetProp(blk, "Events") as IEnumerable;
                object evt = null;
                if (evts != null)
                    foreach (var v in evts)
                        try { if ((GetProp(v, "Name") as string) == name) { evt = v; break; } } catch { }
                if (evt == null) return Json(new { ok = false, error = "block event not found: " + name });
                target = evt;
            }
            else if (kind == "block")
            {
                var blk = FindBlock(es, block);
                if (blk == null) return Json(new { ok = false, error = "block not found: " + block });
                var caNames = new List<object>();
                var caColl = GetProp(blk, "ClientActions") as IEnumerable;
                if (caColl != null)
                    foreach (var ca in caColl)
                        try
                        {
                            var nm = GetProp(ca, "Name") as string ?? "?";
                            caNames.Add(new { type = ca.GetType().FullName, name = nm, name2 = (ca.GetType().GetProperty("Name")?.GetValue(ca) as string) });
                        }
                        catch (Exception ee) { caNames.Add(new { type = ca.GetType().FullName, name = "ERR:" + ee.Message }); }
                var saColl = GetProp(blk, "ScreenActions") as IEnumerable;
                var saNames = new List<object>();
                if (saColl != null)
                    foreach (var sa in saColl)
                        try { saNames.Add(new { type = sa.GetType().FullName, name = GetProp(sa, "Name") as string ?? "?" }); } catch (Exception ee) { saNames.Add(new { type = sa.GetType().FullName, name = "ERR:" + ee.Message }); }
                return Json(new { ok = true, kind = "block", block = block, clientActionCount = caNames.Count, clientActions = caNames, screenActionCount = saNames.Count, screenActions = saNames });
            }
            else return Json(new { ok = false, error = "unknown kind: " + kind });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; return Json(new { ok = false, error = r.GetType().Name + ": " + r.Message }); }
        var methods = new List<object>();
        var props = new List<object>();
        foreach (var t in AllTypes(target.GetType()))
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
                methods.Add(new { name = m.Name, pars = ps, ret = m.ReturnType.Name });
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                props.Add(new { name = p.Name, type = p.PropertyType.Name, writable = p.CanWrite });
            }
        }
        return Json(new { ok = true, kind = kind, name = targetLabel, objType = target.GetType().FullName, methods = methods, props = props });
    }

    static string OverloadSummary(object it)
    {
        var parts = new List<string>();
        foreach (var t in AllTypes(it.GetType()))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName && (m.Name == "CreateIf" || m.Name == "CreateFilter" || m.Name == "AddSource" || m.Name == "CreateExpression" || m.Name == "CreateArgument" || m.Name.StartsWith("Create") || m.Name.StartsWith("Add")))
                    parts.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
        return string.Join(" ; ", parts.Distinct());
    }

    static string MethodSummary(object o)
    {
        var parts = new List<string>();
        foreach (var t in AllTypes(o.GetType()))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName)
                    parts.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");
        return string.Join(" ", parts.Distinct());
    }

    // probe_flow_node_classes: type-scan loaded assemblies for the REAL classes of flow nodes the
    // current gaps need: JavaScript node, Message/feedback node, RaiseEvent / ITriggerNode, If
    // widget. Use the results to fix add_js_node / add_message_node / add_raise_event_node.
    static string ProbeFlowNodeClasses()
    {
        var wants = new List<string> { "JavaScript", "JS", "Message", "Feedback", "RaiseEvent", "TriggerNode", "Raise", "ClientAction" };
        var found = new List<object>();
        var seen = new HashSet<string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types; try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                try
                {
                    if (!t.IsInterface || t.IsGenericType) continue; // interfaces <T> skip explicit puzzle
                    var ns = t.Namespace ?? "";
                    if (ns.IndexOf("NRFlows", StringComparison.OrdinalIgnoreCase) < 0 && ns.IndexOf("Logic.Nodes", StringComparison.OrdinalIgnoreCase) < 0 && ns.IndexOf("WebFlows", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!wants.Any(w => t.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    if (!seen.Add(t.FullName)) continue;
                    found.Add(new { type = t.FullName });
                }
                catch { }
            }
        }
        return Json(new { ok = true, count = found.Count, nodeInterfaces = found });
    }

    // add_js_node: create a JavaScript node in a (client) action flow. Uses ProbeFlowNodeClasses
    // style candidates from worst-to-best and binds the JS text defensively (SetValue/SetProp
    // JavaScript/JSCode/Value). Optional afterNodeIndex inserts it into the flow.
    static string AddJsNode(string module, string action, string js, string nodeName, int afterNodeIndex)
    {
        if (js == null) return Json(new { ok = false, error = "js required" });
        return LiveEdit(module, action, "add js node", (act, es) =>
        {
            object node = null; string via = null; var errs = new List<string>();
            foreach (var cand in new[] { "OutSystems.Model.Logic.Nodes.IJavaScriptNode", "OutSystems.Model.Logic.Nodes.IJavaScriptActionNode", "ServiceStudio.Plugin.NRFlows.IJavaScriptNode" })
            {
                try { node = CreateNodeGeneric(act, cand); via = cand; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(cand + ": " + r.Message); }
            }
            if (node == null) throw new Exception("no JavaScript node interface found: " + string.Join(" ; ", errs));
            if (!string.IsNullOrEmpty(nodeName)) { try { SetProp(node, "Name", nodeName); } catch { } }
            string bindVia = "";
            foreach (var m in new[] { "SetValue", "SetJavaScript", "SetJsCode", "SetCode" })
            {
                try { CallMethod(node, m, new object[] { js }, 1); bindVia = m + "(" + js + ")"; break; }
                catch { }
            }
            if (bindVia == "") foreach (var pn in new[] { "JavaScript", "JsCode", "Code", "Value" }) { try { SetProp(node, pn, js); bindVia = pn; break; } catch { } }
            var sb = new StringBuilder();
            sb.Append("created JS node (" + node.GetType().Name + ") via " + via + (bindVia.Length > 0 ? " | " + bindVia : ""));
            if (afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                try { SetProp(node, "Target", anchorTarget); } catch { }
                SetProp(anchor, "Target", node);
                sb.Append(" | inserted after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            sb.AppendLine();
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // add_message_node: create a Message/feedback node in a (client) action flow and set its
    // message Text/MessageValue. kind optional (Info/Success/Error). Optional afterNodeIndex.
    static string AddMessageNode(string module, string action, string message, string kind, int afterNodeIndex)
    {
        if (message == null) return Json(new { ok = false, error = "message required" });
        return LiveEdit(module, action, "add message node", (act, es) =>
        {
            object node = null; string via = null; var errs = new List<string>();
            foreach (var cand in new[] { "OutSystems.Model.Logic.Nodes.IMessageNode", "OutSystems.Model.Logic.Nodes.INotifyNode", "ServiceStudio.Plugin.NRFlows.IMessageNode", "OutSystems.Model.Logic.Nodes.IFeedbackNode" })
            {
                try { node = CreateNodeGeneric(act, cand); via = cand; break; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; errs.Add(cand + ": " + r.Message); }
            }
            if (node == null) throw new Exception("no Message node interface found: " + string.Join(" ; ", errs));
            string bindVia = "";
            foreach (var m in new[] { "SetMessage", "SetText", "SetValue" })
            {
                try { CallMethod(node, m, new object[] { message }, 1); bindVia = m + "(" + message + ")"; break; }
                catch { }
            }
            if (bindVia == "") foreach (var pn in new[] { "Message", "Text", "MessageValue" }) { try { SetProp(node, pn, message); bindVia = pn; break; } catch { } }
            if (!string.IsNullOrEmpty(kind)) { try { SetProp(node, "Type", kind); bindVia += " Type=" + kind; } catch { } }
            var sb = new StringBuilder();
            sb.Append("created Message node (" + node.GetType().Name + ") via " + via + (bindVia.Length > 0 ? " | " + bindVia : ""));
            if (afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                try { SetProp(node, "Target", anchorTarget); } catch { }
                SetProp(anchor, "Target", node);
                sb.Append(" | inserted after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            sb.AppendLine();
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // add_lifecycle_assign: guarded Assign node inside a block lifecycle child flow (e.g.
    // OnInitialize / OnParametersChange). Same mutation as AddAssign's beforeEnd but addressed via
    // LiveEdit (body runs inside a real command); on the NREvents deadlock path the caller gets a
    // clear error instead of hanging the SS pipeline.
    static string AddLifecycleAssign(string module, string action, string varName, string value)
    {
        if (string.IsNullOrEmpty(varName) || value == null) return Json(new { ok = false, error = "var and value required" });
        return LiveEdit(module, action, "add lifecycle assign", (act, es) =>
        {
            var newAssign = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IAssignNode");
            var a = CallMethod(newAssign, "CreateAssignment", null, 0);
            CallMethod(a, "SetVariable", new object[] { varName }, 1);
            CallMethod(a, "SetValue", new object[] { value }, 1);
            var nodes = NodeList(act);
            var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
            object prev = null;
            if (endNode != null)
                foreach (var nn in nodes)
                {
                    var tgt = GetProp(nn, "Target");
                    if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = nn; break; }
                }
            if (endNode != null && prev == null)
            {
                var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
                if (startNode != null && GetProp(startNode, "Target") == null)
                {
                    SetProp(startNode, "Target", endNode);
                    prev = startNode;
                }
            }
            if (endNode == null || prev == null) throw new Exception("End/prev not found for lifecycle flow '" + action + "'");
            SetProp(newAssign, "Target", endNode);
            SetProp(prev, "Target", newAssign);
            return "inserted " + varName + "=" + value + " before End in '" + action + "'\n" + DumpFlowGraph(act);
        });
    }

    // add_refresh_node: create a RefreshQuery node (IRefreshDataNode) in a block lifecycle or
    // client-action flow and target it at a screen aggregate (WebScreenDataSet), then wire it
    // before End. This is how SS refreshes an aggregate (e.g. GetUserById) when a parameter like
    // UserId changes (OnParametersChange).
    //   action = flow name (e.g. "OnParametersChanged" - resolved via FindAction which unwraps
    //            the NREvents wrapper's .Destination)
    //   block  = web block owning the aggregate
    //   aggregateName = aggregate to refresh (e.g. "GetUserById")
    //   where  = "beforeEnd" (default) | "afterNode" (pass afterNodeIndex)
    static string AddRefreshNode(string module, string action, string block, string aggregateName, string where, int afterNodeIndex = -1)
    {
        return AddRefreshNode2(module, action, block, null, aggregateName, where, afterNodeIndex);
    }

    static string AddRefreshNode2(string module, string action, string block, string screen, string aggregateName, string where, int afterNodeIndex = -1)
    {
        if (string.IsNullOrEmpty(aggregateName)) return Json(new { ok = false, error = "aggregateName required" });
        if (string.IsNullOrEmpty(block) && string.IsNullOrEmpty(screen)) return Json(new { ok = false, error = "block or screen required" });
        return LiveEdit(module, action, "add refresh node", (act, es) =>
        {
            object target = null; string targetKind = null; string targetName = null;
            if (!string.IsNullOrEmpty(block)) { target = FindBlock(es, block); targetKind = "block"; targetName = block; }
            else { target = FindScreen(es, screen); targetKind = "screen"; targetName = screen; }
            if (target == null) throw new Exception(targetKind + " not found: " + targetName);
            object agg = null;
            foreach (var collName in new[] { "ScreenAggregates", "ScreenDataSets", "DataSets" })
            {
                var aggs = GetProp(target, collName) as IEnumerable;
                if (aggs == null) continue;
                foreach (var a in aggs)
                    try { if ((GetProp(a, "Name") as string) == aggregateName) { agg = a; break; } } catch { }
                if (agg != null) break;
            }
            if (agg == null) throw new Exception("aggregate not found: " + aggregateName + " in " + targetKind + " '" + targetName + "'");

            var node = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IRefreshDataNode");
            SetProp(node, "Target", agg);
            var sb = new StringBuilder();
            sb.AppendLine("created " + ShortName(node) + " targeting " + aggregateName);
            if (where == "afterNode" && afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex);
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetFlowTarget(anchor);
                SetFlowTarget(node, anchorTarget);
                SetFlowTarget(anchor, node);
                sb.AppendLine("inserted after node[" + afterNodeIndex + "]");
            }
            else // beforeEnd
            {
                var nodes = NodeList(act);
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                    foreach (var nn in nodes)
                    {
                        var tgt = GetFlowTarget(nn);
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = nn; break; }
                    }
                if (endNode != null && prev == null)
                {
                    var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
                    if (startNode != null && GetFlowTarget(startNode) == null)
                    {
                        SetFlowTarget(startNode, endNode);
                        prev = startNode;
                        sb.AppendLine("auto-linked Start?End");
                    }
                }
                if (endNode == null || prev == null) throw new Exception("End/prev not found in flow '" + action + "'");
                SetFlowTarget(node, endNode);
                SetFlowTarget(prev, node);
                sb.AppendLine("inserted refresh before End");
            }
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // GetFlowTarget/SetFlowTarget: read/write a flow node's NEXT-node link.
    // Priority (preserves historical behavior, fixes RefreshQuery): 1) IFlowNode.Target
    // when present (all classic nodes), 2) any other interface Target whose type fits
    // the target object (e.g. IRefreshDataNode.Target:IActionNode), 3) legacy fallbacks.
    // NEVER route a node object into an IRefreshQueryTarget-typed (data) slot — that exact
    // bug broke RefreshQuery wiring (End into the aggregate slot).
    static object GetFlowTarget(object node)
    {
        if (node == null) return null;
        var iface = node.GetType().GetInterface("IFlowNode");
        if (iface != null)
        {
            var p = iface.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
            if (p != null) { try { return p.GetValue(node, null); } catch { } }
        }
        foreach (var i2 in node.GetType().GetInterfaces())
        {
            if (i2.Name == "IFlowNode") continue;
            var p = i2.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanRead) continue;
            if ((p.PropertyType.FullName ?? "").Contains("RefreshQueryTarget")) continue;
            try { return p.GetValue(node, null); } catch { }
        }
        try { return GetProp(node, "Target"); } catch { return null; }
    }
    static void SetFlowTarget(object node, object target)
    {
        if (node == null) throw new Exception("SetFlowTarget: node null");
        var iface = node.GetType().GetInterface("IFlowNode");
        if (iface != null)
        {
            var p = iface.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try
                {
                    if (target == null || p.PropertyType.IsAssignableFrom(target.GetType())) { p.SetValue(node, target, null); return; }
                }
                catch { }
            }
        }
        foreach (var i2 in node.GetType().GetInterfaces())
        {
            if (i2.Name == "IFlowNode") continue;
            var p = i2.GetProperty("Target", BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) continue;
            if ((p.PropertyType.FullName ?? "").Contains("RefreshQueryTarget")) continue;
            try
            {
                if (target == null || p.PropertyType.IsAssignableFrom(target.GetType())) { p.SetValue(node, target, null); return; }
            }
            catch { }
        }
        SetProp(node, "Target", target);
    }

    // probe_all_node_types: enumerate ALL types in OutSystems.Model.Logic.Nodes namespace
    static string ProbeAllNodeTypes()
    {
        var ns = "OutSystems.Model.Logic.Nodes";
        var found = new SortedSet<string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Namespace == ns && !t.IsGenericType && t.IsInterface)
                        found.Add(t.Name);
                }
            }
            catch { }
        }
        return Json(new { ok = true, ns = ns, nodeTypes = found.ToList() });
    }

    // debug_server_actions: dump all server action names from es.ServerActions
    // and es.ConsumedServerActions (if it exists). For diagnosing consumption issues.
    static string DebugServerActions(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var sas = new List<string>();
        var sas2 = new List<string>();
        try {
            var coll = GetProp(es, "ServerActions") as IEnumerable;
            if (coll != null) foreach (var sa in coll) { var n = GetProp(sa, "Name") as string; sas.Add(n ?? "?"); }
        } catch { sas.Add("<err>"); }
        try {
            var coll2 = GetProp(es, "ConsumedServerActions") as IEnumerable;
            if (coll2 != null) foreach (var sa in coll2) { var n = GetProp(sa, "Name") as string; sas2.Add(n ?? "?"); }
        } catch { sas2.Add("<err>"); }
        return Json(new { ok = true, module = moduleName, serverActions = sas, consumedServerActions = sas2 });
    }

    // debug_find_action: search all IEnumerable collections on an eSpace for an element
    // by name. Returns which collection(s) contain it (and whether Public is true).
    // Use to discover where consumed elements are actually stored.
    static string DebugFindAction(string moduleName, string name)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var found = new List<object>();
        foreach (var pi in es.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IEnumerable).IsAssignableFrom(pi.PropertyType)) continue;
            if (pi.PropertyType == typeof(string)) continue;
            try
            {
                var coll = pi.GetValue(es, null) as IEnumerable;
                if (coll == null) continue;
                foreach (var item in coll)
                {
                    var itemName = GetProp(item, "Name") as string;
                    if (itemName == name)
                    {
                        string pub = "N/A";
                        try { pub = (GetProp(item, "Public") as bool?).ToString(); } catch { }
                        found.Add(new { collection = pi.Name, name = itemName, Public = pub });
                    }
                }
            }
            catch { }
        }
        return Json(new { ok = true, module = moduleName, searchName = name, found = found });
    }

    // debug_eSpace_collections: list all IEnumerable collection names + counts on an eSpace.
    // Use to discover where consumed elements are actually stored.
    static string DebugESpaceCollections(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var colls = new List<object>();
        foreach (var pi in es.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IEnumerable).IsAssignableFrom(pi.PropertyType)) continue;
            if (pi.PropertyType == typeof(string)) continue;
            try
            {
                var coll = pi.GetValue(es, null) as IEnumerable;
                var count = 0;
                if (coll != null)
                    foreach (var _ in coll) count++;
                colls.Add(new { name = pi.Name, itemCount = count, type = pi.PropertyType.Name });
            }
            catch (Exception ex) { colls.Add(new { name = pi.Name, itemCount = -1, error = ex.Message }); }
        }
        return Json(new { ok = true, module = moduleName, collections = colls });
    }

    // debug_eSpace_collection_items: dump all items from a named collection with their key properties.
    static string DebugESpaceCollectionItems(string moduleName, string collName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var pi = es.GetType().GetProperty(collName);
        if (pi == null) return Json(new { ok = false, error = "property not found: " + collName });
        if (!typeof(IEnumerable).IsAssignableFrom(pi.PropertyType)) return Json(new { ok = false, error = "not IEnumerable: " + collName });
        var coll = pi.GetValue(es, null) as IEnumerable;
        if (coll == null) return Json(new { ok = true, module = moduleName, collection = collName, items = new object[0] });
        var items = new List<object>();
        foreach (var item in coll)
        {
            var d = new Dictionary<string, object>();
            d["_type"] = item.GetType().Name;
            foreach (var p in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string))
                {
                    try { d[p.Name] = p.GetValue(item, null)?.ToString() ?? "null"; } catch { }
                }
            }
            items.Add(d);
        }
        return Json(new { ok = true, module = moduleName, collection = collName, items = items });
    }

    // debug_reference: dump all properties of a Reference object (by reference name).
    // Use to understand how consumed elements are linked.
    static string DebugReference(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm != referenceName) continue;
            var d = new Dictionary<string, object>();
            d["_type"] = r.GetType().Name;
            foreach (var p in r.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string))
                {
                    try { d[p.Name] = p.GetValue(r, null)?.ToString() ?? "null"; } catch { }
                }
            }
            return Json(new { ok = true, module = moduleName, reference = referenceName, properties = d });
        }
        return Json(new { ok = false, error = "reference not found: " + referenceName });
    }

    // debug_reference_extended: list the ExtendedElement collection of a Reference.
    static string DebugReferenceExtended(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object targetRef = null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm == referenceName) { targetRef = r; break; }
        }
        if (targetRef == null) return Json(new { ok = false, error = "reference not found: " + referenceName });
        var extElem = GetProp(targetRef, "ExtendedElement") as IEnumerable;
        if (extElem == null) return Json(new { ok = true, module = moduleName, reference = referenceName, extendedElements = new object[0] });
        var items = new List<object>();
        foreach (var e in extElem)
        {
            var d = new Dictionary<string, object>();
            d["_type"] = e.GetType().Name;
            try { d["Name"] = GetProp(e, "Name")?.ToString() ?? "null"; } catch { }
            try { d["Key"] = GetProp(e, "Key")?.ToString() ?? "null"; } catch { }
            try { d["Public"] = (GetProp(e, "Public") as bool?)?.ToString() ?? "N/A"; } catch { }
            items.Add(d);
        }
        return Json(new { ok = true, module = moduleName, reference = referenceName, extendedElements = items });
    }

    // debug_eSpace_from_reference: get the producer eSpace from a consumer Reference Sentinel.
    static string DebugESpaceFromReference(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object targetRef = null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm == referenceName) { targetRef = r; break; }
        }
        if (targetRef == null) return Json(new { ok = false, error = "reference not found: " + referenceName });
        // The Sentinel property links to the actual eSpace object
        object sentinel = null;
        foreach (var p in targetRef.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Sentinel") { sentinel = p.GetValue(targetRef, null); break; }
        }
        if (sentinel == null) return Json(new { ok = false, error = "Sentinel property not found on reference" });
        // Find the eSpace on the Sentinel
        object producerEs = null;
        foreach (var p in sentinel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.PropertyType.Name.Contains("ESpace") || p.PropertyType.FullName?.Contains("ESpace") == true)
            {
                try { producerEs = p.GetValue(sentinel, null); if (producerEs != null) break; } catch { }
            }
        }
        if (producerEs == null) return Json(new { ok = false, error = "could not find eSpace on Sentinel", sentinelType = sentinel.GetType().Name });
        // Now search UserActions on the producer eSpace
        var sas = new List<object>();
        foreach (var collName in new[] { "UserActions", "ServerActions", "DirectChildren", "DirectLoadedChildren" })
        {
            var coll = GetProp(producerEs, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var sa in coll)
            {
                try
                {
                    var nm = GetProp(sa, "Name") as string;
                    if (nm == "ClientCreate")
                    {
                        var d = new Dictionary<string, object>();
                        d["collection"] = collName;
                        d["_type"] = sa.GetType().Name;
                        d["Name"] = nm;
                        try { d["Key"] = GetProp(sa, "Key")?.ToString() ?? "null"; } catch { }
                        try { d["Public"] = (GetProp(sa, "Public") as bool?)?.ToString() ?? "N/A"; } catch { }
                        sas.Add(d);
                    }
                }
                catch { }
            }
        }
        return Json(new { ok = true, module = moduleName, reference = referenceName, producerEspaceType = producerEs.GetType().Name, clientCreate = sas });
    }

    // debug_sentinel: dump ALL properties of the Sentinel object of a Reference.
    // Use to discover what the Sentinel actually exposes (it may not be a direct eSpace proxy).
    static string DebugSentinel(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object targetRef = null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm == referenceName) { targetRef = r; break; }
        }
        if (targetRef == null) return Json(new { ok = false, error = "reference not found: " + referenceName });
        object sentinel = null;
        foreach (var p in targetRef.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Sentinel") { sentinel = p.GetValue(targetRef, null); break; }
        }
        if (sentinel == null) return Json(new { ok = false, error = "Sentinel not found on reference" });
        var d = new Dictionary<string, object>();
        d["_type"] = sentinel.GetType().Name;
        d["_fullType"] = sentinel.GetType().FullName;
        // Dump ALL non-collection properties
        foreach (var p in sentinel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string))
            {
                try
                {
                    var val = p.GetValue(sentinel, null);
                    d[p.Name] = val?.ToString() ?? "null";
                }
                catch (Exception ex) { d[p.Name] = "ERROR:" + ex.Message; }
            }
        }
        return Json(new { ok = true, module = moduleName, reference = referenceName, sentinel = d });
    }

    // debug_sentinel_collections: dump collections on the Sentinel object.
    static string DebugSentinelCollections(string moduleName, string referenceName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object targetRef = null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm == referenceName) { targetRef = r; break; }
        }
        if (targetRef == null) return Json(new { ok = false, error = "reference not found: " + referenceName });
        object sentinel = null;
        foreach (var p in targetRef.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Sentinel") { sentinel = p.GetValue(targetRef, null); break; }
        }
        if (sentinel == null) return Json(new { ok = false, error = "Sentinel not found on reference" });
        var colls = new List<object>();
        foreach (var p in sentinel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string)) continue;
            try
            {
                var coll = p.GetValue(sentinel, null) as IEnumerable;
                var count = 0;
                if (coll != null)
                    foreach (var _ in coll) count++;
                colls.Add(new { name = p.Name, itemCount = count, type = p.PropertyType.Name });
            }
            catch (Exception ex) { colls.Add(new { name = p.Name, itemCount = -1, error = ex.Message }); }
        }
        return Json(new { ok = true, module = moduleName, reference = referenceName, sentinelType = sentinel.GetType().Name, collections = colls });
    }

    // debug_sentinel_collection_items: dump items from a named collection on the Sentinel.
    static string DebugSentinelCollectionItems(string moduleName, string referenceName, string collName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return Json(new { ok = false, error = "References collection is null" });
        object targetRef = null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm == referenceName) { targetRef = r; break; }
        }
        if (targetRef == null) return Json(new { ok = false, error = "reference not found: " + referenceName });
        object sentinel = null;
        foreach (var p in targetRef.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Sentinel") { sentinel = p.GetValue(targetRef, null); break; }
        }
        if (sentinel == null) return Json(new { ok = false, error = "Sentinel not found on reference" });
        var pi = sentinel.GetType().GetProperty(collName);
        if (pi == null) return Json(new { ok = false, error = "property not found: " + collName });
        if (!typeof(IEnumerable).IsAssignableFrom(pi.PropertyType)) return Json(new { ok = false, error = "not IEnumerable: " + collName });
        var coll = pi.GetValue(sentinel, null) as IEnumerable;
        if (coll == null) return Json(new { ok = true, module = moduleName, reference = referenceName, collection = collName, items = new object[0] });
        var items = new List<object>();
        foreach (var item in coll)
        {
            var d = new Dictionary<string, object>();
            d["_type"] = item.GetType().Name;
            try { d["Name"] = GetProp(item, "Name")?.ToString() ?? "null"; } catch { }
            try { d["Key"] = GetProp(item, "Key")?.ToString() ?? "null"; } catch { }
            try { d["Public"] = (GetProp(item, "Public") as bool?)?.ToString() ?? "N/A"; } catch { }
            items.Add(d);
        }
        return Json(new { ok = true, module = moduleName, reference = referenceName, collection = collName, items = items });
    }

// add_action_call: insert a server-action-call node into the flow.
    // The server action must already be consumed by the module (via live_consume_elements).
    // Tries three lookup strategies:
    //   1. es.ConsumedServerActions (if non-empty after AddDependency)
    //   2. Consumer eSpace.ReferenceActions (ReferenceAction stored via AddDependency)
    //   3. Producer ESpace.UserActions by name (for cross-module reference)
    // where: "afterAnchor" (insert after anchor Assign node) or "beforeEnd" (before first End).
    static string AddActionCall(string moduleName, string actionName, string where,
        string anchorVar, string anchorValue, string serverActionName, string producerModule, int afterNodeIndex = -1)
    {
        if (string.IsNullOrEmpty(serverActionName)) return Json(new { ok = false, error = "serverActionName required" });
        return LiveEdit(moduleName, actionName, "add action call node", (act, es) =>
        {
            var saType = FindType("OutSystems.Model.Logic.Nodes.IExecuteServerActionNode");
            if (saType == null) throw new Exception("IExecuteServerActionNode type not found");
            var newNode = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IExecuteServerActionNode");

            // Strategy 0: LOCAL action in the same module (the 90% case for wrappers —
            // entity CRUD actions live alongside their wrappers). Used when producerModule
            // is empty or names this module.
            object consumedSA = null;
            if (string.IsNullOrEmpty(producerModule) || producerModule == moduleName)
            {
                try { consumedSA = FindServerActionInEspace(es, serverActionName); } catch { }
            }

            // Strategy 1: look in ConsumedServerActions (may be empty depending on SS version)
            if (consumedSA == null)
                consumedSA = FindConsumedServerAction(es, serverActionName, producerModule);

            // Strategy 2: look in the consumer's Reference objects' ReferenceActions collections
            // (consumed server actions are stored here by AddDependency)
            if (consumedSA == null)
                consumedSA = FindConsumedServerActionInReference(es, serverActionName, producerModule);

            // Strategy 3: if producerModule is given, find the server action in the producer
            // ESpace's UserActions collection - BUT we must use the ReferenceAction, not the
            // raw producer UserAction, so we resolve it through the consumer's reference first
            if (consumedSA == null && !string.IsNullOrEmpty(producerModule))
            {
                var producerEs = FindEspace(producerModule);
                if (producerEs != null)
                {
                    var producerSA = FindServerActionInEspace(producerEs, serverActionName);
                    if (producerSA != null)
                    {
                        // Get the OriginalKey of the producer's action and find the consumer's ReferenceAction
                        var origKey = GetProp(producerSA, "Key") as string;
                        if (!string.IsNullOrEmpty(origKey))
                            consumedSA = FindConsumedServerActionByOriginalKey(es, origKey, producerModule);
                        // If still null, use the producer SA directly (let SS resolve it)
                        if (consumedSA == null) consumedSA = producerSA;
                    }
                }
            }

            if (consumedSA == null) throw new Exception("consumed server action not found: " + serverActionName + (producerModule != null ? " (producer: " + producerModule + ")" : ""));
            SetProp(newNode, "Action", consumedSA);
            var sb = new StringBuilder();
            if (where == "afterNode" && afterNodeIndex >= 0)
            {
                var nodes = NodeList(act);
                if (afterNodeIndex >= nodes.Count) throw new Exception("afterNodeIndex out of range: " + afterNodeIndex + " (count=" + nodes.Count + ")");
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted call " + serverActionName + " after node[" + afterNodeIndex + "] (" + ShortName(anchor) + ")");
            }
            else if (where == "afterAnchor")
            {
                var anchor = FindAssignNode(act, anchorVar ?? "", anchorValue ?? "");
                if (anchor == null) throw new Exception("anchor not found: " + anchorVar + "=" + anchorValue);
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted call " + serverActionName + " after anchor (" + anchorVar + "=" + anchorValue + ")");
            }
            else // beforeEnd (first End node)
            {
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                {
                    foreach (var n in NodeList(act))
                    {
                        var tgt = GetProp(n, "Target");
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                    }
                }
                // Auto-link Start?End if no node points to End yet (Issue 3 fix).
                if (endNode != null && prev == null)
                {
                    var startNode = NodesOfType(act, "IStartNode").FirstOrDefault();
                    if (startNode != null && GetProp(startNode, "Target") == null)
                    {
                        SetProp(startNode, "Target", endNode);
                        prev = startNode;
                        sb.AppendLine("auto-linked Start?End");
                    }
                }
                if (endNode == null || prev == null) throw new Exception("End/prev not found (create Start+End nodes and link them via live_set_node_target before using where='beforeEnd')");
                SetProp(newNode, "Target", endNode);
                SetProp(prev, "Target", newNode);
                sb.AppendLine("inserted call " + serverActionName + " before End");
            }
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

    // set_action_call: change the server action called by an EXISTING IExecuteServerActionNode.
    // Reuses the same FindConsumedServerAction resolution as AddActionCall. This is the fix
    // for the "can't change Action property" issue � SetProp(node, "Action", string) fails
    // because Action needs an IExecuteActionTarget object, not a string. This method resolves
    // the server action name to the actual consumed action object and sets it via SetProp.
    static string SetActionCall(string moduleName, string actionName, int nodeIndex, string serverActionName, string producerModule)
    {
        if (string.IsNullOrEmpty(serverActionName)) return Json(new { ok = false, error = "serverActionName required" });
        return LiveEdit(moduleName, actionName, "set action call", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IExecuteServerActionNode"))
                throw new Exception("node[" + nodeIndex + "] is not IExecuteServerActionNode (type=" + ShortName(node) + ")");
            object consumedSA = null;
            if (string.IsNullOrEmpty(producerModule) || producerModule == moduleName)
            {
                try { consumedSA = FindServerActionInEspace(es, serverActionName); } catch { }
            }
            if (consumedSA == null) consumedSA = FindConsumedServerAction(es, serverActionName, producerModule);
            if (consumedSA == null) consumedSA = FindConsumedServerActionInReference(es, serverActionName, producerModule);
            if (consumedSA == null && !string.IsNullOrEmpty(producerModule))
            {
                var producerEs = FindEspace(producerModule);
                if (producerEs != null)
                {
                    var producerSA = FindServerActionInEspace(producerEs, serverActionName);
                    if (producerSA != null)
                    {
                        var origKey = GetProp(producerSA, "Key") as string;
                        if (!string.IsNullOrEmpty(origKey))
                            consumedSA = FindConsumedServerActionByOriginalKey(es, origKey, producerModule);
                        if (consumedSA == null) consumedSA = producerSA;
                    }
                }
            }
            if (consumedSA == null) throw new Exception("consumed server action not found: " + serverActionName + (producerModule != null ? " (producer: " + producerModule + ")" : ""));
            SetProp(node, "Action", consumedSA);
            return "set node[" + nodeIndex + "].Action = " + serverActionName + "\n" + DumpFlowGraph(act);
        });
    }

    // Find a consumed server action by searching the consumer's Reference objects'
    // ReferenceActions collections. This is where AddDependency stores consumed actions.
    static object FindConsumedServerActionInReference(object es, string name, string producerModule)
    {
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return null;
        foreach (var r in refs)
        {
            var refName = GetProp(r, "Name") as string;
            if (!string.IsNullOrEmpty(producerModule) && refName != producerModule) continue;
            // Generic scan: ANY public instance property of the Reference (or its Sentinel)
            // whose value is a non-string IEnumerable of named items. System actions may live
            // under a collection other than ReferenceActions (ReferenceClientActions, etc.).
            foreach (var found in ScanNamedItems(r, name, 2)) return found;
            var sentinel = GetProp(r, "Sentinel");
            if (sentinel != null)
                foreach (var found in ScanNamedItems(sentinel, name, 2)) return found;
        }
        return null;
    }

    // ScanNamedItems: walk a host object's public instance properties; for each non-string
    // IEnumerable property, yield the first item whose Name matches `name`. depth limits
    // one level of nested collections (e.g. Reference.ExtendedElement -> items).
    static System.Collections.Generic.IEnumerable<object> ScanNamedItems(object host, string name, int depth)
    {
        if (host == null || depth < 0) yield break;
        foreach (var p in host.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length > 0) continue;
            object coll = null;
            try { coll = p.GetValue(host, null); } catch { continue; }
            if (coll == null || coll is string) continue;
            foreach (var item in (IEnumerable)coll)
            {
                if (item == null || item is string) continue;
                string nm = null;
                try { nm = GetProp(item, "Name") as string; } catch { }
                if (nm == name) yield return item;
                if (depth > 0)
                {
                    foreach (var inner in ScanNamedItems(item, name, depth - 1)) yield return inner;
                }
            }
        }
    }

    // Find a consumed server action by its OriginalKey (the key of the producer's original action).
    // Useful when we have the producer's action and need to find the consumer's ReferenceAction.
    static object FindConsumedServerActionByOriginalKey(object es, string originalKey, string producerModule)
    {
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return null;
        foreach (var r in refs)
        {
            var refName = GetProp(r, "Name") as string;
            if (!string.IsNullOrEmpty(producerModule) && refName != producerModule) continue;
            var refActions = GetProp(r, "ReferenceActions") as IEnumerable;
            if (refActions == null) continue;
            foreach (var sa in refActions)
            {
                try
                {
                    var origKey = GetProp(sa, "OriginalKey") as string;
                    if (origKey == originalKey) return sa;
                }
                catch { }
            }
        }
        return null;
    }

    // find a consumed server action by name (optionally filtered by producer module).
    // searches es.ConsumedServerActions for a matching Name; if producerModule is set, verifies
    // the producer module name via the Reference element.
    static object FindConsumedServerAction(object es, string name, string producerModule)
    {
        var sas = GetProp(es, "ConsumedServerActions") as IEnumerable;
        if (sas == null) return null;
        foreach (var sa in sas)
        {
            try
            {
                var nm = GetProp(sa, "Name") as string;
                if (nm != name) continue;
                if (!string.IsNullOrEmpty(producerModule))
                {
                    var refs = GetProp(es, "References") as IEnumerable;
                    bool match = false;
                    if (refs != null)
                        foreach (var r in refs)
                        {
                            var extender = GetProp(r, "ExtendedElement") as IEnumerable;
                            if (extender != null)
                                foreach (var e2 in extender)
                                    if (object.ReferenceEquals(e2, sa)) { match = true; break; }
                            if (match) break;
                        }
                    if (!match) continue;
                }
                return sa;
            }
            catch { }
        }
        return null;
    }

    // find a server action in an eSpace by name, searching UserActions collection.
    // Used to get the producer's server action for cross-module call node reference.
    static object FindServerActionInEspace(object es, string name)
    {
        // Try UserActions first (where public server actions live)
        foreach (var collName in new[] { "UserActions", "ServerActions", "DirectChildren", "DirectLoadedChildren" })
        {
            var coll = GetProp(es, collName) as IEnumerable;
            if (coll == null) continue;
            foreach (var sa in coll)
            {
                try
                {
                    var nm = GetProp(sa, "Name") as string;
                    if (nm == name) return sa;
                }
                catch { }
            }
        }
        return null;
    }

    // debug_node_props: dump all settable properties on a node at a given flow index.
    // Use to discover the correct property name for TargetAction on action-call nodes.
    static string DebugNodeProps(string moduleName, string actionName, string nodeIndex)
    {
        int idx = int.TryParse(nodeIndex, out var i) ? i : -1;
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found" });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found" });
        var nodes = NodeList(action);
        if (idx < 0 || idx >= nodes.Count) return Json(new { ok = false, error = "nodeIndex out of range", count = nodes.Count });
        var node = nodes[idx];
        var settable = new List<string>();
        foreach (var t in AllTypes(node.GetType()))
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.CanWrite) settable.Add(p.Name);
            }
        }
        return Json(new { ok = true, nodeIndex = idx, nodeType = node.GetType().Name, settableProperties = settable });
    }

    // debug_find_exc_handler: inspect the action object and all nodes for any property
    // related to exception handling. Dumps ALL properties (not just settable) containing
    // "Exception" or "Handler", plus their values. Also dumps ALL properties on Start and
    // ErrorHandler nodes. Use to discover how SS wires exception handling.
    static string DebugFindExcHandler(string moduleName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found" });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found" });
        var sb = new StringBuilder();

        // 1. Search action object for exception-related properties
        sb.AppendLine("=== Action properties containing 'Exception' or 'Handler' ===");
        foreach (var t in AllTypes(action.GetType()))
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.Name.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Name.IndexOf("Handler", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string val = "?";
                    try { var v = p.GetValue(action, null); val = v?.GetType().Name ?? "null"; if (v != null) val += " [" + (GetProp(v, "Name") as string ?? GetProp(v, "Key") as string ?? v.ToString()) + "]"; }
                    catch (Exception e) { val = "ERR:" + e.GetType().Name; }
                    sb.AppendLine("  [" + t.Name + "] " + p.PropertyType.Name + " " + p.Name + " (CanWrite=" + p.CanWrite + ") = " + val);
                }
            }
        }

        // 2. Search ALL properties on the action object (full dump, no filter)
        sb.AppendLine("=== Action ALL properties ===");
        foreach (var t in AllTypes(action.GetType()))
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string))
                {
                    string val = "?";
                    try { var v = p.GetValue(action, null); val = v?.GetType().Name ?? "null"; }
                    catch { val = "ERR"; }
                    sb.AppendLine("  [" + t.Name + "] " + p.PropertyType.Name + " " + p.Name + " = " + val);
                }
            }
        }

        // 3. For each node, show exception-related properties
        var nodes = NodeList(action);
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var sn = ShortName(n);
            sb.AppendLine("=== Node[" + i + "] " + sn + " ===");
            foreach (var t in AllTypes(n.GetType()))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (p.Name.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf("Handler", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string val = "?";
                        try { var v = p.GetValue(n, null); val = v?.GetType().Name ?? "null"; if (v != null) { var nm = GetProp(v, "Name") as string; var ky = GetProp(v, "Key") as string; if (nm != null) val += " [Name=" + nm + "]"; else if (ky != null) val += " [Key=" + ky + "]"; } }
                        catch (Exception e) { val = "ERR:" + e.GetType().Name; }
                        sb.AppendLine("  [" + t.Name + "] " + p.PropertyType.Name + " " + p.Name + " (CanWrite=" + p.CanWrite + ") = " + val);
                    }
                }
            }
        }

        return Json(new { ok = true, report = sb.ToString() });
    }

    // set_error_handler_exception: find an exception object on the ESpace's SystemExceptions
    // collection (the "Exceptions" folder in the SS Logic tab) and set it on an
    // IExceptionHandlerNode's Exception property. Defaults to "All Exceptions" unless
    // exceptionName is specified. Falls back to copying from a reference action's
    // ErrorHandler, then to searching ESpace/ModelServices properties, then to static
    // members on AbstractException-related types.
    static string SetErrorHandlerException(string moduleName, string actionName, int nodeIndex, string exceptionName = null)
    {
        if (string.IsNullOrEmpty(exceptionName)) exceptionName = "All Exceptions";
        var targetName = exceptionName;
        return LiveEdit(moduleName, actionName, "set error handler exception", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IExceptionHandlerNode"))
                throw new Exception("node[" + nodeIndex + "] is not an IExceptionHandlerNode (type=" + ShortName(node) + ")");
            var sb = new StringBuilder();
            object allExc = null;
            // Resolve AbstractException robustly: try the exact full name first, then
            // fall back to a short-name search (the type may be nested/renamed across SS
            // builds). CRITICAL: if excType is null, every (excType == null || ...)
            // guard below short-circuits to true and lets non-exception objects (e.g.
            // Func<AbstractException,AbstractModelFolder> selector delegates) be selected,
            // which then crash SetProp with an ArgumentException. Fail fast instead.
            var excType = FindType("ServiceStudio.Model.AbstractException") ?? FindTypeByShortName("AbstractException");
            if (excType == null)
                throw new Exception("Could not resolve type 'AbstractException' in any loaded assembly. " +
                    "Cannot safely set the Exception property without a type guard.");

            // Strategy 0: Search es.SystemExceptions collection (most reliable � direct instances)
            // The ESpace has a SystemExceptions IEnumerable collection containing SystemException
            // and UserRaisableSystemException instances (the "Exceptions" folder in the Logic tab).
            // Each item has a Name property (e.g. "All Exceptions", "Database Exception", etc.)
            // and is a direct AbstractException subclass that can be assigned to Exception property.
            var sysExcColl = GetProp(es, "SystemExceptions") as IEnumerable;
            if (sysExcColl == null)
            {
                sb.AppendLine("s0: es.SystemExceptions is null � collection not available on this ESpace");
            }
            else
            {
                int s0count = 0;
                foreach (var item in sysExcColl)
                {
                    s0count++;
                    var nm = GetProp(item, "Name") as string;
                    if (nm == null) { sb.AppendLine("s0: item with null Name (type=" + item.GetType().Name + ")"); continue; }
                    sb.AppendLine("s0: SystemExceptions item: " + nm + " (type=" + item.GetType().Name + ")");
                    if (nm == targetName && excType.IsAssignableFrom(item.GetType()))
                    {
                        allExc = item;
                        sb.AppendLine("  -> selected from SystemExceptions");
                        break;
                    }
                }
                if (allExc == null) sb.AppendLine("s0: scanned " + s0count + " SystemExceptions item(s); none matched '" + targetName + "'");
            }

            // Strategy 0b: try reading es.AllExceptions directly (the skill docs reference it).
            // Validate the result is a real AbstractException � some SS builds expose a
            // Func<AbstractException,AbstractModelFolder> selector under a similar name, which
            // must NOT be mistaken for the exception instance.
            if (allExc == null)
            {
                foreach (var propName in new[] { "AllExceptions", "AllExceptionsException" })
                {
                    var direct = GetProp(es, propName);
                    if (direct == null) continue;
                    sb.AppendLine("s0b: es." + propName + " = " + direct.GetType().FullName);
                    if (excType.IsAssignableFrom(direct.GetType()))
                    {
                        allExc = direct;
                        sb.AppendLine("  -> selected from es." + propName);
                        break;
                    }
                }
            }

            // Strategy 1: Read Exception from ANY existing action's ErrorHandler (copy approach).
            // Searches ALL actions in the module (not a hardcoded name) for any
            // IExceptionHandlerNode whose Exception is already set. This works in any
            // project as soon as at least one exception handler has been configured, and
            // the copied object is guaranteed to be a valid AbstractException instance.
            if (allExc == null)
            {
                int scanned = 0;
                foreach (var refAction in AllActions(es))
                {
                    scanned++;
                    var refName = GetProp(refAction, "Name") as string ?? "?";
                    foreach (var n in NodesOfType(refAction, "IExceptionHandlerNode"))
                    {
                        var refExc = GetProp(n, "Exception");
                        if (refExc != null && excType.IsAssignableFrom(refExc.GetType()))
                        {
                            allExc = refExc;
                            sb.AppendLine("s1: Read Exception from action '" + refName + "' ErrorHandler: " + refExc.GetType().Name);
                            break;
                        }
                    }
                    if (allExc != null) break;
                }
                if (allExc == null) sb.AppendLine("s1: scanned " + scanned + " action(s); none had an Exception set on an IExceptionHandlerNode");
            }

            // Strategy 2: search ESpace properties for AbstractException-typed values
            if (allExc == null)
            {
                foreach (var t in AllTypes(es.GetType()))
                {
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        try
                        {
                            if (p.PropertyType.Name.Contains("Exception"))
                            {
                                var v = p.GetValue(es, null);
                                sb.AppendLine("es." + p.Name + " (" + p.PropertyType.Name + ") = " + (v?.GetType().Name ?? "null"));
                                if (v != null && (p.PropertyType.Name == "AbstractException" || v.GetType().Name.Contains("AllExceptions"))
                                    && (excType == null || excType.IsAssignableFrom(v.GetType())))
                                { allExc = v; sb.AppendLine("  -> selected"); }
                            }
                        }
                        catch { }
                    }
                }
            }

            // Strategy 3: search ModelServices for exception-related properties
            if (allExc == null)
            {
                var ms = ModelServices();
                if (ms != null)
                {
                    foreach (var t in AllTypes(ms.GetType()))
                    {
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            try
                            {
                                if (p.PropertyType.Name.Contains("Exception"))
                                {
                                    var v = p.GetValue(ms, null);
                                    sb.AppendLine("ms." + p.Name + " (" + p.PropertyType.Name + ") = " + (v?.GetType().Name ?? "null"));
                                    if (v != null && (p.PropertyType.Name == "AbstractException" || v.GetType().Name.Contains("AllExceptions"))
                                        && (excType == null || excType.IsAssignableFrom(v.GetType())))
                                    { allExc = v; sb.AppendLine("  -> selected"); }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // Strategy 4: find AbstractException type and search static fields/props
            if (allExc == null && excType != null)
            {
                sb.AppendLine("AbstractException type: " + excType.FullName);
                // Search static properties
                foreach (var p in excType.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    try
                    {
                        var v = p.GetValue(null, null);
                        sb.AppendLine("AbstractException." + p.Name + " = " + (v?.GetType().Name ?? "null"));
                        if (v != null && excType.IsAssignableFrom(v.GetType())) { allExc = v; sb.AppendLine("  -> selected"); break; }
                    }
                    catch { }
                }
                // Search static fields
                if (allExc == null)
                {
                    foreach (var f in excType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                    {
                        try
                        {
                            var v = f.GetValue(null);
                            sb.AppendLine("AbstractException field " + f.Name + " = " + (v?.GetType().Name ?? "null"));
                            if (v != null && excType.IsAssignableFrom(v.GetType())) { allExc = v; sb.AppendLine("  -> selected"); break; }
                        }
                        catch { }
                    }
                }
            }

            // Strategy 5: find all types containing "AllException" and search static members
            if (allExc == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var ty in asm.GetTypes())
                        {
                            if (ty.Name.Contains("AllException") && !ty.IsInterface && !ty.IsAbstract)
                            {
                                sb.AppendLine("Found type: " + ty.FullName);
                                foreach (var p in ty.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                                {
                                    try
                                    {
                                        var v = p.GetValue(null, null);
                                        sb.AppendLine("  " + ty.Name + "." + p.Name + " = " + (v?.GetType().Name ?? "null"));
                                        if (v != null && (excType == null || excType.IsAssignableFrom(v.GetType()))) { allExc = v; break; }
                                    }
                                    catch { }
                                }
                                if (allExc == null)
                                {
                                    foreach (var f in ty.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                                    {
                                        try
                                        {
                                            var v = f.GetValue(null);
                                            sb.AppendLine("  " + ty.Name + "." + f.Name + " = " + (v?.GetType().Name ?? "null"));
                                            if (v != null && (excType == null || excType.IsAssignableFrom(v.GetType()))) { allExc = v; break; }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    if (allExc != null) break;
                }
            }

            if (allExc == null) throw new Exception("Exception '" + targetName + "' not found\n" + sb.ToString());
            // FINAL TYPE GUARD: verify allExc is actually an AbstractException instance
            // (not a Func/delegate or other non-exception object a fallback strategy may
            // have grabbed). This prevents a confusing ArgumentException from SetProp and
            // produces a clear, actionable error instead.
            if (!excType.IsAssignableFrom(allExc.GetType()))
                throw new Exception("Found a candidate for '" + targetName + "' but it is NOT an AbstractException " +
                    "(type=" + allExc.GetType().FullName + "). Refusing to assign a non-exception object to the " +
                    "Exception property. Set the Exception property manually in Service Studio on this ErrorHandler " +
                    "node so Strategy 1 can copy it on future runs.\nSearch trace:\n" + sb.ToString());
            SetProp(node, "Exception", allExc);
            // Also set AbortTransaction and LogError to match the reference
            try { SetProp(node, "AbortTransaction", true); sb.AppendLine("set AbortTransaction=true"); } catch { }
            try { SetProp(node, "LogError", true); sb.AppendLine("set LogError=true"); } catch { }
            return "set Exception=" + allExc.GetType().Name + " (Name=" + targetName + ") on node[" + nodeIndex + "]\n" + sb.ToString();
        });
    }

    // debug_action_args: dump the Arguments collection of an IExecuteServerActionNode.
    // Shows each argument's Parameter name, current Value text, and the service action's
    // input parameters. Read-only diagnostic — use map_action_inputs to set values.
    static string DebugActionArgs(string moduleName, string actionName, int nodeIndex)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found" });
        var action = FindAction(es, actionName);
        if (action == null) return Json(new { ok = false, error = "action not found" });
        var nodes = NodeList(action);
        if (nodeIndex < 0 || nodeIndex >= nodes.Count) return Json(new { ok = false, error = "nodeIndex out of range" });
        var node = nodes[nodeIndex];
        if (!node.GetType().GetInterfaces().Any(i => i.Name == "IExecuteServerActionNode"))
            return Json(new { ok = false, error = "node[" + nodeIndex + "] is not IExecuteServerActionNode" });
        var sb = new StringBuilder();

        // Service action input parameters
        sb.AppendLine("=== Service action input parameters ===");
        var inPs = GetProp(action, "InputParameters") as IEnumerable;
        if (inPs != null) foreach (var p in inPs)
        {
            var nm = GetProp(p, "Name") as string ?? "?";
            var ty = TypeLabel(GetProp(p, "DataType"));
            sb.AppendLine("  " + nm + " : " + ty);
        }

        // ExecuteAction Arguments
        sb.AppendLine("=== ExecuteAction Arguments ===");
        var args = GetProp(node, "Arguments") as IEnumerable;
        if (args == null) { sb.AppendLine("  (null)"); }
        else
        {
            int i = 0;
            foreach (var arg in args)
            {
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "(null)";
                var valExpr = GetProp(arg, "Value");
                var valText = "?";
                if (valExpr != null) { var t = GetProp(valExpr, "Text") as string; valText = t ?? "(no Text)"; }
                sb.AppendLine("  Arg[" + i + "] Parameter=" + paramName + " Value=" + valText);
                // List settable properties on the argument for debugging
                var settable = new List<string>();
                foreach (var t in AllTypes(arg.GetType()))
                    foreach (var pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (pr.CanWrite) settable.Add(pr.Name);
                sb.AppendLine("    settable: " + string.Join(", ", settable.Distinct()));
                // List methods with 1 param
                var methods = new List<string>();
                foreach (var t in AllTypes(arg.GetType()))
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        if (!m.IsSpecialName && m.GetParameters().Length == 1 && (m.Name.StartsWith("Set") || m.Name.StartsWith("Bind")))
                            methods.Add(m.Name + "(" + m.GetParameters()[0].ParameterType.Name + ")");
                sb.AppendLine("    methods: " + string.Join(", ", methods.Distinct()));
                i++;
            }
            if (i == 0) sb.AppendLine("  (empty)");
        }
        return Json(new { ok = true, report = sb.ToString() });
    }

    // map_action_inputs: auto-map ExecuteAction input arguments by name. For each Argument
    // on the ExecuteAction node, finds the matching InputParameter on the service action
    // (by name) and sets the Argument's Value. Tries SetValue, then SetVariable, then
    // direct Value.Text manipulation.
    static string MapActionInputs(string moduleName, string actionName, int nodeIndex, string inputParamName = null)
    {
        return LiveEdit(moduleName, actionName, "map action inputs", (act, es) =>
        {            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IExecuteServerActionNode"))
                throw new Exception("node[" + nodeIndex + "] is not IExecuteServerActionNode (type=" + ShortName(node) + ")");
            var sb = new StringBuilder();

            // Get the service action's input parameters (name -> parameter object)
            var inputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var inPs = GetProp(act, "InputParameters") as IEnumerable;
            if (inPs != null) foreach (var p in inPs)
            {
                var nm = GetProp(p, "Name") as string;
                if (nm != null) { inputs[nm] = p; sb.AppendLine("input param: " + nm); }
            }

            // Get the ExecuteAction's Arguments
            var args = GetProp(node, "Arguments") as IEnumerable;
            if (args == null) throw new Exception("Arguments collection is null on ExecuteAction node");

            // If inputParamName is specified explicitly, use it for ALL arguments (Issue 5 fix).
            if (!string.IsNullOrEmpty(inputParamName))
            {
                sb.AppendLine("explicit inputParamName: " + inputParamName);
                foreach (var arg in args)
                {
                    bool mapped = false;
                    try { CallMethod(arg, "SetValue", new object[] { inputParamName }, 1); sb.AppendLine("  SetValue(\"" + inputParamName + "\") OK (explicit)"); mapped = true; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  SetValue err: " + r.Message); }
                    if (!mapped) sb.AppendLine("  FAILED to map with explicit " + inputParamName);
                }
                return sb.ToString();
            }

            int argCount = 0;
            foreach (var arg in args)
            {
                argCount++;
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "?") : "?";
                sb.AppendLine("arg[" + (argCount - 1) + "] param=" + paramName);

                if (inputs.TryGetValue(paramName, out var inputParam))
                {
                    sb.AppendLine("  matched input: " + paramName);
                    // Strategy 1: SetValue(string) � the SS model parser resolves the name to the input parameter
                    bool mapped = false;
                    try { CallMethod(arg, "SetValue", new object[] { paramName }, 1); sb.AppendLine("  SetValue(\"" + paramName + "\") OK"); mapped = true; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  SetValue err: " + r.Message); }

                    // Strategy 2: SetVariable(string)
                    if (!mapped) try { CallMethod(arg, "SetVariable", new object[] { paramName }, 1); sb.AppendLine("  SetVariable(\"" + paramName + "\") OK"); mapped = true; }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  SetVariable err: " + r.Message); }

                    // Strategy 3: Set Value.Text directly
                    if (!mapped) try
                    {
                        var valExpr = GetProp(arg, "Value");
                        if (valExpr != null) { SetProp(valExpr, "Text", paramName); sb.AppendLine("  Value.Text=\"" + paramName + "\" OK"); mapped = true; }
                    }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  Value.Text err: " + r.Message); }

                    // Strategy 4: BindTo / SetBinding
                    if (!mapped) try
                    {
                        var bindMethod = FindMethod(arg, "BindTo", 1) ?? FindMethod(arg, "SetBinding", 1);
                        if (bindMethod != null) { bindMethod.Invoke(arg, new object[] { inputParam }); sb.AppendLine("  " + bindMethod.Name + "(inputParam) OK"); mapped = true; }
                    }
                    catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  Bind err: " + r.Message); }

                    if (!mapped) sb.AppendLine("  FAILED to map " + paramName);
                }
                else
                {
                    sb.AppendLine("  no name match for " + paramName + ", trying type-aware fallback");
                    // Type-aware fallback (Issue 5 fix): match the argument's parameter type to
                    // an input param's type. This prevents picking the wrong param when multiple
                    // inputs exist (e.g. DietId + ProgressId from a clone).
                    var argParamType = param != null ? GetProp(param, "DataType") : null;
                    string matchedName = null;
                    if (argParamType != null)
                    {
                        foreach (var kv in inputs)
                        {
                            var inParamType = GetProp(kv.Value, "DataType");
                            if (inParamType != null && object.ReferenceEquals(inParamType, argParamType))
                            {
                                matchedName = kv.Key;
                                sb.AppendLine("  type match: " + matchedName);
                                break;
                            }
                        }
                    }
                    // If no type match, fall back to single-input case (only 1 input ? map directly)
                    if (matchedName == null && inputs.Count == 1)
                    {
                        matchedName = inputs.Keys.First();
                        sb.AppendLine("  single-input fallback: " + matchedName);
                    }
                    if (matchedName != null)
                    {
                        bool fmapped = false;
                        try { CallMethod(arg, "SetValue", new object[] { matchedName }, 1); sb.AppendLine("  SetValue(\"" + matchedName + "\") OK (fallback)"); fmapped = true; }
                        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  SetValue err: " + r.Message); }
                        if (!fmapped) sb.AppendLine("  FAILED to map with fallback " + matchedName);
                    }
                    else
                    {
                        sb.AppendLine("  no type match and multiple inputs � use inputParamName parameter to specify explicitly");
                    }
                }
            }
            if (argCount == 0) sb.AppendLine("no arguments on ExecuteAction node");
            return sb.ToString();
        });
    }

    // set_action_arg: set ONE argument value on an existing ExecuteAction node (IExecuteServerActionNode
    // or IExecuteClientActionNode) by its parameter name. This is the per-argument counterpart of
    // map_action_inputs (which maps by the action's INPUT PARAMETERS only). argName="*" sets ALL args
    // to the same value. Works for entity-action nodes, consumed server actions, and system client
    // actions (e.g. Navigate). Undo unit.
    static string SetActionArg(string moduleName, string actionName, int nodeIndex, string argName, string value)
    {
        if (string.IsNullOrEmpty(argName)) return Json(new { ok = false, error = "argName required" });
        return LiveEdit(moduleName, actionName, "set action arg", (act, es) =>
        {
            var nodes = NodeList(act);
            if (nodeIndex < 0 || nodeIndex >= nodes.Count) throw new Exception("nodeIndex out of range: " + nodeIndex);
            var node = nodes[nodeIndex];
            if (!node.GetType().GetInterfaces().Any(i => i.Name == "IExecuteServerActionNode" || i.Name == "IExecuteClientActionNode"))
                throw new Exception("node[" + nodeIndex + "] is not an ExecuteAction node (type=" + ShortName(node) + ")");
            var args = GetProp(node, "Arguments") as IEnumerable;
            if (args == null) throw new Exception("Arguments collection is null on ExecuteAction node");
            var sb = new StringBuilder();
            int done = 0;
            foreach (var arg in args)
            {
                var param = GetProp(arg, "Parameter");
                var paramName = param != null ? (GetProp(param, "Name") as string ?? "") : "";
                if (argName != "*" && !string.Equals(paramName, argName, StringComparison.OrdinalIgnoreCase)) continue;
                bool mapped = false;
                try { CallMethod(arg, "SetValue", new object[] { value }, 1); mapped = true; }
                catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; sb.AppendLine("  " + paramName + " SetValue err: " + r.Message); }
                if (!mapped) try { CallMethod(arg, "SetVariable", new object[] { value }, 1); mapped = true; } catch { }
                sb.AppendLine("  arg " + paramName + " = " + (mapped ? value : "FAILED"));
                if (mapped) done++;
            }
            sb.AppendLine("set " + done + " arg(s) on node[" + nodeIndex + "]");
            return sb.ToString();
        });
    }

    // debug_action_ref: create an IExecuteServerActionNode and try all possible property
    // names to set the server action reference. Use to find the correct property name.
    static string DebugActionRef(string moduleName, string actionName, string serverActionName, string producerModule)
    {
        if (string.IsNullOrEmpty(serverActionName)) return Json(new { ok = false, error = "serverActionName required" });
        return LiveEdit(moduleName, actionName, "debug action ref", (act, es) =>
        {
            var newNode = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IExecuteServerActionNode");
            object sa = null;
            if (!string.IsNullOrEmpty(producerModule))
            {
                var producerEs = FindEspace(producerModule);
                if (producerEs != null) sa = FindServerActionInEspace(producerEs, serverActionName);
            }
            if (sa == null) return Json(new { ok = false, error = "sa not found" });

            var settable = new List<string>();
            foreach (var t in AllTypes(newNode.GetType()))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.CanWrite) settable.Add(p.Name);
                }
            }

            // try to set each property with the server action object
            var tried = new List<string>();
            foreach (var propName in settable)
            {
                try
                {
                    var prop = newNode.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(newNode, sa, null);
                        tried.Add(propName + "=OK");
                    }
                }
                catch (Exception e)
                {
                    var r = e.GetBaseException() ?? e;
                    tried.Add(propName + "=FAIL(" + r.Message + ")");
                }
            }
            return Json(new { ok = true, settableProperties = settable, tried = tried, saType = sa.GetType().Name });
        });
    }

    // ---- entity-type input/output parameter support ----

    // add_entity_input: add an input parameter whose type is an entity from a consumed
    // reference (producer module). Searches es.References for the producer, then finds
    // the entity in the producer's Entities collection. Sets DataType to the entity object.
    static string AddEntityInput(string moduleName, string actionName, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName))
            return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(producerModule))
            return Json(new { ok = false, error = "producerModule required for entity input params" });
        return LiveEdit(moduleName, actionName, "add entity input param", (act, es) =>
        {
            var inP = CallMethod(act, "CreateInputParameter", new object[] { name, null }, 2);
            object entityType = FindEntityInReferences(es, entityName, producerModule);
            if (entityType == null)
                throw new Exception("entity not found: " + entityName + " in " + producerModule + " (check that the module is consumed and the entity is public)");
            SetProp(inP, "DataType", entityType);
            return "added entity input " + name + " : " + entityName + " (from " + producerModule + ")";
        });
    }

    // add_entity_identifier_input: add an input parameter whose type is an entity's Identifier
    // type (e.g. "Diet Identifier") from a consumed reference (producer module). This is the
    // correct type for Delete actions that take an Id parameter � NOT LongInteger. The entity's
    // identifier type is obtained from the entity's IdentifierType property (NOT the Id attribute's
    // DataType, which resolves to the base type LongInteger).
    static string AddEntityIdentifierInput(string moduleName, string actionName, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName))
            return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(producerModule))
            return Json(new { ok = false, error = "producerModule required for entity identifier input params" });
        return LiveEdit(moduleName, actionName, "add entity identifier input param", (act, es) =>
        {
            // Idempotency check: prevent duplicates.
            var existingInputs = GetProp(act, "InputParameters") as IEnumerable;
            if (existingInputs != null)
                foreach (var p in existingInputs)
                    try { if ((GetProp(p, "Name") as string) == name) throw new Exception("input param already exists: " + name); } catch (Exception ex) { if (ex.Message.Contains("already exists")) throw; }
            var entity = FindEntityInReferences(es, entityName, producerModule);
            if (entity == null)
                throw new Exception("entity not found: " + entityName + " in " + producerModule + " (check that the module is consumed and the entity is public)");
            // Get the entity's Identifier type from its IdentifierType property.
            // NOT from the Id attribute's DataType (which is the base type LongInteger).
            object identifierType = GetProp(entity, "IdentifierType");
            if (identifierType == null)
                throw new Exception("could not get IdentifierType from entity " + entityName + " (entity has no IdentifierType property)");
            var inP = CallMethod(act, "CreateInputParameter", new object[] { name, null }, 2);
            SetProp(inP, "DataType", identifierType);
            return "added entity identifier input " + name + " : " + entityName + " Identifier (from " + producerModule + ")";
        });
    }

    // add_entity_output: add an output parameter whose type is an entity from a consumed
    // reference (producer module). Same pattern as add_entity_input for output params.
    static string AddEntityOutput(string moduleName, string actionName, string name, string entityName, string producerModule)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(entityName))
            return Json(new { ok = false, error = "name and entityName required" });
        if (string.IsNullOrEmpty(producerModule))
            return Json(new { ok = false, error = "producerModule required for entity output params" });
        return LiveEdit(moduleName, actionName, "add entity output param", (act, es) =>
        {
            var outP = CallMethod(act, "CreateOutputParameter", new object[] { name, null }, 2);
            object entityType = FindEntityInReferences(es, entityName, producerModule);
            if (entityType == null)
                throw new Exception("entity not found: " + entityName + " in " + producerModule);
            SetProp(outP, "DataType", entityType);
            return "added entity output " + name + " : " + entityName + " (from " + producerModule + ")";
        });
    }

    // set_output_param_type: set the DataType of an existing output parameter by name.
    // Supports Structure types (by unique name in es.Structures) or entity types (from
    // a consumed producer module). Use after add_output which creates a param with "?"
    // type, then fix it here.
    static string SetOutputParamType(string moduleName, string actionName, string paramName, string typeName, string producerModule)
    {
        if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(typeName))
            return Json(new { ok = false, error = "paramName and typeName required" });
        return LiveEdit(moduleName, actionName, "set output param type", (act, es) =>
        {
            object outP = null;
            var outs = GetProp(act, "OutputParameters") as IEnumerable;
            if (outs != null) foreach (var p in outs) { try { if ((GetProp(p, "Name") as string) == paramName) { outP = p; break; } } catch { } }
            if (outP == null) throw new Exception("output param not found: " + paramName);
            object dataType = null;
            // Try basic types on eSpace (LongIntegerType, TextType, etc.)
            if (TryGetProp(es, typeName + "Type", out dataType) || TryGetProp(es, typeName, out dataType)) { }
            // Try structure types in the module
            else
            {
                var structures = GetProp(es, "Structures") as IEnumerable;
                if (structures != null)
                    foreach (var s in structures)
                        try { if ((GetProp(s, "Name") as string) == typeName) { dataType = s; break; } } catch { }
            }
            // Try entity type from a consumed reference
            if (dataType == null && !string.IsNullOrEmpty(producerModule))
                dataType = FindEntityInReferences(es, typeName, producerModule);
            if (dataType == null)
                throw new Exception("type not found: " + typeName + (producerModule != null ? " (producer: " + producerModule + ")" : ""));
            SetProp(outP, "DataType", dataType);
            return "set " + paramName + " DataType -> " + typeName + " (" + TypeLabel(dataType) + ")";
        });
    }

    // set_input_param_type: set the DataType of an existing INPUT parameter by name.
    // Mirrors set_output_param_type but searches InputParameters instead of OutputParameters.
    // Supports: basic types (LongInteger, Text, etc.), Structure types (by name),
    // entity types (whole record, from consumed producer), and entity Identifier types
    // (e.g. "Diet Identifier", resolved via entity.IdentifierType).
    // When entityName is provided (with producerModule): resolves the entity's IdentifierType.
    // When typeName is provided: resolves basic type, Structure, or entity record (same as set_output_param_type).
    static string SetInputParamType(string moduleName, string actionName, string paramName, string typeName, string producerModule, string entityName)
    {
        if (string.IsNullOrEmpty(paramName))
            return Json(new { ok = false, error = "paramName required" });
        if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(entityName))
            return Json(new { ok = false, error = "typeName or entityName required" });
        return LiveEdit(moduleName, actionName, "set input param type", (act, es) =>
        {
            // Find the input parameter by name.
            object inP = null;
            var ins = GetProp(act, "InputParameters") as IEnumerable;
            if (ins != null) foreach (var p in ins) { try { if ((GetProp(p, "Name") as string) == paramName) { inP = p; break; } } catch { } }
            if (inP == null) throw new Exception("input param not found: " + paramName);
            object dataType = string.IsNullOrEmpty(typeName) ? null : ResolveAttrDataType(es, typeName);
            if (dataType == null)
            {
                // If entityName is provided, resolve the entity's Identifier type.
                if (!string.IsNullOrEmpty(entityName))
                {
                    if (string.IsNullOrEmpty(producerModule))
                        throw new Exception("producerModule required when entityName is provided");
                    var entity = FindEntityInReferences(es, entityName, producerModule);
                    if (entity == null)
                        throw new Exception("entity not found: " + entityName + " in " + producerModule);
                    dataType = GetProp(entity, "IdentifierType");
                    if (dataType == null)
                        throw new Exception("could not get IdentifierType from entity " + entityName);
                }
            }
            if (dataType == null)
            {
                // Try basic types on eSpace (LongIntegerType, TextType, etc.)
                if (TryGetProp(es, typeName + "Type", out dataType) || TryGetProp(es, typeName, out dataType)) { }
                // Try structure types in the module
                else
                {
                    var structures = GetProp(es, "Structures") as IEnumerable;
                    if (structures != null)
                        foreach (var s in structures)
                            try { if ((GetProp(s, "Name") as string) == typeName) { dataType = s; break; } } catch { }
                }
                // Try entity type from a consumed reference (whole entity record)
                if (dataType == null && !string.IsNullOrEmpty(producerModule))
                    dataType = FindEntityInReferences(es, typeName, producerModule);
            }
            if (dataType == null)
                throw new Exception("type not found: " + typeName + (producerModule != null ? " (producer: " + producerModule + ")" : ""));
            SetProp(inP, "DataType", dataType);
            return "set " + paramName + " DataType -> " + (entityName ?? typeName) + " (" + TypeLabel(dataType) + ")";
        });
    }

    // Find an entity by name in a consumed reference's Entities collection.
    // Searches es.References for a reference whose Name matches producerModule,
    // then looks for the entity in that reference's ExtendedElement Entities collection.
    static object FindEntityInReferences(object es, string entityName, string producerModule)
    {
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return null;
        foreach (var r in refs)
        {
            var nm = GetProp(r, "Name") as string;
            if (nm != producerModule) continue;
            // Walk the extended element hierarchy to find the Entities collection.
            // The Sentinel on the Reference links to the producer eSpace.
            object sentinel = null;
            foreach (var p in r.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.Name == "Sentinel") { sentinel = p.GetValue(r, null); break; }
            if (sentinel == null) continue;
            // Look for Entities on the sentinel (the producer's entity collection)
            foreach (var p in sentinel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.Name != "Entities") continue;
                var entities = p.GetValue(sentinel, null) as IEnumerable;
                if (entities == null) continue;
                foreach (var e in entities)
                {
                    try
                    {
                        var ename = GetProp(e, "Name") as string;
                        if (ename == entityName) return e;
                    }
                    catch { }
                }
            }
            // Fallback: also check ReferenceEntities on the Reference element itself
            // (the consumer-side entity reference records)
            var refEnts = GetProp(r, "ReferenceEntities") as IEnumerable;
            if (refEnts != null)
                foreach (var re in refEnts)
                {
                    try
                    {
                        var reName = GetProp(re, "Name") as string;
                        if (reName == entityName) return re;
                    }
                    catch { }
                }
            // Generic fallback: scan EVERY IEnumerable collection on the reference and on the
            // sentinel for an object named entityName (collection property names vary by build).
            foreach (var hostObj in new[] { r, sentinel })
            {
                if (hostObj == null) continue;
                foreach (var p in hostObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length > 0) continue;
                    object coll = null;
                    try { coll = p.GetValue(hostObj, null); } catch { }
                    if (coll == null || coll is string) continue;
                    foreach (var item in (IEnumerable)coll)
                    {
                        try
                        {
                            var iname = GetProp(item, "Name") as string;
                            if (iname == entityName) return item;
                        }
                        catch { }
                    }
                }
            }
        }
        return null;
    }

    // ---- live dependency management (consume elements from producer modules) ----

    // The 15 consumable IESpace collections: (property name, display type, has Public flag).
    // All element types implement IShareable<ConcreteT, SignatureT> -> passable to
    // IESpace.AddDependency<ConcreteT, SignatureT>(IShareable<ConcreteT, SignatureT>).
    // 12 of 15 expose bool Public; the 3 without (WebFlow, MobileFlow, Folder) are
    // organizational containers consumed to reach their public children.
    static readonly (string prop, string type, bool hasPublic)[] ConsumableCollections = new[]
    {
        ("ServiceActions", "ServiceAction", true),
        ("ServerActions",  "ServerAction",  true),
        ("ClientActions",  "ClientAction",  true),
        ("Entities",       "Entity",        true),
        ("Structures",     "Structure",     true),
        ("Roles",          "Role",          true),
        ("Processes",      "Process",       true),
        ("Scripts",        "Script",        true),
        ("Images",         "Image",         true),
        ("Resources",      "Resource",      true),
        ("WebThemes",      "WebTheme",      true),
        ("MobileThemes",   "MobileTheme",   true),
        ("WebFlows",       "WebFlow",       false),
        ("MobileFlows",    "MobileFlow",    false),
        ("Folders",        "Folder",        false),
    };

    // list_consumable_elements: enumerate ALL public consumable elements in a producer
    // module, grouped by type. For types with Public: filter to Public=true. For containers
    // (WebFlow, MobileFlow, Folder): include all. Read-only.
    static string ListConsumableElements(string moduleName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });

        var elements = new Dictionary<string, object>();
        int total = 0;

        foreach (var (prop, type, hasPublic) in ConsumableCollections)
        {
            var list = new List<object>();
            try
            {
                var coll = GetProp(es, prop) as IEnumerable;
                if (coll == null) continue;
                foreach (var item in coll)
                {
                    try
                    {
                        string name = GetProp(item, "Name") as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (hasPublic && !((GetProp(item, "Public") as bool?) ?? false)) continue;
                        string key = "";
                        try { key = GetProp(item, "Key")?.ToString() ?? ""; } catch { }
                        list.Add(new { name = name, key = key });
                        total++;
                    }
                    catch { }
                }
            }
            catch { }
            if (list.Count > 0) elements[type] = list;
        }

        return Json(new { ok = true, module = moduleName, elements = elements, totalConsumable = total });
    }

    // consume_elements: consume (add references to) elements from a producer module into
    // a consumer module. All consumptions happen inside ONE Command.ExecuteFromAsyncCode
    // (single undo unit). The AddDependency generic method is resolved per-element via
    // reflection (type args extracted from the source object's IShareable<T,S> interface).
    //
    // what parameter:
    //   "*"                                 -> consume ALL consumable (all 15 types, public only)
    //   "ServerAction:*,Entity:*"           -> all of specific types
    //   "ServerAction:GetUser,Entity:User"  -> specific elements by name
    static string ConsumeElements(string consumerName, string producerName, string what)
    {
        var consumerEs = FindEspace(consumerName);
        if (consumerEs == null) return Json(new { ok = false, error = "consumer module not found: " + consumerName });
        var producerEs = FindEspace(producerName);
        if (producerEs == null) return Json(new { ok = false, error = "producer module not found: " + producerName });
        if (string.IsNullOrEmpty(what)) return Json(new { ok = false, error = "what parameter is required ('*' or 'Type:Name,Type:Name')" });

        // Parse "what" into a list of (collectionProp, displayType, nameOrAll)
        var requests = new List<(string prop, string type, string name, bool allOfType)>();
        if (what == "*")
        {
            foreach (var (p, t, _) in ConsumableCollections)
                requests.Add((p, t, null, true));
        }
        else
        {
            foreach (var entry in what.Split(',', ';'))
            {
                var parts = entry.Trim().Split(':', 2);
                if (parts.Length != 2) continue;
                string typeName = parts[0].Trim();
                string nameOrStar = parts[1].Trim();
                var match = ConsumableCollections.FirstOrDefault(c =>
                    string.Equals(c.type, typeName, StringComparison.OrdinalIgnoreCase));
                if (match.type == null) continue;
                requests.Add((match.prop, match.type, nameOrStar == "*" ? null : nameOrStar, nameOrStar == "*"));
            }
        }
        if (requests.Count == 0)
            return Json(new { ok = false, error = "no valid entries in 'what' parameter" });

        // Collect source elements from the producer (matching the parsed requests)
        var sourceElements = new List<(object element, string type, string name)>();
        foreach (var (prop, type, name, allOfType) in requests)
        {
            try
            {
                var coll = GetProp(producerEs, prop) as IEnumerable;
                if (coll == null) continue;
                bool hasPublic = ConsumableCollections.First(c => c.prop == prop).hasPublic;
                foreach (var item in coll)
                {
                    try
                    {
                        string itemName = GetProp(item, "Name") as string;
                        if (string.IsNullOrEmpty(itemName)) continue;
                        if (allOfType)
                        {
                            if (hasPublic && !((GetProp(item, "Public") as bool?) ?? false)) continue;
                            sourceElements.Add((item, type, itemName));
                        }
                        else if (itemName == name)
                        {
                            sourceElements.Add((item, type, itemName));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        if (sourceElements.Count == 0)
            return Json(new { ok = false, error = "no matching elements found in producer '" + producerName + "'" });

        // Find the generic AddDependency<ConcreteT, SignatureT>(IShareable<ConcreteT, SignatureT>)
        // method on IESpace. Must be the generic overload (IsGenericMethod), NOT the non-generic
        // Extensions.IAction overload (also 1 param, same name).
        MethodInfo addDepGeneric = null;
        foreach (var t in AllTypes(consumerEs.GetType()))
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "AddDependency" || !m.IsGenericMethod || m.GetParameters().Length != 1) continue;
                addDepGeneric = m; break;
            }
            if (addDepGeneric != null) break;
        }
        if (addDepGeneric == null)
            return Json(new { ok = false, error = "generic AddDependency<T,S> method not found on IESpace", sourceCount = sourceElements.Count });

        // Find the IShareable`2 open generic type (used to extract ConcreteT + SignatureT
        // from each source object's closed IShareable<T,S> interface).
        Type ishareableOpen = FindType("OutSystems.Model.IShareable`2");

        // Open one command on the consumer and consume all elements inside it (single undo unit)
        var agg = GetContext(consumerEs);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });

        var consumed = new List<object>();
        var errors = new List<string>();
        int consumedCount = 0, skippedCount = 0;
        string err = null;

        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });

            Action mutate = () =>
            {
                foreach (var (sourceEl, type, name) in sourceElements)
                {
                    try
                    {
                        // Extract ConcreteT + SignatureT from the source object's IShareable<T,S>
                        Type[] typeArgs = null;
                        if (ishareableOpen != null)
                        {
                            foreach (var iface in sourceEl.GetType().GetInterfaces())
                            {
                                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == ishareableOpen)
                                {
                                    typeArgs = iface.GetGenericArguments();
                                    break;
                                }
                            }
                        }
                        if (typeArgs == null)
                        {
                            errors.Add(type + ":" + name + " - IShareable<,> not found on " + sourceEl.GetType().Name);
                            continue;
                        }
                        MethodInfo closed = addDepGeneric.MakeGenericMethod(typeArgs);
                        closed.Invoke(consumerEs, new object[] { sourceEl });
                        consumed.Add(new { type = type, name = name, status = "added" });
                        consumedCount++;
                    }
                    catch (Exception e)
                    {
                        var r = e;
                        while (r.InnerException != null) r = r.InnerException;
                        string msg = r.Message ?? "";
                        if (msg.Contains("already") || msg.Contains("duplicate") || msg.Contains("exists") || msg.Contains("Duplicate"))
                        {
                            consumed.Add(new { type = type, name = name, status = "already consumed" });
                            skippedCount++;
                        }
                        else
                        {
                            errors.Add(type + ":" + name + " - " + r.GetType().Name + ": " + msg);
                        }
                    }
                }
            };

            exec.Invoke(null, new object[] { pc, "OsLiveBridge: consume " + sourceElements.Count + " elements from " + producerName, mutate });
        }
        catch (Exception e)
        {
            var r = e;
            while (r.InnerException != null) r = r.InnerException;
            err = r.GetType().Name + ": " + r.Message;
        }

        return Json(new
        {
            ok = consumedCount > 0 || skippedCount > 0,
            consumer = consumerName,
            producer = producerName,
            consumed = consumed,
            totalConsumed = consumedCount,
            totalSkipped = skippedCount,
            error = err,
            errors = errors
        });
    }

    // ---- reflection helpers (interface-aware, for explicit interface impls) ----
    static string SafeToString(object o) => o == null ? null : o.ToString();

    static object GetProp(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(obj, null);
            }
            foreach (var iface in obj.GetType().GetInterfaces())
            {
                var p = iface.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(obj, null);
            }
        }
        catch { }
        return null;
    }

    static bool TryGetProp(object obj, string name, out object result)
    {
        result = GetProp(obj, name);
        return result != null;
    }

    // SafeGetProp: public AND non-public properties across base types + interfaces (GetProp
    // only reads public instance). Used for diagnostic reads of guard state (e.g.
    // HasOpenedActiveESpace). Never throws - returns null on any failure.
    static object SafeGetProp(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    try { return p.GetValue(obj, null); } catch { }
                }
            }
            foreach (var iface in obj.GetType().GetInterfaces())
            {
                var p = iface.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    try { return p.GetValue(obj, null); } catch { }
                }
            }
        }
        catch { }
        return null;
    }

    // Read a private backing field (e.g. _customStyle) across the whole type hierarchy.
    // Returns null if not found or unreadable.
    static string GetFieldStr(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            foreach (var t in AllTypes(obj.GetType()))
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { var v = f.GetValue(obj); return v as string; }
            }
        }
        catch { }
        return null;
    }

    // Read a private field as object (may be a collection/expression, not a string).
    static object GetField(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            foreach (var t in AllTypes(obj.GetType()))
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
        }
        catch { }
        return null;
    }

    // Read the CSS text of a WebStyleSheet. CssSource is stored in the private field
    // _cssSource as a LightweightExpression whose lightweightElements are TextElements
    // carrying the CSS text. Try private fields first, then public properties.
    static string ReadCssText(object sheet)
    {
        try
        {
            foreach (var field in new[] { "_cssSource", "_userCssSource", "_generatedCssSource", "_finalCssSource" })
            {
                var expr = GetField(sheet, field);
                var text = ReadLightweightExpressionText(expr);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            foreach (var prop in new[] { "CssSource", "UserCssSource", "GeneratedCssSource" })
            {
                var expr = GetProp(sheet, prop);
                var text = ReadLightweightExpressionText(expr);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            try { var v = GetProp(sheet, "Value") as string; if (!string.IsNullOrEmpty(v)) return v; } catch { }
        }
        catch { }
        return null;
    }

    // Descend into a LightweightExpression object: lightweightElements (a list) of
    // TextElement objects each carrying CSS text in a 'text'-like field.
    static string ReadLightweightExpressionText(object expr)
    {
        if (expr == null) return null;
        var sb = new StringBuilder();
        object elements = null;
        try { elements = GetField(expr, "lightweightElements"); } catch { }
        if (elements == null) { try { elements = GetProp(expr, "lightweightElements") as IEnumerable; } catch { } }
        var coll = elements as IEnumerable;
        if (coll == null)
        {
            // Maybe the collection is held in _items/_array
            try { coll = GetField(expr, "_items") as IEnumerable ?? GetField(expr, "array") as IEnumerable ?? GetField(expr, "_array") as IEnumerable; } catch { }
        }
        if (coll != null)
        {
            foreach (var el in coll)
            {
                var leaf = el.GetType().Name;
                if (leaf.EndsWith("TextElement"))
                {
                    var text = GetFieldStr(el, "_text") ?? GetFieldStr(el, "text")
                             ?? GetProp(el, "Text") as string ?? GetProp(el, "text") as string
                             ?? GetProp(el, "Value") as string;
                    if (text != null) sb.Append(text);
                }
                else
                {
                    var text = GetFieldStr(el, "_text") ?? GetProp(el, "Text") as string ?? GetProp(el, "value") as string;
                    if (text != null) sb.Append(text);
                }
            }
            if (sb.Length > 0) return sb.ToString();
        }
        return null;
    }

    static MethodInfo FindMethod(object obj, string name, int paramCount)
    {
        if (obj == null) return null;
        for (var bt = obj.GetType(); bt != null; bt = bt.BaseType)
        {
            foreach (var m in bt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (m.Name == name && m.GetParameters().Length == paramCount) return m;
        }
        foreach (var iface in obj.GetType().GetInterfaces())
        {
            foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == name && m.GetParameters().Length == paramCount) return m;
        }
        return null;
    }

    static object CallMethod(object obj, string name, object[] args, int paramCount)
    {
        var m = FindMethod(obj, name, paramCount)
            ?? throw new Exception("method not found: " + name + "/" + paramCount + " on " + obj.GetType().FullName);
        return m.Invoke(obj, args);
    }

    // CallMethodExt: CallMethod first; on miss, scans ALL loaded assemblies for a STATIC
    // method (C# extension method) named `name` whose first parameter accepts obj, and
    // invokes it as static(obj, ...args). Needed because several model APIs the UI calls
    // are extension methods (e.g. CombineSources.MoveChildTo — reflection on the instance
    // type can't see them). Returns (result, viaLabel) semantics via out string.
    static object CallMethodExt(object obj, string name, object[] args, int paramCount, out string via)
    {
        try { via = "instance"; return CallMethod(obj, name, args, paramCount); }
        catch { }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                MethodInfo[] ms;
                try { ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static); } catch { continue; }
                foreach (var m in ms)
                {
                    if (m.Name != name) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != args.Length + 1) continue;
                    if (!ps[0].ParameterType.IsAssignableFrom(obj.GetType())) continue;
                    var full = new object[args.Length + 1];
                    full[0] = obj;
                    for (int i = 0; i < args.Length; i++) full[i + 1] = args[i];
                    via = "extension " + t.FullName;
                    return m.Invoke(null, full);
                }
            }
        }
        throw new Exception("method not found (instance or extension): " + name + "/" + paramCount + " on " + obj.GetType().FullName);
    }

    // CallMethodTyped: like CallMethod but picks the overload whose parameter types MATCH the
    // argument types (FindMethod returns the FIRST overload by name+arity, which can be the wrong
    // one - e.g. CustomProperty.SetValueExpression has BOTH SetValueExpression(String) and
    // SetValueExpression(IImageSignature); reflection order decides, causing non-deterministic
    // "Unable to cast String to IImageSignature" errors). Prefers exact param-type match, then
    // assignability, then falls back to the first by name+arity.
    static object CallMethodTyped(object obj, string name, object[] args)
    {
        if (obj == null) throw new Exception("CallMethodTyped: obj null");
        var candidates = new List<MethodInfo>();
        for (var bt = obj.GetType(); bt != null; bt = bt.BaseType)
            foreach (var m in bt.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (m.Name == name && m.GetParameters().Length == args.Length) candidates.Add(m);
        foreach (var iface in obj.GetType().GetInterfaces())
            foreach (var m in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == name && m.GetParameters().Length == args.Length) candidates.Add(m);
        if (candidates.Count == 0) throw new Exception("method not found: " + name + "/" + args.Length + " on " + obj.GetType().FullName);
        // Pass 1: exact parameter-type match.
        foreach (var m in candidates)
        {
            bool ok = true;
            var ps = m.GetParameters();
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) { ok = false; break; }
            if (ok) return m.Invoke(obj, args);
        }
        // Pass 2: assignable (interface/base match).
        foreach (var m in candidates)
        {
            bool ok = true;
            var ps = m.GetParameters();
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && !ps[i].ParameterType.IsAssignableFrom(args[i].GetType())) { ok = false; break; }
            if (ok) return m.Invoke(obj, args);
        }
        // Pass 3: first candidate (old behavior).
        return candidates[0].Invoke(obj, args);
    }

    // Get the live aggregator for the ESpace (PluginProvider.GetContext)
    static object GetContext(object es)
    {
        var gcp = typeof(PluginProvider).GetProperty("GetContext", BindingFlags.Public | BindingFlags.Static);
        var func = gcp?.GetValue(null, null) as Delegate;
        return func?.DynamicInvoke(es);
    }

    // Build a PresenterContext = new PresenterContext(aggregator, aggregator). The ctor is
    // public: PresenterContext(IBaseTopLevelPresenter source, IBaseTopLevelPresenter target).
    static object BuildPresenterContext(object aggregator)
    {
        try
        {
            var pcType = FindType("ServiceStudio.Presenter.PresenterContext");
            if (pcType == null) { Log("BuildPresenterContext: PresenterContext type not found"); return null; }
            var ctor = pcType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 2);
            if (ctor == null) { Log("BuildPresenterContext: 2-arg ctor not found"); return null; }
            return ctor.Invoke(new object[] { aggregator, aggregator });
        }
        catch (Exception e) { Log("BuildPresenterContext err: " + e); return null; }
    }

    // Find Command.Execute or Command.ExecuteFromAsyncCode (both are
    // static void (PresenterContext, string, Action)). Disambiguate from the
    // Func<CommandResult> overloads by matching the 3rd param to typeof(Action).
    static MethodInfo GetCommandExecuteMethod(string name)
    {
        var cmdType = FindType("ServiceStudio.Commands.Command");
        var pcType = FindType("ServiceStudio.Presenter.PresenterContext");
        if (cmdType == null || pcType == null) { Log("GetCommandExecuteMethod: Command/PresenterContext type missing"); return null; }
        foreach (var m in cmdType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != name) continue;
            var ps = m.GetParameters();
            if (ps.Length != 3) continue;
            if (ps[0].ParameterType != pcType) continue;
            if (ps[1].ParameterType != typeof(string)) continue;
            if (ps[2].ParameterType != typeof(Action)) continue;
            return m;
        }
        Log("GetCommandExecuteMethod: no match for " + name);
        return null;
    }

    // v7: call Command.ExecuteFromAsyncCode directly from the pipe thread (synchronous).
    // ExecuteFromAsyncCode is designed for non-UI/async callers; it uses locks, not a sync
    // UI marshal, so it should not deadlock. Returns the result immediately.
    static string TryCreateV7(string moduleName, string actionName, bool withSkeleton = false)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        // Duplicate name check: prevent creating a service action with a name that already exists.
        var existingSvcs = GetProp(es, "ServiceActions") as IEnumerable;
        if (existingSvcs != null)
            foreach (var s in existingSvcs)
                try { if ((GetProp(s, "Name") as string) == actionName) return Json(new { ok = false, error = "service action already exists: " + actionName }); } catch { }
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int svcBefore = CountProp(es, "ServiceActions");
        object created = null; string err = null; string via = null; string skeletonReport = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            // CRITICAL: catch INSIDE mutate (RunCmd pattern). Letting exceptions propagate
            // out of ExecuteFromAsyncCode wedges the pipe thread (no new connections; SS
            // itself stays alive). Seen 2026-09-16: try_create wedged the bridge twice.
            Action mutate = () =>
            {
                try
                {
                    var key = CallMethod(ms, "NewKey", null, 0);
                    // Path 1: direct factory (worked <= .81; throws on .83:
                    // "Service Action objects can't be children of Module objects").
                    try { created = CallMethod(es, "CreateServiceAction", new object[] { actionName, key }, 2); via += "/es.CreateServiceAction"; }
                    catch (Exception e1)
                    {
                        // Path 2..n: create through the ServiceAPIMethods ISSCollection (NOT the
                        // ServiceActions OfTypeIterator GetProp returns). .83 rejects module
                        // parenting; the collection Add parents correctly.
                        object coll = null;
                        try { coll = GetProp(es, "ServiceAPIMethods"); } catch { }
                        if (coll == null) throw new Exception("no ServiceAPIMethods collection: " + FirstMsg(e1));
                        Exception last = e1;
                        try { created = CallMethod(coll, "Create", new object[] { actionName, key }, 2); via += "/ServiceAPIMethods.Create(name,key)"; }
                        catch (Exception e2)
                        {
                            last = e2;
                            try { created = CallMethod(coll, "Add", new object[] { actionName, key }, 2); via += "/ServiceAPIMethods.Add(name,key)"; }
                            catch (Exception e3)
                            {
                                last = e3;
                                try
                                {
                                    var c0 = FindMethod(coll, "Create", 0);
                                    if (c0 == null) throw last;
                                    created = c0.Invoke(coll, null);
                                    try { SetProp(created, "Name", actionName); } catch { CallMethod(created, "SetName", new object[] { actionName }, 1); }
                                    CallMethod(coll, "Add", new object[] { created }, 1);
                                    via += "/ServiceAPIMethods.Create()+Name+Add";
                                }
                                catch (Exception e4)
                                {
                                    // Path 5: the WRITABLE ServiceAPIMethod ISSCollection - GetProp
                                    // returns a ReadOnlySSCollectionAdapter; the writable collection
                                    // lives in a private ESpace field. Scan fields and retry Create/Add.
                                    try
                                    {
                                        foreach (var wColl in FindWritableCollections(es, "ServiceAPIMethod"))
                                        {
                                            Exception lastW = null;
                                            foreach (var (methodName, args) in new (string, object[])[] {
                                                ("Create", new object[] { actionName, key }),
                                                ("Add", new object[] { actionName, key }) })
                                            {
                                                try
                                                {
                                                    created = CallMethod(wColl, methodName, args, args.Length);
                                                    if (created != null) { via += "/writable-" + methodName; break; }
                                                }
                                                catch (Exception w2) { lastW = w2; }
                                            }
                                            if (created == null && lastW != null) throw lastW;
                                            if (created != null) break;
                                        }
                                        if (created == null) throw new Exception("no writable ServiceAPIMethod collection");
                                    }
                                    catch (Exception eW)
                                    {
                                        // Path 6: EXHAUSTIVE self-validating search. Try every constructor × every
                                        // parent candidate; after each construction attempt Name+Add, then check
                                        // the ServiceActions COUNT — first combo with count+1 wins. No guessing.
                                        try
                                        {
                                        var t = FindType("ServiceStudio.Model.Flows+ServiceAPIMethod");
                                        if (t == null) throw new Exception("ServiceAPIMethod type not found");
                                        var msQ = ModelServices();
                                        var keyQ = CallMethod(msQ, "NewKey", null, 0);
                                        object adapterQ = null;
                                        try { adapterQ = GetProp(es, "ServiceAPIMethods"); } catch { }
                                        object folderQ = null;
                                        try
                                        {
                                            var folders = GetProp(es, "Folders") as IEnumerable;
                                            if (folders != null)
                                                foreach (var f in folders)
                                                {
                                                    bool sa = false;
                                                    try { var v = GetProp(f, "IsAServerActionsFolder"); if (v is bool b) sa = b; } catch { }
                                                    if (!sa) { try { var ef = GetProp(f, "ESpaceTreeFolder"); if (ef != null && ef.ToString().Contains("ServerActions")) sa = true; } catch { } }
                                                    if (sa) { folderQ = f; break; }
                                                }
                                        }
                                        catch { }
                                        var parents = new List<object>();
                                        if (folderQ != null) parents.Add(folderQ);
                                        if (adapterQ != null) parents.Add(adapterQ);
                                        parents.Add(es);
                                        int countBefore = CountProp(es, "ServiceActions");
                                        var tried = new List<string>();
                                        int attempts = 0;
                                        object obj = null; string ctorUsed = null;
                                        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                        {
                                            if (obj != null || attempts > 24) break;
                                            var ps = ctor.GetParameters();
                                            var argSets = new List<object[]>();
                                            if (ps.Length == 0) argSets.Add(new object[0]);
                                            else if (ps.Length == 1)
                                            {
                                                foreach (var par in parents)
                                                    if (ps[0].ParameterType.IsAssignableFrom(par.GetType())) argSets.Add(new object[] { par });
                                                if (ps[0].ParameterType == typeof(string)) { argSets.Add(new object[] { actionName }); argSets.Add(new object[] { null }); }
                                                // ObjectKey ctor: force-pass the fresh key even when the static
                                                // type check is inconclusive (last untried combo with rationale:
                                                // a key has no parent, so no parenting validation can fire).
                                                if (keyQ != null && (ps[0].ParameterType.IsAssignableFrom(keyQ.GetType()) || ps[0].ParameterType.Name.Contains("Key")))
                                                    argSets.Add(new object[] { keyQ });
                                            }
                                            else if (ps.Length == 2 && ps[1].ParameterType == typeof(string))
                                            {
                                                foreach (var par in parents)
                                                    if (ps[0].ParameterType.IsAssignableFrom(par.GetType()))
                                                    { argSets.Add(new object[] { par, null }); argSets.Add(new object[] { par, actionName }); }
                                            }
                                            foreach (var args in argSets)
                                            {
                                                if (obj != null || attempts > 24) break;
                                                attempts++;
                                                try
                                                {
                                                    var cand = ctor.Invoke(args);
                                                    if (cand == null) { tried.Add("null-result"); continue; }
                                                    try { SetProp(cand, "Name", actionName); } catch { try { CallMethod(cand, "SetName", new object[] { actionName }, 1); } catch { } }
                                                    if (adapterQ != null) { try { CallMethod(adapterQ, "Add", new object[] { cand }, 1); } catch (Exception ae) { tried.Add("add-fail:" + FirstMsg(ae)); continue; } }
                                                    int now = CountProp(es, "ServiceActions");
                                                    if (now == countBefore + 1) { obj = cand; ctorUsed = "exhaustive#" + attempts; break; }
                                                    tried.Add("no-count-change");
                                                }
                                                catch (Exception e) { tried.Add(FirstMsg(e)); }
                                            }
                                        }
                                        if (obj == null) throw new Exception("exhaustive search failed (" + attempts + " attempts): " + string.Join(" | ", tried.Take(8)));
                                        created = obj;
                                        via += "/exhaustive-create#" + ctorUsed;
                                    }
                                    catch (Exception e5) { throw new Exception("all create paths failed: " + FirstMsg(e1) + " | " + FirstMsg(last) + " | " + FirstMsg(e4) + " | writable: " + FirstMsg(eW) + " | " + FirstMsg(e5)); }
                                    }
                                }
                            }
                        }
                    }
                    // Skeleton via the UI's own call: AbstractFlow.CreateStandardNodes().
                    // Falls back to manual Start+End creation + link.
                    if (withSkeleton && created != null)
                    {
                        try
                        {
                            CallMethod(created, "CreateStandardNodes", null, 0);
                            skeletonReport = "Start-End skeleton via CreateStandardNodes.";
                        }
                        catch (Exception se0)
                        {
                            try
                            {
                                var startNode = CreateNodeGeneric(created, "OutSystems.Model.Logic.Nodes.IStartNode");
                                var endNode = CreateNodeGeneric(created, "OutSystems.Model.Logic.Nodes.IEndNode");
                                SetProp(startNode, "Target", endNode);
                                skeletonReport = "Start-End skeleton created and linked (manual; CreateStandardNodes: " + FirstMsg(se0) + ").";
                            }
                            catch (Exception se) { skeletonReport = "skeleton FAILED (action created, no skeleton): " + FirstMsg(se); }
                        }
                    }
                }
                catch (Exception me) { var r = me; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create service action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        // Note: do NOT UndoLastCommand here (RunCmd convention) - it crashed SS before,
        // and ExecuteFromAsyncCode auto-rolls-back propagated commands anyway.
        int svcAfter = CountProp(es, "ServiceActions");
        return Json(new { ok = created != null && err == null, via = via, createdType = created?.GetType().FullName, serviceActionsBefore = svcBefore, serviceActionsAfter = svcAfter, withSkeleton = withSkeleton, skeleton = skeletonReport, error = err });
    }

    // Create a Server Action (UserAction) in the open module, live. Same pattern as
    // TryCreateV7 but calls IESpace.CreateServerAction instead of CreateServiceAction.
    // Server actions live in the UserActions collection and have flows (IAction).
    static string TryCreateServerAction(string moduleName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(es, "UserActions");
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                try
                {
                    var key = CallMethod(ms, "NewKey", null, 0);
                    created = CallMethod(es, "CreateServerAction", new object[] { actionName, key }, 2);
                }
                catch (Exception me) { var r = me; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create server action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        int after = CountProp(es, "UserActions");
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, serverActionsBefore = before, serverActionsAfter = after, error = err });
    }

    // Create a Client Action in the open module, live. Same pattern as TryCreateV7
    // but calls IESpace.CreateClientAction. Client actions may live in a different
    // collection depending on the module type ( Reactive vs Traditional Web).
    static string TryCreateClientAction(string moduleName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(es, "ClientActions");
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                try
                {
                    var key = CallMethod(ms, "NewKey", null, 0);
                    created = CallMethod(es, "CreateClientAction", new object[] { actionName, key }, 2);
                }
                catch (Exception me) { var r = me; while (r.InnerException != null) r = r.InnerException; err = "mutate: " + r.GetType().Name + ": " + r.Message; }
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create client action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        int after = CountProp(es, "ClientActions");
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, clientActionsBefore = before, clientActionsAfter = after, error = err });
    }

    // TryCreateScreenClientAction: create a Client Screen Action (ClientScreenActionFlow) on a
    // specific SCREEN's ClientActions collection � NOT a global ClientActionFlow. A Reactive
    // screen Button's OnClick Destination is typed IClientSideDestination, which is implemented by
    // ClientScreenActionFlow (global ClientActionFlow is not). So the "function" a button calls
    // must live on the screen. Creates via the screen's CreateClientAction(name, key) (the same
    // IESpace-style factory the global path uses � on a screen it yields a ClientScreenActionFlow);
    // falls back to invoking the ClientScreenActionFlow(IParent<T>, name) ctor if the factory
    // isn't found on the screen type.
    static string TryCreateScreenClientAction(string moduleName, string screenName, string actionName)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        var sc = FindScreen(es, screenName);
        if (sc == null) return Json(new { ok = false, error = "screen not found: " + screenName });
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int before = CountProp(sc, "ClientActions");
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                try { created = CallMethod(sc, "CreateClientAction", new object[] { actionName, key }, 2); via += "/screen.CreateClientAction"; }
                catch (Exception e1)
                {
                    // Fallback: ClientScreenActionFlow(IParent<T>, String) ctor self-registers in its parent.
                    var cafType = FindType("ServiceStudio.Model.NRFlows+ClientScreenActionFlow");
                    if (cafType == null) { err = e1.Message + "; ClientScreenActionFlow type not found"; return; }
                    try { created = Activator.CreateInstance(cafType, new object[] { sc, actionName }); via += "/ctor(IParent,name)"; }
                    catch (Exception e2) { var r = e2; while (r.InnerException != null) r = r.InnerException; err = e1.Message + "; ctor: " + r.Message; }
                }
                if (err == null && created == null) err = "CreateClientAction returned null";
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create screen client action", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = err ?? (r.GetType().Name + ": " + r.Message); }
        int after = CountProp(sc, "ClientActions");
        return Json(new { ok = created != null, via = via, createdType = created?.GetType().FullName, clientActionsBefore = before, clientActionsAfter = after, error = err });
    }

    // Create a folder in the eSpace tree (e.g. ServiceActions section) live.
    // Calls IESpace.CreateFolder(ESpaceTreeFolder parentFolder, string name, IKey key)
    // inside Command.ExecuteFromAsyncCode. The ESpaceTreeFolder enum is resolved by
    // name via reflection (FindType + Enum.Parse). Default section: "ServiceActions".
    static string TryCreateFolder(string moduleName, string folderName, string parentFolder)
    {
        var es = FindEspace(moduleName);
        if (es == null) return Json(new { ok = false, error = "module not found: " + moduleName });
        if (string.IsNullOrWhiteSpace(folderName)) return Json(new { ok = false, error = "folderName is required" });
        // Resolve ESpaceTreeFolder enum (default: ServiceActions)
        var section = string.IsNullOrWhiteSpace(parentFolder) ? "ServiceActions" : parentFolder;
        var enumType = FindType("OutSystems.Model.Enumerations.ESpaceTreeFolder");
        if (enumType == null) return Json(new { ok = false, error = "ESpaceTreeFolder enum type not found" });
        object enumValue;
        try { enumValue = Enum.Parse(enumType, section, true); }
        catch (Exception ex) { return Json(new { ok = false, error = "invalid parentFolder '" + section + "': " + ex.Message }); }
        // Duplicate-name guard on es.Folders
        var existingFolders = GetProp(es, "Folders") as IEnumerable;
        if (existingFolders != null)
            foreach (var f in existingFolders)
                try { if ((GetProp(f, "Name") as string) == folderName) return Json(new { ok = false, error = "folder already exists: " + folderName }); } catch { }
        var ms = ModelServices();
        if (ms == null) return Json(new { ok = false, error = "ModelServices is null" });
        var agg = GetContext(es);
        if (agg == null) return Json(new { ok = false, error = "aggregator (GetContext) is null" });
        int foldersBefore = CountProp(es, "Folders");
        object created = null; string err = null; string via = null;
        try
        {
            var pc = BuildPresenterContext(agg);
            if (pc == null) return Json(new { ok = false, error = "PresenterContext null" });
            var exec = GetCommandExecuteMethod("ExecuteFromAsyncCode");
            if (exec == null) return Json(new { ok = false, error = "Command.ExecuteFromAsyncCode not found" });
            via = "pipe-thread->Command.ExecuteFromAsyncCode";
            Action mutate = () =>
            {
                var key = CallMethod(ms, "NewKey", null, 0);
                created = CallMethod(es, "CreateFolder", new object[] { enumValue, folderName, key }, 3);
            };
            exec.Invoke(null, new object[] { pc, "OsLiveBridge: create folder", mutate });
        }
        catch (Exception e) { var r = e; while (r.InnerException != null) r = r.InnerException; err = r.GetType().Name + ": " + r.Message; }
        if (err != null) { var ue = UndoLastCommand(agg); if (ue != null) err += " (rollback failed: " + ue + ")"; }
        int foldersAfter = CountProp(es, "Folders");
        return Json(new { ok = created != null && err == null, via = via, createdType = created?.GetType().FullName, folderName = folderName, parentFolder = section, foldersBefore = foldersBefore, foldersAfter = foldersAfter, error = err });
    }

    // Move a service/server/client action into an existing folder.
    // Uses the LiveEdit wrapper (finds action, opens command, runs mutate, rolls back on error).
    // The mutate callback finds the folder by name in es.Folders and sets action.Folder = folder.
    static string MoveToFolder(string moduleName, string actionName, string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return Json(new { ok = false, error = "folderName is required" });
        return LiveEdit(moduleName, actionName, "move to folder", (action, es) =>
        {
            object folder = null;
            var folders = GetProp(es, "Folders") as IEnumerable;
            if (folders != null)
                foreach (var f in folders)
                    try { if ((GetProp(f, "Name") as string) == folderName) { folder = f; break; } } catch { }
            if (folder == null) throw new Exception("folder not found: " + folderName);
            SetProp(action, "Folder", folder);
            return "moved '" + actionName + "' to folder '" + folderName + "'";
        });
    }

    static string NewKey()
    {
        byte[] kb = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(kb);
        return Convert.ToBase64String(kb).TrimEnd('=').Replace('/', '_');
    }

    static IEnumerable<Type> AllTypes(Type t)
    {
        for (var bt = t; bt != null; bt = bt.BaseType) yield return bt;
        foreach (var i in t.GetInterfaces()) yield return i;
    }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var ty = asm.GetType(fullName); if (ty != null) return ty; } catch { }
        }
        return null;
    }

    // Load-tolerant type scan: asm.GetType can return null when dependency loads fail lazily;
    // GetTypes + ReflectionTypeLoadException handling finds whatever types did load.
    static Type FindTypeLoaded(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            if (types == null) continue;
            foreach (var ty in types)
                if (ty != null && ty.FullName == fullName) return ty;
        }
        return null;
    }

    // Fallback type resolver: finds a type by its simple (unqualified) name across
    // all loaded assemblies. Used when FindType(fullName) returns null because the
    // type is nested or namespaced differently in a particular SS build. Handles
    // ReflectionTypeLoadException gracefully (uses whatever types did load).
    // probe_type: dump a type's static members + instance property/method names by full or short
    // name. Diagnostic for descriptor resolution gaps (e.g. NRWebWidgets+If+Kind surface).
    static string ProbeType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return Json(new { ok = false, error = "typeName required" });
        var t = FindType(typeName) ?? FindTypeLoaded(typeName);
        if (t == null)
        {
            // short-name scan (handles nested/namespace drift)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (var ty in types)
                    if (ty != null && (ty.FullName != null && ty.FullName.EndsWith(typeName))) { t = ty; break; }
                if (t != null) break;
            }
        }
        if (t == null) return Json(new { ok = false, error = "type not found: " + typeName });
        var statics = new List<string>();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            statics.Add("field " + f.Name + " : " + f.FieldType.Name);
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            if (p.GetIndexParameters().Length == 0) statics.Add("sprop " + p.Name + " : " + p.PropertyType.Name);
        var props = new List<string>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.GetIndexParameters().Length == 0) props.Add(p.Name + " : " + p.PropertyType.Name);
        var methods = new List<string>();
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (!m.IsSpecialName) methods.Add(m.Name + "(" + string.Join(",", m.GetParameters().Select(pp => pp.ParameterType.Name)) + ")");
        var nested = new List<string>();
        foreach (var n in t.GetNestedTypes()) nested.Add(n.Name);
        return Json(new { ok = true, type = t.FullName, statics = statics, instanceProps = props, instanceMethods = methods, nested = nested });
    }

    // probe_references: dump each module reference and the element NAMES inside every IEnumerable
    // collection it (and its Sentinel) exposes - diagnostic for entity/aggregate source lookup.
    static string ProbeReferences(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var refs = GetProp(es, "References") as IEnumerable;
        var list = new List<object>();
        if (refs != null)
            foreach (var r in refs)
            {
                var entry = new Dictionary<string, object>();
                entry["reference"] = GetProp(r, "Name") as string;
                var colls = new Dictionary<string, object>();
                foreach (var hostObj in new[] { r, (object)(GetProp(r, "Sentinel")) })
                {
                    if (hostObj == null) continue;
                    var hostLabel = hostObj.GetType().Name;
                    foreach (var p in hostObj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!typeof(IEnumerable).IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length > 0) continue;
                        object coll = null;
                        try { coll = p.GetValue(hostObj, null); } catch { }
                        if (coll == null || coll is string) continue;
                        var names = new List<string>();
                        try
                        {
                            foreach (var item in (IEnumerable)coll)
                            {
                                var n = GetProp(item, "Name") as string ?? item.GetType().Name;
                                names.Add(n);
                                if (names.Count >= 30) break;
                            }
                        }
                        catch { }
                        if (names.Count > 0) colls[hostLabel + "." + p.Name] = names;
                    }
                }
                entry["collections"] = colls;
                list.Add(entry);
            }
        return Json(new { ok = true, references = list });
    }

    // probe_system_actions: WHERE do Reactive system-action prototypes live? Dumps
    // (1) the (System) reference with EVERY property: name, type, enumerable item count
    //     (including ZERO-count collections - probe_references hides empties),
    // (2) es-level SystemActions/SystemClientActions/ConsumedServerActions counts,
    // (3) every method on the ESpace whose name contains System/Consumed/Reference.
    // Read-only diagnostic.
    static string ProbeSystemActions(string module)
    {
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        var sysRefs = new List<object>();
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs != null)
            foreach (var r in refs)
            {
                var nm = GetProp(r, "Name") as string ?? "";
                if (nm != "(System)" && nm != "System" && !nm.Contains("System")) continue;
                var props = new List<object>();
                foreach (var p in r.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    object val = null;
                    try { val = p.GetValue(r, null); } catch { continue; }
                    if (val is IEnumerable en && !(val is string))
                    {
                        int count = 0; var first = new List<string>();
                        foreach (var it in en)
                        {
                            count++;
                            if (first.Count < 12)
                            {
                                var iname = "?";
                                try { iname = GetProp(it, "Name") as string ?? it.GetType().Name; } catch { iname = it.GetType().Name; }
                                first.Add(iname);
                            }
                        }
                        props.Add(new { prop = p.Name, kind = "collection", count = count, first = first });
                    }
                }
                sysRefs.Add(new { reference = nm, type = r.GetType().FullName, props = props });
            }
        var esCounts = new Dictionary<string, object>();
        foreach (var cn in new[] { "SystemActions", "SystemClientActions", "ConsumedServerActions", "UserActions", "ClientActionFlows" })
        {
            try
            {
                var c = GetProp(es, cn) as IEnumerable;
                int n = 0; var names = new List<string>();
                if (c != null) foreach (var it in c) { n++; if (names.Count < 15) { try { names.Add(GetProp(it, "Name") as string ?? "?"); } catch { } } }
                esCounts[cn] = new { count = n, names = names };
            }
            catch { esCounts[cn] = "missing"; }
        }
        var esMethods = new List<string>();
        foreach (var t in AllTypes(es.GetType()))
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if ((m.Name.Contains("System") || m.Name.Contains("Consumed") || m.Name.Contains("Builtin")) && !esMethods.Contains(m.Name))
                    esMethods.Add(m.Name + "/" + m.GetParameters().Length);
        return Json(new { ok = true, systemReferences = sysRefs, esCollections = esCounts, esMethods = esMethods });
    }

    // CollectionContains: reference-presence check tolerant of live/empty sequences.
    static bool CollectionContains(object coll, object item)
    {
        if (coll == null || item == null) return false;
        try
        {
            var en = coll as IEnumerable;
            if (en == null) return false;
            foreach (var it in en)
                if (object.ReferenceEquals(it, item)) return true;
        }
        catch { }
        return false;
    }

    // ============ P-B: Reactive system CLIENT actions (list ops) ============
    // DISCOVERY (2026-09-17): Reactive list-ops live ONLY as ReferenceClientAction items
    // under the (System) reference's ReferenceClientActions (proven from KOPA.oml's
    // serialized References fragment); there is NO server-side List* in Reactive. The
    // platform constants (OriginalKey + signature hashes + param OriginalKeys/types) are
    // STABLE platform-wide - copied verbatim from KOPA's serialization.
    // Type keys: %xJyn4vH0KkCfmSAS_CIQBg = generic Record List, %NRGbx9G_WEG4DdPOY7+u7Q =
    // generic Record (matches BasicTypes 'Generic Record'), %oD0fxvc7hUOX_305zIXIlg = Boolean.
    class SysClientActionSpec
    {
        public string Name, OriginalKey, FullSig, CompatSig, Desc;
        public (string name, string typeKey, bool mandatory, string lambdaKey, int index, string origKey)[] Params;
        public (string name, string typeKey, int index, string origKey)[] Outputs;
    }
    static readonly SysClientActionSpec[] SysClientActionCatalog = new[]
    {
        new SysClientActionSpec {
            Name = "ListAppend", OriginalKey = "HgJbqjfpgEuis8lbQzmhHQ",
            FullSig = "P9vLJDrFEGq2lb2ojqkLNw", CompatSig = "Ul9+ZoqqXRa7giOElM3SFg",
            Desc = "Adds an element to the end of a list.",
            Params = new (string, string, bool, string, int, string)[] {
                ("List", "xJyn4vH0KkCfmSAS_CIQBg", true, null, 0, "l_j9meiyyUyDw+vaT8ptFA"),
                ("Element", "NRGbx9G_WEG4DdPOY7+u7Q", true, null, 1, "hV5BY_2mPkekuOAAGNH9TQ") },
            Outputs = new (string, string, int, string)[0] },
        new SysClientActionSpec {
            Name = "ListAppendAll", OriginalKey = "kwLtz+2X90KNWlolguyx2g",
            FullSig = "fSohlNyesVwJedhRIkD3Gg", CompatSig = "ipONMQ0GL2MXI_VuhOW_QA",
            Desc = "Adds the elements of the source list to the end of the destination list.",
            Params = new (string, string, bool, string, int, string)[] {
                ("Destination", "xJyn4vH0KkCfmSAS_CIQBg", true, null, 0, null),
                ("Source", "xJyn4vH0KkCfmSAS_CIQBg", true, null, 1, null) },
            Outputs = new (string, string, int, string)[0] },
        new SysClientActionSpec {
            Name = "ListFilter", OriginalKey = "UsE3UVlN9ki4bra457HhYA",
            FullSig = "kwfo68vLekX+JRQ6NtFtfg", CompatSig = "Uw6EY+M5mKaZARUby0+DKg",
            Desc = "Returns a new list with the elements from the List parameter satisfying the given condition.",
            Params = new (string, string, bool, string, int, string)[] {
                ("SourceList", "xJyn4vH0KkCfmSAS_CIQBg", true, null, 0, "BBK7rrrqbkS3+iMThRuvSA"),
                ("Condition", "oD0fxvc7hUOX_305zIXIlg", true, "NRGbx9G_WEG4DdPOY7+u7Q", 1, "FgcwzwBSdEuaypBuTcN8PQ") },
            Outputs = new (string, string, int, string)[] {
                ("FilteredList", "xJyn4vH0KkCfmSAS_CIQBg", 2, "EX8Srz89pUCh+sukNbSz_Q") } },
    };

    // Resolve a platform type key ("xJyn...") to an AbstractType object: scan BasicTypes,
    // ListTypes, Structures by ObjectKey match; fallback by-name for the generic forms.
    static object ResolveTypeKey(object es, string key)
    {
        foreach (var cn in new[] { "BasicTypes", "ListTypes", "Structures" })
        {
            var coll = GetProp(es, cn) as IEnumerable;
            if (coll == null) continue;
            foreach (var t in coll)
            {
                try
                {
                    var k = GetProp(t, "Key") as string;
                    if (k == key) return t;
                    var gk = GetProp(t, "ObjectKey") as string;
                    if (gk != null && (gk == key || gk.Contains(key))) return t;
                }
                catch { }
            }
        }
        // By-name fallbacks for the generic forms.
        if (key == "NRGbx9G_WEG4DdPOY7+u7Q")
        {
            var bt = GetProp(es, "BasicTypes") as IEnumerable;
            if (bt != null)
                foreach (var t in bt)
                    try { if (((GetProp(t, "Name") as string) ?? "").Contains("Generic Record")) return t; } catch { }
        }
        return null;
    }

    static string ConsumeSystemClientAction(string module, string name)
    {
        if (string.IsNullOrEmpty(name)) return Json(new { ok = false, error = "name required" });
        var es = FindEspace(module);
        if (es == null) return Json(new { ok = false, error = "module not found: " + module });
        object sysRef = null;
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs != null)
            foreach (var rf in refs)
                try { if ((GetProp(rf, "Name") as string) == "(System)") { sysRef = rf; break; } } catch { }
        if (sysRef == null) return Json(new { ok = false, error = "(System) reference not found" });
        var spec = SysClientActionCatalog.FirstOrDefault(s => s.Name == name);
        if (spec == null) return Json(new { ok = false, error = "no platform spec for '" + name + "' (catalog: " + string.Join(",", SysClientActionCatalog.Select(s => s.Name)) + ")" });
        var coll = GetProp(sysRef, "ReferenceClientActions") as IEnumerable;
        if (coll != null)
            foreach (var it in coll)
                try { if ((GetProp(it, "Name") as string) == name) return Json(new { ok = true, alreadyPresent = true, name = name }); } catch { }
        string step = "start";
        string r;
        try
        {
        r = RunCmd(module, "consume system client action " + name, es2 =>        {
            Action<string> mark = s => step = s;
            mark("reload-reference");
            var ref2 = FindEspace(module) == null ? null : GetSysRef(es2);
            if (ref2 == null) throw new Exception("(System) reference not found after reload");
            mark("get-collections");
            var coll2 = GetProp(ref2, "ReferenceClientActions");
            var t = FindType("ServiceStudio.Model.ReferenceClientAction");
            if (t == null) throw new Exception("ReferenceClientAction type not found");
            object rca = null; string ctorUsed = null; var errs = new List<string>();
            // Derive the KEY TYPE from the (Reference, string, KeyType) ctor itself - no name hunting.
            ctorUsed = null;
            var ctor3 = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => { var ps = c.GetParameters(); return ps.Length == 3 && ps[0].ParameterType.IsAssignableFrom(ref2.GetType()) && ps[1].ParameterType == typeof(string); });
            if (ctor3 != null)
            {
                var keyType = ctor3.GetParameters()[2].ParameterType;
                object platKey = null; string keyHow = null;
                foreach (var kc in keyType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var kps = kc.GetParameters();
                    if (kps.Length == 1 && kps[0].ParameterType == typeof(string))
                    {
                        try { platKey = kc.Invoke(new object[] { spec.OriginalKey }); keyHow = "ctor(string)"; break; }
                        catch (Exception e) { errs.Add("keyCtor: " + FirstMsg(e)); }
                    }
                }
                if (platKey == null)
                {
                    var parse = keyType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => (m.Name == "Parse" || m.Name == "FromString") && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                    if (parse != null)
                    { try { platKey = parse.Invoke(null, new object[] { spec.OriginalKey }); keyHow = parse.Name + "(string)"; } catch (Exception e) { errs.Add(parse.Name + ": " + FirstMsg(e)); } }
                }
                if (platKey == null) errs.Add("no string key ctor/Parse on " + keyType.FullName);
                else
                {
                    try { rca = ctor3.Invoke(new object[] { ref2, spec.Name, platKey }); ctorUsed = "(Reference,name," + keyType.Name + " via " + keyHow + ")"; }
                    catch (Exception e) { errs.Add("3p invoke: " + FirstMsg(e)); }
                }
            }
            else errs.Add("no (Reference,string,*) ctor on " + t.FullName);
            if (rca == null) throw new Exception("no ReferenceClientAction ctor worked (" + string.Join(";", errs) + ")");
            // Key factory: derive the platform key type from the ctor we used.
            Func<string, object> keyFactory = null;
            {
                var ctor3b = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(c => { var ps = c.GetParameters(); return ps.Length == 3 && ps[1].ParameterType == typeof(string); });
                if (ctor3b != null)
                {
                    var keyType = ctor3b.GetParameters()[2].ParameterType;
                    var kc = keyType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(string));
                    if (kc != null) keyFactory = s => kc.Invoke(new object[] { s });
                }
            }
            mark("set-name");
            CallMethod(rca, "SetName", new object[] { spec.Name }, 1);
            mark("description");
            try { SetPropForce(rca, "Description", spec.Desc); } catch { }
            if (keyFactory != null) { try { SetPropForce(rca, "OriginalKey", keyFactory(spec.OriginalKey)); } catch (Exception e) { throw new Exception("OriginalKey: " + FirstMsg(e)); } }
            try { SetPropForce(rca, "OriginalName", spec.Name); } catch { }
            Func<string, Guid> guidFromB64 = s =>
            {
                while (s.Length % 4 != 0) s += "=";
                return new Guid(Convert.FromBase64String(s));
            };
            try { SetPropForce(rca, "FullSignatureHash", guidFromB64(spec.FullSig)); } catch (Exception e) { throw new Exception("FullSignatureHash: " + FirstMsg(e)); }
            try { SetPropForce(rca, "CompatibilitySignatureHash", guidFromB64(spec.CompatSig)); } catch (Exception e) { throw new Exception("CompatibilitySignatureHash: " + FirstMsg(e)); }
            var notes = new List<string> { "ctor " + ctorUsed };
            // Key factory for params whose spec has no platform OriginalKey.
            Func<string, object> anyKey = s => keyFactory != null ? keyFactory(s ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=')) : null;
            // Input parameters: construct ReferenceGenericInputParameter(rca, name, ObjectKey) directly.
            mark("get-input-coll");
            object inputs = null;
            foreach (var pn in new[] { "InputParameters", "Parameters" })
            { try { inputs = GetProp(rca, pn); } catch { } if (inputs != null) break; }
            if (inputs == null) throw new Exception("no InputParameters on ReferenceClientAction");
            var ripType = FindType("ServiceStudio.Model.Variables+ReferenceGenericInputParameter") ?? FindType("ServiceStudio.Model.Variables.ReferenceGenericInputParameter") ?? FindTypeLoaded("ServiceStudio.Model.Variables+ReferenceGenericInputParameter");
            if (ripType == null) throw new Exception("ReferenceGenericInputParameter type not found");
            foreach (var p in spec.Params)
            {
                mark("param-ctor " + p.name);
                object ip = null; string ipVia = null;
                foreach (var ctor in ripType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ps = ctor.GetParameters();
                    try
                    {
                        if (ps.Length == 3 && ps[0].ParameterType.IsInstanceOfType(rca) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType.Name == "ObjectKey")
                        { ip = ctor.Invoke(new object[] { rca, p.name, anyKey(p.origKey) }); ipVia = "(rca,name,ObjectKey)"; break; }
                    }
                    catch (Exception e) { throw new Exception("input param " + p.name + " ctor: " + FirstMsg(e)); }
                }
                if (ip == null) throw new Exception("cannot create input param " + p.name);
                mark("param-add " + p.name);
                // Constructing with parent=rca may AUTO-REGISTER the child. Only add if absent.
                bool addedOk = CollectionContains(inputs, ip);
                if (!addedOk)
                {
                    foreach (var mn in new[] { "Add", "AddChild" })
                    { try { CallMethod(inputs, mn, new object[] { ip }, 1); addedOk = true; break; } catch { } }
                }
                if (!addedOk)
                {
                    var ipi = rca.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IParent`1" && i.GetGenericArguments().Length == 1 && i.GetGenericArguments()[0].Name == "ReferenceGenericInputParameter");
                    if (ipi != null)
                        foreach (var m in ipi.GetMethods())
                            if (m.Name == "AddChild" && m.GetParameters().Length == 2)
                            {
                                try { m.Invoke(rca, new object[] { ip, false }); addedOk = true; }
                                catch (InvalidOperationException) { addedOk = CollectionContains(inputs, ip); }
                                break;
                            }
                }
                if (!addedOk) throw new Exception("cannot add input param " + p.name + " to collection");
                notes.Add("param " + p.name + " " + ipVia);
                mark("param-type " + p.name);
                var tObj = ResolveTypeKey(es2, p.typeKey);
                bool typed = false;
                if (tObj != null)
                {
                    foreach (var tpn in new[] { "DataType", "Type" })
                    {
                        try { SetPropForce(ip, tpn, tObj); typed = true; break; } catch { }
                    }
                }
                if (!typed) notes.Add("param " + p.name + " TYPE UNRESOLVED key=" + p.typeKey);
                else notes.Add("param " + p.name + " typed");
                mark("param-mand " + p.name);
                try { SetPropForce(ip, "IsMandatory", p.mandatory); } catch { }
                mark("param-okey " + p.name);
                if (keyFactory != null && !string.IsNullOrEmpty(p.origKey)) { try { SetPropForce(ip, "OriginalKey", keyFactory(p.origKey)); } catch { } }
                if (p.lambdaKey != null)
                {
                    mark("param-lambda " + p.name);
                    var lObj = ResolveTypeKey(es2, p.lambdaKey);
                    if (lObj != null) { try { SetPropForce(ip, "LambdaParameterType", lObj); notes.Add("lambda typed"); } catch { } }
                }
            }
            // Output parameters: construct ReferenceGenericOutputParameter(rca, name, ObjectKey) directly.
            object outputs = null;
            try { outputs = GetProp(rca, "OutputParameters"); } catch { }
            var ropType = FindType("ServiceStudio.Model.Variables+ReferenceGenericOutputParameter") ?? FindType("ServiceStudio.Model.Variables.ReferenceGenericOutputParameter") ?? FindTypeLoaded("ServiceStudio.Model.Variables+ReferenceGenericOutputParameter");
            foreach (var o in spec.Outputs)
            {
                mark("output-ctor " + o.name);
                if (outputs == null || ropType == null) { notes.Add("output " + o.name + " SKIPPED (no collection/type)"); continue; }
                object op = null;
                foreach (var ctor in ropType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var ps = ctor.GetParameters();
                    try
                    {
                        if (ps.Length == 3 && ps[0].ParameterType.IsInstanceOfType(rca) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType.Name == "ObjectKey")
                        { op = ctor.Invoke(new object[] { rca, o.name, anyKey(o.origKey) }); break; }
                    }
                    catch (Exception e) { throw new Exception("output param " + o.name + " ctor: " + FirstMsg(e)); }
                }
                if (op == null) { notes.Add("output " + o.name + " CREATE FAILED"); continue; }
                mark("output-add " + o.name);
                bool addedOk = CollectionContains(outputs, op);
                if (!addedOk)
                {
                    foreach (var mn in new[] { "Add", "AddChild" })
                    { try { CallMethod(outputs, mn, new object[] { op }, 1); addedOk = true; break; } catch { } }
                }
                if (!addedOk)
                {
                    var opi = rca.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IParent`1" && i.GetGenericArguments().Length == 1 && i.GetGenericArguments()[0].Name == "ReferenceGenericOutputParameter");
                    if (opi != null)
                        foreach (var m in opi.GetMethods())
                            if (m.Name == "AddChild" && m.GetParameters().Length == 2)
                            {
                                try { m.Invoke(rca, new object[] { op, false }); addedOk = true; }
                                catch (InvalidOperationException) { addedOk = CollectionContains(outputs, op); }
                                break;
                            }
                }
                if (!addedOk) { notes.Add("output " + o.name + " ADD FAILED"); continue; }
                mark("output-type " + o.name);
                var tObj = ResolveTypeKey(es2, o.typeKey);
                bool typed = false;
                if (tObj != null)
                    foreach (var tpn in new[] { "DataType", "Type" })
                    { try { SetPropForce(op, tpn, tObj); typed = true; break; } catch { } }
                notes.Add("output " + o.name + (typed ? " typed" : " TYPE UNRESOLVED key=" + o.typeKey));
                if (keyFactory != null && !string.IsNullOrEmpty(o.origKey)) { try { SetPropForce(op, "OriginalKey", keyFactory(o.origKey)); } catch { } }
            }
            // Add to the reference collection (constructor may have auto-registered).
            mark("rca-add");
            bool added = CollectionContains(coll2, rca);
            if (!added)
            {
                foreach (var mn in new[] { "Add", "AddChild" })
                {
                    try { CallMethod(coll2, mn, new object[] { rca }, 1); added = true; break; } catch { }
                }
            }
            if (!added)
            {
                var rpi = ref2.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IParent`1" && i.GetGenericArguments().Length == 1 && i.GetGenericArguments()[0].Name == "ReferenceClientAction");
                if (rpi != null)
                    foreach (var m in rpi.GetMethods())
                        if (m.Name == "AddChild" && m.GetParameters().Length == 2)
                        {
                            try { m.Invoke(ref2, new object[] { rca, false }); added = true; }
                            catch (InvalidOperationException) { added = CollectionContains(coll2, rca); }
                            break;
                        }
            }
            if (!added) throw new Exception("could not add ReferenceClientAction to (System).ReferenceClientActions");
            notes.Add("added to ReferenceClientActions");
            return string.Join("; ", notes);
        });
        return Json(new { ok = true, name = name, report = r, lastStep = step });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, name = name, error = ex.Message, lastStep = step, inner = ex.InnerException?.Message });
        }
    }

    static object GetSysRef(object es)
    {
        var refs = GetProp(es, "References") as IEnumerable;
        if (refs == null) return null;
        foreach (var r in refs)
            try { if ((GetProp(r, "Name") as string) == "(System)") return r; } catch { }
        return null;
    }

    // add_client_action_call: NRNodes.ExecuteClientAction node calling a SYSTEM client
    // action (ReferenceClientAction under (System)). args = JSON array of {name, value}
    // mapped onto the node's Arguments (same AbstractExecuteAction surface as server calls).
    static string AddClientActionCall(string module, string actionName, string screen, string block, string systemAction, string where, string anchorVar, string anchorValue, string argsJson)
    {
        if (string.IsNullOrEmpty(actionName) || string.IsNullOrEmpty(systemAction)) return Json(new { ok = false, error = "action and systemAction required" });
        return LiveEdit(module, actionName, "add client action call", (act, es) =>
        {
            var ref1 = GetSysRef(es);
            if (ref1 == null) throw new Exception("(System) reference not found");
            object rca = null;
            var coll = GetProp(ref1, "ReferenceClientActions") as IEnumerable;
            if (coll != null)
                foreach (var it in coll)
                    try { if ((GetProp(it, "Name") as string) == systemAction) { rca = it; break; } } catch { }
            if (rca == null) throw new Exception("system client action not consumed/found on (System): " + systemAction + " (run consume_system_client_action first)");
            var newNode = CreateNodeGeneric(act, "OutSystems.Model.Logic.Nodes.IExecuteClientActionNode");
            SetProp(newNode, "Action", rca);
            var sb = new StringBuilder();
            sb.AppendLine("created " + ShortName(newNode) + " -> " + systemAction);
            // Flow wiring (same semantics as AddActionCall)
            if (where == "afterNode")
            {
                int afterNodeIndex = -1;
                if (anchorVar != null && int.TryParse(anchorVar, out var ani)) afterNodeIndex = ani;
                var nodes = NodeList(act);
                if (afterNodeIndex < 0 || afterNodeIndex >= nodes.Count) throw new Exception("afterNode(anchorVar) must be a node index for afterNode");
                var anchor = nodes[afterNodeIndex];
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted after node[" + afterNodeIndex + "]");
            }
            else if (where == "afterAnchor")
            {
                var anchor = FindAssignNode(act, anchorVar ?? "", anchorValue ?? "");
                if (anchor == null) throw new Exception("anchor not found: " + anchorVar + "=" + anchorValue);
                var anchorTarget = GetProp(anchor, "Target");
                SetProp(newNode, "Target", anchorTarget);
                SetProp(anchor, "Target", newNode);
                sb.AppendLine("inserted after anchor");
            }
            else // beforeEnd
            {
                var nodes = NodeList(act);
                var endNode = NodesOfType(act, "IEndNode").FirstOrDefault();
                object prev = null;
                if (endNode != null)
                    foreach (var n in nodes)
                    {
                        var tgt = GetProp(n, "Target");
                        if (tgt != null && object.ReferenceEquals(tgt, endNode)) { prev = n; break; }
                    }
                if (endNode == null || prev == null) throw new Exception("End/prev not found in flow '" + actionName + "'");
                SetProp(newNode, "Target", endNode);
                SetProp(prev, "Target", newNode);
                sb.AppendLine("inserted before End");
            }
            // Argument mapping
            int mapped = 0;
            if (!string.IsNullOrEmpty(argsJson) && argsJson != "null")
            {
                using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
                var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in doc.RootElement.EnumerateArray())
                    wanted[el.GetProperty("name").GetString() ?? ""] = el.GetProperty("value").GetString() ?? "";
                var args = GetProp(newNode, "Arguments") as IEnumerable;
                if (args != null)
                    foreach (var a in args)
                    {
                        var param = GetProp(a, "Parameter");
                        var pname = param != null ? (GetProp(param, "Name") as string ?? "") : "";
                        if (wanted.TryGetValue(pname, out var val))
                        {
                            CallMethod(a, "SetValue", new object[] { val }, 1);
                            mapped++;
                        }
                    }
                sb.AppendLine("mapped " + mapped + "/" + wanted.Count + " args");
            }
            sb.AppendLine(DumpFlowGraph(act));
            return sb.ToString();
        });
    }

        static Type FindTypeByShortName(string shortName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            if (types == null) continue;
            foreach (var ty in types)
            {
                if (ty == null) continue;
                if (ty.Name == shortName) return ty;
            }
        }
        return null;
    }

    static string GetStr(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) ? v.GetString() : null;

    static string Json(object o) => JsonSerializer.Serialize(o);

    internal static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "OsLiveBridge.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); } catch { }
    }
}
