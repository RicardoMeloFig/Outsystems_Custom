// OutSystemsMcpOmlEditor - a self-contained MCP (Model Context Protocol) stdio
// server exposing headless OutSystems .oml editing tools. No Service Studio UI,
// no running process, no productKey. Edits .oml FILES: load via
// Oml.LoadWithoutUpgrades(bytes, ""), mutate XML fragments, regenerate
// signatures, write a valid .oml. Verify offline (reload + IsValidOml).
//
// Workflow: Save the module in SS (Ctrl+S) -> edit the .oml here -> reload in SS.
// Requires SS 11 installed (the editor loads its model DLLs from the install dir
// via Assembly.LoadFrom); SS need not be running.
using System;
using System.Text.Json.Nodes;

namespace OutSystemsMcpOmlEditor;

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
                    ["serverInfo"] = new JsonObject { ["name"] = "outsystems-omleditor", ["version"] = "1.0" } });
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
            ["name"] = "probe_oml",
            ["description"] = "Inspect a .oml file headlessly: list all fragments, IsValidOml, header Valid/IsValid, productKey cache. Use first to understand a module's structure before editing. No SS running needed.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["omlPath"] = Str("omlPath", "Path to the .oml file") },
                ["required"] = new JsonArray { "omlPath" } }
        },
        new JsonObject {
            ["name"] = "get_fragment",
            ["description"] = "Dump the XML of one fragment from a .oml (e.g. 'ServiceAPIMethods', 'UserActions', 'eSpace', 'References', 'NodesNotShownInESpaceTree#<key>'). Read-only.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["omlPath"] = Str("omlPath", "Path to the .oml file"), ["fragment"] = Str("fragment", "Fragment name") },
                ["required"] = new JsonArray { "omlPath", "fragment" } }
        },
        new JsonObject {
            ["name"] = "scan_oml",
            ["description"] = "List fragments whose XML contains a needle (e.g. an element name, key, or tag). Read-only search.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject { ["omlPath"] = Str("omlPath", "Path to the .oml file"), ["needle"] = Str("needle", "Text to search for") },
                ["required"] = new JsonArray { "omlPath", "needle" } }
        },
        new JsonObject {
            ["name"] = "create_service_action",
            ["description"] = "Create a Service Action in a .oml headlessly. Clones an existing ServiceAPIMethod (and its Start->End flow fragment with remapped node keys) when the module already has service actions; otherwise builds the ServiceAPIMethods fragment from scratch. Regenerates signatures, writes outOml, verifies IsValidOml + read-back. Open outOml in SS to see the new action.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["omlPath"] = Str("omlPath", "Path to the source .oml"),
                    ["outOml"] = Str("outOml", "Path to write the modified .oml"),
                    ["name"] = Str("name", "New service action name"),
                    ["templateName"] = Str("templateName", "Existing service action to clone (omit to clone the first / build from scratch)") },
                ["required"] = new JsonArray { "omlPath", "outOml", "name" } }
        },
        new JsonObject {
            ["name"] = "add_dependency",
            ["description"] = "Append a <Reference> element (provided as XML) to the References fragment, bump Count, regenerate signatures, write outOml, verify. Used to add a module dependency headlessly.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["omlPath"] = Str("omlPath", "Path to the source .oml"),
                    ["outOml"] = Str("outOml", "Path to write the modified .oml"),
                    ["referenceXml"] = Str("referenceXml", "A <Reference ...> element XML to insert") },
                ["required"] = new JsonArray { "omlPath", "outOml", "referenceXml" } }
        },
        new JsonObject {
            ["name"] = "regen_and_write",
            ["description"] = "Load a .oml, regenerate signatures, and write it back to outOml. Round-trip / fixup utility. Verifies IsValidOml after write.",
            ["inputSchema"] = new JsonObject { ["type"] = "object",
                ["properties"] = new JsonObject {
                    ["omlPath"] = Str("omlPath", "Path to the source .oml"),
                    ["outOml"] = Str("outOml", "Path to write the .oml") },
                ["required"] = new JsonArray { "omlPath", "outOml" } }
        },
    };

    static string Dispatch(string name, JsonObject a)
    {
        string Arg(string k) => a?[k]?.ToString();
        return name switch
        {
            "probe_oml" => OmlEditor.Probe(Arg("omlPath")),
            "get_fragment" => OmlEditor.GetFragment(Arg("omlPath"), Arg("fragment")),
            "scan_oml" => OmlEditor.Scan(Arg("omlPath"), Arg("needle")),
            "create_service_action" => OmlEditor.CreateServiceAction(Arg("omlPath"), Arg("outOml"), Arg("name"), Arg("templateName")),
            "add_dependency" => OmlEditor.AddDependency(Arg("omlPath"), Arg("outOml"), Arg("referenceXml")),
            "regen_and_write" => OmlEditor.RegenAndWrite(Arg("omlPath"), Arg("outOml")),
            _ => "ERROR: unknown tool: " + name
        };
    }
}
