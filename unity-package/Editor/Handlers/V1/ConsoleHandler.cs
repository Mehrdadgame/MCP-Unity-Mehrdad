using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Console log capture and querying. Implemented in Phase 10.</summary>
    public class ConsoleHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw UnknownAction(action);
        }
    }
}
