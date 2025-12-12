using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Controllers.Filters;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebSockets;
using Serilog;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: ApiController]
[assembly: InternalsVisibleTo("unit-tests")]
[assembly: InternalsVisibleTo("lib-testing")]
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
        set => _logger = value;
    }

    /// <summary>
    /// Shared dapr client interface, used by all enabled service handlers.
    /// </summary>
    public static DaprClient DaprClient { get; private set; }

    /// <summary>
    /// Plugin loader for managing service plugins.
    /// </summary>
    public static PluginLoader PluginLoader { get; private set; }

    /// <summary>
    /// Service heartbeat manager for publishing instance health to orchestrator.
    /// </summary>
    public static ServiceHeartbeatManager? HeartbeatManager { get; private set; }

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

        // build the dapr client
        var daprClientBuilder = new DaprClientBuilder()
            .UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig);

        // Configure Dapr gRPC endpoint from environment variable (for containerized environments)
        var daprGrpcEndpoint = Environment.GetEnvironmentVariable("DAPR_GRPC_ENDPOINT");
        if (!string.IsNullOrEmpty(daprGrpcEndpoint))
        {
            daprClientBuilder.UseGrpcEndpoint(daprGrpcEndpoint);
            Logger.Log(LogLevel.Information, null, $"Using Dapr gRPC endpoint from environment: {daprGrpcEndpoint}");
        }

        // Configure Dapr HTTP endpoint from environment variable (for containerized environments)
        var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT");
        if (!string.IsNullOrEmpty(daprHttpEndpoint))
        {
            daprClientBuilder.UseHttpEndpoint(daprHttpEndpoint);
            Logger.Log(LogLevel.Information, null, $"Using Dapr HTTP endpoint from environment: {daprHttpEndpoint}");
        }

        DaprClient = daprClientBuilder.Build();

        // Note: Dapr readiness check moved to after web server startup to avoid circular dependency

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
                    // Basic JWT bearer configuration - not used for validation, just to prevent Forbid() errors
                    options.RequireHttpsMetadata = false; // Allow HTTP for development
                    options.SaveToken = false; // We don't need to save tokens
                    options.IncludeErrorDetails = true; // Include error details for debugging

                    // Set actual JWT secret to prevent validation errors in CI
                    var jwtSecret = webAppBuilder.Configuration["BANNOU_JWTSECRET"]
                        ?? webAppBuilder.Configuration["AUTH_JWT_SECRET"]
                        ?? webAppBuilder.Configuration["JWTSECRET"]
                        ?? "default-fallback-secret-key-for-development";

                    var key = System.Text.Encoding.ASCII.GetBytes(jwtSecret);

                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                        RequireExpirationTime = false,
                        RequireSignedTokens = true
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
                .AddDapr(daprClientBuilder =>
                {
                    // CRITICAL: Configure DaprClient serializer here, NOT in separate AddDaprClient call
                    // AddDapr() registers DaprClient first, and subsequent AddDaprClient() calls are ignored
                    // (TryAddSingleton pattern - first registration wins)
                    daprClientBuilder.UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig);
                })
                .AddJsonOptions(jsonOptions =>
                {
                    // Configure System.Text.Json serialization options
                    jsonOptions.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                    jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                    jsonOptions.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // CRITICAL: Allow case-insensitive deserialization
                    jsonOptions.JsonSerializerOptions.WriteIndented = false;
                    jsonOptions.JsonSerializerOptions.AllowTrailingCommas = true;
                    jsonOptions.JsonSerializerOptions.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;
                    jsonOptions.JsonSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;
                    jsonOptions.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                });

            webAppBuilder.Services
                .AddWebSockets((websocketOptions) => { });

            // NOTE: DaprClient is already registered by AddDapr() above with proper serializer config
            // Do NOT call AddDaprClient() here - it would be ignored due to TryAddSingleton pattern

            // Add core service infrastructure (but not clients - PluginLoader handles those)
            webAppBuilder.Services.AddBannouServiceClients();

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
                        .SetMinimumLevel(Configuration.Web_Host_Logging_Level);
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
        try
        {
            // Add diagnostic middleware to track request lifecycle
            webApp.Use(async (context, next) =>
            {
                var requestId = Guid.NewGuid().ToString();
                Logger.Log(LogLevel.Debug, null, $"[{requestId}] Request starting: {context.Request.Method} {context.Request.Path}");

                try
                {
                    await next();
                    Logger.Log(LogLevel.Debug, null, $"[{requestId}] Request completed: Status {context.Response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error, ex, $"[{requestId}] Request failed with exception");
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

            // Add CloudEvents support for Dapr pub/sub
            webApp.UseCloudEvents();

            // Normalize Dapr invoke paths so controllers work with any sidecar app-id
            webApp.UseMiddleware<BeyondImmersion.BannouService.Middleware.InvokeAppIdRewriteMiddleware>();

            // map controller routes and subscription handlers
            _ = webApp.UseRouting().UseEndpoints(endpointOptions =>
            {
                endpointOptions.MapDefaultControllerRoute();
                endpointOptions.MapSubscribeHandler(); // Required for Dapr pub/sub
            });

            // Configure plugin application pipeline
            PluginLoader?.ConfigureApplication(webApp);

            // Resolve services centrally for plugins
            PluginLoader?.ResolveServices(webApp.Services);

            // Initialize plugins
            if (PluginLoader != null)
            {
                if (!await PluginLoader.InitializeAsync())
                {
                    Logger.Log(LogLevel.Error, "Plugin initialization failed- exiting application.");
                    return 1;
                }
            }

            // Start plugins
            if (PluginLoader != null)
            {
                if (!await PluginLoader.StartAsync())
                {
                    Logger.Log(LogLevel.Error, "Plugin startup failed- exiting application.");
                    return 1;
                }
            }

            // Event subscriptions will be handled by generated controller methods

            Logger.Log(LogLevel.Information, null, "Services added and initialized successfully- WebHost starting.");

            // start webhost
            var webHostTask = webApp.RunAsync(ShutdownCancellationTokenSource.Token);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Create heartbeat manager for Dapr connectivity check and ongoing health reporting
            // HEARTBEAT_ENABLED defaults to true - only set to false for minimal infrastructure testing
            // where Dapr pub/sub components are intentionally not configured
            var heartbeatEnabledEnv = Environment.GetEnvironmentVariable("HEARTBEAT_ENABLED");
            var heartbeatEnabled = string.IsNullOrEmpty(heartbeatEnabledEnv) ||
                !string.Equals(heartbeatEnabledEnv, "false", StringComparison.OrdinalIgnoreCase);

            if (heartbeatEnabled)
            {
                if (PluginLoader == null)
                {
                    Logger.Log(LogLevel.Error, null, "PluginLoader not initialized - cannot create heartbeat manager.");
                    return 1;
                }

                using var heartbeatLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var heartbeatLogger = heartbeatLoggerFactory.CreateLogger<ServiceHeartbeatManager>();
                var mappingResolver = webApp.Services.GetRequiredService<IServiceAppMappingResolver>();
                HeartbeatManager = new ServiceHeartbeatManager(DaprClient, heartbeatLogger, PluginLoader, mappingResolver);

                // Wait for Dapr connectivity using heartbeat publishing as the test
                // This replaces the old WaitForDaprReadiness() - publishing a heartbeat proves both
                // Dapr sidecar connectivity AND RabbitMQ pub/sub readiness
                Logger.Log(LogLevel.Information, null, "Waiting for Dapr pub/sub connectivity via startup heartbeat...");
                if (!await HeartbeatManager.WaitForDaprConnectivityAsync(
                    maxRetries: Configuration.Dapr_Readiness_Timeout > 0 ? 30 : 1,
                    retryDelayMs: 2000,
                    ShutdownCancellationTokenSource.Token))
                {
                    Logger.Log(LogLevel.Error, null, "Dapr pub/sub connectivity check failed - exiting application.");
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
                    // Orchestrator/primary container - try loading persisted mappings from Redis
                    await LoadPersistedMappingsAsync(webApp, ShutdownCancellationTokenSource.Token);
                }

                // Start periodic heartbeats now that we've confirmed connectivity
                HeartbeatManager.StartPeriodicHeartbeats();

                // Register service permissions now that Dapr pub/sub is confirmed ready
                // This ensures permission registration events are delivered to the Permissions service
                if (PluginLoader != null)
                {
                    Logger.Log(LogLevel.Information, null, "Registering service permissions with Permissions service...");
                    if (!await PluginLoader.RegisterServicePermissionsAsync())
                    {
                        Logger.Log(LogLevel.Error, null, "Service permission registration failed - exiting application.");
                        return 1;
                    }
                }
            }
            else
            {
                Logger.Log(LogLevel.Warning, null, "Heartbeat system disabled via HEARTBEAT_ENABLED=false (infrastructure testing mode).");
                // Do NOT register permissions here: infra profile uses empty Dapr components (no pubsub),
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

            DaprClient?.Dispose();
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
            Logger.Log(LogLevel.Warning, null, "No plugins were loaded. Running with existing IDaprService implementations only.");
        else
            Logger.Log(LogLevel.Information, null, $"Successfully loaded {pluginsLoaded} plugins.");

        return true;
    }

    /// <summary>
    /// Get the list of requested plugins based on Include_Assemblies configuration.
    /// </summary>
    /// <returns>List of plugin names to load, or null for all plugins</returns>
    private static IList<string>? GetRequestedPlugins()
    {
        if (string.Equals("none", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return new List<string>(); // Empty list = no plugins
        }

        if (string.Equals("all", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return null; // null = all plugins
        }

        if (string.IsNullOrWhiteSpace(Configuration.Include_Assemblies) ||
            string.Equals("common", Configuration.Include_Assemblies, StringComparison.InvariantCultureIgnoreCase))
        {
            return new List<string> { "common" }; // Only common plugins
        }

        // Parse comma-separated list
        var assemblyNames = Configuration.Include_Assemblies.Split(',', StringSplitOptions.RemoveEmptyEntries);
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
            catch (BadImageFormatException) { }
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

        var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT")
            ?? "http://127.0.0.1:3500";
        var url = $"{daprHttpEndpoint}/v1.0/invoke/{sourceAppId}/method/orchestrator/service-routing";

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("dapr-app-id", sourceAppId);
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var mappingsResponse = System.Text.Json.JsonSerializer.Deserialize<ServiceMappingsResponse>(json);

                    if (mappingsResponse?.Mappings != null)
                    {
                        var resolver = webApp.Services.GetService<IServiceAppMappingResolver>();
                        if (resolver != null)
                        {
                            resolver.ImportMappings(mappingsResponse.Mappings);
                            Logger.Log(LogLevel.Information, null,
                                $"Successfully imported {mappingsResponse.TotalServices} service mappings from {sourceAppId}");
                            return true;
                        }
                        else
                        {
                            Logger.Log(LogLevel.Error, null, "ServiceAppMappingResolver not found in DI container");
                        }
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
    /// Load persisted service mappings from Dapr state store (Redis).
    /// Used by orchestrator containers on restart to recover their routing table.
    /// </summary>
    /// <param name="webApp">The web application for service resolution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private static async Task LoadPersistedMappingsAsync(
        WebApplication webApp,
        CancellationToken cancellationToken)
    {
        const string StateStore = "connect-statestore";
        const string MappingsKey = "service-mappings:all";

        try
        {
            if (DaprClient == null)
            {
                Logger.Log(LogLevel.Warning, null, "DaprClient not available, skipping persisted mappings load");
                return;
            }

            var mappings = await DaprClient.GetStateAsync<Dictionary<string, string>>(
                StateStore,
                MappingsKey,
                cancellationToken: cancellationToken);

            if (mappings != null && mappings.Count > 0)
            {
                var resolver = webApp.Services.GetService<IServiceAppMappingResolver>();
                if (resolver != null)
                {
                    resolver.ImportMappings(mappings);
                    Logger.Log(LogLevel.Information, null,
                        $"Loaded {mappings.Count} persisted service mappings from Redis");
                }
            }
            else
            {
                Logger.Log(LogLevel.Debug, null, "No persisted service mappings found in Redis");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal - orchestrator can rebuild from new deployments
            Logger.Log(LogLevel.Warning, ex, "Could not load persisted mappings from Redis");
        }
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
