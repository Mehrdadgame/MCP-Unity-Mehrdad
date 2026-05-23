"""Framing protocol round-trip tests.

A throwaway loopback server speaks the same 4-byte-BE length + UTF-8 JSON framing
as the Unity bridge, so we can verify UnityClient end to end without Unity running.
"""

import json
import socket
import struct
import threading

import pytest

from unity_mcp.exceptions import UnityConnectionError, UnityError
from unity_mcp.unity_client import UnityClient


def _recv_exactly(conn: socket.socket, count: int) -> bytes:
    buf = b""
    while len(buf) < count:
        chunk = conn.recv(count - len(buf))
        if not chunk:
            raise AssertionError("client closed connection early")
        buf += chunk
    return buf


def _start_server(handler):
    """Start a one-shot loopback server. Returns (port, captured_dict)."""
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("127.0.0.1", 0))
    server.listen(1)
    port = server.getsockname()[1]
    captured: dict = {}

    def run():
        try:
            conn, _ = server.accept()
            with conn:
                (length,) = struct.unpack(">I", _recv_exactly(conn, 4))
                request = json.loads(_recv_exactly(conn, length).decode("utf-8"))
                captured["request"] = request
                payload = json.dumps(handler(request)).encode("utf-8")
                conn.sendall(struct.pack(">I", len(payload)) + payload)
        finally:
            server.close()

    thread = threading.Thread(target=run, daemon=True)
    thread.start()
    return port, captured


def test_request_round_trip_success():
    def handler(req):
        return {
            "id": req["id"],
            "type": "response",
            "success": True,
            "data": {"pong": True, "echo": f"{req['category']}.{req['action']}"},
        }

    port, captured = _start_server(handler)
    client = UnityClient(port=port, timeout=5.0)
    try:
        data = client.request("editor", "ping", {"foo": "bar"})
    finally:
        client.close()

    assert data == {"pong": True, "echo": "editor.ping"}

    sent = captured["request"]
    assert sent["type"] == "request"
    assert sent["category"] == "editor"
    assert sent["action"] == "ping"
    assert sent["params"] == {"foo": "bar"}
    assert "id" in sent and sent["id"]


def test_request_error_raises_unity_error():
    def handler(req):
        return {
            "id": req["id"],
            "success": False,
            "error": {"code": "NOT_FOUND", "message": "no such object"},
        }

    port, _ = _start_server(handler)
    client = UnityClient(port=port, timeout=5.0)
    try:
        with pytest.raises(UnityError) as excinfo:
            client.request("gameobject", "find", {"name": "x"})
    finally:
        client.close()

    assert excinfo.value.code == "NOT_FOUND"
    assert "no such object" in excinfo.value.message


def test_connection_refused_is_friendly():
    # Nothing is listening on this port; expect a clear connection error.
    client = UnityClient(port=6555, timeout=1.0)
    with pytest.raises(UnityConnectionError) as excinfo:
        client.request("editor", "ping")
    assert "Unity MCP bridge" in str(excinfo.value)
