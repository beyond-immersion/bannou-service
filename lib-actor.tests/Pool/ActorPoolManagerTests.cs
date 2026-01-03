using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.Pool;

/// <summary>
/// Unit tests for ActorPoolManager - pool node registration and actor assignment.
/// Uses a mock state store factory with flexible type handling.
/// </summary>
public class ActorPoolManagerTests
{
    private readonly Mock<IStateStoreFactory> _stateStoreFactoryMock;
    private readonly Mock<ILogger<ActorPoolManager>> _loggerMock;
    private readonly ActorServiceConfiguration _configuration;

    // In-memory store simulation
    private readonly Dictionary<string, object> _nodeStore = new();
    private readonly Dictionary<string, object> _assignmentStore = new();

    public ActorPoolManagerTests()
    {
        _stateStoreFactoryMock = new Mock<IStateStoreFactory>();
        _loggerMock = new Mock<ILogger<ActorPoolManager>>();

        _configuration = new ActorServiceConfiguration
        {
            DeploymentMode = "shared-pool",
            HeartbeatIntervalSeconds = 10,
            HeartbeatTimeoutSeconds = 30
        };

        // Configure state store factory to return mock stores
        SetupStateStoreFactory();
    }

    private void SetupStateStoreFactory()
    {
        // Pool nodes store - handles PoolNodeState and the private index type
        _stateStoreFactoryMock
            .Setup(f => f.GetStore<PoolNodeState>(It.Is<string>(s => s == "actor-pool-nodes")))
            .Returns(() => CreateMockStore<PoolNodeState>(_nodeStore));

        // Assignment store - handles ActorAssignment and the private actor index type
        _stateStoreFactoryMock
            .Setup(f => f.GetStore<ActorAssignment>(It.Is<string>(s => s == "actor-assignments")))
            .Returns(() => CreateMockStore<ActorAssignment>(_assignmentStore));

        // For internal index types, use Any pattern
        _stateStoreFactoryMock
            .Setup(f => f.GetStore<It.IsAnyType>(It.IsAny<string>()))
            .Returns((string storeName) =>
            {
                var store = storeName == "actor-pool-nodes" ? _nodeStore : _assignmentStore;
                return CreateMockStoreForAnyType(store);
            });
    }

    private IStateStore<T> CreateMockStore<T>(Dictionary<string, object> storage) where T : class
    {
        var mock = new Mock<IStateStore<T>>();

        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                if (storage.TryGetValue(key, out var value) && value is T typed)
                    return typed;
                return null;
            });

        mock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<T>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, T value, StateOptions? _, CancellationToken _) =>
            {
                storage[key] = value;
                return "etag-" + Guid.NewGuid();
            });

        mock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => storage.Remove(key));

        mock.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => storage.ContainsKey(key));

        return mock.Object;
    }

    private object CreateMockStoreForAnyType(Dictionary<string, object> storage)
    {
        // Return a mock that can work with any type - used for internal index types
        var mock = new Mock<IStateStore<object>>();

        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storage.TryGetValue(key, out var value) ? value : null);

        mock.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, object value, StateOptions? _, CancellationToken _) =>
            {
                storage[key] = value;
                return "etag-" + Guid.NewGuid();
            });

        return mock.Object;
    }

    private ActorPoolManager CreateManager()
    {
        // Clear stores between tests
        _nodeStore.Clear();
        _assignmentStore.Clear();

        return new ActorPoolManager(
            _stateStoreFactoryMock.Object,
            _loggerMock.Object,
            _configuration);
    }

    #region RegisterNodeAsync Tests

    [Fact]
    public async Task RegisterNodeAsync_NewNode_ReturnsTrue()
    {
        // Arrange
        var manager = CreateManager();
        var registration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        };

        // Act
        var result = await manager.RegisterNodeAsync(registration);

        // Assert
        Assert.True(result);
        Assert.True(_nodeStore.ContainsKey("node-1"));
    }

    [Fact]
    public async Task RegisterNodeAsync_ExistingNode_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();
        var firstRegistration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        };
        var secondRegistration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1", // Same node ID
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 100
        };

        // Act
        var firstResult = await manager.RegisterNodeAsync(firstRegistration);
        var secondResult = await manager.RegisterNodeAsync(secondRegistration);

        // Assert
        Assert.True(firstResult);  // First registration succeeds
        Assert.False(secondResult); // Second returns false (already exists)
    }

    [Fact]
    public async Task RegisterNodeAsync_SetsHealthyStatus()
    {
        // Arrange
        var manager = CreateManager();
        var registration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "npc-brain",
            Capacity = 100
        };

        // Act
        await manager.RegisterNodeAsync(registration);
        var node = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.NotNull(node);
        Assert.Equal(PoolNodeStatus.Healthy, node.Status);
        Assert.Equal(0, node.CurrentLoad);
        Assert.Equal(100, node.Capacity);
        Assert.Equal("npc-brain", node.PoolType);
    }

    #endregion

    #region UpdateNodeHeartbeatAsync Tests

    [Fact]
    public async Task UpdateNodeHeartbeatAsync_ExistingNode_UpdatesLastHeartbeat()
    {
        // Arrange
        var manager = CreateManager();
        var registration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        };
        await manager.RegisterNodeAsync(registration);

        var initialNode = await manager.GetNodeAsync("node-1");
        var initialHeartbeat = initialNode?.LastHeartbeat ?? DateTimeOffset.MinValue;

        await Task.Delay(10); // Small delay to ensure different timestamp

        var heartbeat = new PoolNodeHeartbeatEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            CurrentLoad = 15,
            Capacity = 50
        };

        // Act
        await manager.UpdateNodeHeartbeatAsync(heartbeat);
        var updatedNode = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.NotNull(updatedNode);
        Assert.Equal(15, updatedNode.CurrentLoad);
        Assert.True(updatedNode.LastHeartbeat >= initialHeartbeat);
    }

    [Fact]
    public async Task UpdateNodeHeartbeatAsync_UnknownNode_AutoRegisters()
    {
        // Arrange
        var manager = CreateManager();
        var heartbeat = new PoolNodeHeartbeatEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-new",
            AppId = "app-new",
            CurrentLoad = 5,
            Capacity = 30
        };

        // Act
        await manager.UpdateNodeHeartbeatAsync(heartbeat);
        var node = await manager.GetNodeAsync("node-new");

        // Assert
        Assert.NotNull(node);
        Assert.Equal("node-new", node.NodeId);
        Assert.Equal(5, node.CurrentLoad);
    }

    [Fact]
    public async Task UpdateNodeHeartbeatAsync_UnhealthyNode_BecomesHealthy()
    {
        // Arrange
        var manager = CreateManager();
        var registration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        };
        await manager.RegisterNodeAsync(registration);

        // Manually set to unhealthy
        var node = await manager.GetNodeAsync("node-1");
        if (node != null)
        {
            node.Status = PoolNodeStatus.Unhealthy;
            _nodeStore["node-1"] = node;
        }

        var heartbeat = new PoolNodeHeartbeatEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            CurrentLoad = 10,
            Capacity = 50
        };

        // Act
        await manager.UpdateNodeHeartbeatAsync(heartbeat);
        var updatedNode = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.NotNull(updatedNode);
        Assert.Equal(PoolNodeStatus.Healthy, updatedNode.Status);
    }

    #endregion

    #region DrainNodeAsync Tests

    [Fact]
    public async Task DrainNodeAsync_ExistingNode_SetsStatusToDraining()
    {
        // Arrange
        var manager = CreateManager();
        var registration = new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        };
        await manager.RegisterNodeAsync(registration);

        // Act
        var remainingActors = await manager.DrainNodeAsync("node-1", 5);
        var node = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.Equal(5, remainingActors);
        Assert.NotNull(node);
        Assert.Equal(PoolNodeStatus.Draining, node.Status);
    }

    [Fact]
    public async Task DrainNodeAsync_UnknownNode_ReturnsZero()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.DrainNodeAsync("node-unknown", 10);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region AcquireNodeForActorAsync Tests

    [Fact]
    public async Task AcquireNodeForActorAsync_NodesWithCapacity_ReturnsLeastLoaded()
    {
        // Arrange
        var manager = CreateManager();

        // Register two nodes with different loads
        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-2",
            AppId = "app-2",
            PoolType = "shared",
            Capacity = 50
        });

        // Set different loads
        var node1 = await manager.GetNodeAsync("node-1");
        var node2 = await manager.GetNodeAsync("node-2");
        if (node1 != null) { node1.CurrentLoad = 30; _nodeStore["node-1"] = node1; }
        if (node2 != null) { node2.CurrentLoad = 10; _nodeStore["node-2"] = node2; }

        // Act
        var result = await manager.AcquireNodeForActorAsync("npc-brain");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("node-2", result.NodeId); // Should pick least loaded
    }

    [Fact]
    public async Task AcquireNodeForActorAsync_NoCapacity_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        // Set to full capacity
        var node = await manager.GetNodeAsync("node-1");
        if (node != null) { node.CurrentLoad = 50; _nodeStore["node-1"] = node; }

        // Act
        var result = await manager.AcquireNodeForActorAsync("shared");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AcquireNodeForActorAsync_DrainingNode_SkipsIt()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        // Set to draining
        await manager.DrainNodeAsync("node-1", 5);

        // Act
        var result = await manager.AcquireNodeForActorAsync("shared");

        // Assert
        Assert.Null(result); // Draining node should be skipped
    }

    [Fact]
    public async Task AcquireNodeForActorAsync_NoNodes_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.AcquireNodeForActorAsync("shared");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RecordActorAssignmentAsync Tests

    [Fact]
    public async Task RecordActorAssignmentAsync_SavesAssignment()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var assignment = new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1",
            Status = "pending"
        };

        // Act
        await manager.RecordActorAssignmentAsync(assignment);
        var retrieved = await manager.GetActorAssignmentAsync("actor-1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("actor-1", retrieved.ActorId);
        Assert.Equal("node-1", retrieved.NodeId);
    }

    [Fact]
    public async Task RecordActorAssignmentAsync_IncrementsNodeLoad()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var initialNode = await manager.GetNodeAsync("node-1");
        var initialLoad = initialNode?.CurrentLoad ?? 0;

        var assignment = new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1",
            Status = "pending"
        };

        // Act
        await manager.RecordActorAssignmentAsync(assignment);
        var updatedNode = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.NotNull(updatedNode);
        Assert.Equal(initialLoad + 1, updatedNode.CurrentLoad);
    }

    #endregion

    #region GetActorAssignmentAsync Tests

    [Fact]
    public async Task GetActorAssignmentAsync_ExistingActor_ReturnsAssignment()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var assignment = new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1"
        };
        await manager.RecordActorAssignmentAsync(assignment);

        // Act
        var result = await manager.GetActorAssignmentAsync("actor-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("actor-1", result.ActorId);
        Assert.Equal("node-1", result.NodeId);
    }

    [Fact]
    public async Task GetActorAssignmentAsync_NonExistingActor_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.GetActorAssignmentAsync("actor-unknown");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RemoveActorAssignmentAsync Tests

    [Fact]
    public async Task RemoveActorAssignmentAsync_ExistingActor_ReturnsTrue()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var assignment = new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1"
        };
        await manager.RecordActorAssignmentAsync(assignment);

        // Act
        var result = await manager.RemoveActorAssignmentAsync("actor-1");
        var afterRemoval = await manager.GetActorAssignmentAsync("actor-1");

        // Assert
        Assert.True(result);
        Assert.Null(afterRemoval);
    }

    [Fact]
    public async Task RemoveActorAssignmentAsync_NonExistingActor_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.RemoveActorAssignmentAsync("actor-unknown");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RemoveActorAssignmentAsync_DecrementsNodeLoad()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var assignment = new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1"
        };
        await manager.RecordActorAssignmentAsync(assignment);

        var nodeAfterAdd = await manager.GetNodeAsync("node-1");
        var loadAfterAdd = nodeAfterAdd?.CurrentLoad ?? 0;

        // Act
        await manager.RemoveActorAssignmentAsync("actor-1");
        var nodeAfterRemove = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.NotNull(nodeAfterRemove);
        Assert.Equal(loadAfterAdd - 1, nodeAfterRemove.CurrentLoad);
    }

    #endregion

    #region GetUnhealthyNodesAsync Tests

    [Fact]
    public async Task GetUnhealthyNodesAsync_NodeMissedHeartbeat_ReturnsNode()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        // Set last heartbeat to old time
        var node = await manager.GetNodeAsync("node-1");
        if (node != null)
        {
            node.LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-60);
            _nodeStore["node-1"] = node;
        }

        var timeout = TimeSpan.FromSeconds(30);

        // Act
        var result = await manager.GetUnhealthyNodesAsync(timeout);

        // Assert
        Assert.Single(result);
        Assert.Equal("node-1", result[0].NodeId);
    }

    [Fact]
    public async Task GetUnhealthyNodesAsync_AllNodesHealthy_ReturnsEmpty()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        var timeout = TimeSpan.FromSeconds(30);

        // Act
        var result = await manager.GetUnhealthyNodesAsync(timeout);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUnhealthyNodesAsync_ExcludesAlreadyUnhealthyNodes()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        // Set to already unhealthy with old heartbeat
        var node = await manager.GetNodeAsync("node-1");
        if (node != null)
        {
            node.Status = PoolNodeStatus.Unhealthy;
            node.LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-60);
            _nodeStore["node-1"] = node;
        }

        var timeout = TimeSpan.FromSeconds(30);

        // Act
        var result = await manager.GetUnhealthyNodesAsync(timeout);

        // Assert - already unhealthy nodes are excluded (to avoid re-processing)
        Assert.Empty(result);
    }

    #endregion

    #region GetCapacitySummaryAsync Tests

    [Fact]
    public async Task GetCapacitySummaryAsync_ReturnsCorrectSummary()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-2",
            AppId = "app-2",
            PoolType = "shared",
            Capacity = 50
        });

        // Set loads
        var node1 = await manager.GetNodeAsync("node-1");
        var node2 = await manager.GetNodeAsync("node-2");
        if (node1 != null) { node1.CurrentLoad = 20; _nodeStore["node-1"] = node1; }
        if (node2 != null) { node2.CurrentLoad = 30; _nodeStore["node-2"] = node2; }

        // Act
        var result = await manager.GetCapacitySummaryAsync();

        // Assert
        Assert.Equal(2, result.TotalNodes);
        Assert.Equal(2, result.HealthyNodes);
        Assert.Equal(100, result.TotalCapacity);
        Assert.Equal(50, result.TotalLoad);
    }

    [Fact]
    public async Task GetCapacitySummaryAsync_ByPoolType_GroupsCorrectly()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-2",
            AppId = "app-2",
            PoolType = "npc-brain",
            Capacity = 100
        });

        // Act
        var result = await manager.GetCapacitySummaryAsync();

        // Assert
        Assert.Equal(2, result.TotalNodes);
        Assert.True(result.ByPoolType.ContainsKey("shared"));
        Assert.True(result.ByPoolType.ContainsKey("npc-brain"));
        Assert.Equal(50, result.ByPoolType["shared"].TotalCapacity);
        Assert.Equal(100, result.ByPoolType["npc-brain"].TotalCapacity);
    }

    [Fact]
    public async Task GetCapacitySummaryAsync_ExcludesDrainingFromCapacity()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-2",
            AppId = "app-2",
            PoolType = "shared",
            Capacity = 50
        });

        // Drain one node
        await manager.DrainNodeAsync("node-1", 5);

        // Act
        var result = await manager.GetCapacitySummaryAsync();

        // Assert
        Assert.Equal(2, result.TotalNodes);
        Assert.Equal(1, result.HealthyNodes);
        Assert.Equal(1, result.DrainingNodes);
        Assert.Equal(50, result.TotalCapacity); // Only healthy node capacity
    }

    #endregion

    #region ListNodesAsync Tests

    [Fact]
    public async Task ListNodesAsync_ReturnsAllNodes()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-2",
            AppId = "app-2",
            PoolType = "npc-brain",
            Capacity = 100
        });

        // Act
        var result = await manager.ListNodesAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListNodesAsync_EmptyPool_ReturnsEmpty()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.ListNodesAsync();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region RemoveNodeAsync Tests

    [Fact]
    public async Task RemoveNodeAsync_ExistingNode_RemovesNode()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        // Act
        await manager.RemoveNodeAsync("node-1", "test removal");
        var node = await manager.GetNodeAsync("node-1");

        // Assert
        Assert.Null(node);
    }

    [Fact]
    public async Task RemoveNodeAsync_WithAssignedActors_RemovesAssignments()
    {
        // Arrange
        var manager = CreateManager();

        await manager.RegisterNodeAsync(new PoolNodeRegisteredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50
        });

        await manager.RecordActorAssignmentAsync(new ActorAssignment
        {
            ActorId = "actor-1",
            NodeId = "node-1",
            NodeAppId = "app-1",
            TemplateId = "template-1"
        });

        // Act
        var removedActorCount = await manager.RemoveNodeAsync("node-1", "test removal");
        var assignment = await manager.GetActorAssignmentAsync("actor-1");

        // Assert
        Assert.Equal(1, removedActorCount);
        Assert.Null(assignment);
    }

    #endregion
}
