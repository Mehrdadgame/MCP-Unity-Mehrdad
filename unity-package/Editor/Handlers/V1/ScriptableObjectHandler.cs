using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>ScriptableObject class generation, instance CRUD, and value editing.</summary>
    public class ScriptableObjectHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_class": return CreateClass(p);
                case "create_instance": return CreateInstance(p);
                case "set_values": return SetValues(p);
                case "get_values": return GetValues(p);
                case "list_instances": return ListInstances(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateClass(JObject p)
        {
            string cls = RequireString(p, "className");
            string ns = OptString(p, "namespace", null);
            string menuName = OptString(p, "menuName", cls);
            string fileName = OptString(p, "fileName", cls);
            string folder = OptString(p, "folder", "Assets/Scripts");
            string path = folder.TrimEnd('/') + "/" + cls + ".cs";

            var sb = new System.Text.StringBuilder();
            sb.Append("using UnityEngine;\n\n");
            string ind = "";
            if (!string.IsNullOrEmpty(ns)) { sb.Append("namespace ").Append(ns).Append("\n{\n"); ind = "    "; }
            sb.Append(ind).Append("[CreateAssetMenu(fileName = \"").Append(fileName).Append("\", menuName = \"").Append(menuName).Append("\")]\n");
            sb.Append(ind).Append("public class ").Append(cls).Append(" : ScriptableObject\n").Append(ind).Append("{\n");
            if (p["fields"] is JArray fields)
            {
                foreach (var f in fields)
                {
                    string fname = f["name"] != null ? f["name"].ToString() : null;
                    if (string.IsNullOrEmpty(fname)) continue;
                    string ftype = f["type"] != null ? f["type"].ToString() : "int";
                    string def = f["default"] != null ? " = " + FormatDefault(ftype, f["default"]) : "";
                    sb.Append(ind).Append("    public ").Append(ftype).Append(" ").Append(fname).Append(def).Append(";\n");
                }
            }
            sb.Append(ind).Append("}\n");
            if (!string.IsNullOrEmpty(ns)) sb.Append("}\n");

            AssetUtil.EnsureFolderForAsset(path);
            File.WriteAllText(AbsPath(path), sb.ToString());
            AssetDatabase.ImportAsset(path);
            return new { created = true, scriptPath = path, recompileHint = "Recompile before create_instance so the type exists." };
        }

        static object CreateInstance(JObject p)
        {
            string cls = RequireString(p, "className");
            string assetPath = RequireString(p, "assetPath");
            var type = TypeResolver.ResolveComponentType(cls);
            if (type == null) throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "Type '" + cls + "' not found (recompile after create_class?).");
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                throw new HandlerException(ErrorCodes.INVALID_PARAMS, "'" + cls + "' is not a ScriptableObject.");

            var so = ScriptableObject.CreateInstance(type);
            if (p["values"] is JObject vals) ApplyValues(so, vals);
            AssetUtil.EnsureFolderForAsset(assetPath);
            AssetDatabase.CreateAsset(so, assetPath);
            AssetDatabase.SaveAssets();
            return new { created = true, path = assetPath, guid = AssetDatabase.AssetPathToGUID(assetPath), className = type.Name };
        }

        static object SetValues(JObject p)
        {
            var so = Load(p);
            if (!(p["values"] is JObject vals)) throw Invalid("Missing 'values' object.");
            ApplyValues(so, vals);
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            return new { ok = true, count = vals.Count };
        }

        static object GetValues(JObject p)
        {
            var so = Load(p);
            var type = so.GetType();
            var dict = new Dictionary<string, object>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try { v = Describe(f.GetValue(so)); } catch { v = null; }
                dict[f.Name] = v;
            }
            return new { path = AssetDatabase.GetAssetPath(so), className = type.Name, values = dict };
        }

        static object ListInstances(JObject p)
        {
            string cls = RequireString(p, "className");
            var list = new List<object>();
            foreach (var g in AssetDatabase.FindAssets("t:" + cls))
                list.Add(new { guid = g, path = AssetDatabase.GUIDToAssetPath(g) });
            return new { count = list.Count, instances = list };
        }

        // -- helpers --

        static ScriptableObject Load(JObject p)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RequireString(p, "assetPath"));
            if (so == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No ScriptableObject at the given assetPath.");
            return so;
        }

        static void ApplyValues(ScriptableObject so, JObject vals)
        {
            var type = so.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            foreach (var kv in vals)
            {
                var f = type.GetField(kv.Key, BF);
                if (f != null) { f.SetValue(so, ValueParser.Convert(kv.Value, f.FieldType)); continue; }
                var prop = type.GetProperty(kv.Key, BF);
                if (prop != null && prop.CanWrite) { prop.SetValue(so, ValueParser.Convert(kv.Value, prop.PropertyType)); continue; }
                throw new HandlerException(ErrorCodes.PROPERTY_NOT_FOUND, "'" + type.Name + "' has no field/property '" + kv.Key + "'.");
            }
        }

        static object Describe(object v)
        {
            if (v == null) return null;
            if (v is UnityEngine.Object uo) return uo == null ? null : (object)new { name = uo.name, instanceId = uo.GetInstanceID() };
            if (v is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
            if (v is Vector2 v2) return new { x = v2.x, y = v2.y };
            if (v is Color c) return new { r = c.r, g = c.g, b = c.b, a = c.a };
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return v;
            return v.ToString();
        }

        static string FormatDefault(string type, JToken val)
        {
            switch (type.ToLowerInvariant())
            {
                case "string": return "\"" + val.ToString() + "\"";
                case "bool": return val.Value<bool>() ? "true" : "false";
                case "float": return val.Value<float>() + "f";
                default: return val.ToString();
            }
        }

        static string AbsPath(string assetPath)
        {
            string data = Application.dataPath;
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
                return data.Substring(0, data.Length - "Assets".Length) + assetPath;
            return assetPath;
        }
    }
}
