using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>C# script generation and compile coordination. Implemented in Phase 5.</summary>
    public class ScriptHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw UnknownAction(action);
        }
    }
}
