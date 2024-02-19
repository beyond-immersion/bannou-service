using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers.Filters;

public class HeaderArrayActionFilter : IActionFilter
{
    private const string HEADER_ARRAY_LIST = "HEADER_ARRAY_PROPERTIES";
    private const string HEADER_ARRAY_PREFIX = "HEADER_ARRAY:";
    private const string PROPERTYINFO_PREFIX = "PROPERTY_INFO:";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        try
        {
            if (context.HttpContext.Request.Headers == null || !context.HttpContext.Request.Headers.Any())
                return;

            var serviceRequestParamsKVP = context.ActionArguments.Where(
                t => t.Value != null &&
                t.Value.GetType().IsAssignableTo(typeof(ApiRequest)));

            if (!serviceRequestParamsKVP.Any())
                return;

            foreach (var serviceRequestParamKVP in serviceRequestParamsKVP)
            {
                var parameterName = serviceRequestParamKVP.Key;
                var requestModel = serviceRequestParamKVP.Value as ApiRequest;
                if (requestModel == null)
                    continue;

                var modelValidationState = context.ModelState[parameterName]?.ValidationState;
                if (modelValidationState == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
                    continue;

                Dictionary<string, IEnumerable<string>> headerLookup = new();
                foreach (var headerKVP in context.HttpContext.Request.Headers)
                    if (!string.IsNullOrWhiteSpace(headerKVP.Key) && headerKVP.Value.Any())
                        headerLookup[headerKVP.Key] = headerKVP.Value;

                requestModel.SetHeadersToProperties(headerLookup);
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "Exception thrown setting header strings to property values.");
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult && objectResult?.Value != null)
        {
            try
            {
                var headerPropertiesWithValues = new List<string>();
                foreach (var propertyInfo in objectResult.Value.GetType().GetProperties())
                {
                    var headerAttr = propertyInfo.GetCustomAttribute<HeaderArrayAttribute>();
                    if (headerAttr == null)
                        continue;

                    var propertyValue = propertyInfo.GetValue(objectResult.Value);
                    if (propertyValue == null)
                        continue;

                    // generate header array from property value
                    var headersToSet = ApiMessage.SetHeaderArrayPropertyToHeaders(propertyInfo, propertyValue, headerAttr);
                    foreach (var header in headersToSet)
                        context.HttpContext.Response.Headers.Append(header.Item1, header.Item2);

                    var propertyName = propertyInfo.Name;
                    headerPropertiesWithValues.Add(propertyName);

                    context.HttpContext.Items[PROPERTYINFO_PREFIX + propertyName] = propertyInfo;
                    context.HttpContext.Items[HEADER_ARRAY_PREFIX + propertyName] = propertyValue;
                }

                if (!headerPropertiesWithValues.Any())
                    return;

                // cache array of all cached property names, for efficiency
                context.HttpContext.Items[HEADER_ARRAY_LIST] = headerPropertiesWithValues.ToArray();
                foreach (var propName in headerPropertiesWithValues)
                    objectResult.Value.GetType().GetProperty(propName)?.SetValue(objectResult.Value, null);
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, "Exception thrown setting header array property values to header strings.");
            }
        }
    }
}
