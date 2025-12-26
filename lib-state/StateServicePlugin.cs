using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
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
    /// Build StateStoreFactoryConfiguration from StateServiceConfiguration (Tenet 21 compliant).
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

            // Redis stores with full-text search (Redis 8+ via NRedisStack)
            ["documentation-statestore"] = (StateBackend.Redis, "doc", true),

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
