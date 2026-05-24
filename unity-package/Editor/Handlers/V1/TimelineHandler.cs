using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
#if MCP_TIMELINE
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using UnityMCP.Utils;
#endif

namespace UnityMCP.Handlers.V1
{
    /// <summary>TimelineAsset + tracks. Gated on com.unity.timeline.</summary>
    public class TimelineHandler : HandlerBase
    {
#if !MCP_TIMELINE
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.PACKAGE_ERROR,
                "Timeline (com.unity.timeline) is not installed. Install it via Package Manager.");
        }
#else
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_timeline": return CreateTimeline(p);
                case "add_track": return AddTrack(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateTimeline(JObject p)
        {
            string path = RequireString(p, "path");
            if (!path.EndsWith(".playable")) path += ".playable";
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetUtil.EnsureFolderForAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return new { path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        static object AddTrack(JObject p)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TimelineAsset>(RequireString(p, "timelinePath"));
            if (asset == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No TimelineAsset at the given timelinePath.");
            string trackType = OptString(p, "trackType", "Animation");
            Type t;
            switch (trackType.ToLowerInvariant())
            {
                case "animation": t = typeof(AnimationTrack); break;
                case "activation": t = typeof(ActivationTrack); break;
                case "audio": t = typeof(AudioTrack); break;
                case "playable": t = typeof(PlayableTrack); break;
                case "signal": t = typeof(SignalTrack); break;
                case "group": t = typeof(GroupTrack); break;
                default: throw Invalid("trackType must be Animation/Activation/Audio/Playable/Signal/Group.");
            }
            var track = asset.CreateTrack(t, null, OptString(p, "name", trackType + " Track"));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return new { ok = true, track = track.name, type = t.Name };
        }
#endif
    }
}
