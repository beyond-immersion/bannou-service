using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.GameService.Tests;

/// <summary>
/// Unit tests for GameServiceService
/// Tests business logic with mocked IStateStoreFactory for state store operations.
/// </summary>
public class GameServiceServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<GameServiceRegistryModel>> _mockModelStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<GameServiceService>> _mockLogger;
    private readonly GameServiceServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private const string STATE_STORE = "game-service-statestore";

    public GameServiceServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockModelStore = new Mock<IStateStore<GameServiceRegistryModel>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<GameServiceService>>();
        _configuration = new GameServiceServiceConfiguration();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<GameServiceRegistryModel>(STATE_STORE))
            .Returns(_mockModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);

        // Default: lock provider succeeds
        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);
    }

    private GameServiceService CreateService()
    {
        return new GameServiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockResourceClient.Object,
            _mockTelemetryProvider.Object);
    }

    #region Constructor Tests

    #endregion

    #region CreateServiceAsync Tests

    [Fact]
    public async Task CreateServiceAsync_ShouldReturnCreated_WhenValidRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "TestService",
            DisplayName = "Test Service",
            Description = "A test service",
            IsActive = true
        };

        // Mock: No existing stub name
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:testservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("testservice", response.StubName); // Normalized to lowercase
        Assert.Equal("Test Service", response.DisplayName);
        Assert.Equal("A test service", response.Description);
        Assert.True(response.IsActive);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldSaveToStateStore_WithCorrectKeys()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "MyGame",
            DisplayName = "My Game",
            IsActive = true
        };

        // Mock: No existing stub name
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:mygame", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify service data saved with game-service: prefix
        _mockModelStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("game-service:") && k != "game-service-list"),
            It.IsAny<GameServiceRegistryModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify stub name index saved
        _mockStringStore.Verify(s => s.SaveAsync(
            "game-service-stub:mygame",
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldAddToServiceListIndex()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "newservice",
            DisplayName = "New Service",
            IsActive = true
        };

        // Mock: No existing stub name
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:newservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Existing service list with one item and ETag support
        var existingList = new List<Guid> { Guid.NewGuid() };
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingList, "etag-1"));

        // Capture the saved list
        List<Guid>? savedList = null;
        _mockListStore
            .Setup(s => s.TrySaveAsync(
                "game-service-list",
                It.IsAny<List<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, string, StateOptions?, CancellationToken>(
                (key, data, etag, _, ct) => savedList = data)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedList);
        Assert.Equal(2, savedList.Count); // Should have existing + new
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldReturnConflict_WhenStubNameExists()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "existing",
            DisplayName = "Duplicate Service",
            IsActive = true
        };

        // Mock: Stub name already exists
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing-service-id");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
    }

    // NOTE: Empty StubName and DisplayName validation is enforced by schema data annotations
    // ([Required], [StringLength]) at the generated controller level, not in the service method.
    // These cases are covered by HTTP integration tests, not unit tests.

    [Fact]
    public async Task CreateServiceAsync_ShouldNormalizeStubNameToLowercase()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "MyGameService",
            DisplayName = "My Game Service",
            IsActive = true
        };

        // Mock: No existing stub name (lowercase)
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:mygameservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("mygameservice", response.StubName);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldDefaultAutoLobbyEnabledToFalse()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "nolobby",
            DisplayName = "No Lobby Game"
        };

        // Mock: No existing stub name
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:nolobby", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.AutoLobbyEnabled);
    }

    #endregion

    #region GetServiceAsync Tests

    [Fact]
    public async Task GetServiceAsync_ById_ShouldReturnService_WhenExists()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new GetServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                Description = "Test description",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAtUnix = null
            });

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(serviceId, response.ServiceId);
        Assert.Equal("testservice", response.StubName);
    }

    [Fact]
    public async Task GetServiceAsync_ById_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new GetServiceRequest { ServiceId = serviceId };

        // Mock: Service doesn't exist
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameServiceRegistryModel?)null);

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task GetServiceAsync_ByStubName_ShouldReturnService_WhenExists()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new GetServiceRequest { StubName = "testservice" };

        // Mock: Stub index returns service ID
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:testservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceId.ToString());

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("testservice", response.StubName);
    }

    [Fact]
    public async Task GetServiceAsync_ByStubName_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var request = new GetServiceRequest { StubName = "nonexistent" };

        // Mock: Stub index doesn't exist
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region ListServicesAsync Tests

    [Fact]
    public async Task ListServicesAsync_ShouldReturnAllServices()
    {
        // Arrange
        var service = CreateService();
        var serviceId1 = Guid.NewGuid();
        var serviceId2 = Guid.NewGuid();
        var request = new ListServicesRequest { ActiveOnly = false };

        // Mock: Service list with two IDs
        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { serviceId1, serviceId2 });

        // Mock: Service 1
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId1,
                StubName = "service1",
                DisplayName = "Service 1",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service 2
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId2,
                StubName = "service2",
                DisplayName = "Service 2",
                IsActive = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Services.Count);
    }

    [Fact]
    public async Task ListServicesAsync_ShouldFilterActiveOnly_WhenRequested()
    {
        // Arrange
        var service = CreateService();
        var serviceId1 = Guid.NewGuid();
        var serviceId2 = Guid.NewGuid();
        var request = new ListServicesRequest { ActiveOnly = true };

        // Mock: Service list with two IDs
        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { serviceId1, serviceId2 });

        // Mock: Service 1 (active)
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId1,
                StubName = "active-service",
                DisplayName = "Active Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service 2 (inactive)
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId2,
                StubName = "inactive-service",
                DisplayName = "Inactive Service",
                IsActive = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
        Assert.Single(response.Services);
        Assert.Equal("active-service", response.Services.First().StubName);
    }

    [Fact]
    public async Task ListServicesAsync_ShouldReturnEmptyList_WhenNoServices()
    {
        // Arrange
        var service = CreateService();
        var request = new ListServicesRequest { ActiveOnly = false };

        // Mock: Empty service list
        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Services);
        Assert.Equal(0, response.TotalCount);
    }

    #endregion

    #region UpdateServiceAsync Tests

    [Fact]
    public async Task UpdateServiceAsync_ShouldUpdateDisplayName()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "Updated Name"
        };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Original Name",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved data
        GameServiceRegistryModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"game-service:{serviceId}",
                It.IsAny<GameServiceRegistryModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, GameServiceRegistryModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.DisplayName);
        Assert.NotNull(savedModel);
        Assert.Equal("Updated Name", savedModel.DisplayName);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldSetUpdatedAtTimestamp()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var originalCreatedAt = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "Updated Name"
        };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Original Name",
                IsActive = true,
                CreatedAtUnix = originalCreatedAt,
                UpdatedAtUnix = null
            });

        // Capture saved data
        GameServiceRegistryModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"game-service:{serviceId}",
                It.IsAny<GameServiceRegistryModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, GameServiceRegistryModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.UpdatedAtUnix);
        Assert.Equal(originalCreatedAt, savedModel.CreatedAtUnix); // CreatedAt unchanged
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldReturnNotFound_WhenServiceNotExists()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "Updated Name"
        };

        // Mock: Service doesn't exist
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameServiceRegistryModel?)null);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldReturnNotFound_WhenServiceIdEmpty()
    {
        // Guid.Empty is no longer rejected as BadRequest — the service does a store lookup
        // which returns null, yielding NotFound. Schema data annotations prevent Guid.Empty
        // from reaching the service in production (controller-level validation).
        // Arrange
        var service = CreateService();
        var request = new UpdateServiceRequest
        {
            ServiceId = Guid.Empty,
            DisplayName = "Updated Name"
        };

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldUpdateIsActive()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            IsActive = false
        };

        // Mock: Service exists (active)
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved data
        GameServiceRegistryModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"game-service:{serviceId}",
                It.IsAny<GameServiceRegistryModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, GameServiceRegistryModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.IsActive);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldUpdateAutoLobbyEnabled()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            AutoLobbyEnabled = true
        };

        // Mock: Service exists (autoLobbyEnabled = false)
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                AutoLobbyEnabled = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved data
        GameServiceRegistryModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"game-service:{serviceId}",
                It.IsAny<GameServiceRegistryModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, GameServiceRegistryModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.AutoLobbyEnabled);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.AutoLobbyEnabled);
    }

    #endregion

    #region DeleteServiceAsync Tests

    [Fact]
    public async Task DeleteServiceAsync_ShouldRemoveFromStateStore()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { serviceId }, "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify delete was called
        _mockModelStore.Verify(s => s.DeleteAsync(
            $"game-service:{serviceId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldRemoveFromStubNameIndex()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists with stub name
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { serviceId }, "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify stub index delete was called
        _mockStringStore.Verify(s => s.DeleteAsync(
            "game-service-stub:testservice",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldRemoveFromServiceList()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list with multiple items and ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { serviceId, otherId }, "etag-1"));

        // Capture saved list
        List<Guid>? savedList = null;
        _mockListStore
            .Setup(s => s.TrySaveAsync(
                "game-service-list",
                It.IsAny<List<Guid>>(),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, string, StateOptions?, CancellationToken>(
                (key, data, etag, _, ct) => savedList = data)
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedList);
        Assert.Single(savedList);
        Assert.Equal(otherId, savedList[0]);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldReturnNotFound_WhenServiceNotExists()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service doesn't exist
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameServiceRegistryModel?)null);

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldReturnNotFound_WhenServiceIdEmpty()
    {
        // Guid.Empty is no longer rejected as BadRequest — the service does a store lookup
        // which returns null, yielding NotFound. Schema data annotations prevent Guid.Empty
        // from reaching the service in production (controller-level validation).
        // Arrange
        var service = CreateService();
        var request = new DeleteServiceRequest { ServiceId = Guid.Empty };

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldReturnConflict_WhenCleanupFails()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: References exist and cleanup fails
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(
                It.Is<CheckReferencesRequest>(req => req.ResourceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "game-service",
                ResourceId = serviceId,
                RefCount = 3,
                Sources = new List<ResourceReference>
                {
                    new ResourceReference { SourceType = "subscription" }
                }
            });

        _mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(
                It.Is<ExecuteCleanupRequest>(req => req.ResourceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCleanupResponse
            {
                ResourceType = "game-service",
                ResourceId = serviceId,
                Success = false,
                AbortReason = "Callback failed for subscription cleanup"
            });

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);

        // Verify service was NOT deleted (cleanup failed)
        _mockModelStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldReturnConflict_WhenResourceClientThrowsApiException()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Resource client throws ApiException (service unavailable)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(
                It.IsAny<CheckReferencesRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Resource service unavailable", 503, null, null, null));

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);

        // Verify service was NOT deleted
        _mockModelStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldPublishDeletedEvent_WithCapturedData()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var deletionReason = "No longer needed";
        var request = new DeleteServiceRequest { ServiceId = serviceId, Reason = deletionReason };

        var createdAtUnix = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds();

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "myservice",
                DisplayName = "My Service",
                Description = "Service description",
                IsActive = true,
                AutoLobbyEnabled = true,
                CreatedAtUnix = createdAtUnix
            });

        // Mock: No references (clean delete)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(
                It.IsAny<CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "game-service",
                ResourceId = serviceId,
                RefCount = 0
            });

        // Mock: Service list
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { serviceId }, "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Capture the published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>(
                (topic, evt, _) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.Equal("game-service.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<GameServiceDeletedEvent>(capturedEvent);
        Assert.Equal(serviceId, typedEvent.GameServiceId);
        Assert.Equal("myservice", typedEvent.StubName);
        Assert.Equal("My Service", typedEvent.DisplayName);
        Assert.Equal("Service description", typedEvent.Description);
        Assert.True(typedEvent.IsActive);
        Assert.True(typedEvent.AutoLobbyEnabled);
        Assert.Equal(deletionReason, typedEvent.DeletedReason);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(createdAtUnix), typedEvent.CreatedAt);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldSucceed_WhenReferencesExistButCleanupSucceeds()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new DeleteServiceRequest { ServiceId = serviceId };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: References exist
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(
                It.Is<CheckReferencesRequest>(req => req.ResourceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "game-service",
                ResourceId = serviceId,
                RefCount = 2
            });

        // Mock: Cleanup succeeds
        _mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(
                It.Is<ExecuteCleanupRequest>(req =>
                    req.ResourceId == serviceId &&
                    req.CleanupPolicy == CleanupPolicy.AllRequired),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCleanupResponse
            {
                ResourceType = "game-service",
                ResourceId = serviceId,
                Success = true
            });

        // Mock: Service list
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { serviceId }, "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify actual deletion happened
        _mockModelStore.Verify(s => s.DeleteAsync(
            $"game-service:{serviceId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockStringStore.Verify(s => s.DeleteAsync(
            "game-service-stub:testservice", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CreateServiceAsync Edge Case Tests

    [Fact]
    public async Task CreateServiceAsync_ShouldReturnConflict_WhenLockAcquisitionFails()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "contested",
            DisplayName = "Contested Service",
            IsActive = true
        };

        // Mock: Lock acquisition fails (concurrent create)
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        failedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);

        // Verify no state was saved (lock was not acquired)
        _mockModelStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<GameServiceRegistryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockStringStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldPublishCreatedEvent_WithCapturedData()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "EventGame",
            DisplayName = "Event Game",
            Description = "A game for testing events",
            IsActive = true,
            AutoLobbyEnabled = true
        };

        // Mock: No existing stub name
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:eventgame", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list with ETag support
        _mockListStore
            .Setup(s => s.GetWithETagAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Capture the published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>(
                (topic, evt, _) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("game-service.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<GameServiceCreatedEvent>(capturedEvent);
        Assert.Equal(response.ServiceId, typedEvent.GameServiceId);
        Assert.Equal("eventgame", typedEvent.StubName);
        Assert.Equal("Event Game", typedEvent.DisplayName);
        Assert.Equal("A game for testing events", typedEvent.Description);
        Assert.True(typedEvent.IsActive);
        Assert.True(typedEvent.AutoLobbyEnabled);
        Assert.True(typedEvent.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    #endregion

    #region UpdateServiceAsync Edge Case Tests

    [Fact]
    public async Task UpdateServiceAsync_ShouldReturnOkWithoutSaving_WhenNoChanges()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "Unchanged Name" // Same as existing
        };

        // Mock: Service exists with same values
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Unchanged Name",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("Unchanged Name", response.DisplayName);

        // Verify NO save was called (nothing changed)
        _mockModelStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<GameServiceRegistryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify NO event was published (nothing changed)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldReturnOkWithoutSaving_WhenNullableFieldsNotProvided()
    {
        // Arrange: request has only ServiceId set, no optional fields
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId
            // All optional fields are null/default
        };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify NO save was called
        _mockModelStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<GameServiceRegistryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldUpdateMultipleFields_AndTrackChangedFields()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "New Name",
            Description = "New Description",
            IsActive = false,
            AutoLobbyEnabled = true
        };

        // Mock: Service exists with different values
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Old Name",
                Description = "Old Description",
                IsActive = true,
                AutoLobbyEnabled = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });

        // Capture saved model
        GameServiceRegistryModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"game-service:{serviceId}",
                It.IsAny<GameServiceRegistryModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, GameServiceRegistryModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>(
                (topic, evt, _) =>
                {
                    capturedTopic = topic;
                    capturedEvent = evt;
                })
            .ReturnsAsync(true);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("New Name", response.DisplayName);
        Assert.Equal("New Description", response.Description);
        Assert.False(response.IsActive);
        Assert.True(response.AutoLobbyEnabled);

        // Verify saved model
        Assert.NotNull(savedModel);
        Assert.Equal("New Name", savedModel.DisplayName);
        Assert.Equal("New Description", savedModel.Description);
        Assert.False(savedModel.IsActive);
        Assert.True(savedModel.AutoLobbyEnabled);
        Assert.NotNull(savedModel.UpdatedAtUnix);

        // Verify event published with correct changedFields
        Assert.Equal("game-service.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<GameServiceUpdatedEvent>(capturedEvent);
        Assert.Equal(serviceId, typedEvent.GameServiceId);
        Assert.Contains("displayName", typedEvent.ChangedFields);
        Assert.Contains("description", typedEvent.ChangedFields);
        Assert.Contains("isActive", typedEvent.ChangedFields);
        Assert.Contains("autoLobbyEnabled", typedEvent.ChangedFields);
        Assert.Equal(4, typedEvent.ChangedFields.Count);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldOnlyTrackActuallyChangedFields()
    {
        // Arrange: DisplayName changes, IsActive stays the same
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new UpdateServiceRequest
        {
            ServiceId = serviceId,
            DisplayName = "New Name",
            IsActive = true // Same as existing
        };

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "testservice",
                DisplayName = "Old Name",
                IsActive = true, // Same as request
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture published event
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>(
                (topic, evt, _) => capturedEvent = evt)
            .ReturnsAsync(true);

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<GameServiceUpdatedEvent>(capturedEvent);
        Assert.Single(typedEvent.ChangedFields);
        Assert.Contains("displayName", typedEvent.ChangedFields);
        Assert.DoesNotContain("isActive", typedEvent.ChangedFields);
    }

    #endregion

    #region GetServiceAsync Edge Case Tests

    [Fact]
    public async Task GetServiceAsync_ShouldPreferServiceId_WhenBothProvided()
    {
        // Arrange: Both ServiceId and StubName provided; ServiceId takes precedence
        var service = CreateService();
        var serviceIdResult = Guid.NewGuid();
        var request = new GetServiceRequest
        {
            ServiceId = serviceIdResult,
            StubName = "shouldbeignored"
        };

        // Mock: Service found by ID
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceIdResult}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceIdResult,
                StubName = "foundbyid",
                DisplayName = "Found By ID",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(serviceIdResult, response.ServiceId);
        Assert.Equal("foundbyid", response.StubName);

        // Verify stub index was never queried (ServiceId takes precedence)
        _mockStringStore.Verify(s => s.GetAsync(
            It.Is<string>(k => k.StartsWith("game-service-stub:")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetServiceAsync_ShouldReturnNotFound_WhenStubIndexExistsButModelMissing()
    {
        // Arrange: Stub index points to a service ID, but the model no longer exists (orphaned index)
        var service = CreateService();
        var orphanedId = Guid.NewGuid();
        var request = new GetServiceRequest { StubName = "orphaned" };

        // Mock: Stub index returns a service ID
        _mockStringStore
            .Setup(s => s.GetAsync("game-service-stub:orphaned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(orphanedId.ToString());

        // Mock: But the model is gone
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{orphanedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameServiceRegistryModel?)null);

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetServiceAsync_ShouldReturnNotFound_WhenNeitherIdNorStubNameProvided()
    {
        // Arrange: Empty request (both fields null)
        var service = CreateService();
        var request = new GetServiceRequest();

        // Act
        var (statusCode, response) = await service.GetServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ListServicesAsync Edge Case Tests

    [Fact]
    public async Task ListServicesAsync_ShouldApplyPagination_SkipAndTake()
    {
        // Arrange
        var service = CreateService();
        var serviceIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var request = new ListServicesRequest
        {
            ActiveOnly = false,
            Skip = 1,
            Take = 2
        };

        // Mock: Service list with 5 items
        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceIds);

        // Mock: All 5 services exist
        for (var i = 0; i < serviceIds.Count; i++)
        {
            var id = serviceIds[i];
            _mockModelStore
                .Setup(s => s.GetAsync($"game-service:{id}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GameServiceRegistryModel
                {
                    ServiceId = id,
                    StubName = $"service{i}",
                    DisplayName = $"Service {i}",
                    IsActive = true,
                    CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
        }

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(5, response.TotalCount); // Total matching count (before pagination)
        Assert.Equal(2, response.Services.Count); // Only 2 returned (Take=2)
        Assert.Equal("service1", response.Services.ElementAt(0).StubName); // Skip=1, so starts at index 1
        Assert.Equal("service2", response.Services.ElementAt(1).StubName);
    }

    [Fact]
    public async Task ListServicesAsync_ShouldSkipMissingServices_InIndex()
    {
        // Arrange: Index has 3 IDs, but one is missing from the store (orphaned index entry)
        var service = CreateService();
        var validId1 = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var validId2 = Guid.NewGuid();
        var request = new ListServicesRequest { ActiveOnly = false };

        // Mock: Service list with 3 IDs
        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { validId1, missingId, validId2 });

        // Mock: First service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{validId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = validId1,
                StubName = "service1",
                DisplayName = "Service 1",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Second service is MISSING from store (null return)
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{missingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameServiceRegistryModel?)null);

        // Mock: Third service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{validId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = validId2,
                StubName = "service3",
                DisplayName = "Service 3",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount); // Only 2 found (missing one skipped)
        Assert.Equal(2, response.Services.Count);
        Assert.Equal("service1", response.Services.ElementAt(0).StubName);
        Assert.Equal("service3", response.Services.ElementAt(1).StubName);
    }

    [Fact]
    public async Task ListServicesAsync_ShouldApplyFilterBeforePagination()
    {
        // Arrange: 3 services, 2 active, skip=0, take=1, activeOnly=true
        // TotalCount should be 2 (filtered count), not 3 (all count)
        var service = CreateService();
        var activeId1 = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var activeId2 = Guid.NewGuid();
        var request = new ListServicesRequest
        {
            ActiveOnly = true,
            Skip = 0,
            Take = 1
        };

        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeId1, inactiveId, activeId2 });

        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{activeId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = activeId1,
                StubName = "active1",
                DisplayName = "Active 1",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{inactiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = inactiveId,
                StubName = "inactive",
                DisplayName = "Inactive",
                IsActive = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{activeId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = activeId2,
                StubName = "active2",
                DisplayName = "Active 2",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount); // Total active count (not total overall)
        Assert.Single(response.Services); // Take=1
        Assert.Equal("active1", response.Services.First().StubName);
    }

    [Fact]
    public async Task ListServicesAsync_ShouldReturnEmpty_WhenSkipExceedsTotalCount()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var request = new ListServicesRequest
        {
            ActiveOnly = false,
            Skip = 100,
            Take = 50
        };

        _mockListStore
            .Setup(s => s.GetAsync("game-service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { serviceId });

        _mockModelStore
            .Setup(s => s.GetAsync($"game-service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameServiceRegistryModel
            {
                ServiceId = serviceId,
                StubName = "onlyservice",
                DisplayName = "Only Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.ListServicesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount); // Total count reflects all matching
        Assert.Empty(response.Services); // But pagination yields empty
    }

    #endregion
}

public class GameServiceConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new GameServiceServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

}
