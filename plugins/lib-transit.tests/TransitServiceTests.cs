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

    // Client events, entity session registry, logging, telemetry, configuration, event consumer
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
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

        // Client events, entity session registry, logging, telemetry
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();
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
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        _mockConnectionStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
        // Full overload (5 params)
        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>((topic, evt, _, _, _) =>
            {
                _capturedEvents.Add((topic, evt));
            })
            .ReturnsAsync(true);
        // Convenience overload (3 params) - the one services actually call
        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
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
            _mockEntitySessionRegistry.Object,
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
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal("horseback", capturedModel.Code);
        Assert.Equal("Horseback Riding", capturedModel.Name);
        Assert.Equal(15.0m, capturedModel.BaseSpeedKmPerGameHour);
        Assert.False(capturedModel.IsDeprecated);

        // Assert event published with captured data per TESTING-PATTERNS capture pattern
        var registeredEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.mode.registered");
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
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
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
        var updatedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.mode.updated");
        Assert.NotNull(updatedEvent.Event);
        var typedEvent = Assert.IsType<TransitModeUpdatedEvent>(updatedEvent.Event);
        Assert.Equal("horseback", typedEvent.Mode.Code);
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
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
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
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
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
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
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
        var deletedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.mode.deleted");
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
        Assert.Equal(StatusCodes.OK, statusCode);
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
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
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
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
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
        var statusEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.connection.status-changed");
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
        Assert.Equal(StatusCodes.OK, statusCode);
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
        var departedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.departed");
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
        var interruptedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.interrupted");
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
        var resumedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.resumed");
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
        var abandonedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.abandoned");
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
        var arrivedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.arrived");
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
        var abandonedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.abandoned");
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
        var revealedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.discovery.revealed");
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

    #region ListModesAsync Tests

    /// <summary>
    /// ListModesAsync should return all non-deprecated modes when no filters specified.
    /// </summary>
    [Fact]
    public async Task ListModesAsync_ShouldReturnNonDeprecatedModes_WhenNoFilters()
    {
        // Arrange
        var service = CreateService();
        var request = new ListModesRequest { IncludeDeprecated = false };

        var activeMode = CreateTestModeModel("walking");
        var deprecatedMode = CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Obsolete");

        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { activeMode, deprecatedMode });

        // Act
        var (statusCode, response) = await service.ListModesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Modes);
        Assert.Equal("walking", response.Modes.First().Code);
    }

    /// <summary>
    /// ListModesAsync should include deprecated modes when IncludeDeprecated is true.
    /// </summary>
    [Fact]
    public async Task ListModesAsync_ShouldIncludeDeprecated_WhenFlagIsTrue()
    {
        // Arrange
        var service = CreateService();
        var request = new ListModesRequest { IncludeDeprecated = true };

        var activeMode = CreateTestModeModel("walking");
        var deprecatedMode = CreateTestModeModel("horseback", isDeprecated: true, deprecationReason: "Obsolete");

        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { activeMode, deprecatedMode });

        // Act
        var (statusCode, response) = await service.ListModesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Modes.Count);
    }

    /// <summary>
    /// ListModesAsync should filter by terrain type when specified.
    /// </summary>
    [Fact]
    public async Task ListModesAsync_ShouldFilterByTerrainType()
    {
        // Arrange
        var service = CreateService();
        var request = new ListModesRequest { TerrainType = "water" };

        var landMode = CreateTestModeModel("walking");
        landMode.CompatibleTerrainTypes = new List<string> { "road", "trail" };

        var waterMode = CreateTestModeModel("boat");
        waterMode.CompatibleTerrainTypes = new List<string> { "water", "river" };

        var anyTerrainMode = CreateTestModeModel("flying");
        anyTerrainMode.CompatibleTerrainTypes = new List<string>(); // Empty = all terrain

        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { landMode, waterMode, anyTerrainMode });

        // Act
        var (statusCode, response) = await service.ListModesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        // Should include "boat" (has water) and "flying" (empty = all terrain)
        Assert.Equal(2, response.Modes.Count);
        Assert.Contains(response.Modes, m => m.Code == "boat");
        Assert.Contains(response.Modes, m => m.Code == "flying");
    }

    /// <summary>
    /// ListModesAsync should filter by realm restrictions when RealmId specified.
    /// </summary>
    [Fact]
    public async Task ListModesAsync_ShouldFilterByRealmRestrictions()
    {
        // Arrange
        var service = CreateService();
        var targetRealmId = Guid.NewGuid();
        var request = new ListModesRequest { RealmId = targetRealmId };

        var unrestrictedMode = CreateTestModeModel("walking");
        unrestrictedMode.RealmRestrictions = null; // No restrictions

        var restrictedMatch = CreateTestModeModel("horseback");
        restrictedMatch.RealmRestrictions = new List<Guid> { targetRealmId, Guid.NewGuid() };

        var restrictedNoMatch = CreateTestModeModel("boat");
        restrictedNoMatch.RealmRestrictions = new List<Guid> { Guid.NewGuid() }; // Different realm

        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { unrestrictedMode, restrictedMatch, restrictedNoMatch });

        // Act
        var (statusCode, response) = await service.ListModesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Modes.Count);
        Assert.Contains(response.Modes, m => m.Code == "walking");
        Assert.Contains(response.Modes, m => m.Code == "horseback");
        Assert.DoesNotContain(response.Modes, m => m.Code == "boat");
    }

    /// <summary>
    /// ListModesAsync should filter by tags requiring all specified tags present.
    /// </summary>
    [Fact]
    public async Task ListModesAsync_ShouldFilterByTags_RequiringAll()
    {
        // Arrange
        var service = CreateService();
        var request = new ListModesRequest { Tags = new List<string> { "fast", "land" } };

        var mode1 = CreateTestModeModel("walking");
        mode1.Tags = new List<string> { "slow", "land" }; // Missing "fast"

        var mode2 = CreateTestModeModel("horseback");
        mode2.Tags = new List<string> { "fast", "land", "mounted" }; // Has both

        _mockModeStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitModeModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitModeModel> { mode1, mode2 });

        // Act
        var (statusCode, response) = await service.ListModesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Modes);
        Assert.Equal("horseback", response.Modes.First().Code);
    }

    #endregion

    #region UpdateModeAsync Tests

    /// <summary>
    /// UpdateModeAsync should update fields and publish event when mode exists.
    /// </summary>
    [Fact]
    public async Task UpdateModeAsync_ShouldUpdateFieldsAndPublishEvent_WhenModeExists()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateModeRequest
        {
            Code = "walking",
            Name = "Fast Walking",
            BaseSpeedKmPerGameHour = 7.0m
        };

        var model = CreateTestModeModel("walking");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitModeModel? capturedModel = null;
        _mockModeStore.Setup(s => s.TrySaveAsync(
            "mode:walking", It.IsAny<TransitModeModel>(),
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitModeModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.UpdateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal("Fast Walking", capturedModel.Name);
        Assert.Equal(7.0m, capturedModel.BaseSpeedKmPerGameHour);

        // Assert event published
        var updatedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.mode.updated");
        Assert.NotNull(updatedEvent.Event);
        var typedEvent = Assert.IsType<TransitModeUpdatedEvent>(updatedEvent.Event);
        Assert.Contains("name", typedEvent.ChangedFields);
        Assert.Contains("baseSpeedKmPerGameHour", typedEvent.ChangedFields);
    }

    /// <summary>
    /// UpdateModeAsync should return NotFound when mode code does not exist.
    /// </summary>
    [Fact]
    public async Task UpdateModeAsync_ShouldReturnNotFound_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateModeRequest { Code = "nonexistent", Name = "Test" };

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((TransitModeModel?)null, (string?)null));

        // Act
        var (statusCode, response) = await service.UpdateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// UpdateModeAsync should skip save and event when no fields changed.
    /// </summary>
    [Fact]
    public async Task UpdateModeAsync_ShouldSkipSave_WhenNoFieldsChanged()
    {
        // Arrange
        var service = CreateService();
        // Request with no updatable fields set (only Code is required)
        var request = new UpdateModeRequest { Code = "walking" };

        var model = CreateTestModeModel("walking");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        // Act
        var (statusCode, response) = await service.UpdateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Should NOT have called TrySaveAsync (no changes)
        _mockModeStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitModeModel>(),
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never());

        // Should NOT have published any events
        Assert.Empty(_capturedEvents);
    }

    /// <summary>
    /// UpdateModeAsync should return Conflict on concurrent modification (ETag mismatch).
    /// </summary>
    [Fact]
    public async Task UpdateModeAsync_ShouldReturnConflict_OnETagMismatch()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateModeRequest { Code = "walking", Name = "Updated" };

        var model = CreateTestModeModel("walking");

        _mockModeStore.Setup(s => s.GetWithETagAsync("mode:walking", It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        // Simulate ETag mismatch: TrySaveAsync returns null
        _mockModeStore.Setup(s => s.TrySaveAsync(
            "mode:walking", It.IsAny<TransitModeModel>(),
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (statusCode, response) = await service.UpdateModeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region GetConnectionAsync Tests

    /// <summary>
    /// GetConnectionAsync should return connection when found by ID.
    /// </summary>
    [Fact]
    public async Task GetConnectionAsync_ShouldReturnConnection_WhenFoundById()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new GetConnectionRequest { ConnectionId = connectionId };

        var model = CreateTestConnectionModel(connectionId: connectionId);

        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (statusCode, response) = await service.GetConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(connectionId, response.Connection.Id);
    }

    /// <summary>
    /// GetConnectionAsync should return connection when found by code.
    /// </summary>
    [Fact]
    public async Task GetConnectionAsync_ShouldReturnConnection_WhenFoundByCode()
    {
        // Arrange
        var service = CreateService();
        var request = new GetConnectionRequest { Code = "ironforge-stormwind" };

        var model = CreateTestConnectionModel();
        model.Code = "ironforge-stormwind";

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { model });

        // Act
        var (statusCode, response) = await service.GetConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
    }

    /// <summary>
    /// GetConnectionAsync should return NotFound when neither ID nor code matches.
    /// </summary>
    [Fact]
    public async Task GetConnectionAsync_ShouldReturnNotFound_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new GetConnectionRequest { ConnectionId = Guid.NewGuid() };

        _mockConnectionStore.Setup(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitConnectionModel?)null);

        // Act
        var (statusCode, response) = await service.GetConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region QueryConnectionsAsync Tests

    /// <summary>
    /// QueryConnectionsAsync should return all connections with no filters.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldReturnAll_WhenNoFilters()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryConnectionsRequest
        {
            Page = 1,
            PageSize = 20,
            IncludeSeasonalClosed = true
        };

        var conn1 = CreateTestConnectionModel(connectionId: Guid.NewGuid());
        var conn2 = CreateTestConnectionModel(connectionId: Guid.NewGuid());

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { conn1, conn2 });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Connections.Count);
        Assert.Equal(2, response.TotalCount);
    }

    /// <summary>
    /// QueryConnectionsAsync should filter by locationId matching either from or to.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldFilterByLocationId()
    {
        // Arrange
        var service = CreateService();
        var targetLocationId = Guid.NewGuid();
        var request = new QueryConnectionsRequest
        {
            LocationId = targetLocationId,
            Page = 1,
            PageSize = 20,
            IncludeSeasonalClosed = true
        };

        var matchFrom = CreateTestConnectionModel(connectionId: Guid.NewGuid(), fromLocationId: targetLocationId);
        var matchTo = CreateTestConnectionModel(connectionId: Guid.NewGuid(), toLocationId: targetLocationId);
        var noMatch = CreateTestConnectionModel(connectionId: Guid.NewGuid());

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { matchFrom, matchTo, noMatch });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
    }

    /// <summary>
    /// QueryConnectionsAsync should exclude seasonal_closed connections by default.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldExcludeSeasonalClosed_ByDefault()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryConnectionsRequest
        {
            Page = 1,
            PageSize = 20,
            IncludeSeasonalClosed = false
        };

        var openConn = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.Open);
        var closedConn = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.SeasonalClosed);

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { openConn, closedConn });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Connections);
        Assert.Equal(1, response.TotalCount);
    }

    /// <summary>
    /// QueryConnectionsAsync should paginate results correctly.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldPaginateResults()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryConnectionsRequest
        {
            Page = 2,
            PageSize = 1,
            IncludeSeasonalClosed = true
        };

        var conn1 = CreateTestConnectionModel(connectionId: Guid.NewGuid());
        var conn2 = CreateTestConnectionModel(connectionId: Guid.NewGuid());
        var conn3 = CreateTestConnectionModel(connectionId: Guid.NewGuid());

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { conn1, conn2, conn3 });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Connections);
        Assert.Equal(3, response.TotalCount); // Total is 3, but page 2 with size 1 returns 1
    }

    #endregion

    #region UpdateConnectionAsync Tests

    /// <summary>
    /// UpdateConnectionAsync should update fields, invalidate graph cache, and publish event.
    /// </summary>
    [Fact]
    public async Task UpdateConnectionAsync_ShouldUpdateAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new UpdateConnectionRequest
        {
            ConnectionId = connectionId,
            DistanceKm = 25.0m,
            TerrainType = "mountain"
        };

        var model = CreateTestConnectionModel(connectionId: connectionId);

        _mockConnectionStore.Setup(s => s.GetWithETagAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((model, "etag-1"));

        TransitConnectionModel? capturedModel = null;
        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<TransitConnectionModel>(),
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitConnectionModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => capturedModel = m)
            .ReturnsAsync("etag-2");

        // Act
        var (statusCode, response) = await service.UpdateConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedModel);
        Assert.Equal(25.0m, capturedModel.DistanceKm);
        Assert.Equal("mountain", capturedModel.TerrainType);

        // Verify graph cache was invalidated
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>
    /// UpdateConnectionAsync should return NotFound when connection does not exist.
    /// </summary>
    [Fact]
    public async Task UpdateConnectionAsync_ShouldReturnNotFound_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateConnectionRequest
        {
            ConnectionId = Guid.NewGuid(),
            DistanceKm = 25.0m
        };

        _mockConnectionStore.Setup(s => s.GetWithETagAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((TransitConnectionModel?)null, (string?)null));

        // Act
        var (statusCode, response) = await service.UpdateConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ArriveJourneyAsync Tests

    /// <summary>
    /// ArriveJourneyAsync should mark journey as Arrived, skip remaining legs, and publish event.
    /// </summary>
    [Fact]
    public async Task ArriveJourneyAsync_ShouldArriveJourney_WhenInTransit()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ArriveJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 150.0m,
            Reason = "teleportation"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit, legCount: 3);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.CurrentLegIndex = 1;
        journey.Legs[1].Status = JourneyLegStatus.InProgress;

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Mock location client for ResolveLocationRealmIdAsync
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { RealmId = TestRealmId });

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var (statusCode, response) = await service.ArriveJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Arrived, capturedJourney.Status);
        Assert.Equal("teleportation", capturedJourney.StatusReason);
        Assert.Equal(150.0m, capturedJourney.ActualArrivalGameTime);

        // Remaining legs should be skipped
        Assert.Equal(JourneyLegStatus.Completed, capturedJourney.Legs[0].Status); // Already completed
        Assert.Equal(JourneyLegStatus.Skipped, capturedJourney.Legs[1].Status);
        Assert.Equal(JourneyLegStatus.Skipped, capturedJourney.Legs[2].Status);

        // Should publish arrived event
        var arrivedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.arrived");
        Assert.NotNull(arrivedEvent.Event);
    }

    /// <summary>
    /// ArriveJourneyAsync should return BadRequest when journey status is not InTransit or AtWaypoint.
    /// </summary>
    [Fact]
    public async Task ArriveJourneyAsync_ShouldReturnBadRequest_WhenStatusInvalid()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ArriveJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 150.0m,
            Reason = "teleportation"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.Preparing);

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Act
        var (statusCode, response) = await service.ArriveJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// ArriveJourneyAsync should return NotFound when journey does not exist.
    /// </summary>
    [Fact]
    public async Task ArriveJourneyAsync_ShouldReturnNotFound_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ArriveJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 150.0m,
            Reason = "teleportation"
        };

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitJourneyModel?)null);

        // Act
        var (statusCode, response) = await service.ArriveJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// ArriveJourneyAsync should return Conflict when lock cannot be acquired.
    /// </summary>
    [Fact]
    public async Task ArriveJourneyAsync_ShouldReturnConflict_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ArriveJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 150.0m,
            Reason = "teleportation"
        };

        // Override default lock to fail
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        failedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (statusCode, response) = await service.ArriveJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);

        // Restore default lock for other tests
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLockResponse.Object);
    }

    #endregion

    #region GetJourneyAsync Tests

    /// <summary>
    /// GetJourneyAsync should return journey from Redis when found in active store.
    /// </summary>
    [Fact]
    public async Task GetJourneyAsync_ShouldReturnFromRedis_WhenActive()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new GetJourneyRequest { JourneyId = journeyId };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit);

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        // Act
        var (statusCode, response) = await service.GetJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(journeyId, response.Journey.Id);
    }

    /// <summary>
    /// GetJourneyAsync should fall back to archive store when not in Redis.
    /// </summary>
    [Fact]
    public async Task GetJourneyAsync_ShouldFallbackToArchive_WhenNotInRedis()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new GetJourneyRequest { JourneyId = journeyId };

        // Redis returns null
        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitJourneyModel?)null);

        // Archive has it
        var archivedJourney = new JourneyArchiveModel
        {
            Id = journeyId,
            EntityId = TestEntityId,
            EntityType = "character",
            Legs = new List<TransitJourneyLegModel>(),
            CurrentLegIndex = 0,
            PrimaryModeCode = "walking",
            EffectiveSpeedKmPerGameHour = 5.0m,
            PlannedDepartureGameTime = 100.0m,
            OriginLocationId = TestLocationAId,
            DestinationLocationId = TestLocationBId,
            CurrentLocationId = TestLocationBId,
            Status = JourneyStatus.Arrived,
            Interruptions = new List<TransitInterruptionModel>(),
            PartySize = 1,
            CargoWeightKg = 0m,
            RealmId = TestRealmId,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            ArchivedAt = DateTimeOffset.UtcNow
        };

        _mockJourneyArchiveStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyArchiveKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedJourney);

        // Act
        var (statusCode, response) = await service.GetJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(journeyId, response.Journey.Id);
    }

    /// <summary>
    /// GetJourneyAsync should return NotFound when journey is in neither store.
    /// </summary>
    [Fact]
    public async Task GetJourneyAsync_ShouldReturnNotFound_WhenInNeitherStore()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new GetJourneyRequest { JourneyId = journeyId };

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitJourneyModel?)null);

        _mockJourneyArchiveStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyArchiveKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JourneyArchiveModel?)null);

        // Act
        var (statusCode, response) = await service.GetJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ListJourneysAsync Tests

    /// <summary>
    /// ListJourneysAsync should return filtered journeys from archive store.
    /// </summary>
    [Fact]
    public async Task ListJourneysAsync_ShouldReturnFilteredJourneys()
    {
        // Arrange
        var service = CreateService();
        var request = new ListJourneysRequest
        {
            EntityId = TestEntityId,
            ActiveOnly = false,
            Page = 1,
            PageSize = 20
        };

        var archived = new List<JourneyArchiveModel>
        {
            CreateTestArchiveModel(status: JourneyStatus.Arrived),
            CreateTestArchiveModel(status: JourneyStatus.InTransit)
        };

        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(archived);

        // Act
        var (statusCode, response) = await service.ListJourneysAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Journeys.Count);
    }

    /// <summary>
    /// ListJourneysAsync should paginate results correctly.
    /// </summary>
    [Fact]
    public async Task ListJourneysAsync_ShouldPaginateResults()
    {
        // Arrange
        var service = CreateService();
        var request = new ListJourneysRequest
        {
            ActiveOnly = false,
            Page = 1,
            PageSize = 1
        };

        var archived = new List<JourneyArchiveModel>
        {
            CreateTestArchiveModel(status: JourneyStatus.Arrived),
            CreateTestArchiveModel(status: JourneyStatus.Abandoned)
        };

        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(archived);

        // Act
        var (statusCode, response) = await service.ListJourneysAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.Single(response.Journeys); // PageSize = 1
    }

    #endregion

    #region QueryJourneysByConnectionAsync Tests

    /// <summary>
    /// QueryJourneysByConnectionAsync should return matching journeys when connection exists.
    /// </summary>
    [Fact]
    public async Task QueryJourneysByConnectionAsync_ShouldReturnMatches_WhenConnectionExists()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new QueryJourneysByConnectionRequest
        {
            ConnectionId = connectionId,
            Page = 1,
            PageSize = 20
        };

        // Connection exists
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestConnectionModel(connectionId: connectionId));

        // Archive has matching journeys
        var archived = new List<JourneyArchiveModel>
        {
            CreateTestArchiveModel(status: JourneyStatus.Arrived)
        };

        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(archived);

        // Act
        var (statusCode, response) = await service.QueryJourneysByConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
    }

    /// <summary>
    /// QueryJourneysByConnectionAsync should return NotFound when connection does not exist.
    /// </summary>
    [Fact]
    public async Task QueryJourneysByConnectionAsync_ShouldReturnNotFound_WhenConnectionMissing()
    {
        // Arrange
        var service = CreateService();
        var connectionId = Guid.NewGuid();
        var request = new QueryJourneysByConnectionRequest
        {
            ConnectionId = connectionId,
            Page = 1,
            PageSize = 20
        };

        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitConnectionModel?)null);

        // Act
        var (statusCode, response) = await service.QueryJourneysByConnectionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region AdvanceBatchJourneysAsync Tests

    /// <summary>
    /// AdvanceBatchJourneysAsync should process each entry and return results.
    /// </summary>
    [Fact]
    public async Task AdvanceBatchJourneysAsync_ShouldProcessAllEntries()
    {
        // Arrange
        var service = CreateService();
        var journeyId1 = Guid.NewGuid();
        var journeyId2 = Guid.NewGuid();
        var request = new AdvanceBatchRequest
        {
            Advances = new List<BatchAdvanceEntry>
            {
                new BatchAdvanceEntry { JourneyId = journeyId1, ArrivedAtGameTime = 110.0m },
                new BatchAdvanceEntry { JourneyId = journeyId2, ArrivedAtGameTime = 120.0m }
            }
        };

        // Journey 1: InTransit with one leg, will advance successfully
        var journey1 = CreateTestJourneyModel(journeyId: journeyId1, status: JourneyStatus.InTransit, legCount: 1);
        journey1.Legs[0].Status = JourneyLegStatus.InProgress;

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey1);

        // Journey 2: Not found
        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId2), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitJourneyModel?)null);

        // Mock location client for arrive events
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { RealmId = TestRealmId });

        // Act
        var (statusCode, response) = await service.AdvanceBatchJourneysAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);

        // First should succeed
        var result1 = response.Results.First(r => r.JourneyId == journeyId1);
        Assert.Null(result1.Error);

        // Second should fail (not found)
        var result2 = response.Results.First(r => r.JourneyId == journeyId2);
        Assert.NotNull(result2.Error);
    }

    #endregion

    #region CalculateRouteAsync (Public API) Tests

    /// <summary>
    /// CalculateRouteAsync should validate locations and delegate to route calculator.
    /// </summary>
    [Fact]
    public async Task CalculateRouteAsync_ShouldDelegateToCalculator_WhenLocationsValid()
    {
        // Arrange
        var service = CreateService();
        var request = new CalculateRouteRequest
        {
            FromLocationId = TestLocationAId,
            ToLocationId = TestLocationBId,
            ModeCode = "walking",
            PreferMultiModal = false,
            SortBy = RouteSortBy.Fastest
        };

        // Mock location client
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationAId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse
            {
                LocationId = TestLocationAId,
                RealmId = TestRealmId,
                Code = "location-a",
                Name = "Location A"
            });

        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationBId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse
            {
                LocationId = TestLocationBId,
                RealmId = TestRealmId,
                Code = "location-b",
                Name = "Location B"
            });

        // Mock worldstate for time ratio
        _mockWorldstateClient.Setup(c => c.GetRealmTimeAsync(
            It.IsAny<Worldstate.GetRealmTimeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Worldstate.GameTimeSnapshot
            {
                RealmId = TestRealmId,
                TimeRatio = 24.0f,
                Season = "spring"
            });

        // Mock route calculator to return one route
        _mockRouteCalculator.Setup(c => c.CalculateAsync(
            It.IsAny<RouteCalculationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RouteCalculationResult>
            {
                new RouteCalculationResult(
                    Waypoints: new List<Guid> { TestLocationAId, TestLocationBId },
                    Connections: new List<Guid> { TestConnectionId },
                    LegModes: new List<string> { "walking" },
                    PrimaryModeCode: "walking",
                    TotalDistanceKm: 10.0m,
                    TotalGameHours: 2.0m,
                    TotalRealMinutes: 5.0m,
                    AverageRisk: 0.1m,
                    MaxLegRisk: 0.1m,
                    AllLegsOpen: true,
                    SeasonalWarnings: null)
            });

        // Act
        var (statusCode, response) = await service.CalculateRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Options);
        Assert.Equal("walking", response.Options.First().PrimaryModeCode);
    }

    /// <summary>
    /// CalculateRouteAsync should return BadRequest when from location does not exist.
    /// </summary>
    [Fact]
    public async Task CalculateRouteAsync_ShouldReturnBadRequest_WhenFromLocationMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new CalculateRouteRequest
        {
            FromLocationId = Guid.NewGuid(),
            ToLocationId = TestLocationBId
        };

        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == request.FromLocationId),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        // Act
        var (statusCode, response) = await service.CalculateRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    /// <summary>
    /// CalculateRouteAsync should return BadRequest when to location does not exist.
    /// </summary>
    [Fact]
    public async Task CalculateRouteAsync_ShouldReturnBadRequest_WhenToLocationMissing()
    {
        // Arrange
        var service = CreateService();
        var request = new CalculateRouteRequest
        {
            FromLocationId = TestLocationAId,
            ToLocationId = Guid.NewGuid()
        };

        // From location exists
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == TestLocationAId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse
            {
                LocationId = TestLocationAId,
                RealmId = TestRealmId,
                Code = "a",
                Name = "A"
            });

        // To location does not exist
        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.Is<Location.GetLocationRequest>(r => r.LocationId == request.ToLocationId),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        // Act
        var (statusCode, response) = await service.CalculateRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ListDiscoveriesAsync Tests

    /// <summary>
    /// ListDiscoveriesAsync should return all discoveries for an entity without realm filter.
    /// </summary>
    [Fact]
    public async Task ListDiscoveriesAsync_ShouldReturnAllDiscoveries_WhenNoRealmFilter()
    {
        // Arrange
        var service = CreateService();
        var request = new ListDiscoveriesRequest { EntityId = TestEntityId };

        var conn1Id = Guid.NewGuid();
        var conn2Id = Guid.NewGuid();

        _mockDiscoveryStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitDiscoveryModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitDiscoveryModel>
            {
                new TransitDiscoveryModel { EntityId = TestEntityId, ConnectionId = conn1Id, Source = "travel", DiscoveredAt = DateTimeOffset.UtcNow },
                new TransitDiscoveryModel { EntityId = TestEntityId, ConnectionId = conn2Id, Source = "guide", DiscoveredAt = DateTimeOffset.UtcNow }
            });

        // Act
        var (statusCode, response) = await service.ListDiscoveriesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.ConnectionIds.Count);
        Assert.Contains(conn1Id, response.ConnectionIds);
        Assert.Contains(conn2Id, response.ConnectionIds);
    }

    /// <summary>
    /// ListDiscoveriesAsync should filter by realm when RealmId specified.
    /// </summary>
    [Fact]
    public async Task ListDiscoveriesAsync_ShouldFilterByRealm_WhenRealmIdSpecified()
    {
        // Arrange
        var service = CreateService();
        var targetRealmId = Guid.NewGuid();
        var request = new ListDiscoveriesRequest { EntityId = TestEntityId, RealmId = targetRealmId };

        var conn1Id = Guid.NewGuid();
        var conn2Id = Guid.NewGuid();

        _mockDiscoveryStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitDiscoveryModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitDiscoveryModel>
            {
                new TransitDiscoveryModel { EntityId = TestEntityId, ConnectionId = conn1Id, Source = "travel", DiscoveredAt = DateTimeOffset.UtcNow },
                new TransitDiscoveryModel { EntityId = TestEntityId, ConnectionId = conn2Id, Source = "guide", DiscoveredAt = DateTimeOffset.UtcNow }
            });

        // Connection 1 is in target realm
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(conn1Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransitConnectionModel
            {
                Id = conn1Id,
                FromRealmId = targetRealmId,
                ToRealmId = targetRealmId,
                FromLocationId = TestLocationAId,
                ToLocationId = TestLocationBId,
                DistanceKm = 10m,
                TerrainType = "road",
                CompatibleModes = new List<string> { "walking" },
                Status = ConnectionStatus.Open,
                StatusChangedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

        // Connection 2 is in a different realm
        _mockConnectionStore.Setup(s => s.GetAsync(
            TransitService.BuildConnectionKey(conn2Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransitConnectionModel
            {
                Id = conn2Id,
                FromRealmId = Guid.NewGuid(), // Different realm
                ToRealmId = Guid.NewGuid(),
                FromLocationId = TestLocationAId,
                ToLocationId = TestLocationCId,
                DistanceKm = 10m,
                TerrainType = "road",
                CompatibleModes = new List<string> { "walking" },
                Status = ConnectionStatus.Open,
                StatusChangedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (statusCode, response) = await service.ListDiscoveriesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.ConnectionIds);
        Assert.Contains(conn1Id, response.ConnectionIds);
    }

    #endregion

    #region CheckDiscoveriesAsync Tests

    /// <summary>
    /// CheckDiscoveriesAsync should return discovery status for each requested connection.
    /// </summary>
    [Fact]
    public async Task CheckDiscoveriesAsync_ShouldReturnPerConnectionStatus()
    {
        // Arrange
        var service = CreateService();
        var conn1Id = Guid.NewGuid();
        var conn2Id = Guid.NewGuid();
        var request = new CheckDiscoveriesRequest
        {
            EntityId = TestEntityId,
            ConnectionIds = new List<Guid> { conn1Id, conn2Id }
        };

        // Connection 1 has been discovered
        _mockDiscoveryStore.Setup(s => s.GetAsync(
            $"discovery:{TestEntityId}:{conn1Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransitDiscoveryModel
            {
                EntityId = TestEntityId,
                ConnectionId = conn1Id,
                Source = "travel",
                DiscoveredAt = DateTimeOffset.UtcNow.AddDays(-1)
            });

        // Connection 2 has NOT been discovered
        _mockDiscoveryStore.Setup(s => s.GetAsync(
            $"discovery:{TestEntityId}:{conn2Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransitDiscoveryModel?)null);

        // Act
        var (statusCode, response) = await service.CheckDiscoveriesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);

        var result1 = response.Results.First(r => r.ConnectionId == conn1Id);
        Assert.True(result1.Discovered);
        Assert.NotNull(result1.DiscoveredAt);
        Assert.Equal("travel", result1.Source);

        var result2 = response.Results.First(r => r.ConnectionId == conn2Id);
        Assert.False(result2.Discovered);
        Assert.Null(result2.DiscoveredAt);
        Assert.Null(result2.Source);
    }

    #endregion

    #region CleanupByLocationAsync Tests

    /// <summary>
    /// CleanupByLocationAsync should close connections and interrupt archived journeys for deleted location.
    /// </summary>
    [Fact]
    public async Task CleanupByLocationAsync_ShouldCloseConnectionsAndInterruptJourneys()
    {
        // Arrange
        var service = CreateService();
        var deletedLocationId = Guid.NewGuid();
        var request = new CleanupByLocationRequest { LocationId = deletedLocationId };

        var connectionId = Guid.NewGuid();
        var affectedConnection = CreateTestConnectionModel(
            connectionId: connectionId,
            fromLocationId: deletedLocationId,
            status: ConnectionStatus.Open);

        // Step 1: Find connections referencing deleted location
        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { affectedConnection });

        // Step 2: Fresh read for optimistic concurrency
        _mockConnectionStore.Setup(s => s.GetWithETagAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((affectedConnection, "etag-1"));

        _mockConnectionStore.Setup(s => s.TrySaveAsync(
            TransitService.BuildConnectionKey(connectionId), It.IsAny<TransitConnectionModel>(),
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Step 4: No archived journeys
        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel>());

        // Step 5: No Redis journeys
        _mockJourneyIndexStore.Setup(s => s.GetAsync(
            TransitService.JOURNEY_INDEX_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var statusCode = await service.CleanupByLocationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify connection was closed via status-changed event
        var statusEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.connection.status-changed");
        Assert.NotNull(statusEvent.Event);

        // Verify graph cache was invalidated
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    /// <summary>
    /// CleanupByLocationAsync should skip already-closed connections.
    /// </summary>
    [Fact]
    public async Task CleanupByLocationAsync_ShouldSkipAlreadyClosedConnections()
    {
        // Arrange
        var service = CreateService();
        var deletedLocationId = Guid.NewGuid();
        var request = new CleanupByLocationRequest { LocationId = deletedLocationId };

        var closedConnection = CreateTestConnectionModel(
            connectionId: Guid.NewGuid(),
            fromLocationId: deletedLocationId,
            status: ConnectionStatus.Closed);

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { closedConnection });

        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel>());

        _mockJourneyIndexStore.Setup(s => s.GetAsync(
            TransitService.JOURNEY_INDEX_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var statusCode = await service.CleanupByLocationAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Should NOT have called TrySaveAsync on connection store (already closed)
        _mockConnectionStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region CleanupByCharacterAsync Tests

    /// <summary>
    /// CleanupByCharacterAsync should delete discoveries, invalidate cache, and abandon archived journeys.
    /// </summary>
    [Fact]
    public async Task CleanupByCharacterAsync_ShouldDeleteDiscoveriesAndAbandonJourneys()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // Step 1: Discovery records to delete
        var discovery1 = new TransitDiscoveryModel
        {
            EntityId = characterId,
            ConnectionId = Guid.NewGuid(),
            Source = "travel",
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        _mockDiscoveryStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitDiscoveryModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitDiscoveryModel> { discovery1 });

        _mockDiscoveryStore.Setup(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Step 2: Discovery cache delete
        _mockDiscoveryCacheStore.Setup(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Step 3: Active archived journey to abandon
        var journeyId = Guid.NewGuid();
        var activeJourney = CreateTestArchiveModel(status: JourneyStatus.InTransit);
        activeJourney.Id = journeyId;
        activeJourney.EntityId = characterId;

        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel> { activeJourney });

        _mockJourneyArchiveStore.Setup(s => s.GetWithETagAsync(
            TransitService.BuildJourneyArchiveKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((activeJourney, "etag-1"));

        _mockJourneyArchiveStore.Setup(s => s.TrySaveAsync(
            TransitService.BuildJourneyArchiveKey(journeyId), It.IsAny<JourneyArchiveModel>(),
            "etag-1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Step 4: No Redis journeys
        _mockJourneyIndexStore.Setup(s => s.GetAsync(
            TransitService.JOURNEY_INDEX_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var statusCode = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify discovery was deleted
        _mockDiscoveryStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

        // Verify cache was invalidated
        _mockDiscoveryCacheStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

        // Verify journey was abandoned via event
        var abandonedEvent = _capturedEvents.FirstOrDefault(e => e.Topic == "transit.journey.abandoned");
        Assert.NotNull(abandonedEvent.Event);
    }

    /// <summary>
    /// CleanupByCharacterAsync should also scan and abandon active Redis journeys.
    /// </summary>
    [Fact]
    public async Task CleanupByCharacterAsync_ShouldAbandonRedisJourneys()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var request = new CleanupByCharacterRequest { CharacterId = characterId };

        // No discoveries
        _mockDiscoveryStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitDiscoveryModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitDiscoveryModel>());

        _mockDiscoveryCacheStore.Setup(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // No archived journeys
        _mockJourneyArchiveStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<JourneyArchiveModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JourneyArchiveModel>());

        // Redis has an active journey for this character
        var journeyId = Guid.NewGuid();
        _mockJourneyIndexStore.Setup(s => s.GetAsync(
            TransitService.JOURNEY_INDEX_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { journeyId });

        var redisJourney = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.InTransit);
        redisJourney.EntityId = characterId;

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(redisJourney);

        TransitJourneyModel? capturedJourney = null;
        _mockJourneyStore.Setup(s => s.SaveAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<TransitJourneyModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TransitJourneyModel, StateOptions?, CancellationToken>((_, j, _, _) => capturedJourney = j)
            .ReturnsAsync("etag-1");

        // Act
        var statusCode = await service.CleanupByCharacterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedJourney);
        Assert.Equal(JourneyStatus.Abandoned, capturedJourney.Status);
        Assert.Equal("character_deleted", capturedJourney.StatusReason);
    }

    #endregion

    #region TransitVariableProvider Tests

    /// <summary>
    /// TransitVariableProvider.Empty should return default/null values for most paths.
    /// journey.active returns false (not null) because it is always a boolean.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_Empty_ShouldReturnDefaultValuesForAllPaths()
    {
        // Arrange
        var provider = Transit.Providers.TransitVariableProvider.Empty;

        // Act & Assert
        // journey.active is a boolean that returns false when no journey (not null)
        Assert.Equal(false, provider.GetValue(new[] { "journey", "active" }.AsSpan()));
        // journey.mode returns null when no active journey
        Assert.Null(provider.GetValue(new[] { "journey", "mode" }.AsSpan()));
        // mode with unknown code returns null
        Assert.Null(provider.GetValue(new[] { "mode", "walking", "available" }.AsSpan()));
        // discovered_connections returns 0 (empty set count)
        Assert.Equal(0, provider.GetValue(new[] { "discovered_connections" }.AsSpan()));
        // unknown connection code returns null
        Assert.Null(provider.GetValue(new[] { "connection", "test", "discovered" }.AsSpan()));
    }

    /// <summary>
    /// TransitVariableProvider should return journey.active=true when journey exists.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldReturnJourneyActive_WhenJourneyExists()
    {
        // Arrange
        var journey = CreateTestJourneyModel(status: JourneyStatus.InTransit, legCount: 2);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.Legs[1].Status = JourneyLegStatus.InProgress;
        journey.Legs[1].EstimatedDurationGameHours = 3.0m;
        journey.Legs[1].WaypointTransferTimeGameHours = 0.5m;
        journey.CurrentLegIndex = 1;

        var provider = new Transit.Providers.TransitVariableProvider(
            journey,
            "ironforge",
            new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

        // Act & Assert
        Assert.Equal(true, provider.GetValue(new[] { "journey", "active" }.AsSpan()));
        Assert.Equal("walking", provider.GetValue(new[] { "journey", "mode" }.AsSpan()));
        Assert.Equal("ironforge", provider.GetValue(new[] { "journey", "destination_code" }.AsSpan()));
        Assert.Equal(2, provider.GetValue(new[] { "discovered_connections" }.AsSpan()));
    }

    /// <summary>
    /// TransitVariableProvider should compute ETA hours from remaining legs.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldComputeEtaHours()
    {
        // Arrange
        var journey = CreateTestJourneyModel(status: JourneyStatus.InTransit, legCount: 3);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.Legs[0].EstimatedDurationGameHours = 2.0m;
        journey.Legs[1].Status = JourneyLegStatus.InProgress;
        journey.Legs[1].EstimatedDurationGameHours = 3.0m;
        journey.Legs[1].WaypointTransferTimeGameHours = 0.5m;
        journey.Legs[2].Status = JourneyLegStatus.Pending;
        journey.Legs[2].EstimatedDurationGameHours = 1.0m;
        journey.CurrentLegIndex = 1;

        var provider = new Transit.Providers.TransitVariableProvider(
            journey, null,
            new HashSet<Guid>(),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

        // Act
        var etaHours = provider.GetValue(new[] { "journey", "eta_hours" }.AsSpan());

        // Assert: Remaining = leg1 (3.0 + 0.5) + leg2 (1.0) = 4.5
        Assert.Equal(4.5m, etaHours);
    }

    /// <summary>
    /// TransitVariableProvider should compute progress as ratio of completed legs.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldComputeProgress()
    {
        // Arrange
        var journey = CreateTestJourneyModel(status: JourneyStatus.InTransit, legCount: 4);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.Legs[1].Status = JourneyLegStatus.Skipped;
        journey.Legs[2].Status = JourneyLegStatus.InProgress;
        journey.Legs[3].Status = JourneyLegStatus.Pending;
        journey.CurrentLegIndex = 2;

        var provider = new Transit.Providers.TransitVariableProvider(
            journey, null,
            new HashSet<Guid>(),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

        // Act
        var progress = provider.GetValue(new[] { "journey", "progress" }.AsSpan());

        // Assert: 2 completed/skipped out of 4 = 0.5
        Assert.Equal(0.5m, progress);
    }

    /// <summary>
    /// TransitVariableProvider should return mode availability data.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldReturnModeAvailability()
    {
        // Arrange
        var modes = new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["walking"] = new Transit.Providers.TransitModeSnapshot(Available: true, EffectiveSpeed: 5.0m, PreferenceCost: 0.0m),
            ["horseback"] = new Transit.Providers.TransitModeSnapshot(Available: true, EffectiveSpeed: 15.0m, PreferenceCost: 0.5m)
        };

        var provider = new Transit.Providers.TransitVariableProvider(
            null, null,
            new HashSet<Guid>(),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            modes);

        // Act & Assert
        Assert.Equal(true, provider.GetValue(new[] { "mode", "walking", "available" }.AsSpan()));
        Assert.Equal(5.0m, provider.GetValue(new[] { "mode", "walking", "speed" }.AsSpan()));
        Assert.Equal(0.0m, provider.GetValue(new[] { "mode", "walking", "preference_cost" }.AsSpan()));
        Assert.Equal(15.0m, provider.GetValue(new[] { "mode", "horseback", "speed" }.AsSpan()));
        Assert.Null(provider.GetValue(new[] { "mode", "nonexistent", "available" }.AsSpan()));
    }

    /// <summary>
    /// TransitVariableProvider should return connection discovery status by code.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldReturnConnectionDiscoveryStatus()
    {
        // Arrange
        var codes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["ironforge-stormwind"] = true,
            ["darkshore-ashenvale"] = false
        };

        var provider = new Transit.Providers.TransitVariableProvider(
            null, null,
            new HashSet<Guid>(),
            codes,
            new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

        // Act & Assert
        Assert.Equal(true, provider.GetValue(new[] { "connection", "ironforge-stormwind", "discovered" }.AsSpan()));
        Assert.Equal(false, provider.GetValue(new[] { "connection", "darkshore-ashenvale", "discovered" }.AsSpan()));
        Assert.Null(provider.GetValue(new[] { "connection", "nonexistent", "discovered" }.AsSpan()));
    }

    /// <summary>
    /// TransitVariableProvider.CanResolve should correctly identify valid paths.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_CanResolve_ShouldValidatePaths()
    {
        // Arrange
        var provider = Transit.Providers.TransitVariableProvider.Empty;

        // Act & Assert
        Assert.True(provider.CanResolve(new[] { "journey", "active" }.AsSpan()));
        Assert.True(provider.CanResolve(new[] { "journey", "mode" }.AsSpan()));
        Assert.True(provider.CanResolve(new[] { "mode", "walking", "available" }.AsSpan()));
        Assert.True(provider.CanResolve(new[] { "discovered_connections" }.AsSpan()));
        Assert.True(provider.CanResolve(new[] { "connection", "test", "discovered" }.AsSpan()));

        // Invalid paths
        Assert.False(provider.CanResolve(new[] { "invalid" }.AsSpan()));
        Assert.False(provider.CanResolve(new[] { "journey", "invalid_field" }.AsSpan()));
        Assert.False(provider.CanResolve(new[] { "mode", "walking", "invalid_prop" }.AsSpan()));
        Assert.False(provider.CanResolve(new[] { "connection", "test" }.AsSpan())); // Missing "discovered"
    }

    /// <summary>
    /// TransitVariableProvider should return remaining legs count.
    /// </summary>
    [Fact]
    public void TransitVariableProvider_ShouldReturnRemainingLegs()
    {
        // Arrange
        var journey = CreateTestJourneyModel(status: JourneyStatus.InTransit, legCount: 3);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.Legs[1].Status = JourneyLegStatus.InProgress;
        journey.Legs[2].Status = JourneyLegStatus.Pending;

        var provider = new Transit.Providers.TransitVariableProvider(
            journey, null,
            new HashSet<Guid>(),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Transit.Providers.TransitModeSnapshot>(StringComparer.OrdinalIgnoreCase));

        // Act
        var remainingLegs = provider.GetValue(new[] { "journey", "remaining_legs" }.AsSpan());

        // Assert: 2 non-completed/skipped legs
        Assert.Equal(2, remainingLegs);
    }

    #endregion

    #region HandleSeasonChangedAsync Tests

    /// <summary>
    /// HandleSeasonChangedAsync should close connections and open connections based on seasonal availability.
    /// </summary>
    [Fact]
    public async Task HandleSeasonChangedAsync_ShouldUpdateConnectionStatuses()
    {
        // Arrange
        var service = CreateService();
        var evt = new WorldstateSeasonChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = TestRealmId,
            PreviousSeason = "summer",
            CurrentSeason = "winter"
        };

        // Connection that should close in winter
        var connectionToClose = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.Open);
        connectionToClose.SeasonalAvailability = new List<SeasonalAvailabilityModel>
        {
            new SeasonalAvailabilityModel { Season = "winter", Available = false }
        };
        connectionToClose.FromRealmId = TestRealmId;

        // Connection that should open in winter (currently seasonal_closed)
        var connectionToOpen = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.SeasonalClosed);
        connectionToOpen.SeasonalAvailability = new List<SeasonalAvailabilityModel>
        {
            new SeasonalAvailabilityModel { Season = "winter", Available = true }
        };
        connectionToOpen.FromRealmId = TestRealmId;

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { connectionToClose, connectionToOpen });

        // Act
        await service.HandleSeasonChangedAsync(evt);

        // Assert: Both connections should have been saved
        _mockConnectionStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<TransitConnectionModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Assert: Graph cache should be invalidated
        _mockGraphCache.Verify(g => g.InvalidateAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(TestRealmId)),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// ArriveJourneyAsync should handle AtWaypoint status (not just InTransit).
    /// </summary>
    [Fact]
    public async Task ArriveJourneyAsync_ShouldSucceed_WhenAtWaypoint()
    {
        // Arrange
        var service = CreateService();
        var journeyId = Guid.NewGuid();
        var request = new ArriveJourneyRequest
        {
            JourneyId = journeyId,
            ArrivedAtGameTime = 150.0m,
            Reason = "fast_travel"
        };

        var journey = CreateTestJourneyModel(journeyId: journeyId, status: JourneyStatus.AtWaypoint, legCount: 2);
        journey.Legs[0].Status = JourneyLegStatus.Completed;
        journey.CurrentLegIndex = 1;

        _mockJourneyStore.Setup(s => s.GetAsync(
            TransitService.BuildJourneyKey(journeyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(journey);

        _mockLocationClient.Setup(c => c.GetLocationAsync(
            It.IsAny<Location.GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Location.LocationResponse { RealmId = TestRealmId });

        // Act
        var (statusCode, response) = await service.ArriveJourneyAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(JourneyStatus.Arrived, response.Journey.Status);
    }

    /// <summary>
    /// QueryConnectionsAsync should filter by status correctly.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldFilterByStatus()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryConnectionsRequest
        {
            Status = ConnectionStatus.Blocked,
            Page = 1,
            PageSize = 20,
            IncludeSeasonalClosed = true
        };

        var openConn = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.Open);
        var blockedConn = CreateTestConnectionModel(connectionId: Guid.NewGuid(), status: ConnectionStatus.Blocked);

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { openConn, blockedConn });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Connections);
        Assert.Equal(1, response.TotalCount);
    }

    /// <summary>
    /// QueryConnectionsAsync should filter by mode code compatibility.
    /// </summary>
    [Fact]
    public async Task QueryConnectionsAsync_ShouldFilterByModeCode()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryConnectionsRequest
        {
            ModeCode = "boat",
            Page = 1,
            PageSize = 20,
            IncludeSeasonalClosed = true
        };

        var landConn = CreateTestConnectionModel(connectionId: Guid.NewGuid());
        landConn.CompatibleModes = new List<string> { "walking", "horseback" };

        var waterConn = CreateTestConnectionModel(connectionId: Guid.NewGuid());
        waterConn.CompatibleModes = new List<string> { "boat", "swimming" };

        _mockConnectionStore.Setup(s => s.QueryAsync(
            It.IsAny<Expression<Func<TransitConnectionModel, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransitConnectionModel> { landConn, waterConn });

        // Act
        var (statusCode, response) = await service.QueryConnectionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Connections);
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Creates a minimal JourneyArchiveModel for test setup.
    /// </summary>
    private static JourneyArchiveModel CreateTestArchiveModel(
        JourneyStatus status = JourneyStatus.Arrived,
        Guid? journeyId = null)
    {
        return new JourneyArchiveModel
        {
            Id = journeyId ?? Guid.NewGuid(),
            EntityId = TestEntityId,
            EntityType = "character",
            Legs = new List<TransitJourneyLegModel>(),
            CurrentLegIndex = 0,
            PrimaryModeCode = "walking",
            EffectiveSpeedKmPerGameHour = 5.0m,
            PlannedDepartureGameTime = 100.0m,
            OriginLocationId = TestLocationAId,
            DestinationLocationId = TestLocationBId,
            CurrentLocationId = TestLocationBId,
            Status = status,
            Interruptions = new List<TransitInterruptionModel>(),
            PartySize = 1,
            CargoWeightKg = 0m,
            RealmId = TestRealmId,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            ArchivedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
