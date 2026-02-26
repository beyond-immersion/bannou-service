using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Background service that periodically cleans up stale members from location-entities sets.
/// When an entity's presence TTL expires, the entity-location key is removed by Redis automatically,
/// but the entity remains in the location's entity set. This worker evicts those stale set members.
/// Uses an index set (location-entities:__index__) to discover active location entity sets.
/// </summary>
public class EntityPresenceCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityPresenceCleanupWorker> _logger;
    private readonly LocationServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private const string ENTITY_LOCATION_PREFIX = "entity-location:";
    private const string LOCATION_ENTITIES_PREFIX = "location-entities:";
    private const string LOCATION_ENTITIES_INDEX_KEY = "location-entities:__index__";

    /// <summary>
    /// Interval between cleanup cycles, from configuration.
    /// </summary>
    private TimeSpan CleanupInterval => TimeSpan.FromSeconds(_configuration.EntityPresenceCleanupIntervalSeconds);

    /// <summary>
    /// Startup delay before first cleanup cycle, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.EntityPresenceCleanupStartupDelaySeconds);

    public EntityPresenceCleanupWorker(
        IServiceProvider serviceProvider,
        ILogger<EntityPresenceCleanupWorker> logger,
        LocationServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Entity presence cleanup worker starting, interval: {Interval}s, startup delay: {Delay}s",
            _configuration.EntityPresenceCleanupIntervalSeconds,
            _configuration.EntityPresenceCleanupStartupDelaySeconds);

        // Wait before first cleanup to allow services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Entity presence cleanup worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStalePresenceEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during entity presence cleanup cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "location",
                        "EntityPresenceCleanup",
                        ex.GetType().Name,
                        ex.Message,
                        severity: BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing cleanup loop");
                }
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Entity presence cleanup worker stopped");
    }

    /// <summary>
    /// Iterates the location index set, checks each location's entity set for stale members
    /// (entities whose presence key has expired), and removes them.
    /// </summary>
    private async Task CleanupStalePresenceEntriesAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "EntityPresenceCleanupWorker.CleanupStalePresenceEntries");

        _logger.LogDebug("Starting entity presence cleanup cycle");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        var entitySetStore = stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.LocationEntitySet);
        var presenceStore = stateStoreFactory.GetStore<EntityPresenceModel>(StateStoreDefinitions.LocationEntityPresence);

        // Get the index of all location IDs that have entity sets
        var locationIdStrings = await entitySetStore.GetSetAsync<string>(LOCATION_ENTITIES_INDEX_KEY, cancellationToken);

        if (locationIdStrings.Count == 0)
        {
            _logger.LogDebug("No location entity sets to clean up");
            return;
        }

        var totalRemoved = 0;
        var emptyLocations = new List<string>();

        foreach (var locationIdStr in locationIdStrings)
        {
            if (!Guid.TryParse(locationIdStr, out var locationId))
            {
                // Corrupted index entry - remove it
                emptyLocations.Add(locationIdStr);
                continue;
            }

            var locationSetKey = $"{LOCATION_ENTITIES_PREFIX}{locationId}";
            var members = await entitySetStore.GetSetAsync<string>(locationSetKey, cancellationToken);

            if (members.Count == 0)
            {
                // Set is empty - remove location from index
                emptyLocations.Add(locationIdStr);
                continue;
            }

            var staleMembers = new List<string>();

            foreach (var member in members)
            {
                // Parse member format: {entityType}:{entityId}
                var separatorIndex = member.LastIndexOf(':');
                if (separatorIndex <= 0)
                {
                    staleMembers.Add(member);
                    continue;
                }

                var entityType = member[..separatorIndex];
                if (!Guid.TryParse(member[(separatorIndex + 1)..], out var entityId))
                {
                    staleMembers.Add(member);
                    continue;
                }

                // Check if the entity's presence key still exists
                var entityLocationKey = $"{ENTITY_LOCATION_PREFIX}{entityType}:{entityId}";
                var presence = await presenceStore.GetAsync(entityLocationKey, cancellationToken);

                if (presence is null)
                {
                    // Presence expired - entity is stale
                    staleMembers.Add(member);
                }
            }

            // Remove stale members from this location's set
            foreach (var staleMember in staleMembers)
            {
                await entitySetStore.RemoveFromSetAsync(locationSetKey, staleMember, cancellationToken);
                totalRemoved++;
            }

            // If all members were removed, clean up the empty set and remove from index
            if (staleMembers.Count == members.Count)
            {
                await entitySetStore.DeleteSetAsync(locationSetKey, cancellationToken);
                emptyLocations.Add(locationIdStr);
            }
        }

        // Remove empty locations from the index
        foreach (var emptyLocationId in emptyLocations)
        {
            await entitySetStore.RemoveFromSetAsync(LOCATION_ENTITIES_INDEX_KEY, emptyLocationId, cancellationToken);
        }

        if (totalRemoved > 0 || emptyLocations.Count > 0)
        {
            _logger.LogInformation(
                "Entity presence cleanup completed: removed {StaleCount} stale entries, cleaned {EmptyCount} empty location sets",
                totalRemoved, emptyLocations.Count);
        }
        else
        {
            _logger.LogDebug("Entity presence cleanup completed: no stale entries found");
        }
    }
}
