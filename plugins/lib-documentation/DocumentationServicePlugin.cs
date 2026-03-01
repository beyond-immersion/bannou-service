using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// Plugin wrapper for Documentation service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class DocumentationServicePlugin : StandardServicePlugin<IDocumentationService>
{
    public override string PluginName => "documentation";
    public override string DisplayName => "Documentation Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register search index service based on available infrastructure
        // Uses Redis Search (FT.*) when available, falls back to in-memory indexing
        services.AddSingleton<ISearchIndexService>(sp =>
        {
            var stateStoreFactory = sp.GetRequiredService<IStateStoreFactory>();
            var logger = sp.GetRequiredService<ILogger<RedisSearchIndexService>>();
            var config = sp.GetRequiredService<DocumentationServiceConfiguration>();
            var fallbackLogger = sp.GetRequiredService<ILogger<SearchIndexService>>();

            var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();

            // Check if Redis Search is available
            if (stateStoreFactory.SupportsSearch(StateStoreDefinitions.Documentation))
            {
                logger.LogInformation("Using Redis Search (FT.*) for documentation full-text search");
                var messageBus = sp.GetRequiredService<IMessageBus>();
                return new RedisSearchIndexService(stateStoreFactory, logger, config, messageBus, telemetryProvider);
            }
            else
            {
                fallbackLogger.LogInformation("Redis Search not available, using in-memory search index");
                return new SearchIndexService(stateStoreFactory, fallbackLogger, config, telemetryProvider);
            }
        });

        // Register git sync service for repository operations
        services.AddSingleton<IGitSyncService, GitSyncService>();

        // Register content transform service for YAML frontmatter and markdown processing
        services.AddSingleton<IContentTransformService, ContentTransformService>();

        // Register background sync scheduler service
        services.AddHostedService<RepositorySyncSchedulerService>();

        // Register search index rebuild service (runs once on startup)
        services.AddHostedService<SearchIndexRebuildService>();

        // Register trashcan purge service (periodic expired entry cleanup)
        services.AddHostedService<TrashcanPurgeService>();

        Logger?.LogDebug("Service dependencies configured");
    }
}
