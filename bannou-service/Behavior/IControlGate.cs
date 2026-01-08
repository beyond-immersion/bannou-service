// ═══════════════════════════════════════════════════════════════════════════
// Control Gate Interfaces
// Core interfaces for the Control Gating system.
// Implementations provided by lib-behavior plugin.
// ═══════════════════════════════════════════════════════════════════════════

using System.Numerics;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Source of control for an entity's Intent Channels.
/// Higher values have higher priority.
/// </summary>
public enum ControlSource
{
    /// <summary>Normal autonomous behavior stack.</summary>
    Behavior = 0,

    /// <summary>Optional opportunity (player can accept/decline).</summary>
    Opportunity = 1,

    /// <summary>Direct player input commands.</summary>
    Player = 2,

    /// <summary>Cinematic/cutscene control (highest priority).</summary>
    Cinematic = 3
}

/// <summary>
/// Style of control handoff when returning control.
/// </summary>
public enum HandoffStyle
{
    /// <summary>Immediate transfer - no transition period.</summary>
    Instant = 0,

    /// <summary>Smooth blend - gradually return control over time.</summary>
    Blend = 1,

    /// <summary>Explicit handoff - wait for explicit trigger (button press, action).</summary>
    Explicit = 2
}

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
    /// <summary>Entity's final position.</summary>
    public Vector3? Position { get; init; }

    /// <summary>Entity's final rotation (euler angles or quaternion).</summary>
    public Vector3? Rotation { get; init; }

    /// <summary>Entity's current health (0.0-1.0).</summary>
    public float? Health { get; init; }

    /// <summary>Current stance/posture.</summary>
    public string? Stance { get; init; }

    /// <summary>Current emotional state.</summary>
    public string? Emotion { get; init; }

    /// <summary>Current target entity (if any).</summary>
    public Guid? CurrentTarget { get; init; }

    /// <summary>Additional state values.</summary>
    public IReadOnlyDictionary<string, object>? AdditionalState { get; init; }

    /// <summary>Creates empty state.</summary>
    public static EntityState Empty => new();
}

/// <summary>
/// Options for taking control of an entity.
/// </summary>
/// <param name="Source">The control source requesting control.</param>
/// <param name="CinematicId">ID of the cinematic taking control (if Cinematic source).</param>
/// <param name="AllowBehaviorInput">Channels where behavior can still contribute (optional).</param>
/// <param name="Duration">Expected duration of control (null = indefinite).</param>
public sealed record ControlOptions(
    ControlSource Source,
    string? CinematicId = null,
    IReadOnlySet<string>? AllowBehaviorInput = null,
    TimeSpan? Duration = null)
{
    /// <summary>Creates options for behavior control.</summary>
    public static ControlOptions ForBehavior()
        => new(ControlSource.Behavior);

    /// <summary>Creates options for player control.</summary>
    public static ControlOptions ForPlayer()
        => new(ControlSource.Player);

    /// <summary>
    /// Creates options for cinematic control.
    /// </summary>
    /// <param name="cinematicId">The cutscene identifier.</param>
    /// <param name="allowBehaviorChannels">Channels where behavior can still contribute.</param>
    /// <param name="duration">Expected duration.</param>
    public static ControlOptions ForCinematic(
        string cinematicId,
        IReadOnlySet<string>? allowBehaviorChannels = null,
        TimeSpan? duration = null)
        => new(ControlSource.Cinematic, cinematicId, allowBehaviorChannels, duration);

    /// <summary>Creates options for offered opportunity.</summary>
    public static ControlOptions ForOpportunity()
        => new(ControlSource.Opportunity);
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
    /// <summary>Creates an instant handoff (no transition).</summary>
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

    /// <summary>Creates an explicit handoff (triggered by action).</summary>
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

/// <summary>
/// Per-entity control gate that determines what sources can write to Intent Channels.
/// </summary>
public interface IControlGate
{
    /// <summary>The entity this gate controls.</summary>
    Guid EntityId { get; }

    /// <summary>The current control source.</summary>
    ControlSource CurrentSource { get; }

    /// <summary>The current control options.</summary>
    ControlOptions? CurrentOptions { get; }

    /// <summary>Whether behavior stack outputs are accepted.</summary>
    bool AcceptsBehaviorOutput { get; }

    /// <summary>Whether player input is accepted.</summary>
    bool AcceptsPlayerInput { get; }

    /// <summary>
    /// Channels where behavior can still contribute during non-Behavior control.
    /// Empty means no behavior output allowed.
    /// </summary>
    IReadOnlySet<string> BehaviorInputChannels { get; }

    /// <summary>
    /// Takes control of the entity.
    /// </summary>
    /// <param name="options">Control options.</param>
    /// <returns>True if control was taken, false if denied.</returns>
    Task<bool> TakeControlAsync(ControlOptions options);

    /// <summary>
    /// Returns control to the default source (behavior).
    /// </summary>
    /// <param name="handoff">Handoff protocol.</param>
    Task ReturnControlAsync(ControlHandoff handoff);

    /// <summary>
    /// Filters emissions based on current control state.
    /// </summary>
    /// <param name="emissions">The emissions to filter.</param>
    /// <param name="source">The source of these emissions.</param>
    /// <returns>The filtered emissions.</returns>
    IReadOnlyList<IntentEmission> FilterEmissions(
        IReadOnlyList<IntentEmission> emissions,
        ControlSource source);

    /// <summary>
    /// Event raised when control source changes.
    /// </summary>
    event EventHandler<ControlChangedEvent>? ControlChanged;
}

/// <summary>
/// Registry of control gates for entity control management.
/// </summary>
public interface IControlGateRegistry
{
    /// <summary>
    /// Gets the control gate for an entity, if it exists.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The control gate, or null if not found.</returns>
    IControlGate? Get(Guid entityId);

    /// <summary>
    /// Gets or creates a control gate for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The control gate.</returns>
    IControlGate GetOrCreate(Guid entityId);

    /// <summary>
    /// Removes the control gate for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if a gate was removed.</returns>
    bool Remove(Guid entityId);

    /// <summary>
    /// Gets all entities currently under cinematic control.
    /// </summary>
    IReadOnlyCollection<Guid> GetCinematicControlledEntities();

    /// <summary>
    /// Gets all entities currently under player control.
    /// </summary>
    IReadOnlyCollection<Guid> GetPlayerControlledEntities();
}
