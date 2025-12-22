using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

#pragma warning disable CS8620 // Argument of type cannot be used for parameter of type due to differences in the nullability

namespace BeyondImmersion.BannouService.Location.Tests;

/// <summary>
/// Comprehensive unit tests for LocationService.
/// Tests location management operations including CRUD, hierarchy, deprecation, and error handling.
/// Note: Tests using StateEntry (CreateLocation, DeleteLocation with index updates) are best
/// covered by HTTP integration tests due to StateEntry mocking complexity.
/// </summary>
public class LocationServiceTests : ServiceTestBase<LocationServiceConfiguration>
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<LocationService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "location-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string LOCATION_KEY_PREFIX = "location:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string REALM_INDEX_PREFIX = "realm-index:";
    private const string PARENT_INDEX_PREFIX = "parent-index:";
    private const string ROOT_LOCATIONS_PREFIX = "root-locations:";

    public LocationServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<LocationService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Default realm validation to pass
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });
    }

    private LocationService CreateService()
    {
        return new LocationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object);
    }

    /// <summary>
    /// Creates a test LocationModel for use in tests.
    /// </summary>
    private static LocationService.LocationModel CreateTestLocationModel(
        Guid? locationId = null,
        Guid? realmId = null,
        string code = "TEST",
        string name = "Test Location",
        string locationType = "CITY",
        Guid? parentLocationId = null,
        int depth = 0,
        bool isDeprecated = false)
    {
        var id = locationId ?? Guid.NewGuid();
        var realm = realmId ?? Guid.NewGuid();
        return new LocationService.LocationModel
        {
            LocationId = id.ToString(),
            RealmId = realm.ToString(),
            Code = code,
            Name = name,
            Description = "Test Description",
            LocationType = locationType,
            ParentLocationId = parentLocationId?.ToString(),
            Depth = depth,
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow : null,
            DeprecationReason = isDeprecated ? "Test reason" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocationService(
            null!,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocationService(
            _mockDaprClient.Object,
            null!,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            null!,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullRealmClient_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocationService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            null!,
            _mockEventConsumer.Object));
    }

    #endregion

    #region GetLocation Tests

    [Fact]
    public async Task GetLocationAsync_WhenLocationExists_ShouldReturnLocation()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new GetLocationRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, "OMEGA_CITY", "Omega City");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal("OMEGA_CITY", response.Code);
        Assert.Equal("Omega City", response.Name);
    }

    [Fact]
    public async Task GetLocationAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new GetLocationRequest { LocationId = locationId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.GetLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetLocationAsync_WhenDaprFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new GetLocationRequest { LocationId = locationId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ThrowsAsync(new Exception("Dapr connection failed"));

        // Act
        var (status, response) = await service.GetLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "location", "GetLocation", "unexpected_exception", It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    }

    #endregion

    #region GetLocationByCode Tests

    [Fact]
    public async Task GetLocationByCodeAsync_WhenCodeExists_ShouldReturnLocation()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new GetLocationByCodeRequest { RealmId = realmId, Code = "omega_city" };
        var testModel = CreateTestLocationModel(locationId, realmId, "OMEGA_CITY", "Omega City");

        // Setup code index lookup
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, $"{CODE_INDEX_PREFIX}{realmId}:OMEGA_CITY", null, null, default))
            .ReturnsAsync(locationId.ToString());

        // Setup location retrieval
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetLocationByCodeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("OMEGA_CITY", response.Code);
    }

    [Fact]
    public async Task GetLocationByCodeAsync_WhenCodeNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetLocationByCodeRequest { RealmId = realmId, Code = "NONEXISTENT" };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, $"{CODE_INDEX_PREFIX}{realmId}:NONEXISTENT", null, null, default))
            .Returns(Task.FromResult<string?>(null));

        // Act
        var (status, response) = await service.GetLocationByCodeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetLocationByCodeAsync_WhenDaprFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetLocationByCodeRequest { RealmId = realmId, Code = "TEST" };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, It.IsAny<string>(), null, null, default))
            .ThrowsAsync(new Exception("State store unavailable"));

        // Act
        var (status, response) = await service.GetLocationByCodeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    #endregion

    #region ListLocationsByRealm Tests

    [Fact]
    public async Task ListLocationsByRealmAsync_WhenLocationsExist_ShouldReturnList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var location1Id = Guid.NewGuid();
        var location2Id = Guid.NewGuid();
        var request = new ListLocationsByRealmRequest { RealmId = realmId };

        var locationIds = new List<string> { location1Id.ToString(), location2Id.ToString() };
        var model1 = CreateTestLocationModel(location1Id, realmId, "LOC1", "Location 1");
        var model2 = CreateTestLocationModel(location2Id, realmId, "LOC2", "Location 2");

        // Setup realm index lookup
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{REALM_INDEX_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(locationIds);

        // Setup location retrieval
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{location1Id}", null, null, default))
            .ReturnsAsync(model1);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{location2Id}", null, null, default))
            .ReturnsAsync(model2);

        // Act
        var (status, response) = await service.ListLocationsByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
    }

    [Fact]
    public async Task ListLocationsByRealmAsync_WhenNoLocations_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new ListLocationsByRealmRequest { RealmId = realmId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{REALM_INDEX_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync((List<string>?)null);

        // Act
        var (status, response) = await service.ListLocationsByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    #endregion

    #region LocationExists Tests

    [Fact]
    public async Task LocationExistsAsync_WhenActiveLocationExists_ShouldReturnExistsTrue()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new LocationExistsRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: false);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.LocationExistsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Exists);
        Assert.True(response.IsActive);
    }

    [Fact]
    public async Task LocationExistsAsync_WhenDeprecatedLocationExists_ShouldReturnExistsTrueNotActive()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new LocationExistsRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.LocationExistsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Exists);
        Assert.False(response.IsActive);
    }

    [Fact]
    public async Task LocationExistsAsync_WhenLocationDoesNotExist_ShouldReturnExistsFalse()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new LocationExistsRequest { LocationId = locationId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.LocationExistsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Exists);
    }

    #endregion

    #region UpdateLocation Tests

    [Fact]
    public async Task UpdateLocationAsync_WhenLocationExists_ShouldUpdateAndReturnOK()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UpdateLocationRequest
        {
            LocationId = locationId,
            Name = "Updated Name",
            Description = "Updated Description"
        };
        var testModel = CreateTestLocationModel(locationId, realmId, "TEST", "Original Name");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UpdateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);

        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}",
            It.IsAny<LocationService.LocationModel>(), null, null, default), Times.Once);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "location.updated", It.IsAny<LocationUpdatedEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateLocationAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new UpdateLocationRequest { LocationId = locationId, Name = "Updated" };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.UpdateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateLocationAsync_WithNoChanges_ShouldNotPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UpdateLocationRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UpdateLocationAsync(request);

        // Assert - no changes means no save and no event
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}",
            It.IsAny<LocationService.LocationModel>(), null, null, default), Times.Never);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "location.updated", It.IsAny<LocationUpdatedEvent>(), default), Times.Never);
    }

    #endregion

    #region DeprecateLocation Tests

    [Fact]
    public async Task DeprecateLocationAsync_WhenValid_ShouldDeprecateAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeprecateLocationRequest
        {
            LocationId = locationId,
            Reason = "No longer in use"
        };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: false);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.DeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("No longer in use", response.DeprecationReason);

        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "location.updated", It.IsAny<LocationUpdatedEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task DeprecateLocationAsync_WhenAlreadyDeprecated_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeprecateLocationRequest { LocationId = locationId, Reason = "New reason" };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.DeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeprecateLocationAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new DeprecateLocationRequest { LocationId = locationId, Reason = "Test" };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.DeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region UndeprecateLocation Tests

    [Fact]
    public async Task UndeprecateLocationAsync_WhenDeprecated_ShouldRestoreAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateLocationRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
        Assert.Null(response.DeprecationReason);

        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "location.updated", It.IsAny<LocationUpdatedEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task UndeprecateLocationAsync_WhenNotDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateLocationRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: false);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UndeprecateLocationAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new UndeprecateLocationRequest { LocationId = locationId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{locationId}", null, null, default))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListRootLocations Tests

    [Fact]
    public async Task ListRootLocationsAsync_WhenRootLocationsExist_ShouldReturnList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var loc1Id = Guid.NewGuid();
        var loc2Id = Guid.NewGuid();
        var request = new ListRootLocationsRequest { RealmId = realmId };

        var rootLocationIds = new List<string> { loc1Id.ToString(), loc2Id.ToString() };
        var model1 = CreateTestLocationModel(loc1Id, realmId, "ROOT1", "Root 1", depth: 0);
        var model2 = CreateTestLocationModel(loc2Id, realmId, "ROOT2", "Root 2", depth: 0);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{ROOT_LOCATIONS_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(rootLocationIds);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{loc1Id}", null, null, default))
            .ReturnsAsync(model1);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{loc2Id}", null, null, default))
            .ReturnsAsync(model2);

        // Act
        var (status, response) = await service.ListRootLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
    }

    [Fact]
    public async Task ListRootLocationsAsync_WhenNoRootLocations_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new ListRootLocationsRequest { RealmId = realmId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{ROOT_LOCATIONS_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync((List<string>?)null);

        // Act
        var (status, response) = await service.ListRootLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    #endregion

    #region ListLocationsByParent Tests

    [Fact]
    public async Task ListLocationsByParentAsync_WhenChildrenExist_ShouldReturnList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var request = new ListLocationsByParentRequest { ParentLocationId = parentId };

        var parentModel = CreateTestLocationModel(parentId, realmId, "PARENT", "Parent Location");
        var childIds = new List<string> { child1Id.ToString(), child2Id.ToString() };
        var model1 = CreateTestLocationModel(child1Id, realmId, "CHILD1", "Child 1", parentLocationId: parentId, depth: 1);
        var model2 = CreateTestLocationModel(child2Id, realmId, "CHILD2", "Child 2", parentLocationId: parentId, depth: 1);

        // Setup parent location retrieval (needed to get realm)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{parentId}", null, null, default))
            .ReturnsAsync(parentModel);

        // Setup parent index lookup
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{PARENT_INDEX_PREFIX}{realmId}:{parentId}", null, null, default))
            .ReturnsAsync(childIds);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{child1Id}", null, null, default))
            .ReturnsAsync(model1);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{child2Id}", null, null, default))
            .ReturnsAsync(model2);

        // Act
        var (status, response) = await service.ListLocationsByParentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
    }

    [Fact]
    public async Task ListLocationsByParentAsync_WhenNoChildren_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var request = new ListLocationsByParentRequest { ParentLocationId = parentId };

        var parentModel = CreateTestLocationModel(parentId, realmId, "PARENT", "Parent Location");

        // Setup parent location retrieval (needed to get realm)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<LocationService.LocationModel>(
                STATE_STORE, $"{LOCATION_KEY_PREFIX}{parentId}", null, null, default))
            .ReturnsAsync(parentModel);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, $"{PARENT_INDEX_PREFIX}{realmId}:{parentId}", null, null, default))
            .ReturnsAsync((List<string>?)null);

        // Act
        var (status, response) = await service.ListLocationsByParentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    #endregion
}

/// <summary>
/// Tests for LocationServiceConfiguration binding and defaults.
/// </summary>
public class LocationConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        var config = new LocationServiceConfiguration();
        Assert.NotNull(config);
    }
}
