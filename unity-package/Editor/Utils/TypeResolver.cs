using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMCP.Utils
{
    /// <summary>
    /// Resolves a type name (e.g. "Rigidbody", "BoxCollider", "MyScript") to a Type,
    /// trying common Unity namespaces first and falling back to a full assembly scan.
    /// Results are cached for the lifetime of the domain.
    /// </summary>
    public static class TypeResolver
    {
        static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static Type ResolveComponentType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Type cached;
            if (Cache.TryGetValue(name, out cached)) return cached;

            Type found = Find(name);
            if (found != null) Cache[name] = found;
            return found;
        }

        static Type Find(string name)
        {
            string[] qualified =
            {
                name,
                "UnityEngine." + name + ", UnityEngine",
                "UnityEngine.UI." + name + ", UnityEngine.UI",
                "UnityEngine." + name + ", UnityEngine.CoreModule",
                "UnityEngine." + name + ", UnityEngine.PhysicsModule",
                "UnityEngine." + name + ", UnityEngine.Physics2DModule",
            };
            foreach (var q in qualified)
            {
                var t = Type.GetType(q);
                if (t != null) return t;
            }

            // Prefer a Component subclass; remember any other exact-name match as a fallback.
            Type nonComponent = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var ty in types)
                {
                    if (ty.Name != name && ty.FullName != name) continue;
                    if (typeof(Component).IsAssignableFrom(ty)) return ty;
                    if (nonComponent == null) nonComponent = ty;
                }
            }
            return nonComponent;
        }
    }
}
