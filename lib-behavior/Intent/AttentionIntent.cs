// =============================================================================
// Attention Intent
// Blendable attention output for multi-model gaze coordination.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Intent;

/// <summary>
/// Attention intent with blendable gaze targets.
/// </summary>
/// <remarks>
/// <para>
/// Unlike simple exclusive channels, attention supports "blending" by providing
/// multiple targets with weights. The game's animation system can then interpolate
/// gaze direction between targets based on their weights.
/// </para>
/// <para>
/// Since entity IDs (Guids) cannot be mathematically blended, this struct provides
/// primary and secondary targets with urgency-derived weights. The game resolves
/// entity positions and performs actual gaze interpolation.
/// </para>
/// <para>
/// Example: Combat model wants to watch the threat (urgency 0.8), Movement model
/// wants to look at the path ahead (urgency 0.4). Game blends gaze 67% toward
/// threat, 33% toward path.
/// </para>
/// </remarks>
public readonly struct AttentionIntent
{
    /// <summary>
    /// Primary attention target entity (highest urgency contributor).
    /// Null if no attention targets.
    /// </summary>
    public Guid? PrimaryTarget { get; init; }

    /// <summary>
    /// Urgency of the primary attention target [0.0 - 1.0].
    /// </summary>
    public float PrimaryUrgency { get; init; }

    /// <summary>
    /// Secondary attention target entity (second-highest urgency contributor).
    /// Null if only one model contributed attention.
    /// </summary>
    public Guid? SecondaryTarget { get; init; }

    /// <summary>
    /// Urgency of the secondary attention target [0.0 - 1.0].
    /// </summary>
    public float SecondaryUrgency { get; init; }

    /// <summary>
    /// Blend weight for primary target [0.0 - 1.0].
    /// The game should blend gaze: BlendWeight toward primary, (1-BlendWeight) toward secondary.
    /// If no secondary target, this will be 1.0.
    /// </summary>
    /// <remarks>
    /// Calculated as: PrimaryUrgency / (PrimaryUrgency + SecondaryUrgency)
    /// </remarks>
    public float BlendWeight { get; init; }

    /// <summary>
    /// Combined urgency for animation transition speed.
    /// </summary>
    public float TotalUrgency { get; init; }

    /// <summary>
    /// Empty attention intent (not looking at anything specific).
    /// </summary>
    public static AttentionIntent None => default;

    /// <summary>
    /// Whether this intent has a valid attention target.
    /// </summary>
    public bool IsValid => PrimaryTarget.HasValue;

    /// <summary>
    /// Whether this intent has multiple targets to blend between.
    /// </summary>
    public bool HasSecondaryTarget => SecondaryTarget.HasValue;

    /// <summary>
    /// Creates an attention intent with a single target (no blending needed).
    /// </summary>
    /// <param name="target">The target entity to look at.</param>
    /// <param name="urgency">Attention urgency [0-1].</param>
    /// <returns>A new AttentionIntent instance.</returns>
    public static AttentionIntent CreateSingle(Guid target, float urgency)
    {
        return new AttentionIntent
        {
            PrimaryTarget = target,
            PrimaryUrgency = Math.Clamp(urgency, 0f, 1f),
            SecondaryTarget = null,
            SecondaryUrgency = 0f,
            BlendWeight = 1f,
            TotalUrgency = Math.Clamp(urgency, 0f, 1f),
        };
    }

    /// <summary>
    /// Creates an attention intent with two blended targets.
    /// </summary>
    /// <param name="primaryTarget">Primary target entity (higher urgency).</param>
    /// <param name="primaryUrgency">Primary target urgency [0-1].</param>
    /// <param name="secondaryTarget">Secondary target entity.</param>
    /// <param name="secondaryUrgency">Secondary target urgency [0-1].</param>
    /// <returns>A new AttentionIntent instance with calculated blend weight.</returns>
    public static AttentionIntent CreateBlended(
        Guid primaryTarget,
        float primaryUrgency,
        Guid secondaryTarget,
        float secondaryUrgency)
    {
        var clampedPrimary = Math.Clamp(primaryUrgency, 0f, 1f);
        var clampedSecondary = Math.Clamp(secondaryUrgency, 0f, 1f);
        var total = clampedPrimary + clampedSecondary;

        // Calculate blend weight: how much to weight primary vs secondary
        var blendWeight = total > 0 ? clampedPrimary / total : 1f;

        return new AttentionIntent
        {
            PrimaryTarget = primaryTarget,
            PrimaryUrgency = clampedPrimary,
            SecondaryTarget = secondaryTarget,
            SecondaryUrgency = clampedSecondary,
            BlendWeight = blendWeight,
            TotalUrgency = Math.Min(total, 1f),
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (!IsValid)
            return "AttentionIntent.None";

        if (!HasSecondaryTarget)
            return $"AttentionIntent({PrimaryTarget}, urgency={PrimaryUrgency:F2})";

        return $"AttentionIntent(primary={PrimaryTarget}@{PrimaryUrgency:F2}, " +
                $"secondary={SecondaryTarget}@{SecondaryUrgency:F2}, blend={BlendWeight:F2})";
    }
}
