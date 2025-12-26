using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers;
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
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    /// <summary>
    /// Required infrastructure plugins that MUST be loaded and enabled.
    /// These plugins provide core functionality (messaging, state, mesh) and
    /// cannot be disabled. Startup will fail if any of these plugins fail to load or initialize.
    /// </summary>
    private static readonly HashSet<string> RequiredInfrastructurePlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "messaging",  // lib-messaging: IMessageBus for pub/sub events
        "state",      // lib-state: IStateStore for state management
        "mesh"        // lib-mesh: IMeshInvocationClient for service-to-service calls
    };

    // All discovered plugins (enabled and disabled)
    private readonly List<IBannouPlugin> _allPlugins = new();

    // Only enabled plugins that will have services started
    private readonly List<IBannouPlugin> _enabledPlugins = new();

    // All loaded assemblies (needed for client types even from disabled plugins)
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();

    // Resolved service instances for enabled plugins only
    [Obsolete]
    private readonly Dictionary<string, IDaprService> _resolvedServices = new();

    // WebApplication reference for service startup (stored during ConfigureApplication)
    private WebApplication? _webApp;

    // Types discovered for registration in DI
    private readonly List<Type> _clientTypesToRegister = new();
    private readonly List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)> _serviceTypesToRegister = new();
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
    /// Discover and load all plugins from the specified directory.
    /// This follows the proper flow: 1) Load assemblies for ALL plugins, 2) Determine which services are enabled,
    /// 3) Register types in DI appropriately (all client types + enabled service types only).
    /// </summary>
    /// <param name="pluginsDirectory">Root plugins directory</param>
    /// <param name="requestedPlugins">List of specific plugins to load, or null for all</param>
    /// <returns>Number of enabled plugins successfully loaded</returns>
    [Obsolete]
    public async Task<int?> DiscoverAndLoadPluginsAsync(string pluginsDirectory, IList<string>? requestedPlugins = null)
    {
        _logger.LogInformation("Discovering plugins in: {PluginsDirectory}", pluginsDirectory);

        if (!Directory.Exists(pluginsDirectory))
        {
            _logger.LogWarning("Plugins directory does not exist: {PluginsDirectory}", pluginsDirectory);
            return 0;
        }

        var pluginDirectories = Directory.GetDirectories(pluginsDirectory);

        // STAGE 1: Load ALL plugin assemblies (enabled and disabled)
        foreach (var pluginDir in pluginDirectories)
        {
            var pluginName = Path.GetFileName(pluginDir);

            // Skip if specific plugins requested and this isn't one of them
            if (requestedPlugins != null && !requestedPlugins.Contains(pluginName))
            {
                _logger.LogDebug("Skipping plugin '{PluginName}' (not in requested list)", pluginName);
                continue;
            }

            try
            {
                var plugin = await LoadPluginFromDirectoryAsync(pluginDir, pluginName);
                if (plugin != null)
                {
                    _allPlugins.Add(plugin);

                    // Determine if this plugin's service should be enabled
                    var serviceName = plugin.PluginName;
                    if (IsServiceEnabled(serviceName))
                    {
                        _enabledPlugins.Add(plugin);
                        _logger.LogDebug("Service {ServiceName} is enabled", serviceName);
                    }
                    else
                    {
                        _logger.LogDebug("Service {ServiceName} is disabled", serviceName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from directory: {PluginDirectory}", pluginDir);
                return null;
            }
        }

        // STAGE 2: Validate required infrastructure plugins are present
        var missingInfrastructure = RequiredInfrastructurePlugins
            .Where(required => !_enabledPlugins.Any(p => p.PluginName.Equals(required, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingInfrastructure.Count > 0)
        {
            var missing = string.Join(", ", missingInfrastructure);
            _logger.LogCritical(
                "STARTUP FAILURE: Required infrastructure plugins are missing: [{MissingPlugins}]. " +
                "These plugins (lib-messaging, lib-state, lib-mesh) are MANDATORY and must be present for the application to function.",
                missing);
            return null; // Indicate fatal failure
        }

        // STAGE 3: Sort enabled plugins so infrastructure loads FIRST
        // This ensures IMessageBus, IStateStore, and IMeshInvocationClient are available
        // before other services try to use them
        var sortedPlugins = _enabledPlugins
            .OrderByDescending(p => RequiredInfrastructurePlugins.Contains(p.PluginName))
            .ThenBy(p => p.PluginName)
            .ToList();
        _enabledPlugins.Clear();
        _enabledPlugins.AddRange(sortedPlugins);

        _logger.LogInformation(
            "Infrastructure plugins validated. Loading order: [{LoadOrder}]",
            string.Join(" -> ", _enabledPlugins.Select(p => p.PluginName)));

        // STAGE 4: Discover types for DI registration from ALL assemblies
        DiscoverTypesForRegistration();

        // STAGE 5: Build valid environment variable prefixes for orchestrator forwarding
        PopulateValidEnvironmentPrefixes();

        var discoveredSummary = string.Join(", ", _allPlugins.Select(p => $"{p.DisplayName} v{p.Version}"));
        var enabledSummary = string.Join(", ", _enabledPlugins.Select(p => $"{p.DisplayName} v{p.Version}"));

        _logger.LogInformation(
            "Plugin discovery complete. Discovered {AllCount}: [{Discovered}]; Enabled {EnabledCount}: [{Enabled}]",
            _allPlugins.Count, discoveredSummary, _enabledPlugins.Count, enabledSummary);

        return _enabledPlugins.Count;
    }

    /// <summary>
    /// Check if a service should be enabled based on SERVICES_ENABLED and {SERVICE}_SERVICE_DISABLED/ENABLED environment variables.
    /// Two modes:
    /// - SERVICES_ENABLED=true: All services enabled by default, use {SERVICE}_SERVICE_DISABLED to disable individual services
    /// - SERVICES_ENABLED=false: All services disabled by default, use {SERVICE}_SERVICE_ENABLED to enable individual services
    ///
    /// IMPORTANT: Required infrastructure plugins (messaging, state, mesh) are ALWAYS enabled.
    /// These cannot be disabled via environment variables as they provide core functionality.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "auth", "accounts")</param>
    /// <returns>True if service should be enabled, false if disabled</returns>
    [Obsolete]
    private bool IsServiceEnabled(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return Program.Configuration.Services_Enabled;

        // Required infrastructure plugins are ALWAYS enabled - they cannot be disabled
        if (RequiredInfrastructurePlugins.Contains(serviceName))
        {
            _logger.LogInformation(
                "Infrastructure plugin '{ServiceName}' is REQUIRED and cannot be disabled",
                serviceName);
            return true;
        }

        // Check SERVICES_ENABLED environment variable directly
        var servicesEnabledEnv = Environment.GetEnvironmentVariable("SERVICES_ENABLED");
        var globalServicesEnabled = string.IsNullOrWhiteSpace(servicesEnabledEnv) ?
            Program.Configuration.Services_Enabled : // Default fallback
            string.Equals(servicesEnabledEnv, "true", StringComparison.OrdinalIgnoreCase);

        // Get service name from DaprServiceAttribute for ENV prefix
        var serviceNameUpper = serviceName.ToUpper();

        if (globalServicesEnabled)
        {
            // Mode 1: Services enabled by default, check for X_SERVICE_DISABLED to disable individual
            var disabledEnv = Environment.GetEnvironmentVariable($"{serviceNameUpper}_SERVICE_DISABLED");
            var isDisabled = string.Equals(disabledEnv, "true", StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug("Service {ServiceName} enabled: {IsEnabled} (SERVICES_ENABLED=true, {ServiceName}_SERVICE_DISABLED={DisabledValue})",
                serviceName, !isDisabled, serviceNameUpper, disabledEnv ?? "null");

            return !isDisabled;
        }
        else
        {
            // Mode 2: Services disabled by default, check for X_SERVICE_ENABLED to enable individual
            var enabledEnv = Environment.GetEnvironmentVariable($"{serviceNameUpper}_SERVICE_ENABLED");
            var isEnabled = string.Equals(enabledEnv, "true", StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug("Service {ServiceName} enabled: {IsEnabled} (SERVICES_ENABLED=false, {ServiceName}_SERVICE_ENABLED={EnabledValue})",
                serviceName, isEnabled, serviceNameUpper, enabledEnv ?? "null");

            return isEnabled;
        }
    }

    /// <summary>
    /// Populate the static set of valid environment variable prefixes based on discovered plugins.
    /// This allows the orchestrator to whitelist which ENVs to forward to deployed containers.
    /// </summary>
    private void PopulateValidEnvironmentPrefixes()
    {
        _validEnvironmentPrefixes.Clear();

        // Only BANNOU_ prefix for shared configuration
        // All other configuration uses plugin-derived {SERVICE}_ prefixes
        _validEnvironmentPrefixes.Add("BANNOU_");

        // Add prefix for each discovered plugin
        // e.g., "auth" → "AUTH_", "game-session" → "GAMESESSION_" and "GAME_SESSION_"
        foreach (var plugin in _allPlugins)
        {
            var pluginName = plugin.PluginName;
            if (string.IsNullOrWhiteSpace(pluginName))
                continue;

            // Primary prefix: uppercase with underscores replaced for hyphens, plus trailing underscore
            // e.g., "game-session" → "GAME_SESSION_"
            var primaryPrefix = pluginName.ToUpperInvariant().Replace('-', '_') + "_";
            _validEnvironmentPrefixes.Add(primaryPrefix);

            // Secondary prefix: hyphens removed (no underscore replacement)
            // e.g., "game-session" → "GAMESESSION_"
            var secondaryPrefix = pluginName.ToUpperInvariant().Replace("-", "") + "_";
            if (primaryPrefix != secondaryPrefix)
            {
                _validEnvironmentPrefixes.Add(secondaryPrefix);
            }
        }

        _logger.LogInformation(
            "Valid environment prefixes for orchestrator forwarding: {Prefixes}",
            string.Join(", ", _validEnvironmentPrefixes.OrderBy(p => p)));
    }

    /// <summary>
    /// Discover types from all loaded assemblies that need to be registered in DI.
    /// This includes ALL client types (even from disabled plugins) and service types from enabled plugins only.
    /// Also scans the host assembly (bannou-service) for centralized client types.
    /// </summary>
    [Obsolete]
    private void DiscoverTypesForRegistration()
    {
        _logger.LogInformation("Discovering types for DI registration from {AssemblyCount} plugin assemblies + host assembly", _loadedAssemblies.Count);

        _clientTypesToRegister.Clear();
        _serviceTypesToRegister.Clear();
        _configurationTypesToRegister.Clear();

        // FIRST: Scan the host assembly (bannou-service) for centralized client types
        // This is necessary because clients are now generated in bannou-service/Generated/Clients/
        var hostAssembly = typeof(PluginLoader).Assembly;
        _logger.LogInformation("Scanning host assembly {AssemblyName} for centralized client types", hostAssembly.GetName().Name);
        DiscoverClientTypes(hostAssembly, "bannou-service-host");

        foreach (var (pluginName, assembly) in _loadedAssemblies)
        {
            try
            {
                // ALWAYS register client types (needed for inter-service dependencies)
                DiscoverClientTypes(assembly, pluginName);

                // Only register service types and configurations for enabled plugins
                if (_enabledPlugins.Any(p => p.PluginName == pluginName))
                {
                    DiscoverServiceTypes(assembly, pluginName);
                    // Note: Configuration discovery happens later to ensure service types are registered first
                }
                else
                {
                    _logger.LogDebug("🚫 Skipping service and configuration type registration for disabled plugin: {PluginName}", pluginName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover types from assembly: {PluginName}", pluginName);
            }
        }

        // PHASE 2: Now that service types are discovered, discover configurations that reference them
        foreach (var (pluginName, assembly) in _loadedAssemblies)
        {
            try
            {
                // Only register configurations for enabled plugins
                if (_enabledPlugins.Any(p => p.PluginName == pluginName))
                {
                    DiscoverConfigurationTypes(assembly, pluginName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover configuration types from assembly: {PluginName}", pluginName);
            }
        }

        _logger.LogInformation("Type discovery complete. {ClientCount} client types, {ServiceCount} service types, {ConfigCount} configuration types",
            _clientTypesToRegister.Count, _serviceTypesToRegister.Count, _configurationTypesToRegister.Count);
    }

    /// <summary>
    /// Discover client types that implement IDaprClient interface.
    /// Pure interface-based discovery - no naming conventions.
    /// </summary>
    private void DiscoverClientTypes(Assembly assembly, string pluginName)
    {
        var clientTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprClient).IsAssignableFrom(t))
            .ToList();

        foreach (var clientType in clientTypes)
        {
            _clientTypesToRegister.Add(clientType);
            _logger.LogDebug("Will register client: {Implementation}", clientType.Name);
        }

        _logger.LogDebug("Discovered {Count} client types in assembly {AssemblyName}",
            clientTypes.Count, assembly.GetName().Name);
    }

    /// <summary>
    /// Discover service types using IDaprService interface and DaprServiceAttribute.
    /// Pure interface/attribute-based discovery from the specific assembly.
    /// </summary>
    [Obsolete]
    private void DiscoverServiceTypes(Assembly assembly, string pluginName)
    {
        var serviceTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprService).IsAssignableFrom(t))
            .ToList();

        foreach (var serviceType in serviceTypes)
        {
            var serviceAttr = serviceType.GetCustomAttribute<DaprServiceAttribute>();
            if (serviceAttr != null)
            {
                var interfaceType = serviceAttr.InterfaceType ?? serviceType;
                var lifetime = serviceAttr.Lifetime;

                _serviceTypesToRegister.Add((interfaceType, serviceType, lifetime));
                _logger.LogDebug("Will register service: {Interface} -> {Implementation} ({Lifetime})",
                    interfaceType.Name, serviceType.Name, lifetime);
            }
            else
            {
                _logger.LogDebug("🚫 Skipping service {ServiceType} - missing [DaprService] attribute",
                    serviceType.Name);
            }
        }

        _logger.LogDebug("Discovered {Count} service types in assembly {AssemblyName}",
            serviceTypes.Count(t => t.GetCustomAttribute<DaprServiceAttribute>() != null), assembly.GetName().Name);
    }

    /// <summary>
    /// Discover configuration types with [ServiceConfiguration] attributes from an assembly.
    /// Configuration lifetimes match their corresponding service lifetimes.
    /// Pure attribute-based discovery - configurations must have ServiceImplementationType specified.
    /// </summary>
    [Obsolete]
    private void DiscoverConfigurationTypes(Assembly assembly, string pluginName)
    {
        _logger.LogInformation("Discovering configuration types in assembly {AssemblyName} for plugin {PluginName}", assembly.GetName().Name, pluginName);

        var configurationTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        _logger.LogInformation("Found {Count} configuration types in assembly {AssemblyName}: {ConfigTypes}",
            configurationTypes.Count, assembly.GetName().Name, string.Join(", ", configurationTypes.Select(t => t.Name)));

        foreach (var configurationType in configurationTypes)
        {
            ServiceConfigurationAttribute? serviceConfigAttr = null;
            Type? serviceType = null;

            try
            {
                // Try to get the ServiceConfiguration attribute
                serviceConfigAttr = configurationType.GetCustomAttribute<ServiceConfigurationAttribute>();
                serviceType = serviceConfigAttr?.ServiceImplementationType;

                if (serviceType != null)
                {
                    _logger.LogDebug("Successfully resolved ServiceConfiguration attribute for {ConfigType} -> {ServiceType}",
                        configurationType.Name, serviceType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to read ServiceConfiguration attribute for {ConfigType}. Error: {Error}",
                    configurationType.Name, ex.Message);
            }

            // If attribute evaluation failed or ServiceImplementationType is null, use naming convention fallback
            if (serviceType == null && configurationType.Name.EndsWith("ServiceConfiguration"))
            {
                var serviceName = configurationType.Name.Replace("ServiceConfiguration", "Service");
                _logger.LogDebug("Using naming convention fallback for {ConfigType} -> {ServiceName}",
                    configurationType.Name, serviceName);

                // Find service by name in the same assembly
                serviceType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == serviceName && typeof(IDaprService).IsAssignableFrom(t));

                if (serviceType != null)
                {
                    _logger.LogDebug("Found service {ServiceType} via naming convention for configuration {ConfigType}",
                        serviceType.Name, configurationType.Name);
                }
            }

            if (serviceType != null)
            {
                // Find the registered service to match its lifetime
                var serviceRegistration = _serviceTypesToRegister
                    .FirstOrDefault(s => s.implementationType == serviceType || s.interfaceType == serviceType);

                if (serviceRegistration.implementationType != null)
                {
                    // Configuration lifetime must be compatible with service lifetime
                    // Singleton services MUST have Singleton configurations (DI constraint)
                    // Scoped services can have Scoped configurations
                    var configLifetime = serviceRegistration.lifetime == ServiceLifetime.Singleton
                        ? ServiceLifetime.Singleton
                        : serviceRegistration.lifetime;

                    _configurationTypesToRegister.Add((configurationType, configLifetime));

                    _logger.LogInformation("Will register configuration: {ConfigType} ({Lifetime}) for service {ServiceType} (service lifetime: {ServiceLifetime})",
                        configurationType.Name, configLifetime, serviceType.Name, serviceRegistration.lifetime);
                }
                else
                {
                    _logger.LogWarning("Configuration {ConfigType} references service {ServiceType} but no matching service registration found. Skipping configuration.", configurationType.Name, serviceType.Name);
                }
            }
            else
            {
                _logger.LogWarning("Skipping configuration {ConfigType} - could not determine associated service via attribute or naming convention",
                    configurationType.Name);
            }
        }

        _logger.LogDebug("Discovered {Count} configuration types in assembly {AssemblyName}",
            _configurationTypesToRegister.Count, assembly.GetName().Name);
    }

    /// <summary>
    /// Register all discovered types in the DI container.
    /// This must be called BEFORE the web application is built.
    /// </summary>
    /// <param name="services">Service collection</param>
    public void ConfigureServices(IServiceCollection services)
    {
        _logger.LogInformation("Centrally registering {ClientCount} client types, {ServiceCount} service types, and {ConfigCount} configuration types",
            _clientTypesToRegister.Count, _serviceTypesToRegister.Count, _configurationTypesToRegister.Count);

        // STAGE 1: Register ALL client types (even from disabled plugins for dependencies)
        RegisterClientTypes(services);

        // STAGE 2: Register service types from ENABLED plugins only
        RegisterServiceTypes(services);

        // STAGE 3: Register configuration types with matching service lifetimes
        RegisterConfigurationTypes(services);

        // STAGE 4: Call plugin ConfigureServices for additional setup (if needed)
        // Note: Plugins should NOT register their main service types or configurations here - that's done centrally above
        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Calling additional ConfigureServices for enabled plugin: {PluginName}", plugin.PluginName);
                plugin.ConfigureServices(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure additional services for plugin: {PluginName}", plugin.PluginName);
                throw; // Re-throw to fail startup if plugin configuration fails
            }
        }

        _logger.LogInformation("Service configuration complete - centralized type registration finished");
    }

    /// <summary>
    /// Final registration pass to override any auto-registered configurations with correct lifetimes.
    /// Called just before WebApplication build to ensure our configuration lifetimes take precedence.
    /// </summary>
    /// <param name="services">Service collection</param>
    public void FinalizeConfigurationRegistrations(IServiceCollection services)
    {
        _logger.LogInformation("Finalizing configuration registrations to override auto-registrations...");

        var finalizedCount = 0;
        foreach (var (configurationType, lifetime) in _configurationTypesToRegister)
        {
            // Remove all existing registrations for this configuration type
            var existingRegistrations = services.Where(s => s.ServiceType == configurationType).ToList();
            foreach (var existing in existingRegistrations)
            {
                _logger.LogWarning("Removing auto-registered configuration: {ConfigType} (Lifetime: {ExistingLifetime})", configurationType.Name, existing.Lifetime);
                services.Remove(existing);
            }

            // Re-add with correct lifetime
            services.Add(new ServiceDescriptor(configurationType, configurationType, lifetime));
            _logger.LogInformation("Finalized configuration: {ConfigType} ({Lifetime})",
                configurationType.Name, lifetime);
            finalizedCount++;
        }

        _logger.LogInformation("Finalized {Count} configuration registrations", finalizedCount);
    }

    /// <summary>
    /// Register all discovered client types in DI with matching service lifetimes.
    /// </summary>
    private void RegisterClientTypes(IServiceCollection services)
    {
        foreach (var clientType in _clientTypesToRegister)
        {
            // Find the corresponding interface
            var interfaceName = "I" + clientType.Name;
            var clientInterface = clientType.Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == interfaceName && t.IsInterface);

            if (clientInterface != null)
            {
                // Determine appropriate lifetime for this client
                var lifetime = GetClientLifetime(clientInterface);

                // Register with the determined lifetime
                services.Add(new ServiceDescriptor(clientInterface, clientType, lifetime));

                _logger.LogDebug("Registered client: {Interface} -> {Implementation} (Lifetime: {Lifetime})",
                    clientInterface.Name, clientType.Name, lifetime);
            }
        }

        _logger.LogInformation("Registered {Count} client types in DI", _clientTypesToRegister.Count);
    }

    /// <summary>
    /// Determine the appropriate lifetime for a client based on its corresponding service.
    /// </summary>
    private ServiceLifetime GetClientLifetime(Type clientInterface)
    {
        // All clients should be Singleton regardless of their service lifetime.
        // Clients are generated code for making Dapr requests and should not depend on their service's lifetime.
        // Some services that USE clients (not the service they communicate with) could be Singleton,
        // so all clients need to be at least Singleton to be injectable.
        _logger.LogDebug("Using Singleton lifetime for client '{ClientInterface}' (all clients are Singleton)", clientInterface.Name);
        return ServiceLifetime.Singleton;
    }

    /// <summary>
    /// Register all discovered service types in DI.
    /// </summary>
    private void RegisterServiceTypes(IServiceCollection services)
    {
        foreach (var (interfaceType, implementationType, lifetime) in _serviceTypesToRegister)
        {
            services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime));
            _logger.LogDebug("Registered service: {Interface} -> {Implementation} ({Lifetime})",
                interfaceType.Name, implementationType.Name, lifetime);
        }

        _logger.LogInformation("Registered {Count} service types in DI", _serviceTypesToRegister.Count);
    }

    /// <summary>
    /// Register all discovered configuration types in DI with Singleton lifetime.
    /// All configurations use Singleton lifetime regardless of service lifetime for proper startup configuration binding.
    /// </summary>
    private void RegisterConfigurationTypes(IServiceCollection services)
    {
        foreach (var (configurationType, _) in _configurationTypesToRegister)
        {
            // Check if this type is already registered
            var existingRegistration = services.FirstOrDefault(s => s.ServiceType == configurationType);
            if (existingRegistration != null)
            {
                _logger.LogWarning("Configuration type {ConfigType} is already registered with lifetime {ExistingLifetime}. Removing existing registration to replace with Singleton.", configurationType.Name, existingRegistration.Lifetime);
                services.Remove(existingRegistration);
            }

            // All configurations must be Singleton for proper configuration binding
            // Register with factory that builds configuration from environment variables
            services.AddSingleton(configurationType, serviceProvider =>
            {
                // Use reflection to call IServiceConfiguration.BuildConfiguration<T>() with specific signature
                var buildMethod = (typeof(IServiceConfiguration).GetMethod(
                    nameof(IServiceConfiguration.BuildConfiguration),
                    BindingFlags.Public | BindingFlags.Static,
                    Type.DefaultBinder,
                    new Type[] { typeof(string[]) }, // string[]? args = null overload
                    null)?.MakeGenericMethod(configurationType)) ?? throw new InvalidOperationException($"Could not find BuildConfiguration<T>(string[]?) method for type {configurationType.Name}");
                var configInstance = buildMethod.Invoke(null, new object?[] { null }); // Pass null for args parameter
                return configInstance ?? throw new InvalidOperationException($"BuildConfiguration returned null for type {configurationType.Name}");
            });
            _logger.LogInformation("Registered configuration: {ConfigType} (Singleton with BuildConfiguration factory)",
                configurationType.Name);
        }

        _logger.LogInformation("Registered {Count} configuration types in DI", _configurationTypesToRegister.Count);

        // Debug: Print all ConnectServiceConfiguration registrations
        var connectConfigRegistrations = services.Where(s =>
            s.ServiceType.Name.Contains("ConnectServiceConfiguration")).ToList();

        _logger.LogWarning("DEBUG: Found {Count} registrations for ConnectServiceConfiguration:",
            connectConfigRegistrations.Count);

        foreach (var reg in connectConfigRegistrations)
        {
            _logger.LogWarning("  - ServiceType: {ServiceType}, ImplementationType: {ImplType}, Lifetime: {Lifetime}",
                reg.ServiceType.Name, reg.ImplementationType?.Name ?? "Unknown", reg.Lifetime);
        }
    }

    /// <summary>
    /// Resolve and store service instances centrally for enabled plugins only.
    /// This happens AFTER the web application is built and DI container is ready.
    /// </summary>
    /// <param name="serviceProvider">Service provider to resolve services from</param>
    [Obsolete]
    public void ResolveServices(IServiceProvider serviceProvider)
    {
        _logger.LogInformation("Centrally resolving services for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // Use a service scope to resolve scoped services correctly
        using var scope = serviceProvider.CreateScope();
        var scopedServiceProvider = scope.ServiceProvider;

        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Resolving service for enabled plugin: {PluginName}", plugin.PluginName);

                // Find the service type that was registered for this plugin
                var serviceRegistration = _serviceTypesToRegister
                    .FirstOrDefault(s => GetServiceNameFromType(s.implementationType) == plugin.PluginName);

                if (serviceRegistration != default)
                {
                    var serviceInstance = scopedServiceProvider.GetService(serviceRegistration.interfaceType);
                    if (serviceInstance is IDaprService daprService)
                    {
                        _resolvedServices[plugin.PluginName] = daprService;
                        _logger.LogInformation("Resolved {ServiceType} for plugin: {PluginName}",
                            serviceRegistration.implementationType.Name, plugin.PluginName);
                    }
                    else if (serviceInstance != null)
                    {
                        _logger.LogWarning("Service {ServiceType} could not be resolved from DI container for plugin: {PluginName}", serviceRegistration.interfaceType.Name, plugin.PluginName);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to resolve service {ServiceType} for plugin: {PluginName}", serviceRegistration.interfaceType.Name, plugin.PluginName);
                    }
                }
                else
                {
                    _logger.LogDebug("No service registration found for plugin: {PluginName} (plugin may not have a service)", plugin.PluginName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while resolving service for plugin: {PluginName}", plugin.PluginName);
            }
        }

        _logger.LogInformation("Service resolution complete. {ResolvedCount} services resolved", _resolvedServices.Count);
    }

    /// <summary>
    /// Extract service name from a service type using DaprService attribute.
    /// </summary>
    [Obsolete]
    private string? GetServiceNameFromType(Type serviceType)
    {
        var daprServiceAttr = serviceType.GetCustomAttribute<DaprServiceAttribute>();
        return daprServiceAttr?.Name;
    }


    /// <summary>
    /// Get a centrally resolved service for a plugin.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <returns>Resolved service instance or null if not found</returns>
    [Obsolete]
    public IDaprService? GetResolvedService(string pluginName)
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
    /// Configure application pipeline for enabled plugins only.
    /// </summary>
    /// <param name="app">Web application</param>
    public void ConfigureApplication(WebApplication app)
    {
        // Store WebApplication reference for use during service startup
        _webApp = app;

        _logger.LogInformation("Configuring application pipeline for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Configuring application for plugin: {PluginName}", plugin.PluginName);
                plugin.ConfigureApplication(app);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure application for plugin: {PluginName}", plugin.PluginName);
                throw; // Re-throw to fail startup if plugin configuration fails
            }
        }

        _logger.LogInformation("Application configuration complete for enabled plugins");
    }

    /// <summary>
    /// Check if a plugin is a required infrastructure plugin.
    /// </summary>
    /// <param name="pluginName">Name of the plugin to check</param>
    /// <returns>True if the plugin is required infrastructure</returns>
    public static bool IsRequiredInfrastructure(string pluginName)
        => RequiredInfrastructurePlugins.Contains(pluginName);

    /// <summary>
    /// Initialize enabled plugins and their resolved services.
    /// This follows the proper lifecycle: plugins first, then services.
    /// Infrastructure plugins (messaging, state, mesh) are initialized first and
    /// their failure causes immediate startup failure.
    /// </summary>
    /// <returns>True if all plugins and services initialized successfully</returns>
    [Obsolete]
    public async Task<bool> InitializeAsync()
    {
        _logger.LogInformation("🚀 Initializing {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // STAGE 1: Initialize enabled plugins (infrastructure first due to sorting)
        foreach (var plugin in _enabledPlugins)
        {
            var isInfrastructure = RequiredInfrastructurePlugins.Contains(plugin.PluginName);
            var pluginType = isInfrastructure ? "INFRASTRUCTURE" : "service";

            try
            {
                _logger.LogDebug("Initializing {PluginType} plugin: {PluginName}",
                    pluginType, plugin.PluginName);

                var success = await plugin.InitializeAsync();
                if (!success)
                {
                    if (isInfrastructure)
                    {
                        _logger.LogCritical(
                            "STARTUP FAILURE: Infrastructure plugin '{PluginName}' failed to initialize. " +
                            "This is a required plugin and the application cannot function without it.",
                            plugin.PluginName);
                    }
                    else
                    {
                        _logger.LogError("Plugin initialization failed: {PluginName}", plugin.PluginName);
                    }
                    return false;
                }

                if (isInfrastructure)
                {
                    _logger.LogInformation("✅ Infrastructure plugin '{PluginName}' initialized successfully",
                        plugin.PluginName);
                }
            }
            catch (Exception ex)
            {
                if (isInfrastructure)
                {
                    _logger.LogCritical(ex,
                        "STARTUP FAILURE: Infrastructure plugin '{PluginName}' threw exception during initialization. " +
                        "This is a required plugin and the application cannot function without it.",
                        plugin.PluginName);
                }
                else
                {
                    _logger.LogError(ex, "Exception during plugin initialization: {PluginName}", plugin.PluginName);
                }
                return false;
            }
        }

        // STAGE 2: Initialize centrally resolved services
        // Use OnStartAsync(WebApplication, CancellationToken) to allow services to register minimal API endpoints
        if (_webApp == null)
        {
            _logger.LogWarning("WebApplication reference not set - services requiring WebApplication will not initialize fully. " +
                "Ensure ConfigureApplication() is called before InitializeAsync().");
        }

        foreach (var (pluginName, service) in _resolvedServices)
        {
            try
            {
                _logger.LogDebug("Initializing centrally resolved service for plugin: {PluginName}", pluginName);
                if (_webApp != null)
                {
                    // Call the WebApplication-aware version for services that need endpoint registration
                    await service.OnStartAsync(_webApp, CancellationToken.None);
                }
                else
                {
                    // Fall back to simple version if WebApplication not available
                    await service.OnStartAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service initialization for plugin: {PluginName}", pluginName);
                return false;
            }
        }

        _logger.LogInformation("All enabled plugins and services initialized successfully");
        return true;
    }

    /// <summary>
    /// Registers service permissions for all enabled plugins with the Permissions service.
    /// This should be called AFTER Dapr connectivity is confirmed to ensure events are delivered.
    /// </summary>
    /// <returns>True if all permissions were registered successfully</returns>
    [Obsolete]
    public async Task<bool> RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering service permissions for {ServiceCount} services: {ServiceNames}",
            _resolvedServices.Count, string.Join(", ", _resolvedServices.Keys));

        foreach (var (pluginName, service) in _resolvedServices)
        {
            try
            {
                _logger.LogInformation("Registering permissions for service: {PluginName} (type: {ServiceType})",
                    pluginName, service.GetType().Name);
                const int maxAttempts = 3;
                var attempt = 0;
                while (true)
                {
                    attempt++;
                    try
                    {
                        await service.RegisterServicePermissionsAsync();
                        _logger.LogInformation("Permissions registered successfully for service: {PluginName} (attempt {Attempt})", pluginName, attempt);
                        break;
                    }
                    catch (HttpRequestException httpEx) when (attempt < maxAttempts)
                    {
                        _logger.LogWarning(httpEx, "Permission registration retry {Attempt}/{Max} for {PluginName}", attempt, maxAttempts, pluginName);
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                    }
                    catch (TimeoutException timeoutEx) when (attempt < maxAttempts)
                    {
                        _logger.LogWarning(timeoutEx, "Permission registration retry {Attempt}/{Max} for {PluginName}", attempt, maxAttempts, pluginName);
                        await Task.Delay(TimeSpan.FromMilliseconds(500));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register permissions for {PluginName}", pluginName);
                return false;
            }
        }

        _logger.LogInformation("All service permissions registered successfully");
        return true;
    }

    /// <summary>
    /// Start enabled plugins and their resolved services.
    /// This follows the proper lifecycle: plugins first, then services.
    /// </summary>
    /// <returns>True if all plugins and services started successfully</returns>
    public async Task<bool> StartAsync()
    {
        _logger.LogInformation("Starting {EnabledCount} enabled plugins", _enabledPlugins.Count);

        // STAGE 1: Start enabled plugins
        foreach (var plugin in _enabledPlugins)
        {
            try
            {
                _logger.LogDebug("Starting plugin: {PluginName}", plugin.PluginName);
                var success = await plugin.StartAsync();
                if (!success)
                {
                    _logger.LogError("Plugin start failed: {PluginName}", plugin.PluginName);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin start: {PluginName}", plugin.PluginName);
                return false;
            }
        }

        // STAGE 2: Start centrally resolved services (with WebApplication access)
        // Note: This would need WebApplication parameter if services need it

        _logger.LogInformation("All enabled plugins started successfully");
        return true;
    }

    /// <summary>
    /// Invoke running methods for enabled plugins and resolved services.
    /// </summary>
    [Obsolete]
    public async Task InvokeRunningAsync()
    {
        _logger.LogInformation("Invoking running methods for {EnabledCount} enabled plugins", _enabledPlugins.Count);

        var runningTasks = _enabledPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("Invoking running for plugin: {PluginName}", plugin.PluginName);
                await plugin.RunningAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin running: {PluginName}", plugin.PluginName);
            }
        });

        // Also invoke running for centrally resolved services
        var serviceRunningTasks = _resolvedServices.Select(async kvp =>
        {
            try
            {
                _logger.LogDebug("Invoking running for centrally resolved service: {PluginName}", kvp.Key);
                await kvp.Value.OnRunningAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service running: {PluginName}", kvp.Key);
            }
        });

        await Task.WhenAll(runningTasks.Concat(serviceRunningTasks));
        _logger.LogInformation("All enabled plugin running methods invoked");
    }

    /// <summary>
    /// Shutdown enabled plugins and resolved services gracefully.
    /// </summary>
    [Obsolete]
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down {EnabledCount} enabled plugins", _enabledPlugins.Count);

        var shutdownTasks = _enabledPlugins.Select(async plugin =>
        {
            try
            {
                _logger.LogDebug("Shutting down plugin: {PluginName}", plugin.PluginName);
                await plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during plugin shutdown: {PluginName}", plugin.PluginName);
            }
        });

        // Also shutdown centrally resolved services
        var serviceShutdownTasks = _resolvedServices.Select(async kvp =>
        {
            try
            {
                _logger.LogDebug("Shutting down centrally resolved service: {PluginName}", kvp.Key);
                await kvp.Value.OnShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during centrally resolved service shutdown: {PluginName}", kvp.Key);
            }
        });

        await Task.WhenAll(shutdownTasks.Concat(serviceShutdownTasks));
        _logger.LogInformation("All enabled plugins shut down");
    }

    /// <summary>
    /// Load a plugin from a specific directory.
    /// </summary>
    /// <param name="pluginDirectory">Directory containing the plugin</param>
    /// <param name="expectedPluginName">Expected name of the plugin</param>
    /// <returns>Loaded plugin instance or null if loading failed</returns>
    private Task<IBannouPlugin?> LoadPluginFromDirectoryAsync(string pluginDirectory, string expectedPluginName)
    {
        _logger.LogDebug("Loading plugin from directory: {PluginDirectory}", pluginDirectory);

        // Find the main assembly (usually lib-{service}.dll)
        var assemblyPath = Path.Combine(pluginDirectory, $"lib-{expectedPluginName}.dll");
        if (!File.Exists(assemblyPath))
        {
            _logger.LogWarning("Plugin assembly not found: {AssemblyPath}", assemblyPath);
            return Task.FromResult<IBannouPlugin?>(null);
        }

        try
        {
            // Check if assembly is already loaded to avoid conflicts
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

            if (assembly == null)
            {
                // Load the assembly if not already loaded
                assembly = Assembly.LoadFrom(assemblyPath);
                _logger.LogDebug("Loaded new assembly: {AssemblyName} from {AssemblyPath}",
                    assembly.GetName().Name, assemblyPath);
            }
            else
            {
                _logger.LogDebug("Using already loaded assembly: {AssemblyName}", assembly.GetName().Name);
            }

            _loadedAssemblies[expectedPluginName] = assembly;

            // Find types that implement IBannouPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IBannouPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            if (pluginTypes.Count == 0)
            {
                _logger.LogWarning("No IBannouPlugin implementations found in assembly: {AssemblyPath}", assemblyPath);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            if (pluginTypes.Count > 1)
            {
                _logger.LogWarning("Multiple IBannouPlugin implementations found in assembly: {AssemblyPath}. Using first one.", assemblyPath);
            }

            // Create plugin instance
            var pluginType = pluginTypes.First();
            var plugin = Activator.CreateInstance(pluginType) as IBannouPlugin;

            if (plugin == null)
            {
                _logger.LogError("Failed to create plugin instance for type: {PluginType}", pluginType.Name);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            // Validate plugin
            if (!plugin.ValidatePlugin())
            {
                _logger.LogWarning("Plugin validation failed: {PluginName}", plugin.PluginName);
                return Task.FromResult<IBannouPlugin?>(null);
            }

            // Verify plugin name matches expected
            if (!string.Equals(plugin.PluginName, expectedPluginName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Plugin name mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedPluginName, plugin.PluginName);
            }

            return Task.FromResult<IBannouPlugin?>(plugin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin assembly: {AssemblyPath}", assemblyPath);
            return Task.FromResult<IBannouPlugin?>(null);
        }
    }
}
