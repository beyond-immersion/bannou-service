using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.Pool;

/// <summary>
/// Unit tests for PoolHealthMonitor - heartbeat monitoring and unhealthy node detection.
/// </summary>
public class PoolHealthMonitorTests
{
    private readonly Mock<IActorPoolManager> _poolManagerMock;
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<ILogger<PoolHealthMonitor>> _loggerMock;
    private readonly ActorServiceConfiguration _configuration;

    public PoolHealthMonitorTests()
    {
        _poolManagerMock = new Mock<IActorPoolManager>();
        _messageBusMock = new Mock<IMessageBus>();
        _loggerMock = new Mock<ILogger<PoolHealthMonitor>>();

        _configuration = new ActorServiceConfiguration
        {
            HeartbeatIntervalSeconds = 10,
            HeartbeatTimeoutSeconds = 30
        };
    }

    private PoolHealthMonitor CreateMonitor()
    {
        return new PoolHealthMonitor(
            _poolManagerMock.Object,
            _messageBusMock.Object,
            _loggerMock.Object,
            _configuration);
    }

    [Fact]
    public void Constructor_NullPoolManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PoolHealthMonitor(null!, _messageBusMock.Object, _loggerMock.Object, _configuration));
    }

    [Fact]
    public void Constructor_NullMessageBus_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PoolHealthMonitor(_poolManagerMock.Object, null!, _loggerMock.Object, _configuration));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PoolHealthMonitor(_poolManagerMock.Object, _messageBusMock.Object, null!, _configuration));
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PoolHealthMonitor(_poolManagerMock.Object, _messageBusMock.Object, _loggerMock.Object, null!));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        // Arrange
        var monitor = CreateMonitor();
        using var cts = new CancellationTokenSource();

        _poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PoolNodeState>());

        // Act
        cts.Cancel();
        await monitor.StartAsync(cts.Token);
        await monitor.StopAsync(cts.Token);

        // Assert - no exception means graceful shutdown
    }

    [Fact]
    public async Task ExecuteAsync_NoUnhealthyNodes_DoesNotPublishEvents()
    {
        // Arrange
        var monitor = CreateMonitor();
        using var cts = new CancellationTokenSource();

        _poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PoolNodeState>());

        // Act - start and stop quickly
        var task = monitor.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await monitor.StopAsync(CancellationToken.None);

        // Assert - no unhealthy events published
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.unhealthy",
                It.IsAny<PoolNodeUnhealthyEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckPoolHealthAsync_UnhealthyNodes_PublishesEvents()
    {
        // This test verifies the behavior after the initial 5-second delay
        // by using a short configuration that triggers faster checking

        // Arrange
        var shortConfiguration = new ActorServiceConfiguration
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 2
        };

        var poolManagerMock = new Mock<IActorPoolManager>();
        var messageBusMock = new Mock<IMessageBus>();
        var loggerMock = new Mock<ILogger<PoolHealthMonitor>>();

        var unhealthyNode = new PoolNodeState
        {
            NodeId = "node-1",
            AppId = "app-1",
            PoolType = "shared",
            Capacity = 50,
            CurrentLoad = 10,
            Status = PoolNodeStatus.Healthy,
            LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-60)
        };

        poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unhealthyNode });

        poolManagerMock
            .Setup(m => m.ListActorsByNodeAsync("node-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActorAssignment>
            {
                new ActorAssignment { ActorId = "actor-1", NodeId = "node-1", NodeAppId = "app-1", TemplateId = "t1" },
                new ActorAssignment { ActorId = "actor-2", NodeId = "node-1", NodeAppId = "app-1", TemplateId = "t1" }
            });

        poolManagerMock
            .Setup(m => m.GetCapacitySummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoolCapacitySummary { TotalNodes = 0, HealthyNodes = 0 });

        var monitor = new PoolHealthMonitor(
            poolManagerMock.Object,
            messageBusMock.Object,
            loggerMock.Object,
            shortConfiguration);

        using var cts = new CancellationTokenSource();

        // Act - run for long enough to trigger at least one check
        var task = monitor.StartAsync(cts.Token);
        await Task.Delay(6500); // Past the 5s initial delay + one check interval
        await cts.CancelAsync();
        await monitor.StopAsync(CancellationToken.None);

        // Assert - should have published unhealthy event
        messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.unhealthy",
                It.Is<PoolNodeUnhealthyEvent>(e =>
                    e.NodeId == "node-1" &&
                    e.AppId == "app-1" &&
                    e.Reason == "heartbeat_timeout" &&
                    e.ActorCount == 2),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckPoolHealthAsync_UnhealthyNode_RemovesFromPool()
    {
        // Arrange
        var shortConfiguration = new ActorServiceConfiguration
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 2
        };

        var poolManagerMock = new Mock<IActorPoolManager>();
        var messageBusMock = new Mock<IMessageBus>();
        var loggerMock = new Mock<ILogger<PoolHealthMonitor>>();

        var unhealthyNode = new PoolNodeState
        {
            NodeId = "node-unhealthy",
            AppId = "app-unhealthy",
            PoolType = "shared",
            Capacity = 50,
            CurrentLoad = 0,
            Status = PoolNodeStatus.Healthy,
            LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-60)
        };

        poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unhealthyNode });

        poolManagerMock
            .Setup(m => m.ListActorsByNodeAsync("node-unhealthy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActorAssignment>());

        poolManagerMock
            .Setup(m => m.GetCapacitySummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoolCapacitySummary { TotalNodes = 0, HealthyNodes = 0 });

        var monitor = new PoolHealthMonitor(
            poolManagerMock.Object,
            messageBusMock.Object,
            loggerMock.Object,
            shortConfiguration);

        using var cts = new CancellationTokenSource();

        // Act
        var task = monitor.StartAsync(cts.Token);
        await Task.Delay(6500);
        await cts.CancelAsync();
        await monitor.StopAsync(CancellationToken.None);

        // Assert - should have called RemoveNodeAsync
        poolManagerMock.Verify(
            m => m.RemoveNodeAsync("node-unhealthy", "heartbeat_timeout", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckPoolHealthAsync_MultipleUnhealthyNodes_ProcessesAll()
    {
        // Arrange
        var shortConfiguration = new ActorServiceConfiguration
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 2
        };

        var poolManagerMock = new Mock<IActorPoolManager>();
        var messageBusMock = new Mock<IMessageBus>();
        var loggerMock = new Mock<ILogger<PoolHealthMonitor>>();

        var unhealthyNodes = new[]
        {
            new PoolNodeState
            {
                NodeId = "node-1",
                AppId = "app-1",
                PoolType = "shared",
                Capacity = 50,
                CurrentLoad = 5,
                Status = PoolNodeStatus.Healthy,
                LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-60)
            },
            new PoolNodeState
            {
                NodeId = "node-2",
                AppId = "app-2",
                PoolType = "shared",
                Capacity = 50,
                CurrentLoad = 10,
                Status = PoolNodeStatus.Healthy,
                LastHeartbeat = DateTimeOffset.UtcNow.AddSeconds(-120)
            }
        };

        // Return nodes only once, then empty (simulating they've been processed)
        var callCount = 0;
        poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => callCount++ == 0 ? unhealthyNodes : Array.Empty<PoolNodeState>());

        poolManagerMock
            .Setup(m => m.ListActorsByNodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActorAssignment>());

        poolManagerMock
            .Setup(m => m.GetCapacitySummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoolCapacitySummary { TotalNodes = 0, HealthyNodes = 0 });

        var monitor = new PoolHealthMonitor(
            poolManagerMock.Object,
            messageBusMock.Object,
            loggerMock.Object,
            shortConfiguration);

        using var cts = new CancellationTokenSource();

        // Act
        var task = monitor.StartAsync(cts.Token);
        await Task.Delay(6500);
        await cts.CancelAsync();
        await monitor.StopAsync(CancellationToken.None);

        // Assert - both nodes should be removed
        poolManagerMock.Verify(
            m => m.RemoveNodeAsync("node-1", "heartbeat_timeout", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        poolManagerMock.Verify(
            m => m.RemoveNodeAsync("node-2", "heartbeat_timeout", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckPoolHealthAsync_ExceptionInHealthCheck_ContinuesRunning()
    {
        // Arrange
        var shortConfiguration = new ActorServiceConfiguration
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 2
        };

        var poolManagerMock = new Mock<IActorPoolManager>();
        var messageBusMock = new Mock<IMessageBus>();
        var loggerMock = new Mock<ILogger<PoolHealthMonitor>>();

        var callCount = 0;
        poolManagerMock
            .Setup(m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Test exception");
                return Array.Empty<PoolNodeState>();
            });

        var monitor = new PoolHealthMonitor(
            poolManagerMock.Object,
            messageBusMock.Object,
            loggerMock.Object,
            shortConfiguration);

        using var cts = new CancellationTokenSource();

        // Act - should continue running after exception
        var task = monitor.StartAsync(cts.Token);
        await Task.Delay(7500); // Long enough for at least 2 check cycles
        await cts.CancelAsync();
        await monitor.StopAsync(CancellationToken.None);

        // Assert - should have been called multiple times despite first exception
        poolManagerMock.Verify(
            m => m.GetUnhealthyNodesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }
}
