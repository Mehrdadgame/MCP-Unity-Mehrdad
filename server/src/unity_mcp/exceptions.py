"""Exception hierarchy for the Unity MCP server."""

from __future__ import annotations


class UnityMCPError(Exception):
    """Base class for every error raised by this package."""


class UnityConnectionError(UnityMCPError):
    """The bridge could not be reached or the connection dropped mid-request."""


class UnityError(UnityMCPError):
    """Unity returned a structured error response (``success: false``)."""

    def __init__(self, code: str, message: str):
        self.code = code
        self.message = message
        super().__init__(f"[{code}] {message}")
