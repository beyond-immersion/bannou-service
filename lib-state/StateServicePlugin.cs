using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Plugin wrapper for State service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class StateServicePlugin : StandardServicePlugin<IStateService>
{
    public override string PluginName => "state";
    public override string DisplayName => "State Service";

    private IServiceProvider? _serviceProvider;

    /// <inheritdoc/>
    public override void ConfigureApplication(WebApplication app)
    {
        base.ConfigureApplication(app);
        _serviceProvider = app.Services;
    }

    /// <summary>
    /// Initialize the StateStoreFactory before any services try to use it.
    /// This prevents sync-over-async initialization when services call GetStore() in constructors.
    /// </summary>
    protected override async Task<bool> OnInitializeAsync()
    {
        if (_serviceProvider == null)
        {
            Logger?.LogError("ServiceProvider not available - ConfigureApplication must be called first");
            return false;
        }

        try
        {
            var stateStoreFactory = _serviceProvider.GetRequiredService<IStateStoreFactory>();
            Logger?.LogInformation("Initializing StateStoreFactory connections...");
            await stateStoreFactory.InitializeAsync();
            Logger?.LogInformation("StateStoreFactory initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to initialize StateStoreFactory");
            return false;
        }
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring state service dependencies");

        // Register the state store factory configuration via DI factory
        services.AddSingleton(sp =>
        {
            var stateConfig = sp.GetRequiredService<StateServiceConfiguration>();
            return BuildFactoryConfiguration(stateConfig);
        });
        services.AddSingleton<IStateStoreFactory, StateStoreFactory>();

        // Register distributed lock provider (used by Permissions service and others)
        services.AddSingleton<IDistributedLockProvider, Services.RedisDistributedLockProvider>();

        Logger?.LogDebug("State service dependencies configured");
    }

    /// <summary>
    /// Build StateStoreFactoryConfiguration from StateServiceConfiguration (IMPLEMENTATION TENETS compliant).
    /// </summary>
    private static StateStoreFactoryConfiguration BuildFactoryConfiguration(StateServiceConfiguration stateConfig)
    {
        var config = new StateStoreFactoryConfiguration
        {
            UseInMemory = stateConfig.UseInMemory,
            RedisConnectionString = !string.IsNullOrEmpty(stateConfig.RedisConnectionString)
                ? stateConfig.RedisConnectionString
                : "localhost:6379",
            MySqlConnectionString = stateConfig.MySqlConnectionString
        };

        // Add default store mappings based on service naming conventions
        // Store names use "{service}-statestore" pattern to match service requests
        var defaultStores = new Dictionary<string, (StateBackend backend, string? prefix, bool enableSearch)>
        {
            // Redis stores (ephemeral/session data)
            ["auth-statestore"] = (StateBackend.Redis, "auth", false),
            ["connect-statestore"] = (StateBackend.Redis, "connect", false),
            ["permissions-statestore"] = (StateBackend.Redis, "permissions", false),
            ["voice-statestore"] = (StateBackend.Redis, "voice", false),
            ["asset-statestore"] = (StateBackend.Redis, "asset", false),

            // Orchestrator stores (heartbeats, routings, configuration)
            ["orchestrator-heartbeats"] = (StateBackend.Redis, "orch:hb", false),
            ["orchestrator-routings"] = (StateBackend.Redis, "orch:rt", false),
            ["orchestrator-config"] = (StateBackend.Redis, "orch:cfg", false),

            // Actor/Behavior stores (agent cognition data)
            ["agent-memories"] = (StateBackend.Redis, "agent:mem", false),
            ["actor-state"] = (StateBackend.Redis, "actor:state", false),
            ["actor-templates"] = (StateBackend.Redis, "actor:tpl", false),
            ["actor-instances"] = (StateBackend.Redis, "actor:inst", false),
            ["actor-pool-nodes"] = (StateBackend.Redis, "actor:pool", false),
            ["actor-assignments"] = (StateBackend.Redis, "actor:assign", false),

            // Redis store for documentation (uses internal indexes via DocumentationService, not State API)
            ["documentation-statestore"] = (StateBackend.Redis, "doc", false),

            // Redis store with full-text search enabled (auto-creates index on startup)
            ["test-search-statestore"] = (StateBackend.Redis, "test-search", true),

            // MySQL stores (durable data)
            ["accounts-statestore"] = (StateBackend.MySql, null, false),
            ["character-statestore"] = (StateBackend.MySql, null, false),
            ["game-session-statestore"] = (StateBackend.MySql, null, false),
            ["location-statestore"] = (StateBackend.MySql, null, false),
            ["realm-statestore"] = (StateBackend.MySql, null, false),
            ["relationship-statestore"] = (StateBackend.MySql, null, false),
            ["relationship-type-statestore"] = (StateBackend.MySql, null, false),
            ["servicedata-statestore"] = (StateBackend.MySql, null, false),
            ["species-statestore"] = (StateBackend.MySql, null, false),
            ["subscriptions-statestore"] = (StateBackend.MySql, null, false),
        };

        foreach (var (storeName, (backend, prefix, enableSearch)) in defaultStores)
        {
            config.Stores[storeName] = new StoreConfiguration
            {
                Backend = backend,
                KeyPrefix = prefix,
                TableName = backend == StateBackend.MySql ? storeName.Replace("-", "_") : null,
                EnableSearch = enableSearch
            };
        }

        return config;
    }
}
