using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Binders;

public class HeaderArrayModelBinder : IModelBinder
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

    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        await Task.CompletedTask;
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var propertyAttr = bindingContext.ModelType.GetCustomAttribute<HeaderArrayAttribute>();
        if (propertyAttr == null)
            return;

        var headerName = bindingContext.BinderModelName ?? bindingContext.ModelName;
        var headerStrings = bindingContext.HttpContext.Request.Headers[headerName];

        bindingContext.Result = BindPropertyToHeaderArray(bindingContext.ModelType, headerStrings, propertyAttr);
    }

    public static ModelBindingResult BindPropertyToHeaderArray(Type propertyType, IEnumerable<string> headers, HeaderArrayAttribute propertyAttr)
    {
        if (!headers.Any())
            return ModelBindingResult.Success(null);

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
                    return BindDictionaryProperty(propertyType, headers, delim);

                // treat other enumerables as if they want arrays
                if (ArrayInterfaces.Contains(propertyType) || isEnumerable)
                    return BindArrayProperty(propertyType, headers, delim);

                if (ListInterfaces.Contains(propertyType))
                    return BindListProperty(propertyType, headers, delim);

                // interface not supported
                return ModelBindingResult.Failed();
            }

            // handle array types
            if (propertyType.IsArray)
                return BindArrayProperty(propertyType, headers, delim);

            // anything not generic isn't valid past here
            if (!propertyType.IsGenericType)
                return ModelBindingResult.Failed();

            // check if list
            var genericType = propertyType.GetGenericTypeDefinition();
            if (genericType == typeof(List<>))
                return BindListProperty(propertyType, headers, delim);

            // check if dictionary
            if (genericType == typeof(Dictionary<,>))
                return BindDictionaryProperty(propertyType, headers, delim);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    }

    private static ModelBindingResult BindArrayProperty(Type propertyType, IEnumerable<string> headers, string delim)
    {
        try
        {
            if (propertyType.IsAssignableFrom(SupportedBindings.Array))
                return ModelBindingResult.Success(headers.ToArray());

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

                return ModelBindingResult.Success(headerLookup.ToArray());
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

                return ModelBindingResult.Success(headerLookupWithTuples.ToArray());
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

                return ModelBindingResult.Success(headerLookup.Values.ToArray());
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

                return ModelBindingResult.Success(headerLookupWithTuples.ToArray());
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind array property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    }

    private static ModelBindingResult BindListProperty(Type propertyType, IEnumerable<string> headers, string delim)
    {
        try
        {
            if (propertyType.IsAssignableFrom(SupportedBindings.List))
                return ModelBindingResult.Success(headers.ToList());

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

                return ModelBindingResult.Success(headerLookup);
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

                return ModelBindingResult.Success(headerLookup.Values.ToList());
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

                return ModelBindingResult.Success(headerLookupWithTuples);
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

                return ModelBindingResult.Success(headerLookupWithTuples);
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind list property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    }

    private static ModelBindingResult BindDictionaryProperty(Type propertyType, IEnumerable<string> headers, string delim)
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

                return ModelBindingResult.Success(headerLookup);
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

                return ModelBindingResult.Success(headerLookup);
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

                return ModelBindingResult.Success(headerLookupWithArrays);
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

                return ModelBindingResult.Success(headerLookup);
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind dictionary property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    }
}
