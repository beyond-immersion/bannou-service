// =============================================================================
// Behavior Output Mask
// Filters behavior stack outputs based on control state.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Masks behavior output when entity is under cinematic/player control.
/// </summary>
public interface IBehaviorOutputMask
{
    /// <summary>
    /// Filters merged intent output based on control state.
    /// </summary>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="emissions">The behavior output emissions.</param>
    /// <returns>Filtered emissions (only allowed channels pass through).</returns>
    IReadOnlyList<IntentEmission> ApplyMask(
        Guid entityId,
        IReadOnlyList<IntentEmission> emissions);
}

/// <summary>
/// Default implementation of behavior output mask.
/// Uses control gate registry to determine what channels are allowed.
/// </summary>
public sealed class BehaviorOutputMask : IBehaviorOutputMask
{
    private readonly IControlGateRegistry _gates;

    /// <summary>
    /// Creates a new behavior output mask.
    /// </summary>
    /// <param name="gates">The control gate registry.</param>
    public BehaviorOutputMask(IControlGateRegistry gates)
    {
        _gates = gates;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IntentEmission> ApplyMask(
        Guid entityId,
        IReadOnlyList<IntentEmission> emissions)
    {
        if (emissions.Count == 0)
        {
            return emissions;
        }

        var gate = _gates.Get(entityId);

        // No gate or gate accepts behavior output - no masking needed
        if (gate == null || gate.AcceptsBehaviorOutput)
        {
            return emissions;
        }

        // Gate blocks behavior output - check allowed channels
        var allowedChannels = gate.BehaviorInputChannels;

        // If no channels allowed, discard all
        if (allowedChannels.Count == 0)
        {
            return Array.Empty<IntentEmission>();
        }

        // Filter to only allowed channels
        var filtered = new List<IntentEmission>(emissions.Count);
        foreach (var emission in emissions)
        {
            if (allowedChannels.Contains(emission.Channel))
            {
                filtered.Add(emission);
            }
        }

        return filtered;
    }
}
