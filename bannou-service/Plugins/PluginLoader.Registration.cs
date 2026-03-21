using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Type discovery from assemblies and DI container registration.
/// </summary>
public partial class PluginLoader
{
    /// <summary>
    /// Discover types from all loaded assemblies that need to be registered in DI.
    /// This includes ALL client types (even from disabled plugins) and service types from enabled plugins only.
    /// Also scans the host assembly (bannou-service) for centralized client types.
    /// </summary>
    private void DiscoverTypesForRegistration()
    {
        _logger.LogInformation("Discovering types for DI registration from {AssemblyCount} plugin assemblies + host assembly", _loadedAssemblies.Count);

        _clientTypesToRegister.Clear();
        _serviceTypesToRegister.Clear();
        _helperServiceTypesToRegister.Clear();
        _concreteHelperTypesToRegister.Clear();
        _bothHelperTypesToRegister.Clear();
        _hostedServiceTypesToRegister.Clear();
        _singletonAndHostedServiceTypesToRegister.Clear();
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
                    DiscoverHelperServiceTypes(assembly, pluginName);
                    // Note: Configuration discovery happens later to ensure service types are registered first
                }
                else
                {
                    _logger.LogDebug("Skipping service and configuration type registration for disabled plugin: {PluginName}", pluginName);
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

        _logger.LogInformation("Type discovery complete. {ClientCount} client types, {ServiceCount} service types, {HelperCount} helper service types, {HostedCount} hosted service types, {DualCount} singleton+hosted types, {ConfigCount} configuration types",
            _clientTypesToRegister.Count, _serviceTypesToRegister.Count, _helperServiceTypesToRegister.Count,
            _hostedServiceTypesToRegister.Count, _singletonAndHostedServiceTypesToRegister.Count, _configurationTypesToRegister.Count);
    }

    /// <summary>
    /// Discover client types that implement IServiceClient interface.
    /// Pure interface-based discovery - no naming conventions.
    /// </summary>
    private void DiscoverClientTypes(Assembly assembly, string pluginName)
    {
        var clientTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceClient).IsAssignableFrom(t))
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
    /// Discover service types using IBannouService interface and BannouServiceAttribute.
    /// Pure interface/attribute-based discovery from the specific assembly.
    /// </summary>
    private void DiscoverServiceTypes(Assembly assembly, string pluginName)
    {
        var serviceTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IBannouService).IsAssignableFrom(t))
            .ToList();

        foreach (var serviceType in serviceTypes)
        {
            var serviceAttr = serviceType.GetCustomAttribute<BannouServiceAttribute>();
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
                _logger.LogDebug("Skipping service {ServiceType} - missing BannouService attribute",
                    serviceType.Name);
            }
        }

        _logger.LogDebug("Discovered {Count} service types in assembly {AssemblyName}",
            serviceTypes.Count(t => t.GetCustomAttribute<BannouServiceAttribute>() != null), assembly.GetName().Name);
    }

    /// <summary>
    /// Discover helper service types using BannouHelperServiceAttribute.
    /// Handles three registration modes:
    /// <list type="bullet">
    /// <item><see cref="HelperRegistrationMode.Interface"/>: Standard interface→implementation (requires non-null InterfaceType)</item>
    /// <item><see cref="HelperRegistrationMode.HostedService"/>: BackgroundService auto-registration via AddHostedService</item>
    /// <item><see cref="HelperRegistrationMode.SingletonAndHostedService"/>: DI-injectable Singleton + hosted service factory</item>
    /// </list>
    /// Types with <see cref="HelperRegistrationMode.Interface"/> and null InterfaceType require
    /// manual registration in Plugin.cs (multi-implementation, factory lambdas, conditional branches).
    /// </summary>
    private void DiscoverHelperServiceTypes(Assembly assembly, string pluginName)
    {
        var helperTypes = assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && t.GetCustomAttribute<BannouHelperServiceAttribute>() != null)
            .ToList();

        var autoRegistered = 0;

        foreach (var helperType in helperTypes)
        {
            var attr = helperType.GetCustomAttribute<BannouHelperServiceAttribute>()!;

            switch (attr.RegistrationMode)
            {
                case HelperRegistrationMode.HostedService:
                    // Hosted services are tracked separately — registered via AddHostedService pattern
                    _hostedServiceTypesToRegister.Add(helperType);
                    _logger.LogDebug("Will auto-register hosted service: {Implementation} (Singleton)",
                        helperType.Name);
                    autoRegistered++;
                    break;

                case HelperRegistrationMode.SingletonAndHostedService:
                    // Dual registration: concrete Singleton for DI injection + hosted service factory
                    _singletonAndHostedServiceTypesToRegister.Add(helperType);
                    _logger.LogDebug("Will auto-register singleton+hosted service: {Implementation}",
                        helperType.Name);
                    autoRegistered++;
                    break;

                case HelperRegistrationMode.Interface:
                default:
                    switch (attr.DependencyMode)
                    {
                        case DependencyRegistrationMode.Concrete:
                            _concreteHelperTypesToRegister.Add((helperType, attr.Lifetime));
                            _logger.LogDebug("Will auto-register concrete helper: {Implementation} ({Lifetime})",
                                helperType.Name, attr.Lifetime);
                            autoRegistered++;
                            break;

                        case DependencyRegistrationMode.Both:
                            if (attr.InterfaceType == null)
                            {
                                // Both without InterfaceType degrades to Concrete
                                _concreteHelperTypesToRegister.Add((helperType, attr.Lifetime));
                                _logger.LogDebug("Will auto-register concrete helper (Both with null InterfaceType): {Implementation} ({Lifetime})",
                                    helperType.Name, attr.Lifetime);
                            }
                            else
                            {
                                _bothHelperTypesToRegister.Add((attr.InterfaceType, helperType, attr.Lifetime));
                                _logger.LogDebug("Will auto-register both concrete+interface helper: {Implementation} -> {Interface} ({Lifetime})",
                                    helperType.Name, attr.InterfaceType.Name, attr.Lifetime);
                            }
                            autoRegistered++;
                            break;

                        case DependencyRegistrationMode.Interface:
                        default:
                            if (attr.InterfaceType == null)
                            {
                                _logger.LogDebug("Skipping helper {HelperType} — Interface mode with no InterfaceType (requires manual registration)",
                                    helperType.Name);
                                continue;
                            }

                            _helperServiceTypesToRegister.Add((attr.InterfaceType, helperType, attr.Lifetime));
                            _logger.LogDebug("Will auto-register helper: {Interface} -> {Implementation} ({Lifetime})",
                                attr.InterfaceType.Name, helperType.Name, attr.Lifetime);
                            autoRegistered++;
                            break;
                    }
                    break;
            }
        }

        _logger.LogDebug("Discovered {Count} auto-registrable helper service types in assembly {AssemblyName}",
            autoRegistered, assembly.GetName().Name);
    }

    /// <summary>
    /// Discover configuration types with [ServiceConfiguration] attributes from an assembly.
    /// Configuration lifetimes match their corresponding service lifetimes.
    /// Pure attribute-based discovery - configurations must have ServiceImplementationType specified.
    /// </summary>
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
                    .FirstOrDefault(t => t.Name == serviceName && typeof(IBannouService).IsAssignableFrom(t));

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
        _logger.LogInformation("Centrally registering {ClientCount} client types, {ServiceCount} service types, {HelperCount} helper service types, {HostedCount} hosted service types, {DualCount} singleton+hosted types, and {ConfigCount} configuration types",
            _clientTypesToRegister.Count, _serviceTypesToRegister.Count, _helperServiceTypesToRegister.Count,
            _hostedServiceTypesToRegister.Count, _singletonAndHostedServiceTypesToRegister.Count, _configurationTypesToRegister.Count);

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

        // STAGE 5: Auto-register helper services discovered via [BannouHelperService] attribute.
        // Runs AFTER plugin.ConfigureServices() so that manual registrations take precedence.
        // Only registers types not already in the collection (safe incremental migration).
        // Handles all three HelperRegistrationMode values:
        //   Interface → standard interface→implementation
        //   HostedService → AddHostedService<T>()
        //   SingletonAndHostedService → AddSingleton(T) + AddHostedService factory
        RegisterHelperServiceTypes(services);
        RegisterConcreteHelperTypes(services);
        RegisterBothHelperTypes(services);
        RegisterHostedServiceTypes(services);
        RegisterSingletonAndHostedServiceTypes(services);

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
        // Clients are generated code for making mesh requests and should not depend on their service's lifetime.
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
    /// Register helper service types discovered via [BannouHelperService] attribute.
    /// Only registers types not already present in the service collection (prevents duplicates
    /// when Plugin.ConfigureServices() has already registered the type manually).
    /// This enables incremental migration: as manual registrations are removed from Plugin.cs,
    /// auto-registration automatically takes over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: This method does NOT deduplicate by interface type alone — it checks the
    /// exact (interface, implementation) pair. If two implementations declare the same
    /// InterfaceType in their [BannouHelperService] attribute, BOTH get registered. .NET DI
    /// returns the last registration for GetRequiredService, but Assembly.GetTypes() ordering
    /// is non-deterministic, so which implementation "wins" is undefined. Worse, if one
    /// implementation has mode-specific dependencies (e.g., IChannelManager only in RabbitMQ
    /// mode), resolving the interface in other modes will throw.
    /// </para>
    /// <para>
    /// For backend-conditional helpers (e.g., InMemoryMessageTap vs RabbitMQMessageTap),
    /// omit InterfaceType from the attribute and register explicitly in Plugin.ConfigureServices
    /// within the correct mode branch.
    /// </para>
    /// </remarks>
    private void RegisterHelperServiceTypes(IServiceCollection services)
    {
        var autoRegistered = 0;
        var skippedAlreadyRegistered = 0;

        foreach (var (interfaceType, implementationType, lifetime) in _helperServiceTypesToRegister)
        {
            // Check if this exact interface→implementation pair is already registered
            // (Plugin.ConfigureServices may have registered it manually)
            var alreadyRegistered = services.Any(sd =>
                sd.ServiceType == interfaceType &&
                sd.ImplementationType == implementationType);

            if (alreadyRegistered)
            {
                _logger.LogDebug(
                    "Skipping auto-registration of helper {Interface} -> {Implementation} (already registered by plugin)",
                    interfaceType.Name, implementationType.Name);
                skippedAlreadyRegistered++;
                continue;
            }

            services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime));
            _logger.LogDebug("Auto-registered helper: {Interface} -> {Implementation} ({Lifetime})",
                interfaceType.Name, implementationType.Name, lifetime);
            autoRegistered++;
        }

        _logger.LogInformation(
            "Helper service registration complete. {AutoRegistered} auto-registered, {Skipped} skipped (already registered by plugin)",
            autoRegistered, skippedAlreadyRegistered);
    }

    /// <summary>
    /// Register helper services with <see cref="DependencyRegistrationMode.Concrete"/>.
    /// Registers the concrete type only (no interface mapping).
    /// </summary>
    private void RegisterConcreteHelperTypes(IServiceCollection services)
    {
        var autoRegistered = 0;
        var skippedAlreadyRegistered = 0;

        foreach (var (concreteType, lifetime) in _concreteHelperTypesToRegister)
        {
            var alreadyRegistered = services.Any(sd => sd.ServiceType == concreteType);

            if (alreadyRegistered)
            {
                _logger.LogDebug(
                    "Skipping auto-registration of concrete helper {Implementation} (already registered by plugin)",
                    concreteType.Name);
                skippedAlreadyRegistered++;
                continue;
            }

            services.Add(new ServiceDescriptor(concreteType, concreteType, lifetime));
            _logger.LogDebug("Auto-registered concrete helper: {Implementation} ({Lifetime})",
                concreteType.Name, lifetime);
            autoRegistered++;
        }

        _logger.LogInformation(
            "Concrete helper registration complete. {AutoRegistered} auto-registered, {Skipped} skipped (already registered by plugin)",
            autoRegistered, skippedAlreadyRegistered);
    }

    /// <summary>
    /// Register helper services with <see cref="DependencyRegistrationMode.Both"/>.
    /// Registers concrete type as Singleton AND interface → factory(ConcreteType).
    /// Supports IEnumerable accumulation (multiple implementations of the same interface).
    /// </summary>
    private void RegisterBothHelperTypes(IServiceCollection services)
    {
        var autoRegistered = 0;
        var skippedAlreadyRegistered = 0;

        foreach (var (interfaceType, implementationType, lifetime) in _bothHelperTypesToRegister)
        {
            var concreteAlreadyRegistered = services.Any(sd => sd.ServiceType == implementationType);

            if (concreteAlreadyRegistered)
            {
                _logger.LogDebug(
                    "Skipping auto-registration of both helper {Implementation} (already registered by plugin)",
                    implementationType.Name);
                skippedAlreadyRegistered++;
                continue;
            }

            // Step 1: Register concrete type (for direct DI injection)
            services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));

            // Step 2: Register interface → factory (for IEnumerable<T> accumulation or polymorphic resolution)
            services.Add(new ServiceDescriptor(interfaceType,
                sp => sp.GetRequiredService(implementationType), lifetime));

            _logger.LogDebug("Auto-registered both helper: {Implementation} + {Interface} ({Lifetime})",
                implementationType.Name, interfaceType.Name, lifetime);
            autoRegistered++;
        }

        _logger.LogInformation(
            "Both (concrete+interface) helper registration complete. {AutoRegistered} auto-registered, {Skipped} skipped (already registered by plugin)",
            autoRegistered, skippedAlreadyRegistered);
    }

    /// <summary>
    /// Register helper services with <see cref="HelperRegistrationMode.HostedService"/>.
    /// Uses the AddHostedService pattern: services.Add(ServiceDescriptor.Singleton&lt;IHostedService, T&gt;()).
    /// Skips types already registered as IHostedService (Plugin.ConfigureServices may have registered manually).
    /// </summary>
    private void RegisterHostedServiceTypes(IServiceCollection services)
    {
        var autoRegistered = 0;
        var skippedAlreadyRegistered = 0;

        foreach (var hostedType in _hostedServiceTypesToRegister)
        {
            // Check if this type is already registered as IHostedService
            var alreadyRegistered = services.Any(sd =>
                sd.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                sd.ImplementationType == hostedType);

            if (alreadyRegistered)
            {
                _logger.LogDebug(
                    "Skipping auto-registration of hosted service {Implementation} (already registered by plugin)",
                    hostedType.Name);
                skippedAlreadyRegistered++;
                continue;
            }

            // Register as IHostedService with Singleton lifetime (same as AddHostedService<T>())
            services.Add(ServiceDescriptor.Singleton(typeof(Microsoft.Extensions.Hosting.IHostedService), hostedType));
            _logger.LogDebug("Auto-registered hosted service: {Implementation}", hostedType.Name);
            autoRegistered++;
        }

        _logger.LogInformation(
            "Hosted service registration complete. {AutoRegistered} auto-registered, {Skipped} skipped (already registered by plugin)",
            autoRegistered, skippedAlreadyRegistered);
    }

    /// <summary>
    /// Register helper services with <see cref="HelperRegistrationMode.SingletonAndHostedService"/>.
    /// Performs dual registration: concrete type as Singleton (for DI injection by other services)
    /// AND as IHostedService via factory (for the .NET generic host to start it).
    /// Skips types already registered as their concrete type or as IHostedService.
    /// </summary>
    private void RegisterSingletonAndHostedServiceTypes(IServiceCollection services)
    {
        var autoRegistered = 0;
        var skippedAlreadyRegistered = 0;

        foreach (var dualType in _singletonAndHostedServiceTypesToRegister)
        {
            // Check if the concrete type is already registered
            var concreteAlreadyRegistered = services.Any(sd =>
                sd.ServiceType == dualType);

            if (concreteAlreadyRegistered)
            {
                _logger.LogDebug(
                    "Skipping auto-registration of singleton+hosted service {Implementation} (already registered by plugin)",
                    dualType.Name);
                skippedAlreadyRegistered++;
                continue;
            }

            // Step 1: Register concrete type as Singleton (for DI injection)
            services.AddSingleton(dualType);

            // Step 2: Register as IHostedService via factory (for .NET host startup)
            services.AddSingleton(typeof(Microsoft.Extensions.Hosting.IHostedService),
                sp => (Microsoft.Extensions.Hosting.IHostedService)sp.GetRequiredService(dualType));

            _logger.LogDebug("Auto-registered singleton+hosted service: {Implementation}", dualType.Name);
            autoRegistered++;
        }

        _logger.LogInformation(
            "Singleton+hosted service registration complete. {AutoRegistered} auto-registered, {Skipped} skipped (already registered by plugin)",
            autoRegistered, skippedAlreadyRegistered);
    }

    /// <summary>
    /// Register all discovered configuration types in DI with Singleton lifetime.
    /// All configurations use Singleton lifetime regardless of service lifetime for proper startup configuration binding.
    /// </summary>
    private void RegisterConfigurationTypes(IServiceCollection services)
    {
        // CRITICAL: Register AppConfiguration first - it's a global config without a matching service
        // but is needed by MeshServicePlugin and other components during early startup
        if (!services.Any(s => s.ServiceType == typeof(AppConfiguration)))
        {
            services.AddSingleton<AppConfiguration>(serviceProvider =>
            {
                var config = IServiceConfiguration.BuildConfiguration<AppConfiguration>(null);
                // IMPLEMENTATION TENETS: Validate configuration properties at startup.
                // Cast to interface to access default interface method
                ((IServiceConfiguration)config).Validate();
                return config;
            });
            // Register as IServiceConfiguration too - MeshServicePlugin resolves this interface
            // to read ForceServiceId for IMeshInstanceIdentifier
            services.AddSingleton<IServiceConfiguration>(sp => sp.GetRequiredService<AppConfiguration>());
            _logger.LogInformation("Registered global configuration: AppConfiguration (Singleton with BuildConfiguration factory)");
        }

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
                var configInstance = buildMethod.Invoke(null, new object?[] { null }) ?? throw new InvalidOperationException($"BuildConfiguration returned null for type {configurationType.Name}"); // Pass null for args parameter

                // IMPLEMENTATION TENETS: Validate configuration properties at startup.
                // Schema provides defaults - invalid values mean explicit overrides with bad data.
                if (configInstance is IServiceConfiguration serviceConfig)
                {
                    serviceConfig.Validate();
                }

                return configInstance;
            });
            _logger.LogInformation("Registered configuration: {ConfigType} (Singleton with BuildConfiguration factory)",
                configurationType.Name);
        }

        _logger.LogInformation("Registered {Count} configuration types in DI", _configurationTypesToRegister.Count);
    }
}
