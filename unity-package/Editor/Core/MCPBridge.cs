using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        static int _startRetries;
        static double _nextWatchdogCheck;

        static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        static MCPBridge()
        {
            MainThreadDispatcher.Initialize();
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            // Rebind right after every recompile/domain reload (fires on the main thread,
            // immediately — no need to wait for the Editor to be focused).
            AssemblyReloadEvents.afterAssemblyReload += AutoStart;
            EditorApplication.delayCall += AutoStart; // initial editor load
            EditorApplication.update += Watchdog;     // self-heal if the listener ever dies
        }

        static void AutoStart()
        {
            if (EditorPrefs.GetBool(AutoStartKey, true)) Start();
        }

        /// <summary>
        /// Periodic self-heal: if the bridge somehow isn't listening (a missed reload event,
        /// an accept-loop fault, a stale port), bring it back within ~5s — so the Python
        /// server can always reconnect after Claude Desktop restarts it.
        /// </summary>
        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < _nextWatchdogCheck) return;
            _nextWatchdogCheck = EditorApplication.timeSinceStartup + 5.0;

            if (!_running
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && EditorPrefs.GetBool(AutoStartKey, true))
            {
                Start();
            }
        }

        public static bool IsRunning { get { return _running; } }
        public static int Port { get { return DefaultPort; } }

        public static void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, DefaultPort);
                // Allow rebinding a port that may still be in TIME_WAIT from the pre-reload
                // listener — without this, restart after a recompile can fail with
                // "address already in use" and the bridge appears disconnected.
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _running = true;
                _startRetries = 0;

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
                try { if (_listener != null) _listener.Stop(); } catch { /* ignore */ }
                _listener = null;
                _startRetries++;
                if (_startRetries <= 10)
                {
                    Debug.LogWarning("[MCP] Bridge start failed (" + e.Message + "); retry " + _startRetries + " shortly.");
                    EditorApplication.delayCall += () => { if (!_running) Start(); };
                }
                else
                {
                    Debug.LogError("[MCP] Bridge failed to start after retries: " + e.Message);
                }
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
            JObject jo;
            try
            {
                jo = JObject.Parse(json);
            }
            catch (Exception e)
            {
                return Serialize(Response.Fail(null, ErrorCodes.MALFORMED_REQUEST, "Invalid JSON: " + e.Message));
            }
            if (jo == null)
                return Serialize(Response.Fail(null, ErrorCodes.MALFORMED_REQUEST, "Empty request."));

            string id = (string)jo["id"];
            string type = (string)jo["type"];
            string label = type == "batch"
                ? "batch:" + ((string)jo["undoGroup"] ?? "?") + " (" + (jo["ops"] is JArray opsArr ? opsArr.Count : 0) + " ops)"
                : ((string)jo["category"] ?? "?") + "." + ((string)jo["action"] ?? "?");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Response response;
            try
            {
                object data = type == "batch"
                    ? MainThreadDispatcher.Run(() => BatchExecutor.Execute(jo), 300000)
                    : MainThreadDispatcher.Run(() => CommandRouter.Route(jo.ToObject<Request>()));
                response = Response.Ok(id, data);
            }
            catch (HandlerException he)
            {
                response = Response.Fail(id, he.Code, he.Message);
            }
            catch (TimeoutException te)
            {
                response = Response.Fail(id, ErrorCodes.TIMEOUT, te.Message);
            }
            catch (Exception e)
            {
                response = Response.Fail(id, ErrorCodes.EXCEPTION, e.Message);
            }
            stopwatch.Stop();

            response.meta = new ResponseMeta { executionMs = (int)stopwatch.ElapsedMilliseconds };
            RequestLog.Record(label, response.success, (int)stopwatch.ElapsedMilliseconds);
            return Serialize(response);
        }

        static string Serialize(Response response)
        {
            return JsonConvert.SerializeObject(response, SerializerSettings);
        }
    }
}
