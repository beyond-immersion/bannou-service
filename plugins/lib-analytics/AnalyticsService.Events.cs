using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Partial class for AnalyticsService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class AnalyticsService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionActionPerformedEvent>(
            "game-session.action.performed",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameActionPerformedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionCreatedEvent>(
            "game-session.created",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameSessionCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionDeletedEvent>(
            "game-session.deleted",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameSessionDeletedAsync(evt));

        // Character history events (batch lifecycle)
        eventConsumer.RegisterHandler<IAnalyticsService, ParticipationBatchCreatedEvent>(
            "character-history.participation.batch-created",
            async (svc, evt) => await ((AnalyticsService)svc).HandleCharacterParticipationBatchCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, CharacterBackstoryCreatedEvent>(
            "character-history.backstory.created",
            async (svc, evt) => await ((AnalyticsService)svc).HandleCharacterBackstoryCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, CharacterBackstoryUpdatedEvent>(
            "character-history.backstory.updated",
            async (svc, evt) => await ((AnalyticsService)svc).HandleCharacterBackstoryUpdatedAsync(evt));

        // Realm history events
        eventConsumer.RegisterHandler<IAnalyticsService, RealmParticipationRecordedEvent>(
            "realm-history.participation.recorded",
            async (svc, evt) => await ((AnalyticsService)svc).HandleRealmParticipationRecordedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, RealmLoreCreatedEvent>(
            "realm-history.lore.created",
            async (svc, evt) => await ((AnalyticsService)svc).HandleRealmLoreCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, RealmLoreUpdatedEvent>(
            "realm-history.lore.updated",
            async (svc, evt) => await ((AnalyticsService)svc).HandleRealmLoreUpdatedAsync(evt));

        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation)
        eventConsumer.RegisterHandler<IAnalyticsService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAccountDeletedAsync(evt));

        // Auth audit events for security monitoring (#142 Phase 1)
        eventConsumer.RegisterHandler<IAnalyticsService, AuthLoginSuccessfulEvent>(
            "auth.login.successful",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthLoginSuccessfulAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthLoginFailedEvent>(
            "auth.login.failed",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthLoginFailedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthRegistrationSuccessfulEvent>(
            "auth.registration.successful",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthRegistrationSuccessfulAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthOAuthLoginSuccessfulEvent>(
            "auth.oauth.successful",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthOAuthSuccessfulAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthSteamLoginSuccessfulEvent>(
            "auth.steam.successful",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthSteamSuccessfulAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthPasswordResetSuccessfulEvent>(
            "auth.password-reset.successful",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthPasswordResetSuccessfulAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthMfaEnabledEvent>(
            "auth.mfa.enabled",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthMfaEnabledAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthMfaDisabledEvent>(
            "auth.mfa.disabled",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthMfaDisabledAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthMfaVerifiedEvent>(
            "auth.mfa.verified",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthMfaVerifiedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, AuthMfaFailedEvent>(
            "auth.mfa.failed",
            async (svc, evt) => await ((AnalyticsService)svc).HandleAuthMfaFailedAsync(evt));

        // Cache invalidation events
        eventConsumer.RegisterHandler<IAnalyticsService, CharacterUpdatedEvent>(
            "character.updated",
            async (svc, evt) => await ((AnalyticsService)svc).HandleCharacterUpdatedForCacheInvalidationAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, RealmUpdatedEvent>(
            "realm.updated",
            async (svc, evt) => await ((AnalyticsService)svc).HandleRealmUpdatedForCacheInvalidationAsync(evt));
    }

    /// <summary>
    /// Handles game-session.action.performed events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameActionPerformedAsync(GameSessionActionPerformedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleGameActionPerformedAsync");
            var gameServiceId = await ResolveGameServiceIdForSessionAsync(evt.SessionId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["actionId"] = evt.ActionId,
                ["actionType"] = evt.ActionType
            };

            if (evt.TargetId.HasValue)
            {
                metadata["targetId"] = evt.TargetId.Value;
            }

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.SessionId,
                EntityType = EntityType.Other,
                EventType = evt.ActionType.ToString(),
                Timestamp = evt.Timestamp,
                Value = 1,
                SessionId = evt.SessionId,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game-session.action.performed event for session {SessionId}", evt.SessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleGameActionPerformed",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:game-session.action.performed",
                details: $"sessionId:{evt.SessionId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles game-session.created events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameSessionCreatedAsync(GameSessionCreatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleGameSessionCreatedAsync");
            var gameServiceId = await ResolveGameServiceIdAsync(evt.GameType.ToString(), CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            await SaveGameSessionMappingAsync(
                evt.SessionId,
                evt.GameType.ToString(),
                gameServiceId.Value,
                CancellationToken.None);

            var metadata = new Dictionary<string, object>
            {
                ["gameType"] = evt.GameType,
                ["status"] = evt.Status
            };

            if (evt.SessionName != null)
            {
                metadata["sessionName"] = evt.SessionName;
            }

            if (evt.MaxPlayers.HasValue)
            {
                metadata["maxPlayers"] = evt.MaxPlayers.Value;
            }

            if (evt.CurrentPlayers.HasValue)
            {
                metadata["currentPlayers"] = evt.CurrentPlayers.Value;
            }

            if (evt.IsPrivate.HasValue)
            {
                metadata["isPrivate"] = evt.IsPrivate.Value;
            }

            if (evt.Owner.HasValue)
            {
                metadata["owner"] = evt.Owner.Value;
            }

            metadata["createdAt"] = evt.CreatedAt;

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.SessionId,
                EntityType = EntityType.Other,
                EventType = "session.created",
                Timestamp = evt.Timestamp,
                Value = 1,
                SessionId = evt.SessionId,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game-session.created event for session {SessionId}", evt.SessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleGameSessionCreated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:game-session.created",
                details: $"sessionId:{evt.SessionId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles game-session.deleted events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameSessionDeletedAsync(GameSessionDeletedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleGameSessionDeletedAsync");
            await RemoveGameSessionMappingAsync(evt.SessionId, CancellationToken.None);

            var gameServiceId = await ResolveGameServiceIdAsync(evt.GameType.ToString(), CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["gameType"] = evt.GameType.ToString(),
                ["status"] = evt.Status.ToString()
            };

            if (evt.SessionName != null)
            {
                metadata["sessionName"] = evt.SessionName;
            }

            if (evt.MaxPlayers.HasValue)
            {
                metadata["maxPlayers"] = evt.MaxPlayers.Value;
            }

            if (evt.CurrentPlayers.HasValue)
            {
                metadata["currentPlayers"] = evt.CurrentPlayers.Value;
            }

            if (evt.IsPrivate.HasValue)
            {
                metadata["isPrivate"] = evt.IsPrivate.Value;
            }

            if (evt.Owner.HasValue)
            {
                metadata["owner"] = evt.Owner.Value;
            }

            if (evt.DeletedReason != null)
            {
                metadata["deletedReason"] = evt.DeletedReason;
            }

            metadata["createdAt"] = evt.CreatedAt;

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.SessionId,
                EntityType = EntityType.Other,
                EventType = "session.deleted",
                Timestamp = evt.Timestamp,
                Value = 1,
                SessionId = evt.SessionId,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game-session.deleted event for session {SessionId}", evt.SessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleGameSessionDeleted",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:game-session.deleted",
                details: $"sessionId:{evt.SessionId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles character-history.participation.batch-created events.
    /// Processes each entry in the batch with per-item error isolation per IMPLEMENTATION TENETS.
    /// </summary>
    /// <param name="evt">The batch event data containing accumulated participation recordings.</param>
    public async Task HandleCharacterParticipationBatchCreatedAsync(ParticipationBatchCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleCharacterParticipationBatchCreatedAsync");
        _logger.LogDebug("Processing participation batch-created event with {Count} entries", evt.Count);

        var successCount = 0;
        var failureCount = 0;
        foreach (var entry in evt.Entries)
        {
            try
            {
                var gameServiceId = await ResolveGameServiceIdForCharacterAsync(entry.CharacterId, CancellationToken.None);
                if (!gameServiceId.HasValue)
                {
                    continue;
                }

                var metadata = new Dictionary<string, object>
                {
                    ["participationId"] = entry.ParticipationId,
                    ["historicalEventId"] = entry.HistoricalEventId,
                    ["role"] = entry.Role
                };

                await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
                {
                    EventId = Guid.NewGuid(),
                    ServiceType = AnalyticsServiceType.Game,
                    ServiceId = gameServiceId.Value.ToString(),
                    EntityId = entry.CharacterId,
                    EntityType = EntityType.Character,
                    EventType = "history.participation.recorded",
                    Timestamp = entry.CreatedAt,
                    Value = 1,
                    SessionId = null,
                    Metadata = metadata
                }, CancellationToken.None);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process participation entry for character {CharacterId}, continuing", entry.CharacterId);
                failureCount++;
            }
        }

        if (failureCount > 0)
        {
            _logger.LogWarning("Participation batch processing: {Success} succeeded, {Failed} failed", successCount, failureCount);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleCharacterParticipationBatchCreated",
                "partial_batch_failure",
                $"{failureCount} of {evt.Count} entries failed",
                dependency: null,
                endpoint: "event:character-history.participation.batch-created",
                details: $"success:{successCount},failed:{failureCount}",
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles character-history.backstory.created events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCharacterBackstoryCreatedAsync(CharacterBackstoryCreatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleCharacterBackstoryCreatedAsync");
            _logger.LogDebug(
                "Buffering character backstory created event for character {CharacterId}",
                evt.CharacterId);

            var gameServiceId = await ResolveGameServiceIdForCharacterAsync(evt.CharacterId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["elementCount"] = evt.ElementCount
            };

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.CharacterId,
                EntityType = EntityType.Character,
                EventType = "history.backstory.created",
                Timestamp = evt.Timestamp,
                Value = evt.ElementCount,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling character-history.backstory.created event for character {CharacterId}", evt.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleCharacterBackstoryCreated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:character-history.backstory.created",
                details: $"characterId:{evt.CharacterId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles character-history.backstory.updated events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCharacterBackstoryUpdatedAsync(CharacterBackstoryUpdatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleCharacterBackstoryUpdatedAsync");
            _logger.LogDebug(
                "Buffering character backstory updated event for character {CharacterId}",
                evt.CharacterId);

            var gameServiceId = await ResolveGameServiceIdForCharacterAsync(evt.CharacterId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["elementCount"] = evt.ElementCount,
                ["replaceExisting"] = evt.ReplaceExisting
            };

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.CharacterId,
                EntityType = EntityType.Character,
                EventType = "history.backstory.updated",
                Timestamp = evt.Timestamp,
                Value = evt.ElementCount,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling character-history.backstory.updated event for character {CharacterId}", evt.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleCharacterBackstoryUpdated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:character-history.backstory.updated",
                details: $"characterId:{evt.CharacterId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles realm-history.participation.recorded events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmParticipationRecordedAsync(RealmParticipationRecordedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleRealmParticipationRecordedAsync");
            _logger.LogDebug(
                "Buffering realm participation recorded event for realm {RealmId}",
                evt.RealmId);

            var gameServiceId = await ResolveGameServiceIdForRealmAsync(evt.RealmId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["participationId"] = evt.ParticipationId,
                ["historicalEventId"] = evt.HistoricalEventId,
                ["role"] = evt.Role
            };

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.RealmId,
                EntityType = EntityType.Realm,
                EventType = "history.participation.recorded",
                Timestamp = evt.Timestamp,
                Value = 1,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling realm-history.participation.recorded event for realm {RealmId}", evt.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleRealmParticipationRecorded",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:realm-history.participation.recorded",
                details: $"realmId:{evt.RealmId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles realm-history.lore.created events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmLoreCreatedAsync(RealmLoreCreatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleRealmLoreCreatedAsync");
            _logger.LogDebug(
                "Buffering realm lore created event for realm {RealmId}",
                evt.RealmId);

            var gameServiceId = await ResolveGameServiceIdForRealmAsync(evt.RealmId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["elementCount"] = evt.ElementCount
            };

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.RealmId,
                EntityType = EntityType.Realm,
                EventType = "history.lore.created",
                Timestamp = evt.Timestamp,
                Value = evt.ElementCount,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling realm-history.lore.created event for realm {RealmId}", evt.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleRealmLoreCreated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:realm-history.lore.created",
                details: $"realmId:{evt.RealmId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles realm-history.lore.updated events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmLoreUpdatedAsync(RealmLoreUpdatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleRealmLoreUpdatedAsync");
            _logger.LogDebug(
                "Buffering realm lore updated event for realm {RealmId}",
                evt.RealmId);

            var gameServiceId = await ResolveGameServiceIdForRealmAsync(evt.RealmId, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            var metadata = new Dictionary<string, object>
            {
                ["elementCount"] = evt.ElementCount
            };

            if (evt.ReplaceExisting.HasValue)
            {
                metadata["replaceExisting"] = evt.ReplaceExisting.Value;
            }

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                ServiceType = AnalyticsServiceType.Game,
                ServiceId = gameServiceId.Value.ToString(),
                EntityId = evt.RealmId,
                EntityType = EntityType.Realm,
                EventType = "history.lore.updated",
                Timestamp = evt.Timestamp,
                Value = evt.ElementCount,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling realm-history.lore.updated event for realm {RealmId}", evt.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleRealmLoreUpdated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:realm-history.lore.updated",
                details: $"realmId:{evt.RealmId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned analytics data.
    /// Deletes controller history records, entity summaries, and skill ratings for the account.
    /// Wraps cleanup in try-catch since event handlers have no generated controller boundary.
    /// </summary>
    /// <param name="evt">The account deleted event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        try
        {
            await CleanupDataForAccountAsync(evt.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up analytics data for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "CleanupDataForAccount",
                ex.GetType().Name,
                ex.Message,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all analytics data for a given account.
    /// Deletes controller history records (MySQL), entity summaries where entityType=Account (MySQL),
    /// and skill ratings where entityType=Account (Redis).
    /// Per-item failures are logged as warnings and do not abort overall cleanup.
    /// </summary>
    /// <param name="accountId">The account whose data should be cleaned up.</param>
    internal async Task CleanupDataForAccountAsync(Guid accountId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.CleanupDataForAccount");

        // 1. Delete controller history records for this account
        var historyDeletedCount = 0;
        var historyFailedCount = 0;
        var historyConditions = new List<QueryCondition>
        {
            new QueryCondition
            {
                Path = "$.AccountId",
                Operator = QueryOperator.Equals,
                Value = accountId.ToString()
            }
        };

        // Query and delete in batches
        while (true)
        {
            var batch = await _historyDataQueryStore.JsonQueryPagedAsync(
                historyConditions, 0, 100, null, CancellationToken.None);

            if (batch.Items.Count == 0)
                break;

            foreach (var item in batch.Items)
            {
                try
                {
                    await _historyDataStore.DeleteAsync(item.Key, CancellationToken.None);
                    historyDeletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete controller history record {Key} for account {AccountId}", item.Key, accountId);
                    historyFailedCount++;
                }
            }
        }

        if (historyDeletedCount > 0 || historyFailedCount > 0)
        {
            _logger.LogInformation(
                "Controller history cleanup for account {AccountId}: {Deleted} deleted, {Failed} failed",
                accountId, historyDeletedCount, historyFailedCount);
        }

        // 2. Delete entity summaries where entityType=Account
        var summaryDeletedCount = 0;
        var summaryFailedCount = 0;
        var summaryConditions = new List<QueryCondition>
        {
            new QueryCondition
            {
                Path = "$.EntityType",
                Operator = QueryOperator.Equals,
                Value = EntityType.Account.ToString()
            },
            new QueryCondition
            {
                Path = "$.EntityId",
                Operator = QueryOperator.Equals,
                Value = accountId.ToString()
            }
        };

        var summaryBatch = await _summaryDataQueryStore.JsonQueryPagedAsync(
            summaryConditions, 0, 1000, null, CancellationToken.None);

        foreach (var item in summaryBatch.Items)
        {
            try
            {
                await _summaryDataStore.DeleteAsync(item.Key, CancellationToken.None);
                summaryDeletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete summary {Key} for account {AccountId}", item.Key, accountId);
                summaryFailedCount++;
            }
        }

        if (summaryDeletedCount > 0 || summaryFailedCount > 0)
        {
            _logger.LogInformation(
                "Summary cleanup for account {AccountId}: {Deleted} deleted, {Failed} failed",
                accountId, summaryDeletedCount, summaryFailedCount);
        }

        // 3. Delete skill ratings where entityType=Account via reverse index
        var ratingDeletedCount = 0;
        var ratingFailedCount = 0;
        var ratingIndexKey = BuildAccountRatingIndexKey(accountId);
        var ratingIndex = await _ratingIndexStore.GetAsync(ratingIndexKey, CancellationToken.None);
        if (ratingIndex != null)
        {
            List<string> ratingKeys;
            try
            {
                ratingKeys = BannouJson.Deserialize<List<string>>(ratingIndex) ?? new List<string>();
            }
            catch
            {
                ratingKeys = new List<string>();
            }

            foreach (var ratingKey in ratingKeys)
            {
                try
                {
                    await _ratingStore.DeleteAsync(ratingKey, CancellationToken.None);
                    ratingDeletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete skill rating {Key} for account {AccountId}", ratingKey, accountId);
                    ratingFailedCount++;
                }
            }

            // Clean up the index key itself
            await _ratingIndexStore.DeleteAsync(ratingIndexKey, CancellationToken.None);
        }

        if (ratingDeletedCount > 0 || ratingFailedCount > 0)
        {
            _logger.LogInformation(
                "Skill rating cleanup for account {AccountId}: {Deleted} deleted, {Failed} failed",
                accountId, ratingDeletedCount, ratingFailedCount);
        }

        if (historyDeletedCount == 0 && summaryDeletedCount == 0 && ratingDeletedCount == 0)
        {
            _logger.LogDebug("No analytics data found for account {AccountId}", accountId);
        }
    }

    // =========================================================================
    // Auth Audit Event Handlers (#142 Phase 1)
    //
    // All auth events use serviceType: System, serviceId: "auth".
    // No game service resolution needed — System type has no resolution step.
    // Per-account entity summaries keyed by EntityType.Account + accountId.
    // =========================================================================

    private const string AUTH_SERVICE_ID = "auth";

    /// <summary>
    /// Buffers an auth audit event into the analytics pipeline.
    /// Shared helper for auth events with a guaranteed non-null accountId.
    /// </summary>
    private async Task BufferAuthEventAsync(Guid accountId, string eventType, DateTimeOffset timestamp)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.BufferAuthEventAsync");
        await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
        {
            EventId = Guid.NewGuid(),
            ServiceType = AnalyticsServiceType.System,
            ServiceId = AUTH_SERVICE_ID,
            EntityId = accountId,
            EntityType = EntityType.Account,
            EventType = eventType,
            Timestamp = timestamp,
            Value = 1,
            SessionId = null,
            Metadata = null
        }, CancellationToken.None);
    }

    /// <summary>
    /// Handles auth.login.successful events.
    /// </summary>
    public async Task HandleAuthLoginSuccessfulAsync(AuthLoginSuccessfulEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthLoginSuccessfulAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.login.successful", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.login.successful event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthLoginSuccessful", "unexpected_exception", ex.Message,
                endpoint: "event:auth.login.successful", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.login.failed events.
    /// Failed logins with null accountId are dropped from per-account aggregation.
    /// Phase 2 (#639) will add per-IP entity tracking for null-accountId events.
    /// </summary>
    public async Task HandleAuthLoginFailedAsync(AuthLoginFailedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthLoginFailedAsync");

            // Null accountId means the login attempt didn't match any account
            // (e.g., enumeration attempt). Drop from per-account aggregation.
            // Phase 2 (#639) will track these per-IP instead.
            if (!evt.AccountId.HasValue)
            {
                _logger.LogDebug("Dropping auth.login.failed event with null accountId (username: {Username})", evt.Username);
                return;
            }

            await BufferAuthEventAsync(evt.AccountId.Value, "auth.login.failed", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.login.failed event for username {Username}", evt.Username);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthLoginFailed", "unexpected_exception", ex.Message,
                endpoint: "event:auth.login.failed", details: $"username:{evt.Username}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.registration.successful events.
    /// </summary>
    public async Task HandleAuthRegistrationSuccessfulAsync(AuthRegistrationSuccessfulEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthRegistrationSuccessfulAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.registration.successful", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.registration.successful event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthRegistrationSuccessful", "unexpected_exception", ex.Message,
                endpoint: "event:auth.registration.successful", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.oauth.successful events.
    /// </summary>
    public async Task HandleAuthOAuthSuccessfulAsync(AuthOAuthLoginSuccessfulEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthOAuthSuccessfulAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.oauth.successful", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.oauth.successful event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthOAuthSuccessful", "unexpected_exception", ex.Message,
                endpoint: "event:auth.oauth.successful", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.steam.successful events.
    /// </summary>
    public async Task HandleAuthSteamSuccessfulAsync(AuthSteamLoginSuccessfulEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthSteamSuccessfulAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.steam.successful", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.steam.successful event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthSteamSuccessful", "unexpected_exception", ex.Message,
                endpoint: "event:auth.steam.successful", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.password-reset.successful events.
    /// </summary>
    public async Task HandleAuthPasswordResetSuccessfulAsync(AuthPasswordResetSuccessfulEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthPasswordResetSuccessfulAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.password-reset.successful", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.password-reset.successful event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthPasswordResetSuccessful", "unexpected_exception", ex.Message,
                endpoint: "event:auth.password-reset.successful", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.mfa.enabled events.
    /// </summary>
    public async Task HandleAuthMfaEnabledAsync(AuthMfaEnabledEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthMfaEnabledAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.mfa.enabled", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.mfa.enabled event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthMfaEnabled", "unexpected_exception", ex.Message,
                endpoint: "event:auth.mfa.enabled", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.mfa.disabled events.
    /// </summary>
    public async Task HandleAuthMfaDisabledAsync(AuthMfaDisabledEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthMfaDisabledAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.mfa.disabled", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.mfa.disabled event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthMfaDisabled", "unexpected_exception", ex.Message,
                endpoint: "event:auth.mfa.disabled", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.mfa.verified events.
    /// </summary>
    public async Task HandleAuthMfaVerifiedAsync(AuthMfaVerifiedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthMfaVerifiedAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.mfa.verified", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.mfa.verified event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthMfaVerified", "unexpected_exception", ex.Message,
                endpoint: "event:auth.mfa.verified", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles auth.mfa.failed events.
    /// </summary>
    public async Task HandleAuthMfaFailedAsync(AuthMfaFailedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleAuthMfaFailedAsync");
            await BufferAuthEventAsync(evt.AccountId, "auth.mfa.failed", evt.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth.mfa.failed event for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "analytics", "HandleAuthMfaFailed", "unexpected_exception", ex.Message,
                endpoint: "event:auth.mfa.failed", details: $"accountId:{evt.AccountId}",
                stack: ex.StackTrace, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles character.updated events for cache invalidation.
    /// Invalidates the character-to-realm resolution cache when a character is updated.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCharacterUpdatedForCacheInvalidationAsync(CharacterUpdatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleCharacterUpdatedForCacheInvalidationAsync");
            var cacheKey = BuildCharacterRealmCacheKey(evt.CharacterId);
            await _characterRealmCacheStore.DeleteAsync(cacheKey, CancellationToken.None);
            _logger.LogDebug(
                "Invalidated character-to-realm cache for character {CharacterId}",
                evt.CharacterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate character-to-realm cache for character {CharacterId}",
                evt.CharacterId);
            // Cache invalidation failures are non-critical - stale cache will expire via TTL
        }
    }

    /// <summary>
    /// Handles realm.updated events for cache invalidation.
    /// Invalidates the realm-to-gameService resolution cache when a realm is updated.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmUpdatedForCacheInvalidationAsync(RealmUpdatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.analytics", "AnalyticsService.HandleRealmUpdatedForCacheInvalidationAsync");
            var cacheKey = BuildRealmGameServiceCacheKey(evt.RealmId);
            await _realmGameServiceCacheStore.DeleteAsync(cacheKey, CancellationToken.None);
            _logger.LogDebug(
                "Invalidated realm-to-gameService cache for realm {RealmId}",
                evt.RealmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate realm-to-gameService cache for realm {RealmId}",
                evt.RealmId);
            // Cache invalidation failures are non-critical - stale cache will expire via TTL
        }
    }
}
