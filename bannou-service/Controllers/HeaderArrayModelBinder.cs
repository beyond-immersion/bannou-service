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

        var delim = "__";
        if (Program.Configuration.Enable_Custom_Header_Delineation)
        {
            var bindingAttr = bindingContext.ModelType.GetCustomAttribute<FromHeaderArrayAttribute>();
            if (bindingAttr != null && !string.IsNullOrWhiteSpace(bindingAttr.Delineator))
                delim = bindingAttr.Delineator;
        }

        // handle as dictionary of <string, string[]>
        if (bindingContext.ModelType.IsAssignableFrom(typeof(Dictionary<string, string[]>)))
        {
            var headerLookup = new Dictionary<string, List<string>>();
            foreach (var headerString in headerStrings)
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

            bindingContext.Result = ModelBindingResult.Success(headerLookupWithArrays);
            return;
        }

        // handle as dictionary of <string, List<string>>
        if (bindingContext.ModelType.IsAssignableFrom(typeof(Dictionary<string, List<string>>)))
        {
            var headerLookup = new Dictionary<string, List<string>>();
            foreach (var headerString in headerStrings)
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

            bindingContext.Result = ModelBindingResult.Success(headerLookup);
            return;
        }

        // handle as dictionary of <string, string>
        if (bindingContext.ModelType.IsAssignableFrom(typeof(Dictionary<string, string>)))
        {
            var headerLookup = new Dictionary<string, string>();
            foreach (var headerString in headerStrings)
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

            bindingContext.Result = ModelBindingResult.Success(headerLookup);
            return;
        }

        // handle as array of (string, string) tuples
        if (bindingContext.ModelType.IsAssignableFrom(typeof((string, string)[])))
        {
            var headerLookup = new List<(string, string)>();
            foreach (var headerString in headerStrings)
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

            bindingContext.Result = ModelBindingResult.Success(headerLookup.ToArray());
            return;
        }

        // handle as list of (string, string) tuples
        if (bindingContext.ModelType.IsAssignableFrom(typeof(List<(string, string)>)))
        {
            var headerLookup = new List<(string, string)>();
            foreach (var headerString in headerStrings)
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

            bindingContext.Result = ModelBindingResult.Success(headerLookup);
            return;
        }

        // handle as simple array of strings, if all else fails
        bindingContext.Result = ModelBindingResult.Success(headerStrings.ToArray());
    }
}
