using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;

namespace UnityMCP.Handlers.V1
{
    /// <summary>Localization. Stub until com.unity.localization is installed (unity_add_package).</summary>
    public class LocalizationHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.LOCALIZATION_NOT_INSTALLED,
                "Localization (com.unity.localization) is not installed. Install it via unity_add_package, then this category can manage locales/string tables.");
        }
    }
}
