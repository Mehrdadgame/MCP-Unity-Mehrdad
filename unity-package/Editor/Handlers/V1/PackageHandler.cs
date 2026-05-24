using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;

namespace UnityMCP.Handlers.V1
{
    /// <summary>UPM package management. Add/remove kick off async UPM resolution (which
    /// usually triggers a recompile/reload); confirm completion by calling list afterward.</summary>
    public class PackageHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "list": return List();
                case "add": return Add(p);
                case "remove": return Remove(p);
                default: throw UnknownAction(action);
            }
        }

        static string ManifestPath()
        {
            string data = Application.dataPath; // <project>/Assets
            return data.Substring(0, data.Length - "Assets".Length) + "Packages/manifest.json";
        }

        static object List()
        {
            string mf = ManifestPath();
            if (!File.Exists(mf)) throw new HandlerException(ErrorCodes.IO_ERROR, "manifest.json not found.");
            var deps = JObject.Parse(File.ReadAllText(mf))["dependencies"] as JObject;
            var list = new List<object>();
            if (deps != null)
                foreach (var kv in deps) list.Add(new { id = kv.Key, version = kv.Value.ToString() });
            return new { count = list.Count, packages = list };
        }

        static object Add(JObject p)
        {
            string id = RequireString(p, "id");
            string version = OptString(p, "version", null);
            string identifier = string.IsNullOrEmpty(version) ? id : id + "@" + version;
            // Fire-and-forget: UPM resolves over editor ticks and may trigger a reload.
            Client.Add(identifier);
            return new
            {
                requested = true,
                identifier = identifier,
                note = "UPM is resolving; this may trigger a recompile/reload. Call package.list after a few seconds to confirm.",
            };
        }

        static object Remove(JObject p)
        {
            string id = RequireString(p, "id");
            if (!OptBool(p, "confirm", false))
                throw new HandlerException(ErrorCodes.CONFIRMATION_REQUIRED, "Pass confirm=true to remove package '" + id + "'.");
            Client.Remove(id);
            return new { requested = true, id = id, note = "UPM is removing the package; call package.list after a few seconds to confirm." };
        }
    }
}
