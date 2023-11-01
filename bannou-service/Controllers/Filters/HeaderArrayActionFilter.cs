using Google.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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
            var serviceRequestParamsKVP = context.ActionArguments.Where(
                t => t.Value != null &&
                t.Value.GetType().IsAssignableTo(typeof(ServiceRequest)));

            if (!serviceRequestParamsKVP.Any())
            {
                Program.Logger.Log(LogLevel.Warning, $"No service request models found for action.");
                return;
            }

            foreach (var serviceRequestParamKVP in serviceRequestParamsKVP)
            {
                var parameterName = serviceRequestParamKVP.Key;
                var requestModel = serviceRequestParamKVP.Value as ServiceRequest;

                if (requestModel == null)
                {
                    Program.Logger.Log(LogLevel.Warning, $"Parameter {parameterName} in request model {requestModel?.GetType().Name} is null / missing.");
                    continue;
                }

                var modelValidationState = context.ModelState[parameterName]?.ValidationState;
                if (modelValidationState == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
                {
                    Program.Logger.Log(LogLevel.Error, $"Model state {modelValidationState} not valid for param {parameterName} in request model {requestModel?.GetType().Name}. " +
                        $"Cannot transfer headers to request model.");

                    continue;
                }

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
                    var headersToSet = ServiceMessage.SetHeaderArrayPropertyToHeaders(propertyInfo, propertyValue, headerAttr);
                    foreach (var header in headersToSet)
                    {
                        Program.Logger.Log(LogLevel.Warning, $"Setting header {header.Item1} to value {JArray.FromObject(header.Item2).ToString(Formatting.None)}.");
                        context.HttpContext.Response.Headers.Add(header.Item1, header.Item2);
                    }

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
                {
                    Program.Logger.Log(LogLevel.Warning, $"Removing header property {propName} value while message payload is serialized.");
                    objectResult.Value.GetType().GetProperty(propName)?.SetValue(objectResult.Value, null);
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, "Exception thrown setting header array property values to header strings.");
            }
        }
    }
}
