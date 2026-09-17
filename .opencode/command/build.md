---
description: Build all MCP servers and the bridge (compile only), then run the workspace integrity verification.
agent: os-toolkit
---
Build and verify the workspace tooling. Run: .\scripts\Build-All.ps1, then
.\scripts\Build-Bridge.ps1 (compile only — never -Install without explicit
confirmation), then .\scripts\Verify-Project.ps1. Report every OK:/MISSING:/
STALE: line. If anything fails, diagnose and fix it, then re-run until
Verify-Project prints OK and exits 0 — or report precisely what is broken.
