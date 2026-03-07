// =============================================================================
// Intent Channel Types
// Fixed behavior model types and output slot layout conventions.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior.Runtime;

namespace BeyondImmersion.Bannou.Client.Behavior.Intent;

/// <summary>
/// The fixed set of behavior model types for multi-model coordination.
/// Each character can have up to 4 simultaneously-active behavior models.
/// </summary>
/// <remarks>
/// <para>
/// These are intentionally fixed (not configurable) for simplicity.
/// The IntentMerger knows how to blend outputs from these 4 model types.
/// </para>
/// <para>
/// Mapping to IntentChannels:
/// - Any model can emit to any channel
/// - Combat typically emits to Action, Stance
/// - Movement emits to Locomotion
/// - Interaction emits to Action, Attention
/// - Idle emits to all channels with low urgency
/// </para>
/// </remarks>
public enum BehaviorModelType : byte
{
    /// <summary>Attack, defend, combo management.</summary>
    Combat = 0,

    /// <summary>Navigation, pathfinding, steering.</summary>
    Movement = 1,

    /// <summary>Item use, dialogue, environment interaction.</summary>
    Interaction = 2,

    /// <summary>Ambient behavior, waiting, breathing.</summary>
    Idle = 3,
}

/// <summary>
/// Output slot layout convention for EmitIntent opcode.
/// Each intent channel uses 2 consecutive output slots: [action_index, urgency].
/// </summary>
/// <remarks>
/// <para>
/// Output buffer layout (10 slots minimum):
/// <code>
/// [0] Action intent (string table index)
/// [1] Action urgency (0.0 - 1.0)
/// [2] Locomotion intent (string table index)
/// [3] Locomotion urgency (0.0 - 1.0)
/// [4] Attention target (entity ID or 0)
/// [5] Attention urgency (0.0 - 1.0)
/// [6] Stance intent (string table index)
/// [7] Stance urgency (0.0 - 1.0)
/// [8] Vocalization intent (string table index)
/// [9] Vocalization urgency (0.0 - 1.0)
/// </code>
/// </para>
/// <para>
/// Extended slots (10+) can be used for:
/// - Locomotion target position (Vector3: slots 10-12)
/// - Action target entity (slot 13)
/// - Additional channel-specific data
/// </para>
/// </remarks>
public static class IntentSlotLayout
{
    /// <summary>
    /// Number of output slots per intent channel (intent + urgency).
    /// </summary>
    public const int SlotsPerChannel = 2;

    /// <summary>
    /// Total number of channels (Action, Locomotion, Attention, Stance, Vocalization).
    /// </summary>
    public const int ChannelCount = 5;

    /// <summary>
    /// Minimum required output buffer size for all intent channels.
    /// </summary>
    public const int MinimumOutputSlots = ChannelCount * SlotsPerChannel;

    /// <summary>
    /// Slot index for locomotion target X coordinate.
    /// </summary>
    public const int LocomotionTargetXSlot = 10;

    /// <summary>
    /// Slot index for locomotion target Y coordinate.
    /// </summary>
    public const int LocomotionTargetYSlot = 11;

    /// <summary>
    /// Slot index for locomotion target Z coordinate.
    /// </summary>
    public const int LocomotionTargetZSlot = 12;

    /// <summary>
    /// Slot index for action target entity ID.
    /// </summary>
    public const int ActionTargetSlot = 13;

    /// <summary>
    /// Slot index for attention target entity ID.
    /// </summary>
    public const int AttentionTargetSlot = 14;

    /// <summary>
    /// Gets the output slot index for a channel's intent value.
    /// </summary>
    /// <param name="channel">The intent channel.</param>
    /// <returns>The slot index for the intent string table index.</returns>
    public static int IntentSlot(IntentChannel channel) => (int)channel * SlotsPerChannel;

    /// <summary>
    /// Gets the output slot index for a channel's urgency value.
    /// </summary>
    /// <param name="channel">The intent channel.</param>
    /// <returns>The slot index for the urgency value.</returns>
    public static int UrgencySlot(IntentChannel channel) => (int)channel * SlotsPerChannel + 1;
}
