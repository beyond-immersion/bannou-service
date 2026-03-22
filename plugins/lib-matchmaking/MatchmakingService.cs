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
    private readonly ILogger<MatchmakingService> _logger;
    private readonly MatchmakingServiceConfiguration _configuration;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IGameSessionClient _gameSessionClient;
    private readonly IPermissionClient _permissionClient;
    private readonly IMatchmakingAlgorithm _algorithm;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>State store for queue definitions.</summary>
    private readonly IStateStore<QueueModel> _queueStore;

    /// <summary>State store for matchmaking tickets.</summary>
    private readonly IStateStore<TicketModel> _ticketStore;

    /// <summary>State store for formed matches.</summary>
    private readonly IStateStore<MatchModel> _matchStore;

    /// <summary>State store for the queue ID list.</summary>
    private readonly IStateStore<List<string>> _stringListStore;

    /// <summary>State store for player ticket and queue ticket ID lists.</summary>
    private readonly IStateStore<List<Guid>> _guidListStore;

    /// <summary>State store for pending match wrappers (reconnection support).</summary>
    private readonly IStateStore<PendingMatchWrapper> _pendingMatchStore;

    private const string QUEUE_KEY_PREFIX = "queue:";
    private const string QUEUE_LIST_KEY = "queue-list";
    private const string TICKET_KEY_PREFIX = "ticket:";
    private const string MATCH_KEY_PREFIX = "match:";
    private const string PLAYER_TICKETS_PREFIX = "player-tickets:";
    private const string PENDING_MATCH_PREFIX = "pending-match:";
    private const string QUEUE_TICKETS_PREFIX = "queue-tickets:";

    #region Key Building Helpers

    /// <summary>
    /// Builds the state store key for a matchmaking queue record.
    /// </summary>
    /// <param name="queueId">The queue ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildQueueKey(string queueId) => $"{QUEUE_KEY_PREFIX}{queueId}";

    /// <summary>
    /// Builds the state store key for a matchmaking ticket record.
    /// </summary>
    /// <param name="ticketId">The ticket ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildTicketKey(Guid ticketId) => $"{TICKET_KEY_PREFIX}{ticketId}";

    /// <summary>
    /// Builds the state store key for a match record.
    /// </summary>
    /// <param name="matchId">The match ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildMatchKey(Guid matchId) => $"{MATCH_KEY_PREFIX}{matchId}";

    /// <summary>
    /// Builds the state store key for a player's ticket index.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildPlayerTicketsKey(Guid accountId) => $"{PLAYER_TICKETS_PREFIX}{accountId}";

    /// <summary>
    /// Builds the state store key for a pending match wrapper.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildPendingMatchKey(Guid accountId) => $"{PENDING_MATCH_PREFIX}{accountId}";

    /// <summary>
    /// Builds the state store key for a queue's ticket index.
    /// </summary>
    /// <param name="queueId">The queue ID.</param>
    /// <returns>State store key.</returns>
    internal static string BuildQueueTicketsKey(string queueId) => $"{QUEUE_TICKETS_PREFIX}{queueId}";

    #endregion

    private readonly string _serverSalt;

    /// <summary>
    /// Creates a new MatchmakingService instance.
    /// </summary>
    public MatchmakingService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<MatchmakingService> logger,
        ILoggerFactory loggerFactory,
        MatchmakingServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IClientEventPublisher clientEventPublisher,
        IGameSessionClient gameSessionClient,
        IPermissionClient permissionClient,
        IDistributedLockProvider lockProvider,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _clientEventPublisher = clientEventPublisher;
        _gameSessionClient = gameSessionClient;
        _permissionClient = permissionClient;
        _lockProvider = lockProvider;
        _telemetryProvider = telemetryProvider;
        // Instantiate directly since internal types are used
        // Tests can access via InternalsVisibleTo
        _algorithm = new MatchmakingAlgorithm(loggerFactory.CreateLogger<MatchmakingAlgorithm>());

        // Constructor-cache all state stores per FOUNDATION TENETS
        _queueStore = stateStoreFactory.GetStore<QueueModel>(StateStoreDefinitions.Matchmaking);
        _ticketStore = stateStoreFactory.GetStore<TicketModel>(StateStoreDefinitions.Matchmaking);
        _matchStore = stateStoreFactory.GetStore<MatchModel>(StateStoreDefinitions.Matchmaking);
        _stringListStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Matchmaking);
        _guidListStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Matchmaking);
        _pendingMatchStore = stateStoreFactory.GetStore<PendingMatchWrapper>(StateStoreDefinitions.Matchmaking);

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
        _logger.LogDebug("Listing matchmaking queues - GameId: {GameId}, IncludeDisabled: {IncludeDisabled}",
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

    /// <summary>
    /// Gets details of a specific queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> GetQueueAsync(
        GetQueueRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting queue {QueueId}", body.QueueId);

        var queue = await LoadQueueAsync(body.QueueId, cancellationToken);
        if (queue == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapQueueModelToResponse(queue));
    }

    /// <summary>
    /// Creates a new matchmaking queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> CreateQueueAsync(
        CreateQueueRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating queue {QueueId} for game {GameId}", body.QueueId, body.GameId);

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
            SessionGameType = body.SessionGameType ?? "generic",
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
            PartySkillAggregation = body.PartySkillAggregation ?? PartySkillAggregation.Average,
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
        await _messageBus.PublishMatchmakingQueueCreatedAsync(new MatchmakingQueueCreatedEvent
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
        }, cancellationToken);

        _logger.LogInformation("Queue {QueueId} created successfully", queue.QueueId);
        return (StatusCodes.OK, MapQueueModelToResponse(queue));
    }

    /// <summary>
    /// Updates a matchmaking queue.
    /// </summary>
    public async Task<(StatusCodes, QueueResponse?)> UpdateQueueAsync(
        UpdateQueueRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating queue {QueueId}", body.QueueId);

        var queueKey = QUEUE_KEY_PREFIX + body.QueueId;
        var (queue, etag) = await _queueStore.GetWithETagAsync(queueKey, cancellationToken);
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

        // Save queue with ETag - etag is non-null when queue was successfully loaded above;
        // null-coalesce satisfies compiler nullable analysis (will never execute)
        var newEtag = await _queueStore.TrySaveAsync(queueKey, queue, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for queue {QueueId}", body.QueueId);
            return (StatusCodes.Conflict, null);
        }

        // Publish event
        await _messageBus.PublishMatchmakingQueueUpdatedAsync(new MatchmakingQueueUpdatedEvent
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
        }, cancellationToken);

        _logger.LogInformation("Queue {QueueId} updated successfully", queue.QueueId);
        return (StatusCodes.OK, MapQueueModelToResponse(queue));
    }

    /// <summary>
    /// Deletes a matchmaking queue.
    /// </summary>
    public async Task<StatusCodes> DeleteQueueAsync(
        DeleteQueueRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting queue {QueueId}", body.QueueId);

        var queue = await LoadQueueAsync(body.QueueId, cancellationToken);
        if (queue == null)
        {
            return StatusCodes.NotFound;
        }

        // Cancel all tickets in the queue
        var ticketIds = await GetQueueTicketIdsAsync(body.QueueId, cancellationToken);
        foreach (var ticketId in ticketIds)
        {
            await CancelTicketInternalAsync(ticketId, CancelReason.QueueDisabled, cancellationToken);
        }

        // Delete queue
        await _queueStore.DeleteAsync(QUEUE_KEY_PREFIX + body.QueueId, cancellationToken);

        // Remove from queue list
        await RemoveFromQueueListAsync(body.QueueId, cancellationToken);

        // Publish event
        await _messageBus.PublishMatchmakingQueueDeletedAsync(new MatchmakingQueueDeletedEvent
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
        }, cancellationToken);

        _logger.LogInformation("Queue {QueueId} deleted successfully", body.QueueId);
        return StatusCodes.OK;
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
        var sessionId = body.WebSocketSessionId;
        var queueId = body.QueueId;

        // Resolve account from session-to-account map per FOUNDATION TENETS (Account Identity Boundary)
        if (!_sessionAccountMap.TryGetValue(sessionId, out var accountId))
        {
            _logger.LogWarning("No account mapping for session {SessionId}", sessionId);
            return (StatusCodes.BadRequest, null);
        }

        _logger.LogDebug("Player {AccountId} joining queue {QueueId}", accountId, queueId);

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

        // Resolve party member accounts from session map per FOUNDATION TENETS (Account Identity Boundary)
        List<PartyMemberModel>? partyMembers = null;
        if (body.PartyMembers != null)
        {
            partyMembers = new List<PartyMemberModel>();
            foreach (var member in body.PartyMembers)
            {
                if (!_sessionAccountMap.TryGetValue(member.WebSocketSessionId, out _))
                {
                    _logger.LogWarning("No account mapping for party member session {SessionId}", member.WebSocketSessionId);
                    return (StatusCodes.BadRequest, null);
                }
                partyMembers.Add(new PartyMemberModel
                {
                    WebSocketSessionId = member.WebSocketSessionId,
                    SkillRating = member.SkillRating
                });
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
            PartyMembers = partyMembers,
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
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Permission service error setting matchmaking state for session {SessionId}: {Status}",
                sessionId, apiEx.StatusCode);
            // Rollback
            await DeleteTicketAsync(ticketId, cancellationToken);
            await RemoveFromPlayerTicketsAsync(accountId, ticketId, cancellationToken);
            await RemoveFromQueueTicketsAsync(queueId, ticketId, cancellationToken);
            return ((StatusCodes)apiEx.StatusCode, null);
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

        // Publish event - use webSocketSessionId per FOUNDATION TENETS (Account Identity Boundary)
        await _messageBus.PublishMatchmakingTicketCreatedAsync(new MatchmakingTicketCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TicketId = ticketId,
            QueueId = queueId,
            WebSocketSessionId = sessionId,
            PartyId = ticket.PartyId,
            PartySize = ticket.PartyMembers?.Count,
            SkillRating = ticket.SkillRating
        }, cancellationToken);

        // Send client event
        await _clientEventPublisher.PublishToSessionAsync(sessionId.ToString(), new QueueJoinedClientEvent
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
            EstimatedWaitSeconds = queue.AverageWaitSeconds > 0 ? (int?)queue.AverageWaitSeconds : null
        });
    }

    /// <summary>
    /// Leaves a matchmaking queue.
    /// </summary>
    public async Task<StatusCodes> LeaveMatchmakingAsync(
        LeaveMatchmakingRequest body,
        CancellationToken cancellationToken)
    {
        var ticketId = body.TicketId;
        var accountId = body.AccountId;

        _logger.LogDebug("Player {AccountId} leaving matchmaking with ticket {TicketId}",
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

        await CancelTicketInternalAsync(ticketId, CancelReason.CancelledByUser, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Gets matchmaking status for a ticket.
    /// </summary>
    public async Task<(StatusCodes, MatchmakingStatusResponse?)> GetMatchmakingStatusAsync(
        GetMatchmakingStatusRequest body,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Accepts a formed match.
    /// </summary>
    public async Task<(StatusCodes, AcceptMatchResponse?)> AcceptMatchAsync(
        AcceptMatchRequest body,
        CancellationToken cancellationToken)
    {
        var matchId = body.MatchId;
        var accountId = body.AccountId;
        var sessionId = body.WebSocketSessionId;

        _logger.LogDebug("Player {AccountId} accepting match {MatchId}", accountId, matchId);

        // Acquire lock on match (multiple players accept concurrently)
        await using var matchLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.MatchmakingLock, matchId.ToString(), Guid.NewGuid().ToString(), _configuration.MatchLockTimeoutSeconds, cancellationToken);
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
            await _clientEventPublisher.PublishToSessionAsync(ticket.WebSocketSessionId.ToString(), new MatchPlayerAcceptedClientEvent
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
                AllAccepted = true,
                AcceptedCount = match.AcceptedPlayers.Count,
                TotalCount = match.MatchedTickets.Count,
                GameSessionId = match.GameSessionId
            });
        }

        return (StatusCodes.OK, new AcceptMatchResponse
        {
            AllAccepted = false,
            AcceptedCount = match.AcceptedPlayers.Count,
            TotalCount = match.MatchedTickets.Count
        });
    }

    /// <summary>
    /// Declines a formed match.
    /// </summary>
    public async Task<StatusCodes> DeclineMatchAsync(
        DeclineMatchRequest body,
        CancellationToken cancellationToken)
    {
        var matchId = body.MatchId;
        var accountId = body.AccountId;

        _logger.LogDebug("Player {AccountId} declining match {MatchId}", accountId, matchId);

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

    /// <summary>
    /// Gets matchmaking statistics.
    /// </summary>
    public async Task<(StatusCodes, MatchmakingStatsResponse?)> GetMatchmakingStatsAsync(
        GetMatchmakingStatsRequest body,
        CancellationToken cancellationToken)
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

    #endregion

    #region Permission Registration

    #endregion
}
