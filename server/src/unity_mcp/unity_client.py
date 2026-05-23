"""TCP client for the Unity bridge.

Speaks the length-prefixed JSON protocol: a 4-byte big-endian length followed by
that many UTF-8 JSON bytes. A single persistent socket is reused across requests
and transparently reconnected once if it drops (e.g. across a domain reload).
"""

from __future__ import annotations

import json
import socket
import struct
import threading
import uuid
from typing import Any, Optional

from .exceptions import UnityConnectionError, UnityError

_PREFIX = struct.Struct(">I")  # 4-byte big-endian unsigned length
_MAX_MESSAGE = 64 * 1024 * 1024  # matches the C# guard rail


class UnityClient:
    """Thread-safe request/response client for the Unity MCP bridge."""

    def __init__(self, host: str = "127.0.0.1", port: int = 6400, timeout: float = 30.0):
        self.host = host
        self.port = port
        self.timeout = timeout
        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()

    # -- connection lifecycle -------------------------------------------------

    def _connect(self) -> socket.socket:
        if self._sock is not None:
            return self._sock
        try:
            sock = socket.create_connection((self.host, self.port), timeout=self.timeout)
        except OSError as exc:
            raise UnityConnectionError(
                f"Could not connect to the Unity MCP bridge at {self.host}:{self.port}. "
                "Open the Unity Editor with the MCP package installed and make sure the "
                "bridge is running (Tools > MCP > Start Bridge)."
            ) from exc
        sock.settimeout(self.timeout)
        self._sock = sock
        return sock

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

    def _round_trip(self, message: dict) -> dict:
        # One transparent reconnect: a reused socket may be stale after a reload.
        last_exc: Optional[Exception] = None
        for attempt in range(2):
            sock = self._connect()  # raises UnityConnectionError on a fresh failure
            try:
                self._send(sock, message)
                return self._recv(sock)
            except (OSError, UnityConnectionError) as exc:
                last_exc = exc
                self.close()
        if isinstance(last_exc, UnityConnectionError):
            raise last_exc
        raise UnityConnectionError(f"Lost connection to the Unity bridge: {last_exc}") from last_exc
