using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
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

        // Character history events
        eventConsumer.RegisterHandler<IAnalyticsService, CharacterParticipationRecordedEvent>(
            "character-history.participation.recorded",
            async (svc, evt) => await ((AnalyticsService)svc).HandleCharacterParticipationRecordedAsync(evt));

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

    }

    /// <summary>
    /// Handles game-session.action.performed events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameActionPerformedAsync(GameSessionActionPerformedEvent evt)
    {
        try
        {
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.SessionId,
                EntityType = EntityType.Custom,
                EventType = evt.ActionType,
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
            var gameServiceId = await ResolveGameServiceIdAsync(evt.GameType, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

            await SaveGameSessionMappingAsync(
                evt.SessionId,
                evt.GameType,
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.SessionId,
                EntityType = EntityType.Custom,
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
            await RemoveGameSessionMappingAsync(evt.SessionId, CancellationToken.None);

            var gameServiceId = await ResolveGameServiceIdAsync(evt.GameType, CancellationToken.None);
            if (!gameServiceId.HasValue)
            {
                return;
            }

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

            if (evt.DeletedReason != null)
            {
                metadata["deletedReason"] = evt.DeletedReason;
            }

            metadata["createdAt"] = evt.CreatedAt;

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                GameServiceId = gameServiceId.Value,
                EntityId = evt.SessionId,
                EntityType = EntityType.Custom,
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
    /// Handles character-history.participation.recorded events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCharacterParticipationRecordedAsync(CharacterParticipationRecordedEvent evt)
    {
        try
        {
            _logger.LogDebug(
                "Buffering character participation recorded event for character {CharacterId}",
                evt.CharacterId);

            var gameServiceId = await ResolveGameServiceIdForCharacterAsync(evt.CharacterId, CancellationToken.None);
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.CharacterId,
                EntityType = EntityType.Character,
                EventType = "history.participation.recorded",
                Timestamp = evt.Timestamp,
                Value = 1,
                SessionId = null,
                Metadata = metadata
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling character-history.participation.recorded event for character {CharacterId}", evt.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "HandleCharacterParticipationRecorded",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:character-history.participation.recorded",
                details: $"characterId:{evt.CharacterId}",
                stack: ex.StackTrace,
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
                GameServiceId = gameServiceId.Value,
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
                ["elementCount"] = evt.ElementCount
            };

            if (evt.ReplaceExisting.HasValue)
            {
                metadata["replaceExisting"] = evt.ReplaceExisting.Value;
            }

            await BufferAnalyticsEventAsync(new BufferedAnalyticsEvent
            {
                EventId = evt.EventId,
                GameServiceId = gameServiceId.Value,
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.RealmId,
                EntityType = EntityType.Custom, // Realm is not in EntityType enum
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.RealmId,
                EntityType = EntityType.Custom, // Realm is not in EntityType enum
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
                GameServiceId = gameServiceId.Value,
                EntityId = evt.RealmId,
                EntityType = EntityType.Custom, // Realm is not in EntityType enum
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
}
