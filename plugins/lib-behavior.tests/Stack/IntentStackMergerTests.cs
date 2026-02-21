// =============================================================================
// Intent Stack Merger Tests
// Tests for per-channel merge strategies.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Stack;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Stack;

/// <summary>
/// Tests for <see cref="IntentStackMerger"/>.
/// </summary>
public sealed class IntentStackMergerTests
{
    private readonly IntentStackMerger _merger;
    private readonly IArchetypeDefinition _humanoidArchetype;

    public IntentStackMergerTests()
    {
        _merger = new IntentStackMerger();
        var registry = new ArchetypeRegistry();
        _humanoidArchetype = registry.GetArchetype("humanoid")!;
    }

    // =========================================================================
    // PRIORITY MERGE TESTS
    // =========================================================================

    [Fact]
    public void MergeChannel_Priority_HighestPriorityWins()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("combat")!; // Priority strategy
        var contributions = new List<IntentContribution>
        {
            new("base", 0, BehaviorCategory.Base, new IntentEmission("combat", "defend", 0.5f)),
            new("situational", 100, BehaviorCategory.Situational, new IntentEmission("combat", "attack", 0.8f)),
            new("personal", 50, BehaviorCategory.Personal, new IntentEmission("combat", "retreat", 0.6f))
        };

        // Act
        var merged = _merger.MergeChannel("combat", contributions, channelDef);

        // Assert - Situational category has highest effective priority
        Assert.NotNull(merged);
        Assert.Equal("attack", merged.Intent);
    }

    [Fact]
    public void MergeChannel_Priority_TiesBreakByUrgency()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("combat")!;
        var contributions = new List<IntentContribution>
        {
            new("layer1", 50, BehaviorCategory.Personal, new IntentEmission("combat", "defend", 0.5f)),
            new("layer2", 50, BehaviorCategory.Personal, new IntentEmission("combat", "attack", 0.8f))
        };

        // Act
        var merged = _merger.MergeChannel("combat", contributions, channelDef);

        // Assert - same priority, higher urgency wins
        Assert.NotNull(merged);
        Assert.Equal("attack", merged.Intent);
    }

    [Fact]
    public void MergeChannel_Priority_CategoryTrumpsLayerPriority()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("combat")!;
        var contributions = new List<IntentContribution>
        {
            new("base-high-priority", 999, BehaviorCategory.Base, new IntentEmission("combat", "defend", 0.9f)),
            new("situational-low-priority", 1, BehaviorCategory.Situational, new IntentEmission("combat", "attack", 0.3f))
        };

        // Act
        var merged = _merger.MergeChannel("combat", contributions, channelDef);

        // Assert - Situational category wins even with lower layer priority
        Assert.NotNull(merged);
        Assert.Equal("attack", merged.Intent);
    }

    // =========================================================================
    // BLEND MERGE TESTS
    // =========================================================================

    [Fact]
    public void MergeChannel_Blend_SingleContribution_ReturnsAsIs()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("movement")!; // Blend strategy
        var contributions = new List<IntentContribution>
        {
            new("only", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.5f))
        };

        // Act
        var merged = _merger.MergeChannel("movement", contributions, channelDef);

        // Assert
        Assert.NotNull(merged);
        Assert.Equal("walk", merged.Intent);
        Assert.Equal(0.5f, merged.Urgency);
    }

    [Fact]
    public void MergeChannel_Blend_UsesHighestPriorityIntent()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("movement")!;
        var contributions = new List<IntentContribution>
        {
            new("base", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.4f)),
            new("situational", 0, BehaviorCategory.Situational, new IntentEmission("movement", "run", 0.6f))
        };

        // Act
        var merged = _merger.MergeChannel("movement", contributions, channelDef);

        // Assert - uses intent from highest priority layer
        Assert.NotNull(merged);
        Assert.Equal("run", merged.Intent);
    }

    [Fact]
    public void MergeChannel_Blend_BlendsUrgency()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("movement")!;
        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.4f)),
            new("layer2", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.4f))
        };

        // Act
        var merged = _merger.MergeChannel("movement", contributions, channelDef);

        // Assert - blended urgency should be reasonable
        Assert.NotNull(merged);
        Assert.True(merged.Urgency > 0.4f, "Blended urgency should be higher than individual");
        Assert.True(merged.Urgency <= 1.0f, "Blended urgency should be clamped to 1.0");
    }

    [Fact]
    public void MergeChannel_Blend_BlendsVector3TargetPosition()
    {
        // Arrange
        var channelDef = _humanoidArchetype.GetChannel("movement")!;

        var data1 = new Dictionary<string, object> { ["TargetPosition"] = new Vector3(10, 0, 0) };
        var data2 = new Dictionary<string, object> { ["TargetPosition"] = new Vector3(0, 0, 10) };

        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.5f, null, data1)),
            new("layer2", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.5f, null, data2))
        };

        // Act
        var merged = _merger.MergeChannel("movement", contributions, channelDef);

        // Assert - should blend the positions
        Assert.NotNull(merged);
        Assert.NotNull(merged.Data);
        Assert.True(merged.Data.ContainsKey("TargetPosition"));
        var blendedPos = (Vector3)merged.Data["TargetPosition"];
        Assert.Equal(5f, blendedPos.X, 0.01f); // Averaged X
        Assert.Equal(5f, blendedPos.Z, 0.01f); // Averaged Z
    }

    // =========================================================================
    // ADDITIVE MERGE TESTS
    // =========================================================================

    [Fact]
    public void MergeChannel_Additive_SumsUrgency()
    {
        // Arrange - Create a channel with additive strategy
        var channelDef = new LogicalChannelDefinition(
            "test",
            PhysicalChannel.Action,
            0.5f,
            MergeStrategy.Additive,
            "Test additive channel");

        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("test", "action", 0.3f)),
            new("layer2", 0, BehaviorCategory.Base, new IntentEmission("test", "action", 0.4f))
        };

        // Act
        var merged = _merger.MergeChannel("test", contributions, channelDef);

        // Assert - urgencies should be summed
        Assert.NotNull(merged);
        Assert.Equal(0.7f, merged.Urgency, 0.01f);
    }

    [Fact]
    public void MergeChannel_Additive_ClampsTo1()
    {
        // Arrange
        var channelDef = new LogicalChannelDefinition(
            "test",
            PhysicalChannel.Action,
            0.5f,
            MergeStrategy.Additive,
            "Test additive channel");

        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("test", "action", 0.8f)),
            new("layer2", 0, BehaviorCategory.Base, new IntentEmission("test", "action", 0.8f))
        };

        // Act
        var merged = _merger.MergeChannel("test", contributions, channelDef);

        // Assert - should be clamped to 1.0
        Assert.NotNull(merged);
        Assert.Equal(1.0f, merged.Urgency, 0.01f);
    }

    // =========================================================================
    // MERGE ALL TESTS
    // =========================================================================

    [Fact]
    public void MergeAll_GroupsByChannel()
    {
        // Arrange
        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.5f)),
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("combat", "defend", 0.3f)),
            new("layer2", 0, BehaviorCategory.Personal, new IntentEmission("expression", "smile", 0.6f))
        };

        // Act
        var merged = _merger.MergeAll(contributions, _humanoidArchetype);

        // Assert
        Assert.Equal(3, merged.Count);
        Assert.True(merged.ContainsKey("movement"));
        Assert.True(merged.ContainsKey("combat"));
        Assert.True(merged.ContainsKey("expression"));
    }

    [Fact]
    public void MergeAll_UsesChannelMergeStrategy()
    {
        // Arrange - combat is Priority, movement is Blend
        var contributions = new List<IntentContribution>
        {
            new("base", 0, BehaviorCategory.Base, new IntentEmission("combat", "defend", 0.9f)),
            new("situational", 0, BehaviorCategory.Situational, new IntentEmission("combat", "attack", 0.3f))
        };

        // Act
        var merged = _merger.MergeAll(contributions, _humanoidArchetype);

        // Assert - Priority: highest effective priority wins (Situational)
        Assert.Equal("attack", merged["combat"].Intent);
    }

    [Fact]
    public void MergeAll_UnknownChannel_UsesPriorityStrategy()
    {
        // Arrange
        var contributions = new List<IntentContribution>
        {
            new("base", 0, BehaviorCategory.Base, new IntentEmission("unknown_channel", "action1", 0.9f)),
            new("situational", 0, BehaviorCategory.Situational, new IntentEmission("unknown_channel", "action2", 0.3f))
        };

        // Act
        var merged = _merger.MergeAll(contributions, _humanoidArchetype);

        // Assert - should use priority strategy for unknown channels
        Assert.True(merged.ContainsKey("unknown_channel"));
        Assert.Equal("action2", merged["unknown_channel"].Intent); // Situational wins
    }

    [Fact]
    public void MergeAll_FiltersLowUrgency()
    {
        // Arrange
        var contributions = new List<IntentContribution>
        {
            new("layer1", 0, BehaviorCategory.Base, new IntentEmission("movement", "walk", 0.5f)),
            new("layer2", 0, BehaviorCategory.Base, new IntentEmission("combat", "defend", 0.0001f)) // Below threshold
        };

        // Act
        var merged = _merger.MergeAll(contributions, _humanoidArchetype);

        // Assert - low urgency contribution should be filtered
        Assert.Single(merged);
        Assert.True(merged.ContainsKey("movement"));
    }

    // =========================================================================
    // EFFECTIVE PRIORITY TESTS
    // =========================================================================

    [Fact]
    public void EffectivePriority_CalculatesCorrectly()
    {
        // Arrange
        var baseContrib = new IntentContribution(
            "base", 50, BehaviorCategory.Base, // EffectivePriority = 0*1000 + 50 = 50
            new IntentEmission("test", "a", 0.5f));

        var situationalContrib = new IntentContribution(
            "situational", 10, BehaviorCategory.Situational, // EffectivePriority = 4*1000 + 10 = 4010
            new IntentEmission("test", "b", 0.5f));

        // Assert
        Assert.Equal(50, baseContrib.EffectivePriority);
        Assert.Equal(4010, situationalContrib.EffectivePriority);
        Assert.True(situationalContrib.EffectivePriority > baseContrib.EffectivePriority);
    }
}
