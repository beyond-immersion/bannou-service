using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Plugin wrapper for State service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class StateServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "state";
    public override string DisplayName => "State Service";

    private IStateService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring state service dependencies");

        // Build state store factory configuration from environment
        var factoryConfig = BuildFactoryConfiguration();

        // Register the state store factory as singleton
        services.AddSingleton(factoryConfig);
        services.AddSingleton<IStateStoreFactory, StateStoreFactory>();

        Logger?.LogDebug("State service dependencies configured with {StoreCount} stores", factoryConfig.Stores.Count);
    }

    /// <summary>
    /// Build StateStoreFactoryConfiguration from environment variables.
    /// </summary>
    private static StateStoreFactoryConfiguration BuildFactoryConfiguration()
    {
        var config = new StateStoreFactoryConfiguration
        {
            // Connection strings from environment
            RedisConnectionString = Environment.GetEnvironmentVariable("STATE_REDIS_CONNECTION") ?? "bannou-redis:6379",
            MySqlConnectionString = Environment.GetEnvironmentVariable("STATE_MYSQL_CONNECTION")
        };

        // Add default store mappings based on Dapr component naming conventions
        // These map Dapr state store names to their backends
        var defaultStores = new Dictionary<string, (StateBackend backend, string? prefix)>
        {
            // Redis stores (ephemeral/session data)
            ["auth"] = (StateBackend.Redis, "auth"),
            ["connect"] = (StateBackend.Redis, "connect"),
            ["permissions"] = (StateBackend.Redis, "permissions"),

            // MySQL stores (durable data)
            ["accounts"] = (StateBackend.MySql, null),
            ["character"] = (StateBackend.MySql, null),
            ["game-session"] = (StateBackend.MySql, null),
            ["location"] = (StateBackend.MySql, null),
            ["realm"] = (StateBackend.MySql, null),
            ["relationship"] = (StateBackend.MySql, null),
            ["relationship-type"] = (StateBackend.MySql, null),
            ["servicedata"] = (StateBackend.MySql, null),
            ["species"] = (StateBackend.MySql, null),
            ["subscriptions"] = (StateBackend.MySql, null),
        };

        foreach (var (storeName, (backend, prefix)) in defaultStores)
        {
            config.Stores[storeName] = new StoreConfiguration
            {
                Backend = backend,
                KeyPrefix = prefix,
                TableName = backend == StateBackend.MySql ? storeName.Replace("-", "_") : null
            };
        }

        return config;
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring State service application pipeline");

        // The generated StateController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("State service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting State service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IStateService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IStateService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for State service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("State service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start State service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("State service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for State service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during State service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down State service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for State service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("State service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during State service shutdown");
        }
    }
}
