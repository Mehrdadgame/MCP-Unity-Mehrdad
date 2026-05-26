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
                case "project_info": return ProjectInfo();
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

        /// <summary>
        /// Capability probe: render pipeline, active platform, and which packages are present —
        /// so Claude can pick the right shaders/UI/input approach before acting.
        /// </summary>
        static object ProjectInfo()
        {
            return new
            {
                productName = Application.productName,
                unityVersion = Application.unityVersion,
                platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                dataPath = Application.dataPath,
                renderPipeline = DetectPipeline(),
                packages = new
                {
                    ugui = Has("UnityEngine.UI.Button, UnityEngine.UI"),
                    textmeshpro = Has("TMPro.TMP_Text, Unity.TextMeshPro"),
                    inputsystem = Has("UnityEngine.InputSystem.InputAction, Unity.InputSystem"),
                    uiToolkit = Has("UnityEngine.UIElements.VisualElement, UnityEngine.UIElementsModule"),
                    cinemachine = Has("Cinemachine.CinemachineVirtualCamera, Cinemachine") || Has("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine"),
                    timeline = Has("UnityEngine.Timeline.TimelineAsset, Unity.Timeline"),
                    addressables = Has("UnityEngine.AddressableAssets.Addressables, Unity.Addressables"),
                    localization = Has("UnityEngine.Localization.Locale, Unity.Localization"),
                    shadergraph = Has("UnityEditor.ShaderGraph.ShaderGraphImporter, Unity.ShaderGraph.Editor"),
                    animationRigging = Has("UnityEngine.Animations.Rigging.RigBuilder, Unity.Animation.Rigging"),
                    anim2d = Has("UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime"),
                    probuilder = Has("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder"),
                    testframework = Has("UnityEngine.TestTools.TestAttribute, UnityEngine.TestRunner"),
                },
            };
        }

        static bool Has(string assemblyQualifiedName)
        {
            return Type.GetType(assemblyQualifiedName) != null;
        }

        static string DetectPipeline()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (rp == null) return "BuiltIn";
            string n = rp.GetType().FullName ?? "";
            if (n.Contains("Universal")) return "URP";
            if (n.Contains("HighDefinition")) return "HDRP";
            return "Custom";
        }
    }
}
