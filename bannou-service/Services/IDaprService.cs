using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Interface to implement for all internal dapr service,
/// which provides the logic for any given set of APIs.
/// 
/// For example, the Inventory service is in charge of
/// any API calls that desire to create/modify inventory
/// data in the game.
/// </summary>
public interface IDaprService
{
    public string GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled()
        => IServiceConfiguration.IsServiceEnabled(GetType());

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredConfiguration(GetType());

    /// <summary>
    /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
    /// </summary>
    public static (Type, DaprServiceAttribute)[] FindAll(bool enabledOnly = false)
    {
        List<(Type, DaprServiceAttribute)> serviceClasses = IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
        if (!serviceClasses.Any())
        {
            Program.Logger.Log(LogLevel.Error, null, $"No dapr services found to instantiate.");
            return Array.Empty<(Type, DaprServiceAttribute)>();
        }

        // prefixes need to be unique, so assign to a tmp hash/dictionary lookup
        var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
        foreach ((Type, DaprServiceAttribute) serviceClass in serviceClasses)
        {
            Type serviceType = serviceClass.Item1;
            DaprServiceAttribute serviceAttr = serviceClass.Item2;

            if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            {
                Program.Logger.Log(LogLevel.Error, null, $"Dapr service attribute attached to a non-service class.",
                    logParams: new JObject() { ["service_type"] = serviceType.Name });
                continue;
            }

            if (enabledOnly && !IServiceConfiguration.IsServiceEnabled(serviceType))
                continue;

            var servicePrefix = ((IDaprService)serviceType).GetName().ToLower();
            if (!serviceLookup.ContainsKey(servicePrefix) || serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                serviceLookup[servicePrefix] = serviceClass;
        }

        return serviceLookup.Values.ToArray();
    }
}
