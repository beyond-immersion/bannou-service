// =============================================================================
// Physical Channel Extensions
// Extension methods for behavior execution.
// Uses core PhysicalChannel enum from bannou-service.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Runtime;

namespace BeyondImmersion.BannouService.Behavior.Archetypes;

/// <summary>
/// Extension methods for physical channel conversion.
/// </summary>
public static class PhysicalChannelExtensions
{
    /// <summary>
    /// Converts a physical channel to its bytecode IntentChannel equivalent.
    /// </summary>
    /// <param name="channel">The physical channel.</param>
    /// <returns>The corresponding IntentChannel, or null if not supported in bytecode.</returns>
    public static IntentChannel? ToIntentChannel(this PhysicalChannel channel)
    {
        return channel switch
        {
            PhysicalChannel.Action => IntentChannel.Action,
            PhysicalChannel.Locomotion => IntentChannel.Locomotion,
            PhysicalChannel.Attention => IntentChannel.Attention,
            PhysicalChannel.Stance => IntentChannel.Stance,
            PhysicalChannel.Vocalization => IntentChannel.Vocalization,
            // Expression is not yet in bytecode IntentChannel
            PhysicalChannel.Expression => null,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the output slot index for a physical channel.
    /// </summary>
    /// <param name="channel">The physical channel.</param>
    /// <returns>The output buffer slot index.</returns>
    public static int GetSlotIndex(this PhysicalChannel channel)
    {
        return (int)channel * 2; // Each channel uses 2 slots (intent + urgency)
    }

    /// <summary>
    /// Checks if this physical channel is supported in the current bytecode.
    /// </summary>
    /// <param name="channel">The physical channel.</param>
    /// <returns>True if the channel is supported in bytecode.</returns>
    public static bool IsBytecodeSupported(this PhysicalChannel channel)
    {
        return channel <= PhysicalChannel.Vocalization;
    }
}
