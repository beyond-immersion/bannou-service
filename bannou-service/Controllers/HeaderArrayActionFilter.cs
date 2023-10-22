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
            foreach (var property in objectResult.Value.GetType().GetProperties())
            {
                var headerAttr = property.GetCustomAttribute<ToHeaderArrayAttribute>();
                if (headerAttr != null)
                {
                    if (property.PropertyType.IsAssignableFrom(typeof(Dictionary<string, string[]>)))
                    {

                    }

                    var value = property.GetValue(objectResult.Value)?.ToString();
                    if (value != null)
                        context.HttpContext.Response.Headers[property.Name] = value;

                    propertiesToRemove.Add(property.Name);
                }
            }

            foreach (var propName in propertiesToRemove)
                objectResult.Value.GetType().GetProperty(propName)?.SetValue(objectResult.Value, null);
        }
    }
}
