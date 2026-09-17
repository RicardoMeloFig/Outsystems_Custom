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

## Pending: granting a role on a screen (IN PROGRESS)

- Screen `Permissions` holds `Permission` WRAPPERS, not Roles — `Permissions.Add(role)`
  throws `ArgumentException` (proven). No `Create/CreatePermission/AddPermission`
  factory found by name hunt (proven).
- Next: `live_probe_collection(module, screen, "Permissions")` (deploy-6, coded —
  needs UAC-approved DLL swap) → construct the wrapper precisely → extend
  `set_screen_permissions` → prove `ScratchAdmin` on a scratch admin screen.
- Until then: set `Public` + roles in SS manually (one dialog), everything else live.

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
- ⚠️ Permission-grant wrapper — probe coded, deploy pending UAC approval.
