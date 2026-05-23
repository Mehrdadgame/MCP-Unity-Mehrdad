#!/usr/bin/env bash
#
# One-time setup for the Unity MCP Python server (macOS / Linux).
#
# Usage (from this folder):
#     ./setup.sh
#
# Creates a local .venv, installs the server, then prints the Claude Desktop
# config entry to paste (with this machine's path filled in).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

if command -v python3 >/dev/null 2>&1; then
  py=python3
elif command -v python >/dev/null 2>&1; then
  py=python
else
  echo "Python 3.10+ was not found. Install it and re-run." >&2
  exit 1
fi

echo "Creating virtual environment..."
"$py" -m venv "$here/.venv"
venv_py="$here/.venv/bin/python"

echo "Installing the server..."
"$venv_py" -m pip install --upgrade pip >/dev/null
"$venv_py" -m pip install -e "$here"

# Config path differs per OS.
case "$(uname -s)" in
  Darwin) cfg="$HOME/Library/Application Support/Claude/claude_desktop_config.json" ;;
  *)      cfg="$HOME/.config/Claude/claude_desktop_config.json" ;;
esac

cat <<EOF

Setup complete.

1) Open your Claude Desktop config (create it if missing):
     $cfg

2) Add this entry inside the top-level "mcpServers" object
   (if the file is empty, wrap it as { "mcpServers": { ... } } ):

  "unity-mcp": {
    "command": "$venv_py",
    "args": ["-m", "unity_mcp.server"]
  }

3) Fully quit Claude Desktop and reopen it.

Then, with the Unity project open, ask Claude to run unity_ping to confirm.
EOF
