using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests.PoolNode;

/// <summary>
/// Unit tests for ActorPoolNodeWorker.
/// Tests direct method behavior without complex BackgroundService lifecycle.
/// Timing-based lifecycle tests belong in integration tests.
/// </summary>
public class ActorPoolNodeWorkerTests : IAsyncLifetime
{
    private readonly Mock<IMessageBus> _messageBusMock;
    private readonly Mock<IMessageSubscriber> _messageSubscriberMock;
    private readonly Mock<IActorRegistry> _actorRegistryMock;
    private readonly Mock<IActorRunnerFactory> _actorRunnerFactoryMock;
    private readonly Mock<ILogger<ActorPoolNodeWorker>> _loggerMock;
    private readonly ActorServiceConfiguration _configuration;
    private readonly HeartbeatEmitter _heartbeatEmitter;
    private readonly List<ActorPoolNodeWorker> _createdWorkers = new();

    public ActorPoolNodeWorkerTests()
    {
        _messageBusMock = new Mock<IMessageBus>();
        _messageSubscriberMock = new Mock<IMessageSubscriber>();
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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var worker in _createdWorkers)
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
        _createdWorkers.Clear();
        _heartbeatEmitter.Dispose();
    }

    private ActorPoolNodeWorker CreateWorker()
    {
        var worker = new ActorPoolNodeWorker(
            _messageBusMock.Object,
            _messageSubscriberMock.Object,
            _actorRegistryMock.Object,
            _actorRunnerFactoryMock.Object,
            _heartbeatEmitter,
            _configuration,
            _loggerMock.Object);
        _createdWorkers.Add(worker);
        return worker;
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<ActorPoolNodeWorker>();
        Assert.NotNull(CreateWorker());
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
                It.IsAny<Guid>(),
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
            TickIntervalMs = 100,
            RealmId = Guid.NewGuid()
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
                It.IsAny<Guid>(),
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
            BehaviorRef = "behavior.yaml",
            RealmId = Guid.NewGuid()
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
                It.IsAny<Guid>(),
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
            RealmId = Guid.NewGuid()
        };

        // Act
        await worker.HandleSpawnCommandAsync(command, CancellationToken.None);

        // Assert
        _messageBusMock.Verify(
            m => m.TryPublishAsync(
                "actor.instance.status-changed",
                It.Is<ActorStatusChangedEvent>(e =>
                    e.ActorId == "actor-1" &&
                    e.PreviousStatus == ActorStatus.Pending &&
                    e.NewStatus == ActorStatus.Running),
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
                It.IsAny<Guid>(),
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
            RealmId = Guid.NewGuid()
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
                    e.NewStatus == ActorStatus.Error),
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

    #region HandleMessageCommandAsync Tests

    [Fact]
    public async Task HandleMessageCommandAsync_ActorNotFound_ReturnsFalse()
    {
        // Arrange
        var worker = CreateWorker();

        _actorRegistryMock
            .Setup(r => r.TryGet("actor-unknown", out It.Ref<IActorRunner?>.IsAny))
            .Returns(false);

        var command = new SendMessageCommand
        {
            ActorId = "actor-unknown",
            MessageType = "test-message"
        };

        // Act
        var result = await worker.HandleMessageCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HandleMessageCommandAsync_InjectsPerceptionToActor()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.PerceptionQueueDepth).Returns(1);
        mockRunner.Setup(r => r.InjectPerception(It.IsAny<PerceptionData>())).Returns(true);

        var outRunner = mockRunner.Object;
        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out outRunner))
            .Returns(true);

        var command = new SendMessageCommand
        {
            ActorId = "actor-1",
            MessageType = "perception-update",
            Payload = new { key = "value" }
        };

        // Act
        var result = await worker.HandleMessageCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockRunner.Verify(
            r => r.InjectPerception(It.Is<PerceptionData>(p =>
                p.PerceptionType == "perception-update" &&
                p.SourceType == PerceptionSourceType.Message)),
            Times.Once);
    }

    [Fact]
    public async Task HandleMessageCommandAsync_CustomUrgency_PassesThrough()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.PerceptionQueueDepth).Returns(1);

        PerceptionData? capturedPerception = null;
        mockRunner.Setup(r => r.InjectPerception(It.IsAny<PerceptionData>()))
            .Callback<PerceptionData>(p => capturedPerception = p)
            .Returns(true);

        var outRunner = mockRunner.Object;
        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out outRunner))
            .Returns(true);

        var command = new SendMessageCommand
        {
            ActorId = "actor-1",
            MessageType = "high-priority-message",
            Urgency = 0.8f
        };

        // Act
        await worker.HandleMessageCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedPerception);
        Assert.Equal(0.8f, capturedPerception.Urgency);
    }

    [Fact]
    public async Task HandleMessageCommandAsync_NoUrgency_DefaultsToHalf()
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.PerceptionQueueDepth).Returns(1);

        PerceptionData? capturedPerception = null;
        mockRunner.Setup(r => r.InjectPerception(It.IsAny<PerceptionData>()))
            .Callback<PerceptionData>(p => capturedPerception = p)
            .Returns(true);

        var outRunner = mockRunner.Object;
        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out outRunner))
            .Returns(true);

        var command = new SendMessageCommand
        {
            ActorId = "actor-1",
            MessageType = "normal-message"
            // Note: Urgency not set, should default to 0.5
        };

        // Act
        await worker.HandleMessageCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedPerception);
        Assert.Equal(0.5f, capturedPerception.Urgency);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    public async Task HandleMessageCommandAsync_BoundaryUrgency_WorksCorrectly(float urgency)
    {
        // Arrange
        var worker = CreateWorker();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.PerceptionQueueDepth).Returns(1);

        PerceptionData? capturedPerception = null;
        mockRunner.Setup(r => r.InjectPerception(It.IsAny<PerceptionData>()))
            .Callback<PerceptionData>(p => capturedPerception = p)
            .Returns(true);

        var outRunner = mockRunner.Object;
        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out outRunner))
            .Returns(true);

        var command = new SendMessageCommand
        {
            ActorId = "actor-1",
            MessageType = "boundary-test",
            Urgency = urgency
        };

        // Act
        await worker.HandleMessageCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedPerception);
        Assert.Equal(urgency, capturedPerception.Urgency);
    }

    #endregion

    #region HandleBindCharacterCommandAsync Tests

    [Fact]
    public async Task HandleBindCharacterCommandAsync_ActorNotFound_ReturnsFalse()
    {
        // Arrange
        var worker = CreateWorker();

        _actorRegistryMock
            .Setup(r => r.TryGet("actor-unknown", out It.Ref<IActorRunner?>.IsAny))
            .Returns(false);

        var command = new BindActorCharacterCommand
        {
            ActorId = "actor-unknown",
            CharacterId = Guid.NewGuid()
        };

        // Act
        var result = await worker.HandleBindCharacterCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HandleBindCharacterCommandAsync_BindsActorToCharacter()
    {
        // Arrange
        var worker = CreateWorker();
        var characterId = Guid.NewGuid();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");

        var outRunner = mockRunner.Object;
        _actorRegistryMock
            .Setup(r => r.TryGet("actor-1", out outRunner))
            .Returns(true);

        var command = new BindActorCharacterCommand
        {
            ActorId = "actor-1",
            CharacterId = characterId
        };

        // Act
        var result = await worker.HandleBindCharacterCommandAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockRunner.Verify(
            r => r.BindCharacterAsync(characterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
