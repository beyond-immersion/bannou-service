using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Controllers;
using System.Reflection;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Validates that each service's generated controller correctly implements
/// <see cref="IBannouController"/> (via the generated I{Service}Controller interface)
/// and has the <see cref="BannouControllerAttribute"/> with the correct service interface type.
/// Catches Controller.liquid template regressions.
/// </summary>
public static class ControllerValidator
{
    /// <summary>
    /// Finds all controller-related violations for a given service type.
    /// Returns an empty array if the controller is correctly generated.
    /// </summary>
    /// <param name="serviceType">The [BannouService]-attributed service type.</param>
    /// <returns>List of violation descriptions, or empty if valid.</returns>
    public static string[] GetControllerViolations(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var attr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (attr == null)
            return [];

        var interfaceType = attr.InterfaceType;
        if (interfaceType == null)
            return [];

        var violations = new List<string>();
        var assembly = serviceType.Assembly;

        // Find types with [BannouController(typeof(I{Service}Service))].
        // This may be the concrete controller class (typical pattern) or an abstract
        // base class like AuthControllerBase / ConnectControllerBase.
        var attributedTypes = assembly.GetTypes()
            .Where(t => t.IsClass)
            .Where(t => t.GetCustomAttributes<BannouControllerAttribute>()
                .Any(a => a.InterfaceType == interfaceType))
            .ToArray();

        if (attributedTypes.Length == 0)
        {
            violations.Add($"No controller class found with [BannouController(typeof({interfaceType.Name}))]");
            return violations.ToArray();
        }

        // Determine the concrete controller types to validate.
        // If the attributed type is abstract (e.g., AuthControllerBase), find its
        // concrete subclass in the same assembly.
        var controllerTypes = new List<Type>();
        foreach (var t in attributedTypes)
        {
            if (t.IsAbstract)
            {
                var concreteSubclass = assembly.GetTypes()
                    .FirstOrDefault(sub => sub.IsClass && !sub.IsAbstract && t.IsAssignableFrom(sub));
                if (concreteSubclass != null)
                    controllerTypes.Add(concreteSubclass);
                else
                    violations.Add($"{t.Name} is abstract but has no concrete subclass in the assembly");
            }
            else
            {
                controllerTypes.Add(t);
            }
        }

        foreach (var controllerType in controllerTypes)
        {
            // Check that the controller implements IBannouController (transitively via I{Service}Controller)
            if (!typeof(IBannouController).IsAssignableFrom(controllerType))
            {
                violations.Add($"{controllerType.Name} does not implement IBannouController");
            }

            // Check that an I{Service}Controller interface exists and extends IBannouController
            var controllerInterfaces = controllerType.GetInterfaces()
                .Where(i => i.Name.EndsWith("Controller", StringComparison.Ordinal)
                            && i != typeof(IBannouController))
                .ToArray();

            if (controllerInterfaces.Length == 0)
            {
                violations.Add($"{controllerType.Name} does not implement a service-specific I*Controller interface");
            }
            else
            {
                foreach (var ci in controllerInterfaces)
                {
                    if (!typeof(IBannouController).IsAssignableFrom(ci))
                    {
                        violations.Add($"{ci.Name} does not extend IBannouController");
                    }
                }
            }
        }

        return violations.ToArray();
    }
}
