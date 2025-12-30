using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Servicedata;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Servicedata.Tests;

/// <summary>
/// Unit tests for ServicedataService
/// Tests business logic with mocked IStateStoreFactory for state store operations.
/// </summary>
public class ServicedataServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ServiceDataModel>> _mockModelStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<ServicedataService>> _mockLogger;
    private readonly ServicedataServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private const string STATE_STORE = "servicedata-statestore";

    public ServicedataServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockModelStore = new Mock<IStateStore<ServiceDataModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<ServicedataService>>();
        _configuration = new ServicedataServiceConfiguration();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<ServiceDataModel>(STATE_STORE))
            .Returns(_mockModelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
    }

    private ServicedataService CreateService()
    {
        return new ServicedataService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ServicedataService(
            null!, _mockMessageBus.Object, _mockLogger.Object, _configuration, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ServicedataService(
            _mockStateStoreFactory.Object, _mockMessageBus.Object, null!, _configuration, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ServicedataService(
            _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLogger.Object, null!, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ServicedataService(
            _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLogger.Object, _configuration, null!));
    }

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
            .Setup(s => s.GetAsync("service-stub:testservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
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
            .Setup(s => s.GetAsync("service-stub:mygame", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Empty service list
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);

        // Verify service data saved with service: prefix
        _mockModelStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("service:") && k != "service-list"),
            It.IsAny<ServiceDataModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify stub name index saved
        _mockStringStore.Verify(s => s.SaveAsync(
            "service-stub:mygame",
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
            .Setup(s => s.GetAsync("service-stub:newservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Mock: Existing service list with one item
        var existingList = new List<string> { "existing-service-id" };
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingList);

        // Capture the saved list
        List<string>? savedList = null;
        _mockListStore
            .Setup(s => s.SaveAsync(
                "service-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedList = data);

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
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
            .Setup(s => s.GetAsync("service-stub:existing", It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetAsync("service-stub:mygameservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceDataModel?)null);

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
            .Setup(s => s.GetAsync("service-stub:testservice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceId.ToString());

        // Mock: Service exists
        _mockModelStore
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
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
            .Setup(s => s.GetAsync("service-stub:nonexistent", It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { serviceId1.ToString(), serviceId2.ToString() });

        // Mock: Service 1
        _mockModelStore
            .Setup(s => s.GetAsync($"service:{serviceId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId1.ToString(),
                StubName = "service1",
                DisplayName = "Service 1",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service 2
        _mockModelStore
            .Setup(s => s.GetAsync($"service:{serviceId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId2.ToString(),
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
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { serviceId1.ToString(), serviceId2.ToString() });

        // Mock: Service 1 (active)
        _mockModelStore
            .Setup(s => s.GetAsync($"service:{serviceId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId1.ToString(),
                StubName = "active-service",
                DisplayName = "Active Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service 2 (inactive)
        _mockModelStore
            .Setup(s => s.GetAsync($"service:{serviceId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId2.ToString(),
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
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Original Name",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved data
        ServiceDataModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"service:{serviceId}",
                It.IsAny<ServiceDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ServiceDataModel, StateOptions?, CancellationToken>(
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Original Name",
                IsActive = true,
                CreatedAtUnix = originalCreatedAt,
                UpdatedAtUnix = null
            });

        // Capture saved data
        ServiceDataModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"service:{serviceId}",
                It.IsAny<ServiceDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ServiceDataModel, StateOptions?, CancellationToken>(
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceDataModel?)null);

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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved data
        ServiceDataModel? savedModel = null;
        _mockModelStore
            .Setup(s => s.SaveAsync(
                $"service:{serviceId}",
                It.IsAny<ServiceDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ServiceDataModel, StateOptions?, CancellationToken>(
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { serviceId.ToString() });

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NoContent, statusCode);

        // Verify delete was called
        _mockModelStore.Verify(s => s.DeleteAsync(
            $"service:{serviceId}",
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { serviceId.ToString() });

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NoContent, statusCode);

        // Verify stub index delete was called
        _mockStringStore.Verify(s => s.DeleteAsync(
            "service-stub:testservice",
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceDataModel
            {
                ServiceId = serviceId.ToString(),
                StubName = "testservice",
                DisplayName = "Test Service",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Service list with multiple items
        _mockListStore
            .Setup(s => s.GetAsync("service-list", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { serviceId.ToString(), otherId.ToString() });

        // Capture saved list
        List<string>? savedList = null;
        _mockListStore
            .Setup(s => s.SaveAsync(
                "service-list",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<string>, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedList = data);

        // Act
        var statusCode = await service.DeleteServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NoContent, statusCode);
        Assert.NotNull(savedList);
        Assert.Single(savedList);
        Assert.Equal(otherId.ToString(), savedList[0]);
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
            .Setup(s => s.GetAsync($"service:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceDataModel?)null);

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

public class ServicedataConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new ServicedataServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_StateStoreName_ShouldHaveDefault()
    {
        // Arrange
        var config = new ServicedataServiceConfiguration();

        // Act & Assert - StateStoreName has a default value
        Assert.Equal("servicedata-statestore", config.StateStoreName);
    }
}
