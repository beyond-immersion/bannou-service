// =============================================================================
// Entity State Registry Tests
// Tests for the entity state tracking registry.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Control;

/// <summary>
/// Tests for <see cref="EntityStateRegistry"/>.
/// </summary>
public sealed class EntityStateRegistryTests
{
    [Fact]
    public void UpdateState_NewEntity_StoresState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(10, 20, 30),
            Health = 0.9f
        };

        // Act
        registry.UpdateState(entityId, state);

        // Assert
        Assert.True(registry.HasState(entityId));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void GetState_ExistingEntity_ReturnsState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var state = new EntityState
        {
            Position = new Vector3(5, 10, 15),
            Health = 0.75f,
            Stance = "crouching"
        };
        registry.UpdateState(entityId, state);

        // Act
        var retrieved = registry.GetState(entityId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(state.Position, retrieved.Position);
        Assert.Equal(state.Health, retrieved.Health);
        Assert.Equal(state.Stance, retrieved.Stance);
    }

    [Fact]
    public void GetState_NonExistentEntity_ReturnsNull()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();

        // Act
        var retrieved = registry.GetState(entityId);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void GetStateOrEmpty_ExistingEntity_ReturnsState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var state = new EntityState { Health = 0.5f };
        registry.UpdateState(entityId, state);

        // Act
        var retrieved = registry.GetStateOrEmpty(entityId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(0.5f, retrieved.Health);
    }

    [Fact]
    public void GetStateOrEmpty_NonExistentEntity_ReturnsEmptyState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();

        // Act
        var retrieved = registry.GetStateOrEmpty(entityId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Position);
        Assert.Null(retrieved.Health);
    }

    [Fact]
    public void UpdateState_ExistingEntity_OverwritesState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var originalState = new EntityState { Health = 1.0f, Stance = "standing" };
        var updatedState = new EntityState { Health = 0.25f, Stance = "prone" };
        registry.UpdateState(entityId, originalState);

        // Act
        registry.UpdateState(entityId, updatedState);

        // Assert
        var retrieved = registry.GetState(entityId);
        Assert.NotNull(retrieved);
        Assert.Equal(0.25f, retrieved.Health);
        Assert.Equal("prone", retrieved.Stance);
        Assert.Equal(1, registry.Count); // Still only one entity
    }

    [Fact]
    public void RemoveState_ExistingEntity_RemovesAndReturnsTrue()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        registry.UpdateState(entityId, new EntityState { Health = 1.0f });

        // Act
        var removed = registry.RemoveState(entityId);

        // Assert
        Assert.True(removed);
        Assert.False(registry.HasState(entityId));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RemoveState_NonExistentEntity_ReturnsFalse()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();

        // Act
        var removed = registry.RemoveState(entityId);

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void GetTrackedEntityIds_ReturnsAllEntityIds()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId1 = Guid.NewGuid();
        var entityId2 = Guid.NewGuid();
        var entityId3 = Guid.NewGuid();
        registry.UpdateState(entityId1, new EntityState());
        registry.UpdateState(entityId2, new EntityState());
        registry.UpdateState(entityId3, new EntityState());

        // Act
        var ids = registry.GetTrackedEntityIds();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains(entityId1, ids);
        Assert.Contains(entityId2, ids);
        Assert.Contains(entityId3, ids);
    }

    [Fact]
    public void Clear_RemovesAllState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        registry.UpdateState(Guid.NewGuid(), new EntityState());
        registry.UpdateState(Guid.NewGuid(), new EntityState());
        registry.UpdateState(Guid.NewGuid(), new EntityState());

        // Act
        registry.Clear();

        // Assert
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.GetTrackedEntityIds());
    }

    [Fact]
    public void StateUpdated_NewEntity_RaisesEventWithNullPreviousState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var state = new EntityState { Health = 0.8f };

        EntityStateUpdatedEventArgs? receivedArgs = null;
        registry.StateUpdated += (_, args) => receivedArgs = args;

        // Act
        registry.UpdateState(entityId, state, "test-source");

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(entityId, receivedArgs.EntityId);
        Assert.Same(state, receivedArgs.NewState);
        Assert.Null(receivedArgs.PreviousState);
        Assert.Equal("test-source", receivedArgs.Source);
    }

    [Fact]
    public void StateUpdated_ExistingEntity_RaisesEventWithPreviousState()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var originalState = new EntityState { Health = 1.0f };
        var newState = new EntityState { Health = 0.5f };
        registry.UpdateState(entityId, originalState);

        EntityStateUpdatedEventArgs? receivedArgs = null;
        registry.StateUpdated += (_, args) => receivedArgs = args;

        // Act
        registry.UpdateState(entityId, newState);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(entityId, receivedArgs.EntityId);
        Assert.Same(newState, receivedArgs.NewState);
        Assert.Same(originalState, receivedArgs.PreviousState);
    }

    [Fact]
    public void UpdateState_NullState_ThrowsArgumentNullException()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.UpdateState(entityId, null!));
    }

    [Fact]
    public void HasState_EmptyRegistry_ReturnsFalse()
    {
        // Arrange
        var registry = new EntityStateRegistry();

        // Act & Assert
        Assert.False(registry.HasState(Guid.NewGuid()));
    }

    [Fact]
    public void Count_EmptyRegistry_ReturnsZero()
    {
        // Arrange
        var registry = new EntityStateRegistry();

        // Act & Assert
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void StateUpdated_SourceIsRecorded()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityId = Guid.NewGuid();
        var sources = new List<string?>();
        registry.StateUpdated += (_, args) => sources.Add(args.Source);

        // Act
        registry.UpdateState(entityId, new EntityState(), "game-server");
        registry.UpdateState(entityId, new EntityState(), "cinematic");
        registry.UpdateState(entityId, new EntityState(), null);

        // Assert
        Assert.Equal(3, sources.Count);
        Assert.Equal("game-server", sources[0]);
        Assert.Equal("cinematic", sources[1]);
        Assert.Null(sources[2]);
    }

    [Fact]
    public void ConcurrentUpdates_AllSucceed()
    {
        // Arrange
        var registry = new EntityStateRegistry();
        var entityIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var updateCount = 0;
        registry.StateUpdated += (_, _) => Interlocked.Increment(ref updateCount);

        // Act - concurrent updates
        Parallel.ForEach(entityIds, entityId =>
        {
            registry.UpdateState(entityId, new EntityState { Health = 1.0f });
            registry.UpdateState(entityId, new EntityState { Health = 0.5f });
        });

        // Assert
        Assert.Equal(100, registry.Count);
        Assert.Equal(200, updateCount); // 2 updates per entity
    }
}
