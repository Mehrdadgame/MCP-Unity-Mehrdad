using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>
    /// Editor-level actions. In Phase 1 this also hosts the smoke path
    /// (<c>ping</c>, <c>get_state</c>) that proves the full round trip works.
    /// </summary>
    public class EditorHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "ping": return Ping();
                case "get_state": return GetState();
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
    }
}
