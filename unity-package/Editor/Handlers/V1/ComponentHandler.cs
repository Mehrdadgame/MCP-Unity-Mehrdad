using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Add/remove/configure components via reflection. Implemented in Phase 2.</summary>
    public class ComponentHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw UnknownAction(action);
        }
    }
}
