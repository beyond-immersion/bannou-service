// =============================================================================
// State Sync Tests
// Tests for state synchronization when control returns from cinematic.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Control;

/// <summary>
/// Tests for <see cref="StateSync"/>.
/// </summary>
public sealed class StateSyncTests
{
    [Fact]
    public async Task SyncStateAsync_InstantHandoff_UpdatesRegistryWithCorrectState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var expectedPosition = new Vector3(10, 0, 5);
        var expectedHealth = 0.8f;
        var state = new EntityState
        {
            Position = expectedPosition,
            Rotation = new Vector3(0, 0, 0),
            Health = expectedHealth,
            Stance = "standing"
        };
        var handoff = ControlHandoff.Instant();

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - verify state was written to registry
        var retrievedState = registry.GetState(entityId);
        Assert.NotNull(retrievedState);
        Assert.Equal(expectedPosition, retrievedState.Position);
        Assert.Equal(expectedHealth, retrievedState.Health);
        Assert.Equal("standing", retrievedState.Stance);
    }

    [Fact]
    public async Task SyncStateAsync_SyncStateFalse_DoesNotUpdateRegistry()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(10, 0, 5),
            Health = 1.0f
        };
        var handoff = new ControlHandoff(HandoffStyle.Instant, null, SyncState: false);

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - verify state was NOT written to registry
        Assert.False(registry.HasState(entityId));
        Assert.Null(registry.GetState(entityId));
    }

    [Fact]
    public async Task SyncStateAsync_BlendHandoff_UpdatesRegistryImmediately()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var expectedPosition = new Vector3(20, 5, 10);
        var state = new EntityState
        {
            Position = expectedPosition,
            Health = 0.5f
        };
        // Pass the state to Blend() to enable SyncState
        var handoff = ControlHandoff.Blend(TimeSpan.FromSeconds(1), state);

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - blend falls through to instant, state should be in registry
        var retrievedState = registry.GetState(entityId);
        Assert.NotNull(retrievedState);
        Assert.Equal(expectedPosition, retrievedState.Position);
        Assert.Equal(0.5f, retrievedState.Health);
    }

    [Fact]
    public async Task SyncStateAsync_RaisesCompletedEvent_WithCorrectData()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(0, 0, 0),
            Health = 1.0f
        };
        var handoff = ControlHandoff.Instant();

        StateSyncCompletedEventArgs? receivedArgs = null;
        stateSync.StateSyncCompleted += (_, args) => receivedArgs = args;

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(entityId, receivedArgs.EntityId);
        Assert.Same(state, receivedArgs.SyncedState);
        Assert.Equal(HandoffStyle.Instant, receivedArgs.HandoffStyle);
        Assert.True(receivedArgs.CompletedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task SyncStateAsync_RegistryRaisesStateUpdatedEvent()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(50, 0, 50),
            Health = 0.75f,
            Emotion = "happy"
        };
        var handoff = ControlHandoff.Instant();

        EntityStateUpdatedEventArgs? receivedArgs = null;
        registry.StateUpdated += (_, args) => receivedArgs = args;

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - registry event should have been raised
        Assert.NotNull(receivedArgs);
        Assert.Equal(entityId, receivedArgs.EntityId);
        Assert.Same(state, receivedArgs.NewState);
        Assert.Equal("cinematic", receivedArgs.Source);
        Assert.Null(receivedArgs.PreviousState); // First update, no previous state
    }

    [Fact]
    public async Task SyncStateAsync_MultipleUpdates_TracksStateHistory()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var firstState = new EntityState { Position = new Vector3(0, 0, 0), Health = 1.0f };
        var secondState = new EntityState { Position = new Vector3(100, 0, 100), Health = 0.5f };
        var handoff = ControlHandoff.Instant();

        EntityState? previousStateFromEvent = null;
        registry.StateUpdated += (_, args) => previousStateFromEvent = args.PreviousState;

        // Act - first sync
        await stateSync.SyncStateAsync(entityId, firstState, handoff, CancellationToken.None);

        // Act - second sync (should capture previous state)
        await stateSync.SyncStateAsync(entityId, secondState, handoff, CancellationToken.None);

        // Assert - second event should have captured first state as previous
        Assert.NotNull(previousStateFromEvent);
        Assert.Equal(firstState.Position, previousStateFromEvent.Position);
        Assert.Equal(firstState.Health, previousStateFromEvent.Health);

        // Current state should be second state
        var currentState = registry.GetState(entityId);
        Assert.NotNull(currentState);
        Assert.Equal(secondState.Position, currentState.Position);
        Assert.Equal(secondState.Health, currentState.Health);
    }

    [Fact]
    public async Task SyncStateAsync_ExplicitHandoff_UpdatesRegistry()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var expectedPosition = new Vector3(100, 0, 100);
        var state = new EntityState { Position = expectedPosition };
        var handoff = new ControlHandoff(HandoffStyle.Explicit, null, true, state);

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert
        var retrievedState = registry.GetState(entityId);
        Assert.NotNull(retrievedState);
        Assert.Equal(expectedPosition, retrievedState.Position);
    }

    [Fact]
    public async Task SyncStateAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var state = new EntityState { Position = new Vector3(0, 0, 0) };
        var handoff = ControlHandoff.Instant();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => stateSync.SyncStateAsync(entityId, state, handoff, cts.Token));

        // State should not have been written
        Assert.False(registry.HasState(entityId));
    }

    [Fact]
    public void StateRegistry_CanBeAccessedFromStateSync()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);

        // Act & Assert
        Assert.Same(registry, stateSync.StateRegistry);
    }

    [Fact]
    public async Task SyncStateAsync_PreservesAllEntityStateProperties()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var stateSync = new StateSync(registry);
        var entityId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(1, 2, 3),
            Rotation = new Vector3(0, 90, 0),
            Health = 0.65f,
            Stance = "combat",
            Emotion = "angry",
            CurrentTarget = targetId,
            AdditionalState = new Dictionary<string, object>
            {
                ["ammo"] = 50,
                ["weapon"] = "sword"
            }
        };
        var handoff = ControlHandoff.Instant();

        // Act
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);

        // Assert - all properties should be preserved
        var retrieved = registry.GetState(entityId);
        Assert.NotNull(retrieved);
        Assert.Equal(new Vector3(1, 2, 3), retrieved.Position);
        Assert.Equal(new Vector3(0, 90, 0), retrieved.Rotation);
        Assert.Equal(0.65f, retrieved.Health);
        Assert.Equal("combat", retrieved.Stance);
        Assert.Equal("angry", retrieved.Emotion);
        Assert.Equal(targetId, retrieved.CurrentTarget);
        Assert.NotNull(retrieved.AdditionalState);
        Assert.Equal(50, retrieved.AdditionalState["ammo"]);
        Assert.Equal("sword", retrieved.AdditionalState["weapon"]);
    }
}
