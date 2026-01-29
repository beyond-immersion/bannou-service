using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<GameServiceRegistryModel>(STATE_STORE))
            .Returns(_mockModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
    }

    private GameServiceService CreateService()
    {
        return new GameServiceService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object);
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
    /// </summary>
    [Fact]
    public void GameServiceService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<GameServiceService>();

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
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

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
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

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
                It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, string, CancellationToken>(
                (key, data, etag, ct) => savedList = data)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldReturnBadRequest_WhenStubNameEmpty()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "",
            DisplayName = "Test Service",
            IsActive = true
        };

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
    }

    [Fact]
    public async Task CreateServiceAsync_ShouldReturnBadRequest_WhenDisplayNameEmpty()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateServiceRequest
        {
            StubName = "testservice",
            DisplayName = "",
            IsActive = true
        };

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
    }

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
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("mygameservice", response.StubName);
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
        var (statusCode, response) = await service.GetServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.GetServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.GetServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.GetServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.ListServicesAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.ListServicesAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.ListServicesAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.UpdateServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.UpdateServiceAsync(request, CancellationToken.None);

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
        var (statusCode, response) = await service.UpdateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task UpdateServiceAsync_ShouldReturnBadRequest_WhenServiceIdEmpty()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateServiceRequest
        {
            ServiceId = Guid.Empty,
            DisplayName = "Updated Name"
        };

        // Act
        var (statusCode, response) = await service.UpdateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
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
        var (statusCode, response) = await service.UpdateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.IsActive);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
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
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

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
            .Setup(s => s.TrySaveAsync("game-service-list", It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

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
                It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, string, CancellationToken>(
                (key, data, etag, ct) => savedList = data)
            .ReturnsAsync("etag-2");

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

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
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task DeleteServiceAsync_ShouldReturnBadRequest_WhenServiceIdEmpty()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteServiceRequest { ServiceId = Guid.Empty };

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
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
