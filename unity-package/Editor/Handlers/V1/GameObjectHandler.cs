using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>GameObject CRUD, transform, hierarchy and queries.</summary>
    public class GameObjectHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create": return Create(p);
                case "delete": return Delete(p);
                case "find": return FindOne(p);
                case "find_all": return FindAll(p);
                case "get_info":
                case "get": return GetInfo(p);
                case "rename": return Rename(p);
                case "set_parent":
                case "reparent": return SetParent(p);
                case "set_transform": return SetTransform(p);
                case "set_active": return SetActive(p);
                case "duplicate": return Duplicate(p);
                case "get_hierarchy":
                case "list": return GetHierarchy(p);
                case "set_tag": return SetTag(p);
                case "set_layer": return SetLayer(p);
                default: throw UnknownAction(action);
            }
        }

        static object Create(JObject p)
        {
            string name = OptString(p, "name", null);
            string primitive = OptString(p, "primitive", null);

            GameObject go;
            if (!string.IsNullOrEmpty(primitive))
            {
                go = GameObject.CreatePrimitive(ParsePrimitive(primitive));
                if (!string.IsNullOrEmpty(name)) go.name = name;
            }
            else
            {
                go = new GameObject(string.IsNullOrEmpty(name) ? "GameObject" : name);
            }
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);

            var parent = p["parent"];
            if (parent != null && parent.Type != JTokenType.Null)
                Undo.SetTransformParent(go.transform, ObjectFinder.Resolve(parent).transform, "Set Parent");

            if (p["position"] != null) go.transform.localPosition = ValueParser.ToVector3(p["position"]);
            if (p["rotation"] != null) go.transform.localEulerAngles = ValueParser.ToVector3(p["rotation"]);
            if (p["scale"] != null) go.transform.localScale = ValueParser.ToVector3(p["scale"], Vector3.one);

            string tag = OptString(p, "tag", null);
            if (!string.IsNullOrEmpty(tag)) TrySetTag(go, tag);

            MarkDirty(go);
            if (OptBool(p, "select", true)) Selection.activeGameObject = go;
            return Info(go);
        }

        static object Delete(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            int id = go.GetInstanceID();
            string name = go.name;
            var scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return new { deleted = true, name = name, instanceId = id };
        }

        static object FindOne(JObject p)
        {
            var matches = Search(p);
            if (matches.Count == 0) throw new HandlerException(ErrorCodes.NOT_FOUND, "No GameObject matched the query.");
            return Info(matches[0]);
        }

        static object FindAll(JObject p)
        {
            var list = new List<object>();
            foreach (var go in Search(p)) list.Add(InfoBrief(go));
            return new { count = list.Count, results = list };
        }

        static List<GameObject> Search(JObject p)
        {
            bool includeInactive = OptBool(p, "includeInactive", true);
            string name = OptString(p, "name", null);
            string path = OptString(p, "path", null);
            string tag = OptString(p, "tag", null);

            var result = new List<GameObject>();
            foreach (var go in ObjectFinder.AllSceneObjects(includeInactive))
            {
                if (name != null && go.name != name) continue;
                if (path != null && ObjectFinder.GetPath(go) != path) continue;
                if (tag != null && !go.CompareTag(tag)) continue;
                result.Add(go);
            }
            return result;
        }

        static object GetInfo(JObject p) => Info(ObjectFinder.Resolve(p["target"]));

        static object Rename(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            Undo.RecordObject(go, "Rename");
            go.name = RequireString(p, "name");
            MarkDirty(go);
            return Info(go);
        }

        static object SetParent(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var parentToken = p["parent"];
            Transform parent = (parentToken == null || parentToken.Type == JTokenType.Null)
                ? null : ObjectFinder.Resolve(parentToken).transform;
            Undo.SetTransformParent(go.transform, parent, "Set Parent");
            MarkDirty(go);
            return Info(go);
        }

        static object SetTransform(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var t = go.transform;
            Undo.RecordObject(t, "Set Transform");
            bool world = string.Equals(OptString(p, "space", "local"), "world", StringComparison.OrdinalIgnoreCase);

            if (p["position"] != null) { var v = ValueParser.ToVector3(p["position"]); if (world) t.position = v; else t.localPosition = v; }
            if (p["rotation"] != null) { var v = ValueParser.ToVector3(p["rotation"]); if (world) t.eulerAngles = v; else t.localEulerAngles = v; }
            if (p["scale"] != null) t.localScale = ValueParser.ToVector3(p["scale"], Vector3.one);

            MarkDirty(go);
            return Info(go);
        }

        static object SetActive(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            if (p["active"] == null) throw Invalid("Missing 'active' (boolean).");
            bool active = p["active"].Value<bool>();
            Undo.RecordObject(go, "Set Active");
            go.SetActive(active);
            MarkDirty(go);
            return Info(go);
        }

        static object Duplicate(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var dup = UnityEngine.Object.Instantiate(go);
            dup.transform.SetParent(go.transform.parent, true);
            dup.name = OptString(p, "name", go.name);
            Undo.RegisterCreatedObjectUndo(dup, "Duplicate " + go.name);
            MarkDirty(dup);
            if (OptBool(p, "select", true)) Selection.activeGameObject = dup;
            return Info(dup);
        }

        static object GetHierarchy(JObject p)
        {
            int maxDepth = OptInt(p, "maxDepth", 6);
            var root = p["root"];
            if (root != null && root.Type != JTokenType.Null)
                return Node(ObjectFinder.Resolve(root).transform, maxDepth, 0);

            var roots = new List<object>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var r in sc.GetRootGameObjects()) roots.Add(Node(r.transform, maxDepth, 0));
            }
            return new { roots = roots };
        }

        static object SetTag(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            Undo.RecordObject(go, "Set Tag");
            TrySetTag(go, RequireString(p, "tag"));
            MarkDirty(go);
            return Info(go);
        }

        static object SetLayer(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var lt = p["layer"];
            if (lt == null) throw Invalid("Missing 'layer' (name or index).");
            int layer = lt.Type == JTokenType.Integer ? lt.Value<int>() : LayerMask.NameToLayer(lt.ToString());
            if (layer < 0 || layer > 31) throw Invalid("Invalid layer '" + lt + "'.");
            Undo.RecordObject(go, "Set Layer");
            go.layer = layer;
            MarkDirty(go);
            return Info(go);
        }

        // -- helpers --------------------------------------------------------

        static object Node(Transform t, int maxDepth, int depth)
        {
            List<object> children = null;
            if (depth < maxDepth && t.childCount > 0)
            {
                children = new List<object>();
                for (int i = 0; i < t.childCount; i++) children.Add(Node(t.GetChild(i), maxDepth, depth + 1));
            }
            return new
            {
                name = t.name,
                instanceId = t.gameObject.GetInstanceID(),
                active = t.gameObject.activeSelf,
                childCount = t.childCount,
                children = children,
            };
        }

        static object Info(GameObject go) => new
        {
            instanceId = go.GetInstanceID(),
            name = go.name,
            path = ObjectFinder.GetPath(go),
            activeSelf = go.activeSelf,
            activeInHierarchy = go.activeInHierarchy,
            tag = go.tag,
            layer = go.layer,
            layerName = LayerMask.LayerToName(go.layer),
            transform = new
            {
                position = V(go.transform.position),
                localPosition = V(go.transform.localPosition),
                eulerAngles = V(go.transform.eulerAngles),
                localScale = V(go.transform.localScale),
            },
            components = ComponentNames(go),
            childCount = go.transform.childCount,
        };

        static object InfoBrief(GameObject go) => new
        {
            instanceId = go.GetInstanceID(),
            name = go.name,
            path = ObjectFinder.GetPath(go),
            activeSelf = go.activeSelf,
        };

        static object V(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        static List<string> ComponentNames(GameObject go)
        {
            var list = new List<string>();
            foreach (var c in go.GetComponents<Component>())
                list.Add(c == null ? "(missing script)" : c.GetType().Name);
            return list;
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static void TrySetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch
            {
                throw new HandlerException(ErrorCodes.INVALID_PARAMS,
                    "Tag '" + tag + "' is not defined. Add it in Project Settings > Tags and Layers first.");
            }
        }

        static PrimitiveType ParsePrimitive(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "cube": return PrimitiveType.Cube;
                case "sphere": return PrimitiveType.Sphere;
                case "capsule": return PrimitiveType.Capsule;
                case "cylinder": return PrimitiveType.Cylinder;
                case "plane": return PrimitiveType.Plane;
                case "quad": return PrimitiveType.Quad;
                default:
                    throw new HandlerException(ErrorCodes.INVALID_PARAMS,
                        "Unknown primitive '" + s + "'. Use Cube/Sphere/Capsule/Cylinder/Plane/Quad.");
            }
        }
    }
}
