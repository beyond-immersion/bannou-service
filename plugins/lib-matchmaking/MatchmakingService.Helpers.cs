using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Matchmaking.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Matchmaking.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Matchmaking;

// =============================================================================
// MatchmakingService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by MatchmakingService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (MatchmakingService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IMatchmakingService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (MatchmakingService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for MatchmakingService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class MatchmakingService
{
    #region Internal Match Processing

    /// <summary>
    /// Tries to form an immediate match for a newly created ticket.
    /// </summary>
    private async Task TryImmediateMatchAsync(TicketModel ticket, QueueModel queue, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.TryImmediateMatchAsync");
        try
        {
            // Get all tickets in queue
            var ticketIds = await GetQueueTicketIdsAsync(queue.QueueId, cancellationToken);
            if (ticketIds.Count < queue.MinCount)
            {
                return; // Not enough tickets
            }

            var tickets = new List<TicketModel>();
            foreach (var id in ticketIds)
            {
                var t = await LoadTicketAsync(id, cancellationToken);
                if (t != null && t.Status == TicketStatus.Searching)
                {
                    tickets.Add(t);
                }
            }

            // Try to form a match with current skill window (no expansion)
            var currentSkillRange = _algorithm.GetCurrentSkillRange(queue, 0);
            var matchedTickets = _algorithm.TryMatchTickets(tickets, queue, currentSkillRange);

            if (matchedTickets != null && matchedTickets.Count >= queue.MinCount)
            {
                await FormMatchAsync(matchedTickets, queue, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during immediate match check for ticket {TicketId}", ticket.TicketId);
        }
    }

    /// <summary>
    /// Forms a match from matched tickets.
    /// </summary>
    private async Task FormMatchAsync(List<TicketModel> tickets, QueueModel queue, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.FormMatchAsync");
        var matchId = Guid.NewGuid();
        var acceptDeadline = DateTimeOffset.UtcNow.AddSeconds(queue.MatchAcceptTimeoutSeconds);

        _logger.LogInformation("Forming match {MatchId} with {Count} tickets in queue {QueueId}",
            matchId, tickets.Count, queue.QueueId);

        // Create match
        var match = new MatchModel
        {
            MatchId = matchId,
            QueueId = queue.QueueId,
            MatchedTickets = tickets.Select(t => new MatchedTicketModel
            {
                TicketId = t.TicketId,
                AccountId = t.AccountId,
                WebSocketSessionId = t.WebSocketSessionId,
                PartyId = t.PartyId,
                SkillRating = t.SkillRating,
                WaitTimeSeconds = (DateTimeOffset.UtcNow - t.CreatedAt).TotalSeconds
            }).ToList(),
            PlayerCount = tickets.Count,
            AcceptDeadline = acceptDeadline,
            AcceptedPlayers = new List<Guid>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Calculate skill stats
        var ratings = tickets.Where(t => t.SkillRating.HasValue).Select(t => t.SkillRating.GetValueOrDefault()).ToList();
        if (ratings.Count > 0)
        {
            match.AverageSkillRating = ratings.Average();
            match.SkillRatingSpread = ratings.Max() - ratings.Min();
        }

        // Save match
        await SaveMatchAsync(match, cancellationToken);

        // Update tickets
        foreach (var ticket in tickets)
        {
            var previousStatus = ticket.Status;
            ticket.Status = TicketStatus.MatchFound;
            ticket.MatchId = matchId;
            await SaveTicketAsync(ticket, cancellationToken);

            // Publish ticket updated event for status change
            await _messageBus.PublishMatchmakingTicketUpdatedAsync(new MatchmakingTicketUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TicketId = ticket.TicketId,
                PreviousStatus = previousStatus,
                NewStatus = TicketStatus.MatchFound,
                IntervalsElapsed = ticket.IntervalsElapsed,
                CurrentSkillRange = _algorithm.GetCurrentSkillRange(queue, ticket.IntervalsElapsed)
            }, cancellationToken);

            // Store pending match for reconnection
            await StorePendingMatchAsync(ticket.AccountId, matchId, cancellationToken);

            // Update session state
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = ticket.WebSocketSessionId,
                    ServiceId = "matchmaking",
                    NewState = "match_pending"
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update session state for ticket {TicketId}", ticket.TicketId);
            }
        }

        // Send match found events
        foreach (var ticket in tickets)
        {
            await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchFoundClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MatchId = matchId,
                QueueId = queue.QueueId,
                QueueDisplayName = queue.DisplayName,
                PlayerCount = tickets.Count,
                AcceptDeadline = acceptDeadline,
                AcceptTimeoutSeconds = queue.MatchAcceptTimeoutSeconds,
                AverageSkillRating = match.AverageSkillRating,
                PartyId = ticket.PartyId
            }, cancellationToken);

            // Publish accept/decline shortcuts
            await PublishMatchShortcutsAsync(ticket.WebSocketSessionId, ticket.AccountId, matchId, cancellationToken);
        }

        // Publish event - use webSocketSessionId per FOUNDATION TENETS (Account Identity Boundary)
        await _messageBus.PublishMatchmakingMatchFormedAsync(new MatchmakingMatchFormedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            MatchId = matchId,
            QueueId = queue.QueueId,
            Tickets = match.MatchedTickets.Select(t => new MatchedTicketInfo
            {
                TicketId = t.TicketId,
                WebSocketSessionId = t.WebSocketSessionId,
                PartyId = t.PartyId,
                SkillRating = t.SkillRating,
                WaitTimeSeconds = t.WaitTimeSeconds
            }).ToList(),
            PlayerCount = tickets.Count,
            AverageSkillRating = match.AverageSkillRating,
            SkillRatingSpread = match.SkillRatingSpread,
            AcceptDeadline = acceptDeadline
        }, cancellationToken);
    }

    /// <summary>
    /// Finalizes a match when all players have accepted.
    /// </summary>
    private async Task FinalizeMatchAsync(MatchModel match, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.FinalizeMatchAsync");
        _logger.LogInformation("Finalizing match {MatchId}", match.MatchId);

        // Create game session via RPC to lib-game-session
        var expectedPlayers = match.MatchedTickets.Select(t => t.AccountId).ToList();
        var queue = await LoadQueueAsync(match.QueueId, cancellationToken);

        try
        {
            // SessionGameType is now a string (game service stub name)
            var gameType = queue?.SessionGameType ?? "generic";

            var sessionResponse = await _gameSessionClient.CreateGameSessionAsync(new CreateGameSessionRequest
            {
                GameType = gameType,
                SessionName = $"Match {match.MatchId.ToString().Substring(0, 8)}",
                MaxPlayers = match.PlayerCount,
                IsPrivate = true,
                SessionType = SessionType.Matchmade,
                ExpectedPlayers = expectedPlayers,
                ReservationTtlSeconds = _configuration.DefaultReservationTtlSeconds
            }, cancellationToken);

            if (sessionResponse == null)
            {
                _logger.LogError("Failed to create game session for match {MatchId}", match.MatchId);
                await CancelMatchAsync(match, null, cancellationToken);
                return;
            }

            match.GameSessionId = sessionResponse.SessionId;
            await SaveMatchAsync(match, cancellationToken);

            // Get reservations from response
            var reservations = sessionResponse.Reservations ?? new List<ReservationInfo>();

            // Send match confirmed events with reservations
            foreach (var ticket in match.MatchedTickets)
            {
                var reservation = reservations.FirstOrDefault(r => r.AccountId == ticket.AccountId);

                await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchConfirmedClientEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    MatchId = match.MatchId,
                    GameSessionId = sessionResponse.SessionId,
                    ReservationToken = reservation?.Token,
                    JoinDeadlineSeconds = _configuration.DefaultJoinDeadlineSeconds
                }, cancellationToken);

                // Publish join shortcut via game-session service
                if (reservation != null)
                {
                    await _gameSessionClient.PublishJoinShortcutAsync(new PublishJoinShortcutRequest
                    {
                        TargetWebSocketSessionId = ticket.WebSocketSessionId,
                        AccountId = ticket.AccountId,
                        GameSessionId = sessionResponse.SessionId,
                        ReservationToken = reservation.Token
                    }, cancellationToken);
                }

                // Clear matchmaking state
                try
                {
                    await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
                    {
                        SessionId = ticket.WebSocketSessionId,
                        ServiceId = "matchmaking"
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clear matchmaking state for ticket {TicketId}", ticket.TicketId);
                }

                // Remove pending match
                await ClearPendingMatchAsync(ticket.AccountId, cancellationToken);
            }

            // Clean up tickets
            foreach (var ticket in match.MatchedTickets)
            {
                await CleanupTicketAsync(ticket.TicketId, ticket.AccountId, match.QueueId, cancellationToken);
            }

            // Publish event
            await _messageBus.PublishMatchmakingMatchAcceptedAsync(new MatchmakingMatchAcceptedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MatchId = match.MatchId,
                QueueId = match.QueueId,
                GameSessionId = sessionResponse.SessionId,
                PlayerCount = match.PlayerCount,
                AverageWaitTimeSeconds = match.MatchedTickets.Average(t => t.WaitTimeSeconds)
            }, cancellationToken);

            // Update match status to completed
            match.Status = MatchStatus.Completed;
            await SaveMatchAsync(match, cancellationToken);

            _logger.LogInformation("Match {MatchId} finalized with game session {GameSessionId}",
                match.MatchId, sessionResponse.SessionId);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Game session service error finalizing match {MatchId}: {Status}",
                match.MatchId, apiEx.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "FinalizeMatch", "GameSessionServiceError", apiEx.Message,
                dependency: "game-session", endpoint: "create-game-session",
                stack: apiEx.StackTrace, cancellationToken: cancellationToken);
            await CancelMatchAsync(match, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing match {MatchId}", match.MatchId);
            await CancelMatchAsync(match, null, cancellationToken);
        }
    }

    /// <summary>
    /// Cancels a match (due to decline or timeout).
    /// </summary>
    private async Task CancelMatchAsync(MatchModel match, Guid? declinedBy, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.CancelMatchAsync");
        _logger.LogInformation("Cancelling match {MatchId}, declined by {DeclinedBy}",
            match.MatchId, declinedBy);

        // Update match status to cancelled
        match.Status = MatchStatus.Cancelled;
        await SaveMatchAsync(match, cancellationToken);

        var playersToRequeue = new List<MatchedTicketModel>();

        // Notify all players
        foreach (var ticket in match.MatchedTickets)
        {
            // Send cancelled event
            await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchDeclinedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MatchId = match.MatchId,
                QueueId = match.QueueId,
                DeclinedByOther = declinedBy.HasValue && declinedBy.Value != ticket.AccountId,
                AutoRequeued = _configuration.AutoRequeueOnDecline && ticket.AccountId != declinedBy
            }, cancellationToken);

            // Clear pending match
            await ClearPendingMatchAsync(ticket.AccountId, cancellationToken);

            if (ticket.AccountId == declinedBy)
            {
                // Cancel the decliner's ticket
                await CancelTicketInternalAsync(ticket.TicketId, CancelReason.MatchDeclined, cancellationToken);
            }
            else if (_configuration.AutoRequeueOnDecline)
            {
                // Requeue other players
                playersToRequeue.Add(ticket);
            }
            else
            {
                // Cancel all tickets
                await CancelTicketInternalAsync(ticket.TicketId, CancelReason.MatchDeclined, cancellationToken);
            }
        }

        // Requeue players
        foreach (var ticket in playersToRequeue)
        {
            var existingTicket = await LoadTicketAsync(ticket.TicketId, cancellationToken);
            if (existingTicket != null)
            {
                existingTicket.Status = TicketStatus.Searching;
                existingTicket.MatchId = null;
                await SaveTicketAsync(existingTicket, cancellationToken);

                // Publish ticket updated event for requeue status change
                await _messageBus.PublishMatchmakingTicketUpdatedAsync(new MatchmakingTicketUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    TicketId = existingTicket.TicketId,
                    PreviousStatus = TicketStatus.MatchFound,
                    NewStatus = TicketStatus.Searching,
                    IntervalsElapsed = existingTicket.IntervalsElapsed
                }, cancellationToken);

                // Update state back to in_queue
                try
                {
                    await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                    {
                        SessionId = ticket.WebSocketSessionId,
                        ServiceId = "matchmaking",
                        NewState = "in_queue"
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to update permission state for session {SessionId}", ticket.WebSocketSessionId);
                }
            }
        }

        // Delete match
        await DeleteMatchAsync(match.MatchId, cancellationToken);

        // Publish event — use ticket IDs, not account IDs (per FOUNDATION TENETS: account identity boundary)
        var declinedByTicketId = declinedBy.HasValue
            ? match.MatchedTickets.FirstOrDefault(t => t.AccountId == declinedBy.Value)?.TicketId
            : null;
        await _messageBus.PublishMatchmakingMatchDeclinedAsync(new MatchmakingMatchDeclinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            MatchId = match.MatchId,
            QueueId = match.QueueId,
            DeclinedByTicketId = declinedByTicketId,
            AffectedTicketIds = match.MatchedTickets.Select(t => t.TicketId).ToList(),
            RequeuingTicketIds = playersToRequeue.Select(t => t.TicketId).ToList()
        }, cancellationToken);
    }

    /// <summary>
    /// Cancels a ticket internally with reason.
    /// Uses atomic delete as an idempotency gate to prevent concurrent cancellations
    /// of the same ticket from blocking the event dispatch pipeline.
    /// </summary>
    private async Task CancelTicketInternalAsync(Guid ticketId, CancelReason reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.CancelTicketInternalAsync");
        var ticket = await LoadTicketAsync(ticketId, cancellationToken);
        if (ticket == null) return;

        // Atomic idempotency gate: delete the ticket first. Only the thread that
        // successfully deletes it proceeds with the expensive cleanup operations
        // (client event publish, permission HTTP call, service event publish).
        // This prevents concurrent cancellations from the API thread and event
        // handler thread from both running the full cancellation path.
        var wasDeleted = await DeleteTicketAsync(ticketId, cancellationToken);
        if (!wasDeleted)
        {
            _logger.LogDebug(
                "Ticket {TicketId} already cancelled by another thread, skipping duplicate cancellation with reason {Reason}",
                ticketId, reason);
            return;
        }

        var waitTime = (DateTimeOffset.UtcNow - ticket.CreatedAt).TotalSeconds;

        _logger.LogInformation("Cancelling ticket {TicketId} with reason {Reason}", ticketId, reason);

        // Send client event
        await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchmakingCancelledClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TicketId = ticketId,
            QueueId = ticket.QueueId,
            Reason = reason,
            WaitTimeSeconds = waitTime,
            CanRequeue = reason != CancelReason.SessionDisconnected && reason != CancelReason.QueueDisabled
        }, cancellationToken);

        // Clear state
        try
        {
            await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = ticket.WebSocketSessionId,
                ServiceId = "matchmaking"
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear permission state for session {SessionId}", ticket.WebSocketSessionId);
        }

        // Clean up remaining ticket references (ticket itself already deleted above)
        await RemoveFromPlayerTicketsAsync(ticket.AccountId, ticketId, cancellationToken);
        await RemoveFromQueueTicketsAsync(ticket.QueueId, ticketId, cancellationToken);

        // Publish service event - use webSocketSessionId per FOUNDATION TENETS (Account Identity Boundary)
        await _messageBus.PublishMatchmakingTicketCancelledAsync(new MatchmakingTicketCancelledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TicketId = ticketId,
            QueueId = ticket.QueueId,
            WebSocketSessionId = ticket.WebSocketSessionId,
            PartyId = ticket.PartyId,
            Reason = (CancelReason)reason,
            WaitTimeSeconds = waitTime
        }, cancellationToken);
    }

    /// <summary>
    /// Cleans up all ticket references.
    /// </summary>
    private async Task CleanupTicketAsync(Guid ticketId, Guid accountId, string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.CleanupTicketAsync");
        await DeleteTicketAsync(ticketId, cancellationToken);
        await RemoveFromPlayerTicketsAsync(accountId, ticketId, cancellationToken);
        await RemoveFromQueueTicketsAsync(queueId, ticketId, cancellationToken);
    }

    #endregion

    #region Shortcut Management

    /// <summary>
    /// Publishes matchmaking shortcuts (leave, status) after joining queue.
    /// </summary>
    private async Task PublishMatchmakingShortcutsAsync(Guid sessionId, Guid accountId, Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.PublishMatchmakingShortcutsAsync");
        try
        {
            var sessionIdStr = sessionId.ToString();

            // Leave shortcut
            var leaveRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionIdStr, "matchmaking_leave", "matchmaking", _serverSalt);
            var leaveTargetGuid = GuidGenerator.GenerateServiceGuid(sessionIdStr, "matchmaking/leave", _serverSalt);

            await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Shortcut = new SessionShortcut
                {
                    RouteGuid = leaveRouteGuid,
                    TargetGuid = leaveTargetGuid,
                    BoundPayload = BannouJson.Serialize(new LeaveMatchmakingRequest
                    {
                        WebSocketSessionId = sessionId,
                        AccountId = accountId,
                        TicketId = ticketId
                    }),
                    Metadata = new SessionShortcutMetadata
                    {
                        Name = "matchmaking_leave",
                        Description = "Leave matchmaking queue",
                        SourceService = "matchmaking",
                        TargetService = "matchmaking",
                        TargetMethod = "POST",
                        TargetEndpoint = "/matchmaking/leave",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                ReplaceExisting = true
            }, cancellationToken);

            // Status shortcut
            var statusRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionIdStr, "matchmaking_status", "matchmaking", _serverSalt);
            var statusTargetGuid = GuidGenerator.GenerateServiceGuid(sessionIdStr, "matchmaking/status", _serverSalt);

            await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Shortcut = new SessionShortcut
                {
                    RouteGuid = statusRouteGuid,
                    TargetGuid = statusTargetGuid,
                    BoundPayload = BannouJson.Serialize(new GetMatchmakingStatusRequest
                    {
                        WebSocketSessionId = sessionId,
                        AccountId = accountId,
                        TicketId = ticketId
                    }),
                    Metadata = new SessionShortcutMetadata
                    {
                        Name = "matchmaking_status",
                        Description = "Get matchmaking status",
                        SourceService = "matchmaking",
                        TargetService = "matchmaking",
                        TargetMethod = "POST",
                        TargetEndpoint = "/matchmaking/status",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                ReplaceExisting = true
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish matchmaking shortcuts for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Publishes match shortcuts (accept, decline) after match formation.
    /// </summary>
    private async Task PublishMatchShortcutsAsync(Guid sessionId, Guid accountId, Guid matchId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.PublishMatchShortcutsAsync");
        try
        {
            var sessionIdStr = sessionId.ToString();

            // Accept shortcut
            var acceptRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionIdStr, "matchmaking_accept", "matchmaking", _serverSalt);
            var acceptTargetGuid = GuidGenerator.GenerateServiceGuid(sessionIdStr, "matchmaking/accept", _serverSalt);

            await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Shortcut = new SessionShortcut
                {
                    RouteGuid = acceptRouteGuid,
                    TargetGuid = acceptTargetGuid,
                    BoundPayload = BannouJson.Serialize(new AcceptMatchRequest
                    {
                        WebSocketSessionId = sessionId,
                        AccountId = accountId,
                        MatchId = matchId
                    }),
                    Metadata = new SessionShortcutMetadata
                    {
                        Name = "matchmaking_accept",
                        Description = "Accept match",
                        SourceService = "matchmaking",
                        TargetService = "matchmaking",
                        TargetMethod = "POST",
                        TargetEndpoint = "/matchmaking/accept",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                ReplaceExisting = true
            }, cancellationToken);

            // Decline shortcut
            var declineRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(sessionIdStr, "matchmaking_decline", "matchmaking", _serverSalt);
            var declineTargetGuid = GuidGenerator.GenerateServiceGuid(sessionIdStr, "matchmaking/decline", _serverSalt);

            await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Shortcut = new SessionShortcut
                {
                    RouteGuid = declineRouteGuid,
                    TargetGuid = declineTargetGuid,
                    BoundPayload = BannouJson.Serialize(new DeclineMatchRequest
                    {
                        WebSocketSessionId = sessionId,
                        AccountId = accountId,
                        MatchId = matchId
                    }),
                    Metadata = new SessionShortcutMetadata
                    {
                        Name = "matchmaking_decline",
                        Description = "Decline match",
                        SourceService = "matchmaking",
                        TargetService = "matchmaking",
                        TargetMethod = "POST",
                        TargetEndpoint = "/matchmaking/decline",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                ReplaceExisting = true
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish match shortcuts for session {SessionId}", sessionId);
        }
    }

    #endregion

    #region State Store Helpers

    private async Task<List<string>> GetQueueIdsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.GetQueueIdsAsync");
        return await _stringListStore.GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
    }

    private async Task AddToQueueListAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.AddToQueueListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, QUEUE_LIST_KEY, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue list lock");
            return;
        }

        var list = await _stringListStore.GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
        if (!list.Contains(queueId))
        {
            list.Add(queueId);
            await _stringListStore.SaveAsync(QUEUE_LIST_KEY, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromQueueListAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.RemoveFromQueueListAsync");
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, QUEUE_LIST_KEY, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue list lock");
            return;
        }

        var list = await _stringListStore.GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
        if (list.Remove(queueId))
        {
            await _stringListStore.SaveAsync(QUEUE_LIST_KEY, list, cancellationToken: cancellationToken);
        }
    }

    private async Task<QueueModel?> LoadQueueAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.LoadQueueAsync");
        return await _queueStore.GetAsync(QUEUE_KEY_PREFIX + queueId, cancellationToken);
    }

    private async Task SaveQueueAsync(QueueModel queue, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.SaveQueueAsync");
        await _queueStore.SaveAsync(QUEUE_KEY_PREFIX + queue.QueueId, queue, cancellationToken: cancellationToken);
    }

    private async Task<TicketModel?> LoadTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.LoadTicketAsync");
        return await _ticketStore.GetAsync(TICKET_KEY_PREFIX + ticketId, cancellationToken);
    }

    private async Task SaveTicketAsync(TicketModel ticket, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.SaveTicketAsync");
        await _ticketStore.SaveAsync(TICKET_KEY_PREFIX + ticket.TicketId, ticket, cancellationToken: cancellationToken);
    }

    private async Task<bool> DeleteTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.DeleteTicketAsync");
        return await _ticketStore.DeleteAsync(TICKET_KEY_PREFIX + ticketId, cancellationToken);
    }

    private async Task<MatchModel?> LoadMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.LoadMatchAsync");
        return await _matchStore.GetAsync(MATCH_KEY_PREFIX + matchId, cancellationToken);
    }

    private async Task SaveMatchAsync(MatchModel match, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.SaveMatchAsync");
        await _matchStore.SaveAsync(MATCH_KEY_PREFIX + match.MatchId, match, cancellationToken: cancellationToken);
    }

    private async Task DeleteMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.DeleteMatchAsync");
        await _matchStore.DeleteAsync(MATCH_KEY_PREFIX + matchId, cancellationToken);
    }

    private async Task<List<Guid>> GetPlayerTicketsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.GetPlayerTicketsAsync");
        return await _guidListStore.GetAsync(PLAYER_TICKETS_PREFIX + accountId, cancellationToken) ?? new List<Guid>();
    }

    private async Task AddToPlayerTicketsAsync(Guid accountId, Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.AddToPlayerTicketsAsync");
        var key = PLAYER_TICKETS_PREFIX + accountId;
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire player tickets lock for {AccountId}", accountId);
            return;
        }

        var list = await _guidListStore.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (!list.Contains(ticketId))
        {
            list.Add(ticketId);
            await _guidListStore.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromPlayerTicketsAsync(Guid accountId, Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.RemoveFromPlayerTicketsAsync");
        var key = PLAYER_TICKETS_PREFIX + accountId;
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire player tickets lock for {AccountId}", accountId);
            return;
        }

        var list = await _guidListStore.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (list.Remove(ticketId))
        {
            await _guidListStore.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task<List<Guid>> GetQueueTicketIdsAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.GetQueueTicketIdsAsync");
        return await _guidListStore.GetAsync(QUEUE_TICKETS_PREFIX + queueId, cancellationToken) ?? new List<Guid>();
    }

    private async Task<int> GetQueueTicketCountAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.GetQueueTicketCountAsync");
        var ids = await GetQueueTicketIdsAsync(queueId, cancellationToken);
        return ids.Count;
    }

    private async Task AddToQueueTicketsAsync(string queueId, Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.AddToQueueTicketsAsync");
        var key = QUEUE_TICKETS_PREFIX + queueId;
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue tickets lock for {QueueId}", queueId);
            return;
        }

        var list = await _guidListStore.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (!list.Contains(ticketId))
        {
            list.Add(ticketId);
            await _guidListStore.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromQueueTicketsAsync(string queueId, Guid ticketId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.RemoveFromQueueTicketsAsync");
        var key = QUEUE_TICKETS_PREFIX + queueId;
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, key, Guid.NewGuid().ToString(), _configuration.ListLockTimeoutSeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue tickets lock for {QueueId}", queueId);
            return;
        }

        var list = await _guidListStore.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (list.Remove(ticketId))
        {
            await _guidListStore.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task StorePendingMatchAsync(Guid accountId, Guid matchId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.StorePendingMatchAsync");
        var wrapper = new PendingMatchWrapper { MatchId = matchId };
        var options = new StateOptions { Ttl = _configuration.PendingMatchRedisKeyTtlSeconds };
        await _pendingMatchStore.SaveAsync(PENDING_MATCH_PREFIX + accountId, wrapper, options, cancellationToken);
    }

    private async Task<Guid?> GetPendingMatchAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.GetPendingMatchAsync");
        var wrapper = await _pendingMatchStore.GetAsync(PENDING_MATCH_PREFIX + accountId, cancellationToken);
        return wrapper?.MatchId;
    }

    private async Task ClearPendingMatchAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.ClearPendingMatchAsync");
        await _pendingMatchStore.DeleteAsync(PENDING_MATCH_PREFIX + accountId, cancellationToken);
    }

    #endregion

    #region Helper Methods

    private static QueueResponse MapQueueModelToResponse(QueueModel queue)
    {
        return new QueueResponse
        {
            QueueId = queue.QueueId,
            GameId = queue.GameId,
            SessionGameType = queue.SessionGameType,
            DisplayName = queue.DisplayName,
            Description = queue.Description,
            Enabled = queue.Enabled,
            MinCount = queue.MinCount,
            MaxCount = queue.MaxCount,
            CountMultiple = queue.CountMultiple,
            IntervalSeconds = queue.IntervalSeconds,
            MaxIntervals = queue.MaxIntervals,
            SkillExpansion = queue.SkillExpansion?.Select(s => new SkillExpansionStep
            {
                Intervals = s.Intervals,
                Range = s.Range
            }).ToList(),
            PartySkillAggregation = queue.PartySkillAggregation,
            PartySkillWeights = queue.PartySkillWeights,
            PartyMaxSize = queue.PartyMaxSize,
            AllowConcurrent = queue.AllowConcurrent,
            ExclusiveGroup = queue.ExclusiveGroup,
            UseSkillRating = queue.UseSkillRating,
            RatingCategory = queue.RatingCategory,
            StartWhenMinimumReached = queue.StartWhenMinimumReached,
            RequiresRegistration = queue.RequiresRegistration,
            TournamentIdRequired = queue.TournamentIdRequired,
            MatchAcceptTimeoutSeconds = queue.MatchAcceptTimeoutSeconds,
            CreatedAt = queue.CreatedAt,
            UpdatedAt = queue.UpdatedAt
        };
    }

    #endregion

    #region Background Processing

    /// <summary>
    /// Processes all active queues for interval-based matching.
    /// Called by MatchmakingBackgroundService at configured intervals.
    /// </summary>
    internal async Task ProcessAllQueuesAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.ProcessAllQueuesAsync");
        var queueIds = await GetQueueIdsAsync(cancellationToken);
        _logger.LogDebug("Processing {Count} queues for interval matching", queueIds.Count);

        foreach (var queueId in queueIds)
        {
            try
            {
                await ProcessQueueIntervalAsync(queueId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue {QueueId} for interval matching", queueId);
            }
        }
    }

    /// <summary>
    /// Processes a single queue for interval-based matching.
    /// Increments ticket intervals and tries to form matches.
    /// </summary>
    private async Task ProcessQueueIntervalAsync(string queueId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.ProcessQueueIntervalAsync");
        var queue = await LoadQueueAsync(queueId, cancellationToken);
        if (queue == null || !queue.Enabled)
        {
            return;
        }

        var ticketIds = await GetQueueTicketIdsAsync(queueId, cancellationToken);
        if (ticketIds.Count == 0)
        {
            return;
        }

        var tickets = new List<TicketModel>();
        var ticketsToTimeout = new List<TicketModel>();

        foreach (var ticketId in ticketIds)
        {
            var ticket = await LoadTicketAsync(ticketId, cancellationToken);
            if (ticket == null)
            {
                continue;
            }

            if (ticket.Status != TicketStatus.Searching)
            {
                continue;
            }

            // Increment interval counter
            ticket.IntervalsElapsed++;
            await SaveTicketAsync(ticket, cancellationToken);

            // Publish ticket updated event for skill window expansion
            await _messageBus.PublishMatchmakingTicketUpdatedAsync(new MatchmakingTicketUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TicketId = ticket.TicketId,
                PreviousStatus = TicketStatus.Searching,
                NewStatus = TicketStatus.Searching,
                IntervalsElapsed = ticket.IntervalsElapsed,
                CurrentSkillRange = _algorithm.GetCurrentSkillRange(queue, ticket.IntervalsElapsed)
            }, cancellationToken);

            // Check for timeout
            if (ticket.IntervalsElapsed >= queue.MaxIntervals)
            {
                if (queue.StartWhenMinimumReached)
                {
                    // Will try to start with minimum players
                    tickets.Add(ticket);
                }
                else
                {
                    // Mark for timeout cancellation
                    ticketsToTimeout.Add(ticket);
                }
            }
            else
            {
                tickets.Add(ticket);
            }
        }

        // Cancel timed-out tickets
        foreach (var ticket in ticketsToTimeout)
        {
            _logger.LogInformation("Ticket {TicketId} timed out after {Intervals} intervals",
                ticket.TicketId, ticket.IntervalsElapsed);
            await CancelTicketInternalAsync(
                ticket.TicketId,
                CancelReason.Timeout,
                cancellationToken);
        }

        // Try to form matches with remaining tickets
        if (tickets.Count >= queue.MinCount)
        {
            // Group tickets by intervals elapsed and process highest first (longest wait)
            var groups = tickets
                .GroupBy(t => t.IntervalsElapsed)
                .OrderByDescending(g => g.Key);

            foreach (var group in groups)
            {
                var groupTickets = group.ToList();
                var skillRange = _algorithm.GetCurrentSkillRange(queue, group.Key);

                _logger.LogDebug("Processing {Count} tickets at interval {Interval} with skill range {Range}",
                    groupTickets.Count, group.Key, skillRange?.ToString() ?? "unlimited");

                // Try to match tickets in this interval group
                while (groupTickets.Count >= queue.MinCount)
                {
                    var matched = _algorithm.TryMatchTickets(groupTickets, queue, skillRange);
                    if (matched != null && matched.Count >= queue.MinCount)
                    {
                        await FormMatchAsync(matched, queue, cancellationToken);

                        // Remove matched tickets from consideration
                        foreach (var t in matched)
                        {
                            groupTickets.Remove(t);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Publishes matchmaking statistics event.
    /// Called by MatchmakingBackgroundService at configured intervals.
    /// </summary>
    internal async Task PublishStatsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.matchmaking", "MatchmakingService.PublishStatsAsync");
        try
        {
            var queueIds = await GetQueueIdsAsync(cancellationToken);
            var stats = new List<Events.QueueStatsSnapshot>();

            foreach (var queueId in queueIds)
            {
                var queue = await LoadQueueAsync(queueId, cancellationToken);
                if (queue == null)
                {
                    continue;
                }

                var ticketCount = await GetQueueTicketCountAsync(queueId, cancellationToken);
                stats.Add(new Events.QueueStatsSnapshot
                {
                    QueueId = queueId,
                    ActiveTickets = ticketCount,
                    MatchesFormedSinceLastSnapshot = 0 // Would need tracking between snapshots
                });
            }

            var statsEvent = new Events.MatchmakingStatsEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Stats = stats
            };

            await _messageBus.PublishMatchmakingStatsAsync(statsEvent, cancellationToken);
            _logger.LogDebug("Published matchmaking stats: {QueueCount} queues, {TotalTickets} tickets",
                stats.Count, stats.Sum(s => s.ActiveTickets));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish matchmaking stats event");
        }
    }

    #endregion
}
