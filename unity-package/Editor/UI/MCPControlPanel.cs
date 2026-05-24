using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityMCP.Core;

namespace UnityMCP.UI
{
    /// <summary>
    /// Status/observability window for the bridge, built with UI Toolkit (itself a demo of
    /// what editorwindow.scaffold produces). Shows running state + a live request log.
    /// </summary>
    public class MCPControlPanel : EditorWindow
    {
        Label _status;
        ScrollView _log;

        [MenuItem("Tools/MCP/Control Panel", false, 40)]
        public static void ShowWindow()
        {
            var wnd = GetWindow<MCPControlPanel>();
            wnd.titleContent = new GUIContent("MCP Bridge");
            wnd.minSize = new Vector2(360, 320);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            _status = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 6 } };
            root.Add(_status);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 8;
            row.Add(new Button(() => { MCPBridge.Start(); Refresh(); }) { text = "Start" });
            row.Add(new Button(() => { MCPBridge.Stop(); Refresh(); }) { text = "Stop" });
            row.Add(new Button(() => { RequestLog.Clear(); Refresh(); }) { text = "Clear Log" });
            root.Add(row);

            root.Add(new Label("Request Log (newest first):") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });

            _log = new ScrollView();
            _log.style.flexGrow = 1;
            _log.style.borderTopWidth = 1; _log.style.borderBottomWidth = 1;
            _log.style.borderLeftWidth = 1; _log.style.borderRightWidth = 1;
            var border = new Color(0f, 0f, 0f, 0.3f);
            _log.style.borderTopColor = border; _log.style.borderBottomColor = border;
            _log.style.borderLeftColor = border; _log.style.borderRightColor = border;
            root.Add(_log);

            Refresh();
            rootVisualElement.schedule.Execute(Refresh).Every(1000);
        }

        void Refresh()
        {
            if (_status == null) return;
            _status.text = (MCPBridge.IsRunning ? "● Running" : "○ Stopped")
                           + "   port " + MCPBridge.Port + "   |   total requests: " + RequestLog.Total;
            _status.style.color = MCPBridge.IsRunning ? new Color(0.5f, 0.85f, 0.5f) : new Color(0.85f, 0.6f, 0.4f);

            _log.Clear();
            var entries = RequestLog.Snapshot();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                var lbl = new Label((e.success ? "✓ " : "✗ ") + e.time + "   " + e.label + "   " + e.ms + "ms");
                lbl.style.color = e.success ? new Color(0.7f, 0.9f, 0.7f) : new Color(0.95f, 0.55f, 0.55f);
                lbl.style.fontSize = 11;
                lbl.style.paddingTop = 1; lbl.style.paddingBottom = 1;
                _log.Add(lbl);
            }
            if (entries.Count == 0)
                _log.Add(new Label("  (no requests yet)") { style = { color = new Color(0.6f, 0.6f, 0.6f) } });
        }
    }
}
