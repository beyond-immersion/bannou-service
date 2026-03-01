using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Transit;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Transit.Tests;

/// <summary>
/// Unit tests for TransitService.
/// Tests business logic with mocked infrastructure dependencies.
/// </summary>
public class TransitServiceTests
{
    // Infrastructure (L0)
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    // State stores (typed mocks matching constructor store creation)
    private readonly Mock<IQueryableStateStore<TransitModeModel>> _mockModeStore;
    private readonly Mock<IQueryableStateStore<TransitConnectionModel>> _mockConnectionStore;
    private readonly Mock<IStateStore<TransitJourneyModel>> _mockJourneyStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockJourneyIndexStore;
    private readonly Mock<IQueryableStateStore<JourneyArchiveModel>> _mockJourneyArchiveStore;
    private readonly Mock<IStateStore<List<ConnectionGraphEntry>>> _mockConnectionGraphStore;
    private readonly Mock<IQueryableStateStore<TransitDiscoveryModel>> _mockDiscoveryStore;
    private readonly Mock<IStateStore<HashSet<Guid>>> _mockDiscoveryCacheStore;

    // Service clients (L1/L2 hard dependencies)
    private readonly Mock<ILocationClient> _mockLocationClient;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<IResourceClient> _mockResourceClient;

    // Helper services
    private readonly Mock<ITransitConnectionGraphCache> _mockGraphCache;
    private readonly Mock<ITransitRouteCalculator> _mockRouteCalculator;

    // Client events, logging, telemetry, configuration, event consumer
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<ILogger<TransitService>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly TransitServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // Default lock response setup
    private readonly Mock<ILockResponse> _mockLockResponse;

    // Captured events for assertion per TESTING-PATTERNS capture pattern
    private readonly List<(string Topic, object Event)> _capturedEvents = new();

    // Test data GUIDs (stable across tests for readability)
    private static readonly Guid TestLocationAId = Guid.NewGuid();
    private static readonly Guid TestLocationBId = Guid.NewGuid();
    private static readonly Guid TestLocationCId = Guid.NewGuid();
    private static readonly Guid TestRealmId = Guid.NewGuid();
    private static readonly Guid TestEntityId = Guid.NewGuid();
    private static readonly Guid TestConnectionId = Guid.NewGuid();
    private static readonly Guid TestJourneyId = Guid.NewGuid();

    public TransitServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        // Create typed store mocks
        _mockModeStore = new Mock<IQueryableStateStore<TransitModeModel>>();
        _mockConnectionStore = new Mock<IQueryableStateStore<TransitConnectionModel>>();
        _mockJourneyStore = new Mock<IStateStore<TransitJourneyModel>>();
        _mockJourneyIndexStore = new Mock<IStateStore<List<Guid>>>();
        _mockJourneyArchiveStore = new Mock<IQueryableStateStore<JourneyArchiveModel>>();
        _mockConnectionGraphStore = new Mock<IStateStore<List<ConnectionGraphEntry>>>();
        _mockDiscoveryStore = new Mock<IQueryableStateStore<TransitDiscoveryModel>>();
        _mockDiscoveryCacheStore = new Mock<IStateStore<HashSet<Guid>>>();

        // Wire factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes))
            .Returns(_mockModeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections))
            .Returns(_mockConnectionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TransitJourneyModel>(StateStoreDefinitions.TransitJourneys))
            .Returns(_mockJourneyStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(StateStoreDefinitions.TransitJourneys))
            .Returns(_mockJourneyIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<JourneyArchiveModel>(StateStoreDefinitions.TransitJourneysArchive))
            .Returns(_mockJourneyArchiveStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<ConnectionGraphEntry>>(StateStoreDefinitions.TransitConnectionGraph))
            .Returns(_mockConnectionGraphStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<TransitDiscoveryModel>(StateStoreDefinitions.TransitDiscovery))
            .Returns(_mockDiscoveryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HashSet<Guid>>(StateStoreDefinitions.TransitDiscoveryCache))
            .Returns(_mockDiscoveryCacheStore.Object);

        // Service clients
        _mockLocationClient = new Mock<ILocationClient>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockResourceClient = new Mock<IResourceClient>();

        // Helper services
        _mockGraphCache = new Mock<ITransitConnectionGraphCache>();
        _mockRouteCalculator = new Mock<ITransitRouteCalculator>();

        // Client events, logging, telemetry
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockLogger = new Mock<ILogger<TransitService>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Configuration with defaults
        _configuration = new TransitServiceConfiguration
        {
            LockTimeoutSeconds = 10,
            MaxRouteCalculationLegs = 8,
            MaxRouteOptions = 5,
            DefaultWalkingSpeedKmPerGameHour = 5.0,
            DefaultFallbackModeCode = "walking",
            MinPreferenceCost = 0.0,
            MaxPreferenceCost = 2.0,
            MinSpeedMultiplier = 0.1,
            MaxSpeedMultiplier = 3.0,
            DefaultCargoSpeedPenaltyRate = 0.3,
            MinimumEffectiveSpeedKmPerGameHour = 0.01,
            AutoUpdateLocationOnTransition = true
        };

        // Default lock behavior: always succeed
        _mockLockResponse = new Mock<ILockResponse>();
        _mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLockResponse.Object);

        // Default save behavior: return etag
        _mockModeStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitModeModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockModeStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitModeModel>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        _mockConnectionStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        _mockJourneyStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockJourneyIndexStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<List<Guid>>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockJourneyIndexStore.Setup(s => s.GetWithETagAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), "etag-idx"));
        _mockJourneyIndexStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<List<Guid>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-idx-2");

        _mockDiscoveryStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitDiscoveryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockDiscoveryCacheStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<HashSet<Guid>>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Default message bus: always succeed and capture published events for assertion
        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>((topic, evt, _, _, _) =>
            {
                _capturedEvents.Add((topic, evt));
            })
            .ReturnsAsync(true);

        // Default graph cache: no-op invalidation
        _mockGraphCache.Setup(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Creates a TransitService instance with all mocked dependencies.
    /// Clears captured events for a fresh test run.
    /// </summary>
    private TransitService CreateService()
    {
        _capturedEvents.Clear();
        return new TransitService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockLocationClient.Object,
            _mockWorldstateClient.Object,
            _mockCharacterClient.Object,
            _mockSpeciesClient.Object,
            _mockInventoryClient.Object,
            _mockResourceClient.Object,
            _mockGraphCache.Object,
            _mockRouteCalculator.Object,
            Enumerable.Empty<ITransitCostModifierProvider>(),
            _mockClientEventPublisher.Object,
            _mockLogger.Object,
            _mockTelemetryProvider.Object,
            _configuration,
            _mockEventConsumer.Object);
    }

    /// <summary>
    /// Creates a minimal TransitModeModel for test setup.
    /// </summary>
    private static TransitModeModel CreateTestModeModel(
        string code = "walking",
        bool isDeprecated = false,
        string? deprecationReason = null)
    {
        return new TransitModeModel
        {
            Code = code,
            Name = $"Test Mode {code}",
            Description = $"A test transit mode: {code}",
            BaseSpeedKmPerGameHour = 5.0m,
            PassengerCapacity = 1,
            CargoCapacityKg = 50.0m,
            CompatibleTerrainTypes = new List<string> { "road", "trail" },
            Requirements = new TransitModeRequirementsModel(),
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow : null,
            DeprecationReason = deprecationReason,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a minimal TransitConnectionModel for test setup.
    /// </summary>
    private static TransitConnectionModel CreateTestConnectionModel(
        Guid? connectionId = null,
        Guid? fromLocationId = null,
        Guid? toLocationId = null,
        ConnectionStatus status = ConnectionStatus.Open,
        bool discoverable = false)
    {
        return new TransitConnectionModel
        {
            Id = connectionId ?? TestConnectionId,
            FromLocationId = fromLocationId ?? TestLocationAId,
            ToLocationId = toLocationId ?? TestLocationBId,
            Bidirectional = true,
            DistanceKm = 10.0m,
            TerrainType = "road",
            CompatibleModes = new List<string> { "walking" },
            Status = status,
            StatusChangedAt = DateTimeOffset.UtcNow,
            Discoverable = discoverable,
            FromRealmId = TestRealmId,
            ToRealmId = TestRealmId,
            CrossRealm = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a minimal TransitJourneyModel for test setup.
    /// </summary>
    private static TransitJourneyModel CreateTestJourneyModel(
        Guid? journeyId = null,
        JourneyStatus status = JourneyStatus.Preparing,
        int legCount = 1)
    {
        var id = journeyId ?? TestJourneyId;
        var legs = new List<TransitJourneyLegModel>();

        for (var i = 0; i < legCount; i++)
        {
            legs.Add(new TransitJourneyLegModel
            {
                ConnectionId = Guid.NewGuid(),
                FromLocationId = i == 0 ? TestLocationAId : Guid.NewGuid(),
                ToLocationId = i == legCount - 1 ? TestLocationBId : Guid.NewGuid(),
                ModeCode = "walking",
                DistanceKm = 10.0m,
                TerrainType = "road",
                EstimatedDurationGameHours = 2.0m,
                Status = JourneyLegStatus.Pending
            });
        }

        return new TransitJourneyModel
        {
            Id = id,
            EntityId = TestEntityId,
            EntityType = "character",
            Legs = legs,
            CurrentLegIndex = 0,
            PrimaryModeCode = "walking",
            EffectiveSpeedKmPerGameHour = 5.0m,
            PlannedDepartureGameTime = 100.0m,
            OriginLocationId = TestLocationAId,
            DestinationLocationId = TestLocationBId,
            CurrentLocationId = TestLocationAId,
            Status = status,
            Interruptions = new List<TransitInterruptionModel>(),
            PartySize = 1,
            CargoWeightKg = 0m,
            RealmId = TestRealmId,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    #region Constructor Validation

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
    public void TransitService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<TransitService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void TransitServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new TransitServiceConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(10, config.LockTimeoutSeconds);
        Assert.Equal(8, config.MaxRouteCalculationLegs);
        Assert.Equal(5, config.MaxRouteOptions);
    }

    #endregion

    #region Mode Management Tests

    /// <summary>
    /// RegisterModeAsync should save to mode store and publish event when code is unique.
    /// </summary>
    [Fact]
    public async Task RegisterModeAsync_ShouldCreateMode_WhenCodeIsUnique()
    {
        // Arrange
        var service = CreateService();
        var request = new RegisterModeRequest
        {
            Code = "horseback",
            Name = "Horseback Riding",
            Description = "Travel by horse",
            BaseSpeedKmPerGameHour = 15.0m,
            PassengerCapacity = 2,
            CargoCapacityKg = 200.0m,
            Requirements = new TransitModeRequirements()
        };

        // Mock: no existing mode with this code
        _mockModeStore.Setup(s => s.GetAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitModeModel?)null);

        TransitModeModel? capturedModel = null;
        _mockModeStore.Setup(s => s.SaveAsync(
            "mode:horseback", It.IsAny<TransitModeModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, StateOptions?, CancellationToken>((_, model, _, _) => capturedModel = model)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.RegisterModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal("horseback", capturedModel.Code);
        Assert.Equal("Horseback Riding", capturedModel.Name);
        Assert.Equal(15.0m, capturedModel.BaseSpeedKmPerGameHour);
        Assert.False(capturedModel.IsDeprecated);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var registeredEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-mode.registered");
        Assert.NotNull(registeredEvent.Event);
        var typedEvent = Assert.IsType<TransitModeRegisteredEvent>(registeredEvent.Event);
        Assert.Equal("horseback", typedEvent.Code);
        Assert.Equal("Horseback Riding", typedEvent.Name);
    }

    /// <summary>
    /// RegisterModeAsync should return Conflict when mode code already exists.
    /// </summary>
    [Fact]
    public async Task RegisterModeAsync_ShouldReturnConflict_WhenCodeExists()
    {
        // Arrange
        var service = CreateService();
        var request = new RegisterModeRequest
        {
            Code = "walking",
            Name = "Walking",
            Description = "Travel on foot",
            BaseSpeedKmPerGameHour = 5.0m,
            Requirements = new TransitModeRequirements()
        };

        // Mock: existing mode with this code
        _mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));

        // Act
        var (statusCode, response) = await service.RegisterModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// GetModeAsync should return OK with mode data when found.
    /// </summary>
    [Fact]
    public async Task GetModeAsync_ShouldReturnMode_WhenFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetModeRequest { Code = "walking" };

        _mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));

        // Act
        var (statusCode, response) = await service.GetModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
    }

    /// <summary>
    /// GetModeAsync should return NotFound when mode code does not exist.
    /// </summary>
    [Fact]
    public async Task GetModeAsync_ShouldReturnNotFound_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new GetModeRequest { Code = "nonexistent" };

        _mockModeStore.Setup(s => s.GetAsync("mode:nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitModeModel?)null);

        // Act
        var (statusCode, response) = await service.GetModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// DeprecateModeAsync should set triple-field deprecation model and publish event.
    /// </summary>
    [Fact]
    public async Task DeprecateModeAsync_ShouldSetTripleField_WhenNotAlreadyDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new DeprecateModeRequest { Code = "horseback", Reason = "Horses extinct" };

        var model = CreateTestModeModel("horseback");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitModeModel? capturedModel = null;
        _mockModeStore.Setup(s => s.TrySaveAsync(
            "mode:horseback", It.IsAny<TransitModeModel>(),
            "etag-1", It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, string, CancellationToken>((_, m, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.DeprecateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.True(capturedModel.IsDeprecated);
        Assert.NotNull(capturedModel.DeprecatedAt);
        Assert.Equal("Horses extinct", capturedModel.DeprecationReason);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var updatedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-mode.updated");
        Assert.NotNull(updatedEvent.Event);
        var typedEvent = Assert.IsType<TransitModeUpdatedEvent>(updatedEvent.Event);
        Assert.Equal("horseback", typedEvent.Code);
        Assert.Contains("isDeprecated", typedEvent.ChangedFields);
    }

    /// <summary>
    /// DeprecateModeAsync should return OK idempotently when mode is already deprecated.
    /// </summary>
    [Fact]
    public async Task DeprecateModeAsync_ShouldReturnOK_WhenAlreadyDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new DeprecateModeRequest { Code = "horseback", Reason = "Horses extinct" };

        var model = CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Old reason");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        // Act
        var (statusCode, response) = await service.DeprecateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Should NOT have called TrySaveAsync (idempotent, no state change)
        _mockModeStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitModeModel>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// UndeprecateModeAsync should clear deprecation fields and publish event.
    /// </summary>
    [Fact]
    public async Task UndeprecateModeAsync_ShouldClearDeprecation_WhenDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new UndeprecateModeRequest { Code = "horseback" };

        var model = CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Horses extinct");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitModeModel? capturedModel = null;
        _mockModeStore.Setup(s => s.TrySaveAsync(
            "mode:horseback", It.IsAny<TransitModeModel>(),
            "etag-1", It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, string, CancellationToken>((_, m, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.UndeprecateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.False(capturedModel.IsDeprecated);
        Assert.Null(capturedModel.DeprecatedAt);
        Assert.Null(capturedModel.DeprecationReason);
    }

    /// <summary>
    /// UndeprecateModeAsync should return OK idempotently when mode is not deprecated.
    /// Per IMPLEMENTATION TENETS: undeprecation is idempotent for Category A entities.
    /// </summary>
    [Fact]
    public async Task UndeprecateModeAsync_ShouldReturnOK_WhenNotDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new UndeprecateModeRequest { Code = "walking" };

        var model = CreateTestModeModel("walking", isDeprecated: false);

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        // Act
        var (statusCode, response) = await service.UndeprecateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Should NOT have called TrySaveAsync (idempotent, no state change needed)
        _mockModeStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitModeModel>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// DeleteModeAsync should reject deletion of non-deprecated mode with BadRequest.
    /// Category A deprecation lifecycle: must deprecate before delete.
    /// </summary>
    [Fact]
    public async Task DeleteModeAsync_ShouldReturnBadRequest_WhenNotDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteModeRequest { Code = "walking" };

        var model = CreateTestModeModel("walking", isDeprecated: false);

        _mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (statusCode, response) = await service.DeleteModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);

        // Should NOT have deleted
        _mockModeStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// DeleteModeAsync should delete when mode is deprecated and not referenced by connections or journeys.
    /// </summary>
    [Fact]
    public async Task DeleteModeAsync_ShouldDelete_WhenDeprecatedAndUnreferenced()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteModeRequest { Code = "horseback" };

        var model = CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Horses extinct");

        _mockModeStore.Setup(s => s.GetAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // No connections reference this mode
        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel>());

        // No active journeys use this mode
        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel>());

        // Act
        var (statusCode, response) = await service.DeleteModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify deletion
        _mockModeStore.Verify(s => s.DeleteAsync("mode:horseback", It.IsAny<CancellationToken>()), Times.Once());

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var deletedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-mode.deleted");
        Assert.NotNull(deletedEvent.Event);
        var typedEvent = Assert.IsType<TransitModeDeletedEvent>(deletedEvent.Event);
        Assert.Equal("horseback", typedEvent.Code);
    }

    #endregion

    #region Connection Management Tests

    /// <summary>
    /// CreateConnectionAsync should validate locations, save to store, invalidate graph cache,
    /// and publish event.
    /// </summary>
    [Fact]
    public async Task CreateConnectionAsync_ShouldCreateConnection_WhenValid()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateConnectionRequest
        {
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            Bidirectional = true,
            DistanceKm = 25.5m,
            TerrainType = "road",
            CompatibleModes = new List<string> { "walking" }
        };

        // Mock: Both locations exist and return realm IDs
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationAId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationBId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationBId, RealmId = TestRealmId });

        // Mock: walking mode exists
        _mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));

        // Mock: no duplicate connections
        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel>());

        TransitConnectionModel? capturedConnection = null;
        _mockConnectionStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, StateOptions?, CancellationToken>((_, conn, _, _) => capturedConnection = conn)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedConnection);
        Assert.Equal(TestLocationAId, capturedConnection.FromLocationId);
        Assert.Equal(TestLocationBId, capturedConnection.ToLocationId);
        Assert.True(capturedConnection.Bidirectional);
        Assert.Equal(25.5m, capturedConnection.DistanceKm);
        Assert.Equal(ConnectionStatus.Open, capturedConnection.Status);
        Assert.Equal(TestRealmId, capturedConnection.FromRealmId);

        // Verify graph cache invalidated
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// CreateConnectionAsync should return BadRequest when from and to locations are the same.
    /// </summary>
    [Fact]
    public async Task CreateConnectionAsync_ShouldReturnBadRequest_WhenSameLocation()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateConnectionRequest
        {
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationAId,
            Bidirectional = true,
            DistanceKm = 10.0m,
            TerrainType = "road"
        };

        // Act
        var (statusCode, response) = await service.CreateConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// UpdateConnectionStatusAsync should update status, invalidate graph cache, and publish events.
    /// </summary>
    [Fact]
    public async Task UpdateConnectionStatusAsync_ShouldUpdateStatus_WhenValid()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new UpdateConnectionStatusRequest
        {
            ConnectionId = connectionId,
            NewStatus = SettableConnectionStatus.Blocked,
            Reason = "Landslide",
            CurrentStatus = ConnectionStatus.Open,
            ForceUpdate = false
        };

        var model = CreateTestConnectionModel(connectionId: connectionId, status: ConnectionStatus.Open);

        var key = TransitService.BuildConnectionKey(connectionId);
        _mockConnectionStore.Setup(s => s.GetWithETagAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitConnectionModel? capturedModel = null;
        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            key, It.IsAny<TransitConnectionModel>(),
            "etag-1", It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, string, CancellationToken>((_, m, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.UpdateConnectionStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal(ConnectionStatus.Blocked, capturedModel.Status);
        Assert.Equal("Landslide", capturedModel.StatusReason);

        // Verify graph cache invalidated
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// UpdateConnectionStatusAsync should return BadRequest when currentStatus does not match actual status.
    /// </summary>
    [Fact]
    public async Task UpdateConnectionStatusAsync_ShouldReturnBadRequest_WhenStatusMismatch()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new UpdateConnectionStatusRequest
        {
            ConnectionId = connectionId,
            NewStatus = SettableConnectionStatus.Open,
            CurrentStatus = ConnectionStatus.Closed, // Mismatch: actual is Open
            ForceUpdate = false
        };

        var model = CreateTestConnectionModel(connectionId: connectionId, status: ConnectionStatus.Open);

        var key = TransitService.BuildConnectionKey(connectionId);
        _mockConnectionStore.Setup(s => s.GetWithETagAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        // Act
        var (statusCode, response) = await service.UpdateConnectionStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// UpdateConnectionStatusAsync should succeed with ForceUpdate even when CurrentStatus does not match.
    /// </summary>
    [Fact]
    public async Task UpdateConnectionStatusAsync_ShouldSucceed_WhenForceUpdateIgnoresMismatch()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new UpdateConnectionStatusRequest
        {
            ConnectionId = connectionId,
            NewStatus = SettableConnectionStatus.Blocked,
            Reason = "Emergency closure",
            CurrentStatus = ConnectionStatus.Closed, // Mismatch: actual is Open
            ForceUpdate = true // Should ignore the mismatch
        };

        var model = CreateTestConnectionModel(connectionId: connectionId, status: ConnectionStatus.Open);

        var key = TransitService.BuildConnectionKey(connectionId);
        _mockConnectionStore.Setup(s => s.GetWithETagAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitConnectionModel? capturedModel = null;
        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            key, It.IsAny<TransitConnectionModel>(),
            "etag-1", It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, string, CancellationToken>((_, m, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.UpdateConnectionStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal(ConnectionStatus.Blocked, capturedModel.Status);
        Assert.Equal("Emergency closure", capturedModel.StatusReason);

        // Assert event published with captured data
        var statusEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-connection.status-changed");
        Assert.NotNull(statusEvent.Event);
        var typedEvent = Assert.IsType<TransitConnectionStatusChangedEvent>(statusEvent.Event);
        Assert.Equal(connectionId, typedEvent.ConnectionId);
    }

    /// <summary>
    /// DeleteConnectionAsync should delete connection, invalidate cache, and publish event.
    /// </summary>
    [Fact]
    public async Task DeleteConnectionAsync_ShouldDelete_WhenNoActiveJourneys()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new DeleteConnectionRequest { ConnectionId = connectionId };

        var model = CreateTestConnectionModel(connectionId: connectionId);

        var key = TransitService.BuildConnectionKey(connectionId);
        _mockConnectionStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // No active journeys
        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel>());

        // Act
        var (statusCode, response) = await service.DeleteConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        _mockConnectionStore.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once());
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    #endregion

    #region Journey Lifecycle Tests

    /// <summary>
    /// CreateJourneyAsync should validate locations, mode, calculate route, and save journey
    /// with Preparing status.
    /// </summary>
    [Fact]
    public async Task CreateJourneyAsync_ShouldCreateWithPreparingStatus_WhenRouteExists()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateJourneyRequest
        {
            EntityId = TestEntityId,
            EntityType = "character",
            OriginLocationId = TestLocationAId,
            DestinationLocationId = TestLocationBId,
            PrimaryModeCode = "walking",
            CargoWeightKg = 10.0m,
            PartySize = 1,
            PreferMultiModal = false
        };

        // Mock: locations exist
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationAId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationBId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationBId, RealmId = TestRealmId });

        // Mock: mode exists and is not deprecated
        _mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));

        // Mock: no mode restrictions for entity
        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { CreateTestModeModel("walking") });

        // Mock: worldstate returns game time
        _mockWorldstateClient.Setup(c => c.GetRealmTimeAsync(
            It.IsAny<Worldstate.GetRealmTimeRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Worldstate.GameTimeSnapshot
            {
                TimeRatio = 24.0f,
                TotalGameSecondsSinceEpoch = 360000,
                Season = "spring"
            });

        // Mock: route calculator returns one route
        var connectionId = Guid.NewGuid();
        var routeResult = new RouteCalculationResult(
            Waypoints: new List<Guid> { TestLocationAId, TestLocationBId },
            Connections: new List<Guid> { connectionId },
            LegModes: new List<string> { "walking" },
            PrimaryModeCode: "walking",
            TotalDistanceKm: 25.5m,
            TotalGameHours: 5.1m,
            TotalRealMinutes: 12.75m,
            AverageRisk: 0.1m,
            MaxLegRisk: 0.1m,
            AllLegsOpen: true,
            SeasonalWarnings: null);
        _mockRouteCalculator.Setup(r => r.CalculateAsync(
            It.IsAny<RouteCalculationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RouteCalculationResult> { routeResult });

        // Mock: connection used by the route
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestConnectionModel(connectionId: connectionId));

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.CreateJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Preparing, capturedJourney.Status);
        Assert.Equal(TestEntityId, capturedJourney.EntityId);
        Assert.Equal(TestLocationAId, capturedJourney.OriginLocationId);
        Assert.Equal(TestLocationBId, capturedJourney.DestinationLocationId);
        Assert.Equal("walking", capturedJourney.PrimaryModeCode);
        Assert.Single(capturedJourney.Legs);
    }

    /// <summary>
    /// CreateJourneyAsync should return BadRequest when mode is deprecated.
    /// </summary>
    [Fact]
    public async Task CreateJourneyAsync_ShouldReturnBadRequest_WhenModeDeprecated()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateJourneyRequest
        {
            EntityId = TestEntityId,
            EntityType = "character",
            OriginLocationId = TestLocationAId,
            DestinationLocationId = TestLocationBId,
            PrimaryModeCode = "horseback",
            PartySize = 1,
            PreferMultiModal = false
        };

        // Mock: locations exist
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        // Mock: mode is deprecated
        _mockModeStore.Setup(s => s.GetAsync("mode:horseback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Horses extinct"));

        // Act
        var (statusCode, response) = await service.CreateJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// DepartJourneyAsync should transition journey from Preparing to In_transit.
    /// </summary>
    [Fact]
    public async Task DepartJourneyAsync_ShouldTransitionToInTransit_WhenPreparing()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new DepartJourneyRequest { JourneyId = journeyId };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.Preparing);

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: first connection is open
        var firstLegConnectionId = journey.Legs[0].ConnectionId;
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(firstLegConnectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestConnectionModel(connectionId: firstLegConnectionId));

        // Mock: location and worldstate for departure time
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });
        _mockWorldstateClient.Setup(c => c.GetRealmTimeAsync(
            It.IsAny<Worldstate.GetRealmTimeRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Worldstate.GameTimeSnapshot
            {
                TimeRatio = 24.0f,
                TotalGameSecondsSinceEpoch = 360000,
                Season = "spring"
            });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.DepartJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.InTransit, capturedJourney.Status);
        Assert.NotNull(capturedJourney.ActualDepartureGameTime);
        Assert.Equal(JourneyLegStatus.InProgress, capturedJourney.Legs[0].Status);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var departedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.departed");
        Assert.NotNull(departedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyDepartedEvent>(departedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
        Assert.Equal(TestEntityId, typedEvent.EntityId);
    }

    /// <summary>
    /// DepartJourneyAsync should return BadRequest when journey is not in Preparing status.
    /// </summary>
    [Fact]
    public async Task DepartJourneyAsync_ShouldReturnBadRequest_WhenNotPreparing()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new DepartJourneyRequest { JourneyId = journeyId };

        // Journey already in transit
        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit);

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Act
        var (statusCode, response) = await service.DepartJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// InterruptJourneyAsync should add interruption record and transition to Interrupted.
    /// </summary>
    [Fact]
    public async Task InterruptJourneyAsync_ShouldTransitionToInterrupted_WhenInTransit()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new InterruptJourneyRequest
        {
            JourneyId = journeyId,
            Reason = "Bandits on the road",
            GameTime = 120.0m
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit);
        journey.Legs[0].Status = JourneyLegStatus.InProgress;

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: location for realm resolution
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.InterruptJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Interrupted, capturedJourney.Status);
        Assert.Equal("Bandits on the road", capturedJourney.StatusReason);
        Assert.Single(capturedJourney.Interruptions);
        Assert.Equal("Bandits on the road", capturedJourney.Interruptions[0].Reason);
        Assert.False(capturedJourney.Interruptions[0].Resolved);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var interruptedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.interrupted");
        Assert.NotNull(interruptedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyInterruptedEvent>(interruptedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
        Assert.Equal("Bandits on the road", typedEvent.Reason);
    }

    /// <summary>
    /// ResumeJourneyAsync should transition from Interrupted back to In_transit.
    /// </summary>
    [Fact]
    public async Task ResumeJourneyAsync_ShouldTransitionToInTransit_WhenInterrupted()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ResumeJourneyRequest { JourneyId = journeyId };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.Interrupted);
        journey.Interruptions.Add(new TransitInterruptionModel
        {
            LegIndex = 0,
            GameTime = 110.0m,
            Reason = "Bandits",
            DurationGameHours = 0,
            Resolved = false
        });

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: current connection is open
        var currentLeg = journey.Legs[0];
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(currentLeg.ConnectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestConnectionModel(connectionId: currentLeg.ConnectionId));

        // Mock: location for realm resolution
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.ResumeJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.InTransit, capturedJourney.Status);

        // All interruptions should be marked resolved
        Assert.All(capturedJourney.Interruptions, i => Assert.True(i.Resolved));

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var resumedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.resumed");
        Assert.NotNull(resumedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyResumedEvent>(resumedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
    }

    /// <summary>
    /// AbandonJourneyAsync should reject if journey status is already terminal (Arrived or Abandoned).
    /// </summary>
    [Fact]
    public async Task AbandonJourneyAsync_ShouldReturnBadRequest_WhenAlreadyArrived()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new AbandonJourneyRequest
        {
            JourneyId = journeyId,
            Reason = "Changed my mind"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.Arrived);

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Act
        var (statusCode, response) = await service.AbandonJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// AbandonJourneyAsync should transition to Abandoned from an active status.
    /// </summary>
    [Fact]
    public async Task AbandonJourneyAsync_ShouldTransitionToAbandoned_WhenActive()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new AbandonJourneyRequest
        {
            JourneyId = journeyId,
            Reason = "Too dangerous"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit);
        journey.Legs[0].Status = JourneyLegStatus.InProgress;

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: location for position reporting and realm resolution
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.AbandonJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Abandoned, capturedJourney.Status);
        Assert.Equal("Too dangerous", capturedJourney.StatusReason);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var abandonedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.abandoned");
        Assert.NotNull(abandonedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyAbandonedEvent>(abandonedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
        Assert.Equal("Too dangerous", typedEvent.Reason);
    }

    /// <summary>
    /// AdvanceJourneyAsync should transition a single-leg journey from InTransit to Arrived
    /// when the final (and only) leg is completed.
    /// </summary>
    [Fact]
    public async Task AdvanceJourneyAsync_ShouldTransitionToArrived_WhenFinalLeg()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new AdvanceJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 200.0m
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit, legCount: 1);
        journey.Legs[0].Status = JourneyLegStatus.InProgress;

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: connection for auto-reveal check (not discoverable, so no reveal)
        var legConnectionId = journey.Legs[0].ConnectionId;
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(legConnectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestConnectionModel(connectionId: legConnectionId, discoverable: false));

        // Mock: location for realm resolution and position reporting
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationBId, RealmId = TestRealmId });

        // Mock: position reporting (best-effort)
        _mockLocationClient.Setup(c => c.ReportEntityPositionAsync(
            It.IsAny<Location.ReportEntityPositionRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.ReportEntityPositionResponse());

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.AdvanceJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Arrived, capturedJourney.Status);
        Assert.Equal(200.0m, capturedJourney.ActualArrivalGameTime);
        Assert.Equal(JourneyLegStatus.Completed, capturedJourney.Legs[0].Status);
        Assert.Equal(TestLocationBId, capturedJourney.CurrentLocationId);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var arrivedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.arrived");
        Assert.NotNull(arrivedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyArrivedEvent>(arrivedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
        Assert.Equal(TestEntityId, typedEvent.EntityId);
    }

    /// <summary>
    /// AbandonJourneyAsync should transition to Abandoned from any non-terminal active status.
    /// Valid active statuses: Preparing, InTransit, AtWaypoint, Interrupted.
    /// </summary>
    [Theory]
    [InlineData(JourneyStatus.Preparing)]
    [InlineData(JourneyStatus.AtWaypoint)]
    [InlineData(JourneyStatus.Interrupted)]
    public async Task AbandonJourneyAsync_ShouldTransitionToAbandoned_WhenActiveStatus(JourneyStatus activeStatus)
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new AbandonJourneyRequest
        {
            JourneyId = journeyId,
            Reason = "Route no longer viable"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: activeStatus);

        var key = TransitService.BuildJourneyKey(journeyId);
        _mockJourneyStore.Setup(s => s.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock: location for position reporting and realm resolution
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { LocationId = TestLocationAId, RealmId = TestRealmId });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            key, It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.AbandonJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Abandoned, capturedJourney.Status);
        Assert.Equal("Route no longer viable", capturedJourney.StatusReason);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var abandonedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-journey.abandoned");
        Assert.NotNull(abandonedEvent.Event);
        var typedEvent = Assert.IsType<TransitJourneyAbandonedEvent>(abandonedEvent.Event);
        Assert.Equal(journeyId, typedEvent.JourneyId);
    }

    #endregion

    #region Discovery Tests

    /// <summary>
    /// RevealDiscoveryAsync should create new discovery record and return isNew=true for first discovery.
    /// </summary>
    [Fact]
    public async Task RevealDiscoveryAsync_ShouldCreateDiscovery_WhenNew()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var request = new RevealDiscoveryRequest
        {
            EntityId = entityId,
            ConnectionId = connectionId,
            Source = "exploration"
        };

        // Mock: connection exists and is discoverable
        var connection = CreateTestConnectionModel(connectionId: connectionId, discoverable: true);
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Mock: no existing discovery
        _mockDiscoveryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitDiscoveryModel?)null);

        // Mock: discovery cache for update
        _mockDiscoveryCacheStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<Guid>?)null);

        TransitDiscoveryModel? capturedDiscovery = null;
        _mockDiscoveryStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitDiscoveryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitDiscoveryModel, StateOptions?, CancellationToken>((_, d, _, _) => capturedDiscovery = d)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.RevealDiscoveryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Discovery);
        Assert.True(response.Discovery.IsNew);

        Assert.NotNull(capturedDiscovery);
        Assert.Equal(entityId, capturedDiscovery.EntityId);
        Assert.Equal(connectionId, capturedDiscovery.ConnectionId);
        Assert.Equal("exploration", capturedDiscovery.Source);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var revealedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit-discovery.revealed");
        Assert.NotNull(revealedEvent.Event);
        var typedEvent = Assert.IsType<TransitDiscoveryRevealedEvent>(revealedEvent.Event);
        Assert.Equal(entityId, typedEvent.EntityId);
        Assert.Equal(connectionId, typedEvent.ConnectionId);
    }

    /// <summary>
    /// RevealDiscoveryAsync should return isNew=false when entity has already discovered the connection.
    /// </summary>
    [Fact]
    public async Task RevealDiscoveryAsync_ShouldReturnIsNewFalse_WhenAlreadyDiscovered()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var request = new RevealDiscoveryRequest
        {
            EntityId = entityId,
            ConnectionId = connectionId,
            Source = "exploration"
        };

        // Mock: connection exists and is discoverable
        var connection = CreateTestConnectionModel(connectionId: connectionId, discoverable: true);
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        // Mock: existing discovery already exists
        var existingDiscovery = new TransitDiscoveryModel
        {
            EntityId = entityId,
            ConnectionId = connectionId,
            Source = "map",
            DiscoveredAt = DateTimeOffset.UtcNow.AddDays(-1),
            IsNew = false
        };
        _mockDiscoveryStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDiscovery);

        // Act
        var (statusCode, response) = await service.RevealDiscoveryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Discovery);
        Assert.False(response.Discovery.IsNew);

        // Should NOT have saved a new discovery
        _mockDiscoveryStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitDiscoveryModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region Route Calculator Tests

    /// <summary>
    /// TransitRouteCalculator constructor should be valid for DI.
    /// </summary>
    [Fact]
    public void TransitRouteCalculator_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<TransitRouteCalculator>();

    /// <summary>
    /// Creates a TransitRouteCalculator with fresh mocks for isolated route calculator tests.
    /// Returns the calculator and its mock dependencies for per-test setup.
    /// </summary>
    private static (
        TransitRouteCalculator Calculator,
        Mock<ITransitConnectionGraphCache> GraphCache,
        Mock<IQueryableStateStore<TransitModeModel>> ModeStore,
        Mock<IQueryableStateStore<TransitConnectionModel>> ConnectionStore,
        Mock<IStateStore<HashSet<Guid>>> DiscoveryCacheStore
    ) CreateRouteCalculator(TransitServiceConfiguration? configOverride = null)
    {
        var mockGraphCache = new Mock<ITransitConnectionGraphCache>();
        var mockCalcStateStoreFactory = new Mock<IStateStoreFactory>();
        var mockCalcModeStore = new Mock<IQueryableStateStore<TransitModeModel>>();
        var mockCalcConnectionStore = new Mock<IQueryableStateStore<TransitConnectionModel>>();
        var mockCalcDiscoveryCacheStore = new Mock<IStateStore<HashSet<Guid>>>();
        var calcConfig = configOverride ?? new TransitServiceConfiguration
        {
            MaxRouteCalculationLegs = 8,
            MaxRouteOptions = 5,
            DefaultWalkingSpeedKmPerGameHour = 5.0,
            MinimumEffectiveSpeedKmPerGameHour = 0.01,
            DefaultFallbackModeCode = "walking"
        };
        var mockCalcTelemetry = new Mock<ITelemetryProvider>();
        var mockCalcLogger = new Mock<ILogger<TransitRouteCalculator>>();

        mockCalcStateStoreFactory.Setup(f => f.GetQueryableStore<TransitModeModel>(StateStoreDefinitions.TransitModes))
            .Returns(mockCalcModeStore.Object);
        mockCalcStateStoreFactory.Setup(f => f.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections))
            .Returns(mockCalcConnectionStore.Object);
        mockCalcStateStoreFactory.Setup(f => f.GetStore<HashSet<Guid>>(StateStoreDefinitions.TransitDiscoveryCache))
            .Returns(mockCalcDiscoveryCacheStore.Object);

        var calculator = new TransitRouteCalculator(
            mockGraphCache.Object,
            mockCalcStateStoreFactory.Object,
            calcConfig,
            mockCalcTelemetry.Object,
            mockCalcLogger.Object);

        return (calculator, mockGraphCache, mockCalcModeStore, mockCalcConnectionStore, mockCalcDiscoveryCacheStore);
    }

    /// <summary>
    /// CalculateAsync should return empty list when no route exists between locations.
    /// </summary>
    [Fact]
    public async Task CalculateAsync_ShouldReturnEmptyList_WhenNoRouteExists()
    {
        // Arrange
        var (calculator, mockGraphCache, _, mockConnectionStore, _) = CreateRouteCalculator();

        // No connections touch origin or destination -- route calculator returns empty before graph lookup
        mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel>());

        // Return empty graph: no connections (backup, in case realm discovery finds something)
        mockGraphCache.Setup(g => g.GetGraphAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionGraphEntry>());

        var request = new RouteCalculationRequest(
            OriginLocationId: TestLocationAId,
            DestinationLocationId: TestLocationBId,
            ModeCode: "walking",
            PreferMultiModal: false,
            SortBy: RouteSortBy.Fastest,
            EntityId: TestEntityId,
            IncludeSeasonalClosed: false,
            CargoWeightKg: 0m,
            MaxLegs: 8,
            MaxOptions: 5,
            CurrentTimeRatio: 24m,
            CurrentSeason: "spring");

        // Act
        var results = await calculator.CalculateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /// <summary>
    /// CalculateAsync should return route when direct connection exists between locations.
    /// </summary>
    [Fact]
    public async Task CalculateAsync_ShouldReturnRoute_WhenDirectConnectionExists()
    {
        // Arrange
        var (calculator, mockGraphCache, mockModeStore, mockConnectionStore, mockDiscoveryCacheStore) =
            CreateRouteCalculator();

        var connectionId = Guid.NewGuid();

        // Graph has a direct connection from A to B
        var graphEntry = new ConnectionGraphEntry
        {
            ConnectionId = connectionId,
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            DistanceKm = 20.0m,
            TerrainType = "road",
            BaseRiskLevel = 0.1m,
            CompatibleModes = new List<string> { "walking" },
            Status = ConnectionStatus.Open,
            Discoverable = false
        };

        mockGraphCache.Setup(g => g.GetGraphAsync(TestRealmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionGraphEntry> { graphEntry });

        // Mode exists
        mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));
        mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { CreateTestModeModel("walking") });

        // Discovery cache: entity has discovered everything (or connection is not discoverable)
        mockDiscoveryCacheStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { connectionId });

        // Connection store for realm determination (QueryAsync for BuildFilteredGraphAsync)
        var connModel = CreateTestConnectionModel(connectionId: connectionId);
        mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { connModel });
        mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(connModel);

        var request = new RouteCalculationRequest(
            OriginLocationId: TestLocationAId,
            DestinationLocationId: TestLocationBId,
            ModeCode: "walking",
            PreferMultiModal: false,
            SortBy: RouteSortBy.Fastest,
            EntityId: TestEntityId,
            IncludeSeasonalClosed: false,
            CargoWeightKg: 0m,
            MaxLegs: 8,
            MaxOptions: 5,
            CurrentTimeRatio: 24m,
            CurrentSeason: "spring");

        // Act
        var results = await calculator.CalculateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);

        var route = results[0];
        Assert.Equal(2, route.Waypoints.Count);
        Assert.Equal(TestLocationAId, route.Waypoints[0]);
        Assert.Equal(TestLocationBId, route.Waypoints[1]);
        Assert.Single(route.Connections);
        Assert.Equal(connectionId, route.Connections[0]);
    }

    /// <summary>
    /// CalculateAsync should exclude connections with SeasonalClosed status when
    /// IncludeSeasonalClosed is false. Only the open connection should be used.
    /// </summary>
    [Fact]
    public async Task CalculateAsync_ShouldExcludeSeasonalClosed_WhenNotIncluded()
    {
        // Arrange
        var (calculator, mockGraphCache, mockModeStore, mockConnectionStore, mockDiscoveryCacheStore) =
            CreateRouteCalculator();

        var openConnectionId = Guid.NewGuid();
        var closedConnectionId = Guid.NewGuid();

        // Two connections: A->B via open, A->B via seasonal closed
        var openEdge = new ConnectionGraphEntry
        {
            ConnectionId = openConnectionId,
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            DistanceKm = 50.0m, // Longer but open
            TerrainType = "road",
            BaseRiskLevel = 0.1m,
            CompatibleModes = new List<string> { "walking" },
            Status = ConnectionStatus.Open,
            Discoverable = false
        };

        var closedEdge = new ConnectionGraphEntry
        {
            ConnectionId = closedConnectionId,
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            DistanceKm = 10.0m, // Shorter but seasonal closed
            TerrainType = "road",
            BaseRiskLevel = 0.0m,
            CompatibleModes = new List<string> { "walking" },
            Status = ConnectionStatus.SeasonalClosed,
            Discoverable = false
        };

        mockGraphCache.Setup(g => g.GetGraphAsync(TestRealmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionGraphEntry> { openEdge, closedEdge });

        // Mode exists
        mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));
        mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { CreateTestModeModel("walking") });

        // Discovery cache: not relevant (connections are not discoverable)
        mockDiscoveryCacheStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { openConnectionId, closedConnectionId });

        // Connection store for realm determination
        mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel>
            {
                CreateTestConnectionModel(connectionId: openConnectionId),
                CreateTestConnectionModel(connectionId: closedConnectionId, status: ConnectionStatus.SeasonalClosed)
            });

        var request = new RouteCalculationRequest(
            OriginLocationId: TestLocationAId,
            DestinationLocationId: TestLocationBId,
            ModeCode: "walking",
            PreferMultiModal: false,
            SortBy: RouteSortBy.Fastest,
            EntityId: TestEntityId,
            IncludeSeasonalClosed: false, // Exclude seasonal closures
            CargoWeightKg: 0m,
            MaxLegs: 8,
            MaxOptions: 5,
            CurrentTimeRatio: 24m,
            CurrentSeason: "winter");

        // Act
        var results = await calculator.CalculateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);

        // All routes should use only the open connection, not the seasonal closed one
        foreach (var route in results)
        {
            Assert.DoesNotContain(closedConnectionId, route.Connections);
            Assert.Contains(openConnectionId, route.Connections);
        }
    }

    /// <summary>
    /// CalculateAsync should exclude discoverable connections that the entity has not discovered.
    /// Only non-discoverable and discovered connections should be used.
    /// </summary>
    [Fact]
    public async Task CalculateAsync_ShouldFilterUndiscovered_WhenDiscoverableConnectionExists()
    {
        // Arrange
        var (calculator, mockGraphCache, mockModeStore, mockConnectionStore, mockDiscoveryCacheStore) =
            CreateRouteCalculator();

        var discoveredConnectionId = Guid.NewGuid();
        var undiscoveredConnectionId = Guid.NewGuid();

        // Two connections: A->B via discovered, A->B via undiscovered discoverable
        var discoveredEdge = new ConnectionGraphEntry
        {
            ConnectionId = discoveredConnectionId,
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            DistanceKm = 50.0m, // Longer but discovered
            TerrainType = "road",
            BaseRiskLevel = 0.1m,
            CompatibleModes = new List<string> { "walking" },
            Status = ConnectionStatus.Open,
            Discoverable = true
        };

        var undiscoveredEdge = new ConnectionGraphEntry
        {
            ConnectionId = undiscoveredConnectionId,
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            DistanceKm = 5.0m, // Shorter but not yet discovered
            TerrainType = "trail",
            BaseRiskLevel = 0.0m,
            CompatibleModes = new List<string> { "walking" },
            Status = ConnectionStatus.Open,
            Discoverable = true
        };

        mockGraphCache.Setup(g => g.GetGraphAsync(TestRealmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionGraphEntry> { discoveredEdge, undiscoveredEdge });

        // Mode exists
        mockModeStore.Setup(s => s.GetAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestModeModel("walking"));
        mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { CreateTestModeModel("walking") });

        // Discovery cache: entity has only discovered one connection
        mockDiscoveryCacheStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { discoveredConnectionId }); // Only first one discovered

        // Connection store for realm determination
        mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel>
            {
                CreateTestConnectionModel(connectionId: discoveredConnectionId, discoverable: true),
                CreateTestConnectionModel(connectionId: undiscoveredConnectionId, discoverable: true)
            });

        var request = new RouteCalculationRequest(
            OriginLocationId: TestLocationAId,
            DestinationLocationId: TestLocationBId,
            ModeCode: "walking",
            PreferMultiModal: false,
            SortBy: RouteSortBy.Fastest,
            EntityId: TestEntityId, // Entity provided, so discovery cache is checked
            IncludeSeasonalClosed: false,
            CargoWeightKg: 0m,
            MaxLegs: 8,
            MaxOptions: 5,
            CurrentTimeRatio: 24m,
            CurrentSeason: "spring");

        // Act
        var results = await calculator.CalculateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);

        // All routes should use only the discovered connection, not the undiscovered one
        foreach (var route in results)
        {
            Assert.DoesNotContain(undiscoveredConnectionId, route.Connections);
            Assert.Contains(discoveredConnectionId, route.Connections);
        }
    }

    #endregion
}
