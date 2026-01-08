// =============================================================================
// Control Gate Tests
// Unit tests for control gating layer.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Control;
using BeyondImmersion.BannouService.Behavior.Handlers;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Control;

/// <summary>
/// Unit tests for ControlGate.
/// </summary>
public class ControlGateTests
{
    #region Initial State Tests

    [Fact]
    public void ControlGate_InitialState_IsBehavior()
    {
        // Arrange & Act
        var gate = new ControlGate(Guid.NewGuid());

        // Assert
        Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        Assert.True(gate.AcceptsBehaviorOutput);
        Assert.True(gate.AcceptsPlayerInput);
    }

    [Fact]
    public void ControlGate_InitialState_HasNoRestrictions()
    {
        // Arrange & Act
        var gate = new ControlGate(Guid.NewGuid());

        // Assert
        Assert.Empty(gate.BehaviorInputChannels);
    }

    #endregion

    #region Take Control Tests

    [Fact]
    public async Task TakeControl_Player_Success()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        var options = ControlOptions.ForPlayer();

        // Act
        var result = await gate.TakeControlAsync(options);

        // Assert
        Assert.True(result);
        Assert.Equal(ControlSource.Player, gate.CurrentSource);
        Assert.False(gate.AcceptsBehaviorOutput);
        Assert.True(gate.AcceptsPlayerInput);
    }

    [Fact]
    public async Task TakeControl_Cinematic_Success()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        var options = ControlOptions.ForCinematic("test-cinematic");

        // Act
        var result = await gate.TakeControlAsync(options);

        // Assert
        Assert.True(result);
        Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
        Assert.False(gate.AcceptsBehaviorOutput);
        Assert.False(gate.AcceptsPlayerInput);
    }

    [Fact]
    public async Task TakeControl_LowerPriority_Denied()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cinematic"));

        // Act - try to take player control while cinematic is active
        var result = await gate.TakeControlAsync(ControlOptions.ForPlayer());

        // Assert
        Assert.False(result);
        Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
    }

    [Fact]
    public async Task TakeControl_EqualPriority_Allowed()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        await gate.TakeControlAsync(ControlOptions.ForCinematic("cinematic-1"));

        // Act - another cinematic can take over
        var result = await gate.TakeControlAsync(ControlOptions.ForCinematic("cinematic-2"));

        // Assert
        Assert.True(result);
        Assert.Equal("cinematic-2", gate.CurrentOptions?.CinematicId);
    }

    #endregion

    #region Return Control Tests

    [Fact]
    public async Task ReturnControl_RestoresToBehavior()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cinematic"));

        // Act
        await gate.ReturnControlAsync(ControlHandoff.Instant());

        // Assert
        Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        Assert.True(gate.AcceptsBehaviorOutput);
        Assert.True(gate.AcceptsPlayerInput);
    }

    [Fact]
    public async Task ReturnControl_RaisesEvent()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test-cinematic"));

        ControlChangedEvent? capturedEvent = null;
        gate.ControlChanged += (_, e) => capturedEvent = e;

        // Act
        await gate.ReturnControlAsync(ControlHandoff.InstantWithState(EntityState.Empty));

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ControlSource.Cinematic, capturedEvent.PreviousSource);
        Assert.Equal(ControlSource.Behavior, capturedEvent.NewSource);
        Assert.NotNull(capturedEvent.Handoff);
    }

    #endregion

    #region Behavior Input Channel Tests

    [Fact]
    public async Task TakeControl_WithBehaviorChannels_AllowsSpecificChannels()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        var allowedChannels = new HashSet<string> { "expression", "speech" };
        var options = ControlOptions.ForCinematic("test-cinematic", allowedChannels);

        // Act
        await gate.TakeControlAsync(options);

        // Assert
        Assert.Contains("expression", gate.BehaviorInputChannels);
        Assert.Contains("speech", gate.BehaviorInputChannels);
    }

    [Fact]
    public async Task FilterEmissions_BehaviorDuringCinematic_OnlyAllowedChannels()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        var allowedChannels = new HashSet<string> { "expression" };
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test", allowedChannels));

        var emissions = new List<IntentEmission>
        {
            new IntentEmission("expression", "smile", 0.5f),
            new IntentEmission("combat", "attack", 0.8f),
            new IntentEmission("movement", "walk", 0.6f)
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
        var gate = new ControlGate(Guid.NewGuid());
        await gate.TakeControlAsync(ControlOptions.ForCinematic("test", new HashSet<string>()));

        var emissions = new List<IntentEmission>
        {
            new IntentEmission("combat", "attack", 0.8f),
            new IntentEmission("movement", "walk", 0.6f)
        };

        // Act
        var filtered = gate.FilterEmissions(emissions, ControlSource.Cinematic);

        // Assert
        Assert.Equal(2, filtered.Count);
    }

    #endregion

    #region Duration Expiry Tests

    [Fact]
    public async Task TakeControl_WithDuration_ExpiresAutomatically()
    {
        // Arrange
        var gate = new ControlGate(Guid.NewGuid());
        var options = new ControlOptions(
            ControlSource.Cinematic,
            CinematicId: "test",
            Duration: TimeSpan.FromMilliseconds(50));

        await gate.TakeControlAsync(options);

        // Act - wait for expiry
        await Task.Delay(100);
        var currentSource = gate.CurrentSource;

        // Assert
        Assert.Equal(ControlSource.Behavior, currentSource);
    }

    #endregion
}

/// <summary>
/// Unit tests for ControlGateManager.
/// </summary>
public class ControlGateManagerTests
{
    #region Gate Management Tests

    [Fact]
    public void GetOrCreate_NewEntity_CreatesGate()
    {
        // Arrange
        var manager = new ControlGateManager();
        var entityId = Guid.NewGuid();

        // Act
        var gate = manager.GetOrCreate(entityId);

        // Assert
        Assert.NotNull(gate);
        Assert.Equal(entityId, gate.EntityId);
        Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
    }

    [Fact]
    public void GetOrCreate_SameEntity_ReturnsSameGate()
    {
        // Arrange
        var manager = new ControlGateManager();
        var entityId = Guid.NewGuid();

        // Act
        var gate1 = manager.GetOrCreate(entityId);
        var gate2 = manager.GetOrCreate(entityId);

        // Assert
        Assert.Same(gate1, gate2);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        // Arrange
        var manager = new ControlGateManager();

        // Act
        var gate = manager.Get(Guid.NewGuid());

        // Assert
        Assert.Null(gate);
    }

    [Fact]
    public void Remove_Existing_ReturnsTrue()
    {
        // Arrange
        var manager = new ControlGateManager();
        var entityId = Guid.NewGuid();
        manager.GetOrCreate(entityId);

        // Act
        var removed = manager.Remove(entityId);

        // Assert
        Assert.True(removed);
        Assert.Null(manager.Get(entityId));
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        // Arrange
        var manager = new ControlGateManager();

        // Act
        var removed = manager.Remove(Guid.NewGuid());

        // Assert
        Assert.False(removed);
    }

    #endregion

    #region Bulk Operation Tests

    [Fact]
    public async Task TakeCinematicControl_MultipleEntities_Success()
    {
        // Arrange
        var manager = new ControlGateManager();
        var entities = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var result = await manager.TakeCinematicControlAsync(entities, "test-cinematic");

        // Assert
        Assert.True(result);
        foreach (var entityId in entities)
        {
            var gate = manager.Get(entityId);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Cinematic, gate.CurrentSource);
        }
    }

    [Fact]
    public async Task ReturnCinematicControl_MultipleEntities_Success()
    {
        // Arrange
        var manager = new ControlGateManager();
        var entities = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await manager.TakeCinematicControlAsync(entities, "test-cinematic");

        // Act
        await manager.ReturnCinematicControlAsync(entities, ControlHandoff.Instant());

        // Assert
        foreach (var entityId in entities)
        {
            var gate = manager.Get(entityId);
            Assert.NotNull(gate);
            Assert.Equal(ControlSource.Behavior, gate.CurrentSource);
        }
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task GetCinematicControlledEntities_ReturnsCorrect()
    {
        // Arrange
        var manager = new ControlGateManager();
        var cinematicEntity = Guid.NewGuid();
        var behaviorEntity = Guid.NewGuid();

        manager.GetOrCreate(behaviorEntity);
        var cinematicGate = manager.GetOrCreate(cinematicEntity);
        await cinematicGate.TakeControlAsync(ControlOptions.ForCinematic("test"));

        // Act
        var cinematicEntities = manager.GetCinematicControlledEntities();

        // Assert
        Assert.Single(cinematicEntities);
        Assert.Contains(cinematicEntity, cinematicEntities);
        Assert.DoesNotContain(behaviorEntity, cinematicEntities);
    }

    [Fact]
    public async Task GetPlayerControlledEntities_ReturnsCorrect()
    {
        // Arrange
        var manager = new ControlGateManager();
        var playerEntity = Guid.NewGuid();
        var behaviorEntity = Guid.NewGuid();

        manager.GetOrCreate(behaviorEntity);
        var playerGate = manager.GetOrCreate(playerEntity);
        await playerGate.TakeControlAsync(ControlOptions.ForPlayer());

        // Act
        var playerEntities = manager.GetPlayerControlledEntities();

        // Assert
        Assert.Single(playerEntities);
        Assert.Contains(playerEntity, playerEntities);
    }

    #endregion
}
