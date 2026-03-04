using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Linq;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Additional tests for ActorService covering UpdateActorTemplateAsync,
/// DeleteActorTemplateAsync, CleanupByCharacterAsync, QueryOptionsAsync,
/// and BindActorCharacterAsync edge cases.
/// </summary>
public class ActorServiceAdditionalTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<ActorService>> _mockLogger;
    private readonly ActorServiceConfiguration _configuration;
    private readonly Mock<IActorRegistry> _mockActorRegistry;
    private readonly Mock<IActorRunnerFactory> _mockActorRunnerFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IBehaviorDocumentLoader> _mockBehaviorLoader;
    private readonly Mock<IActorPoolManager> _mockPoolManager;
    private readonly Mock<IMeshInvocationClient> _mockMeshClient;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IStateStore<ActorTemplateData>> _mockTemplateStore;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public ActorServiceAdditionalTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<ActorService>>();
        _configuration = new ActorServiceConfiguration
        {
            DefaultTickIntervalMs = 100,
            DefaultAutoSaveIntervalSeconds = 60,
            DefaultActorsPerNode = 100,
            DeploymentMode = ActorDeploymentMode.Bannou
        };
        _mockActorRegistry = new Mock<IActorRegistry>();
        _mockActorRunnerFactory = new Mock<IActorRunnerFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockBehaviorLoader = new Mock<IBehaviorDocumentLoader>();
        _mockPoolManager = new Mock<IActorPoolManager>();
        _mockMeshClient = new Mock<IMeshInvocationClient>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockTemplateStore = new Mock<IStateStore<ActorTemplateData>>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory
            .Setup(f => f.GetStore<ActorTemplateData>(It.IsAny<string>()))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<string>>(It.IsAny<string>()))
            .Returns(_mockIndexStore.Object);
    }

    private ActorService CreateService()
    {
        return new ActorService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockActorRegistry.Object,
            _mockActorRunnerFactory.Object,
            _mockEventConsumer.Object,
            _mockBehaviorLoader.Object,
            _mockPoolManager.Object,
            _mockMeshClient.Object,
            _mockResourceClient.Object,
            _mockCharacterClient.Object,
            _mockTelemetryProvider.Object);
    }

    /// <summary>
    /// Creates a standard template data object for test setup.
    /// </summary>
    private static ActorTemplateData CreateTemplateData(Guid? templateId = null, string category = "test-category")
    {
        var id = templateId ?? Guid.NewGuid();
        return new ActorTemplateData
        {
            TemplateId = id,
            Category = category,
            BehaviorRef = "behaviors/test.abml",
            TickIntervalMs = 100,
            AutoSaveIntervalSeconds = 60,
            MaxInstancesPerNode = 100,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    /// <summary>
    /// Creates a mock actor runner with common properties set up.
    /// </summary>
    private static Mock<IActorRunner> CreateMockRunner(
        string actorId,
        Guid templateId,
        Guid? characterId = null,
        ActorStatus status = ActorStatus.Running)
    {
        var mock = new Mock<IActorRunner>();
        mock.SetupGet(r => r.ActorId).Returns(actorId);
        mock.SetupGet(r => r.TemplateId).Returns(templateId);
        mock.SetupGet(r => r.CharacterId).Returns(characterId);
        mock.SetupGet(r => r.Category).Returns("test-category");
        mock.SetupGet(r => r.Status).Returns(status);
        mock.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);
        mock.SetupGet(r => r.RealmId).Returns(Guid.NewGuid());
        mock.SetupGet(r => r.LoopIterations).Returns(42);
        return mock;
    }

    #region UpdateActorTemplateAsync Tests

    [Fact]
    public async Task UpdateActorTemplateAsync_ValidUpdate_ReturnsOkWithUpdatedFields()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);
        var originalBehaviorRef = existing.BehaviorRef;

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/updated.abml"
        };

        // Act
        var (status, response) = await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("behaviors/updated.abml", response.BehaviorRef);
        Assert.Equal(templateId, response.TemplateId);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_MultipleFieldChanges_TracksAllChangedFields()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Capture the published event to verify changedFields
        ActorTemplateUpdatedEvent? capturedEvent = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                "actor.template.updated",
                It.IsAny<ActorTemplateUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ActorTemplateUpdatedEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/new.abml",
            TickIntervalMs = 500,
            AutoSaveIntervalSeconds = 120
        };

        // Act
        await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert - verify all changed fields tracked in event
        Assert.NotNull(capturedEvent);
        Assert.Contains("behaviorRef", capturedEvent.ChangedFields);
        Assert.Contains("tickIntervalMs", capturedEvent.ChangedFields);
        Assert.Contains("autoSaveIntervalSeconds", capturedEvent.ChangedFields);
        Assert.Equal(templateId, capturedEvent.TemplateId);
        Assert.Equal("test-category", capturedEvent.Category);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_ConcurrentModification_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));

        // Simulate concurrent modification by returning null from TrySaveAsync
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/new.abml"
        };

        // Act
        var (status, response) = await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((ActorTemplateData?)null, (string?)null));

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/new.abml"
        };

        // Act
        var (status, response) = await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_NoChanges_StillSavesWithUpdatedTimestamp()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Request with same behaviorRef as existing (no actual changes)
        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = existing.BehaviorRef
        };

        // Act
        var (status, response) = await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert - returns OK (timestamp always updated)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_PublishesEventWithCorrectData()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Capture the event
        string? capturedTopic = null;
        ActorTemplateUpdatedEvent? capturedEvent = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                It.Is<string>(t => t == "actor.template.updated"),
                It.IsAny<ActorTemplateUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ActorTemplateUpdatedEvent, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/changed.abml"
        };

        // Act
        await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("actor.template.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        Assert.Equal(templateId, capturedEvent.TemplateId);
        Assert.Equal(existing.Category, capturedEvent.Category);
        Assert.Equal("behaviors/changed.abml", capturedEvent.BehaviorRef);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
        Assert.True(capturedEvent.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_CognitionTemplateIdChange_TrackedInChangedFields()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);
        existing.CognitionTemplateId = "old-template";

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        ActorTemplateUpdatedEvent? capturedEvent = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                "actor.template.updated",
                It.IsAny<ActorTemplateUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ActorTemplateUpdatedEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            CognitionTemplateId = "new-template"
        };

        // Act
        await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Contains("cognitionTemplateId", capturedEvent.ChangedFields);
    }

    [Fact]
    public async Task UpdateActorTemplateAsync_SavesCategoryIndex()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId, "my-category");

        _mockTemplateStore.Setup(s => s.GetWithETagAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existing, "etag-1"));
        _mockTemplateStore.Setup(s => s.TrySaveAsync(
                templateId.ToString(), It.IsAny<ActorTemplateData>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new UpdateActorTemplateRequest
        {
            TemplateId = templateId,
            BehaviorRef = "behaviors/updated.abml"
        };

        // Act
        await service.UpdateActorTemplateAsync(request, CancellationToken.None);

        // Assert - verify category index was saved
        _mockTemplateStore.Verify(s => s.SaveAsync(
            "category:my-category",
            It.IsAny<ActorTemplateData>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteActorTemplateAsync Tests

    [Fact]
    public async Task DeleteActorTemplateAsync_ValidDeletion_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Setup index store for UpdateTemplateIndexAsync
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = false
        };

        // Act
        var (status, response) = await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.StoppedActorCount);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorTemplateData?)null);

        var request = new DeleteActorTemplateRequest { TemplateId = templateId };

        // Act
        var (status, response) = await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_ForceStopTrue_StopsRunningActors()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Two running actors using this template
        var runner1 = CreateMockRunner("actor-1", templateId);
        runner1.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner1.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var runner2 = CreateMockRunner("actor-2", templateId);
        runner2.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner2.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _mockActorRegistry.Setup(r => r.GetByTemplateId(templateId))
            .Returns(new[] { runner1.Object, runner2.Object });

        // Setup index store for UpdateTemplateIndexAsync
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = true
        };

        // Act
        var (status, response) = await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.StoppedActorCount);

        // Verify stop and dispose were called
        runner1.Verify(r => r.StopAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        runner1.Verify(r => r.DisposeAsync(), Times.Once);
        runner2.Verify(r => r.StopAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        runner2.Verify(r => r.DisposeAsync(), Times.Once);

        // Verify actors removed from registry
        _mockActorRegistry.Verify(r => r.TryRemove("actor-1", out It.Ref<IActorRunner?>.IsAny), Times.Once);
        _mockActorRegistry.Verify(r => r.TryRemove("actor-2", out It.Ref<IActorRunner?>.IsAny), Times.Once);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_ForceStopFalse_DoesNotStopActors()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Setup index store
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = false
        };

        // Act
        var (status, response) = await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.StoppedActorCount);

        // Verify GetByTemplateId was never called (forceStop is false)
        _mockActorRegistry.Verify(r => r.GetByTemplateId(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_ActorStopError_ContinuesAndCountsSuccessful()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // First actor succeeds, second throws
        var runner1 = CreateMockRunner("actor-1", templateId);
        runner1.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner1.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var runner2 = CreateMockRunner("actor-2", templateId);
        runner2.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Actor in bad state"));

        _mockActorRegistry.Setup(r => r.GetByTemplateId(templateId))
            .Returns(new[] { runner1.Object, runner2.Object });

        // Setup index store
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = true
        };

        // Act
        var (status, response) = await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert - only the successful stop is counted
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.StoppedActorCount);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_PublishesDeletedEvent()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId, "my-category");

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Setup index store
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        // Capture the published event
        string? capturedTopic = null;
        ActorTemplateDeletedEvent? capturedEvent = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                It.Is<string>(t => t == "actor.template.deleted"),
                It.IsAny<ActorTemplateDeletedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ActorTemplateDeletedEvent, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = false
        };

        // Act
        await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("actor.template.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
        Assert.Equal(templateId, capturedEvent.TemplateId);
        Assert.Equal("my-category", capturedEvent.Category);
        Assert.Equal(existing.BehaviorRef, capturedEvent.BehaviorRef);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_ForceStopTrue_EventIncludesStoppedCount()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId);

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var runner = CreateMockRunner("actor-1", templateId);
        runner.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockActorRegistry.Setup(r => r.GetByTemplateId(templateId))
            .Returns(new[] { runner.Object });

        // Setup index store
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        ActorTemplateDeletedEvent? capturedEvent = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                It.Is<string>(t => t == "actor.template.deleted"),
                It.IsAny<ActorTemplateDeletedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ActorTemplateDeletedEvent, CancellationToken>((_, evt, _) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = true
        };

        // Act
        await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert - event includes deleted reason with stopped count
        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent.DeletedReason);
        Assert.Contains("1 actors stopped", capturedEvent.DeletedReason);
    }

    [Fact]
    public async Task DeleteActorTemplateAsync_DeletesBothKeyAndCategoryEntry()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var existing = CreateTemplateData(templateId, "my-category");

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Setup index store
        _mockIndexStore.Setup(s => s.GetWithETagAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { templateId.ToString() }, "idx-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("_all_template_ids", It.IsAny<List<string>>(), "idx-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync("idx-etag-2");

        var request = new DeleteActorTemplateRequest
        {
            TemplateId = templateId,
            ForceStopActors = false
        };

        // Act
        await service.DeleteActorTemplateAsync(request, CancellationToken.None);

        // Assert - both ID-based and category-based entries deleted
        _mockTemplateStore.Verify(s => s.DeleteAsync(templateId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        _mockTemplateStore.Verify(s => s.DeleteAsync("category:my-category", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CleanupByCharacterAsync Tests

    [Fact]
    public async Task CleanupByCharacterAsync_BannouMode_StopsLocalActorsForCharacter()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var runner1 = CreateMockRunner("actor-1", Guid.NewGuid(), characterId);
        runner1.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner1.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var runner2 = CreateMockRunner("actor-2", Guid.NewGuid(), characterId);
        runner2.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner2.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var unrelatedRunner = CreateMockRunner("actor-3", Guid.NewGuid(), Guid.NewGuid());

        _mockActorRegistry.Setup(r => r.GetAllRunners())
            .Returns(new[] { runner1.Object, runner2.Object, unrelatedRunner.Object });

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.ActorsCleanedUp);
        Assert.Contains("actor-1", response.ActorIds);
        Assert.Contains("actor-2", response.ActorIds);
        Assert.DoesNotContain("actor-3", response.ActorIds);
    }

    [Fact]
    public async Task CleanupByCharacterAsync_PoolMode_SendsStopCommandsAndRemovesAssignments()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.PoolPerType;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var assignments = new List<ActorAssignment>
        {
            new ActorAssignment
            {
                ActorId = "actor-1",
                NodeId = "node-1",
                NodeAppId = "app-1",
                TemplateId = Guid.NewGuid(),
                Category = "npc",
                CharacterId = characterId,
                Status = ActorStatus.Running,
                AssignedAt = DateTimeOffset.UtcNow
            },
            new ActorAssignment
            {
                ActorId = "actor-2",
                NodeId = "node-2",
                NodeAppId = "app-2",
                TemplateId = Guid.NewGuid(),
                Category = "npc",
                CharacterId = characterId,
                Status = ActorStatus.Running,
                AssignedAt = DateTimeOffset.UtcNow
            }
        };

        _mockPoolManager.Setup(p => p.GetActorAssignmentsByCharacterAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.ActorsCleanedUp);

        // Verify stop commands were published
        _mockMessageBus.Verify(mb => mb.TryPublishAsync(
            "actor.node.app-1.stop",
            It.IsAny<StopActorCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockMessageBus.Verify(mb => mb.TryPublishAsync(
            "actor.node.app-2.stop",
            It.IsAny<StopActorCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify assignments were removed
        _mockPoolManager.Verify(p => p.RemoveActorAssignmentAsync("actor-1", It.IsAny<CancellationToken>()), Times.Once);
        _mockPoolManager.Verify(p => p.RemoveActorAssignmentAsync("actor-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupByCharacterAsync_BannouMode_StopError_ContinuesWithOtherActors()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // First runner throws on stop, second succeeds
        var runner1 = CreateMockRunner("actor-1", Guid.NewGuid(), characterId);
        runner1.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Stop timed out"));

        var runner2 = CreateMockRunner("actor-2", Guid.NewGuid(), characterId);
        runner2.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runner2.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _mockActorRegistry.Setup(r => r.GetAllRunners())
            .Returns(new[] { runner1.Object, runner2.Object });

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert - only successful cleanup counted
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ActorsCleanedUp);
        Assert.Contains("actor-2", response.ActorIds);
        Assert.DoesNotContain("actor-1", response.ActorIds);
    }

    [Fact]
    public async Task CleanupByCharacterAsync_NoActorsForCharacter_ReturnsZeroCount()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockActorRegistry.Setup(r => r.GetAllRunners())
            .Returns(Array.Empty<IActorRunner>());

        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.ActorsCleanedUp);
        Assert.Empty(response.ActorIds);
    }

    #endregion

    #region QueryOptionsAsync Tests

    [Fact]
    public async Task QueryOptionsAsync_CachedFreshness_ReturnsOptionsFromMemory()
    {
        // Arrange
        var service = CreateService();
        var actorId = "npc-brain-1";
        var templateId = Guid.NewGuid();

        var computedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var options = new List<ActorOption>
        {
            new ActorOption { ActionId = "attack", Preference = 0.8f, Available = true },
            new ActorOption { ActionId = "defend", Preference = 0.6f, Available = true }
        };

        var mockRunner = CreateMockRunner(actorId, templateId);
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            Category = "npc-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Memories = new List<MemoryEntry>
            {
                new MemoryEntry
                {
                    MemoryKey = "combat_options",
                    MemoryValue = options,
                    CreatedAt = computedAt
                }
            },
            Feelings = new Dictionary<string, double>()
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out runner)).Returns(true);

        var request = new QueryOptionsRequest
        {
            ActorId = actorId,
            QueryType = OptionsQueryType.Combat,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(actorId, response.ActorId);
        Assert.Equal(OptionsQueryType.Combat, response.QueryType);
        Assert.Equal(2, response.Options.Count);
        var optionsList = response.Options.ToList();
        Assert.Equal("attack", optionsList[0].ActionId);
        Assert.Equal("defend", optionsList[1].ActionId);
    }

    [Fact]
    public async Task QueryOptionsAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        var request = new QueryOptionsRequest
        {
            ActorId = "nonexistent",
            QueryType = OptionsQueryType.Combat,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryOptionsAsync_PoolMode_RemoteActor_ReturnsBadRequest()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.PoolPerType;
        var service = CreateService();

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("remote-actor", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("remote-actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "remote-actor",
                NodeId = "remote-node",
                NodeAppId = "remote-app",
                TemplateId = Guid.NewGuid(),
                Category = "npc",
                Status = ActorStatus.Running,
                AssignedAt = DateTimeOffset.UtcNow
            });

        var request = new QueryOptionsRequest
        {
            ActorId = "remote-actor",
            QueryType = OptionsQueryType.Combat,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert - QueryOptions requires local actor, remote returns BadRequest
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryOptionsAsync_CharacterActor_BuildsCharacterContext()
    {
        // Arrange
        var service = CreateService();
        var actorId = "npc-brain-1";
        var templateId = Guid.NewGuid();
        var characterId = Guid.NewGuid();

        var mockRunner = CreateMockRunner(actorId, templateId, characterId);
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            CharacterId = characterId,
            Category = "npc-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Memories = new List<MemoryEntry>
            {
                new MemoryEntry { MemoryKey = "combat_options", MemoryValue = new List<ActorOption>(), CreatedAt = DateTimeOffset.UtcNow },
                new MemoryEntry { MemoryKey = "combat_style", MemoryValue = "aggressive", CreatedAt = DateTimeOffset.UtcNow },
                new MemoryEntry { MemoryKey = "risk_tolerance", MemoryValue = 0.7f, CreatedAt = DateTimeOffset.UtcNow },
                new MemoryEntry { MemoryKey = "protect_allies", MemoryValue = true, CreatedAt = DateTimeOffset.UtcNow }
            },
            Goals = new GoalStateData { PrimaryGoal = "explore" },
            Feelings = new Dictionary<string, double> { { "anger", 0.8 }, { "fear", 0.3 } }
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out runner)).Returns(true);

        var request = new QueryOptionsRequest
        {
            ActorId = actorId,
            QueryType = OptionsQueryType.Combat,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert - character context populated
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.CharacterContext);
        Assert.Equal("aggressive", response.CharacterContext.CombatStyle);
        Assert.Equal(0.7f, response.CharacterContext.RiskTolerance);
        Assert.Equal(true, response.CharacterContext.ProtectAllies);
        Assert.Equal("explore", response.CharacterContext.CurrentGoal);
        Assert.Equal("anger", response.CharacterContext.EmotionalState);
    }

    [Fact]
    public async Task QueryOptionsAsync_NoCharacter_CharacterContextIsNull()
    {
        // Arrange
        var service = CreateService();
        var actorId = "event-brain-1";
        var templateId = Guid.NewGuid();

        var mockRunner = CreateMockRunner(actorId, templateId, characterId: null);
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            CharacterId = null,
            Category = "event-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Memories = new List<MemoryEntry>(),
            Feelings = new Dictionary<string, double>()
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out runner)).Returns(true);

        var request = new QueryOptionsRequest
        {
            ActorId = actorId,
            QueryType = OptionsQueryType.Dialogue,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.CharacterContext);
    }

    [Fact]
    public async Task QueryOptionsAsync_MemoryEntryConversion_DictToActorOption()
    {
        // Arrange
        var service = CreateService();
        var actorId = "npc-brain-1";
        var templateId = Guid.NewGuid();

        // Options stored as list of dictionaries (common pattern from ABML output)
        var dictOptions = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["actionId"] = "flee",
                ["preference"] = 0.9f,
                ["available"] = true,
                ["risk"] = 0.1f,
                ["cooldownMs"] = 500,
                ["tags"] = new List<string> { "defensive", "movement" }
            }
        };

        var mockRunner = CreateMockRunner(actorId, templateId);
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            Category = "npc-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Memories = new List<MemoryEntry>
            {
                new MemoryEntry
                {
                    MemoryKey = "combat_options",
                    MemoryValue = dictOptions,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            },
            Feelings = new Dictionary<string, double>()
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out runner)).Returns(true);

        var request = new QueryOptionsRequest
        {
            ActorId = actorId,
            QueryType = OptionsQueryType.Combat,
            Freshness = OptionsFreshness.Cached
        };

        // Act
        var (status, response) = await service.QueryOptionsAsync(request, CancellationToken.None);

        // Assert - dict was converted to ActorOption
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Options);
        var option = response.Options.First();
        Assert.Equal("flee", option.ActionId);
        Assert.Equal(0.9f, option.Preference);
        Assert.True(option.Available);
        Assert.Equal(0.1f, option.Risk);
        Assert.Equal(500, option.CooldownMs);
        Assert.NotNull(option.Tags);
        Assert.Contains("defensive", option.Tags);
    }

    #endregion

    #region BindActorCharacterAsync Tests

    [Fact]
    public async Task BindActorCharacterAsync_BannouMode_AlreadyBound_ReturnsBadRequest()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var existingCharacterId = Guid.NewGuid();
        var newCharacterId = Guid.NewGuid();

        var mockRunner = CreateMockRunner("actor-1", Guid.NewGuid(), existingCharacterId);
        mockRunner.Setup(r => r.BindCharacterAsync(newCharacterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Actor already bound to a character"));

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("actor-1", out runner)).Returns(true);

        var request = new BindActorCharacterRequest
        {
            ActorId = "actor-1",
            CharacterId = newCharacterId
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindActorCharacterAsync_BannouMode_RegistersCharacterReference()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var mockRunner = CreateMockRunner("actor-1", Guid.NewGuid());
        mockRunner.Setup(r => r.BindCharacterAsync(characterId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("actor-1", out runner)).Returns(true);

        var request = new BindActorCharacterRequest
        {
            ActorId = "actor-1",
            CharacterId = characterId
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify resource reference was registered
        _mockResourceClient.Verify(rc => rc.RegisterReferenceAsync(
            It.Is<RegisterReferenceRequest>(req =>
                req.ResourceType == "character" &&
                req.ResourceId == characterId &&
                req.SourceType == "actor" &&
                req.SourceId == "actor-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BindActorCharacterAsync_BannouMode_NotFound_ReturnsNotFound()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        var request = new BindActorCharacterRequest
        {
            ActorId = "nonexistent",
            CharacterId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindActorCharacterAsync_PoolMode_AlreadyBound_ReturnsBadRequest()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.PoolPerType;
        var service = CreateService();
        var existingCharacterId = Guid.NewGuid();
        var newCharacterId = Guid.NewGuid();

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "actor-1",
                NodeId = "node-1",
                NodeAppId = "app-1",
                TemplateId = Guid.NewGuid(),
                Category = "npc",
                CharacterId = existingCharacterId,
                Status = ActorStatus.Running,
                AssignedAt = DateTimeOffset.UtcNow
            });

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("actor-1", out runner)).Returns(false);

        var request = new BindActorCharacterRequest
        {
            ActorId = "actor-1",
            CharacterId = newCharacterId
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindActorCharacterAsync_PoolMode_SendsBindCommandAndRegistersReference()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.PoolPerType;
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "actor-1",
                NodeId = "node-1",
                NodeAppId = "app-1",
                TemplateId = Guid.NewGuid(),
                Category = "npc",
                CharacterId = null,
                Status = ActorStatus.Running,
                AssignedAt = DateTimeOffset.UtcNow,
                StartedAt = DateTimeOffset.UtcNow
            });

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("actor-1", out runner)).Returns(false);

        // Capture the bind command
        BindActorCharacterCommand? capturedCommand = null;
        _mockMessageBus.Setup(mb => mb.TryPublishAsync(
                It.Is<string>(t => t == "actor.node.app-1.bind-character"),
                It.IsAny<BindActorCharacterCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BindActorCharacterCommand, CancellationToken>((_, cmd, _) =>
            {
                capturedCommand = cmd;
            })
            .ReturnsAsync(true);

        var request = new BindActorCharacterRequest
        {
            ActorId = "actor-1",
            CharacterId = characterId
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify pool assignment was updated
        _mockPoolManager.Verify(p => p.UpdateActorCharacterAsync("actor-1", characterId, It.IsAny<CancellationToken>()), Times.Once);

        // Verify bind command was published
        Assert.NotNull(capturedCommand);
        Assert.Equal("actor-1", capturedCommand.ActorId);
        Assert.Equal(characterId, capturedCommand.CharacterId);

        // Verify resource reference registered
        _mockResourceClient.Verify(rc => rc.RegisterReferenceAsync(
            It.Is<RegisterReferenceRequest>(req =>
                req.ResourceType == "character" &&
                req.ResourceId == characterId &&
                req.SourceType == "actor" &&
                req.SourceId == "actor-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BindActorCharacterAsync_PoolMode_NotFound_ReturnsNotFound()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.PoolPerType;
        var service = CreateService();

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        var request = new BindActorCharacterRequest
        {
            ActorId = "nonexistent",
            CharacterId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindActorCharacterAsync_BannouMode_ReturnsCorrectResponseData()
    {
        // Arrange
        _configuration.DeploymentMode = ActorDeploymentMode.Bannou;
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.ActorId).Returns("actor-1");
        mockRunner.SetupGet(r => r.TemplateId).Returns(templateId);
        mockRunner.SetupGet(r => r.CharacterId).Returns(characterId);
        mockRunner.SetupGet(r => r.Category).Returns("npc-brain");
        mockRunner.SetupGet(r => r.RealmId).Returns(realmId);
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);
        mockRunner.SetupGet(r => r.LoopIterations).Returns(100);
        mockRunner.Setup(r => r.BindCharacterAsync(characterId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("actor-1", out runner)).Returns(true);

        var request = new BindActorCharacterRequest
        {
            ActorId = "actor-1",
            CharacterId = characterId
        };

        // Act
        var (status, response) = await service.BindActorCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("actor-1", response.ActorId);
        Assert.Equal(templateId, response.TemplateId);
        Assert.Equal("npc-brain", response.Category);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(ActorStatus.Running, response.Status);
        Assert.Equal(100, response.LoopIterations);
    }

    #endregion
}
