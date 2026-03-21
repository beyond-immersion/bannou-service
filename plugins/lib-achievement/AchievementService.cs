using BeyondImmersion.Bannou.Achievement.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Achievement.Sync;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-achievement.tests")]

namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Implementation of the Achievement service.
/// Provides achievement definitions, progress tracking, unlock management, and platform sync.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>AchievementService.cs (this file) - Business logic</item>
///   <item>AchievementServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/AchievementPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("achievement", typeof(IAchievementService), lifetime: ServiceLifetime.Scoped)]
public partial class AchievementService : IAchievementService, ICleanDeprecatedEntity
{
    private readonly IMessageBus _messageBus;
    private readonly ICacheableStateStore<AchievementDefinitionData> _definitionStore;
    private readonly IStateStore<EntityProgressData> _progressStore;
    private readonly ILogger<AchievementService> _logger;
    private readonly AchievementServiceConfiguration _configuration;
    private readonly IEnumerable<IPlatformAchievementSync> _platformSyncs;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly IResourceClient _resourceClient;
    private readonly IStateStore<PlatformSyncTrackingData> _syncStore;

    // State store key prefixes per FOUNDATION TENETS (Build*Key pattern)
    private const string DEFINITION_KEY_PREFIX = "achievement-def:";
    private const string DEFINITION_INDEX_PREFIX = "achievement-definitions";
    private const string PROGRESS_KEY_PREFIX = "achievement-progress:";
    private const string SYNC_KEY_PREFIX = "achievement-sync:";
    private const string GAME_SERVICE_INDEX_KEY = "achievement-game-services";

    /// <summary>
    /// Initializes a new instance of the AchievementService.
    /// </summary>
    public AchievementService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<AchievementService> logger,
        AchievementServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IEnumerable<IPlatformAchievementSync> platformSyncs,
        IDistributedLockProvider lockProvider,
        ITelemetryProvider telemetryProvider,
        IEntitySessionRegistry entitySessionRegistry,
        IResourceClient resourceClient)
    {
        _messageBus = messageBus;
        _definitionStore = stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        _progressStore = stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        _syncStore = stateStoreFactory.GetStore<PlatformSyncTrackingData>(StateStoreDefinitions.AchievementSync);
        _logger = logger;
        _configuration = configuration;
        _platformSyncs = platformSyncs;
        _lockProvider = lockProvider;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _entitySessionRegistry = entitySessionRegistry;
        _resourceClient = resourceClient;

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Builds the key for an achievement definition.
    /// Format: {DEFINITION_KEY_PREFIX}{gameServiceId}:{achievementId}
    /// </summary>
    internal static string BuildDefinitionKey(Guid gameServiceId, string achievementId)
        => $"{DEFINITION_KEY_PREFIX}{gameServiceId}:{achievementId}";

    /// <summary>
    /// Builds the key for the achievement definition index.
    /// Format: {DEFINITION_INDEX_PREFIX}:{gameServiceId}
    /// </summary>
    internal static string BuildDefinitionIndexKey(Guid gameServiceId)
        => $"{DEFINITION_INDEX_PREFIX}:{gameServiceId}";

    /// <summary>
    /// Builds the key for entity progress.
    /// Format: {PROGRESS_KEY_PREFIX}{gameServiceId}:{entityType}:{entityId}
    /// </summary>
    internal static string BuildEntityProgressKey(Guid gameServiceId, EntityType entityType, Guid entityId)
        => $"{PROGRESS_KEY_PREFIX}{gameServiceId}:{entityType}:{entityId}";

    /// <summary>
    /// Builds the key for platform sync tracking.
    /// Format: {SYNC_KEY_PREFIX}{gameServiceId}:{entityId}:{platform}
    /// </summary>
    internal static string BuildSyncTrackingKey(Guid gameServiceId, Guid entityId, Platform platform)
        => $"{SYNC_KEY_PREFIX}{gameServiceId}:{entityId}:{platform}";

    /// <summary>
    /// Implementation of CreateAchievementDefinition operation.
    /// Creates a new achievement definition.
    /// </summary>
    public async Task<(StatusCodes, AchievementDefinitionResponse?)> CreateAchievementDefinitionAsync(CreateAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating achievement {AchievementId} for game service {GameServiceId}",
            body.AchievementId, body.GameServiceId);

        var key = BuildDefinitionKey(body.GameServiceId, body.AchievementId);

        // Check if already exists
        var existing = await _definitionStore.GetAsync(key, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Achievement {AchievementId} already exists", body.AchievementId);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var definition = new AchievementDefinitionData
        {
            GameServiceId = body.GameServiceId,
            AchievementId = body.AchievementId,
            DisplayName = body.DisplayName,
            Description = body.Description,
            HiddenDescription = body.HiddenDescription,
            AchievementType = body.AchievementType,
            Category = body.Category,
            EntityTypes = body.EntityTypes?.ToList() ?? new List<EntityType> { EntityType.Account },
            ProgressTarget = body.ProgressTarget,
            Points = body.Points,
            IconUrl = body.IconUrl,
            Platforms = body.Platforms?.ToList() ?? new List<Platform> { Platform.Internal },
            PlatformMappings = body.PlatformMappings?.Select(m => new PlatformMappingData
            {
                Platform = m.Platform,
                PlatformAchievementId = m.PlatformAchievementId
            }).ToList(),
            Prerequisites = body.Prerequisites?.ToList(),
            ScoreType = body.ScoreType,
            MilestoneType = body.MilestoneType,
            MilestoneValue = body.MilestoneValue,
            MilestoneName = body.MilestoneName,
            LeaderboardId = body.LeaderboardId,
            RankThreshold = body.RankThreshold,
            IsActive = body.IsActive,
            CreatedAt = now,
            Metadata = body.Metadata
        };

        await _definitionStore.SaveAsync(key, definition, options: null, cancellationToken);
        await _definitionStore.AddToSetAsync(
            BuildDefinitionIndexKey(body.GameServiceId),
            body.AchievementId,
            cancellationToken: cancellationToken);

        // Track the game service ID for background rarity recalculation
        await _definitionStore.AddToSetAsync(
            GAME_SERVICE_INDEX_KEY,
            body.GameServiceId.ToString(),
            cancellationToken: cancellationToken);

        // Publish definition created event
        await PublishDefinitionCreatedEventAsync(definition, cancellationToken);

        return (StatusCodes.OK, MapToResponse(definition, 0));
    }

    /// <summary>
    /// Implementation of GetAchievementDefinition operation.
    /// Retrieves an achievement definition.
    /// </summary>
    public async Task<(StatusCodes, AchievementDefinitionResponse?)> GetAchievementDefinitionAsync(GetAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting achievement {AchievementId}", body.AchievementId);

        var key = BuildDefinitionKey(body.GameServiceId, body.AchievementId);

        var definition = await _definitionStore.GetAsync(key, cancellationToken);
        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
    }

    /// <summary>
    /// Implementation of ListAchievementDefinitions operation.
    /// Lists all achievements for a game service with optional filters.
    /// </summary>
    public async Task<(StatusCodes, ListAchievementDefinitionsResponse?)> ListAchievementDefinitionsAsync(ListAchievementDefinitionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing achievements for game service {GameServiceId}", body.GameServiceId);

        var indexKey = BuildDefinitionIndexKey(body.GameServiceId);
        var achievementIds = await _definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

        if (achievementIds.Count == 0)
        {
            return (StatusCodes.OK, new ListAchievementDefinitionsResponse
            {
                Achievements = new List<AchievementDefinitionResponse>()
            });
        }

        var achievements = new List<AchievementDefinitionResponse>();

        foreach (var achievementId in achievementIds)
        {
            var defKey = BuildDefinitionKey(body.GameServiceId, achievementId);
            var definition = await _definitionStore.GetAsync(defKey, cancellationToken);
            if (definition == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(body.Category) && definition.Category != body.Category)
            {
                continue;
            }

            if (body.Platform.HasValue &&
                (definition.Platforms == null || !definition.Platforms.Contains(body.Platform.Value)))
            {
                continue;
            }

            if (body.AchievementType.HasValue && definition.AchievementType != body.AchievementType.Value)
            {
                continue;
            }

            if (body.IsActive.HasValue && definition.IsActive != body.IsActive.Value)
            {
                continue;
            }

            if (!body.IncludeHidden &&
                (definition.AchievementType == AchievementType.Hidden || definition.AchievementType == AchievementType.Secret))
            {
                continue;
            }

            // IMPLEMENTATION TENETS: Filter deprecated by default
            if (!body.IncludeDeprecated && definition.IsDeprecated)
            {
                continue;
            }

            achievements.Add(MapToResponse(definition, definition.EarnedCount));
        }

        var ordered = achievements
            .OrderBy(a => a.AchievementId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (StatusCodes.OK, new ListAchievementDefinitionsResponse
        {
            Achievements = ordered
        });
    }

    /// <summary>
    /// Implementation of UpdateAchievementDefinition operation.
    /// Updates an achievement definition.
    /// </summary>
    public async Task<(StatusCodes, AchievementDefinitionResponse?)> UpdateAchievementDefinitionAsync(UpdateAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating achievement {AchievementId}", body.AchievementId);

        var key = BuildDefinitionKey(body.GameServiceId, body.AchievementId);

        var (definition, etag) = await _definitionStore.GetWithETagAsync(key, cancellationToken);
        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Track which fields are being updated
        var changedFields = new List<string>();

        // Apply updates
        if (!string.IsNullOrEmpty(body.DisplayName) && body.DisplayName != definition.DisplayName)
        {
            definition.DisplayName = body.DisplayName;
            changedFields.Add("displayName");
        }
        if (body.Description != null && body.Description != definition.Description)
        {
            definition.Description = body.Description;
            changedFields.Add("description");
        }
        if (body.Category != null && body.Category != definition.Category)
        {
            definition.Category = body.Category;
            changedFields.Add("category");
        }
        if (body.IsActive.HasValue && body.IsActive.Value != definition.IsActive)
        {
            definition.IsActive = body.IsActive.Value;
            changedFields.Add("isActive");
        }
        if (body.PlatformMappings != null)
        {
            definition.PlatformMappings = body.PlatformMappings.Select(m => new PlatformMappingData
            {
                Platform = m.Platform,
                PlatformAchievementId = m.PlatformAchievementId
            }).ToList();
            changedFields.Add("platformMappings");
        }
        if (body.ScoreType != null && body.ScoreType != definition.ScoreType)
        {
            definition.ScoreType = body.ScoreType;
            changedFields.Add("scoreType");
        }
        if (body.MilestoneType != null && body.MilestoneType != definition.MilestoneType)
        {
            definition.MilestoneType = body.MilestoneType;
            changedFields.Add("milestoneType");
        }
        if (body.MilestoneValue.HasValue && body.MilestoneValue != definition.MilestoneValue)
        {
            definition.MilestoneValue = body.MilestoneValue;
            changedFields.Add("milestoneValue");
        }
        if (body.MilestoneName != null && body.MilestoneName != definition.MilestoneName)
        {
            definition.MilestoneName = body.MilestoneName;
            changedFields.Add("milestoneName");
        }
        if (body.LeaderboardId != null && body.LeaderboardId != definition.LeaderboardId)
        {
            definition.LeaderboardId = body.LeaderboardId;
            changedFields.Add("leaderboardId");
        }
        if (body.RankThreshold.HasValue && body.RankThreshold != definition.RankThreshold)
        {
            definition.RankThreshold = body.RankThreshold;
            changedFields.Add("rankThreshold");
        }

        if (changedFields.Count == 0)
        {
            // No changes
            return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
        }

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var newEtag = await _definitionStore.TrySaveAsync(key, definition, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for achievement definition {AchievementId}", body.AchievementId);
            return (StatusCodes.Conflict, null);
        }

        // Publish definition updated event
        await PublishDefinitionUpdatedEventAsync(definition, changedFields, cancellationToken);

        return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
    }

    /// <summary>
    /// Implementation of DeprecateAchievementDefinition operation.
    /// Marks an achievement definition as deprecated. Idempotent.
    /// </summary>
    public async Task<(StatusCodes, AchievementDefinitionResponse?)> DeprecateAchievementDefinitionAsync(DeprecateAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deprecating achievement {AchievementId}", body.AchievementId);

        var key = BuildDefinitionKey(body.GameServiceId, body.AchievementId);

        var (definition, etag) = await _definitionStore.GetWithETagAsync(key, cancellationToken);
        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Idempotent: already deprecated returns OK
        if (definition.IsDeprecated)
        {
            return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
        }

        var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };

        definition.IsDeprecated = true;
        definition.DeprecatedAt = DateTimeOffset.UtcNow;
        definition.DeprecationReason = body.DeprecationReason;

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var newEtag = await _definitionStore.TrySaveAsync(key, definition, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for achievement definition {AchievementId}", body.AchievementId);
            return (StatusCodes.Conflict, null);
        }

        await PublishDefinitionUpdatedEventAsync(definition, changedFields, cancellationToken);

        _logger.LogInformation("Achievement definition {AchievementId} deprecated", body.AchievementId);

        return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
    }

    /// <summary>
    /// Implementation of GetAchievementProgress operation.
    /// Gets progress for an entity across all or specific achievements.
    /// </summary>
    public async Task<(StatusCodes, AchievementProgressResponse?)> GetAchievementProgressAsync(GetAchievementProgressRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting achievement progress for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        var progressKey = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        var entityProgress = await _progressStore.GetAsync(progressKey, cancellationToken) ?? new EntityProgressData
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Achievements = new Dictionary<string, AchievementProgressData>(),
            TotalPoints = 0
        };

        var progressList = new List<AchievementProgress>();
        var unlockedCount = 0;

        // If specific achievement requested, filter
        if (!string.IsNullOrEmpty(body.AchievementId))
        {
            if (entityProgress.Achievements.TryGetValue(body.AchievementId, out var progress))
            {
                // Verify definition still exists (skip orphaned progress from deleted definitions)
                var defKey = BuildDefinitionKey(body.GameServiceId, body.AchievementId);
                var definition = await _definitionStore.GetAsync(defKey, cancellationToken);
                if (definition != null)
                {
                    progressList.Add(MapToAchievementProgress(body.AchievementId, progress));
                    if (progress.IsUnlocked)
                    {
                        unlockedCount++;
                    }
                }
            }
        }
        else
        {
            // Return all progress, filtering out orphaned entries from deleted definitions
            foreach (var kvp in entityProgress.Achievements)
            {
                var defKey = BuildDefinitionKey(body.GameServiceId, kvp.Key);
                var definition = await _definitionStore.GetAsync(defKey, cancellationToken);
                if (definition == null)
                {
                    continue;
                }

                progressList.Add(MapToAchievementProgress(kvp.Key, kvp.Value));
                if (kvp.Value.IsUnlocked)
                {
                    unlockedCount++;
                }
            }
        }

        return (StatusCodes.OK, new AchievementProgressResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Progress = progressList,
            TotalPoints = entityProgress.TotalPoints,
            UnlockedCount = unlockedCount
        });
    }

    /// <summary>
    /// Implementation of UpdateAchievementProgress operation.
    /// Increments progress on a progressive achievement.
    /// </summary>
    public async Task<(StatusCodes, UpdateAchievementProgressResponse?)> UpdateAchievementProgressAsync(UpdateAchievementProgressRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating progress for {AchievementId} for {EntityType}:{EntityId}",
            body.AchievementId, body.EntityType, body.EntityId);

        // Get achievement definition
        var defKey = BuildDefinitionKey(body.GameServiceId, body.AchievementId);
        var definition = await _definitionStore.GetAsync(defKey, cancellationToken);

        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Deprecated definitions cannot receive new progress
        if (definition.IsDeprecated)
        {
            _logger.LogWarning("Cannot update progress on deprecated achievement {AchievementId}", body.AchievementId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate entity type
        if (definition.EntityTypes != null && !definition.EntityTypes.Contains(body.EntityType))
        {
            return (StatusCodes.BadRequest, null);
        }

        // Must be a progressive achievement
        if (definition.AchievementType != AchievementType.Progressive || !definition.ProgressTarget.HasValue)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Acquire lock on progress key (compound operation: modifies both progress and definition)
        var progressKey = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        await using var progressLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.AchievementLock, progressKey, Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!progressLock.Success)
        {
            _logger.LogWarning("Could not acquire progress lock for {ProgressKey}", progressKey);
            return (StatusCodes.Conflict, null);
        }

        // Re-read under lock
        var existingProgress = await _progressStore.GetAsync(progressKey, cancellationToken);
        var isFirstProgressForEntity = existingProgress == null;
        var entityProgress = existingProgress ?? new EntityProgressData
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Achievements = new Dictionary<string, AchievementProgressData>(),
            TotalPoints = 0
        };

        // Get or create achievement progress
        if (!entityProgress.Achievements.TryGetValue(body.AchievementId, out var achievementProgress))
        {
            achievementProgress = new AchievementProgressData
            {
                DisplayName = definition.DisplayName,
                CurrentProgress = 0,
                TargetProgress = definition.ProgressTarget.Value,
                IsUnlocked = false
            };
        }

        // Already unlocked? No need to update
        if (achievementProgress.IsUnlocked)
        {
            return (StatusCodes.OK, new UpdateAchievementProgressResponse
            {
                PreviousProgress = achievementProgress.CurrentProgress,
                NewProgress = achievementProgress.CurrentProgress,
                TargetProgress = achievementProgress.TargetProgress,
                Unlocked = false,
                UnlockedAt = achievementProgress.UnlockedAt
            });
        }

        var previousProgress = achievementProgress.CurrentProgress;
        achievementProgress.CurrentProgress += body.Increment;

        var unlocked = false;
        DateTimeOffset? unlockedAt = null;

        // Check if this unlocks the achievement
        if (achievementProgress.CurrentProgress >= achievementProgress.TargetProgress)
        {
            achievementProgress.CurrentProgress = achievementProgress.TargetProgress;
            achievementProgress.IsUnlocked = true;
            achievementProgress.UnlockedAt = DateTimeOffset.UtcNow;
            unlocked = true;
            unlockedAt = achievementProgress.UnlockedAt;
            entityProgress.TotalPoints += definition.Points;

            // Increment earned count with retry on ETag conflict
            await IncrementEarnedCountAsync(defKey, cancellationToken);
        }

        // Save progress (permanent by default; positive ProgressTtlSeconds enables expiry)
        entityProgress.Achievements[body.AchievementId] = achievementProgress;
        await _progressStore.SaveAsync(progressKey, entityProgress,
            _configuration.ProgressTtlSeconds > 0 ? new StateOptions { Ttl = _configuration.ProgressTtlSeconds } : null,
            cancellationToken);

        // Register character reference with lib-resource on first progress creation.
        // Only for permanent records — TTL-based progress self-expires and needs no cleanup coordination.
        if (isFirstProgressForEntity && body.EntityType == EntityType.Character
            && _configuration.ProgressTtlSeconds == 0)
        {
            await RegisterCharacterReferenceAsync(progressKey, body.EntityId, cancellationToken);
        }

        // Publish progress event
        var progressEvent = new AchievementProgressUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = body.GameServiceId,
            AchievementId = body.AchievementId,
            DisplayName = definition.DisplayName,
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            PreviousProgress = previousProgress,
            NewProgress = achievementProgress.CurrentProgress,
            TargetProgress = achievementProgress.TargetProgress,
            PercentComplete = (double)achievementProgress.CurrentProgress / achievementProgress.TargetProgress * 100.0
        };
        await _messageBus.PublishAchievementProgressUpdatedAsync(progressEvent, cancellationToken);

        // Check for progress milestones and push client events
        // IMPLEMENTATION TENETS compliant: milestone thresholds from configuration
        var milestones = _configuration.ProgressMilestonePercents
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
        var previousPercent = achievementProgress.TargetProgress > 0
            ? (double)previousProgress / achievementProgress.TargetProgress * 100.0
            : 0;
        var currentPercent = achievementProgress.TargetProgress > 0
            ? (double)achievementProgress.CurrentProgress / achievementProgress.TargetProgress * 100.0
            : 0;

        foreach (var milestone in milestones)
        {
            if (previousPercent < milestone && currentPercent >= milestone)
            {
                await _entitySessionRegistry.PublishToEntitySessionsAsync(
                    body.EntityType.ToString().ToLowerInvariant(), body.EntityId,
                    new AchievementProgressMilestoneClientEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        GameServiceId = body.GameServiceId,
                        AchievementId = body.AchievementId,
                        DisplayName = definition.DisplayName,
                        CurrentProgress = achievementProgress.CurrentProgress,
                        TargetProgress = achievementProgress.TargetProgress,
                        PercentComplete = currentPercent
                    },
                    cancellationToken);
            }
        }

        // If unlocked, publish unlock event
        if (unlocked)
        {
            await PublishUnlockEventAsync(body.GameServiceId, definition, body.EntityId, body.EntityType, entityProgress.TotalPoints, cancellationToken);
        }

        return (StatusCodes.OK, new UpdateAchievementProgressResponse
        {
            PreviousProgress = previousProgress,
            NewProgress = achievementProgress.CurrentProgress,
            TargetProgress = achievementProgress.TargetProgress,
            Unlocked = unlocked,
            UnlockedAt = unlockedAt
        });
    }

    /// <summary>
    /// Implementation of UnlockAchievement operation.
    /// Directly unlocks an achievement for an entity.
    /// </summary>
    public async Task<(StatusCodes, UnlockAchievementResponse?)> UnlockAchievementAsync(UnlockAchievementRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Unlocking achievement {AchievementId} for {EntityType}:{EntityId}",
            body.AchievementId, body.EntityType, body.EntityId);

        // Get achievement definition
        var defKey = BuildDefinitionKey(body.GameServiceId, body.AchievementId);
        var definition = await _definitionStore.GetAsync(defKey, cancellationToken);

        if (definition == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Deprecated definitions cannot receive new progress
        if (definition.IsDeprecated)
        {
            _logger.LogWarning("Cannot unlock deprecated achievement {AchievementId}", body.AchievementId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate entity type
        if (definition.EntityTypes != null && !definition.EntityTypes.Contains(body.EntityType))
        {
            return (StatusCodes.BadRequest, null);
        }

        // Check prerequisites
        if (definition.Prerequisites != null && definition.Prerequisites.Count > 0)
        {
            var progressKey = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
            var entityProgress = await _progressStore.GetAsync(progressKey, cancellationToken);

            if (entityProgress == null)
            {
                _logger.LogWarning("Prerequisites not met for {AchievementId}", body.AchievementId);
                return (StatusCodes.BadRequest, null);
            }

            foreach (var prereq in definition.Prerequisites)
            {
                if (!entityProgress.Achievements.TryGetValue(prereq, out var prereqProgress) || !prereqProgress.IsUnlocked)
                {
                    _logger.LogWarning("Prerequisite {Prerequisite} not unlocked for {AchievementId}", prereq, body.AchievementId);
                    return (StatusCodes.BadRequest, null);
                }
            }
        }

        // Acquire lock on progress key (compound operation: modifies both progress and definition)
        var key = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        await using var progressLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.AchievementLock, key, Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!progressLock.Success)
        {
            _logger.LogWarning("Could not acquire progress lock for {ProgressKey}", key);
            return (StatusCodes.Conflict, null);
        }

        // Re-read under lock
        var existingUnlockProgress = await _progressStore.GetAsync(key, cancellationToken);
        var isFirstProgressForUnlock = existingUnlockProgress == null;
        var progress = existingUnlockProgress ?? new EntityProgressData
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Achievements = new Dictionary<string, AchievementProgressData>(),
            TotalPoints = 0
        };

        // Check if already unlocked
        if (progress.Achievements.TryGetValue(body.AchievementId, out var existing) && existing.IsUnlocked)
        {
            return (StatusCodes.OK, new UnlockAchievementResponse
            {
                UnlockedAt = existing.UnlockedAt ?? DateTimeOffset.UtcNow
            });
        }

        var now = DateTimeOffset.UtcNow;
        progress.Achievements[body.AchievementId] = new AchievementProgressData
        {
            DisplayName = definition.DisplayName,
            CurrentProgress = definition.ProgressTarget ?? 1,
            TargetProgress = definition.ProgressTarget ?? 1,
            IsUnlocked = true,
            UnlockedAt = now
        };
        progress.TotalPoints += definition.Points;

        // Increment earned count with retry on ETag conflict
        await IncrementEarnedCountAsync(defKey, cancellationToken);

        await _progressStore.SaveAsync(key, progress,
            _configuration.ProgressTtlSeconds > 0 ? new StateOptions { Ttl = _configuration.ProgressTtlSeconds } : null,
            cancellationToken);

        // Register character reference with lib-resource on first progress creation.
        // Only for permanent records — TTL-based progress self-expires and needs no cleanup coordination.
        if (isFirstProgressForUnlock && body.EntityType == EntityType.Character
            && _configuration.ProgressTtlSeconds == 0)
        {
            await RegisterCharacterReferenceAsync(key, body.EntityId, cancellationToken);
        }

        // Publish unlock event
        await PublishUnlockEventAsync(body.GameServiceId, definition, body.EntityId, body.EntityType, progress.TotalPoints, cancellationToken);

        // Platform sync
        var platformSyncStatus = new Dictionary<string, SyncStatus>();
        if (_configuration.AutoSyncOnUnlock && !body.SkipPlatformSync && definition.Platforms != null)
        {
            foreach (var platform in definition.Platforms.Where(p => p != Platform.Internal))
            {
                var syncResult = await SyncAchievementToPlatformAsync(body.GameServiceId, body.AchievementId, definition, body.EntityId, platform, cancellationToken);
                platformSyncStatus[platform.ToString().ToLowerInvariant()] = syncResult;
            }
        }

        return (StatusCodes.OK, new UnlockAchievementResponse
        {
            UnlockedAt = now,
            PlatformSyncStatus = platformSyncStatus.Count > 0 ? platformSyncStatus : null
        });
    }

    /// <summary>
    /// Implementation of ListUnlockedAchievements operation.
    /// Lists all achievements unlocked by an entity.
    /// </summary>
    public async Task<(StatusCodes, ListUnlockedAchievementsResponse?)> ListUnlockedAchievementsAsync(ListUnlockedAchievementsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing unlocked achievements for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        var progressKey = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
        var entityProgress = await _progressStore.GetAsync(progressKey, cancellationToken);

        if (entityProgress == null)
        {
            return (StatusCodes.OK, new ListUnlockedAchievementsResponse
            {
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                Achievements = new List<UnlockedAchievement>(),
                TotalPoints = 0
            });
        }

        var unlockedAchievements = new List<UnlockedAchievement>();
        var totalPoints = 0;

        foreach (var kvp in entityProgress.Achievements.Where(a => a.Value.IsUnlocked))
        {
            var defKey = BuildDefinitionKey(body.GameServiceId, kvp.Key);
            var definition = await _definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                continue;
            }

            // IMPLEMENTATION TENETS: Filter deprecated definitions by default
            if (!body.IncludeDeprecated && definition.IsDeprecated)
            {
                continue;
            }

            // Filter by platform if requested
            if (body.Platform.HasValue && definition.Platforms != null && !definition.Platforms.Contains(body.Platform.Value))
            {
                continue;
            }

            if (kvp.Value.UnlockedAt == null)
            {
                _logger.LogError(
                    "Achievement {AchievementId} is marked unlocked but has no UnlockedAt timestamp for {EntityType}:{EntityId} — data inconsistency",
                    kvp.Key, body.EntityType, body.EntityId);
                continue;
            }

            totalPoints += definition.Points;
            unlockedAchievements.Add(new UnlockedAchievement
            {
                AchievementId = kvp.Key,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Points = definition.Points,
                IconUrl = definition.IconUrl,
                UnlockedAt = kvp.Value.UnlockedAt.Value
            });
        }

        return (StatusCodes.OK, new ListUnlockedAchievementsResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Achievements = unlockedAchievements,
            TotalPoints = totalPoints
        });
    }

    /// <summary>
    /// Implementation of SyncPlatformAchievements operation.
    /// Manually triggers platform sync for an entity.
    /// </summary>
    public async Task<(StatusCodes, SyncPlatformAchievementsResponse?)> SyncPlatformAchievementsAsync(SyncPlatformAchievementsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Syncing achievements to {Platform} for {EntityType}:{EntityId}",
            body.Platform, body.EntityType, body.EntityId);

        if (body.EntityType != EntityType.Account)
        {
            _logger.LogWarning("Platform sync requires account entity type, received {EntityType}", body.EntityType);
            return (StatusCodes.BadRequest, null);
        }

        // Get sync provider
        var syncProvider = _platformSyncs.FirstOrDefault(s => s.Platform == body.Platform);
        if (syncProvider == null)
        {
            _logger.LogDebug("Platform {Platform} not supported for achievement sync", body.Platform);
            return (StatusCodes.BadRequest, null);
        }

        if (!syncProvider.IsConfigured)
        {
            _logger.LogWarning("Platform {Platform} is not configured for sync", body.Platform);
            return (StatusCodes.BadRequest, null);
        }

        // Check if linked
        var isLinked = await syncProvider.IsLinkedAsync(body.EntityId, cancellationToken);
        if (!isLinked)
        {
            return (StatusCodes.OK, new SyncPlatformAchievementsResponse
            {
                Platform = body.Platform,
                Synced = 0,
                Failed = 0
            });
        }

        var externalId = await syncProvider.GetExternalIdAsync(body.EntityId, cancellationToken);
        if (string.IsNullOrEmpty(externalId))
        {
            return (StatusCodes.OK, new SyncPlatformAchievementsResponse
            {
                Platform = body.Platform,
                Synced = 0,
                Failed = 0
            });
        }

        // Get entity progress
        var progressKey = BuildEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
        var entityProgress = await _progressStore.GetAsync(progressKey, cancellationToken);

        if (entityProgress == null || entityProgress.Achievements.Count == 0)
        {
            return (StatusCodes.OK, new SyncPlatformAchievementsResponse
            {
                Platform = body.Platform,
                Synced = 0,
                Failed = 0
            });
        }

        var synced = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var kvp in entityProgress.Achievements.Where(a => a.Value.IsUnlocked))
        {
            var defKey = BuildDefinitionKey(body.GameServiceId, kvp.Key);
            var definition = await _definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                _logger.LogError(
                    "Missing achievement definition for {AchievementId} during platform sync",
                    kvp.Key);
                await _messageBus.TryPublishErrorAsync(
                    "achievement",
                    "SyncPlatformAchievements",
                    "platform_sync_definition_missing",
                    $"Missing achievement definition for {kvp.Key}",
                    dependency: body.Platform.ToString().ToLowerInvariant(),
                    endpoint: "post:/achievement/platform/sync",
                    details: $"achievementId:{kvp.Key};entityId:{body.EntityId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                failed++;
                errors.Add($"{kvp.Key}: definition missing");
                continue;
            }

            if (definition.Platforms == null || !definition.Platforms.Contains(body.Platform))
            {
                continue;
            }

            var platformAchievementId = definition.PlatformMappings?.FirstOrDefault(m => m.Platform == body.Platform)?.PlatformAchievementId;
            if (string.IsNullOrEmpty(platformAchievementId))
            {
                _logger.LogError(
                    "Missing platform achievement ID for {Platform} achievement {AchievementId}",
                    body.Platform, kvp.Key);
                await _messageBus.TryPublishErrorAsync(
                    "achievement",
                    "SyncPlatformAchievements",
                    "platform_sync_missing_platform_id",
                    $"Missing platform achievement ID for {body.Platform} achievement {kvp.Key}",
                    dependency: body.Platform.ToString().ToLowerInvariant(),
                    endpoint: "post:/achievement/platform/sync",
                    details: $"achievementId:{kvp.Key};entityId:{body.EntityId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                failed++;
                errors.Add($"{kvp.Key}: missing platform mapping");
                continue;
            }

            var (result, exception) = await ExecutePlatformUnlockWithRetriesAsync(
                syncProvider,
                externalId,
                platformAchievementId,
                cancellationToken);

            if (!result.Success)
            {
                if (exception != null)
                {
                    _logger.LogError(exception,
                        "Platform sync failed for {Platform} achievement {AchievementId} after retries",
                        body.Platform, kvp.Key);
                }
                else
                {
                    _logger.LogError("Platform sync failed for {Platform} achievement {AchievementId}: {ErrorMessage}",
                        body.Platform, kvp.Key, result.ErrorMessage);
                }

                await _messageBus.TryPublishErrorAsync(
                    "achievement",
                    "SyncPlatformAchievements",
                    "platform_sync_failed",
                    result.ErrorMessage ?? "Platform sync failed",
                    dependency: body.Platform.ToString().ToLowerInvariant(),
                    endpoint: "post:/achievement/platform/sync",
                    details: $"achievementId:{kvp.Key};entityId:{body.EntityId}",
                    stack: exception?.StackTrace,
                    cancellationToken: cancellationToken);
            }

            await PublishPlatformSyncEventAsync(
                body.GameServiceId,
                kvp.Key,
                body.EntityId,
                body.Platform,
                platformAchievementId,
                result,
                cancellationToken);
            await RecordSyncOutcomeAsync(body.GameServiceId, body.EntityId, body.Platform, result.Success, result.ErrorMessage, cancellationToken);

            if (result.Success)
            {
                synced++;
            }
            else
            {
                failed++;
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    errors.Add($"{kvp.Key}: {result.ErrorMessage}");
                }
            }
        }

        return (StatusCodes.OK, new SyncPlatformAchievementsResponse
        {
            Platform = body.Platform,
            Synced = synced,
            Failed = failed,
            Errors = errors.Count > 0 ? errors : null
        });
    }

    /// <summary>
    /// Implementation of GetPlatformSyncStatus operation.
    /// Gets the platform sync status for an entity.
    /// </summary>
    public async Task<(StatusCodes, PlatformSyncStatusResponse?)> GetPlatformSyncStatusAsync(GetPlatformSyncStatusRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting platform sync status for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        if (body.EntityType != EntityType.Account)
        {
            _logger.LogWarning("Platform sync status requires account entity type, received {EntityType}", body.EntityType);
            return (StatusCodes.BadRequest, null);
        }

        var platforms = new List<PlatformStatus>();

        foreach (var syncProvider in _platformSyncs)
        {
            // Filter by specific platform if requested
            if (body.Platform.HasValue && syncProvider.Platform != body.Platform.Value)
            {
                continue;
            }

            // Skip unconfigured platforms
            if (!syncProvider.IsConfigured)
            {
                continue;
            }

            var isLinked = await syncProvider.IsLinkedAsync(body.EntityId, cancellationToken);
            var externalId = isLinked ? await syncProvider.GetExternalIdAsync(body.EntityId, cancellationToken) : null;

            var syncKey = BuildSyncTrackingKey(body.GameServiceId, body.EntityId, syncProvider.Platform);
            var syncTracking = await _syncStore.GetAsync(syncKey, cancellationToken);

            platforms.Add(new PlatformStatus
            {
                Platform = syncProvider.Platform,
                IsLinked = isLinked,
                ExternalId = externalId,
                SyncedCount = syncTracking?.SyncedCount ?? 0,
                PendingCount = null,
                FailedCount = syncTracking?.FailedCount ?? 0,
                LastSyncAt = syncTracking?.LastSyncAt,
                LastError = syncTracking?.LastError
            });
        }

        return (StatusCodes.OK, new PlatformSyncStatusResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Platforms = platforms
        });
    }

    /// <summary>
    /// Deletes all achievement progress records for a character across all game services.
    /// Called by lib-resource during character deletion cleanup.
    /// </summary>
    public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up achievement progress for character {CharacterId}", body.CharacterId);

        var progressRecordsDeleted = 0;

        // Get all game services that have achievement definitions
        var gameServiceIds = await _definitionStore.GetSetAsync<string>(GAME_SERVICE_INDEX_KEY, cancellationToken);

        foreach (var gameServiceIdStr in gameServiceIds)
        {
            if (!Guid.TryParse(gameServiceIdStr, out var gameServiceId))
            {
                _logger.LogError("Invalid game service ID in achievement index: {Value}", gameServiceIdStr);
                continue;
            }

            // Progress key: {gameServiceId}:{entityType}:{entityId}
            var progressKey = BuildEntityProgressKey(gameServiceId, EntityType.Character, body.CharacterId);
            var existing = await _progressStore.GetAsync(progressKey, cancellationToken);
            if (existing != null)
            {
                await _progressStore.DeleteAsync(progressKey, cancellationToken);
                progressRecordsDeleted++;
            }
        }

        _logger.LogInformation("Deleted {Count} achievement progress records for character {CharacterId}",
            progressRecordsDeleted, body.CharacterId);

        return (StatusCodes.OK, new CleanupByCharacterResponse
        {
            ProgressRecordsDeleted = progressRecordsDeleted
        });
    }

    /// <summary>
    /// Category B cleanup sweep for deprecated achievement definitions.
    /// Iterates all game services, finds deprecated definitions past the grace period
    /// with zero earned count, and permanently removes them. Publishes
    /// achievement.definition.deleted lifecycle events for each removed definition.
    /// Uses shared DeprecationCleanupHelper per IMPLEMENTATION TENETS.
    /// </summary>
    public async Task<(StatusCodes, CleanDeprecatedStringKeyResponse?)> CleanDeprecatedAchievementDefinitionsAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken = default)
    {
        // Collect all deprecated definitions across all game services
        var gameServiceIds = await _definitionStore.GetSetAsync<string>(GAME_SERVICE_INDEX_KEY, cancellationToken);
        var deprecated = new List<AchievementDefinitionData>();

        foreach (var gameServiceIdStr in gameServiceIds)
        {
            if (!Guid.TryParse(gameServiceIdStr, out var gameServiceId))
            {
                _logger.LogWarning("Invalid game service ID in achievement index: {Value}", gameServiceIdStr);
                continue;
            }

            var indexKey = BuildDefinitionIndexKey(gameServiceId);
            var achievementIds = await _definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

            foreach (var achievementId in achievementIds)
            {
                var defKey = BuildDefinitionKey(gameServiceId, achievementId);
                var definition = await _definitionStore.GetAsync(defKey, cancellationToken);
                if (definition is not null && definition.IsDeprecated)
                {
                    deprecated.Add(definition);
                }
            }
        }

        // Delegate to shared helper per IMPLEMENTATION TENETS (B20)
        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecated,
            getEntityId: d => d.AchievementId,
            getDeprecatedAt: d => d.DeprecatedAt,
            hasInstancesAsync: (d, ct) =>
            {
                // EarnedCount > 0 means entities have unlocked this achievement;
                // treat as having active instances to preserve data integrity
                return Task.FromResult(d.EarnedCount > 0);
            },
            deleteAndPublishAsync: async (d, ct) =>
            {
                var defKey = BuildDefinitionKey(d.GameServiceId, d.AchievementId);

                // Delete definition from primary store
                await _definitionStore.DeleteAsync(defKey, ct);

                // Remove from definition index set
                await _definitionStore.RemoveFromSetAsync(
                    BuildDefinitionIndexKey(d.GameServiceId),
                    d.AchievementId,
                    ct);

                // Publish lifecycle deleted event
                await _messageBus.TryPublishAsync(AchievementPublishedTopics.AchievementDefinitionDeleted, new AchievementDefinitionDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    GameServiceId = d.GameServiceId,
                    AchievementId = d.AchievementId,
                    DisplayName = d.DisplayName,
                    Description = d.Description,
                    HiddenDescription = d.HiddenDescription,
                    Category = d.Category,
                    AchievementType = d.AchievementType,
                    EntityTypes = d.EntityTypes?.ToList(),
                    ProgressTarget = d.ProgressTarget,
                    Points = d.Points,
                    IconUrl = d.IconUrl,
                    Platforms = d.Platforms?.ToList(),
                    PlatformMappings = d.PlatformMappings?.Select(m => new PlatformMapping
                    {
                        Platform = m.Platform,
                        PlatformAchievementId = m.PlatformAchievementId
                    }).ToList(),
                    Prerequisites = d.Prerequisites?.ToList(),
                    ScoreType = d.ScoreType,
                    MilestoneType = d.MilestoneType,
                    MilestoneValue = d.MilestoneValue,
                    MilestoneName = d.MilestoneName,
                    LeaderboardId = d.LeaderboardId,
                    RankThreshold = d.RankThreshold,
                    IsActive = d.IsActive,
                    EarnedCount = d.EarnedCount,
                    Metadata = d.Metadata,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsDeprecated = d.IsDeprecated,
                    DeprecatedAt = d.DeprecatedAt,
                    DeprecationReason = d.DeprecationReason
                }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedStringKeyResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }

}
