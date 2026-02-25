using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Inventory.Tests;

/// <summary>
/// Unit tests for InventoryService.
/// Tests container management, item operations, and constraint enforcement.
/// </summary>
public class InventoryServiceTests : ServiceTestBase<InventoryServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ContainerModel>> _mockContainerStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IItemClient> _mockItemClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<InventoryService>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public InventoryServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockContainerStore = new Mock<IStateStore<ContainerModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockItemClient = new Mock<IItemClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<InventoryService>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory
            .Setup(f => f.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerStore))
            .Returns(_mockContainerStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(StateStoreDefinitions.InventoryContainerStore))
            .Returns(_mockStringStore.Object);

        // Setup container cache store (cache operations are non-fatal so this can return same mock)
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ContainerModel>(StateStoreDefinitions.InventoryContainerCache))
            .Returns(_mockContainerStore.Object);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        Configuration.DefaultMaxSlots = 20;
        Configuration.DefaultMaxWeight = 100.0;
        Configuration.DefaultMaxNestingDepth = 3;
        Configuration.DefaultWeightContribution = WeightContribution.SelfPlusContents;
        Configuration.EnableLazyContainerCreation = true;
    }

    private InventoryService CreateService()
    {
        return new InventoryService(
            _mockMessageBus.Object,
            _mockItemClient.Object,
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            Configuration,
            _mockTelemetryProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void InventoryService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<InventoryService>();

    #endregion

    #region CreateContainer Tests

    [Fact]
    public async Task CreateContainerAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.ContainerType, response.ContainerType);
        Assert.Equal(request.OwnerId, response.OwnerId);
        Assert.Equal(ContainerOwnerType.Character, response.OwnerType);
        Assert.Equal(ContainerConstraintModel.SlotOnly, response.ConstraintModel);
        Assert.NotNull(savedModel);
        Assert.Equal("inventory", savedModel.ContainerType);
    }

    [Fact]
    public async Task CreateContainerAsync_WithMaxSlots_UsesProvidedValue()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxSlots = 50;

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await service.CreateContainerAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(50, savedModel.MaxSlots);
    }

    [Fact]
    public async Task CreateContainerAsync_NoMaxSlots_UsesConfigDefault()
    {
        // Arrange
        Configuration.DefaultMaxSlots = 30;
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxSlots = null;

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await service.CreateContainerAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(30, savedModel.MaxSlots);
    }

    [Fact]
    public async Task CreateContainerAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();

        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await service.CreateContainerAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-container.created",
            It.Is<InventoryContainerCreatedEvent>(e =>
                e.OwnerId == request.OwnerId &&
                e.ContainerType == request.ContainerType &&
                e.ConstraintModel == ContainerConstraintModel.SlotOnly),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_WithParent_SetsNestingDepth()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var parentModel = CreateStoredContainerModel(parentId);
        parentModel.NestingDepth = 1;
        parentModel.MaxNestingDepth = 5;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = CreateValidContainerRequest();
        request.ParentContainerId = parentId;

        // Act
        var (status, _) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.Equal(2, savedModel.NestingDepth);
    }

    [Fact]
    public async Task CreateContainerAsync_ExceedsMaxNesting_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var parentId = Guid.NewGuid();
        var parentModel = CreateStoredContainerModel(parentId);
        parentModel.NestingDepth = 3;
        parentModel.MaxNestingDepth = 3;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        var request = CreateValidContainerRequest();
        request.ParentContainerId = parentId;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_ParentNotFound_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        _mockContainerStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = CreateValidContainerRequest();
        request.ParentContainerId = Guid.NewGuid();

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_NegativeMaxSlots_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxSlots = -5;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_ZeroMaxSlots_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxSlots = 0;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_NegativeMaxWeight_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxWeight = -10.0;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_NegativeMaxVolume_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.MaxVolume = -1.0;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_NegativeGridWidth_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.GridWidth = -2;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_NegativeGridHeight_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.GridHeight = -3;

        // Act
        var (status, response) = await service.CreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateContainerAsync_DefaultWeightContribution_AppliedWhenNone()
    {
        // Arrange
        Configuration.DefaultWeightContribution = WeightContribution.SelfOnly;
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.WeightContribution = WeightContribution.None;

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await service.CreateContainerAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(WeightContribution.SelfOnly, savedModel.WeightContribution);
    }

    [Fact]
    public async Task CreateContainerAsync_ExplicitWeightContribution_NotOverridden()
    {
        // Arrange
        Configuration.DefaultWeightContribution = WeightContribution.SelfOnly;
        var service = CreateService();
        var request = CreateValidContainerRequest();
        request.WeightContribution = WeightContribution.SelfPlusContents;

        ContainerModel? savedModel = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await service.CreateContainerAsync(request);

        // Assert
        Assert.NotNull(savedModel);
        Assert.Equal(WeightContribution.SelfPlusContents, savedModel.WeightContribution);
    }

    #endregion

    #region GetContainer Tests

    [Fact]
    public async Task GetContainerAsync_Found_ReturnsContainer()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>(),
                TotalCount = 0
            });

        var request = new GetContainerRequest { ContainerId = containerId, IncludeContents = true };

        // Act
        var (status, response) = await service.GetContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(containerId, response.Container.ContainerId);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetContainerAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockContainerStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new GetContainerRequest { ContainerId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetContainerAsync_WithContents_ReturnsItems()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);
        var templateId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(
                It.Is<ListItemsByContainerRequest>(r => r.ContainerId == containerId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse
                    {
                        InstanceId = instanceId,
                        TemplateId = templateId,
                        ContainerId = containerId,
                        Quantity = 5,
                        SlotIndex = 0
                    }
                },
                TotalCount = 1
            });

        var request = new GetContainerRequest { ContainerId = containerId, IncludeContents = true };

        // Act
        var (status, response) = await service.GetContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(instanceId, response.Items.First().InstanceId);
        Assert.Equal(5, response.Items.First().Quantity);
    }

    #endregion

    #region GetOrCreateContainer Tests

    [Fact]
    public async Task GetOrCreateContainerAsync_ExistingContainer_ReturnsExisting()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);
        model.OwnerId = ownerId;
        model.OwnerType = ContainerOwnerType.Character;
        model.ContainerType = "inventory";

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new GetOrCreateContainerRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "inventory",
            ConstraintModel = ContainerConstraintModel.SlotOnly
        };

        // Act
        var (status, response) = await service.GetOrCreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(containerId, response.ContainerId);
        // Should NOT have created a new container
        _mockContainerStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateContainerAsync_NoExisting_CreatesNew()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new GetOrCreateContainerRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "bank",
            ConstraintModel = ContainerConstraintModel.SlotAndWeight,
            MaxSlots = 40,
            MaxWeight = 200
        };

        // Act
        var (status, response) = await service.GetOrCreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("bank", response.ContainerType);
        Assert.Equal(ownerId, response.OwnerId);
    }

    [Fact]
    public async Task GetOrCreateContainerAsync_LazyCreationDisabled_ReturnsBadRequest()
    {
        // Arrange
        Configuration.EnableLazyContainerCreation = false;
        var service = CreateService();

        var request = new GetOrCreateContainerRequest
        {
            OwnerId = Guid.NewGuid(),
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "inventory",
            ConstraintModel = ContainerConstraintModel.SlotOnly
        };

        // Act
        var (status, response) = await service.GetOrCreateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateContainer Tests

    [Fact]
    public async Task UpdateContainerAsync_ValidRequest_UpdatesFields()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new UpdateContainerRequest
        {
            ContainerId = containerId,
            MaxSlots = 50,
            MaxWeight = 200
        };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(50, response.MaxSlots);
        Assert.Equal(200, response.MaxWeight);
    }

    [Fact]
    public async Task UpdateContainerAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockContainerStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new UpdateContainerRequest { ContainerId = Guid.NewGuid(), MaxSlots = 10 };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateContainerAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new UpdateContainerRequest { ContainerId = containerId, MaxSlots = 30 };

        // Act
        await service.UpdateContainerAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-container.updated",
            It.Is<InventoryContainerUpdatedEvent>(e => e.ContainerId == containerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_NegativeMaxSlots_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateContainerRequest { ContainerId = Guid.NewGuid(), MaxSlots = -1 };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateContainerAsync_NegativeMaxWeight_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateContainerRequest { ContainerId = Guid.NewGuid(), MaxWeight = -5.0 };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateContainerAsync_NegativeMaxVolume_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateContainerRequest { ContainerId = Guid.NewGuid(), MaxVolume = -1.0 };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateContainerAsync_ZeroGridWidth_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateContainerRequest { ContainerId = Guid.NewGuid(), GridWidth = 0 };

        // Act
        var (status, response) = await service.UpdateContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region DeleteContainer Tests

    [Fact]
    public async Task DeleteContainerAsync_EmptyContainer_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockContainerStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>(),
                TotalCount = 0
            });
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new DeleteContainerRequest
        {
            ContainerId = containerId,
            ItemHandling = ItemHandling.Error
        };

        // Act
        var (status, response) = await service.DeleteContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.ItemsHandled);
    }

    [Fact]
    public async Task DeleteContainerAsync_NotEmpty_ErrorHandling_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = Guid.NewGuid(), Quantity = 1 }
                },
                TotalCount = 1
            });

        var request = new DeleteContainerRequest
        {
            ContainerId = containerId,
            ItemHandling = ItemHandling.Error
        };

        // Act
        var (status, response) = await service.DeleteContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteContainerAsync_NotEmpty_DestroyHandling_DestroysItems()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var model = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
        _mockContainerStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = instanceId, TemplateId = Guid.NewGuid(), Quantity = 1 }
                },
                TotalCount = 1
            });
        _mockItemClient
            .Setup(c => c.DestroyItemInstanceAsync(It.IsAny<DestroyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestroyItemInstanceResponse { TemplateId = Guid.NewGuid() });
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new DeleteContainerRequest
        {
            ContainerId = containerId,
            ItemHandling = ItemHandling.Destroy
        };

        // Act
        var (status, response) = await service.DeleteContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ItemsHandled);
        _mockItemClient.Verify(c => c.DestroyItemInstanceAsync(
            It.Is<DestroyItemInstanceRequest>(r => r.InstanceId == instanceId && r.Reason == DestroyReason.Destroyed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteContainerAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockContainerStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new DeleteContainerRequest { ContainerId = Guid.NewGuid(), ItemHandling = ItemHandling.Error };

        // Act
        var (status, _) = await service.DeleteContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListContainers Tests

    [Fact]
    public async Task ListContainersAsync_ReturnsOwnedContainers()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { id1.ToString(), id2.ToString() }));

        var model1 = CreateStoredContainerModel(id1);
        model1.ContainerType = "inventory";
        var model2 = CreateStoredContainerModel(id2);
        model2.ContainerType = "bank";

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model1);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model2);

        var request = new ListContainersRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character
        };

        // Act
        var (status, response) = await service.ListContainersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Containers.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListContainersAsync_FilterByType_ReturnsFiltered()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { id1.ToString(), id2.ToString() }));

        var model1 = CreateStoredContainerModel(id1);
        model1.ContainerType = "inventory";
        var model2 = CreateStoredContainerModel(id2);
        model2.ContainerType = "bank";

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{id1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model1);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{id2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model2);

        var request = new ListContainersRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "bank"
        };

        // Act
        var (status, response) = await service.ListContainersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Containers);
        Assert.Equal("bank", response.Containers.First().ContainerType);
    }

    [Fact]
    public async Task ListContainersAsync_ExcludeEquipmentSlots_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { id1.ToString(), id2.ToString() }));

        var inventoryModel = CreateStoredContainerModel(id1);
        inventoryModel.IsEquipmentSlot = false;
        var equipModel = CreateStoredContainerModel(id2);
        equipModel.IsEquipmentSlot = true;

        _mockContainerStore.Setup(s => s.GetAsync($"cont:{id1}", It.IsAny<CancellationToken>())).ReturnsAsync(inventoryModel);
        _mockContainerStore.Setup(s => s.GetAsync($"cont:{id2}", It.IsAny<CancellationToken>())).ReturnsAsync(equipModel);

        var request = new ListContainersRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            IncludeEquipmentSlots = false
        };

        // Act
        var (status, response) = await service.ListContainersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Containers);
        Assert.False(response.Containers.First().IsEquipmentSlot);
    }

    #endregion

    #region AddItemToContainer Tests

    [Fact]
    public async Task AddItemToContainerAsync_ValidRequest_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.UsedSlots = 5;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId, SlotIndex = 6 };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_SlotsFull_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.MaxSlots = 10;
        container.UsedSlots = 10;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_WeightExceeded_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.WeightOnly;
        container.MaxWeight = 50;
        container.ContentsWeight = 45;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var heavyTemplate = CreateTestTemplate(templateId);
        heavyTemplate.Weight = 10;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(heavyTemplate);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_ForbiddenCategory_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ForbiddenCategories = new List<string> { "Weapon" };

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var weaponTemplate = CreateTestTemplate(templateId);
        weaponTemplate.Category = ItemCategory.Weapon;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(weaponTemplate);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_AllowedCategoryMismatch_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.AllowedCategories = new List<string> { "Consumable" };

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var weaponTemplate = CreateTestTemplate(templateId);
        weaponTemplate.Category = ItemCategory.Weapon;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(weaponTemplate);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_CaseInsensitiveCategory_AllowedCategories()
    {
        // Arrange - category in allowed list is different case from item category
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.AllowedCategories = new List<string> { "consumable" }; // lowercase

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var template = CreateTestTemplate(templateId);
        template.Category = ItemCategory.Consumable; // Will produce "Consumable" via ToString()
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert - should succeed because comparison is case-insensitive
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_CaseInsensitiveCategory_ForbiddenCategories()
    {
        // Arrange - forbidden category is different case from item category
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ForbiddenCategories = new List<string> { "weapon" }; // lowercase

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var weaponTemplate = CreateTestTemplate(templateId);
        weaponTemplate.Category = ItemCategory.Weapon; // "Weapon" via ToString()
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(weaponTemplate);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert - should be rejected because comparison is case-insensitive
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_UnlimitedConstraint_AlwaysSucceeds()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.Unlimited;
        container.UsedSlots = 9999;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 100 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        var (status, response) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task AddItemToContainerAsync_ContainerNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockContainerStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new AddItemRequest { InstanceId = Guid.NewGuid(), ContainerId = Guid.NewGuid() };

        // Act
        var (status, _) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task AddItemToContainerAsync_ItemNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        var request = new AddItemRequest { InstanceId = Guid.NewGuid(), ContainerId = containerId };

        // Act
        var (status, _) = await service.AddItemToContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task AddItemToContainerAsync_PublishesPlacedEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 3 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId, SlotIndex = 2 };

        // Act
        await service.AddItemToContainerAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-item.placed",
            It.Is<InventoryItemPlacedEvent>(e =>
                e.InstanceId == instanceId &&
                e.ContainerId == containerId &&
                e.Quantity == 3 &&
                e.SlotIndex == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemToContainerAsync_UpdatesContainerWeight()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.SlotAndWeight;
        container.ContentsWeight = 10;
        container.MaxWeight = 100;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        ContainerModel? savedContainer = null;
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerModel, StateOptions?, CancellationToken>((_, m, _, _) => savedContainer = m)
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 2 });

        var template = CreateTestTemplate(templateId);
        template.Weight = 5.0;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        await service.AddItemToContainerAsync(request);

        // Assert
        Assert.NotNull(savedContainer);
        Assert.Equal(20, savedContainer.ContentsWeight); // 10 + (5.0 * 2)
    }

    [Fact]
    public async Task AddItemToContainerAsync_ContainerFull_EmitsContainerFullEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.SlotOnly;
        container.MaxSlots = 5;
        container.UsedSlots = 4; // One slot left - after adding, will be full

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        await service.AddItemToContainerAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-container.full",
            It.Is<InventoryContainerFullEvent>(e =>
                e.ContainerId == containerId &&
                e.ConstraintType == ConstraintLimitType.Slots),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemToContainerAsync_ContainerNotFull_DoesNotEmitFullEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.SlotOnly;
        container.MaxSlots = 10;
        container.UsedSlots = 3; // Plenty of room left

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        await service.AddItemToContainerAsync(request);

        // Assert - container full event should NOT be published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-container.full",
            It.IsAny<InventoryContainerFullEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemToContainerAsync_WeightFull_EmitsContainerFullEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.ConstraintModel = ContainerConstraintModel.WeightOnly;
        container.MaxWeight = 50;
        container.ContentsWeight = 45; // Adding 5 weight will fill it

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var template = CreateTestTemplate(templateId);
        template.Weight = 5.0;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new AddItemRequest { InstanceId = instanceId, ContainerId = containerId };

        // Act
        await service.AddItemToContainerAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-container.full",
            It.Is<InventoryContainerFullEvent>(e =>
                e.ContainerId == containerId &&
                e.ConstraintType == ConstraintLimitType.Weight),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RemoveItem Tests

    [Fact]
    public async Task RemoveItemFromContainerAsync_ValidRequest_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.UsedSlots = 5;
        container.ContentsWeight = 10.0;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = containerId, Quantity = 1 });

        var template = CreateTestTemplate(templateId);
        template.Weight = 2.0;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var request = new RemoveItemRequest { InstanceId = instanceId };

        // Act
        var (status, response) = await service.RemoveItemFromContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(containerId, response.PreviousContainerId);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-item.removed",
            It.Is<InventoryItemRemovedEvent>(e =>
                e.InstanceId == instanceId &&
                e.ContainerId == containerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveItemFromContainerAsync_ItemNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", null, null));

        var request = new RemoveItemRequest { InstanceId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.RemoveItemFromContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveItemFromContainerAsync_ContainerNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = containerId, Quantity = 1 });

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new RemoveItemRequest { InstanceId = instanceId };

        // Act
        var (status, response) = await service.RemoveItemFromContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveItemFromContainerAsync_LockFailure_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = containerId, Quantity = 1 });

        // Override default lock to fail
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.Is<string>(k => k == containerId.ToString()),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var request = new RemoveItemRequest { InstanceId = instanceId };

        // Act
        var (status, response) = await service.RemoveItemFromContainerAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RemoveItemFromContainerAsync_DecrementsSlotAndWeight()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container = CreateStoredContainerModel(containerId);
        container.UsedSlots = 5;
        container.ContentsWeight = 15.0;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = containerId, Quantity = 2 });

        var template = CreateTestTemplate(templateId);
        template.Weight = 3.0;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 2 });

        var request = new RemoveItemRequest { InstanceId = instanceId };

        // Act
        await service.RemoveItemFromContainerAsync(request);

        // Assert - container saved with decremented values
        _mockContainerStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<ContainerModel>(c => c.UsedSlots == 4 && c.ContentsWeight == 9.0),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region MoveItem Tests

    [Fact]
    public async Task MoveItemAsync_SameContainer_PersistsSlotPositionAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var containerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = containerId,
                Quantity = 1,
                SlotIndex = 2,
                SlotX = 0,
                SlotY = 0
            });

        var request = new MoveItemRequest { InstanceId = instanceId, TargetContainerId = containerId, TargetSlotIndex = 5, TargetSlotX = 3, TargetSlotY = 1 };

        // Act
        var (status, response) = await service.MoveItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(containerId, response.SourceContainerId);
        Assert.Equal(5, response.SlotIndex);
        Assert.Equal(3, response.SlotX);
        Assert.Equal(1, response.SlotY);

        // Verify slot position was persisted via lib-item
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r =>
                r.InstanceId == instanceId &&
                r.NewSlotIndex == 5 &&
                r.NewSlotX == 3 &&
                r.NewSlotY == 1),
            It.IsAny<CancellationToken>()), Times.Once);

        // Move event published for same-container slot change
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-item.moved",
            It.Is<InventoryItemMovedEvent>(e =>
                e.InstanceId == instanceId &&
                e.SourceContainerId == containerId &&
                e.TargetContainerId == containerId &&
                e.PreviousSlotIndex == 2 &&
                e.NewSlotIndex == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveItemAsync_DifferentContainer_Success()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var targetContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var sourceContainer = CreateStoredContainerModel(sourceContainerId);
        sourceContainer.UsedSlots = 3;
        sourceContainer.ContentsWeight = 5.0;

        var targetContainer = CreateStoredContainerModel(targetContainerId);
        targetContainer.UsedSlots = 2;

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{sourceContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceContainer);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{targetContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetContainer);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 1 });

        var template = CreateTestTemplate(templateId);
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 1 });

        var request = new MoveItemRequest { InstanceId = instanceId, TargetContainerId = targetContainerId };

        // Act
        var (status, response) = await service.MoveItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(sourceContainerId, response.SourceContainerId);

        // Move event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "inventory-item.moved",
            It.Is<InventoryItemMovedEvent>(e =>
                e.InstanceId == instanceId &&
                e.SourceContainerId == sourceContainerId &&
                e.TargetContainerId == targetContainerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveItemAsync_ItemNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", null, null));

        var request = new MoveItemRequest { InstanceId = Guid.NewGuid(), TargetContainerId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.MoveItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MoveItemAsync_TargetContainerNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var targetContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 1 });

        // Target container doesn't exist
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{targetContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerModel?)null);

        var request = new MoveItemRequest { InstanceId = instanceId, TargetContainerId = targetContainerId };

        // Act
        var (status, response) = await service.MoveItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MoveItemAsync_TargetContainerFull_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var targetContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var targetContainer = CreateStoredContainerModel(targetContainerId);
        targetContainer.MaxSlots = 5;
        targetContainer.UsedSlots = 5; // Full

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{targetContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetContainer);

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 1 });

        var template = CreateTestTemplate(templateId);
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new MoveItemRequest { InstanceId = instanceId, TargetContainerId = targetContainerId };

        // Act
        var (status, response) = await service.MoveItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region TransferItem Tests

    [Fact]
    public async Task TransferItemAsync_NonTradeable_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = Guid.NewGuid(),
                Quantity = 1
            });

        var nonTradeableTemplate = CreateTestTemplate(templateId);
        nonTradeableTemplate.Tradeable = false;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nonTradeableTemplate);

        var request = new TransferItemRequest
        {
            InstanceId = instanceId,
            TargetContainerId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.TransferItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferItemAsync_BoundItem_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = Guid.NewGuid(),
                Quantity = 1,
                BoundToId = Guid.NewGuid()
            });

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new TransferItemRequest
        {
            InstanceId = instanceId,
            TargetContainerId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.TransferItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferItemAsync_FullTransfer_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var targetContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var sourceContainer = CreateStoredContainerModel(sourceContainerId);
        sourceContainer.OwnerId = ownerId;
        sourceContainer.UsedSlots = 3;
        var targetContainer = CreateStoredContainerModel(targetContainerId);
        targetContainer.OwnerId = Guid.NewGuid(); // Different owner

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{sourceContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceContainer);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{targetContainerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetContainer);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 5 });

        var template = CreateTestTemplate(templateId);
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 5 });

        var request = new TransferItemRequest
        {
            InstanceId = instanceId,
            TargetContainerId = targetContainerId
        };

        // Act
        var (status, response) = await service.TransferItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(instanceId, response.InstanceId);
        Assert.Equal(sourceContainerId, response.SourceContainerId);
        Assert.Equal(5, response.QuantityTransferred);
    }

    [Fact]
    public async Task TransferItemAsync_LockFailure_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 1 });

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        // Override lock to fail on the source container
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.Is<string>(k => k == sourceContainerId.ToString()),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var request = new TransferItemRequest
        {
            InstanceId = instanceId,
            TargetContainerId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.TransferItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferItemAsync_ExceedsAvailableQuantity_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceContainerId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, ContainerId = sourceContainerId, Quantity = 5 });

        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(templateId));

        var request = new TransferItemRequest
        {
            InstanceId = instanceId,
            TargetContainerId = Guid.NewGuid(),
            Quantity = 10 // More than available (5)
        };

        // Act
        var (status, response) = await service.TransferItemAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region SplitStack Tests

    [Fact]
    public async Task SplitStackAsync_ValidSplit_CreatesTwoStacks()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var newInstanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = containerId,
                RealmId = Guid.NewGuid(),
                Quantity = 10
            });

        var template = CreateTestTemplate(templateId);
        template.QuantityModel = QuantityModel.Discrete;
        template.MaxStackSize = 99;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 7 });
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = newInstanceId, TemplateId = templateId, Quantity = 3 });

        var request = new SplitStackRequest { InstanceId = instanceId, Quantity = 3 };

        // Act
        var (status, response) = await service.SplitStackAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(newInstanceId, response.NewInstanceId);
        Assert.Equal(7, response.OriginalQuantity); // 10 - 3
        Assert.Equal(3, response.NewQuantity);
    }

    [Fact]
    public async Task SplitStackAsync_CallsModifyWithNegativeQuantityDelta()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = Guid.NewGuid(),
                RealmId = Guid.NewGuid(),
                Quantity = 20
            });

        var template = CreateTestTemplate(templateId);
        template.QuantityModel = QuantityModel.Discrete;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = 12 });
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 8 });

        var request = new SplitStackRequest { InstanceId = instanceId, Quantity = 8 };

        // Act
        await service.SplitStackAsync(request);

        // Assert - Verify ModifyItemInstance was called with negative QuantityDelta
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r =>
                r.InstanceId == instanceId &&
                r.QuantityDelta == -8),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SplitStackAsync_CreateFails_RestoresOriginalQuantity()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                ContainerId = Guid.NewGuid(),
                RealmId = Guid.NewGuid(),
                Quantity = 10
            });

        var template = CreateTestTemplate(templateId);
        template.QuantityModel = QuantityModel.Discrete;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // First call: reduce original quantity (succeeds)
        // Second call: restore original quantity (rollback)
        var modifyCalls = 0;
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                modifyCalls++;
                return new ItemInstanceResponse { InstanceId = instanceId, TemplateId = templateId, Quantity = modifyCalls == 1 ? 7 : 10 };
            });

        // CreateItemInstance fails
        _mockItemClient
            .Setup(c => c.CreateItemInstanceAsync(It.IsAny<CreateItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Failed", 500));

        var request = new SplitStackRequest { InstanceId = instanceId, Quantity = 3 };

        // Act
        var (status, response) = await service.SplitStackAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        // Verify rollback: ModifyItemInstance called twice - first to reduce, then to restore
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r => r.InstanceId == instanceId && r.QuantityDelta == -3),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r => r.InstanceId == instanceId && r.QuantityDelta == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SplitStackAsync_QuantityEqualsTotal_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = Guid.NewGuid(),
                Quantity = 5
            });

        var request = new SplitStackRequest { InstanceId = instanceId, Quantity = 5 };

        // Act
        var (status, _) = await service.SplitStackAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SplitStackAsync_UniqueItem_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse
            {
                InstanceId = instanceId,
                TemplateId = templateId,
                Quantity = 5
            });

        var template = CreateTestTemplate(templateId);
        template.QuantityModel = QuantityModel.Unique;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new SplitStackRequest { InstanceId = instanceId, Quantity = 2 };

        // Act
        var (status, _) = await service.SplitStackAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region MergeStacks Tests

    [Fact]
    public async Task MergeStacksAsync_SameTemplate_MergesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = templateId, ContainerId = containerId, Quantity = 5 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, ContainerId = containerId, Quantity = 8 });

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 99;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 13 });
        _mockItemClient
            .Setup(c => c.DestroyItemInstanceAsync(It.IsAny<DestroyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestroyItemInstanceResponse { TemplateId = templateId });

        var containerModel = CreateStoredContainerModel(containerId);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerModel);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        var (status, response) = await service.MergeStacksAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(13, response.NewQuantity); // 5 + 8
        Assert.True(response.SourceDestroyed);
        Assert.Null(response.OverflowQuantity);
    }

    [Fact]
    public async Task MergeStacksAsync_UpdatesTargetViaModifyItemInstance()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = templateId, ContainerId = containerId, Quantity = 7 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, ContainerId = containerId, Quantity = 3 });

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 99;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 10 });
        _mockItemClient
            .Setup(c => c.DestroyItemInstanceAsync(It.IsAny<DestroyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestroyItemInstanceResponse { TemplateId = templateId });

        var containerModel = CreateStoredContainerModel(containerId);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerModel);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        await service.MergeStacksAsync(request);

        // Assert - Verify target update via ModifyItemInstance with positive QuantityDelta
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r =>
                r.InstanceId == targetId &&
                r.QuantityDelta == 7),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MergeStacksAsync_FullMerge_DestroysSource()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = templateId, ContainerId = containerId, Quantity = 5 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, ContainerId = containerId, Quantity = 3 });

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 99;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 8 });
        _mockItemClient
            .Setup(c => c.DestroyItemInstanceAsync(It.IsAny<DestroyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestroyItemInstanceResponse { TemplateId = templateId });

        var containerModel = CreateStoredContainerModel(containerId);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerModel);
        _mockContainerStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContainerModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        await service.MergeStacksAsync(request);

        // Assert - source destroyed with "merged" reason
        _mockItemClient.Verify(c => c.DestroyItemInstanceAsync(
            It.Is<DestroyItemInstanceRequest>(r =>
                r.InstanceId == sourceId &&
                r.Reason == DestroyReason.Consumed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MergeStacksAsync_ExceedsMaxStack_ReportsOverflow()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = templateId, Quantity = 15 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 18 });

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 20;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 20 });

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        var (status, response) = await service.MergeStacksAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(20, response.NewQuantity);
        Assert.Equal(13, response.OverflowQuantity); // 15 + 18 - 20 = 13
    }

    [Fact]
    public async Task MergeStacksAsync_PartialMerge_ReducesSourceViaModify()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = templateId, Quantity = 10 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 15 });

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 20;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockItemClient
            .Setup(c => c.ModifyItemInstanceAsync(It.IsAny<ModifyItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = templateId, Quantity = 20 });

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        var (status, response) = await service.MergeStacksAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.SourceDestroyed); // Source not destroyed because of overflow
        Assert.Equal(5, response.OverflowQuantity); // 10 + 15 - 20 = 5

        // Verify source was reduced via ModifyItemInstance (not destroyed)
        _mockItemClient.Verify(c => c.ModifyItemInstanceAsync(
            It.Is<ModifyItemInstanceRequest>(r =>
                r.InstanceId == sourceId &&
                r.QuantityDelta == -5), // Only moved 5 to target (20-15=5)
            It.IsAny<CancellationToken>()), Times.Once);

        // Source should NOT be destroyed
        _mockItemClient.Verify(c => c.DestroyItemInstanceAsync(
            It.Is<DestroyItemInstanceRequest>(r => r.InstanceId == sourceId),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MergeStacksAsync_DifferentTemplates_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == sourceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = sourceId, TemplateId = Guid.NewGuid(), Quantity = 5 });
        _mockItemClient
            .Setup(c => c.GetItemInstanceAsync(It.Is<GetItemInstanceRequest>(r => r.InstanceId == targetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = targetId, TemplateId = Guid.NewGuid(), Quantity = 5 });

        var request = new MergeStacksRequest { SourceInstanceId = sourceId, TargetInstanceId = targetId };

        // Act
        var (status, _) = await service.MergeStacksAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region CountItems Tests

    [Fact]
    public async Task CountItemsAsync_SumsQuantitiesAcrossContainers()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var container1 = Guid.NewGuid();
        var container2 = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { container1.ToString(), container2.ToString() }));

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{container1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(container1));
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{container2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(container2));

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.Is<ListItemsByContainerRequest>(r => r.ContainerId == container1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 5 }
                },
                TotalCount = 1
            });
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.Is<ListItemsByContainerRequest>(r => r.ContainerId == container2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 8 }
                },
                TotalCount = 1
            });

        var request = new CountItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = templateId
        };

        // Act
        var (status, response) = await service.CountItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(13, response.TotalQuantity); // 5 + 8
        Assert.Equal(2, response.StackCount);
    }

    [Fact]
    public async Task CountItemsAsync_NoContainers_ReturnsZero()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();

        // No containers for owner
        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new CountItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.CountItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.TotalQuantity);
        Assert.Equal(0, response.StackCount);
    }

    [Fact]
    public async Task CountItemsAsync_FiltersToSpecificTemplate()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var targetTemplateId = Guid.NewGuid();
        var otherTemplateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(containerId));

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = targetTemplateId, Quantity = 3 },
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = otherTemplateId, Quantity = 10 }
                },
                TotalCount = 2
            });

        var request = new CountItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = targetTemplateId
        };

        // Act
        var (status, response) = await service.CountItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalQuantity); // Only target template counted
        Assert.Equal(1, response.StackCount);
    }

    #endregion

    #region HasItems Tests

    [Fact]
    public async Task HasItemsAsync_AllSatisfied_ReturnsHasAllTrue()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(containerId));
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 10 }
                },
                TotalCount = 1
            });

        var request = new HasItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            Requirements = new List<ItemRequirement>
            {
                new ItemRequirement { TemplateId = templateId, Quantity = 5 }
            }
        };

        // Act
        var (status, response) = await service.HasItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasAll);
        Assert.Single(response.Results);
        Assert.True(response.Results.First().Satisfied);
    }

    [Fact]
    public async Task HasItemsAsync_NotEnough_ReturnsHasAllFalse()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(containerId));
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 3 }
                },
                TotalCount = 1
            });

        var request = new HasItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            Requirements = new List<ItemRequirement>
            {
                new ItemRequirement { TemplateId = templateId, Quantity = 10 }
            }
        };

        // Act
        var (status, response) = await service.HasItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasAll);
        Assert.Single(response.Results);
        Assert.False(response.Results.First().Satisfied);
        Assert.Equal(3, response.Results.First().Available);
    }

    #endregion

    #region QueryItems Tests

    [Fact]
    public async Task QueryItemsAsync_ReturnsItemsAcrossContainers()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var container1 = Guid.NewGuid();
        var container2 = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { container1.ToString(), container2.ToString() }));

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{container1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(container1));
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{container2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(container2));

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.Is<ListItemsByContainerRequest>(r => r.ContainerId == container1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 5, SlotIndex = 0 }
                },
                TotalCount = 1
            });
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.Is<ListItemsByContainerRequest>(r => r.ContainerId == container2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 3, SlotIndex = 1 }
                },
                TotalCount = 1
            });

        var request = new QueryItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            Offset = 0,
            Limit = 100
        };

        // Act
        var (status, response) = await service.QueryItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task QueryItemsAsync_FilterByTemplateId()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var targetTemplateId = Guid.NewGuid();
        var otherTemplateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(containerId));

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = targetTemplateId, Quantity = 5 },
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = otherTemplateId, Quantity = 10 }
                },
                TotalCount = 2
            });

        var request = new QueryItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = targetTemplateId,
            Offset = 0,
            Limit = 100
        };

        // Act
        var (status, response) = await service.QueryItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
        Assert.Single(response.Items);
        Assert.Equal(targetTemplateId, response.Items.First().TemplateId);
    }

    [Fact]
    public async Task QueryItemsAsync_PaginationApplied()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoredContainerModel(containerId));

        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 1 },
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 2 },
                    new ItemInstanceResponse { InstanceId = Guid.NewGuid(), TemplateId = templateId, Quantity = 3 }
                },
                TotalCount = 3
            });

        var request = new QueryItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            Offset = 1,
            Limit = 1
        };

        // Act
        var (status, response) = await service.QueryItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalCount); // Total count is unaffected by pagination
        Assert.Single(response.Items); // Only 1 item returned (limit=1)
    }

    [Fact]
    public async Task QueryItemsAsync_NoContainers_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new QueryItemsRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            Offset = 0,
            Limit = 100
        };

        // Act
        var (status, response) = await service.QueryItemsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
    }

    #endregion

    #region FindSpace Tests

    [Fact]
    public async Task FindSpaceAsync_SpaceAvailable_ReturnsCandidates()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        var container = CreateStoredContainerModel(containerId);
        container.UsedSlots = 5; // Has space (max 20)
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        var template = CreateTestTemplate(templateId);
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Return empty items list for stackable check
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse { Items = new List<ItemInstanceResponse>(), TotalCount = 0 });

        var request = new FindSpaceRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = templateId,
            Quantity = 1
        };

        // Act
        var (status, response) = await service.FindSpaceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasSpace);
        Assert.Single(response.Candidates);
        Assert.Equal(containerId, response.Candidates.First().ContainerId);
    }

    [Fact]
    public async Task FindSpaceAsync_NoSpaceAvailable_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        var container = CreateStoredContainerModel(containerId);
        container.MaxSlots = 5;
        container.UsedSlots = 5; // Full
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        var template = CreateTestTemplate(templateId);
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new FindSpaceRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = templateId,
            Quantity = 1
        };

        // Act
        var (status, response) = await service.FindSpaceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasSpace);
        Assert.Empty(response.Candidates);
    }

    [Fact]
    public async Task FindSpaceAsync_CategoryRestriction_FiltersContainers()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var allowedContainer = Guid.NewGuid();
        var forbiddenContainer = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { allowedContainer.ToString(), forbiddenContainer.ToString() }));

        var container1 = CreateStoredContainerModel(allowedContainer);
        container1.AllowedCategories = new List<string> { "Consumable" };
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{allowedContainer}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container1);

        var container2 = CreateStoredContainerModel(forbiddenContainer);
        container2.AllowedCategories = new List<string> { "Equipment" };
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{forbiddenContainer}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container2);

        var template = CreateTestTemplate(templateId);
        template.Category = ItemCategory.Consumable;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Return empty items list for stackable check (PreferStackable defaults to true)
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse { Items = new List<ItemInstanceResponse>(), TotalCount = 0 });

        var request = new FindSpaceRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = templateId,
            Quantity = 1
        };

        // Act
        var (status, response) = await service.FindSpaceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasSpace);
        Assert.Single(response.Candidates);
        Assert.Equal(allowedContainer, response.Candidates.First().ContainerId);
    }

    [Fact]
    public async Task FindSpaceAsync_PreferStackable_FindsExistingStack()
    {
        // Arrange
        var service = CreateService();
        var ownerId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var existingStackId = Guid.NewGuid();

        _mockStringStore
            .Setup(s => s.GetAsync($"cont-owner:Character:{ownerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BannouJson.Serialize(new List<string> { containerId.ToString() }));

        var container = CreateStoredContainerModel(containerId);
        _mockContainerStore
            .Setup(s => s.GetAsync($"cont:{containerId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(container);

        var template = CreateTestTemplate(templateId);
        template.MaxStackSize = 99;
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Existing stack with room
        _mockItemClient
            .Setup(c => c.ListItemsByContainerAsync(It.IsAny<ListItemsByContainerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListItemsResponse
            {
                Items = new List<ItemInstanceResponse>
                {
                    new ItemInstanceResponse { InstanceId = existingStackId, TemplateId = templateId, Quantity = 50 }
                },
                TotalCount = 1
            });

        var request = new FindSpaceRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            TemplateId = templateId,
            Quantity = 10,
            PreferStackable = true
        };

        // Act
        var (status, response) = await service.FindSpaceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasSpace);
        Assert.Single(response.Candidates);
        Assert.Equal(existingStackId, response.Candidates.First().ExistingStackInstanceId);
        Assert.Equal(10, response.Candidates.First().CanFitQuantity);
    }

    [Fact]
    public async Task FindSpaceAsync_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockItemClient
            .Setup(c => c.GetItemTemplateAsync(It.IsAny<GetItemTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", null, null));

        var request = new FindSpaceRequest
        {
            OwnerId = Guid.NewGuid(),
            OwnerType = ContainerOwnerType.Character,
            TemplateId = Guid.NewGuid(),
            Quantity = 1
        };

        // Act
        var (status, response) = await service.FindSpaceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void InventoryServiceConfiguration_CanBeInstantiated()
    {
        var config = new InventoryServiceConfiguration();
        Assert.NotNull(config);
    }

    [Fact]
    public void InventoryServiceConfiguration_HasCorrectDefaults()
    {
        var config = new InventoryServiceConfiguration();
        Assert.Equal(3, config.DefaultMaxNestingDepth);
        Assert.Equal(WeightContribution.SelfPlusContents, config.DefaultWeightContribution);
        Assert.Equal(300, config.ContainerCacheTtlSeconds);
        Assert.Equal(30, config.LockTimeoutSeconds);
        Assert.True(config.EnableLazyContainerCreation);
        Assert.Equal(20, config.DefaultMaxSlots);
        Assert.Equal(100.0, config.DefaultMaxWeight);
        Assert.Equal(15, config.ListLockTimeoutSeconds);
        Assert.Equal(10000, config.MaxCountQueryLimit);
        Assert.Equal(200, config.QueryPageSize);
    }

    #endregion

    #region Helper Methods

    private static CreateContainerRequest CreateValidContainerRequest()
    {
        return new CreateContainerRequest
        {
            OwnerId = Guid.NewGuid(),
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "inventory",
            ConstraintModel = ContainerConstraintModel.SlotOnly,
            MaxSlots = 20,
            WeightContribution = WeightContribution.SelfPlusContents,
            SelfWeight = 1.0
        };
    }

    private static ContainerModel CreateStoredContainerModel(Guid containerId)
    {
        return new ContainerModel
        {
            ContainerId = containerId,
            OwnerId = Guid.NewGuid(),
            OwnerType = ContainerOwnerType.Character,
            ContainerType = "inventory",
            ConstraintModel = ContainerConstraintModel.SlotOnly,
            IsEquipmentSlot = false,
            MaxSlots = 20,
            UsedSlots = 0,
            MaxWeight = 100,
            ContentsWeight = 0,
            NestingDepth = 0,
            MaxNestingDepth = 3,
            SelfWeight = 1.0,
            WeightContribution = WeightContribution.SelfPlusContents,
            SlotCost = 1,
            Tags = new List<string>(),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    private static ItemTemplateResponse CreateTestTemplate(Guid templateId)
    {
        return new ItemTemplateResponse
        {
            TemplateId = templateId,
            Code = "test_item",
            GameId = "game1",
            Name = "Test Item",
            Category = ItemCategory.Consumable,
            Rarity = ItemRarity.Common,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = 99,
            Weight = 1.5,
            Tradeable = true,
            Destroyable = true,
            IsActive = true,
            IsDeprecated = false,
            Tags = new List<string>()
        };
    }

    #endregion
}
