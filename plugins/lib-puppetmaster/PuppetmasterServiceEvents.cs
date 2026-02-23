using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Partial class for PuppetmasterService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class PuppetmasterService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Realm lifecycle events
        eventConsumer.RegisterHandler<IPuppetmasterService, RealmCreatedEvent>(
            "realm.created",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleRealmCreatedAsync(evt));

        eventConsumer.RegisterHandler<IPuppetmasterService, RealmDeletedEvent>(
            "realm.deleted",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleRealmDeletedAsync(evt));

        eventConsumer.RegisterHandler<IPuppetmasterService, RealmUpdatedEvent>(
            "realm.updated",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleRealmUpdatedAsync(evt));

        // Behavior hot-reload: invalidate cache and notify running actors
        eventConsumer.RegisterHandler<IPuppetmasterService, BehaviorUpdatedEvent>(
            "behavior.updated",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleBehaviorUpdatedAsync(evt));

        // Actor lifecycle events for watch cleanup
        eventConsumer.RegisterHandler<IPuppetmasterService, ActorInstanceDeletedEvent>(
            "actor.instance.deleted",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleActorDeletedAsync(evt));

        // Subscribe to lifecycle events for resource watching
        SubscribeToLifecycleEvents();
    }

    /// <summary>
    /// Subscribes to all lifecycle events mapped in ResourceEventMapping.
    /// These subscriptions enable the watch system to detect resource changes.
    /// </summary>
    private void SubscribeToLifecycleEvents()
    {
        foreach (var topic in _resourceEventMapping.GetAllTopics())
        {
            _logger.LogDebug("Subscribing to lifecycle event topic: {Topic}", topic);

            // Use fire-and-forget pattern for subscription setup (runs async)
            // We use SubscribeDynamicRawAsync because SubscribeAsync<T> requires T : class,
            // and we need to parse dynamic JSON payloads to JsonElement (which is a struct).
            _ = SubscribeToLifecycleTopicAsync(topic);
        }

        _logger.LogInformation(
            "Subscribed to {Count} lifecycle event topics for resource watching",
            _resourceEventMapping.GetAllTopics().Count());
    }

    /// <summary>
    /// Creates a raw subscription to a lifecycle event topic and handles parsing.
    /// </summary>
    private async Task SubscribeToLifecycleTopicAsync(string topic)
    {
        try
        {
            // SubscribeDynamicRawAsync gives us raw bytes which we can parse to JsonElement
            await _messageSubscriber.SubscribeDynamicRawAsync(
                topic,
                async (rawBytes, ct) =>
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(rawBytes);
                        // Clone the root element since the document is disposed after this scope
                        var eventData = jsonDoc.RootElement.Clone();
                        await HandleLifecycleEventAsync(topic, eventData, ct);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to parse lifecycle event JSON for topic {Topic}",
                            topic);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to subscribe to lifecycle topic {Topic}",
                topic);
        }
    }

    /// <summary>
    /// Handles lifecycle events by dispatching watch perceptions to interested actors.
    /// </summary>
    private async Task HandleLifecycleEventAsync(
        string eventTopic,
        JsonElement eventData,
        CancellationToken ct)
    {
        // Find all source types that use this topic
        var sourceTypes = _resourceEventMapping.GetSourceTypesForTopic(eventTopic).ToList();

        foreach (var sourceType in sourceTypes)
        {
            var mapping = _resourceEventMapping.GetMapping(sourceType);
            if (mapping == null) continue;

            // Extract resource ID from event data
            if (!TryExtractResourceId(eventData, mapping.ResourceIdField, out var resourceId))
            {
                _logger.LogDebug(
                    "Could not extract {Field} from {Topic} event",
                    mapping.ResourceIdField, eventTopic);
                continue;
            }

            // Get all actors watching this resource
            var watchers = _watchRegistry.GetWatchers(mapping.ResourceType, resourceId);
            if (watchers.Count == 0) continue;

            // Create appropriate perception based on whether this is a deletion event
            WatchPerception perception;
            if (mapping.IsDeletion)
            {
                _logger.LogDebug(
                    "Dispatching deletion for {ResourceType}:{ResourceId} to {WatcherCount} actors",
                    mapping.ResourceType, resourceId, watchers.Count);

                perception = WatchPerception.ResourceDeleted(
                    mapping.ResourceType,
                    resourceId,
                    sourceType,
                    eventTopic);
            }
            else
            {
                _logger.LogDebug(
                    "Dispatching {SourceType} change for {ResourceType}:{ResourceId} to {WatcherCount} actors",
                    sourceType, mapping.ResourceType, resourceId, watchers.Count);

                perception = WatchPerception.ResourceChanged(
                    mapping.ResourceType,
                    resourceId,
                    sourceType,
                    eventTopic,
                    eventData);
            }

            foreach (var actorId in watchers)
            {
                // For deletion events, notify all watchers regardless of source filter
                // For change events, check if the actor's watch matches the source type
                if (!mapping.IsDeletion &&
                    !_watchRegistry.HasMatchingWatch(actorId, mapping.ResourceType, resourceId, sourceType))
                {
                    continue;
                }

                await InjectWatchPerceptionAsync(actorId, perception, ct);

                // For deletion events, automatically unwatch after sending final perception
                if (mapping.IsDeletion)
                {
                    _watchRegistry.RemoveWatch(actorId, mapping.ResourceType, resourceId);
                    _logger.LogDebug(
                        "Auto-unwatched {ResourceType}:{ResourceId} for actor {ActorId} after deletion",
                        mapping.ResourceType, resourceId, actorId);
                }
            }
        }
    }

    /// <summary>
    /// Injects a watch perception into an actor's bounded channel.
    /// </summary>
    private async Task InjectWatchPerceptionAsync(
        Guid actorId,
        WatchPerception perception,
        CancellationToken ct)
    {
        try
        {
            // IActorClient is L2 (GameFoundation) - must be available per FOUNDATION TENETS.
            // Resolved via scope because Puppetmaster is Singleton and IActorClient is Scoped.
            using var scope = _scopeFactory.CreateScope();
            var actorClient = scope.ServiceProvider.GetRequiredService<IActorClient>();

            var request = new InjectPerceptionRequest
            {
                ActorId = actorId.ToString(),
                Perception = new PerceptionData
                {
                    PerceptionType = perception.Type,
                    SourceId = perception.ResourceId.ToString(),
                    SourceType = PerceptionSourceType.Environment,
                    Urgency = perception.Urgency,
                    Data = perception
                }
            };

            await actorClient.InjectPerceptionAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error injecting perception to actor {ActorId}",
                actorId);
        }
    }

    /// <summary>
    /// Extracts a resource ID from event data using the configured field name.
    /// </summary>
    private static bool TryExtractResourceId(
        JsonElement eventData,
        string fieldName,
        out Guid resourceId)
    {
        resourceId = Guid.Empty;

        if (eventData.ValueKind != JsonValueKind.Object)
            return false;

        if (!eventData.TryGetProperty(fieldName, out var idElement))
            return false;

        var idString = idElement.GetString();
        return !string.IsNullOrEmpty(idString) && Guid.TryParse(idString, out resourceId);
    }

    /// <summary>
    /// Handles behavior.updated events from the Behavior service.
    /// Invalidates cached behavior documents and notifies running actors for hot-reload.
    /// </summary>
    public async Task HandleBehaviorUpdatedAsync(BehaviorUpdatedEvent evt)
    {
        _logger.LogInformation("Received behavior.updated event for {BehaviorId}", evt.BehaviorId);

        try
        {
            // Invalidate cached behavior document.
            // BehaviorDocumentCache is keyed by asset reference (GUID), not BehaviorId (string code).
            // AssetId is REQUIRED on BehaviorUpdatedEvent and matches the cache key directly.
            _behaviorCache.Invalidate(evt.AssetId);
            _logger.LogDebug("Invalidated cached behavior asset {AssetId} for {BehaviorId}", evt.AssetId, evt.BehaviorId);

            // IActorClient is L2 (GameFoundation) - must be available per FOUNDATION TENETS.
            // Resolved via scope because Puppetmaster is Singleton and IActorClient is Scoped.
            using var scope = _scopeFactory.CreateScope();
            var actorClient = scope.ServiceProvider.GetRequiredService<IActorClient>();

            // Paginate through all running actors. Behavior updates are rare admin events,
            // so sequential notification is acceptable.
            var offset = 0;
            const int pageSize = 100;
            var notifiedCount = 0;

            while (true)
            {
                var listResponse = await actorClient.ListActorsAsync(
                    new ListActorsRequest { Limit = pageSize, Offset = offset },
                    CancellationToken.None);

                if (listResponse.Actors.Count == 0)
                    break;

                foreach (var actor in listResponse.Actors)
                {
                    await InjectBehaviorUpdatePerceptionAsync(
                        actorClient, actor.ActorId, evt.BehaviorId, CancellationToken.None);
                    notifiedCount++;
                }

                offset += listResponse.Actors.Count;
                if (offset >= listResponse.Total)
                    break;
            }

            _logger.LogDebug(
                "Notified {Count} actors of behavior {BehaviorId} update",
                notifiedCount, evt.BehaviorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling behavior.updated event for {BehaviorId}", evt.BehaviorId);
            await _messageBus.TryPublishErrorAsync(
                "puppetmaster",
                "HandleBehaviorUpdated",
                ex.GetType().Name,
                ex.Message,
                details: new Dictionary<string, object?> { ["behaviorId"] = evt.BehaviorId, ["assetId"] = evt.AssetId },
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Injects a behavior update perception into an actor.
    /// </summary>
    private async Task InjectBehaviorUpdatePerceptionAsync(
        IActorClient actorClient, string actorId, string behaviorId, CancellationToken ct)
    {
        try
        {
            var request = new InjectPerceptionRequest
            {
                ActorId = actorId,
                Perception = new PerceptionData
                {
                    PerceptionType = "system",
                    SourceId = "puppetmaster",
                    SourceType = PerceptionSourceType.Service,
                    Urgency = 0.5f,
                    Data = new Dictionary<string, object?>
                    {
                        ["eventType"] = "behavior_updated",
                        ["behaviorId"] = behaviorId
                    }
                }
            };

            await actorClient.InjectPerceptionAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error injecting behavior update perception to actor {ActorId}", actorId);
        }
    }

    /// <summary>
    /// Handles actor.instance.deleted events by cleaning up all watches for that actor.
    /// </summary>
    public Task HandleActorDeletedAsync(ActorInstanceDeletedEvent evt)
    {
        if (!Guid.TryParse(evt.ActorId, out var actorId))
        {
            _logger.LogWarning(
                "Invalid actor ID in actor.instance.deleted event: {ActorId}",
                evt.ActorId);
            return Task.CompletedTask;
        }

        var removedCount = _watchRegistry.RemoveAllWatches(actorId);

        if (removedCount > 0)
        {
            _logger.LogDebug(
                "Cleaned up {Count} watches for deleted actor {ActorId}",
                removedCount, actorId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles realm.deleted events.
    /// Stops all active watchers for the deleted realm.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmDeletedAsync(RealmDeletedEvent evt)
    {
        _logger.LogInformation(
            "Received realm.deleted event for realm {RealmId} ({Code}), reason: {Reason}",
            evt.RealmId,
            evt.Code,
            evt.DeletedReason);

        var stoppedCount = await StopAllWatchersForRealmAsync(evt.RealmId, "realm_deleted");

        if (stoppedCount > 0)
        {
            _logger.LogInformation(
                "Stopped {Count} watchers for deleted realm {RealmId}",
                stoppedCount,
                evt.RealmId);
        }
    }

    /// <summary>
    /// Handles realm.updated events.
    /// Stops watchers if the realm was deactivated; restarts watchers if reactivated.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmUpdatedAsync(RealmUpdatedEvent evt)
    {
        // Only react when isActive was changed
        if (!evt.ChangedFields.Contains("isActive"))
            return;

        if (!evt.IsActive)
        {
            _logger.LogInformation(
                "Realm {RealmId} ({Code}) deactivated, stopping watchers",
                evt.RealmId,
                evt.Code);

            var stoppedCount = await StopAllWatchersForRealmAsync(evt.RealmId, "realm_deactivated");

            if (stoppedCount > 0)
            {
                _logger.LogInformation(
                    "Stopped {Count} watchers for deactivated realm {RealmId}",
                    stoppedCount,
                    evt.RealmId);
            }
        }
        else
        {
            _logger.LogInformation(
                "Realm {RealmId} ({Code}) reactivated, starting watchers",
                evt.RealmId,
                evt.Code);

            var (status, response) = await StartWatchersForRealmAsync(
                new StartWatchersForRealmRequest { RealmId = evt.RealmId },
                CancellationToken.None);

            if (status == StatusCodes.OK && response != null)
            {
                _logger.LogInformation(
                    "Started {Count} watchers for reactivated realm {RealmId}",
                    response.WatchersStarted,
                    evt.RealmId);
            }
        }
    }

    /// <summary>
    /// Stops all active watchers for a given realm and publishes stop events.
    /// </summary>
    /// <param name="realmId">The realm to stop watchers for.</param>
    /// <param name="reason">The reason for stopping (e.g., "realm_deleted", "realm_deactivated").</param>
    /// <returns>The number of watchers stopped.</returns>
    private async Task<int> StopAllWatchersForRealmAsync(Guid realmId, string reason)
    {
        // Find all watchers for this realm
        var realmWatchers = _activeWatchers.Values
            .Where(w => w.RealmId == realmId)
            .ToList();

        var stoppedCount = 0;

        foreach (var watcher in realmWatchers)
        {
            if (!_activeWatchers.TryRemove(watcher.WatcherId, out _))
                continue;

            // Remove from realm/type index
            var watcherKey = (watcher.RealmId, watcher.WatcherType);
            _watchersByRealmAndType.TryRemove(watcherKey, out _);

            // Publish watcher stopped event
            var evt = new WatcherStoppedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                WatcherId = watcher.WatcherId,
                RealmId = watcher.RealmId,
                WatcherType = watcher.WatcherType,
                Reason = reason
            };
            await _messageBus.TryPublishAsync(
                "puppetmaster.watcher.stopped",
                evt,
                cancellationToken: CancellationToken.None);

            stoppedCount++;
        }

        return stoppedCount;
    }

    /// <summary>
    /// Handles realm.created events.
    /// Auto-starts regional watchers for newly created realms.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmCreatedAsync(RealmCreatedEvent evt)
    {
        _logger.LogInformation(
            "Received realm.created event for realm {RealmId} ({RealmCode})",
            evt.RealmId,
            evt.Code);

        // Only start watchers for active realms
        if (!evt.IsActive)
        {
            _logger.LogDebug(
                "Realm {RealmId} is not active, skipping watcher creation",
                evt.RealmId);
            return;
        }

        // Auto-start regional watchers for the new realm
        var (status, response) = await StartWatchersForRealmAsync(
            new StartWatchersForRealmRequest { RealmId = evt.RealmId },
            CancellationToken.None);

        if (status == StatusCodes.OK && response != null)
        {
            _logger.LogInformation(
                "Auto-started {Count} watchers for new realm {RealmId}",
                response.WatchersStarted,
                evt.RealmId);
        }
        else
        {
            _logger.LogWarning(
                "Failed to auto-start watchers for new realm {RealmId}: status {Status}",
                evt.RealmId,
                status);
        }
    }
}
