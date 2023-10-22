using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
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

        var delim = "__";
        if (Program.Configuration.Enable_Custom_Header_Delineation)
        {
            var bindingAttr = bindingContext.ModelType.GetCustomAttribute<FromHeaderArrayAttribute>();
            if (bindingAttr != null && !string.IsNullOrWhiteSpace(bindingAttr.Delineator))
                delim = bindingAttr.Delineator;
        }

        bindingContext.Result = BindPropertyToHeaderArray(bindingContext.ModelType, headerStrings, delim);
    }

    public static ModelBindingResult BindPropertyToHeaderArray(Type propertyType, IEnumerable<string> headers, string delim = "__")
    {
        if (headers.Count() == 0)
            return ModelBindingResult.Failed();

        try
        {
            if (propertyType.IsAssignableFrom(typeof(Dictionary<string, string[]>)))
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

                    if (headerLookup.ContainsKey(headerKey))
                        headerLookup[headerKey].Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                var headerLookupWithArrays = new Dictionary<string, string[]>();
                foreach (var kvp in headerLookup)
                    headerLookupWithArrays[kvp.Key] = kvp.Value.ToArray();

                return ModelBindingResult.Success(headerLookupWithArrays);
            }

            if (propertyType.IsAssignableFrom(typeof(Dictionary<string, List<string>>)))
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

                    if (headerLookup.ContainsKey(headerKey))
                        headerLookup[headerKey].Add(headerValue);
                    else
                        headerLookup[headerKey] = new List<string>() { headerValue };
                }

                return ModelBindingResult.Success(headerLookup);
            }

            if (propertyType.IsAssignableFrom(typeof(Dictionary<string, string>)))
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

            if (propertyType.IsAssignableFrom(typeof(List<(string, string)>)))
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

            return ModelBindingResult.Success(headers.ToArray());
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception has occurred trying to bind property model of type {propertyType.Name} to header array.");
        }

        return ModelBindingResult.Failed();
    } 
}
