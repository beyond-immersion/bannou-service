using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Service hierarchy validation and variable provider validation.
/// </summary>
public partial class PluginLoader
{
    /// <summary>
    /// Validates that all enabled services follow the service hierarchy rules per SERVICE-HIERARCHY.md.
    /// Services may only depend on services in lower layers (lower ServiceLayer enum values).
    /// </summary>
    /// <returns>True if all services are compliant; false if violations are found.</returns>
    public bool ValidateServiceHierarchies()
    {
        _logger.LogDebug("Validating service hierarchy compliance for {Count} enabled plugins", _enabledPlugins.Count);

        var allViolations = new List<(string ServiceName, string Message)>();

        foreach (var plugin in _enabledPlugins)
        {
            var assembly = _loadedAssemblies.GetValueOrDefault(plugin.PluginName);
            if (assembly == null)
                continue;

            // Find the service type with [BannouService] attribute
            var serviceType = assembly.GetTypes()
                .FirstOrDefault(t =>
                {
                    var attr = t.GetCustomAttribute<BannouServiceAttribute>();
                    return attr != null && attr.Name.Equals(plugin.PluginName, StringComparison.OrdinalIgnoreCase);
                });

            if (serviceType == null)
                continue;

            var violations = GetServiceHierarchyViolations(serviceType);
            allViolations.AddRange(violations);
        }

        if (allViolations.Count > 0)
        {
            _logger.LogError(
                "SERVICE HIERARCHY VIOLATIONS DETECTED! The following services depend on higher-layer services:");

            foreach (var (serviceName, message) in allViolations)
            {
                _logger.LogError("  {ServiceName}: {Message}", serviceName, message);
            }

            _logger.LogError(
                "See SERVICE-HIERARCHY.md for dependency rules. Fix these violations before proceeding.");

            return false;
        }

        _logger.LogDebug("Service hierarchy validation passed for all {Count} enabled plugins", _enabledPlugins.Count);
        return true;
    }

    /// <summary>
    /// Gets hierarchy violations for a single service type.
    /// </summary>
    private List<(string ServiceName, string Message)> GetServiceHierarchyViolations(Type serviceType)
    {
        var violations = new List<(string ServiceName, string Message)>();

        var bannouServiceAttr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
        if (bannouServiceAttr == null)
            return violations;

        var serviceLayer = bannouServiceAttr.Layer;
        var serviceName = bannouServiceAttr.Name;

        // Get constructor parameters
        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
            return violations;

        var ctor = constructors[0];
        var parameters = ctor.GetParameters();

        foreach (var param in parameters)
        {
            var paramType = param.ParameterType;

            // Check if this is a service client interface (I*Client)
            if (!paramType.IsInterface || !paramType.Name.StartsWith('I') || !paramType.Name.EndsWith("Client"))
                continue;

            // Extract service name from client type (IAccountClient → account)
            var clientServiceName = ExtractServiceNameFromClientType(paramType.Name);
            if (clientServiceName == null)
                continue;

            // Get the client's service layer from our registry or attribute
            // (only validate known service clients — skip non-service interfaces like IMeshInvocationClient)
            var clientLayer = GetClientServiceLayerIfKnown(clientServiceName);
            if (clientLayer == null)
                continue;

            // Check for violation based on hierarchy rules
            if (IsHierarchyViolation(serviceLayer, clientLayer.Value))
            {
                violations.Add((serviceName,
                    $"Depends on {clientServiceName} ({clientLayer.Value}, L{(int)clientLayer.Value / 100}) " +
                    $"which is higher than {serviceLayer} (L{(int)serviceLayer / 100})"));
            }
        }

        return violations;
    }

    /// <summary>
    /// Extracts service name from client interface name (IAccountClient → account).
    /// </summary>
    private static string? ExtractServiceNameFromClientType(string clientTypeName)
    {
        if (!clientTypeName.StartsWith('I') || !clientTypeName.EndsWith("Client"))
            return null;

        var servicePascal = clientTypeName[1..^6]; // Remove 'I' and 'Client'

        // Convert PascalCase to kebab-case (GameSession → game-session)
        return System.Text.RegularExpressions.Regex.Replace(servicePascal, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
    }

    /// <summary>
    /// Lazily-built cache mapping service names to their layers, discovered via reflection
    /// from [BannouService] attributes on all loaded plugin assemblies. Returns null for
    /// unknown service names so that non-service interfaces (e.g., IMeshInvocationClient)
    /// are skipped during hierarchy validation.
    /// </summary>
    private Dictionary<string, ServiceLayer>? _serviceLayerCache;

    /// <summary>
    /// Gets the service layer for a client's service name, returning null if the service
    /// is not registered. Discovers layers via reflection from [BannouService] attributes —
    /// no hardcoded registry.
    /// </summary>
    private ServiceLayer? GetClientServiceLayerIfKnown(string serviceName)
    {
        _serviceLayerCache ??= BuildServiceLayerCache();

        if (_serviceLayerCache.TryGetValue(serviceName, out var layer))
            return layer;

        return null;
    }

    /// <summary>
    /// Scans all loaded plugin assemblies for types with [BannouService] attributes
    /// and builds a service name → layer mapping.
    /// </summary>
    private Dictionary<string, ServiceLayer> BuildServiceLayerCache()
    {
        var cache = new Dictionary<string, ServiceLayer>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, assembly) in _loadedAssemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<BannouServiceAttribute>();
                    if (attr == null)
                        continue;

                    // First registration wins (same behavior as test-utilities validator)
                    cache.TryAdd(attr.Name, attr.Layer);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Assembly has types that can't be loaded — skip it
            }
        }

        return cache;
    }

    /// <summary>
    /// Checks if depending on clientLayer from serviceLayer violates the hierarchy.
    /// </summary>
    private static bool IsHierarchyViolation(ServiceLayer serviceLayer, ServiceLayer clientLayer)
    {
        // Extensions (L5) can depend on anything
        if (serviceLayer == ServiceLayer.Extensions)
            return false;

        // AppFeatures (L3) CANNOT depend on GameFoundation (L2) or GameFeatures (L4)
        // They're separate branches in the hierarchy
        if (serviceLayer == ServiceLayer.AppFeatures)
        {
            return clientLayer == ServiceLayer.GameFoundation ||
                    clientLayer == ServiceLayer.GameFeatures ||
                    clientLayer == ServiceLayer.Extensions;
        }

        // For other layers, simply check if client is in a higher layer
        return clientLayer > serviceLayer;
    }


    /// <summary>
    /// Validates that all registered IVariableProviderFactory implementations have ProviderNames
    /// that are defined in VariableProviderDefinitions (generated from schemas/variable-providers.yaml).
    /// Also checks for duplicate provider name registrations.
    /// </summary>
    /// <param name="serviceProvider">The built DI service provider.</param>
    /// <returns>True if all providers are valid; false if violations are found.</returns>
    public bool ValidateVariableProviders(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var factories = scope.ServiceProvider.GetServices<IVariableProviderFactory>().ToList();

        if (factories.Count == 0)
        {
            _logger.LogDebug("No variable provider factories registered, skipping validation");
            return true;
        }

        _logger.LogDebug("Validating {Count} variable provider factory registrations", factories.Count);

        var violations = new List<string>();
        var seenNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var factory in factories)
        {
            var providerName = factory.ProviderName;
            var factoryType = factory.GetType().Name;

            // Check that the provider name is defined in the schema-generated definitions
            if (!VariableProviderDefinitions.Metadata.ContainsKey(providerName))
            {
                violations.Add(
                    $"{factoryType}: ProviderName \"{providerName}\" is not defined in " +
                    "schemas/variable-providers.yaml. Add it to the schema and regenerate.");
            }

            // Check for duplicate provider names
            if (seenNames.TryGetValue(providerName, out var existingFactory))
            {
                violations.Add(
                    $"{factoryType}: Duplicate ProviderName \"{providerName}\" " +
                    $"(already registered by {existingFactory}). " +
                    "Each provider namespace must be unique.");
            }
            else
            {
                seenNames[providerName] = factoryType;
            }
        }

        if (violations.Count > 0)
        {
            _logger.LogError(
                "VARIABLE PROVIDER VIOLATIONS DETECTED! The following provider registrations are invalid:");

            foreach (var violation in violations)
            {
                _logger.LogError("  {Violation}", violation);
            }

            _logger.LogError(
                "Fix provider registrations or update schemas/variable-providers.yaml before proceeding.");

            return false;
        }

        _logger.LogDebug(
            "Variable provider validation passed for {Count} factories", factories.Count);
        return true;
    }
}
