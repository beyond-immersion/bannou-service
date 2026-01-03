using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using Microsoft.AspNetCore.Builder;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Interface to implement for all Bannou service plugins,
/// which provides the logic for any given set of APIs.
///
/// For example, the Inventory service is in charge of
/// any API calls that desire to create/modify inventory
/// data in the game.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - Service Implementation Pattern:</b> All service implementations
/// MUST be declared as <c>partial class</c> to support the following code-split pattern:
/// </para>
/// <list type="bullet">
///   <item><c>{Service}Service.cs</c> - Main business logic (manual)</item>
///   <item><c>{Service}ServiceEvents.cs</c> - Event consumer registrations (generated from x-subscribes-to)</item>
///   <item><c>Generated/{Service}PermissionRegistration.cs</c> - Permission registrations (generated from x-permissions)</item>
/// </list>
/// <para>
/// Services MUST also use the <see cref="Attributes.BannouServiceAttribute"/> for discovery and
/// inject dependencies via constructor following the standardized pattern.
/// </para>
/// <para>
/// See: docs/reference/tenets/FOUNDATION.md (T6: Service Implementation Pattern)
/// </para>
/// </remarks>
public interface IBannouService
{
    private static (Type, Type, BannouServiceAttribute)[]? _services;

    // Static storage for instance IDs (using ConditionalWeakTable to avoid memory leaks)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IBannouService, GuidBox> _instanceIds = new();

    // Helper class to wrap Guid as a reference type for ConditionalWeakTable
    private sealed class GuidBox
    {
        public Guid Value { get; }
        public GuidBox() => Value = Guid.NewGuid();
    }

    /// <summary>
    /// Unique instance identifier for this service plugin.
    /// Used for log correlation and debugging across distributed systems.
    /// Generated once per service instance lifetime.
    /// Default implementation generates a GUID on first access.
    /// </summary>
    Guid InstanceId => _instanceIds.GetOrCreateValue(this).Value;

    /// <summary>
    /// Gets the service version string for heartbeat reporting.
    /// Override to provide a custom version. Default returns "1.0.0".
    /// </summary>
    string ServiceVersion => "1.0.0";

    /// <summary>
    /// Called during heartbeat collection to gather service-specific status and metadata.
    /// Override to provide custom status reporting, capacity info, or metadata.
    /// Default implementation returns healthy status with no additional metadata.
    /// </summary>
    /// <returns>ServiceStatus with this service's current health information</returns>
    ServiceStatus OnHeartbeat()
    {
        return new ServiceStatus
        {
            ServiceId = InstanceId,
            ServiceName = GetName() ?? GetType().Name,
            Status = ServiceStatusStatus.Healthy,
            Version = ServiceVersion
        };
    }

    /// <summary>
    /// Called when a pub/sub event is received by this service.
    /// Override in service implementations to handle specific events.
    /// Default implementation does nothing.
    /// </summary>
    /// <typeparam name="T">The event data type</typeparam>
    /// <param name="topic">The event topic (e.g., "account.deleted")</param>
    /// <param name="eventData">The event data payload</param>
    /// <returns>Task representing the event handling operation</returns>
    virtual async Task OnEventReceivedAsync<T>(string topic, T eventData) where T : class
    {
        await Task.CompletedTask;
        // Default empty implementation
    }

    /// <summary>
    /// Gets all discovered service types with their attributes.
    /// </summary>
    public static (Type, Type, BannouServiceAttribute)[] Services
    {
        get
        {
            if (_services == null)
            {
                List<(Type, BannouServiceAttribute)> serviceProviders = IServiceAttribute.GetClassesWithAttribute<BannouServiceAttribute>();
                if (!serviceProviders.Any())
                {
                    Program.Logger?.Log(LogLevel.Trace, null, "No service handler types were found");
                    return Array.Empty<(Type, Type, BannouServiceAttribute)>();
                }

                var handlerLookup = new Dictionary<Type, (Type, Type, BannouServiceAttribute)>();
                foreach ((Type, BannouServiceAttribute) serviceProvider in serviceProviders)
                {
                    Type serviceType = serviceProvider.Item1;
                    BannouServiceAttribute serviceAttr = serviceProvider.Item2;
                    Type handlerType = serviceAttr.InterfaceType ?? serviceType;

                    Program.Logger?.Log(LogLevel.Trace, null, "Checking service type {ServiceType}", serviceType.Name);

                    if (serviceType.IsAbstract || serviceType.IsInterface || !serviceType.IsAssignableTo(typeof(IBannouService)))
                    {
                        Program.Logger?.Log(LogLevel.Debug, null, "Invalid service type {ServiceType} won't be returned", serviceType.Name);
                        continue;
                    }

                    if (!handlerType.IsAssignableTo(typeof(IBannouService)))
                    {
                        Program.Logger?.Log(LogLevel.Debug, null, "Invalid handler for service type {ServiceType}", serviceType.Name);
                        continue;
                    }

                    if (handlerLookup.TryGetValue(handlerType, out (Type, Type, BannouServiceAttribute) existingEntry))
                    {
                        if (existingEntry.Item3.Priority)
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, "Service type {ServiceType} skipped in favour of existing override {ExistingType}", serviceType.Name, existingEntry.Item2.Name);
                            continue;
                        }

                        if (existingEntry.Item2.IsAssignableTo(serviceType))
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, "Service type {ServiceType} skipped in favour of existing more-derived type {ExistingType}", serviceType.Name, existingEntry.Item2.Name);
                            continue;
                        }

                        // derived types get automatic priority
                        if (existingEntry.Item2.IsAssignableFrom(serviceType))
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, "Service type {ServiceType} is more derived than existing type {ExistingType}, and will override it", serviceType.Name, existingEntry.Item2.Name);
                            _ = handlerLookup.Remove(handlerType);
                        }
                    }

                    handlerLookup[handlerType] = (handlerType, serviceType, serviceAttr);
                }

                _services = handlerLookup.Values.ToArray();
            }

            return _services;
        }
    }

    /// <summary>
    /// Gets all enabled service types (not disabled via configuration).
    /// </summary>
    public static (Type, Type, BannouServiceAttribute)[] EnabledServices
    {
        get
        {
            var enabledServiceInfo = new List<(Type, Type, BannouServiceAttribute)>();
            foreach (var serviceInfo in Services)
            {
                var serviceName = serviceInfo.Item3.Name;
                if (!IsDisabled(serviceName))
                    enabledServiceInfo.Add(serviceInfo);
            }

            return enabledServiceInfo.ToArray();
        }
    }

    private static IDictionary<string, IList<(Type, Type, BannouServiceAttribute)>> _serviceAppMappings;

    /// <summary>
    /// Lookup for mapping applications to services in the Bannou network.
    /// Is applied as an override to hardcoded and configurable network_mode presets.
    ///
    /// <seealso cref="NetworkModePresets"/>
    /// </summary>
    public static IDictionary<string, IList<(Type, Type, BannouServiceAttribute)>> ServiceAppMappings
    {
        get
        {
            if (_serviceAppMappings == null)
            {
                _serviceAppMappings = new Dictionary<string, IList<(Type, Type, BannouServiceAttribute)>>();
                foreach ((Type, Type, BannouServiceAttribute) serviceHandler in Services)
                {
                    var serviceName = serviceHandler.Item3.Name;
                    var appName = Program.ConfigurationRoot.GetValue<string>(serviceName.ToUpper() + "_APP_MAPPING") ?? AppConstants.DEFAULT_APP_NAME;

                    if (_serviceAppMappings.TryGetValue(appName, out IList<(Type, Type, BannouServiceAttribute)>? existingApp))
                    {
                        existingApp.Add(serviceHandler);
                        continue;
                    }

                    _serviceAppMappings.Add(appName, new List<(Type, Type, BannouServiceAttribute)>() { serviceHandler });
                }
            }

            return _serviceAppMappings;
        }
    }

    private static Dictionary<string, string> _networkModePresets;

    /// <summary>
    /// The service->application name mappings for the network.
    /// Specific to the network mode that's been configured.
    ///
    /// Set the `NETWORK_MODE` ENV or `--network-mode` switch to select the preset
    /// mappings to use.
    /// <seealso cref="ServiceAppMappings"/>
    /// </summary>
    public static Dictionary<string, string> NetworkModePresets
    {
        get
        {
            if (_networkModePresets != null)
                return _networkModePresets;

            var networkMode = Program.Configuration?.NetworkMode;
            if (networkMode != null)
            {
                if (networkMode.EndsWith("-scaling", StringComparison.InvariantCultureIgnoreCase))
                {
                    var scaledService = networkMode[..^"-scaling".Length]?.ToUpper();
                    if (!string.IsNullOrWhiteSpace(scaledService))
                    {
                        _networkModePresets = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
                        {
                            [scaledService] = scaledService
                        };

                        return _networkModePresets;
                    }
                    else
                        Program.Logger?.Log(LogLevel.Error, null, "Couldn't determine service to scale for network mode {NetworkMode}", networkMode);
                }

                if (networkMode.IsSafeForPath())
                {
                    var configDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "presets");
                    var configFilePath = Path.Combine(configDirectoryPath, networkMode + ".json");
                    try
                    {
                        if (Directory.Exists(configDirectoryPath) && File.Exists(configFilePath))
                        {
                            var configStr = File.ReadAllText(configFilePath);
                            if (configStr != null)
                            {
                                var configPresets = BannouJson.Deserialize<Dictionary<string, string>>(configStr);
                                if (configPresets != null)
                                {
                                    _networkModePresets = new Dictionary<string, string>(configPresets, StringComparer.InvariantCultureIgnoreCase);
                                    return _networkModePresets;
                                }
                                else
                                    Program.Logger?.Log(LogLevel.Error, null, "Failed to parse json configuration for network mode {NetworkMode} presets at path: {ConfigFilePath}", networkMode, configFilePath);
                            }
                            else
                                Program.Logger?.Log(LogLevel.Error, null, "Failed to read configuration file for network mode {NetworkMode} presets at path: {ConfigFilePath}", networkMode, configFilePath);
                        }
                        else
                            Program.Logger?.Log(LogLevel.Information, null, "No custom configuration file found for network mode {NetworkMode} presets at path: {ConfigFilePath}", networkMode, configFilePath);
                    }
                    catch (Exception exc)
                    {
                        Program.Logger?.Log(LogLevel.Error, exc, "An exception occurred loading network mode {NetworkMode} presets at path: {ConfigFilePath}", networkMode, configFilePath);
                    }
                }
                else
                    Program.Logger?.Log(LogLevel.Warning, null, "Network mode {NetworkMode} contains characters unfit for automated loading of presets", networkMode);
            }
            else
                Program.Logger?.Log(LogLevel.Information, null, "Network mode not set - using default service mapping presets");

            _networkModePresets = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) { };
            return _networkModePresets;
        }

        private set => _networkModePresets = value;
    }

    /// <summary>
    /// Called when the service is starting up. Override to implement custom startup logic.
    /// </summary>
    /// <param name="token">Cancellation token for startup timeout.</param>
    async Task OnStartAsync(CancellationToken token)
    {
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Information, null, "Service {ServiceName} starting up", serviceName);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Called when the service is starting up with access to the WebApplication.
    /// Override to register minimal API endpoints or other startup logic requiring the web app.
    /// </summary>
    /// <param name="webApp">The WebApplication instance for registering endpoints.</param>
    /// <param name="token">Cancellation token for startup timeout.</param>
    async Task OnStartAsync(WebApplication webApp, CancellationToken token)
    {
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Information, null, "Service {ServiceName} starting up with WebApplication", serviceName);
        // Default implementation calls the simpler OnStartAsync
        await OnStartAsync(token);
    }

    /// <summary>
    /// Called when the service is running and ready. Override to implement background processing.
    /// </summary>
    /// <param name="token">Cancellation token for shutdown.</param>
    async Task OnRunningAsync(CancellationToken token)
    {
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Debug, null, "{ServiceName} service running", serviceName);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Called when the service is shutting down. Override to implement cleanup logic.
    /// </summary>
    async Task OnShutdownAsync()
    {
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Information, null, "{ServiceName} service shutting down", serviceName);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the service name from the service type attributes.
    /// </summary>
    /// <returns>The service name if found, otherwise null.</returns>
    public string? GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Registers service permissions with the Permissions service on startup.
    /// This method is automatically called by PluginLoader and should be generated
    /// based on x-permissions sections in the service's OpenAPI schema.
    /// Override this method if the service has custom permission registration logic.
    /// </summary>
    /// <returns>Task representing the registration operation</returns>
    virtual async Task RegisterServicePermissionsAsync()
    {
        await Task.CompletedTask;
        // Default implementation does nothing - method will be overridden
        // by generated code when x-permissions sections are found in schema
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Debug, null, "Service {ServiceName} has no permission registration (no x-permissions in schema)", serviceName);
    }

    /// <summary>
    /// Registers event consumers for pub/sub events this service wants to handle.
    /// This method should be called from the service constructor with the injected IEventConsumer.
    /// Override in {Service}ServiceEvents.cs partial class to register event handlers.
    /// Generated by generate-event-subscriptions.sh based on x-subscribes-to in OpenAPI schema.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    /// <remarks>
    /// Handler registration is idempotent - calling multiple times with the same handler key
    /// will only register once. Handlers are invoked when any plugin's EventsController
    /// receives the corresponding topic event.
    /// </remarks>
    virtual void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Default implementation does nothing - method will be overridden
        // by {Service}ServiceEvents.cs partial class when x-subscribes-to is found in schema
        var serviceName = GetName() ?? GetType().Name;
        Program.Logger?.Log(LogLevel.Debug, null, "Service {ServiceName} has no event consumers (no x-subscribes-to in schema)", serviceName);
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public bool IsDisabled()
        => IsDisabled(GetType());

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsDisabled<T>()
        => IsDisabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public static bool IsDisabled(Type serviceType)
    {
        if (!typeof(IBannouService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IBannouService)}");

        var serviceName = serviceType.GetServiceName();
        return IsDisabled(serviceName);
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// Uses the two-mode enable/disable system based on SERVICES_ENABLED environment variable.
    /// </summary>
    public static bool IsDisabled(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return !Program.Configuration.ServicesEnabled;

        // Use the same two-mode logic as PluginLoader.IsServiceEnabled, but inverted
        var servicesEnabledEnv = Environment.GetEnvironmentVariable("SERVICES_ENABLED");
        var globalServicesEnabled = string.IsNullOrWhiteSpace(servicesEnabledEnv) ?
            Program.Configuration.ServicesEnabled :
            string.Equals(servicesEnabledEnv, "true", StringComparison.OrdinalIgnoreCase);

        var serviceNameUpper = serviceName.ToUpper();

        if (globalServicesEnabled)
        {
            // Mode 1: SERVICES_ENABLED=true → all services enabled by default, use X_SERVICE_DISABLED to disable individual
            var disabledEnv = Environment.GetEnvironmentVariable($"{serviceNameUpper}_SERVICE_DISABLED");
            var isDisabled = string.Equals(disabledEnv, "true", StringComparison.OrdinalIgnoreCase);
            return isDisabled;
        }
        else
        {
            // Mode 2: SERVICES_ENABLED=false → all services disabled by default, use X_SERVICE_ENABLED to enable individual
            var enabledEnv = Environment.GetEnvironmentVariable($"{serviceNameUpper}_SERVICE_ENABLED");
            var isEnabled = string.Equals(enabledEnv, "true", StringComparison.OrdinalIgnoreCase);
            return !isEnabled; // Inverted because this method returns "IsDisabled"
        }
    }

    /// <summary>
    /// Find the highest priority/derived service type with the given name.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, BannouServiceAttribute)? GetServiceInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach ((Type, Type, BannouServiceAttribute) serviceInfo in Services)
        {
            if (string.Equals(name, serviceInfo.Item3.Name, StringComparison.InvariantCultureIgnoreCase))
                return serviceInfo;
        }

        return null;
    }

    /// <summary>
    /// Find the implementation for the given service handler.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, BannouServiceAttribute)? GetServiceInfo(Type interfaceType)
    {
        foreach ((Type, Type, BannouServiceAttribute) serviceInfo in Services)
        {
            if (serviceInfo.Item1 == interfaceType)
                return serviceInfo;
        }

        return null;
    }
}
