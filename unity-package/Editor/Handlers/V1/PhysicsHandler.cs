using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Rigidbodies, colliders, and physics settings (3D and 2D, auto-detected).</summary>
    public class PhysicsHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "add_rigidbody": return AddRigidbody(p);
                case "set_rigidbody": return SetRigidbody(p);
                case "add_collider": return AddCollider(p);
                case "set_physics_settings": return SetPhysicsSettings(p);
                case "set_layer_collision": return SetLayerCollision(p);
                default: throw UnknownAction(action);
            }
        }

        static bool Is2D(JObject p, GameObject go)
        {
            string dim = OptString(p, "dimensions", null);
            if (dim != null) return dim.Contains("2");
            return go.GetComponent<Collider2D>() != null || go.GetComponent<SpriteRenderer>() != null;
        }

        static object AddRigidbody(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            if (Is2D(p, go))
            {
                var rb = go.GetComponent<Rigidbody2D>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody2D>(go);
                Apply2D(rb, p);
                MarkDirty(go);
                return new { ok = true, kind = "Rigidbody2D", instanceId = rb.GetInstanceID() };
            }
            else
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
                Apply3D(rb, p);
                MarkDirty(go);
                return new { ok = true, kind = "Rigidbody", instanceId = rb.GetInstanceID() };
            }
        }

        static object SetRigidbody(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var rb3 = go.GetComponent<Rigidbody>();
            if (rb3 != null) { Apply3D(rb3, p); MarkDirty(go); return new { ok = true, kind = "Rigidbody" }; }
            var rb2 = go.GetComponent<Rigidbody2D>();
            if (rb2 != null) { Apply2D(rb2, p); MarkDirty(go); return new { ok = true, kind = "Rigidbody2D" }; }
            throw new HandlerException(ErrorCodes.NOT_FOUND, "No Rigidbody/Rigidbody2D on '" + go.name + "'.");
        }

        static void Apply3D(Rigidbody rb, JObject p)
        {
            Undo.RecordObject(rb, "Set Rigidbody");
            if (p["mass"] != null) rb.mass = p["mass"].Value<float>();
            if (p["useGravity"] != null) rb.useGravity = p["useGravity"].Value<bool>();
            if (p["isKinematic"] != null) rb.isKinematic = p["isKinematic"].Value<bool>();
#pragma warning disable 618
            if (p["drag"] != null) rb.drag = p["drag"].Value<float>();
            if (p["angularDrag"] != null) rb.angularDrag = p["angularDrag"].Value<float>();
#pragma warning restore 618
        }

        static void Apply2D(Rigidbody2D rb, JObject p)
        {
            Undo.RecordObject(rb, "Set Rigidbody2D");
            if (p["mass"] != null) rb.mass = p["mass"].Value<float>();
            if (p["gravityScale"] != null) rb.gravityScale = p["gravityScale"].Value<float>();
            if (p["isKinematic"] != null)
                rb.bodyType = p["isKinematic"].Value<bool>() ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
#pragma warning disable 618
            if (p["drag"] != null) rb.drag = p["drag"].Value<float>();
#pragma warning restore 618
        }

        static object AddCollider(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            string shape = RequireString(p, "shape");
            bool trig = OptBool(p, "isTrigger", false);
            Component col;
            switch (shape.ToLowerInvariant())
            {
                case "box": { var c = Undo.AddComponent<BoxCollider>(go); c.isTrigger = trig; col = c; break; }
                case "sphere": { var c = Undo.AddComponent<SphereCollider>(go); c.isTrigger = trig; col = c; break; }
                case "capsule": { var c = Undo.AddComponent<CapsuleCollider>(go); c.isTrigger = trig; col = c; break; }
                case "mesh": { var c = Undo.AddComponent<MeshCollider>(go); c.convex = OptBool(p, "convex", false); c.isTrigger = trig && c.convex; col = c; break; }
                case "box2d": { var c = Undo.AddComponent<BoxCollider2D>(go); c.isTrigger = trig; col = c; break; }
                case "circle2d": { var c = Undo.AddComponent<CircleCollider2D>(go); c.isTrigger = trig; col = c; break; }
                case "capsule2d": { var c = Undo.AddComponent<CapsuleCollider2D>(go); c.isTrigger = trig; col = c; break; }
                case "polygon2d": { var c = Undo.AddComponent<PolygonCollider2D>(go); c.isTrigger = trig; col = c; break; }
                default: throw Invalid("shape must be Box/Sphere/Capsule/Mesh/Box2D/Circle2D/Capsule2D/Polygon2D.");
            }
            MarkDirty(go);
            return new { ok = true, collider = col.GetType().Name };
        }

        static object SetPhysicsSettings(JObject p)
        {
            if (p["gravity"] != null) Physics.gravity = ValueParser.ToVector3(p["gravity"]);
            if (p["gravity2D"] != null) Physics2D.gravity = ValueParser.ToVector2(p["gravity2D"]);
            return new { ok = true, gravity = new { x = Physics.gravity.x, y = Physics.gravity.y, z = Physics.gravity.z } };
        }

        static object SetLayerCollision(JObject p)
        {
            int l1 = ResolveLayer(p["layer1"]);
            int l2 = ResolveLayer(p["layer2"]);
            bool collide = OptBool(p, "collide", true);
            Physics.IgnoreLayerCollision(l1, l2, !collide);
            return new { ok = true, layer1 = l1, layer2 = l2, collide = collide };
        }

        static int ResolveLayer(JToken t)
        {
            if (t == null) throw Invalid("Missing layer.");
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            int l = LayerMask.NameToLayer(t.ToString());
            if (l < 0) throw Invalid("Unknown layer '" + t + "'.");
            return l;
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
