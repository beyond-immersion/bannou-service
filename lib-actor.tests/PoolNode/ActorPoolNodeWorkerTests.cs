using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.PoolNode;

/// <summary>
/// Unit tests for ActorPoolNodeWorker.
/// Tests direct method behavior without complex BackgroundService lifecycle.
/// Timing-based lifecycle tests belong in integration tests.
/// </summary>
public class ActorPoolNodeWorkerTests
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<IActorRegistry> _actorRegistryMock;
    private readonly Mock<IActorRunnerFactory> _actorRunnerFactoryMock;
    private readonly Mock<ILogger<ActorPoolNodeWorker>> _loggerMock;
    private readonly ActorServiceConfiguration _configuration;
    private readonly HeartbeatEmitter _heartbeatEmitter;

    public ActorPoolNodeWorkerTests()
    {
        _messageBusMock = new Mock<IMessageBus>();
        _actorRegistryMock = new Mock<IActorRegistry>();
        _actorRunnerFactoryMock = new Mock<IActorRunnerFactory>();
        _loggerMock = new Mock<ILogger<ActorPoolNodeWorker>>();

        _configuration = new ActorServiceConfiguration
        {
            PoolNodeId = "test-node-1",
            PoolNodeAppId = "test-app-1",
            PoolNodeType = "shared",
            PoolNodeCapacity = 50,
            HeartbeatIntervalSeconds = 10
        };

        // Create a real HeartbeatEmitter (it's a sealed class, not an interface)
        var heartbeatLoggerMock = new Mock<ILogger<HeartbeatEmitter>>();
        _heartbeatEmitter = new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            _configuration,
            heartbeatLoggerMock.Object);
    }

    private ActorPoolNodeWorker CreateWorker()
    {
        return new ActorPoolNodeWorker(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            _actorRunnerFactoryMock.Object,
            _heartbeatEmitter,
            _configuration,
            _loggerMock.Object);
    }

    #region Constructor Guard Clauses

    [Fact]
    public void Constructor_NullMessageBus_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(null!, _actorRegistryMock.Object, _actorRunnerFactoryMock.Object,
                _heartbeatEmitter, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullActorRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(_messageBusMock.Object, null!, _actorRunnerFactoryMock.Object,
                _heartbeatEmitter, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullActorRunnerFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(_messageBusMock.Object, _actorRegistryMock.Object, null!,
                _heartbeatEmitter, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullHeartbeatEmitter_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(_messageBusMock.Object, _actorRegistryMock.Object, _actorRunnerFactoryMock.Object,
                null!, _configuration, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(_messageBusMock.Object, _actorRegistryMock.Object, _actorRunnerFactoryMock.Object,
                _heartbeatEmitter, null!, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ActorPoolNodeWorker(_messageBusMock.Object, _actorRegistryMock.Object, _actorRunnerFactoryMock.Object,
                _heartbeatEmitter, _configuration, null!));
    }

    #endregion

    #region HandleSpawnCommandAsync Tests

    [Fact]
    public async Task HandleSpawnCommandAsync_CreatesAndStartsActor()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Running);

        _actorRunnerFactoryMock
            .Setup(f => f.Create(
                "actor-1",
                It.IsAny<ActorTemplateData>(),
                It.IsAny<Guid?>(),
                It.IsAny<object?>(),
                It.IsAny<object?>()))
            .Returns(mockRunner.Object);

        _actorRegistryMock
            .Setup(r => r.TryRegister("actor-1", mockRunner.Object))
            .Returns(true);

        var command = new SpawnActorCommand
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            BehaviorRef = "behavior.yaml",
            TickIntervalMs = 100
        };

        // Act
        var result = await worker.HandleSpawnCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockRunner.Verify(r => r.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSpawnCommandAsync_ActorAlreadyExists_ReturnsFalse()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");

        _actorRunnerFactoryMock
            .Setup(f => f.Create(
                "actor-1",
                It.IsAny<ActorTemplateData>(),
                It.IsAny<Guid?>(),
                It.IsAny<object?>(),
                It.IsAny<object?>()))
            .Returns(mockRunner.Object);

        _actorRegistryMock
            .Setup(r => r.TryRegister("actor-1", mockRunner.Object))
            .Returns(false); // Already exists

        var command = new SpawnActorCommand
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            BehaviorRef = "behavior.yaml"
        };

        // Act
        var result = await worker.HandleSpawnCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HandleSpawnCommandAsync_PublishesStatusChangedEvent()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");

        _actorRunnerFactoryMock
            .Setup(f => f.Create(
                "actor-1",
                It.IsAny<ActorTemplateData>(),
                It.IsAny<Guid?>(),
                It.IsAny<object?>(),
                It.IsAny<object?>()))
            .Returns(mockRunner.Object);

        _actorRegistryMock
            .Setup(r => r.TryRegister("actor-1", mockRunner.Object))
            .Returns(true);

        var command = new SpawnActorCommand
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            BehaviorRef = "behavior.yaml"
        };

        // Act
        await worker.HandleSpawnCommandAsync(command, CancellationToken.None);

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.instance.status-changed",
                It.Is<ActorStatusChangedEvent>(e =>
                    e.ActorId == "actor-1" &&
                    e.PreviousStatus == "pending" &&
                    e.NewStatus == "running"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSpawnCommandAsync_Exception_PublishesErrorStatus()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.Setup(r => r.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        _actorRunnerFactoryMock
            .Setup(f => f.Create(
                "actor-1",
                It.IsAny<ActorTemplateData>(),
                It.IsAny<Guid?>(),
                It.IsAny<object?>(),
                It.IsAny<object?>()))
            .Returns(mockRunner.Object);

        _actorRegistryMock
            .Setup(r => r.TryRegister("actor-1", mockRunner.Object))
            .Returns(true);

        var command = new SpawnActorCommand
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            BehaviorRef = "behavior.yaml"
        };

        // Act
        var result = await worker.HandleSpawnCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result);
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.instance.status-changed",
                It.Is<ActorStatusChangedEvent>(e =>
                    e.ActorId == "actor-1" &&
                    e.NewStatus == "error"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region HandleStopCommandAsync Tests

    [Fact]
    public async Task HandleStopCommandAsync_ActorNotFound_ReturnsFalse()
    {
        // Arrange
        var worker = CreateWorker();

        _actorRegistryMock
            .Setup(r => r.TryGet("actor-unknown", out It.Ref<IActorRunner?>.IsAny))
            .Returns(false);

        var command = new StopActorCommand
        {
            ActorId = "actor-unknown",
            Graceful = true
        };

        // Act
        var result = await worker.HandleStopCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion
}
