---
name: app-security
description: Use when the user wants roles, screen permissions, login gating, or permission-denied flows. LIVE-first (role + Public flag proven; grant wiring pending probe). Read live-editing first.
---

# App Security — LIVE (partial, SS 11.55.83)

## Proven: roles + anonymous flag

```
live_create_role(module, "ScratchAdmin")            # IESpace.CreateRole — green, auto-creates "Not <Role>" exception
live_set_screen_permissions(module, "ScratchHome", isPublic="false")   # Public=False — green
live_get_verify_errors(...)                          # confirm no new errors
```

- Creating a role auto-registers its `Not <Role>` RoleException (system behavior).
- `Public=False` = authenticated users only; `True` = anonymous allowed.
- Check current roles: `live_debug_eSpace_collection_items(module, "Roles")`
  (+ `SystemRoles` for built-ins: Anonymous, Registered).

## Proven: granting a role on a screen (2026-09 close-out)

- Screen `Permissions` holds `Permission` WRAPPERS, not Roles — `Permissions.Add(role)`
  throws `ArgumentException` (proven). `live_grant_screen_permission` works around it:
  **`Duplicate(Permission)+Role`** (clone an existing Permission on this screen, or
  from any other screen, then re-target Role via `SetProp`/`_roleSetter`), with a
  ctor-hunt fallback.
- **Proven on ZombieGame/Squad (SS 11.55.89):** `create_role` → `grant_screen_permission`
  → read-back `[ProbeTmpRole, Registered]`. `AddDependentPermissions` ADDS a second
  Permission for the same role (1→3); the bridge now **dedupes** (latest run:
  `dedupeRemoved=1`, final = exactly one grant per role).
- `live_read_screen_permissions` / `live_remove_screen_permission` round-trip cleanly
  (removes all matching; verified `removed 1/1` → back to `[Registered]`).
- Grant ONLY on Web/Reactive modules — Service/Library modules have no screens.

## Login / permission-denied patterns (convention)

- Use the platform Login/Logout + `UserId` (never hand-rolled passwords).
- `Common/InvalidPermissions` screen exists in most modules — Anonymous users hitting
  a gated screen land there automatically. Keep it styled and informative.
- Stamp rows with `CreatedBy = UserId` (server-side, around the write) for ownership.

## Checklist for a gated screen (T10)
1. Role exists (`live_create_role`) — DONE live.
2. Screen `Public=False` (+ role grant when the wrapper lands).
3. Anonymous smoke: expect redirect to InvalidPermissions (manual check at publish).
4. Logged-in smoke: access granted.

## Status
- ✅ roles, Public flag, exceptions auto-created, collections inspectable.
- ✅ permission-grant wrapper — PROVEN + dedupe-fixed (2026-09 close-out).
- ⚠️ Cross-module clone (`live_clone_service_action_from`) NREs inside SS's
  `ClipboardManager.DeserializeInto` — not a security tool, but same session result:
  do not rely on it for security wiring.
