using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Matchmaking.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Matchmaking.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
// Alias to distinguish client events CancelReason from service events
using ClientCancelReason = BeyondImmersion.Bannou.Matchmaking.ClientEvents.CancelReason;
using EventCancelReason = BeyondImmersion.BannouService.Events.MatchmakingTicketCancelledEventReason;

[assembly: InternalsVisibleTo("lib-matchmaking.tests")]

namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Implementation of the Matchmaking service.
/// Provides ticket-based matchmaking with skill windows, query matching,
/// and configurable queues.
/// </summary>
[BannouService("matchmaking", typeof(IMatchmakingService), lifetime: ServiceLifetime.Scoped)]
public partial class MatchmakingService : IMatchmakingService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<MatchmakingService> _logger;
    private readonly MatchmakingServiceConfiguration _configuration;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IGameSessionClient _gameSessionClient;
    private readonly IPermissionClient _permissionClient;
    private readonly IMatchmakingAlgorithm _algorithm;
    private readonly IDistributedLockProvider _lockProvider;

    private const string QUEUE_KEY_PREFIX = "queue:";
    private const string QUEUE_LIST_KEY = "queue-list";
    private const string TICKET_KEY_PREFIX = "ticket:";
    private const string MATCH_KEY_PREFIX = "match:";
    private const string PLAYER_TICKETS_PREFIX = "player-tickets:";
    private const string PENDING_MATCH_PREFIX = "pending-match:";
    private const string QUEUE_TICKETS_PREFIX = "queue-tickets:";

    // Event topics
    private const string TICKET_CREATED_TOPIC = "matchmaking.ticket-created";
    private const string TICKET_CANCELLED_TOPIC = "matchmaking.ticket-cancelled";
    private const string MATCH_FORMED_TOPIC = "matchmaking.match-formed";
    private const string MATCH_ACCEPTED_TOPIC = "matchmaking.match-accepted";
    private const string MATCH_DECLINED_TOPIC = "matchmaking.match-declined";

    private readonly string _serverSalt;

    /// <summary>
    /// Creates a new MatchmakingService instance.
    /// </summary>
    public MatchmakingService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<MatchmakingService> logger,
        MatchmakingServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IClientEventPublisher clientEventPublisher,
        IGameSessionClient gameSessionClient,
        IPermissionClient permissionClient,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _clientEventPublisher = clientEventPublisher;
        _gameSessionClient = gameSessionClient;
        _permissionClient = permissionClient;
        _lockProvider = lockProvider;
        // Instantiate directly since internal types are used
        // Tests can access via InternalsVisibleTo
        _algorithm = new MatchmakingAlgorithm();

        // Server salt from configuration - REQUIRED
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "MATCHMAKING_SERVER_SALT is required. All service instances must share the same salt for matchmaking GUIDs to work correctly.");
        }
        _serverSalt = configuration.ServerSalt;

        // Register event handlers
        RegisterEventConsumers(eventConsumer);
    }

    #region Queue Management

    /// <summary>
    /// Lists available matchmaking queues.
    /// </summary>
    public async Task<(StatusCodes, ListQueuesResponse?)> ListQueuesAsync(
        ListQueuesRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Listing matchmaking queues - GameId: {GameId}, IncludeDisabled: {IncludeDisabled}",
                body.GameId, body.IncludeDisabled);

            var queueIds = await GetQueueIdsAsync(cancellationToken);
            var queues = new List<QueueSummary>();

            foreach (var queueId in queueIds)
            {
                var queue = await LoadQueueAsync(queueId, cancellationToken);
                if (queue == null) continue;

                // Filter by game ID if provided
                if (!string.IsNullOrEmpty(body.GameId) && queue.GameId != body.GameId)
                    continue;

                // Filter disabled queues unless requested
                if (!queue.Enabled && body.IncludeDisabled != true)
                    continue;

                // Get current ticket count
                var ticketCount = await GetQueueTicketCountAsync(queueId, cancellationToken);

                queues.Add(new QueueSummary
                {
                    QueueId = queue.QueueId,
                    GameId = queue.GameId,
                    DisplayName = queue.DisplayName,
                    Enabled = queue.Enabled,
                    MinCount = queue.MinCount,
                    MaxCount = queue.MaxCount,
                    CurrentTickets = ticketCount,
                    AverageWaitSeconds = queue.AverageWaitSeconds
                });
            }

            return (StatusCodes.OK, new ListQueuesResponse { Queues = queues });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list matchmaking queues");
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "ListQueues", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/queue/list",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets details of a specific queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> GetQueueAsync(
        GetQueueRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting queue {QueueId}", body.QueueId);

            var queue = await LoadQueueAsync(body.QueueId, cancellationToken);
            if (queue == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapQueueModelToResponse(queue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue {QueueId}", body.QueueId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "GetQueue", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/queue/get",
                details: new { QueueId = body.QueueId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new matchmaking queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> CreateQueueAsync(
        CreateQueueRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Creating queue {QueueId} for game {GameId}", body.QueueId, body.GameId);

            // Check if queue already exists
            var existing = await LoadQueueAsync(body.QueueId, cancellationToken);
            if (existing != null)
            {
                _logger.LogWarning("Queue {QueueId} already exists", body.QueueId);
                return (StatusCodes.Conflict, null);
            }

            var queue = new QueueModel
            {
                QueueId = body.QueueId,
                GameId = body.GameId,
                SessionGameType = body.SessionGameType,
                DisplayName = body.DisplayName,
                Description = body.Description,
                Enabled = true,
                MinCount = body.MinCount,
                MaxCount = body.MaxCount,
                CountMultiple = body.CountMultiple > 0 ? body.CountMultiple : 1,
                IntervalSeconds = body.IntervalSeconds > 0 ? body.IntervalSeconds : _configuration.ProcessingIntervalSeconds,
                MaxIntervals = body.MaxIntervals > 0 ? body.MaxIntervals : _configuration.DefaultMaxIntervals,
                SkillExpansion = body.SkillExpansion?.Select(s => new SkillExpansionStepModel
                {
                    Intervals = s.Intervals,
                    Range = s.Range
                }).ToList(),
                PartySkillAggregation = body.PartySkillAggregation,
                PartySkillWeights = body.PartySkillWeights?.ToList(),
                PartyMaxSize = body.PartyMaxSize,
                AllowConcurrent = body.AllowConcurrent,
                ExclusiveGroup = body.ExclusiveGroup,
                UseSkillRating = body.UseSkillRating,
                RatingCategory = body.RatingCategory,
                StartWhenMinimumReached = body.StartWhenMinimumReached,
                RequiresRegistration = body.RequiresRegistration,
                TournamentIdRequired = body.TournamentIdRequired,
                MatchAcceptTimeoutSeconds = body.MatchAcceptTimeoutSeconds > 0
                    ? body.MatchAcceptTimeoutSeconds
                    : _configuration.DefaultMatchAcceptTimeoutSeconds,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Save queue
            await SaveQueueAsync(queue, cancellationToken);

            // Add to queue list
            await AddToQueueListAsync(queue.QueueId, cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync("matchmaking.queue-created", new MatchmakingQueueCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                QueueId = queue.QueueId,
                GameId = queue.GameId,
                DisplayName = queue.DisplayName,
                Enabled = queue.Enabled,
                MinCount = queue.MinCount,
                MaxCount = queue.MaxCount,
                CreatedAt = queue.CreatedAt
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Queue {QueueId} created successfully", queue.QueueId);
            return (StatusCodes.OK, MapQueueModelToResponse(queue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create queue {QueueId}", body.QueueId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "CreateQueue", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/queue/create",
                details: new { QueueId = body.QueueId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a matchmaking queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> UpdateQueueAsync(
        UpdateQueueRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Updating queue {QueueId}", body.QueueId);

            var queueStore = _stateStoreFactory.GetStore<QueueModel>(StateStoreDefinitions.Matchmaking);
            var queueKey = QUEUE_KEY_PREFIX + body.QueueId;
            var (queue, etag) = await queueStore.GetWithETagAsync(queueKey, cancellationToken);
            if (queue == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Apply updates
            if (!string.IsNullOrEmpty(body.DisplayName)) queue.DisplayName = body.DisplayName;
            if (body.Description != null) queue.Description = body.Description;
            if (body.Enabled.HasValue) queue.Enabled = body.Enabled.Value;
            if (body.MinCount.HasValue) queue.MinCount = body.MinCount.Value;
            if (body.MaxCount.HasValue) queue.MaxCount = body.MaxCount.Value;
            if (body.CountMultiple.HasValue) queue.CountMultiple = body.CountMultiple.Value;
            if (body.IntervalSeconds.HasValue) queue.IntervalSeconds = body.IntervalSeconds.Value;
            if (body.MaxIntervals.HasValue) queue.MaxIntervals = body.MaxIntervals.Value;
            if (body.SkillExpansion != null)
            {
                queue.SkillExpansion = body.SkillExpansion.Select(s => new SkillExpansionStepModel
                {
                    Intervals = s.Intervals,
                    Range = s.Range
                }).ToList();
            }
            if (body.PartySkillAggregation.HasValue) queue.PartySkillAggregation = body.PartySkillAggregation.Value;
            if (body.PartyMaxSize.HasValue) queue.PartyMaxSize = body.PartyMaxSize.Value;
            if (body.MatchAcceptTimeoutSeconds.HasValue) queue.MatchAcceptTimeoutSeconds = body.MatchAcceptTimeoutSeconds.Value;

            queue.UpdatedAt = DateTimeOffset.UtcNow;

            // Save queue with ETag
            var newEtag = await queueStore.TrySaveAsync(queueKey, queue, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for queue {QueueId}", body.QueueId);
                return (StatusCodes.Conflict, null);
            }

            // Publish event
            await _messageBus.TryPublishAsync("matchmaking.queue-updated", new MatchmakingQueueUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                QueueId = queue.QueueId,
                GameId = queue.GameId,
                DisplayName = queue.DisplayName,
                Enabled = queue.Enabled,
                MinCount = queue.MinCount,
                MaxCount = queue.MaxCount,
                CreatedAt = queue.CreatedAt
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Queue {QueueId} updated successfully", queue.QueueId);
            return (StatusCodes.OK, MapQueueModelToResponse(queue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update queue {QueueId}", body.QueueId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "UpdateQueue", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/queue/update",
                details: new { QueueId = body.QueueId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a matchmaking queue.
    /// </summary>
    public async Task<StatusCodes> DeleteQueueAsync(
        DeleteQueueRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Deleting queue {QueueId}", body.QueueId);

            var queue = await LoadQueueAsync(body.QueueId, cancellationToken);
            if (queue == null)
            {
                return StatusCodes.NotFound;
            }

            // Cancel all tickets in the queue
            var ticketIds = await GetQueueTicketIdsAsync(body.QueueId, cancellationToken);
            foreach (var ticketId in ticketIds)
            {
                await CancelTicketInternalAsync(ticketId, ClientCancelReason.Queue_disabled, cancellationToken);
            }

            // Delete queue
            await _stateStoreFactory.GetStore<QueueModel>(StateStoreDefinitions.Matchmaking)
                .DeleteAsync(QUEUE_KEY_PREFIX + body.QueueId, cancellationToken);

            // Remove from queue list
            await RemoveFromQueueListAsync(body.QueueId, cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync("matchmaking.queue-deleted", new MatchmakingQueueDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                QueueId = queue.QueueId,
                GameId = queue.GameId,
                DisplayName = queue.DisplayName,
                Enabled = queue.Enabled,
                MinCount = queue.MinCount,
                MaxCount = queue.MaxCount,
                CreatedAt = queue.CreatedAt
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Queue {QueueId} deleted successfully", body.QueueId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete queue {QueueId}", body.QueueId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "DeleteQueue", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/queue/delete",
                details: new { QueueId = body.QueueId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Matchmaking Operations

    /// <summary>
    /// Joins a matchmaking queue.
    /// </summary>
    public async Task<(StatusCodes, JoinMatchmakingResponse?)> JoinMatchmakingAsync(
        JoinMatchmakingRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = body.WebSocketSessionId;
            var accountId = body.AccountId;
            var queueId = body.QueueId;

            _logger.LogInformation("Player {AccountId} joining queue {QueueId}", accountId, queueId);

            // Load queue
            var queue = await LoadQueueAsync(queueId, cancellationToken);
            if (queue == null)
            {
                _logger.LogWarning("Queue {QueueId} not found", queueId);
                return (StatusCodes.NotFound, null);
            }

            if (!queue.Enabled)
            {
                _logger.LogWarning("Queue {QueueId} is disabled", queueId);
                return (StatusCodes.Conflict, null);
            }

            // Check concurrent ticket limits
            var playerTickets = await GetPlayerTicketsAsync(accountId, cancellationToken);
            if (playerTickets.Count >= _configuration.MaxConcurrentTicketsPerPlayer)
            {
                _logger.LogWarning("Player {AccountId} has reached max concurrent tickets ({Max})",
                    accountId, _configuration.MaxConcurrentTicketsPerPlayer);
                return (StatusCodes.Conflict, null);
            }

            // Check exclusive group conflicts
            if (!string.IsNullOrEmpty(queue.ExclusiveGroup))
            {
                foreach (var existingTicketId in playerTickets)
                {
                    var existingTicket = await LoadTicketAsync(existingTicketId, cancellationToken);
                    if (existingTicket != null)
                    {
                        var existingQueue = await LoadQueueAsync(existingTicket.QueueId, cancellationToken);
                        if (existingQueue?.ExclusiveGroup == queue.ExclusiveGroup)
                        {
                            _logger.LogWarning("Player {AccountId} already in queue with exclusive group {Group}",
                                accountId, queue.ExclusiveGroup);
                            return (StatusCodes.Conflict, null);
                        }
                    }
                }
            }

            // Check if already in this queue
            foreach (var existingTicketId in playerTickets)
            {
                var existingTicket = await LoadTicketAsync(existingTicketId, cancellationToken);
                if (existingTicket?.QueueId == queueId)
                {
                    _logger.LogWarning("Player {AccountId} already in queue {QueueId}", accountId, queueId);
                    return (StatusCodes.Conflict, null);
                }
            }

            // Create ticket
            var ticketId = Guid.NewGuid();
            var ticket = new TicketModel
            {
                TicketId = ticketId,
                QueueId = queueId,
                AccountId = accountId,
                WebSocketSessionId = sessionId,
                PartyId = body.PartyId,
                PartyMembers = body.PartyMembers?.Select(m => new PartyMemberModel
                {
                    AccountId = m.AccountId,
                    WebSocketSessionId = m.WebSocketSessionId,
                    SkillRating = m.SkillRating
                }).ToList(),
                StringProperties = body.StringProperties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, string>(),
                NumericProperties = body.NumericProperties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, double>(),
                Query = body.Query,
                TournamentId = body.TournamentId,
                Status = TicketStatus.Searching,
                CreatedAt = DateTimeOffset.UtcNow,
                IntervalsElapsed = 0
            };

            // Calculate effective skill rating (for skill-based queues)
            if (queue.UseSkillRating)
            {
                // Use party skill aggregation when party members are present
                if (ticket.PartyMembers?.Count > 0)
                {
                    var ratings = ticket.PartyMembers
                        .Where(m => m.SkillRating.HasValue)
                        .Select(m => m.SkillRating.GetValueOrDefault())
                        .ToList();

                    if (ratings.Count > 0)
                    {
                        ticket.SkillRating = queue.PartySkillAggregation switch
                        {
                            PartySkillAggregation.Highest => ratings.Max(),
                            PartySkillAggregation.Average => ratings.Average(),
                            PartySkillAggregation.Weighted when queue.PartySkillWeights?.Count >= ratings.Count =>
                                ratings.Zip(queue.PartySkillWeights, (r, w) => r * w).Sum() / queue.PartySkillWeights.Sum(),
                            _ => ratings.Average()
                        };
                    }
                }
            }

            // Save ticket
            await SaveTicketAsync(ticket, cancellationToken);

            // Add to player tickets
            await AddToPlayerTicketsAsync(accountId, ticketId, cancellationToken);

            // Add to queue tickets
            await AddToQueueTicketsAsync(queueId, ticketId, cancellationToken);

            // Update session state for shortcuts
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = body.WebSocketSessionId,
                    ServiceId = "matchmaking",
                    NewState = "in_queue"
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set matchmaking state for session {SessionId}", sessionId);
                // Rollback
                await DeleteTicketAsync(ticketId, cancellationToken);
                await RemoveFromPlayerTicketsAsync(accountId, ticketId, cancellationToken);
                await RemoveFromQueueTicketsAsync(queueId, ticketId, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }

            // Publish shortcuts for leave/status endpoints
            await PublishMatchmakingShortcutsAsync(sessionId, accountId, ticketId, cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync(TICKET_CREATED_TOPIC, new MatchmakingTicketCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TicketId = ticketId,
                QueueId = queueId,
                AccountId = accountId,
                PartyId = ticket.PartyId,
                PartySize = ticket.PartyMembers?.Count,
                SkillRating = ticket.SkillRating
            }, cancellationToken: cancellationToken);

            // Send client event
            await _clientEventPublisher.PublishToSessionAsync(sessionId.ToString(), new QueueJoinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TicketId = ticketId,
                QueueId = queueId,
                QueueDisplayName = queue.DisplayName,
                EstimatedWaitSeconds = queue.AverageWaitSeconds > 0 ? (int?)queue.AverageWaitSeconds : null
            }, cancellationToken);

            // Try immediate match if enabled
            if (_configuration.ImmediateMatchCheckEnabled)
            {
                await TryImmediateMatchAsync(ticket, queue, cancellationToken);
            }

            _logger.LogInformation("Player {AccountId} joined queue {QueueId} with ticket {TicketId}",
                accountId, queueId, ticketId);

            return (StatusCodes.OK, new JoinMatchmakingResponse
            {
                TicketId = ticketId,
                QueueId = queueId,
                EstimatedWaitSeconds = queue.AverageWaitSeconds > 0 ? (int?)queue.AverageWaitSeconds : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join queue {QueueId}", body.QueueId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "JoinMatchmaking", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/join",
                details: new { QueueId = body.QueueId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Leaves a matchmaking queue.
    /// </summary>
    public async Task<StatusCodes> LeaveMatchmakingAsync(
        LeaveMatchmakingRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var ticketId = body.TicketId;
            var accountId = body.AccountId;

            _logger.LogInformation("Player {AccountId} leaving matchmaking with ticket {TicketId}",
                accountId, ticketId);

            var ticket = await LoadTicketAsync(ticketId, cancellationToken);
            if (ticket == null)
            {
                return StatusCodes.NotFound;
            }

            if (ticket.AccountId != accountId)
            {
                _logger.LogWarning("Ticket {TicketId} does not belong to account {AccountId}",
                    ticketId, accountId);
                return StatusCodes.Forbidden;
            }

            await CancelTicketInternalAsync(ticketId, ClientCancelReason.Cancelled_by_user, cancellationToken);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave matchmaking with ticket {TicketId}", body.TicketId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "LeaveMatchmaking", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/leave",
                details: new { TicketId = body.TicketId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Gets matchmaking status for a ticket.
    /// </summary>
    public async Task<(StatusCodes, MatchmakingStatusResponse?)> GetMatchmakingStatusAsync(
        GetMatchmakingStatusRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var ticket = await LoadTicketAsync(body.TicketId, cancellationToken);
            if (ticket == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (ticket.AccountId != body.AccountId)
            {
                return (StatusCodes.Forbidden, null);
            }

            var queue = await LoadQueueAsync(ticket.QueueId, cancellationToken);
            var currentSkillRange = _algorithm.GetCurrentSkillRange(queue, ticket.IntervalsElapsed);

            return (StatusCodes.OK, new MatchmakingStatusResponse
            {
                TicketId = ticket.TicketId,
                QueueId = ticket.QueueId,
                Status = ticket.Status,
                IntervalsElapsed = ticket.IntervalsElapsed,
                CurrentSkillRange = currentSkillRange,
                EstimatedWaitSeconds = queue?.AverageWaitSeconds > 0 ? (int?)queue.AverageWaitSeconds : null,
                CreatedAt = ticket.CreatedAt,
                MatchId = ticket.MatchId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get matchmaking status for ticket {TicketId}", body.TicketId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "GetMatchmakingStatus", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/status",
                details: new { TicketId = body.TicketId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Accepts a formed match.
    /// </summary>
    public async Task<(StatusCodes, AcceptMatchResponse?)> AcceptMatchAsync(
        AcceptMatchRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var matchId = body.MatchId;
            var accountId = body.AccountId;
            var sessionId = body.WebSocketSessionId;

            _logger.LogInformation("Player {AccountId} accepting match {MatchId}", accountId, matchId);

            // Acquire lock on match (multiple players accept concurrently)
            await using var matchLock = await _lockProvider.LockAsync(
                "matchmaking-match", matchId.ToString(), Guid.NewGuid().ToString(), 30, cancellationToken);
            if (!matchLock.Success)
            {
                _logger.LogWarning("Could not acquire match lock for {MatchId}", matchId);
                return (StatusCodes.Conflict, null);
            }

            var match = await LoadMatchAsync(matchId, cancellationToken);
            if (match == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check if player is in this match
            var playerTicket = match.MatchedTickets.FirstOrDefault(t => t.AccountId == accountId);
            if (playerTicket == null)
            {
                _logger.LogWarning("Player {AccountId} not in match {MatchId}", accountId, matchId);
                return (StatusCodes.Forbidden, null);
            }

            // Check deadline
            if (DateTimeOffset.UtcNow > match.AcceptDeadline)
            {
                _logger.LogWarning("Match {MatchId} acceptance deadline passed", matchId);
                return (StatusCodes.Conflict, null);
            }

            // Mark as accepted
            if (!match.AcceptedPlayers.Contains(accountId))
            {
                match.AcceptedPlayers.Add(accountId);
                await SaveMatchAsync(match, cancellationToken);
            }

            // Notify all players of acceptance progress
            foreach (var ticket in match.MatchedTickets)
            {
                await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchPlayerAcceptedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    MatchId = matchId,
                    AcceptedCount = match.AcceptedPlayers.Count,
                    TotalCount = match.MatchedTickets.Count,
                    AllAccepted = match.AcceptedPlayers.Count == match.MatchedTickets.Count
                }, cancellationToken);
            }

            // Check if all players accepted
            if (match.AcceptedPlayers.Count == match.MatchedTickets.Count)
            {
                match.Status = MatchStatus.Accepted;
                await SaveMatchAsync(match, cancellationToken);
                await FinalizeMatchAsync(match, cancellationToken);

                return (StatusCodes.OK, new AcceptMatchResponse
                {
                    MatchId = matchId,
                    AllAccepted = true,
                    AcceptedCount = match.AcceptedPlayers.Count,
                    TotalCount = match.MatchedTickets.Count,
                    GameSessionId = match.GameSessionId
                });
            }

            return (StatusCodes.OK, new AcceptMatchResponse
            {
                MatchId = matchId,
                AllAccepted = false,
                AcceptedCount = match.AcceptedPlayers.Count,
                TotalCount = match.MatchedTickets.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept match {MatchId}", body.MatchId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "AcceptMatch", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/accept",
                details: new { MatchId = body.MatchId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Declines a formed match.
    /// </summary>
    public async Task<StatusCodes> DeclineMatchAsync(
        DeclineMatchRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var matchId = body.MatchId;
            var accountId = body.AccountId;

            _logger.LogInformation("Player {AccountId} declining match {MatchId}", accountId, matchId);

            var match = await LoadMatchAsync(matchId, cancellationToken);
            if (match == null)
            {
                return StatusCodes.NotFound;
            }

            // Check if player is in this match
            if (!match.MatchedTickets.Any(t => t.AccountId == accountId))
            {
                return StatusCodes.Forbidden;
            }

            // Cancel the match
            await CancelMatchAsync(match, accountId, cancellationToken);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decline match {MatchId}", body.MatchId);
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "DeclineMatch", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/decline",
                details: new { MatchId = body.MatchId }, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Gets matchmaking statistics.
    /// </summary>
    public async Task<(StatusCodes, MatchmakingStatsResponse?)> GetMatchmakingStatsAsync(
        GetMatchmakingStatsRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var queueIds = await GetQueueIdsAsync(cancellationToken);
            var stats = new List<QueueStats>();

            foreach (var queueId in queueIds)
            {
                // Filter by specific queue if requested
                if (!string.IsNullOrEmpty(body.QueueId) && queueId != body.QueueId)
                    continue;

                var queue = await LoadQueueAsync(queueId, cancellationToken);
                if (queue == null) continue;

                // Filter by game ID if requested
                if (!string.IsNullOrEmpty(body.GameId) && queue.GameId != body.GameId)
                    continue;

                var ticketCount = await GetQueueTicketCountAsync(queueId, cancellationToken);

                stats.Add(new QueueStats
                {
                    QueueId = queueId,
                    CurrentTickets = ticketCount,
                    MatchesFormedLastHour = queue.MatchesFormedLastHour,
                    AverageWaitSeconds = queue.AverageWaitSeconds,
                    MedianWaitSeconds = queue.MedianWaitSeconds,
                    TimeoutRatePercent = queue.TimeoutRatePercent,
                    CancelRatePercent = queue.CancelRatePercent
                });
            }

            return (StatusCodes.OK, new MatchmakingStatsResponse
            {
                Timestamp = DateTimeOffset.UtcNow,
                QueueStats = stats
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get matchmaking stats");
            await _messageBus.TryPublishErrorAsync(
                "matchmaking", "GetMatchmakingStats", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/matchmaking/stats",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Internal Match Processing

    /// <summary>
    /// Tries to form an immediate match for a newly created ticket.
    /// </summary>
    private async Task TryImmediateMatchAsync(TicketModel ticket, QueueModel queue, CancellationToken cancellationToken)
    {
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
            ticket.Status = TicketStatus.Match_found;
            ticket.MatchId = matchId;
            await SaveTicketAsync(ticket, cancellationToken);

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
            await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchFoundEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MatchId = matchId,
                QueueId = queue.QueueId,
                QueueDisplayName = queue.DisplayName,
                PlayerCount = tickets.Count,
                AcceptDeadline = acceptDeadline,
                AcceptTimeoutSeconds = queue.MatchAcceptTimeoutSeconds,
                AverageSkillRating = match.AverageSkillRating
            }, cancellationToken);

            // Publish accept/decline shortcuts
            await PublishMatchShortcutsAsync(ticket.WebSocketSessionId, ticket.AccountId, matchId, cancellationToken);
        }

        // Publish event
        await _messageBus.TryPublishAsync(MATCH_FORMED_TOPIC, new MatchmakingMatchFormedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            MatchId = matchId,
            QueueId = queue.QueueId,
            Tickets = match.MatchedTickets.Select(t => new MatchedTicketInfo
            {
                TicketId = t.TicketId,
                AccountId = t.AccountId,
                PartyId = t.PartyId,
                WebSocketSessionId = t.WebSocketSessionId,
                SkillRating = t.SkillRating,
                WaitTimeSeconds = t.WaitTimeSeconds
            }).ToList(),
            PlayerCount = tickets.Count,
            AverageSkillRating = match.AverageSkillRating,
            SkillRatingSpread = match.SkillRatingSpread,
            AcceptDeadline = acceptDeadline
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Finalizes a match when all players have accepted.
    /// </summary>
    private async Task FinalizeMatchAsync(MatchModel match, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Finalizing match {MatchId}", match.MatchId);

        // Create game session via RPC to lib-game-session
        var expectedPlayers = match.MatchedTickets.Select(t => t.AccountId).ToList();
        var queue = await LoadQueueAsync(match.QueueId, cancellationToken);

        try
        {
            // Map queue SessionGameType to game-session CreateGameSessionRequestGameType
            var gameType = queue?.SessionGameType switch
            {
                SessionGameType.Arcadia => CreateGameSessionRequestGameType.Arcadia,
                _ => CreateGameSessionRequestGameType.Generic
            };

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

                await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchConfirmedEvent
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
            await _messageBus.TryPublishAsync(MATCH_ACCEPTED_TOPIC, new MatchmakingMatchAcceptedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MatchId = match.MatchId,
                QueueId = match.QueueId,
                GameSessionId = sessionResponse.SessionId,
                PlayerCount = match.PlayerCount,
                AverageWaitTimeSeconds = match.MatchedTickets.Average(t => t.WaitTimeSeconds)
            }, cancellationToken: cancellationToken);

            // Update match status to completed
            match.Status = MatchStatus.Completed;
            await SaveMatchAsync(match, cancellationToken);

            _logger.LogInformation("Match {MatchId} finalized with game session {GameSessionId}",
                match.MatchId, sessionResponse.SessionId);
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
            await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchDeclinedEvent
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
                await CancelTicketInternalAsync(ticket.TicketId, ClientCancelReason.Match_declined, cancellationToken);
            }
            else if (_configuration.AutoRequeueOnDecline)
            {
                // Requeue other players
                playersToRequeue.Add(ticket);
            }
            else
            {
                // Cancel all tickets
                await CancelTicketInternalAsync(ticket.TicketId, ClientCancelReason.Match_declined, cancellationToken);
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
                catch { /* Ignore */ }
            }
        }

        // Delete match
        await DeleteMatchAsync(match.MatchId, cancellationToken);

        // Publish event
        await _messageBus.TryPublishAsync(MATCH_DECLINED_TOPIC, new MatchmakingMatchDeclinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            MatchId = match.MatchId,
            QueueId = match.QueueId,
            DeclinedBy = declinedBy ?? Guid.Empty,
            AffectedPlayers = match.MatchedTickets.Select(t => t.AccountId).ToList(),
            RequeuingPlayers = playersToRequeue.Select(t => t.AccountId).ToList()
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels a ticket internally with reason.
    /// </summary>
    private async Task CancelTicketInternalAsync(Guid ticketId, ClientCancelReason reason, CancellationToken cancellationToken)
    {
        var ticket = await LoadTicketAsync(ticketId, cancellationToken);
        if (ticket == null) return;

        var waitTime = (DateTimeOffset.UtcNow - ticket.CreatedAt).TotalSeconds;

        _logger.LogInformation("Cancelling ticket {TicketId} with reason {Reason}", ticketId, reason);

        // Send client event
        await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchmakingCancelledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TicketId = ticketId,
            QueueId = ticket.QueueId,
            Reason = reason,
            WaitTimeSeconds = waitTime,
            CanRequeue = reason != ClientCancelReason.Session_disconnected && reason != ClientCancelReason.Queue_disabled
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
        catch { /* Ignore */ }

        // Clean up ticket
        await CleanupTicketAsync(ticketId, ticket.AccountId, ticket.QueueId, cancellationToken);

        // Publish service event (cast enum - both enums have identical values)
        await _messageBus.TryPublishAsync(TICKET_CANCELLED_TOPIC, new MatchmakingTicketCancelledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TicketId = ticketId,
            QueueId = ticket.QueueId,
            AccountId = ticket.AccountId,
            PartyId = ticket.PartyId,
            Reason = (EventCancelReason)reason,
            WaitTimeSeconds = waitTime
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cleans up all ticket references.
    /// </summary>
    private async Task CleanupTicketAsync(Guid ticketId, Guid accountId, string queueId, CancellationToken cancellationToken)
    {
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
        return await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Matchmaking)
            .GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
    }

    private async Task AddToQueueListAsync(string queueId, CancellationToken cancellationToken)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", QUEUE_LIST_KEY, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue list lock");
            return;
        }

        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
        if (!list.Contains(queueId))
        {
            list.Add(queueId);
            await store.SaveAsync(QUEUE_LIST_KEY, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromQueueListAsync(string queueId, CancellationToken cancellationToken)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", QUEUE_LIST_KEY, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue list lock");
            return;
        }

        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(QUEUE_LIST_KEY, cancellationToken) ?? new List<string>();
        if (list.Remove(queueId))
        {
            await store.SaveAsync(QUEUE_LIST_KEY, list, cancellationToken: cancellationToken);
        }
    }

    private async Task<QueueModel?> LoadQueueAsync(string queueId, CancellationToken cancellationToken)
    {
        return await _stateStoreFactory.GetStore<QueueModel>(StateStoreDefinitions.Matchmaking)
            .GetAsync(QUEUE_KEY_PREFIX + queueId, cancellationToken);
    }

    private async Task SaveQueueAsync(QueueModel queue, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<QueueModel>(StateStoreDefinitions.Matchmaking)
            .SaveAsync(QUEUE_KEY_PREFIX + queue.QueueId, queue, cancellationToken: cancellationToken);
    }

    private async Task<TicketModel?> LoadTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        return await _stateStoreFactory.GetStore<TicketModel>(StateStoreDefinitions.Matchmaking)
            .GetAsync(TICKET_KEY_PREFIX + ticketId, cancellationToken);
    }

    private async Task SaveTicketAsync(TicketModel ticket, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<TicketModel>(StateStoreDefinitions.Matchmaking)
            .SaveAsync(TICKET_KEY_PREFIX + ticket.TicketId, ticket, cancellationToken: cancellationToken);
    }

    private async Task DeleteTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<TicketModel>(StateStoreDefinitions.Matchmaking)
            .DeleteAsync(TICKET_KEY_PREFIX + ticketId, cancellationToken);
    }

    private async Task<MatchModel?> LoadMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await _stateStoreFactory.GetStore<MatchModel>(StateStoreDefinitions.Matchmaking)
            .GetAsync(MATCH_KEY_PREFIX + matchId, cancellationToken);
    }

    private async Task SaveMatchAsync(MatchModel match, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<MatchModel>(StateStoreDefinitions.Matchmaking)
            .SaveAsync(MATCH_KEY_PREFIX + match.MatchId, match, cancellationToken: cancellationToken);
    }

    private async Task DeleteMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<MatchModel>(StateStoreDefinitions.Matchmaking)
            .DeleteAsync(MATCH_KEY_PREFIX + matchId, cancellationToken);
    }

    private async Task<List<Guid>> GetPlayerTicketsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking)
            .GetAsync(PLAYER_TICKETS_PREFIX + accountId, cancellationToken) ?? new List<Guid>();
    }

    private async Task AddToPlayerTicketsAsync(Guid accountId, Guid ticketId, CancellationToken cancellationToken)
    {
        var key = PLAYER_TICKETS_PREFIX + accountId;
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", key, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire player tickets lock for {AccountId}", accountId);
            return;
        }

        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (!list.Contains(ticketId))
        {
            list.Add(ticketId);
            await store.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromPlayerTicketsAsync(Guid accountId, Guid ticketId, CancellationToken cancellationToken)
    {
        var key = PLAYER_TICKETS_PREFIX + accountId;
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", key, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire player tickets lock for {AccountId}", accountId);
            return;
        }

        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (list.Remove(ticketId))
        {
            await store.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task<List<Guid>> GetQueueTicketIdsAsync(string queueId, CancellationToken cancellationToken)
    {
        return await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking)
            .GetAsync(QUEUE_TICKETS_PREFIX + queueId, cancellationToken) ?? new List<Guid>();
    }

    private async Task<int> GetQueueTicketCountAsync(string queueId, CancellationToken cancellationToken)
    {
        var ids = await GetQueueTicketIdsAsync(queueId, cancellationToken);
        return ids.Count;
    }

    private async Task AddToQueueTicketsAsync(string queueId, Guid ticketId, CancellationToken cancellationToken)
    {
        var key = QUEUE_TICKETS_PREFIX + queueId;
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", key, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue tickets lock for {QueueId}", queueId);
            return;
        }

        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (!list.Contains(ticketId))
        {
            list.Add(ticketId);
            await store.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromQueueTicketsAsync(string queueId, Guid ticketId, CancellationToken cancellationToken)
    {
        var key = QUEUE_TICKETS_PREFIX + queueId;
        await using var lockResponse = await _lockProvider.LockAsync(
            "matchmaking-index", key, Guid.NewGuid().ToString(), 15, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire queue tickets lock for {QueueId}", queueId);
            return;
        }

        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking);
        var list = await store.GetAsync(key, cancellationToken) ?? new List<Guid>();
        if (list.Remove(ticketId))
        {
            await store.SaveAsync(key, list, cancellationToken: cancellationToken);
        }
    }

    private async Task StorePendingMatchAsync(Guid accountId, Guid matchId, CancellationToken cancellationToken)
    {
        var wrapper = new PendingMatchWrapper { MatchId = matchId };
        var options = new StateOptions { Ttl = _configuration.PendingMatchRedisKeyTtlSeconds };
        await _stateStoreFactory.GetStore<PendingMatchWrapper>(StateStoreDefinitions.Matchmaking)
            .SaveAsync(PENDING_MATCH_PREFIX + accountId, wrapper, options, cancellationToken);
    }

    private async Task<Guid?> GetPendingMatchAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var wrapper = await _stateStoreFactory.GetStore<PendingMatchWrapper>(StateStoreDefinitions.Matchmaking)
            .GetAsync(PENDING_MATCH_PREFIX + accountId, cancellationToken);
        return wrapper?.MatchId;
    }

    private async Task ClearPendingMatchAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await _stateStoreFactory.GetStore<PendingMatchWrapper>(StateStoreDefinitions.Matchmaking)
            .DeleteAsync(PENDING_MATCH_PREFIX + accountId, cancellationToken);
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
                ClientCancelReason.Timeout,
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

            await _messageBus.TryPublishAsync("matchmaking.stats", statsEvent, cancellationToken);
            _logger.LogDebug("Published matchmaking stats: {QueueCount} queues, {TotalTickets} tickets",
                stats.Count, stats.Sum(s => s.ActiveTickets));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish matchmaking stats event");
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers service permissions with the Permission service.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Matchmaking service permissions...");
        await MatchmakingPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}

#region Internal Models

/// <summary>
/// Internal model for queue storage.
/// </summary>
internal class QueueModel
{
    public string QueueId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public SessionGameType SessionGameType { get; set; } = SessionGameType.Generic;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int MinCount { get; set; }
    public int MaxCount { get; set; }
    public int CountMultiple { get; set; } = 1;
    public int IntervalSeconds { get; set; } = 15;
    public int MaxIntervals { get; set; } = 6;
    public List<SkillExpansionStepModel>? SkillExpansion { get; set; }
    public PartySkillAggregation PartySkillAggregation { get; set; } = PartySkillAggregation.Average;
    public List<double>? PartySkillWeights { get; set; }
    public int? PartyMaxSize { get; set; }
    public bool AllowConcurrent { get; set; } = true;
    public string? ExclusiveGroup { get; set; }
    public bool UseSkillRating { get; set; } = true;
    public string? RatingCategory { get; set; }
    public bool StartWhenMinimumReached { get; set; }
    public bool RequiresRegistration { get; set; }
    public bool TournamentIdRequired { get; set; }
    public int MatchAcceptTimeoutSeconds { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Stats
    public double AverageWaitSeconds { get; set; }
    public double? MedianWaitSeconds { get; set; }
    public int MatchesFormedLastHour { get; set; }
    public double? TimeoutRatePercent { get; set; }
    public double? CancelRatePercent { get; set; }
}

internal class SkillExpansionStepModel
{
    public int Intervals { get; set; }
    public int? Range { get; set; }
}

/// <summary>
/// Internal model for ticket storage.
/// </summary>
internal class TicketModel
{
    public Guid TicketId { get; set; }
    public string QueueId { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public Guid? PartyId { get; set; }
    public List<PartyMemberModel>? PartyMembers { get; set; }
    public Dictionary<string, string> StringProperties { get; set; } = new();
    public Dictionary<string, double> NumericProperties { get; set; } = new();
    public string? Query { get; set; }
    public Guid? TournamentId { get; set; }
    public double? SkillRating { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Searching;
    public DateTimeOffset CreatedAt { get; set; }
    public int IntervalsElapsed { get; set; }
    public Guid? MatchId { get; set; }
}

internal class PartyMemberModel
{
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public double? SkillRating { get; set; }
}

/// <summary>
/// Internal model for match storage.
/// </summary>
internal class MatchModel
{
    public Guid MatchId { get; set; }
    public string QueueId { get; set; } = string.Empty;
    public List<MatchedTicketModel> MatchedTickets { get; set; } = new();
    public int PlayerCount { get; set; }
    public double? AverageSkillRating { get; set; }
    public double? SkillRatingSpread { get; set; }
    public DateTimeOffset AcceptDeadline { get; set; }
    public List<Guid> AcceptedPlayers { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? GameSessionId { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Pending;
}

/// <summary>
/// Match lifecycle status.
/// </summary>
internal enum MatchStatus
{
    Pending,
    Accepted,
    Cancelled,
    Completed
}

internal class MatchedTicketModel
{
    public Guid TicketId { get; set; }
    public Guid AccountId { get; set; }
    public Guid WebSocketSessionId { get; set; }
    public Guid? PartyId { get; set; }
    public double? SkillRating { get; set; }
    public double WaitTimeSeconds { get; set; }
}

/// <summary>
/// Wrapper for pending match storage (value types can't be stored directly).
/// </summary>
internal class PendingMatchWrapper
{
    public Guid MatchId { get; set; }
}

#endregion
