// =============================================================================
// Behavior Output
// Structured output from a single behavior model evaluation.
// =============================================================================

using BeyondImmersion.Bannou.Client.SDK.Behavior.Runtime;
using System.Numerics;

namespace BeyondImmersion.Bannou.Client.SDK.Behavior.Intent;

/// <summary>
/// Structured output from a single behavior model evaluation.
/// Extracted from raw output buffer based on IntentSlotLayout conventions.
/// </summary>
/// <remarks>
/// <para>
/// Each behavior model (combat, movement, interaction, idle) produces one
/// BehaviorOutput. The IntentMerger combines these into a single MergedIntent.
/// </para>
/// <para>
/// Output extraction uses the standardized slot layout:
/// - Slots 0-1: Action intent + urgency
/// - Slots 2-3: Locomotion intent + urgency
/// - Slots 4-5: Attention target + urgency
/// - Slots 6-7: Stance intent + urgency
/// - Slots 8-9: Vocalization intent + urgency
/// - Slots 10-12: Locomotion target Vector3 (optional)
/// - Slot 13: Action target entity ID (optional)
/// - Slot 14: Attention target entity ID (optional)
/// </para>
/// </remarks>
public readonly struct BehaviorOutput
{
    // =========================================================================
    // ACTION CHANNEL
    // =========================================================================

    /// <summary>
    /// Action intent name (e.g., "attack", "block", "use", "talk").
    /// Null if no action output.
    /// </summary>
    public string? ActionIntent { get; init; }

    /// <summary>
    /// Action channel urgency [0.0 - 1.0].
    /// Higher urgency wins in conflict resolution.
    /// </summary>
    public float ActionUrgency { get; init; }

    /// <summary>
    /// Target entity for the action (e.g., attack target, item to pick up).
    /// Null if action doesn't require a target.
    /// </summary>
    public Guid? ActionTarget { get; init; }

    // =========================================================================
    // LOCOMOTION CHANNEL
    // =========================================================================

    /// <summary>
    /// Locomotion intent name (e.g., "walk_to", "run_away", "strafe").
    /// Null if no locomotion output.
    /// </summary>
    public string? LocomotionIntent { get; init; }

    /// <summary>
    /// Locomotion channel urgency [0.0 - 1.0].
    /// Used for blending when multiple models contribute locomotion.
    /// </summary>
    public float LocomotionUrgency { get; init; }

    /// <summary>
    /// Target position for locomotion in world space.
    /// Null if locomotion doesn't specify a target.
    /// </summary>
    public Vector3? LocomotionTarget { get; init; }

    // =========================================================================
    // ATTENTION CHANNEL
    // =========================================================================

    /// <summary>
    /// Target entity for attention/gaze direction.
    /// Null if not looking at a specific entity.
    /// </summary>
    public Guid? AttentionTarget { get; init; }

    /// <summary>
    /// Attention channel urgency [0.0 - 1.0].
    /// Higher urgency overrides lower urgency attention targets.
    /// </summary>
    public float AttentionUrgency { get; init; }

    // =========================================================================
    // STANCE CHANNEL
    // =========================================================================

    /// <summary>
    /// Stance intent name (e.g., "combat_ready", "defensive", "relaxed").
    /// Null if no stance change.
    /// </summary>
    public string? StanceIntent { get; init; }

    /// <summary>
    /// Stance channel urgency [0.0 - 1.0].
    /// </summary>
    public float StanceUrgency { get; init; }

    // =========================================================================
    // VOCALIZATION CHANNEL
    // =========================================================================

    /// <summary>
    /// Vocalization intent name (e.g., "battle_cry", "greeting", "pain").
    /// Null if no vocalization.
    /// </summary>
    public string? VocalizationIntent { get; init; }

    /// <summary>
    /// Vocalization channel urgency [0.0 - 1.0].
    /// </summary>
    public float VocalizationUrgency { get; init; }

    // =========================================================================
    // FACTORY METHODS
    // =========================================================================

    /// <summary>
    /// Extracts BehaviorOutput from raw interpreter output buffer.
    /// </summary>
    /// <param name="outputState">Raw output buffer from interpreter.</param>
    /// <param name="stringTable">String table for intent name lookup.</param>
    /// <returns>Structured behavior output.</returns>
    public static BehaviorOutput FromOutputBuffer(
        ReadOnlySpan<double> outputState,
        IReadOnlyList<string> stringTable)
    {
        return new BehaviorOutput
        {
            // Action channel
            ActionIntent = GetIntent(outputState, stringTable, IntentSlotLayout.IntentSlot(IntentChannel.Action)),
            ActionUrgency = GetUrgency(outputState, IntentSlotLayout.UrgencySlot(IntentChannel.Action)),
            ActionTarget = GetGuid(outputState, IntentSlotLayout.ActionTargetSlot),

            // Locomotion channel
            LocomotionIntent = GetIntent(outputState, stringTable, IntentSlotLayout.IntentSlot(IntentChannel.Locomotion)),
            LocomotionUrgency = GetUrgency(outputState, IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion)),
            LocomotionTarget = GetVector3(outputState, IntentSlotLayout.LocomotionTargetXSlot),

            // Attention channel
            AttentionTarget = GetGuid(outputState, IntentSlotLayout.AttentionTargetSlot),
            AttentionUrgency = GetUrgency(outputState, IntentSlotLayout.UrgencySlot(IntentChannel.Attention)),

            // Stance channel
            StanceIntent = GetIntent(outputState, stringTable, IntentSlotLayout.IntentSlot(IntentChannel.Stance)),
            StanceUrgency = GetUrgency(outputState, IntentSlotLayout.UrgencySlot(IntentChannel.Stance)),

            // Vocalization channel
            VocalizationIntent = GetIntent(outputState, stringTable, IntentSlotLayout.IntentSlot(IntentChannel.Vocalization)),
            VocalizationUrgency = GetUrgency(outputState, IntentSlotLayout.UrgencySlot(IntentChannel.Vocalization)),
        };
    }

    /// <summary>
    /// Safely gets intent string from output slot.
    /// </summary>
    private static string? GetIntent(ReadOnlySpan<double> outputState, IReadOnlyList<string> stringTable, int slot)
    {
        if (slot >= outputState.Length)
            return null;

        var idx = (int)outputState[slot];
        if (idx < 0 || idx >= stringTable.Count)
            return null;

        return stringTable[idx];
    }

    /// <summary>
    /// Safely gets urgency value from output slot.
    /// </summary>
    private static float GetUrgency(ReadOnlySpan<double> outputState, int slot)
    {
        if (slot >= outputState.Length)
            return 0f;

        return (float)Math.Clamp(outputState[slot], 0.0, 1.0);
    }

    /// <summary>
    /// Safely gets Guid from output slot.
    /// </summary>
    private static Guid? GetGuid(ReadOnlySpan<double> outputState, int slot)
    {
        if (slot >= outputState.Length)
            return null;

        var value = outputState[slot];
        if (value == 0)
            return null;

        // GUIDs are stored as a single double representing the hash or index
        // For now, we interpret as entity ID lookup (game provides actual mapping)
        // A value of 0 means no target
        return value > 0 ? new Guid((int)value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) : null;
    }

    /// <summary>
    /// Safely gets Vector3 from consecutive output slots.
    /// </summary>
    private static Vector3? GetVector3(ReadOnlySpan<double> outputState, int baseSlot)
    {
        if (baseSlot + 2 >= outputState.Length)
            return null;

        var x = (float)outputState[baseSlot];
        var y = (float)outputState[baseSlot + 1];
        var z = (float)outputState[baseSlot + 2];

        // Check if any component is non-zero (zero vector = no target)
        if (x == 0 && y == 0 && z == 0)
            return null;

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Creates an empty behavior output (no intents).
    /// </summary>
    public static BehaviorOutput Empty => default;

    /// <summary>
    /// Whether this output has any non-zero urgency intents.
    /// </summary>
    public bool HasAnyIntent =>
        ActionUrgency > 0 ||
        LocomotionUrgency > 0 ||
        AttentionUrgency > 0 ||
        StanceUrgency > 0 ||
        VocalizationUrgency > 0;

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new List<string>();

        if (ActionIntent != null)
            parts.Add($"Action({ActionIntent}, {ActionUrgency:F2})");
        if (LocomotionIntent != null)
            parts.Add($"Locomotion({LocomotionIntent}, {LocomotionUrgency:F2})");
        if (AttentionUrgency > 0)
            parts.Add($"Attention({AttentionTarget}, {AttentionUrgency:F2})");
        if (StanceIntent != null)
            parts.Add($"Stance({StanceIntent}, {StanceUrgency:F2})");
        if (VocalizationIntent != null)
            parts.Add($"Vocalization({VocalizationIntent}, {VocalizationUrgency:F2})");

        return parts.Count > 0
            ? $"BehaviorOutput[{string.Join(", ", parts)}]"
            : "BehaviorOutput.Empty";
    }
}
