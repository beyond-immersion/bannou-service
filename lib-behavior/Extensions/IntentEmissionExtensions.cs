// =============================================================================
// Intent Emission Extensions
// Extension methods for working with IntentEmission TargetPosition data.
// =============================================================================

using System.Numerics;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Extensions;

/// <summary>
/// Extension methods for working with IntentEmission.
/// </summary>
public static class IntentEmissionExtensions
{
    /// <summary>
    /// Key used to store target position in the Data dictionary.
    /// </summary>
    public const string TargetPositionKey = "target_position";

    /// <summary>
    /// Gets the target position from an emission's data dictionary.
    /// </summary>
    /// <param name="emission">The emission to get position from.</param>
    /// <returns>The target position, or null if not set.</returns>
    public static Vector3? GetTargetPosition(this IntentEmission emission)
    {
        if (emission.Data?.TryGetValue(TargetPositionKey, out var posObj) == true &&
            posObj is Vector3 vec)
        {
            return vec;
        }

        return null;
    }

    /// <summary>
    /// Creates a new emission with a target position added to the data dictionary.
    /// </summary>
    /// <param name="emission">The original emission.</param>
    /// <param name="position">The target position to add.</param>
    /// <returns>A new emission with the position in the data dictionary.</returns>
    public static IntentEmission WithTargetPosition(this IntentEmission emission, Vector3 position)
    {
        var data = emission.Data != null
            ? new Dictionary<string, object>(emission.Data) { [TargetPositionKey] = position }
            : new Dictionary<string, object> { [TargetPositionKey] = position };

        return emission with { Data = data };
    }

    /// <summary>
    /// Creates an IntentEmission with a target position.
    /// </summary>
    /// <param name="channel">The logical channel.</param>
    /// <param name="intent">The intent value.</param>
    /// <param name="urgency">The urgency level.</param>
    /// <param name="targetPosition">The target position.</param>
    /// <param name="target">Optional target entity ID.</param>
    /// <returns>A new emission with the position data.</returns>
    public static IntentEmission CreateWithPosition(
        string channel,
        string intent,
        float urgency,
        Vector3 targetPosition,
        Guid? target = null)
    {
        var data = new Dictionary<string, object> { [TargetPositionKey] = targetPosition };
        return new IntentEmission(channel, intent, urgency, target, data);
    }
}
