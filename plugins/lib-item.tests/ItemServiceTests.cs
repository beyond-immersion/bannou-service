using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Item.Tests;

/// <summary>
/// Unit tests for ItemService.
/// Tests item template and instance management operations.
/// </summary>
public class ItemServiceTests : ServiceTestBase<ItemServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ItemTemplateModel>> _mockTemplateStore;
    private readonly Mock<IStateStore<ItemInstanceModel>> _mockInstanceStore;
    private readonly Mock<IStateStore<ItemTemplateModel>> _mockTemplateCacheStore;
    private readonly Mock<IStateStore<ItemInstanceModel>> _mockInstanceCacheStore;
    private readonly Mock<IStateStore<string>> _mockTemplateStringStore;
    private readonly Mock<IStateStore<string>> _mockInstanceStringStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<IQueryableStateStore<ItemInstanceModel>> _mockInstanceQueryableStore;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ILogger<ItemService>> _mockLogger;

    public ItemServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTemplateStore = new Mock<IStateStore<ItemTemplateModel>>();
        _mockInstanceStore = new Mock<IStateStore<ItemInstanceModel>>();
        _mockTemplateCacheStore = new Mock<IStateStore<ItemTemplateModel>>();
        _mockInstanceCacheStore = new Mock<IStateStore<ItemInstanceModel>>();
        _mockTemplateStringStore = new Mock<IStateStore<string>>();
        _mockInstanceStringStore = new Mock<IStateStore<string>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockContractClient = new Mock<IContractClient>();
        _mockInstanceQueryableStore = new Mock<IQueryableStateStore<ItemInstanceModel>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLogger = new Mock<ILogger<ItemService>>();

        // Default lock provider returns successful lock
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Template persistent stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateStore))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(StateStoreDefinitions.ItemTemplateStore))
            .Returns(_mockTemplateStringStore.Object);

        // Template cache store (Redis)
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ItemTemplateModel>(StateStoreDefinitions.ItemTemplateCache))
            .Returns(_mockTemplateCacheStore.Object);

        // Instance persistent stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore))
            .Returns(_mockInstanceStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(StateStoreDefinitions.ItemInstanceStore))
            .Returns(_mockInstanceStringStore.Object);

        // Instance cache store (Redis)
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceCache))
            .Returns(_mockInstanceCacheStore.Object);

        // Instance queryable store (MySQL LINQ queries)
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<ItemInstanceModel>(StateStoreDefinitions.ItemInstanceStore))
            .Returns(_mockInstanceQueryableStore.Object);

        // Default cache stores return null (cache miss) so tests hit persistent stores
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);
        _mockTemplateCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cache-etag");
        _mockTemplateCacheStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);
        _mockInstanceCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cache-etag");
        _mockInstanceCacheStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // Default GetBulkAsync returns empty dict (cache miss) so tests fall through to persistent store
        _mockInstanceCacheStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ItemInstanceModel>());
        _mockInstanceCacheStore
            .Setup(s => s.SaveBulkAsync(It.IsAny<IEnumerable<KeyValuePair<string, ItemInstanceModel>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Default GetBulkAsync for persistent store returns empty (individual tests set up specific returns)
        _mockInstanceStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ItemInstanceModel>());

        // Default GetWithETagAsync for optimistic concurrency in list operations
        _mockTemplateStringStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string?)null, (string?)null));
        _mockTemplateStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTemplateStringStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockInstanceStringStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((string?)null, (string?)null));
        _mockInstanceStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default message bus setup
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default configuration: disable admin override so bind tests expect Conflict
        Configuration.BindingAllowAdminOverride = false;
    }

    private ItemService CreateService()
    {
        return new ItemService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockContractClient.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            Configuration);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void ItemService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ItemService>();

    #endregion

    #region CreateItemTemplate Tests

    [Fact]
    public async Task CreateItemTemplateAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidTemplateRequest();

        _mockTemplateStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("tpl-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.Code, response.Code);
        Assert.Equal(request.GameId, response.GameId);
        Assert.Equal(request.Name, response.Name);
        Assert.Equal(ItemCategory.Weapon, response.Category);
        Assert.Equal(ItemRarity.Rare, response.Rarity);
        Assert.Equal(QuantityModel.Unique, response.QuantityModel);
        Assert.True(response.IsActive);
        Assert.False(response.IsDeprecated);
        Assert.NotNull(savedModel);
        Assert.Equal(request.Code, savedModel.Code);
        Assert.Equal(request.GameId, savedModel.GameId);
        Assert.Equal(request.Name, savedModel.Name);
        Assert.Equal(ItemCategory.Weapon, savedModel.Category);
        Assert.Equal(ItemRarity.Rare, savedModel.Rarity);
        Assert.True(savedModel.IsActive);
        Assert.False(savedModel.IsDeprecated);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidTemplateRequest();

        // Override default TrySaveAsync to simulate code already claimed
        _mockTemplateStringStore
            .Setup(s => s.TrySaveAsync(It.Is<string>(k => k.StartsWith("tpl-code:")), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidTemplateRequest();

        _mockTemplateStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.Equal("item-template.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemTemplateCreatedEvent>(capturedEvent);
        Assert.Equal(request.Code, typedEvent.Code);
        Assert.Equal(request.GameId, typedEvent.GameId);
        Assert.Equal(request.Name, typedEvent.Name);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_UsesDefaultRarity_WhenNotProvided()
    {
        // Arrange
        Configuration.DefaultRarity = ItemRarity.Epic;
        var service = CreateService();
        var request = CreateValidTemplateRequest();
        request.Rarity = null;

        _mockTemplateStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(ItemRarity.Epic, savedModel.Rarity);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_UsesDefaultWeightPrecision_WhenNotProvided()
    {
        // Arrange
        Configuration.DefaultWeightPrecision = WeightPrecision.Integer;
        var service = CreateService();
        var request = CreateValidTemplateRequest();
        request.WeightPrecision = null;

        _mockTemplateStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(WeightPrecision.Integer, savedModel.WeightPrecision);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_UsesDefaultSoulboundType_WhenNotProvided()
    {
        // Arrange
        Configuration.DefaultSoulboundType = SoulboundType.OnPickup;
        var service = CreateService();
        var request = CreateValidTemplateRequest();
        request.SoulboundType = null;

        _mockTemplateStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        await service.CreateItemTemplateAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(SoulboundType.OnPickup, savedModel.SoulboundType);
    }

    #endregion

    #region GetItemTemplate Tests

    [Fact]
    public async Task GetItemTemplateAsync_ById_ReturnsTemplate()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetItemTemplateRequest { TemplateId = templateId };

        // Act
        var (status, response) = await service.GetItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(templateId, response.TemplateId);
        Assert.Equal("test_sword", response.Code);
    }

    [Fact]
    public async Task GetItemTemplateAsync_ByCodeAndGameId_ReturnsTemplate()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStringStore
            .Setup(s => s.GetAsync("tpl-code:game1:test_sword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateId.ToString());
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetItemTemplateRequest { Code = "test_sword", GameId = "game1" };

        // Act
        var (status, response) = await service.GetItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("test_sword", response.Code);
    }

    [Fact]
    public async Task GetItemTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);

        var request = new GetItemTemplateRequest { TemplateId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListItemTemplates Tests

    [Fact]
    public async Task ListItemTemplatesAsync_ReturnsFilteredTemplates()
    {
        // Arrange
        var service = CreateService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockTemplateStringStore
            .Setup(s => s.GetAsync("tpl-game:game1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { id1.ToString(), id2.ToString() }));

        var activeModel = CreateStoredTemplateModel(id1);
        var inactiveModel = CreateStoredTemplateModel(id2);
        inactiveModel.IsActive = false;

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeModel);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveModel);

        var request = new ListItemTemplatesRequest
        {
            GameId = "game1",
            IncludeInactive = false,
            IncludeDeprecated = false,
            Offset = 0,
            Limit = 50
        };

        // Act
        var (status, response) = await service.ListItemTemplatesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Templates);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task ListItemTemplatesAsync_Pagination_ReturnsCorrectPage()
    {
        // Arrange
        var service = CreateService();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        _mockTemplateStringStore
            .Setup(s => s.GetAsync("all-templates", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(ids.Select(id => id.ToString()).ToList()));

        foreach (var id in ids)
        {
            _mockTemplateStore
                .Setup(s => s.GetAsync($"tpl:{id}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateStoredTemplateModel(id));
        }

        var request = new ListItemTemplatesRequest
        {
            IncludeInactive = true,
            IncludeDeprecated = true,
            Offset = 2,
            Limit = 2
        };

        // Act
        var (status, response) = await service.ListItemTemplatesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Templates.Count);
        Assert.Equal(5, response.TotalCount);
    }

    #endregion

    #region UpdateItemTemplate Tests

    [Fact]
    public async Task UpdateItemTemplateAsync_ValidRequest_UpdatesFields()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new UpdateItemTemplateRequest
        {
            TemplateId = templateId,
            Name = "Updated Sword",
            Rarity = ItemRarity.Legendary
        };

        // Act
        var (status, response) = await service.UpdateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Sword", response.Name);
        Assert.Equal(ItemRarity.Legendary, response.Rarity);
        Assert.NotNull(savedModel);
        Assert.Equal("Updated Sword", savedModel.Name);
        Assert.Equal(ItemRarity.Legendary, savedModel.Rarity);
    }

    [Fact]
    public async Task UpdateItemTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);

        var request = new UpdateItemTemplateRequest { TemplateId = Guid.NewGuid(), Name = "New Name" };

        // Act
        var (status, response) = await service.UpdateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateItemTemplateAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateItemTemplateRequest { TemplateId = templateId, Name = "Updated" };

        // Act
        await service.UpdateItemTemplateAsync(request);

        // Assert
        Assert.Equal("item-template.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemTemplateUpdatedEvent>(capturedEvent);
        Assert.Equal(templateId, typedEvent.TemplateId);
    }

    #endregion

    #region DeprecateItemTemplate Tests

    [Fact]
    public async Task DeprecateItemTemplateAsync_ValidRequest_DeprecatesTemplate()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var migrationTargetId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        ItemTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new DeprecateItemTemplateRequest
        {
            TemplateId = templateId,
            MigrationTargetId = migrationTargetId
        };

        // Act
        var (status, response) = await service.DeprecateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.NotNull(response.DeprecatedAt);
        Assert.Equal(migrationTargetId, response.MigrationTargetId);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsDeprecated);
        Assert.NotNull(savedModel.DeprecatedAt);
        Assert.Equal(migrationTargetId, savedModel.MigrationTargetId);
    }

    [Fact]
    public async Task DeprecateItemTemplateAsync_PublishesUpdatedEventWithChangedFields()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var migrationTargetId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new DeprecateItemTemplateRequest
        {
            TemplateId = templateId,
            MigrationTargetId = migrationTargetId
        };

        // Act
        await service.DeprecateItemTemplateAsync(request);

        // Assert — per IMPLEMENTATION TENETS: deprecation published as *.updated with changedFields
        Assert.Equal("item-template.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemTemplateUpdatedEvent>(capturedEvent);
        Assert.Equal(templateId, typedEvent.TemplateId);
        Assert.Equal(model.Code, typedEvent.Code);
        Assert.Equal(model.GameId, typedEvent.GameId);
        Assert.True(typedEvent.IsDeprecated);
        Assert.Contains("isDeprecated", typedEvent.ChangedFields);
        Assert.Contains("deprecatedAt", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task DeprecateItemTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);

        var request = new DeprecateItemTemplateRequest { TemplateId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.DeprecateItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CreateItemInstance Tests

    [Fact]
    public async Task CreateItemInstanceAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ItemInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = realmId,
            Quantity = 1,
            OriginType = ItemOriginType.Loot
        };

        // Act
        var (status, response) = await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(templateId, response.TemplateId);
        Assert.Equal(containerId, response.ContainerId);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(1, response.Quantity);
        Assert.Equal(ItemOriginType.Loot, response.OriginType);
        Assert.NotNull(savedModel);
        Assert.Equal(templateId, savedModel.TemplateId);
        Assert.Equal(containerId, savedModel.ContainerId);
        Assert.Equal(realmId, savedModel.RealmId);
        Assert.Equal(1, savedModel.Quantity);
        Assert.Equal(ItemOriginType.Loot, savedModel.OriginType);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);

        var request = new CreateItemInstanceRequest
        {
            TemplateId = Guid.NewGuid(),
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot
        };

        // Act
        var (status, response) = await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_InactiveTemplate_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);
        template.IsActive = false;

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot
        };

        // Act
        var (status, response) = await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_DeprecatedTemplate_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);
        template.IsDeprecated = true;

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot
        };

        // Act
        var (status, response) = await service.CreateItemInstanceAsync(request);

        // Assert — per IMPLEMENTATION TENETS: deprecated templates must not produce new instances
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_UniqueQuantityModel_ForcesQuantityToOne()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);
        template.QuantityModel = QuantityModel.Unique;

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ItemInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 99,
            OriginType = ItemOriginType.Craft
        };

        // Act
        var (status, response) = await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.Equal(1, savedModel.Quantity);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_DiscreteQuantity_ClampsToMaxStack()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);
        template.QuantityModel = QuantityModel.Discrete;
        template.MaxStackSize = 20;

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ItemInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 50,
            OriginType = ItemOriginType.Loot
        };

        // Act
        await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(20, savedModel.Quantity);
    }

    [Fact]
    public async Task CreateItemInstanceAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var containerId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = realmId,
            Quantity = 1,
            OriginType = ItemOriginType.Quest
        };

        // Act
        await service.CreateItemInstanceAsync(request);

        // Assert
        Assert.Equal("item-instance.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemInstanceCreatedEvent>(capturedEvent);
        Assert.Equal(templateId, typedEvent.TemplateId);
        Assert.Equal(containerId, typedEvent.ContainerId);
        Assert.Equal(realmId, typedEvent.RealmId);
    }

    #endregion

    #region GetItemInstance Tests

    [Fact]
    public async Task GetItemInstanceAsync_Found_ReturnsInstance()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetItemInstanceRequest { InstanceId = instanceId };

        // Act
        var (status, response) = await service.GetItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(instanceId, response.InstanceId);
    }

    [Fact]
    public async Task GetItemInstanceAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        var request = new GetItemInstanceRequest { InstanceId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ModifyItemInstance Tests

    [Fact]
    public async Task ModifyItemInstanceAsync_DurabilityDelta_AppliesCorrectly()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.CurrentDurability = 100;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new ModifyItemInstanceRequest
        {
            InstanceId = instanceId,
            DurabilityDelta = -25
        };

        // Act
        var (status, response) = await service.ModifyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(75, response.CurrentDurability);
    }

    [Fact]
    public async Task ModifyItemInstanceAsync_DurabilityCannotGoBelowZero()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.CurrentDurability = 10;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new ModifyItemInstanceRequest
        {
            InstanceId = instanceId,
            DurabilityDelta = -50
        };

        // Act
        var (status, response) = await service.ModifyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.CurrentDurability);
    }

    [Fact]
    public async Task ModifyItemInstanceAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        var request = new ModifyItemInstanceRequest { InstanceId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.ModifyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ModifyItemInstanceAsync_CustomName_Updates()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new ModifyItemInstanceRequest
        {
            InstanceId = instanceId,
            CustomName = "My Legendary Sword"
        };

        // Act
        var (status, response) = await service.ModifyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("My Legendary Sword", response.CustomName);
    }

    #endregion

    #region BindItemInstance Tests

    [Fact]
    public async Task BindItemInstanceAsync_ValidRequest_BindsItem()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = null;

        var template = CreateStoredTemplateModel(model.TemplateId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        ItemInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new BindItemInstanceRequest
        {
            InstanceId = instanceId,
            CharacterId = characterId,
            BindType = SoulboundType.OnPickup
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.BoundToId);
        Assert.NotNull(response.BoundAt);
        Assert.NotNull(savedModel);
        Assert.Equal(characterId, savedModel.BoundToId);
        Assert.NotNull(savedModel.BoundAt);
    }

    [Fact]
    public async Task BindItemInstanceAsync_PublishesBoundEvent()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = null;

        var template = CreateStoredTemplateModel(model.TemplateId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new BindItemInstanceRequest
        {
            InstanceId = instanceId,
            CharacterId = characterId,
            BindType = SoulboundType.OnPickup
        };

        // Act
        await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal("item-instance.bound", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemInstanceBoundEvent>(capturedEvent);
        Assert.Equal(instanceId, typedEvent.InstanceId);
        Assert.Equal(characterId, typedEvent.CharacterId);
        Assert.Equal(SoulboundType.OnPickup, typedEvent.BindType);
    }

    [Fact]
    public async Task BindItemInstanceAsync_AlreadyBound_ReturnsConflict()
    {
        // Arrange
        Configuration.BindingAllowAdminOverride = false;
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new BindItemInstanceRequest
        {
            InstanceId = instanceId,
            CharacterId = Guid.NewGuid(),
            BindType = SoulboundType.OnEquip
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindItemInstanceAsync_AlreadyBound_AdminOverride_Succeeds()
    {
        // Arrange
        Configuration.BindingAllowAdminOverride = true;
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var newCharacterId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = Guid.NewGuid();

        var template = CreateStoredTemplateModel(model.TemplateId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new BindItemInstanceRequest
        {
            InstanceId = instanceId,
            CharacterId = newCharacterId,
            BindType = SoulboundType.OnEquip
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(newCharacterId, response.BoundToId);
    }

    [Fact]
    public async Task BindItemInstanceAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        var request = new BindItemInstanceRequest
        {
            InstanceId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            BindType = SoulboundType.OnPickup
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region UnbindItemInstance Tests

    [Fact]
    public async Task UnbindItemInstanceAsync_ValidRequest_UnbindsItem()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var previousCharacterId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = previousCharacterId;
        model.BoundAt = DateTimeOffset.UtcNow.AddHours(-1);

        var template = CreateStoredTemplateModel(model.TemplateId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        ItemInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new UnbindItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = UnbindReason.Admin
        };

        // Act
        var (status, response) = await service.UnbindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.BoundToId);
        Assert.Null(response.BoundAt);
        Assert.NotNull(savedModel);
        Assert.Null(savedModel.BoundToId);
        Assert.Null(savedModel.BoundAt);
    }

    [Fact]
    public async Task UnbindItemInstanceAsync_NotBound_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = null;
        model.BoundAt = null;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UnbindItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = UnbindReason.Admin
        };

        // Act
        var (status, response) = await service.UnbindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UnbindItemInstanceAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        var request = new UnbindItemInstanceRequest
        {
            InstanceId = Guid.NewGuid(),
            Reason = UnbindReason.Admin
        };

        // Act
        var (status, response) = await service.UnbindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UnbindItemInstanceAsync_PublishesUnboundEvent()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var previousCharacterId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        model.BoundToId = previousCharacterId;
        model.BoundAt = DateTimeOffset.UtcNow.AddHours(-1);

        var template = CreateStoredTemplateModel(model.TemplateId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UnbindItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = UnbindReason.TransferOverride
        };

        // Act
        await service.UnbindItemInstanceAsync(request);

        // Assert
        Assert.Equal("item-instance.unbound", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<ItemInstanceUnboundEvent>(capturedEvent);
        Assert.Equal(instanceId, typedEvent.InstanceId);
        Assert.Equal(previousCharacterId, typedEvent.PreviousCharacterId);
        Assert.Equal(UnbindReason.TransferOverride, typedEvent.Reason);
    }

    #endregion

    #region DestroyItemInstance Tests

    [Fact]
    public async Task DestroyItemInstanceAsync_ValidRequest_DestroysItem()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        var template = CreateStoredTemplateModel(model.TemplateId);
        template.Destroyable = true;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        string? deletedKey = null;
        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((k, _) => deletedKey = k)
            .ReturnsAsync(true);

        var request = new DestroyItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = DestroyReason.Destroyed
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(deletedKey);
        Assert.Contains(instanceId.ToString(), deletedKey);
    }

    [Fact]
    public async Task DestroyItemInstanceAsync_NotDestroyable_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        var template = CreateStoredTemplateModel(model.TemplateId);
        template.Destroyable = false;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new DestroyItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = DestroyReason.Destroyed
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DestroyItemInstanceAsync_NotDestroyableButAdmin_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);
        var template = CreateStoredTemplateModel(model.TemplateId);
        template.Destroyable = false;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new DestroyItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = DestroyReason.Admin
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DestroyItemInstanceAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        var request = new DestroyItemInstanceRequest
        {
            InstanceId = Guid.NewGuid(),
            Reason = DestroyReason.Destroyed
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListItemsByContainer Tests

    [Fact]
    public async Task ListItemsByContainerAsync_ReturnsItems()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockInstanceStringStore
            .Setup(s => s.GetAsync($"inst-container:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { id1.ToString(), id2.ToString() }));

        // Implementation uses GetBulkAsync for performance
        var model1 = CreateStoredInstanceModel(id1);
        var model2 = CreateStoredInstanceModel(id2);
        _mockInstanceStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ItemInstanceModel>
            {
                [$"inst:{id1}"] = model1,
                [$"inst:{id2}"] = model2
            });

        var request = new ListItemsByContainerRequest { ContainerId = containerId };

        // Act
        var (status, response) = await service.ListItemsByContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListItemsByContainerAsync_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        _mockInstanceStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new ListItemsByContainerRequest { ContainerId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.ListItemsByContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListItemsByContainerAsync_MaxInstancesPerQuery_CapsResults()
    {
        // Arrange
        Configuration.MaxInstancesPerQuery = 2;
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        _mockInstanceStringStore
            .Setup(s => s.GetAsync($"inst-container:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(ids.Select(id => id.ToString()).ToList()));

        // Implementation uses GetBulkAsync for performance - only first 2 will be fetched due to cap
        var bulkResult = new Dictionary<string, ItemInstanceModel>();
        foreach (var id in ids.Take(2))
        {
            bulkResult[$"inst:{id}"] = CreateStoredInstanceModel(id);
        }
        _mockInstanceStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        var request = new ListItemsByContainerRequest { ContainerId = containerId };

        // Act
        var (status, response) = await service.ListItemsByContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
    }

    #endregion

    #region ListItemsByTemplate Tests

    [Fact]
    public async Task ListItemsByTemplateAsync_WithRealmFilter_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var targetRealm = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var matchModel = CreateStoredInstanceModel(matchId);
        matchModel.TemplateId = templateId;
        matchModel.RealmId = targetRealm;

        // Implementation uses GetQueryableStore for MySQL LINQ queries
        _mockInstanceQueryableStore
            .Setup(s => s.QueryAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ItemInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemInstanceModel> { matchModel });

        var request = new ListItemsByTemplateRequest
        {
            TemplateId = templateId,
            RealmId = targetRealm,
            Offset = 0,
            Limit = 50
        };

        // Act
        var (status, response) = await service.ListItemsByTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(matchId, response.Items.First().InstanceId);
    }

    [Fact]
    public async Task ListItemsByTemplateAsync_MaxInstancesPerQuery_CapsResults()
    {
        // Arrange
        Configuration.MaxInstancesPerQuery = 2;
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        // Implementation uses GetQueryableStore for MySQL LINQ queries
        var queryResult = ids.Select(id =>
        {
            var model = CreateStoredInstanceModel(id);
            model.TemplateId = templateId;
            return model;
        }).ToList();

        _mockInstanceQueryableStore
            .Setup(s => s.QueryAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ItemInstanceModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        var request = new ListItemsByTemplateRequest
        {
            TemplateId = templateId,
            Offset = 0,
            Limit = 50
        };

        // Act
        var (status, response) = await service.ListItemsByTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
    }

    #endregion

    #region BatchGetItemInstances Tests

    [Fact]
    public async Task BatchGetItemInstancesAsync_MixedResults_ReturnsFoundAndNotFound()
    {
        // Arrange
        var service = CreateService();
        var foundId = Guid.NewGuid();
        var notFoundId = Guid.NewGuid();

        // Implementation uses GetBulkAsync for performance
        // Only foundId is in the result - notFoundId is missing (not found)
        _mockInstanceStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ItemInstanceModel>
            {
                [$"inst:{foundId}"] = CreateStoredInstanceModel(foundId)
                // notFoundId is not present - simulates not found
            });

        var request = new BatchGetItemInstancesRequest
        {
            InstanceIds = new List<Guid> { foundId, notFoundId }
        };

        // Act
        var (status, response) = await service.BatchGetItemInstancesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(foundId, response.Items.First().InstanceId);
        Assert.Single(response.NotFound);
        Assert.Contains(notFoundId, response.NotFound);
    }

    #endregion

    #region Cache Behavior Tests

    [Fact]
    public async Task GetItemTemplateAsync_CacheHit_DoesNotQueryPersistentStore()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetItemTemplateRequest { TemplateId = templateId };

        // Act
        var (status, response) = await service.GetItemTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        _mockTemplateStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetItemInstanceAsync_CacheHit_DoesNotQueryPersistentStore()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredInstanceModel(instanceId);

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetItemInstanceRequest { InstanceId = instanceId };

        // Act
        var (status, response) = await service.GetItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        _mockInstanceStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateItemTemplateAsync_InvalidatesCache()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateStoredTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new UpdateItemTemplateRequest { TemplateId = templateId, Name = "Updated" };

        // Act
        await service.UpdateItemTemplateAsync(request);

        // Assert
        _mockTemplateCacheStore.Verify(
            s => s.DeleteAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region UseItem Tests

    [Fact]
    public async Task UseItemAsync_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UseItemRequest
        {
            InstanceId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemAsync_TemplateNotFound_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemTemplateModel?)null);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
    }

    [Fact]
    public async Task UseItemAsync_TemplateHasNoBehaviorContract_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "test_item",
            GameId = "game1",
            Name = "Test Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = null  // No behavior contract
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        // Per T8: Error responses return null, status code is sufficient
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemAsync_ContractCreationFails_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "test_potion",
            GameId = "game1",
            Name = "Test Potion",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Simulate contract creation failure with ApiException
        // Note: The helper method catches ApiException and returns null, so UseItem returns BadRequest
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Contract template not found", 404, "", null, null));

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Helper catches exception and returns null, causing BadRequest;
        // per T8, error response is null
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemAsync_MilestoneCompletionFails_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 5,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "test_scroll",
            GameId = "game1",
            Name = "Test Scroll",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Contract creation succeeds
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });

        // Milestone completion fails
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Pending  // Not completed
                }
            });

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Per T8, error response is null, status code is sufficient
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemAsync_Success_UniqueItem_DestroyedOnUse()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = Guid.NewGuid(),
            Quantity = 1,  // Unique item
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "unique_artifact",
            GameId = "game1",
            Name = "Unique Artifact",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Contract succeeds
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Completed
                }
            });

        // Instance store for deletion
        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
                capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Consumed);
        Assert.Null(response.RemainingQuantity);  // Destroyed
        Assert.Equal(contractInstanceId, response.ContractInstanceId);

        var destroyEvent = capturedEvents
            .Where(e => e.Topic == "item-instance.destroyed")
            .Select(e => e.Event)
            .SingleOrDefault();
        Assert.NotNull(destroyEvent);
        Assert.IsType<ItemInstanceDestroyedEvent>(destroyEvent);
    }

    [Fact]
    public async Task UseItemAsync_Success_StackableItem_QuantityDecremented()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 5,  // Stack of 5
            OriginType = ItemOriginType.Craft,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "health_potion",
            GameId = "game1",
            Name = "Health Potion",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = 99,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Contract succeeds
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Completed
                }
            });

        // Instance store for update (simulate quantity decrement)
        ItemInstanceModel? savedInstance = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ItemInstanceModel, StateOptions?, CancellationToken>((_, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Consumed);
        Assert.Equal(4, response.RemainingQuantity);  // 5 - 1 = 4
        Assert.Equal(contractInstanceId, response.ContractInstanceId);
        Assert.NotNull(savedInstance);
        Assert.Equal(4, savedInstance.Quantity);
    }

    [Fact]
    public async Task UseItemAsync_DeterministicSystemPartyId_ConsistentForSameGameId()
    {
        // Arrange
        var service = CreateService();
        var instanceId1 = Guid.NewGuid();
        var instanceId2 = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var gameId = "consistent_game";

        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "test_item",
            GameId = gameId,
            Name = "Test Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = 99,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Capture the system party IDs used in contract creation requests
        var capturedSystemPartyIds = new List<Guid>();
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContractInstanceRequest, CancellationToken>((req, _) =>
            {
                var systemParty = req.Parties.FirstOrDefault(p => p.Role == "system");
                if (systemParty != null)
                {
                    capturedSystemPartyIds.Add(systemParty.EntityId);
                }
            })
            .ReturnsAsync(new ContractInstanceResponse { ContractId = Guid.NewGuid() });

        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse { Code = "use", Status = MilestoneStatus.Completed }
            });

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Use two different instances
        var instance1 = new ItemInstanceModel
        {
            InstanceId = instanceId1,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 5,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var instance2 = new ItemInstanceModel
        {
            InstanceId = instanceId2,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 3,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance1);
        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance2);

        // Act
        await service.UseItemAsync(new UseItemRequest
        {
            InstanceId = instanceId1,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        });
        await service.UseItemAsync(new UseItemRequest
        {
            InstanceId = instanceId2,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        });

        // Assert - Both uses should have the same system party ID for the same game
        Assert.Equal(2, capturedSystemPartyIds.Count);
        Assert.Equal(capturedSystemPartyIds[0], capturedSystemPartyIds[1]);
    }

    [Fact]
    public async Task UseItemAsync_WithTargetContext_PassesContextToContract()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "targeted_spell",
            GameId = "game1",
            Name = "Targeted Spell Scroll",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            TargetId = targetId,
            TargetType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        CreateContractInstanceRequest? capturedRequest = null;
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContractInstanceRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ContractInstanceResponse { ContractId = Guid.NewGuid() });
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse { Code = "use", Status = MilestoneStatus.Completed }
            });

        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.UseItemAsync(request);

        // Assert - Verify the contract request includes item and user context
        Assert.NotNull(capturedRequest);
        Assert.Equal(2, capturedRequest.Parties.Count);
        Assert.Contains(capturedRequest.Parties, p => p.Role == "user");
        Assert.Contains(capturedRequest.Parties, p => p.Role == "system");
    }

    [Fact]
    public async Task UseItemAsync_ItemUseBehaviorDisabled_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "non_usable_item",
            GameId = "game1",
            Name = "Non-Usable Item",
            Category = ItemCategory.Weapon,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = Guid.NewGuid(),
            ItemUseBehavior = ItemUseBehavior.Disabled  // Key: item use is disabled
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Per T8, error response is null, status code is sufficient
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify no contract was created
        _mockContractClient.Verify(
            c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UseItemAsync_CanUseValidationBlocks_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var canUseBehaviorTemplateId = Guid.NewGuid();
        var canUseContractId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "guarded_item",
            GameId = "game1",
            Name = "Guarded Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = Guid.NewGuid(),
            CanUseBehaviorContractTemplateId = canUseBehaviorTemplateId,
            CanUseBehavior = CanUseBehavior.Block  // Key: block on validation failure
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // CanUse validation contract created successfully
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == canUseBehaviorTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = canUseContractId });

        // CanUse milestone FAILS (validation didn't pass)
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == canUseContractId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "validate",
                    Status = MilestoneStatus.Pending  // Not completed = validation failed
                }
            });

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        // Per T8: Error responses return null, status code is sufficient
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemAsync_CanUseValidationWarnsButProceeds_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var canUseBehaviorTemplateId = Guid.NewGuid();
        var mainBehaviorTemplateId = Guid.NewGuid();
        var canUseContractId = Guid.NewGuid();
        var mainContractId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "flexible_item",
            GameId = "game1",
            Name = "Flexible Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = mainBehaviorTemplateId,
            CanUseBehaviorContractTemplateId = canUseBehaviorTemplateId,
            CanUseBehavior = CanUseBehavior.WarnAndProceed  // Key: warn but proceed
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // CanUse validation contract created
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == canUseBehaviorTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = canUseContractId });

        // CanUse milestone fails (but we're set to warn and proceed)
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == canUseContractId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "validate",
                    Status = MilestoneStatus.Pending  // Validation failed
                }
            });

        // Main behavior contract created
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == mainBehaviorTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = mainContractId });

        // Main use milestone succeeds
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == mainContractId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Completed
                }
            });

        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Despite validation failure, use proceeds
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task UseItemAsync_DestroyAlways_ConsumesOnFailure()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "explosive_item",
            GameId = "game1",
            Name = "Explosive Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId,
            ItemUseBehavior = ItemUseBehavior.DestroyAlways  // Key: consume even on failure
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Contract succeeds
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });

        // Milestone FAILS
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Pending  // Failed
                }
            });

        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Use failed but item was consumed (per T8, error response is null)
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify deletion was called (key assertion: consumed even on failure with DestroyAlways)
        _mockInstanceStore.Verify(
            s => s.DeleteAsync(It.Is<string>(k => k.Contains(instanceId.ToString())), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UseItemAsync_OnUseFailedHandler_ExecutedOnFailure()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var mainContractTemplateId = Guid.NewGuid();
        var failedHandlerTemplateId = Guid.NewGuid();
        var mainContractId = Guid.NewGuid();
        var failedHandlerContractId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "handled_item",
            GameId = "game1",
            Name = "Handled Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = mainContractTemplateId,
            OnUseFailedBehaviorContractTemplateId = failedHandlerTemplateId  // Key: failure handler
        };

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Main behavior contract created
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == mainContractTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = mainContractId });

        // Main milestone fails
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == mainContractId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "use",
                    Status = MilestoneStatus.Pending  // Failed
                }
            });

        // OnUseFailed handler contract created
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == failedHandlerTemplateId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = failedHandlerContractId });

        // OnUseFailed milestone completed
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == failedHandlerContractId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "handle_failure",
                    Status = MilestoneStatus.Completed
                }
            });

        // Act
        var (status, response) = await service.UseItemAsync(request);

        // Assert - Use failed but handler was invoked (per T8, error response is null)
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify OnUseFailed handler was called (key assertion)
        _mockContractClient.Verify(
            c => c.CreateContractInstanceAsync(
                It.Is<CreateContractInstanceRequest>(r => r.TemplateId == failedHandlerTemplateId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region UseItemStep Tests

    [Fact]
    public async Task UseItemStepAsync_InstanceNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UseItemStepRequest
        {
            InstanceId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            MilestoneCode = "step_1"
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

        // Act
        var (status, response) = await service.UseItemStepAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UseItemStepAsync_TemplateHasNoBehaviorContract_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "no_behavior",
            GameId = "game1",
            Name = "No Behavior",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
            // No UseBehaviorContractTemplateId
        };

        var request = new UseItemStepRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            MilestoneCode = "step_1"
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.UseItemStepAsync(request);

        // Assert - Per T8, error response is null, status code is sufficient
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UseItemStepAsync_FirstStep_CreatesContract()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
            // No ContractInstanceId = first step
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "multi_step_item",
            GameId = "game1",
            Name = "Multi-Step Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemStepRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            MilestoneCode = "step_1"
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // For lock re-read
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-1"));
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Contract created for first step
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });

        // Milestone completed
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "step_1",
                    Status = MilestoneStatus.Completed
                }
            });

        // Remaining milestones query
        _mockContractClient
            .Setup(c => c.GetContractInstanceAsync(It.IsAny<GetContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse
            {
                ContractId = contractInstanceId,
                Milestones = new List<MilestoneInstanceResponse>
                {
                    new() { Code = "step_1", Status = MilestoneStatus.Completed },
                    new() { Code = "step_2", Status = MilestoneStatus.Pending }
                }
            });

        // Act
        var (status, response) = await service.UseItemStepAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("step_1", response.CompletedMilestone);
        Assert.False(response.IsComplete);  // More milestones remaining
        Assert.False(response.Consumed);
        Assert.NotNull(response.RemainingMilestones);
        Assert.Contains("step_2", response.RemainingMilestones);

        // Verify contract was created
        _mockContractClient.Verify(
            c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UseItemStepAsync_SubsequentStep_UsesExistingContract()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var existingContractId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow,
            ContractInstanceId = existingContractId,  // Existing contract = not first step
            ContractBindingType = ContractBindingType.Session
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "multi_step_item",
            GameId = "game1",
            Name = "Multi-Step Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };

        var request = new UseItemStepRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            MilestoneCode = "step_2"
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // For lock re-read
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-1"));

        // Milestone completed on EXISTING contract
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.Is<CompleteMilestoneRequest>(r => r.ContractId == existingContractId && r.MilestoneCode == "step_2"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse
                {
                    Code = "step_2",
                    Status = MilestoneStatus.Completed
                }
            });

        // No remaining milestones = complete
        _mockContractClient
            .Setup(c => c.GetContractInstanceAsync(It.IsAny<GetContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse
            {
                ContractId = existingContractId,
                Milestones = new List<MilestoneInstanceResponse>
                {
                    new() { Code = "step_1", Status = MilestoneStatus.Completed },
                    new() { Code = "step_2", Status = MilestoneStatus.Completed }
                }
            });

        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UseItemStepAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("step_2", response.CompletedMilestone);
        Assert.True(response.IsComplete);  // All milestones done
        Assert.True(response.Consumed);

        // Verify NO new contract was created (used existing)
        _mockContractClient.Verify(
            c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UseItemStepAsync_LockAcquisitionFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "multi_step_item",
            GameId = "game1",
            Name = "Multi-Step Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Unique,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = Guid.NewGuid()
        };

        var request = new UseItemStepRequest
        {
            InstanceId = instanceId,
            UserId = Guid.NewGuid(),
            UserType = EntityType.Character,
            MilestoneCode = "step_1"
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Lock fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.ItemLock,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, response) = await service.UseItemStepAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Helper Methods

    private static CreateItemTemplateRequest CreateValidTemplateRequest()
    {
        return new CreateItemTemplateRequest
        {
            Code = "test_sword",
            GameId = "game1",
            Name = "Test Sword",
            Description = "A test sword for unit testing",
            Category = ItemCategory.Weapon,
            Rarity = ItemRarity.Rare,
            QuantityModel = QuantityModel.Unique,
            MaxStackSize = 1,
            WeightPrecision = WeightPrecision.Decimal2,
            Weight = 3.5,
            Tradeable = true,
            Destroyable = true,
            SoulboundType = SoulboundType.None,
            HasDurability = true,
            MaxDurability = 100,
            Scope = ItemScope.Global
        };
    }

    private static ItemTemplateModel CreateStoredTemplateModel(Guid templateId)
    {
        return new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "test_sword",
            GameId = "game1",
            Name = "Test Sword",
            Description = "A test sword",
            Category = ItemCategory.Weapon,
            Rarity = ItemRarity.Rare,
            QuantityModel = QuantityModel.Unique,
            MaxStackSize = 1,
            WeightPrecision = WeightPrecision.Decimal2,
            Weight = 3.5,
            Tradeable = true,
            Destroyable = true,
            SoulboundType = SoulboundType.None,
            HasDurability = true,
            MaxDurability = 100,
            Scope = ItemScope.Global,
            IsActive = true,
            IsDeprecated = false,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    private static ItemInstanceModel CreateStoredInstanceModel(Guid instanceId)
    {
        return new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = Guid.NewGuid(),
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 1,
            CurrentDurability = 100,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
    }

    #endregion

    #region Batch Event Flush Tests

    [Fact]
    public async Task UseItemAsync_ExpiredSuccessBatch_PublishesBeforeNewRecord()
    {
        // Arrange - set up a successful item use
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var contractInstanceId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "batch_test_item",
            GameId = "game1",
            Name = "Batch Test Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = 99,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 50,
            OriginType = ItemOriginType.Craft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = contractInstanceId });
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(It.IsAny<CompleteMilestoneRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse
            {
                Milestone = new MilestoneInstanceResponse { Code = "use", Status = MilestoneStatus.Completed }
            });
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Inject a pre-expired batch into the static _useBatches dictionary via reflection.
        // This simulates a batch that accumulated records and then the window expired.
        var batchKey = $"{templateId}:{userId}";
        var useBatchesField = typeof(ItemService).GetField("_useBatches",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(useBatchesField);
        var useBatches = useBatchesField.GetValue(null) as System.Collections.Concurrent.ConcurrentDictionary<string, ItemUseBatchState>;
        Assert.NotNull(useBatches);

        // Create an expired batch with one record already in it
        var expiredBatch = new ItemUseBatchState();
        expiredBatch.AddRecord(new ItemUseRecord
        {
            InstanceId = Guid.NewGuid(),
            TemplateId = templateId,
            TemplateCode = "batch_test_item",
            UserId = userId,
            UserType = EntityType.Character,
            UsedAt = DateTimeOffset.UtcNow.AddSeconds(-120),
            Consumed = true,
            ContractInstanceId = Guid.NewGuid()
        });

        // Set WindowStart to the past so it appears expired
        var windowStartField = typeof(ItemUseBatchState).GetField("<WindowStart>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(windowStartField);
        windowStartField.SetValue(expiredBatch, DateTimeOffset.UtcNow.AddSeconds(-120));
        useBatches[batchKey] = expiredBatch;

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
                capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = userId,
            UserType = EntityType.Character
        };

        // Act - this should pre-flush the expired batch, then process the new use
        var (status, response) = await service.UseItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // The expired batch should have been published as an item.used event
        var usedEvents = capturedEvents.Where(e => e.Topic == "item.used").ToList();
        Assert.True(usedEvents.Count >= 1, "Expected at least one item.used event from expired batch flush");
        var flushedEvent = Assert.IsType<ItemUsedEvent>(usedEvents[0].Event);
        Assert.Equal(1, flushedEvent.TotalCount);

        // Clean up static state to avoid cross-test pollution
        useBatches.TryRemove(batchKey, out _);
    }

    [Fact]
    public async Task UseItemAsync_ExpiredFailureBatch_PublishesOnNextFailure()
    {
        // Arrange - set up a use that will fail via contract creation failure.
        // The template HAS a UseBehaviorContractTemplateId so UseItemAsync passes validation,
        // but CreateContractInstanceAsync throws ApiException → RecordUseFailureAsync is called.
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var contractTemplateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var template = new ItemTemplateModel
        {
            TemplateId = templateId,
            Code = "fail_use_item",
            GameId = "game1",
            Name = "Fail Use Item",
            Category = ItemCategory.Consumable,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = 10,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UseBehaviorContractTemplateId = contractTemplateId
        };
        var instance = new ItemInstanceModel
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            ContainerId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Quantity = 5,
            OriginType = ItemOriginType.Loot,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockInstanceCacheStore
            .Setup(s => s.GetAsync($"inst:{instanceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockTemplateCacheStore
            .Setup(s => s.GetAsync($"tpl:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Contract creation throws ApiException → CreateItemUseContractInstanceAsync returns null
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(It.IsAny<CreateContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Contract creation failed", 500));

        // Inject a pre-expired failure batch via reflection
        var batchKey = $"{templateId}:{userId}";
        var failureBatchesField = typeof(ItemService).GetField("_failureBatches",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(failureBatchesField);
        var failureBatches = failureBatchesField.GetValue(null) as System.Collections.Concurrent.ConcurrentDictionary<string, ItemUseFailureBatchState>;
        Assert.NotNull(failureBatches);

        var expiredBatch = new ItemUseFailureBatchState();
        expiredBatch.AddRecord(new ItemUseFailureRecord
        {
            InstanceId = Guid.NewGuid(),
            TemplateId = templateId,
            TemplateCode = "fail_use_item",
            UserId = userId,
            UserType = EntityType.Character,
            FailedAt = DateTimeOffset.UtcNow.AddSeconds(-120),
            Reason = "previous_failure"
        });

        var windowStartField = typeof(ItemUseFailureBatchState).GetField("<WindowStart>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(windowStartField);
        windowStartField.SetValue(expiredBatch, DateTimeOffset.UtcNow.AddSeconds(-120));
        failureBatches[batchKey] = expiredBatch;

        // Capture published events
        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
                capturedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        var request = new UseItemRequest
        {
            InstanceId = instanceId,
            UserId = userId,
            UserType = EntityType.Character
        };

        // Act - contract creation fails, triggering RecordUseFailureAsync which pre-flushes
        var (status, _) = await service.UseItemAsync(request);

        // Assert - the request fails (contract creation error)
        Assert.Equal(StatusCodes.BadRequest, status);

        // The expired failure batch should have been published before the new failure was recorded
        var failedEvents = capturedEvents.Where(e => e.Topic == "item.use-failed").ToList();
        Assert.True(failedEvents.Count >= 1, "Expected at least one item.use-failed event from expired batch flush");
        var flushedEvent = Assert.IsType<ItemUseFailedEvent>(failedEvents[0].Event);
        Assert.Equal(1, flushedEvent.TotalCount);

        // Clean up static state
        failureBatches.TryRemove(batchKey, out _);
    }

    #endregion
}
