using Newtonsoft.Json.Linq;
using UnityMCP.Protocol;

namespace UnityMCP.Handlers
{
    /// <summary>
    /// Shared base for handlers: action-not-found helper plus small parameter
    /// validation/extraction utilities used across the v1 and v2 handlers.
    /// </summary>
    public abstract class HandlerBase : IHandler
    {
        public abstract object Handle(string action, JObject p);

        protected static HandlerException UnknownAction(string action)
        {
            return new HandlerException(ErrorCodes.UNKNOWN_ACTION, "Unknown action '" + action + "'.");
        }

        protected static HandlerException Invalid(string message)
        {
            return new HandlerException(ErrorCodes.INVALID_PARAMS, message);
        }

        protected static string RequireString(JObject p, string key)
        {
            JToken token = p[key];
            if (token == null || token.Type == JTokenType.Null)
                throw Invalid("Missing required parameter '" + key + "'.");
            return token.ToString();
        }

        protected static int RequireInt(JObject p, string key)
        {
            JToken token = p[key];
            if (token == null || token.Type == JTokenType.Null)
                throw Invalid("Missing required parameter '" + key + "'.");
            try { return token.Value<int>(); }
            catch { throw Invalid("Parameter '" + key + "' must be an integer."); }
        }

        protected static string OptString(JObject p, string key, string fallback = null)
        {
            JToken token = p[key];
            return (token == null || token.Type == JTokenType.Null) ? fallback : token.ToString();
        }

        protected static int OptInt(JObject p, string key, int fallback = 0)
        {
            JToken token = p[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            try { return token.Value<int>(); }
            catch { return fallback; }
        }

        protected static bool OptBool(JObject p, string key, bool fallback = false)
        {
            JToken token = p[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            try { return token.Value<bool>(); }
            catch { return fallback; }
        }
    }
}
