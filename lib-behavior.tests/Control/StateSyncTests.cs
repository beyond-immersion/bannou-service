// =============================================================================
// State Sync Tests
// Tests for state synchronization when control returns from cinematic.
// =============================================================================

using System.Numerics;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Control;

/// <summary>
/// Tests for <see cref="StateSync"/>.
/// </summary>
public sealed class StateSyncTests
{
    [Fact]
    public async Task SyncStateAsync_SyncStateTrue_CompletesSuccessfully()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(10, 0, 5),
            Rotation = new Vector3(0, 0, 0),
            Health = 0.8f
        };
        var handoff = ControlHandoff.Instant();

        // Act & Assert - should not throw
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);
    }

    [Fact]
    public async Task SyncStateAsync_SyncStateFalse_SkipsSync()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(10, 0, 5),
            Health = 1.0f
        };
        var handoff = new ControlHandoff(HandoffStyle.Instant, null, SyncState: false);

        // Act & Assert - should not throw and skip sync
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);
    }

    [Fact]
    public async Task SyncStateAsync_BlendHandoff_CompletesSuccessfully()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(20, 5, 10),
            Health = 0.5f
        };
        var handoff = ControlHandoff.Blend(TimeSpan.FromSeconds(1));

        // Act & Assert - should complete (currently falls through to instant)
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);
    }

    [Fact]
    public async Task SyncStateAsync_RaisesCompletedEvent()
    {
        // Arrange
        var stateSync = new StateSync();
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
    }

    [Fact]
    public async Task SyncStateAsync_ExplicitHandoff_CompletesSuccessfully()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(100, 0, 100)
        };
        var handoff = new ControlHandoff(HandoffStyle.Explicit, null, true, state);

        // Act & Assert
        await stateSync.SyncStateAsync(entityId, state, handoff, CancellationToken.None);
    }

    [Fact]
    public async Task SyncStateAsync_NullState_ThrowsArgumentNullException()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var handoff = ControlHandoff.Instant();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => stateSync.SyncStateAsync(entityId, null!, handoff, CancellationToken.None));
    }

    [Fact]
    public async Task SyncStateAsync_NullHandoff_ThrowsArgumentNullException()
    {
        // Arrange
        var stateSync = new StateSync();
        var entityId = Guid.NewGuid();
        var state = new EntityState();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => stateSync.SyncStateAsync(entityId, state, null!, CancellationToken.None));
    }
}
