using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>AudioSource authoring (component creation + property edits).</summary>
    public class AudioHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "add_source": return AddSource(p);
                case "set_source_properties": return SetSourceProps(p);
                default: throw UnknownAction(action);
            }
        }

        static object AddSource(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var src = go.GetComponent<AudioSource>();
            if (src == null) src = Undo.AddComponent<AudioSource>(go);

            string clipPath = OptString(p, "clipPath", null);
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No AudioClip at '" + clipPath + "'.");
                src.clip = clip;
            }
            Apply(src, p);
            MarkDirty(go);
            return new { ok = true, instanceId = src.GetInstanceID(), clip = src.clip != null ? src.clip.name : null };
        }

        static object SetSourceProps(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var src = go.GetComponent<AudioSource>();
            if (src == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No AudioSource on '" + go.name + "'.");
            Undo.RecordObject(src, "Set AudioSource");
            Apply(src, p);
            MarkDirty(go);
            return new { ok = true };
        }

        static void Apply(AudioSource src, JObject p)
        {
            if (p["volume"] != null) src.volume = p["volume"].Value<float>();
            if (p["pitch"] != null) src.pitch = p["pitch"].Value<float>();
            if (p["loop"] != null) src.loop = p["loop"].Value<bool>();
            if (p["playOnAwake"] != null) src.playOnAwake = p["playOnAwake"].Value<bool>();
            if (p["spatialBlend"] != null) src.spatialBlend = p["spatialBlend"].Value<float>();
            if (p["mute"] != null) src.mute = p["mute"].Value<bool>();
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
