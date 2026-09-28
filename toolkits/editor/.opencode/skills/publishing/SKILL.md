---
name: publishing
description: Use when the user wants to save, verify, or 1-Click Publish a module and triage the errors. Covers the SS save loop, headless verify via bridge, and UI-automation publish (needs Ollama for vision). Read ui-tars-automation + live-editing first.
---

# Publishing — save → verify → 1CP → triage (DRAFT, dry run pending)

## 1. Save (deterministic, PROVEN pattern)

Live edits are in-memory until saved. Two paths:
- **User presses `Ctrl+S` in SS** (canon — used after every batch this project).
- **Model-assisted**: focus SS (`SetForegroundWindow` on the ServiceStudio process)
  + `ui_hotkey(Ctrl+S)` + `ui_wait(30)` — proven 2026-09-16 (saved a hung-session scare).
  Confirm via `live_status` + state re-read after a restart cycle.

## 2. Verify WITHOUT publishing (PROVEN — the inner loop)

```
live_get_verify_errors(module, "screen"|"block"|"action"|"widget", name)  # ≤50 msgs, [] = clean
```

- Verbose mode (`verbose=true`) dumps message internals (Ids like
  `ExpressionUnknownFunction`, `RequiredPropertyValue_Property`, culprit
  `ObjectIdentifier`s) — this is how the 39-function bible sweep pinpointed every red.
- Per-widget checks isolate expression errors; screen/block checks catch aggregate,
  sort, calc, param, and style errors. **Fix → re-check → green before 1CP.**
- **Read back VALUES, not just errors.** A mutation reporting success is not proof —
  re-read the object afterwards (`live_debug_eSpace_collection_items` for timers/
  entities/roles, `get_verify_errors` for flows/widgets, `live_probe_*` for surfaces).
  Proven failure this caught: `set_timer_action` reported success while `Action` stayed
  null (lost across a restart); the re-read exposed it and the re-set stuck.
- Typical reds and owners: `expression-reference` (functions), aggregate skills
  (sort scope, calc formula, implicit param type), `fix_style_literal` (Style classes).

## 3. 1-Click Publish (UNVERIFIED — first dry run pending)

- Deterministic attempt: focus SS → publish hotkey/button → long wait → screenshot.
  Exact publish hotkey/button coordinates: VERIFY-PENDING (do not assert F-keys;
  confirm visually on first run).
- **Hotkey leads (from the official docs):** the SS publish button is **1 Publish**;
  **Shift+F5** opens **1-Click Publish with message** (Windows). Try F5/plain
  publish first on the dry run, then Shift+F5 for the message dialog. Confirm
  visually before relying on either.
- Publish messages: optional, **≤ 2000 chars**, permanently attached to the module
  version. Query them programmatically via the public read-only
  **`Espace_CommitMessage`** entity (or Service Center > Factory > Modules >
  Versions tab) — useful for receipts/audit of agent-published versions.
- Vision path (needs Ollama `ui-tars-1.5-7b` UP — it was DOWN 2026-09-16):
  `ui_tars_goal("publish the open module (1-Click Publish) and report errors")`.
  **SUPERSEDED — do NOT use vision for publish anymore. Prefer the no-vision
  paths below (probed in SS 11.55.83 binaries, 2026-09-24).**

## 3.0 Publish WITHOUT UI automation (no Ollama — PROVEN 2026-09-24)

1-Click Publish is a **real SS command** and is driven the same way we do live edits — no
clicks, no vision. PROVEN on SS 11.55.83 AND re-proven 2026-09-25 on 11.55.89
(`ZombieGame_CS`, Development environment, `InstalledKind=Development`):

- **Bridge command `publish_module {module}`** — invokes
  `ServiceStudio.Presenter.Commands.Publish` (the F5 command) via
  `AutoRegistryType<Publish>.Instance` (the singleton — `new Publish()` throws
  "An item with the same key has already been added" because the [Command] registry
  already registered it), calling public
  `Execute(ICommandTarget, IPresenter)` = `agg.Execute(agg, agg)`. The command opens its
  own command via `GetPresenterContext()` — do NOT wrap in `Command.ExecuteFromAsyncCode`.
- **Semantics (critical):** `Execute` returns **null almost immediately (async)** — the
  real work runs in the `ServerProcess` (`PublishCommand`2+`prs#qurwhwvn`) registered in
  `ServerProcess.ProcessesStarted`, which progresses `Uploading → … → done` and removes
  itself. So **`ok:false` from publish_module is EXPECTED**; the authoritative checks are:
  1. `debug_publish_state` — `ProcessesStarted: "none"` = the process finished;
  2. `live_get_verify_errors` clean;
  3. **Service Center → Factory → Modules → Versions** (a new version with today's
     date = the receipt).
- **⚠ NEVER use `commitMessage`** (`prs#lislasrz` = publish-with-message): observed
  leaving the ServerProcess **STUCK at `InnerState: Uploading`**, which then blocks ALL
  further publishes (guard `ServerOperationRunning` refuses while a process is
  registered) until SS is restarted. Plain publish only; use SS's Shift+F5 dialog if you
  need a message (or fix the message path later).
- **Why a null result can also be legitimate pre-existing guards** (all read back in
  `diag`): `HasOpenedActiveESpace` (drives `CanExecute`; needs the module ACTIVE in SS),
  `ESpaceCanBePublished`, `HasOpenDebugSession`, `ServerProcessActive` (another publish
  running), `InstallationKind` (Development skips the "publish to Production?" confirm;
  Production shows a dialog = needs UI, not the bridge).
- **MCP tooling:** `live_publish_module(module, commitMessage?)` (commitMessage only when
  the stuck-process bug is fixed), `live_debug_publish_state(module)` (poll monitor),
  `live_debug_publish_surface(module)` (surface probe). Bridge-only commands via
  `scripts\Send-BridgeCmd.ps1 -Cmd publish_module`.
- Known caveat: `Execute` returning null means we can't get the CommandResult text over
  the pipe yet; success/failure is judged via `debug_publish_state` + Service Center.
  LLM fix candidate (next): await the ServerProcess completion inside the command and
  dump `Messages`/`MessageStatistics`.
- Error triage: publish errors map 1:1 to verify messages — route each to its skill
  (table above), fix live, re-verify headless, republish. TrueChange/upgrade errors
  (e.g. after entity changes with `Requires1CP`) may need a full 1CP to surface —
  that is precisely what this loop is for.

## 3.1 Publish vs deploy vs republish vs redeploy (platform semantics)

Condensed from the official O11 reference
(`docs-product\src\ref\publish-deploy\publish-deploy.md`, CC BY-NC-ND 4.0) —
these four are NOT variations of the same action:

| Action | What it does | Where | Version changes? |
|---|---|---|---|
| **Publish** | Compiles the app, assigns the next incremental version, makes it available in the **development** environment | SS (1 Publish / Shift+F5 with message) or Service Center | **Yes** (+1) |
| **Deploy** | Moves an existing version to a target environment (Quality/Production) via LifeTime; modules are **recompiled in the target** against the producers available there | LifeTime portal or LifeTime API | No (promotes an existing version) |
| **Republish** | During a deployment plan, LifeTime recompiles **consumer** apps whose dependencies the deployed change makes outdated | Deployment plan (suggested) | No |
| **Redeploy** | Re-sends the **latest compiled** bits to the app servers without recompiling (force reload on all front-ends) | **Service Center only** (Factory > Modules > Redeploy Published Version) | No |

Agent notes:
- Our editor's loop is publish-scoped (dev env). Anything deploy/republish/
  redeploy is an IT/lifecycle operation — route to LifeTime/Service Center, not SS.
- Deploy recompiles in the target: a module that verifies green in dev can still
  fail in the target if a producer's version differs there (the dependency
  warnings in the `editor-workflow` validity checklist are the leading indicator).

## 4. Receipt (after green publish)

```
outsystems-ui extract (screens/blocks/ui-tree/themes) + logic module_report → docs/<Module>/
```

Commit the receipt: proves the app builds from the documented calls. See `full-app-composer`.

## Status
- ✅ save (both paths), headless verify loop (the daily driver), triage routing.
- ⚠️ 1CP click path + first green-publish dry run — pending (needs SS focus time + Ollama).
