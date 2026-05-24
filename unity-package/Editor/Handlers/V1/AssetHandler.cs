using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>AssetDatabase operations: search, info, folders, move/rename/copy/delete, deps.</summary>
    public class AssetHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "find":
                case "find_assets": return Find(p);
                case "get_info": return GetInfo(p);
                case "create_folder": return CreateFolder(p);
                case "move": return Move(p);
                case "rename": return Rename(p);
                case "copy": return Copy(p);
                case "delete": return Delete(p);
                case "refresh": AssetDatabase.Refresh(); return new { refreshed = true };
                case "save": AssetDatabase.SaveAssets(); return new { saved = true };
                case "reimport": return Reimport(p);
                case "get_dependencies": return GetDependencies(p);
                case "guid_to_path": return new { path = AssetDatabase.GUIDToAssetPath(RequireString(p, "guid")) };
                case "path_to_guid": return new { guid = AssetDatabase.AssetPathToGUID(RequireString(p, "path")) };
                case "exists": return new { exists = Exists(RequireString(p, "path")) };
                default: throw UnknownAction(action);
            }
        }

        static bool Exists(string path) => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));

        static object Find(JObject p)
        {
            string filter = OptString(p, "filter", "");
            int max = OptInt(p, "maxResults", 200);
            var folders = ToStringArray(p["folders"]);
            string[] guids = (folders != null && folders.Length > 0)
                ? AssetDatabase.FindAssets(filter, folders)
                : AssetDatabase.FindAssets(filter);

            var list = new List<object>();
            int n = Mathf.Min(guids.Length, max);
            for (int i = 0; i < n; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                list.Add(new { guid = guids[i], path = path, type = type != null ? type.Name : null });
            }
            return new { count = guids.Length, shown = list.Count, results = list };
        }

        static object GetInfo(JObject p)
        {
            string path = RequireString(p, "path");
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No asset at '" + path + "'.");
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            return new
            {
                path = path,
                guid = guid,
                type = type != null ? type.Name : null,
                fullType = type != null ? type.FullName : null,
                name = obj != null ? obj.name : null,
                isFolder = AssetDatabase.IsValidFolder(path),
            };
        }

        static object CreateFolder(JObject p)
        {
            string path = RequireString(p, "path");
            AssetUtil.EnsureFolder(path);
            return new { created = true, path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object Move(JObject p)
        {
            string from = RequireString(p, "from");
            string to = RequireString(p, "to");
            AssetUtil.EnsureFolderForAsset(to);
            string err = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(err)) throw new HandlerException(ErrorCodes.IO_ERROR, err);
            return new { moved = true, from = from, to = to };
        }

        static object Rename(JObject p)
        {
            string path = RequireString(p, "path");
            string newName = RequireString(p, "newName");
            string err = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(err)) throw new HandlerException(ErrorCodes.IO_ERROR, err);
            return new { renamed = true, path = path, newName = newName };
        }

        static object Copy(JObject p)
        {
            string from = RequireString(p, "from");
            string to = RequireString(p, "to");
            AssetUtil.EnsureFolderForAsset(to);
            if (!AssetDatabase.CopyAsset(from, to))
                throw new HandlerException(ErrorCodes.IO_ERROR, "Failed to copy '" + from + "' to '" + to + "'.");
            return new { copied = true, from = from, to = to };
        }

        static object Delete(JObject p)
        {
            string path = RequireString(p, "path");
            if (!Exists(path)) throw new HandlerException(ErrorCodes.NOT_FOUND, "No asset at '" + path + "'.");
            if (!OptBool(p, "confirm", false))
                throw new HandlerException(ErrorCodes.CONFIRMATION_REQUIRED,
                    "Deleting '" + path + "' is not undoable. Pass confirm=true to proceed.");
            bool ok = AssetDatabase.DeleteAsset(path);
            return new { deleted = ok, path = path };
        }

        static object Reimport(JObject p)
        {
            string path = RequireString(p, "path");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return new { reimported = true, path = path };
        }

        static object GetDependencies(JObject p)
        {
            string path = RequireString(p, "path");
            bool recursive = OptBool(p, "recursive", true);
            var deps = AssetDatabase.GetDependencies(path, recursive);
            return new { count = deps.Length, dependencies = deps };
        }

        static string[] ToStringArray(JToken t)
        {
            if (t == null || t.Type != JTokenType.Array) return null;
            var list = new List<string>();
            foreach (var x in (JArray)t) list.Add(x.ToString());
            return list.ToArray();
        }
    }
}
