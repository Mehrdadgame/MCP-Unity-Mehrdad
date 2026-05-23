using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Asset database operations (find, create, move, refresh). Implemented in Phase 3.</summary>
    public class AssetHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw UnknownAction(action);
        }
    }
}
