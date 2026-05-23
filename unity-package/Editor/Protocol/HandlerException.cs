using System;

namespace UnityMCP.Protocol
{
    /// <summary>
    /// Thrown by handlers to signal a structured failure. The <see cref="Code"/>
    /// surfaces to the client as the response error code (see <see cref="ErrorCodes"/>).
    /// </summary>
    public class HandlerException : Exception
    {
        public string Code { get; }

        public HandlerException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
