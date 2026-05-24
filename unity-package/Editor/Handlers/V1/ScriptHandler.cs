using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityMCP.Core;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>C# script create/read/delete plus compile control (recompile + result).</summary>
    public class ScriptHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "recompile": return Recompile(p);
                case "get_compile_result": return CompileWatcher.GetResult();
                case "create": return Create(p);
                case "read": return Read(p);
                case "delete": return Delete(p);
                case "exists": return new { exists = File.Exists(AbsPath(RequireString(p, "path"))) };
                default: throw UnknownAction(action);
            }
        }

        static object Create(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".cs")) path += ".cs";
            string abs = AbsPath(path);
            if (File.Exists(abs) && !OptBool(p, "overwrite", false))
                throw new HandlerException(ErrorCodes.IO_ERROR, "'" + path + "' already exists. Pass overwrite=true to replace.");

            string content = OptString(p, "content", null);
            if (content == null) content = Template(p);

            AssetUtil.EnsureFolderForAsset(path);
            File.WriteAllText(abs, content);
            AssetDatabase.ImportAsset(path);
            return new
            {
                created = true,
                path = path,
                length = content.Length,
                recompileHint = "Call script.recompile (or unity_recompile_and_wait) to compile before using the new type.",
            };
        }

        static object Read(JObject p)
        {
            string path = RequireString(p, "path");
            string abs = AbsPath(path);
            if (!File.Exists(abs)) throw new HandlerException(ErrorCodes.NOT_FOUND, "No file at '" + path + "'.");
            return new { path = path, content = File.ReadAllText(abs) };
        }

        static object Delete(JObject p)
        {
            string path = RequireString(p, "path");
            if (!OptBool(p, "confirm", false))
                throw new HandlerException(ErrorCodes.CONFIRMATION_REQUIRED, "Pass confirm=true to delete '" + path + "'.");
            return new { deleted = AssetDatabase.DeleteAsset(path), path = path };
        }

        static string Template(JObject p)
        {
            string cls = OptString(p, "className", Path.GetFileNameWithoutExtension(RequireString(p, "path")));
            string ns = OptString(p, "namespace", null);
            string baseClass = OptString(p, "baseClass", "MonoBehaviour");

            var sb = new System.Text.StringBuilder();
            sb.Append("using UnityEngine;\n\n");
            string ind = "";
            if (!string.IsNullOrEmpty(ns)) { sb.Append("namespace ").Append(ns).Append("\n{\n"); ind = "    "; }
            sb.Append(ind).Append("public class ").Append(cls).Append(" : ").Append(baseClass).Append("\n").Append(ind).Append("{\n");
            sb.Append(ind).Append("}\n");
            if (!string.IsNullOrEmpty(ns)) sb.Append("}\n");
            return sb.ToString();
        }

        static object Recompile(JObject p)
        {
            // Default to incremental (AssetDatabase.Refresh) — only changed assemblies
            // recompile, which is far faster than a full RequestScriptCompilation. Pass
            // force=true for a full rebuild.
            bool force = OptBool(p, "force", false);
            long before = CompileWatcher.LastFinishedTicks;
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                if (force) CompilationPipeline.RequestScriptCompilation();
            };
            return new { requested = true, force = force, previousFinishedAtTicks = before };
        }

        static string AbsPath(string assetPath)
        {
            // Application.dataPath ends with "/Assets"; map "Assets/.." and "Packages/.." to disk.
            string data = Application.dataPath;
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
                return data.Substring(0, data.Length - "Assets".Length) + assetPath;
            return assetPath;
        }
    }
}
