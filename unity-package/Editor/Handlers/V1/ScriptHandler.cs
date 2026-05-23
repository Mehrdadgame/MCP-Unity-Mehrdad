using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityMCP.Core;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>
    /// Script compilation control. (Full script generation arrives in Phase 5; the
    /// recompile/result actions are here early because the dev loop needs them.)
    /// </summary>
    public class ScriptHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "recompile": return Recompile(p);
                case "get_compile_result": return CompileWatcher.GetResult();
                default: throw UnknownAction(action);
            }
        }

        static object Recompile(JObject p)
        {
            bool force = OptBool(p, "force", true);
            long before = CompileWatcher.LastFinishedTicks;

            // Defer the refresh/compile so THIS response is written before the domain
            // reload tears down the socket. The client then polls get_compile_result,
            // reconnecting across the reload.
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                if (force) CompilationPipeline.RequestScriptCompilation();
            };

            return new { requested = true, force = force, previousFinishedAtTicks = before };
        }
    }
}
