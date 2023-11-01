using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Filters;

public class HeaderArrayResultFilter : IResultFilter
{
    private const string HEADER_ARRAY_LIST = "HEADER_ARRAY_PROPERTIES";
    private const string HEADER_ARRAY_PREFIX = "HEADER_ARRAY:";
    private const string PROPERTYINFO_PREFIX = "PROPERTY_INFO:";

    public void OnResultExecuting(ResultExecutingContext context) { }
    public void OnResultExecuted(ResultExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult && objectResult?.Value != null)
        {
            try
            {
                var headerPropertyNames = GetCachedHeaderPropertyNames(context);
                if (headerPropertyNames == null)
                    return;

                foreach (var propertyName in headerPropertyNames)
                {
                    var propertyInfo = GetCachedPropertyInfo(context, propertyName);
                    var propertyValue = GetCachedPropertyValue(context, propertyName);
                    if (propertyInfo == null || propertyValue == null)
                        continue;

                    propertyInfo.SetValue(objectResult.Value, propertyValue);
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, "Exception thrown re-assigning the header array property values to the request after completing.");
            }
        }
    }

    private static string[]? GetCachedHeaderPropertyNames(ResultExecutedContext context)
    {
        var key = HEADER_ARRAY_LIST;
        var propertyNames = (string[]?)context.HttpContext.Items[key];

        return propertyNames;
    }

    private static PropertyInfo? GetCachedPropertyInfo(ResultExecutedContext context, string propertyName)
    {
        var key = PROPERTYINFO_PREFIX + propertyName;
        var propertyInfo = (PropertyInfo?)context.HttpContext.Items[key];

        return propertyInfo;
    }

    private static object? GetCachedPropertyValue(ResultExecutedContext context, string propertyName)
    {
        var key = HEADER_ARRAY_PREFIX + propertyName;
        var propertyValue = context.HttpContext.Items[key];

        return propertyValue;
    }
}
