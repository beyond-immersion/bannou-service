using BeyondImmersion.BannouService.Messaging;
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
            Logger?.LogDebug("Initializing StateStoreFactory connections...");
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

        // Register state store factory with telemetry instrumentation
        // NullTelemetryProvider is registered by default; lib-telemetry overrides it when enabled
        // IMessageBus is optional for error event publishing - may be null during minimal startup
        services.AddSingleton<IStateStoreFactory>(sp =>
        {
            var config = sp.GetRequiredService<StateStoreFactoryConfiguration>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
            var messageBus = sp.GetService<IMessageBus>();

            return new StateStoreFactory(config, loggerFactory, telemetryProvider, messageBus);
        });

        // Register distributed lock provider (used by Permission service and others)
        services.AddSingleton<IDistributedLockProvider, Services.RedisDistributedLockProvider>();

        Logger?.LogDebug("State service dependencies configured");
    }

    /// <summary>
    /// Build StateStoreFactoryConfiguration from StateServiceConfiguration.
    /// Uses generated StateStoreDefinitions from schemas/state-stores.yaml.
    /// </summary>
    private static StateStoreFactoryConfiguration BuildFactoryConfiguration(StateServiceConfiguration stateConfig)
    {
        var config = new StateStoreFactoryConfiguration
        {
            UseInMemory = stateConfig.UseInMemory,
            RedisConnectionString = !string.IsNullOrEmpty(stateConfig.RedisConnectionString)
                ? stateConfig.RedisConnectionString
                : "localhost:6379",
            MySqlConnectionString = stateConfig.MySqlConnectionString,
            ConnectionTimeoutSeconds = stateConfig.ConnectionTimeoutSeconds,
            ConnectionRetryCount = stateConfig.ConnectionRetryCount,
            MinRetryDelayMs = stateConfig.MinRetryDelayMs,
            InMemoryFallbackLimit = stateConfig.InMemoryFallbackLimit,
            EnableErrorEventPublishing = stateConfig.EnableErrorEventPublishing,
            ErrorEventDeduplicationWindowSeconds = stateConfig.ErrorEventDeduplicationWindowSeconds
        };

        // Load store configurations from generated definitions (schema-first approach)
        // Source of truth: schemas/state-stores.yaml
        foreach (var (storeName, storeConfig) in StateStoreDefinitions.Configurations)
        {
            config.Stores[storeName] = storeConfig;
        }

        return config;
    }
}
