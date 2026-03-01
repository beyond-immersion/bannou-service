using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Background service that periodically scans connections with seasonal availability
/// restrictions and updates their status based on the current season from Worldstate.
/// </summary>
/// <remarks>
/// <para>
/// This worker serves as a safety net alongside the <c>worldstate.season-changed</c>
/// event handler in <see cref="TransitService"/>. The event handler provides immediate
/// response to season transitions; the worker catches any connections that may have
/// been missed due to timing, restarting, or event delivery failures.
/// </para>
/// <para>
/// Runs every <see cref="TransitServiceConfiguration.SeasonalConnectionCheckIntervalSeconds"/>
/// (default 60 seconds). Connections closing for the current season transition to
/// <c>seasonal_closed</c>; connections that should be open transition to <c>open</c>.
/// Uses <c>forceUpdate=true</c> since the seasonal worker is the authoritative source
/// for seasonal state.
/// </para>
/// </remarks>
public class SeasonalConnectionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<SeasonalConnectionWorker> _logger;
    private readonly TransitServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new instance of the SeasonalConnectionWorker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scoped resolution.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Transit service configuration.</param>
    public SeasonalConnectionWorker(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<SeasonalConnectionWorker> logger,
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
        _logger.LogInformation("Seasonal connection worker starting, check interval: {IntervalSeconds}s",
            _configuration.SeasonalConnectionCheckIntervalSeconds);

        // Startup delay to allow other services to initialize (configurable per IMPLEMENTATION TENETS)
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.SeasonalWorkerStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Seasonal connection worker cancelled during startup delay");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSeasonalConnectionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during seasonal connection check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "transit",
                        "SeasonalConnectionWorker.CheckSeasonalConnections",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event for seasonal connection check failure");
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.SeasonalConnectionCheckIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Seasonal connection worker stopped");
    }

    /// <summary>
    /// Scans all connections with seasonal availability restrictions and updates their
    /// status based on the current season from Worldstate for each realm. Groups connections
    /// by realm to minimize Worldstate queries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task CheckSeasonalConnectionsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "SeasonalConnectionWorker.CheckSeasonalConnections");

        _logger.LogDebug("Checking seasonal connection statuses");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var worldstateClient = scope.ServiceProvider.GetRequiredService<Worldstate.IWorldstateClient>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var graphCache = scope.ServiceProvider.GetRequiredService<ITransitConnectionGraphCache>();

        var connectionStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(
            StateStoreDefinitions.TransitConnections);

        // Query all connections that have seasonal availability restrictions
        var seasonalConnections = await connectionStore.QueryAsync(
            c => c.SeasonalAvailability != null,
            cancellationToken);

        if (seasonalConnections.Count == 0)
        {
            _logger.LogDebug("No connections with seasonal restrictions found");
            return;
        }

        // Group by realm to minimize Worldstate API calls
        var connectionsByRealm = new Dictionary<Guid, List<TransitConnectionModel>>();
        foreach (var connection in seasonalConnections)
        {
            if (!connectionsByRealm.ContainsKey(connection.FromRealmId))
            {
                connectionsByRealm[connection.FromRealmId] = new List<TransitConnectionModel>();
            }
            connectionsByRealm[connection.FromRealmId].Add(connection);

            // For cross-realm connections, also group by the destination realm
            if (connection.CrossRealm && connection.ToRealmId != connection.FromRealmId)
            {
                if (!connectionsByRealm.ContainsKey(connection.ToRealmId))
                {
                    connectionsByRealm[connection.ToRealmId] = new List<TransitConnectionModel>();
                }
                // Don't add duplicates -- the connection is already tracked under FromRealmId
            }
        }

        var totalClosed = 0;
        var totalOpened = 0;
        var affectedRealmIds = new HashSet<Guid>();

        foreach (var (realmId, connections) in connectionsByRealm)
        {
            try
            {
                var (closed, opened) = await ProcessRealmSeasonalConnectionsAsync(
                    realmId, connections, connectionStore, worldstateClient, messageBus, cancellationToken);
                totalClosed += closed;
                totalOpened += opened;
                if (closed > 0 || opened > 0)
                {
                    affectedRealmIds.Add(realmId);
                }
            }
            catch (Exception ex)
            {
                // Per-realm error handling: log and continue to next realm
                _logger.LogError(ex, "Error processing seasonal connections for realm {RealmId}", realmId);
                try
                {
                    await messageBus.TryPublishErrorAsync(
                        "transit",
                        "SeasonalConnectionWorker.ProcessRealm",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event for realm {RealmId} seasonal check failure", realmId);
                }
            }
        }

        // Invalidate graph cache for all affected realms
        if (affectedRealmIds.Count > 0)
        {
            await graphCache.InvalidateAsync(affectedRealmIds, cancellationToken);
        }

        if (totalClosed > 0 || totalOpened > 0)
        {
            _logger.LogInformation("Seasonal connection check complete: {ClosedCount} closed, {OpenedCount} opened across {RealmCount} realm(s)",
                totalClosed, totalOpened, connectionsByRealm.Count);
        }
        else
        {
            _logger.LogDebug("Seasonal connection check complete: no status changes");
        }
    }

    /// <summary>
    /// Processes seasonal connections for a single realm. Queries Worldstate for the
    /// current season, then updates connection statuses accordingly.
    /// </summary>
    /// <param name="realmId">The realm to process.</param>
    /// <param name="connections">Connections in this realm with seasonal restrictions.</param>
    /// <param name="connectionStore">Connection state store.</param>
    /// <param name="worldstateClient">Worldstate client for season queries.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (connections closed, connections opened).</returns>
    private async Task<(int closed, int opened)> ProcessRealmSeasonalConnectionsAsync(
        Guid realmId,
        List<TransitConnectionModel> connections,
        IQueryableStateStore<TransitConnectionModel> connectionStore,
        Worldstate.IWorldstateClient worldstateClient,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "SeasonalConnectionWorker.ProcessRealmSeasonalConnections");

        // Get current season for this realm from Worldstate
        string currentSeason;
        try
        {
            var snapshot = await worldstateClient.GetRealmTimeAsync(
                new Worldstate.GetRealmTimeRequest { RealmId = realmId },
                cancellationToken);
            currentSeason = snapshot.Season;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Realm {RealmId} has no Worldstate clock configured, skipping seasonal check", realmId);
            return (0, 0);
        }

        if (string.IsNullOrEmpty(currentSeason))
        {
            _logger.LogDebug("Realm {RealmId} has no current season set, skipping seasonal check", realmId);
            return (0, 0);
        }

        var closedCount = 0;
        var openedCount = 0;

        foreach (var connection in connections)
        {
            if (connection.SeasonalAvailability == null || connection.SeasonalAvailability.Count == 0)
            {
                continue;
            }

            var seasonEntry = connection.SeasonalAvailability
                .FirstOrDefault(s => string.Equals(s.Season, currentSeason, StringComparison.OrdinalIgnoreCase));

            if (seasonEntry == null)
            {
                // No restriction for current season -- reopen if currently seasonal_closed
                if (connection.Status == ConnectionStatus.SeasonalClosed)
                {
                    var connKey = TransitService.BuildConnectionKey(connection.Id);
                    var (freshConnection, connEtag) = await connectionStore.GetWithETagAsync(connKey, cancellationToken);
                    if (freshConnection == null || freshConnection.Status != ConnectionStatus.SeasonalClosed)
                    {
                        _logger.LogDebug("Connection {ConnectionId} was modified concurrently, skipping seasonal reopen", connection.Id);
                        continue;
                    }

                    var previousStatus = freshConnection.Status;
                    freshConnection.Status = ConnectionStatus.Open;
                    freshConnection.StatusReason = $"season_changed:{currentSeason}";
                    freshConnection.StatusChangedAt = DateTimeOffset.UtcNow;
                    freshConnection.ModifiedAt = DateTimeOffset.UtcNow;

                    var savedEtag = await connectionStore.TrySaveAsync(connKey, freshConnection, connEtag ?? string.Empty, cancellationToken);
                    if (savedEtag == null)
                    {
                        _logger.LogWarning("Concurrent modification on connection {ConnectionId} during seasonal reopen, skipping", connection.Id);
                        continue;
                    }

                    await PublishConnectionStatusChangedAsync(
                        freshConnection, previousStatus, messageBus, cancellationToken);
                    openedCount++;
                }
                continue;
            }

            if (!seasonEntry.Available && connection.Status != ConnectionStatus.SeasonalClosed)
            {
                // Should be closed but isn't -- re-fetch with ETag for optimistic concurrency
                var connKey = TransitService.BuildConnectionKey(connection.Id);
                var (freshConnection, connEtag) = await connectionStore.GetWithETagAsync(connKey, cancellationToken);
                if (freshConnection == null || freshConnection.Status == ConnectionStatus.SeasonalClosed)
                {
                    _logger.LogDebug("Connection {ConnectionId} was modified concurrently, skipping seasonal close", connection.Id);
                    continue;
                }

                var previousStatus = freshConnection.Status;
                freshConnection.Status = ConnectionStatus.SeasonalClosed;
                freshConnection.StatusReason = $"seasonal_closure:{currentSeason}";
                freshConnection.StatusChangedAt = DateTimeOffset.UtcNow;
                freshConnection.ModifiedAt = DateTimeOffset.UtcNow;

                var savedEtag = await connectionStore.TrySaveAsync(connKey, freshConnection, connEtag ?? string.Empty, cancellationToken);
                if (savedEtag == null)
                {
                    _logger.LogWarning("Concurrent modification on connection {ConnectionId} during seasonal close, skipping", connection.Id);
                    continue;
                }

                await PublishConnectionStatusChangedAsync(
                    freshConnection, previousStatus, messageBus, cancellationToken);
                closedCount++;
            }
            else if (seasonEntry.Available && connection.Status == ConnectionStatus.SeasonalClosed)
            {
                // Should be open but is currently seasonal_closed -- re-fetch with ETag for optimistic concurrency
                var connKey = TransitService.BuildConnectionKey(connection.Id);
                var (freshConnection, connEtag) = await connectionStore.GetWithETagAsync(connKey, cancellationToken);
                if (freshConnection == null || freshConnection.Status != ConnectionStatus.SeasonalClosed)
                {
                    _logger.LogDebug("Connection {ConnectionId} was modified concurrently, skipping seasonal reopen", connection.Id);
                    continue;
                }

                var previousStatus = freshConnection.Status;
                freshConnection.Status = ConnectionStatus.Open;
                freshConnection.StatusReason = $"season_changed:{currentSeason}";
                freshConnection.StatusChangedAt = DateTimeOffset.UtcNow;
                freshConnection.ModifiedAt = DateTimeOffset.UtcNow;

                var savedEtag = await connectionStore.TrySaveAsync(connKey, freshConnection, connEtag ?? string.Empty, cancellationToken);
                if (savedEtag == null)
                {
                    _logger.LogWarning("Concurrent modification on connection {ConnectionId} during seasonal reopen, skipping", connection.Id);
                    continue;
                }

                await PublishConnectionStatusChangedAsync(
                    freshConnection, previousStatus, messageBus, cancellationToken);
                openedCount++;
            }
        }

        return (closedCount, openedCount);
    }

    /// <summary>
    /// Publishes a transit-connection.status-changed event from the background worker context.
    /// Uses the same event model as the main service but publishes directly via IMessageBus
    /// since the worker operates in its own scope.
    /// </summary>
    /// <param name="connection">The connection after status change.</param>
    /// <param name="previousStatus">The status before the change.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishConnectionStatusChangedAsync(
        TransitConnectionModel connection,
        ConnectionStatus previousStatus,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "SeasonalConnectionWorker.PublishConnectionStatusChanged");

        var eventModel = new TransitConnectionStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ConnectionId = connection.Id,
            FromLocationId = connection.FromLocationId,
            ToLocationId = connection.ToLocationId,
            PreviousStatus = previousStatus,
            NewStatus = connection.Status,
            // StatusReason is nullable (null when status is "open"), but the event schema requires
            // a non-null string for Reason. Empty string represents "no specific reason" for the transition.
            // Coalesce satisfies the required field contract (will execute when StatusReason is legitimately null).
            Reason = connection.StatusReason ?? string.Empty,
            ForceUpdated = true,
            FromRealmId = connection.FromRealmId,
            ToRealmId = connection.ToRealmId,
            CrossRealm = connection.CrossRealm
        };

        var published = await messageBus.TryPublishAsync(
            "transit-connection.status-changed", eventModel, cancellationToken: cancellationToken);
        if (published)
        {
            _logger.LogDebug("Published transit-connection.status-changed event for {ConnectionId}: {PreviousStatus} -> {NewStatus}",
                connection.Id, previousStatus, connection.Status);
        }
        else
        {
            _logger.LogWarning("Failed to publish transit-connection.status-changed event for {ConnectionId}",
                connection.Id);
        }
    }
}
