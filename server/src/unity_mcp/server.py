"""FastMCP server exposing the Unity bridge to Claude.

Phase 1 ships only the smoke tools (``unity_ping`` and ``unity_get_state``),
which exercise the full path: MCP tool -> UnityClient -> TCP framing -> Unity
bridge -> dispatcher -> router -> EditorHandler -> response.
"""

from __future__ import annotations

from typing import Any, Optional

from mcp.server.fastmcp import FastMCP

from .exceptions import UnityConnectionError, UnityError
from .unity_client import UnityClient

mcp = FastMCP("unity-mcp")
_client = UnityClient()


def _call(category: str, action: str, params: Optional[dict] = None) -> Any:
    """Invoke a bridge action, translating client errors into clear tool errors."""
    try:
        return _client.request(category, action, params)
    except UnityConnectionError as exc:
        raise RuntimeError(str(exc)) from exc
    except UnityError as exc:
        raise RuntimeError(f"Unity returned an error: {exc}") from exc


@mcp.tool()
def unity_ping() -> dict:
    """Smoke-test the Unity MCP bridge.

    Returns Unity version, product name, and a server timestamp. Use this to
    confirm the Editor is reachable before issuing other commands.
    """
    return _call("editor", "ping")


@mcp.tool()
def unity_get_state() -> dict:
    """Report the current Unity Editor state.

    Includes play/pause flags, whether the Editor is compiling or updating,
    the active platform, and the Unity version.
    """
    return _call("editor", "get_state")


def main() -> None:
    """Console-script entry point: serve over stdio for Claude Desktop / Claude Code."""
    mcp.run()


if __name__ == "__main__":
    main()
