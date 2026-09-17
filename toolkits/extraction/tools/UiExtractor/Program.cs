// UiExtractor - generic, parameterized OutSystems UI (presentation-layer) extractor.
//
// Captures the full presentation layer of ANY OutSystems module: themes & CSS,
// screens, web blocks, the hierarchical widget tree, events/handlers, and
// screen-scoped client actions with link-traced flows.
//
// 100% generic - no module name is hardcoded. The target module is resolved at
// runtime from (in priority order): --module CLI flag, OUTSYSTEMS_MODULE env
// var, or a local .env file. It is matched against the module open in a running
// Service Studio instance.
//
// Extraction method: ClrMD read-only heap walk (bypasses the .oml productKey
// gate - same proven approach as the other extractors in this repo).
//
// Usage:
//   dotnet run -c Release -- --module=<ModuleName>
//   dotnet run -c Release -- --module=<ModuleName> --pid=<PID>
//   set OUTSYSTEMS_MODULE=<ModuleName> && dotnet run -c Release --
//
// Output (under <output-dir>/<ModuleName>/, default docs/<ModuleName>/):
//   <ThemeName>.css      one raw CSS file per theme (main + invisible stylesheet)
//   ui-tree.txt          visual widget-tree map of every screen & web block
//   metadata.json        screens/blocks, parameters, variables, events (no widget tree)
//   client-actions.json  client action flows, JS nodes, step sequences
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Diagnostics.Runtime;

// ---------- resolve module name: CLI flag > env var > .env file ----------
string moduleName = null;
int? pidArg = null;
string outputBase = null;
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    string GetVal() => a.Contains('=') ? a.Substring(a.IndexOf('=') + 1).Trim('"') : (i + 1 < args.Length ? args[++i].Trim('"') : null);
    if (a.StartsWith("--module", StringComparison.OrdinalIgnoreCase)) moduleName = GetVal();
    else if (a.StartsWith("--pid", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(GetVal(), out var p)) pidArg = p; }
    else if (a.StartsWith("--output-dir", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--out", StringComparison.OrdinalIgnoreCase)) outputBase = GetVal();
}
// fall back to environment variable
if (string.IsNullOrEmpty(moduleName))
    moduleName = Environment.GetEnvironmentVariable("OUTSYSTEMS_MODULE");
// fall back to .env file next to the project / current directory
if (string.IsNullOrEmpty(moduleName))
{
    foreach (var envPath in new[] { Path.Combine(Directory.GetCurrentDirectory(), ".env"), Path.Combine(AppContext.BaseDirectory, ".env") })
    {
        if (!File.Exists(envPath)) continue;
        foreach (var line in File.ReadAllLines(envPath))
        {
            var l = line.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            var eq = l.IndexOf('=');
            if (eq <= 0) continue;
            if (string.Equals(l.Substring(0, eq).Trim(), "OUTSYSTEMS_MODULE", StringComparison.OrdinalIgnoreCase))
            { moduleName = l.Substring(eq + 1).Trim().Trim('"'); break; }
        }
        if (!string.IsNullOrEmpty(moduleName)) break;
    }
}
if (string.IsNullOrEmpty(moduleName))
{
    Console.Error.WriteLine("Usage: UiExtractor --module=<ModuleName> [--pid=<PID>] [--output-dir=<path>]");
    Console.Error.WriteLine("  or set OUTSYSTEMS_MODULE in the environment / a .env file.");
    Console.Error.WriteLine("Example: UiExtractor --module=MyApp");
    return 1;
}
if (string.IsNullOrEmpty(outputBase)) outputBase = Path.Combine(Directory.GetCurrentDirectory(), "docs");

// ---------- locate the Service Studio process running the requested module ----------
Console.WriteLine($"UI extraction requested for module '{moduleName}'.");
int pid;
if (pidArg.HasValue)
{
    pid = pidArg.Value;
    Console.WriteLine($"Using explicit PID {pid} (module name will be verified).");
}
else
{
    pid = FindPidForModule(moduleName);
    if (pid == 0)
    {
        Console.Error.WriteLine($"Could not find a running Service Studio instance with module '{moduleName}' open.");
        Console.Error.WriteLine("Open the module in Service Studio, or pass --pid=<PID>.");
        return 1;
    }
}

Console.WriteLine($"Attaching (read-only) to Service Studio PID {pid}...");
using var target = DataTarget.AttachToProcess(pid, suspend: false);
var runtime = target.ClrVersions[0].CreateRuntime();
var heap = runtime.Heap;
Console.WriteLine($"Heap ready. CanWalkHeap={heap.CanWalkHeap}");

// ClrMD 3.0 ReadString truncates strings at 4096 chars. These offsets are
// used by ReadStrManual to read .NET strings directly from process memory.
int _stringLengthOffset = IntPtr.Size;
int _firstCharOffset = IntPtr.Size + 4;

// ---------- generic ClrMD field readers ----------
string ReadStrManual(ClrObject o, string want)
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
string ReadStr(ClrObject o, string want)
{
    if (o.IsNull) return null;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String" && f.Name == want)
            { try {
                var s = f.ReadString(o.Address, false);
                if (s != null && s.Length >= 4096)
                {
                    var manual = ReadStrManual(o, want);
                    if (manual != null && manual.Length > s.Length) return manual;
                }
                if (!string.IsNullOrEmpty(s)) return s;
            } catch { } }
    return null;
}
ClrObject ReadRef(ClrObject o, string want)
{
    if (o.IsNull) return default;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Name == want)
            { try { var c = f.ReadObject(o.Address, false); if (!c.IsNull) return c; } catch { } }
    return default;
}
int ReadInt(ClrObject o, string want)
{
    if (o.IsNull) return 0;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (!f.IsObjectReference && f.Name == want)
            { try { return f.Read<int>(o.Address, false); } catch { } }
    return 0;
}
bool ReadBool(ClrObject o, string want)
{
    if (o.IsNull) return false;
    for (var t = o.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (!f.IsObjectReference && f.Name == want)
            { try { return f.Read<bool>(o.Address, false); } catch { } }
    return false;
}
static string Leaf(string fn)
{
    if (string.IsNullOrEmpty(fn)) return "?";
    return fn.Contains("+") ? fn.Split('+').Last() : fn.Contains(".") ? fn.Split('.').Last() : fn;
}
List<ClrObject> ReadColl(ClrObject coll)
{
    var result = new List<ClrObject>();
    if (coll.IsNull) return result;
    var arrObj = ReadRef(coll, "_items"); if (arrObj.IsNull) arrObj = ReadRef(coll, "array");
    var size = ReadInt(coll, "_size"); if (size <= 0) size = ReadInt(coll, "size");
    if (!arrObj.IsNull && arrObj.IsArray && size > 0)
    {
        var arr = arrObj.AsArray();
        for (int i = 0; i < Math.Min(size, arr.Length); i++)
        { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { } }
    }
    return result;
}
// ReadCollAll: reads ALL elements in the backing array (arr.Length), not just _size.
// Used for LightweightExpression.lightweightElements where _size may be stale/wrong,
// causing truncated CSS reconstruction. Also tries additional backing-array field names.
List<ClrObject> ReadCollAll(ClrObject coll)
{
    var result = new List<ClrObject>();
    if (coll.IsNull) return result;
    var arrObj = ReadRef(coll, "_items");
    if (arrObj.IsNull) arrObj = ReadRef(coll, "array");
    if (arrObj.IsNull) arrObj = ReadRef(coll, "_data");
    if (arrObj.IsNull) arrObj = ReadRef(coll, "_buffer");
    if (arrObj.IsNull) arrObj = ReadRef(coll, "_elements");
    if (arrObj.IsNull || !arrObj.IsArray) return result;
    var arr = arrObj.AsArray();
    for (int i = 0; i < arr.Length; i++)
    { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var elem = heap.GetObject((ulong)ptr.ToInt64()); if (!elem.IsNull) result.Add(elem); } catch { } }
    return result;
}
string ReadRefObjName(ClrObject refObj)
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
string ReadElementText(ClrObject elem, int depth = 0)
{
    if (elem.IsNull || depth > 6) return "?";
    var elemType = Leaf(elem.Type?.Name ?? "?");
    switch (elemType)
    {
        case "TextLiteral": return "\"" + (ReadStr(elem, "value") ?? "") + "\"";
        case "IntegerLiteral": return ReadStr(elem, "value") ?? "?";
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
            var left = ReadElementText(ReadRef(elem, "leftSide"), depth + 1);
            var right = ReadElementText(ReadRef(elem, "rightSide"), depth + 1);
            var opVal = ReadInt(elem, "operator");
            var op = opVal switch { 0 => "+", 1 => "-", 2 => "*", 3 => "/", 4 => "mod", 5 => "=", 6 => "<>", 7 => "<", 8 => "<=", 9 => ">", 10 => ">=", 11 => "and", 12 => "or", 13 => "&", _ => $"op{opVal}" };
            return $"{left} {op} {right}";
        }
        case "CallFunction":
        {
            var r = ReadRef(elem, "reference"); var funcName = ""; if (!r.IsNull) funcName = ReadRefObjName(r) ?? "";
            var args = ReadRef(elem, "arguments"); var argTexts = new List<string>();
            if (!args.IsNull && args.IsArray) { var arr = args.AsArray(); for (int i = 0; i < arr.Length; i++) { try { var ptr = arr.GetValue<IntPtr>(i); if (ptr == IntPtr.Zero) continue; var argElem = heap.GetObject((ulong)ptr.ToInt64()); if (argElem.IsNull) continue; var inner = ReadRef(argElem, "argumentExpressionElement"); if (!inner.IsNull) argTexts.Add(ReadElementText(inner, depth + 1)); } catch { } } }
            return $"{funcName}({string.Join(", ", argTexts)})";
        }
    }
    for (var t = elem.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String")
            { try { var s = f.ReadString(elem.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 500) return s; } catch { } }
    return elemType;
}
string ReadExpressionText(ClrObject parsedExpr)
{
    if (parsedExpr.IsNull) return null;
    var elem = ReadRef(parsedExpr, "expressionElement");
    if (elem.IsNull) return null;
    return ReadElementText(elem);
}
string ReadType(ClrObject obj)
{
    var typeObj = ReadRef(obj, "_type");
    if (typeObj.IsNull) return null;
    return ReadStr(typeObj, "_name") ?? Leaf(typeObj.Type?.Name ?? "Unknown");
}
string ReadDestinationName(ClrObject dest)
{
    if (dest.IsNull) return null;
    var n = ReadStr(dest, "_name") ?? ReadStr(dest, "name");
    if (!string.IsNullOrEmpty(n)) return n;
    return ReadRefObjName(dest);
}
static int FindPidForModule(string want)
{
    var procs = Process.GetProcessesByName("ServiceStudio");
    if (procs.Length == 0) return 0;
    foreach (var p in procs)
    {
        try
        {
            using var t = DataTarget.AttachToProcess(p.Id, suspend: false);
            var rt = t.ClrVersions[0].CreateRuntime();
            var h = rt.Heap;
            string found = null;
            foreach (var obj in h.EnumerateObjects())
            {
                var tp = obj.Type; if (tp is null) continue;
                var fn = tp.Name ?? "";
                if (!fn.EndsWith(".OmlHeader", StringComparison.Ordinal)) continue;
                for (var bt = tp; bt != null && found == null; bt = bt.BaseType)
                    foreach (var f in bt.Fields)
                        if (f.IsObjectReference && f.Type?.Name == "System.String")
                        { try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && s.IndexOfAny(new[] { '|', '{', ';', '/', '+', '=' }) < 0) { found = s; break; } } catch { } }
                break;
            }
            Console.WriteLine($"  PID {p.Id}: open module = '{found ?? "(unknown)"}'");
            if (found != null && string.Equals(found, want, StringComparison.OrdinalIgnoreCase)) return p.Id;
        }
        catch (Exception ex) { Console.WriteLine($"  PID {p.Id}: could not read ({ex.Message})"); }
    }
    return 0;
}

// ====================================================================
//  SINGLE HEAP WALK - collect all UI elements
// ====================================================================
Console.WriteLine("Scanning heap for UI elements...");
string liveModuleName = null;
var themes = new List<ClrObject>();
var screens = new List<ClrObject>();
var blocks = new List<ClrObject>();
var styleSheets = new Dictionary<ulong, ClrObject>();
var widgetsByAddr = new Dictionary<ulong, ClrObject>();
var widgetChildren = new Dictionary<ulong, List<ClrObject>>();
var eventsByWidget = new Dictionary<ulong, List<ClrObject>>();
var clientActionsByParent = new Dictionary<ulong, List<ClrObject>>();
var nodesByParent = new Dictionary<ulong, List<ClrObject>>();
var linksBySource = new Dictionary<ulong, List<(ulong target, string linkType)>>();
var styleSheetsByParent = new Dictionary<ulong, List<ClrObject>>();

bool IsSkip(string fn) => fn.Contains('[') || fn.Contains("+<>c") || fn.Contains("Enumerator") || fn.Contains("Iterator") || fn.Contains("Descriptor");
bool IsWidget(string fn) => fn.StartsWith("ServiceStudio.Model.NRWebWidgets+", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.WebWidgets+", StringComparison.Ordinal);
bool IsConcreteWidget(string leaf) => !leaf.StartsWith("Reference") && !leaf.StartsWith("From") && !leaf.StartsWith("Concrete_") && leaf != "Kind" && !leaf.Contains("Conversion");

foreach (var obj in heap.EnumerateObjects())
{
    var t = obj.Type; if (t is null) continue;
    var fn = t.Name ?? "";
    if (IsSkip(fn)) continue;
    var leaf = Leaf(fn);

    if (fn.EndsWith(".OmlHeader", StringComparison.Ordinal))
    {
        if (liveModuleName == null)
            for (var bt = t; bt != null && liveModuleName == null; bt = bt.BaseType)
                foreach (var f in bt.Fields)
                    if (f.IsObjectReference && f.Type?.Name == "System.String")
                    { try { var s = f.ReadString(obj.Address, false); if (!string.IsNullOrEmpty(s) && s.Length < 60 && s.IndexOfAny(new[] { '|', '{', ';', '/', '+', '=' }) < 0) { liveModuleName = s; break; } } catch { } }
        continue;
    }
    if (fn == "ServiceStudio.Model.NewRuntime.Theme" || fn == "ServiceStudio.Model.Theme")
    { themes.Add(obj); continue; }
    if (leaf == "WebScreen" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
    { screens.Add(obj); continue; }
    if (leaf == "WebBlock" && (fn.StartsWith("ServiceStudio.Model.NRNodes", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.Nodes", StringComparison.Ordinal)))
    { blocks.Add(obj); continue; }
    if (fn == "ServiceStudio.Model.WebStyleSheet" || fn == "ServiceStudio.Model.ReferenceWebStyleSheet" || fn == "ServiceStudio.Model.InvisibleWebStyleSheet" || fn == "ServiceStudio.Model.ReferenceInvisibleWebStyleSheet")
    {
        if (!styleSheets.ContainsKey(obj.Address)) styleSheets[obj.Address] = obj;
        // Store by parent (block/screen) for UI tree CSS attribution
        var ssParent = ReadRef(obj, "parent");
        if (!ssParent.IsNull)
        {
            if (!styleSheetsByParent.ContainsKey(ssParent.Address)) styleSheetsByParent[ssParent.Address] = new List<ClrObject>();
            styleSheetsByParent[ssParent.Address].Add(obj);
        }
        continue;
    }
    if (IsWidget(fn) && IsConcreteWidget(leaf))
    {
        widgetsByAddr[obj.Address] = obj;
        var parent = ReadRef(obj, "parent");
        if (parent.IsNull) parent = ReadRef(obj, "owner");
        if (!parent.IsNull)
        {
            if (!widgetChildren.ContainsKey(parent.Address)) widgetChildren[parent.Address] = new List<ClrObject>();
            widgetChildren[parent.Address].Add(obj);
        }
        continue;
    }
    if (fn.StartsWith("ServiceStudio.Model.NRWebWidgetEvents+", StringComparison.Ordinal) || fn.StartsWith("ServiceStudio.Model.WebWidgetEvents+", StringComparison.Ordinal))
    {
        if (leaf == "EventHandler" || leaf.Contains("OnClick"))
        {
            var parent = ReadRef(obj, "parent");
            if (!parent.IsNull)
            {
                if (!eventsByWidget.ContainsKey(parent.Address)) eventsByWidget[parent.Address] = new List<ClrObject>();
                eventsByWidget[parent.Address].Add(obj);
            }
        }
        continue;
    }
    if (fn == "ServiceStudio.Model.NRFlows+ClientScreenActionFlow" || fn == "ServiceStudio.Model.NRFlows+ClientActionFlow" || fn == "ServiceStudio.Model.NRFlows+DataScreenActionFlow")
    {
        var owner = ReadRef(obj, "parent");
        if (!owner.IsNull)
        {
            if (!clientActionsByParent.ContainsKey(owner.Address)) clientActionsByParent[owner.Address] = new List<ClrObject>();
            clientActionsByParent[owner.Address].Add(obj);
        }
        continue;
    }
    // flow nodes - Traditional (Nodes+*) and Reactive (NRNodes+*) including JS nodes
    if (fn.StartsWith("ServiceStudio.Model.Nodes+", StringComparison.Ordinal)
        || fn.StartsWith("ServiceStudio.Model.NRNodes+", StringComparison.Ordinal)
        || fn.Contains(".Nodes+"))
    {
        if (leaf == "Kind" || leaf.StartsWith("From") || leaf.Contains("Conversion")) continue;
        var parent = ReadRef(obj, "parent");
        if (!parent.IsNull)
        {
            if (!nodesByParent.ContainsKey(parent.Address)) nodesByParent[parent.Address] = new List<ClrObject>();
            nodesByParent[parent.Address].Add(obj);
        }
        continue;
    }
    if (fn.StartsWith("ServiceStudio.Model.Links+", StringComparison.Ordinal) || fn.Contains(".Links+"))
    {
        var src = ReadRef(obj, "parent");
        var tgt = ReadRef(obj, "_targetNode");
        if (!src.IsNull && !tgt.IsNull)
        {
            if (!linksBySource.ContainsKey(src.Address)) linksBySource[src.Address] = new List<(ulong, string)>();
            linksBySource[src.Address].Add((tgt.Address, leaf));
        }
        continue;
    }
}

if (!string.IsNullOrEmpty(liveModuleName) && !string.Equals(liveModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"WARNING: requested module '{moduleName}' but open module is '{liveModuleName}'. Extracting the open module anyway.");
    moduleName = liveModuleName;
}
Console.WriteLine($"Module: {moduleName}");
Console.WriteLine($"  Themes={themes.Count}  Screens={screens.Count}  WebBlocks={blocks.Count}  StyleSheets={styleSheets.Count}");
Console.WriteLine($"  Widgets={widgetsByAddr.Count}  WidgetEvents={eventsByWidget.Values.Sum(l => l.Count)}  ClientActions={clientActionsByParent.Values.Sum(l => l.Count)}");
Console.WriteLine($"  FlowNodes={nodesByParent.Values.Sum(l => l.Count)}  FlowLinks={linksBySource.Values.Sum(l => l.Count)}");

// ---------- CSS / style helpers ----------
string ResolveLightweightRef(ClrObject refElem)
{
    if (refElem.IsNull) return null;
    var directName = ReadStr(refElem, "_name") ?? ReadStr(refElem, "name") ?? ReadStr(refElem, "refName") ?? ReadStr(refElem, "_refName");
    if (!string.IsNullOrEmpty(directName)) return directName;
    foreach (var fn in new[] { "reference", "_reference", "referedObject", "_referedObject", "referencedObject", "_target", "target", "value", "_value" })
    {
        var r = ReadRef(refElem, fn);
        if (r.IsNull) continue;
        var n = ReadStr(r, "_name") ?? ReadStr(r, "name") ?? ReadStr(r, "refName");
        if (!string.IsNullOrEmpty(n)) return n;
        for (var t = r.Type; t != null; t = t.BaseType)
            foreach (var f in t.Fields)
                if (f.IsObjectReference && f.Type?.Name != "System.String")
                { try { var v = f.ReadObject(r.Address, false); if (!v.IsNull) { var vn = ReadStr(v, "_name") ?? ReadStr(v, "name"); if (!string.IsNullOrEmpty(vn)) return vn; } } catch { } }
    }
    for (var t = refElem.Type; t != null; t = t.BaseType)
        foreach (var f in t.Fields)
            if (f.IsObjectReference && f.Type?.Name == "System.String")
            { try { var s = f.ReadString(refElem.Address, false); if (!string.IsNullOrEmpty(s)) return s; } catch { } }
    return "?";
}
string ReadLightweightCss(ClrObject expr)
{
    if (expr.IsNull) return null;
    var elements = ReadRef(expr, "lightweightElements");
    var list = ReadCollAll(elements);
    if (list.Count == 0) return null;
    var sb = new StringBuilder();
    foreach (var el in list)
    {
        var leaf = Leaf(el.Type?.Name ?? "?");
        if (leaf == "TextElement")
        {
            var text = ReadStr(el, "text") ?? ReadStr(el, "_text") ?? ReadStr(el, "value") ?? ReadStr(el, "_value") ?? ReadStr(el, "Text") ?? ReadStr(el, "Value");
            if (text == null)
            {
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
            var suffix = ReadStr(el, "suffix") ?? ReadStr(el, "_suffix");
            if (suffix != null) sb.Append(suffix);
        }
        else
        {
            var text = ReadStr(el, "text") ?? ReadStr(el, "value");
            if (text != null) sb.Append(text);
        }
    }
    return sb.Length == 0 ? null : sb.ToString();
}
string ReadCss(ClrObject styleSheet)
{
    if (styleSheet.IsNull) return null;
    // Try ALL CSS sources and pick the LONGEST non-empty result.
    // _finalCssSource is a lazily-computed cache that may be non-empty but PARTIAL;
    // taking the first non-empty result would shadow _cssSource (the original full source).
    string best = null;
    // Traditional WebStyleSheet — full CSS string
    var dtv = ReadStr(styleSheet, "designTimeValue");
    if (!string.IsNullOrEmpty(dtv) && (best == null || dtv.Length > best.Length)) best = dtv;
    // LightweightExpression sources (try all, pick longest = most complete)
    foreach (var fn in new[] { "_finalCssSource", "_generatedCssSource", "_cssSource", "_userCssSource" })
    {
        var css = ReadLightweightCss(ReadRef(styleSheet, fn));
        if (!string.IsNullOrEmpty(css) && (best == null || css.Length > best.Length)) best = css;
    }
    return best;
}
string Cap(string s)
{
    if (string.IsNullOrEmpty(s)) return s;
    const int max = 500000;
    return s.Length <= max ? s : s.Substring(0, max) + $"\n/* ... truncated ({s.Length} chars total) ... */";
}

// ---------- parameter / variable / event readers ----------
JsonNode ReadParam(ClrObject p, string direction) => new JsonObject
{
    ["name"] = ReadStr(p, "_name") ?? "(unnamed)",
    ["dataType"] = ReadType(p),
    ["direction"] = direction,
    ["defaultValue"] = ReadExpressionText(ReadRef(p, "_defaultValue")),
    ["isMandatory"] = ReadBool(p, "_isMandatory"),
    ["description"] = ReadStr(p, "_description")
};
JsonArray ReadParams(ClrObject coll, string direction)
{
    var arr = new JsonArray();
    foreach (var p in ReadColl(coll)) arr.Add(ReadParam(p, direction));
    return arr;
}
JsonArray ReadLocalVariables(ClrObject coll)
{
    var arr = new JsonArray();
    foreach (var v in ReadColl(coll))
        arr.Add(new JsonObject { ["name"] = ReadStr(v, "_name") ?? "(unnamed)", ["dataType"] = ReadType(v), ["kind"] = Leaf(v.Type?.Name ?? "?") });
    return arr;
}
JsonArray ReadEvents(ClrObject sb)
{
    var evArr = new JsonArray();
    void AddLifecycle(string fieldName, string eventName)
    {
        var slot = ReadRef(sb, fieldName);
        if (slot.IsNull) return;
        var handler = ReadStr(slot, "_name") ?? ReadDestinationName(ReadRef(slot, "_destination"));
        evArr.Add(new JsonObject { ["name"] = eventName, ["kind"] = "System", ["handler"] = handler });
    }
    AddLifecycle("_onInitialize", "OnInitialize");
    AddLifecycle("_onReady", "OnReady");
    AddLifecycle("_onRender", "OnRender");
    AddLifecycle("_onDestroy", "OnDestroy");
    AddLifecycle("_onParametersChanged", "OnParametersChanged");
    foreach (var ce in ReadColl(ReadRef(sb, "_customEvents")))
        evArr.Add(new JsonObject
        {
            ["name"] = ReadStr(ce, "_name") ?? ReadStr(ce, "_eventName") ?? "(custom event)",
            ["kind"] = "Custom",
            ["handler"] = ReadDestinationName(ReadRef(ce, "_destination"))
        });
    return evArr;
}

// ====================================================================
//  OUTPUT 1: <ThemeName>.css per theme  (raw, clean CSS)
// ====================================================================
string SafeName(string s) => string.Join("_", s.Split(Path.GetInvalidPathChars())).Trim();
string moduleDir = Path.Combine(outputBase, SafeName(moduleName));
Directory.CreateDirectory(moduleDir);

var themeCssFiles = new List<string>();
foreach (var th in themes)
{
    var name = ReadStr(th, "_name") ?? "(unnamed)";
    var baseT = ReadRef(th, "_baseTheme");
    var cssOut = new StringBuilder();
    var mainCss = ReadCss(ReadRef(th, "_styleSheet"));
    if (!string.IsNullOrEmpty(mainCss))
    {
        cssOut.AppendLine("/* ============================================================");
        cssOut.AppendLine($"   Theme: {name}  (base: {(baseT.IsNull ? "(none)" : ReadStr(baseT, "_name") ?? "?")})");
        cssOut.AppendLine("   ============================================================ */");
        cssOut.AppendLine(Cap(mainCss));
        cssOut.AppendLine();
    }
    var iss = ReadRef(th, "_invisibleStyleSheet");
    var icss = ReadCss(iss);
    if (!string.IsNullOrEmpty(icss))
    {
        cssOut.AppendLine("/* ============================================================");
        cssOut.AppendLine($"   Theme: {name}  (invisible stylesheet)");
        cssOut.AppendLine("   ============================================================ */");
        cssOut.AppendLine(Cap(icss));
        cssOut.AppendLine();
    }
    if (cssOut.Length > 0)
    {
        var fileName = SafeName(name) + ".css";
        File.WriteAllText(Path.Combine(moduleDir, fileName), cssOut.ToString());
        themeCssFiles.Add(fileName);
        Console.WriteLine($"  -> {fileName}  ({cssOut.Length:N0} chars)");
    }
}

// ====================================================================
//  OUTPUT 2: ui-tree.txt  (visual widget-tree map)
// ====================================================================
string WidgetTypeLabel(string leaf) => leaf switch
{
    "CustomPlaceholderWidget" => "Container",
    "WebBlockInstance" => "Block",
    "PlaceholderArgument" => "Placeholder",
    "IfBranch" => "IfBranch",
    _ => leaf
};
string WidgetDisplayName(ClrObject w, out string htmlClass)
{
    var leaf = Leaf(w.Type?.Name ?? "?");
    var name = ReadStr(w, "_name") ?? ReadStr(w, "_customName");
    var type = WidgetTypeLabel(leaf);
    if (leaf == "PlaceholderArgument") { var ph = ReadRef(w, "_placeholder"); name = name ?? (ph.IsNull ? null : ReadStr(ph, "_name")); }
    else if (leaf == "WebBlockInstance") { var src = ReadRef(w, "_sourceWebBlock"); name = src.IsNull ? name : (ReadStr(src, "_name") ?? ReadRefObjName(src)); }
    htmlClass = ReadStr(w, "_customStyle");
    return string.IsNullOrEmpty(name) ? $"({type})" : $"{name} ({type})";
}
string WidgetEventLabel(ClrObject ev)
{
    var leaf = Leaf(ev.Type?.Name ?? "?");
    var evName = ReadStr(ev, "_name") ?? ReadStr(ev, "_eventName");
    if (string.IsNullOrEmpty(evName)) evName = leaf == "OnClick" ? "OnClick" : leaf;
    var handler = ReadDestinationName(ReadRef(ev, "_destination"));
    return string.IsNullOrEmpty(handler) ? evName : $"{evName}={handler}";
}
void RenderWidgets(List<ClrObject> widgets, string prefix, StringBuilder sb)
{
    for (int i = 0; i < widgets.Count; i++)
    {
        var last = i == widgets.Count - 1;
        var branch = last ? "\u2514\u2500 " : "\u251C\u2500 "; // └─ / ├─
        var label = WidgetDisplayName(widgets[i], out var cls);
        var classPart = string.IsNullOrEmpty(cls) ? "" : (cls.IndexOf(':') >= 0 || cls.IndexOf(';') >= 0 ? $" [style: {cls}]" : " ." + cls.Replace(" ", " ."));
        var eventPart = "";
        if (eventsByWidget.TryGetValue(widgets[i].Address, out var evs) && evs.Count > 0)
        {
            var evLabels = evs.Select(e => { try { return WidgetEventLabel(e); } catch { return "?"; } }).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (evLabels.Length > 0) eventPart = " [" + string.Join(", ", evLabels) + "]";
        }
        sb.AppendLine(prefix + branch + label + classPart + eventPart);
        var kids = widgetChildren.TryGetValue(widgets[i].Address, out var c) ? c : new List<ClrObject>();
        // fall back to explicit _childWidgets for container variants
        if (kids.Count == 0) kids = ReadColl(ReadRef(widgets[i], "_childWidgets"));
        RenderWidgets(kids.OrderBy(x => ReadStr(x, "_name") ?? "").ThenBy(x => Leaf(x.Type?.Name ?? "?")).ToList(), prefix + (last ? "   " : "\u2502  "), sb);
    }
}
var treeOut = new StringBuilder();
void RenderScreenOrBlock(ClrObject sb, string kind)
{
    var nm = ReadStr(sb, "_name") ?? "(unnamed)";
    treeOut.AppendLine(new string('=', 60));
    treeOut.AppendLine($"{kind}: {nm}");
    treeOut.AppendLine(new string('=', 60));
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
    foreach (var ce in ReadColl(ReadRef(sb, "_customEvents")))
    {
        var ceName = ReadStr(ce, "_name") ?? ReadStr(ce, "_eventName") ?? "(custom event)";
        var handler = ReadDestinationName(ReadRef(ce, "_destination"));
        lifecycleEvents.Add(string.IsNullOrEmpty(handler) ? ceName : $"{ceName}={handler}");
    }
    if (lifecycleEvents.Count > 0) treeOut.AppendLine($"Events: {string.Join(", ", lifecycleEvents)}");
    // Block/screen's own stylesheet CSS
    if (styleSheetsByParent.TryGetValue(sb.Address, out var ssForThis) && ssForThis.Count > 0)
    {
        var cssParts = new List<string>();
        foreach (var ss in ssForThis)
        {
            var css = ReadCss(ss);
            if (!string.IsNullOrEmpty(css)) cssParts.Add(css);
        }
        if (cssParts.Count > 0)
        {
            treeOut.AppendLine("CSS:");
            treeOut.AppendLine(string.Join("\n", cssParts));
        }
    }
    var top = ReadColl(ReadRef(sb, "_widgets"));
    if (top.Count == 0) treeOut.AppendLine("  (empty)");
    else RenderWidgets(top, "", treeOut);
    treeOut.AppendLine();
}
foreach (var sb in screens.OrderBy(x => ReadStr(x, "_name") ?? "")) RenderScreenOrBlock(sb, "SCREEN");
foreach (var sb in blocks.OrderBy(x => ReadStr(x, "_name") ?? "")) RenderScreenOrBlock(sb, "WEB BLOCK");
File.WriteAllText(Path.Combine(moduleDir, "ui-tree.txt"), treeOut.ToString());
Console.WriteLine($"  -> ui-tree.txt  ({treeOut.Length:N0} chars)");

// ====================================================================
//  OUTPUT 3: metadata.json  (screens/blocks, params, vars, events - no widget tree)
// ====================================================================
JsonObject BuildMetadataEntry(ClrObject sb, string kind)
{
    var o = new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["type"] = kind };
    o["description"] = ReadStr(sb, "_description");
    o["urlPath"] = ReadStr(sb, "_pageName") ?? ReadStr(sb, "_url");
    o["isPublic"] = ReadBool(sb, "_public");
    var permArr = new JsonArray();
    foreach (var perm in ReadColl(ReadRef(sb, "_permissions")))
    {
        var role = ReadRef(perm, "_role");
        var rn = role.IsNull ? ReadRefObjName(ReadRef(perm, "_referedObject")) : (ReadStr(role, "_name") ?? ReadRefObjName(role));
        if (!string.IsNullOrEmpty(rn)) permArr.Add(rn);
    }
    o["permissions"] = permArr;
    o["inputParameters"] = ReadParams(ReadRef(sb, "_inputParameters"), "In");
    o["outputParameters"] = ReadParams(ReadRef(sb, "_outputParameters"), "Out");
    o["localVariables"] = ReadLocalVariables(ReadRef(sb, "_localVariables"));
    o["events"] = ReadEvents(sb);
    return o;
}
var metadata = new JsonObject
{
    ["module"] = moduleName,
    ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    ["source"] = new JsonObject { ["method"] = "ClrMD", ["pid"] = pid },
    ["screens"] = BuildArray(screens, "Screen", BuildMetadataEntry),
    ["webBlocks"] = BuildArray(blocks, "WebBlock", BuildMetadataEntry)
};
WriteJson(Path.Combine(moduleDir, "metadata.json"), metadata);
Console.WriteLine($"  -> metadata.json");

// ====================================================================
//  OUTPUT 4: client-actions.json  (flows, JS nodes, step sequences)
// ====================================================================
JsonObject BuildClientActionEntry(ClrObject ca) => new JsonObject
{
    ["name"] = ReadStr(ca, "_name") ?? "(unnamed)",
    ["description"] = ReadStr(ca, "_description"),
    ["inputs"] = ReadParams(ReadRef(ca, "_inputParameters"), "In"),
    ["outputs"] = ReadParams(ReadRef(ca, "_outputParameters"), "Out"),
    ["localVariables"] = ReadLocalVariables(ReadRef(ca, "_localVariables")),
    ["flow"] = TraceFlow(ca)
};
JsonArray BuildClientActionsFor(ClrObject sb)
{
    var arr = new JsonArray();
    if (clientActionsByParent.TryGetValue(sb.Address, out var owned))
        foreach (var ca in owned.OrderBy(x => ReadStr(x, "_name") ?? ""))
            arr.Add(BuildClientActionEntry(ca));
    return arr;
}
var clientActions = new JsonObject
{
    ["module"] = moduleName,
    ["extractedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    ["screens"] = ActionsArray(screens),
    ["webBlocks"] = ActionsArray(blocks)
};
WriteJson(Path.Combine(moduleDir, "client-actions.json"), clientActions);
Console.WriteLine($"  -> client-actions.json");

Console.WriteLine($"\nOutput written to: {moduleDir}");
return 0;

// ====================================================================
//  shared builders
// ====================================================================
JsonArray BuildArray(List<ClrObject> list, string kind, Func<ClrObject, string, JsonObject> builder)
{
    var arr = new JsonArray();
    foreach (var sb in list.OrderBy(x => ReadStr(x, "_name") ?? ""))
    { try { arr.Add(builder(sb, kind)); } catch (Exception ex) { arr.Add(new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["type"] = kind, ["error"] = ex.Message }); } }
    return arr;
}
JsonArray ActionsArray(List<ClrObject> list)
{
    var arr = new JsonArray();
    foreach (var sb in list.OrderBy(x => ReadStr(x, "_name") ?? ""))
        arr.Add(new JsonObject { ["name"] = ReadStr(sb, "_name") ?? "(unnamed)", ["clientActions"] = BuildClientActionsFor(sb) });
    return arr;
}
void WriteJson(string path, JsonNode node)
{
    using var ms = new MemoryStream();
    using (var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        node.WriteTo(writer);
    File.WriteAllBytes(path, ms.ToArray());
}

// ---------- flow tracer (DFS from Start via links) ----------
JsonArray TraceFlow(ClrObject action)
{
    var arr = new JsonArray();
    List<ClrObject> nodes;
    if (!nodesByParent.TryGetValue(action.Address, out nodes) || nodes.Count == 0)
    {
        nodes = new List<ClrObject>();
        nodes.AddRange(ReadColl(ReadRef(action, "_nodesShownInESpaceTree")));
        nodes.AddRange(ReadColl(ReadRef(action, "_nodesNotShownInESpaceTree")));
    }
    if (nodes.Count == 0) return arr;
    var nodeDict = nodes.ToDictionary(n => n.Address, n => n);
    var visited = new HashSet<ulong>();
    int counter = 1;
    string NType(ulong a) => nodeDict.TryGetValue(a, out var n) ? Leaf(n.Type?.Name ?? "?") : "?";
    string NName(ulong a) => nodeDict.TryGetValue(a, out var n) ? (ReadStr(n, "_name") ?? ReadStr(n, "_customName")) : null;
    string NAction(ulong a)
    {
        if (!nodeDict.TryGetValue(a, out var n)) return null;
        var nt = Leaf(n.Type?.Name ?? "?");
        if (nt == "ExecuteAction" || nt == "ExecuteClientAction")
        { var ac = ReadRef(n, "_action"); if (!ac.IsNull) return ReadStr(ac, "_name") ?? ReadRefObjName(ac); var ca = ReadRef(n, "_clientAction"); return ca.IsNull ? null : (ReadStr(ca, "_name") ?? ReadRefObjName(ca)); }
        return null;
    }
    List<string> NAssign(ulong a)
    {
        var list = new List<string>();
        if (!nodeDict.TryGetValue(a, out var n)) return list;
        if (Leaf(n.Type?.Name ?? "?") == "Assign")
            foreach (var asg in ReadColl(ReadRef(n, "_assignments")))
                list.Add($"{ReadExpressionText(ReadRef(asg, "_variable")) ?? "?"} = {ReadExpressionText(ReadRef(asg, "_value")) ?? "?"}");
        return list;
    }
    string NDetails(ulong a)
    {
        if (!nodeDict.TryGetValue(a, out var n)) return null;
        var nt = Leaf(n.Type?.Name ?? "?");
        if (nt == "Comment") return ReadStr(n, "_text");
        if (nt == "JavascriptNode" || nt == "JSNode")
            return ReadStr(n, "_sourceCode") ?? ReadStr(n, "_javascript") ?? ReadStr(n, "_script") ?? ReadStr(n, "_body");
        return null;
    }
    void Trace(ulong addr, int indent, string label)
    {
        if (visited.Contains(addr)) { arr.Add(Step(counter++, indent, label, NType(addr), "(loops back)", null, null, null)); return; }
        visited.Add(addr);
        if (!nodeDict.TryGetValue(addr, out var node)) return;
        arr.Add(Step(counter++, indent, label, Leaf(node.Type?.Name ?? "?"), NName(addr), NAction(addr), NAssign(addr), NDetails(addr)));
        if (!linksBySource.TryGetValue(addr, out var links) || links.Count == 0) return;
        foreach (var lk in links.Where(l => l.linkType == "Cycle")) { arr.Add(Step(counter++, indent, "Cycle", "Cycle", null, null, null, null)); Trace(lk.target, indent + 1, ""); }
        foreach (var lk in links.Where(l => l.linkType == "Condition")) { arr.Add(Step(counter++, indent, "Condition", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); }
        foreach (var lk in links.Where(l => l.linkType == "Otherwise")) { arr.Add(Step(counter++, indent, "Otherwise", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); }
        foreach (var lk in links.Where(l => l.linkType == "True")) { arr.Add(Step(counter++, indent, "True", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); }
        foreach (var lk in links.Where(l => l.linkType == "False")) { arr.Add(Step(counter++, indent, "False", NType(lk.target), NName(lk.target), null, null, null)); Trace(lk.target, indent + 1, ""); }
        foreach (var lk in links.Where(l => l.linkType == "Sequence")) Trace(lk.target, indent, "");
    }
    var start = nodes.FirstOrDefault(n => Leaf(n.Type?.Name ?? "?") == "Start");
    if (!start.IsNull) Trace(start.Address, 0, null);
    else arr.Add(Step(counter++, 0, null, "Start", "(no Start node found)", null, null, null));
    foreach (var n in nodes.Where(n => !visited.Contains(n.Address)))
        arr.Add(Step(counter++, 1, "Unvisited", Leaf(n.Type?.Name ?? "?"), NName(n.Address), NAction(n.Address), NAssign(n.Address), NDetails(n.Address)));
    return arr;
}
JsonObject Step(int step, int indent, string label, string nodeType, string name, string actionRef, List<string> assignments, string details)
{
    var o = new JsonObject { ["step"] = step, ["indent"] = indent, ["nodeType"] = nodeType };
    o["label"] = label;
    o["name"] = name;
    o["actionRef"] = actionRef;
    var aa = new JsonArray();
    if (assignments != null) foreach (var a in assignments) aa.Add(a);
    o["assignments"] = aa;
    o["details"] = details;
    return o;
}
