using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Partial class for TransitService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class TransitService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ITransitService, WorldstateSeasonChangedEvent>(
            "worldstate.season-changed",
            async (svc, evt) => await ((TransitService)svc).HandleSeasonChangedAsync(evt));

    }

    /// <summary>
    /// Handles worldstate.season-changed events. Scans connections in the affected realm
    /// that have seasonal availability restrictions and updates their status accordingly.
    /// Connections closing for the new season transition to <c>seasonal_closed</c>;
    /// connections opening transition to <c>open</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler acts as the immediate response to a season change event. The
    /// <see cref="SeasonalConnectionWorker"/> also runs periodic checks as a safety net.
    /// </para>
    /// <para>
    /// Uses <c>forceUpdate=true</c> for all status changes since the seasonal worker
    /// is the authoritative source for seasonal state per the deep dive document.
    /// </para>
    /// </remarks>
    /// <param name="evt">The season change event data containing realm ID, previous and current season.</param>
    public async Task HandleSeasonChangedAsync(WorldstateSeasonChangedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.transit", "TransitService.HandleSeasonChangedAsync");

        _logger.LogDebug("Handling season change for realm {RealmId}: {PreviousSeason} -> {CurrentSeason}",
            evt.RealmId, evt.PreviousSeason, evt.CurrentSeason);

        // Query connections in this realm that have seasonal availability restrictions
        var realmId = evt.RealmId;
        var connectionsInRealm = await _connectionStore.QueryAsync(
            c => (c.FromRealmId == realmId || c.ToRealmId == realmId) &&
                c.SeasonalAvailability != null,
            CancellationToken.None);

        var closedCount = 0;
        var openedCount = 0;

        foreach (var connection in connectionsInRealm)
        {
            if (connection.SeasonalAvailability == null || connection.SeasonalAvailability.Count == 0)
            {
                continue;
            }

            // Check if the new season has an availability entry for this connection
            var seasonEntry = connection.SeasonalAvailability
                .FirstOrDefault(s => string.Equals(s.Season, evt.CurrentSeason, StringComparison.OrdinalIgnoreCase));

            if (seasonEntry == null)
            {
                // No seasonal restriction for this season -- if currently seasonal_closed, reopen
                if (connection.Status == ConnectionStatus.SeasonalClosed)
                {
                    var previousStatus = connection.Status;
                    connection.Status = ConnectionStatus.Open;
                    connection.StatusReason = $"season_changed:{evt.CurrentSeason}";
                    connection.StatusChangedAt = DateTimeOffset.UtcNow;
                    connection.ModifiedAt = DateTimeOffset.UtcNow;

                    var connKey = BuildConnectionKey(connection.Id);
                    await _connectionStore.SaveAsync(connKey, connection, cancellationToken: CancellationToken.None);

                    await PublishConnectionStatusChangedEventAsync(connection, previousStatus, forceUpdated: true, CancellationToken.None);
                    openedCount++;

                    _logger.LogDebug("Opened connection {ConnectionId} for season {Season} (no restriction entry)",
                        connection.Id, evt.CurrentSeason);
                }
                continue;
            }

            if (!seasonEntry.Available && connection.Status != ConnectionStatus.SeasonalClosed)
            {
                // Connection should be closed for this season but isn't yet
                var previousStatus = connection.Status;
                connection.Status = ConnectionStatus.SeasonalClosed;
                connection.StatusReason = $"seasonal_closure:{evt.CurrentSeason}";
                connection.StatusChangedAt = DateTimeOffset.UtcNow;
                connection.ModifiedAt = DateTimeOffset.UtcNow;

                var connKey = BuildConnectionKey(connection.Id);
                await _connectionStore.SaveAsync(connKey, connection, cancellationToken: CancellationToken.None);

                await PublishConnectionStatusChangedEventAsync(connection, previousStatus, forceUpdated: true, CancellationToken.None);
                closedCount++;

                _logger.LogDebug("Closed connection {ConnectionId} for season {Season}",
                    connection.Id, evt.CurrentSeason);
            }
            else if (seasonEntry.Available && connection.Status == ConnectionStatus.SeasonalClosed)
            {
                // Connection should be open for this season but is currently seasonal_closed
                var previousStatus = connection.Status;
                connection.Status = ConnectionStatus.Open;
                connection.StatusReason = $"season_changed:{evt.CurrentSeason}";
                connection.StatusChangedAt = DateTimeOffset.UtcNow;
                connection.ModifiedAt = DateTimeOffset.UtcNow;

                var connKey = BuildConnectionKey(connection.Id);
                await _connectionStore.SaveAsync(connKey, connection, cancellationToken: CancellationToken.None);

                await PublishConnectionStatusChangedEventAsync(connection, previousStatus, forceUpdated: true, CancellationToken.None);
                openedCount++;

                _logger.LogDebug("Opened connection {ConnectionId} for season {Season}",
                    connection.Id, evt.CurrentSeason);
            }
        }

        // Invalidate graph cache for the affected realm if any connections changed
        if (closedCount > 0 || openedCount > 0)
        {
            await _graphCache.InvalidateAsync(new[] { evt.RealmId }, CancellationToken.None);
        }

        _logger.LogInformation("Season change handled for realm {RealmId}: {ClosedCount} connections closed, {OpenedCount} connections opened",
            evt.RealmId, closedCount, openedCount);
    }
}
