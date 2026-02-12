using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Plugin wrapper for Status service enabling plugin-based discovery and lifecycle management.
/// Registers DI services and cleanup callbacks on startup.
/// </summary>
public class StatusServicePlugin : StandardServicePlugin<IStatusService>
{
    /// <inheritdoc />
    public override string PluginName => "status";

    /// <inheritdoc />
    public override string DisplayName => "Status Service";

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register seed evolution listener as singleton for DI discovery by Seed service (L2).
        // Seed discovers all ISeedEvolutionListener implementations via IEnumerable<ISeedEvolutionListener>.
        services.AddSingleton<ISeedEvolutionListener, StatusSeedEvolutionListener>();
    }

    /// <inheritdoc />
    protected override async Task OnRunningAsync(CancellationToken cancellationToken)
    {
        await base.OnRunningAsync(cancellationToken);

        await RegisterResourceCleanupCallbacksAsync(cancellationToken);
    }

    /// <summary>
    /// Registers cleanup callbacks with lib-resource for entity types that can have statuses.
    /// When a character is deleted, lib-resource calls our cleanup endpoint to remove all status data.
    /// </summary>
    private async Task RegisterResourceCleanupCallbacksAsync(CancellationToken cancellationToken)
    {
        var resourceClient = ServiceProvider.GetService<IResourceClient>();
        if (resourceClient == null)
        {
            var logger = ServiceProvider.GetRequiredService<ILogger<StatusServicePlugin>>();
            logger.LogWarning("IResourceClient not available; cleanup callbacks not registered");
            return;
        }

        try
        {
            // Register cleanup for character deletion per FOUNDATION TENETS (resource-managed cleanup).
            // When a character is deleted, all their status effects must be cleaned up.
            await resourceClient.DefineCleanupCallbackAsync(
                new DefineCleanupRequest
                {
                    ResourceType = "character",
                    SourceType = "status",
                    OnDeleteAction = OnDeleteAction.CASCADE,
                    ServiceName = "status",
                    CallbackEndpoint = "/status/cleanup-by-owner",
                    PayloadTemplate = "{\"ownerType\": \"character\", \"ownerId\": \"{{resourceId}}\"}",
                    Description = "Cleanup status effects and containers for deleted character"
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            var logger = ServiceProvider.GetRequiredService<ILogger<StatusServicePlugin>>();
            logger.LogError(ex,
                "Failed to register character cleanup callback with lib-resource");
        }
    }
}
