using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Background service that cleans up expired reservations for matchmade sessions.
/// Runs periodically to:
/// - Find matchmade sessions with expired reservations
/// - Cancel sessions where not enough players claimed their reservations
/// - Notify remaining players of session cancellation
/// - Clean up orphaned session state
/// </summary>
public class ReservationCleanupService : BackgroundService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly ILogger<ReservationCleanupService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    // Use shared constants from GameSessionService to avoid duplication
    private const string SESSION_KEY_PREFIX = GameSessionService.SESSION_KEY_PREFIX;
    private const string SESSION_LIST_KEY = GameSessionService.SESSION_LIST_KEY;

    /// <summary>
    /// Creates a new ReservationCleanupService instance.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for session data access.</param>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="clientEventPublisher">Client event publisher for WebSocket notifications.</param>
    /// <param name="lockProvider">Distributed lock provider for multi-instance coordination.</param>
    /// <param name="configuration">Game session service configuration.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    public ReservationCleanupService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IDistributedLockProvider lockProvider,
        GameSessionServiceConfiguration configuration,
        ILogger<ReservationCleanupService> logger,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _lockProvider = lockProvider;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Executes the cleanup processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "ReservationCleanupService.ExecuteAsync");

        // Wait for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(_configuration.CleanupServiceStartupDelaySeconds), stoppingToken);

        _logger.LogInformation(
            "ReservationCleanupService starting with interval of {IntervalSeconds} seconds",
            _configuration.CleanupIntervalSeconds);

        var interval = TimeSpan.FromSeconds(_configuration.CleanupIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reservation cleanup");
            }

            // Wait for next interval
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("ReservationCleanupService stopped");
    }

    /// <summary>
    /// Cleans up expired reservations from matchmade sessions.
    /// </summary>
    private async Task CleanupExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "ReservationCleanupService.CleanupExpiredReservations");

        // Get all session IDs
        var store = _stateStoreFactory.GetStore<CleanupSessionModel>(StateStoreDefinitions.GameSession);
        var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
        var sessionIds = await listStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

        var now = DateTimeOffset.UtcNow;
        var expiredCount = 0;

        foreach (var sessionId in sessionIds)
        {
            var session = await store.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
            if (session == null)
            {
                continue;
            }

            // Only process matchmade sessions with expired reservations
            if (session.SessionType != SessionType.Matchmade)
            {
                continue;
            }

            if (!session.ReservationExpiresAt.HasValue)
            {
                continue;
            }

            if (session.ReservationExpiresAt.Value > now)
            {
                continue;
            }

            // Reservations have expired - check if enough players claimed
            var claimedCount = session.Reservations.Count(r => r.Claimed);
            var totalReservations = session.Reservations.Count;

            // Session should be cancelled if not enough players joined
            if (claimedCount < totalReservations)
            {
                // Acquire per-session lock to prevent duplicate cancellation across instances
                await using var sessionLock = await _lockProvider.LockAsync(
                    "game-session", SESSION_KEY_PREFIX + sessionId, Guid.NewGuid().ToString(),
                    _configuration.LockTimeoutSeconds, cancellationToken);
                if (!sessionLock.Success)
                {
                    _logger.LogDebug("Could not acquire lock for expired session {SessionId}, another instance is handling it", sessionId);
                    continue;
                }

                // Re-check session state under lock — another instance may have already cancelled it
                var sessionUnderLock = await store.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
                if (sessionUnderLock == null)
                {
                    _logger.LogDebug("Session {SessionId} already deleted by another instance", sessionId);
                    continue;
                }

                _logger.LogInformation(
                    "Cancelling matchmade session {SessionId}: only {Claimed}/{Total} reservations claimed before expiry",
                    sessionId, claimedCount, totalReservations);

                await CancelExpiredSessionAsync(session, sessionId, cancellationToken);
                expiredCount++;
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired matchmade sessions", expiredCount);
        }
    }

    /// <summary>
    /// Cancels an expired matchmade session and notifies players.
    /// </summary>
    private async Task CancelExpiredSessionAsync(CleanupSessionModel session, string sessionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "ReservationCleanupService.CancelExpiredSession");

        try
        {
            // Notify players who claimed their reservations
            foreach (var reservation in session.Reservations.Where(r => r.Claimed))
            {
                try
                {
                    // Find the player in the session
                    var player = session.Players.FirstOrDefault(p => p.AccountId == reservation.AccountId);
                    if (player != null && player.WebSocketSessionId.HasValue)
                    {
                        await _clientEventPublisher.PublishToSessionAsync(
                            player.WebSocketSessionId.Value.ToString(),
                            new SessionCancelledEvent
                            {
                                SessionId = session.SessionId,
                                Reason = "Not enough players joined before reservation expiry"
                            },
                            cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to notify player {AccountId} of session cancellation",
                        reservation.AccountId);
                }
            }

            // Publish server-side domain event
            await _messageBus.TryPublishAsync(
                "game-session.cancelled",
                new GameSessionCancelledEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = session.SessionId,
                    Reason = "Reservation timeout - not enough players"
                },
                cancellationToken);

            // Read full model for lifecycle event before deletion
            var fullStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
            var fullModel = await fullStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
            if (fullModel != null)
            {
                // Publish lifecycle deleted event using shared helper
                await _messageBus.TryPublishAsync(
                    "game-session.deleted",
                    GameSessionService.BuildDeletedEvent(fullModel, "Reservation timeout - not enough players"),
                    cancellationToken);
            }

            // Delete the session
            var store = _stateStoreFactory.GetStore<CleanupSessionModel>(StateStoreDefinitions.GameSession);
            await store.DeleteAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            // Remove from session list under distributed lock (read-modify-write)
            await using var listLock = await _lockProvider.LockAsync(
                "game-session", SESSION_LIST_KEY, Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds, cancellationToken);
            if (listLock.Success)
            {
                var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
                var sessionIds = await listStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();
                if (sessionIds.Remove(sessionId))
                {
                    await listStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);
                }
            }
            else
            {
                _logger.LogWarning("Could not acquire session-list lock when cleaning up session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel expired session {SessionId}", sessionId);
        }
    }
}
