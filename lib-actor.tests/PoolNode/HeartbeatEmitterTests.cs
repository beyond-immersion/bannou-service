using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.PoolNode;

/// <summary>
/// Unit tests for HeartbeatEmitter - periodic heartbeat publishing.
/// </summary>
public class HeartbeatEmitterTests
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<IActorRegistry> _actorRegistryMock;
    private readonly Mock<ILogger<HeartbeatEmitter>> _loggerMock;
    private readonly ActorServiceConfiguration _configuration;

    public HeartbeatEmitterTests()
    {
        _messageBusMock = new Mock<IMessageBus>();
        _actorRegistryMock = new Mock<IActorRegistry>();
        _loggerMock = new Mock<ILogger<HeartbeatEmitter>>();

        _configuration = new ActorServiceConfiguration
        {
            PoolNodeId = "test-node-1",
            PoolNodeAppId = "test-app-1",
            PoolNodeType = "shared",
            HeartbeatIntervalSeconds = 1
        };
    }

    private HeartbeatEmitter CreateEmitter()
    {
        return new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_NullMessageBus_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HeartbeatEmitter(null!, _actorRegistryMock.Object, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullActorRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HeartbeatEmitter(_messageBusMock.Object, null!, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HeartbeatEmitter(_messageBusMock.Object, _actorRegistryMock.Object, null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HeartbeatEmitter(_messageBusMock.Object, _actorRegistryMock.Object, _configuration, null!));
    }

    [Fact]
    public async Task Start_EmitsHeartbeat()
    {
        // Arrange
        var emitter = CreateEmitter();
        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        emitter.Start();
        await Task.Delay(1500); // Wait for at least one heartbeat
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - use the convenience overload signature (topic, event, ct)
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.Is<PoolNodeHeartbeatEvent>(e =>
                    e.NodeId == "test-node-1" &&
                    e.AppId == "test-app-1" &&
                    e.CurrentLoad == 0),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Start_IncludesActorCount()
    {
        // Arrange
        var emitter = CreateEmitter();
        var mockRunner1 = new Mock<IActorRunner>();
        var mockRunner2 = new Mock<IActorRunner>();
        var mockRunner3 = new Mock<IActorRunner>();

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(new[] { mockRunner1.Object, mockRunner2.Object, mockRunner3.Object });

        // Act
        emitter.Start();
        await Task.Delay(1500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.Is<PoolNodeHeartbeatEvent>(e => e.CurrentLoad == 3),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Start_CalledTwice_DoesNotStartSecondTask()
    {
        // Arrange
        var emitter = CreateEmitter();
        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        emitter.Start();
        emitter.Start(); // Second call should be ignored
        await Task.Delay(500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - no exception thrown means test passes
    }

    [Fact]
    public async Task StopAsync_StopsHeartbeats()
    {
        // Arrange
        var emitter = CreateEmitter();
        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        emitter.Start();
        await Task.Delay(500);
        await emitter.StopAsync();

        // Clear the mock to reset verification count
        _messageBusMock.Invocations.Clear();

        // Wait and verify no more heartbeats are sent
        await Task.Delay(2000);
        emitter.Dispose();

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.IsAny<PoolNodeHeartbeatEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StopAsync_NotStarted_DoesNotThrow()
    {
        // Arrange
        var emitter = CreateEmitter();

        // Act & Assert - should not throw
        await emitter.StopAsync();
        emitter.Dispose();
    }

    [Fact]
    public async Task EmitHeartbeat_MissingNodeId_DoesNotPublish()
    {
        // Arrange
        var configWithoutNodeId = new ActorServiceConfiguration
        {
            PoolNodeId = "",
            PoolNodeAppId = "test-app",
            HeartbeatIntervalSeconds = 1
        };

        var emitter = new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            configWithoutNodeId,
            _loggerMock.Object);

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        emitter.Start();
        await Task.Delay(1500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - should not have published
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.IsAny<PoolNodeHeartbeatEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmitHeartbeat_MissingAppId_DoesNotPublish()
    {
        // Arrange
        var configWithoutAppId = new ActorServiceConfiguration
        {
            PoolNodeId = "test-node",
            PoolNodeAppId = "",
            HeartbeatIntervalSeconds = 1
        };

        var emitter = new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            configWithoutAppId,
            _loggerMock.Object);

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        emitter.Start();
        await Task.Delay(1500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - should not have published
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.IsAny<PoolNodeHeartbeatEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var emitter = CreateEmitter();

        // Act & Assert
        emitter.Dispose();
        emitter.Dispose();
        emitter.Dispose();
        // No exception means test passes
    }

    [Fact]
    public async Task EmitHeartbeat_ExceptionInPublish_ContinuesEmitting()
    {
        // Arrange
        var emitter = CreateEmitter();
        var callCount = 0;

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        _messageBusMock
            .Setup(m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.IsAny<PoolNodeHeartbeatEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Test exception");
            })
            .ReturnsAsync(true);

        // Act
        emitter.Start();
        await Task.Delay(3000); // Wait for multiple heartbeat cycles
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - should have been called multiple times despite first exception
        Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task HeartbeatEvent_HasCorrectTimestamp()
    {
        // Arrange
        var emitter = CreateEmitter();
        var capturedEvent = (PoolNodeHeartbeatEvent?)null;

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        _messageBusMock
            .Setup(m => m.TryPublishAsync(
                "actor.pool-node.heartbeat",
                It.IsAny<PoolNodeHeartbeatEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PoolNodeHeartbeatEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var beforeStart = DateTimeOffset.UtcNow;

        // Act
        emitter.Start();
        await Task.Delay(1500);
        await emitter.StopAsync();
        emitter.Dispose();

        var afterStop = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.True(capturedEvent.Timestamp >= beforeStart);
        Assert.True(capturedEvent.Timestamp <= afterStop);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
    }
}
