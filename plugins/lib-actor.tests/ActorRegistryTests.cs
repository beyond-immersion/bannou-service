using BeyondImmersion.BannouService.Actor.Runtime;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Unit tests for ActorRegistry - thread-safe registry operations.
/// </summary>
public class ActorRegistryTests
{
    private static IActorRunner CreateMockRunner(
        string actorId,
        Guid templateId,
        string category = "npc-brain",
        Guid? characterId = null,
        ActorStatus status = ActorStatus.Running)
    {
        var mock = new Mock<IActorRunner>();
        mock.Setup(r => r.ActorId).Returns(actorId);
        mock.Setup(r => r.TemplateId).Returns(templateId);
        mock.Setup(r => r.Category).Returns(category);
        mock.Setup(r => r.CharacterId).Returns(characterId);
        mock.Setup(r => r.Status).Returns(status);
        return mock.Object;
    }

    #region Registration Tests

    [Fact]
    public void TryRegister_NewActor_ReturnsTrue()
    {
        // Arrange
        var registry = new ActorRegistry();
        var runner = CreateMockRunner("actor-1", Guid.NewGuid());

        // Act
        var result = registry.TryRegister("actor-1", runner);

        // Assert
        Assert.True(result);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryRegister_DuplicateActorId_ReturnsFalse()
    {
        // Arrange
        var registry = new ActorRegistry();
        var runner1 = CreateMockRunner("actor-1", Guid.NewGuid());
        var runner2 = CreateMockRunner("actor-1", Guid.NewGuid());
        registry.TryRegister("actor-1", runner1);

        // Act
        var result = registry.TryRegister("actor-1", runner2);

        // Assert
        Assert.False(result);
        Assert.Equal(1, registry.Count);
    }

    #endregion

    #region Get Tests

    [Fact]
    public void TryGet_RegisteredActor_ReturnsRunnerAndTrue()
    {
        // Arrange
        var registry = new ActorRegistry();
        var runner = CreateMockRunner("actor-1", Guid.NewGuid());
        registry.TryRegister("actor-1", runner);

        // Act
        var result = registry.TryGet("actor-1", out var retrievedRunner);

        // Assert
        Assert.True(result);
        Assert.Same(runner, retrievedRunner);
    }

    [Fact]
    public void TryGet_NotRegistered_ReturnsFalseAndNull()
    {
        // Arrange
        var registry = new ActorRegistry();

        // Act
        var result = registry.TryGet("nonexistent", out var runner);

        // Assert
        Assert.False(result);
        Assert.Null(runner);
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void TryRemove_RegisteredActor_RemovesAndReturnsRunner()
    {
        // Arrange
        var registry = new ActorRegistry();
        var runner = CreateMockRunner("actor-1", Guid.NewGuid());
        registry.TryRegister("actor-1", runner);

        // Act
        var result = registry.TryRemove("actor-1", out var removedRunner);

        // Assert
        Assert.True(result);
        Assert.Same(runner, removedRunner);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void TryRemove_NotRegistered_ReturnsFalse()
    {
        // Arrange
        var registry = new ActorRegistry();

        // Act
        var result = registry.TryRemove("nonexistent", out var runner);

        // Assert
        Assert.False(result);
        Assert.Null(runner);
    }

    #endregion

    #region Query Tests

    [Fact]
    public void GetActiveActorIds_ReturnsAllIds()
    {
        // Arrange
        var registry = new ActorRegistry();
        registry.TryRegister("actor-1", CreateMockRunner("actor-1", Guid.NewGuid()));
        registry.TryRegister("actor-2", CreateMockRunner("actor-2", Guid.NewGuid()));
        registry.TryRegister("actor-3", CreateMockRunner("actor-3", Guid.NewGuid()));

        // Act
        var ids = registry.GetActiveActorIds().ToList();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("actor-1", ids);
        Assert.Contains("actor-2", ids);
        Assert.Contains("actor-3", ids);
    }

    [Fact]
    public void GetAllRunners_ReturnsAllRunners()
    {
        // Arrange
        var registry = new ActorRegistry();
        var runner1 = CreateMockRunner("actor-1", Guid.NewGuid());
        var runner2 = CreateMockRunner("actor-2", Guid.NewGuid());
        registry.TryRegister("actor-1", runner1);
        registry.TryRegister("actor-2", runner2);

        // Act
        var runners = registry.GetAllRunners().ToList();

        // Assert
        Assert.Equal(2, runners.Count);
        Assert.Contains(runner1, runners);
        Assert.Contains(runner2, runners);
    }

    [Fact]
    public void GetByCategory_ReturnsMatchingActors()
    {
        // Arrange
        var registry = new ActorRegistry();
        registry.TryRegister("npc-1", CreateMockRunner("npc-1", Guid.NewGuid(), "npc-brain"));
        registry.TryRegister("npc-2", CreateMockRunner("npc-2", Guid.NewGuid(), "npc-brain"));
        registry.TryRegister("admin-1", CreateMockRunner("admin-1", Guid.NewGuid(), "world-admin"));

        // Act
        var npcRunners = registry.GetByCategory("npc-brain").ToList();

        // Assert
        Assert.Equal(2, npcRunners.Count);
        Assert.All(npcRunners, r => Assert.Equal("npc-brain", r.Category));
    }

    [Fact]
    public void GetByCategory_CaseInsensitive()
    {
        // Arrange
        var registry = new ActorRegistry();
        registry.TryRegister("actor-1", CreateMockRunner("actor-1", Guid.NewGuid(), "NPC-Brain"));

        // Act
        var runners = registry.GetByCategory("npc-brain").ToList();

        // Assert
        Assert.Single(runners);
    }

    [Fact]
    public void GetByTemplateId_ReturnsMatchingActors()
    {
        // Arrange
        var registry = new ActorRegistry();
        var templateId = Guid.NewGuid();
        var otherTemplateId = Guid.NewGuid();
        registry.TryRegister("actor-1", CreateMockRunner("actor-1", templateId));
        registry.TryRegister("actor-2", CreateMockRunner("actor-2", templateId));
        registry.TryRegister("actor-3", CreateMockRunner("actor-3", otherTemplateId));

        // Act
        var runners = registry.GetByTemplateId(templateId).ToList();

        // Assert
        Assert.Equal(2, runners.Count);
        Assert.All(runners, r => Assert.Equal(templateId, r.TemplateId));
    }

    [Fact]
    public void GetByCharacterId_ReturnsMatchingActors()
    {
        // Arrange
        var registry = new ActorRegistry();
        var characterId = Guid.NewGuid();
        registry.TryRegister("actor-1", CreateMockRunner("actor-1", Guid.NewGuid(), characterId: characterId));
        registry.TryRegister("actor-2", CreateMockRunner("actor-2", Guid.NewGuid(), characterId: characterId));
        registry.TryRegister("actor-3", CreateMockRunner("actor-3", Guid.NewGuid(), characterId: null));

        // Act
        var runners = registry.GetByCharacterId(characterId).ToList();

        // Assert
        Assert.Equal(2, runners.Count);
        Assert.All(runners, r => Assert.Equal(characterId, r.CharacterId));
    }

    [Fact]
    public void GetByStatus_ReturnsMatchingActors()
    {
        // Arrange
        var registry = new ActorRegistry();
        registry.TryRegister("running-1", CreateMockRunner("running-1", Guid.NewGuid(), status: ActorStatus.Running));
        registry.TryRegister("running-2", CreateMockRunner("running-2", Guid.NewGuid(), status: ActorStatus.Running));
        registry.TryRegister("stopped-1", CreateMockRunner("stopped-1", Guid.NewGuid(), status: ActorStatus.Stopped));

        // Act
        var runners = registry.GetByStatus(ActorStatus.Running).ToList();

        // Assert
        Assert.Equal(2, runners.Count);
        Assert.All(runners, r => Assert.Equal(ActorStatus.Running, r.Status));
    }

    #endregion

    #region Count Tests

    [Fact]
    public void Count_EmptyRegistry_ReturnsZero()
    {
        // Arrange
        var registry = new ActorRegistry();

        // Assert
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Count_ReflectsRegisteredActors()
    {
        // Arrange
        var registry = new ActorRegistry();

        // Act & Assert
        registry.TryRegister("actor-1", CreateMockRunner("actor-1", Guid.NewGuid()));
        Assert.Equal(1, registry.Count);

        registry.TryRegister("actor-2", CreateMockRunner("actor-2", Guid.NewGuid()));
        Assert.Equal(2, registry.Count);

        registry.TryRemove("actor-1", out _);
        Assert.Equal(1, registry.Count);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentRegistrations_ThreadSafe()
    {
        // Arrange
        var registry = new ActorRegistry();
        var tasks = new List<Task>();
        var successCount = 0;

        // Act - concurrent registrations
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var runner = CreateMockRunner($"actor-{index}", Guid.NewGuid());
                if (registry.TryRegister($"actor-{index}", runner))
                {
                    Interlocked.Increment(ref successCount);
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - all registrations should succeed (unique IDs)
        Assert.Equal(100, successCount);
        Assert.Equal(100, registry.Count);
    }

    [Fact]
    public async Task ConcurrentMixedOperations_ThreadSafe()
    {
        // Arrange
        var registry = new ActorRegistry();
        var tasks = new List<Task>();

        // Pre-populate some actors
        for (int i = 0; i < 50; i++)
        {
            registry.TryRegister($"actor-{i}", CreateMockRunner($"actor-{i}", Guid.NewGuid()));
        }

        // Act - concurrent mix of register, get, remove operations
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                // Mix of operations
                registry.TryGet($"actor-{index % 50}", out _);
                if (index >= 50)
                {
                    registry.TryRegister($"new-actor-{index}", CreateMockRunner($"new-actor-{index}", Guid.NewGuid()));
                }
                if (index % 3 == 0)
                {
                    registry.TryRemove($"actor-{index % 25}", out _);
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - should not throw and registry should be in consistent state
        Assert.True(registry.Count >= 0);
        Assert.NotNull(registry.GetActiveActorIds());
    }

    #endregion
}
