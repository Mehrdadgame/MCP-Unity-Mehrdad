"""FastMCP server exposing the Unity bridge to Claude.

Tools: smoke (unity_ping, unity_get_state), a generic passthrough (unity_request),
console reading (unity_get_console_logs, unity_clear_console), and compile control
(unity_recompile_and_wait, unity_get_compile_result).
"""

from __future__ import annotations

import time
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


@mcp.tool()
def unity_request(category: str, action: str, params: Optional[dict] = None) -> Any:
    """Low-level passthrough to any Unity bridge handler.

    Sends {category, action, params} straight to the Editor and returns its data.
    Handy for actions that don't have a dedicated tool yet, e.g.
    unity_request("editor", "get_state").
    """
    return _call(category, action, params or {})


@mcp.tool()
def unity_get_console_logs(level: str = "All", limit: int = 100) -> dict:
    """Read the Unity Editor Console.

    level: "All" | "Error" | "Warning" | "Log". limit: max (most recent) entries.
    Returns {total, errorCount, warningCount, shown, entries:[{level,message,file,line}]}.
    """
    return _call("console", "get_logs", {"level": level, "limit": limit})


@mcp.tool()
def unity_clear_console() -> dict:
    """Clear the Unity Editor Console."""
    return _call("console", "clear")


@mcp.tool()
def unity_get_compile_result() -> dict:
    """Return the most recent C# compilation result without triggering a new compile.

    {isCompiling, succeeded, errorCount, warningCount, errors[], warnings[]}.
    """
    return _call("script", "get_compile_result")


def _format_compile(res: dict) -> dict:
    return {
        "succeeded": res.get("succeeded", False),
        "isCompiling": res.get("isCompiling", False),
        "errorCount": res.get("errorCount", 0),
        "warningCount": res.get("warningCount", 0),
        "errors": res.get("errors", []),
        "warnings": res.get("warnings", []),
    }


@mcp.tool()
def unity_recompile_and_wait(force: bool = True, timeout_seconds: float = 180.0) -> dict:
    """Recompile C# in the Editor and wait for the result.

    Use after editing scripts in the package. Transparently rides out the domain
    reload (the bridge drops and reconnects). Returns
    {succeeded, errorCount, warningCount, errors[], warnings[]}; each error carries
    {message, file, line, column, assembly}.
    """
    try:
        started = _client.request("script", "recompile", {"force": force})
    except (UnityConnectionError, UnityError) as exc:
        raise RuntimeError(str(exc)) from exc
    prev = int(started.get("previousFinishedAtTicks", 0))

    start_time = time.time()
    deadline = start_time + timeout_seconds
    time.sleep(1.5)  # let Unity register the compile / begin the domain reload

    last: Optional[dict] = None
    saw_activity = False
    while time.time() < deadline:
        try:
            res = _client.request("script", "get_compile_result")
        except (UnityConnectionError, UnityError):
            saw_activity = True  # a dropped socket almost always means a reload is underway
            time.sleep(1.0)
            continue
        last = res
        if res.get("isCompiling") or res.get("isUpdating"):
            saw_activity = True
            time.sleep(1.0)
            continue
        if int(res.get("finishedAtTicks", 0)) > prev:
            return _format_compile(res)
        if not force and not saw_activity and (time.time() - start_time) > 5.0:
            out = _format_compile(res)
            out["note"] = "No compilation was triggered (no script changes detected)."
            return out
        time.sleep(1.0)

    out: dict = {"timedOut": True, "note": f"Compile did not finish within {timeout_seconds}s."}
    if last:
        out.update(_format_compile(last))
    return out


def main() -> None:
    """Console-script entry point: serve over stdio for Claude Desktop / Claude Code."""
    mcp.run()


if __name__ == "__main__":
    main()
