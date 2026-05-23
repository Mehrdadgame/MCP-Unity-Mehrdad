using Newtonsoft.Json.Linq;

namespace UnityMCP.Handlers
{
    /// <summary>
    /// One handler per request category. Implementations dispatch on <paramref name="action"/>
    /// and return any JSON-serializable object as the response data.
    /// </summary>
    public interface IHandler
    {
        object Handle(string action, JObject p);
    }
}
