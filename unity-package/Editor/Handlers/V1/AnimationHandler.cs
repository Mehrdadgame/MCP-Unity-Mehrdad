using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Animator controllers, states, transitions, parameters, clips, and assignment.</summary>
    public class AnimationHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_controller": return CreateController(p);
                case "add_parameter": return AddParameter(p);
                case "add_state": return AddState(p);
                case "add_transition": return AddTransition(p);
                case "set_default_state": return SetDefaultState(p);
                case "create_clip": return CreateClip(p);
                case "assign_to_animator": return AssignToAnimator(p);
                default: throw UnknownAction(action);
            }
        }

        static AnimatorController LoadController(JObject p)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(RequireString(p, "controllerPath"));
            if (c == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No AnimatorController at the given controllerPath.");
            return c;
        }

        static object CreateController(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".controller")) path += ".controller";
            AssetUtil.EnsureFolderForAsset(path);
            AnimatorController.CreateAnimatorControllerAtPath(path);
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object AddParameter(JObject p)
        {
            var c = LoadController(p);
            string name = RequireString(p, "name");
            AnimatorControllerParameterType t;
            switch (OptString(p, "type", "Float").ToLowerInvariant())
            {
                case "float": t = AnimatorControllerParameterType.Float; break;
                case "int": t = AnimatorControllerParameterType.Int; break;
                case "bool": t = AnimatorControllerParameterType.Bool; break;
                case "trigger": t = AnimatorControllerParameterType.Trigger; break;
                default: throw Invalid("type must be Float/Int/Bool/Trigger.");
            }
            c.AddParameter(name, t);
            EditorUtility.SetDirty(c);
            return new { ok = true, name = name, type = t.ToString() };
        }

        static object AddState(JObject p)
        {
            var c = LoadController(p);
            var sm = c.layers[OptInt(p, "layer", 0)].stateMachine;
            var state = sm.AddState(RequireString(p, "stateName"));
            string clipPath = OptString(p, "clipPath", null);
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null) state.motion = clip;
            }
            EditorUtility.SetDirty(c);
            return new { ok = true, stateName = state.name };
        }

        static object AddTransition(JObject p)
        {
            var c = LoadController(p);
            var sm = c.layers[OptInt(p, "layer", 0)].stateMachine;
            var from = FindState(sm, RequireString(p, "from"));
            var to = FindState(sm, RequireString(p, "to"));
            if (from == null || to == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "from/to state not found.");
            var tr = from.AddTransition(to);
            tr.hasExitTime = OptBool(p, "hasExitTime", false);
            if (p["duration"] != null) tr.duration = p["duration"].Value<float>();
            if (p["conditions"] is JArray conds)
            {
                foreach (var cd in conds)
                {
                    if (cd["parameter"] == null) continue;
                    var mode = ParseMode(cd["mode"] != null ? cd["mode"].ToString() : "If");
                    float thr = cd["threshold"] != null ? cd["threshold"].Value<float>() : 0f;
                    tr.AddCondition(mode, thr, cd["parameter"].ToString());
                }
            }
            EditorUtility.SetDirty(c);
            return new { ok = true, from = from.name, to = to.name };
        }

        static object SetDefaultState(JObject p)
        {
            var c = LoadController(p);
            var sm = c.layers[OptInt(p, "layer", 0)].stateMachine;
            var state = FindState(sm, RequireString(p, "stateName"));
            if (state == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "State not found.");
            sm.defaultState = state;
            EditorUtility.SetDirty(c);
            return new { ok = true, defaultState = state.name };
        }

        static object CreateClip(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".anim")) path += ".anim";
            var clip = new AnimationClip();
            if (p["frameRate"] != null) clip.frameRate = p["frameRate"].Value<float>();
            AssetUtil.EnsureFolderForAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object AssignToAnimator(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = Undo.AddComponent<Animator>(go);
            var c = LoadController(p);
            anim.runtimeAnimatorController = c;
            EditorUtility.SetDirty(go);
            return new { ok = true, controller = c.name };
        }

        static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var s in sm.states) if (s.state.name == name) return s.state;
            return null;
        }

        static AnimatorConditionMode ParseMode(string mode)
        {
            switch (mode.ToLowerInvariant())
            {
                case "if":
                case "true": return AnimatorConditionMode.If;
                case "ifnot":
                case "false": return AnimatorConditionMode.IfNot;
                case "greater": return AnimatorConditionMode.Greater;
                case "less": return AnimatorConditionMode.Less;
                case "equals": return AnimatorConditionMode.Equals;
                case "notequal": return AnimatorConditionMode.NotEqual;
                default: return AnimatorConditionMode.If;
            }
        }
    }
}
