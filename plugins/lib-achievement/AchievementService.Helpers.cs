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
using System.Diagnostics;
using System.Linq;

namespace BeyondImmersion.BannouService.Achievement;

// =============================================================================
// AchievementService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AchievementService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AchievementService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAchievementService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AchievementService.Helpers.cs):
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
/// Private and internal helper methods for AchievementService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AchievementService
{
    /// <summary>
    /// Increments the EarnedCount on an achievement definition with optimistic concurrency retry.
    /// Retries up to 3 times on ETag conflict to prevent silent count loss when multiple
    /// entities unlock the same achievement concurrently.
    /// </summary>
    /// <param name="defKey">The definition key to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task IncrementEarnedCountAsync(
        string defKey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.IncrementEarnedCountAsync");
        var maxAttempts = Math.Max(1, _configuration.EarnedCountRetryAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (freshDef, defEtag) = await _definitionStore.GetWithETagAsync(defKey, cancellationToken);
            if (freshDef == null)
            {
                _logger.LogWarning("Definition {DefKey} not found during EarnedCount increment", defKey);
                return;
            }

            freshDef.EarnedCount++;
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var savedEtag = await _definitionStore.TrySaveAsync(defKey, freshDef, defEtag ?? string.Empty, cancellationToken: cancellationToken);
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
    /// Records a platform sync outcome (success or failure) to the sync tracking store.
    /// Uses ETag-based optimistic concurrency with configurable retry.
    /// </summary>
    private async Task RecordSyncOutcomeAsync(
        Guid gameServiceId,
        Guid entityId,
        Platform platform,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.RecordSyncOutcomeAsync");
        var key = BuildSyncTrackingKey(gameServiceId, entityId, platform);
        var maxAttempts = Math.Max(1, _configuration.SyncStatusRetryAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (existing, etag) = await _syncStore.GetWithETagAsync(key, cancellationToken);
            var tracking = existing ?? new PlatformSyncTrackingData();

            if (success)
            {
                tracking.SyncedCount++;
                tracking.LastSyncAt = DateTimeOffset.UtcNow;
                tracking.LastError = null;
            }
            else
            {
                tracking.FailedCount++;
                tracking.LastError = errorMessage;
            }

            if (existing == null)
            {
                await _syncStore.SaveAsync(key, tracking,
                    _configuration.SyncHistoryTtlSeconds > 0
                        ? new StateOptions { Ttl = _configuration.SyncHistoryTtlSeconds }
                        : null,
                    cancellationToken);
                return;
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var savedEtag = await _syncStore.TrySaveAsync(key, tracking, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (savedEtag != null)
            {
                return;
            }

            _logger.LogDebug(
                "Sync tracking update conflict on {Key}, attempt {Attempt}/{MaxAttempts}",
                key, attempt, maxAttempts);
        }

        _logger.LogWarning(
            "Sync tracking update failed after {MaxAttempts} attempts for {GameServiceId}:{EntityId}:{Platform}",
            maxAttempts, gameServiceId, entityId, platform);
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
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.PublishUnlockEventAsync");
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
        await _messageBus.PublishAchievementUnlockedAsync(unlockEvent, cancellationToken);

        // Push client event to unlocking entity's WebSocket sessions
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            entityType.ToString().ToLowerInvariant(), entityId,
            new AchievementUnlockedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameServiceId = gameServiceId,
                AchievementId = definition.AchievementId,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Points = definition.Points,
                TotalPoints = totalPoints,
                IconUrl = definition.IconUrl,
                IsRare = isRare,
                Rarity = rarityPercent
            },
            cancellationToken);
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
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.SyncAchievementToPlatformAsync");
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
            await RecordSyncOutcomeAsync(gameServiceId, entityId, platform, false, providerMissingMessage, cancellationToken);
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

        var platformAchievementId = definition.PlatformMappings?.FirstOrDefault(m => m.Platform == platform)?.PlatformAchievementId;
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
            await RecordSyncOutcomeAsync(gameServiceId, entityId, platform, false, missingIdMessage, cancellationToken);
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
        await RecordSyncOutcomeAsync(gameServiceId, entityId, platform, result.Success, result.ErrorMessage, cancellationToken);

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
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.ExecutePlatformUnlockWithRetriesAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.PublishPlatformSyncEventAsync");
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
        await _messageBus.PublishAchievementPlatformSyncedAsync(syncEvent, cancellationToken);
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
            Category = definition.Category,
            EntityTypes = definition.EntityTypes ?? new List<EntityType>(),
            ProgressTarget = definition.ProgressTarget,
            Points = definition.Points,
            IconUrl = definition.IconUrl,
            Platforms = definition.Platforms ?? new List<Platform>(),
            PlatformMappings = definition.PlatformMappings?.Select(m => new PlatformMapping
            {
                Platform = m.Platform,
                PlatformAchievementId = m.PlatformAchievementId
            }).ToList(),
            Prerequisites = definition.Prerequisites,
            ScoreType = definition.ScoreType,
            MilestoneType = definition.MilestoneType,
            MilestoneValue = definition.MilestoneValue,
            MilestoneName = definition.MilestoneName,
            LeaderboardId = definition.LeaderboardId,
            RankThreshold = definition.RankThreshold,
            IsActive = definition.IsActive,
            IsDeprecated = definition.IsDeprecated,
            DeprecatedAt = definition.DeprecatedAt,
            DeprecationReason = definition.DeprecationReason,
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
    /// <summary>
    /// Publishes an event when a new achievement definition is created.
    /// Populates all model fields per lifecycle event contract.
    /// </summary>
    private async Task PublishDefinitionCreatedEventAsync(AchievementDefinitionData definition, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.PublishDefinitionCreatedEventAsync");
        var eventModel = new AchievementDefinitionCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = "achievement.definition.created",
            GameServiceId = definition.GameServiceId,
            AchievementId = definition.AchievementId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            HiddenDescription = definition.HiddenDescription,
            AchievementType = definition.AchievementType,
            Category = definition.Category,
            EntityTypes = definition.EntityTypes?.ToList(),
            ProgressTarget = definition.ProgressTarget,
            Points = definition.Points,
            IconUrl = definition.IconUrl,
            Platforms = definition.Platforms?.ToList(),
            PlatformMappings = definition.PlatformMappings?.Select(m => new PlatformMapping
            {
                Platform = m.Platform,
                PlatformAchievementId = m.PlatformAchievementId
            }).ToList(),
            Prerequisites = definition.Prerequisites?.ToList(),
            ScoreType = definition.ScoreType,
            MilestoneType = definition.MilestoneType,
            MilestoneValue = definition.MilestoneValue,
            MilestoneName = definition.MilestoneName,
            LeaderboardId = definition.LeaderboardId,
            RankThreshold = definition.RankThreshold,
            IsActive = definition.IsActive,
            IsDeprecated = definition.IsDeprecated,
            DeprecatedAt = definition.DeprecatedAt,
            DeprecationReason = definition.DeprecationReason,
            EarnedCount = definition.EarnedCount,
            CreatedAt = definition.CreatedAt,
            Metadata = definition.Metadata
        };
        await _messageBus.PublishAchievementDefinitionCreatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published achievement.definition.created event for {AchievementId}", definition.AchievementId);
    }

    /// <summary>
    /// Publishes an event when an achievement definition is updated.
    /// Populates all model fields and changedFields per lifecycle event contract.
    /// </summary>
    private async Task PublishDefinitionUpdatedEventAsync(AchievementDefinitionData definition, List<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.PublishDefinitionUpdatedEventAsync");
        var eventModel = new AchievementDefinitionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EventName = "achievement.definition.updated",
            GameServiceId = definition.GameServiceId,
            AchievementId = definition.AchievementId,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            HiddenDescription = definition.HiddenDescription,
            AchievementType = definition.AchievementType,
            Category = definition.Category,
            EntityTypes = definition.EntityTypes?.ToList(),
            ProgressTarget = definition.ProgressTarget,
            Points = definition.Points,
            IconUrl = definition.IconUrl,
            Platforms = definition.Platforms?.ToList(),
            PlatformMappings = definition.PlatformMappings?.Select(m => new PlatformMapping
            {
                Platform = m.Platform,
                PlatformAchievementId = m.PlatformAchievementId
            }).ToList(),
            Prerequisites = definition.Prerequisites?.ToList(),
            ScoreType = definition.ScoreType,
            MilestoneType = definition.MilestoneType,
            MilestoneValue = definition.MilestoneValue,
            MilestoneName = definition.MilestoneName,
            LeaderboardId = definition.LeaderboardId,
            RankThreshold = definition.RankThreshold,
            IsActive = definition.IsActive,
            IsDeprecated = definition.IsDeprecated,
            DeprecatedAt = definition.DeprecatedAt,
            DeprecationReason = definition.DeprecationReason,
            EarnedCount = definition.EarnedCount,
            CreatedAt = definition.CreatedAt,
            Metadata = definition.Metadata,
            ChangedFields = changedFields
        };
        await _messageBus.PublishAchievementDefinitionUpdatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published achievement.definition.updated event for {AchievementId} (changed: {ChangedFields})",
            definition.AchievementId, string.Join(", ", changedFields));
    }
}
