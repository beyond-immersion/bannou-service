using BeyondImmersion.Bannou.Matchmaking.ClientEvents;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

using ClientCancelReason = BeyondImmersion.Bannou.Matchmaking.ClientEvents.CancelReason;

namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Partial class for MatchmakingService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class MatchmakingService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IMatchmakingService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionConnectedAsync(evt));

        eventConsumer.RegisterHandler<IMatchmakingService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionDisconnectedAsync(evt));

        eventConsumer.RegisterHandler<IMatchmakingService, SessionReconnectedEvent>(
            "session.reconnected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionReconnectedAsync(evt));

    }

    /// <summary>
    /// Handles session.connected events.
    /// New connections don't require any matchmaking action - players must explicitly join queues.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
    {
        // New connections don't require any matchmaking action
        // Players must explicitly join queues via the JoinMatchmaking endpoint
        _logger.LogDebug("Session {SessionId} connected, account {AccountId}",
            evt.SessionId, evt.AccountId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// Cancels all matchmaking tickets for the disconnected player.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        // SessionDisconnectedEvent.AccountId is nullable Guid?
        if (!evt.AccountId.HasValue)
        {
            _logger.LogDebug("Session {SessionId} disconnected without account ID, skipping matchmaking cleanup",
                evt.SessionId);
            return;
        }

        var accountId = evt.AccountId.Value;
        _logger.LogInformation("Session {SessionId} disconnected for account {AccountId}, cleaning up matchmaking state",
            evt.SessionId, accountId);

        try
        {
            // Get all tickets for this player
            var ticketIds = await GetPlayerTicketsAsync(accountId, CancellationToken.None);

            foreach (var ticketId in ticketIds)
            {
                var ticket = await LoadTicketAsync(ticketId, CancellationToken.None);
                if (ticket == null)
                {
                    continue;
                }

                // Only cancel tickets associated with this session
                if (ticket.WebSocketSessionId != evt.SessionId.ToString())
                {
                    continue;
                }

                _logger.LogInformation("Cancelling ticket {TicketId} for disconnected session {SessionId}",
                    ticketId, evt.SessionId);

                // Cancel the ticket
                await CancelTicketInternalAsync(
                    ticket.TicketId,
                    ClientCancelReason.Session_disconnected,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup matchmaking state for disconnected session {SessionId}",
                evt.SessionId);
        }
    }

    /// <summary>
    /// Handles session.reconnected events.
    /// Restores matchmaking state if the player was in a match accept flow.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionReconnectedAsync(SessionReconnectedEvent evt)
    {
        // SessionReconnectedEvent.AccountId is non-nullable Guid
        var accountId = evt.AccountId;
        _logger.LogInformation("Session {SessionId} reconnected for account {AccountId}, checking for pending match",
            evt.SessionId, accountId);

        try
        {
            // Check for pending match (player was in accept flow when disconnected)
            Guid? pendingMatchId = await GetPendingMatchAsync(accountId, CancellationToken.None);

            if (pendingMatchId.HasValue)
            {
                var match = await LoadMatchAsync(pendingMatchId.Value, CancellationToken.None);
                if (match != null && match.Status == MatchStatus.Pending)
                {
                    _logger.LogInformation("Restoring match {MatchId} state for reconnected player {AccountId}",
                        pendingMatchId.Value, accountId);

                    // Re-send the match found event to the reconnected session
                    var playerTicket = match.MatchedTickets.FirstOrDefault(t => t.AccountId == accountId);
                    if (playerTicket != null)
                    {
                        // Update ticket with new session ID
                        var ticket = await LoadTicketAsync(playerTicket.TicketId, CancellationToken.None);
                        if (ticket != null)
                        {
                            ticket.WebSocketSessionId = evt.SessionId.ToString();
                            await SaveTicketAsync(ticket, CancellationToken.None);
                        }

                        // Get queue for display name
                        var queue = await LoadQueueAsync(match.QueueId, CancellationToken.None);

                        // Calculate remaining time to accept
                        var remainingSeconds = Math.Max(0, (int)(match.AcceptDeadline - DateTimeOffset.UtcNow).TotalSeconds);

                        await _clientEventPublisher.PublishToSessionAsync(evt.SessionId.ToString(), new MatchFoundEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            MatchId = match.MatchId,
                            QueueId = match.QueueId,
                            QueueDisplayName = queue?.DisplayName ?? match.QueueId,
                            PlayerCount = match.PlayerCount,
                            AcceptDeadline = match.AcceptDeadline,
                            AcceptTimeoutSeconds = remainingSeconds,
                            AverageSkillRating = match.AverageSkillRating
                        }, CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore matchmaking state for reconnected session {SessionId}",
                evt.SessionId);
        }
    }
}
