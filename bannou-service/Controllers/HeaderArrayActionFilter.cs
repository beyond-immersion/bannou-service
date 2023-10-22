using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
            foreach (var property in objectResult.Value.GetType().GetProperties())
            {
                var headerAttr = property.GetCustomAttribute<ToHeaderArrayAttribute>();
                if (headerAttr != null)
                {
                    SetPropertyValuesToHeaderArray(context, objectResult, property, headerAttr);
                    propertiesToRemove.Add(property.Name);
                }
            }

            foreach (var propName in propertiesToRemove)
                objectResult.Value.GetType().GetProperty(propName)?.SetValue(objectResult.Value, null);
        }
    }

    private static void SetPropertyValuesToHeaderArray(ActionExecutedContext context, ObjectResult objectResult, PropertyInfo propertyInfo, ToHeaderArrayAttribute headerAttr)
    {
        try
        {
            var propType = propertyInfo.PropertyType;
            var propVal = propertyInfo.GetValue(objectResult.Value);
            if (propVal == null)
                return;

            var headerKVPs = new List<(string, string?)>();
            switch (propVal)
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
                return;

            var delin = "__";
            if (!string.IsNullOrWhiteSpace(headerAttr.Delineator))
                delin = headerAttr.Delineator;

            var headerName = headerAttr.Name ?? propertyInfo.Name;
            if (headerKVPs.Count > 1 && !headerName.EndsWith("[]"))
                headerName += "[]";

            foreach (var headerKVP in headerKVPs)
            {
                var headerValue = headerKVP.Item1;
                if (!string.IsNullOrWhiteSpace(headerKVP.Item2))
                    headerValue = headerValue + delin + headerKVP.Item2;

                context.HttpContext.Response.Headers[headerName] = headerValue;
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"Could not set property values for '{propertyInfo.Name}' of type '{propertyInfo.PropertyType.Name}' to header array.");
        }
    }
}
