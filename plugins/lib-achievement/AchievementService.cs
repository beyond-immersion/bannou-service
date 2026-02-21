using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Achievement.Sync;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
public partial class AchievementService : IAchievementService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<AchievementService> _logger;
    private readonly AchievementServiceConfiguration _configuration;
    private readonly IEnumerable<IPlatformAchievementSync> _platformSyncs;
    private readonly IDistributedLockProvider _lockProvider;

    // State store key prefixes
    private const string DEFINITION_INDEX_PREFIX = "achievement-definitions";
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
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _platformSyncs = platformSyncs;
        _lockProvider = lockProvider;

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Generates the key for an achievement definition.
    /// Format: gameServiceId:achievementId
    /// </summary>
    private static string GetDefinitionKey(Guid gameServiceId, string achievementId)
        => $"{gameServiceId}:{achievementId}";

    /// <summary>
    /// Generates the key for the achievement definition index.
    /// Format: achievement-definitions:gameServiceId
    /// </summary>
    private static string GetDefinitionIndexKey(Guid gameServiceId)
        => $"{DEFINITION_INDEX_PREFIX}:{gameServiceId}";

    /// <summary>
    /// Generates the key for entity progress.
    /// Format: gameServiceId:entityType:entityId
    /// </summary>
    private static string GetEntityProgressKey(Guid gameServiceId, EntityType entityType, Guid entityId)
        => $"{gameServiceId}:{entityType}:{entityId}";

    /// <summary>
    /// Generates the key for specific achievement progress.
    /// Format: gameServiceId:achievementId:entityType:entityId
    /// </summary>
    private static string GetAchievementProgressKey(Guid gameServiceId, string achievementId, EntityType entityType, Guid entityId)
        => $"{gameServiceId}:{achievementId}:{entityType}:{entityId}";

    /// <summary>
    /// Implementation of CreateAchievementDefinition operation.
    /// Creates a new achievement definition.
    /// </summary>
    public async Task<(StatusCodes, AchievementDefinitionResponse?)> CreateAchievementDefinitionAsync(CreateAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating achievement {AchievementId} for game service {GameServiceId}",
            body.AchievementId, body.GameServiceId);

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var key = GetDefinitionKey(body.GameServiceId, body.AchievementId);

        // Check if already exists
        var existing = await definitionStore.GetAsync(key, cancellationToken);
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
            EntityTypes = body.EntityTypes?.ToList() ?? new List<EntityType> { EntityType.Account },
            ProgressTarget = body.ProgressTarget,
            Points = body.Points,
            IconUrl = body.IconUrl,
            Platforms = body.Platforms?.ToList() ?? new List<Platform> { Platform.Internal },
            PlatformIds = ParsePlatformIds(body.PlatformIds),
            Prerequisites = body.Prerequisites?.ToList(),
            IsActive = body.IsActive,
            CreatedAt = now,
            Metadata = MetadataHelper.ConvertToDictionary(body.Metadata)
        };

        await definitionStore.SaveAsync(key, definition, options: null, cancellationToken);
        await definitionStore.AddToSetAsync(
            GetDefinitionIndexKey(body.GameServiceId),
            body.AchievementId,
            cancellationToken: cancellationToken);

        // Track the game service ID for background rarity recalculation
        await definitionStore.AddToSetAsync(
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

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var key = GetDefinitionKey(body.GameServiceId, body.AchievementId);

        var definition = await definitionStore.GetAsync(key, cancellationToken);
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

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var indexKey = GetDefinitionIndexKey(body.GameServiceId);
        var achievementIds = await definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

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
            var defKey = GetDefinitionKey(body.GameServiceId, achievementId);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);
            if (definition == null)
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

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var key = GetDefinitionKey(body.GameServiceId, body.AchievementId);

        var (definition, etag) = await definitionStore.GetWithETagAsync(key, cancellationToken);
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
        if (body.IsActive.HasValue && body.IsActive.Value != definition.IsActive)
        {
            definition.IsActive = body.IsActive.Value;
            changedFields.Add("isActive");
        }
        if (body.PlatformIds != null)
        {
            definition.PlatformIds = ParsePlatformIds(body.PlatformIds);
            changedFields.Add("platformIds");
        }

        if (changedFields.Count == 0)
        {
            // No changes
            return (StatusCodes.OK, MapToResponse(definition, definition.EarnedCount));
        }

        var newEtag = await definitionStore.TrySaveAsync(key, definition, etag ?? string.Empty, cancellationToken);
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
    /// Implementation of DeleteAchievementDefinition operation.
    /// Deletes an achievement definition.
    /// </summary>
    public async Task<StatusCodes> DeleteAchievementDefinitionAsync(DeleteAchievementDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting achievement {AchievementId}", body.AchievementId);

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var key = GetDefinitionKey(body.GameServiceId, body.AchievementId);

        var definition = await definitionStore.GetAsync(key, cancellationToken);
        if (definition == null)
        {
            return StatusCodes.NotFound;
        }

        await definitionStore.DeleteAsync(key, cancellationToken);
        await definitionStore.RemoveFromSetAsync(
            GetDefinitionIndexKey(body.GameServiceId),
            body.AchievementId,
            cancellationToken);

        // Publish definition deleted event
        await PublishDefinitionDeletedEventAsync(definition, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Implementation of GetAchievementProgress operation.
    /// Gets progress for an entity across all or specific achievements.
    /// </summary>
    public async Task<(StatusCodes, AchievementProgressResponse?)> GetAchievementProgressAsync(GetAchievementProgressRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting achievement progress for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        var progressStore = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        var progressKey = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        var entityProgress = await progressStore.GetAsync(progressKey, cancellationToken) ?? new EntityProgressData
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Achievements = new Dictionary<string, AchievementProgressData>(),
            TotalPoints = 0
        };

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var progressList = new List<AchievementProgress>();
        var unlockedCount = 0;

        // If specific achievement requested, filter
        if (!string.IsNullOrEmpty(body.AchievementId))
        {
            if (entityProgress.Achievements.TryGetValue(body.AchievementId, out var progress))
            {
                // Verify definition still exists (skip orphaned progress from deleted definitions)
                var defKey = GetDefinitionKey(body.GameServiceId, body.AchievementId);
                var definition = await definitionStore.GetAsync(defKey, cancellationToken);
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
                var defKey = GetDefinitionKey(body.GameServiceId, kvp.Key);
                var definition = await definitionStore.GetAsync(defKey, cancellationToken);
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
        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var defKey = GetDefinitionKey(body.GameServiceId, body.AchievementId);
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

        // Must be a progressive achievement
        if (definition.AchievementType != AchievementType.Progressive || !definition.ProgressTarget.HasValue)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Acquire lock on progress key (compound operation: modifies both progress and definition)
        var progressStore = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        var progressKey = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        await using var progressLock = await _lockProvider.LockAsync(
            "achievement-progress", progressKey, Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!progressLock.Success)
        {
            _logger.LogWarning("Could not acquire progress lock for {ProgressKey}", progressKey);
            return (StatusCodes.Conflict, null);
        }

        // Re-read under lock
        var entityProgress = await progressStore.GetAsync(progressKey, cancellationToken) ?? new EntityProgressData
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
                AchievementId = body.AchievementId,
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
            await IncrementEarnedCountAsync(definitionStore, defKey, cancellationToken);
        }

        // Save progress (permanent by default; positive ProgressTtlSeconds enables expiry)
        entityProgress.Achievements[body.AchievementId] = achievementProgress;
        await progressStore.SaveAsync(progressKey, entityProgress,
            _configuration.ProgressTtlSeconds > 0 ? new StateOptions { Ttl = _configuration.ProgressTtlSeconds } : null,
            cancellationToken);

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
        await _messageBus.TryPublishAsync("achievement.progress.updated", progressEvent, cancellationToken: cancellationToken);

        // If unlocked, publish unlock event
        if (unlocked)
        {
            await PublishUnlockEventAsync(body.GameServiceId, definition, body.EntityId, body.EntityType, entityProgress.TotalPoints, cancellationToken);
        }

        return (StatusCodes.OK, new UpdateAchievementProgressResponse
        {
            AchievementId = body.AchievementId,
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
        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var defKey = GetDefinitionKey(body.GameServiceId, body.AchievementId);
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

        // Check prerequisites
        if (definition.Prerequisites != null && definition.Prerequisites.Count > 0)
        {
            var progressStore = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
            var progressKey = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
            var entityProgress = await progressStore.GetAsync(progressKey, cancellationToken);

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
        var store = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        var key = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);

        await using var progressLock = await _lockProvider.LockAsync(
            "achievement-progress", key, Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!progressLock.Success)
        {
            _logger.LogWarning("Could not acquire progress lock for {ProgressKey}", key);
            return (StatusCodes.Conflict, null);
        }

        // Re-read under lock
        var progress = await store.GetAsync(key, cancellationToken) ?? new EntityProgressData
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
                AchievementId = body.AchievementId,
                Unlocked = false, // Already was unlocked
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
        await IncrementEarnedCountAsync(definitionStore, defKey, cancellationToken);

        await store.SaveAsync(key, progress,
            _configuration.ProgressTtlSeconds > 0 ? new StateOptions { Ttl = _configuration.ProgressTtlSeconds } : null,
            cancellationToken);

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
            AchievementId = body.AchievementId,
            Unlocked = true,
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

        var progressStore = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        var progressKey = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
        var entityProgress = await progressStore.GetAsync(progressKey, cancellationToken);

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

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var unlockedAchievements = new List<UnlockedAchievement>();
        var totalPoints = 0;

        foreach (var kvp in entityProgress.Achievements.Where(a => a.Value.IsUnlocked))
        {
            var defKey = GetDefinitionKey(body.GameServiceId, kvp.Key);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

            if (definition == null)
            {
                continue;
            }

            // Filter by platform if requested
            if (body.Platform.HasValue && definition.Platforms != null && !definition.Platforms.Contains(body.Platform.Value))
            {
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
                UnlockedAt = kvp.Value.UnlockedAt ?? DateTimeOffset.UtcNow
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
                Failed = 0,
                NotLinked = true
            });
        }

        var externalId = await syncProvider.GetExternalIdAsync(body.EntityId, cancellationToken);
        if (string.IsNullOrEmpty(externalId))
        {
            return (StatusCodes.OK, new SyncPlatformAchievementsResponse
            {
                Platform = body.Platform,
                Synced = 0,
                Failed = 0,
                NotLinked = true
            });
        }

        // Get entity progress
        var progressStore = _stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        var progressKey = GetEntityProgressKey(body.GameServiceId, body.EntityType, body.EntityId);
        var entityProgress = await progressStore.GetAsync(progressKey, cancellationToken);

        if (entityProgress == null || entityProgress.Achievements.Count == 0)
        {
            return (StatusCodes.OK, new SyncPlatformAchievementsResponse
            {
                Platform = body.Platform,
                Synced = 0,
                Failed = 0,
                NotLinked = false
            });
        }

        var definitionStore = _stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);
        var synced = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var kvp in entityProgress.Achievements.Where(a => a.Value.IsUnlocked))
        {
            var defKey = GetDefinitionKey(body.GameServiceId, kvp.Key);
            var definition = await definitionStore.GetAsync(defKey, cancellationToken);

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

            var platformAchievementId = definition.PlatformIds?.GetValueOrDefault(body.Platform);
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
            NotLinked = false,
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

            platforms.Add(new PlatformStatus
            {
                Platform = syncProvider.Platform,
                IsLinked = isLinked,
                ExternalId = externalId,
                SyncedCount = 0, // Would need to track this per-entity
                PendingCount = 0,
                FailedCount = 0,
                LastSyncAt = null,
                LastError = null
            });
        }

        return (StatusCodes.OK, new PlatformSyncStatusResponse
        {
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            Platforms = platforms
        });
    }

    #region Helper Methods

    /// <summary>
    /// Parses platform ID dictionary from string-keyed request format to typed Platform-keyed format.
    /// Converts at the boundary between external API (string keys) and internal model (Platform enum keys).
    /// </summary>
    /// <param name="platformIds">The string-keyed dictionary from the request model.</param>
    /// <returns>Platform enum-keyed dictionary, or null if input is null.</returns>
    private static Dictionary<Platform, string>? ParsePlatformIds(IDictionary<string, string>? platformIds)
    {
        if (platformIds == null)
        {
            return null;
        }

        var result = new Dictionary<Platform, string>();
        foreach (var kvp in platformIds)
        {
            if (Enum.TryParse<Platform>(kvp.Key, ignoreCase: true, out var platform))
            {
                result[platform] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// Increments the EarnedCount on an achievement definition with optimistic concurrency retry.
    /// Retries up to 3 times on ETag conflict to prevent silent count loss when multiple
    /// entities unlock the same achievement concurrently.
    /// </summary>
    /// <param name="definitionStore">The state store for achievement definitions.</param>
    /// <param name="defKey">The definition key to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task IncrementEarnedCountAsync(
        IStateStore<AchievementDefinitionData> definitionStore,
        string defKey,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _configuration.EarnedCountRetryAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (freshDef, defEtag) = await definitionStore.GetWithETagAsync(defKey, cancellationToken);
            if (freshDef == null)
            {
                _logger.LogWarning("Definition {DefKey} not found during EarnedCount increment", defKey);
                return;
            }

            freshDef.EarnedCount++;
            var savedEtag = await definitionStore.TrySaveAsync(defKey, freshDef, defEtag ?? string.Empty, cancellationToken);
            if (savedEtag != null)
            {
                return;
            }

            _logger.LogDebug(
                "EarnedCount increment conflict on {DefKey}, attempt {Attempt}/{MaxAttempts}",
                defKey, attempt, maxAttempts);
        }

        _logger.LogWarning(
            "EarnedCount increment failed after {MaxAttempts} attempts for {DefKey} due to concurrent modifications",
            maxAttempts, defKey);
    }

    /// <summary>
    /// Publishes an achievement unlock event.
    /// </summary>
    private async Task PublishUnlockEventAsync(
        Guid gameServiceId,
        AchievementDefinitionData definition,
        Guid entityId,
        EntityType entityType,
        int totalPoints,
        CancellationToken cancellationToken)
    {
        var isRare = definition.EarnedCount < _configuration.RarityThresholdEarnedCount;
        double? rarityPercent = definition.RarityPercent;

        if (rarityPercent.HasValue)
        {
            isRare = isRare || rarityPercent.Value < _configuration.RareThresholdPercent;
        }
        else if (definition.TotalEligibleEntities > 0)
        {
            rarityPercent = (double)definition.EarnedCount / definition.TotalEligibleEntities * 100.0;
            isRare = isRare || rarityPercent.Value < _configuration.RareThresholdPercent;
        }

        var unlockEvent = new AchievementUnlockedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = gameServiceId,
            AchievementId = definition.AchievementId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            EntityId = entityId,
            EntityType = entityType,
            Points = definition.Points,
            TotalPoints = totalPoints,
            IconUrl = definition.IconUrl,
            IsRare = isRare,
            Rarity = rarityPercent
        };
        await _messageBus.TryPublishAsync("achievement.unlocked", unlockEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Syncs an achievement to a specific platform.
    /// </summary>
    private async Task<SyncStatus> SyncAchievementToPlatformAsync(
        Guid gameServiceId,
        string achievementId,
        AchievementDefinitionData definition,
        Guid entityId,
        Platform platform,
        CancellationToken cancellationToken)
    {
        var syncProvider = _platformSyncs.FirstOrDefault(s => s.Platform == platform);
        if (syncProvider == null)
        {
            var providerMissingMessage = $"No sync provider registered for platform {platform}";
            _logger.LogError(
                "No sync provider registered for platform {Platform}",
                platform);
            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "SyncAchievementToPlatform",
                "platform_sync_provider_missing",
                providerMissingMessage,
                dependency: platform.ToString().ToLowerInvariant(),
                endpoint: "post:/achievement/unlock",
                details: $"achievementId:{achievementId};entityId:{entityId}",
                stack: null,
                cancellationToken: cancellationToken);
            await PublishPlatformSyncEventAsync(
                gameServiceId,
                achievementId,
                entityId,
                platform,
                platformAchievementId: null,
                new PlatformSyncResult { Success = false, ErrorMessage = providerMissingMessage },
                cancellationToken);
            return SyncStatus.Failed;
        }

        if (!syncProvider.IsConfigured)
        {
            _logger.LogDebug(
                "Platform {Platform} is not configured, skipping sync for achievement {AchievementId}",
                platform, achievementId);
            return SyncStatus.Pending;
        }

        var isLinked = await syncProvider.IsLinkedAsync(entityId, cancellationToken);
        if (!isLinked)
        {
            return SyncStatus.NotLinked;
        }

        var externalId = await syncProvider.GetExternalIdAsync(entityId, cancellationToken);
        if (string.IsNullOrEmpty(externalId))
        {
            return SyncStatus.NotLinked;
        }

        var platformAchievementId = definition.PlatformIds?.GetValueOrDefault(platform);
        if (string.IsNullOrEmpty(platformAchievementId))
        {
            var missingIdMessage = $"Missing platform achievement ID for {platform} achievement {achievementId}";
            _logger.LogError(
                "Missing platform achievement ID for {Platform} achievement {AchievementId}",
                platform, achievementId);
            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "SyncAchievementToPlatform",
                "platform_sync_missing_platform_id",
                missingIdMessage,
                dependency: platform.ToString().ToLowerInvariant(),
                endpoint: "post:/achievement/unlock",
                details: $"achievementId:{achievementId};entityId:{entityId}",
                stack: null,
                cancellationToken: cancellationToken);
            await PublishPlatformSyncEventAsync(
                gameServiceId,
                achievementId,
                entityId,
                platform,
                platformAchievementId: null,
                new PlatformSyncResult { Success = false, ErrorMessage = missingIdMessage },
                cancellationToken);
            return SyncStatus.Failed;
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
                    platform, achievementId);
            }
            else
            {
                _logger.LogError("Platform sync failed for {Platform} achievement {AchievementId}: {ErrorMessage}",
                    platform, achievementId, result.ErrorMessage);
            }

            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "SyncAchievementToPlatform",
                "platform_sync_failed",
                result.ErrorMessage ?? "Platform sync failed",
                dependency: platform.ToString().ToLowerInvariant(),
                endpoint: "post:/achievement/unlock",
                details: $"achievementId:{achievementId};entityId:{entityId}",
                stack: exception?.StackTrace,
                cancellationToken: cancellationToken);
        }

        await PublishPlatformSyncEventAsync(
            gameServiceId,
            achievementId,
            entityId,
            platform,
            platformAchievementId,
            result,
            cancellationToken);

        return result.Success ? SyncStatus.Synced : SyncStatus.Failed;
    }

    /// <summary>
    /// Attempts to unlock an achievement on a platform with configurable retry logic.
    /// Returns success on first successful attempt, or the last failure result after all retries exhausted.
    /// </summary>
    /// <param name="syncProvider">The platform sync provider to use for the unlock call.</param>
    /// <param name="externalId">The external platform user ID.</param>
    /// <param name="platformAchievementId">The platform-specific achievement identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of the sync result and any exception from the last failed attempt.</returns>
    private async Task<(PlatformSyncResult result, Exception? exception)> ExecutePlatformUnlockWithRetriesAsync(
        IPlatformAchievementSync syncProvider,
        string externalId,
        string platformAchievementId,
        CancellationToken cancellationToken)
    {
        if (_configuration.MockPlatformSync)
        {
            _logger.LogInformation(
                "Mock mode enabled - returning success without API call for {Platform} achievement {AchievementId}",
                syncProvider.Platform,
                platformAchievementId);

            var mockResult = new PlatformSyncResult
            {
                Success = true,
                SyncId = $"mock-{Guid.NewGuid():N}"
            };
            return (mockResult, null);
        }

        var attempts = Math.Max(1, _configuration.SyncRetryAttempts);
        var delaySeconds = Math.Max(0, _configuration.SyncRetryDelaySeconds);
        PlatformSyncResult? lastResult = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var result = await syncProvider.UnlockAsync(externalId, platformAchievementId, cancellationToken);
                if (result.Success)
                {
                    return (result, null);
                }

                lastResult = result;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (attempt < attempts && delaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        var fallbackResult = lastResult ?? new PlatformSyncResult
        {
            Success = false,
            ErrorMessage = lastException?.Message ?? "Platform sync failed without error detail"
        };

        return (fallbackResult, lastException);
    }

    /// <summary>
    /// Publishes an achievement platform sync event to record the result of a sync attempt.
    /// </summary>
    /// <param name="gameServiceId">The game service ID.</param>
    /// <param name="achievementId">The achievement identifier.</param>
    /// <param name="entityId">The entity ID that was synced.</param>
    /// <param name="platform">The target platform.</param>
    /// <param name="platformAchievementId">The platform-specific achievement ID, or null if lookup failed.</param>
    /// <param name="result">The sync result containing success/failure details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishPlatformSyncEventAsync(
        Guid gameServiceId,
        string achievementId,
        Guid entityId,
        Platform platform,
        string? platformAchievementId,
        PlatformSyncResult result,
        CancellationToken cancellationToken)
    {
        var syncEvent = new AchievementPlatformSyncedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = gameServiceId,
            AchievementId = achievementId,
            EntityId = entityId,
            EntityType = EntityType.Account, // Platform sync is account-level
            Platform = platform,
            PlatformAchievementId = platformAchievementId,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage
        };
        await _messageBus.TryPublishAsync("achievement.platform.synced", syncEvent, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Maps internal data to response model.
    /// </summary>
    private static AchievementDefinitionResponse MapToResponse(AchievementDefinitionData definition, long earnedCount)
        => new AchievementDefinitionResponse
        {
            GameServiceId = definition.GameServiceId,
            AchievementId = definition.AchievementId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            HiddenDescription = definition.HiddenDescription,
            AchievementType = definition.AchievementType,
            EntityTypes = definition.EntityTypes ?? new List<EntityType>(),
            ProgressTarget = definition.ProgressTarget,
            Points = definition.Points,
            IconUrl = definition.IconUrl,
            Platforms = definition.Platforms ?? new List<Platform>(),
            PlatformIds = definition.PlatformIds?.ToDictionary(
                kvp => kvp.Key.ToString().ToLowerInvariant(),
                kvp => kvp.Value),
            Prerequisites = definition.Prerequisites,
            IsActive = definition.IsActive,
            EarnedCount = earnedCount,
            CreatedAt = definition.CreatedAt,
            Metadata = definition.Metadata
        };

    /// <summary>
    /// Maps progress data to response model.
    /// </summary>
    private static AchievementProgress MapToAchievementProgress(string achievementId, AchievementProgressData data)
        => new AchievementProgress
        {
            AchievementId = achievementId,
            DisplayName = data.DisplayName,
            CurrentProgress = data.CurrentProgress,
            TargetProgress = data.TargetProgress,
            PercentComplete = data.TargetProgress > 0 ? (double)data.CurrentProgress / data.TargetProgress * 100.0 : 0,
            IsUnlocked = data.IsUnlocked,
            UnlockedAt = data.UnlockedAt
        };

    #endregion

    #region Definition Lifecycle Event Publishing

    /// <summary>
    /// Publishes an event when a new achievement definition is created.
    /// </summary>
    private async Task PublishDefinitionCreatedEventAsync(AchievementDefinitionData definition, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new AchievementDefinitionCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = definition.GameServiceId,
                AchievementId = definition.AchievementId,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                AchievementType = definition.AchievementType,
                Points = definition.Points,
                ProgressTarget = definition.ProgressTarget,
                Platforms = definition.Platforms?.ToList() ?? new List<Platform>()
            };
            await _messageBus.TryPublishAsync("achievement.definition.created", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published achievement.definition.created event for {AchievementId}", definition.AchievementId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish achievement.definition.created event for {AchievementId}", definition.AchievementId);
        }
    }

    /// <summary>
    /// Publishes an event when an achievement definition is updated.
    /// </summary>
    private async Task PublishDefinitionUpdatedEventAsync(AchievementDefinitionData definition, List<string> changedFields, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new AchievementDefinitionUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = definition.GameServiceId,
                AchievementId = definition.AchievementId,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Points = definition.Points,
                ProgressTarget = definition.ProgressTarget
            };
            await _messageBus.TryPublishAsync("achievement.definition.updated", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published achievement.definition.updated event for {AchievementId} (changed: {ChangedFields})",
                definition.AchievementId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish achievement.definition.updated event for {AchievementId}", definition.AchievementId);
        }
    }

    /// <summary>
    /// Publishes an event when an achievement definition is deleted.
    /// </summary>
    private async Task PublishDefinitionDeletedEventAsync(AchievementDefinitionData definition, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new AchievementDefinitionDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = definition.GameServiceId,
                AchievementId = definition.AchievementId,
                DisplayName = definition.DisplayName
            };
            await _messageBus.TryPublishAsync("achievement.definition.deleted", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published achievement.definition.deleted event for {AchievementId}", definition.AchievementId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish achievement.definition.deleted event for {AchievementId}", definition.AchievementId);
        }
    }

    #endregion

    #region Permission Registration

    #endregion
}
