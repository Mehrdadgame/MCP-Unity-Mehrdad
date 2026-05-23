using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Reads and clears the Unity Editor Console (via ConsoleLogReader).</summary>
    public class ConsoleHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "get_logs": return GetLogs(p);
                case "clear": return Clear();
                default: throw UnknownAction(action);
            }
        }

        static object GetLogs(JObject p)
        {
            string level = OptString(p, "level", "All");
            int limit = OptInt(p, "limit", 100);
            return ConsoleLogReader.GetLogs(level, limit);
        }

        static object Clear()
        {
            ConsoleLogReader.Clear();
            return new { cleared = true };
        }
    }
}
