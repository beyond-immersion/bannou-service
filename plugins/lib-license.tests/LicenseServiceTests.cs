using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.License;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.License.Tests;

public class LicenseServiceTests : ServiceTestBase<LicenseServiceConfiguration>
{
    // Infrastructure mocks
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<LicenseService>> _mockLogger;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    // Store mocks
    private readonly Mock<IQueryableStateStore<BoardTemplateModel>> _mockTemplateStore;
    private readonly Mock<IQueryableStateStore<LicenseDefinitionModel>> _mockDefinitionStore;
    private readonly Mock<IQueryableStateStore<BoardInstanceModel>> _mockBoardStore;
    private readonly Mock<IStateStore<BoardCacheModel>> _mockBoardCache;

    // Client mocks
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<IItemClient> _mockItemClient;
    private readonly Mock<ICurrencyClient> _mockCurrencyClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IResourceClient> _mockResourceClient;

    // Lock response mock
    private readonly Mock<ILockResponse> _mockLockResponse;

    // Shared test IDs
    private static readonly Guid TestGameServiceId = Guid.NewGuid();
    private static readonly Guid TestBoardTemplateId = Guid.NewGuid();
    private static readonly Guid TestContractTemplateId = Guid.NewGuid();
    private static readonly Guid TestCharacterId = Guid.NewGuid();
    private static readonly Guid TestBoardId = Guid.NewGuid();
    private static readonly Guid TestContainerId = Guid.NewGuid();
    private static readonly Guid TestItemTemplateId = Guid.NewGuid();
    private static readonly Guid TestRealmId = Guid.NewGuid();

    public LicenseServiceTests()
    {
        // Initialize infrastructure mocks
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<LicenseService>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        // Initialize store mocks
        _mockTemplateStore = new Mock<IQueryableStateStore<BoardTemplateModel>>();
        _mockDefinitionStore = new Mock<IQueryableStateStore<LicenseDefinitionModel>>();
        _mockBoardStore = new Mock<IQueryableStateStore<BoardInstanceModel>>();
        _mockBoardCache = new Mock<IStateStore<BoardCacheModel>>();

        // Initialize client mocks
        _mockContractClient = new Mock<IContractClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockItemClient = new Mock<IItemClient>();
        _mockCurrencyClient = new Mock<ICurrencyClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockResourceClient = new Mock<IResourceClient>();

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<BoardTemplateModel>(StateStoreDefinitions.LicenseBoardTemplates))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<LicenseDefinitionModel>(StateStoreDefinitions.LicenseDefinitions))
            .Returns(_mockDefinitionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<BoardInstanceModel>(StateStoreDefinitions.LicenseBoards))
            .Returns(_mockBoardStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<BoardCacheModel>(StateStoreDefinitions.LicenseBoardCache))
            .Returns(_mockBoardCache.Object);

        // Default save behavior
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockBoardStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockBoardCache
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default delete behavior
        _mockTemplateStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockDefinitionStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockBoardStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockBoardCache
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default query behavior (empty results)
        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardTemplateModel>());
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>());
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel>());

        // Default publish behavior (both overloads)
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default resource client behavior
        _mockResourceClient
            .Setup(m => m.RegisterReferenceAsync(It.IsAny<RegisterReferenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterReferenceResponse());
        _mockResourceClient
            .Setup(m => m.UnregisterReferenceAsync(It.IsAny<UnregisterReferenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnregisterReferenceResponse());

        // Default lock behavior (success)
        _mockLockResponse = new Mock<ILockResponse>();
        _mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLockResponse.Object);
    }

    #region Helpers

    private LicenseService CreateService() => new LicenseService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLogger.Object,
        Configuration,
        _mockContractClient.Object,
        _mockCharacterClient.Object,
        _mockInventoryClient.Object,
        _mockItemClient.Object,
        _mockCurrencyClient.Object,
        _mockGameServiceClient.Object,
        _mockLockProvider.Object,
        _mockResourceClient.Object);

    private static BoardTemplateModel CreateTestTemplate(
        Guid? boardTemplateId = null,
        Guid? gameServiceId = null,
        int gridWidth = 5,
        int gridHeight = 5,
        AdjacencyMode adjacencyMode = AdjacencyMode.EightWay,
        bool isActive = true,
        List<EntityType>? allowedOwnerTypes = null)
    {
        return new BoardTemplateModel
        {
            BoardTemplateId = boardTemplateId ?? TestBoardTemplateId,
            GameServiceId = gameServiceId ?? TestGameServiceId,
            Name = "Test Board",
            Description = "A test board template",
            GridWidth = gridWidth,
            GridHeight = gridHeight,
            StartingNodes = new List<GridPositionEntry> { new GridPositionEntry { X = 0, Y = 0 } },
            BoardContractTemplateId = TestContractTemplateId,
            AdjacencyMode = adjacencyMode,
            AllowedOwnerTypes = allowedOwnerTypes ?? new List<EntityType> { EntityType.Character },
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static LicenseDefinitionModel CreateTestDefinition(
        string code = "fire_mastery_1",
        int positionX = 0,
        int positionY = 0,
        int lpCost = 10,
        Guid? boardTemplateId = null,
        List<string>? prerequisites = null)
    {
        return new LicenseDefinitionModel
        {
            LicenseDefinitionId = Guid.NewGuid(),
            BoardTemplateId = boardTemplateId ?? TestBoardTemplateId,
            Code = code,
            PositionX = positionX,
            PositionY = positionY,
            LpCost = lpCost,
            ItemTemplateId = TestItemTemplateId,
            Prerequisites = prerequisites,
            Description = $"Test definition: {code}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static BoardInstanceModel CreateTestBoard(
        Guid? boardId = null,
        EntityType ownerType = EntityType.Character,
        Guid? ownerId = null,
        Guid? realmId = null,
        Guid? boardTemplateId = null,
        Guid? containerId = null)
    {
        return new BoardInstanceModel
        {
            BoardId = boardId ?? TestBoardId,
            OwnerType = ownerType,
            OwnerId = ownerId ?? TestCharacterId,
            RealmId = realmId ?? TestRealmId,
            BoardTemplateId = boardTemplateId ?? TestBoardTemplateId,
            GameServiceId = TestGameServiceId,
            ContainerId = containerId ?? TestContainerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static BoardCacheModel CreateTestCache(
        Guid? boardId = null,
        List<UnlockedLicenseEntry>? unlockedPositions = null)
    {
        return new BoardCacheModel
        {
            BoardId = boardId ?? TestBoardId,
            UnlockedPositions = unlockedPositions ?? new List<UnlockedLicenseEntry>(),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Sets up all mocks needed for a successful unlock scenario.
    /// Returns IDs used in the setup for assertion.
    /// </summary>
    private (Guid contractId, Guid itemInstanceId) SetupUnlockScenario(
        BoardInstanceModel board,
        BoardTemplateModel template,
        LicenseDefinitionModel definition,
        BoardCacheModel? cache = null)
    {
        var contractId = Guid.NewGuid();
        var itemInstanceId = Guid.NewGuid();

        // Board exists
        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{board.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        // Template exists
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Definition exists
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"lic-def:{template.BoardTemplateId}:{definition.Code}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        // Board cache
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{board.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache ?? CreateTestCache(board.BoardId));

        // Contract creation succeeds
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractId });

        // Character fetch (only needed for character owner type validation in CreateBoard)
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = board.OwnerId, RealmId = TestRealmId });

        // Item creation succeeds
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = itemInstanceId });

        return (contractId, itemInstanceId);
    }

    #endregion

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void LicenseService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<LicenseService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void LicenseServiceConfiguration_CanBeInstantiated()
    {
        var config = new LicenseServiceConfiguration();

        Assert.NotNull(config);
        Assert.Equal(10, config.MaxBoardsPerOwner);
        Assert.Equal(200, config.MaxDefinitionsPerBoard);
        Assert.Equal(30, config.LockTimeoutSeconds);
        Assert.Equal(300, config.BoardCacheTtlSeconds);
        Assert.Equal(20, config.DefaultPageSize);
        Assert.Equal(3, config.MaxConcurrencyRetries);
        Assert.Equal(AdjacencyMode.EightWay, config.DefaultAdjacencyMode);
    }

    #endregion

    #region Board Template CRUD Tests

    [Fact]
    public async Task CreateBoardTemplate_ValidRequest_SavesAndPublishesEvent()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockContractClient
            .Setup(c => c.GetContractTemplateAsync(It.IsAny<GetContractTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractTemplateResponse { TemplateId = TestContractTemplateId });

        BoardTemplateModel? savedTemplate = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedTemplate = m)
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new CreateBoardTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Name = "Fire License Board",
            Description = "Grants fire abilities",
            GridWidth = 5,
            GridHeight = 5,
            StartingNodes = new List<GridPosition> { new GridPosition { X = 0, Y = 0 } },
            BoardContractTemplateId = TestContractTemplateId,
            AdjacencyMode = AdjacencyMode.EightWay,
            AllowedOwnerTypes = new List<EntityType> { EntityType.Character }
        };

        // Act
        var (status, response) = await service.CreateBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Fire License Board", response.Name);
        Assert.Equal(5, response.GridWidth);
        Assert.Equal(5, response.GridHeight);
        Assert.Equal(AdjacencyMode.EightWay, response.AdjacencyMode);
        Assert.True(response.IsActive);

        Assert.NotNull(savedTemplate);
        Assert.Equal(TestGameServiceId, savedTemplate.GameServiceId);
        Assert.Equal("Fire License Board", savedTemplate.Name);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board-template.created", It.IsAny<LicenseBoardTemplateCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateBoardTemplate_InvalidGameServiceId_ReturnsNotFound()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();
        var request = new CreateBoardTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            Name = "Test",
            GridWidth = 5,
            GridHeight = 5,
            StartingNodes = new List<GridPosition> { new GridPosition { X = 0, Y = 0 } },
            BoardContractTemplateId = TestContractTemplateId,
            AllowedOwnerTypes = new List<EntityType> { EntityType.Character }
        };

        // Act
        var (status, response) = await service.CreateBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBoardTemplate_StartingNodeOutOfBounds_ReturnsBadRequest()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });

        var service = CreateService();
        var request = new CreateBoardTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Name = "Test",
            GridWidth = 3,
            GridHeight = 3,
            StartingNodes = new List<GridPosition> { new GridPosition { X = 5, Y = 5 } },
            BoardContractTemplateId = TestContractTemplateId,
            AllowedOwnerTypes = new List<EntityType> { EntityType.Character }
        };

        // Act
        var (status, response) = await service.CreateBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBoardTemplate_InvalidContractTemplateId_ReturnsNotFound()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockContractClient
            .Setup(c => c.GetContractTemplateAsync(It.IsAny<GetContractTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();
        var request = new CreateBoardTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Name = "Test",
            GridWidth = 5,
            GridHeight = 5,
            StartingNodes = new List<GridPosition> { new GridPosition { X = 0, Y = 0 } },
            BoardContractTemplateId = Guid.NewGuid(),
            AllowedOwnerTypes = new List<EntityType> { EntityType.Character }
        };

        // Act
        var (status, response) = await service.CreateBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetBoardTemplate_Exists_ReturnsTemplate()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardTemplateAsync(
            new GetBoardTemplateRequest { BoardTemplateId = TestBoardTemplateId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestBoardTemplateId, response.BoardTemplateId);
        Assert.Equal("Test Board", response.Name);
        Assert.Equal(5, response.GridWidth);
    }

    [Fact]
    public async Task GetBoardTemplate_NotFound_ReturnsNotFound()
    {
        // Arrange - default GetAsync returns null
        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardTemplateAsync(
            new GetBoardTemplateRequest { BoardTemplateId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteBoardTemplate_ActiveBoardInstances_ReturnsConflict()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Board store returns active boards for this template
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { CreateTestBoard() });

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteBoardTemplateAsync(
            new DeleteBoardTemplateRequest { BoardTemplateId = TestBoardTemplateId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region License Definition Tests

    [Fact]
    public async Task AddLicenseDefinition_ValidRequest_SavesAndReturns()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemTemplateResponse { TemplateId = TestItemTemplateId });

        LicenseDefinitionModel? savedDefinition = null;
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LicenseDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDefinition = m)
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "fire_mastery_1",
            Position = new GridPosition { X = 2, Y = 3 },
            LpCost = 15,
            ItemTemplateId = TestItemTemplateId,
            Description = "Grants fire mastery level 1"
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("fire_mastery_1", response.Code);
        Assert.Equal(2, response.Position.X);
        Assert.Equal(3, response.Position.Y);
        Assert.Equal(15, response.LpCost);

        Assert.NotNull(savedDefinition);
        Assert.Equal(TestBoardTemplateId, savedDefinition.BoardTemplateId);
        Assert.Equal("fire_mastery_1", savedDefinition.Code);
        Assert.Equal(2, savedDefinition.PositionX);
        Assert.Equal(3, savedDefinition.PositionY);
    }

    [Fact]
    public async Task AddLicenseDefinition_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "fire_mastery_1", positionX: 1, positionY: 1)
            });

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "fire_mastery_1",
            Position = new GridPosition { X = 2, Y = 2 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddLicenseDefinition_DuplicatePosition_ReturnsConflict()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "existing_skill", positionX: 2, positionY: 3)
            });

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "new_skill",
            Position = new GridPosition { X = 2, Y = 3 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddLicenseDefinition_PositionOutOfBounds_ReturnsBadRequest()
    {
        // Arrange
        var template = CreateTestTemplate(gridWidth: 3, gridHeight: 3);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "out_of_bounds",
            Position = new GridPosition { X = 5, Y = 5 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddLicenseDefinition_ExceedsMaxPerBoard_ReturnsConflict()
    {
        // Arrange
        Configuration.MaxDefinitionsPerBoard = 2;
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "skill_1", positionX: 0, positionY: 0),
                CreateTestDefinition(code: "skill_2", positionX: 1, positionY: 0)
            });

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "skill_3",
            Position = new GridPosition { X = 2, Y = 0 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddLicenseDefinition_InvalidItemTemplate_ReturnsNotFound()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "test_skill",
            Position = new GridPosition { X = 1, Y = 1 },
            LpCost = 10,
            ItemTemplateId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveLicenseDefinition_UnlockedByInstance_ReturnsConflict()
    {
        // Arrange
        var definition = CreateTestDefinition(code: "unlocked_skill", positionX: 1, positionY: 1);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:unlocked_skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var board = CreateTestBoard();
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { board });

        // Board cache shows this definition is unlocked
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
            {
                new UnlockedLicenseEntry
                {
                    Code = "unlocked_skill",
                    PositionX = 1,
                    PositionY = 1,
                    ItemInstanceId = Guid.NewGuid(),
                    UnlockedAt = DateTimeOffset.UtcNow
                }
            }));

        var service = CreateService();

        // Act
        var (status, response) = await service.RemoveLicenseDefinitionAsync(
            new RemoveLicenseDefinitionRequest
            {
                BoardTemplateId = TestBoardTemplateId,
                Code = "unlocked_skill"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveLicenseDefinition_CacheExpiredButUnlocked_ReturnsConflict()
    {
        // Arrange - cache returns null (TTL expired) but inventory has the item
        var definition = CreateTestDefinition(code: "unlocked_skill", positionX: 1, positionY: 1);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:unlocked_skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var board = CreateTestBoard();
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { board });

        // All definitions for this template (needed by LoadOrRebuildBoardCacheAsync)
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel> { definition });

        // Cache returns null (simulating TTL expiry)
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BoardCacheModel?)null);

        // Inventory shows the item exists (authoritative source confirms unlock)
        var itemInstanceId = Guid.NewGuid();
        _mockInventoryClient
            .Setup(c => c.GetContainerAsync(It.IsAny<GetContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerWithContentsResponse
            {
                Items = new List<ContainerItem>
                {
                    new ContainerItem { InstanceId = itemInstanceId, TemplateId = TestItemTemplateId }
                }
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.RemoveLicenseDefinitionAsync(
            new RemoveLicenseDefinitionRequest
            {
                BoardTemplateId = TestBoardTemplateId,
                Code = "unlocked_skill"
            },
            CancellationToken.None);

        // Assert - should detect the unlock via inventory rebuild, not miss it due to expired cache
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region Board Instance Tests

    [Fact]
    public async Task CreateBoard_ValidRequest_CreatesContainerAndBoard()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = TestCharacterId, RealmId = TestRealmId });
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var newContainerId = Guid.NewGuid();
        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = newContainerId });

        BoardInstanceModel? savedBoard = null;
        _mockBoardStore
            .Setup(s => s.SaveAsync(It.Is<string>(k => k.StartsWith("board:")), It.IsAny<BoardInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBoard = m)
            .ReturnsAsync("etag");

        BoardCacheModel? savedCache = null;
        _mockBoardCache
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardCacheModel, StateOptions?, CancellationToken>((_, m, _, _) => savedCache = m)
            .ReturnsAsync("etag");

        var service = CreateService();

        // Act
        var (status, response) = await service.CreateBoardAsync(
            new CreateBoardRequest
            {
                OwnerType = EntityType.Character,
                OwnerId = TestCharacterId,
                BoardTemplateId = TestBoardTemplateId,
                GameServiceId = TestGameServiceId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(EntityType.Character, response.OwnerType);
        Assert.Equal(TestCharacterId, response.OwnerId);
        Assert.Equal(TestBoardTemplateId, response.BoardTemplateId);
        Assert.Equal(newContainerId, response.ContainerId);

        Assert.NotNull(savedBoard);
        Assert.Equal(newContainerId, savedBoard.ContainerId);
        Assert.Equal(EntityType.Character, savedBoard.OwnerType);
        Assert.Equal(TestCharacterId, savedBoard.OwnerId);
        Assert.Equal(TestRealmId, savedBoard.RealmId);

        Assert.NotNull(savedCache);
        Assert.Empty(savedCache.UnlockedPositions);

        _mockInventoryClient.Verify(
            c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board.created", It.IsAny<LicenseBoardCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateBoard_DuplicateTemplatePerOwner_ReturnsConflict()
    {
        // Arrange
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = TestCharacterId, RealmId = TestRealmId });
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        // Existing board for this owner+template combination
        _mockBoardStore
            .Setup(s => s.GetAsync($"board-owner:character:{TestCharacterId}:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestBoard());

        var service = CreateService();

        // Act
        var (status, response) = await service.CreateBoardAsync(
            new CreateBoardRequest
            {
                OwnerType = EntityType.Character,
                OwnerId = TestCharacterId,
                BoardTemplateId = TestBoardTemplateId,
                GameServiceId = TestGameServiceId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBoard_ExceedsMaxBoardsPerOwner_ReturnsConflict()
    {
        // Arrange
        Configuration.MaxBoardsPerOwner = 1;
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = TestCharacterId, RealmId = TestRealmId });
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        // Owner already has max boards
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { CreateTestBoard(boardId: Guid.NewGuid(), boardTemplateId: Guid.NewGuid()) });

        var service = CreateService();

        // Act
        var (status, response) = await service.CreateBoardAsync(
            new CreateBoardRequest
            {
                OwnerType = EntityType.Character,
                OwnerId = TestCharacterId,
                BoardTemplateId = TestBoardTemplateId,
                GameServiceId = TestGameServiceId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBoard_InvalidCharacterOwner_ReturnsNotFound()
    {
        // Arrange
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        var service = CreateService();

        // Act
        var (status, response) = await service.CreateBoardAsync(
            new CreateBoardRequest
            {
                OwnerType = EntityType.Character,
                OwnerId = Guid.NewGuid(),
                BoardTemplateId = TestBoardTemplateId,
                GameServiceId = TestGameServiceId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBoard_InactiveTemplate_ReturnsBadRequest()
    {
        // Arrange
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = TestCharacterId, RealmId = TestRealmId });
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(isActive: false));

        var service = CreateService();

        // Act
        var (status, response) = await service.CreateBoardAsync(
            new CreateBoardRequest
            {
                OwnerType = EntityType.Character,
                OwnerId = TestCharacterId,
                BoardTemplateId = TestBoardTemplateId,
                GameServiceId = TestGameServiceId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteBoard_ValidBoard_DeletesContainerAndBoard()
    {
        // Arrange
        var board = CreateTestBoard();
        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteBoardAsync(
            new DeleteBoardRequest { BoardId = TestBoardId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestBoardId, response.BoardId);

        // Verify container deleted
        _mockInventoryClient.Verify(
            c => c.DeleteContainerAsync(It.Is<DeleteContainerRequest>(r => r.ContainerId == TestContainerId), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify board records deleted (board key + uniqueness key)
        _mockBoardStore.Verify(
            s => s.DeleteAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockBoardStore.Verify(
            s => s.DeleteAsync($"board-owner:character:{TestCharacterId}:{TestBoardTemplateId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify cache invalidated
        _mockBoardCache.Verify(
            s => s.DeleteAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify event published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board.deleted", It.IsAny<LicenseBoardDeletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Unlock Tests

    [Fact]
    public async Task Unlock_StartingNode_UnlocksWithoutAdjacency()
    {
        // Arrange - definition is at starting node position (0,0)
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0, lpCost: 5);
        var (contractId, itemInstanceId) = SetupUnlockScenario(board, template, definition);

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("start_skill", response.LicenseCode);
        Assert.Equal(0, response.Position.X);
        Assert.Equal(0, response.Position.Y);
        Assert.Equal(itemInstanceId, response.ItemInstanceId);
        Assert.Equal(contractId, response.ContractInstanceId);
    }

    [Fact]
    public async Task Unlock_AdjacentToUnlocked_EightWay_Succeeds()
    {
        // Arrange - EightWay mode, unlocked at (0,0), trying (1,1) diagonal
        var board = CreateTestBoard();
        var template = CreateTestTemplate(adjacencyMode: AdjacencyMode.EightWay);
        var definition = CreateTestDefinition(code: "diagonal_skill", positionX: 1, positionY: 1, lpCost: 10);

        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        var (contractId, itemInstanceId) = SetupUnlockScenario(board, template, definition, cache);
        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "diagonal_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("diagonal_skill", response.LicenseCode);
    }

    [Fact]
    public async Task Unlock_AdjacentToUnlocked_FourWay_DiagonalFails()
    {
        // Arrange - FourWay mode, unlocked at (0,0), trying (1,1) diagonal = NOT adjacent
        var board = CreateTestBoard();
        var template = CreateTestTemplate(adjacencyMode: AdjacencyMode.FourWay);
        var definition = CreateTestDefinition(code: "diagonal_skill", positionX: 1, positionY: 1, lpCost: 10);

        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        // Board, template, definition, and cache exist
        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:diagonal_skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "diagonal_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task Unlock_NotAdjacent_ReturnsBadRequest()
    {
        // Arrange - unlocked at (0,0), trying (3,3) = not adjacent in any mode
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "far_skill", positionX: 3, positionY: 3, lpCost: 10);

        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:far_skill", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "far_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task Unlock_AlreadyUnlocked_ReturnsConflict()
    {
        // Arrange - license already in cache
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "already_done", positionX: 0, positionY: 0);

        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "already_done", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:already_done", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "already_done" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task Unlock_PrerequisiteNotMet_ReturnsBadRequest()
    {
        // Arrange - definition requires "basic_fire" which is not unlocked
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(
            code: "advanced_fire",
            positionX: 0,
            positionY: 1,
            prerequisites: new List<string> { "basic_fire" });

        // Adjacent to starting node (0,0) -> (0,1) is FourWay adjacent
        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:advanced_fire", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "advanced_fire" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task Unlock_PrerequisiteMet_Succeeds()
    {
        // Arrange - definition requires "basic_fire" which IS unlocked
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(
            code: "advanced_fire",
            positionX: 0,
            positionY: 1,
            prerequisites: new List<string> { "basic_fire" });

        var cache = CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedLicenseEntry { Code = "basic_fire", PositionX = 1, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        var (contractId, itemInstanceId) = SetupUnlockScenario(board, template, definition, cache);
        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "advanced_fire" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("advanced_fire", response.LicenseCode);
    }

    [Fact]
    public async Task Unlock_ContractFails_ReturnsErrorAndPublishesFailEvent()
    {
        // Arrange - starting node, item creation succeeds but contract creation throws
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0);
        var itemInstanceId = Guid.NewGuid();

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:start_skill", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        // Item creation succeeds (step 9b — now happens before contract creation)
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = itemInstanceId });

        // Contract creation fails (step 10 — triggers compensation)
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Contract creation failed", 500, null, null, null));

        var service = CreateService();

        // Act
        var (status, response) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        // Verify unlock-failed event was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(LicenseTopics.LicenseUnlockFailed, It.IsAny<LicenseUnlockFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify compensation: item was destroyed after contract failure
        _mockItemClient.Verify(
            c => c.DestroyItemInstanceAsync(It.Is<DestroyItemInstanceRequest>(r => r.InstanceId == itemInstanceId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Unlock_Success_CreatesItemAndPlacesInContainer()
    {
        // Arrange
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0, lpCost: 5);

        var (_, _) = SetupUnlockScenario(board, template, definition);

        CreateItemInstanceRequest? capturedItemRequest = null;
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateItemInstanceRequest, CancellationToken>((r, _) => capturedItemRequest = r)
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = Guid.NewGuid() });

        var service = CreateService();

        // Act
        var (status, _) = await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedItemRequest);
        Assert.Equal(TestItemTemplateId, capturedItemRequest.TemplateId);
        Assert.Equal(TestContainerId, capturedItemRequest.ContainerId);
        Assert.Equal(TestRealmId, capturedItemRequest.RealmId);
        Assert.Equal(1, capturedItemRequest.Quantity);
    }

    [Fact]
    public async Task Unlock_Success_PublishesUnlockedEvent()
    {
        // Arrange
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0, lpCost: 5);
        var (contractId, itemInstanceId) = SetupUnlockScenario(board, template, definition);

        LicenseUnlockedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(LicenseTopics.LicenseUnlocked, It.IsAny<LicenseUnlockedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, LicenseUnlockedEvent, CancellationToken>((_, e, _) => capturedEvent = e)
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(TestBoardId, capturedEvent.BoardId);
        Assert.Equal(EntityType.Character, capturedEvent.OwnerType);
        Assert.Equal(TestCharacterId, capturedEvent.OwnerId);
        Assert.Equal("start_skill", capturedEvent.LicenseCode);
        Assert.Equal(0, capturedEvent.Position.X);
        Assert.Equal(0, capturedEvent.Position.Y);
        Assert.Equal(itemInstanceId, capturedEvent.ItemInstanceId);
        Assert.Equal(contractId, capturedEvent.ContractInstanceId);
        Assert.Equal(5, capturedEvent.LpCost);
    }

    [Fact]
    public async Task Unlock_Success_UpdatesBoardCache()
    {
        // Arrange
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0);
        var (_, itemInstanceId) = SetupUnlockScenario(board, template, definition);

        BoardCacheModel? savedCache = null;
        _mockBoardCache
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BoardCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardCacheModel, StateOptions?, CancellationToken>((_, m, _, _) => savedCache = m)
            .ReturnsAsync("etag");

        var service = CreateService();

        // Act
        await service.UnlockLicenseAsync(
            new UnlockLicenseRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(savedCache);
        Assert.Single(savedCache.UnlockedPositions);
        var entry = savedCache.UnlockedPositions[0];
        Assert.Equal("start_skill", entry.Code);
        Assert.Equal(0, entry.PositionX);
        Assert.Equal(0, entry.PositionY);
        Assert.Equal(itemInstanceId, entry.ItemInstanceId);
    }

    #endregion

    #region Check-Unlockable Tests

    [Fact]
    public async Task CheckUnlockable_AllConditionsMet_ReturnsTrue()
    {
        // Arrange - starting node, sufficient LP
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0, lpCost: 10);

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:start_skill", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        _mockCurrencyClient
            .Setup(c => c.GetOrCreateWalletAsync(It.IsAny<GetOrCreateWalletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrCreateWalletResponse
            {
                Balances = new List<BalanceSummary> { new BalanceSummary { Amount = 100 } }
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.CheckUnlockableAsync(
            new CheckUnlockableRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Unlockable);
        Assert.True(response.AdjacencyMet);
        Assert.True(response.PrerequisitesMet);
        Assert.True(response.LpSufficient);
        Assert.Equal(100, response.CurrentLp);
        Assert.Equal(10, response.RequiredLp);
    }

    [Fact]
    public async Task CheckUnlockable_NotAdjacent_ReturnsFalseWithReason()
    {
        // Arrange - non-starting node with no adjacent unlocked
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "isolated_skill", positionX: 3, positionY: 3, lpCost: 5);

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:isolated_skill", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        _mockCurrencyClient
            .Setup(c => c.GetOrCreateWalletAsync(It.IsAny<GetOrCreateWalletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrCreateWalletResponse
            {
                Balances = new List<BalanceSummary> { new BalanceSummary { Amount = 100 } }
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.CheckUnlockableAsync(
            new CheckUnlockableRequest { BoardId = TestBoardId, LicenseCode = "isolated_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Unlockable);
        Assert.False(response.AdjacencyMet);
        Assert.True(response.PrerequisitesMet);
        Assert.True(response.LpSufficient);
    }

    [Fact]
    public async Task CheckUnlockable_InsufficientLp_ReturnsFalseWithReason()
    {
        // Arrange - starting node but insufficient LP
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var definition = CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0, lpCost: 100);

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockDefinitionStore.Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:start_skill", It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        _mockCurrencyClient
            .Setup(c => c.GetOrCreateWalletAsync(It.IsAny<GetOrCreateWalletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetOrCreateWalletResponse
            {
                Balances = new List<BalanceSummary> { new BalanceSummary { Amount = 5 } }
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.CheckUnlockableAsync(
            new CheckUnlockableRequest { BoardId = TestBoardId, LicenseCode = "start_skill" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Unlockable);
        Assert.True(response.AdjacencyMet);
        Assert.True(response.PrerequisitesMet);
        Assert.False(response.LpSufficient);
        Assert.Equal(5, response.CurrentLp);
        Assert.Equal(100, response.RequiredLp);
    }

    #endregion

    #region Board State Tests

    [Fact]
    public async Task GetBoardState_EmptyBoard_AllNodesLocked()
    {
        // Arrange - board with definitions but no unlocks; non-starting nodes should be locked
        var board = CreateTestBoard();
        var template = CreateTestTemplate();

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "skill_far", positionX: 3, positionY: 3)
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardStateAsync(
            new BoardStateRequest { BoardId = TestBoardId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Nodes);
        Assert.Equal(LicenseStatus.Locked, response.Nodes.First().Status);
    }

    [Fact]
    public async Task GetBoardState_WithUnlocks_CorrectStatusPerNode()
    {
        // Arrange
        var board = CreateTestBoard();
        var template = CreateTestTemplate();
        var itemInstanceId = Guid.NewGuid();

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);

        // One node unlocked at (0,0), another at (3,3) locked
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
            {
                new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = itemInstanceId, UnlockedAt = DateTimeOffset.UtcNow }
            }));

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0),
                CreateTestDefinition(code: "far_skill", positionX: 3, positionY: 3)
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardStateAsync(
            new BoardStateRequest { BoardId = TestBoardId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Nodes.Count);

        var unlockedNode = response.Nodes.First(n => n.Code == "start_skill");
        Assert.Equal(LicenseStatus.Unlocked, unlockedNode.Status);
        Assert.Equal(itemInstanceId, unlockedNode.ItemInstanceId);

        var lockedNode = response.Nodes.First(n => n.Code == "far_skill");
        Assert.Equal(LicenseStatus.Locked, lockedNode.Status);
        Assert.Null(lockedNode.ItemInstanceId);
    }

    [Fact]
    public async Task GetBoardState_StartingNodes_Unlockable()
    {
        // Arrange - empty board, starting node at (0,0) should be Unlockable
        var board = CreateTestBoard();
        var template = CreateTestTemplate(); // Starting node at (0,0)

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockBoardCache.Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(CreateTestCache());

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0)
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardStateAsync(
            new BoardStateRequest { BoardId = TestBoardId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Nodes);
        Assert.Equal(LicenseStatus.Unlockable, response.Nodes.First().Status);
    }

    [Fact]
    public async Task GetBoardState_AdjacentToUnlocked_Unlockable()
    {
        // Arrange - (0,0) unlocked, (0,1) should be Unlockable (adjacent), (3,3) should be Locked
        var board = CreateTestBoard();
        var template = CreateTestTemplate();

        _mockBoardStore.Setup(s => s.GetAsync($"board:{TestBoardId}", It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _mockTemplateStore.Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>())).ReturnsAsync(template);

        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{TestBoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(TestBoardId, new List<UnlockedLicenseEntry>
            {
                new UnlockedLicenseEntry { Code = "start_skill", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
            }));

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>
            {
                CreateTestDefinition(code: "start_skill", positionX: 0, positionY: 0),
                CreateTestDefinition(code: "adjacent_skill", positionX: 0, positionY: 1),
                CreateTestDefinition(code: "far_skill", positionX: 3, positionY: 3)
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.GetBoardStateAsync(
            new BoardStateRequest { BoardId = TestBoardId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Nodes.Count);

        Assert.Equal(LicenseStatus.Unlocked, response.Nodes.First(n => n.Code == "start_skill").Status);
        Assert.Equal(LicenseStatus.Unlockable, response.Nodes.First(n => n.Code == "adjacent_skill").Status);
        Assert.Equal(LicenseStatus.Locked, response.Nodes.First(n => n.Code == "far_skill").Status);
    }

    #endregion

    #region Cleanup Operations Tests

    [Fact]
    public async Task CleanupByOwner_CleansUpAllBoards()
    {
        // Arrange
        var board1 = CreateTestBoard(boardId: Guid.NewGuid(), containerId: Guid.NewGuid());
        var board2 = CreateTestBoard(boardId: Guid.NewGuid(), containerId: Guid.NewGuid());

        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { board1, board2 });

        var service = CreateService();
        var request = new CleanupByOwnerRequest { OwnerType = EntityType.Character, OwnerId = TestCharacterId };

        // Act
        var (status, response) = await service.CleanupByOwnerAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(EntityType.Character, response.OwnerType);
        Assert.Equal(TestCharacterId, response.OwnerId);
        Assert.Equal(2, response.BoardsDeleted);

        // Verify container cleanup for both boards
        _mockInventoryClient.Verify(
            c => c.DeleteContainerAsync(It.Is<DeleteContainerRequest>(r => r.ContainerId == board1.ContainerId), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockInventoryClient.Verify(
            c => c.DeleteContainerAsync(It.Is<DeleteContainerRequest>(r => r.ContainerId == board2.ContainerId), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify board records deleted (board key + uniqueness key per board)
        _mockBoardStore.Verify(
            s => s.DeleteAsync($"board:{board1.BoardId}", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockBoardStore.Verify(
            s => s.DeleteAsync($"board:{board2.BoardId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify cache invalidated for both boards
        _mockBoardCache.Verify(
            s => s.DeleteAsync($"cache:{board1.BoardId}", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockBoardCache.Verify(
            s => s.DeleteAsync($"cache:{board2.BoardId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupByOwner_NoBoards_ReturnsZeroDeleted()
    {
        // Arrange
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel>());

        var service = CreateService();
        var request = new CleanupByOwnerRequest { OwnerType = EntityType.Character, OwnerId = TestCharacterId };

        // Act
        var (status, response) = await service.CleanupByOwnerAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.BoardsDeleted);
    }

    #endregion

    #region Lock Behavior Tests

    [Fact]
    public async Task AddLicenseDefinition_LockFailure_ReturnsConflict()
    {
        // Arrange - lock acquisition fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        failedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.LicenseLock,
                $"tpl:{TestBoardTemplateId}",
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "test_skill",
            Position = new GridPosition { X = 1, Y = 1 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        var (status, response) = await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no save was attempted
        _mockDefinitionStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddLicenseDefinition_AcquiresTemplateLock()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemTemplateResponse { TemplateId = TestItemTemplateId });

        var service = CreateService();
        var request = new AddLicenseDefinitionRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Code = "test_skill",
            Position = new GridPosition { X = 1, Y = 1 },
            LpCost = 10,
            ItemTemplateId = TestItemTemplateId
        };

        // Act
        await service.AddLicenseDefinitionAsync(request, CancellationToken.None);

        // Assert - verify lock was acquired on the correct key
        _mockLockProvider.Verify(
            l => l.LockAsync(
                StateStoreDefinitions.LicenseLock,
                $"tpl:{TestBoardTemplateId}",
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveLicenseDefinition_LockFailure_ReturnsConflict()
    {
        // Arrange - lock acquisition fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        failedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.LicenseLock,
                $"tpl:{TestBoardTemplateId}",
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var service = CreateService();

        // Act
        var (status, response) = await service.RemoveLicenseDefinitionAsync(
            new RemoveLicenseDefinitionRequest
            {
                BoardTemplateId = TestBoardTemplateId,
                Code = "test_skill"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no delete was attempted
        _mockDefinitionStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveLicenseDefinition_AcquiresTemplateLock()
    {
        // Arrange
        var definition = CreateTestDefinition(code: "removable_skill", positionX: 1, positionY: 1);
        _mockDefinitionStore
            .Setup(s => s.GetAsync($"lic-def:{TestBoardTemplateId}:removable_skill", It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var service = CreateService();

        // Act
        await service.RemoveLicenseDefinitionAsync(
            new RemoveLicenseDefinitionRequest
            {
                BoardTemplateId = TestBoardTemplateId,
                Code = "removable_skill"
            },
            CancellationToken.None);

        // Assert - verify lock was acquired on the correct key
        _mockLockProvider.Verify(
            l => l.LockAsync(
                StateStoreDefinitions.LicenseLock,
                $"tpl:{TestBoardTemplateId}",
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Seed Tests

    [Fact]
    public async Task SeedBoardTemplate_ValidDefinitions_CreatesAll()
    {
        // Arrange
        var template = CreateTestTemplate(gridWidth: 5, gridHeight: 5);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var savedDefinitions = new List<LicenseDefinitionModel>();
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LicenseDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDefinitions.Add(m))
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new SeedBoardTemplateRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Definitions = new List<AddLicenseDefinitionRequest>
            {
                new AddLicenseDefinitionRequest { Code = "fire_1", Position = new GridPosition { X = 0, Y = 0 }, LpCost = 5, ItemTemplateId = TestItemTemplateId },
                new AddLicenseDefinitionRequest { Code = "fire_2", Position = new GridPosition { X = 1, Y = 0 }, LpCost = 10, ItemTemplateId = TestItemTemplateId },
                new AddLicenseDefinitionRequest { Code = "fire_3", Position = new GridPosition { X = 2, Y = 0 }, LpCost = 15, ItemTemplateId = TestItemTemplateId }
            }
        };

        // Act
        var (status, response) = await service.SeedBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.DefinitionsCreated);
        Assert.Equal(3, response.Definitions.Count);
        Assert.Equal(3, savedDefinitions.Count);

        Assert.Equal("fire_1", savedDefinitions[0].Code);
        Assert.Equal("fire_2", savedDefinitions[1].Code);
        Assert.Equal("fire_3", savedDefinitions[2].Code);
    }

    [Fact]
    public async Task SeedBoardTemplate_PrerequisiteResolution_LinksCorrectly()
    {
        // Arrange
        var template = CreateTestTemplate(gridWidth: 5, gridHeight: 5);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var savedDefinitions = new List<LicenseDefinitionModel>();
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LicenseDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDefinitions.Add(m))
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new SeedBoardTemplateRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Definitions = new List<AddLicenseDefinitionRequest>
            {
                new AddLicenseDefinitionRequest
                {
                    Code = "basic_fire",
                    Position = new GridPosition { X = 0, Y = 0 },
                    LpCost = 5,
                    ItemTemplateId = TestItemTemplateId
                },
                new AddLicenseDefinitionRequest
                {
                    Code = "advanced_fire",
                    Position = new GridPosition { X = 1, Y = 0 },
                    LpCost = 15,
                    ItemTemplateId = TestItemTemplateId,
                    Prerequisites = new List<string> { "basic_fire" }
                }
            }
        };

        // Act
        var (status, response) = await service.SeedBoardTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.DefinitionsCreated);

        var advancedDef = savedDefinitions.First(d => d.Code == "advanced_fire");
        Assert.NotNull(advancedDef.Prerequisites);
        Assert.Single(advancedDef.Prerequisites);
        Assert.Equal("basic_fire", advancedDef.Prerequisites[0]);
    }

    [Fact]
    public async Task SeedBoardTemplate_InvalidItemTemplate_SkipsDefinition()
    {
        // Arrange
        var template = CreateTestTemplate(gridWidth: 5, gridHeight: 5);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{TestBoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var validItemTemplateId = Guid.NewGuid();
        var invalidItemTemplateId = Guid.NewGuid();

        // Valid item template returns successfully
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(
                It.Is<GetItemTemplateRequest>(r => r.TemplateId == validItemTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemTemplateResponse { TemplateId = validItemTemplateId });

        // Invalid item template throws 404
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(
                It.Is<GetItemTemplateRequest>(r => r.TemplateId == invalidItemTemplateId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var savedDefinitions = new List<LicenseDefinitionModel>();
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LicenseDefinitionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, LicenseDefinitionModel, StateOptions?, CancellationToken>((_, m, _, _) => savedDefinitions.Add(m))
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new SeedBoardTemplateRequest
        {
            BoardTemplateId = TestBoardTemplateId,
            Definitions = new List<AddLicenseDefinitionRequest>
            {
                new AddLicenseDefinitionRequest { Code = "valid_skill", Position = new GridPosition { X = 0, Y = 0 }, LpCost = 5, ItemTemplateId = validItemTemplateId },
                new AddLicenseDefinitionRequest { Code = "invalid_skill", Position = new GridPosition { X = 1, Y = 0 }, LpCost = 10, ItemTemplateId = invalidItemTemplateId },
                new AddLicenseDefinitionRequest { Code = "another_valid", Position = new GridPosition { X = 2, Y = 0 }, LpCost = 15, ItemTemplateId = validItemTemplateId }
            }
        };

        // Act
        var (status, response) = await service.SeedBoardTemplateAsync(request, CancellationToken.None);

        // Assert - invalid_skill should be skipped, others created
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.DefinitionsCreated);
        Assert.Equal(2, savedDefinitions.Count);
        Assert.Equal("valid_skill", savedDefinitions[0].Code);
        Assert.Equal("another_valid", savedDefinitions[1].Code);
    }

    #endregion

    #region Board Clone Tests

    [Fact]
    public async Task CloneBoard_ValidRequest_ClonesAllLicenses()
    {
        // Arrange - source board with 3 unlocked licenses
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();
        var newContainerId = Guid.NewGuid();

        var def1 = CreateTestDefinition(code: "skill_a", positionX: 0, positionY: 0);
        var def2 = CreateTestDefinition(code: "skill_b", positionX: 1, positionY: 0);
        var def3 = CreateTestDefinition(code: "skill_c", positionX: 0, positionY: 1);
        var allDefinitions = new List<LicenseDefinitionModel> { def1, def2, def3 };

        var sourceCache = CreateTestCache(sourceBoard.BoardId, new List<UnlockedLicenseEntry>
        {
            new UnlockedLicenseEntry { Code = "skill_a", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedLicenseEntry { Code = "skill_b", PositionX = 1, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedLicenseEntry { Code = "skill_c", PositionX = 0, PositionY = 1, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });

        // Source board exists
        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);

        // Template exists
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Target character exists
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        // Definitions for the template
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allDefinitions);

        // Source board cache
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceCache);

        // Container creation
        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = newContainerId });

        // Item creation returns unique IDs
        var itemCallCount = 0;
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ItemInstanceResponse { InstanceId = Guid.NewGuid() })
            .Callback(() => itemCallCount++);

        // Capture saved board
        BoardInstanceModel? savedBoard = null;
        _mockBoardStore
            .Setup(s => s.SaveAsync(It.Is<string>(k => k.StartsWith("board:")), It.IsAny<BoardInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedBoard = m)
            .ReturnsAsync("etag");

        // Capture saved cache
        BoardCacheModel? savedCache = null;
        _mockBoardCache
            .Setup(s => s.SaveAsync(It.Is<string>(k => k.StartsWith("cache:")), It.IsAny<BoardCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BoardCacheModel, StateOptions?, CancellationToken>((_, m, _, _) => savedCache = m)
            .ReturnsAsync("etag");

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(sourceBoard.BoardId, response.SourceBoardId);
        Assert.Equal(EntityType.Character, response.TargetOwnerType);
        Assert.Equal(targetOwnerId, response.TargetOwnerId);
        Assert.Equal(newContainerId, response.TargetContainerId);
        Assert.Equal(3, response.LicensesCloned);
        Assert.Equal(3, itemCallCount);

        Assert.NotNull(savedBoard);
        Assert.Equal(EntityType.Character, savedBoard.OwnerType);
        Assert.Equal(targetOwnerId, savedBoard.OwnerId);
        Assert.Equal(newContainerId, savedBoard.ContainerId);
        Assert.Equal(template.BoardTemplateId, savedBoard.BoardTemplateId);

        Assert.NotNull(savedCache);
        Assert.Equal(3, savedCache.UnlockedPositions.Count);
    }

    [Fact]
    public async Task CloneBoard_SourceBoardNotFound_ReturnsNotFound()
    {
        // Arrange - source board does not exist
        _mockBoardStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BoardInstanceModel?)null);

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = Guid.NewGuid(),
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = Guid.NewGuid()
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CloneBoard_TargetAlreadyHasBoard_ReturnsConflict()
    {
        // Arrange
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        // Target already has a board for this template
        _mockBoardStore
            .Setup(s => s.GetAsync($"board-owner:character:{targetOwnerId}:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestBoard(boardId: Guid.NewGuid(), ownerId: targetOwnerId));

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no container was created
        _mockInventoryClient.Verify(
            c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CloneBoard_MaxBoardsExceeded_ReturnsConflict()
    {
        // Arrange
        Configuration.MaxBoardsPerOwner = 1;
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        // Target already at max boards (different template)
        _mockBoardStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<BoardInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BoardInstanceModel> { CreateTestBoard(boardId: Guid.NewGuid(), boardTemplateId: Guid.NewGuid()) });

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CloneBoard_OwnerTypeNotAllowed_ReturnsBadRequest()
    {
        // Arrange - template only allows "character", request targets "account"
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate(allowedOwnerTypes: new List<EntityType> { EntityType.Character });

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Account,
                TargetOwnerId = Guid.NewGuid()
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CloneBoard_CharacterNotFound_ReturnsNotFound()
    {
        // Arrange
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = Guid.NewGuid()
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CloneBoard_EmptySourceBoard_ClonesEmptyBoard()
    {
        // Arrange - source board has 0 unlocked licenses
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();
        var newContainerId = Guid.NewGuid();

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        // Empty definitions list and empty cache
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel>());
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(sourceBoard.BoardId));

        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = newContainerId });

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.LicensesCloned);
        Assert.Equal(newContainerId, response.TargetContainerId);

        // Verify no items were created
        _mockItemClient.Verify(
            c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // CloneBoard_InvalidOwnerTypeFormat_ReturnsBadRequest test removed:
    // TargetOwnerType is now EntityType enum — colon-separated strings are
    // impossible at the type level, making this validation test untestable.

    [Fact]
    public async Task CloneBoard_PublishesBoardCreatedAndClonedEvents()
    {
        // Arrange
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();
        var newContainerId = Guid.NewGuid();
        var def1 = CreateTestDefinition(code: "skill_x", positionX: 0, positionY: 0);

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel> { def1 });
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(sourceBoard.BoardId, new List<UnlockedLicenseEntry>
            {
                new UnlockedLicenseEntry { Code = "skill_x", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
            }));

        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = newContainerId });
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = Guid.NewGuid() });

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify both events published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board.created", It.IsAny<LicenseBoardCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board.cloned", It.IsAny<LicenseBoardClonedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify character reference registered
        _mockResourceClient.Verify(
            m => m.RegisterReferenceAsync(It.IsAny<RegisterReferenceRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CloneBoard_ItemCreationFails_CleansUpAndReturnsError()
    {
        // Arrange
        var sourceBoard = CreateTestBoard();
        var template = CreateTestTemplate();
        var targetOwnerId = Guid.NewGuid();
        var newContainerId = Guid.NewGuid();

        var def1 = CreateTestDefinition(code: "skill_a", positionX: 0, positionY: 0);
        var def2 = CreateTestDefinition(code: "skill_b", positionX: 1, positionY: 0);

        _mockBoardStore
            .Setup(s => s.GetAsync($"board:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBoard);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"board-tpl:{template.BoardTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = targetOwnerId, RealmId = TestRealmId });

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<LicenseDefinitionModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LicenseDefinitionModel> { def1, def2 });
        _mockBoardCache
            .Setup(s => s.GetAsync($"cache:{sourceBoard.BoardId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache(sourceBoard.BoardId, new List<UnlockedLicenseEntry>
            {
                new UnlockedLicenseEntry { Code = "skill_a", PositionX = 0, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
                new UnlockedLicenseEntry { Code = "skill_b", PositionX = 1, PositionY = 0, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
            }));

        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = newContainerId });

        // First item succeeds, second fails
        var itemCallSequence = 0;
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                itemCallSequence++;
                if (itemCallSequence >= 2)
                    throw new ApiException("Item creation failed", 500, null, null, null);
                return new ItemInstanceResponse { InstanceId = Guid.NewGuid() };
            });

        var service = CreateService();

        // Act
        var (status, response) = await service.CloneBoardAsync(
            new CloneBoardRequest
            {
                SourceBoardId = sourceBoard.BoardId,
                TargetOwnerType = EntityType.Character,
                TargetOwnerId = targetOwnerId
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        // Verify container was cleaned up
        _mockInventoryClient.Verify(
            c => c.DeleteContainerAsync(It.Is<DeleteContainerRequest>(r => r.ContainerId == newContainerId), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify no board records were saved
        _mockBoardStore.Verify(
            s => s.SaveAsync(It.Is<string>(k => k.StartsWith("board:")), It.IsAny<BoardInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify no events published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync("license-board.created", It.IsAny<LicenseBoardCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
