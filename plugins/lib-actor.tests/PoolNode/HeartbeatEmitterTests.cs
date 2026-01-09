using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.PoolNode;

/// <summary>
/// Unit tests for HeartbeatEmitter.
/// Only tests that verify real behavior without complex mocking.
/// Timing-based lifecycle tests belong in integration tests.
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

    private HeartbeatEmitter CreateEmitter(ActorServiceConfiguration? config = null)
    {
        return new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            config ?? _configuration,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<HeartbeatEmitter>();
        Assert.NotNull(CreateEmitter());
    }

    #endregion

    #region Lifecycle Edge Cases

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
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var emitter = CreateEmitter();

        // Act & Assert - IDisposable contract requires this to be safe
        emitter.Dispose();
        emitter.Dispose();
        emitter.Dispose();
    }

    #endregion

    #region Exception Recovery

    [Fact]
    public async Task EmitHeartbeat_ExceptionInPublish_ContinuesEmitting()
    {
        // Arrange - use fast heartbeat interval for quicker test
        var fastConfig = new ActorServiceConfiguration
        {
            PoolNodeId = "test-node-1",
            PoolNodeAppId = "test-app-1",
            PoolNodeType = "shared",
            HeartbeatIntervalSeconds = 1
        };

        var emitter = CreateEmitter(fastConfig);
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

        // Act - wait for multiple heartbeat cycles
        emitter.Start();
        await Task.Delay(2500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - should have continued after exception
        Assert.True(callCount >= 2, $"Expected at least 2 calls but got {callCount}");
    }

    #endregion

    #region Event Data Validation

    [Fact]
    public async Task HeartbeatEvent_HasCorrectStructure()
    {
        // Arrange
        var emitter = CreateEmitter();
        PoolNodeHeartbeatEvent? capturedEvent = null;

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

        // Assert - verify the event has all required fields populated correctly
        Assert.NotNull(capturedEvent);
        Assert.Equal("test-node-1", capturedEvent.NodeId);
        Assert.Equal("test-app-1", capturedEvent.AppId);
        Assert.True(capturedEvent.Timestamp >= beforeStart);
        Assert.True(capturedEvent.Timestamp <= afterStop);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
    }

    [Fact]
    public async Task HeartbeatEvent_IncludesActorCount()
    {
        // Arrange
        var emitter = CreateEmitter();
        PoolNodeHeartbeatEvent? capturedEvent = null;

        var mockRunner1 = new Mock<IActorRunner>();
        var mockRunner2 = new Mock<IActorRunner>();
        var mockRunner3 = new Mock<IActorRunner>();

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(new[] { mockRunner1.Object, mockRunner2.Object, mockRunner3.Object });

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

        // Act
        emitter.Start();
        await Task.Delay(1500);
        await emitter.StopAsync();
        emitter.Dispose();

        // Assert - verify actor count is included
        Assert.NotNull(capturedEvent);
        Assert.Equal(3, capturedEvent.CurrentLoad);
    }

    #endregion
}
