using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
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
        _mockLogger = new Mock<ILogger<ItemService>>();

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

        // Act
        await service.CreateItemTemplateAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "item-template.created",
            It.Is<ItemTemplateCreatedEvent>(e =>
                e.Code == request.Code &&
                e.GameId == request.GameId &&
                e.Name == request.Name),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_UsesDefaultMaxStackSize_WhenNotProvided()
    {
        // Arrange
        Configuration.DefaultMaxStackSize = 50;
        var service = CreateService();
        var request = CreateValidTemplateRequest();
        request.MaxStackSize = 0;

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
        Assert.Equal(50, savedModel.MaxStackSize);
    }

    [Fact]
    public async Task CreateItemTemplateAsync_UsesDefaultRarity_WhenNotProvided()
    {
        // Arrange
        Configuration.DefaultRarity = "epic";
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
        Configuration.DefaultWeightPrecision = "integer";
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
        Configuration.DefaultSoulboundType = "on_pickup";
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
        Assert.Equal(SoulboundType.On_pickup, savedModel.SoulboundType);
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
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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

        var request = new UpdateItemTemplateRequest { TemplateId = templateId, Name = "Updated" };

        // Act
        await service.UpdateItemTemplateAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "item-template.updated",
            It.Is<ItemTemplateUpdatedEvent>(e => e.TemplateId == templateId),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "item-instance.created",
            It.Is<ItemInstanceCreatedEvent>(e =>
                e.TemplateId == templateId &&
                e.ContainerId == containerId &&
                e.RealmId == realmId),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ItemInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new BindItemInstanceRequest
        {
            InstanceId = instanceId,
            CharacterId = characterId,
            BindType = SoulboundType.On_pickup
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.BoundToId);
        Assert.NotNull(response.BoundAt);
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
            BindType = SoulboundType.On_equip
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
            BindType = SoulboundType.On_equip
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
            BindType = SoulboundType.On_pickup
        };

        // Act
        var (status, response) = await service.BindItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
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
        _mockInstanceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new DestroyItemInstanceRequest
        {
            InstanceId = instanceId,
            Reason = "player_discard"
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Destroyed);
        Assert.Equal(instanceId, response.InstanceId);
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
            Reason = "player_discard"
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
            Reason = "admin"
        };

        // Act
        var (status, response) = await service.DestroyItemInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Destroyed);
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
            Reason = "player_discard"
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

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredInstanceModel(id1));
        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredInstanceModel(id2));

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

        foreach (var id in ids)
        {
            _mockInstanceStore
                .Setup(s => s.GetAsync($"inst:{id}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateStoredInstanceModel(id));
        }

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
        var otherId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        _mockInstanceStringStore
            .Setup(s => s.GetAsync($"inst-template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { matchId.ToString(), otherId.ToString() }));

        var matchModel = CreateStoredInstanceModel(matchId);
        matchModel.RealmId = targetRealm;
        var otherModel = CreateStoredInstanceModel(otherId);
        otherModel.RealmId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{matchId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchModel);
        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{otherId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherModel);

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

        _mockInstanceStringStore
            .Setup(s => s.GetAsync($"inst-template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(ids.Select(id => id.ToString()).ToList()));

        foreach (var id in ids)
        {
            _mockInstanceStore
                .Setup(s => s.GetAsync($"inst:{id}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateStoredInstanceModel(id));
        }

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

        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{foundId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredInstanceModel(foundId));
        _mockInstanceStore
            .Setup(s => s.GetAsync($"inst:{notFoundId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemInstanceModel?)null);

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
            WeightPrecision = WeightPrecision.Decimal_2,
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
            WeightPrecision = WeightPrecision.Decimal_2,
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
}
