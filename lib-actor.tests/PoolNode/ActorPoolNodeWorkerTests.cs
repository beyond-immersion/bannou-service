using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.PoolNode;

/// <summary>
/// Unit tests for ActorPoolNodeWorker - pool node background service.
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

    [Fact]
    public async Task ExecuteAsync_PublishesRegistrationEvent()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(500); // Allow registration to happen
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.registered",
                It.Is<PoolNodeRegisteredEvent>(e =>
                    e.NodeId == "test-node-1" &&
                    e.AppId == "test-app-1" &&
                    e.PoolType == "shared" &&
                    e.Capacity == 50),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

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

    [Fact]
    public async Task HandleStopCommandAsync_StopsActor()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner.SetupGet(r => r.LoopIterations).Returns(42);
        mockRunner.SetupGet(r => r.CharacterId).Returns(Guid.NewGuid());

        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out It.Ref<IActorRunner?>.IsAny))
            .Callback((string id, out IActorRunner? runner) => runner = mockRunner.Object)
            .Returns(true);

        _actorRegistryMock
            .Setup(r => r.TryRemove("actor-1", out It.Ref<IActorRunner?>.IsAny))
            .Returns(true);

        var command = new StopActorCommand
        {
            ActorId = "actor-1",
            Graceful = true
        };

        // Act
        var result = await worker.HandleStopCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockRunner.Verify(r => r.StopAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

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

    [Fact]
    public async Task HandleStopCommandAsync_PublishesCompletedEvent()
    {
        // Arrange
        var worker = CreateWorker();
        var characterId = Guid.NewGuid();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner.SetupGet(r => r.LoopIterations).Returns(100);
        mockRunner.SetupGet(r => r.CharacterId).Returns(characterId);

        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out It.Ref<IActorRunner?>.IsAny))
            .Callback((string id, out IActorRunner? runner) => runner = mockRunner.Object)
            .Returns(true);

        _actorRegistryMock
            .Setup(r => r.TryRemove("actor-1", out It.Ref<IActorRunner?>.IsAny))
            .Returns(true);

        var command = new StopActorCommand
        {
            ActorId = "actor-1",
            Graceful = false
        };

        // Act
        await worker.HandleStopCommandAsync(command, CancellationToken.None);

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.instance.completed",
                It.Is<ActorCompletedEvent>(e =>
                    e.ActorId == "actor-1" &&
                    e.NodeId == "test-node-1" &&
                    e.ExitReason == ActorCompletedEventExitReason.External_stop &&
                    e.LoopIterations == 100 &&
                    e.CharacterId == characterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Shutdown_StopsAllRunningActors()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        var mockRunner1 = new Mock<IActorRunner>();
        mockRunner1.SetupGet(r => r.ActorId).Returns("actor-1");
        var mockRunner2 = new Mock<IActorRunner>();
        mockRunner2.SetupGet(r => r.ActorId).Returns("actor-2");

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(new[] { mockRunner1.Object, mockRunner2.Object });

        _actorRegistryMock
            .Setup(r => r.TryRemove(It.IsAny<string>(), out It.Ref<IActorRunner?>.IsAny))
            .Returns(true);

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Assert - both actors should be stopped gracefully
        mockRunner1.Verify(r => r.StopAsync(true, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockRunner2.Verify(r => r.StopAsync(true, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Shutdown_PublishesDrainingEvent()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(new[] { mockRunner.Object });

        _actorRegistryMock
            .Setup(r => r.TryRemove(It.IsAny<string>(), out It.Ref<IActorRunner?>.IsAny))
            .Returns(true);

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.draining",
                It.Is<PoolNodeDrainingEvent>(e =>
                    e.NodeId == "test-node-1" &&
                    e.RemainingActors == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPoolNodeId_StopsEarly()
    {
        // Arrange
        var configWithoutNodeId = new ActorServiceConfiguration
        {
            PoolNodeId = "",
            PoolNodeAppId = "test-app-1",
            HeartbeatIntervalSeconds = 10
        };

        var heartbeatLoggerMock = new Mock<ILogger<HeartbeatEmitter>>();
        var heartbeatEmitter = new HeartbeatEmitter(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            configWithoutNodeId,
            heartbeatLoggerMock.Object);

        var worker = new ActorPoolNodeWorker(
            _messageBusMock.Object,
            _actorRegistryMock.Object,
            _actorRunnerFactoryMock.Object,
            heartbeatEmitter,
            configWithoutNodeId,
            _loggerMock.Object);

        using var cts = new CancellationTokenSource();

        _actorRegistryMock
            .Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        // Act
        var task = worker.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        heartbeatEmitter.Dispose();

        // Assert - should not have registered
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.pool-node.registered",
                It.IsAny<PoolNodeRegisteredEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
