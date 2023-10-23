using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

public class HeaderArrayModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        await Task.CompletedTask;
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var headerName = bindingContext.BinderModelName ?? bindingContext.ModelName;
        if (string.IsNullOrWhiteSpace(headerName))
            return;

        var headerStrings = bindingContext.HttpContext.Request.Headers[headerName];
        if (headerStrings.Count == 0)
            return;

        var propertyAttr = bindingContext.ModelType.GetCustomAttribute<HeaderArrayAttribute>();
        if (propertyAttr == null)
            return;

        bindingContext.Result = BindPropertyToHeaderArray(bindingContext.ModelType, headerStrings, propertyAttr);
    }

    public static ModelBindingResult BindPropertyToHeaderArray(Type propertyType, IEnumerable<string> headers, HeaderArrayAttribute propertyAttr)
    {
        if (!headers.Any())
            return ModelBindingResult.Failed();

        try
        {
            var delim = "__";
            if (!string.IsNullOrWhiteSpace(propertyAttr.Delimeter))
                delim = propertyAttr.Delimeter;

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<KeyValuePair<string, string[]>>)) ||
                propertyType.IsAssignableFrom(typeof(Dictionary<string, string[]>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<KeyValuePair<string, List<string>>>)) ||
                propertyType.IsAssignableFrom(typeof(Dictionary<string, List<string>>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<KeyValuePair<string, IEnumerable<string>>>)) ||
                propertyType.IsAssignableFrom(typeof(Dictionary<string, IEnumerable<string>>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<KeyValuePair<string, string>>)) ||
                propertyType.IsAssignableFrom(typeof(Dictionary<string, string>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<(string, IEnumerable<string>)>)) ||
                propertyType.IsAssignableFrom(typeof((string, IEnumerable<string>)[])))
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

            if (propertyType.IsAssignableFrom(typeof(List<(string, IEnumerable<string>)>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<(string, string[])>)) ||
                propertyType.IsAssignableFrom(typeof((string, string[])[])))
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

            if (propertyType.IsAssignableFrom(typeof(List<(string, string[])>)))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<(string, List<string>)>)) ||
                propertyType.IsAssignableFrom(typeof(List<(string, List<string>)>)))
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

            if (propertyType.IsAssignableFrom(typeof((string, List<string>)[])))
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

            if (propertyType.IsAssignableFrom(typeof(IEnumerable<(string, string)>)) ||
                propertyType.IsAssignableFrom(typeof(List<(string, string)>)))
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

            if (propertyType.IsAssignableFrom(typeof((string, string)[])))
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

            if (propertyType.IsAssignableFrom(typeof(List<string>)))
                return ModelBindingResult.Success(headers.ToList());

            if (propertyType.IsAssignableFrom(typeof(string[])))
                return ModelBindingResult.Success(headers.ToArray());
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    }
}
