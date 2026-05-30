using UnityEditor;
using UnityEngine;

namespace UnityMCP.Core
{
    /// <summary>
    /// Tools menu for controlling the bridge. "Start" marks the bridge as wanted so it
    /// auto-restarts after every recompile; "Stop" is the only thing that keeps it down.
    /// </summary>
    public static class MCPMenu
    {
        [MenuItem("Tools/MCP/Start Bridge", false, 0)]
        static void StartBridge() => MCPBridge.Enable();

        [MenuItem("Tools/MCP/Start Bridge", true)]
        static bool StartBridgeValidate() => !MCPBridge.IsRunning;

        [MenuItem("Tools/MCP/Stop Bridge", false, 1)]
        static void StopBridge() => MCPBridge.Disable();

        [MenuItem("Tools/MCP/Stop Bridge", true)]
        static bool StopBridgeValidate() => MCPBridge.IsRunning;

        [MenuItem("Tools/MCP/Restart Bridge", false, 2)]
        static void RestartBridge() => MCPBridge.Enable();

        [MenuItem("Tools/MCP/Print Status", false, 20)]
        static void PrintStatus()
        {
            Debug.Log("[MCP] running=" + MCPBridge.IsRunning +
                      " port=" + MCPBridge.Port +
                      " enabled=" + MCPBridge.Enabled);
        }
    }
}
