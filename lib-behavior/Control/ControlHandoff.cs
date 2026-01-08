// =============================================================================
// Control Handoff
// Protocol for transferring control back to behavior stack.
// =============================================================================

using System.Numerics;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Final state information for control handoff.
/// </summary>
/// <remarks>
/// <para>
/// When a cinematic returns control, it must communicate the final state
/// so the behavior stack can resume with accurate world knowledge.
/// </para>
/// </remarks>
public sealed class EntityState
{
    /// <summary>
    /// Entity's final position.
    /// </summary>
    public Vector3? Position { get; init; }

    /// <summary>
    /// Entity's final rotation (euler angles or quaternion).
    /// </summary>
    public Vector3? Rotation { get; init; }

    /// <summary>
    /// Entity's current health (0.0-1.0).
    /// </summary>
    public float? Health { get; init; }

    /// <summary>
    /// Current stance/posture.
    /// </summary>
    public string? Stance { get; init; }

    /// <summary>
    /// Current emotional state.
    /// </summary>
    public string? Emotion { get; init; }

    /// <summary>
    /// Current target entity (if any).
    /// </summary>
    public Guid? CurrentTarget { get; init; }

    /// <summary>
    /// Additional state values.
    /// </summary>
    public IReadOnlyDictionary<string, object>? AdditionalState { get; init; }

    /// <summary>
    /// Creates empty state.
    /// </summary>
    public static EntityState Empty => new();
}

/// <summary>
/// Protocol for transferring control between sources.
/// </summary>
/// <param name="Style">How to perform the handoff.</param>
/// <param name="BlendDuration">Duration for blend-style handoffs.</param>
/// <param name="SyncState">Whether to push final state to behavior stack.</param>
/// <param name="FinalState">The entity's final state (if syncing).</param>
public sealed record ControlHandoff(
    HandoffStyle Style,
    TimeSpan? BlendDuration = null,
    bool SyncState = true,
    EntityState? FinalState = null)
{
    /// <summary>
    /// Creates an instant handoff (no transition).
    /// </summary>
    public static ControlHandoff Instant()
        => new(HandoffStyle.Instant);

    /// <summary>
    /// Creates an instant handoff with state sync.
    /// </summary>
    /// <param name="finalState">The final entity state.</param>
    public static ControlHandoff InstantWithState(EntityState finalState)
        => new(HandoffStyle.Instant, SyncState: true, FinalState: finalState);

    /// <summary>
    /// Creates a blended handoff.
    /// </summary>
    /// <param name="duration">Blend duration.</param>
    /// <param name="finalState">Optional final state.</param>
    public static ControlHandoff Blend(TimeSpan duration, EntityState? finalState = null)
        => new(HandoffStyle.Blend, duration, SyncState: finalState != null, FinalState: finalState);

    /// <summary>
    /// Creates an explicit handoff (triggered by action).
    /// </summary>
    public static ControlHandoff Explicit()
        => new(HandoffStyle.Explicit);
}

/// <summary>
/// Event raised when control source changes for an entity.
/// </summary>
/// <param name="EntityId">The entity whose control changed.</param>
/// <param name="PreviousSource">The previous control source.</param>
/// <param name="NewSource">The new control source.</param>
/// <param name="Handoff">The handoff protocol used (if returning control).</param>
public sealed record ControlChangedEvent(
    Guid EntityId,
    ControlSource PreviousSource,
    ControlSource NewSource,
    ControlHandoff? Handoff = null);
