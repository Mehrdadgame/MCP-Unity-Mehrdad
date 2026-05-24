using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Add/remove components and read/write their members (reflection + SerializedObject).</summary>
    public class ComponentHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "add": return Add(p);
                case "remove": return Remove(p);
                case "list":
                case "get_all": return List(p);
                case "get_properties":
                case "get": return GetProperties(p);
                case "set_property": return SetProperty(p);
                case "set_properties": return SetProperties(p);
                default: throw UnknownAction(action);
            }
        }

        static GameObject Target(JObject p) => ObjectFinder.Resolve(p["target"]);

        static System.Type ResolveType(JObject p)
        {
            string typeName = RequireString(p, "type");
            var type = TypeResolver.ResolveComponentType(typeName);
            if (type == null) throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "Type '" + typeName + "' not found.");
            if (!typeof(Component).IsAssignableFrom(type))
                throw new HandlerException(ErrorCodes.INVALID_PARAMS, "'" + typeName + "' is not a Component.");
            return type;
        }

        static Component GetComp(GameObject go, System.Type type)
        {
            var c = go.GetComponent(type);
            if (c == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Component '" + type.Name + "' not found on '" + go.name + "'.");
            return c;
        }

        static object Add(JObject p)
        {
            var go = Target(p);
            var type = ResolveType(p);
            var comp = Undo.AddComponent(go, type);
            if (comp == null) throw new HandlerException(ErrorCodes.EXCEPTION, "Failed to add '" + type.Name + "'.");
            if (p["values"] is JObject vals) ApplyValues(comp, vals);
            EditorUtility.SetDirty(go);
            return new { added = true, type = type.Name, instanceId = comp.GetInstanceID() };
        }

        static object Remove(JObject p)
        {
            var go = Target(p);
            var comp = GetComp(go, ResolveType(p));
            string n = comp.GetType().Name;
            Undo.DestroyObjectImmediate(comp);
            EditorUtility.SetDirty(go);
            return new { removed = true, type = n };
        }

        static object List(JObject p)
        {
            var go = Target(p);
            var list = new List<object>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) { list.Add(new { type = "(missing script)", fullType = (string)null, instanceId = 0 }); continue; }
                var ty = c.GetType();
                list.Add(new { type = ty.Name, fullType = ty.FullName, instanceId = c.GetInstanceID() });
            }
            return new { count = list.Count, components = list };
        }

        static object SetProperty(JObject p)
        {
            var go = Target(p);
            var comp = GetComp(go, ResolveType(p));
            string prop = RequireString(p, "property");
            Undo.RecordObject(comp, "Set " + prop);
            SetMember(comp, prop, p["value"]);
            EditorUtility.SetDirty(go);
            return new { ok = true, type = comp.GetType().Name, property = prop };
        }

        static object SetProperties(JObject p)
        {
            var go = Target(p);
            var comp = GetComp(go, ResolveType(p));
            if (!(p["values"] is JObject vals)) throw new HandlerException(ErrorCodes.INVALID_PARAMS, "Missing 'values' object.");
            ApplyValues(comp, vals);
            EditorUtility.SetDirty(go);
            return new { ok = true, type = comp.GetType().Name, count = vals.Count };
        }

        static void ApplyValues(Component comp, JObject vals)
        {
            Undo.RecordObject(comp, "Set " + comp.GetType().Name + " values");
            foreach (var kv in vals) SetMember(comp, kv.Key, kv.Value);
        }

        static void SetMember(Component comp, string member, JToken value)
        {
            var type = comp.GetType();
            const BindingFlags BF = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            var prop = type.GetProperty(member, BF);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(comp, ValueParser.Convert(value, prop.PropertyType));
                return;
            }
            var field = type.GetField(member, BF);
            if (field != null)
            {
                field.SetValue(comp, ValueParser.Convert(value, field.FieldType));
                return;
            }
            throw new HandlerException(ErrorCodes.PROPERTY_NOT_FOUND,
                "'" + type.Name + "' has no writable public property/field '" + member + "'.");
        }

        static object GetProperties(JObject p)
        {
            var go = Target(p);
            var comp = GetComp(go, ResolveType(p));
            var so = new SerializedObject(comp);
            var props = new List<object>();

            var it = so.GetIterator();
            if (it.NextVisible(true))
            {
                do
                {
                    props.Add(new { name = it.name, type = it.propertyType.ToString(), value = ReadSerialized(it) });
                }
                while (it.NextVisible(false));
            }
            return new { type = comp.GetType().Name, properties = props };
        }

        static object ReadSerialized(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer: return sp.intValue;
                case SerializedPropertyType.Boolean: return sp.boolValue;
                case SerializedPropertyType.Float: return sp.floatValue;
                case SerializedPropertyType.String: return sp.stringValue;
                case SerializedPropertyType.Enum:
                    return (sp.enumNames != null && sp.enumValueIndex >= 0 && sp.enumValueIndex < sp.enumNames.Length)
                        ? (object)sp.enumNames[sp.enumValueIndex] : sp.intValue;
                case SerializedPropertyType.Vector2: { var v = sp.vector2Value; return new { x = v.x, y = v.y }; }
                case SerializedPropertyType.Vector3: { var v = sp.vector3Value; return new { x = v.x, y = v.y, z = v.z }; }
                case SerializedPropertyType.Vector4: { var v = sp.vector4Value; return new { x = v.x, y = v.y, z = v.z, w = v.w }; }
                case SerializedPropertyType.Color: { var c = sp.colorValue; return new { r = c.r, g = c.g, b = c.b, a = c.a }; }
                case SerializedPropertyType.ObjectReference:
                    var o = sp.objectReferenceValue;
                    return o == null ? null : (object)new { name = o.name, instanceId = o.GetInstanceID() };
                default:
                    return sp.propertyType.ToString();
            }
        }
    }
}
