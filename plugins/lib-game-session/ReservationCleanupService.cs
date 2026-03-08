using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly ILogger<ReservationCleanupService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    // Use shared constants from GameSessionService to avoid duplication
    private const string SESSION_KEY_PREFIX = GameSessionService.SESSION_KEY_PREFIX;
    private const string SESSION_LIST_KEY = GameSessionService.SESSION_LIST_KEY;

    /// <summary>
    /// Creates a new ReservationCleanupService instance.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped service access (FOUNDATION TENETS).</param>
    /// <param name="configuration">Game session service configuration.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    public ReservationCleanupService(
        IServiceProvider serviceProvider,
        GameSessionServiceConfiguration configuration,
        ILogger<ReservationCleanupService> logger,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Executes the cleanup processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for other services to initialize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.CleanupServiceStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

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
                await _serviceProvider.TryPublishWorkerErrorAsync("game-session", "ReservationCleanup", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("ReservationCleanupService stopped");
    }

    /// <summary>
    /// Cleans up expired reservations from matchmade sessions.
    /// Creates a DI scope to resolve state stores once per cleanup cycle (FOUNDATION TENETS).
    /// </summary>
    private async Task CleanupExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "ReservationCleanupService.CleanupExpiredReservations");

        // Create a scope to resolve scoped services (BackgroundService cannot constructor-inject them)
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var cleanupStore = stateStoreFactory.GetStore<CleanupSessionModel>(StateStoreDefinitions.GameSession);
        var sessionListStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
        var fullSessionStore = stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var clientEventPublisher = scope.ServiceProvider.GetRequiredService<IClientEventPublisher>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Get all session IDs
        var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

        var now = DateTimeOffset.UtcNow;
        var expiredCount = 0;

        foreach (var sessionId in sessionIds)
        {
            var session = await cleanupStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
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
                await using var sessionLock = await lockProvider.LockAsync(
                    StateStoreDefinitions.GameSessionLock, SESSION_KEY_PREFIX + sessionId, Guid.NewGuid().ToString(),
                    _configuration.LockTimeoutSeconds, cancellationToken);
                if (!sessionLock.Success)
                {
                    _logger.LogDebug("Could not acquire lock for expired session {SessionId}, another instance is handling it", sessionId);
                    continue;
                }

                // Re-check session state under lock — another instance may have already cancelled it
                var sessionUnderLock = await cleanupStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
                if (sessionUnderLock == null)
                {
                    _logger.LogDebug("Session {SessionId} already deleted by another instance", sessionId);
                    continue;
                }

                _logger.LogInformation(
                    "Cancelling matchmade session {SessionId}: only {Claimed}/{Total} reservations claimed before expiry",
                    sessionId, claimedCount, totalReservations);

                await CancelExpiredSessionAsync(session, sessionId, cleanupStore, sessionListStore, fullSessionStore, messageBus, clientEventPublisher, lockProvider, cancellationToken);
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
    /// Receives pre-resolved services from the caller's scope (FOUNDATION TENETS).
    /// </summary>
    /// <param name="session">The cleanup session model for the expired session.</param>
    /// <param name="sessionId">The session ID string.</param>
    /// <param name="cleanupStore">Pre-resolved cleanup session store.</param>
    /// <param name="sessionListStore">Pre-resolved session list store.</param>
    /// <param name="fullSessionStore">Pre-resolved full session model store.</param>
    /// <param name="messageBus">Scope-resolved message bus for publishing events.</param>
    /// <param name="clientEventPublisher">Scope-resolved client event publisher for WebSocket notifications.</param>
    /// <param name="lockProvider">Scope-resolved distributed lock provider for multi-instance coordination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task CancelExpiredSessionAsync(
        CleanupSessionModel session,
        string sessionId,
        IStateStore<CleanupSessionModel> cleanupStore,
        IStateStore<List<string>> sessionListStore,
        IStateStore<GameSessionModel> fullSessionStore,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IDistributedLockProvider lockProvider,
        CancellationToken cancellationToken)
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
                        await clientEventPublisher.PublishToSessionAsync(
                            player.WebSocketSessionId.Value.ToString(),
                            new SessionCancelledClientEvent
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
            await messageBus.PublishGameSessionCancelledAsync(
                new GameSessionCancelledEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = session.SessionId,
                    Reason = "Reservation timeout - not enough players"
                },
                cancellationToken);

            // Read full model for lifecycle event before deletion
            var fullModel = await fullSessionStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);
            if (fullModel != null)
            {
                // Publish lifecycle deleted event using shared helper
                await messageBus.PublishGameSessionDeletedAsync(
                    GameSessionService.BuildDeletedEvent(fullModel, "Reservation timeout - not enough players"),
                    cancellationToken);
            }

            // Delete the session
            await cleanupStore.DeleteAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            // Remove from session list under distributed lock (read-modify-write)
            await using var listLock = await lockProvider.LockAsync(
                StateStoreDefinitions.GameSessionLock, SESSION_LIST_KEY, Guid.NewGuid().ToString(),
                _configuration.LockTimeoutSeconds, cancellationToken);
            if (listLock.Success)
            {
                var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();
                if (sessionIds.Remove(sessionId))
                {
                    await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);
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
