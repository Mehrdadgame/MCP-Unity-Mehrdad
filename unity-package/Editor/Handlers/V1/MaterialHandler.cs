using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Material + shader authoring with render-pipeline awareness (Built-in / URP / HDRP).</summary>
    public class MaterialHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "detect_pipeline": return DetectPipelineInfo();
                case "create": return Create(p);
                case "clone": return Clone(p);
                case "get_info": return GetInfo(p);
                case "list_properties": return ListProperties(p);
                case "set_color": return SetColor(p);
                case "set_float": return SetFloat(p);
                case "set_int": return SetInt(p);
                case "set_vector": return SetVector(p);
                case "set_texture": return SetTexture(p);
                case "set_keyword": return SetKeyword(p);
                case "set_render_queue": return SetRenderQueue(p);
                case "assign_to_renderer": return AssignToRenderer(p);
                case "assign_to_ui_image": return AssignToUiImage(p);
                case "swap_on_all_renderers": return SwapOnAllRenderers(p);
                default: throw UnknownAction(action);
            }
        }

        // -- render pipeline --

        static string DetectPipeline()
        {
            var rp = GraphicsSettings.defaultRenderPipeline;
            if (rp == null) rp = QualitySettings.renderPipeline;
            if (rp == null) return "BuiltIn";
            string n = rp.GetType().FullName ?? "";
            if (n.Contains("Universal")) return "URP";
            if (n.Contains("HighDefinition")) return "HDRP";
            return "Custom";
        }

        static string DefaultShader(string pipeline)
        {
            switch (pipeline)
            {
                case "URP": return "Universal Render Pipeline/Lit";
                case "HDRP": return "HDRP/Lit";
                default: return "Standard";
            }
        }

        static object DetectPipelineInfo()
        {
            string pipe = DetectPipeline();
            var rp = GraphicsSettings.defaultRenderPipeline;
            return new { pipeline = pipe, defaultShader = DefaultShader(pipe), renderPipelineAsset = rp != null ? rp.name : null };
        }

        // -- create / clone --

        static object Create(JObject p)
        {
            string path = RequireString(p, "path");
            string pipeline = OptString(p, "renderPipeline", null);
            if (string.IsNullOrEmpty(pipeline)) pipeline = DetectPipeline();
            string shaderName = OptString(p, "shader", null);
            if (string.IsNullOrEmpty(shaderName)) shaderName = DefaultShader(pipeline);

            var shader = Shader.Find(shaderName);
            if (shader == null) throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "Shader '" + shaderName + "' not found.");

            var mat = new Material(shader);
            AssetUtil.EnsureFolderForAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path), shader = shader.name, pipeline = pipeline };
        }

        static object Clone(JObject p)
        {
            string src = RequireString(p, "sourcePath");
            string dst = RequireString(p, "newPath");
            var srcMat = AssetDatabase.LoadAssetAtPath<Material>(src);
            if (srcMat == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No material at '" + src + "'.");
            var clone = new Material(srcMat);
            AssetUtil.EnsureFolderForAsset(dst);
            AssetDatabase.CreateAsset(clone, dst);
            AssetDatabase.SaveAssets();
            return new { path = dst, guid = AssetDatabase.AssetPathToGUID(dst), shader = clone.shader.name };
        }

        // -- setters --

        static object SetColor(JObject p)
        {
            var mat = LoadMat(p);
            string prop = OptString(p, "property", null);
            if (string.IsNullOrEmpty(prop)) prop = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            RequireProp(mat, prop);
            if (p["color"] == null) throw Invalid("Missing 'color'.");
            Undo.RecordObject(mat, "Set Material Color");
            mat.SetColor(prop, ValueParser.ToColor(p["color"]));
            Save(mat);
            return new { ok = true, property = prop };
        }

        static object SetFloat(JObject p)
        {
            var mat = LoadMat(p);
            string prop = RequireString(p, "property");
            RequireProp(mat, prop);
            if (p["value"] == null) throw Invalid("Missing 'value'.");
            Undo.RecordObject(mat, "Set Material Float");
            mat.SetFloat(prop, p["value"].Value<float>());
            Save(mat);
            return new { ok = true, property = prop };
        }

        static object SetInt(JObject p)
        {
            var mat = LoadMat(p);
            string prop = RequireString(p, "property");
            RequireProp(mat, prop);
            Undo.RecordObject(mat, "Set Material Int");
            mat.SetFloat(prop, RequireInt(p, "value"));
            Save(mat);
            return new { ok = true, property = prop };
        }

        static object SetVector(JObject p)
        {
            var mat = LoadMat(p);
            string prop = RequireString(p, "property");
            RequireProp(mat, prop);
            if (p["value"] == null) throw Invalid("Missing 'value'.");
            Undo.RecordObject(mat, "Set Material Vector");
            mat.SetVector(prop, ValueParser.ToVector4(p["value"]));
            Save(mat);
            return new { ok = true, property = prop };
        }

        static object SetTexture(JObject p)
        {
            var mat = LoadMat(p);
            string prop = OptString(p, "property", null);
            if (string.IsNullOrEmpty(prop)) prop = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            RequireProp(mat, prop);
            string texPath = RequireString(p, "texturePath");
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No texture at '" + texPath + "'.");
            Undo.RecordObject(mat, "Set Material Texture");
            mat.SetTexture(prop, tex);
            if (p["tiling"] != null) mat.SetTextureScale(prop, ValueParser.ToVector2(p["tiling"]));
            if (p["offset"] != null) mat.SetTextureOffset(prop, ValueParser.ToVector2(p["offset"]));
            Save(mat);
            return new { ok = true, property = prop, texture = tex.name };
        }

        static object SetKeyword(JObject p)
        {
            var mat = LoadMat(p);
            string kw = RequireString(p, "keyword");
            bool en = OptBool(p, "enabled", true);
            Undo.RecordObject(mat, "Set Material Keyword");
            if (en) mat.EnableKeyword(kw); else mat.DisableKeyword(kw);
            Save(mat);
            return new { ok = true, keyword = kw, enabled = en };
        }

        static object SetRenderQueue(JObject p)
        {
            var mat = LoadMat(p);
            Undo.RecordObject(mat, "Set Render Queue");
            mat.renderQueue = RequireInt(p, "queue");
            Save(mat);
            return new { ok = true, renderQueue = mat.renderQueue };
        }

        // -- info --

        static object GetInfo(JObject p)
        {
            var mat = LoadMat(p);
            return new
            {
                path = AssetDatabase.GetAssetPath(mat),
                shader = mat.shader != null ? mat.shader.name : null,
                renderQueue = mat.renderQueue,
                keywords = mat.shaderKeywords,
            };
        }

        static object ListProperties(JObject p)
        {
            var mat = LoadMat(p);
            var shader = mat.shader;
            var list = new List<object>();
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                list.Add(new
                {
                    name = ShaderUtil.GetPropertyName(shader, i),
                    type = ShaderUtil.GetPropertyType(shader, i).ToString(),
                    description = ShaderUtil.GetPropertyDescription(shader, i),
                });
            }
            return new { shader = shader.name, count = list.Count, properties = list };
        }

        // -- assignment --

        static object AssignToRenderer(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var mat = LoadMat(p, "materialPath");

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Undo.RecordObject(sr, "Assign Material");
                sr.sharedMaterial = mat;
            }
            else
            {
                var r = go.GetComponent<Renderer>();
                if (r == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Renderer/SpriteRenderer on '" + go.name + "'.");
                Undo.RecordObject(r, "Assign Material");
                var mats = r.sharedMaterials;
                int slot = OptInt(p, "slot", 0);
                if (slot < 0 || slot >= mats.Length)
                {
                    if (p["slot"] != null)
                        throw Invalid("slot " + slot + " out of range (0.." + (mats.Length - 1) + ").");
                    slot = 0;
                }
                mats[slot] = mat;
                r.sharedMaterials = mats;
            }
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            return new { ok = true, target = go.name, material = mat.name };
        }

        static object AssignToUiImage(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var g = go.GetComponent<UnityEngine.UI.Graphic>();
            if (g == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No UI Graphic (Image/Text) on '" + go.name + "'.");
            var mat = LoadMat(p, "materialPath");
            Undo.RecordObject(g, "Assign UI Material");
            g.material = mat;
            EditorUtility.SetDirty(go);
            return new { ok = true };
        }

        static object SwapOnAllRenderers(JObject p)
        {
            var fromMat = LoadMat(p, "from");
            var toMat = LoadMat(p, "to");
            bool includeInactive = OptBool(p, "includeInactive", true);

            List<Renderer> renderers = new List<Renderer>();
            if (p["target"] != null && p["target"].Type != JTokenType.Null)
            {
                renderers.AddRange(ObjectFinder.Resolve(p["target"]).GetComponentsInChildren<Renderer>(includeInactive));
            }
            else
            {
                foreach (var go in ObjectFinder.AllSceneObjects(includeInactive))
                {
                    var r = go.GetComponent<Renderer>();
                    if (r != null) renderers.Add(r);
                }
            }

            int swapped = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == fromMat) { mats[i] = toMat; changed = true; }
                if (changed) { Undo.RecordObject(r, "Swap Material"); r.sharedMaterials = mats; swapped++; }
            }
            return new { ok = true, swappedCount = swapped };
        }

        // -- helpers --

        static Material LoadMat(JObject p, string key = "path")
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RequireString(p, key));
            if (mat == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No material at the given '" + key + "'.");
            return mat;
        }

        static void RequireProp(Material mat, string prop)
        {
            if (!mat.HasProperty(prop))
                throw new HandlerException(ErrorCodes.PROPERTY_NOT_FOUND,
                    "Shader '" + mat.shader.name + "' has no property '" + prop + "'. Use material.list_properties to see what's available.");
        }

        static void Save(Material mat)
        {
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
    }
}
