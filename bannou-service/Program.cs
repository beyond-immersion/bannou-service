using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers.Filters;
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
        if (!EnabledServicesHaveRequiredConfiguration())
        {
            Logger.Log(LogLevel.Error, null, "Required configuration missing for enabled services- exiting application.");
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

            _ = webAppBuilder.Services
                .AddControllers(mvcOptions =>
                {
                    mvcOptions.Filters.Add(typeof(HeaderArrayActionFilter));
                    mvcOptions.Filters.Add(typeof(HeaderArrayResultFilter));
                }).
                AddNewtonsoftJson(jsonSettings =>
                {
                    jsonSettings.SerializerSettings.ConstructorHandling = ConstructorHandling.Default;
                    jsonSettings.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                    jsonSettings.SerializerSettings.DateParseHandling = DateParseHandling.DateTimeOffset;
                    jsonSettings.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                    jsonSettings.SerializerSettings.DefaultValueHandling = DefaultValueHandling.Include;
                    jsonSettings.SerializerSettings.FloatFormatHandling = FloatFormatHandling.String;
                    jsonSettings.SerializerSettings.FloatParseHandling = FloatParseHandling.Double;
                    jsonSettings.SerializerSettings.MetadataPropertyHandling = MetadataPropertyHandling.Default;
                    jsonSettings.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Ignore;
                    jsonSettings.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    jsonSettings.SerializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
                    jsonSettings.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Error;
                    jsonSettings.SerializerSettings.StringEscapeHandling = StringEscapeHandling.Default;
                    jsonSettings.SerializerSettings.TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple;
                    jsonSettings.SerializerSettings.TypeNameHandling = TypeNameHandling.None;
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

            // enable websocket connections
            webApp.UseWebSockets(new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            // invoke all Service.Start() methods on enabled service handlers
            if (!await webApp.InvokeAllServiceStartMethods())
            {
                Logger.Log(LogLevel.Error, "An enabled service handler has failed to start- exiting application.");
                return;
            }

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
            if (webApp != null)
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
            if (webApp != null)
                await webApp.DisposeAsync();

            DaprClient?.Dispose();
        }

        Logger.Log(LogLevel.Debug, null, "Application shutdown complete.");
    }

    /// <summary>
    /// Returns whether all enabled services have their required configuration set.
    /// </summary>
    private static bool EnabledServicesHaveRequiredConfiguration()
    {
        foreach (var serviceInfo in IDaprService.EnabledServices)
        {
            Type interfaceType = serviceInfo.Item1;
            Type implementationType = serviceInfo.Item2;
            Type serviceConfig = IDaprService.GetConfigurationType(interfaceType);
            if (serviceConfig == null)
                continue;

            if (!IServiceConfiguration.HasRequiredForType(serviceConfig))
            {
                Logger.Log(LogLevel.Error, null, $"Required configuration is missing for the '{serviceInfo.Item3.Name}' service.");
                return false;
            }
        }

        return true;
    }

    private static void LoadAssemblies()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        if (string.Equals("none", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
            return;

        // load root app assemblies (probably already loaded anyways)
        var appDirectory = Directory.GetCurrentDirectory();
        foreach (var assemblyPath in Directory.GetFiles(appDirectory, "*.dll"))
            TryLoadAssembly(assemblyPath, out _);

        var libsRootDirectory = Path.Combine(appDirectory, "libs");
        if (libsRootDirectory == null || !Directory.Exists(libsRootDirectory))
        {
            Logger.Log(LogLevel.Warning, null, $"Failed to load additional assemblies- libs directory does not exist.");
            return;
        }

        // load root lib assemblies (should be loaded if `none` isn't selected)
        foreach (var assemblyPath in Directory.GetFiles(libsRootDirectory, "*.dll"))
            TryLoadAssembly(assemblyPath, out _);

        var libDirectories = Directory.GetDirectories(libsRootDirectory);
        if (libDirectories == null || libDirectories.Length == 0)
        {
            Logger.Log(LogLevel.Warning, null, $"Failed to load non-root assemblies- no subdirectories found in libs.");
            return;
        }

        if (string.Equals("all", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            foreach (var libDirectory in libDirectories)
            {
                var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
                foreach (var assemblyPath in assemblyPaths)
                    TryLoadAssembly(assemblyPath, out _);
            }

            return;
        }

        // load common lib assemblies (should be loaded if `none` isn't selected and the directory exists)
        {
            var libDirectory = Directory.GetDirectories(libsRootDirectory, "common", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (libDirectory != null)
            {
                var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
                foreach (var assemblyPath in assemblyPaths)
                    TryLoadAssembly(assemblyPath, out _);
            }
        }

        // if no configuration, or common, then that's all we need
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

            // load all files from subdirectories, if assembly directory is there
            var assemblyPaths = Directory.GetFiles(libDirectory, "*.dll", SearchOption.AllDirectories);
            foreach (var assemblyPath in assemblyPaths)
                TryLoadAssembly(assemblyPath, out _);
        }

        return;
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args?.Name))
            return null;

        var assemblyName = new AssemblyName(args.Name).Name;

        // try in app directory
        var appDirectory = Directory.GetCurrentDirectory();
        {
            var assemblyPath = Path.Combine(appDirectory, $"{assemblyName}.dll");
            if (TryLoadAssembly(assemblyPath, out var assemblyFound))
                return assemblyFound;
        }

        // try in root libs directory
        var libsRootDirectory = Path.Combine(appDirectory, "libs");
        {
            var assemblyPath = Path.Combine(libsRootDirectory, $"{assemblyName}.dll");
            if (TryLoadAssembly(assemblyPath, out var assemblyFound))
                return assemblyFound;
        }

        // try sub-lib directories
        var libSubdirectories = Directory.GetDirectories(libsRootDirectory, "*", searchOption: SearchOption.AllDirectories);
        foreach (var libSubdirectory in libSubdirectories)
        {
            var assemblyPath = Path.Combine(libSubdirectory, $"{assemblyName}.dll");
            if (TryLoadAssembly(assemblyPath, out var assemblyFound))
                return assemblyFound;
        }

        return null;
    }

    private static bool TryLoadAssembly(string assemblyPath, out Assembly? assembly)
    {
        if (File.Exists(assemblyPath))
        {
            try
            {
                assembly = Assembly.LoadFile(assemblyPath);
                Logger.Log(LogLevel.Information, null, $"Successfully loaded assembly at path: {assemblyPath}.");

                return true;
            }
            catch (Exception exc)
            {
                Logger.Log(LogLevel.Error, exc, $"Failed to load assembly at path: {assemblyPath}.");
            }
        }

        assembly = null;
        return false;
    }

    /// <summary>
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
