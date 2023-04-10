using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
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
            if (_configurationRoot == null)
                _configurationRoot = IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());

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
            if (_configuration == null)
                _configuration = ConfigurationRoot.Get<ServiceConfiguration>() ?? new ServiceConfiguration();

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
            if (_serviceGUID == null)
                _serviceGUID = Configuration.ForceServiceID ?? Guid.NewGuid().ToString().ToLower();

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
                foreach (var serviceHandler in IDaprService.FindHandlers(enabledOnly: false))
                {
                    string serviceName = serviceHandler.Item3.Name;
                    string defaultApp = serviceHandler.Item3.DefaultApp.ToLower();
                    string? appOverride = ConfigurationRoot.GetValue<string>(serviceName.ToUpper() + "_APP_MAPPING");
                    string appName = appOverride ?? defaultApp;

                    if (_serviceAppLookup.TryGetValue(appName, out var existingApp))
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
        Logger.Log(LogLevel.Debug, null, "Service starting.");

        if (!Validators.RunAll())
        {
            Logger.Log(LogLevel.Error, null, "Validation error- service start aborted.");
            return;
        }

        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(args);
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create web application builder- exiting application.");
            return;
        }

        webAppBuilder.Services.AddAuthentication();
        webAppBuilder.Services.AddControllers();
        webAppBuilder.Services.AddDaprClient();
        webAppBuilder.Services.AddDaprServices();

        WebApplication webApp = webAppBuilder.Build();

        DaprClient = new DaprClientBuilder()
            .UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig)
            .Build();

        try
        {
            webApp.MapNonServiceControllers();
            webApp.MapDaprServiceControllers();

            Logger.Log(LogLevel.Debug, null, "Service startup complete- webhost starting.");
            {
                // blocks until webhost dies / server shutdown command received
                await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token), ShutdownCancellationTokenSource.Token);
            }

            Logger.Log(LogLevel.Debug, null, "Webhost stopped- starting controlled service shutdown.");
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Error, e, "A critical error has occurred- starting service shutdown.");
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
