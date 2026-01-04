// =============================================================================
// Merged Intent
// Unified output from all behavior models for game engine consumption.
// =============================================================================

namespace BeyondImmersion.Bannou.SDK.Behavior.Intent;

/// <summary>
/// The merged result of all behavior model outputs.
/// This is what the game engine acts on each frame.
/// </summary>
/// <remarks>
/// <para>
/// MergedIntent represents the final decision after the IntentMerger resolves
/// conflicts between multiple simultaneously-active behavior models.
/// </para>
/// <para>
/// Resolution rules:
/// - Action: Exclusive (highest urgency wins)
/// - Locomotion: Blendable (targets weighted by urgency)
/// - Attention: Blendable (multiple targets with weights for gaze interpolation)
/// - Stance: Exclusive (highest urgency wins)
/// - Vocalization: Exclusive (highest urgency wins)
/// </para>
/// </remarks>
public readonly struct MergedIntent
{
    // =========================================================================
    // ACTION CHANNEL (Exclusive)
    // =========================================================================

    /// <summary>
    /// Winning action intent from highest-urgency contributor.
    /// Null if no model emitted an action.
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Target entity for the winning action.
    /// Null if the action doesn't require a target.
    /// </summary>
    public Guid? ActionTarget { get; init; }

    /// <summary>
    /// Urgency of the winning action [0.0 - 1.0].
    /// Useful for animation blending speed.
    /// </summary>
    public float ActionUrgency { get; init; }

    // =========================================================================
    // LOCOMOTION CHANNEL (Blendable)
    // =========================================================================

    /// <summary>
    /// Blended locomotion intent from all contributors.
    /// </summary>
    public LocomotionIntent Locomotion { get; init; }

    // =========================================================================
    // ATTENTION CHANNEL (Blendable)
    // =========================================================================

    /// <summary>
    /// Blended attention intent from all contributors.
    /// Contains primary and secondary targets with weights for gaze interpolation.
    /// </summary>
    public AttentionIntent Attention { get; init; }

    // =========================================================================
    // STANCE CHANNEL (Exclusive)
    // =========================================================================

    /// <summary>
    /// Winning stance intent from highest-urgency contributor.
    /// Null if no model emitted a stance.
    /// </summary>
    public string? Stance { get; init; }

    /// <summary>
    /// Urgency of the winning stance [0.0 - 1.0].
    /// </summary>
    public float StanceUrgency { get; init; }

    // =========================================================================
    // VOCALIZATION CHANNEL (Exclusive)
    // =========================================================================

    /// <summary>
    /// Winning vocalization intent from highest-urgency contributor.
    /// Null if no model emitted a vocalization.
    /// </summary>
    public string? Vocalization { get; init; }

    /// <summary>
    /// Urgency of the winning vocalization [0.0 - 1.0].
    /// </summary>
    public float VocalizationUrgency { get; init; }

    // =========================================================================
    // DEBUG TRACE (DEBUG builds only)
    // =========================================================================

#if DEBUG
    /// <summary>
    /// Debug trace showing which model contributed to each channel.
    /// Only available in DEBUG builds.
    /// </summary>
    public ContributionTrace? Trace { get; init; }
#endif

    // =========================================================================
    // FACTORY METHODS
    // =========================================================================

    /// <summary>
    /// Empty merged intent (no actions, no movement).
    /// </summary>
    public static MergedIntent Empty => default;

    /// <summary>
    /// Whether this intent has any actionable content.
    /// </summary>
    public bool HasAnyIntent =>
        Action != null ||
        Locomotion.IsValid ||
        Attention.IsValid ||
        Stance != null ||
        Vocalization != null;

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new List<string>();

        if (Action != null)
            parts.Add($"Action={Action}({ActionUrgency:F2})");
        if (Locomotion.IsValid)
            parts.Add($"Locomotion={Locomotion.Intent}({Locomotion.Urgency:F2})");
        if (Attention.IsValid)
            parts.Add(Attention.HasSecondaryTarget
                ? $"Attention={Attention.PrimaryTarget}@{Attention.BlendWeight:F2},{Attention.SecondaryTarget}@{1 - Attention.BlendWeight:F2}"
                : $"Attention={Attention.PrimaryTarget}({Attention.PrimaryUrgency:F2})");
        if (Stance != null)
            parts.Add($"Stance={Stance}({StanceUrgency:F2})");
        if (Vocalization != null)
            parts.Add($"Vocalization={Vocalization}({VocalizationUrgency:F2})");

        return parts.Count > 0
            ? $"MergedIntent[{string.Join(", ", parts)}]"
            : "MergedIntent.Empty";
    }
}
