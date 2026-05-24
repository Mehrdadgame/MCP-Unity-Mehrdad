using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Protocol;

namespace UnityMCP.Utils
{
    /// <summary>
    /// Converts JSON tokens into Unity value types. Vectors/colors accept either a
    /// JSON array ([x,y,z]) or object ({x:..}); colors also accept #hex or color names.
    /// </summary>
    public static class ValueParser
    {
        public static Vector3 ToVector3(JToken t, Vector3 fallback = default)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Array)
            {
                var a = (JArray)t;
                return new Vector3(El(a, 0), El(a, 1), El(a, 2));
            }
            if (t.Type == JTokenType.Object)
            {
                var o = (JObject)t;
                return new Vector3(F(o, "x"), F(o, "y"), F(o, "z"));
            }
            throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Expected a Vector3 as [x,y,z] or {x,y,z}.");
        }

        public static Vector2 ToVector2(JToken t, Vector2 fallback = default)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Array) { var a = (JArray)t; return new Vector2(El(a, 0), El(a, 1)); }
            if (t.Type == JTokenType.Object) { var o = (JObject)t; return new Vector2(F(o, "x"), F(o, "y")); }
            throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Expected a Vector2.");
        }

        public static Vector4 ToVector4(JToken t, Vector4 fallback = default)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Array) { var a = (JArray)t; return new Vector4(El(a, 0), El(a, 1), El(a, 2), El(a, 3)); }
            if (t.Type == JTokenType.Object) { var o = (JObject)t; return new Vector4(F(o, "x"), F(o, "y"), F(o, "z"), F(o, "w")); }
            throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Expected a Vector4.");
        }

        public static Color ToColor(JToken t, Color fallback = default)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Array)
            {
                var a = (JArray)t;
                return new Color(El(a, 0), El(a, 1), El(a, 2), a.Count > 3 ? El(a, 3) : 1f);
            }
            if (t.Type == JTokenType.Object)
            {
                var o = (JObject)t;
                return new Color(F(o, "r"), F(o, "g"), F(o, "b"), o["a"] != null ? F(o, "a") : 1f);
            }
            if (t.Type == JTokenType.String && ColorUtility.TryParseHtmlString(t.ToString(), out var c)) return c;
            throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Expected a Color as #hex, a color name, or [r,g,b,a].");
        }

        /// <summary>Convert a token to an arbitrary target type for reflection-based setters.</summary>
        public static object Convert(JToken t, Type target)
        {
            if (t == null) return null;
            if (target == typeof(string)) return t.Type == JTokenType.Null ? null : t.ToString();
            if (target == typeof(bool)) return t.Value<bool>();
            if (target == typeof(int)) return t.Value<int>();
            if (target == typeof(long)) return t.Value<long>();
            if (target == typeof(float)) return t.Value<float>();
            if (target == typeof(double)) return t.Value<double>();
            if (target.IsEnum)
                return t.Type == JTokenType.Integer ? Enum.ToObject(target, t.Value<int>()) : Enum.Parse(target, t.ToString(), true);
            if (target == typeof(Vector2)) return ToVector2(t);
            if (target == typeof(Vector3)) return ToVector3(t);
            if (target == typeof(Vector4)) return ToVector4(t);
            if (target == typeof(Quaternion)) return Quaternion.Euler(ToVector3(t));
            if (target == typeof(Color)) return ToColor(t);
            if (typeof(UnityEngine.Object).IsAssignableFrom(target)) return ResolveObject(t, target);
            try { return t.ToObject(target); }
            catch { throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Cannot convert value to " + target.Name + "."); }
        }

        static UnityEngine.Object ResolveObject(JToken t, Type target)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Integer) return EditorUtility.InstanceIDToObject(t.Value<int>());
            string s = t.ToString();
            if (string.IsNullOrEmpty(s)) return null;
            var asset = AssetDatabase.LoadAssetAtPath(s, target);
            return asset;
        }

        static float El(JArray a, int i) => i < a.Count ? a[i].Value<float>() : 0f;
        static float F(JObject o, string k) { var v = o[k]; return (v == null || v.Type == JTokenType.Null) ? 0f : v.Value<float>(); }
    }
}
