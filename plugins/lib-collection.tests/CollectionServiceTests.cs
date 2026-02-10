using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Collection;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Collection.Tests;

public class CollectionServiceTests : ServiceTestBase<CollectionServiceConfiguration>
{
    // Infrastructure mocks
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<CollectionService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    // Store mocks
    private readonly Mock<IQueryableStateStore<EntryTemplateModel>> _mockTemplateStore;
    private readonly Mock<IQueryableStateStore<CollectionInstanceModel>> _mockCollectionStore;
    private readonly Mock<IQueryableStateStore<AreaMusicConfigModel>> _mockAreaMusicStore;
    private readonly Mock<IStateStore<CollectionCacheModel>> _mockCollectionCache;

    // Client mocks
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<IItemClient> _mockItemClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;

    // Lock response mock
    private readonly Mock<ILockResponse> _mockLockResponse;

    // Shared test IDs
    private static readonly Guid TestGameServiceId = Guid.NewGuid();
    private static readonly Guid TestEntryTemplateId = Guid.NewGuid();
    private static readonly Guid TestItemTemplateId = Guid.NewGuid();
    private static readonly Guid TestCollectionId = Guid.NewGuid();
    private static readonly Guid TestOwnerId = Guid.NewGuid();
    private static readonly Guid TestContainerId = Guid.NewGuid();
    private static readonly Guid TestAreaConfigId = Guid.NewGuid();

    public CollectionServiceTests()
    {
        // Initialize infrastructure mocks
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<CollectionService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        // Initialize store mocks
        _mockTemplateStore = new Mock<IQueryableStateStore<EntryTemplateModel>>();
        _mockCollectionStore = new Mock<IQueryableStateStore<CollectionInstanceModel>>();
        _mockAreaMusicStore = new Mock<IQueryableStateStore<AreaMusicConfigModel>>();
        _mockCollectionCache = new Mock<IStateStore<CollectionCacheModel>>();

        // Initialize client mocks
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockItemClient = new Mock<IItemClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<EntryTemplateModel>(StateStoreDefinitions.CollectionEntryTemplates))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<CollectionInstanceModel>(StateStoreDefinitions.CollectionInstances))
            .Returns(_mockCollectionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<AreaMusicConfigModel>(StateStoreDefinitions.CollectionAreaMusicConfigs))
            .Returns(_mockAreaMusicStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CollectionCacheModel>(StateStoreDefinitions.CollectionCache))
            .Returns(_mockCollectionCache.Object);

        // Default save behavior
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EntryTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockCollectionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CollectionInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockAreaMusicStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AreaMusicConfigModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockCollectionCache
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CollectionCacheModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default delete behavior
        _mockTemplateStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCollectionStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCollectionCache
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockAreaMusicStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default query behavior (empty results)
        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntryTemplateModel>());
        _mockCollectionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CollectionInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionInstanceModel>());
        _mockAreaMusicStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<AreaMusicConfigModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AreaMusicConfigModel>());

        // Default publish behavior (both overloads)
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default lock behavior (success)
        _mockLockResponse = new Mock<ILockResponse>();
        _mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLockResponse.Object);
    }

    #region Helpers

    private CollectionService CreateService() => new CollectionService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLogger.Object,
        Configuration,
        _mockEventConsumer.Object,
        _mockInventoryClient.Object,
        _mockItemClient.Object,
        _mockGameServiceClient.Object,
        _mockLockProvider.Object);

    private static EntryTemplateModel CreateTestTemplate(
        Guid? entryTemplateId = null,
        string code = "boss_dragon",
        CollectionType collectionType = CollectionType.Bestiary,
        Guid? gameServiceId = null,
        string displayName = "Dragon Boss",
        string? category = "boss",
        Guid? itemTemplateId = null,
        List<DiscoveryLevelEntry>? discoveryLevels = null,
        List<string>? themes = null)
    {
        return new EntryTemplateModel
        {
            EntryTemplateId = entryTemplateId ?? TestEntryTemplateId,
            Code = code,
            CollectionType = collectionType,
            GameServiceId = gameServiceId ?? TestGameServiceId,
            DisplayName = displayName,
            Category = category,
            Tags = new List<string> { "monster", "dragon" },
            AssetId = "asset_dragon",
            ItemTemplateId = itemTemplateId ?? TestItemTemplateId,
            DiscoveryLevels = discoveryLevels,
            Themes = themes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static CollectionInstanceModel CreateTestCollection(
        Guid? collectionId = null,
        Guid? ownerId = null,
        string ownerType = "character",
        CollectionType collectionType = CollectionType.Bestiary,
        Guid? gameServiceId = null,
        Guid? containerId = null)
    {
        return new CollectionInstanceModel
        {
            CollectionId = collectionId ?? TestCollectionId,
            OwnerId = ownerId ?? TestOwnerId,
            OwnerType = ownerType,
            CollectionType = collectionType,
            GameServiceId = gameServiceId ?? TestGameServiceId,
            ContainerId = containerId ?? TestContainerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static CollectionCacheModel CreateTestCache(
        Guid? collectionId = null,
        List<UnlockedEntryRecord>? unlockedEntries = null)
    {
        return new CollectionCacheModel
        {
            CollectionId = collectionId ?? TestCollectionId,
            UnlockedEntries = unlockedEntries ?? new List<UnlockedEntryRecord>(),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private static AreaMusicConfigModel CreateTestAreaConfig(
        Guid? areaConfigId = null,
        string areaCode = "enchanted_forest",
        Guid? gameServiceId = null,
        List<string>? themes = null,
        string defaultTrackCode = "forest_ambient")
    {
        return new AreaMusicConfigModel
        {
            AreaConfigId = areaConfigId ?? TestAreaConfigId,
            AreaCode = areaCode,
            GameServiceId = gameServiceId ?? TestGameServiceId,
            Themes = themes ?? new List<string> { "forest", "peaceful" },
            DefaultTrackCode = defaultTrackCode,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Sets up mocks needed for a successful grant operation.
    /// </summary>
    private Guid SetupGrantScenario(
        CollectionInstanceModel collection,
        EntryTemplateModel template,
        CollectionCacheModel? cache = null)
    {
        var itemInstanceId = Guid.NewGuid();

        // Collection exists by owner key
        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{collection.OwnerId}:{collection.OwnerType}:{collection.GameServiceId}:{collection.CollectionType}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        // Template exists by code key
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{template.GameServiceId}:{template.CollectionType}:{template.Code}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Cache exists
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{collection.CollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache ?? CreateTestCache(collection.CollectionId));

        // ETag-based cache update
        _mockCollectionCache
            .Setup(s => s.GetWithETagAsync($"cache:{collection.CollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((cache ?? CreateTestCache(collection.CollectionId), "etag-1"));

        _mockCollectionCache
            .Setup(s => s.TrySaveAsync($"cache:{collection.CollectionId}", It.IsAny<CollectionCacheModel>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Templates for milestone check
        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntryTemplateModel> { template });

        // Item creation succeeds
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = itemInstanceId });

        return itemInstanceId;
    }

    #endregion

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void CollectionService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CollectionService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CollectionServiceConfiguration_CanBeInstantiated()
    {
        var config = new CollectionServiceConfiguration();

        Assert.NotNull(config);
        Assert.Equal(20, config.MaxCollectionsPerOwner);
        Assert.Equal(500, config.MaxEntriesPerCollection);
        Assert.Equal(30, config.LockTimeoutSeconds);
        Assert.Equal(300, config.CollectionCacheTtlSeconds);
        Assert.Equal(20, config.DefaultPageSize);
        Assert.Equal(3, config.MaxConcurrencyRetries);
    }

    #endregion

    #region Entry Template CRUD Tests

    [Fact]
    public async Task CreateEntryTemplate_ValidRequest_SavesAndPublishesEvent()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemTemplateResponse { TemplateId = TestItemTemplateId });

        EntryTemplateModel? savedTemplate = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EntryTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EntryTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedTemplate = m)
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new CreateEntryTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Code = "boss_dragon",
            CollectionType = CollectionType.Bestiary,
            DisplayName = "Dragon Boss",
            Category = "boss",
            Tags = new List<string> { "monster", "dragon" },
            ItemTemplateId = TestItemTemplateId,
            HideWhenLocked = false
        };

        // Act
        var (status, response) = await service.CreateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("boss_dragon", response.Code);
        Assert.Equal("Dragon Boss", response.DisplayName);
        Assert.Equal(CollectionType.Bestiary, response.CollectionType);
        Assert.Equal(TestGameServiceId, response.GameServiceId);
        Assert.Equal("boss", response.Category);
        Assert.False(response.HideWhenLocked);

        Assert.NotNull(savedTemplate);
        Assert.Equal("boss_dragon", savedTemplate.Code);
        Assert.Equal(TestGameServiceId, savedTemplate.GameServiceId);

        // Verify two saves (by ID and by code key)
        _mockTemplateStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EntryTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection-entry-template.created", It.IsAny<CollectionEntryTemplateCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateEntryTemplate_InvalidGameServiceId_ReturnsNotFound()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();
        var request = new CreateEntryTemplateRequest
        {
            GameServiceId = Guid.NewGuid(),
            Code = "test",
            CollectionType = CollectionType.Bestiary,
            DisplayName = "Test",
            ItemTemplateId = TestItemTemplateId,
            HideWhenLocked = false
        };

        // Act
        var (status, response) = await service.CreateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEntryTemplate_InvalidItemTemplateId_ReturnsNotFound()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();
        var request = new CreateEntryTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Code = "test",
            CollectionType = CollectionType.Bestiary,
            DisplayName = "Test",
            ItemTemplateId = Guid.NewGuid(),
            HideWhenLocked = false
        };

        // Act
        var (status, response) = await service.CreateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEntryTemplate_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemTemplateResponse { TemplateId = TestItemTemplateId });

        // Code lookup returns existing template
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        var service = CreateService();
        var request = new CreateEntryTemplateRequest
        {
            GameServiceId = TestGameServiceId,
            Code = "boss_dragon",
            CollectionType = CollectionType.Bestiary,
            DisplayName = "Dragon Boss",
            ItemTemplateId = TestItemTemplateId,
            HideWhenLocked = false
        };

        // Act
        var (status, response) = await service.CreateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetEntryTemplate_Exists_ReturnsOk()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestEntryTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetEntryTemplateAsync(
            new GetEntryTemplateRequest { EntryTemplateId = TestEntryTemplateId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestEntryTemplateId, response.EntryTemplateId);
        Assert.Equal("boss_dragon", response.Code);
    }

    [Fact]
    public async Task GetEntryTemplate_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.GetEntryTemplateAsync(
            new GetEntryTemplateRequest { EntryTemplateId = Guid.NewGuid() },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListEntryTemplates_ReturnsFilteredResults()
    {
        // Arrange
        var templates = new List<EntryTemplateModel>
        {
            CreateTestTemplate(code: "boss_dragon", category: "boss"),
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "wolf_pack", category: "ambient"),
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "boss_lich", category: "boss")
        };

        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var service = CreateService();
        var request = new ListEntryTemplatesRequest
        {
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            Category = "boss"
        };

        // Act
        var (status, response) = await service.ListEntryTemplatesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Templates.Count);
        Assert.All(response.Templates, t => Assert.Equal("boss", t.Category));
    }

    [Fact]
    public async Task UpdateEntryTemplate_ValidRequest_UpdatesFieldsAndPublishesEvent()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestEntryTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();
        var request = new UpdateEntryTemplateRequest
        {
            EntryTemplateId = TestEntryTemplateId,
            DisplayName = "Updated Dragon",
            Category = "elite_boss"
        };

        // Act
        var (status, response) = await service.UpdateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Dragon", response.DisplayName);
        Assert.Equal("elite_boss", response.Category);
        Assert.NotNull(response.UpdatedAt);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection-entry-template.updated", It.IsAny<CollectionEntryTemplateUpdatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateEntryTemplate_NoFieldsChanged_SkipsPublishReturnsOk()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestEntryTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();
        var request = new UpdateEntryTemplateRequest
        {
            EntryTemplateId = TestEntryTemplateId
            // No fields specified
        };

        // Act
        var (status, response) = await service.UpdateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection-entry-template.updated", It.IsAny<CollectionEntryTemplateUpdatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateEntryTemplate_LockFails_ReturnsConflict()
    {
        // Arrange
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        failedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var service = CreateService();
        var request = new UpdateEntryTemplateRequest
        {
            EntryTemplateId = TestEntryTemplateId,
            DisplayName = "Won't update"
        };

        // Act
        var (status, response) = await service.UpdateEntryTemplateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteEntryTemplate_Exists_DeletesBothKeysAndPublishesEvent()
    {
        // Arrange
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestEntryTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteEntryTemplateAsync(
            new DeleteEntryTemplateRequest { EntryTemplateId = TestEntryTemplateId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify both keys deleted (by ID and by code)
        _mockTemplateStore.Verify(
            s => s.DeleteAsync($"tpl:{TestEntryTemplateId}", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockTemplateStore.Verify(
            s => s.DeleteAsync($"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon", It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection-entry-template.deleted", It.IsAny<CollectionEntryTemplateDeletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SeedEntryTemplates_CreatesNewAndSkipsDuplicates()
    {
        // Arrange
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:existing_code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(code: "existing_code"));
        // "new_code" returns null by default (not found)

        var service = CreateService();
        var request = new SeedEntryTemplatesRequest
        {
            Templates = new List<CreateEntryTemplateRequest>
            {
                new CreateEntryTemplateRequest
                {
                    GameServiceId = TestGameServiceId,
                    Code = "existing_code",
                    CollectionType = CollectionType.Bestiary,
                    DisplayName = "Existing",
                    ItemTemplateId = TestItemTemplateId,
                    HideWhenLocked = false
                },
                new CreateEntryTemplateRequest
                {
                    GameServiceId = TestGameServiceId,
                    Code = "new_code",
                    CollectionType = CollectionType.Bestiary,
                    DisplayName = "New Entry",
                    ItemTemplateId = TestItemTemplateId,
                    HideWhenLocked = false
                }
            }
        };

        // Act
        var (status, response) = await service.SeedEntryTemplatesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);
        Assert.Equal(1, response.Skipped);
    }

    #endregion

    #region Collection Instance Tests

    [Fact]
    public async Task CreateCollection_ValidRequest_CreatesContainerAndSavesInstance()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });
        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = TestContainerId });

        CollectionInstanceModel? savedInstance = null;
        _mockCollectionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CollectionInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CollectionInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag");

        var service = CreateService();
        var request = new CreateCollectionRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            CollectionType = CollectionType.Bestiary,
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.CreateCollectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestOwnerId, response.OwnerId);
        Assert.Equal("character", response.OwnerType);
        Assert.Equal(CollectionType.Bestiary, response.CollectionType);
        Assert.Equal(TestContainerId, response.ContainerId);
        Assert.Equal(0, response.EntryCount);

        Assert.NotNull(savedInstance);
        Assert.Equal(TestOwnerId, savedInstance.OwnerId);

        // Verify container created with correct owner type mapping
        _mockInventoryClient.Verify(
            c => c.CreateContainerAsync(
                It.Is<CreateContainerRequest>(r =>
                    r.OwnerId == TestOwnerId &&
                    r.OwnerType == ContainerOwnerType.Character &&
                    r.ConstraintModel == ContainerConstraintModel.Unlimited),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify saved twice (by ID and by owner key)
        _mockCollectionStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CollectionInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection.created", It.IsAny<CollectionCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateCollection_InvalidOwnerType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateCollectionRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "invalid:type",
            CollectionType = CollectionType.Bestiary,
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.CreateCollectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCollection_UnmappableOwnerType_ReturnsBadRequest()
    {
        // Arrange - "npc" is valid syntax but has no ContainerOwnerType mapping
        var service = CreateService();
        var request = new CreateCollectionRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "npc",
            CollectionType = CollectionType.Bestiary,
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.CreateCollectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCollection_Duplicate_ReturnsConflict()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });

        // Owner key lookup returns existing collection
        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCollection());

        var service = CreateService();
        var request = new CreateCollectionRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            CollectionType = CollectionType.Bestiary,
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.CreateCollectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCollection_MaxLimitReached_ReturnsConflict()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });

        // Return 20 collections (= max)
        var existingCollections = Enumerable.Range(0, 20)
            .Select(_ => CreateTestCollection(collectionId: Guid.NewGuid()))
            .ToList();
        _mockCollectionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CollectionInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCollections);

        var service = CreateService();
        var request = new CreateCollectionRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            CollectionType = CollectionType.SceneArchive,
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.CreateCollectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCollection_Exists_ReturnsWithEntryCount()
    {
        // Arrange
        var collection = CreateTestCollection();
        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord { Code = "entry1", EntryTemplateId = Guid.NewGuid(), ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedEntryRecord { Code = "entry2", EntryTemplateId = Guid.NewGuid(), ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetCollectionAsync(
            new GetCollectionRequest { CollectionId = TestCollectionId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestCollectionId, response.CollectionId);
        Assert.Equal(2, response.EntryCount);
    }

    [Fact]
    public async Task DeleteCollection_Exists_DeletesContainerCacheAndInstance()
    {
        // Arrange
        var collection = CreateTestCollection();
        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        _mockInventoryClient
            .Setup(c => c.DeleteContainerAsync(It.IsAny<DeleteContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteContainerResponse());

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteCollectionAsync(
            new DeleteCollectionRequest { CollectionId = TestCollectionId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.EntryCount);

        // Verify container deleted
        _mockInventoryClient.Verify(
            c => c.DeleteContainerAsync(
                It.Is<DeleteContainerRequest>(r => r.ContainerId == TestContainerId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify cache deleted
        _mockCollectionCache.Verify(
            s => s.DeleteAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify both collection keys deleted
        _mockCollectionStore.Verify(
            s => s.DeleteAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()),
            Times.Once);
        _mockCollectionStore.Verify(
            s => s.DeleteAsync($"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}", It.IsAny<CancellationToken>()),
            Times.Once);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection.deleted", It.IsAny<CollectionDeletedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteCollection_ContainerAlreadyDeleted_StillSucceeds()
    {
        // Arrange
        var collection = CreateTestCollection();
        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        _mockInventoryClient
            .Setup(c => c.DeleteContainerAsync(It.IsAny<DeleteContainerRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();

        // Act
        var (status, response) = await service.DeleteCollectionAsync(
            new DeleteCollectionRequest { CollectionId = TestCollectionId },
            CancellationToken.None);

        // Assert - succeeds even though container already deleted
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ListCollections_FiltersByOwnerAndOptionalGameService()
    {
        // Arrange
        var collections = new List<CollectionInstanceModel>
        {
            CreateTestCollection(collectionId: Guid.NewGuid(), collectionType: CollectionType.Bestiary),
            CreateTestCollection(collectionId: Guid.NewGuid(), collectionType: CollectionType.MusicLibrary, gameServiceId: Guid.NewGuid())
        };

        _mockCollectionStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CollectionInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var service = CreateService();
        var request = new ListCollectionsRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId
        };

        // Act
        var (status, response) = await service.ListCollectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Only one collection matches the GameServiceId filter
        Assert.Single(response.Collections);
    }

    #endregion

    #region Grant Entry Tests

    [Fact]
    public async Task GrantEntry_ValidRequest_CreatesItemAndUpdatesCache()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate();
        var itemInstanceId = SetupGrantScenario(collection, template);

        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "boss_dragon"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(template.EntryTemplateId, response.EntryTemplateId);
        Assert.Equal("boss_dragon", response.Code);
        Assert.Equal(collection.CollectionId, response.CollectionId);
        Assert.Equal(itemInstanceId, response.ItemInstanceId);
        Assert.False(response.AlreadyUnlocked);

        // Verify item creation
        _mockItemClient.Verify(
            c => c.CreateItemInstanceAsync(
                It.Is<CreateItemInstanceRequest>(r =>
                    r.TemplateId == TestItemTemplateId &&
                    r.ContainerId == TestContainerId &&
                    r.Quantity == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify entry-unlocked event
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(CollectionTopics.EntryUnlocked, It.IsAny<CollectionEntryUnlockedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantEntry_AlreadyUnlocked_ReturnsIdempotentResponse()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate();
        var existingItemInstanceId = Guid.NewGuid();
        var existingUnlockTime = DateTimeOffset.UtcNow.AddHours(-1);

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord
            {
                Code = "boss_dragon",
                EntryTemplateId = TestEntryTemplateId,
                ItemInstanceId = existingItemInstanceId,
                UnlockedAt = existingUnlockTime
            }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "boss_dragon"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.AlreadyUnlocked);
        Assert.Equal(existingItemInstanceId, response.ItemInstanceId);
        Assert.Equal(existingUnlockTime, response.UnlockedAt);

        // Should NOT create a new item
        _mockItemClient.Verify(
            c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantEntry_TemplateNotFound_ReturnsNotFoundAndPublishesFailEvent()
    {
        // Arrange - no template exists for the given code
        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "nonexistent_code"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                It.Is<CollectionEntryGrantFailedEvent>(e => e.Reason == GrantFailureReason.EntryNotFound),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantEntry_MaxEntriesReached_ReturnsConflictAndPublishesFailEvent()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate();

        // Create cache at max entries limit
        var maxEntries = Configuration.MaxEntriesPerCollection;
        var entries = Enumerable.Range(0, maxEntries)
            .Select(i => new UnlockedEntryRecord
            {
                Code = $"entry_{i}",
                EntryTemplateId = Guid.NewGuid(),
                ItemInstanceId = Guid.NewGuid(),
                UnlockedAt = DateTimeOffset.UtcNow
            }).ToList();
        var cache = CreateTestCache(unlockedEntries: entries);

        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "boss_dragon"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                It.Is<CollectionEntryGrantFailedEvent>(e => e.Reason == GrantFailureReason.MaxEntriesReached),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantEntry_AutoCreatesCollectionIfMissing()
    {
        // Arrange
        var template = CreateTestTemplate();

        // Template found by code
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // No existing collection (returns null)
        // Container creation for auto-create
        _mockInventoryClient
            .Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerResponse { ContainerId = TestContainerId });

        // Item creation succeeds
        var itemInstanceId = Guid.NewGuid();
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = itemInstanceId });

        // Cache operations
        _mockCollectionCache
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((CollectionCacheModel?)null, (string?)null));

        // Template query for milestones
        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntryTemplateModel> { template });

        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "boss_dragon"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.AlreadyUnlocked);

        // Verify container was created (auto-create)
        _mockInventoryClient.Verify(
            c => c.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify collection.created event published (from auto-create)
        _mockMessageBus.Verify(
            m => m.TryPublishAsync("collection.created", It.IsAny<CollectionCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantEntry_ItemCreationFails_ReturnsInternalServerError()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate();

        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCache());

        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Internal error", 500, null, null, null));

        var service = CreateService();
        var request = new GrantEntryRequest
        {
            OwnerId = TestOwnerId,
            OwnerType = "character",
            GameServiceId = TestGameServiceId,
            CollectionType = CollectionType.Bestiary,
            EntryCode = "boss_dragon"
        };

        // Act
        var (status, response) = await service.GrantEntryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                CollectionTopics.EntryGrantFailed,
                It.Is<CollectionEntryGrantFailedEvent>(e => e.Reason == GrantFailureReason.ItemCreationFailed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region HasEntry Tests

    [Fact]
    public async Task HasEntry_EntryExists_ReturnsTrue()
    {
        // Arrange
        var collection = CreateTestCollection();
        var unlockTime = DateTimeOffset.UtcNow.AddHours(-2);
        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord { Code = "boss_dragon", EntryTemplateId = TestEntryTemplateId, ItemInstanceId = Guid.NewGuid(), UnlockedAt = unlockTime }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.HasEntryAsync(
            new HasEntryRequest
            {
                OwnerId = TestOwnerId,
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                CollectionType = CollectionType.Bestiary,
                EntryCode = "boss_dragon"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasEntry);
        Assert.Equal(unlockTime, response.UnlockedAt);
    }

    [Fact]
    public async Task HasEntry_NoCollection_ReturnsFalse()
    {
        // Arrange - no collection exists for this owner
        var service = CreateService();

        // Act
        var (status, response) = await service.HasEntryAsync(
            new HasEntryRequest
            {
                OwnerId = Guid.NewGuid(),
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                CollectionType = CollectionType.Bestiary,
                EntryCode = "boss_dragon"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasEntry);
        Assert.Null(response.UnlockedAt);
    }

    #endregion

    #region Completion Stats Tests

    [Fact]
    public async Task GetCompletionStats_WithProgress_ReturnsCorrectPercentages()
    {
        // Arrange
        var templates = new List<EntryTemplateModel>
        {
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "boss1", category: "boss"),
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "boss2", category: "boss"),
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "ambient1", category: "ambient"),
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "ambient2", category: "ambient")
        };

        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var collection = CreateTestCollection();
        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.Bestiary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord { Code = "boss1", EntryTemplateId = templates[0].EntryTemplateId, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedEntryRecord { Code = "ambient1", EntryTemplateId = templates[2].EntryTemplateId, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetCompletionStatsAsync(
            new GetCompletionStatsRequest
            {
                OwnerId = TestOwnerId,
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                CollectionType = CollectionType.Bestiary
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(4, response.TotalEntries);
        Assert.Equal(2, response.UnlockedEntries);
        Assert.Equal(50.0, response.CompletionPercentage);

        Assert.NotNull(response.ByCategory);
        Assert.Equal(2, response.ByCategory.Count);
        Assert.Equal(50.0, response.ByCategory["boss"].Percentage);
        Assert.Equal(50.0, response.ByCategory["ambient"].Percentage);
    }

    [Fact]
    public async Task GetCompletionStats_NoCollection_ReturnsZeroProgress()
    {
        // Arrange
        var templates = new List<EntryTemplateModel>
        {
            CreateTestTemplate(entryTemplateId: Guid.NewGuid(), code: "boss1")
        };

        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        // No collection exists
        var service = CreateService();

        // Act
        var (status, response) = await service.GetCompletionStatsAsync(
            new GetCompletionStatsRequest
            {
                OwnerId = Guid.NewGuid(),
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                CollectionType = CollectionType.Bestiary
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalEntries);
        Assert.Equal(0, response.UnlockedEntries);
        Assert.Equal(0.0, response.CompletionPercentage);
    }

    #endregion

    #region Music Operations Tests

    [Fact]
    public async Task SetAreaMusicConfig_NewConfig_CreatesAndReturns()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });

        var defaultTrack = CreateTestTemplate(
            code: "forest_ambient",
            collectionType: CollectionType.MusicLibrary,
            themes: new List<string> { "forest", "peaceful" });
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.MusicLibrary}:forest_ambient",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultTrack);

        var service = CreateService();
        var request = new SetAreaMusicConfigRequest
        {
            GameServiceId = TestGameServiceId,
            AreaCode = "enchanted_forest",
            Themes = new List<string> { "forest", "peaceful", "magical" },
            DefaultTrackCode = "forest_ambient"
        };

        // Act
        var (status, response) = await service.SetAreaMusicConfigAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("enchanted_forest", response.AreaCode);
        Assert.Equal(3, response.Themes.Count);
        Assert.Equal("forest_ambient", response.DefaultTrackCode);

        // Verify saved twice (by ID and by code key)
        _mockAreaMusicStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AreaMusicConfigModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SetAreaMusicConfig_ExistingConfig_Updates()
    {
        // Arrange
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = TestGameServiceId });

        var defaultTrack = CreateTestTemplate(
            code: "forest_ambient",
            collectionType: CollectionType.MusicLibrary);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.MusicLibrary}:forest_ambient",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultTrack);

        // Existing config
        var existingConfig = CreateTestAreaConfig();
        _mockAreaMusicStore
            .Setup(s => s.GetAsync(
                $"amc:{TestGameServiceId}:enchanted_forest",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingConfig);

        var service = CreateService();
        var request = new SetAreaMusicConfigRequest
        {
            GameServiceId = TestGameServiceId,
            AreaCode = "enchanted_forest",
            Themes = new List<string> { "dark", "eerie" },
            DefaultTrackCode = "forest_ambient"
        };

        // Act
        var (status, response) = await service.SetAreaMusicConfigAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Themes.Count);
        Assert.Contains("dark", response.Themes);
        Assert.NotNull(response.UpdatedAt);
    }

    [Fact]
    public async Task GetAreaMusicConfig_Exists_ReturnsOk()
    {
        // Arrange
        var config = CreateTestAreaConfig();
        _mockAreaMusicStore
            .Setup(s => s.GetAsync(
                $"amc:{TestGameServiceId}:enchanted_forest",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var service = CreateService();

        // Act
        var (status, response) = await service.GetAreaMusicConfigAsync(
            new GetAreaMusicConfigRequest { GameServiceId = TestGameServiceId, AreaCode = "enchanted_forest" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("enchanted_forest", response.AreaCode);
    }

    [Fact]
    public async Task SelectTrackForArea_NoAreaConfig_ReturnsNotFound()
    {
        // Arrange - no area config
        var service = CreateService();

        // Act
        var (status, response) = await service.SelectTrackForAreaAsync(
            new SelectTrackForAreaRequest
            {
                OwnerId = TestOwnerId,
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                AreaCode = "unknown_area"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SelectTrackForArea_NoMusicCollection_ReturnsDefaultTrack()
    {
        // Arrange
        var areaConfig = CreateTestAreaConfig();
        _mockAreaMusicStore
            .Setup(s => s.GetAsync(
                $"amc:{TestGameServiceId}:enchanted_forest",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(areaConfig);

        // No music collection for this owner

        var defaultTrack = CreateTestTemplate(
            code: "forest_ambient",
            collectionType: CollectionType.MusicLibrary,
            displayName: "Forest Ambience",
            themes: new List<string> { "forest" });
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.MusicLibrary}:forest_ambient",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultTrack);

        var service = CreateService();

        // Act
        var (status, response) = await service.SelectTrackForAreaAsync(
            new SelectTrackForAreaRequest
            {
                OwnerId = TestOwnerId,
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                AreaCode = "enchanted_forest"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("forest_ambient", response.TrackCode);
        Assert.Equal("Forest Ambience", response.DisplayName);
        Assert.Empty(response.MatchedThemes);
    }

    [Fact]
    public async Task SelectTrackForArea_WithMatchingTracks_SelectsWeightedCandidate()
    {
        // Arrange
        var areaConfig = CreateTestAreaConfig(themes: new List<string> { "forest", "peaceful", "magical" });
        _mockAreaMusicStore
            .Setup(s => s.GetAsync(
                $"amc:{TestGameServiceId}:enchanted_forest",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(areaConfig);

        var collection = CreateTestCollection(collectionType: CollectionType.MusicLibrary);
        _mockCollectionStore
            .Setup(s => s.GetAsync(
                $"col:{TestOwnerId}:character:{TestGameServiceId}:{CollectionType.MusicLibrary}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        var track1 = CreateTestTemplate(
            entryTemplateId: Guid.NewGuid(),
            code: "forest_theme",
            collectionType: CollectionType.MusicLibrary,
            displayName: "Forest Theme",
            themes: new List<string> { "forest", "peaceful" });
        var track2 = CreateTestTemplate(
            entryTemplateId: Guid.NewGuid(),
            code: "battle_theme",
            collectionType: CollectionType.MusicLibrary,
            displayName: "Battle Theme",
            themes: new List<string> { "battle", "intense" });

        _mockTemplateStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EntryTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntryTemplateModel> { track1, track2 });

        var cache = CreateTestCache(collectionId: collection.CollectionId, unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord { Code = "forest_theme", EntryTemplateId = track1.EntryTemplateId, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow },
            new UnlockedEntryRecord { Code = "battle_theme", EntryTemplateId = track2.EntryTemplateId, ItemInstanceId = Guid.NewGuid(), UnlockedAt = DateTimeOffset.UtcNow }
        });
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{collection.CollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.SelectTrackForAreaAsync(
            new SelectTrackForAreaRequest
            {
                OwnerId = TestOwnerId,
                OwnerType = "character",
                GameServiceId = TestGameServiceId,
                AreaCode = "enchanted_forest"
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Only forest_theme should match (forest + peaceful themes overlap)
        Assert.Equal("forest_theme", response.TrackCode);
        Assert.Equal("Forest Theme", response.DisplayName);
        Assert.Contains("forest", response.MatchedThemes);
        Assert.Contains("peaceful", response.MatchedThemes);
    }

    #endregion

    #region Discovery Tests

    [Fact]
    public async Task AdvanceDiscovery_ValidRequest_AdvancesLevelAndPublishesEvent()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate(
            discoveryLevels: new List<DiscoveryLevelEntry>
            {
                new DiscoveryLevelEntry { Level = 0, Reveals = new List<string> { "name" } },
                new DiscoveryLevelEntry { Level = 1, Reveals = new List<string> { "habitat", "weakness" } },
                new DiscoveryLevelEntry { Level = 2, Reveals = new List<string> { "lore", "drops" } }
            });

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord
            {
                Code = "boss_dragon",
                EntryTemplateId = TestEntryTemplateId,
                ItemInstanceId = Guid.NewGuid(),
                UnlockedAt = DateTimeOffset.UtcNow,
                Metadata = new EntryMetadataModel { DiscoveryLevel = 0 }
            }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // ETag-based cache update
        _mockCollectionCache
            .Setup(s => s.GetWithETagAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((cache, "etag-1"));
        _mockCollectionCache
            .Setup(s => s.TrySaveAsync($"cache:{TestCollectionId}", It.IsAny<CollectionCacheModel>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var service = CreateService();

        // Act
        var (status, response) = await service.AdvanceDiscoveryAsync(
            new AdvanceDiscoveryRequest { CollectionId = TestCollectionId, EntryCode = "boss_dragon" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.NewLevel);
        Assert.Contains("habitat", response.Reveals);
        Assert.Contains("weakness", response.Reveals);

        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                CollectionTopics.DiscoveryAdvanced,
                It.Is<CollectionDiscoveryAdvancedEvent>(e =>
                    e.NewLevel == 1 &&
                    e.EntryCode == "boss_dragon"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceDiscovery_AlreadyAtMaxLevel_ReturnsConflict()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate(
            discoveryLevels: new List<DiscoveryLevelEntry>
            {
                new DiscoveryLevelEntry { Level = 0, Reveals = new List<string> { "name" } },
                new DiscoveryLevelEntry { Level = 1, Reveals = new List<string> { "all" } }
            });

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord
            {
                Code = "boss_dragon",
                EntryTemplateId = TestEntryTemplateId,
                ItemInstanceId = Guid.NewGuid(),
                UnlockedAt = DateTimeOffset.UtcNow,
                Metadata = new EntryMetadataModel { DiscoveryLevel = 1 }
            }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.AdvanceDiscoveryAsync(
            new AdvanceDiscoveryRequest { CollectionId = TestCollectionId, EntryCode = "boss_dragon" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AdvanceDiscovery_NoDiscoveryLevels_ReturnsBadRequest()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate(); // No discovery levels

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord
            {
                Code = "boss_dragon",
                EntryTemplateId = TestEntryTemplateId,
                ItemInstanceId = Guid.NewGuid(),
                UnlockedAt = DateTimeOffset.UtcNow
            }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.AdvanceDiscoveryAsync(
            new AdvanceDiscoveryRequest { CollectionId = TestCollectionId, EntryCode = "boss_dragon" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region Update Entry Metadata Tests

    [Fact]
    public async Task UpdateEntryMetadata_ValidRequest_UpdatesAndReturnsEnrichedResponse()
    {
        // Arrange
        var collection = CreateTestCollection();
        var template = CreateTestTemplate();

        var cache = CreateTestCache(unlockedEntries: new List<UnlockedEntryRecord>
        {
            new UnlockedEntryRecord
            {
                Code = "boss_dragon",
                EntryTemplateId = TestEntryTemplateId,
                ItemInstanceId = Guid.NewGuid(),
                UnlockedAt = DateTimeOffset.UtcNow,
                Metadata = new EntryMetadataModel { PlayCount = 5, Favorited = false }
            }
        });

        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);
        _mockCollectionCache
            .Setup(s => s.GetWithETagAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((cache, "etag-1"));
        _mockCollectionCache
            .Setup(s => s.TrySaveAsync($"cache:{TestCollectionId}", It.IsAny<CollectionCacheModel>(), "etag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockTemplateStore
            .Setup(s => s.GetAsync(
                $"tpl:{TestGameServiceId}:{CollectionType.Bestiary}:boss_dragon",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var service = CreateService();

        // Act
        var (status, response) = await service.UpdateEntryMetadataAsync(
            new UpdateEntryMetadataRequest
            {
                CollectionId = TestCollectionId,
                EntryCode = "boss_dragon",
                PlayCount = 10,
                Favorited = true,
                KillCount = 3
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("boss_dragon", response.Code);
        Assert.NotNull(response.Metadata);
        Assert.Equal(10, response.Metadata.PlayCount);
        Assert.True(response.Metadata.Favorited);
        Assert.Equal(3, response.Metadata.KillCount);
    }

    [Fact]
    public async Task UpdateEntryMetadata_EntryNotFound_ReturnsNotFound()
    {
        // Arrange
        var collection = CreateTestCollection();
        var cache = CreateTestCache(); // Empty cache

        _mockCollectionStore
            .Setup(s => s.GetAsync($"col:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _mockCollectionCache
            .Setup(s => s.GetAsync($"cache:{TestCollectionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cache);

        var service = CreateService();

        // Act
        var (status, response) = await service.UpdateEntryMetadataAsync(
            new UpdateEntryMetadataRequest
            {
                CollectionId = TestCollectionId,
                EntryCode = "nonexistent",
                PlayCount = 1
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Event Consumer Registration Tests

    [Fact]
    public void Constructor_RegistersEventConsumers()
    {
        // Act - creating the service should register event consumers
        var service = CreateService();

        // Assert - verify the underlying Register<TEvent> interface method is called
        // (RegisterHandler is an extension method that delegates to Register)
        _mockEventConsumer.Verify(
            ec => ec.Register<CharacterDeletedEvent>(
                "character.deleted",
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CharacterDeletedEvent, Task>>()),
            Times.Once);

        _mockEventConsumer.Verify(
            ec => ec.Register<AccountDeletedEvent>(
                "account.deleted",
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, AccountDeletedEvent, Task>>()),
            Times.Once);
    }

    #endregion
}
