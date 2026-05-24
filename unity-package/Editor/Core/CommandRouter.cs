using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Handlers.V1;
using UnityMCP.Protocol;

namespace UnityMCP.Core
{
    /// <summary>
    /// Maps a request's <c>category</c> to its handler and dispatches the <c>action</c>.
    /// Runs on the main thread (invoked via <see cref="MainThreadDispatcher"/>).
    /// </summary>
    public static class CommandRouter
    {
        static readonly Dictionary<string, IHandler> Handlers =
            new Dictionary<string, IHandler>(StringComparer.OrdinalIgnoreCase)
            {
                { "gameobject", new GameObjectHandler() },
                { "component",  new ComponentHandler() },
                { "ui",         new UIHandler() },
                { "material",   new MaterialHandler() },
                { "editorwindow", new EditorWindowHandler() },
                { "uitoolkit",  new UIToolkitHandler() },
                { "animation",  new AnimationHandler() },
                { "particles",  new ParticlesHandler() },
                { "lighting",   new LightingHandler() },
                { "audio",      new AudioHandler() },
                { "sprite",     new SpriteHandler() },
                { "tilemap",    new TilemapHandler() },
                { "physics",    new PhysicsHandler() },
                { "cinemachine", new CinemachineHandler() },
                { "input",      new InputHandler() },
                { "timeline",   new TimelineHandler() },
                { "capture",    new CaptureHandler() },
                { "asset",      new AssetHandler() },
                { "prefab",     new PrefabHandler() },
                { "script",     new ScriptHandler() },
                { "scriptableobject", new ScriptableObjectHandler() },
                { "scene",      new SceneHandler() },
                { "package",    new PackageHandler() },
                { "console",    new ConsoleHandler() },
                { "editor",     new EditorHandler() },
            };

        public static object Route(Request request)
        {
            if (request == null || string.IsNullOrEmpty(request.category))
                throw new HandlerException(ErrorCodes.MALFORMED_REQUEST, "Request is missing 'category'.");
            if (string.IsNullOrEmpty(request.action))
                throw new HandlerException(ErrorCodes.MALFORMED_REQUEST, "Request is missing 'action'.");

            if (!Handlers.TryGetValue(request.category, out IHandler handler))
                throw new HandlerException(ErrorCodes.UNKNOWN_CATEGORY,
                    "Unknown category '" + request.category + "'.");

            return handler.Handle(request.action, request.@params ?? new JObject());
        }

        public static ICollection<string> Categories
        {
            get { return Handlers.Keys; }
        }
    }
}
