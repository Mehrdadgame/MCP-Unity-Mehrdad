using Newtonsoft.Json.Linq;

namespace UnityMCP.Protocol
{
    /// <summary>
    /// Inbound message from the Python server. Mirrors the wire format in the spec:
    /// { id, type, category, action, params }.
    /// </summary>
    public class Request
    {
        public string id;
        public string type;       // "request" | "batch" | "stream" (only "request" handled in Phase 1)
        public string category;
        public string action;
        public JObject @params;
    }
}
