using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>2D Tilemap authoring: grids, tilemaps, tile assets, and painting.</summary>
    public class TilemapHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_grid": return CreateGrid(p);
                case "create_tilemap": return CreateTilemap(p);
                case "create_tile_asset": return CreateTileAsset(p);
                case "paint_tile": return PaintTile(p);
                case "paint_box": return PaintBox(p);
                case "erase_tile": return EraseTile(p);
                case "get_tile": return GetTile(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateGrid(JObject p)
        {
            var go = new GameObject(OptString(p, "name", "Grid"));
            var grid = go.AddComponent<Grid>();
            if (p["cellSize"] != null) grid.cellSize = ValueParser.ToVector3(p["cellSize"], Vector3.one);
            Undo.RegisterCreatedObjectUndo(go, "Create Grid");
            MarkDirty(go);
            Selection.activeGameObject = go;
            return new { instanceId = go.GetInstanceID(), name = go.name };
        }

        static object CreateTilemap(JObject p)
        {
            var gridGo = ObjectFinder.Resolve(p["gridTarget"]);
            if (gridGo.GetComponent<Grid>() == null)
                throw new HandlerException(ErrorCodes.INVALID_PARAMS, "gridTarget has no Grid component.");
            var go = new GameObject(OptString(p, "name", "Tilemap"));
            Undo.RegisterCreatedObjectUndo(go, "Create Tilemap");
            go.transform.SetParent(gridGo.transform, false);
            go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            if (OptBool(p, "collider", false)) go.AddComponent<TilemapCollider2D>();
            MarkDirty(go);
            Selection.activeGameObject = go;
            return new { instanceId = go.GetInstanceID(), name = go.name };
        }

        static object CreateTileAsset(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".asset")) path += ".asset";
            var sprite = LoadSprite(RequireString(p, "spritePath"));
            if (sprite == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No sprite at the given spritePath.");
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = ParseCollider(OptString(p, "colliderType", "Sprite"));
            AssetUtil.EnsureFolderForAsset(path);
            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object PaintTile(JObject p)
        {
            var tm = GetTilemap(p);
            var pos = Cell(p["position"]);
            tm.SetTile(pos, LoadTile(RequireString(p, "tilePath")));
            MarkDirty(tm.gameObject);
            return new { ok = true, position = new { x = pos.x, y = pos.y } };
        }

        static object PaintBox(JObject p)
        {
            var tm = GetTilemap(p);
            var tile = LoadTile(RequireString(p, "tilePath"));
            var from = Cell(p["from"]); var to = Cell(p["to"]);
            int x0 = Mathf.Min(from.x, to.x), x1 = Mathf.Max(from.x, to.x);
            int y0 = Mathf.Min(from.y, to.y), y1 = Mathf.Max(from.y, to.y);
            int count = 0;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++) { tm.SetTile(new Vector3Int(x, y, 0), tile); count++; }
            MarkDirty(tm.gameObject);
            return new { ok = true, count = count };
        }

        static object EraseTile(JObject p)
        {
            var tm = GetTilemap(p);
            tm.SetTile(Cell(p["position"]), null);
            MarkDirty(tm.gameObject);
            return new { ok = true };
        }

        static object GetTile(JObject p)
        {
            var tm = GetTilemap(p);
            var t = tm.GetTile(Cell(p["position"]));
            return new { hasTile = t != null, tile = t != null ? AssetDatabase.GetAssetPath(t) : null };
        }

        // -- helpers --

        static Tilemap GetTilemap(JObject p)
        {
            var go = ObjectFinder.Resolve(p["tilemapTarget"]);
            var tm = go.GetComponent<Tilemap>();
            if (tm == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Tilemap on '" + go.name + "'.");
            return tm;
        }

        static TileBase LoadTile(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<TileBase>(path);
            if (t == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No tile asset at '" + path + "'.");
            return t;
        }

        static Vector3Int Cell(JToken t)
        {
            var v = ValueParser.ToVector3(t);
            return new Vector3Int((int)v.x, (int)v.y, (int)v.z);
        }

        static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("builtin:")) return AssetDatabase.GetBuiltinExtraResource<Sprite>(path.Substring(8));
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s != null) return s;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Sprite sp) return sp;
            return null;
        }

        static Tile.ColliderType ParseCollider(string s)
        {
            switch ((s ?? "Sprite").ToLowerInvariant())
            {
                case "none": return Tile.ColliderType.None;
                case "grid": return Tile.ColliderType.Grid;
                default: return Tile.ColliderType.Sprite;
            }
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
