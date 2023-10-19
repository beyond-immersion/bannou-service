using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Logging;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: ApiController]
[assembly: InternalsVisibleTo("unit-tests")]
namespace BeyondImmersion.BannouService;

public static class Program
{
    private static IConfigurationRoot _configurationRoot;
    /// <summary>
    /// Shared service configuration root.
    /// Includes command line args.
    /// </summary>
    public static IConfigurationRoot ConfigurationRoot
    {
        get
        {
            _configurationRoot ??= IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());

            return _configurationRoot;
        }

        internal set => _configurationRoot = value;
    }

    private static AppConfiguration _configuration;
    /// <summary>
    /// Service configuration.
    /// 
    /// Pull from:
    ///     Config.json,
    ///     ENVs,
    ///     command line args.
    /// </summary>
    public static AppConfiguration Configuration
    {
        get
        {
            _configuration ??= ConfigurationRoot.Get<AppConfiguration>() ?? new AppConfiguration();

            return _configuration;
        }

        internal set => _configuration = value;
    }

    private static string _serviceGUID;
    /// <summary>
    /// Internal service GUID- largely used for administrative network commands.
    /// Randomly generated on service startup.
    /// </summary>
    public static string ServiceGUID
    {
        get
        {
            _serviceGUID ??= Configuration.Force_Service_ID ?? Guid.NewGuid().ToString().ToLower();

            return _serviceGUID;
        }

        internal set => _serviceGUID = value;
    }

    private static ILogger _logger;
    /// <summary>
    /// Service logger.
    /// </summary>
    public static ILogger Logger
    {
        get
        {
            if (_logger == null)
            {
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                var isDevelopment = string.Equals(Environments.Development, environment, StringComparison.CurrentCultureIgnoreCase);
                _logger = isDevelopment ? ServiceLogging.CreateSimpleLogger() : ServiceLogging.CreateLogger();
            }

            return _logger;
        }

        internal set => _logger = value;
    }

    private static IDictionary<string, IList<(Type, Type, DaprServiceAttribute)>> _serviceAppLookup;
    /// <summary>
    /// 
    /// </summary>
    public static IDictionary<string, IList<(Type, Type, DaprServiceAttribute)>> ServiceAppLookup
    {
        get
        {
            if (_serviceAppLookup == null)
            {
                _serviceAppLookup = new Dictionary<string, IList<(Type, Type, DaprServiceAttribute)>>();
                foreach ((Type, Type, DaprServiceAttribute) serviceHandler in IDaprService.GetAllServiceInfo(enabledOnly: false))
                {
                    var serviceName = serviceHandler.Item3.Name;
                    var appName = ConfigurationRoot.GetValue<string>(serviceName.ToUpper() + "_APP_MAPPING") ?? AppConstants.DEFAULT_APP_NAME;

                    if (_serviceAppLookup.TryGetValue(appName, out IList<(Type, Type, DaprServiceAttribute)>? existingApp))
                    {
                        existingApp.Add(serviceHandler);
                        continue;
                    }

                    _serviceAppLookup.Add(appName, new List<(Type, Type, DaprServiceAttribute)>() { serviceHandler });
                }
            }

            return _serviceAppLookup;
        }
    }

    private static Dictionary<string, string> _networkModePresets;
    /// <summary>
    /// The service->application name mappings for the network.
    /// Specific to the network mode that's been configured.
    /// 
    /// Set the `NETWORK_MODE` ENV or `--network-mode` switch to select the preset
    /// mappings to use.
    /// </summary>
    public static Dictionary<string, string> NetworkModePresets
    {
        get
        {
            if (_networkModePresets != null)
                return _networkModePresets;

            // build presets based on network mode and configuration
            if (Configuration?.Network_Mode != null)
            {
                if (Configuration.Network_Mode.EndsWith("-scaling", StringComparison.InvariantCultureIgnoreCase))
                {
                    var scaledService = Configuration.Network_Mode[..^"-scaling".Length]?.ToUpper();
                    if (!string.IsNullOrWhiteSpace(scaledService))
                    {
                        _networkModePresets = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
                        {
                            [scaledService] = scaledService
                        };

                        return _networkModePresets;
                    }
                    else
                        Logger.Log(LogLevel.Error, null, $"Couldn't determine service to scale for network mode '{Configuration.Network_Mode}'.");
                }

                if (Configuration.Network_Mode.IsSafeForPath())
                {
                    var configDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "presets");
                    var configFilePath = Path.Combine(configDirectoryPath, Configuration.Network_Mode + ".json");
                    try
                    {
                        if (Directory.Exists(configDirectoryPath) && File.Exists(configFilePath))
                        {
                            var configStr = File.ReadAllText(configFilePath);
                            if (configStr != null)
                            {
                                var configPresets = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(configStr);
                                if (configPresets != null)
                                {
                                    _networkModePresets = new Dictionary<string, string>(configPresets, StringComparer.InvariantCultureIgnoreCase);
                                    return _networkModePresets;
                                }
                                else
                                    Logger.Log(LogLevel.Error, null, $"Failed to parse json configuration for network mode '{Configuration.Network_Mode}' presets at path: {configFilePath}.");
                            }
                            else
                                Logger.Log(LogLevel.Error, null, $"Failed to read configuration file for network mode '{Configuration.Network_Mode}' presets at path: {configFilePath}.");
                        }
                        else
                            Logger.Log(LogLevel.Information, null, $"No custom configuration file found for network mode '{Configuration.Network_Mode}' presets at path: {configFilePath}.");
                    }
                    catch (Exception exc)
                    {
                        Logger.Log(LogLevel.Error, exc, $"An exception occurred loading network mode '{Configuration.Network_Mode}' presets at path: {configFilePath}.");
                    }
                }
                else
                    Logger.Log(LogLevel.Warning, null, $"Network mode '{Configuration.Network_Mode}' contains characters unfit for automated loading of presets.");
            }
            else
                Logger.Log(LogLevel.Information, null, $"Network mode not set- using default service mapping presets.");

            _networkModePresets = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) { };
            return _networkModePresets;
        }

        private set => _networkModePresets = value;
    }

    /// <summary>
    /// Shared dapr client interface, used by all enabled service handlers.
    /// </summary>
    public static DaprClient DaprClient { get; private set; }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    private static async Task Main(string[] args)
    {
        Logger.Log(LogLevel.Information, null, "Service starting.");

        // configuration is auto-created on first get, so this call creates the config too
        if (Configuration == null)
        {
            Logger.Log(LogLevel.Error, null, "Service configuration missing- exiting application.");
            return;
        }

        // load the assemblies
        LoadAssemblies();

        // get info for dapr services in loaded assemblies
        var enabledServiceInfo = IDaprService.GetAllServiceInfo(enabledOnly: true);
        if (enabledServiceInfo == null || enabledServiceInfo.Length == 0)
        {
            Logger.Log(LogLevel.Error, null, "No services have been enabled- exiting application.");
            return;
        }

        // ensure dapr services have their required configuration
        if (!IDaprService.AllHaveRequiredConfiguration(enabledServiceInfo))
        {
            Logger.Log(LogLevel.Error, null, "Required configuration not set for enabled services- exiting application.");
            return;
        }

        Logger.Log(LogLevel.Information, null, "Configuration built and validated.");

        // build the dapr client
        DaprClient = new DaprClientBuilder()
            .UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig)
            .Build();

        // prepare to build the application
        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(args);
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create WebApplicationBuilder- exiting application.");
            return;
        }

        try
        {
            // configure services
            _ = webAppBuilder.Services.AddAuthentication();
            _ = webAppBuilder.Services.AddControllers();
            webAppBuilder.Services.AddDaprClient();
            webAppBuilder.Services.AddDaprServices(enabledServiceInfo);

            // configure webhost
            webAppBuilder.WebHost
                .UseKestrel((kestrelOptions) =>
                {
                    kestrelOptions.ListenAnyIP(Configuration.HTTP_Web_Host_Port);
                    kestrelOptions.ListenAnyIP(Configuration.HTTPS_Web_Host_Port, (listenOptions) =>
                    {
                        listenOptions.UseHttps((httpsOptions) =>
                        {
                            httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                        });
                    });
                })
                .UseConfiguration(ConfigurationRoot)
                .UseSetting(WebHostDefaults.SuppressStatusMessagesKey, "True")
                .ConfigureLogging((loggingOptions) =>
                {
                    loggingOptions
                        .AddSimpleConsole()
                        .SetMinimumLevel(Configuration.Web_Host_Logging_Level);
                });
        }
        catch (Exception exc)
        {
            Logger.Log(LogLevel.Error, exc, "Failed to add required services to registry- exiting application.");
            return;
        }

        // build the application
        WebApplication webApp = webAppBuilder.Build();
        try
        {
            // add controllers / configure navigation
            _ = webApp.MapNonServiceControllers();
            _ = webApp.MapDaprServiceControllers(enabledServiceInfo);
            _ = webApp.UseHttpsRedirection();

            // enable websocket connections
            webApp.UseWebSockets(new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            // invoke all Service.Start() methods on enabled service handlers
            var serviceImplTypes = enabledServiceInfo.Select(t => t.Item2);
            await InvokeAllServiceStartMethods(webApp, serviceImplTypes);

            Logger.Log(LogLevel.Information, null, "Services added and initialized successfully- WebHost starting.");

            // start webhost
            var webHostTask = webApp.RunAsync(ShutdownCancellationTokenSource.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // invoke all Service.Running() methods on enabled service handlers
            await InvokeAllServiceRunningMethods(webApp, serviceImplTypes);

            Logger.Log(LogLevel.Information, null, "WebHost started successfully and services running- settling in.");

            // !!! block here until token cancelled or webhost crashes
            await webHostTask;

            Logger.Log(LogLevel.Information, null, "WebHost stopped- starting controlled application shutdown.");

            // invoke all Service.Shutdown() methods on enabled service handlers
            await InvokeAllServiceShutdownMethods(webApp, serviceImplTypes);
        }
        catch (Exception exc)
        {
            Logger.Log(LogLevel.Error, exc, "A critical error has occurred- starting application shutdown.");
            ShutdownCancellationTokenSource.Cancel();
        }
        finally
        {
            // perform cleanup
            await webApp.DisposeAsync();
            DaprClient.Dispose();
        }

        Logger.Log(LogLevel.Debug, null, "Application shutdown complete.");
    }

    private static void LoadAssemblies()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        if (string.Equals("none", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
            return;

        var libsRootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        if (libsRootDirectory == null || !Directory.Exists(libsRootDirectory))
        {
            Logger.Log(LogLevel.Warning, null, $"Failed to load additional assemblies- libs directory does not exist.");
            return;
        }

        var libDirectories = Directory.GetDirectories(libsRootDirectory);
        if (libDirectories == null || libDirectories.Length == 0)
        {
            Logger.Log(LogLevel.Warning, null, $"Failed to load additional assemblies- nothing found in libs directory.");
            return;
        }

        if (string.Equals("all", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            foreach (var libDirectory in libDirectories)
            {
                var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
                foreach (var assemblyPath in assemblyPaths)
                {
                    try
                    {
                        Assembly.LoadFrom(assemblyPath);
                        Logger.Log(LogLevel.Information, null, $"Successfully loaded assembly at path: {assemblyPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Error, ex, $"Failed to load assembly at path: {assemblyPath}.");
                    }
                }
            }

            return;
        }

        // common libs should always be loaded, if `none` isn't specified
        {
            var libDirectory = Directory.GetDirectories(libsRootDirectory, "common", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (libDirectory != null)
            {
                var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
                foreach (var assemblyPath in assemblyPaths)
                {
                    try
                    {
                        var loadedAssembly = Assembly.LoadFile(assemblyPath);
                        Logger.Log(LogLevel.Information, null, $"Successfully loaded assembly at path: {assemblyPath}.");
                    }
                    catch (Exception exc)
                    {
                        Logger.Log(LogLevel.Error, exc, $"Failed to load assembly at path: {assemblyPath}.");
                    }
                }
            }
        }

        // if no configuration, or common, then that's all we're going to load
        if (Configuration.Include_Assemblies == null || string.Equals("common", Configuration.Include_Assemblies))
            return;

        // otherwise, split by commas, and load the rest manually by assembly name
        var assemblyNames = Configuration.Include_Assemblies.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (assemblyNames == null || assemblyNames.Length == 0)
            return;

        foreach (var assemblyName in assemblyNames)
        {
            var libDirectory = Directory.GetDirectories(libsRootDirectory, assemblyName, SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (libDirectory == null)
            {
                Logger.Log(LogLevel.Warning, null, $"Failed to load assemblies for {assemblyName}- specified in configuration, but no libs were found.");
                continue;
            }

            var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
            foreach (var assemblyPath in assemblyPaths)
            {
                try
                {
                    var loadedAssembly = Assembly.LoadFile(assemblyPath);
                    Logger.Log(LogLevel.Information, null, $"Successfully loaded assembly at path: {assemblyPath}.");
                }
                catch (Exception exc)
                {
                    Logger.Log(LogLevel.Error, exc, $"Failed to load assembly at path: {assemblyPath}.");
                }
            }
        }

        return;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args?.Name))
            return null;

        var assemblyName = new AssemblyName(args.Name).Name;
        var libsRootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        var libDirectories = Directory.GetDirectories(libsRootDirectory);

        foreach (var libDirectory in libDirectories)
        {
            var potentialAssemblyPath = Path.Combine(libDirectory, $"{assemblyName}.dll");
            if (File.Exists(potentialAssemblyPath))
            {
                try
                {
                    var loadedAssembly = Assembly.LoadFile(potentialAssemblyPath);
                    Logger.Log(LogLevel.Error, null, $"Successfully loaded assembly {assemblyName} at path: {potentialAssemblyPath}.");

                    return loadedAssembly;
                }
                catch (Exception exc)
                {
                    Logger.Log(LogLevel.Error, exc, $"Failed to load assembly {assemblyName} at path: {potentialAssemblyPath}.");
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Iterates through and invokes the Start() method on all loaded service handlers.
    /// </summary>
    private static async Task InvokeAllServiceStartMethods(WebApplication webApp, IEnumerable<Type> implTypes)
    {
        foreach (var implType in implTypes)
        {
            var serviceInst = (IDaprService?)webApp.Services.GetService(implType);
            if (serviceInst != null)
                await serviceInst.OnStart();
        }
    }

    /// <summary>
    /// Iterates through and invokes the Running() method on all loaded service handlers.
    /// </summary>
    private static async Task InvokeAllServiceRunningMethods(WebApplication webApp, IEnumerable<Type> implTypes)
    {
        foreach (var implType in implTypes)
        {
            var serviceInst = (IDaprService?)webApp.Services.GetService(implType);
            if (serviceInst != null)
                await serviceInst.OnRunning();
        }
    }

    /// <summary>
    /// Iterates through and invokes the Shutdown() method on all loaded service handlers.
    /// </summary>
    private static async Task InvokeAllServiceShutdownMethods(WebApplication webApp, IEnumerable<Type> implTypes)
    {
        foreach (var implType in implTypes)
        {
            var serviceInst = (IDaprService?)webApp.Services.GetService(implType);
            if (serviceInst != null)
                await serviceInst.OnShutdown();
        }
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
            var allServiceInfo = IDaprService.GetAllServiceInfo();
            if (allServiceInfo != null)
            {
                var serviceList = new List<string>();
                foreach (var serviceInfo in allServiceInfo)
                    serviceList.Add(serviceInfo.Item3.Name);

                var unhandledServiceList = new List<string>();
                foreach (var serviceItem in serviceList)
                    if (!NetworkModePresets.ContainsKey(serviceItem))
                        unhandledServiceList.Add(serviceItem);

                return unhandledServiceList.ToArray();
            }

            return Array.Empty<string>();
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

        var serviceAppList = ServiceAppLookup.Where(t => t.Value.Any(s => s.Item1 == interfaceType));
        if (serviceAppList.Count() > 0)
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

        var serviceAppList = ServiceAppLookup.Where(t => t.Value.Any(s => s.Item2 == implementationType));
        if (serviceAppList.Count() > 0)
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

        var serviceAppList = ServiceAppLookup.Where(t => t.Value.Any(s => string.Equals(serviceName, s.Item3.Name, StringComparison.InvariantCultureIgnoreCase)));
        if (serviceAppList.Count() > 0)
            return serviceAppList.First().Key;

        var serviceApp = GetPresetAppNameFromServiceName(serviceName);
        if (!string.IsNullOrWhiteSpace(serviceApp))
            return serviceApp;

        return null;
    }

    /// <summary>
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
