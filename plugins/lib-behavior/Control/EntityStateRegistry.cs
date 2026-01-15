// =============================================================================
// Entity State Registry Implementation
// Thread-safe registry for tracking entity state in the behavior system.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Thread-safe implementation of <see cref="IEntityStateRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// This registry maintains the current state of entities for use in behavior evaluation.
/// When cinematics complete, StateSync updates this registry so subsequent behavior
/// decisions have accurate world knowledge.
/// </para>
/// <para>
/// This implementation uses ConcurrentDictionary for thread-safe access per
/// IMPLEMENTATION TENETS (multi-instance safety).
/// </para>
/// </remarks>
public sealed class EntityStateRegistry : IEntityStateRegistry
{
    private readonly ConcurrentDictionary<Guid, EntityState> _states;
    private readonly ILogger<EntityStateRegistry>? _logger;

    /// <summary>
    /// Creates a new entity state registry.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public EntityStateRegistry(ILogger<EntityStateRegistry>? logger = null)
    {
        _logger = logger;
        _states = new ConcurrentDictionary<Guid, EntityState>();
    }

    /// <inheritdoc/>
    public event EventHandler<EntityStateUpdatedEventArgs>? StateUpdated;

    /// <inheritdoc/>
    public int Count => _states.Count;

    /// <inheritdoc/>
    public void UpdateState(Guid entityId, EntityState state, string? source = null)
    {

        EntityState? previousState = null;
        _states.AddOrUpdate(
            entityId,
            state,
            (_, existing) =>
            {
                previousState = existing;
                return state;
            });

        _logger?.LogDebug(
            "Updated state for entity {EntityId} from source {Source}: Position={Position}, Health={Health}",
            entityId,
            source ?? "unknown",
            state.Position,
            state.Health);

        // Raise event for subscribers
        StateUpdated?.Invoke(this, new EntityStateUpdatedEventArgs(
            entityId,
            previousState,
            state,
            source));
    }

    /// <inheritdoc/>
    public EntityState? GetState(Guid entityId)
    {
        return _states.TryGetValue(entityId, out var state) ? state : null;
    }

    /// <inheritdoc/>
    public EntityState GetStateOrEmpty(Guid entityId)
    {
        return _states.TryGetValue(entityId, out var state) ? state : EntityState.Empty;
    }

    /// <inheritdoc/>
    public bool RemoveState(Guid entityId)
    {
        var removed = _states.TryRemove(entityId, out _);

        if (removed)
        {
            _logger?.LogDebug("Removed state tracking for entity {EntityId}", entityId);
        }

        return removed;
    }

    /// <inheritdoc/>
    public bool HasState(Guid entityId)
    {
        return _states.ContainsKey(entityId);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Guid> GetTrackedEntityIds()
    {
        return _states.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        var count = _states.Count;
        _states.Clear();
        _logger?.LogInformation("Cleared all entity state tracking ({Count} entities)", count);
    }
}
