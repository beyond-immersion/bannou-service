// =============================================================================
// Locomotion Intent
// Blendable locomotion output for multi-model coordination.
// =============================================================================

using System.Numerics;

namespace BeyondImmersion.Bannou.Client.Behavior.Intent;

/// <summary>
/// Locomotion intent with blendable target position.
/// </summary>
/// <remarks>
/// <para>
/// Unlike other intent channels which are exclusive (highest urgency wins),
/// locomotion can blend multiple contributors weighted by urgency.
/// </para>
/// <para>
/// Example: Combat model wants to strafe left (urgency 0.8), Movement model
/// wants to walk forward (urgency 0.4). Result: diagonal movement weighted
/// toward strafing.
/// </para>
/// </remarks>
public readonly struct LocomotionIntent
{
    /// <summary>
    /// Movement intent name (e.g., "walk_to", "run_away", "strafe", "dodge").
    /// Null if no locomotion intent.
    /// </summary>
    public string? Intent { get; init; }

    /// <summary>
    /// Blended target position in world space.
    /// Null if the intent doesn't specify a target (e.g., "stop", "idle").
    /// </summary>
    public Vector3? Target { get; init; }

    /// <summary>
    /// Normalized movement speed multiplier [0.0 - 1.0].
    /// 0.0 = stationary, 1.0 = maximum speed for this intent.
    /// </summary>
    public float Speed { get; init; }

    /// <summary>
    /// Combined urgency of all contributing locomotion intents.
    /// Used by animation system to determine transition speed.
    /// </summary>
    public float Urgency { get; init; }

    /// <summary>
    /// Empty locomotion intent (no movement).
    /// </summary>
    public static LocomotionIntent None => default;

    /// <summary>
    /// Whether this intent represents valid locomotion data.
    /// </summary>
    public bool IsValid => Intent != null;

    /// <summary>
    /// Creates a locomotion intent with target position.
    /// </summary>
    /// <param name="intent">Movement intent name.</param>
    /// <param name="target">Target world position.</param>
    /// <param name="speed">Normalized speed [0-1].</param>
    /// <param name="urgency">Intent urgency [0-1].</param>
    /// <returns>A new LocomotionIntent instance.</returns>
    public static LocomotionIntent Create(string intent, Vector3 target, float speed, float urgency)
    {
        return new LocomotionIntent
        {
            Intent = intent,
            Target = target,
            Speed = Math.Clamp(speed, 0f, 1f),
            Urgency = Math.Clamp(urgency, 0f, 1f),
        };
    }

    /// <summary>
    /// Creates a locomotion intent without target (for intents like "stop", "idle").
    /// </summary>
    /// <param name="intent">Movement intent name.</param>
    /// <param name="urgency">Intent urgency [0-1].</param>
    /// <returns>A new LocomotionIntent instance.</returns>
    public static LocomotionIntent CreateWithoutTarget(string intent, float urgency)
    {
        return new LocomotionIntent
        {
            Intent = intent,
            Target = null,
            Speed = 0f,
            Urgency = Math.Clamp(urgency, 0f, 1f),
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (!IsValid)
            return "LocomotionIntent.None";

        return Target.HasValue
            ? $"LocomotionIntent({Intent}, target={Target.Value}, speed={Speed:F2}, urgency={Urgency:F2})"
            : $"LocomotionIntent({Intent}, urgency={Urgency:F2})";
    }
}
