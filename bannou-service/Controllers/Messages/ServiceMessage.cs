using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The basic service message payload model.
/// </summary>
[JsonObject]
public abstract class ServiceMessage
{
    private static class SupportedBindings
    {
        public static Type Dictionary => typeof(Dictionary<string, string>);
        public static Type DictionaryWithEnumerable => typeof(Dictionary<string, IEnumerable<string>>);
        public static Type DictionaryWithArray => typeof(Dictionary<string, string[]>);
        public static Type DictionaryWithList => typeof(Dictionary<string, List<string>>);
        public static Type ArrayTuple => typeof((string, string)[]);
        public static Type ArrayTupleWithEnumerable => typeof((string, IEnumerable<string>)[]);
        public static Type ArrayTupleWithArray => typeof((string, string[])[]);
        public static Type ArrayTupleWithList => typeof((string, List<string>)[]);
        public static Type ListTuple => typeof(List<(string, string)>);
        public static Type ListTupleWithEnumerable => typeof(List<(string, IEnumerable<string>)>);
        public static Type ListTupleWithArray => typeof(List<(string, string[])>);
        public static Type ListTupleWithList => typeof(List<(string, List<string>)>);
        public static Type Array => typeof(string[]);
        public static Type List => typeof(List<string>);
    };

    private static Type[] _arrayInterfaces;
    public static Type[] ArrayInterfaces
            => _arrayInterfaces ??= typeof(Array).GetAllImplementedInterfaces();

    private static Type[] _listInterfaces;
    public static Type[] ListInterfaces
            => _listInterfaces ??= typeof(List<>).GetAllImplementedInterfaces();

    private static Type[] _dictionaryInterfaces;
    public static Type[] DictionaryInterfaces
            => _dictionaryInterfaces ??= typeof(Dictionary<,>).GetAllImplementedInterfaces();

    [JsonIgnore]
    [HeaderArray(Name = "REQUEST_IDS")]
    public Dictionary<string, string>? RequestIDs { get; set; }

    public virtual void SetHeadersToProperties(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        if (headers == null || !headers.Any())
            return;

        foreach (var propertyInfo in GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            var headerAttr = propertyInfo.GetCustomAttributes<HeaderArrayAttribute>(true).FirstOrDefault();
            if (headerAttr == null)
                continue;

            var headerName = headerAttr.Name ?? propertyInfo.Name;
            var headerStrings = headers.FirstOrDefault(t => string.Equals(headerName, t.Key, StringComparison.InvariantCultureIgnoreCase)).Value;
            if (headerStrings == null || !headerStrings.Any())
            {
                Program.Logger.Log(LogLevel.Debug, $"No values were found for header {headerName} bound to property {propertyInfo.Name}.");
                continue;
            }

            var bindingResult = BuildPropertyValueFromHeaders(propertyInfo.PropertyType, headerStrings, headerAttr);
            if (bindingResult == null)
            {
                Program.Logger.Log(LogLevel.Error, "A problem occurred generating property value from received headers.");
                continue;
            }

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

    public static object? BuildPropertyValueFromHeaders(Type propertyType, IEnumerable<string> headers, HeaderArrayAttribute propertyAttr)
    {
        if (headers == null || !headers.Any())
            return null;

        try
        {
            var delim = "__";
            if (!string.IsNullOrWhiteSpace(propertyAttr.Delimeter))
                delim = propertyAttr.Delimeter;

            if (propertyType.IsAbstract)
            {
                var isEnumerable = propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>);
                var enumerableType = isEnumerable ? propertyType.GetGenericArguments()?.FirstOrDefault() : null;
                var isEnumerableKVP = enumerableType != null && enumerableType.IsGenericType && enumerableType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

                // treat enumerable KVPs as if they want dictionaries
                if (DictionaryInterfaces.Contains(propertyType) || isEnumerableKVP)
                    return BuildDictionaryFromHeaders(propertyType, headers, delim);

                // treat other enumerables as if they want arrays
                if (ArrayInterfaces.Contains(propertyType) || isEnumerable)
                    return BuildArrayFromHeaders(propertyType, headers, delim);

                if (ListInterfaces.Contains(propertyType))
                    return BuildListFromHeaders(propertyType, headers, delim);

                // interface not supported
                return null;
            }

            // handle array types
            if (propertyType.IsArray)
                return BuildArrayFromHeaders(propertyType, headers, delim);

            // anything not generic isn't valid past here
            if (!propertyType.IsGenericType)
                return null;

            // check if list
            var genericType = propertyType.GetGenericTypeDefinition();
            if (genericType == typeof(List<>))
                return BuildListFromHeaders(propertyType, headers, delim);

            // check if dictionary
            if (genericType == typeof(Dictionary<,>))
                return BuildDictionaryFromHeaders(propertyType, headers, delim);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind property model of type {propertyType.Name} to header array.");
        }

        return null;
    }

    private static object? BuildArrayFromHeaders(Type propertyType, IEnumerable<string> headers, string delim)
    {
        try
        {
            if (propertyType.IsAssignableFrom(SupportedBindings.Array))
                return headers.ToArray();

            if (propertyType.IsAssignableFrom(SupportedBindings.ArrayTuple))
            {
                var headerLookup = new List<(string, string)>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    headerLookup.Add((headerKey, headerValue));
                }

                return headerLookup.ToArray();
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ArrayTupleWithArray))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithTuples = new List<(string, string[])>();
                foreach (var kvp in headerLookup)
                    headerLookupWithTuples.Add((kvp.Key, kvp.Value.ToArray()));

                return headerLookupWithTuples.ToArray();
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ArrayTupleWithList))
            {
                var headerLookup = new Dictionary<string, (string, List<string>)>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Item2.Add(headerValue);
                    else
                        headerLookup[headerKey] = (headerKey, new List<string>() { headerValue });
                }

                return headerLookup.Values.ToArray();
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ArrayTupleWithEnumerable))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithTuples = new List<(string, IEnumerable<string>)>();
                foreach (var kvp in headerLookup)
                    headerLookupWithTuples.Add((kvp.Key, kvp.Value.ToArray()));

                return headerLookupWithTuples.ToArray();
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind array property model of type {propertyType.Name} to header array.");
        }

        return null;
    }

    private static object? BuildListFromHeaders(Type propertyType, IEnumerable<string> headers, string delim)
    {
        try
        {
            if (propertyType.IsAssignableFrom(SupportedBindings.List))
                return headers.ToList();

            if (propertyType.IsAssignableFrom(SupportedBindings.ListTuple))
            {
                var headerLookup = new List<(string, string)>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    headerLookup.Add((headerKey, headerValue));
                }

                return headerLookup;
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ListTupleWithList))
            {
                var headerLookup = new Dictionary<string, (string, List<string>)>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Item2.Add(headerValue);
                    else
                        headerLookup[headerKey] = (headerKey, new List<string>() { headerValue });
                }

                return headerLookup.Values.ToList();
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ListTupleWithArray))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithTuples = new List<(string, string[])>();
                foreach (var kvp in headerLookup)
                    headerLookupWithTuples.Add((kvp.Key, kvp.Value.ToArray()));

                return headerLookupWithTuples;
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.ListTupleWithEnumerable))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithTuples = new List<(string, IEnumerable<string>)>();
                foreach (var kvp in headerLookup)
                    headerLookupWithTuples.Add((kvp.Key, kvp.Value.ToArray()));

                return headerLookupWithTuples;
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind list property model of type {propertyType.Name} to header array.");
        }

        return null;
    }

    private static object? BuildDictionaryFromHeaders(Type propertyType, IEnumerable<string> headers, string delim)
    {
        try
        {
            if (propertyType.IsAssignableFrom(SupportedBindings.Dictionary))
            {
                var headerLookup = new Dictionary<string, string>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    headerLookup[headerKey] = headerValue;
                }

                return headerLookup;
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.DictionaryWithList))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                return headerLookup;
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.DictionaryWithArray))
            {
                var headerLookup = new Dictionary<string, List<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        existingList.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithArrays = new Dictionary<string, string[]>();
                foreach (var kvp in headerLookup)
                    headerLookupWithArrays[kvp.Key] = kvp.Value.ToArray();

                return headerLookupWithArrays;
            }

            if (propertyType.IsAssignableFrom(SupportedBindings.DictionaryWithEnumerable))
            {
                var headerLookup = new Dictionary<string, IEnumerable<string>>();
                foreach (var headerString in headers)
                {
                    if (string.IsNullOrWhiteSpace(headerString))
                        continue;

                    var delimIndex = headerString.IndexOf(delim);
                    if (delimIndex < 1 || delimIndex == headerString.Length - delim.Length)
                        continue;

                    var headerKey = headerString[..delimIndex];
                    var headerValue = headerString.Substring(headerKey.Length + delim.Length, headerString.Length - headerKey.Length - delim.Length);

                    if (headerLookup.TryGetValue(headerKey, out var existingList))
                        (existingList as List<string>)?.Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                return headerLookup;
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind dictionary property model of type {propertyType.Name} to header array.");
        }

        return null;
    }
}
