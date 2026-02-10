using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-leaderboard.tests")]

namespace BeyondImmersion.BannouService.Leaderboard;

/// <summary>
/// Implementation of the Leaderboard service.
/// Provides real-time rankings using Redis Sorted Sets with definition storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>LeaderboardService.cs (this file) - Business logic</item>
///   <item>LeaderboardServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/LeaderboardPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("leaderboard", typeof(ILeaderboardService), lifetime: ServiceLifetime.Scoped)]
public partial class LeaderboardService : ILeaderboardService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<LeaderboardService> _logger;
    private readonly LeaderboardServiceConfiguration _configuration;

    // State store key prefixes
    private const string DEFINITION_INDEX_PREFIX = "leaderboard-definitions";
    private const string SEASON_INDEX_PREFIX = "leaderboard-seasons";

    /// <summary>
    /// Initializes a new instance of the LeaderboardService.
    /// </summary>
    public LeaderboardService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<LeaderboardService> logger,
        LeaderboardServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Generates the key for a leaderboard definition.
    /// Format: gameServiceId:leaderboardId
    /// </summary>
    private static string GetDefinitionKey(Guid gameServiceId, string leaderboardId)
        => $"{gameServiceId}:{leaderboardId}";

    /// <summary>
    /// Generates the key for the leaderboard definition index.
    /// Format: leaderboard-definitions:gameServiceId
    /// </summary>
    private static string GetDefinitionIndexKey(Guid gameServiceId)
        => $"{DEFINITION_INDEX_PREFIX}:{gameServiceId}";

    /// <summary>
    /// Generates the key for the leaderboard season index.
    /// Format: leaderboard-seasons:gameServiceId:leaderboardId
    /// </summary>
    private static string GetSeasonIndexKey(Guid gameServiceId, string leaderboardId)
        => $"{SEASON_INDEX_PREFIX}:{gameServiceId}:{leaderboardId}";

    /// <summary>
    /// Generates the key for a leaderboard ranking sorted set.
    /// Format: lb:gameServiceId:leaderboardId[:seasonN]
    /// </summary>
    private static string GetRankingKey(Guid gameServiceId, string leaderboardId, int? season = null)
        => season.HasValue
            ? $"lb:{gameServiceId}:{leaderboardId}:season{season}"
            : $"lb:{gameServiceId}:{leaderboardId}";

    /// <summary>
    /// Generates the member key for a polymorphic entity.
    /// Format: entityType:entityId
    /// </summary>
    private static string GetMemberKey(EntityType entityType, Guid entityId)
        => $"{entityType}:{entityId}";

    /// <summary>
    /// Parses a member key back to entity type and ID.
    /// </summary>
    private static (EntityType entityType, Guid entityId) ParseMemberKey(string member)
    {
        var parts = member.Split(':', 2);
        var entityType = Enum.Parse<EntityType>(parts[0], ignoreCase: true);
        var entityId = Guid.Parse(parts[1]);
        return (entityType, entityId);
    }

    /// <summary>
    /// Implementation of CreateLeaderboardDefinition operation.
    /// Creates a new leaderboard definition.
    /// </summary>
    public async Task<(StatusCodes, LeaderboardDefinitionResponse?)> CreateLeaderboardDefinitionAsync(CreateLeaderboardDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating leaderboard {LeaderboardId} for game service {GameServiceId}",
            body.LeaderboardId, body.GameServiceId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var key = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);

            // Check if already exists
            var existing = await definitionStore.GetAsync(key, cancellationToken);
            if (existing != null)
            {
                _logger.LogWarning("Leaderboard {LeaderboardId} already exists", body.LeaderboardId);
                return (StatusCodes.Conflict, null);
            }

            var now = DateTimeOffset.UtcNow;
            var definition = new LeaderboardDefinitionData
            {
                GameServiceId = body.GameServiceId,
                LeaderboardId = body.LeaderboardId,
                DisplayName = body.DisplayName,
                Description = body.Description,
                EntityTypes = body.EntityTypes?.ToList() ?? new List<EntityType> { EntityType.Account },
                SortOrder = body.SortOrder,
                UpdateMode = body.UpdateMode,
                IsSeasonal = body.IsSeasonal,
                IsPublic = body.IsPublic,
                CurrentSeason = body.IsSeasonal ? 1 : null,
                CreatedAt = now,
                Metadata = body.Metadata
            };

            await definitionStore.SaveAsync(key, definition, options: null, cancellationToken);
            await definitionStore.AddToSetAsync(
                GetDefinitionIndexKey(body.GameServiceId),
                body.LeaderboardId,
                cancellationToken: cancellationToken);

            if (definition.IsSeasonal && definition.CurrentSeason.HasValue)
            {
                await definitionStore.AddToSetAsync(
                    GetSeasonIndexKey(body.GameServiceId, body.LeaderboardId),
                    definition.CurrentSeason.Value,
                    cancellationToken: cancellationToken);
            }

            return (StatusCodes.OK, MapToResponse(definition, 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating leaderboard definition");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "CreateLeaderboardDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/definition/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetLeaderboardDefinition operation.
    /// Retrieves a leaderboard definition.
    /// </summary>
    public async Task<(StatusCodes, LeaderboardDefinitionResponse?)> GetLeaderboardDefinitionAsync(GetLeaderboardDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting leaderboard {LeaderboardId}", body.LeaderboardId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var key = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);

            var definition = await definitionStore.GetAsync(key, cancellationToken);
            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get entry count from sorted set
            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
            var entryCount = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            return (StatusCodes.OK, MapToResponse(definition, entryCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting leaderboard definition");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "GetLeaderboardDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/definition/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ListLeaderboardDefinitions operation.
    /// Lists all leaderboards for a game service.
    /// </summary>
    public async Task<(StatusCodes, ListLeaderboardDefinitionsResponse?)> ListLeaderboardDefinitionsAsync(ListLeaderboardDefinitionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing leaderboards for game service {GameServiceId}", body.GameServiceId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var indexKey = GetDefinitionIndexKey(body.GameServiceId);
            var leaderboardIds = await definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

            if (leaderboardIds.Count == 0)
            {
                return (StatusCodes.OK, new ListLeaderboardDefinitionsResponse
                {
                    Leaderboards = new List<LeaderboardDefinitionResponse>()
                });
            }

            if (body.IncludeArchived)
            {
                _logger.LogWarning("IncludeArchived not supported for game service {GameServiceId}", body.GameServiceId);
                return (StatusCodes.NotImplemented, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var leaderboards = new List<LeaderboardDefinitionResponse>();

            foreach (var leaderboardId in leaderboardIds)
            {
                var defKey = GetDefinitionKey(body.GameServiceId, leaderboardId);
                var definition = await definitionStore.GetAsync(defKey, cancellationToken);
                if (definition == null)
                {
                    continue;
                }

                var rankingKey = GetRankingKey(body.GameServiceId, leaderboardId, definition.CurrentSeason);
                var entryCount = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

                leaderboards.Add(MapToResponse(definition, entryCount));
            }

            var ordered = leaderboards
                .OrderBy(l => l.LeaderboardId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (StatusCodes.OK, new ListLeaderboardDefinitionsResponse
            {
                Leaderboards = ordered
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing leaderboard definitions");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "ListLeaderboardDefinitions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/definition/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of UpdateLeaderboardDefinition operation.
    /// Updates a leaderboard definition.
    /// </summary>
    public async Task<(StatusCodes, LeaderboardDefinitionResponse?)> UpdateLeaderboardDefinitionAsync(UpdateLeaderboardDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating leaderboard {LeaderboardId}", body.LeaderboardId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var key = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);

            var (definition, etag) = await definitionStore.GetWithETagAsync(key, cancellationToken);
            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Apply updates
            if (!string.IsNullOrEmpty(body.DisplayName))
            {
                definition.DisplayName = body.DisplayName;
            }
            if (body.Description != null)
            {
                definition.Description = body.Description;
            }
            if (body.IsPublic.HasValue)
            {
                definition.IsPublic = body.IsPublic.Value;
            }

            // etag is non-null at this point; coalesce satisfies compiler nullable analysis
            var newEtag = await definitionStore.TrySaveAsync(key, definition, etag ?? string.Empty, cancellationToken);
            if (newEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for leaderboard {LeaderboardId}", body.LeaderboardId);
                return (StatusCodes.Conflict, null);
            }

            // Get entry count
            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
            var entryCount = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            return (StatusCodes.OK, MapToResponse(definition, entryCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating leaderboard definition");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "UpdateLeaderboardDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/definition/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of DeleteLeaderboardDefinition operation.
    /// Deletes a leaderboard definition and its data.
    /// </summary>
    public async Task<StatusCodes> DeleteLeaderboardDefinitionAsync(DeleteLeaderboardDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting leaderboard {LeaderboardId}", body.LeaderboardId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var key = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);

            var definition = await definitionStore.GetAsync(key, cancellationToken);
            if (definition == null)
            {
                return StatusCodes.NotFound;
            }

            // Delete the definition
            await definitionStore.DeleteAsync(key, cancellationToken);

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);

            if (definition.IsSeasonal)
            {
                var seasonIndexKey = GetSeasonIndexKey(body.GameServiceId, body.LeaderboardId);
                var seasons = await definitionStore.GetSetAsync<int>(seasonIndexKey, cancellationToken);

                if (seasons.Count == 0 && definition.CurrentSeason.HasValue)
                {
                    seasons = new List<int> { definition.CurrentSeason.Value };
                }

                foreach (var season in seasons)
                {
                    var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, season);
                    await rankingStore.SortedSetDeleteAsync(rankingKey, cancellationToken);
                }

                await definitionStore.DeleteSetAsync(seasonIndexKey, cancellationToken);
            }
            else
            {
                var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
                await rankingStore.SortedSetDeleteAsync(rankingKey, cancellationToken);
            }

            await definitionStore.RemoveFromSetAsync(
                GetDefinitionIndexKey(body.GameServiceId),
                body.LeaderboardId,
                cancellationToken);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting leaderboard definition");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "DeleteLeaderboardDefinition",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/definition/delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Implementation of SubmitScore operation.
    /// Submits a score using Redis Sorted Sets.
    /// </summary>
    public async Task<(StatusCodes, SubmitScoreResponse?)> SubmitScoreAsync(SubmitScoreRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Submitting score for {EntityType}:{EntityId} to {LeaderboardId}",
            body.EntityType, body.EntityId, body.LeaderboardId);

        try
        {
            // Get leaderboard definition
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Validate entity type
            if (definition.EntityTypes != null && !definition.EntityTypes.Contains(body.EntityType))
            {
                return (StatusCodes.BadRequest, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
            var memberKey = GetMemberKey(body.EntityType, body.EntityId);

            // Get previous score and rank
            var previousScore = await rankingStore.SortedSetScoreAsync(rankingKey, memberKey, cancellationToken);
            var previousRank = previousScore.HasValue
                ? await rankingStore.SortedSetRankAsync(rankingKey, memberKey, definition.SortOrder == SortOrder.Descending, cancellationToken)
                : null;

            // Calculate final score based on update mode
            var finalScore = CalculateFinalScore(definition.UpdateMode, previousScore, body.Score);

            // Add/update score in sorted set
            await rankingStore.SortedSetAddAsync(rankingKey, memberKey, finalScore, options: null, cancellationToken);

            // Get new rank
            var newRank = await rankingStore.SortedSetRankAsync(rankingKey, memberKey, definition.SortOrder == SortOrder.Descending, cancellationToken);

            // Calculate rank change (positive = improved)
            var currentRank = (newRank ?? 0) + 1; // Convert 0-based to 1-based
            var prevRank = previousRank.HasValue ? previousRank.Value + 1 : currentRank;
            var rankChange = definition.SortOrder == SortOrder.Descending
                ? prevRank - currentRank // Higher rank (lower number) is better
                : currentRank - prevRank; // Lower rank is better for ascending

            // Publish entry added event for new entities (first time on leaderboard)
            if (!previousScore.HasValue && newRank.HasValue)
            {
                var totalEntries = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);
                var entryAddedEvent = new LeaderboardEntryAddedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    GameServiceId = body.GameServiceId,
                    LeaderboardId = body.LeaderboardId,
                    EntityId = body.EntityId,
                    EntityType = body.EntityType,
                    Score = finalScore,
                    Rank = currentRank,
                    TotalEntries = totalEntries
                };
                await _messageBus.TryPublishAsync("leaderboard.entry.added", entryAddedEvent, cancellationToken: cancellationToken);
            }
            // Publish rank changed event if rank changed significantly (for existing entries)
            else if (previousRank.HasValue && newRank.HasValue && previousRank.Value != newRank.Value)
            {
                var rankEvent = new LeaderboardRankChangedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    GameServiceId = body.GameServiceId,
                    LeaderboardId = body.LeaderboardId,
                    EntityId = body.EntityId,
                    EntityType = body.EntityType,
                    PreviousRank = prevRank,
                    NewRank = currentRank,
                    RankChange = rankChange,
                    PreviousScore = previousScore ?? 0,
                    CurrentScore = finalScore
                };
                await _messageBus.TryPublishAsync("leaderboard.rank.changed", rankEvent, cancellationToken: cancellationToken);
            }

            return (StatusCodes.OK, new SubmitScoreResponse
            {
                Accepted = true,
                PreviousScore = previousScore,
                CurrentScore = finalScore,
                PreviousRank = previousRank.HasValue ? prevRank : null,
                CurrentRank = currentRank,
                RankChange = rankChange
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting score");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "SubmitScore",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/score/submit",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of SubmitScoreBatch operation.
    /// Submits multiple scores efficiently.
    /// </summary>
    public async Task<(StatusCodes, SubmitScoreBatchResponse?)> SubmitScoreBatchAsync(SubmitScoreBatchRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Submitting batch of {Count} scores to {LeaderboardId}",
            body.Scores.Count, body.LeaderboardId);

        try
        {
            if (body.Scores.Count == 0 || body.Scores.Count > _configuration.ScoreUpdateBatchSize)
            {
                _logger.LogWarning(
                    "Invalid score batch size {Count} for leaderboard {LeaderboardId}; max is {MaxBatchSize}",
                    body.Scores.Count, body.LeaderboardId, _configuration.ScoreUpdateBatchSize);
                return (StatusCodes.BadRequest, null);
            }

            // Get leaderboard definition
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);

            var accepted = 0;
            var rejected = 0;

            // Prepare batch entries
            var entries = new List<(string member, double score)>();
            foreach (var entry in body.Scores)
            {
                // Validate entity type
                if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entry.EntityType))
                {
                    rejected++;
                    continue;
                }

                var memberKey = GetMemberKey(entry.EntityType, entry.EntityId);

                // For batch, we use replace mode only (simpler)
                entries.Add((memberKey, entry.Score));
                accepted++;
            }

            // Batch add to sorted set
            if (entries.Count > 0)
            {
                await rankingStore.SortedSetAddBatchAsync(rankingKey, entries, options: null, cancellationToken);
            }

            return (StatusCodes.OK, new SubmitScoreBatchResponse
            {
                Accepted = accepted,
                Rejected = rejected
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting batch scores");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "SubmitScoreBatch",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/score/submit-batch",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetEntityRank operation.
    /// Gets an entity's rank on a leaderboard.
    /// </summary>
    public async Task<(StatusCodes, EntityRankResponse?)> GetEntityRankAsync(GetEntityRankRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting rank for {EntityType}:{EntityId} on {LeaderboardId}",
            body.EntityType, body.EntityId, body.LeaderboardId);

        try
        {
            // Get leaderboard definition
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
            var memberKey = GetMemberKey(body.EntityType, body.EntityId);

            // Get score
            var score = await rankingStore.SortedSetScoreAsync(rankingKey, memberKey, cancellationToken);
            if (!score.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get rank (0-based)
            var rank = await rankingStore.SortedSetRankAsync(rankingKey, memberKey, definition.SortOrder == SortOrder.Descending, cancellationToken);
            if (!rank.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get total entries
            var totalEntries = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            // Calculate percentile
            var percentile = totalEntries > 0
                ? (1.0 - (double)rank.Value / totalEntries) * 100.0
                : 100.0;

            return (StatusCodes.OK, new EntityRankResponse
            {
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                Score = score.Value,
                Rank = rank.Value + 1, // Convert to 1-based
                TotalEntries = totalEntries,
                Percentile = Math.Round(percentile, 2)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity rank");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "GetEntityRank",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/rank/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetTopRanks operation.
    /// Gets the top entries on a leaderboard.
    /// </summary>
    public async Task<(StatusCodes, LeaderboardEntriesResponse?)> GetTopRanksAsync(GetTopRanksRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting top {Count} for {LeaderboardId}", body.Count, body.LeaderboardId);

        try
        {
            if (body.Count <= 0 || body.Count > _configuration.MaxEntriesPerQuery)
            {
                _logger.LogWarning("Invalid count {Count} for leaderboard {LeaderboardId}; max is {MaxEntries}",
                    body.Count, body.LeaderboardId, _configuration.MaxEntriesPerQuery);
                return (StatusCodes.BadRequest, null);
            }

            if (body.Offset < 0)
            {
                _logger.LogWarning("Invalid offset {Offset} for leaderboard {LeaderboardId}", body.Offset, body.LeaderboardId);
                return (StatusCodes.BadRequest, null);
            }

            // Get leaderboard definition
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);

            // Get range from sorted set
            var start = body.Offset;
            var stop = body.Offset + body.Count - 1;
            var entries = await rankingStore.SortedSetRangeByRankAsync(
                rankingKey, start, stop, definition.SortOrder == SortOrder.Descending, cancellationToken);

            // Get total entries
            var totalEntries = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            // Convert to response entries
            var responseEntries = entries.Select((entry, index) =>
            {
                var (entityType, entityId) = ParseMemberKey(entry.member);
                return new LeaderboardEntry
                {
                    EntityId = entityId,
                    EntityType = entityType,
                    Score = entry.score,
                    Rank = body.Offset + index + 1 // 1-based rank
                };
            }).ToList();

            return (StatusCodes.OK, new LeaderboardEntriesResponse
            {
                LeaderboardId = body.LeaderboardId,
                Entries = responseEntries,
                TotalEntries = totalEntries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top ranks");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "GetTopRanks",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/rank/top",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetRanksAround operation.
    /// Gets entries around a specific entity.
    /// </summary>
    public async Task<(StatusCodes, LeaderboardEntriesResponse?)> GetRanksAroundAsync(GetRanksAroundRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting ranks around {EntityType}:{EntityId} on {LeaderboardId}",
            body.EntityType, body.EntityId, body.LeaderboardId);

        try
        {
            if (body.CountBefore < 0 || body.CountAfter < 0)
            {
                _logger.LogWarning(
                    "Invalid surrounding counts (before:{CountBefore}, after:{CountAfter}) for leaderboard {LeaderboardId}",
                    body.CountBefore, body.CountAfter, body.LeaderboardId);
                return (StatusCodes.BadRequest, null);
            }

            var totalRequested = body.CountBefore + body.CountAfter + 1;
            if (totalRequested > _configuration.MaxEntriesPerQuery)
            {
                _logger.LogWarning(
                    "Requested {TotalRequested} entries around entity exceeds max {MaxEntries} for leaderboard {LeaderboardId}",
                    totalRequested, _configuration.MaxEntriesPerQuery, body.LeaderboardId);
                return (StatusCodes.BadRequest, null);
            }

            // Get leaderboard definition
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, definition.CurrentSeason);
            var memberKey = GetMemberKey(body.EntityType, body.EntityId);

            // Get entity's rank
            var entityRank = await rankingStore.SortedSetRankAsync(rankingKey, memberKey, definition.SortOrder == SortOrder.Descending, cancellationToken);
            if (!entityRank.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            // Calculate range around the entity
            var start = Math.Max(0, entityRank.Value - body.CountBefore);
            var stop = entityRank.Value + body.CountAfter;

            var entries = await rankingStore.SortedSetRangeByRankAsync(
                rankingKey, start, stop, definition.SortOrder == SortOrder.Descending, cancellationToken);

            // Get total entries
            var totalEntries = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            // Convert to response entries
            var responseEntries = entries.Select((entry, index) =>
            {
                var (entryEntityType, entryEntityId) = ParseMemberKey(entry.member);
                return new LeaderboardEntry
                {
                    EntityId = entryEntityId,
                    EntityType = entryEntityType,
                    Score = entry.score,
                    Rank = start + index + 1 // 1-based rank
                };
            }).ToList();

            return (StatusCodes.OK, new LeaderboardEntriesResponse
            {
                LeaderboardId = body.LeaderboardId,
                Entries = responseEntries,
                TotalEntries = totalEntries
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ranks around entity");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "GetRanksAround",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/rank/around",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of CreateSeason operation.
    /// Creates a new season for a seasonal leaderboard.
    /// </summary>
    public async Task<(StatusCodes, SeasonResponse?)> CreateSeasonAsync(CreateSeasonRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating new season for {LeaderboardId}", body.LeaderboardId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var (definition, defEtag) = await definitionStore.GetWithETagAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!definition.IsSeasonal)
            {
                return (StatusCodes.BadRequest, null);
            }

            var now = DateTimeOffset.UtcNow;
            var newSeasonNumber = (definition.CurrentSeason ?? 0) + 1;

            var previousSeason = definition.CurrentSeason;
            // Use per-request archivePrevious flag (defaults to true in schema)
            if (!body.ArchivePrevious && previousSeason.HasValue)
            {
                var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
                var previousRankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, previousSeason.Value);
                await rankingStore.SortedSetDeleteAsync(previousRankingKey, cancellationToken);

                await definitionStore.RemoveFromSetAsync(
                    GetSeasonIndexKey(body.GameServiceId, body.LeaderboardId),
                    previousSeason.Value,
                    cancellationToken: cancellationToken);
            }

            // Update definition with new season using optimistic concurrency
            definition.CurrentSeason = newSeasonNumber;
            // defEtag is non-null at this point; coalesce satisfies compiler nullable analysis
            var newDefEtag = await definitionStore.TrySaveAsync(defKey, definition, defEtag ?? string.Empty, cancellationToken);
            if (newDefEtag == null)
            {
                _logger.LogWarning("Concurrent modification detected for leaderboard {LeaderboardId} season creation", body.LeaderboardId);
                return (StatusCodes.Conflict, null);
            }
            await definitionStore.AddToSetAsync(
                GetSeasonIndexKey(body.GameServiceId, body.LeaderboardId),
                newSeasonNumber,
                cancellationToken: cancellationToken);

            // Publish season started event
            var seasonEvent = new LeaderboardSeasonStartedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                GameServiceId = body.GameServiceId,
                LeaderboardId = body.LeaderboardId,
                SeasonNumber = newSeasonNumber,
                PreviousSeasonNumber = newSeasonNumber - 1
            };
            await _messageBus.TryPublishAsync("leaderboard.season.started", seasonEvent, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new SeasonResponse
            {
                LeaderboardId = body.LeaderboardId,
                SeasonNumber = newSeasonNumber,
                SeasonName = body.SeasonName,
                StartedAt = now,
                EndedAt = null,
                IsActive = true,
                EntryCount = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating season");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "CreateSeason",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/season/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetSeason operation.
    /// Gets season information.
    /// </summary>
    public async Task<(StatusCodes, SeasonResponse?)> GetSeasonAsync(GetSeasonRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting season info for {LeaderboardId}", body.LeaderboardId);

        try
        {
            var definitionStore = _stateStoreFactory.GetCacheableStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
            var defKey = GetDefinitionKey(body.GameServiceId, body.LeaderboardId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!definition.IsSeasonal)
            {
                return (StatusCodes.BadRequest, null);
            }

            var seasonNumber = body.SeasonNumber ?? definition.CurrentSeason ?? 1;
            var isActive = seasonNumber == definition.CurrentSeason;

            var seasonIndexKey = GetSeasonIndexKey(body.GameServiceId, body.LeaderboardId);
            var seasons = await definitionStore.GetSetAsync<int>(seasonIndexKey, cancellationToken);
            if (seasons.Count > 0 && !seasons.Contains(seasonNumber))
            {
                return (StatusCodes.NotFound, null);
            }

            // Get entry count for the season
            var rankingStore = _stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.LeaderboardRanking);
            var rankingKey = GetRankingKey(body.GameServiceId, body.LeaderboardId, seasonNumber);
            var entryCount = await rankingStore.SortedSetCountAsync(rankingKey, cancellationToken);

            return (StatusCodes.OK, new SeasonResponse
            {
                LeaderboardId = body.LeaderboardId,
                SeasonNumber = seasonNumber,
                StartedAt = definition.CreatedAt, // Simplified - would need actual season start tracking
                EndedAt = isActive ? null : DateTimeOffset.UtcNow, // Simplified
                IsActive = isActive,
                EntryCount = entryCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting season");
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "GetSeason",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/leaderboard/season/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Calculates the final score based on update mode.
    /// </summary>
    private static double CalculateFinalScore(UpdateMode updateMode, double? previousScore, double newScore)
        => updateMode switch
        {
            UpdateMode.Replace => newScore,
            UpdateMode.Increment => (previousScore ?? 0) + newScore,
            UpdateMode.Max => Math.Max(previousScore ?? double.MinValue, newScore),
            UpdateMode.Min => Math.Min(previousScore ?? double.MaxValue, newScore),
            _ => newScore
        };

    /// <summary>
    /// Maps internal data to response model.
    /// </summary>
    private static LeaderboardDefinitionResponse MapToResponse(LeaderboardDefinitionData definition, long entryCount)
        => new LeaderboardDefinitionResponse
        {
            GameServiceId = definition.GameServiceId,
            LeaderboardId = definition.LeaderboardId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            EntityTypes = definition.EntityTypes ?? new List<EntityType>(),
            SortOrder = definition.SortOrder,
            UpdateMode = definition.UpdateMode,
            IsSeasonal = definition.IsSeasonal,
            IsPublic = definition.IsPublic,
            CurrentSeason = definition.CurrentSeason,
            EntryCount = entryCount,
            CreatedAt = definition.CreatedAt,
            Metadata = definition.Metadata
        };

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering Leaderboard service permissions...");
        await LeaderboardPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}
