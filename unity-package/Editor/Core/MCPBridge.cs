using System;
using System.IO;
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
        // "The user wants the bridge running." Default TRUE. Only an explicit menu
        // Stop sets it false; involuntary stops (domain reload, editor quit) never
        // touch it, so the bridge always self-restarts after a recompile.
        // NOTE: a fresh key (not the old "MCP.AutoStart") so any stale false value
        // from the removed Toggle Auto-Start menu can't keep recovery disabled.
        const string EnabledKey = "MCP.Enabled";

        static TcpListener _listener;
        static Thread _acceptThread;
        static volatile bool _running;
        static int _startRetries;
        static double _nextWatchdogCheck;
        static int _activePort = DefaultPort;
        const int MaxPortAttempts = 10;

        static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        static MCPBridge()
        {
            MainThreadDispatcher.Initialize();
            // Involuntary teardown only — these must NOT disable auto-recovery.
            EditorApplication.quitting += StopListener;
            AssemblyReloadEvents.beforeAssemblyReload += StopListener;
            // Rebind right after every recompile/domain reload (fires on the main thread,
            // immediately — no need to wait for the Editor to be focused).
            AssemblyReloadEvents.afterAssemblyReload += AutoStart;
            EditorApplication.delayCall += AutoStart; // initial editor load
            EditorApplication.update += Watchdog;     // self-heal if the listener ever dies
        }

        public static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledKey, true); }
            set { EditorPrefs.SetBool(EnabledKey, value); }
        }

        static void AutoStart()
        {
            if (Enabled) Start();
        }

        /// <summary>
        /// Periodic self-heal: if the bridge isn't listening but should be (a missed
        /// reload event, an accept-loop fault, a stale port), bring it back within ~5s —
        /// so the Python server can always reconnect after Claude Desktop restarts it.
        /// </summary>
        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < _nextWatchdogCheck) return;
            _nextWatchdogCheck = EditorApplication.timeSinceStartup + 5.0;

            if (!_running
                && Enabled
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating)
            {
                Start();
            }
        }

        public static bool IsRunning { get { return _running; } }
        public static int Port { get { return _activePort; } }

        /// <summary>Menu "Start": mark the bridge as wanted (persists across reloads) and start it.</summary>
        public static void Enable()
        {
            Enabled = true;
            Start();
        }

        /// <summary>Menu "Stop": the only way to keep the bridge down across reloads.</summary>
        public static void Disable()
        {
            Enabled = false;
            StopListener();
        }

        public static void Start()
        {
            if (_running) return;

            // Try the default port, then walk forward — sidesteps any phantom/leaked
            // listener (e.g. a previous Unity crash that left a zombie socket on 6400).
            Exception lastEx = null;
            for (int port = DefaultPort; port < DefaultPort + MaxPortAttempts; port++)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Start();
                    _running = true;
                    _activePort = port;
                    _startRetries = 0;
                    WritePortFile(); // publish the active port so the Python client can find it

                    _acceptThread = new Thread(AcceptLoop)
                    {
                        IsBackground = true,
                        Name = "MCP-Bridge-Accept",
                    };
                    _acceptThread.Start();

                    if (port == DefaultPort)
                        Debug.Log("[MCP] Bridge listening on 127.0.0.1:" + port);
                    else
                        Debug.LogWarning("[MCP] Bridge listening on 127.0.0.1:" + port
                            + " (fell back from " + DefaultPort + "; another process holds it).");
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { if (_listener != null) _listener.Stop(); } catch { /* ignore */ }
                    _listener = null;
                }
            }

            // Every port in the range was unavailable.
            _running = false;
            _startRetries++;
            if (_startRetries <= 5)
            {
                Debug.LogWarning("[MCP] Bridge bind failed on " + DefaultPort + ".." + (DefaultPort + MaxPortAttempts - 1)
                    + " (" + (lastEx != null ? lastEx.Message : "?") + "); retry " + _startRetries + " shortly.");
                EditorApplication.delayCall += () => { if (!_running) Start(); };
            }
            else
            {
                Debug.LogError("[MCP] Bridge could not bind any port in range: "
                    + (lastEx != null ? lastEx.Message : "unknown"));
            }
        }

        /// <summary>
        /// Tears down the socket WITHOUT changing the Enabled pref. Used for domain
        /// reloads and editor quit, so the bridge always comes back on its own.
        /// </summary>
        static void StopListener()
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

        // -- port discovery file --
        // Writes the active port to %LOCALAPPDATA%\UnityMCP\port.txt so the Python
        // client can connect to the right port even when the default is taken by a
        // zombie/phantom listener.
        static void WritePortFile()
        {
            try
            {
                string path = PortFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, _activePort.ToString());
            }
            catch { /* best-effort */ }
        }

        static string PortFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "UnityMCP", "port.txt");
        }
    }
}
