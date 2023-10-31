using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The base class for service responses.
/// </summary>
[JsonObject]
public class ServiceResponse : IServiceResponse
{
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }

    public (string, string[])[] PropertyValuesToHeaders()
    {
        var messageType = GetType();
        var headersToSet = new List<(string, string[])>();

        foreach (var propertyInfo in messageType.GetProperties())
        {
            var headerAttr = propertyInfo.GetCustomAttribute<HeaderArrayAttribute>();
            if (headerAttr == null)
                continue;

            var propertyValue = propertyInfo.GetValue(this);
            if (propertyValue == null)
                continue;

            var newHeadersToSet = ServiceRequest.PropertyValueToHeaderArray(propertyInfo, propertyValue, headerAttr);
            if (newHeadersToSet != null)
                headersToSet.AddRange(newHeadersToSet);
        }

        return headersToSet.ToArray();
    }
}
