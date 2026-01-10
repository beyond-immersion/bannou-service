using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Actor.Tests;

/// <summary>
/// Tests for ActorService.
/// </summary>
public class ActorServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<ActorService>> _mockLogger;
    private readonly ActorServiceConfiguration _configuration;
    private readonly Mock<IActorRegistry> _mockActorRegistry;
    private readonly Mock<IActorRunnerFactory> _mockActorRunnerFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IBehaviorDocumentCache> _mockBehaviorCache;
    private readonly Mock<IActorPoolManager> _mockPoolManager;
    private readonly Mock<IPersonalityCache> _mockPersonalityCache;
    private readonly Mock<IMeshInvocationClient> _mockMeshClient;
    private readonly Mock<IStateStore<ActorTemplateData>> _mockTemplateStore;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;

    public ActorServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<ActorService>>();
        _configuration = new ActorServiceConfiguration
        {
            DefaultTickIntervalMs = 100,
            DefaultAutoSaveIntervalSeconds = 60,
            DefaultActorsPerNode = 100,
            DeploymentMode = "bannou"
        };
        _mockActorRegistry = new Mock<IActorRegistry>();
        _mockActorRunnerFactory = new Mock<IActorRunnerFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockBehaviorCache = new Mock<IBehaviorDocumentCache>();
        _mockPoolManager = new Mock<IActorPoolManager>();
        _mockPersonalityCache = new Mock<IPersonalityCache>();
        _mockMeshClient = new Mock<IMeshInvocationClient>();
        _mockTemplateStore = new Mock<IStateStore<ActorTemplateData>>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();

        // Setup default store factory returns
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
            _mockBehaviorCache.Object,
            _mockPoolManager.Object,
            _mockPersonalityCache.Object,
            _mockMeshClient.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void ActorService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ActorService>();

    #endregion

    #region CreateActorTemplateAsync Tests

    [Fact]
    public async Task CreateActorTemplateAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateActorTemplateRequest
        {
            Category = "npc-brain",
            BehaviorRef = "behaviors/npc/standard.abml"
        };

        _mockTemplateStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("category:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorTemplateData?)null);
        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.CreateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("npc-brain", response.Category);
        Assert.Equal("behaviors/npc/standard.abml", response.BehaviorRef);
    }

    [Fact]
    public async Task CreateActorTemplateAsync_EmptyCategory_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateActorTemplateRequest
        {
            Category = "",
            BehaviorRef = "behaviors/test.abml"
        };

        // Act
        var (status, response) = await service.CreateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateActorTemplateAsync_EmptyBehaviorRef_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateActorTemplateRequest
        {
            Category = "test-category",
            BehaviorRef = ""
        };

        // Act
        var (status, response) = await service.CreateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateActorTemplateAsync_DuplicateCategory_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateActorTemplateRequest
        {
            Category = "existing-category",
            BehaviorRef = "behaviors/test.abml"
        };

        var existingTemplate = new ActorTemplateData
        {
            TemplateId = Guid.NewGuid(),
            Category = "existing-category",
            BehaviorRef = "behaviors/existing.abml"
        };

        _mockTemplateStore.Setup(s => s.GetAsync("category:existing-category", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);

        // Act
        var (status, response) = await service.CreateActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateActorTemplateAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateActorTemplateRequest
        {
            Category = "test-category",
            BehaviorRef = "behaviors/test.abml"
        };

        _mockTemplateStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorTemplateData?)null);
        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await service.CreateActorTemplateAsync(request, CancellationToken.None);

        // Assert - verify the convenience overload was called (topic, event, cancellationToken)
        _mockMessageBus.Verify(
            mb => mb.TryPublishAsync(
                "actor-template.created",
                It.IsAny<ActorTemplateCreatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetActorTemplateAsync Tests

    [Fact]
    public async Task GetActorTemplateAsync_ById_ReturnsTemplate()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "test-category",
            BehaviorRef = "behaviors/test.abml"
        };

        var request = new GetActorTemplateRequest { TemplateId = templateId };

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.GetActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(templateId, response.TemplateId);
    }

    [Fact]
    public async Task GetActorTemplateAsync_ByCategory_ReturnsTemplate()
    {
        // Arrange
        var service = CreateService();
        var template = new ActorTemplateData
        {
            TemplateId = Guid.NewGuid(),
            Category = "test-category",
            BehaviorRef = "behaviors/test.abml"
        };

        var request = new GetActorTemplateRequest { Category = "test-category" };

        _mockTemplateStore.Setup(s => s.GetAsync("category:test-category", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.GetActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("test-category", response.Category);
    }

    [Fact]
    public async Task GetActorTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetActorTemplateRequest { TemplateId = Guid.NewGuid() };

        _mockTemplateStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorTemplateData?)null);

        // Act
        var (status, response) = await service.GetActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetActorTemplateAsync_NoIdOrCategory_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new GetActorTemplateRequest();

        // Act
        var (status, response) = await service.GetActorTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region ListActorTemplatesAsync Tests

    [Fact]
    public async Task ListActorTemplatesAsync_EmptyList_ReturnsEmptyResponse()
    {
        // Arrange
        var service = CreateService();
        var request = new ListActorTemplatesRequest { Limit = 10, Offset = 0 };

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.ListActorTemplatesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Templates);
        Assert.Equal(0, response.Total);
    }

    [Fact]
    public async Task ListActorTemplatesAsync_WithTemplates_ReturnsPaginated()
    {
        // Arrange
        var service = CreateService();
        var request = new ListActorTemplatesRequest { Limit = 10, Offset = 0 };

        var templateIds = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        var templates = new Dictionary<string, ActorTemplateData>
        {
            [templateIds[0]] = new ActorTemplateData
            {
                TemplateId = Guid.Parse(templateIds[0]),
                Category = "category-1",
                BehaviorRef = "behaviors/1.abml",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            [templateIds[1]] = new ActorTemplateData
            {
                TemplateId = Guid.Parse(templateIds[1]),
                Category = "category-2",
                BehaviorRef = "behaviors/2.abml",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateIds);
        _mockTemplateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        // Act
        var (status, response) = await service.ListActorTemplatesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Templates.Count);
        Assert.Equal(2, response.Total);
    }

    #endregion

    #region SpawnActorAsync Tests

    [Fact]
    public async Task SpawnActorAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "test-category",
            BehaviorRef = "behaviors/test.abml"
        };

        var request = new SpawnActorRequest { TemplateId = templateId };

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "test-actor",
            TemplateId = templateId,
            Category = "test-category",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        _mockActorRunnerFactory.Setup(f => f.Create(
            It.IsAny<string>(),
            It.IsAny<ActorTemplateData>(),
            It.IsAny<Guid?>(),
            It.IsAny<object?>(),
            It.IsAny<object?>()))
            .Returns(mockRunner.Object);

        _mockActorRegistry.Setup(r => r.TryGet(It.IsAny<string>(), out It.Ref<IActorRunner?>.IsAny))
            .Returns(false);
        _mockActorRegistry.Setup(r => r.TryRegister(It.IsAny<string>(), It.IsAny<IActorRunner>()))
            .Returns(true);

        // Act
        var (status, response) = await service.SpawnActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SpawnActorAsync_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new SpawnActorRequest { TemplateId = Guid.NewGuid() };

        _mockTemplateStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorTemplateData?)null);

        // Act
        var (status, response) = await service.SpawnActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SpawnActorAsync_DuplicateActorId_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "test-category",
            BehaviorRef = "behaviors/test.abml"
        };

        var request = new SpawnActorRequest { TemplateId = templateId, ActorId = "existing-actor" };

        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var existingRunner = new Mock<IActorRunner>().Object;
        _mockActorRegistry.Setup(r => r.TryGet("existing-actor", out existingRunner))
            .Returns(true);

        // Act
        var (status, response) = await service.SpawnActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region StopActorAsync Tests

    [Fact]
    public async Task StopActorAsync_RunningActor_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var request = new StopActorRequest { ActorId = "test-actor", Graceful = true };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.SetupGet(r => r.TemplateId).Returns(Guid.NewGuid());
        mockRunner.SetupGet(r => r.CharacterId).Returns((Guid?)null);
        mockRunner.SetupGet(r => r.Status).Returns(ActorStatus.Stopped);
        mockRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);
        mockRunner.Setup(r => r.StopAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("test-actor", out runner)).Returns(true);

        // Act
        var (status, response) = await service.StopActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Stopped);
    }

    [Fact]
    public async Task StopActorAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new StopActorRequest { ActorId = "nonexistent", Graceful = true };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        // Act
        var (status, response) = await service.StopActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetActorAsync Tests

    [Fact]
    public async Task GetActorAsync_ExistingActor_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var request = new GetActorRequest { ActorId = "test-actor" };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "test-actor",
            TemplateId = Guid.NewGuid(),
            Category = "test",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("test-actor", out runner)).Returns(true);

        // Act
        var (status, response) = await service.GetActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("test-actor", response.ActorId);
    }

    [Fact]
    public async Task GetActorAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetActorRequest { ActorId = "nonexistent" };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        // Act
        var (status, response) = await service.GetActorAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetActorAsync Auto-Spawn CharacterId Extraction Tests

    [Fact]
    public async Task GetActorAsync_AutoSpawnWithCaptureGroup_ExtractsCharacterId()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var actorId = $"npc-brain-{characterId}";
        var templateId = Guid.NewGuid();
        var request = new GetActorRequest { ActorId = actorId };

        // Actor doesn't exist initially
        IActorRunner? nullRunner = null;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out nullRunner)).Returns(false);

        // Template with auto-spawn and capture group
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "npc-brain",
            BehaviorRef = "behaviors/npc-brain.abml",
            TickIntervalMs = 100,
            AutoSaveIntervalSeconds = 60,
            MaxInstancesPerNode = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            AutoSpawn = new AutoSpawnConfigData
            {
                Enabled = true,
                IdPattern = @"npc-brain-([a-f0-9-]+)",
                CharacterIdCaptureGroup = 1
            }
        };

        _mockIndexStore.Setup(s => s.GetAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { templateId.ToString() });

        _mockTemplateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ActorTemplateData> { { templateId.ToString(), template } });

        // Also mock GetAsync for SpawnActorAsync's template lookup
        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Mock the runner creation after spawn
        var spawnedRunner = new Mock<IActorRunner>();
        spawnedRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            CharacterId = characterId,
            Category = "npc-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        spawnedRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);

        // Setup spawn registration
        _mockActorRegistry.Setup(r => r.TryRegister(actorId, It.IsAny<IActorRunner>())).Returns(true);
        _mockActorRunnerFactory.Setup(f => f.Create(actorId, It.IsAny<ActorTemplateData>(), characterId, It.IsAny<object?>(), It.IsAny<object?>()))
            .Returns(spawnedRunner.Object);

        // Act
        var (status, response) = await service.GetActorAsync(request, CancellationToken.None);

        // Assert
        // Verify that Create was called with the extracted characterId
        _mockActorRunnerFactory.Verify(
            f => f.Create(actorId, It.IsAny<ActorTemplateData>(), characterId, It.IsAny<object?>(), It.IsAny<object?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetActorAsync_AutoSpawnWithoutCaptureGroup_CharacterIdIsNull()
    {
        // Arrange
        var service = CreateService();
        var actorId = "npc-merchant-123";
        var templateId = Guid.NewGuid();
        var request = new GetActorRequest { ActorId = actorId };

        // Actor doesn't exist initially
        IActorRunner? nullRunner = null;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out nullRunner)).Returns(false);

        // Template with auto-spawn but no capture group
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "npc-merchant",
            BehaviorRef = "behaviors/merchant.abml",
            TickIntervalMs = 100,
            AutoSaveIntervalSeconds = 60,
            MaxInstancesPerNode = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            AutoSpawn = new AutoSpawnConfigData
            {
                Enabled = true,
                IdPattern = @"npc-merchant-.*"
                // No CharacterIdCaptureGroup
            }
        };

        _mockIndexStore.Setup(s => s.GetAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { templateId.ToString() });

        _mockTemplateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ActorTemplateData> { { templateId.ToString(), template } });

        // Also mock GetAsync for SpawnActorAsync's template lookup
        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Mock spawn flow
        var spawnedRunner = new Mock<IActorRunner>();
        spawnedRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            Category = "npc-merchant",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        spawnedRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);

        _mockActorRegistry.Setup(r => r.TryRegister(actorId, It.IsAny<IActorRunner>())).Returns(true);
        _mockActorRunnerFactory.Setup(f => f.Create(actorId, It.IsAny<ActorTemplateData>(), null, It.IsAny<object?>(), It.IsAny<object?>()))
            .Returns(spawnedRunner.Object);

        // Act
        var (status, response) = await service.GetActorAsync(request, CancellationToken.None);

        // Assert
        // Verify that Create was called with null characterId
        _mockActorRunnerFactory.Verify(
            f => f.Create(actorId, It.IsAny<ActorTemplateData>(), null, It.IsAny<object?>(), It.IsAny<object?>()),
            Times.Once);
    }

    [Fact]
    public async Task GetActorAsync_AutoSpawnInvalidGuidInCaptureGroup_CharacterIdIsNull()
    {
        // Arrange
        var service = CreateService();
        var actorId = "npc-brain-not-a-guid";
        var templateId = Guid.NewGuid();
        var request = new GetActorRequest { ActorId = actorId };

        // Actor doesn't exist initially
        IActorRunner? nullRunner = null;
        _mockActorRegistry.Setup(r => r.TryGet(actorId, out nullRunner)).Returns(false);

        // Template with auto-spawn and capture group, but captured value isn't a GUID
        var template = new ActorTemplateData
        {
            TemplateId = templateId,
            Category = "npc-brain",
            BehaviorRef = "behaviors/npc-brain.abml",
            TickIntervalMs = 100,
            AutoSaveIntervalSeconds = 60,
            MaxInstancesPerNode = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            AutoSpawn = new AutoSpawnConfigData
            {
                Enabled = true,
                IdPattern = @"npc-brain-(.+)",
                CharacterIdCaptureGroup = 1
            }
        };

        _mockIndexStore.Setup(s => s.GetAsync("_all_template_ids", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { templateId.ToString() });

        _mockTemplateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ActorTemplateData> { { templateId.ToString(), template } });

        // Also mock GetAsync for SpawnActorAsync's template lookup
        _mockTemplateStore.Setup(s => s.GetAsync(templateId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Mock spawn flow
        var spawnedRunner = new Mock<IActorRunner>();
        spawnedRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = actorId,
            TemplateId = templateId,
            Category = "npc-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        spawnedRunner.SetupGet(r => r.StartedAt).Returns(DateTimeOffset.UtcNow);

        _mockActorRegistry.Setup(r => r.TryRegister(actorId, It.IsAny<IActorRunner>())).Returns(true);
        _mockActorRunnerFactory.Setup(f => f.Create(actorId, It.IsAny<ActorTemplateData>(), null, It.IsAny<object?>(), It.IsAny<object?>()))
            .Returns(spawnedRunner.Object);

        // Act
        var (status, response) = await service.GetActorAsync(request, CancellationToken.None);

        // Assert
        // Verify that Create was called with null characterId (invalid GUID gracefully handled)
        _mockActorRunnerFactory.Verify(
            f => f.Create(actorId, It.IsAny<ActorTemplateData>(), null, It.IsAny<object?>(), It.IsAny<object?>()),
            Times.Once);
    }

    #endregion

    #region ListActorsAsync Tests

    [Fact]
    public async Task ListActorsAsync_ReturnsActorsList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListActorsRequest { Limit = 10, Offset = 0 };

        var mockRunner1 = new Mock<IActorRunner>();
        mockRunner1.SetupGet(r => r.Category).Returns("test-category");
        mockRunner1.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner1.SetupGet(r => r.CharacterId).Returns((Guid?)null);
        mockRunner1.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            Category = "test-category",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        _mockActorRegistry.Setup(r => r.GetAllRunners()).Returns(new[] { mockRunner1.Object });

        // Act
        var (status, response) = await service.ListActorsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Actors);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task ListActorsAsync_FilterByCategory_ReturnsFilteredList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListActorsRequest { Category = "target-category", Limit = 10, Offset = 0 };

        var mockRunner1 = new Mock<IActorRunner>();
        mockRunner1.SetupGet(r => r.Category).Returns("target-category");
        mockRunner1.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner1.SetupGet(r => r.CharacterId).Returns((Guid?)null);
        mockRunner1.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "actor-1",
            TemplateId = Guid.NewGuid(),
            Category = "target-category",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var mockRunner2 = new Mock<IActorRunner>();
        mockRunner2.SetupGet(r => r.Category).Returns("other-category");
        mockRunner2.SetupGet(r => r.Status).Returns(ActorStatus.Running);
        mockRunner2.SetupGet(r => r.CharacterId).Returns((Guid?)null);

        _mockActorRegistry.Setup(r => r.GetAllRunners()).Returns(new[] { mockRunner1.Object, mockRunner2.Object });

        // Act
        var (status, response) = await service.ListActorsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Actors);
        Assert.Equal("target-category", response.Actors.First().Category);
    }

    #endregion

    #region InjectPerceptionAsync Tests

    [Fact]
    public async Task InjectPerceptionAsync_ValidActor_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var request = new InjectPerceptionRequest
        {
            ActorId = "test-actor",
            Perception = new PerceptionData
            {
                PerceptionType = "test",
                SourceId = "test-source",
                SourceType = "unit-test",
                Urgency = 0.5f
            }
        };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.InjectPerception(It.IsAny<PerceptionData>())).Returns(true);
        mockRunner.SetupGet(r => r.PerceptionQueueDepth).Returns(1);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("test-actor", out runner)).Returns(true);

        // Act
        var (status, response) = await service.InjectPerceptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Queued);
        Assert.Equal(1, response.QueueDepth);
    }

    [Fact]
    public async Task InjectPerceptionAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new InjectPerceptionRequest
        {
            ActorId = "nonexistent",
            Perception = new PerceptionData()
        };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent", out runner)).Returns(false);

        // Act
        var (status, response) = await service.InjectPerceptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region StartEncounterAsync Tests

    [Fact]
    public async Task StartEncounterAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var participantId = Guid.NewGuid();
        var request = new StartEncounterRequest
        {
            ActorId = "event-brain-1",
            EncounterId = "encounter-001",
            EncounterType = "combat",
            Participants = new List<Guid> { participantId }
        };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.StartEncounter(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<Dictionary<string, object?>?>()))
            .Returns(true);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.StartEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("encounter-001", response.EncounterId);
    }

    [Fact]
    public async Task StartEncounterAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new StartEncounterRequest
        {
            ActorId = "nonexistent-actor",
            EncounterId = "encounter-001",
            EncounterType = "combat",
            Participants = new List<Guid> { Guid.NewGuid() }
        };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent-actor", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        // Act
        var (status, response) = await service.StartEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task StartEncounterAsync_AlreadyHasActiveEncounter_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new StartEncounterRequest
        {
            ActorId = "event-brain-1",
            EncounterId = "encounter-002",
            EncounterType = "combat",
            Participants = new List<Guid> { Guid.NewGuid() }
        };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.StartEncounter(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<Dictionary<string, object?>?>()))
            .Returns(false); // Returns false when encounter already active

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.StartEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateEncounterPhaseAsync Tests

    [Fact]
    public async Task UpdateEncounterPhaseAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateEncounterPhaseRequest
        {
            ActorId = "event-brain-1",
            Phase = "executing"
        };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "event-brain-1",
            TemplateId = Guid.NewGuid(),
            Category = "event-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Encounter = new EncounterStateData
            {
                EncounterId = "encounter-001",
                EncounterType = "combat",
                Participants = new List<Guid>(),
                Phase = "initializing",
                StartedAt = DateTimeOffset.UtcNow
            }
        });
        mockRunner.Setup(r => r.SetEncounterPhase("executing")).Returns(true);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.UpdateEncounterPhaseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("initializing", response.PreviousPhase);
        Assert.Equal("executing", response.CurrentPhase);
    }

    [Fact]
    public async Task UpdateEncounterPhaseAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateEncounterPhaseRequest
        {
            ActorId = "nonexistent-actor",
            Phase = "executing"
        };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent-actor", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        // Act
        var (status, response) = await service.UpdateEncounterPhaseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region EndEncounterAsync Tests

    [Fact]
    public async Task EndEncounterAsync_ValidRequest_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var request = new EndEncounterRequest
        {
            ActorId = "event-brain-1"
        };

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "event-brain-1",
            TemplateId = Guid.NewGuid(),
            Category = "event-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Encounter = new EncounterStateData
            {
                EncounterId = "encounter-001",
                EncounterType = "combat",
                Participants = new List<Guid>(),
                Phase = "complete",
                StartedAt = startedAt
            }
        });
        mockRunner.Setup(r => r.EndEncounter()).Returns(true);

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.EndEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("encounter-001", response.EncounterId);
        Assert.True(response.DurationMs > 0);
    }

    [Fact]
    public async Task EndEncounterAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new EndEncounterRequest
        {
            ActorId = "nonexistent-actor"
        };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent-actor", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        // Act
        var (status, response) = await service.EndEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetEncounterAsync Tests

    [Fact]
    public async Task GetEncounterAsync_WithActiveEncounter_ReturnsEncounterState()
    {
        // Arrange
        var service = CreateService();
        var request = new GetEncounterRequest
        {
            ActorId = "event-brain-1"
        };

        var participantId = Guid.NewGuid();
        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "event-brain-1",
            TemplateId = Guid.NewGuid(),
            Category = "event-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Encounter = new EncounterStateData
            {
                EncounterId = "encounter-001",
                EncounterType = "combat",
                Participants = new List<Guid> { participantId },
                Phase = "executing",
                StartedAt = DateTimeOffset.UtcNow
            }
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.GetEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasActiveEncounter);
        Assert.NotNull(response.Encounter);
        Assert.Equal("encounter-001", response.Encounter.EncounterId);
        Assert.Equal("combat", response.Encounter.EncounterType);
        Assert.Equal("executing", response.Encounter.Phase);
        Assert.Contains(participantId, response.Encounter.Participants);
    }

    [Fact]
    public async Task GetEncounterAsync_NoActiveEncounter_ReturnsHasActiveEncounterFalse()
    {
        // Arrange
        var service = CreateService();
        var request = new GetEncounterRequest
        {
            ActorId = "event-brain-1"
        };

        var mockRunner = new Mock<IActorRunner>();
        mockRunner.Setup(r => r.GetStateSnapshot()).Returns(new ActorStateSnapshot
        {
            ActorId = "event-brain-1",
            TemplateId = Guid.NewGuid(),
            Category = "event-brain",
            Status = ActorStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Encounter = null
        });

        IActorRunner? runner = mockRunner.Object;
        _mockActorRegistry.Setup(r => r.TryGet("event-brain-1", out runner)).Returns(true);

        // Act
        var (status, response) = await service.GetEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasActiveEncounter);
        Assert.Null(response.Encounter);
    }

    [Fact]
    public async Task GetEncounterAsync_ActorNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetEncounterRequest
        {
            ActorId = "nonexistent-actor"
        };

        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent-actor", out runner)).Returns(false);

        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        // Act
        var (status, response) = await service.GetEncounterAsync(request, CancellationToken.None);

        // Assert - Actor not found returns NotFound with null response
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Remote Actor Invocation Tests

    [Fact]
    public async Task StartEncounterAsync_RemoteActor_ForwardsRequestViaMesh()
    {
        // Arrange
        var service = CreateService();
        var remoteNodeId = "remote-node-001";
        var request = new StartEncounterRequest
        {
            ActorId = "remote-actor-1",
            EncounterId = "encounter-001",
            EncounterType = "combat",
            Participants = new List<Guid> { Guid.NewGuid() }
        };

        // Actor not in local registry
        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("remote-actor-1", out runner)).Returns(false);

        // Actor found on remote node
        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("remote-actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "remote-actor-1",
                NodeId = remoteNodeId,
                NodeAppId = "app-remote-001",
                TemplateId = Guid.NewGuid().ToString(),
                Category = "event-brain",
                Status = "running"
            });

        // Mock the remote invocation response
        var expectedResponse = new StartEncounterResponse
        {
            ActorId = "remote-actor-1",
            EncounterId = "encounter-001"
        };

        _mockMeshClient.Setup(m => m.InvokeMethodAsync<StartEncounterRequest, StartEncounterResponse>(
            remoteNodeId,
            "actor/encounter/start",
            It.IsAny<StartEncounterRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var (status, response) = await service.StartEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("encounter-001", response.EncounterId);

        // Verify mesh client was called
        _mockMeshClient.Verify(
            m => m.InvokeMethodAsync<StartEncounterRequest, StartEncounterResponse>(
                remoteNodeId,
                "actor/encounter/start",
                It.Is<StartEncounterRequest>(r => r.ActorId == "remote-actor-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateEncounterPhaseAsync_RemoteActor_ForwardsRequestViaMesh()
    {
        // Arrange
        var service = CreateService();
        var remoteNodeId = "remote-node-002";
        var request = new UpdateEncounterPhaseRequest
        {
            ActorId = "remote-actor-2",
            Phase = "executing"
        };

        // Actor not in local registry
        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("remote-actor-2", out runner)).Returns(false);

        // Actor found on remote node
        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("remote-actor-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "remote-actor-2",
                NodeId = remoteNodeId,
                NodeAppId = "app-remote-002",
                TemplateId = Guid.NewGuid().ToString(),
                Category = "event-brain",
                Status = "running"
            });

        // Mock the remote invocation response
        var expectedResponse = new UpdateEncounterPhaseResponse
        {
            ActorId = "remote-actor-2",
            PreviousPhase = "initializing",
            CurrentPhase = "executing"
        };

        _mockMeshClient.Setup(m => m.InvokeMethodAsync<UpdateEncounterPhaseRequest, UpdateEncounterPhaseResponse>(
            remoteNodeId,
            "actor/encounter/phase/update",
            It.IsAny<UpdateEncounterPhaseRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var (status, response) = await service.UpdateEncounterPhaseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("executing", response.CurrentPhase);

        // Verify mesh client was called
        _mockMeshClient.Verify(
            m => m.InvokeMethodAsync<UpdateEncounterPhaseRequest, UpdateEncounterPhaseResponse>(
                remoteNodeId,
                "actor/encounter/phase/update",
                It.Is<UpdateEncounterPhaseRequest>(r => r.ActorId == "remote-actor-2" && r.Phase == "executing"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EndEncounterAsync_RemoteActor_ForwardsRequestViaMesh()
    {
        // Arrange
        var service = CreateService();
        var remoteNodeId = "remote-node-003";
        var request = new EndEncounterRequest
        {
            ActorId = "remote-actor-3"
        };

        // Actor not in local registry
        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("remote-actor-3", out runner)).Returns(false);

        // Actor found on remote node
        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("remote-actor-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "remote-actor-3",
                NodeId = remoteNodeId,
                NodeAppId = "app-remote-003",
                TemplateId = Guid.NewGuid().ToString(),
                Category = "event-brain",
                Status = "running"
            });

        // Mock the remote invocation response
        var expectedResponse = new EndEncounterResponse
        {
            ActorId = "remote-actor-3",
            EncounterId = "encounter-003",
            DurationMs = 5000
        };

        _mockMeshClient.Setup(m => m.InvokeMethodAsync<EndEncounterRequest, EndEncounterResponse>(
            remoteNodeId,
            "actor/encounter/end",
            It.IsAny<EndEncounterRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var (status, response) = await service.EndEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("encounter-003", response.EncounterId);
        Assert.Equal(5000, response.DurationMs);

        // Verify mesh client was called
        _mockMeshClient.Verify(
            m => m.InvokeMethodAsync<EndEncounterRequest, EndEncounterResponse>(
                remoteNodeId,
                "actor/encounter/end",
                It.Is<EndEncounterRequest>(r => r.ActorId == "remote-actor-3"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEncounterAsync_RemoteActor_ForwardsRequestViaMesh()
    {
        // Arrange
        var service = CreateService();
        var remoteNodeId = "remote-node-004";
        var participantId = Guid.NewGuid();
        var request = new GetEncounterRequest
        {
            ActorId = "remote-actor-4"
        };

        // Actor not in local registry
        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("remote-actor-4", out runner)).Returns(false);

        // Actor found on remote node
        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("remote-actor-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorAssignment
            {
                ActorId = "remote-actor-4",
                NodeId = remoteNodeId,
                NodeAppId = "app-remote-004",
                TemplateId = Guid.NewGuid().ToString(),
                Category = "event-brain",
                Status = "running"
            });

        // Mock the remote invocation response
        var expectedResponse = new GetEncounterResponse
        {
            ActorId = "remote-actor-4",
            HasActiveEncounter = true,
            Encounter = new EncounterState
            {
                EncounterId = "encounter-004",
                EncounterType = "dialogue",
                Participants = new List<Guid> { participantId },
                Phase = "active",
                StartedAt = DateTimeOffset.UtcNow
            }
        };

        _mockMeshClient.Setup(m => m.InvokeMethodAsync<GetEncounterRequest, GetEncounterResponse>(
            remoteNodeId,
            "actor/encounter/get",
            It.IsAny<GetEncounterRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var (status, response) = await service.GetEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasActiveEncounter);
        Assert.NotNull(response.Encounter);
        Assert.Equal("encounter-004", response.Encounter.EncounterId);
        Assert.Equal("dialogue", response.Encounter.EncounterType);
        Assert.Contains(participantId, response.Encounter.Participants);

        // Verify mesh client was called
        _mockMeshClient.Verify(
            m => m.InvokeMethodAsync<GetEncounterRequest, GetEncounterResponse>(
                remoteNodeId,
                "actor/encounter/get",
                It.Is<GetEncounterRequest>(r => r.ActorId == "remote-actor-4"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartEncounterAsync_LocalAndRemoteNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new StartEncounterRequest
        {
            ActorId = "nonexistent-actor",
            EncounterId = "encounter-999",
            EncounterType = "combat",
            Participants = new List<Guid> { Guid.NewGuid() }
        };

        // Actor not in local registry
        IActorRunner? runner = null;
        _mockActorRegistry.Setup(r => r.TryGet("nonexistent-actor", out runner)).Returns(false);

        // Actor not found in pool manager either
        _mockPoolManager.Setup(p => p.GetActorAssignmentAsync("nonexistent-actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActorAssignment?)null);

        // Act
        var (status, response) = await service.StartEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        // Verify mesh client was NOT called (no remote node to forward to)
        _mockMeshClient.Verify(
            m => m.InvokeMethodAsync<StartEncounterRequest, StartEncounterResponse>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<StartEncounterRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}

/// <summary>
/// Tests for ActorServiceConfiguration.
/// </summary>
public class ActorConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new ActorServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_DefaultValues_AreReasonable()
    {
        // Arrange & Act
        var config = new ActorServiceConfiguration
        {
            DefaultTickIntervalMs = 100,
            DefaultAutoSaveIntervalSeconds = 60,
            DefaultActorsPerNode = 100,
            PerceptionQueueSize = 100,
            MessageQueueSize = 50,
            DeploymentMode = "bannou"
        };

        // Assert
        Assert.Equal(100, config.DefaultTickIntervalMs);
        Assert.Equal(60, config.DefaultAutoSaveIntervalSeconds);
        Assert.Equal(100, config.DefaultActorsPerNode);
        Assert.Equal(100, config.PerceptionQueueSize);
        Assert.Equal(50, config.MessageQueueSize);
        Assert.Equal("bannou", config.DeploymentMode);
    }
}
