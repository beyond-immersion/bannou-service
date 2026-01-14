// =============================================================================
// Intent Merger Tests
// Tests for urgency-based conflict resolution in multi-model coordination.
// =============================================================================

using BeyondImmersion.Bannou.Client.Behavior.Intent;
using BeyondImmersion.Bannou.Client.Behavior.Runtime;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Tests.Behavior;

/// <summary>
/// Tests for the IntentMerger class.
/// Verifies urgency-based conflict resolution across all intent channels.
/// </summary>
public class IntentMergerTests
{
    private readonly IntentMerger _merger = new();

    // =========================================================================
    // EMPTY INPUT TESTS
    // =========================================================================

    [Fact]
    public void Merge_AllNullInputs_ReturnsEmptyIntent()
    {
        var result = _merger.Merge(null, null, null, null);

        Assert.False(result.HasAnyIntent);
        Assert.Null(result.Action);
        Assert.False(result.Locomotion.IsValid);
        Assert.False(result.Attention.IsValid);
        Assert.Null(result.Stance);
        Assert.Null(result.Vocalization);
    }

    [Fact]
    public void Merge_AllEmptyOutputs_ReturnsEmptyIntent()
    {
        var empty = BehaviorOutput.Empty;

        var result = _merger.Merge(empty, empty, empty, empty);

        Assert.False(result.HasAnyIntent);
    }

    // =========================================================================
    // SINGLE MODEL CONTRIBUTION TESTS
    // =========================================================================

    [Fact]
    public void Merge_SingleCombatAction_ReturnsAction()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.8f,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Equal("attack", result.Action);
        Assert.Equal(0.8f, result.ActionUrgency);
    }

    [Fact]
    public void Merge_SingleMovementLocomotion_ReturnsLocomotion()
    {
        var target = new Vector3(10, 0, 5);
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "walk_to",
            LocomotionUrgency = 0.6f,
            LocomotionTarget = target,
        };

        var result = _merger.Merge(null, movement, null, null);

        Assert.True(result.Locomotion.IsValid);
        Assert.Equal("walk_to", result.Locomotion.Intent);
        Assert.Equal(target, result.Locomotion.Target);
        Assert.Equal(0.6f, result.Locomotion.Urgency);
    }

    [Fact]
    public void Merge_SingleInteractionAttention_ReturnsAttention()
    {
        var target = Guid.NewGuid();
        var interaction = new BehaviorOutput
        {
            AttentionTarget = target,
            AttentionUrgency = 0.7f,
        };

        var result = _merger.Merge(null, null, interaction, null);

        Assert.True(result.Attention.IsValid);
        Assert.Equal(target, result.Attention.PrimaryTarget);
        Assert.Equal(0.7f, result.Attention.PrimaryUrgency);
        Assert.False(result.Attention.HasSecondaryTarget);
        Assert.Equal(1f, result.Attention.BlendWeight);  // Single target = 100% weight
    }

    [Fact]
    public void Merge_SingleIdleStance_ReturnsStance()
    {
        var idle = new BehaviorOutput
        {
            StanceIntent = "relaxed",
            StanceUrgency = 0.3f,
        };

        var result = _merger.Merge(null, null, null, idle);

        Assert.Equal("relaxed", result.Stance);
        Assert.Equal(0.3f, result.StanceUrgency);
    }

    [Fact]
    public void Merge_SingleVocalization_ReturnsVocalization()
    {
        var combat = new BehaviorOutput
        {
            VocalizationIntent = "battle_cry",
            VocalizationUrgency = 0.9f,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Equal("battle_cry", result.Vocalization);
        Assert.Equal(0.9f, result.VocalizationUrgency);
    }

    // =========================================================================
    // CONFLICT RESOLUTION TESTS (Exclusive Channels)
    // =========================================================================

    [Fact]
    public void Merge_ActionConflict_HighestUrgencyWins()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
        };
        var interaction = new BehaviorOutput
        {
            ActionIntent = "talk",
            ActionUrgency = 0.5f,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        Assert.Equal("attack", result.Action);
        Assert.Equal(0.9f, result.ActionUrgency);
    }

    [Fact]
    public void Merge_ActionConflict_InteractionWinsWithHigherUrgency()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.4f,
        };
        var interaction = new BehaviorOutput
        {
            ActionIntent = "talk",
            ActionUrgency = 0.7f,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        Assert.Equal("talk", result.Action);
        Assert.Equal(0.7f, result.ActionUrgency);
    }

    [Fact]
    public void Merge_StanceConflict_HighestUrgencyWins()
    {
        var combat = new BehaviorOutput
        {
            StanceIntent = "combat_ready",
            StanceUrgency = 0.8f,
        };
        var idle = new BehaviorOutput
        {
            StanceIntent = "relaxed",
            StanceUrgency = 0.2f,
        };

        var result = _merger.Merge(combat, null, null, idle);

        Assert.Equal("combat_ready", result.Stance);
        Assert.Equal(0.8f, result.StanceUrgency);
    }

    [Fact]
    public void Merge_VocalizationConflict_HighestUrgencyWins()
    {
        var combat = new BehaviorOutput
        {
            VocalizationIntent = "battle_cry",
            VocalizationUrgency = 0.6f,
        };
        var idle = new BehaviorOutput
        {
            VocalizationIntent = "humming",
            VocalizationUrgency = 0.1f,
        };

        var result = _merger.Merge(combat, null, null, idle);

        Assert.Equal("battle_cry", result.Vocalization);
    }

    [Fact]
    public void Merge_AttentionConflict_BlendsWithPrimaryAndSecondary()
    {
        var enemy = Guid.NewGuid();
        var friend = Guid.NewGuid();

        var combat = new BehaviorOutput
        {
            AttentionTarget = enemy,
            AttentionUrgency = 0.9f,
        };
        var interaction = new BehaviorOutput
        {
            AttentionTarget = friend,
            AttentionUrgency = 0.3f,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        // Attention now blends: primary is higher urgency, secondary is lower
        Assert.True(result.Attention.IsValid);
        Assert.True(result.Attention.HasSecondaryTarget);
        Assert.Equal(enemy, result.Attention.PrimaryTarget);
        Assert.Equal(0.9f, result.Attention.PrimaryUrgency);
        Assert.Equal(friend, result.Attention.SecondaryTarget);
        Assert.Equal(0.3f, result.Attention.SecondaryUrgency);

        // Blend weight = 0.9 / (0.9 + 0.3) = 0.75
        Assert.Equal(0.75f, result.Attention.BlendWeight, 0.01f);
    }

    // =========================================================================
    // ATTENTION BLENDING TESTS
    // =========================================================================

    [Fact]
    public void Merge_AttentionEqualUrgency_EqualBlendWeight()
    {
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();

        var combat = new BehaviorOutput
        {
            AttentionTarget = target1,
            AttentionUrgency = 0.5f,
        };
        var interaction = new BehaviorOutput
        {
            AttentionTarget = target2,
            AttentionUrgency = 0.5f,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        Assert.True(result.Attention.IsValid);
        Assert.True(result.Attention.HasSecondaryTarget);
        // Equal urgency = 50% blend weight
        Assert.Equal(0.5f, result.Attention.BlendWeight, 0.01f);
    }

    [Fact]
    public void Merge_ThreeAttentionContributors_UsesTopTwo()
    {
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();
        var target3 = Guid.NewGuid();

        var combat = new BehaviorOutput
        {
            AttentionTarget = target1,
            AttentionUrgency = 0.9f,
        };
        var movement = new BehaviorOutput
        {
            AttentionTarget = target2,
            AttentionUrgency = 0.5f,
        };
        var idle = new BehaviorOutput
        {
            AttentionTarget = target3,
            AttentionUrgency = 0.2f,
        };

        var result = _merger.Merge(combat, movement, null, idle);

        // Only top 2 are included in blending
        Assert.Equal(target1, result.Attention.PrimaryTarget);
        Assert.Equal(target2, result.Attention.SecondaryTarget);
        // Third target (idle) is not included
    }

    [Fact]
    public void AttentionIntent_CreateSingle_HasNoSecondary()
    {
        var target = Guid.NewGuid();
        var intent = AttentionIntent.CreateSingle(target, 0.8f);

        Assert.True(intent.IsValid);
        Assert.Equal(target, intent.PrimaryTarget);
        Assert.Equal(0.8f, intent.PrimaryUrgency);
        Assert.False(intent.HasSecondaryTarget);
        Assert.Equal(1f, intent.BlendWeight);
    }

    [Fact]
    public void AttentionIntent_CreateBlended_CalculatesCorrectWeight()
    {
        var primary = Guid.NewGuid();
        var secondary = Guid.NewGuid();
        var intent = AttentionIntent.CreateBlended(primary, 0.8f, secondary, 0.2f);

        Assert.True(intent.IsValid);
        Assert.True(intent.HasSecondaryTarget);
        Assert.Equal(primary, intent.PrimaryTarget);
        Assert.Equal(secondary, intent.SecondaryTarget);
        // 0.8 / (0.8 + 0.2) = 0.8
        Assert.Equal(0.8f, intent.BlendWeight, 0.01f);
    }

    [Fact]
    public void AttentionIntent_None_IsNotValid()
    {
        var none = AttentionIntent.None;

        Assert.False(none.IsValid);
        Assert.Null(none.PrimaryTarget);
        Assert.False(none.HasSecondaryTarget);
    }

    // =========================================================================
    // LOCOMOTION BLENDING TESTS
    // =========================================================================

    [Fact]
    public void Merge_TwoLocomotionContributors_BlendsTargets()
    {
        var target1 = new Vector3(10, 0, 0);
        var target2 = new Vector3(0, 0, 10);

        var combat = new BehaviorOutput
        {
            LocomotionIntent = "strafe",
            LocomotionUrgency = 0.5f,
            LocomotionTarget = target1,
        };
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "walk_to",
            LocomotionUrgency = 0.5f,
            LocomotionTarget = target2,
        };

        var result = _merger.Merge(combat, movement, null, null);

        Assert.True(result.Locomotion.IsValid);
        // Equal urgency = equal weight blend
        Assert.NotNull(result.Locomotion.Target);
        Assert.Equal(5f, result.Locomotion.Target!.Value.X, 0.01f);
        Assert.Equal(5f, result.Locomotion.Target!.Value.Z, 0.01f);
    }

    [Fact]
    public void Merge_LocomotionWeightedBlend_HigherUrgencyDominates()
    {
        var target1 = new Vector3(10, 0, 0);
        var target2 = new Vector3(0, 0, 10);

        var combat = new BehaviorOutput
        {
            LocomotionIntent = "strafe",
            LocomotionUrgency = 0.8f,  // Higher weight
            LocomotionTarget = target1,
        };
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "walk_to",
            LocomotionUrgency = 0.2f,  // Lower weight
            LocomotionTarget = target2,
        };

        var result = _merger.Merge(combat, movement, null, null);

        Assert.True(result.Locomotion.IsValid);
        // 80/20 weight = target closer to combat's target
        Assert.NotNull(result.Locomotion.Target);
        Assert.True(result.Locomotion.Target!.Value.X > 5f);  // Closer to 10
        Assert.True(result.Locomotion.Target!.Value.Z < 5f);  // Closer to 0
    }

    [Fact]
    public void Merge_LocomotionWithoutTarget_UsesIntentName()
    {
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "stop",
            LocomotionUrgency = 0.5f,
            LocomotionTarget = null,
        };

        var result = _merger.Merge(null, movement, null, null);

        Assert.True(result.Locomotion.IsValid);
        Assert.Equal("stop", result.Locomotion.Intent);
        Assert.Null(result.Locomotion.Target);
    }

    [Fact]
    public void Merge_SingleLocomotion_UsesDirectly()
    {
        var target = new Vector3(5, 0, 5);
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "run",
            LocomotionUrgency = 0.7f,
            LocomotionTarget = target,
        };

        var result = _merger.Merge(null, movement, null, null);

        Assert.Equal("run", result.Locomotion.Intent);
        Assert.Equal(target, result.Locomotion.Target);
        Assert.Equal(0.7f, result.Locomotion.Urgency);
    }

    // =========================================================================
    // ZERO URGENCY EXCLUSION TESTS
    // =========================================================================

    [Fact]
    public void Merge_ZeroUrgencyAction_Ignored()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0f,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Null(result.Action);
        Assert.Equal(0f, result.ActionUrgency);
    }

    [Fact]
    public void Merge_BelowThresholdUrgency_Ignored()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.0005f,  // Below threshold (0.001)
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Null(result.Action);
    }

    [Fact]
    public void Merge_AtThresholdUrgency_Included()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = IntentMerger.UrgencyThreshold,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Equal("attack", result.Action);
    }

    // =========================================================================
    // MULTI-CHANNEL TESTS
    // =========================================================================

    [Fact]
    public void Merge_AllChannelsFromDifferentModels_ResolvesSeparately()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
            StanceIntent = "combat_ready",
            StanceUrgency = 0.8f,
        };
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "strafe",
            LocomotionUrgency = 0.7f,
            LocomotionTarget = new Vector3(5, 0, 0),
        };
        var interaction = new BehaviorOutput
        {
            AttentionTarget = Guid.NewGuid(),
            AttentionUrgency = 0.6f,
        };
        var idle = new BehaviorOutput
        {
            VocalizationIntent = "breathing",
            VocalizationUrgency = 0.1f,
        };

        var result = _merger.Merge(combat, movement, interaction, idle);

        Assert.Equal("attack", result.Action);
        Assert.Equal("strafe", result.Locomotion.Intent);
        Assert.True(result.Attention.IsValid);
        Assert.Equal("combat_ready", result.Stance);
        Assert.Equal("breathing", result.Vocalization);
    }

    [Fact]
    public void Merge_AllFourModels_EachWinsItsChannel()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
        };
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "run",
            LocomotionUrgency = 0.8f,
        };
        var interaction = new BehaviorOutput
        {
            StanceIntent = "alert",
            StanceUrgency = 0.7f,
        };
        var idle = new BehaviorOutput
        {
            VocalizationIntent = "sigh",
            VocalizationUrgency = 0.2f,
        };

        var result = _merger.Merge(combat, movement, interaction, idle);

        Assert.Equal("attack", result.Action);
        Assert.Equal("run", result.Locomotion.Intent);
        Assert.Equal("alert", result.Stance);
        Assert.Equal("sigh", result.Vocalization);
    }

    // =========================================================================
    // ACTION TARGET TESTS
    // =========================================================================

    [Fact]
    public void Merge_ActionWithTarget_PreservesTarget()
    {
        var targetId = Guid.NewGuid();
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
            ActionTarget = targetId,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.Equal("attack", result.Action);
        Assert.Equal(targetId, result.ActionTarget);
    }

    [Fact]
    public void Merge_ActionConflictWithTargets_WinnerTargetPreserved()
    {
        var enemyId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
            ActionTarget = enemyId,
        };
        var interaction = new BehaviorOutput
        {
            ActionIntent = "pickup",
            ActionUrgency = 0.3f,
            ActionTarget = itemId,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        Assert.Equal("attack", result.Action);
        Assert.Equal(enemyId, result.ActionTarget);
    }

    // =========================================================================
    // INTENT SLOT LAYOUT TESTS
    // =========================================================================

    [Fact]
    public void IntentSlotLayout_IntentSlot_ReturnsCorrectIndex()
    {
        Assert.Equal(0, IntentSlotLayout.IntentSlot(IntentChannel.Action));
        Assert.Equal(2, IntentSlotLayout.IntentSlot(IntentChannel.Locomotion));
        Assert.Equal(4, IntentSlotLayout.IntentSlot(IntentChannel.Attention));
        Assert.Equal(6, IntentSlotLayout.IntentSlot(IntentChannel.Stance));
        Assert.Equal(8, IntentSlotLayout.IntentSlot(IntentChannel.Vocalization));
    }

    [Fact]
    public void IntentSlotLayout_UrgencySlot_ReturnsCorrectIndex()
    {
        Assert.Equal(1, IntentSlotLayout.UrgencySlot(IntentChannel.Action));
        Assert.Equal(3, IntentSlotLayout.UrgencySlot(IntentChannel.Locomotion));
        Assert.Equal(5, IntentSlotLayout.UrgencySlot(IntentChannel.Attention));
        Assert.Equal(7, IntentSlotLayout.UrgencySlot(IntentChannel.Stance));
        Assert.Equal(9, IntentSlotLayout.UrgencySlot(IntentChannel.Vocalization));
    }

    // =========================================================================
    // BEHAVIOR OUTPUT EXTRACTION TESTS
    // =========================================================================

    [Fact]
    public void BehaviorOutput_FromEmptyBuffer_ReturnsEmpty()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "walk" };

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.False(output.HasAnyIntent);
    }

    [Fact]
    public void BehaviorOutput_FromBuffer_ExtractsIntentsAndUrgencies()
    {
        var buffer = new double[15];
        var strings = new[] { "attack", "walk" };

        // Set action: intent=0 ("attack"), urgency=0.8
        buffer[0] = 0;    // Action intent index
        buffer[1] = 0.8;  // Action urgency

        // Set locomotion: intent=1 ("walk"), urgency=0.6
        buffer[2] = 1;    // Locomotion intent index
        buffer[3] = 0.6;  // Locomotion urgency

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.True(output.HasAnyIntent);
        Assert.Equal("attack", output.ActionIntent);
        Assert.Equal(0.8f, output.ActionUrgency);
        Assert.Equal("walk", output.LocomotionIntent);
        Assert.Equal(0.6f, output.LocomotionUrgency);
    }

    [Fact]
    public void BehaviorOutput_FromBuffer_ExtractsLocomotionTarget()
    {
        var buffer = new double[15];
        var strings = new[] { "walk_to" };

        buffer[2] = 0;     // Locomotion intent index
        buffer[3] = 0.7;   // Locomotion urgency
        buffer[10] = 5.0;  // Target X
        buffer[11] = 0.0;  // Target Y
        buffer[12] = 10.0; // Target Z

        var output = BehaviorOutput.FromOutputBuffer(buffer, strings);

        Assert.Equal("walk_to", output.LocomotionIntent);
        Assert.NotNull(output.LocomotionTarget);
        Assert.Equal(5f, output.LocomotionTarget!.Value.X);
        Assert.Equal(10f, output.LocomotionTarget!.Value.Z);
    }

    // =========================================================================
    // LOCOMOTION INTENT TESTS
    // =========================================================================

    [Fact]
    public void LocomotionIntent_Create_SetsAllProperties()
    {
        var target = new Vector3(10, 0, 5);
        var intent = LocomotionIntent.Create("run", target, 0.8f, 0.9f);

        Assert.True(intent.IsValid);
        Assert.Equal("run", intent.Intent);
        Assert.Equal(target, intent.Target);
        Assert.Equal(0.8f, intent.Speed);
        Assert.Equal(0.9f, intent.Urgency);
    }

    [Fact]
    public void LocomotionIntent_CreateWithoutTarget_HasNullTarget()
    {
        var intent = LocomotionIntent.CreateWithoutTarget("stop", 0.5f);

        Assert.True(intent.IsValid);
        Assert.Equal("stop", intent.Intent);
        Assert.Null(intent.Target);
        Assert.Equal(0f, intent.Speed);
        Assert.Equal(0.5f, intent.Urgency);
    }

    [Fact]
    public void LocomotionIntent_None_IsNotValid()
    {
        var none = LocomotionIntent.None;

        Assert.False(none.IsValid);
        Assert.Null(none.Intent);
        Assert.Null(none.Target);
    }

    [Fact]
    public void LocomotionIntent_Create_ClampsSpeadAndUrgency()
    {
        var intent = LocomotionIntent.Create("run", Vector3.Zero, 1.5f, 2.0f);

        Assert.Equal(1.0f, intent.Speed);   // Clamped from 1.5
        Assert.Equal(1.0f, intent.Urgency); // Clamped from 2.0
    }

#if DEBUG
    // =========================================================================
    // CONTRIBUTION TRACE TESTS (DEBUG only)
    // =========================================================================

    [Fact]
    public void Merge_WithTrace_RecordsAllContributions()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
        };
        var interaction = new BehaviorOutput
        {
            ActionIntent = "talk",
            ActionUrgency = 0.5f,
        };

        var result = _merger.Merge(combat, null, interaction, null);

        Assert.NotNull(result.Trace);
        var actionResolution = result.Trace.GetResolution(IntentChannel.Action);
        Assert.NotNull(actionResolution);
        Assert.Equal(BehaviorModelType.Combat, actionResolution!.Value.Winner);
        Assert.Equal(2, actionResolution.Value.AllContributions.Length);
    }

    [Fact]
    public void ContributionTrace_GetChannelsWonBy_ReturnsCorrectChannels()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
            StanceIntent = "combat_ready",
            StanceUrgency = 0.8f,
        };
        var movement = new BehaviorOutput
        {
            LocomotionIntent = "run",
            LocomotionUrgency = 0.7f,
        };

        var result = _merger.Merge(combat, movement, null, null);

        Assert.NotNull(result.Trace);
        var combatWins = result.Trace.GetChannelsWonBy(BehaviorModelType.Combat).ToList();
        Assert.Contains(IntentChannel.Action, combatWins);
        Assert.Contains(IntentChannel.Stance, combatWins);
        Assert.DoesNotContain(IntentChannel.Locomotion, combatWins);
    }

    [Fact]
    public void ContributionTrace_HasContributionFrom_ReturnsTrueForContributor()
    {
        var combat = new BehaviorOutput
        {
            ActionIntent = "attack",
            ActionUrgency = 0.9f,
        };

        var result = _merger.Merge(combat, null, null, null);

        Assert.NotNull(result.Trace);
        Assert.True(result.Trace.HasContributionFrom(BehaviorModelType.Combat));
        Assert.False(result.Trace.HasContributionFrom(BehaviorModelType.Movement));
        Assert.False(result.Trace.HasContributionFrom(BehaviorModelType.Interaction));
        Assert.False(result.Trace.HasContributionFrom(BehaviorModelType.Idle));
    }
#endif
}
