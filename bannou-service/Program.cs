using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Logging;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Serilog;
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
        get => _configurationRoot ??= IServiceConfiguration.BuildConfigurationRoot(Environment.GetCommandLineArgs());
        internal set => _configurationRoot = value;
    }

    private static AppConfiguration _configuration;
    /// <summary>
    /// Service configuration.
    /// Pull from Config.json, ENVs, and command line args.
    /// </summary>
    public static AppConfiguration Configuration
    {
        get => _configuration ??= ConfigurationRoot.Get<AppConfiguration>() ?? new AppConfiguration();
        internal set => _configuration = value;
    }

    private static string _serviceGUID;
    /// <summary>
    /// Internal service GUID- largely used for administrative network commands.
    /// Randomly generated on service startup.
    /// </summary>
    public static string ServiceGUID
    {
        get => _serviceGUID ??= Configuration.Force_Service_ID ?? Guid.NewGuid().ToString().ToLower();
        internal set => _serviceGUID = value;
    }

    private static Microsoft.Extensions.Logging.ILogger _logger;
    /// <summary>
    /// Application/global logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get => _logger ??= ServiceLogging.CreateApplicationLogger();
        internal set => _logger = value;
    }

    /// <summary>
    /// Shared dapr client interface, used by all enabled service handlers.
    /// </summary>
    public static DaprClient DaprClient { get; private set; }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    private static async Task Main()
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
        if (!IDaprService.EnabledServices.Any())
        {
            Logger.Log(LogLevel.Error, null, "No services have been enabled- exiting application.");
            return;
        }

        // ensure dapr services have their required configuration
        if (!IDaprService.EnabledServicesHaveRequiredConfiguration())
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
        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs());
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create WebApplicationBuilder- exiting application.");
            return;
        }

        try
        {
            // configure services
            _ = webAppBuilder.Services.AddAuthentication();
            _ = webAppBuilder.Services.AddControllers(mvcOptions =>
            {
                mvcOptions.Filters.Add(typeof(HeaderArrayActionFilter));
            });
            webAppBuilder.Services.AddDaprClient();
            webAppBuilder.Services.AddDaprServices();

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
                        .AddSerilog()
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
            _ = webApp.MapDaprServiceControllers();
            _ = webApp.UseHttpsRedirection();

            // enable websocket connections
            webApp.UseWebSockets(new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            // invoke all Service.Start() methods on enabled service handlers
            await webApp.InvokeAllServiceStartMethods();

            Logger.Log(LogLevel.Information, null, "Services added and initialized successfully- WebHost starting.");

            // start webhost
            var webHostTask = webApp.RunAsync(ShutdownCancellationTokenSource.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // invoke all Service.Running() methods on enabled service handlers
            await webApp.InvokeAllServiceRunningMethods();

            Logger.Log(LogLevel.Information, null, "WebHost started successfully and services running- settling in.");

            // !!! block here until token cancelled or webhost crashes
            await webHostTask;

            Logger.Log(LogLevel.Information, null, "WebHost stopped- starting controlled application shutdown.");

            // invoke all Service.Shutdown() methods on enabled service handlers
            await webApp.InvokeAllServiceShutdownMethods();
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
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
