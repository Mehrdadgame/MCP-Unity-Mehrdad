using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Protocol;

namespace UnityMCP.Utils
{
    /// <summary>
    /// Resolves a request's "target" token to a GameObject. A target may be an
    /// integer instanceId, a full hierarchy path ("Parent/Child"), or a plain name.
    /// </summary>
    public static class ObjectFinder
    {
        public static GameObject Resolve(JToken target)
        {
            if (target == null || target.Type == JTokenType.Null)
                throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Missing 'target' (instanceId, path, or name).");

            if (target.Type == JTokenType.Integer)
                return ByInstanceId(target.Value<int>());

            string key = target.ToString();

            var active = GameObject.Find(key);
            if (active != null) return active;

            var matches = FindAll(key, true);
            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0)
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No GameObject matching '" + key + "'.");
            throw new HandlerException(ErrorCodes.AMBIGUOUS_TARGET,
                matches.Count + " GameObjects match '" + key + "'. Use a full path or an instanceId.");
        }

        public static GameObject ByInstanceId(int id)
        {
            var go = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (go == null)
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No GameObject with instanceId " + id + ".");
            return go;
        }

        public static List<GameObject> AllSceneObjects(bool includeInactive)
        {
            var list = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                    Collect(root.transform, list, includeInactive);
            }
            return list;
        }

        static void Collect(Transform t, List<GameObject> list, bool includeInactive)
        {
            if (!includeInactive && !t.gameObject.activeInHierarchy) return;
            list.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), list, includeInactive);
        }

        public static List<GameObject> FindAll(string key, bool includeInactive)
        {
            var result = new List<GameObject>();
            foreach (var go in AllSceneObjects(includeInactive))
                if (go.name == key || GetPath(go) == key) result.Add(go);
            return result;
        }

        public static string GetPath(GameObject go)
        {
            var t = go.transform;
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
