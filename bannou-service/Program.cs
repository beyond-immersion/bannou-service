using BeyondImmersion.BannouService.Logging;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: ApiController]
[assembly: InternalsVisibleTo("unit-tests")]
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
        LoadAssembliesFromLibs();

        // get info for dapr services in loaded assemblies
        var enabledServiceInfo = IDaprService.GetAllServiceInfo(enabledOnly: true);
        if (enabledServiceInfo == null || enabledServiceInfo.Count() == 0)
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

    public static void LoadAssembliesFromLibs()
    {
        var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        foreach (var dllPath in GetDllFiles(directoryPath))
        {
            try
            {
                Assembly.LoadFrom(dllPath);
                Logger.Log(LogLevel.Error, null, $"Successfully loaded assembly from {dllPath}");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex, $"Failed to load assembly from {dllPath}.");
            }
        }
    }

    private static string[] GetDllFiles(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
            return Directory.GetFiles(directoryPath, "*.dll", SearchOption.AllDirectories);

        return Array.Empty<string>();
    }

    private static async Task InvokeAllServiceStartMethods(WebApplication webApp, IEnumerable<Type> implTypes)
    {
        foreach (var implType in implTypes)
        {
            var serviceInst = (IDaprService?)webApp.Services.GetService(implType);
            if (serviceInst != null)
                await serviceInst.OnStart();
        }
    }

    private static async Task InvokeAllServiceRunningMethods(WebApplication webApp, IEnumerable<Type> implTypes)
    {
        foreach (var implType in implTypes)
        {
            var serviceInst = (IDaprService?)webApp.Services.GetService(implType);
            if (serviceInst != null)
                await serviceInst.OnRunning();
        }
    }

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
    /// Get the app name for the given handler type.
    /// </summary>
    public static string? GetAppByHandlerType(Type handlerType)
        => ServiceAppLookup.Where(t => t.Value.Any(s => s.Item1 == handlerType)).FirstOrDefault().Key;

    /// <summary>
    /// Get the app name for the given handler type.
    /// </summary>
    public static string? GetAppByImplementationType(Type implementationType)
        => ServiceAppLookup.Where(t => t.Value.Any(s => s.Item2 == implementationType)).FirstOrDefault().Key;

    /// <summary>
    /// Get the app name for the given handler type.
    /// </summary>
    public static string? GetAppByServiceName(string serviceName)
        => ServiceAppLookup.Where(t => t.Value.Any(s => string.Equals(serviceName, s.Item3.Name, StringComparison.InvariantCultureIgnoreCase))).FirstOrDefault().Key;

    /// <summary>
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
