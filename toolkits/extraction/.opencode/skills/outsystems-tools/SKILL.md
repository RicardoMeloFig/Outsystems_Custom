---
name: outsystems-tools
description: Use when the user wants to inspect or extract OutSystems module metadata (entities, attributes, actions, structures) from a running Service Studio instance or from .oml files. Triggers on keywords like OutSystems, Service Studio, .oml, entity, attribute, module metadata, eSpaceKey, live process, AutoSave. Provides three MCP tools: get_open_module, parse_oml_header, live_reader.
---

# OutSystems module metadata tools

Three tools are available via the `outsystems-tools` MCP server. They extract
module metadata WITHOUT the Service Studio UI, WITHOUT a platform server
connection, and WITHOUT the productKey signature gate.

## Prerequisites

- **`get_open_module` / `live_reader`:** Service Studio 11 must be running with
  the target module open and **fully loaded into memory**. UI focus is not
  required. A module on the recent-projects screen is NOT loaded.
- **`parse_oml_header`:** no prerequisites — works offline on any `.oml` file
  on disk; Service Studio does not need to be running.
- **Multiple `ServiceStudio.exe` instances:** pass `pid` explicitly to
  `live_reader` (auto-detect only works when exactly one instance is running).
- **Multi-module situations:** all modules can stay open in one PID — the
  `live_reader` attributes content per module automatically via `ownerESpace`.
  See the `multi-module-extraction` skill.

## When to use which tool

### `get_open_module`
Use when the user asks "what module is open in Service Studio" or wants the
header of a currently-open module. It finds the running `ServiceStudio.exe`
process(es), reads the AutoSave cache
(`%LOCALAPPDATA%\OutSystems\ServiceStudio 11 XPlatform Stable\AutoSave\<PID>_*.bak`),
and returns module name, eSpaceKey, IsExtension, SS/platform versions, saved
time. No arguments. No .NET/DLLs needed.

### `parse_oml_header`
Use when the user gives you a `.oml` file path and wants its header info.
Argument: `path` (string). Returns module name, eSpaceKey, versions,
description, saved time. Works fully offline - the .oml header is plaintext
(magic `OML` + pipe-delimited fields, terminated by the `wg|/` body marker).

### `live_reader`
Use when the user wants the ACTUAL CONTENT (entities, structures, their
attributes) of the module currently open in Service Studio. Attaches to the
running Service Studio process (PID auto-detected, or pass `pid`) with ClrMD
and walks the .NET 8 heap, printing a structured tree:
```
MODULE: <name>   (PID <pid>)

ENTITIES (n):
  - <EntityName>
      . <AttributeName>  [label]
      ...
STRUCTURES (n):
  - <StructureName>  [system]   <- built-in tagged
      . <AttributeName>
ACTION NODES (n) [template/layout calls]:
  - <name>
```
This bypasses the .oml `productKey` signature gate entirely because Service
Studio has already loaded and verified the module in memory. Attributes are
mapped to their owning Entity/Structure via the objects' `.parent`
back-references (robust against the C5 collection internals).

## Key facts
- Service Studio 11 runs on **.NET 8**. `live_reader` needs the module to be
  open in SS at call time.
- The `.oml` body is a proprietary `omi` format (NOT plain XML/deflate); full
  offline body parsing is gated by a server-issued **productKey**
  (`Oml.LoadWithoutUpgrades` throws "False not equal to True" without it).
  `live_reader` sidesteps this by reading memory.
- The AutoSave `.bak` filename embeds the SS PID, so `get_open_module` maps
  each running instance to its open module.
- `parse_oml_header` and `get_open_module` need no runtime; `live_reader` is
  bundled self-contained in the MCP exe.
- See `docs/project_map.md` for full extraction reference and module catalog.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `live_reader` says "Service Studio is not running" | SS not running, or module only on recent-projects screen | Open the module in SS and wait until it finishes loading. |
| `live_reader` shows `MODULE: (unknown)` with 0 entities | The DevEnv hub / a non-module tab is read as a pseudo-module | Ignore it. Real modules appear as `MODULE: <name>` with their entities. Other resident modules you did not open (e.g. a leftover `Dummy_ARR`) may also appear — they are genuine open modules, not errors. |
| `get_open_module` shows "no AutoSave file" for a module | Module just opened, never saved | Still extractable. Find PID via `Get-Process ServiceStudio`; pass `pid` to `live_reader`. |
| `live_reader` output labeled with the wrong module / counts too high | Stale exe — source has per-module attribution but exe wasn't rebuilt | Rebuild with `dotnet publish` (see AGENTS.md). |
| `parse_oml_header` errors or returns empty | Bad path, or path is a `.oap` archive not a `.oml` | Confirm `path` points to a real `.oml` file. |
| `live_reader` times out | Heap too large (many modules in one PID) | Isolate: fewer modules per instance. |
