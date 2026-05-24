using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Player Settings + build pipeline. build_player runs deferred (poll get_build_result).</summary>
    public class BuildHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "get_build_targets": return GetBuildTargets();
                case "get_player_settings": return GetPlayerSettings();
                case "set_player_setting": return SetPlayerSetting(p);
                case "set_company_name": PlayerSettings.companyName = RequireString(p, "name"); return new { ok = true, companyName = PlayerSettings.companyName };
                case "set_product_name": PlayerSettings.productName = RequireString(p, "name"); return new { ok = true, productName = PlayerSettings.productName };
                case "set_bundle_identifier": return SetBundleId(p);
                case "set_define_symbols": return SetDefines(p);
                case "switch_platform": return SwitchPlatform(p);
                case "build_player": return BuildPlayer(p);
                case "get_build_result": return GetBuildResult();
                default: throw UnknownAction(action);
            }
        }

        static object GetBuildTargets()
        {
            var list = new List<object>();
            foreach (BuildTarget t in Enum.GetValues(typeof(BuildTarget)))
            {
                if ((int)t <= 0) continue;
                if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(t), t))
                    list.Add(t.ToString());
            }
            return new { active = EditorUserBuildSettings.activeBuildTarget.ToString(), supported = list };
        }

        static object GetPlayerSettings()
        {
            return new
            {
                companyName = PlayerSettings.companyName,
                productName = PlayerSettings.productName,
                bundleVersion = PlayerSettings.bundleVersion,
                applicationIdentifier = PlayerSettings.applicationIdentifier,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString(),
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            };
        }

        static object SetPlayerSetting(JObject p)
        {
            string setting = RequireString(p, "setting");
            var prop = typeof(PlayerSettings).GetProperty(setting,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
                throw new HandlerException(ErrorCodes.PROPERTY_NOT_FOUND, "PlayerSettings has no writable static property '" + setting + "'.");
            prop.SetValue(null, ValueParser.Convert(p["value"], prop.PropertyType));
            return new { ok = true, setting = setting };
        }

        static object SetBundleId(JObject p)
        {
            var grp = ParseGroup(OptString(p, "group", "Standalone"));
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.FromBuildTargetGroup(grp), RequireString(p, "id"));
            return new { ok = true };
        }

        static object SetDefines(JObject p)
        {
            var grp = ParseGroup(OptString(p, "group", "Standalone"));
            var symbols = new List<string>();
            if (p["symbols"] is JArray arr) foreach (var s in arr) symbols.Add(s.ToString());
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(grp), symbols.ToArray());
            return new { ok = true, symbols = symbols };
        }

        static object SwitchPlatform(JObject p)
        {
            var target = ParseTarget(RequireString(p, "target"));
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target);
            return new { ok = ok, active = EditorUserBuildSettings.activeBuildTarget.ToString() };
        }

        static object BuildPlayer(JObject p)
        {
            var target = ParseTarget(RequireString(p, "target"));
            string outputPath = RequireString(p, "outputPath");
            bool dev = OptBool(p, "development", false);
            string[] scenes = ScenesFrom(p);

            SessionState.SetBool("MCP.Build.Done", false);
            SessionState.SetString("MCP.Build.Result", "");
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var options = new BuildPlayerOptions
                    {
                        scenes = scenes,
                        locationPathName = outputPath,
                        target = target,
                        targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                        options = dev ? BuildOptions.Development : BuildOptions.None,
                    };
                    var s = BuildPipeline.BuildPlayer(options).summary;
                    SessionState.SetString("MCP.Build.Result", JsonConvert.SerializeObject(new
                    {
                        result = s.result.ToString(),
                        totalSize = (long)s.totalSize,
                        totalTimeSeconds = s.totalTime.TotalSeconds,
                        outputPath = s.outputPath,
                        totalErrors = s.totalErrors,
                        totalWarnings = s.totalWarnings,
                    }));
                }
                catch (Exception e)
                {
                    SessionState.SetString("MCP.Build.Result", JsonConvert.SerializeObject(new { result = "Exception", error = e.Message }));
                }
                SessionState.SetBool("MCP.Build.Done", true);
            };
            return new
            {
                requested = true,
                target = target.ToString(),
                outputPath = outputPath,
                scenes = scenes.Length,
                note = "The editor freezes while building. Poll build.get_build_result until done=true.",
            };
        }

        static object GetBuildResult()
        {
            bool done = SessionState.GetBool("MCP.Build.Done", false);
            string res = SessionState.GetString("MCP.Build.Result", "");
            return new { done = done, result = string.IsNullOrEmpty(res) ? null : JToken.Parse(res) };
        }

        static string[] ScenesFrom(JObject p)
        {
            if (p["scenes"] is JArray arr && arr.Count > 0)
            {
                var list = new List<string>();
                foreach (var s in arr) list.Add(s.ToString());
                return list.ToArray();
            }
            var enabled = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) enabled.Add(s.path);
            return enabled.ToArray();
        }

        static BuildTarget ParseTarget(string s)
        {
            try { return (BuildTarget)Enum.Parse(typeof(BuildTarget), s, true); }
            catch { throw Invalid("Unknown build target '" + s + "'. e.g. StandaloneWindows64, Android, iOS, WebGL."); }
        }

        static BuildTargetGroup ParseGroup(string s)
        {
            try { return (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), s, true); }
            catch { return BuildTargetGroup.Standalone; }
        }
    }
}
