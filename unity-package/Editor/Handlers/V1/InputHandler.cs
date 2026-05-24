using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
#if MCP_INPUTSYSTEM
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityMCP.Utils;
#endif

namespace UnityMCP.Handlers.V1
{
    /// <summary>Input System action assets (maps, actions, bindings). Gated on com.unity.inputsystem.</summary>
    public class InputHandler : HandlerBase
    {
#if !MCP_INPUTSYSTEM
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.INPUT_SYSTEM_NOT_INSTALLED,
                "The Input System (com.unity.inputsystem) is not installed. Install it via Package Manager.");
        }
#else
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_action_asset": return CreateAsset(p);
                case "add_action_map": return AddMap(p);
                case "add_action": return AddAction(p);
                case "add_binding": return AddBinding(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateAsset(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".inputactions")) path += ".inputactions";
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            string map = OptString(p, "map", null);
            if (!string.IsNullOrEmpty(map)) asset.AddActionMap(map);
            AssetUtil.EnsureFolderForAsset(path);
            Save(asset, path);
            UnityEngine.Object.DestroyImmediate(asset);
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object AddMap(JObject p)
        {
            string path = RequireString(p, "path");
            var asset = Load(path);
            asset.AddActionMap(RequireString(p, "mapName"));
            Save(asset, path);
            return new { ok = true };
        }

        static object AddAction(JObject p)
        {
            string path = RequireString(p, "path");
            var asset = Load(path);
            var map = asset.FindActionMap(RequireString(p, "map"), false);
            if (map == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Action map not found.");
            InputActionType t;
            switch (OptString(p, "type", "Button").ToLowerInvariant())
            {
                case "value": t = InputActionType.Value; break;
                case "passthrough": t = InputActionType.PassThrough; break;
                default: t = InputActionType.Button; break;
            }
            map.AddAction(RequireString(p, "actionName"), t);
            Save(asset, path);
            return new { ok = true };
        }

        static object AddBinding(JObject p)
        {
            string path = RequireString(p, "path");
            var asset = Load(path);
            var map = asset.FindActionMap(RequireString(p, "map"), false);
            if (map == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Action map not found.");
            var action = map.FindAction(RequireString(p, "action"));
            if (action == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Action not found.");
            action.AddBinding(RequireString(p, "binding"));
            Save(asset, path);
            return new { ok = true };
        }

        static InputActionAsset Load(string path)
        {
            var a = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (a == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No .inputactions asset at '" + path + "'.");
            return a;
        }

        static void Save(InputActionAsset asset, string path)
        {
            File.WriteAllText(AbsPath(path), asset.ToJson());
            AssetDatabase.ImportAsset(path);
        }

        static string AbsPath(string ap)
        {
            string data = Application.dataPath;
            if (ap.StartsWith("Assets/") || ap.StartsWith("Packages/"))
                return data.Substring(0, data.Length - "Assets".Length) + ap;
            return ap;
        }
#endif
    }
}
