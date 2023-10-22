using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

public class HeaderArrayActionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { }
    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult && objectResult?.Value != null)
        {
            var propertiesToRemove = new List<string>();
            foreach (var propertyInfo in objectResult.Value.GetType().GetProperties())
            {
                var headerAttr = propertyInfo.GetCustomAttribute<ToHeaderArrayAttribute>();
                if (headerAttr != null)
                {
                    var headersToSet = PropertyValueToHeaderArray(propertyInfo, objectResult.Value, headerAttr);
                    foreach (var header in headersToSet)
                        context.HttpContext.Request.Headers.Add(header.Item1, header.Item2);

                    propertiesToRemove.Add(propertyInfo.Name);
                }
            }

            foreach (var propName in propertiesToRemove)
                objectResult.Value.GetType().GetProperty(propName)?.SetValue(objectResult.Value, null);
        }
    }

    public static (string, string[])[] PropertyValueToHeaderArray(PropertyInfo propertyInfo, object obj, ToHeaderArrayAttribute headerAttr)
    {
        var headersToSet = new List<(string, string[])>();

        try
        {
            var headerKVPs = new List<(string, string?)>();
            switch (obj)
            {
                case IEnumerable<KeyValuePair<string, string[]>> realPropVal:
                    foreach (var kvp in realPropVal)
                        foreach (var stringVal in kvp.Value)
                            headerKVPs.Add((kvp.Key, stringVal));

                    break;

                case IEnumerable<KeyValuePair<string, List<string>>> realPropVal:
                    foreach (var kvp in realPropVal)
                        foreach (var stringVal in kvp.Value)
                            headerKVPs.Add((kvp.Key, stringVal));

                    break;

                case IEnumerable<KeyValuePair<string, string>> realPropVal:
                    foreach (var kvp in realPropVal)
                        headerKVPs.Add((kvp.Key, kvp.Value));

                    break;

                case IEnumerable<(string, string)> realPropVal:
                    foreach (var kvp in realPropVal)
                        headerKVPs.Add((kvp.Item1, kvp.Item2));

                    break;

                case IEnumerable<string> realPropVal:
                    foreach (var stringVal in realPropVal)
                        headerKVPs.Add((stringVal, null));

                    break;

                default:
                    break;
            }

            if (headerKVPs.Count == 0)
                return headersToSet.ToArray();

            var delin = "__";
            if (!string.IsNullOrWhiteSpace(headerAttr.Delineator))
                delin = headerAttr.Delineator;

            var headerName = headerAttr.Name ?? propertyInfo.Name;
            if (headerKVPs.Count > 1 && !headerName.EndsWith("[]"))
                headerName += "[]";

            var headerValues = new List<string>();
            foreach (var headerKVP in headerKVPs)
            {
                var headerValue = headerKVP.Item1;
                if (!string.IsNullOrWhiteSpace(headerKVP.Item2))
                    headerValue = headerValue + delin + headerKVP.Item2;

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
