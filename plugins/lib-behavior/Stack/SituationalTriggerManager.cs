// =============================================================================
// Situational Trigger Manager
// Manages event-driven and GOAP-driven activation of situational behaviors.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Stack;

/// <summary>
/// Trigger type for situational behavior activation.
/// </summary>
public enum TriggerType
{
    /// <summary>Triggered by discrete event (e.g., enemy_spotted).</summary>
    Event,

    /// <summary>Triggered by GOAP planner (e.g., mode switch to reach goal).</summary>
    Goap,

    /// <summary>Triggered by proximity or spatial conditions.</summary>
    Spatial,

    /// <summary>Triggered by time/schedule.</summary>
    Temporal
}

/// <summary>
/// Definition of a situational trigger.
/// </summary>
/// <param name="TriggerId">Unique identifier for this trigger.</param>
/// <param name="BehaviorId">The behavior to activate.</param>
/// <param name="Type">The trigger type.</param>
/// <param name="Priority">Priority within Situational category.</param>
/// <param name="Duration">Optional auto-deactivation duration.</param>
/// <param name="Condition">Optional condition predicate.</param>
public sealed record SituationalTriggerDefinition(
    string TriggerId,
    string BehaviorId,
    TriggerType Type,
    int Priority = 0,
    TimeSpan? Duration = null,
    Func<TriggerContext, bool>? Condition = null);

/// <summary>
/// Context available to trigger conditions.
/// </summary>
public sealed class TriggerContext
{
    /// <summary>
    /// The entity ID being evaluated.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// The trigger event name (for Event triggers).
    /// </summary>
    public string? EventName { get; init; }

    /// <summary>
    /// Event-specific data.
    /// </summary>
    public IReadOnlyDictionary<string, object>? EventData { get; init; }

    /// <summary>
    /// The current GOAP goal (for GOAP triggers).
    /// </summary>
    public string? GoapGoal { get; init; }

    /// <summary>
    /// Current simulation time.
    /// </summary>
    public TimeSpan SimulationTime { get; init; }
}

/// <summary>
/// An active situational trigger with expiration tracking.
/// </summary>
public sealed class ActiveTrigger
{
    /// <summary>
    /// The trigger definition.
    /// </summary>
    public required SituationalTriggerDefinition Definition { get; init; }

    /// <summary>
    /// When this trigger was activated.
    /// </summary>
    public DateTime ActivatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this trigger expires (null = never).
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Whether this trigger has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
}

/// <summary>
/// Interface for managing situational triggers.
/// </summary>
public interface ISituationalTriggerManager
{
    /// <summary>
    /// Registers a trigger definition.
    /// </summary>
    /// <param name="definition">The trigger definition.</param>
    void RegisterTrigger(SituationalTriggerDefinition definition);

    /// <summary>
    /// Fires an event trigger.
    /// </summary>
    /// <param name="entityId">The entity to trigger for.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="eventData">Optional event data.</param>
    /// <returns>Trigger requests generated.</returns>
    IReadOnlyList<SituationalTriggerRequest> FireEvent(
        Guid entityId,
        string eventName,
        IReadOnlyDictionary<string, object>? eventData = null);

    /// <summary>
    /// Evaluates GOAP triggers for a goal.
    /// </summary>
    /// <param name="entityId">The entity to evaluate for.</param>
    /// <param name="goapGoal">The current GOAP goal.</param>
    /// <returns>Trigger requests generated.</returns>
    IReadOnlyList<SituationalTriggerRequest> EvaluateGoap(
        Guid entityId,
        string goapGoal);

    /// <summary>
    /// Gets active triggers for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>Active triggers.</returns>
    IReadOnlyList<ActiveTrigger> GetActiveTriggers(Guid entityId);

    /// <summary>
    /// Deactivates a trigger for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="triggerId">The trigger ID to deactivate.</param>
    /// <returns>True if a trigger was deactivated.</returns>
    bool DeactivateTrigger(Guid entityId, string triggerId);

    /// <summary>
    /// Cleans up expired triggers.
    /// </summary>
    void CleanupExpired();
}

/// <summary>
/// Default implementation of situational trigger manager.
/// </summary>
public sealed class SituationalTriggerManager : ISituationalTriggerManager
{
    private readonly ConcurrentDictionary<string, SituationalTriggerDefinition> _triggerDefinitions;
    private readonly ConcurrentDictionary<string, List<SituationalTriggerDefinition>> _eventTriggers;
    private readonly ConcurrentDictionary<string, List<SituationalTriggerDefinition>> _goapTriggers;
    private readonly ConcurrentDictionary<Guid, List<ActiveTrigger>> _activeTriggers;
    private readonly ILogger<SituationalTriggerManager>? _logger;

    /// <summary>
    /// Creates a new situational trigger manager.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public SituationalTriggerManager(ILogger<SituationalTriggerManager>? logger = null)
    {
        _logger = logger;
        _triggerDefinitions = new ConcurrentDictionary<string, SituationalTriggerDefinition>(StringComparer.OrdinalIgnoreCase);
        _eventTriggers = new ConcurrentDictionary<string, List<SituationalTriggerDefinition>>(StringComparer.OrdinalIgnoreCase);
        _goapTriggers = new ConcurrentDictionary<string, List<SituationalTriggerDefinition>>(StringComparer.OrdinalIgnoreCase);
        _activeTriggers = new ConcurrentDictionary<Guid, List<ActiveTrigger>>();
    }

    /// <inheritdoc/>
    public void RegisterTrigger(SituationalTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _triggerDefinitions[definition.TriggerId] = definition;

        // Index by event name for Event triggers
        if (definition.Type == TriggerType.Event)
        {
            var eventName = definition.TriggerId; // Assume triggerId is the event name
            _eventTriggers.AddOrUpdate(
                eventName,
                _ => new List<SituationalTriggerDefinition> { definition },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(definition);
                    }
                    return list;
                });
        }

        // Index by goal name for GOAP triggers
        if (definition.Type == TriggerType.Goap)
        {
            var goalName = definition.TriggerId;
            _goapTriggers.AddOrUpdate(
                goalName,
                _ => new List<SituationalTriggerDefinition> { definition },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(definition);
                    }
                    return list;
                });
        }

        _logger?.LogDebug(
            "Registered situational trigger {TriggerId} -> {BehaviorId} ({Type})",
            definition.TriggerId,
            definition.BehaviorId,
            definition.Type);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SituationalTriggerRequest> FireEvent(
        Guid entityId,
        string eventName,
        IReadOnlyDictionary<string, object>? eventData = null)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return Array.Empty<SituationalTriggerRequest>();
        }

        var requests = new List<SituationalTriggerRequest>();

        if (!_eventTriggers.TryGetValue(eventName, out var triggers))
        {
            return requests;
        }

        var context = new TriggerContext
        {
            EntityId = entityId,
            EventName = eventName,
            EventData = eventData
        };

        lock (triggers)
        {
            foreach (var trigger in triggers)
            {
                if (trigger.Condition != null && !trigger.Condition(context))
                {
                    continue;
                }

                var request = new SituationalTriggerRequest(
                    trigger.TriggerId,
                    trigger.BehaviorId,
                    trigger.Priority,
                    trigger.Duration);

                requests.Add(request);
                ActivateTrigger(entityId, trigger);
            }
        }

        if (requests.Count > 0)
        {
            _logger?.LogDebug(
                "Event {EventName} fired {Count} triggers for entity {EntityId}",
                eventName,
                requests.Count,
                entityId);
        }

        return requests;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SituationalTriggerRequest> EvaluateGoap(
        Guid entityId,
        string goapGoal)
    {
        if (string.IsNullOrEmpty(goapGoal))
        {
            return Array.Empty<SituationalTriggerRequest>();
        }

        var requests = new List<SituationalTriggerRequest>();

        if (!_goapTriggers.TryGetValue(goapGoal, out var triggers))
        {
            return requests;
        }

        var context = new TriggerContext
        {
            EntityId = entityId,
            GoapGoal = goapGoal
        };

        lock (triggers)
        {
            foreach (var trigger in triggers)
            {
                if (trigger.Condition != null && !trigger.Condition(context))
                {
                    continue;
                }

                var request = new SituationalTriggerRequest(
                    trigger.TriggerId,
                    trigger.BehaviorId,
                    trigger.Priority,
                    trigger.Duration);

                requests.Add(request);
                ActivateTrigger(entityId, trigger);
            }
        }

        if (requests.Count > 0)
        {
            _logger?.LogDebug(
                "GOAP goal {Goal} triggered {Count} behaviors for entity {EntityId}",
                goapGoal,
                requests.Count,
                entityId);
        }

        return requests;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ActiveTrigger> GetActiveTriggers(Guid entityId)
    {
        if (!_activeTriggers.TryGetValue(entityId, out var triggers))
        {
            return Array.Empty<ActiveTrigger>();
        }

        lock (triggers)
        {
            return triggers.Where(t => !t.IsExpired).ToList();
        }
    }

    /// <inheritdoc/>
    public bool DeactivateTrigger(Guid entityId, string triggerId)
    {
        if (!_activeTriggers.TryGetValue(entityId, out var triggers))
        {
            return false;
        }

        lock (triggers)
        {
            var removed = triggers.RemoveAll(t => t.Definition.TriggerId == triggerId);
            return removed > 0;
        }
    }

    /// <inheritdoc/>
    public void CleanupExpired()
    {
        foreach (var kvp in _activeTriggers)
        {
            var entityId = kvp.Key;
            var triggers = kvp.Value;

            lock (triggers)
            {
                var expiredCount = triggers.RemoveAll(t => t.IsExpired);
                if (expiredCount > 0)
                {
                    _logger?.LogDebug(
                        "Cleaned up {Count} expired triggers for entity {EntityId}",
                        expiredCount,
                        entityId);
                }
            }
        }
    }

    private void ActivateTrigger(Guid entityId, SituationalTriggerDefinition definition)
    {
        var activeTrigger = new ActiveTrigger
        {
            Definition = definition,
            ActivatedAt = DateTime.UtcNow,
            ExpiresAt = definition.Duration.HasValue
                ? DateTime.UtcNow + definition.Duration.Value
                : null
        };

        _activeTriggers.AddOrUpdate(
            entityId,
            _ => new List<ActiveTrigger> { activeTrigger },
            (_, list) =>
            {
                lock (list)
                {
                    // Remove existing trigger with same ID
                    list.RemoveAll(t => t.Definition.TriggerId == definition.TriggerId);
                    list.Add(activeTrigger);
                }
                return list;
            });
    }
}

/// <summary>
/// Common situational triggers for standard behaviors.
/// </summary>
public static class CommonTriggers
{
    /// <summary>
    /// Combat mode trigger when enemy is spotted.
    /// </summary>
    public static SituationalTriggerDefinition CombatEntered => new(
        "enemy_spotted",
        "combat-mode",
        TriggerType.Event,
        Priority: 100);

    /// <summary>
    /// Combat mode ends when threat is cleared.
    /// </summary>
    public static SituationalTriggerDefinition CombatEnded => new(
        "threat_cleared",
        "normal-mode",
        TriggerType.Event,
        Priority: 0);

    /// <summary>
    /// Flee behavior when fear threshold exceeded.
    /// </summary>
    public static SituationalTriggerDefinition FleeTriggered => new(
        "fear_threshold_exceeded",
        "fleeing",
        TriggerType.Event,
        Priority: 150);

    /// <summary>
    /// Vehicle control mode when entering vehicle via GOAP.
    /// </summary>
    public static SituationalTriggerDefinition EnterVehicle => new(
        "enter_vehicle",
        "vehicle-control",
        TriggerType.Goap,
        Priority: 50);

    /// <summary>
    /// Exit vehicle mode via GOAP.
    /// </summary>
    public static SituationalTriggerDefinition ExitVehicle => new(
        "exit_vehicle",
        "humanoid-base",
        TriggerType.Goap,
        Priority: 0);

    /// <summary>
    /// Conversation mode when dialogue starts.
    /// </summary>
    public static SituationalTriggerDefinition ConversationStarted => new(
        "conversation_started",
        "conversation-mode",
        TriggerType.Event,
        Priority: 75);

    /// <summary>
    /// Returns all common triggers.
    /// </summary>
    public static IEnumerable<SituationalTriggerDefinition> All => new[]
    {
        CombatEntered,
        CombatEnded,
        FleeTriggered,
        EnterVehicle,
        ExitVehicle,
        ConversationStarted
    };
}
