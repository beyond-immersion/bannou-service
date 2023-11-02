using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public abstract class ServiceMessage
{
    [JsonIgnore]
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }

    public virtual void SetHeadersToProperties(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        if (headers == null || !headers.Any())
            return;

        foreach (var propertyInfo in GetType().GetProperties())
        {
            var headerAttr = propertyInfo.GetCustomAttributes<HeaderArrayAttribute>(true).FirstOrDefault();
            if (headerAttr == null)
            {
                Program.Logger.Log(LogLevel.Warning, "Appropriate headers for transfer not found- moving on...");
                continue;
            }

            var headerName = headerAttr.Name ?? propertyInfo.Name;
            var headerStrings = headers.FirstOrDefault(t => t.Key == headerName).Value;
            if (headerStrings == null || !headerStrings.Any())
            {
                Program.Logger.Log(LogLevel.Warning, $"No values were found for header {headerName} bound to property {propertyInfo.Name}.");
                continue;
            }

            var bindingResult = Binders.HeaderArrayModelBinder.BuildPropertyValueFromHeaders(propertyInfo.PropertyType, headerStrings, headerAttr);
            if (bindingResult == null)
            {
                Program.Logger.Log(LogLevel.Error, "A problem occurred generating property value from received headers.");
                continue;
            }

            Program.Logger.Log(LogLevel.Warning, $"Packing headers {JArray.FromObject(headerStrings).ToString(Formatting.None)} into property {propertyInfo.Name}.");
            propertyInfo.SetValue(this, bindingResult);
        }
    }

    public virtual (string, string[])[] SetPropertiesToHeaders()
    {
        var messageType = GetType();
        var headersToSet = new List<(string, string[])>();

        foreach (PropertyInfo propertyInfo in messageType.GetProperties())
        {
            var headerAttr = propertyInfo.GetCustomAttribute<HeaderArrayAttribute>();
            if (headerAttr == null)
                continue;

            var propertyValue = propertyInfo.GetValue(this);
            if (propertyValue == null)
                continue;

            var newHeadersToSet = SetHeaderArrayPropertyToHeaders(propertyInfo, propertyValue, headerAttr);
            if (newHeadersToSet != null)
                headersToSet.AddRange(newHeadersToSet);
        }

        return headersToSet.ToArray();
    }

    public static (string, string[])[] SetHeaderArrayPropertyToHeaders(PropertyInfo propertyInfo, object value, HeaderArrayAttribute headerAttr)
    {
        var headersToSet = new List<(string, string[])>();

        try
        {
            var headerKVPs = new List<(string, string?)>();
            switch (value)
            {
                case IEnumerable<KeyValuePair<string, IEnumerable<string>>> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Value)
                            headerKVPs.Add((kvp.Key, stringVal));

                    break;

                case IEnumerable<KeyValuePair<string, string[]>> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Value)
                            headerKVPs.Add((kvp.Key, stringVal));

                    break;

                case IEnumerable<KeyValuePair<string, List<string>>> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Value)
                            headerKVPs.Add((kvp.Key, stringVal));

                    break;

                case IEnumerable<KeyValuePair<string, string>> propVal:
                    foreach (var kvp in propVal)
                        headerKVPs.Add((kvp.Key, kvp.Value));

                    break;

                case IEnumerable<(string, IEnumerable<string>)> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Item2)
                            headerKVPs.Add((kvp.Item1, stringVal));

                    break;

                case IEnumerable<(string, string[])> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Item2)
                            headerKVPs.Add((kvp.Item1, stringVal));

                    break;

                case IEnumerable<(string, List<string>)> propVal:
                    foreach (var kvp in propVal)
                        foreach (var stringVal in kvp.Item2)
                            headerKVPs.Add((kvp.Item1, stringVal));

                    break;

                case IEnumerable<(string, string)> propVal:
                    foreach (var kvp in propVal)
                        headerKVPs.Add((kvp.Item1, kvp.Item2));

                    break;

                case IEnumerable<string> propVal:
                    foreach (var stringVal in propVal)
                        headerKVPs.Add((stringVal, null));

                    break;

                default:
                    break;
            }

            if (headerKVPs.Count == 0)
                return headersToSet.ToArray();

            var delim = "__";
            if (!string.IsNullOrWhiteSpace(headerAttr.Delimeter))
                delim = headerAttr.Delimeter;

            var headerName = headerAttr.Name ?? propertyInfo.Name;
            var headerValues = new List<string>();
            foreach (var headerKVP in headerKVPs)
            {
                var headerValue = headerKVP.Item1;
                if (!string.IsNullOrWhiteSpace(headerKVP.Item2))
                    headerValue = headerValue + delim + headerKVP.Item2;

                headerValues.Add(headerValue);
            }

            if (headerValues.Count > 0)
                headersToSet.Add((headerName, headerValues.ToArray()));
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"Could not set property values for '{propertyInfo.Name}' of type '{propertyInfo.PropertyType.Name}' to header array.");
        }

        return headersToSet.ToArray();
    }
}
