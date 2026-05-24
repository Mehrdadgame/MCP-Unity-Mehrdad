using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Scene create/open/save and queries. (Full Phase 3 scene tooling expands this.)</summary>
    public class SceneHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "new":
                case "create": return NewScene(p);
                case "save": return Save(p);
                case "open": return Open(p);
                case "get_open_scenes":
                case "list": return GetOpenScenes();
                case "get_active": return ActiveInfo();
                default: throw UnknownAction(action);
            }
        }

        static object NewScene(JObject p)
        {
            var mode = OptBool(p, "additive", false) ? NewSceneMode.Additive : NewSceneMode.Single;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
            string path = OptString(p, "path", null);
            if (!string.IsNullOrEmpty(path)) EditorSceneManager.SaveScene(scene, path);
            return SceneInfo(scene);
        }

        static object Save(JObject p)
        {
            var scene = SceneManager.GetActiveScene();
            string path = OptString(p, "path", null);
            bool ok = string.IsNullOrEmpty(path)
                ? EditorSceneManager.SaveScene(scene)
                : EditorSceneManager.SaveScene(scene, path);
            return new { saved = ok, path = scene.path, name = scene.name };
        }

        static object Open(JObject p)
        {
            string path = RequireString(p, "path");
            var mode = OptBool(p, "additive", false) ? OpenSceneMode.Additive : OpenSceneMode.Single;
            return SceneInfo(EditorSceneManager.OpenScene(path, mode));
        }

        static object GetOpenScenes()
        {
            var list = new List<object>();
            for (int i = 0; i < SceneManager.sceneCount; i++) list.Add(SceneInfo(SceneManager.GetSceneAt(i)));
            return new { count = list.Count, scenes = list };
        }

        static object ActiveInfo() => SceneInfo(SceneManager.GetActiveScene());

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
