using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Renders the Scene view / Game view / a camera to a PNG (base64) so Claude can see results.</summary>
    public class CaptureHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "screenshot_scene_view": return SceneViewShot(p);
                case "screenshot_game_view": return GameViewShot(p);
                case "render_camera": return RenderCameraShot(p);
                default: throw UnknownAction(action);
            }
        }

        static object SceneViewShot(JObject p)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No active Scene View — open/focus the Scene tab first.");
            int w = OptInt(p, "width", Mathf.Max(64, (int)sv.camera.pixelWidth));
            int h = OptInt(p, "height", Mathf.Max(64, (int)sv.camera.pixelHeight));
            return Capture(sv.camera, w, h);
        }

        static object GameViewShot(JObject p)
        {
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0)
            {
                var cams = Camera.allCameras;
                if (cams.Length > 0) cam = cams[0];
            }
            if (cam == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No camera in the scene to render the Game view.");
            return Capture(cam, OptInt(p, "width", 1280), OptInt(p, "height", 720));
        }

        static object RenderCameraShot(JObject p)
        {
            var go = ObjectFinder.Resolve(p["cameraTarget"]);
            var cam = go.GetComponent<Camera>();
            if (cam == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Camera on '" + go.name + "'.");
            return Capture(cam, OptInt(p, "width", 1280), OptInt(p, "height", 720));
        }

        static object Capture(Camera cam, int w, int h)
        {
            w = Mathf.Clamp(w, 16, 4096);
            h = Mathf.Clamp(h, 16, 4096);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                bool rendered = false;
                try
                {
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        RenderPipeline.SubmitRenderRequest(cam, request);
                        rendered = true;
                    }
                }
                catch { rendered = false; }

                if (!rendered)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevTarget;
                }

                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                return new { base64Png = Convert.ToBase64String(png), width = w, height = h, bytes = png.Length };
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
