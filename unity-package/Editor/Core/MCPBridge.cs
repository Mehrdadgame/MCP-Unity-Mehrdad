using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Core
{
    /// <summary>
    /// The TCP front door for the bridge. Listens on 127.0.0.1:6400, reads framed
    /// JSON requests on background threads, routes them on the main thread, and writes
    /// framed JSON responses back. Auto-starts on editor load and survives domain
    /// reloads by releasing the port before reload and rebinding afterward.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPBridge
    {
        public const int DefaultPort = 6400;
        const string AutoStartKey = "MCP.AutoStart";

        static TcpListener _listener;
        static Thread _acceptThread;
        static volatile bool _running;

        static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        static MCPBridge()
        {
            MainThreadDispatcher.Initialize();
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;

            if (EditorPrefs.GetBool(AutoStartKey, true))
                EditorApplication.delayCall += Start; // wait until the editor is ready
        }

        public static bool IsRunning { get { return _running; } }
        public static int Port { get { return DefaultPort; } }

        public static void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, DefaultPort);
                _listener.Start();
                _running = true;

                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "MCP-Bridge-Accept",
                };
                _acceptThread.Start();

                Debug.Log("[MCP] Bridge listening on 127.0.0.1:" + DefaultPort);
            }
            catch (Exception e)
            {
                _running = false;
                Debug.LogError("[MCP] Failed to start bridge on port " + DefaultPort + ": " + e.Message);
            }
        }

        public static void Stop()
        {
            if (!_running && _listener == null) return;
            _running = false;
            try { if (_listener != null) _listener.Stop(); }
            catch { /* ignore */ }
            _listener = null;
        }

        static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException) { break; }        // listener stopped
                catch (ObjectDisposedException) { break; } // listener disposed
                catch (Exception e)
                {
                    if (_running) Debug.LogError("[MCP] Accept error: " + e.Message);
                    break;
                }

                var clientThread = new Thread(() => HandleClient(client))
                {
                    IsBackground = true,
                    Name = "MCP-Bridge-Client",
                };
                clientThread.Start();
            }
        }

        static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    while (_running)
                    {
                        string json;
                        try { json = Framing.ReadMessage(stream); }
                        catch { break; }

                        if (json == null) break;       // peer disconnected
                        if (json.Length == 0) continue; // keep-alive / empty frame

                        string response = ProcessMessage(json);
                        try { Framing.WriteMessage(stream, response); }
                        catch { break; }
                    }
                }
            }
            catch (Exception e)
            {
                if (_running) Debug.LogWarning("[MCP] Client handler error: " + e.Message);
            }
        }

        static string ProcessMessage(string json)
        {
            Request request;
            try
            {
                request = JsonConvert.DeserializeObject<Request>(json);
            }
            catch (Exception e)
            {
                return Serialize(Response.Fail(null, ErrorCodes.MALFORMED_REQUEST, "Invalid JSON: " + e.Message));
            }

            if (request == null)
                return Serialize(Response.Fail(null, ErrorCodes.MALFORMED_REQUEST, "Empty request."));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Response response;
            try
            {
                object data = MainThreadDispatcher.Run(() => CommandRouter.Route(request));
                response = Response.Ok(request.id, data);
            }
            catch (HandlerException he)
            {
                response = Response.Fail(request.id, he.Code, he.Message);
            }
            catch (TimeoutException te)
            {
                response = Response.Fail(request.id, ErrorCodes.TIMEOUT, te.Message);
            }
            catch (Exception e)
            {
                response = Response.Fail(request.id, ErrorCodes.EXCEPTION, e.Message);
            }
            stopwatch.Stop();

            response.meta = new ResponseMeta { executionMs = (int)stopwatch.ElapsedMilliseconds };
            return Serialize(response);
        }

        static string Serialize(Response response)
        {
            return JsonConvert.SerializeObject(response, SerializerSettings);
        }
    }
}
