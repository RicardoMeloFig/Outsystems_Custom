# Publishes the current outsystems-liveeditor MCP build to the folder opencode loads it from.
# Run AFTER closing any opencode session that has the outsystems-liveeditor MCP server running
# (the running server locks publish\OutSystemsMcpLiveEditor.dll).
#
# Usage: powershell -File scripts\Update-LiveEditorMcp.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet publish (Join-Path $root "mcp\outsystems-liveeditor\OutSystemsMcpLiveEditor.csproj") -c Release -o (Join-Path $root "mcp\outsystems-liveeditor\publish")
if ($LASTEXITCODE -eq 0) { "MCP server published. Restart opencode to pick up the new tools." } else { "PUBLISH FAILED - see output above."; exit 1 }
