// =============================================================================
// Entity State Registry Interface
// Central registry for tracking entity state across the behavior system.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Registry for tracking entity state used by the behavior system.
/// </summary>
/// <remarks>
/// <para>
/// When cinematics complete, they must sync their final state back to this registry
/// so that subsequent behavior evaluations have accurate world knowledge.
/// </para>
/// <para>
/// Implementations must be thread-safe as state may be updated from multiple sources
/// (cinematics completing, game server updates, perception events).
/// </para>
/// </remarks>
public interface IEntityStateRegistry
{
    /// <summary>
    /// Updates the state for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="state">The new state.</param>
    /// <param name="source">Source of the update (for debugging/tracing).</param>
    void UpdateState(Guid entityId, EntityState state, string? source = null);

    /// <summary>
    /// Gets the current state for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The current state, or null if no state is tracked.</returns>
    EntityState? GetState(Guid entityId);

    /// <summary>
    /// Gets the current state for an entity, or empty state if not found.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The current state, or EntityState.Empty if not tracked.</returns>
    EntityState GetStateOrEmpty(Guid entityId);

    /// <summary>
    /// Removes state tracking for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if state was removed.</returns>
    bool RemoveState(Guid entityId);

    /// <summary>
    /// Checks if state is tracked for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if state is tracked.</returns>
    bool HasState(Guid entityId);

    /// <summary>
    /// Gets all tracked entity IDs.
    /// </summary>
    IReadOnlyCollection<Guid> GetTrackedEntityIds();

    /// <summary>
    /// Gets the count of tracked entities.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Clears all tracked state.
    /// </summary>
    void Clear();

    /// <summary>
    /// Event raised when entity state is updated.
    /// </summary>
    event EventHandler<EntityStateUpdatedEventArgs>? StateUpdated;
}

/// <summary>
/// Event args for entity state updates.
/// </summary>
public sealed class EntityStateUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event args.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="previousState">The previous state (null if first update).</param>
    /// <param name="newState">The new state.</param>
    /// <param name="source">The source of the update.</param>
    public EntityStateUpdatedEventArgs(
        Guid entityId,
        EntityState? previousState,
        EntityState newState,
        string? source)
    {
        EntityId = entityId;
        PreviousState = previousState;
        NewState = newState ?? throw new ArgumentNullException(nameof(newState));
        Source = source;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the entity ID.</summary>
    public Guid EntityId { get; }

    /// <summary>Gets the previous state (null if first update).</summary>
    public EntityState? PreviousState { get; }

    /// <summary>Gets the new state.</summary>
    public EntityState NewState { get; }

    /// <summary>Gets the source of the update.</summary>
    public string? Source { get; }

    /// <summary>Gets when the update occurred.</summary>
    public DateTime UpdatedAt { get; }
}
