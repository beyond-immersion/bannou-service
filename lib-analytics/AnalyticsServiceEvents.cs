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
}
