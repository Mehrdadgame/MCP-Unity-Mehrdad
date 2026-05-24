using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Lights, reflection probes, and scene RenderSettings (ambient, fog, skybox).</summary>
    public class LightingHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_light": return CreateLight(p);
                case "set_light_properties": return SetLightProps(p);
                case "create_reflection_probe": return CreateProbe(p);
                case "set_ambient": return SetAmbient(p);
                case "set_fog": return SetFog(p);
                case "set_skybox": return SetSkybox(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateLight(JObject p)
        {
            string typeStr = OptString(p, "type", "Directional");
            LightType lt;
            switch (typeStr.ToLowerInvariant())
            {
                case "directional": lt = LightType.Directional; break;
                case "point": lt = LightType.Point; break;
                case "spot": lt = LightType.Spot; break;
                case "area": lt = LightType.Rectangle; break;
                default: throw Invalid("type must be Directional/Point/Spot/Area.");
            }
            var go = new GameObject(OptString(p, "name", typeStr + " Light"));
            var light = go.AddComponent<Light>();
            light.type = lt;
            if (p["color"] != null) light.color = ValueParser.ToColor(p["color"]);
            if (p["intensity"] != null) light.intensity = p["intensity"].Value<float>();
            if (p["range"] != null) light.range = p["range"].Value<float>();
            if (p["spotAngle"] != null) light.spotAngle = p["spotAngle"].Value<float>();

            Undo.RegisterCreatedObjectUndo(go, "Create Light");
            if (p["parent"] != null && p["parent"].Type != JTokenType.Null)
                Undo.SetTransformParent(go.transform, ObjectFinder.Resolve(p["parent"]).transform, "Parent");
            if (p["position"] != null) go.transform.localPosition = ValueParser.ToVector3(p["position"]);
            if (p["rotation"] != null) go.transform.localEulerAngles = ValueParser.ToVector3(p["rotation"]);

            MarkDirty(go);
            Selection.activeGameObject = go;
            return new { instanceId = go.GetInstanceID(), name = go.name, type = lt.ToString() };
        }

        static object SetLightProps(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var light = go.GetComponent<Light>();
            if (light == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Light on '" + go.name + "'.");
            Undo.RecordObject(light, "Set Light");
            if (p["color"] != null) light.color = ValueParser.ToColor(p["color"]);
            if (p["intensity"] != null) light.intensity = p["intensity"].Value<float>();
            if (p["range"] != null) light.range = p["range"].Value<float>();
            if (p["spotAngle"] != null) light.spotAngle = p["spotAngle"].Value<float>();
            if (p["shadows"] != null)
                light.shadows = (LightShadows)System.Enum.Parse(typeof(LightShadows), p["shadows"].ToString(), true);
            MarkDirty(go);
            return new { ok = true };
        }

        static object CreateProbe(JObject p)
        {
            var go = new GameObject(OptString(p, "name", "Reflection Probe"));
            go.AddComponent<ReflectionProbe>();
            Undo.RegisterCreatedObjectUndo(go, "Create Reflection Probe");
            if (p["position"] != null) go.transform.localPosition = ValueParser.ToVector3(p["position"]);
            MarkDirty(go);
            return new { instanceId = go.GetInstanceID(), name = go.name };
        }

        static object SetAmbient(JObject p)
        {
            string mode = OptString(p, "mode", null);
            if (mode != null)
            {
                switch (mode.ToLowerInvariant())
                {
                    case "skybox": RenderSettings.ambientMode = AmbientMode.Skybox; break;
                    case "trilight":
                    case "gradient": RenderSettings.ambientMode = AmbientMode.Trilight; break;
                    case "flat":
                    case "color": RenderSettings.ambientMode = AmbientMode.Flat; break;
                }
            }
            if (p["skyColor"] != null) RenderSettings.ambientSkyColor = ValueParser.ToColor(p["skyColor"]);
            if (p["equatorColor"] != null) RenderSettings.ambientEquatorColor = ValueParser.ToColor(p["equatorColor"]);
            if (p["groundColor"] != null) RenderSettings.ambientGroundColor = ValueParser.ToColor(p["groundColor"]);
            if (p["color"] != null) RenderSettings.ambientLight = ValueParser.ToColor(p["color"]);
            if (p["intensity"] != null) RenderSettings.ambientIntensity = p["intensity"].Value<float>();
            MarkActiveDirty();
            return new { ok = true, mode = RenderSettings.ambientMode.ToString() };
        }

        static object SetFog(JObject p)
        {
            if (p["enabled"] != null) RenderSettings.fog = p["enabled"].Value<bool>();
            if (p["color"] != null) RenderSettings.fogColor = ValueParser.ToColor(p["color"]);
            if (p["density"] != null) RenderSettings.fogDensity = p["density"].Value<float>();
            if (p["start"] != null) RenderSettings.fogStartDistance = p["start"].Value<float>();
            if (p["end"] != null) RenderSettings.fogEndDistance = p["end"].Value<float>();
            if (p["mode"] != null)
            {
                switch (p["mode"].ToString().ToLowerInvariant())
                {
                    case "linear": RenderSettings.fogMode = FogMode.Linear; break;
                    case "exponential": RenderSettings.fogMode = FogMode.Exponential; break;
                    case "exponentialsquared":
                    case "exp2": RenderSettings.fogMode = FogMode.ExponentialSquared; break;
                }
            }
            MarkActiveDirty();
            return new { ok = true, fog = RenderSettings.fog };
        }

        static object SetSkybox(JObject p)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RequireString(p, "materialPath"));
            if (mat == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No material at the given path.");
            RenderSettings.skybox = mat;
            MarkActiveDirty();
            return new { ok = true };
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static void MarkActiveDirty() => EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}
