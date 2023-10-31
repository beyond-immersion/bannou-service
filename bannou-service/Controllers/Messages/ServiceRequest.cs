using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest<T> : ServiceRequest, IServiceRequest<T>
    where T : class, IServiceResponse, new()
{
    public T CreateResponse()
    {
        var requestType = GetType();
        var responseType = typeof(T);
        T? responseObj = null;

        try
        {
            responseObj = Activator.CreateInstance(responseType, true) as T;
            if (responseObj == null)
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to create instance of response type [{responseType.Name}].");
                return new();
            }

            var requestProps = requestType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => t.GetCustomAttribute<HeaderArrayAttribute>(true) != null);

            if (requestProps == null || !requestProps.Any())
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to fetch header properties on request type [{requestType.Name}].");
                return responseObj;
            }

            var responseProps = responseType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(t => t.GetCustomAttribute<HeaderArrayAttribute>(true) != null);

            if (responseProps == null || !responseProps.Any())
            {
                Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to fetch header properties on response type [{responseType.Name}].");
                return responseObj;
            }

            foreach (var responseProp in responseProps)
            {
                try
                {
                    var requestProp = requestProps.First(requestProp => string.Equals(responseProp.Name, requestProp.Name));
                    if (requestProp != null && responseProp.PropertyType.IsAssignableFrom(requestProp.PropertyType))
                        responseProp.SetValue(responseObj, requestProp.GetValue(this));
                    else
                        Program.Logger.Log(LogLevel.Error, $"A problem occurred attempting to set header property value " +
                            $"from request type [{requestType.Name}] to response type [{responseType.Name}].");
                }
                catch (Exception exc)
                {
                    Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown copying property from [{requestType.Name}] to [{responseType.Name}].");
                }
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception was thrown using request model [{requestType.Name}] to create response model [{responseType.Name}].");
        }

        return responseObj ?? new();
    }
}

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public class ServiceRequest : IServiceRequest
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

            var newHeadersToSet = PropertyValueToHeaderArray(propertyInfo, propertyValue, headerAttr);
            if (newHeadersToSet != null)
                headersToSet.AddRange(newHeadersToSet);
        }

        return headersToSet.ToArray();
    }

    public static (string, string[])[] PropertyValueToHeaderArray(PropertyInfo propertyInfo, object value, HeaderArrayAttribute headerAttr)
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
