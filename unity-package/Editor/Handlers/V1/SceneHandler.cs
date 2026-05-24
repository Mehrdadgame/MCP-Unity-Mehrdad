using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Scene create/open/save/close, active scene, build scenes, and root queries.</summary>
    public class SceneHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "new":
                case "create": return NewScene(p);
                case "save": return Save(p);
                case "save_all": return SaveAll();
                case "open": return Open(p);
                case "close": return Close(p);
                case "set_active": return SetActive(p);
                case "get_open_scenes":
                case "list": return GetOpenScenes();
                case "get_active": return ActiveInfo();
                case "get_build_scenes": return GetBuildScenes();
                case "get_root_objects": return GetRootObjects(p);
                default: throw UnknownAction(action);
            }
        }

        static object NewScene(JObject p)
        {
            var mode = OptBool(p, "additive", false) ? NewSceneMode.Additive : NewSceneMode.Single;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
            string path = OptString(p, "path", null);
            if (!string.IsNullOrEmpty(path))
            {
                AssetUtil.EnsureFolderForAsset(path);
                EditorSceneManager.SaveScene(scene, path);
            }
            return SceneInfo(scene);
        }

        static object Save(JObject p)
        {
            var scene = SceneManager.GetActiveScene();
            string path = OptString(p, "path", null);
            if (!string.IsNullOrEmpty(path)) AssetUtil.EnsureFolderForAsset(path);
            bool ok = string.IsNullOrEmpty(path)
                ? EditorSceneManager.SaveScene(scene)
                : EditorSceneManager.SaveScene(scene, path);
            return new { saved = ok, path = scene.path, name = scene.name };
        }

        static object SaveAll() => new { saved = EditorSceneManager.SaveOpenScenes() };

        static object Open(JObject p)
        {
            string path = RequireString(p, "path");
            var mode = OptBool(p, "additive", false) ? OpenSceneMode.Additive : OpenSceneMode.Single;
            return SceneInfo(EditorSceneManager.OpenScene(path, mode));
        }

        static object Close(JObject p)
        {
            var scene = ResolveScene(p);
            bool ok = EditorSceneManager.CloseScene(scene, OptBool(p, "remove", true));
            return new { closed = ok, name = scene.name };
        }

        static object SetActive(JObject p)
        {
            var scene = ResolveScene(p);
            return new { active = SceneManager.SetActiveScene(scene), name = scene.name };
        }

        static object GetOpenScenes()
        {
            var list = new List<object>();
            for (int i = 0; i < SceneManager.sceneCount; i++) list.Add(SceneInfo(SceneManager.GetSceneAt(i)));
            return new { count = list.Count, scenes = list };
        }

        static object ActiveInfo() => SceneInfo(SceneManager.GetActiveScene());

        static object GetBuildScenes()
        {
            var list = new List<object>();
            foreach (var s in EditorBuildSettings.scenes)
                list.Add(new { path = s.path, enabled = s.enabled, guid = s.guid.ToString() });
            return new { count = list.Count, scenes = list };
        }

        static object GetRootObjects(JObject p)
        {
            var scene = (p["path"] != null || p["name"] != null) ? ResolveScene(p) : SceneManager.GetActiveScene();
            var list = new List<object>();
            foreach (var go in scene.GetRootGameObjects())
                list.Add(new { name = go.name, instanceId = go.GetInstanceID(), active = go.activeSelf, childCount = go.transform.childCount });
            return new { scene = scene.name, count = list.Count, roots = list };
        }

        static Scene ResolveScene(JObject p)
        {
            string path = OptString(p, "path", null);
            string name = OptString(p, "name", null);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (path != null && s.path == path) return s;
                if (name != null && s.name == name) return s;
            }
            throw new HandlerException(ErrorCodes.NOT_FOUND, "No open scene matching the given path/name.");
        }

        static object SceneInfo(Scene s) => new
        {
            name = s.name,
            path = s.path,
            isValid = s.IsValid(),
            isLoaded = s.isLoaded,
            isDirty = s.isDirty,
            rootCount = s.IsValid() ? s.rootCount : 0,
            buildIndex = s.buildIndex,
        };
    }
}
