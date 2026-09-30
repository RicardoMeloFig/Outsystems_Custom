// OutSystemsMcpLiveEditor - a self-contained MCP (Model Context Protocol) stdio
// server exposing LIVE in-process OutSystems editing tools. Unlike the headless
// outsystems-omleditor (which edits .oml FILES with no SS running), this server
// talks to the OsLiveBridge plugin LOADED INSIDE a running Service Studio, over a
// named pipe (OsLiveBridge-<SSpid>). Mutations land in the open module's live
// tree immediately (no Save/edit/reload) via the SS command system
// (ServiceStudio.Commands.Command.Execute -> UndoManager opens a command).
//
// Requires: SS 11 running with the OsLiveBridge plugin installed
// (Plugins\ServiceStudio\ServiceStudio.Plugin.OsLiveBridge.dll) and the target
// module OPEN in SS. The plugin starts a named-pipe server at SS startup.
//
// Bridge protocol: newline-delimited JSON, one request per line, one response per
// line. This MCP server is a thin stdio(JSON-RPC) -> pipe(newline-JSON) forwarder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OutSystemsMcpLiveEditor;

internal static class Program
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
                Respond(id, new JsonObject { ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "outsystems-liveeditor", ["version"] = "1.0" } });
            else if (method == "notifications/initialized") { /* no response */ }
            else if (method == "tools/list") Respond(id, new JsonObject { ["tools"] = ToolsList() });
            else if (method == "tools/call")
            {
                var name = msg["params"]?["name"]?.ToString();
                var arguments = msg["params"]?["arguments"] as JsonObject;
                string result;
                try { result = Dispatch(name, arguments); }
                catch (Exception ex) { var r = ex; while (r.InnerException != null) r = r.InnerException; result = "ERROR: " + r.GetType().Name + ": " + r.Message; }
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

    static JsonObject Str(string name, string desc) => new() { ["type"] = "string", ["description"] = desc };

    static JsonArray ToolsList() => new()
    {
        new JsonObject {
            ["name"] = "live_status",
            ["description"] = "Check the live-editing bridge: discover Service Studio processes with the OsLiveBridge plugin loaded (named pipes OsLiveBridge-<pid>), ping each, and report which modules are open in each. Use first to confirm SS is running with the plugin and the target module is open. No args.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        },
        new JsonObject {
            ["name"] = "live_list_modules",
            ["description"] = "List modules currently OPEN in Service Studio (live, in-memory) across all bridge pipes. Unlike the headless editor, this reads the live open modules, not .oml files.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        },
        new JsonObject {
            ["name"] = "live_module_info",
            ["description"] = "Read live properties of an OPEN module: Name, Kind, and counts (entities, serviceActions, serverActions, clientActions, anonymousStructures) + the concrete ESpace type. Read-only, no mutation.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name (e.g. MyModule)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_create_service_action",
            ["description"] = "Create a Service Action in a module that is OPEN in Service Studio - the mutation lands in the LIVE tree immediately (no Save/edit/reload). Opens a real SS command (Command.ExecuteFromAsyncCode -> UndoManager) so the new action is a proper undo unit (Ctrl+Z in SS removes it). Returns the created element type + service-action count before/after. By default creates only a Comment placeholder; pass withSkeleton=true to auto-create a linked Start→End flow (required before using where='beforeEnd' on live_add_action_call_node). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["name"] = Str("name", "New service action name"),
                    ["withSkeleton"] = new JsonObject { ["type"] = "boolean", ["description"] = "If true, auto-create a linked Start→End flow skeleton so beforeEnd works immediately (default: false)" } },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_clone_service_action",
            ["description"] = "Deep-clone an existing Service Action in an OPEN module into a new one (exact copy: parameters, flow, metadata) via IModelServices.Duplicate inside a real SS command. The clone gets a fresh key and is renamed to the new name; it lands in the LIVE tree immediately and is an undo unit (Ctrl+Z). Use to replicate an action exactly, no UI. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["sourceName"] = Str("sourceName", "Existing service action to clone (e.g. SourceAction)"),
                    ["newName"] = Str("newName", "Name for the cloned service action (e.g. ClonedAction)") },
                ["required"] = new JsonArray { "module", "sourceName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_clone_element",
            ["description"] = "Deep-clone a model element in an OPEN module into a new one (exact copy: attributes/params/flow/metadata) via IModelServices.Duplicate inside a real SS command. Same-module only. kind = entity | structure | serviceaction | serveraction | clientaction. The clone gets a fresh key and is renamed; it lands in the LIVE tree immediately as an undo unit (Ctrl+Z). Use to derive a second entity/structure or replicate an action pattern instead of replaying node-by-node construction. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["kind"] = Str("kind", "Element kind: entity, structure, serviceaction, serveraction, clientaction"),
                    ["name"] = Str("name", "Existing element to clone"),
                    ["newName"] = Str("newName", "Name for the cloned element") },
                ["required"] = new JsonArray { "module", "kind", "name", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_clone_server_action",
            ["description"] = "Deep-clone an existing Server Action in an OPEN module into a new one (exact copy: parameters, flow, metadata) via IModelServices.Duplicate inside a real SS command. The clone gets a fresh key and is renamed; it lands in the LIVE tree immediately and is an undo unit (Ctrl+Z). Same mechanism as live_clone_service_action, generalized to server actions. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["sourceName"] = Str("sourceName", "Existing server action to clone"),
                    ["newName"] = Str("newName", "Name for the cloned server action") },
                ["required"] = new JsonArray { "module", "sourceName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_clone_client_action",
            ["description"] = "Deep-clone an existing Client Action in an OPEN module into a new one (exact copy: parameters, flow, metadata) via IModelServices.Duplicate inside a real SS command. The clone gets a fresh key and is renamed; it lands in the LIVE tree immediately and is an undo unit (Ctrl+Z). Same mechanism as live_clone_service_action, generalized to module-level client actions. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["sourceName"] = Str("sourceName", "Existing client action to clone"),
                    ["newName"] = Str("newName", "Name for the cloned client action") },
                ["required"] = new JsonArray { "module", "sourceName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_create_server_action",
            ["description"] = "Create a Server Action in a module that is OPEN in Service Studio - the mutation lands in the LIVE tree immediately (no Save/edit/reload). Opens a real SS command (Command.ExecuteFromAsyncCode) so the new action is a proper undo unit (Ctrl+Z in SS removes it). Returns the created element type + server-action count before/after. Server actions have flows that can be edited with all live_* flow tools. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["name"] = Str("name", "New server action name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_client_action",
            ["description"] = "Create a Client Action in a module that is OPEN in Service Studio - the mutation lands in the LIVE tree immediately (no Save/edit/reload). Opens a real SS command (Command.ExecuteFromAsyncCode) so the new action is a proper undo unit (Ctrl+Z in SS removes it). Returns the created element type + client-action count before/after. Client actions have flows that can be edited with all live_* flow tools. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["name"] = Str("name", "New client action name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_screen_client_action",
            ["description"] = "Create a SCREEN-level Client Action (ClientScreenActionFlow) on a screen in an OPEN module - LIVE tree, undo unit. This is the client action a Reactive screen Button's OnClick can be wired to (its Destination is typed IClientSideDestination, which only screen-level client actions implement - a global ClientActionFlow is NOT assignable to a button). Creates via the screen's CreateClientAction(name,key) (falls back to the ClientScreenActionFlow(IParent,name) ctor). Add the button's OnClick with live_set_button_onclick afterwards.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["screen"] = Str("screen", "Screen name the client action belongs to"),
                    ["name"] = Str("name", "New screen client action name") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_local_variable",
            ["description"] = "Add a LOCAL VARIABLE to a service action, server action, or client action in an OPEN module (live). Supports basic types (LongInteger, Integer, Text, Decimal, Boolean, DateTime, Date, Time, PhoneNumber, Email, BinaryData, Currency, TextIdentifier, IntegerIdentifier, LongIntegerIdentifier) and Structure types (unique name match in es.Structures). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name (service action, server action, or client action)"),
                    ["name"] = Str("name", "Local variable name"),
                    ["type"] = Str("type", "Type name (e.g. LongInteger or a Structure name)") },
                ["required"] = new JsonArray { "module", "action", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_list_flow",
            ["description"] = "Dump the flow graph of a service action in an OPEN module: each node's index, type (IStartNode/IAssignNode/IEndNode...), assignments (var = value), and Target (-> [index]). Read-only. Use to see the topology (normal vs exception paths) before editing.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_assign_value",
            ["description"] = "Change an Assign node's assignment value in an OPEN module's service action (live). Finds the assignment whose Value.Text contains matchValue (and optionally Variable.Text contains matchVar) and sets its value to newValue. The value is passed as-is to SetValue (same as live_add_assignment_to_node): for text literals, include the double quotes as part of newValue. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["matchValue"] = Str("matchValue", "Substring of the current value to find (e.g. 'old value' or '1')"),
                    ["newValue"] = Str("newValue", "New value (e.g. 'new value' or '2')"),
                    ["matchVar"] = Str("matchVar", "Optional: substring of the variable to disambiguate") },
                ["required"] = new JsonArray { "module", "action", "matchValue", "newValue" } }
        },
        new JsonObject {
            ["name"] = "live_add_output_param",
            ["description"] = "Add an output parameter to a service action in an OPEN module (live). type is a basic type name: LongInteger, Integer, Text, Decimal, Boolean, DateTime, Date, Time, PhoneNumber, Email, BinaryData, Currency, TextIdentifier, IntegerIdentifier, LongIntegerIdentifier. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["name"] = Str("name", "Output parameter name (e.g. Id)"), ["type"] = Str("type", "Basic type (e.g. LongInteger)") },
                ["required"] = new JsonArray { "module", "action", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_add_assign_node",
            ["description"] = "Add an Assign node (var = value) to a service action's flow in an OPEN module (live). where: 'afterAnchor' inserts after the Assign node whose assignment matches anchorVar/anchorValue; 'beforeEnd' inserts before the first End node (auto-links Start→End if needed); 'afterNode' inserts after any node by index (pass afterNodeIndex). Note: creates a NEW node with ONE assignment. For multi-assignment nodes (2+ assignments on one node), use live_add_assignment_to_node after creating the node. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["var"] = Str("var", "Variable to assign (e.g. Id)"), ["value"] = Str("value", "Value expression (e.g. 1)"),
                    ["where"] = Str("where", "afterAnchor | beforeEnd | afterNode"),
                    ["anchorVar"] = Str("anchorVar", "For afterAnchor: variable substring of the anchor assignment"),
                    ["anchorValue"] = Str("anchorValue", "For afterAnchor: value substring of the anchor assignment"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "For afterNode: index of the node to insert after" } },
                ["required"] = new JsonArray { "module", "action", "var", "value", "where" } }
        },
        new JsonObject {
            ["name"] = "live_delete_node",
            ["description"] = "Delete an Assign node (matched by an assignment's variable+value substrings) from a service action's flow in an OPEN module (live). Relinks the previous node to the deleted node's target. Undo unit (Ctrl+Z). Use to remove a misplaced node, then re-add it correctly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["matchVar"] = Str("matchVar", "Variable substring of the node's assignment (e.g. Id)"),
                    ["matchValue"] = Str("matchValue", "Value substring of the node's assignment (e.g. 1)") },
                ["required"] = new JsonArray { "module", "action", "matchVar", "matchValue" } }
        },
        new JsonObject {
            ["name"] = "live_list_consumable_elements",
            ["description"] = "List ALL public consumable elements in an OPEN module (the producer), grouped by type. Covers all 15 consumable types: ServiceActions, ServerActions, ClientActions, Entities, Structures, Roles, Processes, Scripts, Images, Resources, WebThemes, MobileThemes, WebFlows, MobileFlows, Folders. For types with a Public property: only public elements are listed. For containers (WebFlow, MobileFlow, Folder): all are listed. Read-only. Use before live_consume_elements to see what's available.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Producer module name (must be OPEN in SS)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_consume_elements",
            ["description"] = "Consume (add references to) elements from a producer module into a consumer module - both must be OPEN in Service Studio. All consumptions land in the live tree immediately as a single undo unit (Ctrl+Z). Uses IESpace.AddDependency inside Command.ExecuteFromAsyncCode. The 'what' parameter: '*' = all consumable elements; 'ServerAction:*,Entity:*' = all of specific types; 'ServerAction:GetUser,Entity:User' = specific elements by name. Covers all 15 consumable element types.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["consumer"] = Str("consumer", "Consumer module name (must be OPEN in SS)"),
                    ["producer"] = Str("producer", "Producer module name (must be OPEN in SS)"),
                    ["what"] = Str("what", "'*' for all, or 'Type:Name,Type:Name' (e.g. 'ServerAction:*,Entity:User')") },
                ["required"] = new JsonArray { "consumer", "producer", "what" } }
        },
        new JsonObject {
            ["name"] = "live_remove_dependency",
            ["description"] = "Remove a module dependency (the reference to a producer module) from an OPEN module - live, undo unit (Ctrl+Z). Finds the Reference in the module's References collection by producer module name and deletes it inside a real SS command. Use after consuming the wrong module or when a producer is no longer needed. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name (the consumer)"),
                    ["producer"] = Str("producer", "Producer module name whose reference should be removed") },
                ["required"] = new JsonArray { "module", "producer" } }
        },
        new JsonObject {
            ["name"] = "live_debug_publish_state",
            ["description"] = "Read-only monitor for an in-flight in-process publish: reports whether a ServerProcess is registered for the module's aggregator (ProcessesPending / ProcessesStarted), its type and CurrentState/InnerState. Does NOT start anything. Use AFTER live_publish_module: 'none' on both = the publish process finished (check Service Center version + live_get_verify_errors to confirm success).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_save_module",
            ["description"] = "Save the OPEN module in-process (Ctrl+S equivalent): invokes the registered Save command (ESpaceCommands/Save) via its AutoRegistry singleton. Persists live edits to the server/local .oml without UI automation. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_open_producer_module",
            ["description"] = "Open a CONSUMED producer module in a new Service Studio tab, in-process (the 'Open Producer' flow): resolves the Reference's ReferenceKey and calls the UI's own open engine. The bridge can only mutate open modules - use this to bring a producer online before live_consume_elements / cross-module clones. Arbitrary (non-consumed) modules still need the manual open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open consumer module"),
                    ["reference"] = Str("reference", "Reference (producer module) name to open") },
                ["required"] = new JsonArray { "module", "reference" } }
        },
        new JsonObject {
            ["name"] = "live_clone_service_action_from",
            ["description"] = "Create a REAL local Service Action in the consumer by deep-cloning one from an OPEN producer module (consume + IModelServices.Duplicate - the SS copy/paste mechanism). NOTE (proven 2026-09-29, SS 11.55.89): the cross-module paste pipeline is PLATFORM-UNREACHABLE headlessly - every gate passes (licensing, mocks, serialization) but SS's FirstPassDeserializer parents nothing, so this route NREs. WORKING ALTERNATIVES: same-module live_clone_service_action / live_clone_element (proven), or live_create_service_action (proven), or UI copy-paste. Kept for diagnostics (extra: diag array reports gate states).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["consumer"] = Str("consumer", "Consumer module (gets the new service action)"),
                    ["producer"] = Str("producer", "Producer module (OPEN, holds the template action)"),
                    ["source"] = Str("source", "Template service action name in the producer"),
                    ["name"] = Str("name", "Name for the new local service action") },
                ["required"] = new JsonArray { "consumer", "producer", "source", "name" } }
        },
        new JsonObject {
            ["name"] = "live_delete_block_client_action",
            ["description"] = "Delete a client action that lives on a WEB BLOCK in an OPEN module (module-level live_delete_action does not see block-owned actions). Real SS command, undo unit (Ctrl+Z). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. UserInfo)"),
                    ["name"] = Str("name", "Client action name to delete") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_remove_event_from_block",
            ["description"] = "Delete a custom event (WebBlockCustomEvent) from a web block in an OPEN module - inverse of live_add_event_to_block. Real SS command, undo unit (Ctrl+Z). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Event name to remove") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_sql_node",
            ["description"] = "Create an Advanced SQL node (ISQLNode/AdvancedQuery) in an action flow in an OPEN module, with the SQL statement set. Optional afterNodeIndex inserts it into the flow (wire with live_set_node_target). Input params/outputs via node's QueryParameters/OutputStructure (probe with live_debug_node_props); note: the UI's derive-outputs-from-SELECT step does not run headless, so a SELECT without an explicit OutputStructure may show an 'Invalid SQL' verify error until outputs are defined. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name to add the node to"),
                    ["sql"] = Str("sql", "SQL statement (OS SQL syntax, e.g. SELECT {Entity}.[ID] FROM {Entity})"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional node index to insert after" } },
                ["required"] = new JsonArray { "module", "action", "sql" } }
        },
        new JsonObject {
            ["name"] = "live_create_user_exception",
            ["description"] = "Create a User Exception (Data > Exceptions) in an OPEN module via the public UserException(ESpace, name) ctor inside a real SS command. Undo unit (Ctrl+Z). Read-back via live_debug_eSpace_collection_items(collection=UserExceptions). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "User exception name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_rest_client",
            ["description"] = "Create a CONSUMED REST API (RestClient from the REST plugin) in an OPEN module + optionally one method (RestAction) with URL path, HTTP method (GET/POST/PUT/DELETE/PATCH) and ResponseFormat=JSON. PROVEN. Follow-ups (not yet in MVP): Base URL lives in lazily-created ExtendedProperties; structure-from-SampleResponse parsing; callbacks. Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "REST API name (e.g. TicketmasterApi)"),
                    ["actionName"] = Str("actionName", "Optional method name (e.g. GetTodos)"),
                    ["urlPath"] = Str("urlPath", "Optional URL path for the method (e.g. /todos/1)"),
                    ["httpMethod"] = Str("httpMethod", "Optional HTTP method: GET, POST, PUT, DELETE, PATCH") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_debug_object_prop_surface",
            ["description"] = "Read-only dump of a model object's settable surface for live_set_object_prop_deep: public properties (name/type/value/settable), private backing fields (_prop), static setter delegates (_propSetter) and property-grid descriptors (PropPropertyDescriptor). kind: entity|attribute|structure|structureattribute|timer|siteproperty|role (entity = parent entity name for attribute kinds).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["kind"] = Str("kind", "entity|attribute|structure|structureattribute|timer|siteproperty|role"),
                    ["entity"] = Str("entity", "Parent entity/structure name (attribute kinds only)"),
                    ["name"] = Str("name", "Object name") },
                ["required"] = new JsonArray { "module", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_object_prop_deep",
            ["description"] = "Set a property that has NO public setter, using the UI's own surfaces in order: public property -> static _propSetter delegate -> property-grid descriptor (e.g. Attribute DefaultValue) -> raw backing field (+revalidate). Covers entity-attr DefaultValue, Timer Schedule/Timeout/Priority, SiteProperty default, Role on Permission, etc. Use live_debug_object_prop_surface first to see what exists. Undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["kind"] = Str("kind", "entity|attribute|structure|structureattribute|timer|siteproperty|role"),
                    ["entity"] = Str("entity", "Parent entity/structure name (attribute kinds only)"),
                    ["name"] = Str("name", "Object name"),
                    ["propName"] = Str("propName", "Property to set (e.g. DefaultValue, Schedule, Timeout, Priority)"),
                    ["value"] = Str("value", "Value as text (parsed to int/double/bool when needed)") },
                ["required"] = new JsonArray { "module", "kind", "name", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_upload_image",
            ["description"] = "Upload a module Image from a base64 payload via IESpace.CreateImage - no file dialog, no SS UI. Undo unit. Keep payloads modest (< ~1 MB base64) for the pipe.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "Image name in the Images tree"),
                    ["base64Data"] = Str("base64Data", "File bytes as base64"),
                    ["description"] = Str("description", "Optional description") },
                ["required"] = new JsonArray { "module", "name", "base64Data" } }
        },
        new JsonObject {
            ["name"] = "live_upload_resource",
            ["description"] = "Upload a module Resource from a base64 payload via IESpace.CreateResource - no file dialog, no SS UI. Undo unit. Keep payloads modest (< ~1 MB base64) for the pipe.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "Resource name in the Resources tree"),
                    ["base64Data"] = Str("base64Data", "File bytes as base64") },
                ["required"] = new JsonArray { "module", "name", "base64Data" } }
        },
        new JsonObject {
            ["name"] = "live_delete_structure",
            ["description"] = "Delete a Structure by name from the OPEN module (Delete inside a real SS command, undo unit). The Structure twin of live_delete_entity.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "Structure name to delete") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_remove_unused_dependencies",
            ["description"] = "Remove all UNUSED module references via IESpace.RemoveUnusedDependencies - the 'Remove Unused References' menu action, in-process. Undo unit. Reports References count before/after.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_publish_module",
            ["description"] = "1-Click Publish the OPEN module IN-PROCESS (no UI automation, no vision, no Ollama): invokes ServiceStudio.Presenter.Commands.Publish - the same command the F5 button runs - on the UI thread and waits for the CommandResult. Optional commitMessage uses the internal publish-with-message path (Shift+F5 equivalent, no dialog). Long-running (minutes). Requires SS running with OsLiveBridge and the module open. Confirm before use - this publishes to the connected environment.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name to publish"),
                    ["commitMessage"] = Str("commitMessage", "Optional publish message (attached to the module version)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_debug_publish_surface",
            ["description"] = "Read-only dump of the in-process publish API surface: whether ServiceStudio.Presenter.Commands.Publish loads, its ctor access, the Execute/message method candidates, the aggregator's IsUnattended flag (True = no confirm dialogs), and ExecuteInContext(Action,bool) availability. Use before live_publish_module on a new SS build to confirm the surface is unchanged.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_add_input_param",
            ["description"] = "Add an INPUT parameter to a service action in an OPEN module (live). Supports basic types (LongInteger, Integer, Text, Decimal, Boolean, DateTime, Date, Time, PhoneNumber, Email, BinaryData, Currency, TextIdentifier, IntegerIdentifier, LongIntegerIdentifier) and Structure types (unique name match in es.Structures). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["name"] = Str("name", "Input parameter name (e.g. UserId)"), ["type"] = Str("type", "Type name (e.g. LongInteger or a Structure name)") },
                ["required"] = new JsonArray { "module", "action", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_add_end_node",
            ["description"] = "Add an End node to a service action's flow in an OPEN module (live). where: 'beforeEnd' inserts before the first End node (existing flow end becomes the new node's Target); 'atEnd' appends after the last node; 'exceptionPath' creates a standalone End node for an exception handler branch (wire manually with live_set_exception_handler). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["where"] = Str("where", "beforeEnd | atEnd | exceptionPath") },
                ["required"] = new JsonArray { "module", "action", "where" } }
        },
        new JsonObject {
            ["name"] = "live_set_exception_handler",
            ["description"] = "Wire an exception handler node to another node's ExceptionHandler property. Both nodes are found by assignment var+value substrings. NOTE: Start.ExceptionHandler does NOT exist as a settable property. To configure exception handling, use live_debug_create_node('IExceptionHandlerNode') + live_set_error_handler_exception + live_set_node_target instead. This tool is kept for advanced use cases. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["endVar"] = Str("endVar", "End node assignment variable substring"),
                    ["endValue"] = Str("endValue", "End node assignment value substring"),
                    ["handlerVar"] = Str("handlerVar", "Handler node assignment variable substring"),
                    ["handlerValue"] = Str("handlerValue", "Handler node assignment value substring") },
                ["required"] = new JsonArray { "module", "action", "endVar", "endValue", "handlerVar", "handlerValue" } }
        },
        new JsonObject {
            ["name"] = "live_set_start_exception_handler",
            ["description"] = "Attempt to wire the exception handler branch to the Start node's ExceptionHandler property. NOTE: Start.ExceptionHandler does NOT exist as a settable property on SS 11.55.81+ — this tool will fail. To configure exception handling, use live_debug_create_node('IExceptionHandlerNode') + live_set_error_handler_exception + live_set_node_target instead. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["handlerVar"] = Str("handlerVar", "Handler node assignment variable substring"),
                    ["handlerValue"] = Str("handlerValue", "Handler node assignment value substring") },
                ["required"] = new JsonArray { "module", "action", "handlerVar", "handlerValue" } }
        },
        new JsonObject {
            ["name"] = "live_probe_node_types",
            ["description"] = "Enumerate all node interface types currently present in a service action's flow graph (read-only). Use to discover the exact interface name for call-server-action, try, catch, and other node types at runtime before using live_add_action_call_node. Returns a list of I*Node interface names.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_add_action_call_node",
            ["description"] = "Insert a server-action-call node into a service action's flow in an OPEN module (live). The server action must already be consumed by the module (via live_consume_elements). Use where='afterAnchor' to insert after an existing Assign node (matched by var+value), where='beforeEnd' to insert before the first End node (REQUIRES Start→End linked via live_set_node_target first; auto-links if Start has no Target), or where='afterNode' to insert after any node by index (pass afterNodeIndex). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["where"] = Str("where", "afterAnchor | beforeEnd | afterNode"),
                    ["anchorVar"] = Str("anchorVar", "For afterAnchor: variable substring of the anchor assignment"),
                    ["anchorValue"] = Str("anchorValue", "For afterAnchor: value substring of the anchor assignment"),
                    ["serverActionName"] = Str("serverActionName", "Name of the consumed server action to call"),
                    ["producerModule"] = Str("producerModule", "Optional: producer module name to disambiguate when multiple consumed modules have the same server action name"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "For afterNode: index of the node to insert after" } },
                ["required"] = new JsonArray { "module", "action", "where", "serverActionName" } }
        },
        new JsonObject {
            ["name"] = "live_add_refresh_node",
            ["description"] = "Create a RefreshQuery node (IRefreshDataNode) in a block lifecycle or client-action flow and target it at a screen aggregate, then wire it before End. Use in OnParametersChange/OnParametersChanged to refresh an aggregate (e.g. GetUserById) when an input like UserId changes. action resolves via FindAction (unwraps the NREvents OnParametersChanged wrapper's .Destination flow). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Flow name (e.g. OnParametersChanged)"),
                    ["block"] = Str("block", "Web block owning the aggregate (OR screen - pass one)"),
                    ["screen"] = Str("screen", "Screen owning the aggregate (OR block - pass one)"),
                    ["aggregateName"] = Str("aggregateName", "Aggregate to refresh (e.g. GetUserById)"),
                    ["where"] = Str("where", "beforeEnd | afterNode") },
                ["required"] = new JsonArray { "module", "action", "aggregateName" } }
        },
        new JsonObject {
            ["name"] = "live_set_action_call",
            ["description"] = "Change the server action called by an EXISTING IExecuteServerActionNode. Resolves the server action name to the consumed action object (same resolution as live_add_action_call_node) and sets it on the node's Action property. Use after cloning a service action to repoint the ExecuteAction to a different server action (e.g. clone DietCreate_BL then set action to DietDelete). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the IExecuteServerActionNode to change" },
                    ["serverActionName"] = Str("serverActionName", "Name of the consumed server action to call"),
                    ["producerModule"] = Str("producerModule", "Optional: producer module name to disambiguate") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "serverActionName" } }
        },
        new JsonObject {
            ["name"] = "live_remove_input_param",
            ["description"] = "Remove an input parameter by name from a service/server/client action. Finds the parameter in InputParameters by name and deletes it. Useful when cloning an action and replacing a parameter with a different type (e.g. removing DietId before adding ProgressId). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Action name"),
                    ["paramName"] = Str("paramName", "Name of the input parameter to remove") },
                ["required"] = new JsonArray { "module", "action", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_remove_output_param",
            ["description"] = "Remove an output parameter by name from a service/server/client action. Same pattern as live_remove_input_param but for OutputParameters. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Action name"),
                    ["paramName"] = Str("paramName", "Name of the output parameter to remove") },
                ["required"] = new JsonArray { "module", "action", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_input",
            ["description"] = "Add an INPUT parameter whose type is an entity from a consumed reference (producer module). Searches the consumer module's References for the producer, then finds the entity in the producer's Entities collection. Use for entity-reference input parameters (e.g. Client entity). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (consumer)"), ["action"] = Str("action", "Service action name"),
                    ["name"] = Str("name", "Input parameter name (e.g. Client)"),
                    ["entityName"] = Str("entityName", "Entity name in the producer module (e.g. Client)"),
                    ["producerModule"] = Str("producerModule", "Name of the producer module that exposes the entity (e.g. Diet_CS)") },
                ["required"] = new JsonArray { "module", "action", "name", "entityName", "producerModule" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_identifier_input",
            ["description"] = "Add an INPUT parameter whose type is an entity's Identifier type (e.g. 'Diet Identifier') from a consumed reference (producer module). This is the correct type for Delete actions that take an Id parameter — NOT LongInteger. The identifier type is obtained from the entity's IdentifierType property. Includes idempotency check (rejects duplicate param names). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (consumer)"), ["action"] = Str("action", "Service action name"),
                    ["name"] = Str("name", "Input parameter name (e.g. DietId)"),
                    ["entityName"] = Str("entityName", "Entity name in the producer module (e.g. Diet)"),
                    ["producerModule"] = Str("producerModule", "Name of the producer module that exposes the entity (e.g. Diet_CS)") },
                ["required"] = new JsonArray { "module", "action", "name", "entityName", "producerModule" } }
        },
        new JsonObject {
            ["name"] = "live_set_output_param_type",
            ["description"] = "Set the DataType of an existing output parameter by name. Use to fix a parameter type that was added with a placeholder type (e.g. '?'). Supports Structure types (by unique name in the module's Structures) and entity types (from a consumed producer module). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["paramName"] = Str("paramName", "Name of the existing output parameter to fix"),
                    ["typeName"] = Str("typeName", "Structure or entity type name (e.g. Result or Client)"),
                    ["producerModule"] = Str("producerModule", "Optional: producer module name for entity types") },
                ["required"] = new JsonArray { "module", "action", "paramName", "typeName" } }
        },
        new JsonObject {
            ["name"] = "live_set_input_param_type",
            ["description"] = "Set the DataType of an existing INPUT parameter by name. Use to fix a parameter type that was added with a placeholder or incorrect type. Supports basic types (LongInteger, Text, etc.), Structure types (by name), entity types (whole record, from consumed producer), and entity Identifier types (e.g. 'Diet Identifier'). When entityName is provided with producerModule: resolves the entity's IdentifierType (correct for Delete action Id parameters). When typeName is provided: resolves basic type, Structure, or entity record. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["paramName"] = Str("paramName", "Name of the existing input parameter to fix"),
                    ["typeName"] = Str("typeName", "Basic type, Structure name, or entity name (e.g. LongInteger, Result, Diet). Leave empty if using entityName for Identifier type."),
                    ["producerModule"] = Str("producerModule", "Optional: producer module name for entity or entity Identifier types"),
                    ["entityName"] = Str("entityName", "Optional: entity name to resolve its Identifier type (e.g. Diet). When provided, sets the entity's IdentifierType (NOT the whole entity record).") },
                ["required"] = new JsonArray { "module", "action", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_output",
            ["description"] = "Add an OUTPUT parameter whose type is an entity from a consumed reference (producer module). Same pattern as live_add_entity_input but for output parameters. Use for entity-reference output parameters. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (consumer)"), ["action"] = Str("action", "Service action name"),
                    ["name"] = Str("name", "Output parameter name"),
                    ["entityName"] = Str("entityName", "Entity name in the producer module"),
                    ["producerModule"] = Str("producerModule", "Name of the producer module that exposes the entity") },
                ["required"] = new JsonArray { "module", "action", "name", "entityName", "producerModule" } }
        },
        new JsonObject {
            ["name"] = "live_delete_node_by_index",
            ["description"] = "Delete a node by its index in the flow's NodeList. Works for ALL node types (Assign, ExecuteAction, End, ErrorHandler, Start, Comment) - unlike live_delete_node which only works on Assign nodes. Relinks the previous node (found via ReferenceEquals) to the deleted node's target. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node to delete in the NodeList" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_set_node_target",
            ["description"] = "Set the Target property of a node at nodeIndex to point to the node at targetIndex. This is the CORRECT way to wire flow nodes - deterministic, no reference identity bugs. Use after live_list_flow confirms actual indices. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node whose Target to set" },
                    ["targetIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node to point to" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "targetIndex" } }
        },
        new JsonObject {
            ["name"] = "live_set_node_property",
            ["description"] = "Set any settable string property on a node by name. NOTE: Cannot set the Action property on ExecuteAction (it requires an IExecuteActionTarget object, not a string) — use live_set_action_call to change an ExecuteAction's server action. For typed properties (like Exception on ErrorHandler), use live_set_error_handler_exception instead. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node" },
                    ["propName"] = Str("propName", "Property name to set (e.g. 'Target')"),
                    ["propValue"] = Str("propValue", "Value to set") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "propName", "propValue" } }
        },
        new JsonObject {
            ["name"] = "live_debug_create_node",
            ["description"] = "Create a flow node by its interface short name. Valid interfaces: IStartNode, IEndNode, IAssignNode, IExecuteServerActionNode, IExceptionHandlerNode. Use live_probe_node_types first to see what's available at runtime. Returns the created node type. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeInterface"] = Str("nodeInterface", "Interface short name (e.g. 'IStartNode', 'IExceptionHandlerNode')") },
                ["required"] = new JsonArray { "module", "action", "nodeInterface" } }
        },
        new JsonObject {
            ["name"] = "live_delete_service_action",
            ["description"] = "Delete a Service Action by name. Use to remove a broken action before replacing via live_clone_service_action. This is the fast path for replacing broken actions - delete then clone. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name to delete") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_add_assignment_to_node",
            ["description"] = "Add a second (or subsequent) assignment to an EXISTING IAssignNode by its index. Unlike live_add_assign_node (which creates a NEW node with one assignment), this operates on an existing node, enabling multi-assignment nodes (e.g. Result.IsError=False AND Result.Message=\"Success\" on a single Assign node). The value is passed as-is to SetValue. For text literals, include the double quotes as part of the value (e.g. \"Success\"). Do NOT wrap in single quotes. Booleans/identifiers are bare (e.g. False, AllExceptions.ExceptionMessage). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the existing IAssignNode to add the assignment to" },
                    ["var"] = Str("var", "Variable to assign (e.g. Result.Message)"), ["value"] = Str("value", "Value expression. For text literals, include double quotes in the value (e.g. \"Success\"). For booleans/identifiers, pass bare (e.g. False). Do NOT wrap in single quotes.") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "var", "value" } }
        },
        new JsonObject {
            ["name"] = "live_remove_assignment",
            ["description"] = "Remove a single assignment (by variable name) from an existing IAssignNode. Useful for cleaning up leftover dummy/placeholder assignments (e.g. Temp=False) without deleting the entire node. Matches the assignment whose Variable.Text equals (or contains) varName and removes it from the Assignments collection. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the IAssignNode to remove the assignment from" },
                    ["var"] = Str("var", "Variable name to remove (e.g. Temp, matched by substring on Variable.Text)") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "var" } }
        },
        new JsonObject {
            ["name"] = "live_set_error_handler_exception",
            ["description"] = "Set the Exception property on an IExceptionHandlerNode. Defaults to 'All Exceptions' (SystemException) - the generic catch-all. Optionally specify exceptionName to catch a specific exception (e.g. 'Database Exception', 'Security Exception', 'Communication Exception', 'User Exception'). Searches the ESpace's SystemExceptions collection (the Exceptions folder in the Logic tab) for direct exception instances. Also sets AbortTransaction=true and LogError=true. Use after live_debug_create_node('IExceptionHandlerNode') to configure the exception handler. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the IExceptionHandlerNode (ErrorHandler) to configure" },
                    ["exceptionName"] = Str("exceptionName", "Optional: exception name to catch (defaults to 'All Exceptions'). E.g. 'Database Exception', 'Security Exception'.") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_set_action_arg",
            ["description"] = "Set ONE argument value on an ExecuteAction node (server/entity/system client action) by parameter name, e.g. set the SimulateBattle_SA 'SAtk' arg to a local var. argName='*' sets all args to the same value. Use when map_action_inputs can't (arguments sourced from locals/vars, not the action's input params).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Flow action"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "integer", ["description"] = "ExecuteAction node index" },
                    ["argName"] = Str("argName", "Argument/parameter name (or *)"),
                    ["value"] = Str("value", "Value expression (e.g. SAtk or \"42\")") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "argName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_map_action_inputs",
            ["description"] = "Auto-map input arguments on an IExecuteServerActionNode by name. For each Argument on the ExecuteAction node, finds the matching InputParameter on the service action (by name) and sets the Argument's Value via SetValue. When parameter names don't match (e.g. server action uses 'Source' but service action uses 'Client'), falls back to type-aware matching (matches argument's parameter DataType to input param DataType), then single-input fallback. Pass inputParamName to explicitly specify which input parameter to map to (bypasses name matching and fallback — useful when multiple input params exist from cloning). Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the IExecuteServerActionNode to map inputs on" },
                    ["inputParamName"] = Str("inputParamName", "Optional: explicitly specify which input parameter to map all arguments to (bypasses name matching and fallback)") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_debug_node_props",
            ["description"] = "Inspect a node's type and settable properties by its index. Returns the node type name (e.g. 'Start', 'Assign', 'ErrorHandler') and a list of all settable property names. Read-only diagnostic — use to identify node types after creating them (since node indices are not creation order).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node to inspect (pass as number)" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_debug_action_args",
            ["description"] = "Inspect the Arguments collection of an IExecuteServerActionNode. Shows each argument's Parameter name, current Value, and the service action's input parameters. Read-only diagnostic — use before live_map_action_inputs to verify what needs mapping.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the IExecuteServerActionNode to inspect" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_debug_eSpace_collections",
            ["description"] = "List all IEnumerable collection names on an eSpace with item counts. Use to discover where elements like exceptions, entities, server actions, etc. are stored. Read-only diagnostic.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_debug_eSpace_collection_items",
            ["description"] = "Dump all items from a named IEnumerable collection on an eSpace. Shows each item's type name and key properties. Use after debug_eSpace_collections to find the right collection name. Read-only diagnostic.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["collection"] = Str("collection", "Collection property name on the eSpace (e.g. Exceptions, ServerActions, Entities)") },
                ["required"] = new JsonArray { "module", "collection" } }
        },
        new JsonObject {
            ["name"] = "live_layout_flow",
            ["description"] = "Auto-position all nodes in a service action's flow so they don't stack on top of each other. Analyzes the flow topology: the main flow (Start -> ... -> End) is placed in the left column (X=3200), exception flows (ErrorHandler -> ... -> End) in the right column (X=12800). Y increments downward (~2000 per node). Call this AFTER the flow is fully built and linked. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_get_node_positions",
            ["description"] = "Read the X/Y canvas position of every node in a service action's flow. Read-only diagnostic — use to verify a layout or inspect how nodes are positioned.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_node_position",
            ["description"] = "Set the X/Y canvas position of a single node by its index. Handles numeric type conversion automatically. Use for custom layouts or fine-tuning after live_layout_flow. Undo unit (Ctrl+Z).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"), ["action"] = Str("action", "Service action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the node to position" },
                    ["x"] = new JsonObject { ["type"] = "number", ["description"] = "X coordinate (e.g. 3200 for left column, 12800 for right column)" },
                    ["y"] = new JsonObject { ["type"] = "number", ["description"] = "Y coordinate (e.g. 914 for first row, increments ~2000 per row)" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "x", "y" } }
        },
        new JsonObject {
            ["name"] = "live_create_folder",
            ["description"] = "Create a folder in a module that is OPEN in Service Studio - the mutation lands in the LIVE tree immediately. Opens a real SS command (Command.ExecuteFromAsyncCode). The folder appears in the specified tree section (default: ServiceActions). Undo unit (Ctrl+Z in SS removes it). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (e.g. MyModule)"),
                    ["folderName"] = Str("folderName", "New folder name"),
                    ["parentFolder"] = Str("parentFolder", "Tree section enum value: ServiceActions (default), ServerActions, ClientActions, Structures, SiteProperties, Timers, Roles, Images, Resources, Scripts, Processes, EntityDiagrams, Themes, ServerEntities, ClientEntities, Views") },
                ["required"] = new JsonArray { "module", "folderName" } }
        },
        new JsonObject {
            ["name"] = "live_move_to_folder",
            ["description"] = "Move a service action, server action, or client action into an existing folder in an OPEN module. Sets the action's Folder property inside a real SS command. Works for any action type (FindAction searches ServiceActions, UserActions/ServerActions, ClientActions). Undo unit (Ctrl+Z). The folder must already exist (create it first with live_create_folder).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name to move (service/server/client action)"),
                    ["folderName"] = Str("folderName", "Target folder name (must already exist)") },
                ["required"] = new JsonArray { "module", "action", "folderName" } }
        },
        // ---- UI / screen / widget / CSS tools (live) ----
        new JsonObject {
            ["name"] = "live_create_web_flow",
            ["description"] = "Create a bare Web Flow (no screens, no cloned children) in an OPEN module - lands in the LIVE tree immediately via Command.ExecuteFromAsyncCode (undo unit, Ctrl+Z). Uses es.CreateWebFlow (from-scratch), NOT Duplicate, so it does not clone the source flow's web blocks. The concrete flow type (NRWebFlow / WebFlow) follows the module kind. Use as the parent for live_create_web_screen.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New web flow name (e.g. HomeFlow)") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_web_screen",
            ["description"] = "Create a Web Screen inside an existing Web Flow in an OPEN module - LIVE tree, undo unit. The concrete screen type follows the module kind (Reactive NRWebScreen / Traditional WebScreen). Returns the created type. Create the flow first with live_create_web_flow.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["webFlow"] = Str("webFlow", "Existing web flow name"),
                    ["name"] = Str("name", "New screen name (e.g. Home)") },
                ["required"] = new JsonArray { "module", "webFlow", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_web_block",
            ["description"] = "Create a Web Block from scratch (NO clone/move) inside an existing Web Flow in an OPEN module - LIVE tree, undo unit. Hunts a Create*/New* web-block factory on the eSpace or the flow (prefers CreateWebBlock(name,key)) and invokes it inside Command.ExecuteFromAsyncCode, then adds the node to the flow's Nodes if needed. Returns the created type/name/flow. Use to build a brand-new block (e.g. a game header) in a flow like zombieGame.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["flow"] = Str("flow", "Existing web flow name (e.g. zombieGame)"),
                    ["name"] = Str("name", "New web block name (e.g. ZombieHeader)") },
                ["required"] = new JsonArray { "module", "flow", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_placeholder_to_block",
            ["description"] = "Add a Placeholder widget into a web block's widget tree (or a named container inside it) in an OPEN module - LIVE tree, undo unit. Creates the placeholder via IPlaceholderWidget/CustomPlaceholderWidget factory so it can be filled when the block is used.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["parent"] = Str("parent", "Container name inside the block (optional; empty = block root)"),
                    ["name"] = Str("name", "New placeholder name") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_widget_to_block",
            ["description"] = "Add any widget kind (container/text/expression/link/html/placeholder) into a web block's widget tree (or a named container inside it) in an OPEN module - LIVE tree, undo unit. kind maps to the CreateWidgetByKind candidates; value only applies to text/expression. Use to put an Expression (value = text literal with quotes, e.g. \"Hello\") or any widget into a block like ZombieHeader.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["parent"] = Str("parent", "Container name inside the block (optional; empty = block root)"),
                    ["kind"] = Str("kind", "Widget kind: container|text|expression|link|html|placeholder"),
                    ["name"] = Str("name", "New widget name"),
                    ["value"] = Str("value", "Value text (for text/expression; text literals use quotes)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "block", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_delete_widget_from_block",
            ["description"] = "Delete a widget by name from a web block's widget tree (recursively removes children too) in an OPEN module - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["name"] = Str("name", "Widget name to delete") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_dump_block_widget_types",
            ["description"] = "Read-only diagnostic: dump every widget in a web block with its concrete type AND all interfaces, including anonymous widgets (shown as (anon)). Use to discover the exact concrete type/interface of widgets SS creates (e.g. a real Reactive [Expression] vs [Text]).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)") },
                ["required"] = new JsonArray { "module", "block" } }
        },
        new JsonObject {
            ["name"] = "live_add_variable_to_block",
            ["description"] = "Create a Local Variable in a web block (NRWebBlock.Variables) in an OPEN module - LIVE tree, undo unit. Hunts a factory on the Variables collection (CreateLocalVariable/CreateVariable/Create/Add) or the block itself. Returns the created variable type.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["name"] = Str("name", "New variable name (e.g. Test2Variable)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_screen_variable",
            ["description"] = "Create a Local Variable on a screen (page vars for pagination: PageNumber, PageSize, SearchText...) in an OPEN module - LIVE tree, undo unit. Optional type + default applied best-effort. Mirrors live_add_variable_to_block.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "New variable name (e.g. PageNumber)"),
                    ["type"] = Str("type", "Optional type (e.g. Integer, Text, Boolean)"),
                    ["defaultValue"] = Str("defaultValue", "Optional default value") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_widget_property",
            ["description"] = "Set any string property on a widget inside a web block - LIVE tree, undo unit. To set an Expression's value, use propName 'Value' (expression ref, e.g. the variable name Test2Variable) or 'Text'; falls back to SetValue then SetProp. Mirrors live_set_widget_property but for blocks.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["widgetName"] = Str("widgetName", "Widget name (e.g. ZombieScore)"),
                    ["propName"] = Str("propName", "Property name (e.g. Value or Text)"),
                    ["propValue"] = Str("propValue", "Value (for Value, pass the expression reference, e.g. Test2Variable)") },
                ["required"] = new JsonArray { "module", "block", "widgetName", "propName", "propValue" } }
        },
        new JsonObject {
            ["name"] = "live_list_web_flows",
            ["description"] = "List the web flows, their screens, and the module's themes (with stylesheet length) for an OPEN module. Also reports the module Kind (name + value) so you can tell Reactive vs Traditional. Read-only. Use to verify screen creation and find screen/flow names before add_* calls.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_add_container",
            ["description"] = "Add a Container widget to a screen (or inside another container/link) in an OPEN module - LIVE tree, undo unit. Containers render as a div and hold child widgets; use parent='' (empty) to add to the screen's top-level widgets, or parent=<containerName> to nest. Optionally set StyleClasses (the CSS class). Every widget is addressable by its name thereafter.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty to add to the screen top level"),
                    ["name"] = Str("name", "New container name (must be unique within the screen)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) to set on the container (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_expression",
            ["description"] = "Add an Expression widget (shows a computed/text value) to a screen or container in an OPEN module - LIVE, undo unit. value is an OutSystems expression string (text literals use double quotes, e.g. \"Hello\"); set via IExpressionWidget.SetValue. parent='' = screen top level. Optionally set StyleClasses.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level"),
                    ["name"] = Str("name", "New expression widget name"),
                    ["value"] = Str("value", "Expression text (e.g. \"Hello\")"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_text",
            ["description"] = "Add a Text widget (static text) to a screen or container in an OPEN module - LIVE, undo unit. text is the literal text content. parent='' = screen top level. Optionally set StyleClasses. Simpler than an expression for plain labels.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level"),
                    ["name"] = Str("name", "New text widget name"),
                    ["text"] = Str("text", "Literal text content"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_link",
            ["description"] = "Add a Link widget to a screen or container in an OPEN module - LIVE, undo unit. text becomes the link title. If targetScreen names an existing screen, the link's OnClick destination is set to navigate there (non-fatal if it cannot be set). parent='' = screen top level. Optionally set StyleClasses.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level"),
                    ["name"] = Str("name", "New link widget name"),
                    ["text"] = Str("text", "Link title text"),
                    ["targetScreen"] = Str("targetScreen", "Destination screen name (optional; must exist to wire navigation)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_button",
            ["description"] = "Add a REAL Button widget (the NRWidgets plugin CustomWidget, serialized as CustomWidget-OutSystems.Plugin.NRWidgets.Button) to a layout placeholder (e.g. Actions) or to the screen/parent container of a screen in an OPEN module - LIVE tree, undo unit. text becomes the button label (a Text child inside its 'content' placeholder). Set styleClass for the CSS class (e.g. 'btn btn-primary'). target: pass placeholder=<layout placeholder name> to drop into a layout placeholder (e.g. Actions), or parent=<container name> ('' for screen top level) otherwise.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["placeholder"] = Str("placeholder", "Layout placeholder name (e.g. Actions); overrides parent when set"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level (used when placeholder is empty)"),
                    ["name"] = Str("name", "New button widget name"),
                    ["text"] = Str("text", "Button label text"),
                    ["styleClass"] = Str("styleClass", "CSS class(es), e.g. \"btn btn-primary\" (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_button_onclick",
            ["description"] = "Wire a Button widget's OnClick event handler to an action (Client Action for Reactive) in an OPEN module - LIVE tree, undo unit. The OnClick Destination is set to the named action. If the button has no OnClick yet, it is created (CreateOnClick / OnClick EventHandler). button = the button widget's name; actionName = the Client Action name to run on click.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["button"] = Str("button", "Button widget name"),
                    ["actionName"] = Str("actionName", "Action (Client Action) name to run on click") },
                ["required"] = new JsonArray { "module", "screen", "button", "actionName" } }
        },
        new JsonObject {
            ["name"] = "live_add_button_to_block",
            ["description"] = "Add a REAL Button widget (NRWidgets plugin CustomWidget) into a WEB BLOCK's widget tree (or a named container inside it) in an OPEN module - LIVE tree, undo unit. Same mechanism as live_add_button on screens (ButtonDescriptor -> CreateWidget(descriptor)); blocks are found via FindBlock. parent='' = block root. text becomes the button label; styleClass = CSS class (e.g. \"btn btn-primary\").",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["parent"] = Str("parent", "Container name inside the block (optional; empty = block root)"),
                    ["name"] = Str("name", "New button widget name"),
                    ["text"] = Str("text", "Button label text"),
                    ["styleClass"] = Str("styleClass", "CSS class(es), e.g. \"btn btn-primary\" (optional)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_button_onclick",
            ["description"] = "Wire a Button widget's OnClick inside a web block to an action in an OPEN module - LIVE tree, undo unit. The action is looked up on the block's own ClientActions first, then the module. Wires handler.Destination when the action is IClientSideDestination (block/screen client action), else falls back to the descriptor SetOnClickHandler.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["button"] = Str("button", "Button widget name inside the block"),
                    ["actionName"] = Str("actionName", "Action (Client Action) name to run on click") },
                ["required"] = new JsonArray { "module", "block", "button", "actionName" } }
        },
        new JsonObject {
            ["name"] = "live_list_block_input_params",
            ["description"] = "Read-only: dump a web block's InputParameters (name + type). Web block input parameters are how parent screens/blocks pass data INTO the block.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)") },
                ["required"] = new JsonArray { "module", "block" } }
        },
        new JsonObject {
            ["name"] = "live_add_input_param_to_block",
            ["description"] = "Create an INPUT PARAMETER on a web block (block's InputParameters collection) so parent screens/blocks can pass data in - LIVE tree, undo unit. type is a basic type (LongInteger, Text, ...) or a Structure name. Factory hunt: WebBlock.CreateInputParameter etc.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieHeader)"),
                    ["name"] = Str("name", "New input parameter name (e.g. Score)"),
                    ["type"] = Str("type", "Type name (e.g. Text, LongInteger)") },
                ["required"] = new JsonArray { "module", "block", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_input_param_type",
            ["description"] = "Set the DataType of an existing web-block input parameter - LIVE tree, undo unit. Mirrors live_set_input_param_type: typeName for basic/Structure/entity record (+producerModule), entityName for an entity Identifier type (+producerModule).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["paramName"] = Str("paramName", "Input parameter name"),
                    ["typeName"] = Str("typeName", "Basic type, Structure name, or entity name (optional if entityName is set)"),
                    ["producerModule"] = Str("producerModule", "Producer module for entity/entity-Identifier types (optional)"),
                    ["entityName"] = Str("entityName", "Entity name -> sets IdentifierType (optional)") },
                ["required"] = new JsonArray { "module", "block", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_remove_input_param_from_block",
            ["description"] = "Remove an input parameter by name from a web block - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["paramName"] = Str("paramName", "Input parameter name to remove") },
                ["required"] = new JsonArray { "module", "block", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_remove_screen_input_param",
            ["description"] = "Remove an input parameter by name from a SCREEN - LIVE tree, undo unit. The revert path when a test input breaks existing callers (every link to that screen then requires the argument).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["paramName"] = Str("paramName", "Input parameter name to remove") },
                ["required"] = new JsonArray { "module", "screen", "paramName" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_input_to_block",
            ["description"] = "Add an INPUT parameter to a web block typed as an entity record from a consumed reference (producerModule) - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Input parameter name"),
                    ["entityName"] = Str("entityName", "Entity name in the producer module"),
                    ["producerModule"] = Str("producerModule", "Producer module that exposes the entity") },
                ["required"] = new JsonArray { "module", "block", "name", "entityName", "producerModule" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_identifier_input_to_block",
            ["description"] = "Add an INPUT parameter to a web block typed as an entity's Identifier type from a consumed reference - LIVE tree, undo unit. Correct for passing a record's Id into a block.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Input parameter name"),
                    ["entityName"] = Str("entityName", "Entity name in the producer module"),
                    ["producerModule"] = Str("producerModule", "Producer module that exposes the entity") },
                ["required"] = new JsonArray { "module", "block", "name", "entityName", "producerModule" } }
        },
        new JsonObject {
            ["name"] = "live_add_if_node",
            ["description"] = "Create an If decision node in an action flow - LIVE tree, undo unit. Sets the condition via IIfNode.SetCondition (proven). Optional afterNodeIndex inserts it after an existing node (chains via TrueTarget). True/false branches are TrueTarget/FalseTarget - wire them with live_set_connector_target (probe with live_probe_node_connectors).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name (service/server/client action)"),
                    ["condition"] = Str("condition", "Condition expression (e.g. True or UserId > 0)"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: node index to insert after (chains the previous node's Target to the If)" } },
                ["required"] = new JsonArray { "module", "action", "condition" } }
        },
        new JsonObject {
            ["name"] = "live_add_switch_node",
            ["description"] = "Create a Switch decision node in an action flow - LIVE tree, undo unit. Switch conditions live per-case (not a single expression). Otherwise branch = ISwitchNode.OtherwiseTarget; wire with live_set_connector_target. Optional afterNodeIndex inserts it into the flow.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: node index to insert after" } },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_probe_node_connectors",
            ["description"] = "Read-only: dump a flow node's connector-like settable props (TrueTarget/FalseTarget for If, OtherwiseTarget for Switch, etc.) with types and current targets. Use before live_set_connector_target to find the exact branch property name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Node index (from live_list_flow)" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_probe_node_conditions",
            ["description"] = "Read-only: dump a Switch node's Conditions collection - each condition's concrete type, settable properties, and current values. Use to discover how to add/set switch cases (per-case value + target) before editing.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Switch node index (from live_list_flow)" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex" } }
        },
        new JsonObject {
            ["name"] = "live_set_switch_case",
            ["description"] = "Add or update a Switch condition in an action flow - LIVE tree, undo unit. If a case whose Value text matches is found, updates its Target; otherwise creates a new condition via the Conditions collection's Create/Add factory or the node's CreateCondition/CreateCase method. value is the case expression (e.g. Admin or \"Admin\" for text). Otherwise branch is set separately with live_set_connector_target (OtherwiseTarget).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Switch node index" },
                    ["value"] = Str("value", "Case expression value (e.g. Admin)"),
                    ["targetIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Node index to route this case to" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "value", "targetIndex" } }
        },
        new JsonObject {
            ["name"] = "live_set_connector_target",
            ["description"] = "Set a named branch/target property on a flow node to point at another node - LIVE tree, undo unit. Use propName TrueTarget/FalseTarget on an If (or OtherwiseTarget on a Switch) since live_set_node_target only sets 'Target'. Works on explicit interface props (IIfNode.TrueTarget etc.).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Source node index" },
                    ["propName"] = Str("propName", "Branch property name (e.g. TrueTarget, FalseTarget, OtherwiseTarget)"),
                    ["targetIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Target node index" } },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "propName", "targetIndex" } }
        },
        new JsonObject {
            ["name"] = "live_probe_block_members",
            ["description"] = "Read-only: dump a web block's properties AND collections (InputParameters, Variables, Events, ClientActions, Widgets) with counts and first-item concrete types. Use to discover the Events surface and lifecycle-handler property names (OnInitialize/OnReady/...) before add_event_to_block / set_block_handler.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name") },
                ["required"] = new JsonArray { "module", "block" } }
        },
        new JsonObject {
            ["name"] = "live_add_event_to_block",
            ["description"] = "Create a custom EVENT on a web block (the block's Events collection) - LIVE tree, undo unit. Events let a block signal data/state UP to parent screens/blocks. Factory hunt: Events.CreateEvent/Create/Add.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "New event name (e.g. OnSave)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_event_param_to_block",
            ["description"] = "Add a payload parameter to an existing web-block Event (the data the block passes to the parent when raising the event) - LIVE tree, undo unit. type is a basic type or Structure name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["event"] = Str("event", "Event name (e.g. OnSave)"),
                    ["name"] = Str("name", "Payload parameter name"),
                    ["type"] = Str("type", "Type name (e.g. Text, LongInteger)") },
                ["required"] = new JsonArray { "module", "block", "event", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_handler",
            ["description"] = "Assign a lifecycle/event-handler property of a web block to a Client Action - LIVE tree, undo unit. handler = OnInitialize / OnReady / OnRender / OnParametersChange / OnDestroy (matched case-insensitively; confirm the exact prop with live_probe_block_members). Action looked up on the block's ClientActions first, then the module.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["handler"] = Str("handler", "Lifecycle handler name (e.g. OnInitialize, OnReady, OnParametersChange)"),
                    ["actionName"] = Str("actionName", "Client Action name to run as the handler") },
                ["required"] = new JsonArray { "module", "block", "handler", "actionName" } }
        },
        new JsonObject {
            ["name"] = "live_add_raise_event_node",
            ["description"] = "Create a Raise-Event node in an action/client-action flow and (best-effort) bind it to an event by name - LIVE tree, undo unit. Tries candidate RaiseEvent node interfaces; use live_list_flow to find the node index afterwards and live_set_connector_target to wire outgoing branches. Optional afterNodeIndex inserts into the flow.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["eventName"] = Str("eventName", "Event name to raise (optional)"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: node index to insert after" } },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_style_class",
            ["description"] = "Set the StyleClasses (CSS class) on an existing widget in a screen of an OPEN module - LIVE, undo unit. Containers: the class goes into the widget's 'Style' CustomProperty as a ParsedExpression text literal (SetValueExpression) and any stray CustomStyle is cleared - this is what persists and shows in SS. Links/Text: CustomStyle (correct SS convention). Use live_list_widgets to find widget names. Verify with live_probe_style_prop.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name (as created / shown by live_list_widgets)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) to set") },
                ["required"] = new JsonArray { "module", "screen", "widget", "styleClass" } }
        },
        new JsonObject {
            ["name"] = "live_set_module_css",
            ["description"] = "Write the module stylesheet CSS into an OPEN module - LIVE, undo unit. Uses the public WebStyleSheet.SetUserCssSource API, so the CSS appears in SS's theme CSS editor (_userCssSource, NOT the render-only _cssSource). Pass the full CSS text.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["css"] = Str("css", "Full CSS text for the module stylesheet") },
                ["required"] = new JsonArray { "module", "css" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_title",
            ["description"] = "Set the Title of a web screen in an OPEN module - LIVE, undo unit. The title shows in the browser tab / page heading.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["title"] = Str("title", "Screen title text") },
                ["required"] = new JsonArray { "module", "screen", "title" } }
        },
        new JsonObject {
            ["name"] = "live_list_widgets",
            ["description"] = "Dump the widget tree of a web screen in an OPEN module - names, widget interface types, StyleClasses, and nested children. Read-only. Use to verify add_* calls and find widget names for live_set_style_class / nesting.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_set_user_css",
            ["description"] = "Write the module's USER CSS source into an OPEN module - LIVE, undo unit. Uses the public WebStyleSheet.SetUserCssSource(String) API (the source SS's theme CSS editor displays). Sets _userCssSource, NOT the render-only _cssSource. Use when CSS must appear in the theme CSS editor. Pass the full CSS text.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["css"] = Str("css", "Full CSS text for the module stylesheet") },
                ["required"] = new JsonArray { "module", "css" } }
        },
        new JsonObject {
            ["name"] = "live_probe_style_prop",
            ["description"] = "Inspect a widget's 'Style' CustomProperty in a screen of an OPEN module - read-only diagnostic. Confirms a container's CSS class is stored as a ParsedExpression text literal in the Style CustomProperty (_valueExpression), which is the surface SS persists. Use after live_set_style_class to verify the class landed.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name") },
                ["required"] = new JsonArray { "module", "screen", "widget" } }
        },
        new JsonObject {
            ["name"] = "live_read_theme_css",
            ["description"] = "Report a theme stylesheet's CSS source field lengths (_cssSource/_userCssSource/_generatedCssSource/_finalCssSource/designTimeValue) for an OPEN module - read-only diagnostic. _userCssSource is what SS's theme CSS editor shows. Use to verify a set_user_css / set_module_css write landed.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_probe_sheet",
            ["description"] = "Dump a theme stylesheet's concrete type, full public API (methods + settable props), the _cssSource expression type + Clone support, and ModelServices methods for an OPEN module - read-only diagnostic. Use to pick the correct surface for writing CSS (e.g. SetUserCssSource, SetCssSource).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_create_theme",
            ["description"] = "Create a new theme by deep-cloning an existing one in an OPEN module (e.g. FitnessManager -> ZombieTheme) - LIVE tree, undo unit. Uses IModelServices.Duplicate inside a real SS command, then renames. Optionally writes the given CSS to the new theme's user-CSS source (the source SS's theme CSS editor displays). Requires SS running with OsLiveBridge and the module open.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["sourceName"] = Str("sourceName", "Existing theme to clone (e.g. FitnessManager)"),
                    ["newName"] = Str("newName", "New theme name (e.g. ZombieTheme)"),
                    ["css"] = Str("css", "Optional full CSS text to write to the new theme's user-CSS source") },
                ["required"] = new JsonArray { "module", "sourceName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_set_theme_css",
            ["description"] = "Write the user-CSS source on a SPECIFIC theme by name in an OPEN module - LIVE, undo unit. Uses the public WebStyleSheet.SetUserCssSource(String) API so the CSS appears in SS's theme CSS editor (_userCssSource). Use when the module's first theme is NOT the one you want to style (e.g. writing zombie CSS to ZombieTheme while FitnessManager is first). Pass the full CSS text.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["themeName"] = Str("themeName", "Theme name to write CSS to (e.g. ZombieTheme)"),
                    ["css"] = Str("css", "Full CSS text for the theme stylesheet") },
                ["required"] = new JsonArray { "module", "themeName", "css" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_theme",
            ["description"] = "Set the Theme property on a screen in an OPEN module - LIVE, undo unit. Assigns the named theme to the screen (tries ThemeNewRuntime/Theme/WebTheme property names). Use to point a new screen (e.g. ZombieGame) at a cloned theme (e.g. ZombieTheme).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["themeName"] = Str("themeName", "Theme name (e.g. ZombieTheme)") },
                ["required"] = new JsonArray { "module", "screen", "themeName" } }
        },
        new JsonObject {
            ["name"] = "live_set_flow_theme",
            ["description"] = "Set the Theme property on a web flow in an OPEN module - LIVE, undo unit. Screens in the flow inherit this theme. Used for New Runtime (CrossDevice/Reactive) modules where a screen's own Theme property is read-only but the flow's is settable.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["flow"] = Str("flow", "Web flow name (e.g. zombieGame)"),
                    ["themeName"] = Str("themeName", "Theme name (e.g. ZombieTheme)") },
                ["required"] = new JsonArray { "module", "flow", "themeName" } }
        },
        new JsonObject {
            ["name"] = "live_clone_web_block",
            ["description"] = "Deep-clone a web block (e.g. the Layout or Menu block) in an OPEN module into a new one - LIVE tree, undo unit. Uses IModelServices.Duplicate + rename (the same proven mechanism as live_clone_service_action). Use to create ZombieLayout/ZombieMenu from the module's existing layout/menu blocks before wiring them into a cloned theme.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["sourceName"] = Str("sourceName", "Existing web block to clone (e.g. LayoutWrapper or Menu)"),
                    ["newName"] = Str("newName", "New web block name (e.g. ZombieLayout)") },
                ["required"] = new JsonArray { "module", "sourceName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_move_web_block_to_flow",
            ["description"] = "Move an existing web block from its current WebFlow to a different WebFlow in an OPEN module - LIVE tree, undo unit. Needed because live_clone_web_block places the clone in the source flow's Nodes, but the block may need to live in a different flow (e.g. Header cloned from Menu in Common, consumed in zombieGame). Reparents the block node between flows.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name to move (e.g. Header)"),
                    ["targetFlow"] = Str("targetFlow", "Destination WebFlow name (e.g. zombieGame)") },
                ["required"] = new JsonArray { "module", "block", "targetFlow" } }
        },
        new JsonObject {
            ["name"] = "live_set_theme_layout",
            ["description"] = "Wire a theme's layout and/or menu web blocks in an OPEN module - LIVE, undo unit. Sets NormalPageLayout (and MenuWebBlock) on the theme to point at the named web blocks (which must already exist). Use after live_clone_web_block to make ZombieTheme use ZombieLayout/ZombieMenu.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["themeName"] = Str("themeName", "Theme name (e.g. ZombieTheme)"),
                    ["layoutBlock"] = Str("layoutBlock", "Web block name to use as NormalPageLayout (optional)"),
                    ["menuBlock"] = Str("menuBlock", "Web block name to use as MenuWebBlock (optional)") },
                ["required"] = new JsonArray { "module", "themeName" } }
        },
        new JsonObject {
            ["name"] = "live_probe_theme",
            ["description"] = "Dump a theme's settable properties (with current values) + its stylesheet's CSS source field lengths for an OPEN module - read-only diagnostic. Use to discover the correct property names for layout/menu wiring and verify a theme clone landed.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["themeName"] = Str("themeName", "Theme name (optional; omit for all themes)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_add_html_element",
            ["description"] = "Add an HTML element (div/iframe/etc.) to a screen or container in an OPEN module - LIVE tree, undo unit. tag sets the element tag (e.g. iframe). parent='' = screen top level. Optionally set StyleClasses. Use for content that needs a raw HTML element (e.g. the YouTube trailer iframe).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level"),
                    ["name"] = Str("name", "New element name"),
                    ["tag"] = Str("tag", "HTML tag (e.g. div, iframe, section)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_to_placeholder",
            ["description"] = "Add a widget (container/text/expression/link) into a named placeholder of a screen's LAYOUT instance in an OPEN module - LIVE tree, undo unit. Requires the screen to have a layout WebBlockInstance (from a NormalPageLayout theme). kind: container|text|expression|link. Use to fill layout placeholders like MainContent/Header/Footer.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["placeholder"] = Str("placeholder", "Placeholder name (e.g. MainContent)"),
                    ["kind"] = Str("kind", "container|text|expression|link"),
                    ["name"] = Str("name", "New widget name"),
                    ["text"] = Str("text", "Text/expression value (for text/expression/link)"),
                    ["targetScreen"] = Str("targetScreen", "Destination screen for links (optional)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "placeholder", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_inside_placeholder",
            ["description"] = "Add a widget INSIDE a named container that lives inside a layout placeholder on a screen in an OPEN module - LIVE tree, undo unit. Finds the container by name within the placeholder's child widgets, then creates the widget inside it (so text/link/cards nest within a container dropped into a layout placeholder).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["placeholder"] = Str("placeholder", "Placeholder name (e.g. MainContent)"),
                    ["parent"] = Str("parent", "Container name inside the placeholder"),
                    ["kind"] = Str("kind", "container|text|expression|link"),
                    ["name"] = Str("name", "New widget name"),
                    ["text"] = Str("text", "Text/expression value (for text/expression/link)"),
                    ["targetScreen"] = Str("targetScreen", "Destination screen for links (optional)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "screen", "placeholder", "parent", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_link_to_block",
            ["description"] = "Add a Link widget into a web block's widget tree (or a named container inside it) in an OPEN module - LIVE tree, undo unit. Use to put nav links into the Menu block's nav container so every screen using the layout gets the same navigation.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (e.g. ZombieMenu)"),
                    ["parent"] = Str("parent", "Container name inside the block (optional; empty = block root)"),
                    ["name"] = Str("name", "New link widget name"),
                    ["text"] = Str("text", "Link title text"),
                    ["targetScreen"] = Str("targetScreen", "Destination screen name (optional)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_list_placeholders",
            ["description"] = "List the layout placeholders on a screen in an OPEN module - read-only diagnostic. Shows each placeholder's Name + type + CreateWidget accessor. Use before live_add_to_placeholder to discover placeholder names (e.g. MainContent/Header/Footer).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_delete_from_placeholder",
            ["description"] = "Delete a widget by name from a layout placeholder on a screen in an OPEN module - LIVE tree, undo unit. Use to remove a placeholder fill that was added with the wrong kind/label before re-adding it correctly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["placeholder"] = Str("placeholder", "Placeholder name"),
                    ["name"] = Str("name", "Widget name to delete") },
                ["required"] = new JsonArray { "module", "screen", "placeholder", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_extended_property",
            ["description"] = "Set an HTML attribute on a widget (screen/container/html element) in an OPEN module - LIVE tree, undo unit. Writes an extended property (e.g. src on an iframe, or allowfullscreen). Handles HTML elements (Tag + Attributes collection) and CustomWidgets (CustomProperties). Use after live_add_html_element for content like iframes.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["attrName"] = Str("attrName", "Attribute name (e.g. src, allowfullscreen)"),
                    ["attrValue"] = Str("attrValue", "Attribute value (e.g. the iframe URL, or 'true' for a boolean attribute)") },
                ["required"] = new JsonArray { "module", "screen", "widget", "attrName", "attrValue" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_layout",
            ["description"] = "Repoint a screen's layout WebBlockInstance SourceWebBlock to a different web block (e.g. ZombieLayout) on a screen in an OPEN module - LIVE tree, undo unit. Use to apply a custom layout to a screen. The target web block must exist in the module.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["layoutBlock"] = Str("layoutBlock", "Web block name to use as the screen layout (e.g. ZombieLayout)") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_delete_web_flow",
            ["description"] = "Delete a web flow (and its screens) from an OPEN module - LIVE tree, undo unit. Removes the flow from whichever collection holds it (WebFlows / NRWebFlows). Use to remove a scratch flow after testing.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["flow"] = Str("flow", "Web flow name to delete") },
                ["required"] = new JsonArray { "module", "flow" } }
        },
        new JsonObject {
            ["name"] = "live_delete_screen_from_flow",
            ["description"] = "Delete a single screen node from a web flow in an OPEN module - LIVE tree, undo unit. Removes the named IScreen node from the flow's Nodes. Use to remove a test screen while keeping the other screens in the flow.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["flow"] = Str("flow", "Web flow name"),
                    ["screen"] = Str("screen", "Screen name to delete") },
                ["required"] = new JsonArray { "module", "flow", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_delete_widget",
            ["description"] = "Delete a widget by name (recursively, with its children) from a screen in an OPEN module - LIVE tree, undo unit. Mirrors DeleteWidgetFromBlock for blocks. Use to remove a misplaced widget, then re-add it correctly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "Widget name to delete (recursively)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_delete_layout",
            ["description"] = "Delete the screen's layout web block instance (the first IWebBlockInstanceWidget) from a screen in an OPEN module - LIVE tree, undo unit. Recursively removes the layout instance and its children. Use to strip a layout before wiring a custom one.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_add_html_text",
            ["description"] = "Add an HTML element with text content (real <h1>/<h2>/<p>... via its Tag) to a screen or container in an OPEN module - LIVE tree, undo unit. Unlike live_add_html_element (empty tag), this also writes the text into the element. parent='' = screen top level.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["parent"] = Str("parent", "Parent widget name, or empty for screen top level"),
                    ["name"] = Str("name", "New element name"),
                    ["tag"] = Str("tag", "HTML tag (e.g. h1, p, div)"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)"),
                    ["text"] = Str("text", "Text content") },
                ["required"] = new JsonArray { "module", "screen", "name", "text" } }
        },
        new JsonObject {
            ["name"] = "live_probe_widget_deep",
            ["description"] = "Deep-probe a widget's full public API in an OPEN module - read-only diagnostic. Enumerates the widget's interfaces, methods, and properties to discover how to manipulate it (e.g. event handlers, attributes). Use before trying to set attributes/handlers on an unfamiliar widget.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widgetName"] = Str("widgetName", "Widget name") },
                ["required"] = new JsonArray { "module", "screen", "widgetName" } }
        },
        new JsonObject {
            ["name"] = "live_probe_layout_ref",
            ["description"] = "Probe a screen's layout WebBlockInstance properties in an OPEN module - read-only diagnostic. Shows the layout instance's SourceWebBlock and related props. Use before live_set_screen_layout to confirm the current layout.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_read_user_css_text",
            ["description"] = "Read a theme's USER CSS source text in an OPEN module - read-only diagnostic. Mirrors live_read_theme_css but by theme name and returns the _userCssSource text straight.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["themeName"] = Str("themeName", "Theme name (optional; defaults to first theme)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_probe_block_widget",
            ["description"] = "Probe a widget's Style CustomProperty / settable props INSIDE a web block in an OPEN module - read-only diagnostic. Use to inspect a block widget before setting its properties or style class.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name inside the block") },
                ["required"] = new JsonArray { "module", "block", "widget" } }
        },
        new JsonObject {
            ["name"] = "live_dump_widget_concretes",
            ["description"] = "Dump all concrete NRWidgets widget types available in the loaded plugin - read-only diagnostic, no module needed. Use to discover concrete widget type names (e.g. for ProbeWidgetApi / widget kinds). Useful when extending widget support.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject() } },
        new JsonObject {
            ["name"] = "live_probe_data_sources",
            ["description"] = "Probe the DATA SOURCING collections (DataActions / ScreenAggregates) on a web block OR a screen in an OPEN module - read-only diagnostic. Dumps each collection's concrete type, its factory methods (Create*/Add*/New*), and any existing items. Pass block=<block> OR screen=<screen>. Use to discover the factory (CreateAggregate/CreateDataAction/Add) before adding aggregates/data actions.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_to_block",
            ["description"] = "Create a SCREEN AGGREGATE (SERVER-FETCH) on a web block in an OPEN module - LIVE tree, undo unit. The block's ScreenAggregates data source, with an optional source entity (from a consumed reference; pass entityName + producerModule). Factory: block.CreateScreenAggregate(isClientSide=false, String, IKey) -> IScreenAggregate. NOTE: isClientSide MUST be false - true creates a client-side/local aggregate that corrupts the module. After creating, re-probe with live_probe_block_members or live_probe_data_sources to verify.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["entityName"] = Str("entityName", "Source entity name (optional)"),
                    ["producerModule"] = Str("producerModule", "Producer module that exposes the entity (optional)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_data_action_to_block",
            ["description"] = "Create a DATA ACTION (DataScreenActionFlow) on a web block in an OPEN module - LIVE tree, undo unit. The block's DataActions collection (a client-side data fetch action with its own flow). Factory proven: block.CreateDataAction(String,IKey) -> IDataAction. Use live_set_block_handler / flow tools on '<name>' afterwards, or re-probe with live_probe_data_sources.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Data action name") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_to_screen",
            ["description"] = "Create a SCREEN AGGREGATE (SERVER-FETCH) on a SCREEN in an OPEN module - LIVE tree, undo unit. Same as live_add_aggregate_to_block but targets a screen's ScreenAggregates collection. Factory: screen.CreateScreenAggregate(isClientSide=false, String, IKey) -> IScreenAggregate. NOTE: isClientSide MUST be false - true creates a client-side/local aggregate that corrupts the module. Optional source entity via entityName+producerModule.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["entityName"] = Str("entityName", "Source entity name (optional)"),
                    ["producerModule"] = Str("producerModule", "Producer module that exposes the entity (optional)") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_data_action_to_screen",
            ["description"] = "Create a DATA ACTION (DataScreenActionFlow) on a SCREEN in an OPEN module - LIVE tree, undo unit. Same as live_add_data_action_to_block but targets a screen's DataActions collection.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "Data action name") },
                ["required"] = new JsonArray { "module", "screen", "name" } }
        },
        new JsonObject {
            ["name"] = "live_delete_data_action",
            ["description"] = "Delete a data action (DataScreenActionFlow) from a block OR screen by name in an OPEN module - LIVE tree, undo unit. Pass block=<block> or screen=<screen>.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["name"] = Str("name", "Data action name to delete") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_delete_aggregate",
            ["description"] = "Delete an aggregate (WebScreenDataSet) from a block OR screen by name in an OPEN module - LIVE tree, undo unit. Pass block=<block> or screen=<screen>. Use to remove test aggregates.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["name"] = Str("name", "Aggregate name to delete") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_probe_widget_kinds",
            ["description"] = "Enumerate all widget KINDS available in the loaded NRWidgets plugin at runtime - read-only diagnostic (no module needed, module is accepted for routing). Reveals the exact concrete classes + descriptor objects (e.g. ServiceStudio.Plugin.NRWidgets.Label+Kind -> LabelDescriptor) used to create Reactive widgets. Use to discover/extend widget kinds.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (used for pipe discovery)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_add_nr_widget",
            ["description"] = "Create ANY Reactive custom widget (label, input, textarea, checkbox, dropdown, radio, radio-group, switch, list, table, headercell, rowcell, image, icon, form, button-group, container, expression, link, html) on a SCREEN or WEB BLOCK - LIVE tree, undo unit. Uses the Button-proven descriptor pattern (<Kind>+Kind.Instance.Descriptor -> CreateWidget(descriptor)). text applies to label/input/textarea. Pass screen=<screen> OR block=<block>; parent nests inside a container. Table cells: kind=headercell with parent='Table.HeaderRow' and kind=rowcell with parent='Table.Row' (the table widget must exist first).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["parent"] = Str("parent", "Parent container name (optional)"),
                    ["kind"] = Str("kind", "Widget kind: label|input|textarea|checkbox|dropdown|radio|radio-group|switch|list|table|headercell|rowcell|image|icon|form|button-group|container|expression|link|html"),
                    ["name"] = Str("name", "New widget name"),
                    ["styleClass"] = Str("styleClass", "CSS class(es) (optional)"),
                    ["text"] = Str("text", "Text/initial value (label/input/textarea, optional)") },
                ["required"] = new JsonArray { "module", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_widget_handler",
            ["description"] = "Wire ANY widget event handler (OnChange/OnFocus/OnBlur/OnClick/...) on a screen or block widget to a client action - LIVE tree, undo unit. The handler lives on the widget's EventHandlers collection (matched by EventName). Works for buttons (OnClick), inputs/dropdowns/checkboxes/switches (OnChange/OnBlur...). Pass screen=<screen> OR block=<block>. The action must be a client action (screen-level or block-level, or module global).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["widget"] = Str("widget", "Widget name (e.g. the input/button name)"),
                    ["event"] = Str("event", "Event name (e.g. OnChange, OnFocus, OnBlur, OnClick)"),
                    ["actionName"] = Str("actionName", "Client action name to route to") },
                ["required"] = new JsonArray { "module", "widget", "event", "actionName" } }
        },
        new JsonObject {
            ["name"] = "live_set_element_description",
            ["description"] = "Set the Description property on an element (web block, screen, or action) in an OPEN module - LIVE tree, undo unit. kind: block | screen | action. name = element name. Description is a writable property (confirmed via probe_block_members). Use to document elements headlessly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["kind"] = Str("kind", "Element kind: block | screen | action"),
                    ["name"] = Str("name", "Element name"),
                    ["description"] = Str("description", "Description text") },
                ["required"] = new JsonArray { "module", "kind", "name", "description" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_variable_type",
            ["description"] = "Set DataType (and optional default value) on an existing web-block LocalVariable in an OPEN module - LIVE tree, undo unit. type: basic type (Boolean, Text, BinaryData...), Structure name, ListType name (e.g. 'User Record List'), or - with identifier=true + entityName + producerModule - an entity's Identifier type (e.g. User Identifier for dropdown selected-value variables). producerModule '(System)' exposes User/Group/Tenant etc.; use live_probe_references to discover names. optional default (text literals pre-quoted, booleans/numbers bare).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["var"] = Str("var", "Local variable name"),
                    ["type"] = Str("type", "Type name (basic, Structure, ListType, or entity name when identifier=true)"),
                    ["default"] = Str("default", "Default value expression (optional; text literals pre-quoted)"),
                    ["producerModule"] = Str("producerModule", "Optional: producer reference module exposing the entity (e.g. '(System)')"),
                    ["identifier"] = new JsonObject { ["type"] = "boolean", ["description"] = "Set the entity's Identifier type instead of the entity record (requires entityName/producerModule; entityName defaults to type)" },
                    ["entityName"] = Str("entityName", "Optional: entity name when it differs from type") },
                ["required"] = new JsonArray { "module", "block", "var", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_aggregate_source",
            ["description"] = "Set the source entity on an existing SCREEN AGGREGATE of a web block in an OPEN module - LIVE tree, undo unit. entityName + producerModule (the module exposing the entity, e.g. a consumed reference). Use after live_add_aggregate_to_block.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["entityName"] = Str("entityName", "Source entity name (e.g. User)"),
                    ["producerModule"] = Str("producerModule", "Producer module exposing the entity") },
                ["required"] = new JsonArray { "module", "block", "name", "entityName" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_filter",
            ["description"] = "Add a filter expression to an existing SCREEN AGGREGATE of a web block in an OPEN module - LIVE tree, undo unit. filter is the condition text (e.g. 'User.Id = TextToIdentifier(UserId)'). Use after live_set_block_aggregate_source.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["filter"] = Str("filter", "Filter expression (e.g. 'User.Id = TextToIdentifier(UserId)')") },
                ["required"] = new JsonArray { "module", "block", "name", "filter" } }
        },
        new JsonObject {
            ["name"] = "live_add_if_widget_to_block",
            ["description"] = "Create an If widget with a condition inside a web block (or a named container inside it) in an OPEN module - LIVE tree, undo unit. condition e.g. 'IsEditable'. Use for the EditGate.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["parent"] = Str("parent", "Parent container name (optional; empty = block root)"),
                    ["name"] = Str("name", "New If widget name"),
                    ["condition"] = Str("condition", "Condition expression (e.g. IsEditable)") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_move_widget_in_block",
            ["description"] = "Reparent a widget inside a web block in an OPEN module - LIVE tree, undo unit. Moves widget onto a new parent container (or block root when newParent empty) via the widget's Move/MoveTo api.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name to move"),
                    ["newParent"] = Str("newParent", "New parent container name (empty = block root)") },
                ["required"] = new JsonArray { "module", "block", "widget" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_cp",
            ["description"] = "Set ANY named CustomProperty on a web-block widget via CustomProperty.SetValueExpression in an OPEN module - LIVE tree, undo unit. General form used for Variable bindings, Dropdown Labels/Values/List, Image Source, etc. value is the expression text. NOTE: SetValueExpression PARSES the value - bare identifiers become references (valid binding when the name exists in scope), quoted strings become Text literals with the quote characters stored VERBATIM. For plain text-literal properties (e.g. Style classes) use live_set_block_cp_text instead.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name (e.g. Variable, Labels, Values, Source)"),
                    ["value"] = Str("value", "Expression value") },
                ["required"] = new JsonArray { "module", "block", "widget", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_cp_parsed",
            ["description"] = "Set a web-block widget CustomProperty as a PARSED expression with verification (typed Set<Prop> → SetPropertyValue → Value setter, literal fallback) - LIVE tree, undo unit. Reports parsed OK or literal-fallback with diagnostics. Prefer over live_set_block_cp when the value must resolve (Variable bindings).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name (e.g. Variable)"),
                    ["value"] = Str("value", "Expression value (e.g. TitleEdit)") },
                ["required"] = new JsonArray { "module", "block", "widget", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_cp_parsed",
            ["description"] = "Set a SCREEN widget CustomProperty as a PARSED expression with verification - LIVE tree, undo unit. Screen twin of live_set_block_cp_parsed. Use for input Variable bindings (e.g. SearchBox → SearchText) and any screen CP that must resolve.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name (e.g. Variable)"),
                    ["value"] = Str("value", "Expression value (e.g. SearchText)") },
                ["required"] = new JsonArray { "module", "screen", "widget", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_cp_text",
            ["description"] = "Set a TEXT-LITERAL CustomProperty (e.g. a Container's Style classes) to a plain string on a web-block widget in an OPEN module - LIVE tree, undo unit. Produces a Text element whose value is EXACTLY the string (no quotes, no expression parsing) - matches SS-created widgets. Use this for Style Classes; live_set_block_cp would store quote characters verbatim or create invalid references.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name (e.g. Style)"),
                    ["value"] = Str("value", "Plain text value (e.g. header or 'btn btn-primary')") },
                ["required"] = new JsonArray { "module", "block", "widget", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_cp_text",
            ["description"] = "Set a TEXT-LITERAL CustomProperty (e.g. a Container's Style classes) to a plain string on a SCREEN widget in an OPEN module - LIVE tree, undo unit. Screen twin of live_set_block_cp_text. Produces a Text element whose value is EXACTLY the string (no quotes, no expression parsing) - matches SS-created widgets, then forces revalidation so SS's error list is not stale. Use for Style Classes on screen containers; set_style_class / set_screen_cp_parsed with a bare class name parse as arithmetic (invalid ref) or leave stale verify errors.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name (e.g. Style)"),
                    ["value"] = Str("value", "Plain text value (e.g. zg-grid or 'btn btn-primary')") },
                ["required"] = new JsonArray { "module", "screen", "widget", "propName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_delete_screen_client_action",
            ["description"] = "Delete a SCREEN-level Client Action (ClientScreenActionFlow) from a screen's ClientActions collection in an OPEN module - LIVE tree, undo unit. Module-level delete_action does NOT cover screen actions (they live on the screen). Use to remove unused no-op handlers (e.g. leftover PurgeNoop2).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["action"] = Str("action", "Client action name") },
                ["required"] = new JsonArray { "module", "screen", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_cp_image",
            ["description"] = "Set an IMAGE-typed CustomProperty (e.g. NRWidgets.Image 'Image') with the ACTUAL Image object from the module's Images collection on a web-block widget in an OPEN module - LIVE tree, undo unit. The Image CP must hold a real ServiceStudio.Model.Image object (via CP.SetPropertyValue) - strings (SetValueExpression or raw _value writes) fail validation with 'Object data type required instead of Text'. imageName matches the image Name or Key.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "Image-typed CustomProperty name (e.g. Image)"),
                    ["imageName"] = Str("imageName", "Image Name or Key in the module's Images collection") },
                ["required"] = new JsonArray { "module", "block", "widget", "propName", "imageName" } }
        },
        new JsonObject {
            ["name"] = "live_delete_anon_block_widgets",
            ["description"] = "Delete UNNAMED (anonymous) widgets from a web block in an OPEN module by concrete-type substring - LIVE tree, undo unit. Anon widgets have no Name so they can't be addressed by delete_widget_from_block. With recursive=true also walks child widgets and placeholders (e.g. the auto-created ListItemAction inside a List Item's rightActions). Named widgets are never touched.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["typeContains"] = Str("typeContains", "Concrete-type substring (e.g. ListItemAction, NRWidgets.Image)"),
                    ["recursive"] = new JsonObject { ["type"] = "boolean", ["description"] = "Also walk child widgets and placeholders (default false)" } },
                ["required"] = new JsonArray { "module", "block", "typeContains" } }
        },
        new JsonObject {
            ["name"] = "live_probe_block_cp",
            ["description"] = "Dump a single CustomProperty's internals on a web-block widget (read-only): CP methods, ValueExpression type + fields (expression element dump incl. [Type]/[Value]/[Reference]), _value field, _valueType (runtime type) and _valueExpression. Use to verify whether a binding parsed as a Reference vs a Text literal, and whether an image CP holds a real Image object.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["propName"] = Str("propName", "CustomProperty name") },
                ["required"] = new JsonArray { "module", "block", "widget", "propName" } }
        },
        new JsonObject {
            ["name"] = "live_probe_type",
            ["description"] = "Dump a .NET type's surface in the running Service Studio (read-only): static fields/properties, instance properties, declared instance methods, nested types. Resolves by full name (nested with '+') or full-name suffix. Use to discover factory patterns before extending widget support (e.g. NRWebWidgets+If+Kind).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["typeName"] = Str("typeName", "Full or suffix type name (e.g. ServiceStudio.Model.NRWebWidgets+If+Kind)") },
                ["required"] = new JsonArray { "typeName" } }
        },
        new JsonObject {
            ["name"] = "live_probe_references",
            ["description"] = "Dump each module reference and the element NAMES inside every collection it (and its Sentinel) exposes (read-only). Use to find which producer reference exposes an entity for aggregates (e.g. User is under the '(System)' reference's ReferenceEntities) before live_add_aggregate_to_block / live_set_block_variable_type.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_create_block_client_action",
            ["description"] = "Create a ClientActionFlow on a WEB BLOCK's ClientActions collection in an OPEN module - LIVE tree, undo unit. Block-scoped client actions are the correct target for radio OnChange / widget handlers. Flow tools (live_list_flow, live_add_if_node, ...) work on '<name>' afterwards (FindAction searches block ClientActions too).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["name"] = Str("name", "New block client action name") },
                ["required"] = new JsonArray { "module", "block", "name" } }
        },
        new JsonObject {
            ["name"] = "live_create_structure",
            ["description"] = "Create a NAMED Structure (e.g. UserStats) on the eSpace in an OPEN module - LIVE tree, undo unit. Uses IESpace.CreateStructure(name, key). Add attributes with live_add_structure_attribute afterwards.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New structure name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_structure_attribute",
            ["description"] = "Add an ATTRIBUTE to a named Structure on the eSpace in an OPEN module - LIVE tree, undo unit. type is a basic type (Integer, Text, DateTime, Boolean, ...) resolved as es.<type>Type. Supports list types via List of <Type> when recognized.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["structure"] = Str("structure", "Structure name (e.g. UserStats)"),
                    ["attrName"] = Str("attrName", "Attribute name (e.g. TotalLogins)"),
                    ["type"] = Str("type", "Type name (e.g. Integer)") },
                ["required"] = new JsonArray { "module", "structure", "attrName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_structure_attribute_type",
            ["description"] = "Set the DataType (and list element type) on an EXISTING structure attribute - LIVE tree, undo unit. type resolves via es.<type>Type, then es.ListTypes by Name (e.g. 'ActivityTag List'), then es.Structures by Name. Use to fix an attribute created without its DataType (e.g. RecentActivityTags).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["structure"] = Str("structure", "Structure name (e.g. UserStats)"),
                    ["attrName"] = Str("attrName", "Attribute name (e.g. RecentActivityTags)"),
                    ["type"] = Str("type", "Type name (e.g. ActivityTag List or Integer)") },
                ["required"] = new JsonArray { "module", "structure", "attrName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_create_entity",
            ["description"] = "Create a SERVER Entity on the eSpace in an OPEN module - LIVE tree, undo unit. Uses IESpace.CreateServerEntity(name, key). Add attributes with live_add_entity_attribute afterwards.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New entity name (e.g. ScratchOrder)") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_entity_attribute",
            ["description"] = "Add an ATTRIBUTE to a server Entity on the eSpace in an OPEN module - LIVE tree, undo unit. type is a basic type (Text, Integer, DateTime, Boolean, ...) resolved as es.<type>Type, plus entity/structure/list names. Optional isMandatory (\"true\"/\"false\") and defaultValue (best-effort).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name (e.g. ScratchOrder)"),
                    ["attrName"] = Str("attrName", "Attribute name (e.g. Title)"),
                    ["type"] = Str("type", "Type name (e.g. Text)"),
                    ["isMandatory"] = Str("isMandatory", "Optional: \"true\"/\"false\""),
                    ["defaultValue"] = Str("defaultValue", "Optional default value") },
                ["required"] = new JsonArray { "module", "entity", "attrName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_entity_attribute_type",
            ["description"] = "Set the DataType on an EXISTING entity attribute - LIVE tree, undo unit. type resolves via es.<type>Type, then es.ListTypes, es.Structures, es.Entities by Name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name"),
                    ["attrName"] = Str("attrName", "Attribute name"),
                    ["type"] = Str("type", "Type name") },
                ["required"] = new JsonArray { "module", "entity", "attrName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_set_entity_attribute_name",
            ["description"] = "Rename an EXISTING entity attribute in an OPEN module - LIVE tree, undo unit. Finds the entity by name, the attribute by its current name (attrName), then sets Name to the new value. Fails clearly if entity or attribute is not found, if newName is empty, or if newName equals the current name. The report includes the old name and a read-back of the new name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name"),
                    ["attrName"] = Str("attrName", "Current attribute name"),
                    ["newName"] = Str("newName", "New attribute name") },
                ["required"] = new JsonArray { "module", "entity", "attrName", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_delete_entity_attribute",
            ["description"] = "Delete one attribute from a server Entity in an OPEN module - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name"),
                    ["attrName"] = Str("attrName", "Attribute name to delete") },
                ["required"] = new JsonArray { "module", "entity", "attrName" } }
        },
        new JsonObject {
            ["name"] = "live_delete_entity",
            ["description"] = "Delete a whole server Entity by name in an OPEN module - LIVE tree, undo unit (Ctrl+Z restores).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "Entity name to delete") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_get_verify_errors",
            ["description"] = "Read-only validation readback for an action, screen, block, or single widget in an OPEN module. kind=widget needs screen or block. verbose=true adds a per-message property dump (to pinpoint culprits). Returns up to 50 messages; empty = clean.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["kind"] = Str("kind", "action|screen|block|widget"),
                    ["name"] = Str("name", "Element name"),
                    ["screen"] = Str("screen", "Screen scoping (for kind=widget)"),
                    ["block"] = Str("block", "Block scoping (for kind=widget)"),
                    ["verbose"] = Str("verbose", "Optional \"true\" for per-message prop dump") },
                ["required"] = new JsonArray { "module", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_widget_source",
            ["description"] = "Bind the Source list on a Table/List widget on a screen OR block in an OPEN module - LIVE tree, undo unit. Uses the typed SetSource(String) setter (CustomProperty fallback). Pass screen=<screen> OR block=<block>. source is the list expression (e.g. GetOrders.List).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["widget"] = Str("widget", "Table/List widget name"),
                    ["source"] = Str("source", "Source list expression (e.g. GetOrders.List)") },
                ["required"] = new JsonArray { "module", "widget", "source" } }
        },
        new JsonObject {
            ["name"] = "live_set_aggregate_paging",
            ["description"] = "Set MaxRecords/StartIndex (expression text) on a screen/block aggregate in an OPEN module - LIVE tree, undo unit. E.g. maxRecords=10, startIndex=(PageNumber-1)*PageSize. Pass screen=<screen> OR block=<block>.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["maxRecords"] = Str("maxRecords", "MaxRecords expression (e.g. 10 or PageSize)"),
                    ["startIndex"] = Str("startIndex", "StartIndex expression (e.g. (PageNumber-1)*PageSize)") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_sort",
            ["description"] = "Add a sort (Table.AddOrderBy) on a screen/block aggregate in an OPEN module - LIVE tree, undo unit. attr is the entity-qualified attribute (e.g. ScratchOrder.Title); ascending defaults true (pass \"false\" for DESC).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["attr"] = Str("attr", "Attribute (e.g. ScratchOrder.Title)"),
                    ["ascending"] = Str("ascending", "Optional: \"false\" for DESC") },
                ["required"] = new JsonArray { "module", "name", "attr" } }
        },
        new JsonObject {
            ["name"] = "live_remove_aggregate_sort",
            ["description"] = "Remove sort(s) from a screen/block aggregate in an OPEN module - LIVE tree, undo unit. UI RemoveDefaultSort equivalent: deletes AttributeSorts whose display matches attr (\"Entity.Attr\" or bare \"Attr\"); empty attr removes ALL sorts. Delta-verified (refuses to worsen verify count).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["attr"] = Str("attr", "Sort to remove (e.g. ScratchOrder.Title); empty = all") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_dynamic_sort",
            ["description"] = "Add a DYNAMIC sort (CombineSources.AddOrderBy(var, isDynamic:true), moved first) on a screen/block aggregate in an OPEN module - LIVE tree, undo unit. UI CreateStandardNodesForOnSort sort equivalent: varName is a screen/block Text variable holding the sort expression (e.g. TableSort, toggled \"col\" / \"col DESC\" by the OnSort handler). Delta-verified.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["varName"] = Str("varName", "Variable holding the sort expression (e.g. TableSort)") },
                ["required"] = new JsonArray { "module", "name", "varName" } }
        },
        new JsonObject {
            ["name"] = "live_set_input_param_mandatory",
            ["description"] = "Flip an action input between mandatory and optional (ActivityInput YesMandatory/YesOptional) in an OPEN module - LIVE tree, undo unit. Needed for event-handler payload inputs (e.g. OnSort SortBy), which the platform generates OPTIONAL while the CreateInputParameter factory defaults to mandatory.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["paramName"] = Str("paramName", "Input parameter name"),
                    ["mandatory"] = Str("mandatory", "\"false\" for optional, anything else for mandatory") },
                ["required"] = new JsonArray { "module", "action", "paramName", "mandatory" } }
        },
        new JsonObject {
            ["name"] = "live_set_widget_handler_arg",
            ["description"] = "Set a call-site argument value on a widget event handler in an OPEN module - LIVE tree, undo unit. Completes the platform event contract: e.g. ProbeTable OnSort -> SortBy = ClickedColumn (the table raises OnSort(ClickedColumn) and the handler action's SortBy input is fed from it).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["widget"] = Str("widget", "Widget name (e.g. ProbeTable)"),
                    ["event"] = Str("event", "Event name (e.g. OnSort)"),
                    ["argName"] = Str("argName", "Action input name (e.g. SortBy)"),
                    ["value"] = Str("value", "Value expression (e.g. ClickedColumn)") },
                ["required"] = new JsonArray { "module", "widget", "event", "argName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_add_screen_aggregate_filter",
            ["description"] = "Add a filter (Table.AddFilter) on a SCREEN aggregate in an OPEN module - LIVE tree, undo unit. Screen twin of live_add_aggregate_filter (block-side). filter e.g. 'ScratchOrder.IsDone = False'.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["filter"] = Str("filter", "Filter expression") },
                ["required"] = new JsonArray { "module", "screen", "name", "filter" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_calculated_attr",
            ["description"] = "Add a calculated attribute (CreateCalculatedAttribute + DataType) on a screen/block aggregate in an OPEN module - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["attrName"] = Str("attrName", "Calculated attribute name"),
                    ["type"] = Str("type", "Type name (e.g. Text)") },
                ["required"] = new JsonArray { "module", "name", "attrName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_add_foreach_node",
            ["description"] = "Create a ForEach (IForEachNode) loop node in an action flow with SetRecordList - LIVE tree, undo unit. Wire Target (body entry) / CycleTarget (exit) afterwards with live_set_connector_target after live_probe_node_connectors.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["recordList"] = Str("recordList", "List expression to iterate"),
                    ["maxIterations"] = Str("maxIterations", "Optional max iterations"),
                    ["startIndex"] = Str("startIndex", "Optional start index") },
                ["required"] = new JsonArray { "module", "action", "recordList" } }
        },
        new JsonObject {
            ["name"] = "live_add_screen_input_param",
            ["description"] = "Create an INPUT PARAMETER on a screen (receiving side of navigation params, e.g. OrderId) in an OPEN module - LIVE tree, undo unit. Factory-hunted on the screen/InputParameters.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["name"] = Str("name", "Input parameter name (e.g. OrderId)"),
                    ["type"] = Str("type", "Type name (e.g. Text, LongInteger)") },
                ["required"] = new JsonArray { "module", "screen", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_create_role",
            ["description"] = "Create a Role on the eSpace in an OPEN module - LIVE tree, undo unit. Uses IESpace.CreateRole(name, key). Grant on screens with live_set_screen_permissions.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New role name (e.g. ScratchAdmin)") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_permissions",
            ["description"] = "Set Public flag and/or role gating on a screen in an OPEN module - LIVE tree, undo unit. roles = comma-separated existing role names. At least one of roles/isPublic required.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["roles"] = Str("roles", "Optional comma-separated role names"),
                    ["isPublic"] = Str("isPublic", "Optional \"true\"/\"false\"") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_create_site_property",
            ["description"] = "Create a Site Property on the eSpace in an OPEN module - LIVE tree, undo unit. Uses IESpace.CreateSiteProperty(shared, name, key) + DataType. shared defaults true.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New site property name"),
                    ["type"] = Str("type", "Type name (e.g. Text, Integer, Boolean)"),
                    ["shared"] = Str("shared", "Optional \"false\" for tenant-private"),
                    ["defaultValue"] = Str("defaultValue", "Optional default value") },
                ["required"] = new JsonArray { "module", "name", "type" } }
        },
        new JsonObject {
            ["name"] = "live_create_timer",
            ["description"] = "Create a Timer on the eSpace in an OPEN module - LIVE tree, undo unit. Uses IESpace.CreateTimer(name, key). Set schedule + wake action in SS afterwards.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["name"] = Str("name", "New timer name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_fix_style_literal",
            ["description"] = "Store a widget Style class as a QUOTED text literal in the Style CustomProperty - LIVE tree, undo unit. Fixes classes stored as expressions (e.g. expr-bible parsed as expr MINUS bible). Tries strategies in order, keeps the first that reduces parent verify errors. Pass screen OR block.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["cssClass"] = Str("cssClass", "CSS class (e.g. expr-bible)") },
                ["required"] = new JsonArray { "module", "widget", "cssClass" } }
        },
        new JsonObject {
            ["name"] = "live_set_aggregate_calc_formula",
            ["description"] = "Set the formula Value on a calculated attribute of a screen/block aggregate - LIVE tree, undo unit. Tries Value/Expression/Formula/Definition + nested SetValue, keeps the first that reduces parent verify errors.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["attrName"] = Str("attrName", "Calculated attribute name"),
                    ["formula"] = Str("formula", "Formula expression") },
                ["required"] = new JsonArray { "module", "name", "attrName", "formula" } }
        },
        new JsonObject {
            ["name"] = "live_set_aggregate_param_type",
            ["description"] = "Set DataType on an implicit/query parameter of a screen/block aggregate (e.g. DataSetImplicitParameter from a filter/sort) - LIVE tree, undo unit. Searches ImplicitParameters/Parameters/Arguments by name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["paramName"] = Str("paramName", "Parameter name"),
                    ["type"] = Str("type", "Type name") },
                ["required"] = new JsonArray { "module", "name", "paramName", "type" } }
        },
        new JsonObject {
            ["name"] = "live_probe_collection",
            ["description"] = "Read-only factory discovery on any eSpace/screen/block collection. Lists Create/Add/New/Remove/Delete methods + item count + first item types. Pass screen OR block to scope, else the eSpace itself.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Optional screen scope"),
                    ["block"] = Str("block", "Optional block scope"),
                    ["collection"] = Str("collection", "Collection name (e.g. Permissions, ServiceAPIMethods)") },
                ["required"] = new JsonArray { "module", "collection" } }
        },
        new JsonObject {
            ["name"] = "live_read_aggregate_sorts",
            ["description"] = "Read-only: dump an aggregate's Sorts (stored OriginalAttribute text + direction). Use to read back a UI-created sort's exact stored form, then replicate it headlessly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_read_aggregate_calcs",
            ["description"] = "Read-only: dump an aggregate's calculated attributes (name, type, formula text). Use to read back a UI-entered formula's exact stored form.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_grant_screen_permission",
            ["description"] = "Grant a role on a screen by duplicating an existing Permission entry + repointing Role - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["roleName"] = Str("roleName", "Role name to grant") },
                ["required"] = new JsonArray { "module", "screen", "roleName" } }
        },
        new JsonObject {
            ["name"] = "live_remove_screen_permission",
            ["description"] = "Remove Permission entries for a role by name from a screen - LIVE tree, undo unit.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["roleName"] = Str("roleName", "Role name to remove") },
                ["required"] = new JsonArray { "module", "screen", "roleName" } }
        },
        new JsonObject {
            ["name"] = "live_read_screen_permissions",
            ["description"] = "Read-only: list each Permission entry's role on a screen. Use to verify grants.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name") },
                ["required"] = new JsonArray { "module", "screen" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_source",
            ["description"] = "Add an ADDITIONAL source entity to a screen/block aggregate (for joins; the first source comes from create/set-source) - LIVE tree, undo unit. Local entities only. Reports Sources count before/after.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["entityName"] = Str("entityName", "Additional source entity (local)") },
                ["required"] = new JsonArray { "module", "name", "entityName" } }
        },
        new JsonObject {
            ["name"] = "live_add_aggregate_join",
            ["description"] = "Create a Join on a screen/block aggregate (CreateJoin + Left/Right sources by index + condition) - LIVE tree, undo unit. The aggregate needs 2+ sources first (live_add_aggregate_source). Keeps the result only if verify count does not worsen.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["condition"] = Str("condition", "Join condition (e.g. ScratchOrder.Id = ScratchItem.OrderRef)"),
                    ["leftIndex"] = Str("leftIndex", "Optional left source index (default 0)"),
                    ["rightIndex"] = Str("rightIndex", "Optional right source index (default 1)") },
                ["required"] = new JsonArray { "module", "name", "condition" } }
        },
        new JsonObject {
            ["name"] = "live_debug_create_surface",
            ["description"] = "Read-only diagnostics for the service-action creation puzzle: reflection-visible ctors, folder resolution, collection concrete types.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_set_link_params",
            ["description"] = "Set navigation parameter values on a Link widget's OnClick destination (e.g. passing OrderId to a Detail screen) - LIVE tree, undo unit. params is comma-separated Name=Value pairs. The destination screen must already have the input params.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen holding the link"),
                    ["widget"] = Str("widget", "Link widget name"),
                    ["params"] = Str("params", "Comma-separated Name=Value (e.g. OrderId=123)") },
                ["required"] = new JsonArray { "module", "screen", "widget", "params" } }
        },
        new JsonObject {
            ["name"] = "live_delete_aggregate_sort",
            ["description"] = "Delete one sort by index (see live_read_aggregate_sorts) from a screen/block aggregate - LIVE tree, undo unit. Use to clean up failed sort trials.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["index"] = new JsonObject { ["type"] = "number", ["description"] = "Sort index (from live_read_aggregate_sorts)" } },
                ["required"] = new JsonArray { "module", "name", "index" } }
        },
        new JsonObject {
            ["name"] = "live_delete_aggregate_filter",
            ["description"] = "Delete one filter by index from a screen/block aggregate - LIVE tree, undo unit. Indices follow creation order. Use for dynamic search (replace filter text).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name"),
                    ["index"] = new JsonObject { ["type"] = "number", ["description"] = "Filter index (creation order)" } },
                ["required"] = new JsonArray { "module", "name", "index" } }
        },
        new JsonObject {
            ["name"] = "live_read_aggregate_params",
            ["description"] = "Read-only: dump an aggregate's implicit/query parameters (name, type, value). Use to discover the unnamed parameters sorts/filters spawn.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name (OR block - pass one)"),
                    ["block"] = Str("block", "Web block name (OR screen - pass one)"),
                    ["name"] = Str("name", "Aggregate name") },
                ["required"] = new JsonArray { "module", "name" } }
        },
        new JsonObject {
            ["name"] = "live_set_timer_action",
            ["description"] = "Point a Timer's wake Action at a server/client action in an OPEN module - LIVE tree, undo unit. Resolves the action by name (must exist).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["timer"] = Str("timer", "Timer name"),
                    ["action"] = Str("action", "Wake action name (server action)") },
                ["required"] = new JsonArray { "module", "timer", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_block_extended_property",
            ["description"] = "Set an HTML attribute on a widget INSIDE a web block in an OPEN module - LIVE tree, undo unit. Mirror of live_set_extended_property but on the block's own widget tree.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["block"] = Str("block", "Web block name"),
                    ["widget"] = Str("widget", "Widget name"),
                    ["attrName"] = Str("attrName", "Attribute name (e.g. data-testid, aria-expanded)"),
                    ["attrValue"] = Str("attrValue", "Attribute value") },
                ["required"] = new JsonArray { "module", "block", "widget", "attrName", "attrValue" } }
        },
        new JsonObject {
            ["name"] = "live_add_js_node",
            ["description"] = "Add a JavaScript node in a (client) action flow in an OPEN module - LIVE tree, undo unit. js is the JavaScript text. Optional afterNodeIndex inserts it into the flow; created standalone otherwise (wire with live_set_node_target).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Client action name"),
                    ["js"] = Str("js", "JavaScript text"),
                    ["nodeName"] = Str("nodeName", "Optional node name"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: node index to insert after" } },
                ["required"] = new JsonArray { "module", "action", "js" } }
        },
        new JsonObject {
            ["name"] = "live_add_message_node",
            ["description"] = "Add a Message/feedback node in a (client) action flow in an OPEN module - LIVE tree, undo unit. message is the message text. kind optional (Info/Success/Error). Optional afterNodeIndex inserts it into the flow.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["message"] = Str("message", "Message text"),
                    ["kind"] = Str("kind", "Optional kind (Info/Success/Error)"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: node index to insert after" } },
                ["required"] = new JsonArray { "module", "action", "message" } }
        },
        new JsonObject {
            ["name"] = "live_probe_flow_node_classes",
            ["description"] = "Type-scan loaded assemblies for REAL classes of JavaScript / Message / RaiseEvent flow node interfaces - read-only diagnostic. Use to fix add_js_node / add_message_node / add_raise_event_node.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name (used for pipe discovery)") },
                ["required"] = new JsonArray { "module" } }
        },
        new JsonObject {
            ["name"] = "live_delete_action",
            ["description"] = "Delete a MODULE-LEVEL Service/Server/Client action by name in an OPEN module - LIVE tree, undo unit. Only deletes the module-level action (searches es.ServiceActions/UserActions/ServerActions/ClientActions), never a block-scoped ClientScreenActionFlow. Use to remove module-level duplicates so flow tools resolve to the block-scoped versions.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name to delete") },
                ["required"] = new JsonArray { "module", "action" } }
        },
        new JsonObject {
            ["name"] = "live_set_action_name",
            ["description"] = "Rename an action by its current name (module-level OR block-scoped ClientScreenActionFlow) in an OPEN module - LIVE tree, undo unit. The ClientScreenActionFlow ctor auto-suffixes names (SaveProfile2) to avoid colliding with module-level actions; once the module-level duplicates are deleted, rename to the clean name so widget handlers / flow tools resolve by the spec name. Uses SetName inside a real SS command.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Current action name (e.g. SaveProfile2)"),
                    ["newName"] = Str("newName", "New action name (e.g. SaveProfile)") },
                ["required"] = new JsonArray { "module", "action", "newName" } }
        },
        new JsonObject {
            ["name"] = "live_set_raise_event_arg",
            ["description"] = "Set the value of a RaiseEvent (TriggerEvent) node's payload argument in an OPEN module - LIVE tree, undo unit. The TriggerEvent holds ISSCollection<Argument>; when the Event is bound, each input parameter of the WebBlockCustomEvent gets a matching Argument. Locates the Argument whose Parameter.Name == argName and SetValue(expr).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["nodeIndex"] = new JsonObject { ["type"] = "number", ["description"] = "Index of the TriggerEvent node (from live_list_flow)" },
                    ["argName"] = Str("argName", "Payload parameter name (e.g. UpdatedUserId)"),
                    ["value"] = Str("value", "Value expression (e.g. UserId)") },
                ["required"] = new JsonArray { "module", "action", "nodeIndex", "argName", "value" } }
        },
        new JsonObject {
            ["name"] = "live_probe_obj",
            ["description"] = "Probe the reflective API surface of a named live object (aggregate / widget / structure / client action / block event) in an OPEN module - read-only diagnostic. kind: widget|screenwidget|aggregate|structure|clientaction|blockevent. For aggregates, sub walks a dot-path into collections (e.g. 'Table.TableOperations') and dumps items + their Create*/Add* methods. Use to discover the EXACT API before editing.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["kind"] = Str("kind", "widget|screenwidget|aggregate|structure|clientaction|blockevent"),
                    ["block"] = Str("block", "Web block name (for widget/aggregate/blockevent)"),
                    ["screen"] = Str("screen", "Screen name (for screenwidget)"),
                    ["name"] = Str("name", "Object name"),
                    ["sub"] = Str("sub", "Optional dot-path into collections (aggregates only)") },
                ["required"] = new JsonArray { "module", "kind", "name" } }
        },
        new JsonObject {
            ["name"] = "live_add_lifecycle_assign",
            ["description"] = "Guarded Assign node (var = value) inside a block lifecycle child flow (OnInitialize / OnParametersChange) in an OPEN module - LIVE tree, undo unit. Deadlock-safe variant for lifecycle flows.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Lifecycle action name (e.g. OnInitialize)"),
                    ["var"] = Str("var", "Variable to assign"),
                    ["value"] = Str("value", "Value expression") },
                ["required"] = new JsonArray { "module", "action", "var", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_entity_prop",
            ["description"] = "Set a settable property on a server Entity (e.g. Public=True, ExposeReadOnly, Description, PrettyName) in an OPEN module - LIVE tree, undo unit. Bool/int/long/string values auto-converted. ExposeCreateAndChangeActions/IdentifierType are read-only (computed) and return a clean error.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name"),
                    ["prop"] = Str("prop", "Property name (e.g. Public)"),
                    ["value"] = Str("value", "New value (true/false/1/2 or text)") },
                ["required"] = new JsonArray { "module", "entity", "prop", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_structure_prop",
            ["description"] = "Set a settable property on a Structure (e.g. Public=True) in an OPEN module - LIVE tree, undo unit. Bool/int/long/string values auto-converted.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["structure"] = Str("structure", "Structure name (e.g. BattleResult)"),
                    ["prop"] = Str("prop", "Property name (e.g. Public)"),
                    ["value"] = Str("value", "New value (true/false or text)") },
                ["required"] = new JsonArray { "module", "structure", "prop", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_server_action_prop",
            ["description"] = "Set a settable property on a Server/Service Action (e.g. Public=True) in an OPEN module - LIVE tree, undo unit. Use to expose server actions for cross-module consumption.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Action name"),
                    ["prop"] = Str("prop", "Property name (e.g. Public)"),
                    ["value"] = Str("value", "New value (true/false or text)") },
                ["required"] = new JsonArray { "module", "action", "prop", "value" } }
        },
        new JsonObject {
            ["name"] = "live_set_entity_identifier",
            ["description"] = "Wire an existing attribute as the entity's Identifier (primary key) in an OPEN module - LIVE tree, undo unit. Sets entity.Identifier then RefreshEntityActions() to regenerate the auto entity actions. Use right after adding the Id attribute to a live-created entity.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name"),
                    ["attrName"] = Str("attrName", "Attribute to make the identifier (e.g. Id)") },
                ["required"] = new JsonArray { "module", "entity", "attrName" } }
        },
        new JsonObject {
            ["name"] = "live_probe_entity_actions",
            ["description"] = "List the auto-generated entity actions (CreateEntity/UpdateEntity/DeleteEntity/GetEntity/...) with their input/output parameter names and types. Read-only. Call after live_set_entity_identifier to see the regenerated actions.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["entity"] = Str("entity", "Entity name") },
                ["required"] = new JsonArray { "module", "entity" } }
        },
        new JsonObject {
            ["name"] = "live_create_entity_action_node",
            ["description"] = "Insert an ExecuteAction node that calls an ENTITY action (CreateEntity/Create, UpdateEntity/Update, DeleteEntity/Delete, GetEntity/Get, CreateOrUpdateEntity...) in an OPEN module - LIVE tree, undo unit. Auto-maps arguments by name against the flow action's input params/locals. where: beforeEnd | afterAnchor (anchorVar/anchorValue) | afterNode (afterNodeIndex).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["action"] = Str("action", "Target flow action"),
                    ["entity"] = Str("entity", "Entity whose action to call"),
                    ["entityAction"] = Str("entityAction", "Entity action (e.g. CreateEntity or Create)"),
                    ["where"] = Str("where", "beforeEnd (default) | afterAnchor | afterNode"),
                    ["anchorVar"] = Str("anchorVar", "Anchor assign var (afterAnchor)"),
                    ["anchorValue"] = Str("anchorValue", "Anchor assign value (afterAnchor)"),
                    ["afterNodeIndex"] = new JsonObject { ["type"] = "integer", ["description"] = "Node index (afterNode)" } },
                ["required"] = new JsonArray { "module", "action", "entity", "entityAction" } }
        },
        new JsonObject {
            ["name"] = "live_set_screen_handler",
            ["description"] = "Wire a SCREEN lifecycle handler (OnInitialize / OnReady / OnRender / OnDestroy) to a Client Action in an OPEN module - LIVE tree, undo unit. Screens have NO assignable lifecycle reference prop (read-only lifecycle children that own their own flow), so instead of forcing an assignment this inserts an ExecuteAction node INTO the lifecycle flow calling the named screen Client Action (screen ClientActions first, then module actions). The flow tools (live_list_flow etc.) also address the screen lifecycle flow by its name.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["handler"] = Str("handler", "Lifecycle handler name (e.g. OnReady)"),
                    ["actionName"] = Str("actionName", "Screen Client Action name to call from the lifecycle flow") },
                ["required"] = new JsonArray { "module", "screen", "handler", "actionName" } }
        },
        new JsonObject {
            ["name"] = "live_wire_button_to_screen",
            ["description"] = "Set a Button's OnClick Destination DIRECTLY to a screen (click = navigate) in an OPEN module - LIVE tree, undo unit. The Reactive screen implements IClientSideDestination, the exact type of the Button OnClick handler's Destination (verified by reflection), so the screen is assignable like an action is. Optional params maps the destination screen's inputs (comma-separated Name=Value, mirroring live_set_link_params).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen holding the button"),
                    ["button"] = Str("button", "Button widget name"),
                    ["targetScreen"] = Str("targetScreen", "Destination screen name"),
                    ["params"] = Str("params", "Optional comma-separated Name=Value for the target screen's input params (e.g. OrderId=123)") },
                ["required"] = new JsonArray { "module", "screen", "button", "targetScreen" } }
        },
        new JsonObject {
            ["name"] = "live_move_widget",
            ["description"] = "Reparent a widget on a SCREEN in an OPEN module - LIVE tree, undo unit. Uses the widget's ChangeParent API. newParent supports the add_nr_widget parent syntax: empty (screen root), container name, IfName:True / IfName:False, LayoutName:MainContent (named placeholder), Table.Row / Table.HeaderRow (dotted table content hosts).",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["module"] = Str("module", "Open module name"),
                    ["screen"] = Str("screen", "Screen name"),
                    ["widget"] = Str("widget", "Widget name to move"),
                    ["newParent"] = Str("newParent", "New parent (empty = screen root; container name; IfName:True/False; Layout:Placeholder; Table.Row / Table.HeaderRow)") },
                ["required"] = new JsonArray { "module", "screen", "widget" } }
        },
    };

    static string Dispatch(string name, JsonObject a)
    {
        string Arg(string k) => a?[k]?.ToString();
        int GetInt(string k, int dflt = 0) => a != null && a.ContainsKey(k) && a[k]?.GetValue<int>() is int v ? v : dflt;
        return name switch
        {
            "live_status" => Bridge.Status(),
            "live_list_modules" => Bridge.ListModules(),
            "live_module_info" => Bridge.ModuleInfo(Arg("module")),
            "live_create_service_action" => Bridge.CreateServiceAction(Arg("module"), Arg("name"), a?.ContainsKey("withSkeleton") == true && a["withSkeleton"]?.GetValue<bool>() == true),
            "live_create_server_action" => Bridge.CreateServerAction(Arg("module"), Arg("name")),
            "live_create_client_action" => Bridge.CreateClientAction(Arg("module"), Arg("name")),
            "live_create_screen_client_action" => Bridge.CreateScreenClientAction(Arg("module"), Arg("screen"), Arg("name")),
            "live_clone_service_action" => Bridge.CloneServiceAction(Arg("module"), Arg("sourceName"), Arg("newName")),
            "live_clone_element" => Bridge.CloneElement(Arg("module"), Arg("kind"), Arg("name"), Arg("newName")),
            "live_clone_server_action" => Bridge.CloneServerAction(Arg("module"), Arg("sourceName"), Arg("newName")),
            "live_clone_client_action" => Bridge.CloneClientAction(Arg("module"), Arg("sourceName"), Arg("newName")),
            "live_list_flow" => Bridge.ListFlow(Arg("module"), Arg("action")),
            "live_set_assign_value" => Bridge.SetAssignValue(Arg("module"), Arg("action"), Arg("matchValue"), Arg("newValue"), Arg("matchVar")),
            "live_add_output_param" => Bridge.AddOutputParam(Arg("module"), Arg("action"), Arg("name"), Arg("type")),
            "live_add_assign_node" => Bridge.AddAssignNode(Arg("module"), Arg("action"), Arg("var"), Arg("value"), Arg("where"), Arg("anchorVar"), Arg("anchorValue"), a?.ContainsKey("afterNodeIndex") == true ? a["afterNodeIndex"]?.GetValue<int>() ?? -1 : -1),
            "live_delete_node" => Bridge.DeleteNode(Arg("module"), Arg("action"), Arg("matchVar"), Arg("matchValue")),
            "live_list_consumable_elements" => Bridge.ListConsumableElements(Arg("module")),
            "live_consume_elements" => Bridge.ConsumeElements(Arg("consumer"), Arg("producer"), Arg("what")),
            "live_remove_dependency" => Bridge.RemoveDependency(Arg("module"), Arg("producer")),
            "live_publish_module" => Bridge.PublishModule(Arg("module"), Arg("commitMessage")),
            "live_debug_publish_state" => Bridge.DebugPublishState(Arg("module")),
            "live_save_module" => Bridge.SaveModule(Arg("module")),
            "live_open_producer_module" => Bridge.OpenProducerModule(Arg("module"), Arg("reference")),
            "live_clone_service_action_from" => Bridge.CloneServiceActionFrom(Arg("consumer"), Arg("producer"), Arg("source"), Arg("name")),
            "live_delete_block_client_action" => Bridge.DeleteBlockClientAction(Arg("module"), Arg("block"), Arg("name")),
            "live_remove_event_from_block" => Bridge.RemoveEventFromBlock(Arg("module"), Arg("block"), Arg("name")),
            "live_add_sql_node" => Bridge.AddSqlNode(Arg("module"), Arg("action"), Arg("sql"), a?.ContainsKey("afterNodeIndex") == true ? a["afterNodeIndex"]?.GetValue<int>() ?? -1 : -1),
            "live_create_user_exception" => Bridge.CreateUserException(Arg("module"), Arg("name")),
            "live_create_rest_client" => Bridge.CreateRestClient(Arg("module"), Arg("name"), Arg("actionName"), Arg("urlPath"), Arg("httpMethod")),
            "live_debug_object_prop_surface" => Bridge.DebugObjectPropSurface(Arg("module"), Arg("kind"), Arg("entity"), Arg("name")),
            "live_set_object_prop_deep" => Bridge.SetObjectPropDeep(Arg("module"), Arg("kind"), Arg("entity"), Arg("name"), Arg("propName"), Arg("value")),
            "live_upload_image" => Bridge.UploadImage(Arg("module"), Arg("name"), Arg("base64Data"), Arg("description")),
            "live_upload_resource" => Bridge.UploadResource(Arg("module"), Arg("name"), Arg("base64Data")),
            "live_delete_structure" => Bridge.DeleteStructure(Arg("module"), Arg("name")),
            "live_remove_unused_dependencies" => Bridge.RemoveUnusedDependencies(Arg("module")),
            "live_debug_publish_surface" => Bridge.DebugPublishSurface(Arg("module")),
            "live_add_input_param" => Bridge.AddInputParam(Arg("module"), Arg("action"), Arg("name"), Arg("type")),
            "live_add_local_variable" => Bridge.AddLocalVariable(Arg("module"), Arg("action"), Arg("name"), Arg("type")),
            "live_add_end_node" => Bridge.AddEndNode(Arg("module"), Arg("action"), Arg("where")),
            "live_set_exception_handler" => Bridge.SetExceptionHandler(Arg("module"), Arg("action"), Arg("endVar"), Arg("endValue"), Arg("handlerVar"), Arg("handlerValue")),
            "live_set_start_exception_handler" => Bridge.SetStartExceptionHandler(Arg("module"), Arg("action"), Arg("handlerVar"), Arg("handlerValue")),
            "live_probe_node_types" => Bridge.ProbeNodeTypes(Arg("module"), Arg("action")),
            "live_add_action_call_node" => Bridge.AddActionCall(Arg("module"), Arg("action"), Arg("where"), Arg("anchorVar"), Arg("anchorValue"), Arg("serverActionName"), Arg("producerModule"), a?.ContainsKey("afterNodeIndex") == true ? a["afterNodeIndex"]?.GetValue<int>() ?? -1 : -1),
            "live_add_refresh_node" => Bridge.AddRefreshNode2(Arg("module"), Arg("action"), Arg("block"), Arg("screen"), Arg("aggregateName"), Arg("where") ?? "beforeEnd"),
            "live_set_action_call" => Bridge.SetActionCall(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("serverActionName"), Arg("producerModule")),
            "live_remove_input_param" => Bridge.RemoveInputParam(Arg("module"), Arg("action"), Arg("paramName")),
            "live_remove_output_param" => Bridge.RemoveOutputParam(Arg("module"), Arg("action"), Arg("paramName")),
            "live_set_entity_prop" => Bridge.SetEntityProp(Arg("module"), Arg("entity"), Arg("prop"), Arg("value")),
            "live_set_structure_prop" => Bridge.SetStructureProp(Arg("module"), Arg("structure"), Arg("prop"), Arg("value")),
            "live_set_server_action_prop" => Bridge.SetServerActionProp(Arg("module"), Arg("action"), Arg("prop"), Arg("value")),
            "live_set_entity_identifier" => Bridge.SetEntityIdentifier(Arg("module"), Arg("entity"), Arg("attrName")),
            "live_probe_entity_actions" => Bridge.ProbeEntityActions(Arg("module"), Arg("entity")),
            "live_create_entity_action_node" => Bridge.CreateEntityActionNode(Arg("module"), Arg("action"), Arg("entity"), Arg("entityAction"), Arg("where"), Arg("anchorVar"), Arg("anchorValue"), a?.ContainsKey("afterNodeIndex") == true ? a["afterNodeIndex"]?.GetValue<int>() ?? -1 : -1),
            "live_add_entity_input" => Bridge.AddEntityInput(Arg("module"), Arg("action"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_entity_identifier_input" => Bridge.AddEntityIdentifierInput(Arg("module"), Arg("action"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_set_output_param_type" => Bridge.SetOutputParamType(Arg("module"), Arg("action"), Arg("paramName"), Arg("typeName"), Arg("producerModule")),
            "live_set_input_param_type" => Bridge.SetInputParamType(Arg("module"), Arg("action"), Arg("paramName"), Arg("typeName"), Arg("producerModule"), Arg("entityName")),
            "live_add_entity_output" => Bridge.AddEntityOutput(Arg("module"), Arg("action"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_delete_node_by_index" => Bridge.DeleteNodeByIndex(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0),
            "live_set_node_target" => Bridge.SetNodeTarget(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, a?["targetIndex"]?.GetValue<int>() ?? 0),
            "live_set_node_property" => Bridge.SetNodeProp(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("propName"), Arg("propValue")),
            "live_debug_create_node" => Bridge.DebugCreateNode(Arg("module"), Arg("action"), Arg("nodeInterface")),
            "live_delete_service_action" => Bridge.DeleteServiceAction(Arg("module"), Arg("action")),
            "live_add_assignment_to_node" => Bridge.AddAssignmentToNode(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("var"), Arg("value")),
            "live_remove_assignment" => Bridge.RemoveAssignment(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("var")),
            "live_set_error_handler_exception" => Bridge.SetErrorHandlerException(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("exceptionName")),
            "live_map_action_inputs" => Bridge.MapActionInputs(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("inputParamName")),
            "live_set_action_arg" => Bridge.SetActionArg(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("argName"), Arg("value")),
            "live_debug_node_props" => Bridge.DebugNodeProps(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0),
            "live_debug_action_args" => Bridge.DebugActionArgs(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0),
            "live_layout_flow" => Bridge.LayoutFlow(Arg("module"), Arg("action")),
            "live_get_node_positions" => Bridge.GetNodePositions(Arg("module"), Arg("action")),
            "live_set_node_position" => Bridge.SetNodePosition(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, a?["x"]?.GetValue<double>() ?? 0, a?["y"]?.GetValue<double>() ?? 0),
            "live_debug_eSpace_collections" => Bridge.DebugESpaceCollections(Arg("module")),
            "live_debug_eSpace_collection_items" => Bridge.DebugESpaceCollectionItems(Arg("module"), Arg("collection")),
            "live_create_folder" => Bridge.CreateFolder(Arg("module"), Arg("folderName"), Arg("parentFolder")),
            "live_move_to_folder" => Bridge.MoveToFolder(Arg("module"), Arg("action"), Arg("folderName")),
            "live_create_web_flow" => Bridge.CreateFlowScratch(Arg("module"), Arg("name")),
            "live_create_web_screen" => Bridge.CreateWebScreen(Arg("module"), Arg("webFlow"), Arg("name")),
            "live_create_web_block" => Bridge.CreateWebBlock(Arg("module"), Arg("flow"), Arg("name")),
            "live_list_web_flows" => Bridge.ListWebFlows(Arg("module")),
            "live_add_container" => Bridge.AddContainer(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("styleClass")),
            "live_add_expression" => Bridge.AddExpression(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("value"), Arg("styleClass")),
            "live_add_text" => Bridge.AddText(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("text"), Arg("styleClass")),
            "live_add_link" => Bridge.AddLink(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("text"), Arg("targetScreen"), Arg("styleClass")),
            "live_add_button" => Bridge.AddButton(Arg("module"), Arg("screen"), Arg("placeholder"), Arg("parent"), Arg("name"), Arg("text"), Arg("styleClass")),
            "live_set_button_onclick" => Bridge.SetButtonOnClick(Arg("module"), Arg("screen"), Arg("button"), Arg("actionName")),
            "live_wire_button_to_screen" => Bridge.WireButtonToScreen(Arg("module"), Arg("screen"), Arg("button"), Arg("targetScreen"), Arg("params")),
            "live_set_style_class" => Bridge.SetStyleClass(Arg("module"), Arg("screen"), Arg("widget"), Arg("styleClass")),
            "live_set_module_css" => Bridge.SetUserCss(Arg("module"), Arg("css")),
            "live_set_user_css" => Bridge.SetUserCss(Arg("module"), Arg("css")),
            "live_probe_style_prop" => Bridge.ProbeStyleProp(Arg("module"), Arg("screen"), Arg("widget")),
            "live_read_theme_css" => Bridge.ReadThemeCss(Arg("module")),
            "live_probe_sheet" => Bridge.ProbeSheet(Arg("module")),
            "live_set_screen_title" => Bridge.SetScreenTitle(Arg("module"), Arg("screen"), Arg("title")),
            "live_list_widgets" => Bridge.ListWidgets(Arg("module"), Arg("screen")),
            "live_create_theme" => Bridge.CreateTheme(Arg("module"), Arg("sourceName"), Arg("newName"), Arg("css")),
            "live_set_theme_css" => Bridge.SetThemeCss(Arg("module"), Arg("themeName"), Arg("css")),
            "live_set_screen_theme" => Bridge.SetScreenTheme(Arg("module"), Arg("screen"), Arg("themeName")),
            "live_set_flow_theme" => Bridge.SetFlowTheme(Arg("module"), Arg("flow"), Arg("themeName")),
            "live_clone_web_block" => Bridge.CloneWebBlock(Arg("module"), Arg("sourceName"), Arg("newName")),
            "live_move_web_block_to_flow" => Bridge.MoveWebBlockToFlow(Arg("module"), Arg("block"), Arg("targetFlow")),
            "live_set_theme_layout" => Bridge.SetThemeLayout(Arg("module"), Arg("themeName"), Arg("layoutBlock"), Arg("menuBlock")),
            "live_probe_theme" => Bridge.ProbeTheme(Arg("module"), Arg("themeName")),
            "live_add_html_element" => Bridge.AddHtmlElement(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("tag"), Arg("styleClass")),
            "live_add_to_placeholder" => Bridge.AddToPlaceholder(Arg("module"), Arg("screen"), Arg("placeholder"), Arg("kind"), Arg("name"), Arg("text"), Arg("targetScreen"), Arg("styleClass")),
            "live_add_inside_placeholder" => Bridge.AddInsidePlaceholder(Arg("module"), Arg("screen"), Arg("placeholder"), Arg("parent"), Arg("kind"), Arg("name"), Arg("text"), Arg("targetScreen"), Arg("styleClass")),
            "live_add_link_to_block" => Bridge.AddLinkToBlock(Arg("module"), Arg("block"), Arg("parent"), Arg("name"), Arg("text"), Arg("targetScreen"), Arg("styleClass")),
            "live_add_placeholder_to_block" => Bridge.AddPlaceholderToBlock(Arg("module"), Arg("block"), Arg("parent"), Arg("name")),
            "live_add_widget_to_block" => Bridge.AddWidgetToBlock(Arg("module"), Arg("block"), Arg("parent"), Arg("kind"), Arg("name"), Arg("value"), Arg("styleClass")),
            "live_delete_widget_from_block" => Bridge.DeleteWidgetFromBlock(Arg("module"), Arg("block"), Arg("name")),
            "live_dump_block_widget_types" => Bridge.DumpBlockWidgetTypes(Arg("module"), Arg("block")),
            "live_add_variable_to_block" => Bridge.AddVariableToBlock(Arg("module"), Arg("block"), Arg("name")),
            "live_add_screen_variable" => Bridge.AddVariableToScreen(Arg("module"), Arg("screen"), Arg("name"), Arg("type"), Arg("defaultValue")),
            "live_set_block_widget_property" => Bridge.SetBlockWidgetProperty(Arg("module"), Arg("block"), Arg("widgetName"), Arg("propName"), Arg("propValue")),
            "live_list_placeholders" => Bridge.ListPlaceholders(Arg("module"), Arg("screen")),
            "live_delete_from_placeholder" => Bridge.DeleteFromPlaceholder(Arg("module"), Arg("screen"), Arg("placeholder"), Arg("name")),
            "live_add_button_to_block" => Bridge.AddButtonToBlock(Arg("module"), Arg("block"), Arg("parent"), Arg("name"), Arg("text"), Arg("styleClass")),
            "live_set_block_button_onclick" => Bridge.SetBlockButtonOnClick(Arg("module"), Arg("block"), Arg("button"), Arg("actionName")),
            "live_list_block_input_params" => Bridge.ListBlockInputParams(Arg("module"), Arg("block")),
            "live_add_input_param_to_block" => Bridge.AddInputParamToBlock(Arg("module"), Arg("block"), Arg("name"), Arg("type")),
            "live_set_block_input_param_type" => Bridge.SetBlockInputParamType(Arg("module"), Arg("block"), Arg("paramName"), Arg("typeName"), Arg("producerModule"), Arg("entityName")),
            "live_remove_input_param_from_block" => Bridge.RemoveInputParamFromBlock(Arg("module"), Arg("block"), Arg("paramName")),
            "live_remove_screen_input_param" => Bridge.RemoveScreenInputParam(Arg("module"), Arg("screen"), Arg("paramName")),
            "live_add_entity_input_to_block" => Bridge.AddEntityInputToBlock(Arg("module"), Arg("block"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_entity_identifier_input_to_block" => Bridge.AddEntityIdentifierInputToBlock(Arg("module"), Arg("block"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_if_node" => Bridge.AddIfNode(Arg("module"), Arg("action"), Arg("condition"), GetInt("afterNodeIndex", -1)),
            "live_add_switch_node" => Bridge.AddSwitchNode(Arg("module"), Arg("action"), GetInt("afterNodeIndex", -1)),
            "live_probe_node_connectors" => Bridge.ProbeNodeConnectors(Arg("module"), Arg("action"), GetInt("nodeIndex", 0)),
            "live_probe_node_conditions" => Bridge.ProbeNodeConditions(Arg("module"), Arg("action"), GetInt("nodeIndex", 0)),
            "live_set_switch_case" => Bridge.SetSwitchCase(Arg("module"), Arg("action"), GetInt("nodeIndex", 0), Arg("value"), GetInt("targetIndex", -1)),
            "live_set_connector_target" => Bridge.SetConnectorTarget(Arg("module"), Arg("action"), GetInt("nodeIndex", 0), Arg("propName"), GetInt("targetIndex", 0)),
            "live_probe_block_members" => Bridge.ProbeBlockMembers(Arg("module"), Arg("block")),
            "live_add_event_to_block" => Bridge.AddEventToBlock(Arg("module"), Arg("block"), Arg("name")),
            "live_add_event_param_to_block" => Bridge.AddEventParamToBlock(Arg("module"), Arg("block"), Arg("event"), Arg("name"), Arg("type")),
            "live_set_block_handler" => Bridge.SetBlockHandler(Arg("module"), Arg("block"), Arg("handler"), Arg("actionName")),
            "live_set_screen_handler" => Bridge.SetScreenHandler(Arg("module"), Arg("screen"), Arg("handler"), Arg("actionName")),
            "live_add_raise_event_node" => Bridge.AddRaiseEventNode(Arg("module"), Arg("action"), Arg("eventName"), GetInt("afterNodeIndex", -1)),
            "live_set_extended_property" => Bridge.SetHtmlAttr(Arg("module"), Arg("screen"), Arg("widget"), Arg("attrName"), Arg("attrValue")),
            "live_set_screen_layout" => Bridge.SetScreenLayout(Arg("module"), Arg("screen"), Arg("layoutBlock")),
            "live_delete_web_flow" => Bridge.DeleteWebFlow(Arg("module"), Arg("flow")),
            "live_delete_screen_from_flow" => Bridge.DeleteScreenFromFlow(Arg("module"), Arg("flow"), Arg("screen")),
            "live_delete_widget" => Bridge.DeleteWidget(Arg("module"), Arg("screen"), Arg("name")),
            "live_delete_layout" => Bridge.DeleteLayout(Arg("module"), Arg("screen")),
            "live_add_html_text" => Bridge.AddHtmlText(Arg("module"), Arg("screen"), Arg("parent"), Arg("name"), Arg("tag"), Arg("styleClass"), Arg("text")),
            "live_probe_widget_deep" => Bridge.ProbeWidgetDeep(Arg("module"), Arg("screen"), Arg("widgetName")),
            "live_probe_layout_ref" => Bridge.ProbeLayoutRef(Arg("module"), Arg("screen")),
            "live_read_user_css_text" => Bridge.ReadUserCssText(Arg("module"), Arg("themeName")),
            "live_probe_block_widget" => Bridge.ProbeBlockWidget(Arg("module"), Arg("block"), Arg("widget")),
            "live_dump_widget_concretes" => Bridge.DumpWidgetConcretes(),
            "live_probe_data_sources" => Bridge.ProbeDataSources(Arg("module"), Arg("block"), Arg("screen")),
            "live_add_aggregate_to_block" => Bridge.AddAggregateToBlock(Arg("module"), Arg("block"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_data_action_to_block" => Bridge.AddDataActionToBlock(Arg("module"), Arg("block"), Arg("name")),
            "live_add_aggregate_to_screen" => Bridge.AddAggregateToScreen(Arg("module"), Arg("screen"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_data_action_to_screen" => Bridge.AddDataActionToScreen(Arg("module"), Arg("screen"), Arg("name")),
            "live_delete_data_action" => Bridge.DeleteDataAction(Arg("module"), Arg("block"), Arg("screen"), Arg("name")),
            "live_delete_aggregate" => Bridge.DeleteAggregate(Arg("module"), Arg("block"), Arg("screen"), Arg("name")),
            "live_probe_widget_kinds" => Bridge.ProbeWidgetKinds(Arg("module")),
            "live_add_nr_widget" => Bridge.AddNRWidget(Arg("module"), Arg("screen"), Arg("block"), Arg("parent"), Arg("kind"), Arg("name"), Arg("styleClass"), Arg("text")),
            "live_set_widget_handler" => Bridge.SetWidgetHandler(Arg("module"), Arg("screen"), Arg("block"), Arg("widget"), Arg("event"), Arg("actionName")),
            "live_set_element_description" => Bridge.SetElementDescription(Arg("module"), Arg("kind"), Arg("name"), Arg("description")),
            "live_set_block_variable_type" => Bridge.SetBlockVariableType(Arg("module"), Arg("block"), Arg("var"), Arg("type"), Arg("default"), Arg("producerModule"), a?["identifier"]?.GetValue<bool>() ?? false, Arg("entityName")),
            "live_set_block_cp_text" => Bridge.SetBlockCpText(Arg("module"), Arg("block"), Arg("widget"), Arg("propName"), Arg("value")),
            "live_set_screen_cp_text" => Bridge.SetScreenCpText(Arg("module"), Arg("screen"), Arg("widget"), Arg("propName"), Arg("value")),
            "live_delete_screen_client_action" => Bridge.DeleteScreenClientAction(Arg("module"), Arg("screen"), Arg("action")),
            "live_set_block_cp_image" => Bridge.SetBlockCpImage(Arg("module"), Arg("block"), Arg("widget"), Arg("propName"), Arg("imageName")),
            "live_delete_anon_block_widgets" => Bridge.DeleteAnonBlockWidgets(Arg("module"), Arg("block"), Arg("typeContains"), a?["recursive"]?.GetValue<bool>() ?? false),
            "live_probe_block_cp" => Bridge.ProbeBlockCp(Arg("module"), Arg("block"), Arg("widget"), Arg("propName")),
            "live_probe_type" => Bridge.ProbeType(Arg("typeName")),
            "live_probe_references" => Bridge.ProbeReferences(Arg("module")),
            "live_set_block_aggregate_source" => Bridge.SetBlockAggregateSource(Arg("module"), Arg("block"), Arg("name"), Arg("entityName"), Arg("producerModule")),
            "live_add_aggregate_filter" => Bridge.SetBlockAggregateFilter(Arg("module"), Arg("block"), Arg("name"), Arg("filter")),
            "live_add_if_widget_to_block" => Bridge.AddIfWidgetToBlock(Arg("module"), Arg("block"), Arg("parent"), Arg("name"), Arg("condition")),
            "live_move_widget_in_block" => Bridge.MoveWidgetInBlock(Arg("module"), Arg("block"), Arg("widget"), Arg("newParent")),
            "live_move_widget" => Bridge.MoveWidget(Arg("module"), Arg("screen"), Arg("widget"), Arg("newParent")),
            "live_set_block_cp" => Bridge.SetBlockCpExpression(Arg("module"), Arg("block"), Arg("widget"), Arg("propName"), Arg("value")),
            "live_set_block_cp_parsed" => Bridge.SetBlockCpParsed(Arg("module"), Arg("block"), Arg("widget"), Arg("propName"), Arg("value")),
            "live_set_screen_cp_parsed" => Bridge.SetScreenCpParsed(Arg("module"), Arg("screen"), Arg("widget"), Arg("propName"), Arg("value")),
            "live_create_block_client_action" => Bridge.CreateBlockClientAction(Arg("module"), Arg("block"), Arg("name")),
            "live_create_structure" => Bridge.CreateStructure(Arg("module"), Arg("name")),
            "live_add_structure_attribute" => Bridge.AddStructureAttribute(Arg("module"), Arg("structure"), Arg("attrName"), Arg("type")),
            "live_set_structure_attribute_type" => Bridge.SetStructureAttributeType(Arg("module"), Arg("structure"), Arg("attrName"), Arg("type")),
            "live_create_entity" => Bridge.CreateEntity(Arg("module"), Arg("name")),
            "live_add_entity_attribute" => Bridge.AddEntityAttribute(Arg("module"), Arg("entity"), Arg("attrName"), Arg("type"), Arg("isMandatory"), Arg("defaultValue")),
            "live_set_entity_attribute_type" => Bridge.SetEntityAttributeType(Arg("module"), Arg("entity"), Arg("attrName"), Arg("type")),
            "live_set_entity_attribute_name" => Bridge.SetEntityAttributeName(Arg("module"), Arg("entity"), Arg("attrName"), Arg("newName")),
            "live_delete_entity_attribute" => Bridge.DeleteEntityAttribute(Arg("module"), Arg("entity"), Arg("attrName")),
            "live_delete_entity" => Bridge.DeleteEntity(Arg("module"), Arg("name")),
            "live_get_verify_errors" => Bridge.GetVerifyErrors(Arg("module"), Arg("kind"), Arg("name"), Arg("screen"), Arg("block"), Arg("verbose")),
            "live_set_widget_source" => Bridge.SetWidgetSource(Arg("module"), Arg("screen"), Arg("block"), Arg("widget"), Arg("source")),
            "live_set_aggregate_paging" => Bridge.SetAggregatePaging(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("maxRecords"), Arg("startIndex")),
            "live_add_aggregate_sort" => Bridge.AddAggregateSort(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("attr"), Arg("ascending")),
            "live_remove_aggregate_sort" => Bridge.RemoveAggregateSort(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("attr")),
            "live_add_aggregate_dynamic_sort" => Bridge.AddAggregateDynamicSort(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("varName")),
            "live_set_input_param_mandatory" => Bridge.SetInputParamMandatory(Arg("module"), Arg("action"), Arg("paramName"), Arg("mandatory")),
            "live_set_widget_handler_arg" => Bridge.SetWidgetHandlerArg(Arg("module"), Arg("screen"), Arg("block"), Arg("widget"), Arg("event"), Arg("argName"), Arg("value")),
            "live_add_screen_aggregate_filter" => Bridge.AddScreenAggregateFilter(Arg("module"), Arg("screen"), Arg("name"), Arg("filter")),
            "live_add_aggregate_calculated_attr" => Bridge.AddAggregateCalculatedAttr(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("attrName"), Arg("type")),
            "live_add_foreach_node" => Bridge.AddForeachNode(Arg("module"), Arg("action"), Arg("recordList"), Arg("maxIterations"), Arg("startIndex")),
            "live_add_screen_input_param" => Bridge.AddScreenInputParam(Arg("module"), Arg("screen"), Arg("name"), Arg("type")),
            "live_create_role" => Bridge.CreateRole(Arg("module"), Arg("name")),
            "live_set_screen_permissions" => Bridge.SetScreenPermissions(Arg("module"), Arg("screen"), Arg("roles"), Arg("isPublic")),
            "live_create_site_property" => Bridge.CreateSiteProperty(Arg("module"), Arg("name"), Arg("type"), Arg("shared"), Arg("defaultValue")),
            "live_create_timer" => Bridge.CreateTimer(Arg("module"), Arg("name")),
            "live_fix_style_literal" => Bridge.FixStyleLiteral(Arg("module"), Arg("screen"), Arg("block"), Arg("widget"), Arg("cssClass")),
            "live_set_aggregate_calc_formula" => Bridge.SetAggregateCalcFormula(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("attrName"), Arg("formula")),
            "live_set_aggregate_param_type" => Bridge.SetAggregateParamType(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("paramName"), Arg("type")),
            "live_probe_collection" => Bridge.ProbeCollection(Arg("module"), Arg("screen"), Arg("block"), Arg("collection")),
            "live_read_aggregate_sorts" => Bridge.ReadAggregateSorts(Arg("module"), Arg("screen"), Arg("block"), Arg("name")),
            "live_read_aggregate_calcs" => Bridge.ReadAggregateCalcs(Arg("module"), Arg("screen"), Arg("block"), Arg("name")),
            "live_read_aggregate_params" => Bridge.ReadAggregateParams(Arg("module"), Arg("screen"), Arg("block"), Arg("name")),
            "live_delete_aggregate_sort" => Bridge.DeleteAggregateSort(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), GetInt("index", -1)),
            "live_delete_aggregate_filter" => Bridge.DeleteAggregateFilter(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), GetInt("index", -1)),
            "live_grant_screen_permission" => Bridge.GrantScreenPermission(Arg("module"), Arg("screen"), Arg("roleName")),
            "live_remove_screen_permission" => Bridge.RemoveScreenPermission(Arg("module"), Arg("screen"), Arg("roleName")),
            "live_read_screen_permissions" => Bridge.ReadScreenPermissions(Arg("module"), Arg("screen")),
            "live_add_aggregate_source" => Bridge.AddAggregateSource(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("entityName")),
            "live_add_aggregate_join" => Bridge.AddAggregateJoin(Arg("module"), Arg("screen"), Arg("block"), Arg("name"), Arg("condition"), Arg("leftIndex"), Arg("rightIndex")),
            "live_set_link_params" => Bridge.SetLinkParams(Arg("module"), Arg("screen"), Arg("widget"), Arg("params")),
            "live_debug_create_surface" => Bridge.DebugCreateSurface(Arg("module")),
            "live_set_timer_action" => Bridge.SetTimerAction(Arg("module"), Arg("timer"), Arg("action")),
            "live_set_block_extended_property" => Bridge.SetBlockHtmlAttr(Arg("module"), Arg("block"), Arg("widget"), Arg("attrName"), Arg("attrValue")),
            "live_add_js_node" => Bridge.AddJsNode(Arg("module"), Arg("action"), Arg("js"), Arg("nodeName"), GetInt("afterNodeIndex", -1)),
            "live_add_message_node" => Bridge.AddMessageNode(Arg("module"), Arg("action"), Arg("message"), Arg("kind"), GetInt("afterNodeIndex", -1)),
            "live_probe_flow_node_classes" => Bridge.ProbeFlowNodeClasses(Arg("module")),
            "live_set_raise_event_arg" => Bridge.SetRaiseEventArg(Arg("module"), Arg("action"), a?["nodeIndex"]?.GetValue<int>() ?? 0, Arg("argName"), Arg("value")),
            "live_set_action_name" => Bridge.SetActionName(Arg("module"), Arg("action"), Arg("newName")),
            "live_delete_action" => Bridge.DeleteAction(Arg("module"), Arg("action")),
            "live_probe_obj" => Bridge.ProbeObj(Arg("module"), Arg("kind"), Arg("block"), Arg("screen"), Arg("name"), Arg("sub")),
            "live_add_lifecycle_assign" => Bridge.AddLifecycleAssign(Arg("module"), Arg("action"), Arg("var"), Arg("value")),
            _ => "ERROR: unknown tool: " + name
        };
    }
}

// Thin forwarder to the OsLiveBridge named-pipe server(s) running inside SS.
// SS uses a multi-process architecture; the bridge loads in each process. The
// "main IDE" process is the one whose list_modules is non-empty (it holds the
// open modules). We auto-discover the right pipe per module.
internal static class Bridge
{
    const string PipePrefix = "OsLiveBridge-";
    static readonly Encoding Enc = new UTF8Encoding(false);
    // module -> pid cache (the SS pid whose bridge holds that module)
    static readonly Dictionary<string, int> _pidForModule = new();

    // Discover Service Studio process ids. The bridge pipe is named
    // OsLiveBridge-<SSpid>, so the SS pids ARE the pipe pids. Using the process
    // list is far more reliable than enumerating \\.\pipe\ (which is flaky and
    // misses pipes during the bridge's per-connection recreation window).
    static List<int> DiscoverPids()
    {
        var pids = new List<int>();
        foreach (var proc in Process.GetProcessesByName("ServiceStudio"))
        {
            try { pids.Add(proc.Id); } finally { try { proc.Dispose(); } catch { } }
        }
        return pids;
    }

    // Send one JSON request line to the bridge on pid; read one response line.
    // Uses the raw stream (not StreamReader/StreamWriter) so disposing the client
    // can't throw and mask the already-read response.
    static string Send(int pid, string cmdJson, int timeoutMs = 8000)
    {
        using var client = new NamedPipeClientStream(".", PipePrefix + pid, PipeDirection.InOut);
        client.Connect(timeoutMs);
        var bytes = Enc.GetBytes(cmdJson + "\n");
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
        var ms = new MemoryStream();
        int b;
        while ((b = client.ReadByte()) != -1)
        {
            if (b == '\n') break;
            ms.WriteByte((byte)b);
        }
        var resp = Enc.GetString(ms.ToArray()).TrimEnd('\r');
        return resp.Length == 0 ? "{\"ok\":false,\"error\":\"empty response from bridge\"}" : resp;
    }

    // Find the SS pid whose bridge holds the given module (list_modules contains it).
    static int FindPidForModule(string module)
    {
        if (module != null && _pidForModule.TryGetValue(module, out var cached))
        {
            // verify still valid
            try
            {
                var resp = Send(cached, "{\"cmd\":\"list_modules\"}", 2000);
                if (HasModule(resp, module)) return cached;
            }
            catch { }
            _pidForModule.Remove(module);
        }
        foreach (var pid in DiscoverPids())
        {
            try
            {
                var resp = Send(pid, "{\"cmd\":\"list_modules\"}", 2000);
                if (HasModule(resp, module)) { _pidForModule[module] = pid; return pid; }
            }
            catch { }
        }
        return -1;
    }

    static bool HasModule(string listResp, string module)
    {
        try
        {
            var j = JsonNode.Parse(listResp);
            if (j?["ok"]?.GetValue<bool>() != true) return false;
            var mods = j["modules"]?.AsArray();
            if (mods == null) return false;
            return mods.Any(m => m?.GetValue<string>() == module);
        }
        catch { return false; }
    }

    static string NoBridgeError() =>
        "ERROR: no OsLiveBridge pipe found. Start Service Studio (with the OsLiveBridge plugin installed in Plugins\\ServiceStudio\\) and open the module, then retry. Pipes are named OsLiveBridge-<SSpid>.";

    static string RequireModulePipe(string module)
    {
        if (string.IsNullOrWhiteSpace(module)) return "ERROR: module is required";
        var pid = FindPidForModule(module);
        if (pid < 0) return $"ERROR: module '{module}' is not open in any Service Studio bridge. Open it in SS (with the OsLiveBridge plugin loaded) and retry.";
        return null; // ok
    }

    public static string Status()
    {
        var pids = DiscoverPids();
        if (pids.Count == 0) return NoBridgeError();
        var sb = new StringBuilder();
        sb.AppendLine("OsLiveBridge pipes found: " + pids.Count);
        foreach (var pid in pids)
        {
            string ping, list;
            try { ping = Send(pid, "{\"cmd\":\"ping\"}", 2000); }
            catch (Exception e) { sb.AppendLine($"  PID {pid}: ping FAILED ({e.GetType().Name})"); continue; }
            try { list = Send(pid, "{\"cmd\":\"list_modules\"}", 2000); }
            catch (Exception e) { sb.AppendLine($"  PID {pid}: ping OK but list_modules FAILED ({e.GetType().Name})"); continue; }
            try
            {
                var lj = JsonNode.Parse(list);
                var mods = lj?["modules"]?.AsArray();
                var names = mods != null ? string.Join(", ", mods.Select(m => m?.GetValue<string>())) : "(none)";
                var count = lj?["count"]?.GetValue<int>() ?? 0;
                sb.AppendLine($"  PID {pid}: bridge OK, {count} module(s) open: {names}");
            }
            catch { sb.AppendLine($"  PID {pid}: ping OK, list_modules unparseable: {list}"); }
        }
        sb.AppendLine("\nUse live_list_modules / live_module_info / live_create_service_action with an open module name.");
        return sb.ToString();
    }

    public static string ListModules()
    {
        var pids = DiscoverPids();
        if (pids.Count == 0) return NoBridgeError();
        var all = new List<string>();
        foreach (var pid in pids)
        {
            try
            {
                var resp = Send(pid, "{\"cmd\":\"list_modules\"}", 2000);
                var j = JsonNode.Parse(resp);
                if (j?["ok"]?.GetValue<bool>() == true)
                {
                    var mods = j["modules"]?.AsArray();
                    if (mods != null)
                        foreach (var m in mods)
                        {
                            var nm = m?.GetValue<string>();
                            if (nm != null) { _pidForModule[nm] = pid; all.Add(nm + "  (SS pid " + pid + ")"); }
                        }
                }
            }
            catch { }
        }
        if (all.Count == 0) return "No modules currently open in Service Studio. Open a module in SS and retry.";
        return "Open modules (live):\n  " + string.Join("\n  ", all);
    }

    public static string ModuleInfo(string module)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"module_info\",\"module\":\"" + Escape(module) + "\"}");
    }

    public static string CreateServiceAction(string module, string name, bool withSkeleton = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        // v7 = Command.ExecuteFromAsyncCode on the bridge pipe thread (synchronous,
        // proven). Opens a real SS command; the new action is an undo unit (Ctrl+Z).
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"try_create_v7\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\",\"withSkeleton\":" + (withSkeleton ? "true" : "false") + "}", 30000);
        // surface a concise summary + the raw bridge json
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var before = j?["serviceActionsBefore"]?.GetValue<int>();
            var after = j?["serviceActionsAfter"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: created '{name}' ({created}) in live module '{module}' via {via}. Service actions: {before} -> {after}. It is a real undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CreateServerAction(string module, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"try_create_server_action\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var before = j?["serverActionsBefore"]?.GetValue<int>();
            var after = j?["serverActionsAfter"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: created '{name}' ({created}) in live module '{module}' via {via}. Server actions: {before} -> {after}. It is a real undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CreateClientAction(string module, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"try_create_client_action\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var before = j?["clientActionsBefore"]?.GetValue<int>();
            var after = j?["clientActionsAfter"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: created '{name}' ({created}) in live module '{module}' via {via}. Client actions: {before} -> {after}. It is a real undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CreateScreenClientAction(string module, string screen, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"try_create_screen_client_action\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var before = j?["clientActionsBefore"]?.GetValue<int>();
            var after = j?["clientActionsAfter"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: created screen client action '{name}' ({created}) on screen '{screen}' in live module '{module}' via {via}. Client actions: {before} -> {after}. It is a real undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CloneServiceAction(string module, string sourceName, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return "ERROR: sourceName is required";
        if (string.IsNullOrWhiteSpace(newName)) return "ERROR: newName is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        // v8 = IModelServices.Duplicate(source, es) inside Command.ExecuteFromAsyncCode,
        // then rename. Exact clone (params/flow/metadata) with a fresh key. Undo unit (Ctrl+Z).
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"try_clone_v8\",\"module\":\"" + Escape(module) + "\",\"source\":\"" + Escape(sourceName) + "\",\"name\":\"" + Escape(newName) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var createdName = j?["createdName"]?.GetValue<string>();
            var before = j?["serviceActionsBefore"]?.GetValue<int>();
            var after = j?["serviceActionsAfter"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: cloned '{sourceName}' -> '{createdName}' ({created}) in live module '{module}' via {via}. Service actions: {before} -> {after}. Exact copy; undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CloneElement(string module, string kind, string name, string newName)
    {
        if (string.IsNullOrWhiteSpace(kind)) return "ERROR: kind is required";
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        if (string.IsNullOrWhiteSpace(newName)) return "ERROR: newName is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"clone_element\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind) + "\",\"name\":\"" + Escape(name) + "\",\"newName\":\"" + Escape(newName) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var createdName = j?["createdName"]?.GetValue<string>();
            var before = j?["before"]?.GetValue<int>();
            var after = j?["after"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: cloned {kind} '{name}' -> '{createdName}' ({created}) in live module '{module}' via {via}. Count: {before} -> {after}. Undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string DeleteBlockClientAction(string module, string block, string name)
    {
        if (string.IsNullOrWhiteSpace(block)) return "ERROR: block is required";
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"delete_block_client_action\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block) + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var report = j?["report"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true ? $"OK: {report}. Undo unit (Ctrl+Z in SS restores it)." : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string RemoveEventFromBlock(string module, string block, string name)
    {
        if (string.IsNullOrWhiteSpace(block)) return "ERROR: block is required";
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"remove_event_from_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block) + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var report = j?["report"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true ? $"OK: {report}. Undo unit (Ctrl+Z in SS restores it)." : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string AddSqlNode(string module, string action, string sql, int afterNodeIndex)
    {
        if (string.IsNullOrWhiteSpace(action)) return "ERROR: action is required";
        if (string.IsNullOrWhiteSpace(sql)) return "ERROR: sql is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"add_sql_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"sql\":\"" + Escape(sql) + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var report = j?["report"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true ? $"OK: {report}" : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CreateUserException(string module, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"create_user_exception\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var report = j?["report"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true ? $"OK: {report}. Undo unit (Ctrl+Z in SS removes it)." : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CreateRestClient(string module, string name, string actionName, string urlPath, string httpMethod)
    {
        if (string.IsNullOrWhiteSpace(name)) return "ERROR: name is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"create_rest_client\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\",\"urlPath\":\"" + Escape(urlPath ?? "") + "\",\"httpMethod\":\"" + Escape(httpMethod ?? "") + "\"}", 60000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var report = j?["report"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true ? $"OK: {report}" : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CloneServerAction(string module, string sourceName, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return "ERROR: sourceName is required";
        if (string.IsNullOrWhiteSpace(newName)) return "ERROR: newName is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"clone_server_action\",\"module\":\"" + Escape(module) + "\",\"source\":\"" + Escape(sourceName) + "\",\"name\":\"" + Escape(newName) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var createdName = j?["createdName"]?.GetValue<string>();
            var before = j?["before"]?.GetValue<int>();
            var after = j?["after"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: cloned server action '{sourceName}' -> '{createdName}' ({created}) in live module '{module}' via {via}. Server actions: {before} -> {after}. Exact copy; undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CloneClientAction(string module, string sourceName, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return "ERROR: sourceName is required";
        if (string.IsNullOrWhiteSpace(newName)) return "ERROR: newName is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"clone_client_action\",\"module\":\"" + Escape(module) + "\",\"source\":\"" + Escape(sourceName) + "\",\"name\":\"" + Escape(newName) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var createdName = j?["createdName"]?.GetValue<string>();
            var before = j?["before"]?.GetValue<int>();
            var after = j?["after"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: cloned client action '{sourceName}' -> '{createdName}' ({created}) in live module '{module}' via {via}. Client actions: {before} -> {after}. Exact copy; undo unit (Ctrl+Z in SS removes it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string RemoveDependency(string module, string producer)
    {
        if (string.IsNullOrWhiteSpace(producer)) return "ERROR: producer is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"remove_dependency\",\"module\":\"" + Escape(module) + "\",\"producer\":\"" + Escape(producer) + "\"}", 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: removed dependency on '{producer}' from live module '{module}' via {via}. Undo unit (Ctrl+Z in SS restores it)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string PublishModule(string module, string commitMessage)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"publish_module\",\"module\":\"" + Escape(module) + "\",\"commitMessage\":\"" + Escape(commitMessage ?? "") + "\"}", 960000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var resultType = j?["resultType"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: publish finished for live module '{module}' via {via} (result: {resultType})."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string DebugPublishSurface(string module) =>
        RunCmd(module, "{\"cmd\":\"debug_publish_surface\",\"module\":\"" + Escape(module) + "\"}", "Publish surface:");

    public static string DebugPublishState(string module) =>
        RunCmd(module, "{\"cmd\":\"debug_publish_state\",\"module\":\"" + Escape(module) + "\"}", "Publish state:");

    public static string SaveModule(string module) =>
        RunCmd(module, "{\"cmd\":\"save_module\",\"module\":\"" + Escape(module) + "\"}", "Save:");

    public static string OpenProducerModule(string module, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return "ERROR: reference is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"open_producer_module\",\"module\":\"" + Escape(module) + "\",\"reference\":\"" + Escape(reference) + "\"}", 120000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var e = j?["error"]?.GetValue<string>();
            return (ok == true ? $"OK: opened producer module '{reference}' in a new tab (in-process)." : $"FAILED: {e}") + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string CloneServiceActionFrom(string consumer, string producer, string source, string name) =>
        RunCmd(consumer, "{\"cmd\":\"clone_service_action_from\",\"consumer\":\"" + Escape(consumer) + "\",\"producer\":\"" + Escape(producer) + "\",\"source\":\"" + Escape(source) + "\",\"name\":\"" + Escape(name) + "\"}", "Clone from producer:");

    public static string DebugObjectPropSurface(string module, string kind, string entity, string name)
    {
        var extra = (entity != null ? ",\"entity\":\"" + Escape(entity) + "\"" : "");
        return RunCmd(module, "{\"cmd\":\"debug_object_prop_surface\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind) + "\"" + extra + ",\"name\":\"" + Escape(name) + "\"}", "Object surface:");
    }

    public static string SetObjectPropDeep(string module, string kind, string entity, string name, string propName, string value)
    {
        var extra = (entity != null ? ",\"entity\":\"" + Escape(entity) + "\"" : "");
        return RunCmd(module, "{\"cmd\":\"set_object_prop_deep\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind) + "\"" + extra
            + ",\"name\":\"" + Escape(name) + "\",\"propName\":\"" + Escape(propName) + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set prop:");
    }

    public static string UploadImage(string module, string name, string base64Data, string description)
    {
        if (string.IsNullOrWhiteSpace(base64Data)) return "ERROR: base64Data is required";
        var extra = (description != null ? ",\"description\":\"" + Escape(description) + "\"" : "");
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"upload_image\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\",\"base64Data\":\"" + base64Data.Trim() + "\"" + extra + "}", 120000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var e = j?["error"]?.GetValue<string>();
            return (ok == true ? $"OK: uploaded image '{name}' (live tree, undo unit)." : $"FAILED: {e}") + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string UploadResource(string module, string name, string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data)) return "ERROR: base64Data is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module],
            "{\"cmd\":\"upload_resource\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\",\"base64Data\":\"" + base64Data.Trim() + "\"}", 120000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var e = j?["error"]?.GetValue<string>();
            return (ok == true ? $"OK: uploaded resource '{name}' (live tree, undo unit)." : $"FAILED: {e}") + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string DeleteStructure(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_structure\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", "Delete structure:");

    public static string RemoveUnusedDependencies(string module) =>
        RunCmd(module, "{\"cmd\":\"remove_unused_dependencies\",\"module\":\"" + Escape(module) + "\"}", "Remove unused dependencies:");

    static string RunCmd(string module, string cmdJson, string okSummary)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var resp = Send(_pidForModule[module], cmdJson, 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var e = j?["error"]?.GetValue<string>();
            var report = j?["report"]?.GetValue<string>();
            return (ok == true ? okSummary : "FAILED: " + e) + (report != null ? "\n" + report : "") + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string ListFlow(string module, string action) =>
        RunCmd(module, "{\"cmd\":\"list_flow\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}", "Flow graph:");

    public static string SetAssignValue(string module, string action, string matchValue, string newValue, string matchVar) =>
        RunCmd(module, "{\"cmd\":\"set_assign\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"matchValue\":\"" + Escape(matchValue) + "\",\"newValue\":\"" + Escape(newValue) + "\",\"matchVar\":\"" + Escape(matchVar ?? "") + "\"}", "Set assign:");

    public static string AddOutputParam(string module, string action, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_output\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"type\":\"" + Escape(type) + "\"}", "Added output:");

    public static string AddAssignNode(string module, string action, string varName, string value, string where, string anchorVar, string anchorValue, int afterNodeIndex = -1) =>
        RunCmd(module, "{\"cmd\":\"add_assign\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"var\":\"" + Escape(varName) + "\",\"value\":\"" + Escape(value) + "\",\"where\":\"" + Escape(where) + "\",\"anchorVar\":\"" + Escape(anchorVar ?? "") + "\",\"anchorValue\":\"" + Escape(anchorValue ?? "") + "\"" + (afterNodeIndex >= 0 ? ",\"afterNodeIndex\":" + afterNodeIndex : "") + "}", "Added assign node:");

    public static string DeleteNode(string module, string action, string matchVar, string matchValue) =>
        RunCmd(module, "{\"cmd\":\"del_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"matchVar\":\"" + Escape(matchVar) + "\",\"matchValue\":\"" + Escape(matchValue) + "\"}", "Deleted node:");

    public static string ListConsumableElements(string module) =>
        RunCmd(module, "{\"cmd\":\"list_consumable_elements\",\"module\":\"" + Escape(module) + "\"}", "Consumable elements:");

    public static string ConsumeElements(string consumer, string producer, string what)
    {
        if (string.IsNullOrWhiteSpace(consumer)) return "ERROR: consumer is required";
        if (string.IsNullOrWhiteSpace(producer)) return "ERROR: producer is required";
        if (string.IsNullOrWhiteSpace(what)) return "ERROR: what is required ('*' or 'Type:Name,Type:Name')";
        var err = RequireModulePipe(consumer);
        if (err != null) return err;
        var resp = Send(_pidForModule[consumer],
            "{\"cmd\":\"consume_elements\",\"consumer\":\"" + Escape(consumer) + "\",\"producer\":\"" + Escape(producer) + "\",\"what\":\"" + Escape(what) + "\"}", 60000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var consumed = j?["totalConsumed"]?.GetValue<int>();
            var skipped = j?["totalSkipped"]?.GetValue<int>();
            var e = j?["error"]?.GetValue<string>();
            var errorsArr = j?["errors"]?.AsArray();
            var summary = ok == true
                ? $"OK: consumed {consumed} element(s) from '{producer}' into '{consumer}' (skipped {skipped} already consumed). Single undo unit (Ctrl+Z in SS removes all)."
                : $"FAILED: {e}";
            if (errorsArr != null && errorsArr.Count > 0)
                summary += "\nErrors:\n  " + string.Join("\n  ", errorsArr.Select(x => x?.GetValue<string>()));
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string AddInputParam(string module, string action, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_input\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"type\":\"" + Escape(type) + "\"}", "Added input:");

    public static string AddLocalVariable(string module, string action, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_local_variable\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"type\":\"" + Escape(type) + "\"}", "Added local variable:");

    public static string AddEndNode(string module, string action, string where) =>
        RunCmd(module, "{\"cmd\":\"add_end_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"where\":\"" + Escape(where) + "\"}", "Added end node:");

    public static string SetExceptionHandler(string module, string action, string endVar, string endValue, string handlerVar, string handlerValue) =>
        RunCmd(module, "{\"cmd\":\"set_exception_handler\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"endVar\":\"" + Escape(endVar) + "\",\"endValue\":\"" + Escape(endValue) + "\",\"handlerVar\":\"" + Escape(handlerVar) + "\",\"handlerValue\":\"" + Escape(handlerValue) + "\"}", "Set exception handler:");

    public static string SetStartExceptionHandler(string module, string action, string handlerVar, string handlerValue) =>
        RunCmd(module, "{\"cmd\":\"set_start_exception_handler\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"handlerVar\":\"" + Escape(handlerVar) + "\",\"handlerValue\":\"" + Escape(handlerValue) + "\"}", "Set start exception handler:");

    public static string ProbeNodeTypes(string module, string action) =>
        RunCmd(module, "{\"cmd\":\"probe_node_types\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}", "Node types:");

    public static string AddActionCall(string module, string action, string where, string anchorVar, string anchorValue, string serverActionName, string producerModule, int afterNodeIndex = -1) =>
        RunCmd(module, "{\"cmd\":\"add_action_call\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"where\":\"" + Escape(where) + "\",\"anchorVar\":\"" + Escape(anchorVar ?? "") + "\",\"anchorValue\":\"" + Escape(anchorValue ?? "") + "\",\"serverActionName\":\"" + Escape(serverActionName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"" + (afterNodeIndex >= 0 ? ",\"afterNodeIndex\":" + afterNodeIndex : "") + "}", "Added action call:");

    public static string SetActionCall(string module, string action, int nodeIndex, string serverActionName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"set_action_call\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"serverActionName\":\"" + Escape(serverActionName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Set action call:");

    public static string AddRefreshNode(string module, string action, string block, string aggregateName, string where) =>
        RunCmd(module, "{\"cmd\":\"add_refresh_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"block\":\"" + Escape(block) + "\",\"aggregateName\":\"" + Escape(aggregateName) + "\",\"where\":\"" + Escape(where ?? "beforeEnd") + "\"}", "Added refresh node:");

    public static string AddRefreshNode2(string module, string action, string block, string screen, string aggregateName, string where) =>
        RunCmd(module, "{\"cmd\":\"add_refresh_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"aggregateName\":\"" + Escape(aggregateName ?? "") + "\",\"where\":\"" + Escape(where ?? "beforeEnd") + "\"}", "Added refresh node:");

    public static string RemoveInputParam(string module, string action, string paramName) =>
        RunCmd(module, "{\"cmd\":\"remove_input_param\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"paramName\":\"" + Escape(paramName) + "\"}", "Removed input param:");

    public static string RemoveOutputParam(string module, string action, string paramName) =>
        RunCmd(module, "{\"cmd\":\"remove_output_param\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"paramName\":\"" + Escape(paramName) + "\"}", "Removed output param:");

    public static string AddEntityInput(string module, string action, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_entity_input\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"entityName\":\"" + Escape(entityName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added entity input:");

    public static string AddEntityIdentifierInput(string module, string action, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_entity_identifier_input\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"entityName\":\"" + Escape(entityName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added entity identifier input:");

    public static string SetOutputParamType(string module, string action, string paramName, string typeName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"set_output_param_type\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"paramName\":\"" + Escape(paramName) + "\",\"typeName\":\"" + Escape(typeName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Set output param type:");

    public static string SetInputParamType(string module, string action, string paramName, string typeName, string producerModule, string entityName) =>
        RunCmd(module, "{\"cmd\":\"set_input_param_type\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"paramName\":\"" + Escape(paramName) + "\",\"typeName\":\"" + Escape(typeName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\"}", "Set input param type:");

    public static string AddEntityOutput(string module, string action, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_entity_output\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"name\":\"" + Escape(name) + "\",\"entityName\":\"" + Escape(entityName) + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added entity output:");

    public static string DeleteNodeByIndex(string module, string action, int nodeIndex) =>
        RunCmd(module, "{\"cmd\":\"del_node_by_index\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + "}", "Deleted node by index:");

    public static string SetNodeTarget(string module, string action, int nodeIndex, int targetIndex) =>
        RunCmd(module, "{\"cmd\":\"set_node_target\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"targetIndex\":" + targetIndex + "}", "Set node target:");

    public static string SetNodeProp(string module, string action, int nodeIndex, string propName, string propValue) =>
        RunCmd(module, "{\"cmd\":\"set_node_prop\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"propName\":\"" + Escape(propName) + "\",\"propValue\":\"" + Escape(propValue) + "\"}", "Set node prop:");

    public static string DebugCreateNode(string module, string action, string nodeInterface) =>
        RunCmd(module, "{\"cmd\":\"debug_create_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeInterface\":\"" + Escape(nodeInterface) + "\"}", "Created node:");

    public static string DeleteServiceAction(string module, string action) =>
        RunCmd(module, "{\"cmd\":\"delete_service_action\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}", "Deleted service action:");

    public static string AddAssignmentToNode(string module, string action, int nodeIndex, string varName, string value) =>
        RunCmd(module, "{\"cmd\":\"add_assignment_to_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"var\":\"" + Escape(varName) + "\",\"value\":\"" + Escape(value) + "\"}", "Added assignment:");

    public static string RemoveAssignment(string module, string action, int nodeIndex, string varName) =>
        RunCmd(module, "{\"cmd\":\"remove_assignment\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"var\":\"" + Escape(varName) + "\"}", "Removed assignment:");

    public static string SetErrorHandlerException(string module, string action, int nodeIndex, string exceptionName) =>
        RunCmd(module, "{\"cmd\":\"set_error_handler_exception\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + (exceptionName != null ? ",\"exceptionName\":\"" + Escape(exceptionName) + "\"" : "") + "}", "Set error handler exception:");

    public static string MapActionInputs(string module, string action, int nodeIndex, string inputParamName = null) =>
        RunCmd(module, "{\"cmd\":\"map_action_inputs\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + (inputParamName != null ? ",\"inputParamName\":\"" + Escape(inputParamName) + "\"" : "") + "}", "Mapped action inputs:");

    public static string SetActionArg(string module, string action, int nodeIndex, string argName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_action_arg\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"argName\":\"" + Escape(argName) + "\",\"value\":\"" + Escape(value) + "\"}", "Set action arg:");

    public static string DebugNodeProps(string module, string action, int nodeIndex) =>
        RunCmd(module, "{\"cmd\":\"debug_node_props\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":\"" + nodeIndex + "\"}", "Node props:");

    public static string DebugActionArgs(string module, string action, int nodeIndex) =>
        RunCmd(module, "{\"cmd\":\"debug_action_args\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + "}", "Action args:");

    public static string DebugESpaceCollections(string module) =>
        RunCmd(module, "{\"cmd\":\"debug_eSpace_collections\",\"module\":\"" + Escape(module) + "\"}", "eSpace collections:");

    public static string DebugESpaceCollectionItems(string module, string collection) =>
        RunCmd(module, "{\"cmd\":\"debug_eSpace_collection_items\",\"module\":\"" + Escape(module) + "\",\"collection\":\"" + Escape(collection) + "\"}", "eSpace collection items:");

    public static string LayoutFlow(string module, string action) =>
        RunCmd(module, "{\"cmd\":\"layout_flow\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}", "Layout flow:");

    public static string GetNodePositions(string module, string action) =>
        RunCmd(module, "{\"cmd\":\"get_node_positions\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}", "Node positions:");

    public static string SetNodePosition(string module, string action, int nodeIndex, double x, double y) =>
        RunCmd(module, "{\"cmd\":\"set_node_position\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"x\":" + x + ",\"y\":" + y + "}", "Set node position:");

    public static string CreateFolder(string module, string folderName, string parentFolder)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return "ERROR: folderName is required";
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var cmdJson = "{\"cmd\":\"try_create_folder\",\"module\":\"" + Escape(module) + "\",\"folderName\":\"" + Escape(folderName) + "\""
            + (parentFolder != null ? ",\"parentFolder\":\"" + Escape(parentFolder) + "\"" : "") + "}";
        var resp = Send(_pidForModule[module], cmdJson, 30000);
        try
        {
            var j = JsonNode.Parse(resp);
            var ok = j?["ok"]?.GetValue<bool>();
            var via = j?["via"]?.GetValue<string>();
            var created = j?["createdType"]?.GetValue<string>();
            var before = j?["foldersBefore"]?.GetValue<int>();
            var after = j?["foldersAfter"]?.GetValue<int>();
            var pf = j?["parentFolder"]?.GetValue<string>();
            var e = j?["error"]?.GetValue<string>();
            var summary = ok == true
                ? $"OK: created folder '{folderName}' ({created}) in section '{pf}' in live module '{module}' via {via}. Folders: {before} -> {after}. Undo unit (Ctrl+Z)."
                : $"FAILED: {e}";
            return summary + "\n[bridge] " + resp;
        }
        catch { return resp; }
    }

    public static string MoveToFolder(string module, string action, string folderName) =>
        RunCmd(module, "{\"cmd\":\"move_to_folder\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"folderName\":\"" + Escape(folderName) + "\"}", "Moved to folder:");

    // ---- UI / screen / widget / CSS forwarders ----
    public static string CreateWebFlow(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_web_flow\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", "Created web flow:");

    public static string CreateFlowScratch(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_flow_scratch\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name) + "\"}", "Created web flow (scratch):");

    public static string CreateWebScreen(string module, string webFlow, string name) =>
        RunCmd(module, "{\"cmd\":\"create_web_screen\",\"module\":\"" + Escape(module) + "\",\"webFlow\":\"" + Escape(webFlow) + "\",\"name\":\"" + Escape(name) + "\"}", "Created web screen:");

    public static string CreateWebBlock(string module, string flow, string name) =>
        RunCmd(module, "{\"cmd\":\"create_web_block\",\"module\":\"" + Escape(module) + "\",\"flow\":\"" + Escape(flow) + "\",\"name\":\"" + Escape(name) + "\"}", "Created web block:");

    public static string ListWebFlows(string module)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"list_web_flows\",\"module\":\"" + Escape(module) + "\"}");
    }

    public static string AddContainer(string module, string screen, string parent, string name, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_container\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name) + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added container:");

    public static string AddExpression(string module, string screen, string parent, string name, string value, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_expression\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name) + "\",\"value\":\"" + Escape(value ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added expression:");

    public static string AddText(string module, string screen, string parent, string name, string text, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_text\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name) + "\",\"text\":\"" + Escape(text ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added text:");

    public static string AddLink(string module, string screen, string parent, string name, string text, string targetScreen, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_link\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name) + "\",\"text\":\"" + Escape(text ?? "") + "\",\"targetScreen\":\"" + Escape(targetScreen ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added link:");

    public static string AddButton(string module, string screen, string placeholder, string parent, string name, string text, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_button\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"placeholder\":\"" + Escape(placeholder ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name) + "\",\"text\":\"" + Escape(text ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added button:");

    public static string SetButtonOnClick(string module, string screen, string button, string actionName) =>
        RunCmd(module, "{\"cmd\":\"set_button_onclick\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"button\":\"" + Escape(button) + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\"}", "Set button OnClick:");

    public static string SetStyleClass(string module, string screen, string widget, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"set_style_class\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"widget\":\"" + Escape(widget) + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Set style class:");

    public static string SetUserCss(string module, string css)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        // css may contain newlines/quotes: serialize it as a proper JSON string so the
        // bridge's newline-delimited JSON framing isn't broken.
        var cmdJson = "{\"cmd\":\"set_user_css\",\"module\":\"" + Escape(module) + "\",\"css\":" + JsonSerializer.Serialize(css ?? "") + "}";
        return RunCmd(module, cmdJson, "Set user CSS:");
    }

    public static string ProbeStyleProp(string module, string screen, string widget) =>
        RunCmd(module, "{\"cmd\":\"probe_style_prop\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"widget\":\"" + Escape(widget) + "\"}", "Style prop probe:");

    public static string ReadThemeCss(string module) =>
        RunCmd(module, "{\"cmd\":\"read_theme_css\",\"module\":\"" + Escape(module) + "\"}", "Theme CSS:");

    public static string ProbeSheet(string module) =>
        RunCmd(module, "{\"cmd\":\"probe_sheet\",\"module\":\"" + Escape(module) + "\"}", "Sheet probe:");

    public static string SetScreenTitle(string module, string screen, string title) =>
        RunCmd(module, "{\"cmd\":\"set_screen_title\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\",\"title\":\"" + Escape(title ?? "") + "\"}", "Set screen title:");

    public static string ListWidgets(string module, string screen)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"list_widgets\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen) + "\"}");
    }

    // ---- theme / layout forwarders ----
    public static string CreateTheme(string module, string sourceName, string newName, string css)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var cmdJson = "{\"cmd\":\"create_theme\",\"module\":\"" + Escape(module) + "\",\"sourceName\":\"" + Escape(sourceName ?? "") + "\",\"newName\":\"" + Escape(newName ?? "") + "\",\"css\":" + JsonSerializer.Serialize(css ?? "") + "}";
        return RunCmd(module, cmdJson, "Created theme:");
    }

    public static string SetThemeCss(string module, string themeName, string css)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        var cmdJson = "{\"cmd\":\"set_theme_css\",\"module\":\"" + Escape(module) + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\",\"css\":" + JsonSerializer.Serialize(css ?? "") + "}";
        return RunCmd(module, cmdJson, "Set theme CSS:");
    }

    public static string SetScreenTheme(string module, string screen, string themeName) =>
        RunCmd(module, "{\"cmd\":\"set_screen_theme\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\"}", "Set screen theme:");

    public static string SetFlowTheme(string module, string flow, string themeName) =>
        RunCmd(module, "{\"cmd\":\"set_flow_theme\",\"module\":\"" + Escape(module) + "\",\"flow\":\"" + Escape(flow ?? "") + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\"}", "Set flow theme:");

    public static string CloneWebBlock(string module, string sourceName, string newName) =>
        RunCmd(module, "{\"cmd\":\"clone_web_block\",\"module\":\"" + Escape(module) + "\",\"sourceName\":\"" + Escape(sourceName ?? "") + "\",\"newName\":\"" + Escape(newName ?? "") + "\"}", "Cloned web block:");

    public static string MoveWebBlockToFlow(string module, string block, string targetFlow) =>
        RunCmd(module, "{\"cmd\":\"move_web_block_to_flow\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"targetFlow\":\"" + Escape(targetFlow ?? "") + "\"}", "Moved web block:");

    public static string SetThemeLayout(string module, string themeName, string layoutBlock, string menuBlock) =>
        RunCmd(module, "{\"cmd\":\"set_theme_layout\",\"module\":\"" + Escape(module) + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\",\"layoutBlock\":\"" + Escape(layoutBlock ?? "") + "\",\"menuBlock\":\"" + Escape(menuBlock ?? "") + "\"}", "Set theme layout:");

    public static string ProbeTheme(string module, string themeName)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_theme\",\"module\":\"" + Escape(module) + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\"}");
    }

    // ---- placeholder / html / block forwarders ----
    public static string AddHtmlElement(string module, string screen, string parent, string name, string tag, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_html_element\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"tag\":\"" + Escape(tag ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added html element:");

    public static string AddToPlaceholder(string module, string screen, string placeholder, string kind, string name, string text, string targetScreen, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_to_placeholder\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"placeholder\":\"" + Escape(placeholder ?? "") + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"text\":\"" + Escape(text ?? "") + "\",\"targetScreen\":\"" + Escape(targetScreen ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added to placeholder:");

    public static string AddInsidePlaceholder(string module, string screen, string placeholder, string parent, string kind, string name, string text, string targetScreen, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_inside_placeholder\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"placeholder\":\"" + Escape(placeholder ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"text\":\"" + Escape(text ?? "") + "\",\"targetScreen\":\"" + Escape(targetScreen ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added inside placeholder:");

    public static string AddLinkToBlock(string module, string block, string parent, string name, string text, string targetScreen, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_link_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"text\":\"" + Escape(text ?? "") + "\",\"targetScreen\":\"" + Escape(targetScreen ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added link to block:");

    public static string AddPlaceholderToBlock(string module, string block, string parent, string name) =>
        RunCmd(module, "{\"cmd\":\"add_placeholder_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Added placeholder to block:");

    public static string AddWidgetToBlock(string module, string block, string parent, string kind, string name, string value, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_widget_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added widget to block:");

    public static string DeleteWidgetFromBlock(string module, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_widget_from_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted widget from block:");

    public static string AddVariableToBlock(string module, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"add_variable_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Added variable to block:");

    public static string AddVariableToScreen(string module, string screen, string name, string type, string defaultValue) =>
        RunCmd(module, "{\"cmd\":\"add_variable_to_screen\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\",\"defaultValue\":\"" + Escape(defaultValue ?? "") + "\"}", "Added variable to screen:");

    public static string SetBlockWidgetProperty(string module, string block, string widgetName, string propName, string propValue) =>
        RunCmd(module, "{\"cmd\":\"set_block_widget_property\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widgetName\":\"" + Escape(widgetName ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"propValue\":\"" + Escape(propValue ?? "") + "\"}", "Set block widget property:");

    public static string DumpBlockWidgetTypes(string module, string block)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"dump_block_widget_types\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\"}");
    }

    public static string AddButtonToBlock(string module, string block, string parent, string name, string text, string styleClass) =>
        RunCmd(module, "{\"cmd\":\"add_button_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"text\":\"" + Escape(text ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\"}", "Added button to block:");

    public static string SetBlockButtonOnClick(string module, string block, string button, string actionName) =>
        RunCmd(module, "{\"cmd\":\"set_block_button_onclick\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"button\":\"" + Escape(button ?? "") + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\"}", "Set block button OnClick:");

    public static string ListBlockInputParams(string module, string block)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"list_block_input_params\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\"}");
    }

    public static string AddInputParamToBlock(string module, string block, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_input_param_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Added input param to block:");

    public static string SetBlockInputParamType(string module, string block, string paramName, string typeName, string producerModule, string entityName) =>
        RunCmd(module, "{\"cmd\":\"set_block_input_param_type\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"paramName\":\"" + Escape(paramName ?? "") + "\",\"typeName\":\"" + Escape(typeName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\"}", "Set block input param type:");

    public static string RemoveInputParamFromBlock(string module, string block, string paramName) =>
        RunCmd(module, "{\"cmd\":\"remove_input_param_from_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"paramName\":\"" + Escape(paramName ?? "") + "\"}", "Removed input param from block:");

    public static string RemoveScreenInputParam(string module, string screen, string paramName) =>
        RunCmd(module, "{\"cmd\":\"remove_screen_input_param\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"paramName\":\"" + Escape(paramName ?? "") + "\"}", "Removed screen input param:");

    public static string AddEntityInputToBlock(string module, string block, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_entity_input_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added entity input to block:");

    public static string AddEntityIdentifierInputToBlock(string module, string block, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_entity_identifier_input_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added entity identifier input to block:");

    public static string AddIfNode(string module, string action, string condition, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"add_if_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"condition\":\"" + Escape(condition ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Added If node:");

    public static string AddSwitchNode(string module, string action, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"add_switch_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Added Switch node:");

    public static string ProbeNodeConnectors(string module, string action, int nodeIndex)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_node_connectors\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"nodeIndex\":" + nodeIndex + "}");
    }

    public static string ProbeNodeConditions(string module, string action, int nodeIndex)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_node_conditions\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"nodeIndex\":" + nodeIndex + "}");
    }

    public static string SetSwitchCase(string module, string action, int nodeIndex, string value, int targetIndex) =>
        RunCmd(module, "{\"cmd\":\"set_switch_case\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"nodeIndex\":" + nodeIndex + ",\"value\":\"" + Escape(value ?? "") + "\",\"targetIndex\":" + targetIndex + "}", "Set switch case:");

    public static string SetConnectorTarget(string module, string action, int nodeIndex, string propName, int targetIndex) =>
        RunCmd(module, "{\"cmd\":\"set_connector_target\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"nodeIndex\":" + nodeIndex + ",\"propName\":\"" + Escape(propName ?? "") + "\",\"targetIndex\":" + targetIndex + "}", "Set connector target:");

    public static string ProbeBlockMembers(string module, string block)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_block_members\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\"}");
    }

    public static string AddEventToBlock(string module, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"add_event_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Added event to block:");

    public static string AddEventParamToBlock(string module, string block, string eventName, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_event_param_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"event\":\"" + Escape(eventName ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Added event param to block:");

    public static string SetBlockHandler(string module, string block, string handler, string actionName) =>
        RunCmd(module, "{\"cmd\":\"set_block_handler\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"handler\":\"" + Escape(handler ?? "") + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\"}", "Set block handler:");

    public static string SetScreenHandler(string module, string screen, string handler, string actionName) =>
        RunCmd(module, "{\"cmd\":\"set_screen_handler\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"handler\":\"" + Escape(handler ?? "") + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\"}", "Set screen handler:");

    public static string WireButtonToScreen(string module, string screen, string button, string targetScreen, string pars) =>
        RunCmd(module, "{\"cmd\":\"wire_button_to_screen\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"button\":\"" + Escape(button ?? "") + "\",\"targetScreen\":\"" + Escape(targetScreen ?? "") + "\",\"params\":\"" + Escape(pars ?? "") + "\"}", "Wired button to screen:");

    public static string MoveWidget(string module, string screen, string widget, string newParent) =>
        RunCmd(module, "{\"cmd\":\"move_widget\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"newParent\":\"" + Escape(newParent ?? "") + "\"}", "Moved widget:");

    public static string AddRaiseEventNode(string module, string action, string eventName, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"add_raise_event_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"eventName\":\"" + Escape(eventName ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Added RaiseEvent node:");

    public static string ListPlaceholders(string module, string screen)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"list_placeholders\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}");
    }

    public static string DeleteFromPlaceholder(string module, string screen, string placeholder, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_from_placeholder\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"placeholder\":\"" + Escape(placeholder ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted from placeholder:");

    public static string SetHtmlAttr(string module, string screen, string widget, string attrName, string attrValue) =>
        RunCmd(module, "{\"cmd\":\"set_html_attr\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"attrValue\":\"" + Escape(attrValue ?? "") + "\"}", "Set html attr:");

    public static string SetScreenLayout(string module, string screen, string layoutBlock) =>
        RunCmd(module, "{\"cmd\":\"set_screen_layout\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"layoutBlock\":\"" + Escape(layoutBlock ?? "") + "\"}", "Set screen layout:");

    public static string DeleteWebFlow(string module, string flow) =>
        RunCmd(module, "{\"cmd\":\"delete_web_flow\",\"module\":\"" + Escape(module) + "\",\"flow\":\"" + Escape(flow ?? "") + "\"}", "Deleted web flow:");

    public static string DeleteScreenFromFlow(string module, string flow, string screen) =>
        RunCmd(module, "{\"cmd\":\"delete_screen_from_flow\",\"module\":\"" + Escape(module) + "\",\"flow\":\"" + Escape(flow ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}", "Deleted screen from flow:");

    public static string DeleteWidget(string module, string screen, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_widget\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted widget:");

    public static string DeleteLayout(string module, string screen) =>
        RunCmd(module, "{\"cmd\":\"delete_layout\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}", "Deleted layout:");

    public static string AddHtmlText(string module, string screen, string parent, string name, string tag, string styleClass, string text) =>
        RunCmd(module, "{\"cmd\":\"add_html_text\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"tag\":\"" + Escape(tag ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\",\"text\":" + JsonSerializer.Serialize(text ?? "") + "}", "Added html text:");

    public static string ProbeWidgetDeep(string module, string screen, string widgetName)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_widget_deep\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widgetName\":\"" + Escape(widgetName ?? "") + "\"}");
    }

    public static string ProbeLayoutRef(string module, string screen)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_layout_ref\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}");
    }

    public static string ReadUserCssText(string module, string themeName)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"read_user_css_text\",\"module\":\"" + Escape(module) + "\",\"themeName\":\"" + Escape(themeName ?? "") + "\"}");
    }

    public static string ProbeBlockWidget(string module, string block, string widget)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_block_widget\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\"}");
    }

    public static string ProbeDataSources(string module, string block, string screen)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_data_sources\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}");
    }

    public static string AddAggregateToBlock(string module, string block, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added aggregate to block:");

    public static string AddDataActionToBlock(string module, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"add_data_action_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Added data action to block:");

    public static string AddAggregateToScreen(string module, string screen, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_to_screen\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Added aggregate to screen:");

    public static string AddDataActionToScreen(string module, string screen, string name) =>
        RunCmd(module, "{\"cmd\":\"add_data_action_to_screen\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Added data action to screen:");

    public static string DeleteDataAction(string module, string block, string screen, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_data_action\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted data action:");

    public static string DeleteAggregate(string module, string block, string screen, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_aggregate\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted aggregate:");

    public static string ProbeWidgetKinds(string module)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_widget_kinds\",\"module\":\"" + Escape(module) + "\"}");
    }

    public static string AddNRWidget(string module, string screen, string block, string parent, string kind, string name, string styleClass, string text) =>
        RunCmd(module, "{\"cmd\":\"add_nr_widget\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"styleClass\":\"" + Escape(styleClass ?? "") + "\",\"text\":" + JsonSerializer.Serialize(text ?? "") + "}", "Added nr widget:");

    public static string SetWidgetHandler(string module, string screen, string block, string widget, string eventName, string actionName) =>
        RunCmd(module, "{\"cmd\":\"set_widget_handler\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"event\":\"" + Escape(eventName ?? "") + "\",\"actionName\":\"" + Escape(actionName ?? "") + "\"}", "Set widget handler:");

    public static string SetElementDescription(string module, string kind, string name, string description) =>
        RunCmd(module, "{\"cmd\":\"set_element_description\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"description\":" + JsonSerializer.Serialize(description ?? "") + "}", "Set element description:");

    public static string SetBlockVariableType(string module, string block, string var, string type, string defaultValue, string producerModule, bool identifier, string entityName) =>
        RunCmd(module, "{\"cmd\":\"set_block_variable_type\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"var\":\"" + Escape(var ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\",\"default\":\"" + Escape(defaultValue ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\",\"identifier\":" + (identifier ? "true" : "false") + ",\"entityName\":\"" + Escape(entityName ?? "") + "\"}", "Set block variable type:");

    public static string SetBlockCpText(string module, string block, string widget, string propName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_block_cp_text\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"value\":" + JsonSerializer.Serialize(value ?? "") + "}", "Set block CP text:");

    public static string SetScreenCpText(string module, string screen, string widget, string propName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_screen_cp_text\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"value\":" + JsonSerializer.Serialize(value ?? "") + "}", "Set screen CP text:");

    public static string DeleteScreenClientAction(string module, string screen, string action) =>
        RunCmd(module, "{\"cmd\":\"delete_screen_client_action\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"action\":\"" + Escape(action ?? "") + "\"}", "Delete screen client action:");

    public static string SetBlockCpImage(string module, string block, string widget, string propName, string imageName) =>
        RunCmd(module, "{\"cmd\":\"set_block_cp_image\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"imageName\":\"" + Escape(imageName ?? "") + "\"}", "Set block CP image:");

    public static string DeleteAnonBlockWidgets(string module, string block, string typeContains, bool recursive) =>
        RunCmd(module, "{\"cmd\":\"delete_anon_block_widgets\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"typeContains\":\"" + Escape(typeContains ?? "") + "\",\"recursive\":" + (recursive ? "true" : "false") + "}", "Deleted anon block widgets:");

    public static string ProbeBlockCp(string module, string block, string widget, string propName) =>
        RunCmd(module, "{\"cmd\":\"probe_block_cp\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\"}", "Probe block CP:");

    public static string ProbeType(string typeName)
    {
        var pid = DiscoverPids().FirstOrDefault();
        if (pid == 0) return NoBridgeError();
        try { return Send(pid, "{\"cmd\":\"probe_type\",\"typeName\":\"" + Escape(typeName ?? "") + "\"}"); }
        catch (Exception e) { return "ERROR: " + e.GetType().Name + ": " + e.Message; }
    }

    public static string ProbeReferences(string module) =>
        RunCmd(module, "{\"cmd\":\"probe_references\",\"module\":\"" + Escape(module ?? "") + "\"}", "Probe references:");

    public static string SetBlockAggregateSource(string module, string block, string name, string entityName, string producerModule) =>
        RunCmd(module, "{\"cmd\":\"set_block_aggregate_source\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\",\"producerModule\":\"" + Escape(producerModule ?? "") + "\"}", "Set block aggregate source:");

    public static string SetBlockAggregateFilter(string module, string block, string name, string filter) =>
        RunCmd(module, "{\"cmd\":\"set_block_aggregate_filter\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"filter\":\"" + Escape(filter ?? "") + "\"}", "Set block aggregate filter:");

    public static string AddIfWidgetToBlock(string module, string block, string parent, string name, string condition) =>
        RunCmd(module, "{\"cmd\":\"add_if_widget_to_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"parent\":\"" + Escape(parent ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"condition\":\"" + Escape(condition ?? "") + "\"}", "Added If widget to block:");

    public static string MoveWidgetInBlock(string module, string block, string widget, string newParent) =>
        RunCmd(module, "{\"cmd\":\"move_widget_in_block\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"newParent\":\"" + Escape(newParent ?? "") + "\"}", "Moved widget in block:");

    public static string SetBlockCpExpression(string module, string block, string widget, string propName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_block_cp_expression\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set block CP expression:");

    public static string SetBlockCpParsed(string module, string block, string widget, string propName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_block_cp_parsed\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set block CP parsed:");

    public static string SetScreenCpParsed(string module, string screen, string widget, string propName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_screen_cp_parsed\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"propName\":\"" + Escape(propName ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set screen CP parsed:");

    public static string CreateBlockClientAction(string module, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"create_block_client_action\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Created block client action:");

    public static string CreateStructure(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_structure\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Created structure:");

    public static string AddStructureAttribute(string module, string structure, string attrName, string type) =>
        RunCmd(module, "{\"cmd\":\"add_structure_attribute\",\"module\":\"" + Escape(module) + "\",\"structure\":\"" + Escape(structure ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Added structure attribute:");

    public static string SetStructureAttributeType(string module, string structure, string attrName, string type) =>
        RunCmd(module, "{\"cmd\":\"set_structure_attribute_type\",\"module\":\"" + Escape(module) + "\",\"structure\":\"" + Escape(structure ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Set structure attribute type:");

    public static string CreateEntity(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_entity\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Created entity:");

    public static string AddEntityAttribute(string module, string entity, string attrName, string type, string isMandatory, string defaultValue) =>
        RunCmd(module, "{\"cmd\":\"add_entity_attribute\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\",\"isMandatory\":\"" + Escape(isMandatory ?? "") + "\",\"defaultValue\":\"" + Escape(defaultValue ?? "") + "\"}", "Added entity attribute:");

    public static string SetEntityAttributeType(string module, string entity, string attrName, string type) =>
        RunCmd(module, "{\"cmd\":\"set_entity_attribute_type\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Set entity attribute type:");

    public static string SetEntityAttributeName(string module, string entity, string attrName, string newName) =>
        RunCmd(module, "{\"cmd\":\"set_entity_attribute_name\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"newName\":\"" + Escape(newName ?? "") + "\"}", "Set entity attribute name:");

    public static string DeleteEntityAttribute(string module, string entity, string attrName) =>
        RunCmd(module, "{\"cmd\":\"delete_entity_attribute\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\"}", "Deleted entity attribute:");

    public static string DeleteEntity(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"delete_entity\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Deleted entity:");

    public static string SetEntityProp(string module, string entity, string prop, string value) =>
        RunCmd(module, "{\"cmd\":\"set_entity_prop\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"prop\":\"" + Escape(prop ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set entity prop:");

    public static string SetStructureProp(string module, string structure, string prop, string value) =>
        RunCmd(module, "{\"cmd\":\"set_structure_prop\",\"module\":\"" + Escape(module) + "\",\"structure\":\"" + Escape(structure ?? "") + "\",\"prop\":\"" + Escape(prop ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set structure prop:");

    public static string SetServerActionProp(string module, string action, string prop, string value) =>
        RunCmd(module, "{\"cmd\":\"set_server_action_prop\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"prop\":\"" + Escape(prop ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set server action prop:");

    public static string SetEntityIdentifier(string module, string entity, string attrName) =>
        RunCmd(module, "{\"cmd\":\"set_entity_identifier\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\"}", "Set entity identifier:");

    public static string ProbeEntityActions(string module, string entity) =>
        RunCmd(module, "{\"cmd\":\"probe_entity_actions\",\"module\":\"" + Escape(module) + "\",\"entity\":\"" + Escape(entity ?? "") + "\"}", "Entity actions:");

    public static string CreateEntityActionNode(string module, string action, string entity, string entityAction, string where, string anchorVar, string anchorValue, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"create_entity_action_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"entity\":\"" + Escape(entity ?? "") + "\",\"entityAction\":\"" + Escape(entityAction ?? "") + "\",\"where\":\"" + Escape(where ?? "beforeEnd") + "\",\"anchorVar\":\"" + Escape(anchorVar ?? "") + "\",\"anchorValue\":\"" + Escape(anchorValue ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Created entity action node:");

    public static string GetVerifyErrors(string module, string kind, string name, string screen, string block, string verbose) =>
        RunCmd(module, "{\"cmd\":\"get_verify_errors\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"verbose\":\"" + Escape(verbose ?? "") + "\"}", "Verify errors:");

    public static string SetWidgetSource(string module, string screen, string block, string widget, string source) =>
        RunCmd(module, "{\"cmd\":\"set_widget_source\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"source\":\"" + Escape(source ?? "") + "\"}", "Set widget source:");

    public static string SetAggregatePaging(string module, string screen, string block, string name, string maxRecords, string startIndex) =>
        RunCmd(module, "{\"cmd\":\"set_aggregate_paging\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"maxRecords\":\"" + Escape(maxRecords ?? "") + "\",\"startIndex\":\"" + Escape(startIndex ?? "") + "\"}", "Set aggregate paging:");

    public static string RemoveAggregateSort(string module, string screen, string block, string name, string attr) =>
        RunCmd(module, "{\"cmd\":\"remove_aggregate_sort\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"attr\":\"" + Escape(attr ?? "") + "\"}", "Removed aggregate sort:");

    public static string AddAggregateDynamicSort(string module, string screen, string block, string name, string varName) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_dynamic_sort\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"varName\":\"" + Escape(varName ?? "") + "\"}", "Added aggregate dynamic sort:");

    public static string SetInputParamMandatory(string module, string action, string paramName, string mandatory) =>
        RunCmd(module, "{\"cmd\":\"set_input_param_mandatory\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"paramName\":\"" + Escape(paramName ?? "") + "\",\"mandatory\":\"" + Escape(mandatory ?? "") + "\"}", "Set input param mandatory:");

    public static string SetWidgetHandlerArg(string module, string screen, string block, string widget, string eventName, string argName, string value) =>
        RunCmd(module, "{\"cmd\":\"set_widget_handler_arg\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"event\":\"" + Escape(eventName ?? "") + "\",\"argName\":\"" + Escape(argName ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Set widget handler arg:");

    public static string AddAggregateSort(string module, string screen, string block, string name, string attr, string ascending) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_sort\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"attr\":\"" + Escape(attr ?? "") + "\",\"ascending\":\"" + Escape(ascending ?? "") + "\"}", "Added aggregate sort:");

    public static string AddScreenAggregateFilter(string module, string screen, string name, string filter) =>
        RunCmd(module, "{\"cmd\":\"add_screen_aggregate_filter\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"filter\":\"" + Escape(filter ?? "") + "\"}", "Added screen aggregate filter:");

    public static string AddAggregateCalculatedAttr(string module, string screen, string block, string name, string attrName, string type) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_calculated_attr\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Added aggregate calculated attr:");

    public static string AddForeachNode(string module, string action, string recordList, string maxIterations, string startIndex) =>
        RunCmd(module, "{\"cmd\":\"add_foreach_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"recordList\":\"" + Escape(recordList ?? "") + "\",\"maxIterations\":\"" + Escape(maxIterations ?? "") + "\",\"startIndex\":\"" + Escape(startIndex ?? "") + "\"}", "Added foreach node:");

    public static string AddScreenInputParam(string module, string screen, string name, string type) =>
        RunCmd(module, "{\"cmd\":\"add_screen_input_param\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Added screen input param:");

    public static string CreateRole(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_role\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Created role:");

    public static string SetScreenPermissions(string module, string screen, string roles, string isPublic) =>
        RunCmd(module, "{\"cmd\":\"set_screen_permissions\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"roles\":\"" + Escape(roles ?? "") + "\",\"isPublic\":\"" + Escape(isPublic ?? "") + "\"}", "Set screen permissions:");

    public static string CreateSiteProperty(string module, string name, string type, string shared, string defaultValue) =>
        RunCmd(module, "{\"cmd\":\"create_site_property\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\",\"shared\":\"" + Escape(shared ?? "") + "\",\"defaultValue\":\"" + Escape(defaultValue ?? "") + "\"}", "Created site property:");

    public static string CreateTimer(string module, string name) =>
        RunCmd(module, "{\"cmd\":\"create_timer\",\"module\":\"" + Escape(module) + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Created timer:");

    public static string FixStyleLiteral(string module, string screen, string block, string widget, string cssClass) =>
        RunCmd(module, "{\"cmd\":\"fix_style_literal\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"cssClass\":\"" + Escape(cssClass ?? "") + "\"}", "Fix style literal:");

    public static string SetAggregateCalcFormula(string module, string screen, string block, string name, string attrName, string formula) =>
        RunCmd(module, "{\"cmd\":\"set_aggregate_calc_formula\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"formula\":\"" + Escape(formula ?? "") + "\"}", "Set calc formula:");

    public static string SetAggregateParamType(string module, string screen, string block, string name, string paramName, string type) =>
        RunCmd(module, "{\"cmd\":\"set_aggregate_param_type\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"paramName\":\"" + Escape(paramName ?? "") + "\",\"type\":\"" + Escape(type ?? "") + "\"}", "Set aggregate param type:");

    public static string ProbeCollection(string module, string screen, string block, string collection) =>
        RunCmd(module, "{\"cmd\":\"probe_collection\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"collection\":\"" + Escape(collection ?? "") + "\"}", "Collection probe:");

    public static string ReadAggregateSorts(string module, string screen, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"read_aggregate_sorts\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Aggregate sorts:");

    public static string ReadAggregateCalcs(string module, string screen, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"read_aggregate_calcs\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Aggregate calcs:");

    public static string ReadAggregateParams(string module, string screen, string block, string name) =>
        RunCmd(module, "{\"cmd\":\"read_aggregate_params\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\"}", "Aggregate params:");

    public static string DeleteAggregateSort(string module, string screen, string block, string name, int index) =>
        RunCmd(module, "{\"cmd\":\"delete_aggregate_sort\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"index\":" + index + "}", "Deleted aggregate sort:");

    public static string DeleteAggregateFilter(string module, string screen, string block, string name, int index) =>
        RunCmd(module, "{\"cmd\":\"delete_aggregate_filter\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"index\":" + index + "}", "Deleted aggregate filter:");

    public static string GrantScreenPermission(string module, string screen, string roleName) =>
        RunCmd(module, "{\"cmd\":\"grant_screen_permission\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"roleName\":\"" + Escape(roleName ?? "") + "\"}", "Granted permission:");

    public static string RemoveScreenPermission(string module, string screen, string roleName) =>
        RunCmd(module, "{\"cmd\":\"remove_screen_permission\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"roleName\":\"" + Escape(roleName ?? "") + "\"}", "Removed permission:");

    public static string ReadScreenPermissions(string module, string screen) =>
        RunCmd(module, "{\"cmd\":\"read_screen_permissions\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\"}", "Screen permissions:");

    public static string AddAggregateSource(string module, string screen, string block, string name, string entityName) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_source\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"entityName\":\"" + Escape(entityName ?? "") + "\"}", "Added aggregate source:");

    public static string AddAggregateJoin(string module, string screen, string block, string name, string condition, string leftIndex, string rightIndex) =>
        RunCmd(module, "{\"cmd\":\"add_aggregate_join\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"condition\":\"" + Escape(condition ?? "") + "\",\"leftIndex\":\"" + Escape(leftIndex ?? "") + "\",\"rightIndex\":\"" + Escape(rightIndex ?? "") + "\"}", "Added aggregate join:");

    public static string DebugCreateSurface(string module) =>
        RunCmd(module, "{\"cmd\":\"debug_create_surface\",\"module\":\"" + Escape(module) + "\"}", "Create surface:");

    public static string SetLinkParams(string module, string screen, string widget, string pars) =>
        RunCmd(module, "{\"cmd\":\"set_link_params\",\"module\":\"" + Escape(module) + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"params\":\"" + Escape(pars ?? "") + "\"}", "Set link params:");

    public static string SetTimerAction(string module, string timer, string action) =>
        RunCmd(module, "{\"cmd\":\"set_timer_action\",\"module\":\"" + Escape(module) + "\",\"timer\":\"" + Escape(timer ?? "") + "\",\"action\":\"" + Escape(action ?? "") + "\"}", "Set timer action:");

    public static string SetBlockHtmlAttr(string module, string block, string widget, string attrName, string attrValue) =>
        RunCmd(module, "{\"cmd\":\"set_block_html_attr\",\"module\":\"" + Escape(module) + "\",\"block\":\"" + Escape(block ?? "") + "\",\"widget\":\"" + Escape(widget ?? "") + "\",\"attrName\":\"" + Escape(attrName ?? "") + "\",\"attrValue\":\"" + Escape(attrValue ?? "") + "\"}", "Set block html attr:");

    public static string AddJsNode(string module, string action, string js, string nodeName, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"add_js_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"js\":\"" + Escape(js ?? "") + "\",\"nodeName\":\"" + Escape(nodeName ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Added JS node:");

    public static string AddMessageNode(string module, string action, string message, string kind, int afterNodeIndex) =>
        RunCmd(module, "{\"cmd\":\"add_message_node\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"message\":\"" + Escape(message ?? "") + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"afterNodeIndex\":" + afterNodeIndex + "}", "Added Message node:");

    public static string ProbeFlowNodeClasses(string module)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_flow_node_classes\",\"module\":\"" + Escape(module) + "\"}");
    }

    public static string DeleteAction(string module, string action)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"delete_action\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\"}");
    }

    public static string SetActionName(string module, string action, string newName)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"set_action_name\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"newName\":\"" + Escape(newName) + "\"}");
    }

    public static string SetRaiseEventArg(string module, string action, int nodeIndex, string argName, string value)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"set_raise_event_arg\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"nodeIndex\":" + nodeIndex + ",\"argName\":\"" + Escape(argName) + "\",\"value\":\"" + Escape(value) + "\"}");
    }

    public static string ProbeObj(string module, string kind, string block, string screen, string name, string sub)
    {
        var err = RequireModulePipe(module);
        if (err != null) return err;
        return Send(_pidForModule[module], "{\"cmd\":\"probe_obj\",\"module\":\"" + Escape(module) + "\",\"kind\":\"" + Escape(kind ?? "") + "\",\"block\":\"" + Escape(block ?? "") + "\",\"screen\":\"" + Escape(screen ?? "") + "\",\"name\":\"" + Escape(name ?? "") + "\",\"sub\":\"" + Escape(sub ?? "") + "\"}");
    }

    public static string AddLifecycleAssign(string module, string action, string varName, string value) =>
        RunCmd(module, "{\"cmd\":\"add_lifecycle_assign\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action ?? "") + "\",\"var\":\"" + Escape(varName ?? "") + "\",\"value\":\"" + Escape(value ?? "") + "\"}", "Added lifecycle assign:");

    public static string DumpWidgetConcretes()
    {
        var pid = DiscoverPids().FirstOrDefault();
        if (pid == 0) return NoBridgeError();
        try { return Send(pid, "{\"cmd\":\"dump_widget_concretes\"}"); }
        catch (Exception e) { return "ERROR: " + e.GetType().Name + ": " + e.Message; }
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
