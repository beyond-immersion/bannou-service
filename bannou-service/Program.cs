using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers.Filters;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.WebSockets;
using Serilog;
using System.Reflection;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Main program class for the Bannou service platform.
/// </summary>
public static class Program
{
    private static AppRunningStates _appRunningState = AppRunningStates.Stopped;
    /// <summary>
    /// Current startup/run state for the application.
    /// </summary>
    public static AppRunningStates AppRunningState
    {
        get => _appRunningState;
        private set => _appRunningState = value;
    }

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
        get => _configuration ??= IServiceConfiguration.BuildConfiguration<AppConfiguration>(Environment.GetCommandLineArgs());
        internal set => _configuration = value;
    }

    private static string _serviceGUID;

    /// <summary>
    /// Internal service GUID- largely used for administrative network commands.
    /// Randomly generated on service startup.
    /// </summary>
    public static string ServiceGUID
    {
        get => _serviceGUID ??= Configuration.ForceServiceId ?? Guid.NewGuid().ToString().ToLower();
        internal set => _serviceGUID = value;
    }

    private static Microsoft.Extensions.Logging.ILogger _logger;

    /// <summary>
    /// Application/global logger.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get => _logger ??= ServiceLogging.CreateApplicationLogger();
        set => _logger = value;
    }

    /// <summary>
    /// Plugin loader for managing service plugins.
    /// </summary>
    public static PluginLoader PluginLoader { get; private set; }

    /// <summary>
    /// Service heartbeat manager for publishing instance health to orchestrator.
    /// </summary>
    public static ServiceHeartbeatManager? HeartbeatManager { get; private set; }

    /// <summary>
    /// Mesh invocation client for service-to-service HTTP communication.
    /// Replaces mesh client for service invocation.
    /// </summary>
    public static IMeshInvocationClient? MeshInvocationClient { get; private set; }

    /// <summary>
    /// Token source for initiating a clean shutdown.
    /// </summary>
    public static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

    private static async Task<int> Main()
    {
        Logger.Log(LogLevel.Information, null, "Service starting.");
        AppRunningState = AppRunningStates.Starting;

        // configuration is auto-created on first get, so this call creates the config too
        if (Configuration == null)
        {
            Logger.Log(LogLevel.Error, null, "Service configuration missing- exiting application.");
            return 1;
        }

        Logger.Log(LogLevel.Information, null, "Configuration built and validated.");

        // load the plugins
        if (!await LoadPlugins())
        {
            Logger.Log(LogLevel.Error, null, "Failed to load enabled plugins- exiting application.");
            return 1;
        }

        // prepare to build the application
        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs());
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create WebApplicationBuilder- exiting application.");
            return 1;
        }

        try
        {
            // configure services - add default authentication scheme to prevent Forbid() errors
            _ = webAppBuilder.Services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", options =>
                {
                    // JWT bearer configuration with full validation enabled
                    options.RequireHttpsMetadata = false; // Allow HTTP for development
                    options.SaveToken = false; // We don't need to save tokens
                    options.IncludeErrorDetails = true; // Include error details for debugging

                    // JWT secret is REQUIRED - no fallback to prevent accidental insecure deployments
                    if (string.IsNullOrEmpty(Configuration.JwtSecret))
                    {
                        throw new InvalidOperationException(
                            "JWT secret not configured. Set BANNOU_JWT_SECRET environment variable.");
                    }

                    var key = System.Text.Encoding.ASCII.GetBytes(Configuration.JwtSecret);

                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = Configuration.JwtIssuer,
                        ValidAudience = Configuration.JwtAudience,
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromMinutes(5)
                    };
                });

            _ = webAppBuilder.Services
                .AddControllers(mvcOptions =>
                {
                    mvcOptions.Filters.Add(typeof(HeaderArrayActionFilter));
                    mvcOptions.Filters.Add(typeof(HeaderArrayResultFilter));
                })
                // Add plugin controller assemblies dynamically
                .ConfigureApplicationPartManager(manager =>
                {
                    if (PluginLoader != null)
                    {
                        var pluginAssemblies = PluginLoader.GetControllerAssemblies();
                        foreach (var assembly in pluginAssemblies)
                        {
                            manager.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(assembly));
                        }
                    }
                })
                .AddJsonOptions(jsonOptions =>
                {
                    // Apply BannouJson standard settings as base configuration
                    BeyondImmersion.Bannou.Core.BannouJson.ApplyBannouSettings(jsonOptions.JsonSerializerOptions);

                    // Web API-specific overrides for client compatibility:
                    // - CamelCase for JavaScript clients expecting camelCase JSON
                    // - AllowReadingFromString for lenient parsing of numeric strings from clients
                    // - Skip comments for more lenient request parsing
                    jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                    jsonOptions.JsonSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;
                    jsonOptions.JsonSerializerOptions.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;
                });

            webAppBuilder.Services
                .AddWebSockets((websocketOptions) => { });

            // Add core service infrastructure (but not clients - PluginLoader handles those)
            webAppBuilder.Services.AddBannouServiceClients();

            // Add client event publisher (for pushing events to WebSocket clients via Bannou pub/sub)
            webAppBuilder.Services.AddClientEventPublisher();

            // Configure plugin services (includes centralized client, service, and configuration registration)
            PluginLoader?.ConfigureServices(webAppBuilder.Services);

            // Configure OpenAPI documentation with NSwag
            webAppBuilder.Services.AddOpenApiDocument(document =>
            {
                document.Title = "Bannou API";
                document.Version = "v1";
                document.Description = "Schema-first microservice APIs for Bannou platform - Generated from OpenAPI specifications";
                document.DocumentName = "v1";

            });

            // configure webhost
            webAppBuilder.WebHost
                .UseKestrel((kestrelOptions) =>
                {
                    kestrelOptions.ListenAnyIP(Configuration.HttpWebHostPort);
                    kestrelOptions.ListenAnyIP(Configuration.HttpsWebHostPort, (listenOptions) =>
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
                        .SetMinimumLevel(Configuration.WebHostLoggingLevel);
                });

            // Enable DI validation to catch missing service registrations at startup
            // ValidateScopes: Ensures scoped services aren't resolved from root provider
            // ValidateOnBuild: Validates all service registrations when Build() is called
            webAppBuilder.Host.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
        }
        catch (Exception exc)
        {
            Logger.Log(LogLevel.Error, exc, "Failed to add required services to registry- exiting application.");
            return 1;
        }

        // build the application
        Logger.Log(LogLevel.Information, null, "About to build WebApplication - checking for DI conflicts...");

        WebApplication webApp;
        try
        {
            webApp = webAppBuilder.Build();
            Logger.Log(LogLevel.Information, null, "WebApplication built successfully - no DI validation errors detected");
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Error, ex, "Failed to build WebApplication - DI validation error detected");
            Logger.Log(LogLevel.Error, null, "Exception type: {ExceptionType}", ex?.GetType()?.Name ?? "null");
            Logger.Log(LogLevel.Error, null, "Exception message: {ExceptionMessage}", ex?.Message ?? "null");
            if (ex?.InnerException != null)
            {
                Logger.Log(LogLevel.Error, null, "Inner exception type: {InnerExceptionType}", ex.InnerException?.GetType()?.Name ?? "null");
                Logger.Log(LogLevel.Error, null, "Inner exception message: {InnerExceptionMessage}", ex.InnerException?.Message ?? "null");
            }
            throw; // Re-throw to maintain original behavior
        }

        // Defensive: webApp is guaranteed non-null here since Build() succeeded (exception re-thrown above if it failed)
        // This explicit check satisfies static analysis tools that can't track the throw-based control flow
        if (webApp == null)
        {
            Logger.Log(LogLevel.Critical, null, "WebApplication is unexpectedly null after successful build - this should never happen");
            return 1;
        }

        try
        {
            // Add service request context middleware to capture session ID from incoming requests
            // This MUST be early in the pipeline to capture context before any service calls
            webApp.UseServiceRequestContext();

            // Add diagnostic middleware to track request lifecycle
            webApp.Use(async (context, next) =>
            {
                var requestId = Guid.NewGuid().ToString();
                Logger.Log(LogLevel.Debug, null, "Request {RequestId} starting: {Method} {Path}", requestId, context.Request.Method, context.Request.Path);

                try
                {
                    await next();
                    Logger.Log(LogLevel.Debug, null, "Request {RequestId} completed: Status {StatusCode}", requestId, context.Response.StatusCode);
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error, ex, "Request {RequestId} failed with exception", requestId);
                    throw;
                }
            });

            // Configure OpenAPI documentation in development
            if (webApp.Environment.IsDevelopment())
            {
                webApp.UseOpenApi(); // Serves OpenAPI specification
                webApp.UseSwaggerUi(); // Serves Swagger UI
                webApp.UseReDoc(); // Alternative documentation UI
            }

            // enable websocket connections
            webApp.UseWebSockets(new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            // map controller routes
            _ = webApp.UseRouting().UseEndpoints(endpointOptions =>
            {
                endpointOptions.MapDefaultControllerRoute();
            });

            // Configure plugin application pipeline
            Logger.Log(LogLevel.Debug, null, "Configuring plugin application pipeline...");
            PluginLoader?.ConfigureApplication(webApp);

            // Resolve services centrally for plugins
            Logger.Log(LogLevel.Debug, null, "Resolving plugin services from DI container...");
            PluginLoader?.ResolveServices(webApp.Services);

            // Initialize plugins
            if (PluginLoader != null)
            {
                Logger.Log(LogLevel.Information, null, "Initializing plugins...");
                if (!await PluginLoader.InitializeAsync())
                {
                    Logger.Log(LogLevel.Error, "Plugin initialization failed- exiting application.");
                    return 1;
                }
                Logger.Log(LogLevel.Information, null, "Plugin initialization complete.");
            }

            // Register event types before starting messaging plugin (required for NativeEventConsumerBackend)
            // This MUST happen before PluginLoader.StartAsync() which starts the messaging backend
            EventSubscriptionRegistration.RegisterAll();
            Logger.Log(LogLevel.Debug, null, "Registered {Count} event subscription types.", Events.EventSubscriptionRegistry.Count);

            // Start plugins
            if (PluginLoader != null)
            {
                Logger.Log(LogLevel.Information, null, "Starting plugins...");
                if (!await PluginLoader.StartAsync())
                {
                    Logger.Log(LogLevel.Error, "Plugin startup failed- exiting application.");
                    return 1;
                }
                Logger.Log(LogLevel.Information, null, "Plugin startup complete.");
            }

            // Event subscriptions will be handled by generated controller methods

            Logger.Log(LogLevel.Information, null, "Services added and initialized successfully- starting Kestrel WebHost on ports {HttpPort}/{HttpsPort}...", Configuration.HttpWebHostPort, Configuration.HttpsWebHostPort);

            // start webhost
            var webHostTask = webApp.RunAsync(ShutdownCancellationTokenSource.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Create heartbeat manager for mesh connectivity check and ongoing health reporting
            // HeartbeatEnabled defaults to true - only set to false for minimal infrastructure testing
            // where Bannou pub/sub components are intentionally not configured
            if (Configuration.HeartbeatEnabled)
            {
                if (PluginLoader == null)
                {
                    Logger.Log(LogLevel.Error, null, "PluginLoader not initialized - cannot create heartbeat manager.");
                    return 1;
                }

                using var heartbeatLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var heartbeatLogger = heartbeatLoggerFactory.CreateLogger<ServiceHeartbeatManager>();
                var mappingResolver = webApp.Services.GetRequiredService<IServiceAppMappingResolver>();
                var messageBus = webApp.Services.GetRequiredService<IMessageBus>();
                HeartbeatManager = new ServiceHeartbeatManager(messageBus, heartbeatLogger, PluginLoader, mappingResolver, Configuration);

                // Initialize mesh invocation client for service-to-service communication
                MeshInvocationClient = webApp.Services.GetRequiredService<IMeshInvocationClient>();

                // Wait for message bus connectivity using heartbeat publishing as the test
                // Publishing a heartbeat proves RabbitMQ pub/sub readiness
                Logger.Log(LogLevel.Information, null, "Waiting for message bus connectivity via startup heartbeat...");
                if (!await HeartbeatManager.WaitForConnectivityAsync(
                    maxRetries: Configuration.MeshReadinessTimeout > 0 ? 30 : 1,
                    retryDelayMs: 2000,
                    ShutdownCancellationTokenSource.Token))
                {
                    Logger.Log(LogLevel.Error, null, "Message bus connectivity check failed - exiting application.");
                    return 1;
                }

                // Initialize service mappings before participating in the network
                // This ensures newly deployed containers have correct routing information
                if (!string.IsNullOrEmpty(Configuration.MappingSourceAppId))
                {
                    // Container deployed by orchestrator - query source for mappings
                    Logger.Log(LogLevel.Information, null,
                        $"Fetching initial service mappings from app-id: {Configuration.MappingSourceAppId}");

                    if (!await ImportServiceMappingsFromSourceAsync(
                        Configuration.MappingSourceAppId,
                        webApp,
                        ShutdownCancellationTokenSource.Token))
                    {
                        Logger.Log(LogLevel.Error, null,
                            $"Failed to import service mappings from {Configuration.MappingSourceAppId} - exiting application.");
                        return 1;
                    }
                }
                else
                {
                    // Orchestrator/primary container - no initial mappings to load
                    // Mappings are discovered dynamically via:
                    // - Service heartbeats publishing their services
                    // - FullServiceMappingsEvent from orchestrator (RabbitMQ pub/sub)
                    Logger.Log(LogLevel.Debug, null, "No source app-id configured - mappings will be discovered from heartbeats");
                }

                // Start periodic heartbeats now that we've confirmed connectivity
                HeartbeatManager.StartPeriodicHeartbeats();

                // Register service permissions now that Bannou pub/sub is confirmed ready
                // This ensures permission registration events are delivered to the Permission service
                if (PluginLoader != null)
                {
                    Logger.Log(LogLevel.Information, null, "Registering service permissions with Permission service...");
                    if (!await PluginLoader.RegisterServicePermissionsAsync())
                    {
                        Logger.Log(LogLevel.Error, null, "Service permission registration failed - exiting application.");
                        return 1;
                    }
                }
            }
            else
            {
                Logger.Log(LogLevel.Warning, null, "Heartbeat system disabled via BANNOU_HEARTBEAT_ENABLED=false (infrastructure testing mode).");
                // Do NOT register permissions here: infra profile uses empty components (no pubsub),
                // and permission registration publishes over pubsub. Calling it would fail startup.
            }

            // Invoke plugin running methods
            if (PluginLoader != null)
            {
                await PluginLoader.InvokeRunningAsync();
            }

            Logger.Log(LogLevel.Information, null, "WebHost started successfully and services running- settling in.");

            // !!! block here until token cancelled or webhost crashes
            AppRunningState = AppRunningStates.Running;
            await webHostTask;
            AppRunningState = AppRunningStates.Stopped;

            Logger.Log(LogLevel.Information, null, "WebHost stopped- starting controlled application shutdown.");

            // Publish shutdown heartbeat to notify orchestrator
            if (HeartbeatManager != null)
            {
                await HeartbeatManager.PublishShutdownHeartbeatAsync();
                await HeartbeatManager.DisposeAsync();
                HeartbeatManager = null;
            }

            // Shutdown plugins
            if (PluginLoader != null)
            {
                await PluginLoader.ShutdownAsync();
            }
        }
        catch (Exception exc)
        {
            Logger.Log(LogLevel.Error, exc, "A critical error has occurred- starting application shutdown.");
            ShutdownCancellationTokenSource.Cancel();
        }
        finally
        {
            AppRunningState = AppRunningStates.Stopped;
            // perform cleanup
            if (webApp != null)
                await webApp.DisposeAsync();
        }

        Logger.Log(LogLevel.Debug, null, "Application shutdown complete.");
        return 0;
    }

    /// <summary>
    /// Load and initialize plugins based on current application configuration.
    /// </summary>
    private static async Task<bool> LoadPlugins()
    {
        // Enable assembly resolution for plugin dependencies
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        // Create plugin loader
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var pluginLogger = loggerFactory.CreateLogger<PluginLoader>();
        PluginLoader = new PluginLoader(pluginLogger);

        // Determine which plugins to load
        var requestedPlugins = GetRequestedPlugins();

        // Load plugins from the plugins directory
        var appDirectory = Directory.GetCurrentDirectory();
        var pluginsDirectory = Path.Combine(appDirectory, "plugins");

        var pluginsLoaded = await PluginLoader.DiscoverAndLoadPluginsAsync(pluginsDirectory, requestedPlugins);
        if (pluginsLoaded == null)
            return false;

        if (pluginsLoaded == 0)
            Logger.Log(LogLevel.Warning, null, "No plugins were loaded. Running with existing IBannouService implementations only.");
        else
            Logger.Log(LogLevel.Information, null, "Successfully loaded {PluginsLoaded} plugins.", pluginsLoaded);

        return true;
    }

    /// <summary>
    /// Get the list of requested plugins based on IncludeAssemblies configuration.
    /// </summary>
    /// <returns>List of plugin names to load, or null for all plugins</returns>
    private static IList<string>? GetRequestedPlugins()
    {
        if (string.Equals("none", Configuration.IncludeAssemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return new List<string>(); // Empty list = no plugins
        }

        if (string.Equals("all", Configuration.IncludeAssemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return null; // null = all plugins
        }

        if (string.IsNullOrWhiteSpace(Configuration.IncludeAssemblies) ||
            string.Equals("common", Configuration.IncludeAssemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return new List<string> { "common" }; // Only common plugins
        }

        // Parse comma-separated list
        var assemblyNames = Configuration.IncludeAssemblies.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return assemblyNames.Select(name => name.Trim()).ToList();
    }

    /// <summary>
    /// Include /plugins/ and subdirectories in resolving .dll dependencies.
    /// </summary>
    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args?.Name))
            return null;

        var assemblyName = new AssemblyName(args.Name).Name;

        // CRITICAL: Avoid loading duplicate copies of already-loaded assemblies.
        // Assembly.LoadFile will always load a new copy; we must reuse the existing one
        // so static singletons (e.g., ServiceAppMappingResolver) stay process-wide.
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);
        if (alreadyLoaded != null)
            return alreadyLoaded;

        // try in app directory
        var appDirectory = Directory.GetCurrentDirectory();
        {
            var assemblyPath = Path.Combine(appDirectory, $"{assemblyName}.dll");
            if (TryLoadAssembly(assemblyPath, out var assemblyFound))
                return assemblyFound;
        }

        // try in root plugins directory
        var pluginsRootDirectory = Path.Combine(appDirectory, "plugins");
        {
            var assemblyPath = Path.Combine(pluginsRootDirectory, $"{assemblyName}.dll");
            if (TryLoadAssembly(assemblyPath, out var assemblyFound))
                return assemblyFound;
        }

        // try sub-plugin directories
        var pluginSubdirectories = Directory.GetDirectories(pluginsRootDirectory, "*", searchOption: SearchOption.AllDirectories);
        foreach (var pluginSubdirectory in pluginSubdirectories)
        {
            var assemblyPath = Path.Combine(pluginSubdirectory, $"{assemblyName}.dll");
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
                return true;
            }
            catch (BadImageFormatException ex)
            {
                Logger.Log(LogLevel.Warning, ex,
                    "Assembly at '{Path}' is not a valid .NET assembly (corrupted or architecture mismatch)",
                    assemblyPath);
            }
            catch (Exception exc)
            {
                Logger.Log(LogLevel.Error, exc, "Failed to load assembly at path: {AssemblyPath}", assemblyPath);
            }
        }

        assembly = null;
        return false;
    }

    /// <summary>
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();

    /// <summary>
    /// Import service mappings from a source app-id during startup.
    /// Used by newly deployed containers to get the current routing table
    /// from the orchestrator before participating in the network.
    /// </summary>
    /// <param name="sourceAppId">The app-id to query for mappings</param>
    /// <param name="webApp">The web application for service resolution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if import succeeded, false otherwise</returns>
    private static async Task<bool> ImportServiceMappingsFromSourceAsync(
        string sourceAppId,
        WebApplication webApp,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;

        var bannouHttpEndpoint = Configuration.EffectiveHttpEndpoint;
        var url = $"{bannouHttpEndpoint}/orchestrator/service-routing";

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("bannou-app-id", sourceAppId);
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var mappingsResponse = BannouJson.Deserialize<ServiceMappingsResponse>(json);

                    if (mappingsResponse?.Mappings != null)
                    {
                        var resolver = webApp.Services.GetRequiredService<IServiceAppMappingResolver>();
                        resolver.ImportMappings(mappingsResponse.Mappings);
                        Logger.Log(LogLevel.Information, null,
                            $"Successfully imported {mappingsResponse.TotalServices} service mappings from {sourceAppId}");
                        return true;
                    }
                }
                else
                {
                    Logger.Log(LogLevel.Warning, null,
                        $"Mapping import attempt {attempt}/{maxRetries} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Warning, ex,
                    $"Mapping import attempt {attempt}/{maxRetries} failed");
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelayMs * attempt, cancellationToken);
            }
        }

        return false;
    }

    /// <summary>
    /// Response model for service mappings query.
    /// </summary>
    private class ServiceMappingsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("mappings")]
        public Dictionary<string, string>? Mappings { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalServices")]
        public int TotalServices { get; set; }
    }
}
