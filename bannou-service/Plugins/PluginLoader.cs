using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Responsible for discovering, loading, and managing Bannou service plugins.
/// Centrally handles the complete service lifecycle: assembly loading -> type registration ->
/// service resolution -> initialization -> startup -> running.
/// </summary>
/// <remarks>
/// Split into partial classes by responsibility:
/// <list type="bullet">
///   <item>PluginLoader.cs — Fields, properties, constructor, accessors</item>
///   <item>PluginLoader.Discovery.cs — Assembly scanning, plugin loading, service enablement</item>
///   <item>PluginLoader.Registration.cs — Type discovery and DI container registration</item>
///   <item>PluginLoader.Lifecycle.cs — Service resolution, initialization, startup, shutdown</item>
///   <item>PluginLoader.Validation.cs — Hierarchy validation, variable provider validation</item>
/// </list>
/// </remarks>
public partial class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    /// <summary>
    /// Required infrastructure plugins that MUST be loaded and enabled.
    /// These plugins provide core functionality (messaging, state, mesh) and
    /// cannot be disabled. Startup will fail if any of these plugins fail to load or initialize.
    /// </summary>
    private static readonly HashSet<string> RequiredInfrastructurePlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "state",      // lib-state: IStateStore for state management (MUST load first)
        "messaging",  // lib-messaging: IMessageBus for pub/sub events (depends on state)
        "mesh"        // lib-mesh: IMeshInvocationClient for service-to-service calls (depends on messaging)
    };

    /// <summary>
    /// Loading priority for infrastructure plugins. Lower values load first.
    /// This ensures dependencies are available before dependent plugins load.
    /// </summary>
    private static readonly Dictionary<string, int> InfrastructureLoadOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "telemetry", -1 }, // First: telemetry provides ITelemetryProvider for instrumentation
        { "state", 0 },      // Second: state management foundation
        { "messaging", 1 },  // Third: messaging depends on state being available
        { "mesh", 2 }        // Fourth: mesh may depend on messaging for events
    };

    // All discovered plugins (enabled and disabled)
    private readonly List<IBannouPlugin> _allPlugins = new();

    // Only enabled plugins that will have services started
    private readonly List<IBannouPlugin> _enabledPlugins = new();

    // All loaded assemblies (needed for client types even from disabled plugins)
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

    // Resolved service instances for enabled plugins only
    private readonly Dictionary<string, IBannouService> _resolvedServices = new();

    // WebApplication reference for service startup (stored during ConfigureWebPipeline)
    private WebApplication? _webApp;

    // Types discovered for registration in DI
    private readonly List<Type> _clientTypesToRegister = new();
    private readonly List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)> _serviceTypesToRegister = new();
    private readonly List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)> _helperServiceTypesToRegister = new();
    private readonly List<(Type configurationType, ServiceLifetime lifetime)> _configurationTypesToRegister = new();

    // Static set of valid environment variable prefixes for forwarding to deployed containers
    // Populated during plugin discovery and accessible by OrchestratorService
    private static readonly HashSet<string> _validEnvironmentPrefixes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valid environment variable prefixes based on discovered plugins.
    /// Used by OrchestratorService to filter which ENV vars to forward to deployed containers.
    /// </summary>
    public static IReadOnlySet<string> ValidEnvironmentPrefixes => _validEnvironmentPrefixes;

    /// <inheritdoc/>
    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get all discovered plugins (enabled and disabled).
    /// </summary>
    public IReadOnlyList<IBannouPlugin> AllPlugins => _allPlugins.AsReadOnly();

    /// <summary>
    /// Get only enabled plugins that will have services started.
    /// </summary>
    public IReadOnlyList<IBannouPlugin> EnabledPlugins => _enabledPlugins.AsReadOnly();

    /// <summary>
    /// Get a centrally resolved service for a plugin.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <returns>Resolved service instance or null if not found</returns>
    public IBannouService? GetResolvedService(string pluginName)
    {
        return _resolvedServices.GetValueOrDefault(pluginName);
    }


    /// <summary>
    /// Get assemblies for ALL loaded plugins (enabled and disabled).
    /// Used for client type registration where disabled plugin client types are needed
    /// for inter-service communication.
    /// </summary>
    public IEnumerable<Assembly> GetAllPluginAssemblies()
    {
        var assemblies = new List<Assembly>();
        var loadedPluginNames = new HashSet<string>();

        // Get all discovered plugin names (enabled and disabled)
        foreach (var plugin in _allPlugins)
        {
            loadedPluginNames.Add(plugin.PluginName.ToLower());
        }

        // Also include plugin names from loaded assemblies (even if plugin instantiation failed)
        foreach (var (pluginName, assembly) in _loadedAssemblies)
        {
            loadedPluginNames.Add(pluginName.ToLower());
        }

        _logger.LogDebug("Available plugin assemblies (all): {PluginNames}", string.Join(", ", loadedPluginNames));

        // Return all loaded plugin assemblies - they contain controllers and clients
        foreach (var (pluginName, assembly) in _loadedAssemblies)
        {
            if (!assemblies.Contains(assembly))
            {
                _logger.LogDebug("Adding plugin assembly (all): {AssemblyName}", assembly.GetName().Name);
                assemblies.Add(assembly);
            }
        }

        _logger.LogInformation("Found {Count} plugin assemblies (all) for client registration", assemblies.Count);

        return assemblies;
    }

    /// <summary>
    /// Get assemblies containing controllers for ENABLED plugins only.
    /// Controllers from disabled plugins should not be registered to avoid DI failures
    /// when their service dependencies are not registered.
    /// </summary>
    public IEnumerable<Assembly> GetControllerAssemblies()
    {
        var assemblies = new List<Assembly>();

        _logger.LogDebug("Enabled plugins for controller registration: {PluginNames}",
            string.Join(", ", _enabledPlugins.Select(p => p.PluginName)));

        foreach (var plugin in _enabledPlugins)
        {
            var assembly = _loadedAssemblies.GetValueOrDefault(plugin.PluginName);
            if (assembly != null && !assemblies.Contains(assembly))
            {
                _logger.LogDebug("Adding ENABLED plugin assembly for controllers: {AssemblyName}",
                    assembly.GetName().Name);
                assemblies.Add(assembly);
            }
        }

        _logger.LogInformation("Found {Count} ENABLED plugin assemblies for controller registration " +
            "(excluded {DisabledCount} disabled plugin assemblies)",
            assemblies.Count, _allPlugins.Count - _enabledPlugins.Count);

        return assemblies;
    }

    /// <summary>
    /// Check if a plugin is a required infrastructure plugin.
    /// </summary>
    /// <param name="pluginName">Name of the plugin to check</param>
    /// <returns>True if the plugin is required infrastructure</returns>
    public static bool IsRequiredInfrastructure(string pluginName)
        => RequiredInfrastructurePlugins.Contains(pluginName);
}
