<#
  One-time setup for the Unity MCP Python server (Windows).

  Usage (from this folder):
      powershell -ExecutionPolicy Bypass -File .\setup.ps1

  Creates a local .venv, installs the server into it, then prints the exact
  Claude Desktop config entry to paste (with this machine's path filled in).
#>
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Locate Python (prefer 'python', fall back to the 'py' launcher).
$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) { $pythonCmd = Get-Command py -ErrorAction SilentlyContinue }
if (-not $pythonCmd) {
    Write-Error "Python 3.10+ was not found on PATH. Install it from https://www.python.org/downloads/ and re-run."
    exit 1
}
$python = $pythonCmd.Source

Write-Host "Creating virtual environment..." -ForegroundColor Cyan
& $python -m venv "$here\.venv"
$venvPy = Join-Path $here ".venv\Scripts\python.exe"

Write-Host "Installing the server..." -ForegroundColor Cyan
& $venvPy -m pip install --upgrade pip | Out-Null
& $venvPy -m pip install -e "$here"

$escaped = $venvPy -replace '\\', '\\'
$cfgPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"

$block = @"
  "unity-mcp": {
    "command": "$escaped",
    "args": ["-m", "unity_mcp.server"]
  }
"@

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host ""
Write-Host "1) Open your Claude Desktop config (create the file if it doesn't exist):"
Write-Host "     $cfgPath"
Write-Host ""
Write-Host "2) Add this entry inside the top-level `"mcpServers`" object"
Write-Host "   (if the file is empty, wrap it as { `"mcpServers`": { ... } } ):"
Write-Host ""
Write-Host $block
Write-Host "3) Fully quit Claude Desktop from the system tray (Quit, not just close), then reopen."
Write-Host ""
Write-Host "Then, with the Unity project open, ask Claude to run unity_ping to confirm."
