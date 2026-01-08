// ═══════════════════════════════════════════════════════════════════════════
// Control Gate Interfaces
// Core interfaces for the Control Gating system.
// Implementations provided by lib-behavior plugin.
// ═══════════════════════════════════════════════════════════════════════════

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
/// Per-entity control gate that determines what sources can write to Intent Channels.
/// </summary>
public interface IControlGate
{
    /// <summary>The entity this gate controls.</summary>
    Guid EntityId { get; }

    /// <summary>The current control source.</summary>
    ControlSource CurrentSource { get; }

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
    /// Filters emissions based on current control state.
    /// </summary>
    /// <param name="emissions">The emissions to filter.</param>
    /// <param name="source">The source of these emissions.</param>
    /// <returns>The filtered emissions.</returns>
    IReadOnlyList<IntentEmission> FilterEmissions(
        IReadOnlyList<IntentEmission> emissions,
        ControlSource source);
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
}
