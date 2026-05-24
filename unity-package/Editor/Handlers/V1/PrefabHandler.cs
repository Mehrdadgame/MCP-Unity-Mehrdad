using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Prefab authoring: create from scene, instantiate, variants, overrides, unpack, stages.</summary>
    public class PrefabHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create":
                case "create_from_scene": return Create(p);
                case "instantiate": return Instantiate(p);
                case "create_variant": return CreateVariant(p);
                case "apply_overrides": return ApplyOverrides(p);
                case "revert_overrides": return RevertOverrides(p);
                case "get_overrides": return GetOverrides(p);
                case "unpack": return Unpack(p);
                case "is_prefab_instance": return IsInstance(p);
                case "open_stage":
                case "edit_in_isolation": return OpenStage(p);
                case "close_stage": return CloseStage(p);
                default: throw UnknownAction(action);
            }
        }

        static object Create(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            string path = RequireString(p, "path");
            AssetUtil.EnsureFolderForAsset(path);
            bool connect = OptBool(p, "connect", true);
            bool success;
            GameObject prefab = connect
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction, out success)
                : PrefabUtility.SaveAsPrefabAsset(go, path, out success);
            if (!success || prefab == null)
                throw new HandlerException(ErrorCodes.IO_ERROR, "Failed to save prefab at '" + path + "'.");
            return new { guid = AssetDatabase.AssetPathToGUID(path), path = path, name = prefab.name };
        }

        static object Instantiate(JObject p)
        {
            string path = RequireString(p, "path");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No prefab at '" + path + "'.");

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(inst, "Instantiate Prefab");

            string name = OptString(p, "name", null);
            if (!string.IsNullOrEmpty(name)) inst.name = name;

            var parent = p["parent"];
            if (parent != null && parent.Type != JTokenType.Null)
                Undo.SetTransformParent(inst.transform, ObjectFinder.Resolve(parent).transform, "Set Parent");

            if (p["position"] != null) inst.transform.localPosition = ValueParser.ToVector3(p["position"]);
            if (p["rotation"] != null) inst.transform.localEulerAngles = ValueParser.ToVector3(p["rotation"]);
            if (p["scale"] != null) inst.transform.localScale = ValueParser.ToVector3(p["scale"], Vector3.one);

            if (OptBool(p, "unpack", false))
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            if (inst.scene.IsValid()) EditorSceneManager.MarkSceneDirty(inst.scene);
            if (OptBool(p, "select", true)) Selection.activeGameObject = inst;

            return new { instanceId = inst.GetInstanceID(), name = inst.name, path = ObjectFinder.GetPath(inst) };
        }

        static object CreateVariant(JObject p)
        {
            string basePath = RequireString(p, "basePath");
            string newPath = RequireString(p, "newPath");
            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (baseAsset == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No prefab at '" + basePath + "'.");
            AssetUtil.EnsureFolderForAsset(newPath);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
            bool success;
            var variant = PrefabUtility.SaveAsPrefabAsset(instance, newPath, out success);
            UnityEngine.Object.DestroyImmediate(instance);
            if (!success || variant == null)
                throw new HandlerException(ErrorCodes.IO_ERROR, "Failed to create variant at '" + newPath + "'.");
            return new { guid = AssetDatabase.AssetPathToGUID(newPath), path = newPath, name = variant.name };
        }

        static object ApplyOverrides(JObject p)
        {
            var go = RequireInstance(p);
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            return new { applied = true, name = go.name };
        }

        static object RevertOverrides(JObject p)
        {
            var go = RequireInstance(p);
            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            return new { reverted = true, name = go.name };
        }

        static object GetOverrides(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(go);
            return new
            {
                isPrefabInstance = isInstance,
                hasOverrides = isInstance && PrefabUtility.HasPrefabInstanceAnyOverrides(go, false),
                source = isInstance ? SourcePath(go) : null,
            };
        }

        static object Unpack(JObject p)
        {
            var go = RequireInstance(p);
            var mode = string.Equals(OptString(p, "mode", "OutermostRoot"), "Completely", System.StringComparison.OrdinalIgnoreCase)
                ? PrefabUnpackMode.Completely : PrefabUnpackMode.OutermostRoot;
            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.AutomatedAction);
            return new { unpacked = true, name = go.name, mode = mode.ToString() };
        }

        static object IsInstance(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            bool isInstance = PrefabUtility.IsPartOfPrefabInstance(go);
            return new { isPrefabInstance = isInstance, source = isInstance ? SourcePath(go) : null };
        }

        static object OpenStage(JObject p)
        {
            string path = RequireString(p, "path");
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No prefab at '" + path + "'.");
            var stage = PrefabStageUtility.OpenPrefab(path);
            return new { opened = stage != null, path = path };
        }

        static object CloseStage(JObject p)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return new { closed = false, note = "No prefab stage is open." };
            if (OptBool(p, "save", true)) AssetDatabase.SaveAssets();
            StageUtility.GoBackToPreviousStage();
            return new { closed = true };
        }

        // -- helpers --------------------------------------------------------

        static GameObject RequireInstance(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                throw new HandlerException(ErrorCodes.INVALID_PARAMS, "'" + go.name + "' is not a prefab instance.");
            return go;
        }

        static string SourcePath(GameObject go)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            return src != null ? AssetDatabase.GetAssetPath(src) : null;
        }
    }
}
