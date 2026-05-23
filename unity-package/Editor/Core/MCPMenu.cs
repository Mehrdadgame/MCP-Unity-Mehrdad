using UnityEditor;
using UnityEngine;

namespace UnityMCP.Core
{
    /// <summary>
    /// Minimal Tools menu for controlling the bridge during development and for
    /// verifying the Phase 1 smoke path by hand.
    /// </summary>
    public static class MCPMenu
    {
        const string AutoStartKey = "MCP.AutoStart";

        [MenuItem("Tools/MCP/Start Bridge", false, 0)]
        static void StartBridge() => MCPBridge.Start();

        [MenuItem("Tools/MCP/Start Bridge", true)]
        static bool StartBridgeValidate() => !MCPBridge.IsRunning;

        [MenuItem("Tools/MCP/Stop Bridge", false, 1)]
        static void StopBridge() => MCPBridge.Stop();

        [MenuItem("Tools/MCP/Stop Bridge", true)]
        static bool StopBridgeValidate() => MCPBridge.IsRunning;

        [MenuItem("Tools/MCP/Print Status", false, 20)]
        static void PrintStatus()
        {
            Debug.Log("[MCP] running=" + MCPBridge.IsRunning +
                      " port=" + MCPBridge.Port +
                      " autoStart=" + EditorPrefs.GetBool(AutoStartKey, true));
        }

        [MenuItem("Tools/MCP/Toggle Auto-Start", false, 21)]
        static void ToggleAutoStart()
        {
            bool value = !EditorPrefs.GetBool(AutoStartKey, true);
            EditorPrefs.SetBool(AutoStartKey, value);
            Debug.Log("[MCP] Auto-start on editor load = " + value);
        }
    }
}
