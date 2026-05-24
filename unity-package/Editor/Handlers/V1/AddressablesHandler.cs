using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Addressables. Stub until com.unity.addressables is installed (unity_add_package).</summary>
    public class AddressablesHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.ADDRESSABLES_NOT_INSTALLED,
                "Addressables (com.unity.addressables) is not installed. Install it via unity_add_package, then this category can manage groups/labels/builds.");
        }
    }
}
