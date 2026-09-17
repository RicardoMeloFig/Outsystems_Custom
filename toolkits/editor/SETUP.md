# SETUP.md — First-time setup on a new PC

This guide gets the headless `.oml` editor baseline running after a fresh clone.
The compiled MCP exe is **gitignored** (`bin/`, `obj/`, `publish/`), so it must
be rebuilt locally.

If the MCP server shows as **inactive/disabled** in opencode right after a clone,
the `.exe` doesn't exist yet — follow this guide.

---

## Why the MCP is inactive after a fresh clone

`opencode.json` points the one MCP server at a compiled `.exe` inside a gitignored
build-output folder:

| Server | Expected exe path | Target framework |
|---|---|---|
| `outsystems-omleditor` | `mcp/outsystems-omleditor/publish/OutSystemsMcpOmlEditor.exe` | net8.0 |
| `outsystems-liveeditor` | `mcp/outsystems-liveeditor/publish/OutSystemsMcpLiveEditor.exe` | net8.0 |

`git clone` only downloads the C# source; the `.exe` doesn't exist, so opencode
can't launch them. Rebuild with the steps below.

The **live editor** also requires the **OsLiveBridge plugin** DLL installed in SS
(`Plugins\ServiceStudio\ServiceStudio.Plugin.OsLiveBridge.dll`). The build produces
it; see "Live editor setup" below.

---

## Prerequisites on the new PC

- **Git** — https://git-scm.com
- **.NET 8+ SDK** — `dotnet --list-sdks` must show an `8.0.x` (or newer) entry.
- **opencode CLI** — https://opencode.ai
- **OutSystems Service Studio 11** — **installed**. The editor loads the SS model
  DLLs (`OutSystems.Model.Implementation.dll` etc.) from the install dir
  (`C:\Program Files\OutSystems\Service Studio 11\Service Studio\`) at runtime via
  `Assembly.LoadFrom`. SS does **not** need to be running to edit `.oml` files.
  (Override the install dir with the `OSSS_DIR` env var.)
- **Windows** (x64).

> No FlaUI, no Ollama/UI-TARS, no .NET 10 requirement — this baseline is pure
> `net8.0` and UI-free.

---

## Step-by-step

### 1. Clone / copy the repo and `cd` into the root

### 2. Build (publish) the editor MCP server

```powershell
.\scripts\Build-All.ps1
```

For a PC with **no .NET runtime installed**, build self-contained:

```powershell
.\scripts\Build-All.ps1 -SelfContained
```

### 3. Verify the toolkit is complete

```powershell
.\scripts\Verify-Project.ps1
```

It must print `OK:` and exit 0. (Source-only check before building:
`.\scripts\Verify-Project.ps1 -SourceOnly`.)

### 4. Smoke-test the server (optional)

The server speaks MCP over stdio. Pipe an initialize; it should respond, then
wait silently. `Ctrl+C` to exit.

```powershell
'{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | & ./mcp/outsystems-omleditor/publish/OutSystemsMcpOmlEditor.exe
```

### 5. Launch opencode from the repo root

opencode reads the local `opencode.json`, so launch from the repo root:

```powershell
opencode
```

Or use the launcher (runs the staleness gate first):

```powershell
.\scripts\start-opencode.ps1
```

Inside opencode, `/mcp` — `outsystems-omleditor` and `outsystems-liveeditor` should
both show active/connected.

### 5b. Live editor setup (OsLiveBridge plugin)

The live editor requires the **OsLiveBridge plugin** installed in SS. `Build-All.ps1`
builds it (skip the auto-copy if SS is running):

```powershell
# Build the bridge plugin (DLL, not the MCP exe)
dotnet build ".\bridge\OsLiveBridge\OsLiveBridge.csproj" -c Release -p:SkipCopy=true

# Stop SS + CrashHandler, clear recovery state, swap the DLL, restart SS
$src = ".\bridge\OsLiveBridge\bin\Release\ServiceStudio.Plugin.OsLiveBridge.dll"
$dst = "C:\Program Files\OutSystems\Service Studio 11\Service Studio\Plugins\ServiceStudio\ServiceStudio.Plugin.OsLiveBridge.dll"
Get-Process CrashHandler,ServiceStudio -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\OutSystems\ServiceStudio 11 XPlatform Stable\AutoSave\*.bak" -Force -ErrorAction SilentlyContinue
'{}' | Set-Content "$env:LOCALAPPDATA\OutSystems\ServiceStudio 11 XPlatform Stable\EditData\PendingChanges.bin"
Copy-Item $src $dst -Force
Start-Process "C:\Program Files\OutSystems\Service Studio 11\Service Studio\ServiceStudio.exe"
```

Then open a module in SS and verify via `live_status` in opencode. See the
`live-editing` skill for details.

### 5c. Pre-launch staleness check (recommended habit)

After any MCP source edit, the built exe goes stale. Run the gate before
launching to avoid a mid-session restart:

```powershell
.\scripts\Ensure-Built.ps1
opencode
```

Optional PowerShell profile alias:

```powershell
function Start-OSAutoEditor { .\scripts\Ensure-Built.ps1; if ($LASTEXITCODE -eq 0) { opencode } }
```

---

## Using the editor

**ALWAYS LIVE FIRST.** If the module is open in SS, use the live editor
(`outsystems-liveeditor` tools: `live_create_service_action`, `live_consume_elements`,
etc.) — no Save, no reload, instant tree update. Only fall back to headless when SS
isn't running or the module isn't open.

### Live workflow (preferred — module open in SS)
1. Open the module in Service Studio.
2. In opencode, call `live_status` to confirm the bridge + module.
3. Use live tools (e.g. `live_create_service_action`, `live_consume_elements`).
4. Changes appear in SS immediately (undo unit — `Ctrl+Z`).
5. `Ctrl+S` in SS to persist.

### Headless workflow (fallback — SS not running / module not open)
1. In Service Studio, open the module and **Save (`Ctrl+S`)**.
2. Find the `.oml`: `.\scripts\Get-OmlPath.ps1 <Module>`.
3. In opencode, call a tool, e.g. `probe_oml(omlPath=…)` to inspect, then
   `create_service_action(omlPath=…, outOml=…, name=…)`.
4. The tool self-verifies (`IsValidOml` + read-back). Reload the result in SS
   (close the module, open the new `.oml`) to see the change.

See `AGENTS.md` and the `editor-workflow` / `outsystems-omleditor` / `live-editing` / `creating-service-actions-live` / `exception-handlers-in-service-actions` / `building-service-action-flows` skills.

---

## Troubleshooting

### The server fails to start / "type not found" / assembly load error
SS 11 is not installed (the model DLLs are missing), or the install dir differs.
Install SS 11, or set `$env:OSSS_DIR = "C:\path\to\Service Studio 11\Service Studio"`.

### `dotnet publish` says the SDK is missing
Install the **.NET 8+ SDK** (`dotnet --list-sdks` must show `8.0.x` or newer).

### MCP still inactive after building
- Launch opencode from the **repo root** (where `opencode.json` lives).
- Run `.\scripts\Verify-Project.ps1` — it lists what's missing.
- Restart opencode (it reads `opencode.json` only at startup).

---

## Quick reference (copy-paste block)

```powershell
# From repo root, after clone:
.\scripts\Build-All.ps1          # builds the editor exe into <project>/publish/
.\scripts\Verify-Project.ps1     # hard-fail gate; must print OK

# Launch:
opencode
```
