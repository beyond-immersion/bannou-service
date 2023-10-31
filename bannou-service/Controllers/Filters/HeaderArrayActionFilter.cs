using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
            var serviceRequestParamsKVP = context.ActionArguments.Where(
                t => t.Value != null &&
                t.Value.GetType().IsAssignableTo(typeof(IServiceRequest)));

            if (!serviceRequestParamsKVP.Any())
            {
                Program.Logger.Log(LogLevel.Warning, $"No service request models found for action.");
                return;
            }

            foreach (var serviceRequestParamKVP in serviceRequestParamsKVP)
            {
                var parameterName = serviceRequestParamKVP.Key;
                var requestModel = serviceRequestParamKVP.Value as IServiceRequest;

                if (requestModel == null)
                {
                    Program.Logger.Log(LogLevel.Error, $"Parameter {parameterName} in request model {requestModel?.GetType().Name} is null / missing.");
                    continue;
                }

                var modelValidationState = context.ModelState[parameterName]?.ValidationState;
                if (modelValidationState != Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Valid)
                {
                    Program.Logger.Log(LogLevel.Error, $"Model state {modelValidationState} not valid for param {parameterName} in request model {requestModel?.GetType().Name}. " +
                        $"Cannot transfer headers to request model.");

                    continue;
                }

                foreach (var propertyInfo in requestModel.GetType().GetProperties())
                {
                    var headerAttr = propertyInfo.GetCustomAttributes<HeaderArrayAttribute>(true).FirstOrDefault();
                    if (headerAttr == null)
                        continue;

                    var delim = "__";
                    if (!string.IsNullOrWhiteSpace(headerAttr.Delimeter))
                        delim = headerAttr.Delimeter;

                    var headerName = headerAttr.Name ?? propertyInfo.Name;
                    var headerStrings = context.HttpContext.Request.Headers[headerName];

                    var bindingResult = Binders.HeaderArrayModelBinder.BindPropertyToHeaderArray(propertyInfo.PropertyType, headerStrings, headerAttr);
                    if (!bindingResult.IsModelSet)
                        continue;

                    propertyInfo.SetValue(requestModel, bindingResult.Model);
                }
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
                    var headersToSet = PropertyValueToHeaderArray(propertyInfo, propertyValue, headerAttr);
                    foreach (var header in headersToSet)
                        context.HttpContext.Request.Headers.Add(header.Item1, header.Item2);

                    var propertyName = propertyInfo.Name;
                    headerPropertiesWithValues.Add(propertyName);

                    context.HttpContext.Items[PROPERTYINFO_PREFIX + propertyName] = propertyInfo;
                    context.HttpContext.Items[HEADER_ARRAY_PREFIX + propertyName] = propertyValue;
                }

                if (headerPropertiesWithValues.Count == 0)
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

    public static (string, string[])[] PropertyValueToHeaderArray(PropertyInfo propertyInfo, object value, HeaderArrayAttribute headerAttr)
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
}
