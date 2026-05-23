namespace UnityMCP.Protocol
{
    /// <summary>
    /// Outbound message to the Python server. Null fields are dropped during
    /// serialization (see MCPBridge serializer settings), so a success response
    /// carries no "error" and a failure carries no "data".
    /// </summary>
    public class Response
    {
        public string id;
        public string type = "response";
        public bool success;
        public object data;
        public ErrorInfo error;
        public ResponseMeta meta;

        public static Response Ok(string id, object data)
        {
            return new Response { id = id, success = true, data = data };
        }

        public static Response Fail(string id, string code, string message)
        {
            return new Response
            {
                id = id,
                success = false,
                error = new ErrorInfo { code = code, message = message }
            };
        }
    }

    public class ErrorInfo
    {
        public string code;
        public string message;
    }

    public class ResponseMeta
    {
        public int executionMs;
    }
}
