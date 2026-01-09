// =============================================================================
// Schedule Event Handler
// ABML action handler for scheduling delayed events (Event Brain support).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
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
/// ABML usage:
/// <code>
/// - schedule_event:
///     delay_ms: 5000
///     event_type: "choreography.timeout"
///     data:
///       encounter_id: "${encounter.id}"
/// </code>
/// </para>
/// <para>
/// Events are scheduled to be published to the actor's perception topic
/// after the specified delay. The executing actor's ID is automatically
/// used as the target.
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
        _scheduledEventManager = scheduledEventManager ?? throw new ArgumentNullException(nameof(scheduledEventManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
        => action is DomainAction da && da.Name == ACTION_NAME;

    /// <inheritdoc/>
    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        AbmlExecutionContext context,
        CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // Get required parameters
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

        // Get event data
        var data = evaluatedParams.GetValueOrDefault("data");

        // Get the executing actor's ID from scope (should be registered by ActorRunner)
        var actorId = scope.GetValue("actor_id")?.ToString();
        if (string.IsNullOrEmpty(actorId))
        {
            // Fallback: try root scope
            actorId = context.RootScope.GetValue("actor_id")?.ToString() ?? "unknown";
        }

        // Schedule the event
        var scheduledEvent = new ScheduledEvent
        {
            Id = Guid.NewGuid().ToString(),
            TargetActorId = actorId,
            EventType = eventType,
            ScheduledAt = DateTimeOffset.UtcNow,
            FireAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs),
            Data = ConvertToDictionary(data)
        };

        _scheduledEventManager.Schedule(scheduledEvent);

        _logger.LogDebug("Scheduled event {EventType} for actor {ActorId} in {DelayMs}ms",
            eventType, actorId, delayMs);

        return ValueTask.FromResult(ActionResult.Continue);
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
    public required string Id { get; init; }

    /// <summary>
    /// Target actor to receive the event.
    /// </summary>
    public required string TargetActorId { get; init; }

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
    private readonly ConcurrentDictionary<string, ScheduledEvent> _pendingEvents = new();
    private readonly Timer _timer;
    private bool _disposed;

    /// <summary>
    /// Creates a new scheduled event manager.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing events.</param>
    /// <param name="logger">Logger instance.</param>
    public ScheduledEventManager(
        IMessageBus messageBus,
        ILogger<ScheduledEventManager> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Check for events to fire every 100ms
        _timer = new Timer(CheckEvents, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
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
        var cancelled = _pendingEvents.TryRemove(eventId, out _);
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
            var perceptionEvent = new PerceptionEvent
            {
                TargetActorId = evt.TargetActorId,
                PerceptionType = evt.EventType,
                Timestamp = DateTimeOffset.UtcNow,
                Data = evt.Data
            };

            var topic = $"actor.{evt.TargetActorId}.perceptions";
            await _messageBus.TryPublishAsync(topic, perceptionEvent, CancellationToken.None);

            _logger.LogDebug("Fired scheduled event {EventType} for actor {ActorId}",
                evt.EventType, evt.TargetActorId);
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
