using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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

    private static ServiceConfiguration _configuration;
    /// <summary>
    /// Service configuration.
    /// 
    /// Pull from:
    ///     Config.json,
    ///     ENVs,
    ///     command line args.
    /// </summary>
    public static ServiceConfiguration Configuration
    {
        get
        {
            _configuration ??= ConfigurationRoot.Get<ServiceConfiguration>() ?? new ServiceConfiguration();

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
                foreach ((Type, Type, DaprServiceAttribute) serviceHandler in IDaprService.FindHandlers(enabledOnly: false))
                {
                    var serviceName = serviceHandler.Item3.Name;
                    var defaultApp = serviceHandler.Item3.DefaultApp.ToLower();
                    var appOverride = ConfigurationRoot.GetValue<string>(serviceName.ToUpper() + "_APP_MAPPING");
                    var appName = appOverride ?? defaultApp;

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

        if (Configuration == null)
        {
            Logger.Log(LogLevel.Error, null, "Service configuration missing.");
            return;
        }

        if (!IDaprService.IsAnyEnabled())
        {
            Logger.Log(LogLevel.Error, null, "No Dapr services have been enabled.");
            return;
        }

        if (!IDaprService.AllHaveRequiredConfiguration())
        {
            Logger.Log(LogLevel.Error, null, "Required configuration not set for enabled dapr services.");
            return;
        }

        Logger.Log(LogLevel.Information, null, "Configuration established.");

        var serviceTypes = IDaprService.FindHandlers(enabledOnly: true).Select(t => t.Item2);
        if (serviceTypes == null)
        {
            Logger.Log(LogLevel.Error, null, "No services enabled- exiting application.");
            return;
        }

        DaprClient = new DaprClientBuilder()
            .UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig)
            .Build();

        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(args);
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create web application builder- exiting application.");
            return;
        }

        _ = webAppBuilder.Services.AddAuthentication();
        _ = webAppBuilder.Services.AddControllers();
        webAppBuilder.Services.AddDaprClient();
        webAppBuilder.Services.AddDaprServices();

        WebApplication webApp = webAppBuilder.Build();
        try
        {
            _ = webApp.MapNonServiceControllers();
            _ = webApp.MapDaprServiceControllers();

            // initialize service handlers
            foreach (var serviceType in serviceTypes)
            {
                if (serviceType != null)
                {
                    var serviceInst = (IDaprService?)webApp.Services.GetService(serviceType);
                    if (serviceInst != null)
                        if (!await serviceInst.OnBuild(webApp))
                            throw new Exception($"Service handler [{serviceType}] failed on build.");
                }
            }

            Logger.Log(LogLevel.Information, null, "Webhost starting.");
            {
                // start running webhost, but don't block on it just yet
                var runTask = Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token), ShutdownCancellationTokenSource.Token);

                // run secondary initialization on service handlers, now that webhost is running
                foreach (var serviceType in serviceTypes)
                {
                    if (serviceType != null)
                    {
                        var serviceInst = (IDaprService?)webApp.Services.GetService(serviceType);
                        if (serviceInst != null)
                            await serviceInst.OnRunning();
                    }
                }

                Logger.Log(LogLevel.Information, null, "Service startup complete- settling in.");

                // wait for webhost to finish / shutdown to be initiated
                await runTask;
            }

            Logger.Log(LogLevel.Information, null, "Webhost stopped- starting controlled service shutdown.");

            // stop service handlers
            foreach (var serviceType in serviceTypes)
            {
                if (serviceType != null)
                {
                    IDaprService? serviceInst = (IDaprService?)webApp.Services.GetService(serviceType);
                    if (serviceInst != null)
                        await serviceInst.OnShutdown();
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, e, "A critical error has occurred- starting service shutdown.");
            ShutdownCancellationTokenSource.Cancel();
        }
        finally
        {
            await webApp.DisposeAsync();
            DaprClient.Dispose();
        }

        Logger.Log(LogLevel.Debug, null, "Service shutdown complete.");
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
