using System.Text.Json;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

            _logger.LogDebug(
                "Dispatching {SourceType} change for {ResourceType}:{ResourceId} to {WatcherCount} actors",
                sourceType, mapping.ResourceType, resourceId, watchers.Count);

            // Create perception and dispatch to each watcher
            var perception = WatchPerception.ResourceChanged(
                mapping.ResourceType,
                resourceId,
                sourceType,
                eventTopic,
                eventData);

            foreach (var actorId in watchers)
            {
                // Check if this actor's watch matches the source type
                if (!_watchRegistry.HasMatchingWatch(actorId, mapping.ResourceType, resourceId, sourceType))
                {
                    continue;
                }

                await InjectWatchPerceptionAsync(actorId, perception, ct);
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
            using var scope = _scopeFactory.CreateScope();
            var actorClient = scope.ServiceProvider.GetService<IActorClient>();

            if (actorClient == null)
            {
                _logger.LogWarning("IActorClient not available, cannot inject perception");
                return;
            }

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

            var response = await actorClient.InjectPerceptionAsync(request, ct);

            if (!response.Queued)
            {
                _logger.LogDebug(
                    "Perception not queued for actor {ActorId} (queue may be full)",
                    actorId);
            }
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
