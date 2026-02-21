// =============================================================================
// Control Gate Integration Tests
// Tests end-to-end control gating flows including priority, filtering, and
// multi-entity coordination.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the control gate system covering entity control,
/// priority hierarchy, emission filtering, and multi-entity coordination.
/// </summary>
public sealed class ControlGateIntegrationTests
{
    private readonly ControlGateManager _registry;

    public ControlGateIntegrationTests()
    {
        _registry = new ControlGateManager();
    }

    // =========================================================================
    // CONTROL GATE LIFECYCLE TESTS
    // =========================================================================

    [Fact]
    public void GetOrCreate_NewEntity_CreatesGateWithBehaviorControl()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var gate = _registry.GetOrCreate(entityId);

        // Assert
        Assert.NotNull(gate);
        Assert.Equal(entityId, gate.EntityId);
        Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        Assert.True(gate.AcceptsBehaviorOutput);
        Assert.True(gate.AcceptsPlayerInput);
        Assert.Empty(gate.BehaviorInputChannels);
    }

    [Fact]
    public void GetOrCreate_SameEntity_ReturnsSameGate()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var gate1 = _registry.GetOrCreate(entityId);
        var gate2 = _registry.GetOrCreate(entityId);

        // Assert
        Assert.Same(gate1, gate2);
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void Remove_ExistingGate_RemovesFromRegistry()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        _registry.GetOrCreate(entityId);

        // Act
        var removed = _registry.Remove(entityId);
        var gate = _registry.Get(entityId);

        // Assert
        Assert.True(removed);
        Assert.Null(gate);
        Assert.Equal(0, _registry.Count);
    }

    // =========================================================================
    // CONTROL HIERARCHY TESTS
    // =========================================================================

    [Fact]
    public async Task TakeControl_HigherPriority_Succeeds()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);

        // Act - Take player control (higher than behavior)
        var result = await gate.TakeControlAsync(ControlOptions.ForPlayer());

        // Assert
        Assert.True(result);
        Assert.Equal(ControlSource.Player, gate.CurrentSource);
    }

    [Fact]
    public async Task TakeControl_LowerPriority_Denied()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        // Act - Attempt player control (lower than cinematic)
        var result = await gate.TakeControlAsync(ControlOptions.ForPlayer());

        // Assert
        Assert.False(result);
        Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
    }

    [Fact]
    public async Task TakeControl_Cinematic_BlocksPlayerInput()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);

        // Act
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        // Assert
        Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
        Assert.False(gate.AcceptsBehaviorOutput);
        Assert.False(gate.AcceptsPlayerInput);
    }

    [Fact]
    public async Task ReturnControl_AfterCinematic_RestoresBehavior()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        // Act
        await gate.ReturnControlAsync(ControlHandoff.Instant());

        // Assert
        Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        Assert.True(gate.AcceptsBehaviorOutput);
        Assert.True(gate.AcceptsPlayerInput);
    }

    // =========================================================================
    // EMISSION FILTERING TESTS
    // =========================================================================

    [Fact]
    public void FilterEmissions_BehaviorControlled_PassesBehaviorEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("expression", "smile", 0.7f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Behavior);

        // Assert
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public async Task FilterEmissions_CinematicControlled_BlocksBehaviorEmissions()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("expression", "smile", 0.7f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Behavior);

        // Assert
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task FilterEmissions_CinematicWithAllowedChannels_PassesAllowedOnly()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        var allowedChannels = new HashSet<string> { "expression" };
        await gate.TakeControlAsync(ControlOptions.ForCinematic(
            "test-cutscene",
            allowedChannels));

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f),
            new("expression", "smile", 0.7f),
            new("dialogue", "greeting", 0.9f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Behavior);

        // Assert
        Assert.Single(filtered);
        Assert.Equal("expression", filtered[0].Channel);
    }

    [Fact]
    public async Task FilterEmissions_CinematicSource_AlwaysPasses()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        var emissions = new List<IntentEmission>
        {
            new("movement", "walk", 0.5f)
        };

        // Act - Filter as cinematic source
        var filtered = gate.FilterEmissions(emissions, ControlSource.Cinematic);

        // Assert
        Assert.Single(filtered);
    }

    // =========================================================================
    // MULTI-ENTITY COORDINATION TESTS
    // =========================================================================

    [Fact]
    public async Task TakeCinematicControl_MultipleEntities_ControlsAll()
    {
        // Arrange
        var entityIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var result = await _registry.TakeCinematicControlAsync(
            entityIds,
            "group-cutscene");

        // Assert
        Assert.True(result);
        var cinematicEntities = _registry.GetCinematicControlledEntities();
        Assert.Equal(3, cinematicEntities.Count);
        foreach (var entityId in entityIds)
        {
            Assert.Contains(entityId, cinematicEntities);
        }
    }

    [Fact]
    public async Task ReturnCinematicControl_MultipleEntities_ReleasesAll()
    {
        // Arrange
        var entityIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await _registry.TakeCinematicControlAsync(entityIds, "group-cutscene");

        // Act
        await _registry.ReturnCinematicControlAsync(entityIds, ControlHandoff.Instant());

        // Assert
        var cinematicEntities = _registry.GetCinematicControlledEntities();
        Assert.Empty(cinematicEntities);
        foreach (var entityId in entityIds)
        {
            var gate = _registry.Get(entityId);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        }
    }

    // =========================================================================
    // CONTROL CHANGED EVENT TESTS
    // =========================================================================

    [Fact]
    public async Task ControlChanged_OnTakeControl_RaisesEvent()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        ControlChangedEvent? capturedEvent = null;
        gate.ControlChanged += (_, evt) => capturedEvent = evt;

        // Act
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(entityId, capturedEvent.EntityId);
        Assert.Equal(ControlSource.Behavior, capturedEvent.PreviousSource);
        Assert.Equal(ControlSource.Cinematic, capturedEvent.NewSource);
        Assert.Null(capturedEvent.Handoff);
    }

    [Fact]
    public async Task ControlChanged_OnReturnControl_IncludesHandoff()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var gate = _registry.GetOrCreate(entityId);
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cutscene"));

        ControlChangedEvent? capturedEvent = null;
        gate.ControlChanged += (_, evt) => capturedEvent = evt;

        var finalState = new EntityState
        {
            Position = new System.Numerics.Vector3(1, 2, 3),
            Stance = "standing"
        };
        var handoff = ControlHandoff.InstantWithState(finalState);

        // Act
        await gate.ReturnControlAsync(handoff);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ControlSource.Cinematic, capturedEvent.PreviousSource);
        Assert.Equal(ControlSource.Behavior, capturedEvent.NewSource);
        Assert.NotNull(capturedEvent.Handoff);
        Assert.Equal(HandoffStyle.Instant, capturedEvent.Handoff.Style);
        Assert.True(capturedEvent.Handoff.SyncState);
        Assert.NotNull(capturedEvent.Handoff.FinalState);
        Assert.Equal("standing", capturedEvent.Handoff.FinalState.Stance);
    }

    // =========================================================================
    // PLAYER CONTROL TRACKING TESTS
    // =========================================================================

    [Fact]
    public async Task GetPlayerControlledEntities_ReturnsCorrectEntities()
    {
        // Arrange
        var playerId1 = Guid.NewGuid();
        var playerId2 = Guid.NewGuid();
        var npcId = Guid.NewGuid();

        var gate1 = _registry.GetOrCreate(playerId1);
        var gate2 = _registry.GetOrCreate(playerId2);
        var gateNpc = _registry.GetOrCreate(npcId);

        await gate1.TakeControlAsync(ControlOptions.ForPlayer());
        await gate2.TakeControlAsync(ControlOptions.ForPlayer());
        // NPC remains in behavior control

        // Act
        var playerControlled = _registry.GetPlayerControlledEntities();

        // Assert
        Assert.Equal(2, playerControlled.Count);
        Assert.Contains(playerId1, playerControlled);
        Assert.Contains(playerId2, playerControlled);
        Assert.DoesNotContain(npcId, playerControlled);
    }
}
