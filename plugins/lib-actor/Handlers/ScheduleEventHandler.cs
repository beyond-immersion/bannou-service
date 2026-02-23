// =============================================================================
// Schedule Event Handler
// ABML action handler for scheduling delayed events (Event Brain support).
// Uses character.{characterId}.perceptions topic per ACTOR_BEHAVIORS_GAP_ANALYSIS §6.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Actor.Handlers;

/// <summary>
/// ABML action handler for scheduling delayed events.
/// Enables Event Brain to set up timeouts and delayed actions.
/// </summary>
/// <remarks>
/// <para>
/// ABML usage for scheduling self-events (no target, uses executing actor's character):
/// <code>
/// - schedule_event:
///     delay_ms: 5000
///     event_type: "choreography.timeout"
///     data:
///       encounter_id: "${encounter.id}"
/// </code>
/// </para>
/// <para>
/// ABML usage for scheduling events to a specific character:
/// <code>
/// - schedule_event:
///     delay_ms: 5000
///     target_character: "${participant.characterId}"
///     event_type: "encounter_instruction"
///     data:
///       instruction_type: "timeout_warning"
/// </code>
/// </para>
/// <para>
/// Events are scheduled to be published to the character's perception topic
/// after the specified delay. Uses the standard tap pattern.
/// </para>
/// </remarks>
public sealed class ScheduleEventHandler : IActionHandler
{
    private const string ACTION_NAME = "schedule_event";
    private readonly IScheduledEventManager _scheduledEventManager;
    private readonly ILogger<ScheduleEventHandler> _logger;

    /// <summary>
    /// Creates a new schedule event handler.
    /// </summary>
    /// <param name="scheduledEventManager">Manager for scheduled events.</param>
    /// <param name="logger">Logger instance.</param>
    public ScheduleEventHandler(
        IScheduledEventManager scheduledEventManager,
        ILogger<ScheduleEventHandler> logger)
    {
        _scheduledEventManager = scheduledEventManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get required delay
        var delayMs = 0;
        if (evaluatedParams.TryGetValue("delay_ms", out var delayObj) && delayObj != null)
        {
            delayMs = Convert.ToInt32(delayObj);
        }
        else
        {
            throw new InvalidOperationException("schedule_event requires delay_ms parameter");
        }

        var eventType = evaluatedParams.GetValueOrDefault("event_type")?.ToString();
        if (string.IsNullOrEmpty(eventType))
        {
            throw new InvalidOperationException("schedule_event requires event_type parameter");
        }

        // Get target character - if not specified, try to get executing actor's character
        Guid? targetCharacterId = null;
        var targetCharacterStr = evaluatedParams.GetValueOrDefault("target_character")?.ToString();
        if (!string.IsNullOrEmpty(targetCharacterStr))
        {
            if (Guid.TryParse(targetCharacterStr, out var parsed))
            {
                targetCharacterId = parsed;
            }
            else
            {
                throw new InvalidOperationException($"schedule_event target_character must be a valid GUID, got: {targetCharacterStr}");
            }
        }
        else
        {
            // Try to get character_id from scope (for NPC Brain actors scheduling self-events)
            var characterIdStr = scope.GetValue("character_id")?.ToString()
                ?? context.RootScope.GetValue("character_id")?.ToString();
            if (!string.IsNullOrEmpty(characterIdStr) && Guid.TryParse(characterIdStr, out var charId))
            {
                targetCharacterId = charId;
            }
        }

        // Get actor ID for logging
        var actorId = scope.GetValue("actor_id")?.ToString()
            ?? context.RootScope.GetValue("actor_id")?.ToString()
            ?? "unknown";

        // Get event data
        var data = evaluatedParams.GetValueOrDefault("data");

        // Get source_id (who/what is sending this perception)
        var sourceId = evaluatedParams.GetValueOrDefault("source_id")?.ToString() ?? actorId;

        // Schedule the event
        var scheduledEvent = new ScheduledEvent
        {
            Id = Guid.NewGuid(),
            TargetCharacterId = targetCharacterId,
            SourceActorId = actorId,
            SourceId = sourceId,
            EventType = eventType,
            ScheduledAt = DateTimeOffset.UtcNow,
            FireAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs),
            Data = ConvertToDictionary(data)
        };

        _scheduledEventManager.Schedule(scheduledEvent);

        _logger.LogDebug("Scheduled event {EventType} for character {TargetCharacterId} from actor {ActorId} in {DelayMs}ms",
            eventType, targetCharacterId?.ToString() ?? "(self)", actorId, delayMs);

        await Task.CompletedTask;
        return ActionResult.Continue;
    }

    private static Dictionary<string, object?> ConvertToDictionary(object? data)
    {
        if (data == null)
        {
            return new Dictionary<string, object?>();
        }

        if (data is Dictionary<string, object?> dict)
        {
            return dict;
        }

        if (data is IReadOnlyDictionary<string, object?> roDict)
        {
            return new Dictionary<string, object?>(roDict);
        }

        return new Dictionary<string, object?> { ["value"] = data };
    }
}

/// <summary>
/// Represents a scheduled event waiting to fire.
/// </summary>
public sealed class ScheduledEvent
{
    /// <summary>
    /// Unique ID for this scheduled event.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Target character to receive the event. Null if Event Brain self-event.
    /// </summary>
    public Guid? TargetCharacterId { get; init; }

    /// <summary>
    /// Actor that scheduled this event (for logging/tracking).
    /// </summary>
    public required string SourceActorId { get; init; }

    /// <summary>
    /// Source ID for the perception event.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Event type to emit.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// When this event was scheduled.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; init; }

    /// <summary>
    /// When this event should fire.
    /// </summary>
    public DateTimeOffset FireAt { get; init; }

    /// <summary>
    /// Event data payload.
    /// </summary>
    public Dictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Interface for managing scheduled events.
/// </summary>
public interface IScheduledEventManager
{
    /// <summary>
    /// Schedules an event to fire after a delay.
    /// </summary>
    /// <param name="scheduledEvent">The event to schedule.</param>
    void Schedule(ScheduledEvent scheduledEvent);

    /// <summary>
    /// Cancels a scheduled event by ID.
    /// </summary>
    /// <param name="eventId">ID of the event to cancel.</param>
    /// <returns>True if the event was found and cancelled.</returns>
    bool Cancel(string eventId);
}

/// <summary>
/// In-memory implementation of scheduled event management.
/// Uses a background timer to check for events that need to fire.
/// </summary>
public sealed class ScheduledEventManager : IScheduledEventManager, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ScheduledEventManager> _logger;
    private readonly ActorServiceConfiguration _config;
    private readonly ConcurrentDictionary<Guid, ScheduledEvent> _pendingEvents = new();
    private readonly Timer _timer;
    private bool _disposed;

    /// <summary>
    /// Creates a new scheduled event manager.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Actor service configuration.</param>
    public ScheduledEventManager(
        IMessageBus messageBus,
        ILogger<ScheduledEventManager> logger,
        ActorServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _logger = logger;
        _config = configuration;

        var checkInterval = TimeSpan.FromMilliseconds(configuration.ScheduledEventCheckIntervalMilliseconds);
        _timer = new Timer(CheckEvents, null, checkInterval, checkInterval);
    }

    /// <inheritdoc/>
    public void Schedule(ScheduledEvent scheduledEvent)
    {
        _pendingEvents[scheduledEvent.Id] = scheduledEvent;
        _logger.LogDebug("Scheduled event {EventId} to fire at {FireAt}",
            scheduledEvent.Id, scheduledEvent.FireAt);
    }

    /// <inheritdoc/>
    public bool Cancel(string eventId)
    {
        if (!Guid.TryParse(eventId, out var parsedId))
        {
            return false;
        }
        var cancelled = _pendingEvents.TryRemove(parsedId, out _);
        if (cancelled)
        {
            _logger.LogDebug("Cancelled scheduled event {EventId}", eventId);
        }
        return cancelled;
    }

    private void CheckEvents(object? state)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _pendingEvents)
        {
            if (kvp.Value.FireAt <= now)
            {
                if (_pendingEvents.TryRemove(kvp.Key, out var evt))
                {
                    _ = FireEventAsync(evt);
                }
            }
        }
    }

    private async Task FireEventAsync(ScheduledEvent evt)
    {
        try
        {
            if (!evt.TargetCharacterId.HasValue)
            {
                // Event Brain self-event without character target - log warning and skip
                // These should be handled via internal state rather than message bus
                _logger.LogWarning("Scheduled event {EventType} has no target character, skipping (Event Brain self-events should use internal state)",
                    evt.EventType);
                return;
            }

            // Build CharacterPerceptionEvent for the target character
            var perceptionEvent = new CharacterPerceptionEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = evt.TargetCharacterId.Value,
                SourceAppId = _config.LocalModeAppId, // Scheduled events come from Bannou per IMPLEMENTATION TENETS (no hardcoded tunables)
                Perception = new PerceptionData
                {
                    PerceptionType = evt.EventType,
                    SourceId = evt.SourceId,
                    SourceType = PerceptionSourceType.Scheduled,
                    Data = evt.Data,
                    Urgency = (float)_config.ScheduledEventDefaultUrgency
                }
            };

            // Use the standard character perception topic (tap pattern)
            var topic = $"character.{evt.TargetCharacterId}.perceptions";
            await _messageBus.TryPublishAsync(topic, perceptionEvent, CancellationToken.None);

            _logger.LogDebug("Fired scheduled event {EventType} for character {CharacterId}",
                evt.EventType, evt.TargetCharacterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fire scheduled event {EventId}", evt.Id);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _timer.Dispose();
            _disposed = true;
        }
    }
}
