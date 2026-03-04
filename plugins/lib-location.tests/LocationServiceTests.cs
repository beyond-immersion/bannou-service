using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<LocationService.LocationModel>> _mockLocationStore;
    private readonly Mock<IStateStore<LocationService.LocationModel>> _mockLocationCacheStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<LocationService>> _mockLogger;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;

    private const string STATE_STORE = "location-statestore";
    private const string CACHE_STORE = "location-cache";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string LOCATION_KEY_PREFIX = "location:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string REALM_INDEX_PREFIX = "realm-index:";
    private const string PARENT_INDEX_PREFIX = "parent-index:";
    private const string ROOT_LOCATIONS_PREFIX = "root-locations:";

    public LocationServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLocationStore = new Mock<IStateStore<LocationService.LocationModel>>();
        _mockLocationCacheStore = new Mock<IStateStore<LocationService.LocationModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<LocationService>>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();

        // Default lock provider behavior - always succeed with proper disposable
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        mockLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LocationService.LocationModel>(STATE_STORE))
            .Returns(_mockLocationStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LocationService.LocationModel>(CACHE_STORE))
            .Returns(_mockLocationCacheStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Default bulk operation behaviors for location store
        _mockLocationStore
            .Setup(s => s.SaveBulkAsync(It.IsAny<IEnumerable<KeyValuePair<string, LocationService.LocationModel>>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockLocationStore
            .Setup(s => s.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Default bulk operation behaviors for cache store
        _mockLocationCacheStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LocationService.LocationModel>());
        _mockLocationCacheStore
            .Setup(s => s.DeleteBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Default message bus behavior - 3-param convenience overload (what services actually call)
        // Moq doesn't call through default interface implementations, so we must mock this overload
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default realm validation to pass
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });
    }

    private LocationService CreateService()
    {
        return new LocationService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockRealmClient.Object,
            _mockLockProvider.Object,
            _mockResourceClient.Object,
            _mockTelemetryProvider.Object,
            _mockEntitySessionRegistry.Object);
    }

    /// <summary>
    /// Creates a test LocationModel for use in tests.
    /// </summary>
    private static LocationService.LocationModel CreateTestLocationModel(
        Guid? locationId = null,
        Guid? realmId = null,
        string code = "TEST",
        string name = "Test Location",
        LocationType locationType = LocationType.City,
        Guid? parentLocationId = null,
        int depth = 0,
        bool isDeprecated = false)
    {
        var id = locationId ?? Guid.NewGuid();
        var realm = realmId ?? Guid.NewGuid();
        return new LocationService.LocationModel
        {
            LocationId = id,
            RealmId = realm,
            Code = code,
            Name = name,
            Description = "Test Description",
            LocationType = locationType,
            ParentLocationId = parentLocationId,
            Depth = depth,
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow : null,
            DeprecationReason = isDeprecated ? "Test reason" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
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
    public void LocationService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<LocationService>();

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
        var testModel = CreateTestLocationModel(locationId, realmId, "TEST_CITY", "Test City");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal("TEST_CITY", response.Code);
        Assert.Equal("Test City", response.Name);
    }

    [Fact]
    public async Task GetLocationAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new GetLocationRequest { LocationId = locationId };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.GetLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
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
        var request = new GetLocationByCodeRequest { RealmId = realmId, Code = "test_city" };
        var testModel = CreateTestLocationModel(locationId, realmId, "TEST_CITY", "Test City");

        // Setup code index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}{realmId}:TEST_CITY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationId.ToString());

        // Setup location retrieval
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetLocationByCodeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("TEST_CITY", response.Code);
    }

    [Fact]
    public async Task GetLocationByCodeAsync_WhenCodeNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetLocationByCodeRequest { RealmId = realmId, Code = "NONEXISTENT" };

        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}{realmId}:NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetLocationByCodeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
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

        var locationIds = new List<Guid> { location1Id, location2Id };
        var model1 = CreateTestLocationModel(location1Id, realmId, "LOC1", "Location 1");
        var model2 = CreateTestLocationModel(location2Id, realmId, "LOC2", "Location 2");

        // Setup realm index lookup
        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationIds);

        // Setup bulk location retrieval
        _mockLocationStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LocationService.LocationModel>
            {
                [$"{LOCATION_KEY_PREFIX}{location1Id}"] = model1,
                [$"{LOCATION_KEY_PREFIX}{location2Id}"] = model2
            });

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

        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UpdateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);

        _mockLocationStore.Verify(s => s.SaveAsync(
            $"{LOCATION_KEY_PREFIX}{locationId}",
            It.IsAny<LocationService.LocationModel>(), null, It.IsAny<CancellationToken>()), Times.Once);
        // 3-param convenience overload (what services actually call)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "location.updated", It.IsAny<LocationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateLocationAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new UpdateLocationRequest { LocationId = locationId, Name = "Updated" };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UpdateLocationAsync(request);

        // Assert - no changes means no save and no event
        Assert.Equal(StatusCodes.OK, status);
        _mockLocationStore.Verify(s => s.SaveAsync(
            $"{LOCATION_KEY_PREFIX}{locationId}",
            It.IsAny<LocationService.LocationModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
        // 3-param convenience overload (what services actually call)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "location.updated", It.IsAny<LocationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.DeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("No longer in use", response.DeprecationReason);

        // 3-param convenience overload (what services actually call)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "location.updated", It.IsAny<LocationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeprecateLocationAsync_WhenAlreadyDeprecated_ShouldReturnOkIdempotent()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeprecateLocationRequest { LocationId = locationId, Reason = "New reason" };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.DeprecateLocationAsync(request);

        // Assert - IMPLEMENTATION TENETS: deprecation must be idempotent (return OK when already deprecated)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DeprecateLocationAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new DeprecateLocationRequest { LocationId = locationId, Reason = "Test" };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
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

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
        Assert.Null(response.DeprecationReason);

        // 3-param convenience overload (what services actually call)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "location.updated", It.IsAny<LocationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UndeprecateLocationAsync_WhenNotDeprecated_ShouldReturnOkIdempotent()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateLocationRequest { LocationId = locationId };
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: false);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert - IMPLEMENTATION TENETS: undeprecation must be idempotent (return OK when not deprecated)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task UndeprecateLocationAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var request = new UndeprecateLocationRequest { LocationId = locationId };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.UndeprecateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ValidateTerritory Tests

    [Fact]
    public async Task ValidateTerritoryAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var territoryId = Guid.NewGuid();
        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { territoryId }
        };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_ExclusiveMode_WhenLocationOverlapsTerritory_ShouldReturnInvalid()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", parentLocationId: cityId, depth: 2);
        var cityModel = CreateTestLocationModel(cityId, realmId, "CITY", "City", parentLocationId: rootId, depth: 1);
        var rootModel = CreateTestLocationModel(rootId, realmId, "ROOT", "Root", depth: 0);

        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { cityId }, // Location is child of this territory
            TerritoryMode = TerritoryMode.Exclusive
        };

        // Setup location retrieval
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{cityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cityModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.NotNull(response.ViolationReason);
        Assert.Contains("exclusive", response.ViolationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(cityId, response.MatchedTerritoryId);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_ExclusiveMode_WhenLocationOutsideTerritory_ShouldReturnValid()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var unrelatedTerritoryId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", depth: 0);

        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { unrelatedTerritoryId }, // No overlap
            TerritoryMode = TerritoryMode.Exclusive
        };

        // Setup location retrieval (no parent, so no ancestors)
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsValid);
        Assert.Null(response.ViolationReason);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_InclusiveMode_WhenLocationInsideTerritory_ShouldReturnValid()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", parentLocationId: rootId, depth: 1);
        var rootModel = CreateTestLocationModel(rootId, realmId, "ROOT", "Root", depth: 0);

        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { rootId }, // Location is child of this territory
            TerritoryMode = TerritoryMode.Inclusive
        };

        // Setup location retrieval
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsValid);
        Assert.Equal(rootId, response.MatchedTerritoryId);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_InclusiveMode_WhenLocationOutsideTerritory_ShouldReturnInvalid()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var unrelatedTerritoryId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", depth: 0);

        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { unrelatedTerritoryId }, // No overlap
            TerritoryMode = TerritoryMode.Inclusive
        };

        // Setup location retrieval (no parent, so no ancestors)
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.NotNull(response.ViolationReason);
        Assert.Contains("inclusive", response.ViolationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.MatchedTerritoryId);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_DefaultsToExclusiveMode_WhenModeNotSpecified()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", depth: 0);

        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { Guid.NewGuid() },
            TerritoryMode = null // Not specified - should default to exclusive
        };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert - should pass since no overlap in exclusive mode
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsValid);
    }

    [Fact]
    public async Task ValidateTerritoryAsync_WhenLocationIsTerritory_ShouldMatchItself()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", depth: 0);

        // Location IS the territory (edge case)
        var request = new ValidateTerritoryRequest
        {
            LocationId = locationId,
            TerritoryLocationIds = new List<Guid> { locationId },
            TerritoryMode = TerritoryMode.Exclusive
        };

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.ValidateTerritoryAsync(request);

        // Assert - location overlaps with itself, so exclusive mode fails
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsValid);
        Assert.Equal(locationId, response.MatchedTerritoryId);
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

        var rootLocationIds = new List<Guid> { loc1Id, loc2Id };
        var model1 = CreateTestLocationModel(loc1Id, realmId, "ROOT1", "Root 1", depth: 0);
        var model2 = CreateTestLocationModel(loc2Id, realmId, "ROOT2", "Root 2", depth: 0);

        _mockListStore
            .Setup(s => s.GetAsync($"{ROOT_LOCATIONS_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootLocationIds);

        // Setup bulk location retrieval
        _mockLocationStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LocationService.LocationModel>
            {
                [$"{LOCATION_KEY_PREFIX}{loc1Id}"] = model1,
                [$"{LOCATION_KEY_PREFIX}{loc2Id}"] = model2
            });

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

        _mockListStore
            .Setup(s => s.GetAsync($"{ROOT_LOCATIONS_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

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
        var childIds = new List<Guid> { child1Id, child2Id };
        var model1 = CreateTestLocationModel(child1Id, realmId, "CHILD1", "Child 1", parentLocationId: parentId, depth: 1);
        var model2 = CreateTestLocationModel(child2Id, realmId, "CHILD2", "Child 2", parentLocationId: parentId, depth: 1);

        // Setup parent location retrieval (needed to get realm)
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Setup parent index lookup
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(childIds);

        // Setup bulk location retrieval for children
        _mockLocationStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LocationService.LocationModel>
            {
                [$"{LOCATION_KEY_PREFIX}{child1Id}"] = model1,
                [$"{LOCATION_KEY_PREFIX}{child2Id}"] = model2
            });

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
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (status, response) = await service.ListLocationsByParentAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    #endregion

    #region CreateLocation Tests

    [Fact]
    public async Task CreateLocationAsync_WhenValid_ShouldCreateAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new CreateLocationRequest
        {
            Code = "new_city",
            Name = "New City",
            Description = "A new city",
            RealmId = realmId,
            LocationType = LocationType.City
        };

        // Code index returns null (no existing location with this code)
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("NEW_CITY", response.Code);
        Assert.Equal("New City", response.Name);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(LocationType.City, response.LocationType);
        Assert.Equal(0, response.Depth);
        Assert.Null(response.ParentLocationId);

        Assert.Equal("location.created", capturedTopic);
        var typedEvent = Assert.IsType<LocationCreatedEvent>(capturedEvent);
        Assert.Equal(response.LocationId, typedEvent.LocationId);
        Assert.Equal("NEW_CITY", typedEvent.Code);
    }

    [Fact]
    public async Task CreateLocationAsync_WhenRealmInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateLocationRequest
        {
            Code = "city",
            Name = "City",
            RealmId = Guid.NewGuid(),
            LocationType = LocationType.City
        };

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = false, IsActive = false });

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateLocationAsync_WhenDuplicateCode_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new CreateLocationRequest
        {
            Code = "existing_city",
            Name = "City",
            RealmId = realmId,
            LocationType = LocationType.City
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateLocationAsync_WithParent_ShouldSetDepth()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var parentModel = CreateTestLocationModel(parentId, realmId, "PARENT", "Parent", depth: 2);

        var request = new CreateLocationRequest
        {
            Code = "child",
            Name = "Child",
            RealmId = realmId,
            LocationType = LocationType.Room,
            ParentLocationId = parentId
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Depth);
        Assert.Equal(parentId, response.ParentLocationId);
    }

    [Fact]
    public async Task CreateLocationAsync_WithMissingParent_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new CreateLocationRequest
        {
            Code = "child",
            Name = "Child",
            RealmId = realmId,
            LocationType = LocationType.Room,
            ParentLocationId = Guid.NewGuid()
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockLocationStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(LOCATION_KEY_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateLocationAsync_WithParentInDifferentRealm_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var otherRealmId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var parentModel = CreateTestLocationModel(parentId, otherRealmId, "PARENT", "Parent");

        var request = new CreateLocationRequest
        {
            Code = "child",
            Name = "Child",
            RealmId = realmId,
            LocationType = LocationType.Room,
            ParentLocationId = parentId
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{parentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentModel);

        // Act
        var (status, response) = await service.CreateLocationAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region DeleteLocation Tests

    [Fact]
    public async Task DeleteLocationAsync_WhenDeprecatedAndNoChildren_ShouldDelete()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var testModel = CreateTestLocationModel(locationId, realmId, "DEL_LOC", "Delete Me", isDeprecated: true);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // No children
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(PARENT_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // No lib-resource references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse { RefCount = 0 });

        // Capture deleted event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var status = await service.DeleteLocationAsync(new DeleteLocationRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("location.deleted", capturedTopic);
        var typedEvent = Assert.IsType<LocationDeletedEvent>(capturedEvent);
        Assert.Equal(locationId, typedEvent.LocationId);
        Assert.Equal("DEL_LOC", typedEvent.Code);

        // Verify location was deleted
        _mockLocationStore.Verify(s => s.DeleteAsync(
            $"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteLocationAsync_WhenNotDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: false);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var status = await service.DeleteLocationAsync(new DeleteLocationRequest { LocationId = locationId });

        // Assert - Category A requires deprecation before deletion
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task DeleteLocationAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var status = await service.DeleteLocationAsync(new DeleteLocationRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteLocationAsync_WhenHasChildren_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Has children
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(PARENT_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });

        // Act
        var status = await service.DeleteLocationAsync(new DeleteLocationRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task DeleteLocationAsync_WhenResourceRefsBlockCleanup_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var testModel = CreateTestLocationModel(locationId, realmId, isDeprecated: true);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // No children
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(PARENT_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Has references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse { RefCount = 3 });

        // Cleanup fails
        _mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCleanupResponse { Success = false, AbortReason = "RESTRICT policy" });

        // Act
        var status = await service.DeleteLocationAsync(new DeleteLocationRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region SetLocationParent Tests

    [Fact]
    public async Task SetLocationParentAsync_WhenValid_ShouldSetParentAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var newParentId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "CHILD", "Child", depth: 0);
        var newParentModel = CreateTestLocationModel(newParentId, realmId, "PARENT", "Parent", depth: 1);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{newParentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newParentModel);

        // No descendants (circular reference check) - parent index empty
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(PARENT_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = newParentId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(newParentId, response.ParentLocationId);
        Assert.Equal(2, response.Depth); // parent depth (1) + 1

        Assert.Equal("location.updated", capturedTopic);
        var typedEvent = Assert.IsType<LocationUpdatedEvent>(capturedEvent);
        Assert.Contains("parentLocationId", typedEvent.ChangedFields);
        Assert.Contains("depth", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task SetLocationParentAsync_WhenAlreadySetToSameParent_ShouldReturnOkNoOp()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", parentLocationId: parentId, depth: 1);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = parentId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // No save should occur
        _mockLocationStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<LocationService.LocationModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLocationParentAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, _) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task SetLocationParentAsync_WhenNewParentNotFound_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var newParentId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{newParentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, _) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = newParentId });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SetLocationParentAsync_WhenDifferentRealm_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var otherRealmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var newParentId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location");
        var newParentModel = CreateTestLocationModel(newParentId, otherRealmId, "PARENT", "Parent");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{newParentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newParentModel);

        // Act
        var (status, _) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = newParentId });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SetLocationParentAsync_WhenCircularReference_ShouldReturnBadRequest()
    {
        // Arrange: parentId is a child of locationId, so setting locationId's parent to parentId would create a cycle
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var locationModel = CreateTestLocationModel(locationId, realmId, "ROOT", "Root", depth: 0);
        var childModel = CreateTestLocationModel(childId, realmId, "CHILD", "Child", parentLocationId: locationId, depth: 1);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(childModel);

        // When checking if childId is a descendant of locationId, the parent index for locationId returns [childId]
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { childId });

        // childId has no further children
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{childId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act - try to set locationId's parent to childId (creating circular ref)
        var (status, _) = await service.SetLocationParentAsync(
            new SetLocationParentRequest { LocationId = locationId, ParentLocationId = childId });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region RemoveLocationParent Tests

    [Fact]
    public async Task RemoveLocationParentAsync_WhenHasParent_ShouldPromoteToRoot()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location", parentLocationId: parentId, depth: 2);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // No descendants to update
        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(PARENT_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.RemoveLocationParentAsync(
            new RemoveLocationParentRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.ParentLocationId);
        Assert.Equal(0, response.Depth);

        Assert.Equal("location.updated", capturedTopic);
        var typedEvent = Assert.IsType<LocationUpdatedEvent>(capturedEvent);
        Assert.Contains("parentLocationId", typedEvent.ChangedFields);
        Assert.Contains("depth", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task RemoveLocationParentAsync_WhenAlreadyRoot_ShouldReturnOkNoOp()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "ROOT", "Root", depth: 0);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.RemoveLocationParentAsync(
            new RemoveLocationParentRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // No save should occur
        _mockLocationStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<LocationService.LocationModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveLocationParentAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, _) = await service.RemoveLocationParentAsync(
            new RemoveLocationParentRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListLocations Tests

    [Fact]
    public async Task ListLocationsAsync_WhenLocationsExist_ShouldReturnFilteredList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var loc1Id = Guid.NewGuid();
        var loc2Id = Guid.NewGuid();
        var loc3Id = Guid.NewGuid();
        var request = new ListLocationsRequest
        {
            RealmId = realmId,
            LocationType = LocationType.City,
            IncludeDeprecated = false
        };

        var locationIds = new List<Guid> { loc1Id, loc2Id, loc3Id };
        var model1 = CreateTestLocationModel(loc1Id, realmId, "CITY1", "City 1", locationType: LocationType.City);
        var model2 = CreateTestLocationModel(loc2Id, realmId, "REGION1", "Region 1", locationType: LocationType.Region);
        var model3 = CreateTestLocationModel(loc3Id, realmId, "CITY2", "City 2", locationType: LocationType.City, isDeprecated: true);

        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationIds);

        // LoadLocationsByIdsAsync reads from cache (bulk) then fallback to persistent
        _mockLocationCacheStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, LocationService.LocationModel>
            {
                [$"{LOCATION_KEY_PREFIX}{loc1Id}"] = model1,
                [$"{LOCATION_KEY_PREFIX}{loc2Id}"] = model2,
                [$"{LOCATION_KEY_PREFIX}{loc3Id}"] = model3
            });

        // Act
        var (status, response) = await service.ListLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Only city1 matches (city type + not deprecated)
        Assert.Single(response.Locations);
        Assert.Equal("CITY1", response.Locations.First().Code);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task ListLocationsAsync_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var request = new ListLocationsRequest
        {
            RealmId = realmId,
            Page = 2,
            PageSize = 2,
            IncludeDeprecated = true
        };

        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        var cacheResult = ids.ToDictionary(
            id => $"{LOCATION_KEY_PREFIX}{id}",
            id => CreateTestLocationModel(id, realmId, $"LOC_{ids.IndexOf(id)}", $"Location {ids.IndexOf(id)}"));

        _mockLocationCacheStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheResult);

        // Act
        var (status, response) = await service.ListLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Page);
        Assert.True(response.HasNextPage);
        Assert.True(response.HasPreviousPage);
    }

    #endregion

    #region GetLocationAncestors Tests

    [Fact]
    public async Task GetLocationAncestorsAsync_WhenHasAncestors_ShouldReturnAncestorChain()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var cityId = Guid.NewGuid();
        var roomId = Guid.NewGuid();

        var rootModel = CreateTestLocationModel(rootId, realmId, "ROOT", "Root", depth: 0);
        var cityModel = CreateTestLocationModel(cityId, realmId, "CITY", "City", parentLocationId: rootId, depth: 1);
        var roomModel = CreateTestLocationModel(roomId, realmId, "ROOM", "Room", parentLocationId: cityId, depth: 2);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{roomId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{cityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cityModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootModel);

        // Act
        var (status, response) = await service.GetLocationAncestorsAsync(
            new GetLocationAncestorsRequest { LocationId = roomId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
        Assert.Equal(cityId, response.Locations.ElementAt(0).LocationId);
        Assert.Equal(rootId, response.Locations.ElementAt(1).LocationId);
    }

    [Fact]
    public async Task GetLocationAncestorsAsync_WhenRoot_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var rootModel = CreateTestLocationModel(rootId, realmId, "ROOT", "Root", depth: 0);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootModel);

        // Act
        var (status, response) = await service.GetLocationAncestorsAsync(
            new GetLocationAncestorsRequest { LocationId = rootId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    [Fact]
    public async Task GetLocationAncestorsAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, _) = await service.GetLocationAncestorsAsync(
            new GetLocationAncestorsRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetLocationDescendants Tests

    [Fact]
    public async Task GetLocationDescendantsAsync_WhenHasDescendants_ShouldReturnDescendantTree()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var rootModel = CreateTestLocationModel(rootId, realmId, "ROOT", "Root", depth: 0);
        var child1Model = CreateTestLocationModel(child1Id, realmId, "CHILD1", "Child 1", parentLocationId: rootId, depth: 1);
        var child2Model = CreateTestLocationModel(child2Id, realmId, "CHILD2", "Child 2", parentLocationId: rootId, depth: 1);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootModel);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{child1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child1Model);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{child2Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(child2Model);

        // Root's children
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{rootId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { child1Id, child2Id });

        // Children have no further children
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{child1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);
        _mockListStore
            .Setup(s => s.GetAsync($"{PARENT_INDEX_PREFIX}{realmId}:{child2Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (status, response) = await service.GetLocationDescendantsAsync(
            new GetLocationDescendantsRequest { LocationId = rootId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task GetLocationDescendantsAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        // Act
        var (status, _) = await service.GetLocationDescendantsAsync(
            new GetLocationDescendantsRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region QueryLocationsByPosition Tests

    [Fact]
    public async Task QueryLocationsByPositionAsync_WhenInsideBounds_ShouldReturnMatchingLocations()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var loc1Id = Guid.NewGuid();
        var loc2Id = Guid.NewGuid();

        var loc1Model = CreateTestLocationModel(loc1Id, realmId, "CITY", "City", depth: 0);
        loc1Model.Bounds = new BoundingBox3D { MinX = 0, MinY = 0, MinZ = 0, MaxX = 100, MaxY = 100, MaxZ = 100 };
        loc1Model.BoundsPrecision = BoundsPrecision.Exact;

        var loc2Model = CreateTestLocationModel(loc2Id, realmId, "DISTRICT", "District", depth: 1);
        loc2Model.Bounds = new BoundingBox3D { MinX = 20, MinY = 0, MinZ = 20, MaxX = 50, MaxY = 50, MaxZ = 50 };
        loc2Model.BoundsPrecision = BoundsPrecision.Exact;

        var locationIds = new List<Guid> { loc1Id, loc2Id };
        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationIds);

        // GetLocationWithCacheAsync path: cache miss -> persistent store
        _mockLocationCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{loc1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(loc1Model);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{loc2Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(loc2Model);

        var request = new QueryLocationsByPositionRequest
        {
            Position = new Position3D { X = 30, Y = 10, Z = 30 },
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.QueryLocationsByPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Locations.Count);
        // Most specific (deeper) first
        Assert.Equal("DISTRICT", response.Locations.ElementAt(0).Code);
        Assert.Equal("CITY", response.Locations.ElementAt(1).Code);
    }

    [Fact]
    public async Task QueryLocationsByPositionAsync_WhenOutsideBounds_ShouldReturnEmpty()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locId = Guid.NewGuid();

        var locModel = CreateTestLocationModel(locId, realmId, "CITY", "City", depth: 0);
        locModel.Bounds = new BoundingBox3D { MinX = 0, MinY = 0, MinZ = 0, MaxX = 10, MaxY = 10, MaxZ = 10 };
        locModel.BoundsPrecision = BoundsPrecision.Exact;

        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { locId });

        _mockLocationCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locModel);

        var request = new QueryLocationsByPositionRequest
        {
            Position = new Position3D { X = 999, Y = 999, Z = 999 },
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.QueryLocationsByPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    [Fact]
    public async Task QueryLocationsByPositionAsync_WhenNoBoundsSet_ShouldSkipLocation()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locId = Guid.NewGuid();

        var locModel = CreateTestLocationModel(locId, realmId, "CITY", "City", depth: 0);
        // Bounds is null, BoundsPrecision defaults to None

        _mockListStore
            .Setup(s => s.GetAsync($"{REALM_INDEX_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { locId });

        _mockLocationCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locModel);

        var request = new QueryLocationsByPositionRequest
        {
            Position = new Position3D { X = 5, Y = 5, Z = 5 },
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.QueryLocationsByPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Locations);
    }

    #endregion

    #region TransferLocationToRealm Tests

    [Fact]
    public async Task TransferLocationToRealmAsync_WhenValid_ShouldTransferAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var sourceRealmId = Guid.NewGuid();
        var targetRealmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, sourceRealmId, "CITY", "City", depth: 0);

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Target realm is active
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // No code conflict in target realm
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX) && k.Contains(targetRealmId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.TransferLocationToRealmAsync(
            new TransferLocationToRealmRequest { LocationId = locationId, TargetRealmId = targetRealmId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(targetRealmId, response.RealmId);
        Assert.Null(response.ParentLocationId);
        Assert.Equal(0, response.Depth);

        Assert.Equal("location.updated", capturedTopic);
        var typedEvent = Assert.IsType<LocationUpdatedEvent>(capturedEvent);
        Assert.Contains("realmId", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task TransferLocationToRealmAsync_WhenAlreadyInTargetRealm_ShouldReturnOkNoOp()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "CITY", "City");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        // Act
        var (status, response) = await service.TransferLocationToRealmAsync(
            new TransferLocationToRealmRequest { LocationId = locationId, TargetRealmId = realmId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // No event should be published (idempotent)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "location.updated", It.IsAny<LocationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TransferLocationToRealmAsync_WhenTargetRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceRealmId = Guid.NewGuid();
        var targetRealmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, sourceRealmId, "CITY", "City");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = false, IsActive = false });

        // Act
        var (status, _) = await service.TransferLocationToRealmAsync(
            new TransferLocationToRealmRequest { LocationId = locationId, TargetRealmId = targetRealmId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task TransferLocationToRealmAsync_WhenCodeConflict_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var sourceRealmId = Guid.NewGuid();
        var targetRealmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, sourceRealmId, "CITY", "City");

        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Code already exists in target realm
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, _) = await service.TransferLocationToRealmAsync(
            new TransferLocationToRealmRequest { LocationId = locationId, TargetRealmId = targetRealmId });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region Entity Presence Tests

    [Fact]
    public async Task ReportEntityPositionAsync_WhenFirstReport_ShouldSetPresenceAndPublishArrival()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(locationId, realmId, "LOC", "Location");

        // Setup cache read-through path: cache miss -> persistent store
        _mockLocationCacheStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{locationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityPresenceModel?)null); // No existing presence

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        var mockEntitySetStore = new Mock<ICacheableStateStore<string>>();
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>("location-entity-set"))
            .Returns(mockEntitySetStore.Object);

        // Capture events
        var capturedTopics = new List<string>();
        var capturedEvents = new List<object>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopics.Add(topic);
                capturedEvents.Add(evt);
            })
            .ReturnsAsync(true);

        var request = new ReportEntityPositionRequest
        {
            EntityType = "character",
            EntityId = entityId,
            LocationId = locationId
        };

        // Act
        var (status, response) = await service.ReportEntityPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.ArrivedAt);
        Assert.Null(response.DepartedFrom);

        // Verify presence was saved
        mockPresenceStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains("character") && k.Contains(entityId.ToString())),
            It.IsAny<EntityPresenceModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify arrival event was published
        Assert.Contains("location.entity-arrived", capturedTopics);
    }

    [Fact]
    public async Task ReportEntityPositionAsync_WhenLocationChanged_ShouldPublishDepartureAndArrival()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var oldLocationId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var locationModel = CreateTestLocationModel(newLocationId, realmId, "NEW_LOC", "New Location");

        _mockLocationCacheStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{newLocationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync($"{LOCATION_KEY_PREFIX}{newLocationId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationModel);

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        var mockEntitySetStore = new Mock<ICacheableStateStore<string>>();
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>("location-entity-set"))
            .Returns(mockEntitySetStore.Object);

        var capturedTopics = new List<string>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, _, _2) => capturedTopics.Add(topic))
            .ReturnsAsync(true);

        var request = new ReportEntityPositionRequest
        {
            EntityType = "character",
            EntityId = entityId,
            LocationId = newLocationId,
            PreviousLocationId = oldLocationId // Explicitly provided
        };

        // Act
        var (status, response) = await service.ReportEntityPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(newLocationId, response.ArrivedAt);
        Assert.Equal(oldLocationId, response.DepartedFrom);

        Assert.Contains("location.entity-departed", capturedTopics);
        Assert.Contains("location.entity-arrived", capturedTopics);
    }

    [Fact]
    public async Task ReportEntityPositionAsync_WhenLocationNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();

        _mockLocationCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);
        _mockLocationStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationService.LocationModel?)null);

        var request = new ReportEntityPositionRequest
        {
            EntityType = "character",
            EntityId = Guid.NewGuid(),
            LocationId = locationId
        };

        // Act
        var (status, _) = await service.ReportEntityPositionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetEntityLocationAsync_WhenPresenceExists_ShouldReturnLocation()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var reportedAt = DateTimeOffset.UtcNow;

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("character") && k.Contains(entityId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityPresenceModel
            {
                EntityId = entityId,
                EntityType = "character",
                LocationId = locationId,
                RealmId = realmId,
                ReportedAt = reportedAt,
                ReportedBy = "game-server"
            });

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        // Act
        var (status, response) = await service.GetEntityLocationAsync(
            new GetEntityLocationRequest { EntityType = "character", EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal("game-server", response.ReportedBy);
    }

    [Fact]
    public async Task GetEntityLocationAsync_WhenNoPresence_ShouldReturnEmptyResponse()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityPresenceModel?)null);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        // Act
        var (status, response) = await service.GetEntityLocationAsync(
            new GetEntityLocationRequest { EntityType = "character", EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.LocationId);
        Assert.Null(response.RealmId);
    }

    [Fact]
    public async Task ListEntitiesAtLocationAsync_ShouldReturnEntitiesInSet()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();

        var mockEntitySetStore = new Mock<ICacheableStateStore<string>>();
        mockEntitySetStore
            .Setup(s => s.GetSetAsync<string>(It.Is<string>(k => k.Contains(locationId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>
            {
                $"character:{entity1Id}",
                $"npc:{entity2Id}"
            });

        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>("location-entity-set"))
            .Returns(mockEntitySetStore.Object);

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityPresenceModel?)null);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        // Act
        var (status, response) = await service.ListEntitiesAtLocationAsync(
            new ListEntitiesAtLocationRequest { LocationId = locationId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(locationId, response.LocationId);
        Assert.Equal(2, response.Entities.Count);
    }

    [Fact]
    public async Task ListEntitiesAtLocationAsync_WithEntityTypeFilter_ShouldFilterResults()
    {
        // Arrange
        var service = CreateService();
        var locationId = Guid.NewGuid();
        var entity1Id = Guid.NewGuid();
        var entity2Id = Guid.NewGuid();

        var mockEntitySetStore = new Mock<ICacheableStateStore<string>>();
        mockEntitySetStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>
            {
                $"character:{entity1Id}",
                $"npc:{entity2Id}"
            });

        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>("location-entity-set"))
            .Returns(mockEntitySetStore.Object);

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityPresenceModel?)null);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        // Act
        var (status, response) = await service.ListEntitiesAtLocationAsync(
            new ListEntitiesAtLocationRequest { LocationId = locationId, EntityType = "character" });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Entities);
        Assert.Equal("character", response.Entities.First().EntityType);
        Assert.Equal(entity1Id, response.Entities.First().EntityId);
    }

    [Fact]
    public async Task ClearEntityPositionAsync_WhenPresenceExists_ShouldClearAndPublishDeparture()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityPresenceModel
            {
                EntityId = entityId,
                EntityType = "character",
                LocationId = locationId,
                RealmId = realmId,
                ReportedAt = DateTimeOffset.UtcNow
            });

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        var mockEntitySetStore = new Mock<ICacheableStateStore<string>>();
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>("location-entity-set"))
            .Returns(mockEntitySetStore.Object);

        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, _, _2) => capturedTopic = topic)
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.ClearEntityPositionAsync(
            new ClearEntityPositionRequest { EntityType = "character", EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(locationId, response.PreviousLocationId);

        // Verify departure event
        Assert.Equal("location.entity-departed", capturedTopic);

        // Verify presence was deleted
        mockPresenceStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains("character") && k.Contains(entityId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearEntityPositionAsync_WhenNoPresence_ShouldReturnOkWithNullPrevious()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        var mockPresenceStore = new Mock<IStateStore<EntityPresenceModel>>();
        mockPresenceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityPresenceModel?)null);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntityPresenceModel>("location-entity-presence"))
            .Returns(mockPresenceStore.Object);

        // Act
        var (status, response) = await service.ClearEntityPositionAsync(
            new ClearEntityPositionRequest { EntityType = "character", EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.PreviousLocationId);
    }

    #endregion

    #region SeedLocations Tests

    [Fact]
    public async Task SeedLocationsAsync_WhenNewLocations_ShouldCreateAndSetParents()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        // Mock realm lookup
        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.Is<GetRealmByCodeRequest>(req => req.Code == "ARCADIA"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = realmId, Code = "ARCADIA", Name = "Arcadia" });

        // No existing code indexes
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new SeedLocationsRequest
        {
            Locations = new List<SeedLocation>
            {
                new SeedLocation
                {
                    Code = "ROOT",
                    Name = "Root Region",
                    RealmCode = "ARCADIA",
                    LocationType = LocationType.Region
                },
                new SeedLocation
                {
                    Code = "CITY",
                    Name = "City",
                    RealmCode = "ARCADIA",
                    LocationType = LocationType.City,
                    ParentLocationCode = "ROOT"
                }
            },
            UpdateExisting = false
        };

        // Act
        var (status, response) = await service.SeedLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Created);
        Assert.Equal(0, response.Skipped);
        Assert.Equal(0, response.Updated);
    }

    [Fact]
    public async Task SeedLocationsAsync_WhenRealmNotFound_ShouldReportErrors()
    {
        // Arrange
        var service = CreateService();

        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmResponse?)null);

        var request = new SeedLocationsRequest
        {
            Locations = new List<SeedLocation>
            {
                new SeedLocation
                {
                    Code = "LOC",
                    Name = "Location",
                    RealmCode = "NONEXISTENT",
                    LocationType = LocationType.City
                }
            }
        };

        // Act
        var (status, response) = await service.SeedLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.NotEmpty(response.Errors);
        Assert.Contains("NONEXISTENT", response.Errors.First());
    }

    [Fact]
    public async Task SeedLocationsAsync_WhenExistsAndNotUpdateExisting_ShouldSkip()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.Is<GetRealmByCodeRequest>(req => req.Code == "ARCADIA"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = realmId, Code = "ARCADIA", Name = "Arcadia" });

        // Code index returns existing ID
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        var request = new SeedLocationsRequest
        {
            Locations = new List<SeedLocation>
            {
                new SeedLocation
                {
                    Code = "EXISTING",
                    Name = "Existing",
                    RealmCode = "ARCADIA",
                    LocationType = LocationType.City
                }
            },
            UpdateExisting = false
        };

        // Act
        var (status, response) = await service.SeedLocationsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(1, response.Skipped);
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
