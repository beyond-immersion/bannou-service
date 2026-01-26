using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;
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
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly ILogger<ReservationCleanupService> _logger;

    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";

    /// <summary>
    /// Creates a new ReservationCleanupService instance.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for session data access.</param>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="clientEventPublisher">Client event publisher for WebSocket notifications.</param>
    /// <param name="configuration">Game session service configuration.</param>
    /// <param name="logger">Logger for this service.</param>
    public ReservationCleanupService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        GameSessionServiceConfiguration configuration,
        ILogger<ReservationCleanupService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Executes the cleanup processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        try
        {
            // Notify players who claimed their reservations
            foreach (var reservation in session.Reservations.Where(r => r.Claimed))
            {
                try
                {
                    // Find the player in the session
                    var player = session.Players.FirstOrDefault(p => p.AccountId == reservation.AccountId);
                    if (player != null && !string.IsNullOrEmpty(player.WebSocketSessionId))
                    {
                        await _clientEventPublisher.PublishToSessionAsync(
                            player.WebSocketSessionId,
                            new SessionCancelledClientEvent
                            {
                                SessionId = sessionId,
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

            // Publish server-side event
            await _messageBus.TryPublishAsync(
                "game-session.session-cancelled",
                new SessionCancelledServerEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = sessionId,
                    Reason = "Reservation timeout - not enough players"
                },
                cancellationToken);

            // Delete the session
            var store = _stateStoreFactory.GetStore<CleanupSessionModel>(StateStoreDefinitions.GameSession);
            await store.DeleteAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            // Remove from list
            var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
            var sessionIds = await listStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();
            if (sessionIds.Remove(sessionId))
            {
                await listStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel expired session {SessionId}", sessionId);
        }
    }
}

/// <summary>
/// Minimal session model for cleanup service.
/// Only includes fields needed for reservation cleanup.
/// </summary>
internal class CleanupSessionModel
{
    public Guid SessionId { get; set; }
    public SessionType SessionType { get; set; } = SessionType.Lobby;
    public List<CleanupReservationModel> Reservations { get; set; } = new();
    public DateTimeOffset? ReservationExpiresAt { get; set; }
    public List<CleanupPlayerModel> Players { get; set; } = new();
}

/// <summary>
/// Minimal reservation model for cleanup service.
/// </summary>
internal class CleanupReservationModel
{
    public Guid AccountId { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>
/// Minimal player model for cleanup service.
/// </summary>
internal class CleanupPlayerModel
{
    public Guid AccountId { get; set; }
    public string WebSocketSessionId { get; set; } = string.Empty;
}

/// <summary>
/// Client event for session cancellation.
/// </summary>
internal class SessionCancelledClientEvent : BaseClientEvent
{
    public override string EventName => "game-session.session_cancelled";
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Server-side event for session cancellation.
/// </summary>
internal class SessionCancelledServerEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
