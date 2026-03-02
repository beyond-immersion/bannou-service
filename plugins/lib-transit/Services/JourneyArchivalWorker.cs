using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Background service that periodically archives completed/abandoned journeys from
/// Redis to MySQL and enforces retention policy on archived journeys.
/// </summary>
/// <remarks>
/// <para>
/// Active journeys live in Redis for fast access during gameplay. Once a journey reaches
/// a terminal state (<c>Arrived</c> or <c>Abandoned</c>) and has been in that state for
/// longer than <see cref="TransitServiceConfiguration.JourneyArchiveAfterGameHours"/>,
/// it is copied to the MySQL archive store and removed from Redis. This frees Redis
/// memory while preserving historical travel data for Trade velocity calculations,
/// Analytics aggregation, and Character History travel biography.
/// </para>
/// <para>
/// The worker discovers journeys via a <c>List&lt;Guid&gt;</c> index maintained by
/// <see cref="TransitService.AddToJourneyIndexAsync"/>. This avoids Redis SCAN operations
/// which are not supported by <see cref="IStateStore{T}"/>.
/// </para>
/// <para>
/// Also enforces <see cref="TransitServiceConfiguration.JourneyArchiveRetentionDays"/>:
/// archived journeys older than the retention threshold are deleted from MySQL. When set
/// to 0, archives are retained indefinitely.
/// </para>
/// <para>
/// Runs every <see cref="TransitServiceConfiguration.JourneyArchivalWorkerIntervalSeconds"/>
/// (default 300 seconds = 5 minutes real-time).
/// </para>
/// </remarks>
public class JourneyArchivalWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<JourneyArchivalWorker> _logger;
    private readonly TransitServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new instance of the JourneyArchivalWorker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scoped resolution.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Transit service configuration.</param>
    public JourneyArchivalWorker(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<JourneyArchivalWorker> logger,
        TransitServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Journey archival worker starting, check interval: {IntervalSeconds}s",
            _configuration.JourneyArchivalWorkerIntervalSeconds);

        // Startup delay to allow other services to initialize (configurable per IMPLEMENTATION TENETS)
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.JourneyArchivalWorkerStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Journey archival worker cancelled during startup delay");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ArchiveCompletedJourneysAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during journey archival check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "transit",
                        "JourneyArchivalWorker.ArchiveCompletedJourneys",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event for journey archival failure");
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.JourneyArchivalWorkerIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Journey archival worker stopped");
    }

    /// <summary>
    /// Scans the journey index for completed/abandoned journeys, checks game-time age
    /// via Worldstate, archives eligible journeys to MySQL, and cleans up Redis.
    /// Also enforces retention policy on already-archived journeys.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task ArchiveCompletedJourneysAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "JourneyArchivalWorker.ArchiveCompletedJourneys");

        _logger.LogDebug("Starting journey archival check");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var worldstateClient = scope.ServiceProvider.GetRequiredService<Worldstate.IWorldstateClient>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Distributed lock ensures only one node runs the archival sweep per cycle
        // (per IMPLEMENTATION TENETS: multi-instance safety)
        var lockOwner = $"journey-archival-{Guid.NewGuid():N}";
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.TransitLock,
            "journey-archival-sweep",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Another node is running journey archival sweep, skipping this cycle");
            return;
        }

        var journeyStore = stateStoreFactory.GetStore<TransitJourneyModel>(
            StateStoreDefinitions.TransitJourneys);
        var journeyIndexStore = stateStoreFactory.GetStore<List<Guid>>(
            StateStoreDefinitions.TransitJourneys);
        var archiveStore = stateStoreFactory.GetQueryableStore<JourneyArchiveModel>(
            StateStoreDefinitions.TransitJourneysArchive);

        // Step 1: Read the journey index
        var journeyIndex = await journeyIndexStore.GetAsync(
            TransitService.JOURNEY_INDEX_KEY, cancellationToken);

        if (journeyIndex == null || journeyIndex.Count == 0)
        {
            _logger.LogDebug("No journeys in index to check for archival");
        }
        else
        {
            await ProcessJourneyIndexAsync(
                journeyIndex, journeyStore, journeyIndexStore, archiveStore,
                worldstateClient, cancellationToken);
        }

        // Step 2: Enforce retention policy on archived journeys
        await EnforceRetentionPolicyAsync(archiveStore, cancellationToken);
    }

    /// <summary>
    /// Processes the journey index, fetching each journey and archiving those that are
    /// in a terminal state and old enough. Groups journeys by realm for efficient
    /// Worldstate batch queries.
    /// </summary>
    /// <param name="journeyIndex">Current journey IDs from the index.</param>
    /// <param name="journeyStore">Redis journey store.</param>
    /// <param name="journeyIndexStore">Redis journey index store.</param>
    /// <param name="archiveStore">MySQL archive store.</param>
    /// <param name="worldstateClient">Worldstate client for game-time queries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessJourneyIndexAsync(
        List<Guid> journeyIndex,
        IStateStore<TransitJourneyModel> journeyStore,
        IStateStore<List<Guid>> journeyIndexStore,
        IQueryableStateStore<JourneyArchiveModel> archiveStore,
        Worldstate.IWorldstateClient worldstateClient,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "JourneyArchivalWorker.ProcessJourneyIndex");

        var archivedCount = 0;
        var removedFromIndexCount = 0;
        var idsToRemoveFromIndex = new List<Guid>();

        // First pass: fetch all journeys and group terminal ones by realm
        var terminalJourneysByRealm = new Dictionary<Guid, List<TransitJourneyModel>>();

        foreach (var journeyId in journeyIndex)
        {
            try
            {
                var key = TransitService.BuildJourneyKey(journeyId);
                var journey = await journeyStore.GetAsync(key, cancellationToken: cancellationToken);

                if (journey == null)
                {
                    // Journey was already deleted/archived by another process -- remove from index
                    idsToRemoveFromIndex.Add(journeyId);
                    continue;
                }

                // Only archive journeys in terminal states
                if (journey.Status != JourneyStatus.Arrived && journey.Status != JourneyStatus.Abandoned)
                {
                    continue;
                }

                // Group by realm for batch game-time lookup
                if (!terminalJourneysByRealm.ContainsKey(journey.RealmId))
                {
                    terminalJourneysByRealm[journey.RealmId] = new List<TransitJourneyModel>();
                }
                terminalJourneysByRealm[journey.RealmId].Add(journey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch journey {JourneyId} from Redis during archival check", journeyId);
            }
        }

        if (terminalJourneysByRealm.Count == 0 && idsToRemoveFromIndex.Count == 0)
        {
            _logger.LogDebug("No terminal journeys found in index");
            return;
        }

        // Batch fetch game-time snapshots for all realms with terminal journeys
        var realmGameTimes = new Dictionary<Guid, decimal>();
        if (terminalJourneysByRealm.Count > 0)
        {
            var realmIds = terminalJourneysByRealm.Keys.ToList();
            try
            {
                var batchResponse = await worldstateClient.BatchGetRealmTimesAsync(
                    new Worldstate.BatchGetRealmTimesRequest { RealmIds = realmIds },
                    cancellationToken);

                foreach (var snapshot in batchResponse.Snapshots)
                {
                    // Convert total game-seconds to game-hours for comparison with JourneyArchiveAfterGameHours
                    realmGameTimes[snapshot.RealmId] = snapshot.TotalGameSecondsSinceEpoch / 3600m;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to batch-fetch realm game times for archival, skipping archival this cycle");
                // Still proceed with index cleanup for orphaned entries
                if (idsToRemoveFromIndex.Count > 0)
                {
                    await CleanupJourneyIndexAsync(journeyIndexStore, idsToRemoveFromIndex, cancellationToken);
                }
                return;
            }
        }

        // Second pass: archive eligible journeys
        var archiveThresholdGameHours = (decimal)_configuration.JourneyArchiveAfterGameHours;

        foreach (var (realmId, journeys) in terminalJourneysByRealm)
        {
            if (!realmGameTimes.TryGetValue(realmId, out var currentGameTimeHours))
            {
                // Realm has no Worldstate clock configured -- skip journeys in this realm
                _logger.LogDebug("Realm {RealmId} has no game time available, skipping {Count} journeys for archival",
                    realmId, journeys.Count);
                continue;
            }

            foreach (var journey in journeys)
            {
                try
                {
                    // Determine game-time of completion/abandonment
                    var journeyEndGameTimeHours = journey.Status == JourneyStatus.Arrived
                        ? journey.ActualArrivalGameTime ?? journey.EstimatedArrivalGameTime
                        : journey.EstimatedArrivalGameTime; // For abandoned, use estimated as proxy

                    var gameHoursElapsed = currentGameTimeHours - journeyEndGameTimeHours;

                    if (gameHoursElapsed < archiveThresholdGameHours)
                    {
                        continue; // Not old enough yet
                    }

                    // Archive to MySQL
                    var archiveModel = MapJourneyToArchive(journey);
                    var archiveKey = TransitService.BuildJourneyArchiveKey(journey.Id);
                    await archiveStore.SaveAsync(archiveKey, archiveModel, cancellationToken: cancellationToken);

                    // Delete from Redis
                    var journeyKey = TransitService.BuildJourneyKey(journey.Id);
                    await journeyStore.DeleteAsync(journeyKey, cancellationToken);

                    idsToRemoveFromIndex.Add(journey.Id);
                    archivedCount++;

                    _logger.LogDebug("Archived journey {JourneyId} ({Status}, {GameHoursElapsed:F1} game-hours old)",
                        journey.Id, journey.Status, gameHoursElapsed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to archive journey {JourneyId}", journey.Id);
                }
            }
        }

        // Cleanup the journey index
        if (idsToRemoveFromIndex.Count > 0)
        {
            await CleanupJourneyIndexAsync(journeyIndexStore, idsToRemoveFromIndex, cancellationToken);
            removedFromIndexCount = idsToRemoveFromIndex.Count;
        }

        if (archivedCount > 0 || removedFromIndexCount > 0)
        {
            _logger.LogInformation("Journey archival complete: {ArchivedCount} archived, {RemovedCount} removed from index",
                archivedCount, removedFromIndexCount);
        }
        else
        {
            _logger.LogDebug("Journey archival complete: no journeys archived this cycle");
        }
    }

    /// <summary>
    /// Enforces the retention policy on archived journeys by deleting entries
    /// older than <see cref="TransitServiceConfiguration.JourneyArchiveRetentionDays"/>
    /// from MySQL. When retention days is 0, archives are retained indefinitely.
    /// </summary>
    /// <param name="archiveStore">MySQL archive store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task EnforceRetentionPolicyAsync(
        IQueryableStateStore<JourneyArchiveModel> archiveStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "JourneyArchivalWorker.EnforceRetentionPolicy");

        var retentionDays = _configuration.JourneyArchiveRetentionDays;
        if (retentionDays <= 0)
        {
            _logger.LogDebug("Journey archive retention is indefinite (0 days), skipping cleanup");
            return;
        }

        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var expiredArchives = await archiveStore.QueryAsync(
            a => a.ArchivedAt < cutoffDate,
            cancellationToken);

        if (expiredArchives.Count == 0)
        {
            _logger.LogDebug("No expired archived journeys to clean up (retention: {RetentionDays} days)", retentionDays);
            return;
        }

        var deletedCount = 0;
        foreach (var expired in expiredArchives)
        {
            try
            {
                var archiveKey = TransitService.BuildJourneyArchiveKey(expired.Id);
                await archiveStore.DeleteAsync(archiveKey, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired archived journey {JourneyId}", expired.Id);
            }
        }

        _logger.LogInformation("Retention cleanup: deleted {DeletedCount} archived journeys older than {RetentionDays} days",
            deletedCount, retentionDays);
    }

    /// <summary>
    /// Removes processed journey IDs from the journey index using optimistic concurrency.
    /// Follows the same pattern as <see cref="TransitService.AddToJourneyIndexAsync"/>.
    /// </summary>
    /// <param name="journeyIndexStore">Redis journey index store.</param>
    /// <param name="idsToRemove">Journey IDs to remove from the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task CleanupJourneyIndexAsync(
        IStateStore<List<Guid>> journeyIndexStore,
        List<Guid> idsToRemove,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "JourneyArchivalWorker.CleanupJourneyIndex");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (currentIndex, etag) = await journeyIndexStore.GetWithETagAsync(
                TransitService.JOURNEY_INDEX_KEY, cancellationToken);

            if (currentIndex == null || currentIndex.Count == 0)
            {
                return;
            }

            var removeSet = new HashSet<Guid>(idsToRemove);
            var updatedIndex = currentIndex.Where(id => !removeSet.Contains(id)).ToList();

            if (updatedIndex.Count == currentIndex.Count)
            {
                return; // Nothing to remove
            }

            if (etag == null)
            {
                // Index exists but has no etag (first write) -- save directly
                await journeyIndexStore.SaveAsync(
                    TransitService.JOURNEY_INDEX_KEY, updatedIndex, cancellationToken: cancellationToken);
                _logger.LogDebug("Cleaned {Count} entries from journey index", currentIndex.Count - updatedIndex.Count);
                return;
            }

            var result = await journeyIndexStore.TrySaveAsync(
                TransitService.JOURNEY_INDEX_KEY, updatedIndex, etag, cancellationToken);
            if (result != null)
            {
                _logger.LogDebug("Cleaned {Count} entries from journey index", currentIndex.Count - updatedIndex.Count);
                return;
            }

            _logger.LogDebug("Concurrent modification on journey index during cleanup, retrying (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to clean journey index after retries - will retry next cycle");
    }

    /// <summary>
    /// Maps an active <see cref="TransitJourneyModel"/> to a <see cref="JourneyArchiveModel"/>
    /// for storage in the MySQL archive store.
    /// </summary>
    /// <param name="journey">The active journey to archive.</param>
    /// <returns>The archive model with <see cref="JourneyArchiveModel.ArchivedAt"/> set to now.</returns>
    private static JourneyArchiveModel MapJourneyToArchive(TransitJourneyModel journey)
    {
        return new JourneyArchiveModel
        {
            Id = journey.Id,
            EntityId = journey.EntityId,
            EntityType = journey.EntityType,
            Legs = journey.Legs,
            CurrentLegIndex = journey.CurrentLegIndex,
            PrimaryModeCode = journey.PrimaryModeCode,
            EffectiveSpeedKmPerGameHour = journey.EffectiveSpeedKmPerGameHour,
            PlannedDepartureGameTime = journey.PlannedDepartureGameTime,
            ActualDepartureGameTime = journey.ActualDepartureGameTime,
            EstimatedArrivalGameTime = journey.EstimatedArrivalGameTime,
            ActualArrivalGameTime = journey.ActualArrivalGameTime,
            OriginLocationId = journey.OriginLocationId,
            DestinationLocationId = journey.DestinationLocationId,
            CurrentLocationId = journey.CurrentLocationId,
            Status = journey.Status,
            StatusReason = journey.StatusReason,
            Interruptions = journey.Interruptions,
            PartySize = journey.PartySize,
            CargoWeightKg = journey.CargoWeightKg,
            RealmId = journey.RealmId,
            CreatedAt = journey.CreatedAt,
            ModifiedAt = journey.ModifiedAt,
            ArchivedAt = DateTimeOffset.UtcNow
        };
    }
}
