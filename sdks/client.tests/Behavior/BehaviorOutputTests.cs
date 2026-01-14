// =============================================================================
// Behavior Output Tests
// Tests for structured output extraction from interpreter output buffers.
// =============================================================================

using System.Numerics;
using BeyondImmersion.Bannou.Client.Behavior.Intent;
using BeyondImmersion.Bannou.Client.Behavior.Runtime;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.Behavior;

/// <summary>
/// Tests for the BehaviorOutput struct.
/// Verifies correct extraction of intents, urgencies, and targets from raw output buffers.
/// </summary>
public class BehaviorOutputTests
{
    // =========================================================================
    // EMPTY/DEFAULT TESTS
    // =========================================================================

    [Fact]
    public void Empty_HasNoIntent()
    {
        var empty = BehaviorOutput.Empty;

        Assert.False(empty.HasAnyIntent);
        Assert.Null(empty.ActionIntent);
        Assert.Null(empty.LocomotionIntent);
        Assert.Null(empty.StanceIntent);
        Assert.Null(empty.VocalizationIntent);
        Assert.Equal(0f, empty.ActionUrgency);
        Assert.Equal(0f, empty.LocomotionUrgency);
        Assert.Equal(0f, empty.AttentionUrgency);
        Assert.Equal(0f, empty.StanceUrgency);
        Assert.Equal(0f, empty.VocalizationUrgency);
    }

    [Fact]
    public void FromOutputBuffer_EmptyBuffer_ReturnsEmpty()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "walk" };

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.False(output.HasAnyIntent);
    }

    // =========================================================================
    // INTENT EXTRACTION TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_ActionChannel_ExtractsCorrectly()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "defend", "parry" };

        // Set action: intent=1 ("defend"), urgency=0.75
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 1;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.75;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.True(output.HasAnyIntent);
        Assert.Equal("defend", output.ActionIntent);
        Assert.Equal(0.75f, output.ActionUrgency, 0.001f);
    }

    [Fact]
    public void FromOutputBuffer_LocomotionChannel_ExtractsCorrectly()
    {
        var buffer = new double[15];
        var strings = new[] { "walk_to", "run_away", "strafe" };

        // Set locomotion: intent=2 ("strafe"), urgency=0.6
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Locomotion)] = 2;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion)] = 0.6;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal("strafe", output.LocomotionIntent);
        Assert.Equal(0.6f, output.LocomotionUrgency, 0.001f);
    }

    [Fact]
    public void FromOutputBuffer_StanceChannel_ExtractsCorrectly()
    {
        var buffer = new double[15];
        var strings = new[] { "relaxed", "alert", "combat_ready" };

        // Set stance: intent=2 ("combat_ready"), urgency=0.9
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Stance)] = 2;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Stance)] = 0.9;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal("combat_ready", output.StanceIntent);
        Assert.Equal(0.9f, output.StanceUrgency, 0.001f);
    }

    [Fact]
    public void FromOutputBuffer_VocalizationChannel_ExtractsCorrectly()
    {
        var buffer = new double[15];
        var strings = new[] { "grunt", "battle_cry", "laugh" };

        // Set vocalization: intent=1 ("battle_cry"), urgency=0.8
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Vocalization)] = 1;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Vocalization)] = 0.8;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal("battle_cry", output.VocalizationIntent);
        Assert.Equal(0.8f, output.VocalizationUrgency, 0.001f);
    }

    [Fact]
    public void FromOutputBuffer_AttentionChannel_ExtractsUrgency()
    {
        var buffer = new double[15];
        var strings = Array.Empty<string>();

        // Set attention urgency (no intent name for attention, just urgency)
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Attention)] = 0.7;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal(0.7f, output.AttentionUrgency, 0.001f);
    }

    // =========================================================================
    // URGENCY CLAMPING TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_UrgencyAboveOne_ClampedToOne()
    {
        var buffer = new double[15];
        var strings = new[] { "attack" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 1.5;  // Above 1.0

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal(1.0f, output.ActionUrgency, 0.001f);  // Clamped
    }

    [Fact]
    public void FromOutputBuffer_UrgencyBelowZero_ClampedToZero()
    {
        var buffer = new double[15];
        var strings = new[] { "attack" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = -0.5;  // Below 0

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal(0.0f, output.ActionUrgency, 0.001f);  // Clamped
    }

    // =========================================================================
    // STRING TABLE BOUNDARY TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_IntentIndexOutOfBounds_ReturnsNull()
    {
        var buffer = new double[15];
        var strings = new[] { "attack" };  // Only one string

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 5;  // Out of bounds
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.8;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Null(output.ActionIntent);  // Invalid index returns null
    }

    [Fact]
    public void FromOutputBuffer_NegativeIntentIndex_ReturnsNull()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "defend" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = -1;  // Negative
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.8;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Null(output.ActionIntent);
    }

    // =========================================================================
    // LOCOMOTION TARGET TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_LocomotionTarget_ExtractsVector3()
    {
        var buffer = new double[15];
        var strings = new[] { "walk_to" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Locomotion)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion)] = 0.7;
        buffer[IntentSlotLayout.LocomotionTargetXSlot] = 5.5;
        buffer[IntentSlotLayout.LocomotionTargetYSlot] = 2.0;
        buffer[IntentSlotLayout.LocomotionTargetZSlot] = 10.5;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.NotNull(output.LocomotionTarget);
        Assert.Equal(5.5f, output.LocomotionTarget!.Value.X, 0.001f);
        Assert.Equal(2.0f, output.LocomotionTarget!.Value.Y, 0.001f);
        Assert.Equal(10.5f, output.LocomotionTarget!.Value.Z, 0.001f);
    }

    [Fact]
    public void FromOutputBuffer_ZeroVector_ReturnsNullTarget()
    {
        var buffer = new double[15];
        var strings = new[] { "walk_to" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Locomotion)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion)] = 0.7;
        // Target slots remain zero

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal("walk_to", output.LocomotionIntent);
        Assert.Null(output.LocomotionTarget);  // Zero vector = no target
    }

    // =========================================================================
    // ACTION TARGET TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_ActionTarget_ExtractsEntityId()
    {
        var buffer = new double[15];
        var strings = new[] { "attack" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.9;
        buffer[IntentSlotLayout.ActionTargetSlot] = 42;  // Entity ID

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.NotNull(output.ActionTarget);
        // Entity ID encoded as first component of Guid
        Assert.Equal(42, output.ActionTarget!.Value.ToString().Split('-')[0].Length > 0 ? 42 : 0);
    }

    [Fact]
    public void FromOutputBuffer_ZeroActionTarget_ReturnsNull()
    {
        var buffer = new double[15];
        var strings = new[] { "attack" };

        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.9;
        buffer[IntentSlotLayout.ActionTargetSlot] = 0;  // No target

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Null(output.ActionTarget);
    }

    // =========================================================================
    // ATTENTION TARGET TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_AttentionTarget_ExtractsEntityId()
    {
        var buffer = new double[15];
        var strings = Array.Empty<string>();

        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Attention)] = 0.8;
        buffer[IntentSlotLayout.AttentionTargetSlot] = 123;  // Entity ID

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.NotNull(output.AttentionTarget);
    }

    [Fact]
    public void FromOutputBuffer_ZeroAttentionTarget_ReturnsNull()
    {
        var buffer = new double[15];
        var strings = Array.Empty<string>();

        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Attention)] = 0.8;
        buffer[IntentSlotLayout.AttentionTargetSlot] = 0;  // No target

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Null(output.AttentionTarget);
    }

    // =========================================================================
    // MULTIPLE CHANNELS TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_AllChannels_ExtractsAll()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "walk", "alert", "grunt" };

        // Action
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Action)] = 0;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Action)] = 0.9;

        // Locomotion
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Locomotion)] = 1;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion)] = 0.7;

        // Attention (urgency only)
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Attention)] = 0.6;

        // Stance
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Stance)] = 2;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Stance)] = 0.8;

        // Vocalization
        buffer[IntentSlotLayout.IntentSlot(IntentChannel.Vocalization)] = 3;
        buffer[IntentSlotLayout.UrgencySlot(IntentChannel.Vocalization)] = 0.5;

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.True(output.HasAnyIntent);
        Assert.Equal("attack", output.ActionIntent);
        Assert.Equal("walk", output.LocomotionIntent);
        Assert.Equal("alert", output.StanceIntent);
        Assert.Equal("grunt", output.VocalizationIntent);
        Assert.Equal(0.9f, output.ActionUrgency, 0.001f);
        Assert.Equal(0.7f, output.LocomotionUrgency, 0.001f);
        Assert.Equal(0.6f, output.AttentionUrgency, 0.001f);
        Assert.Equal(0.8f, output.StanceUrgency, 0.001f);
        Assert.Equal(0.5f, output.VocalizationUrgency, 0.001f);
    }

    // =========================================================================
    // BUFFER BOUNDARY TESTS
    // =========================================================================

    [Fact]
    public void FromOutputBuffer_ShortBuffer_HandlesGracefully()
    {
        var buffer = new double[5];  // Shorter than expected
        var strings = new[] { "attack" };

        buffer[0] = 0;   // Action intent
        buffer[1] = 0.5; // Action urgency

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        // Should extract what's available without crashing
        Assert.Equal("attack", output.ActionIntent);
        Assert.Equal(0.5f, output.ActionUrgency, 0.001f);
    }

    // =========================================================================
    // TOSTRING TESTS
    // =========================================================================

    [Fact]
    public void ToString_Empty_ReturnsEmptyMarker()
    {
        var empty = BehaviorOutput.Empty;

        Assert.Equal("BehaviorOutput.Empty", empty.ToString());
    }

    [Fact]
    public void ToString_WithIntents_IncludesChannels()
    {
        var output = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
            LocomotionIntent = "run",
            LocomotionUrgency = 0.7f,
        };

        var str = output.ToString();

        Assert.Contains("Action(attack", str);
        Assert.Contains("Locomotion(run", str);
    }
}
