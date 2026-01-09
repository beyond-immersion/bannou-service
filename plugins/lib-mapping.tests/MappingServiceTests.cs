using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mapping;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Mapping.Tests;

/// <summary>
/// Unit tests for MappingService.
/// Tests authority management, publishing, queries, and affordance scoring.
/// </summary>
public class MappingServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IMessageSubscriber> _mockMessageSubscriber;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<MappingService>> _mockLogger;
    private readonly MappingServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IAssetClient> _mockAssetClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;

    // Typed state stores for specific record types
    private readonly Mock<IStateStore<MappingService.ChannelRecord>> _mockChannelStore;
    private readonly Mock<IStateStore<MappingService.AuthorityRecord>> _mockAuthorityStore;
    private readonly Mock<IStateStore<MappingService.CheckoutRecord>> _mockCheckoutStore;
    private readonly Mock<IStateStore<MapObject>> _mockObjectStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockIndexStore;
    private readonly Mock<IStateStore<MappingService.LongWrapper>> _mockVersionStore;
    private readonly Mock<IStateStore<MappingService.CachedAffordanceResult>> _mockAffordanceCacheStore;
    private readonly Mock<IStateStore<MappingService.DefinitionRecord>> _mockDefinitionStore;
    private readonly Mock<IStateStore<MappingService.DefinitionIndexEntry>> _mockDefinitionIndexStore;

    public MappingServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockMessageSubscriber = new Mock<IMessageSubscriber>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<MappingService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockAssetClient = new Mock<IAssetClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();

        _configuration = new MappingServiceConfiguration
        {
            Enabled = true,
            AuthorityTimeoutSeconds = 60,
            AuthorityGracePeriodSeconds = 30,
            AuthorityHeartbeatIntervalSeconds = 30,
            DefaultSpatialCellSize = 64.0,
            MaxObjectsPerQuery = 5000,
            MaxPayloadsPerPublish = 100,
            AffordanceCacheTimeoutSeconds = 60,
            MaxAffordanceCandidates = 1000,
            InlinePayloadMaxBytes = 65536,
            MaxCheckoutDurationSeconds = 1800,
            DefaultLayerCacheTtlSeconds = 3600
        };

        // Create typed store mocks
        _mockChannelStore = new Mock<IStateStore<MappingService.ChannelRecord>>();
        _mockAuthorityStore = new Mock<IStateStore<MappingService.AuthorityRecord>>();
        _mockCheckoutStore = new Mock<IStateStore<MappingService.CheckoutRecord>>();
        _mockObjectStore = new Mock<IStateStore<MapObject>>();
        _mockIndexStore = new Mock<IStateStore<List<Guid>>>();
        _mockVersionStore = new Mock<IStateStore<MappingService.LongWrapper>>();
        _mockAffordanceCacheStore = new Mock<IStateStore<MappingService.CachedAffordanceResult>>();
        _mockDefinitionStore = new Mock<IStateStore<MappingService.DefinitionRecord>>();
        _mockDefinitionIndexStore = new Mock<IStateStore<MappingService.DefinitionIndexEntry>>();

        // Wire up state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.ChannelRecord>(It.IsAny<string>()))
            .Returns(_mockChannelStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.AuthorityRecord>(It.IsAny<string>()))
            .Returns(_mockAuthorityStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.CheckoutRecord>(It.IsAny<string>()))
            .Returns(_mockCheckoutStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MapObject>(It.IsAny<string>()))
            .Returns(_mockObjectStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(It.IsAny<string>()))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.LongWrapper>(It.IsAny<string>()))
            .Returns(_mockVersionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.CachedAffordanceResult>(It.IsAny<string>()))
            .Returns(_mockAffordanceCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.DefinitionRecord>(It.IsAny<string>()))
            .Returns(_mockDefinitionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MappingService.DefinitionIndexEntry>(It.IsAny<string>()))
            .Returns(_mockDefinitionIndexStore.Object);

        // Default behaviors
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockMessageSubscriber.Setup(m => m.SubscribeDynamicAsync<MapIngestEvent>(
                It.IsAny<string>(),
                It.IsAny<Func<MapIngestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        // Default store behaviors
        _mockChannelStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MappingService.ChannelRecord>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockAuthorityStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MappingService.AuthorityRecord>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockVersionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MappingService.LongWrapper>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockObjectStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MapObject>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockDefinitionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MappingService.DefinitionRecord>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockDefinitionIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<MappingService.DefinitionIndexEntry>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
    }

    private MappingService CreateService()
    {
        return new MappingService(
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockAssetClient.Object,
            _mockHttpClientFactory.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    /// </summary>
    [Fact]
    public void MappingService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<MappingService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void MappingServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new MappingServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void MappingServiceConfiguration_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var config = new MappingServiceConfiguration();

        // Assert - verify defaults from schema
        Assert.True(config.Enabled);
        Assert.Equal(60, config.AuthorityTimeoutSeconds);
        Assert.Equal(30, config.AuthorityGracePeriodSeconds);
        Assert.Equal(30, config.AuthorityHeartbeatIntervalSeconds);
        Assert.Equal(64.0, config.DefaultSpatialCellSize);
        Assert.Equal(5000, config.MaxObjectsPerQuery);
        Assert.Equal(100, config.MaxPayloadsPerPublish);
        Assert.Equal(60, config.AffordanceCacheTimeoutSeconds);
        Assert.Equal(1000, config.MaxAffordanceCandidates);
        Assert.Equal(65536, config.InlinePayloadMaxBytes);
        Assert.Equal(1800, config.MaxCheckoutDurationSeconds);
        Assert.Equal(3600, config.DefaultLayerCacheTtlSeconds);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void MappingPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = MappingPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.Equal(18, endpoints.Count); // 18 endpoints defined in mapping-api.yaml
    }

    [Fact]
    public void MappingPermissionRegistration_ServiceId_ShouldBeMapping()
    {
        // Assert
        Assert.Equal("mapping", MappingPermissionRegistration.ServiceId);
    }

    [Fact]
    public void MappingPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var registrationEvent = MappingPermissionRegistration.CreateRegistrationEvent(instanceId);

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("mapping", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.Equal(18, registrationEvent.Endpoints.Count);
        Assert.NotEmpty(registrationEvent.Version);
    }

    #endregion

    #region Channel Creation Tests

    [Fact]
    public async Task CreateChannelAsync_WithValidRequest_ShouldReturnAuthorityGrant()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();
        var request = new CreateChannelRequest
        {
            RegionId = regionId,
            Kind = MapKind.Terrain,
            NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
        };

        // Act
        var (status, response) = await service.CreateChannelAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.ChannelId);
        Assert.NotEmpty(response.AuthorityToken);
        Assert.StartsWith("map.ingest.", response.IngestTopic);
        Assert.Equal(regionId, response.RegionId);
        Assert.Equal(MapKind.Terrain, response.Kind);
        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateChannelAsync_WithExistingActiveAuthority_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();
        var request = new CreateChannelRequest
        {
            RegionId = regionId,
            Kind = MapKind.Terrain,
            NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
        };

        // Setup existing channel
        _mockChannelStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.ChannelRecord
            {
                ChannelId = Guid.NewGuid(),
                RegionId = regionId,
                Kind = MapKind.Terrain,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup existing active authority
        _mockAuthorityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.AuthorityRecord
            {
                ChannelId = Guid.NewGuid(),
                AuthorityToken = "existing-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), // Not expired
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.CreateChannelAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateChannelAsync_ShouldSaveChannelAndAuthorityRecords()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();
        var request = new CreateChannelRequest
        {
            RegionId = regionId,
            Kind = MapKind.Static_geometry,
            NonAuthorityHandling = NonAuthorityHandlingMode.Accept_and_alert
        };

        // Act
        var (status, _) = await service.CreateChannelAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockChannelStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("map:channel:")),
            It.IsAny<MappingService.ChannelRecord>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockAuthorityStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("map:authority:")),
            It.IsAny<MappingService.AuthorityRecord>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChannelAsync_ShouldPublishChannelCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateChannelRequest
        {
            RegionId = Guid.NewGuid(),
            Kind = MapKind.Navigation,
            NonAuthorityHandling = NonAuthorityHandlingMode.Reject_silent
        };

        // Act
        await service.CreateChannelAsync(request, CancellationToken.None);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync<MappingChannelCreatedEvent>(
            "mapping.channel.created",
            It.IsAny<MappingChannelCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateChannelAsync_ShouldSubscribeToIngestTopic()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateChannelRequest
        {
            RegionId = Guid.NewGuid(),
            Kind = MapKind.Resources,
            NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
        };

        // Act
        await service.CreateChannelAsync(request, CancellationToken.None);

        // Assert
        _mockMessageSubscriber.Verify(m => m.SubscribeDynamicAsync<MapIngestEvent>(
            It.Is<string>(t => t.StartsWith("map.ingest.")),
            It.IsAny<Func<MapIngestEvent, CancellationToken, Task>>(),
            It.IsAny<string?>(),
            It.IsAny<SubscriptionExchangeType>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Authority Release Tests

    // NOTE: Complex multi-step tests with stateful mock callbacks have been moved to
    // HTTP integration tests (http-tester/Tests/MappingTestHandler.cs) where real
    // state management provides more reliable test behavior.

    [Fact]
    public async Task ReleaseAuthorityAsync_WithInvalidToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = CreateService();
        var request = new ReleaseAuthorityRequest
        {
            ChannelId = Guid.NewGuid(),
            AuthorityToken = "invalid-token"
        };

        // Act
        var (status, response) = await service.ReleaseAuthorityAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.NotNull(response);
        Assert.False(response.Released);
    }

    #endregion

    #region Authority Heartbeat Tests

    // NOTE: AuthorityHeartbeatAsync tests with valid tokens and expiration scenarios
    // are covered by HTTP integration tests (http-tester/Tests/MappingTestHandler.cs)
    // where real state management provides more reliable test behavior.

    #endregion

    #region Publishing Tests

    // NOTE: PublishMapUpdateAsync with valid authority is covered by HTTP integration tests
    // (http-tester/Tests/MappingTestHandler.cs) where real state management provides
    // more reliable test behavior for multi-step flows.

    [Fact]
    public async Task PublishMapUpdateAsync_WithInvalidAuthority_ShouldReject()
    {
        // Arrange
        var service = CreateService();

        // Setup channel without authority
        _mockChannelStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.ChannelRecord
            {
                ChannelId = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                Kind = MapKind.Terrain,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_silent,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var publishRequest = new PublishMapUpdateRequest
        {
            ChannelId = Guid.NewGuid(),
            AuthorityToken = "bad-token",
            Payload = new MapPayload { ObjectType = "rock" },
            DeltaType = DeltaType.Delta
        };

        // Act
        var (status, response) = await service.PublishMapUpdateAsync(publishRequest, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.NotNull(response);
        Assert.False(response.Accepted);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryPointAsync_ShouldReturnObjectsNearPosition()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        // Setup spatial index to return our object
        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { objectId });

        // Setup object store
        _mockObjectStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MapObject
            {
                ObjectId = objectId,
                RegionId = regionId,
                Kind = MapKind.Points_of_interest,
                ObjectType = "landmark",
                Position = new Position3D { X = 100, Y = 0, Z = 100 },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var request = new QueryPointRequest
        {
            RegionId = regionId,
            Position = new Position3D { X = 100, Y = 0, Z = 100 },
            Radius = 10
        };

        // Act
        var (status, response) = await service.QueryPointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Objects);
        Assert.Equal(100, response.Position.X);
    }

    [Fact]
    public async Task QueryBoundsAsync_ShouldReturnObjectsInBounds()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        var request = new QueryBoundsRequest
        {
            RegionId = regionId,
            Bounds = new Bounds
            {
                Min = new Position3D { X = 0, Y = 0, Z = 0 },
                Max = new Position3D { X = 100, Y = 100, Z = 100 }
            },
            MaxObjects = 100
        };

        // Act
        var (status, response) = await service.QueryBoundsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Objects);
        Assert.NotNull(response.Bounds);
    }

    [Fact]
    public async Task QueryObjectsByTypeAsync_ShouldReturnObjectsOfType()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        // Setup type index
        _mockIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("map:type-index:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { objectId });

        // Setup object store
        _mockObjectStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MapObject
            {
                ObjectId = objectId,
                RegionId = regionId,
                Kind = MapKind.Resources,
                ObjectType = "gold_vein",
                Position = new Position3D { X = 50, Y = -10, Z = 50 },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var request = new QueryObjectsByTypeRequest
        {
            RegionId = regionId,
            ObjectType = "gold_vein",
            MaxObjects = 100
        };

        // Act
        var (status, response) = await service.QueryObjectsByTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("gold_vein", response.ObjectType);
    }

    #endregion

    #region Affordance Query Tests

    [Fact]
    public async Task QueryAffordanceAsync_ShouldReturnScoredLocations()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        var request = new AffordanceQueryRequest
        {
            RegionId = regionId,
            AffordanceType = AffordanceType.Shelter,
            Bounds = new Bounds
            {
                Min = new Position3D { X = 0, Y = 0, Z = 0 },
                Max = new Position3D { X = 500, Y = 100, Z = 500 }
            },
            MaxResults = 10,
            MinScore = 0.1,
            Freshness = AffordanceFreshness.Fresh
        };

        // Act
        var (status, response) = await service.QueryAffordanceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Locations);
        Assert.NotNull(response.QueryMetadata);
        Assert.False(response.QueryMetadata.CacheHit);
    }

    [Fact]
    public async Task QueryAffordanceAsync_WithCachedFreshness_ShouldUseCacheWhenAvailable()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        // Setup cache hit
        _mockAffordanceCacheStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.CachedAffordanceResult
            {
                Response = new AffordanceQueryResponse
                {
                    Locations = new List<AffordanceLocation>
                    {
                        new AffordanceLocation
                        {
                            Position = new Position3D { X = 100, Y = 0, Z = 100 },
                            Score = 0.8,
                            ObjectIds = new List<Guid> { Guid.NewGuid() }
                        }
                    }
                },
                CachedAt = DateTimeOffset.UtcNow // Fresh cache
            });

        var request = new AffordanceQueryRequest
        {
            RegionId = regionId,
            AffordanceType = AffordanceType.Ambush,
            Freshness = AffordanceFreshness.Cached,
            MaxResults = 10,
            MinScore = 0.1
        };

        // Act
        var (status, response) = await service.QueryAffordanceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.QueryMetadata?.CacheHit);
    }

    #endregion

    #region Authoring Tests

    [Fact]
    public async Task CheckoutForAuthoringAsync_WhenNotLocked_ShouldGrantCheckout()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        var request = new AuthoringCheckoutRequest
        {
            RegionId = regionId,
            Kind = MapKind.Terrain,
            EditorId = "editor-123"
        };

        // Act
        var (status, response) = await service.CheckoutForAuthoringAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotEmpty(response.AuthorityToken ?? "");
        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CheckoutForAuthoringAsync_WhenAlreadyLocked_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        // Setup existing checkout
        _mockCheckoutStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.CheckoutRecord
            {
                RegionId = regionId,
                Kind = MapKind.Terrain,
                EditorId = "other-editor",
                AuthorityToken = "other-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), // Not expired
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new AuthoringCheckoutRequest
        {
            RegionId = regionId,
            Kind = MapKind.Terrain,
            EditorId = "editor-123"
        };

        // Act
        var (status, response) = await service.CheckoutForAuthoringAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("other-editor", response.LockedBy);
    }

    [Fact]
    public async Task CommitAuthoringAsync_WithValidToken_ShouldCommitAndReleaseLock()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        // First checkout
        var checkoutRequest = new AuthoringCheckoutRequest
        {
            RegionId = regionId,
            Kind = MapKind.Static_geometry,
            EditorId = "editor-123"
        };
        var (_, checkoutResponse) = await service.CheckoutForAuthoringAsync(checkoutRequest, CancellationToken.None);
        Assert.NotNull(checkoutResponse);
        Assert.True(checkoutResponse.Success);

        // Setup checkout record for commit
        _mockCheckoutStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.CheckoutRecord
            {
                RegionId = regionId,
                Kind = MapKind.Static_geometry,
                EditorId = "editor-123",
                AuthorityToken = checkoutResponse.AuthorityToken ?? "",
                ExpiresAt = checkoutResponse.ExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow
            });

        var commitRequest = new AuthoringCommitRequest
        {
            RegionId = regionId,
            Kind = MapKind.Static_geometry,
            AuthorityToken = checkoutResponse.AuthorityToken ?? ""
        };

        // Act
        var (status, response) = await service.CommitAuthoringAsync(commitRequest, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.Version > 0);
    }

    [Fact]
    public async Task ReleaseAuthoringAsync_WithValidToken_ShouldReleaseLock()
    {
        // Arrange
        var service = CreateService();
        var regionId = Guid.NewGuid();

        // First checkout
        var checkoutRequest = new AuthoringCheckoutRequest
        {
            RegionId = regionId,
            Kind = MapKind.Navigation,
            EditorId = "editor-123"
        };
        var (_, checkoutResponse) = await service.CheckoutForAuthoringAsync(checkoutRequest, CancellationToken.None);
        Assert.NotNull(checkoutResponse);

        // Setup checkout record for release
        _mockCheckoutStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.CheckoutRecord
            {
                RegionId = regionId,
                Kind = MapKind.Navigation,
                EditorId = "editor-123",
                AuthorityToken = checkoutResponse.AuthorityToken ?? "",
                ExpiresAt = checkoutResponse.ExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow
            });

        var releaseRequest = new AuthoringReleaseRequest
        {
            RegionId = regionId,
            Kind = MapKind.Navigation,
            AuthorityToken = checkoutResponse.AuthorityToken ?? ""
        };

        // Act
        var (status, response) = await service.ReleaseAuthoringAsync(releaseRequest, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Released);
    }

    #endregion

    #region Ingest Event Tests

    [Fact]
    public async Task HandleIngestEventAsync_WithValidToken_ShouldProcessPayloads()
    {
        // Arrange
        var service = CreateService();

        // First create a channel
        var createRequest = new CreateChannelRequest
        {
            RegionId = Guid.NewGuid(),
            Kind = MapKind.Dynamic_objects,
            NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
        };
        var (_, createResponse) = await service.CreateChannelAsync(createRequest, CancellationToken.None);
        Assert.NotNull(createResponse);

        // Setup authority record
        _mockAuthorityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.AuthorityRecord
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken,
                ExpiresAt = createResponse.ExpiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Setup channel record
        _mockChannelStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.ChannelRecord
            {
                ChannelId = createResponse.ChannelId,
                RegionId = createResponse.RegionId,
                Kind = createResponse.Kind,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var ingestEvent = new MapIngestEvent
        {
            AuthorityToken = createResponse.AuthorityToken,
            Timestamp = DateTimeOffset.UtcNow,
            Payloads = new List<IngestPayload>
            {
                new IngestPayload
                {
                    ObjectType = "enemy_spawn",
                    Position = new EventPosition3D { X = 200, Y = 0, Z = 200 }
                }
            }
        };

        // Act - call internal handler directly
        await service.HandleIngestEventAsync(createResponse.ChannelId, ingestEvent, CancellationToken.None);

        // Assert - verify object was saved
        _mockObjectStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<MapObject>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region Definition CRUD Tests

    [Fact]
    public async Task CreateDefinitionAsync_WithValidRequest_ShouldReturnDefinition()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateDefinitionRequest
        {
            Name = "Test Map",
            Description = "A test map definition",
            Layers = new List<LayerDefinition>
            {
                new LayerDefinition { Kind = MapKind.Terrain }
            }
        };

        // Act
        var (status, response) = await service.CreateDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Test Map", response.Name);
        Assert.Equal("A test map definition", response.Description);
        Assert.NotEqual(Guid.Empty, response.DefinitionId);
    }

    [Fact]
    public async Task CreateDefinitionAsync_WithDuplicateName_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();

        // Setup existing definition with same name
        _mockDefinitionIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionIndexEntry { DefinitionIds = new List<Guid> { existingId } });

        _mockDefinitionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord
            {
                DefinitionId = existingId,
                Name = "Test Map",
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new CreateDefinitionRequest
        {
            Name = "Test Map", // Same name as existing
            Description = "Another map"
        };

        // Act
        var (status, response) = await service.CreateDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetDefinitionAsync_WhenExists_ShouldReturnDefinition()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord
            {
                DefinitionId = definitionId,
                Name = "My Map",
                Description = "A great map",
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new GetDefinitionRequest { DefinitionId = definitionId };

        // Act
        var (status, response) = await service.GetDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(definitionId, response.DefinitionId);
        Assert.Equal("My Map", response.Name);
    }

    [Fact]
    public async Task GetDefinitionAsync_WhenNotExists_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetDefinitionRequest { DefinitionId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.GetDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListDefinitionsAsync_ShouldReturnPaginatedList()
    {
        // Arrange
        var service = CreateService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockDefinitionIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionIndexEntry { DefinitionIds = new List<Guid> { id1, id2 } });

        _mockDefinitionStore.SetupSequence(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord { DefinitionId = id1, Name = "Alpha Map", CreatedAt = DateTimeOffset.UtcNow })
            .ReturnsAsync(new MappingService.DefinitionRecord { DefinitionId = id2, Name = "Beta Map", CreatedAt = DateTimeOffset.UtcNow });

        var request = new ListDefinitionsRequest { Offset = 0, Limit = 10 };

        // Act
        var (status, response) = await service.ListDefinitionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Total);
        Assert.Equal(2, response.Definitions.Count);
    }

    [Fact]
    public async Task ListDefinitionsAsync_WithNameFilter_ShouldFilterResults()
    {
        // Arrange
        var service = CreateService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _mockDefinitionIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionIndexEntry { DefinitionIds = new List<Guid> { id1, id2 } });

        _mockDefinitionStore.SetupSequence(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord { DefinitionId = id1, Name = "Forest Map", CreatedAt = DateTimeOffset.UtcNow })
            .ReturnsAsync(new MappingService.DefinitionRecord { DefinitionId = id2, Name = "Desert Map", CreatedAt = DateTimeOffset.UtcNow });

        var request = new ListDefinitionsRequest { NameFilter = "Forest", Offset = 0, Limit = 10 };

        // Act
        var (status, response) = await service.ListDefinitionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Total);
        Assert.Single(response.Definitions);
        Assert.Equal("Forest Map", response.Definitions.First().Name);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_WhenExists_ShouldUpdateAndReturn()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord
            {
                DefinitionId = definitionId,
                Name = "Old Name",
                Description = "Old description",
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new UpdateDefinitionRequest
        {
            DefinitionId = definitionId,
            Name = "New Name",
            Description = "New description"
        };

        // Act
        var (status, response) = await service.UpdateDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("New Name", response.Name);
        Assert.Equal("New description", response.Description);
        Assert.NotNull(response.UpdatedAt);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_WhenNotExists_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateDefinitionRequest
        {
            DefinitionId = Guid.NewGuid(),
            Name = "New Name"
        };

        // Act
        var (status, response) = await service.UpdateDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_WhenExists_ShouldDeleteAndReturnSuccess()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionRecord
            {
                DefinitionId = definitionId,
                Name = "To Delete",
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockDefinitionIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingService.DefinitionIndexEntry { DefinitionIds = new List<Guid> { definitionId } });

        var request = new DeleteDefinitionRequest { DefinitionId = definitionId };

        // Act
        var (status, response) = await service.DeleteDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Deleted);

        // Verify delete was called
        _mockDefinitionStore.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_WhenNotExists_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteDefinitionRequest { DefinitionId = Guid.NewGuid() };

        // Act
        var (status, response) = await service.DeleteDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.NotNull(response);
        Assert.False(response.Deleted);
    }

    #endregion
}
