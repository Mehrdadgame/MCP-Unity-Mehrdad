using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Generates EditorWindow scripts (optionally UI Toolkit) and opens/closes them.</summary>
    public class EditorWindowHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "scaffold": return Scaffold(p);
                case "open_window": return OpenWindow(p);
                case "close_window": return CloseWindow(p);
                default: throw UnknownAction(action);
            }
        }

        static object Scaffold(JObject p)
        {
            string name = RequireString(p, "name");
            string ns = OptString(p, "namespace", null);
            string folder = OptString(p, "folder", "Assets/Editor").TrimEnd('/');
            string title = OptString(p, "title", Humanize(name));
            string menuPath = OptString(p, "menuPath", "Tools/" + title);
            bool useUITK = OptBool(p, "useUITK", true);

            string csPath = folder + "/" + name + ".cs";
            string uxmlPath = folder + "/" + name + ".uxml";
            string ussPath = folder + "/" + name + ".uss";

            AssetUtil.EnsureFolder(folder);

            string cs = OptString(p, "csContent", null);
            if (cs == null) cs = BuildCs(name, ns, title, menuPath, useUITK, uxmlPath, ussPath);
            File.WriteAllText(AbsPath(csPath), cs);
            AssetDatabase.ImportAsset(csPath);

            var created = new List<string> { csPath };
            if (useUITK)
            {
                string uxml = OptString(p, "uxmlContent", null);
                if (uxml == null) uxml = BuildUxml(title);
                File.WriteAllText(AbsPath(uxmlPath), uxml);
                AssetDatabase.ImportAsset(uxmlPath);
                created.Add(uxmlPath);

                string uss = OptString(p, "ussContent", null);
                if (uss == null) uss = BuildUss();
                File.WriteAllText(AbsPath(ussPath), uss);
                AssetDatabase.ImportAsset(ussPath);
                created.Add(ussPath);
            }

            return new
            {
                created = created,
                className = name,
                menuPath = menuPath,
                recompileHint = "Recompile, then editorwindow.open_window to show it.",
            };
        }

        static string BuildCs(string name, string ns, string title, string menuPath, bool useUITK, string uxmlPath, string ussPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("using UnityEngine;\nusing UnityEditor;\n");
            if (useUITK) sb.Append("using UnityEngine.UIElements;\n");
            sb.Append("\n");
            string ind = "";
            if (!string.IsNullOrEmpty(ns)) { sb.Append("namespace ").Append(ns).Append("\n{\n"); ind = "    "; }

            sb.Append(ind).Append("public class ").Append(name).Append(" : EditorWindow\n").Append(ind).Append("{\n");
            sb.Append(ind).Append("    [MenuItem(\"").Append(menuPath).Append("\")]\n");
            sb.Append(ind).Append("    public static void ShowWindow()\n").Append(ind).Append("    {\n");
            sb.Append(ind).Append("        var wnd = GetWindow<").Append(name).Append(">();\n");
            sb.Append(ind).Append("        wnd.titleContent = new GUIContent(\"").Append(title).Append("\");\n");
            sb.Append(ind).Append("    }\n\n");

            if (useUITK)
            {
                sb.Append(ind).Append("    public void CreateGUI()\n").Append(ind).Append("    {\n");
                sb.Append(ind).Append("        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(\"").Append(uxmlPath).Append("\");\n");
                sb.Append(ind).Append("        if (tree != null) tree.CloneTree(rootVisualElement);\n");
                sb.Append(ind).Append("        var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(\"").Append(ussPath).Append("\");\n");
                sb.Append(ind).Append("        if (style != null) rootVisualElement.styleSheets.Add(style);\n");
                sb.Append(ind).Append("        BindUI();\n");
                sb.Append(ind).Append("    }\n\n");
                sb.Append(ind).Append("    void BindUI()\n").Append(ind).Append("    {\n");
                sb.Append(ind).Append("        // Query elements by name and wire events, e.g.:\n");
                sb.Append(ind).Append("        // var btn = rootVisualElement.Q<Button>(\"spawnButton\");\n");
                sb.Append(ind).Append("        // if (btn != null) btn.clicked += () => Debug.Log(\"Spawn\");\n");
                sb.Append(ind).Append("    }\n");
            }
            else
            {
                sb.Append(ind).Append("    void OnGUI()\n").Append(ind).Append("    {\n");
                sb.Append(ind).Append("        GUILayout.Label(\"").Append(title).Append("\", EditorStyles.boldLabel);\n");
                sb.Append(ind).Append("    }\n");
            }

            sb.Append(ind).Append("}\n");
            if (!string.IsNullOrEmpty(ns)) sb.Append("}\n");
            return sb.ToString();
        }

        static string BuildUxml(string title)
        {
            return
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\">\n" +
                "    <ui:VisualElement name=\"root\" class=\"root\">\n" +
                "        <ui:Label text=\"" + title + "\" class=\"header\" />\n" +
                "    </ui:VisualElement>\n" +
                "</ui:UXML>\n";
        }

        static string BuildUss()
        {
            return
                ".root { padding: 10px; }\n" +
                ".header { font-size: 16px; -unity-font-style: bold; margin-bottom: 8px; }\n";
        }

        static object OpenWindow(JObject p)
        {
            string typeName = RequireString(p, "typeName");
            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null || !typeof(EditorWindow).IsAssignableFrom(type))
                throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "EditorWindow type '" + typeName + "' not found (recompile after scaffold?).");
            var wnd = EditorWindow.GetWindow(type);
            wnd.Show();
            return new { opened = true, type = type.Name };
        }

        static object CloseWindow(JObject p)
        {
            string typeName = RequireString(p, "typeName");
            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null) throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "Type '" + typeName + "' not found.");
            int closed = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll(type))
            {
                var w = o as EditorWindow;
                if (w != null) { w.Close(); closed++; }
            }
            return new { closed = closed };
        }

        static string Humanize(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string AbsPath(string assetPath)
        {
            string data = Application.dataPath;
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
                return data.Substring(0, data.Length - "Assets".Length) + assetPath;
            return assetPath;
        }
    }
}
