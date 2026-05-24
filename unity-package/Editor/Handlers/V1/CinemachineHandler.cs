using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;

namespace UnityMCP.Handlers.V1
{
    /// <summary>
    /// Cinemachine authoring. Cinemachine (com.unity.cinemachine) is not installed in the
    /// current project, so every action reports CINEMACHINE_NOT_INSTALLED with guidance.
    /// (Real implementation is gated for a future pass once the package is present.)
    /// </summary>
    public class CinemachineHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.CINEMACHINE_NOT_INSTALLED,
                "Cinemachine (com.unity.cinemachine) is not installed. Install it via Package Manager " +
                "(Window > Package Manager > Unity Registry > Cinemachine), then this category can drive virtual cameras.");
        }
    }
}
