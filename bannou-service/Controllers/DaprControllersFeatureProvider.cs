using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

internal class DaprControllersFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        var controllersToRemove = new List<TypeInfo>();
        foreach (var controller in feature.Controllers)
        {
            Program.Logger.Log(LogLevel.Warning, $"Controller type {controller.FullName} being added...");

            var attribute = controller.GetCustomAttributes<DaprControllerAttribute>().FirstOrDefault();
            if (attribute == null)
                continue;

            if (attribute.InterfaceType == null)
                continue;

            if (!IDaprService.EnabledServices.Any(t => t.Item1 == attribute.InterfaceType))
                controllersToRemove.Add(controller);
        }

        foreach (var controller in controllersToRemove)
            feature.Controllers.Remove(controller);
    }
}
