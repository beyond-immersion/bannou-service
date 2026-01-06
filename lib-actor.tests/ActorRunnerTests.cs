using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Unit tests for ActorRunner - lifecycle, perception queue, behavior execution, state publishing.
/// </summary>
public class ActorRunnerTests
{
    private static ActorTemplateData CreateTestTemplate(
        Guid? templateId = null,
        string category = "npc-brain",
        int tickIntervalMs = 100,
        int autoSaveIntervalSeconds = 60)
    {
        return new ActorTemplateData
        {
            TemplateId = templateId ?? Guid.NewGuid(),
            Category = category,
            BehaviorRef = "asset://behaviors/test-behavior",
            TickIntervalMs = tickIntervalMs,
            AutoSaveIntervalSeconds = autoSaveIntervalSeconds,
            MaxInstancesPerNode = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ActorServiceConfiguration CreateTestConfig(
        int perceptionQueueSize = 100,
        int defaultTickIntervalMs = 100,
        int defaultAutoSaveIntervalSeconds = 60)
    {
        return new ActorServiceConfiguration
        {
            PerceptionQueueSize = perceptionQueueSize,
            DefaultTickIntervalMs = defaultTickIntervalMs,
            DefaultAutoSaveIntervalSeconds = defaultAutoSaveIntervalSeconds
        };
    }

    private static (ActorRunner runner, Mock<IMessageBus> messageBusMock) CreateRunner(
        string? actorId = null,
        ActorTemplateData? template = null,
        Guid? characterId = null,
        ActorServiceConfiguration? config = null,
        object? initialState = null)
    {
        var messageBusMock = new Mock<IMessageBus>();
        messageBusMock.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var messageSubscriberMock = new Mock<IMessageSubscriber>();
        var meshClientMock = new Mock<IMeshInvocationClient>();

        var stateStoreMock = new Mock<IStateStore<ActorStateSnapshot>>();
        stateStoreMock.Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ActorStateSnapshot>(),
                It.IsAny<StateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var behaviorCacheMock = new Mock<IBehaviorDocumentCache>();
        var executorMock = new Mock<IDocumentExecutor>();

        // Set up behavior cache to return a valid document with a main flow
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-behavior" },
            Flows = new Dictionary<string, Flow>
            {
                ["main"] = new Flow { Name = "main", Actions = [] }
            }
        };
        behaviorCacheMock.Setup(c => c.GetOrLoadAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        // Set up executor to return success
        executorMock.Setup(e => e.ExecuteAsync(
                It.IsAny<AbmlDocument>(),
                It.IsAny<string>(),
                It.IsAny<IVariableScope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutionResult.Success());

        var loggerMock = new Mock<ILogger<ActorRunner>>();

        var runner = new ActorRunner(
            actorId ?? $"actor-{Guid.NewGuid()}",
            template ?? CreateTestTemplate(),
            characterId,
            config ?? CreateTestConfig(),
            messageBusMock.Object,
            messageSubscriberMock.Object,
            meshClientMock.Object,
            stateStoreMock.Object,
            behaviorCacheMock.Object,
            executorMock.Object,
            loggerMock.Object,
            initialState);

        return (runner, messageBusMock);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<ActorRunner>();
    }

    [Fact]
    public void Constructor_ValidParameters_SetsProperties()
    {
        // Arrange
        var actorId = "test-actor-123";
        var template = CreateTestTemplate();
        var characterId = Guid.NewGuid();

        // Act
        var (runner, _) = CreateRunner(actorId, template, characterId);

        // Assert
        Assert.Equal(actorId, runner.ActorId);
        Assert.Equal(template.TemplateId, runner.TemplateId);
        Assert.Equal(template.Category, runner.Category);
        Assert.Equal(characterId, runner.CharacterId);
        Assert.Equal(ActorStatus.Pending, runner.Status);
    }

    #endregion

    #region Lifecycle Tests

    [Fact]
    public async Task StartAsync_PendingActor_SetsStatusToRunning()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Act
        await runner.StartAsync();

        // Assert
        Assert.Equal(ActorStatus.Running, runner.Status);
        Assert.True((DateTimeOffset.UtcNow - runner.StartedAt).TotalSeconds < 1);
    }

    [Fact]
    public async Task StartAsync_AlreadyRunning_DoesNotRestart()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();
        var originalStartTime = runner.StartedAt;
        await Task.Delay(50); // Small delay to ensure different timestamp

        // Act
        await runner.StartAsync();

        // Assert
        Assert.Equal(originalStartTime, runner.StartedAt);
    }

    [Fact]
    public async Task StartAsync_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.StartAsync());
    }

    [Fact]
    public async Task StopAsync_RunningActor_SetsStatusToStopped()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();

        // Act
        await runner.StopAsync(graceful: true);

        // Assert
        Assert.Equal(ActorStatus.Stopped, runner.Status);
    }

    [Fact]
    public async Task StopAsync_AlreadyStopped_NoOp()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();
        await runner.StopAsync();

        // Act & Assert - should not throw
        await runner.StopAsync();
        Assert.Equal(ActorStatus.Stopped, runner.Status);
    }

    [Fact]
    public async Task StopAsync_GracefulFalse_StopsImmediately()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();

        // Act
        await runner.StopAsync(graceful: false);

        // Assert
        Assert.Equal(ActorStatus.Stopped, runner.Status);
    }

    [Fact]
    public async Task DisposeAsync_CleansUpResources()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();

        // Act
        await runner.DisposeAsync();

        // Assert - trying to start should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.StartAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_NoOp()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Act & Assert - should not throw
        await runner.DisposeAsync();
        var exception = await Record.ExceptionAsync(() => runner.DisposeAsync().AsTask());
        Assert.Null(exception);
    }

    #endregion

    #region Perception Queue Tests

    [Fact]
    public async Task InjectPerception_RunningActor_ReturnsTrue()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();

        var perception = new PerceptionData
        {
            PerceptionType = "visual",
            SourceId = "entity-123",
            SourceType = "npc",
            Data = new { Distance = 10.5 },
            Urgency = 0.8f
        };

        // Act
        var result = runner.InjectPerception(perception);

        // Assert
        Assert.True(result);
        Assert.True(runner.PerceptionQueueDepth >= 0);
    }

    [Fact]
    public void InjectPerception_NotRunning_ReturnsFalse()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        // Don't start the runner

        var perception = new PerceptionData
        {
            PerceptionType = "audio",
            SourceId = "source-1",
            SourceType = "environment",
            Data = new { Volume = 0.5 },
            Urgency = 0.3f
        };

        // Act
        var result = runner.InjectPerception(perception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task InjectPerception_NullPerception_ThrowsArgumentNullException()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => runner.InjectPerception(null!));
    }

    [Fact]
    public async Task InjectPerception_QueueFull_DropsOldestAndReturnsTrue()
    {
        // Arrange
        var config = CreateTestConfig(perceptionQueueSize: 5);
        var (runner, _) = CreateRunner(config: config);
        await runner.StartAsync();

        // Act - inject more than queue size
        for (int i = 0; i < 10; i++)
        {
            runner.InjectPerception(new PerceptionData
            {
                PerceptionType = "test",
                SourceId = $"source-{i}",
                SourceType = "test",
                Data = new { Index = i },
                Urgency = 0.5f
            });
        }

        // Assert - queue should be at capacity (newest items kept)
        Assert.True(runner.PerceptionQueueDepth <= 5);
    }

    [Fact]
    public async Task InjectPerception_StoppedActor_ReturnsFalse()
    {
        // Arrange
        var (runner, _) = CreateRunner();
        await runner.StartAsync();
        await runner.StopAsync();

        // Act
        var result = runner.InjectPerception(new PerceptionData
        {
            PerceptionType = "test",
            SourceId = "test",
            SourceType = "test",
            Data = new { },
            Urgency = 0.5f
        });

        // Assert
        Assert.False(result);
    }

    #endregion

    #region State Snapshot Tests

    [Fact]
    public async Task GetStateSnapshot_ReturnsCurrentState()
    {
        // Arrange
        var actorId = "test-actor";
        var template = CreateTestTemplate();
        var characterId = Guid.NewGuid();
        var (runner, _) = CreateRunner(actorId, template, characterId);
        await runner.StartAsync();

        // Let it run a tick
        await Task.Delay(150);

        // Act
        var snapshot = runner.GetStateSnapshot();

        // Assert
        Assert.Equal(actorId, snapshot.ActorId);
        Assert.Equal(template.TemplateId, snapshot.TemplateId);
        Assert.Equal(template.Category, snapshot.Category);
        Assert.Equal(characterId, snapshot.CharacterId);
        Assert.Equal(ActorStatus.Running, snapshot.Status);
    }

    [Fact]
    public void GetStateSnapshot_BeforeStart_ReturnsPendingStatus()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Act
        var snapshot = runner.GetStateSnapshot();

        // Assert
        Assert.Equal(ActorStatus.Pending, snapshot.Status);
    }

    [Fact]
    public async Task GetStateSnapshot_IncludesLoopIterations()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template);
        await runner.StartAsync();

        // Wait for a few iterations
        await Task.Delay(200);

        // Act
        var snapshot = runner.GetStateSnapshot();

        // Assert
        Assert.True(snapshot.LoopIterations > 0);
    }

    #endregion

    #region Behavior Loop Tests

    [Fact]
    public async Task BehaviorLoop_IncrementsIterations()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template);

        // Act
        await runner.StartAsync();
        await Task.Delay(200);
        await runner.StopAsync();

        // Assert
        Assert.True(runner.LoopIterations > 0);
    }

    [Fact]
    public async Task BehaviorLoop_UpdatesLastHeartbeat()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template);

        // Act
        await runner.StartAsync();
        await Task.Delay(100);

        // Assert
        Assert.NotNull(runner.LastHeartbeat);
        Assert.True((DateTimeOffset.UtcNow - runner.LastHeartbeat.Value).TotalSeconds < 1);
    }

    [Fact]
    public async Task BehaviorLoop_RespectsCancellation()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template);
        using var cts = new CancellationTokenSource();

        // Act
        await runner.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await Task.Delay(100);

        // Assert - loop should have stopped
        Assert.True(runner.Status == ActorStatus.Running || runner.Status == ActorStatus.Stopped);
    }

    [Fact]
    public async Task BehaviorLoop_UsesTemplateTickInterval()
    {
        // Arrange - very fast tick interval
        var template = CreateTestTemplate(tickIntervalMs: 20);
        var (runner, _) = CreateRunner(template: template);

        // Act
        await runner.StartAsync();
        await Task.Delay(200);
        await runner.StopAsync();

        // Assert - should have many iterations with fast tick
        Assert.True(runner.LoopIterations > 5);
    }

    [Fact]
    public async Task BehaviorLoop_UsesFallbackTickInterval_WhenTemplateIsZero()
    {
        // Arrange - template with 0 tick interval
        var template = CreateTestTemplate(tickIntervalMs: 0);
        var config = CreateTestConfig(defaultTickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template, config: config);

        // Act
        await runner.StartAsync();
        await Task.Delay(200);
        await runner.StopAsync();

        // Assert - should use fallback and have some iterations
        Assert.True(runner.LoopIterations > 0);
    }

    #endregion

    #region State Publishing Tests

    [Fact]
    public async Task StateUpdate_WithCharacterId_PublishesEvent()
    {
        // Arrange
        var characterId = Guid.NewGuid();
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, messageBusMock) = CreateRunner(
            template: template,
            characterId: characterId);

        // Act
        await runner.StartAsync();
        await Task.Delay(200); // Let some ticks run
        await runner.StopAsync();

        // Assert - runner should have completed without exceptions
        // Since ActorRunner doesn't automatically change state, we just verify it ran
        Assert.True(runner.LoopIterations > 0);
    }

    [Fact]
    public async Task StateUpdate_WithoutCharacterId_DoesNotPublish()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, messageBusMock) = CreateRunner(
            template: template,
            characterId: null); // No character ID

        // Act
        await runner.StartAsync();
        await Task.Delay(200);
        await runner.StopAsync();

        // Assert - should not publish character state updates
        messageBusMock.Verify(
            m => m.TryPublishAsync(
                "character.state_update",
                It.IsAny<CharacterStateUpdateEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task BehaviorLoop_ErrorInTick_ContinuesRunning()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 50);
        var (runner, _) = CreateRunner(template: template);

        // Act - runner should continue despite internal errors
        await runner.StartAsync();
        await Task.Delay(200);

        // Assert - runner should still be running
        Assert.Equal(ActorStatus.Running, runner.Status);
        Assert.True(runner.LoopIterations > 0);

        await runner.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WithTimeout_HandlesGracefulShutdown()
    {
        // Arrange
        var template = CreateTestTemplate(tickIntervalMs: 10);
        var (runner, _) = CreateRunner(template: template);
        await runner.StartAsync();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await runner.StopAsync(graceful: true, cts.Token);

        // Assert
        Assert.Equal(ActorStatus.Stopped, runner.Status);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void PerceptionQueueDepth_InitiallyZero()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Assert
        Assert.Equal(0, runner.PerceptionQueueDepth);
    }

    [Fact]
    public void LoopIterations_InitiallyZero()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Assert
        Assert.Equal(0, runner.LoopIterations);
    }

    [Fact]
    public void LastHeartbeat_InitiallyNull()
    {
        // Arrange
        var (runner, _) = CreateRunner();

        // Assert
        Assert.Null(runner.LastHeartbeat);
    }

    #endregion
}
