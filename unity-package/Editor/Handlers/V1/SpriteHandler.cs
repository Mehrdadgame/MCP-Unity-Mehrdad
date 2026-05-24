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
    /// <summary>2D SpriteRenderer authoring + sprite import (slice / pivot / PPU).</summary>
    public class SpriteHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "set_sprite": return SetSprite(p);
                case "set_sorting": return SetSorting(p);
                case "set_flip": return SetFlip(p);
                case "set_color": return SetColor(p);
                case "set_pixels_per_unit": return SetPPU(p);
                case "set_pivot": return SetPivot(p);
                case "slice_texture": return Slice(p);
                default: throw UnknownAction(action);
            }
        }

        static SpriteRenderer GetOrAdd(GameObject go)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = Undo.AddComponent<SpriteRenderer>(go);
            return sr;
        }

        static object SetSprite(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var sr = GetOrAdd(go);
            var sprite = LoadSprite(RequireString(p, "spritePath"), OptString(p, "subSprite", null));
            if (sprite == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No sprite found at the given path.");
            Undo.RecordObject(sr, "Set Sprite");
            sr.sprite = sprite;
            MarkDirty(go);
            return new { ok = true, sprite = sprite.name };
        }

        static object SetSorting(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var sr = GetOrAdd(go);
            Undo.RecordObject(sr, "Set Sorting");
            if (p["layer"] != null) sr.sortingLayerName = p["layer"].ToString();
            if (p["order"] != null) sr.sortingOrder = p["order"].Value<int>();
            MarkDirty(go);
            return new { ok = true, sortingLayer = sr.sortingLayerName, order = sr.sortingOrder };
        }

        static object SetFlip(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var sr = GetOrAdd(go);
            Undo.RecordObject(sr, "Set Flip");
            if (p["x"] != null) sr.flipX = p["x"].Value<bool>();
            if (p["y"] != null) sr.flipY = p["y"].Value<bool>();
            MarkDirty(go);
            return new { ok = true, flipX = sr.flipX, flipY = sr.flipY };
        }

        static object SetColor(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var sr = GetOrAdd(go);
            Undo.RecordObject(sr, "Set Sprite Color");
            sr.color = ValueParser.ToColor(p["color"]);
            MarkDirty(go);
            return new { ok = true };
        }

        static object SetPPU(JObject p)
        {
            var imp = Importer(RequireString(p, "texturePath"));
            imp.spritePixelsPerUnit = p["ppu"].Value<float>();
            imp.SaveAndReimport();
            return new { ok = true, ppu = imp.spritePixelsPerUnit };
        }

        static object SetPivot(JObject p)
        {
            var imp = Importer(RequireString(p, "texturePath"));
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = ValueParser.ToVector2(p["pivot"]);
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return new { ok = true };
        }

        static object Slice(JObject p)
        {
            var path = RequireString(p, "texturePath");
            var imp = Importer(path);
            imp.spriteImportMode = SpriteImportMode.Multiple;
            var grid = ValueParser.ToVector2(p["gridSize"]);
            int gw = Mathf.Max(1, (int)grid.x), gh = Mathf.Max(1, (int)grid.y);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Texture not loadable at '" + path + "'.");
            int cols = Mathf.Max(1, tex.width / gw), rows = Mathf.Max(1, tex.height / gh);
            var metas = new List<SpriteMetaData>();
            int idx = 0;
            for (int y = rows - 1; y >= 0; y--)
                for (int x = 0; x < cols; x++)
                {
                    metas.Add(new SpriteMetaData
                    {
                        rect = new Rect(x * gw, y * gh, gw, gh),
                        name = tex.name + "_" + idx++,
                        alignment = (int)SpriteAlignment.Center,
                    });
                }
#pragma warning disable 618
            imp.spritesheet = metas.ToArray();
#pragma warning restore 618
            imp.SaveAndReimport();
            return new { ok = true, sliced = metas.Count, columns = cols, rows = rows };
        }

        static TextureImporter Importer(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No texture importer at '" + path + "'.");
            return imp;
        }

        static Sprite LoadSprite(string path, string sub)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("builtin:")) return AssetDatabase.GetBuiltinExtraResource<Sprite>(path.Substring(8));
            if (!string.IsNullOrEmpty(sub))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite s && s.name == sub) return s;
            var single = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (single != null) return single;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Sprite sp) return sp;
            return null;
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
