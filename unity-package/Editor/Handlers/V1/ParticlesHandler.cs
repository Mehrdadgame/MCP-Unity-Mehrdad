using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>ParticleSystem authoring: create (with presets), main/emission edits, material, play/stop.</summary>
    public class ParticlesHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create": return Create(p);
                case "set_main": return SetMain(p);
                case "set_emission": return SetEmission(p);
                case "set_material": return SetMaterial(p);
                case "play": return PlayStop(p, true);
                case "stop": return PlayStop(p, false);
                default: throw UnknownAction(action);
            }
        }

        static ParticleSystem Get(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No ParticleSystem on '" + go.name + "'.");
            return ps;
        }

        static object Create(JObject p)
        {
            var go = new GameObject(OptString(p, "name", "Particle System"));
            var ps = go.AddComponent<ParticleSystem>();
            Undo.RegisterCreatedObjectUndo(go, "Create Particle System");
            if (p["parent"] != null && p["parent"].Type != JTokenType.Null)
                Undo.SetTransformParent(go.transform, ObjectFinder.Resolve(p["parent"]).transform, "Parent");
            if (p["position"] != null) go.transform.localPosition = ValueParser.ToVector3(p["position"]);

            ApplyPreset(ps, OptString(p, "preset", "Default"));
            MarkDirty(go);
            Selection.activeGameObject = go;
            return new { instanceId = go.GetInstanceID(), name = go.name, preset = OptString(p, "preset", "Default") };
        }

        static void ApplyPreset(ParticleSystem ps, string preset)
        {
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            switch ((preset ?? "Default").ToLowerInvariant())
            {
                case "fire":
                    main.startColor = new Color(1f, 0.5f, 0.1f, 1f);
                    main.startLifetime = 1.5f; main.startSpeed = 2f; main.startSize = 0.5f;
                    shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15f;
                    emission.rateOverTime = 50f;
                    break;
                case "smoke":
                    main.startColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    main.startLifetime = 3f; main.startSpeed = 1f; main.startSize = 1f;
                    shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 10f;
                    emission.rateOverTime = 20f;
                    break;
                case "explosion":
                    main.startColor = new Color(1f, 0.6f, 0.1f, 1f);
                    main.startLifetime = 1f; main.startSpeed = 8f; main.startSize = 0.6f;
                    main.loop = false;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    emission.rateOverTime = 200f;
                    break;
                default:
                    break;
            }
        }

        static object SetMain(JObject p)
        {
            var ps = Get(p);
            var main = ps.main;
            if (p["startColor"] != null) main.startColor = ValueParser.ToColor(p["startColor"]);
            if (p["startLifetime"] != null) main.startLifetime = p["startLifetime"].Value<float>();
            if (p["startSpeed"] != null) main.startSpeed = p["startSpeed"].Value<float>();
            if (p["startSize"] != null) main.startSize = p["startSize"].Value<float>();
            if (p["gravityModifier"] != null) main.gravityModifier = p["gravityModifier"].Value<float>();
            if (p["maxParticles"] != null) main.maxParticles = p["maxParticles"].Value<int>();
            if (p["loop"] != null) main.loop = p["loop"].Value<bool>();
            MarkDirty(ps.gameObject);
            return new { ok = true };
        }

        static object SetEmission(JObject p)
        {
            var ps = Get(p);
            var emission = ps.emission;
            if (p["enabled"] != null) emission.enabled = p["enabled"].Value<bool>();
            if (p["rateOverTime"] != null) emission.rateOverTime = p["rateOverTime"].Value<float>();
            if (p["rateOverDistance"] != null) emission.rateOverDistance = p["rateOverDistance"].Value<float>();
            MarkDirty(ps.gameObject);
            return new { ok = true };
        }

        static object SetMaterial(JObject p)
        {
            var ps = Get(p);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RequireString(p, "materialPath"));
            if (mat == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No material at the given path.");
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Undo.RecordObject(renderer, "Set Particle Material");
            renderer.sharedMaterial = mat;
            MarkDirty(ps.gameObject);
            return new { ok = true };
        }

        static object PlayStop(JObject p, bool play)
        {
            var ps = Get(p);
            bool withChildren = OptBool(p, "withChildren", true);
            if (play) ps.Play(withChildren); else ps.Stop(withChildren);
            return new { ok = true, playing = ps.isPlaying };
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
