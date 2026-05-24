using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Authors UXML and USS files (XDocument-based, with simple #name/.class/Type selectors).</summary>
    public class UIToolkitHandler : HandlerBase
    {
        static readonly XNamespace UI = "UnityEngine.UIElements";
        static readonly XNamespace UIE = "UnityEditor.UIElements";

        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_uxml": return CreateUxml(p);
                case "add_element": return AddElement(p);
                case "set_attribute": return SetAttribute(p);
                case "add_class": return AddClass(p);
                case "remove_element": return RemoveElement(p);
                case "create_uss": return CreateUss(p);
                case "add_uss_rule": return AddUssRule(p);
                case "validate_uxml": return ValidateUxml(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateUxml(JObject p)
        {
            string path = RequireString(p, "path");
            string title = OptString(p, "title", null);
            var root = new XElement(UI + "VisualElement",
                new XAttribute("name", "root"),
                new XAttribute("class", "root"));
            if (title != null)
                root.Add(new XElement(UI + "Label", new XAttribute("text", title), new XAttribute("class", "header")));

            var doc = new XDocument(
                new XElement(UI + "UXML",
                    new XAttribute(XNamespace.Xmlns + "ui", UI.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "uie", UIE.NamespaceName),
                    root));
            WriteDoc(doc, path);
            return new { created = true, path = path };
        }

        static object AddElement(JObject p)
        {
            string path = RequireString(p, "uxmlPath");
            var doc = LoadDoc(path);
            string parentSel = OptString(p, "parentSelector", "root");
            var parent = FindElement(doc, parentSel);
            if (parent == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Parent '" + parentSel + "' not found.");

            var el = new XElement(UI + RequireString(p, "elementType"));
            if (p["attributes"] is JObject attrs)
                foreach (var kv in attrs) el.SetAttributeValue(kv.Key, kv.Value.ToString());
            string name = OptString(p, "name", null);
            if (name != null) el.SetAttributeValue("name", name);
            string text = OptString(p, "text", null);
            if (text != null) el.SetAttributeValue("text", text);
            if (p["classes"] is JArray classes)
                el.SetAttributeValue("class", string.Join(" ", classes.Select(c => c.ToString())));

            parent.Add(el);
            WriteDoc(doc, path);
            return new { ok = true, added = el.Name.LocalName, parent = parentSel };
        }

        static object SetAttribute(JObject p)
        {
            string path = RequireString(p, "uxmlPath");
            var doc = LoadDoc(path);
            var el = FindElement(doc, RequireString(p, "selector"));
            if (el == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Element not found.");
            el.SetAttributeValue(RequireString(p, "attribute"), RequireString(p, "value"));
            WriteDoc(doc, path);
            return new { ok = true };
        }

        static object AddClass(JObject p)
        {
            string path = RequireString(p, "uxmlPath");
            var doc = LoadDoc(path);
            var el = FindElement(doc, RequireString(p, "selector"));
            if (el == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Element not found.");
            string cls = RequireString(p, "className");
            string existing = (string)el.Attribute("class") ?? "";
            var parts = existing.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!parts.Contains(cls)) parts.Add(cls);
            el.SetAttributeValue("class", string.Join(" ", parts));
            WriteDoc(doc, path);
            return new { ok = true };
        }

        static object RemoveElement(JObject p)
        {
            string path = RequireString(p, "uxmlPath");
            var doc = LoadDoc(path);
            var el = FindElement(doc, RequireString(p, "selector"));
            if (el == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Element not found.");
            el.Remove();
            WriteDoc(doc, path);
            return new { ok = true };
        }

        static object CreateUss(JObject p)
        {
            string path = RequireString(p, "path");
            var sb = new System.Text.StringBuilder();
            if (p["rules"] is JObject rules)
                foreach (var kv in rules)
                    if (kv.Value is JObject props) sb.Append(FormatRule(kv.Key, props));
            File.WriteAllText(AbsPath(path), sb.ToString());
            AssetDatabase.ImportAsset(path);
            return new { created = true, path = path };
        }

        static object AddUssRule(JObject p)
        {
            string path = RequireString(p, "ussPath");
            string abs = AbsPath(path);
            string existing = File.Exists(abs) ? File.ReadAllText(abs) : "";
            if (!(p["properties"] is JObject props)) throw Invalid("Missing 'properties' object.");
            existing += FormatRule(RequireString(p, "selector"), props);
            File.WriteAllText(abs, existing);
            AssetDatabase.ImportAsset(path);
            return new { ok = true };
        }

        static object ValidateUxml(JObject p)
        {
            string path = RequireString(p, "uxmlPath");
            try { XDocument.Load(AbsPath(path)); }
            catch (System.Exception e) { return new { valid = false, error = e.Message }; }
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            return new { valid = asset != null, importedAsVisualTree = asset != null };
        }

        // -- helpers --

        static string FormatRule(string selector, JObject props)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(selector).Append(" {\n");
            foreach (var kv in props) sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value.ToString()).Append(";\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static XElement RootVisual(XDocument doc)
        {
            return doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElement") ?? doc.Root;
        }

        static XElement FindElement(XDocument doc, string selector)
        {
            if (string.IsNullOrEmpty(selector) || selector == "root")
                return doc.Descendants().FirstOrDefault(e => (string)e.Attribute("name") == "root") ?? RootVisual(doc);
            if (selector.StartsWith("#"))
            {
                string n = selector.Substring(1);
                return doc.Descendants().FirstOrDefault(e => (string)e.Attribute("name") == n);
            }
            if (selector.StartsWith("."))
            {
                string c = selector.Substring(1);
                return doc.Descendants().FirstOrDefault(e =>
                {
                    string cls = (string)e.Attribute("class");
                    return cls != null && cls.Split(' ').Contains(c);
                });
            }
            return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == selector);
        }

        static XDocument LoadDoc(string path)
        {
            string abs = AbsPath(path);
            if (!File.Exists(abs)) throw new HandlerException(ErrorCodes.NOT_FOUND, "No UXML at '" + path + "'.");
            return XDocument.Load(abs);
        }

        static void WriteDoc(XDocument doc, string path)
        {
            AssetUtil.EnsureFolderForAsset(path);
            File.WriteAllText(AbsPath(path), doc.ToString());
            AssetDatabase.ImportAsset(path);
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
