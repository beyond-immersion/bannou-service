using BeyondImmersion.BannouService.Configuration;

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
    private static (Type, Type, DaprServiceAttribute)[]? _services;
    public static (Type, Type, DaprServiceAttribute)[] Services
    {
        get
        {
            if (_services == null)
            {
                List<(Type, DaprServiceAttribute)> serviceProviders = IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
                if (!serviceProviders.Any())
                {
                    Program.Logger?.Log(LogLevel.Trace, null, $"No service handler types were found.");
                    return Array.Empty<(Type, Type, DaprServiceAttribute)>();
                }

                var handlerLookup = new Dictionary<Type, (Type, Type, DaprServiceAttribute)>();
                foreach ((Type, DaprServiceAttribute) serviceProvider in serviceProviders)
                {
                    Type serviceType = serviceProvider.Item1;
                    DaprServiceAttribute serviceAttr = serviceProvider.Item2;
                    Type handlerType = serviceAttr.InterfaceType ?? serviceType;

                    Program.Logger?.Log(LogLevel.Trace, null, $"Checking service type {serviceType.Name}...");

                    if (serviceType.IsAbstract || serviceType.IsInterface || !serviceType.IsAssignableTo(typeof(IDaprService)))
                    {
                        Program.Logger?.Log(LogLevel.Debug, null, $"Invalid service type {serviceType.Name} won't be returned.");
                        continue;
                    }

                    if (!handlerType.IsAssignableTo(typeof(IDaprService)))
                    {
                        Program.Logger?.Log(LogLevel.Debug, null, $"Invalid handler for service type {serviceType.Name}.");
                        continue;
                    }

                    if (handlerLookup.TryGetValue(handlerType, out (Type, Type, DaprServiceAttribute) existingEntry))
                    {
                        if (existingEntry.Item3.Priority)
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} skipped in favour of existing override {existingEntry.Item2.Name}.");
                            continue;
                        }

                        if (existingEntry.Item2.IsAssignableTo(serviceType))
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} skipped in favour of existing more-derived type {existingEntry.Item2.Name}.");
                            continue;
                        }

                        // derived types get automatic priority
                        if (existingEntry.Item2.IsAssignableFrom(serviceType))
                        {
                            Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} is more derived than existing type {existingEntry.Item2.Name}, and will override it.");
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

    public static (Type, Type, DaprServiceAttribute)[] EnabledServices
    {
        get
        {
            var enabledServiceInfo = new List<(Type, Type, DaprServiceAttribute)>();
            foreach (var serviceInfo in Services)
            {
                var serviceName = serviceInfo.Item3.Name;
                if (!IsDisabled(serviceName))
                    enabledServiceInfo.Add(serviceInfo);
            }

            return enabledServiceInfo.ToArray();
        }
    }

    private static IDictionary<string, IList<(Type, Type, DaprServiceAttribute)>> _serviceAppMappings;
    /// <summary>
    /// Lookup for mapping applications to services in the Dapr network.
    /// Is applied as an override to hardcoded and configurable network_mode presets.
    /// 
    /// <seealso cref="NetworkModePresets"/>
    /// </summary>
    public static IDictionary<string, IList<(Type, Type, DaprServiceAttribute)>> ServiceAppMappings
    {
        get
        {
            if (_serviceAppMappings == null)
            {
                _serviceAppMappings = new Dictionary<string, IList<(Type, Type, DaprServiceAttribute)>>();
                foreach ((Type, Type, DaprServiceAttribute) serviceHandler in Services)
                {
                    var serviceName = serviceHandler.Item3.Name;
                    var appName = Program.ConfigurationRoot.GetValue<string>(serviceName.ToUpper() + "_APP_MAPPING") ?? AppConstants.DEFAULT_APP_NAME;

                    if (_serviceAppMappings.TryGetValue(appName, out IList<(Type, Type, DaprServiceAttribute)>? existingApp))
                    {
                        existingApp.Add(serviceHandler);
                        continue;
                    }

                    _serviceAppMappings.Add(appName, new List<(Type, Type, DaprServiceAttribute)>() { serviceHandler });
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

            var networkMode = Program.Configuration?.Network_Mode;
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
                        Program.Logger.Log(LogLevel.Error, null, $"Couldn't determine service to scale for network mode '{networkMode}'.");
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
                                var configPresets = JsonConvert.DeserializeObject<Dictionary<string, string>>(configStr);
                                if (configPresets != null)
                                {
                                    _networkModePresets = new Dictionary<string, string>(configPresets, StringComparer.InvariantCultureIgnoreCase);
                                    return _networkModePresets;
                                }
                                else
                                    Program.Logger.Log(LogLevel.Error, null, $"Failed to parse json configuration for network mode '{networkMode}' presets at path: {configFilePath}.");
                            }
                            else
                                Program.Logger.Log(LogLevel.Error, null, $"Failed to read configuration file for network mode '{networkMode}' presets at path: {configFilePath}.");
                        }
                        else
                            Program.Logger.Log(LogLevel.Information, null, $"No custom configuration file found for network mode '{networkMode}' presets at path: {configFilePath}.");
                    }
                    catch (Exception exc)
                    {
                        Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred loading network mode '{networkMode}' presets at path: {configFilePath}.");
                    }
                }
                else
                    Program.Logger.Log(LogLevel.Warning, null, $"Network mode '{networkMode}' contains characters unfit for automated loading of presets.");
            }
            else
                Program.Logger.Log(LogLevel.Information, null, $"Network mode not set- using default service mapping presets.");

            _networkModePresets = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) { };
            return _networkModePresets;
        }

        private set => _networkModePresets = value;
    }

    async Task OnStartAsync(CancellationToken token)
    {
        await Task.CompletedTask;
    }

    async Task OnRunningAsync(CancellationToken token)
    {
        await Task.CompletedTask;
    }

    async Task OnShutdownAsync()
    {
        await Task.CompletedTask;
    }

    public string? GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public bool IsDisabled()
        => IsDisabled(GetType());

    /// <summary>
    /// Returns the best configuration type for this service type.
    /// </summary>
    public Type GetConfigurationType()
        => GetConfigurationType(GetType());

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredForType(GetConfigurationType());

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration<T>()
        where T : class, IDaprService
        => HasRequiredConfiguration(typeof(T));

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration(Type implementationType)
    {
        return typeof(IDaprService).IsAssignableFrom(implementationType)
            && IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>()
                .Where(t => t.Item2.ServiceImplementationType == implementationType)
                .All(t => IServiceConfiguration.HasRequiredForType(t.Item1));
    }

    /// <summary>
    /// Returns the best service configuration type for the given service type.
    /// Returned type is based on DaprServiceAttribute service target type.
    /// </summary>
    public static Type GetConfigurationType(Type implementationType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(implementationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        Type? serviceConfigType = null;
        foreach ((Type, ServiceConfigurationAttribute) configAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
        {
            if (implementationType == configAttr.Item2.ServiceImplementationType)
            {
                if (serviceConfigType != null)
                    continue;

                serviceConfigType = configAttr.Item1;
            }
        }

        return serviceConfigType ?? typeof(AppConfiguration);
    }

    /// <summary>
    /// Returns whether the configuration indicates ANY services should be enabled.
    /// </summary>
    public static bool IsAnyEnabled()
        => EnabledServices.Any();

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
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        var serviceName = serviceType.GetServiceName();
        return IsDisabled(serviceName);
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public static bool IsDisabled(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return !Program.Configuration.Services_Enabled;

        var config = IServiceConfiguration.BuildConfiguration(typeof(BaseServiceConfiguration), null, serviceName.ToUpper() + "_");
        return config?.Service_Disabled == null ? !Program.Configuration.Services_Enabled : config.Service_Disabled.Value;
    }

    /// <summary>
    /// Find the highest priority/derived service type with the given name.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, DaprServiceAttribute)? GetServiceInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach ((Type, Type, DaprServiceAttribute) serviceInfo in Services)
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
    public static (Type, Type, DaprServiceAttribute)? GetServiceInfo(Type interfaceType)
    {
        foreach ((Type, Type, DaprServiceAttribute) serviceInfo in Services)
        {
            if (serviceInfo.Item1 == interfaceType)
                return serviceInfo;
        }

        return null;
    }

    /// <summary>
    /// Returns the application name mapped for the given service name, from the
    /// network mode preset.
    /// 
    /// Set the `NETWORK_MODE` ENV or `--network-mode` switch to select the preset
    /// mappings to use.
    /// </summary>
    public static string GetPresetAppNameFromServiceName(string serviceName)
        => NetworkModePresets.TryGetValue(serviceName, out var presetAppName) ? presetAppName : "bannou";

    public static string[] GetServicePresetsByAppName(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return Array.Empty<string>();

        // list all possible services, then determine which ones are handled
        // - the rest are all handled by "bannou"
        if (string.Equals("bannou", appName, StringComparison.InvariantCultureIgnoreCase))
        {
            var serviceList = new List<string>();
            foreach (var serviceInfo in IDaprService.Services)
                serviceList.Add(serviceInfo.Item3.Name);

            var unhandledServiceList = new List<string>();
            foreach (var serviceItem in serviceList)
                if (!NetworkModePresets.ContainsKey(serviceItem))
                    unhandledServiceList.Add(serviceItem);

            return unhandledServiceList.ToArray();
        }

        var presetList = NetworkModePresets.Where(t => t.Value.Equals(appName, StringComparison.InvariantCultureIgnoreCase)).Select(t => t.Key).ToArray();
        return presetList;
    }

    /// <summary>
    /// Get the app name for the given service interface type.
    /// </summary>
    public static string? GetAppByServiceInterfaceType(Type interfaceType)
    {
        if (!interfaceType.IsAssignableTo(typeof(IDaprService)))
            return null;

        var serviceAppList = ServiceAppMappings.Where(t => t.Value.Any(s => s.Item1 == interfaceType));
        if (serviceAppList.Any())
            return serviceAppList.First().Key;

        var serviceName = interfaceType.GetServiceName();
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            var serviceApp = GetPresetAppNameFromServiceName(serviceName);
            if (!string.IsNullOrWhiteSpace(serviceApp))
                return serviceApp;
        }

        return null;
    }

    /// <summary>
    /// Get the app name for the given service implementation type.
    /// </summary>
    public static string? GetAppByServiceImplementationType(Type implementationType)
    {
        if (!implementationType.IsAssignableTo(typeof(IDaprService)))
            return null;

        var serviceAppList = ServiceAppMappings.Where(t => t.Value.Any(s => s.Item2 == implementationType));
        if (serviceAppList.Any())
            return serviceAppList.First().Key;

        var serviceName = implementationType.GetServiceName();
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            var serviceApp = GetPresetAppNameFromServiceName(serviceName);
            if (!string.IsNullOrWhiteSpace(serviceApp))
                return serviceApp;
        }

        return null;
    }

    /// <summary>
    /// Get the app name for the given service name.
    /// 
    /// The service name is primarily obtained from the DaprService
    /// attribute attached to the implementation type.
    /// </summary>
    public static string? GetAppByServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return null;

        var serviceAppList = ServiceAppMappings.Where(t => t.Value.Any(s => string.Equals(serviceName, s.Item3.Name, StringComparison.InvariantCultureIgnoreCase)));
        if (serviceAppList.Any())
            return serviceAppList.First().Key;

        var serviceApp = GetPresetAppNameFromServiceName(serviceName);
        if (!string.IsNullOrWhiteSpace(serviceApp))
            return serviceApp;

        return null;
    }
}
