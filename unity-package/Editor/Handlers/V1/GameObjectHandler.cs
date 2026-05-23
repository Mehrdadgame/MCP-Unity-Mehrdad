using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>GameObject CRUD and hierarchy operations. Implemented in Phase 2.</summary>
    public class GameObjectHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw UnknownAction(action);
        }
    }
}
