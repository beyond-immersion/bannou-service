// =============================================================================
// Control Gate Interface
// Determines what controls each entity's Intent Channels.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Handlers;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Gate that determines what can write to an entity's Intent Channels.
/// </summary>
/// <remarks>
/// <para>
/// The control gate is the arbiter of what source controls an entity at any moment.
/// It implements the priority system:
/// Cinematic > Player > Opportunity > Behavior
/// </para>
/// <para>
/// During cinematic control:
/// - Behavior stack continues running (for QTE defaults, perception)
/// - Behavior outputs are discarded
/// - Cinematic outputs go directly to channels
/// </para>
/// </remarks>
public interface IControlGate
{
    /// <summary>
    /// The entity this gate controls.
    /// </summary>
    Guid EntityId { get; }

    /// <summary>
    /// The current control source.
    /// </summary>
    ControlSource CurrentSource { get; }

    /// <summary>
    /// The current control options.
    /// </summary>
    ControlOptions? CurrentOptions { get; }

    /// <summary>
    /// Whether the behavior stack's outputs are currently accepted.
    /// </summary>
    bool AcceptsBehaviorOutput { get; }

    /// <summary>
    /// Whether player input commands are currently accepted.
    /// </summary>
    bool AcceptsPlayerInput { get; }

    /// <summary>
    /// Channels where behavior input is allowed even during cinematic.
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
    /// Filters intent emissions based on current control state.
    /// </summary>
    /// <param name="emissions">The emissions to filter.</param>
    /// <param name="source">The source of the emissions.</param>
    /// <returns>Emissions that are accepted by the gate.</returns>
    IReadOnlyList<IntentEmission> FilterEmissions(
        IReadOnlyList<IntentEmission> emissions,
        ControlSource source);

    /// <summary>
    /// Event raised when control source changes.
    /// </summary>
    event EventHandler<ControlChangedEvent>? ControlChanged;
}

/// <summary>
/// Registry for control gates, one per entity.
/// </summary>
public interface IControlGateRegistry
{
    /// <summary>
    /// Gets or creates a control gate for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The entity's control gate.</returns>
    IControlGate GetOrCreate(Guid entityId);

    /// <summary>
    /// Gets the control gate for an entity if it exists.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>The gate, or null if not found.</returns>
    IControlGate? Get(Guid entityId);

    /// <summary>
    /// Removes the control gate for an entity.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <returns>True if removed, false if not found.</returns>
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
