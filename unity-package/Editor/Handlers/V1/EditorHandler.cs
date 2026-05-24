using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>
    /// Editor-level actions: the Phase 1 smoke path (ping, get_state) plus play-mode
    /// control, menu execution, and an asset refresh.
    /// </summary>
    public class EditorHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "ping": return Ping();
                case "get_state": return GetState();
                case "play": return SetPlaying(true);
                case "stop": return SetPlaying(false);
                case "pause": return SetPaused(OptBool(p, "paused", true));
                case "execute_menu_item": return ExecuteMenuItem(p);
                case "refresh": AssetDatabase.Refresh(); return new { refreshed = true };
                default: throw UnknownAction(action);
            }
        }

        static object Ping()
        {
            return new
            {
                pong = true,
                unityVersion = Application.unityVersion,
                productName = Application.productName,
                time = DateTime.UtcNow.ToString("o"),
            };
        }

        static object GetState()
        {
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                applicationPlatform = Application.platform.ToString(),
                unityVersion = Application.unityVersion,
                productName = Application.productName,
                dataPath = Application.dataPath,
            };
        }

        static object SetPlaying(bool playing)
        {
            EditorApplication.isPlaying = playing;
            return new { isPlaying = playing };
        }

        static object SetPaused(bool paused)
        {
            EditorApplication.isPaused = paused;
            return new { isPaused = EditorApplication.isPaused };
        }

        static object ExecuteMenuItem(JObject p)
        {
            string item = RequireString(p, "menuItem");
            bool executed = EditorApplication.ExecuteMenuItem(item);
            return new { executed = executed, menuItem = item };
        }
    }
}
