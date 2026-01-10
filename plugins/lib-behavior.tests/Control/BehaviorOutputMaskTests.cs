// =============================================================================
// Behavior Output Mask Tests
// Tests for behavior output masking based on control state.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Control;

/// <summary>
/// Tests for <see cref="BehaviorOutputMask"/>.
/// </summary>
public sealed class BehaviorOutputMaskTests
{
    private readonly ControlGateManager _gates;
    private readonly BehaviorOutputMask _mask;

    public BehaviorOutputMaskTests()
    {
        _gates = new ControlGateManager();
        _mask = new BehaviorOutputMask(_gates);
    }

    [Fact]
    public void ApplyMask_NoGate_ReturnsUnfilteredEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("attention", "look_at", 0.6f),
            new("expression", "happy", 0.4f)
        };

        // Act
        var result = _mask.ApplyMask(entityId, emissions);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Same(emissions, result);
    }

    [Fact]
    public void ApplyMask_GateAcceptsBehavior_ReturnsUnfilteredEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _gates.GetOrCreate(entityId);
        // Gate starts with Behavior control - should accept behavior output

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("attention", "look_at", 0.6f)
        };

        // Act
        var result = _mask.ApplyMask(entityId, emissions);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ApplyMask_PlayerControl_FiltersAllEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _gates.GetOrCreate(entityId);

        // Take player control with no allowed behavior channels
        await gate.TakeControlAsync(ControlOptions.ForPlayer());

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("attention", "look_at", 0.6f)
        };

        // Act
        var result = _mask.ApplyMask(entityId, emissions);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ApplyMask_CinematicWithAllowedChannels_FiltersToAllowed()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _gates.GetOrCreate(entityId);

        // Take cinematic control but allow attention channel
        var allowedChannels = new HashSet<string> { "attention" };
        await gate.TakeControlAsync(
            ControlOptions.ForCinematic("cutscene_01", allowedChannels));

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("attention", "look_at", 0.6f),
            new("expression", "happy", 0.4f)
        };

        // Act
        var result = _mask.ApplyMask(entityId, emissions);

        // Assert
        Assert.Single(result);
        Assert.Equal("attention", result[0].Channel);
    }

    [Fact]
    public void ApplyMask_EmptyEmissions_ReturnsEmpty()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var emissions = Array.Empty<IntentEmission>();

        // Act
        var result = _mask.ApplyMask(entityId, emissions);

        // Assert
        Assert.Empty(result);
    }
}
