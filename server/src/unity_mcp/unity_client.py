"""TCP client for the Unity bridge.

Speaks the length-prefixed JSON protocol: a 4-byte big-endian length followed by
that many UTF-8 JSON bytes. A single persistent socket is reused across requests
and transparently reconnected once if it drops (e.g. across a domain reload).
"""

from __future__ import annotations

import json
import os
import socket
import struct
import threading
import time
import uuid
from typing import Any, Optional

from .exceptions import UnityConnectionError, UnityError

_PREFIX = struct.Struct(">I")  # 4-byte big-endian unsigned length
_MAX_MESSAGE = 64 * 1024 * 1024  # matches the C# guard rail


def _read_port_file() -> Optional[int]:
    """Read the active bridge port published by the Unity side, if any.

    The Unity bridge writes the port it actually bound to (which may differ from the
    default when a zombie/phantom listener holds it). We check the platform-native
    LocalAppData path first, then a Linux/macOS-friendly fallback.
    """
    candidates = []
    local_app = os.environ.get("LOCALAPPDATA")
    if local_app:
        candidates.append(os.path.join(local_app, "UnityMCP", "port.txt"))
    candidates.append(os.path.join(os.path.expanduser("~"), ".local", "share", "UnityMCP", "port.txt"))
    for p in candidates:
        try:
            with open(p, "r", encoding="utf-8") as f:
                return int(f.read().strip())
        except (OSError, ValueError):
            continue
    return None


class UnityClient:
    """Thread-safe request/response client for the Unity MCP bridge."""

    def __init__(self, host: str = "127.0.0.1", port: int = 6400, timeout: float = 30.0,
                 port_scan: int = 10):
        self.host = host
        self.port = port
        self.timeout = timeout
        self.port_scan = port_scan  # how many ports to try in sequence (default+0 .. default+N-1)
        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()
        self._discovered_port: Optional[int] = None  # cache: the port we last connected to

    # -- connection lifecycle -------------------------------------------------

    def _connect(self) -> socket.socket:
        if self._sock is not None:
            return self._sock
        # Order: the port the bridge PUBLISHED (port-discovery file) → previously
        # discovered → configured default → scan range. The file wins — it tells us
        # exactly where the live bridge is, even when a phantom holds the default port.
        candidates: list = []
        file_port = _read_port_file()
        if file_port is not None:
            candidates.append(file_port)
        if self._discovered_port is not None and self._discovered_port not in candidates:
            candidates.append(self._discovered_port)
        if self.port not in candidates:
            candidates.append(self.port)
        for p in range(self.port + 1, self.port + self.port_scan):
            if p not in candidates:
                candidates.append(p)

        last_exc: Optional[Exception] = None
        for p in candidates:
            try:
                sock = socket.create_connection((self.host, p), timeout=self.timeout)
                sock.settimeout(self.timeout)
                self._sock = sock
                self._discovered_port = p
                return sock
            except OSError as exc:
                last_exc = exc
        # Forget any cached port — next call will rescan from scratch.
        self._discovered_port = None
        raise UnityConnectionError(
            f"Could not connect to the Unity MCP bridge on {self.host}:{self.port}"
            f"..{self.port + self.port_scan - 1}. Open the Unity Editor with the MCP package "
            "installed and make sure the bridge is running (Tools > MCP > Start Bridge)."
        ) from last_exc

    def close(self) -> None:
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None

    # -- framing --------------------------------------------------------------

    @staticmethod
    def _send(sock: socket.socket, obj: dict) -> None:
        payload = json.dumps(obj).encode("utf-8")
        sock.sendall(_PREFIX.pack(len(payload)) + payload)

    def _recv(self, sock: socket.socket) -> dict:
        (length,) = _PREFIX.unpack(self._recv_exactly(sock, 4))
        if length < 0 or length > _MAX_MESSAGE:
            raise UnityConnectionError(f"Invalid response length from bridge: {length}")
        body = self._recv_exactly(sock, length) if length else b""
        return json.loads(body.decode("utf-8"))

    @staticmethod
    def _recv_exactly(sock: socket.socket, count: int) -> bytes:
        chunks = []
        remaining = count
        while remaining > 0:
            chunk = sock.recv(remaining)
            if not chunk:
                raise UnityConnectionError("Connection closed by the Unity bridge.")
            chunks.append(chunk)
            remaining -= len(chunk)
        return b"".join(chunks)

    # -- public API -----------------------------------------------------------

    def request(self, category: str, action: str, params: Optional[dict] = None) -> Any:
        """Send one request and return its ``data`` payload.

        Raises :class:`UnityError` for a structured failure response and
        :class:`UnityConnectionError` if the bridge is unreachable.
        """
        message = {
            "id": str(uuid.uuid4()),
            "type": "request",
            "category": category,
            "action": action,
            "params": params or {},
        }
        with self._lock:
            response = self._round_trip(message)

        if not response.get("success", False):
            error = response.get("error") or {}
            raise UnityError(error.get("code", "UNKNOWN"), error.get("message", "Unknown error"))
        return response.get("data")

    def batch(self, ops: list, undo_group: str = "MCP Batch", atomic: bool = True) -> Any:
        """Run multiple ops in one round-trip and one Undo group.

        ``ops`` is a list of {category, action, params}. Returns the batch result
        {success, count, failedIndex, results}. Raises only on transport/protocol errors.
        """
        message = {
            "id": str(uuid.uuid4()),
            "type": "batch",
            "undoGroup": undo_group,
            "atomic": atomic,
            "ops": ops,
        }
        with self._lock:
            response = self._round_trip(message)
        if not response.get("success", False):
            error = response.get("error") or {}
            raise UnityError(error.get("code", "UNKNOWN"), error.get("message", "Unknown error"))
        return response.get("data")

    def _round_trip(self, message: dict, retries: int = 6) -> dict:
        # Retry across brief outages (e.g. the bridge rebinding after a domain reload)
        # so a recompile window does not surface to the caller as a disconnect.
        last_exc: Optional[Exception] = None
        for attempt in range(retries):
            try:
                sock = self._connect()
                self._send(sock, message)
                return self._recv(sock)
            except (OSError, UnityConnectionError) as exc:
                last_exc = exc
                self.close()
                if attempt < retries - 1:
                    time.sleep(0.4)
        if isinstance(last_exc, UnityConnectionError):
            raise last_exc
        raise UnityConnectionError(f"Lost connection to the Unity bridge: {last_exc}") from last_exc
