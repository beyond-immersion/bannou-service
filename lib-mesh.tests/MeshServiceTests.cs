using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Yarp.ReverseProxy.Forwarder;

namespace BeyondImmersion.BannouService.Mesh.Tests;

/// <summary>
/// Collection definition for tests that require sequential execution due to static state.
/// </summary>
[CollectionDefinition("MeshCacheTests", DisableParallelization = true)]
public class MeshCacheTestsCollection { }

/// <summary>
/// Tests for MeshService.
/// Uses interface-based mocking for all dependencies.
/// Note: Tests that interact with static cache use the MeshCacheTests collection
/// to avoid parallel execution interference.
/// </summary>
[Collection("MeshCacheTests")]
public class MeshServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<MeshService>> _mockLogger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IMeshRedisManager> _mockRedisManager;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public MeshServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MeshService>>();
        _configuration = new MeshServiceConfiguration();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockRedisManager = new Mock<IMeshRedisManager>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    private MeshService CreateService()
    {
        return new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockRedisManager.Object,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            null!,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockRedisManager.Object,
            _mockEventConsumer.Object));

        Assert.Equal("messageBus", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            _mockMessageBus.Object,
            null!,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockRedisManager.Object,
            _mockEventConsumer.Object));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockRedisManager.Object,
            _mockEventConsumer.Object));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            null!,
            _mockRedisManager.Object,
            _mockEventConsumer.Object));

        Assert.Equal("errorEventEmitter", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullRedisManager_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            null!,
            _mockEventConsumer.Object));

        Assert.Equal("redisManager", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockRedisManager.Object,
            null!));

        Assert.Equal("eventConsumer", exception.ParamName);
    }

    #endregion

    #region GetEndpointsAsync Tests

    [Fact]
    public async Task GetEndpointsAsync_WithHealthyEndpoints_ShouldReturnOK()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy }
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetEndpointsRequest { AppId = "bannou" };

        // Act
        var (statusCode, response) = await service.GetEndpointsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.AppId);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.HealthyCount);
    }

    [Fact]
    public async Task GetEndpointsAsync_WithServiceNameFilter_ShouldFilterEndpoints()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "auth", "accounts" } },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "connect" } }
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetEndpointsRequest { AppId = "bannou", ServiceName = "auth" };

        // Act
        var (statusCode, response) = await service.GetEndpointsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Endpoints);
        Assert.Contains("auth", response.Endpoints.First().Services);
    }

    [Fact]
    public async Task GetEndpointsAsync_WhenRedisThrows_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        _mockErrorEventEmitter
            .Setup(x => x.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new GetEndpointsRequest { AppId = "bannou" };

        // Act
        var (statusCode, response) = await service.GetEndpointsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ListEndpointsAsync Tests

    [Fact]
    public async Task ListEndpointsAsync_ShouldReturnAllEndpointsWithSummary()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Degraded },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-db", Status = EndpointStatus.Unavailable }
        };

        _mockRedisManager
            .Setup(x => x.GetAllEndpointsAsync(null))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new ListEndpointsRequest();

        // Act
        var (statusCode, response) = await service.ListEndpointsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.Endpoints.Count);
        Assert.Equal(3, response.Summary.TotalEndpoints);
        Assert.Equal(1, response.Summary.HealthyCount);
        Assert.Equal(1, response.Summary.DegradedCount);
        Assert.Equal(1, response.Summary.UnavailableCount);
    }

    [Fact]
    public async Task ListEndpointsAsync_WithPrefixFilter_ShouldFilterByPrefix()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Healthy }
        };

        _mockRedisManager
            .Setup(x => x.GetAllEndpointsAsync("bannou-auth"))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new ListEndpointsRequest { AppIdFilter = "bannou-auth" };

        // Act
        var (statusCode, response) = await service.ListEndpointsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Endpoints);
    }

    #endregion

    #region RegisterEndpointAsync Tests

    [Fact]
    public async Task RegisterEndpointAsync_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.RegisterEndpointAsync(It.IsAny<MeshEndpoint>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var service = CreateService();
        var request = new RegisterEndpointRequest
        {
            AppId = "bannou",
            Host = "localhost",
            Port = 3500,
            Services = new List<string> { "auth", "accounts" }
        };

        // Act
        var (statusCode, response) = await service.RegisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Endpoint);
        Assert.Equal("bannou", response.Endpoint.AppId);
        Assert.Equal("localhost", response.Endpoint.Host);
        Assert.Equal(3500, response.Endpoint.Port);
    }

    [Fact]
    public async Task RegisterEndpointAsync_WhenRegistrationFails_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.RegisterEndpointAsync(It.IsAny<MeshEndpoint>(), It.IsAny<int>()))
            .ReturnsAsync(false);

        var service = CreateService();
        var request = new RegisterEndpointRequest
        {
            AppId = "bannou",
            Host = "localhost",
            Port = 3500
        };

        // Act
        var (statusCode, response) = await service.RegisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterEndpointAsync_WithCustomInstanceId_ShouldUseProvidedId()
    {
        // Arrange
        var expectedInstanceId = Guid.NewGuid();
        MeshEndpoint? capturedEndpoint = null;

        _mockRedisManager
            .Setup(x => x.RegisterEndpointAsync(It.IsAny<MeshEndpoint>(), It.IsAny<int>()))
            .Callback<MeshEndpoint, int>((endpoint, _) => capturedEndpoint = endpoint)
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var service = CreateService();
        var request = new RegisterEndpointRequest
        {
            AppId = "bannou",
            Host = "localhost",
            Port = 3500,
            InstanceId = expectedInstanceId
        };

        // Act
        await service.RegisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedEndpoint);
        Assert.Equal(expectedInstanceId, capturedEndpoint.InstanceId);
    }

    #endregion

    #region DeregisterEndpointAsync Tests

    [Fact]
    public async Task DeregisterEndpointAsync_WhenEndpointExists_ShouldReturnNoContent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "bannou",
            Status = EndpointStatus.Healthy
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockRedisManager
            .Setup(x => x.DeregisterEndpointAsync(instanceId, "bannou"))
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var service = CreateService();
        var request = new DeregisterEndpointRequest
        {
            InstanceId = instanceId
        };

        // Act
        var (statusCode, response) = await service.DeregisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NoContent, statusCode);
    }

    [Fact]
    public async Task DeregisterEndpointAsync_WhenEndpointNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        _mockRedisManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService();
        var request = new DeregisterEndpointRequest
        {
            InstanceId = instanceId
        };

        // Act
        var (statusCode, response) = await service.DeregisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region HeartbeatAsync Tests

    [Fact]
    public async Task HeartbeatAsync_WithValidRequest_ShouldReturnOKWithNextHeartbeat()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "bannou",
            Status = EndpointStatus.Healthy
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockRedisManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Healthy, 50, 100, 90))
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new HeartbeatRequest
        {
            InstanceId = instanceId,
            Status = EndpointStatus.Healthy,
            LoadPercent = 50,
            CurrentConnections = 100
        };

        // Act
        var (statusCode, response) = await service.HeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(30, response.NextHeartbeatSeconds); // 90 / 3 = 30
    }

    [Fact]
    public async Task HeartbeatAsync_WhenEndpointNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        _mockRedisManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService();
        var request = new HeartbeatRequest
        {
            InstanceId = instanceId,
            Status = EndpointStatus.Healthy
        };

        // Act
        var (statusCode, response) = await service.HeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task HeartbeatAsync_ShouldUseDefaultTtlOf90Seconds()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "bannou",
            Status = EndpointStatus.Healthy
        };
        int? capturedTtl = null;

        _mockRedisManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockRedisManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Healthy,
                It.IsAny<float>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<Guid, string, EndpointStatus, float, int, int>(
                (_, _, _, _, _, ttl) => capturedTtl = ttl)
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new HeartbeatRequest
        {
            InstanceId = instanceId,
            Status = EndpointStatus.Healthy
        };

        // Act
        await service.HeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(90, capturedTtl);
    }

    #endregion

    #region GetRouteAsync Tests

    [Fact]
    public async Task GetRouteAsync_WithHealthyEndpoints_ShouldReturnEndpoint()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Host = "host1", Port = 3500, Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Host = "host2", Port = 3500, Status = EndpointStatus.Healthy }
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetRouteRequest { AppId = "bannou" };

        // Act
        var (statusCode, response) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Endpoint);
        Assert.Single(response.Alternates); // One alternate since we have 2 endpoints
    }

    [Fact]
    public async Task GetRouteAsync_WithNoEndpoints_ShouldReturnNotFound()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(new List<MeshEndpoint>());

        var service = CreateService();
        var request = new GetRouteRequest { AppId = "bannou" };

        // Act
        var (statusCode, response) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetRouteAsync_WithServiceFilter_ShouldFilterByService()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "auth" } },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "connect" } }
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetRouteRequest { AppId = "bannou", ServiceName = "auth" };

        // Act
        var (statusCode, response) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Contains("auth", response.Endpoint.Services);
    }

    [Fact]
    public async Task GetRouteAsync_WithLeastConnectionsAlgorithm_ShouldSelectLeastLoaded()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, CurrentConnections = 100, Host = "loaded" },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, CurrentConnections = 10, Host = "light" }
        };

        _mockRedisManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetRouteRequest
        {
            AppId = "bannou",
            Algorithm = LoadBalancerAlgorithm.LeastConnections
        };

        // Act
        var (statusCode, response) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("light", response.Endpoint.Host); // Should select endpoint with fewer connections
    }

    #endregion

    #region GetMappingsAsync Tests

    [Fact]
    public async Task GetMappingsAsync_WhenCacheEmpty_ShouldFetchFromRedisAndReturnMappings()
    {
        // Arrange - Reset cache to ensure clean state for this test
        MeshService.ResetCacheForTesting();

        var mappings = new Dictionary<string, string> { ["auth"] = "bannou-auth" };

        _mockRedisManager
            .Setup(x => x.GetServiceMappingsAsync())
            .ReturnsAsync(mappings);

        _mockRedisManager
            .Setup(x => x.GetMappingsVersionAsync())
            .ReturnsAsync(10);

        var service = CreateService();
        var request = new GetMappingsRequest();

        // Act
        var (statusCode, response) = await service.GetMappingsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.DefaultAppId);
        Assert.Equal(10, response.Version);
        Assert.Single(response.Mappings);
        Assert.Equal("bannou-auth", response.Mappings["auth"]);

        // Verify Redis was called since cache was empty
        _mockRedisManager.Verify(x => x.GetServiceMappingsAsync(), Times.Once);
        _mockRedisManager.Verify(x => x.GetMappingsVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task GetMappingsAsync_WhenCachePopulated_ShouldReturnFromCache()
    {
        // Arrange - Reset and populate cache with known values
        MeshService.ResetCacheForTesting();
        MeshService.UpdateMappingsCache(
            new Dictionary<string, string> { ["cached-service"] = "bannou-cached" },
            version: 50);

        var service = CreateService();
        var request = new GetMappingsRequest();

        // Act
        var (statusCode, response) = await service.GetMappingsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.DefaultAppId);
        Assert.Equal(50, response.Version);
        Assert.Single(response.Mappings);
        Assert.Equal("bannou-cached", response.Mappings["cached-service"]);

        // Verify Redis was NOT called since cache was populated
        _mockRedisManager.Verify(x => x.GetServiceMappingsAsync(), Times.Never);
        _mockRedisManager.Verify(x => x.GetMappingsVersionAsync(), Times.Never);
    }

    [Fact]
    public async Task GetMappingsAsync_WithServiceNameFilter_ShouldFilterMappings()
    {
        // Arrange - Reset cache and set up fresh state
        MeshService.ResetCacheForTesting();

        var mappings = new Dictionary<string, string>
        {
            ["auth-login"] = "bannou-auth",
            ["auth-logout"] = "bannou-auth",
            ["connect-ws"] = "bannou"
        };

        _mockRedisManager
            .Setup(x => x.GetServiceMappingsAsync())
            .ReturnsAsync(mappings);

        _mockRedisManager
            .Setup(x => x.GetMappingsVersionAsync())
            .ReturnsAsync(20);

        var service = CreateService();
        var request = new GetMappingsRequest { ServiceNameFilter = "auth" };

        // Act
        var (statusCode, response) = await service.GetMappingsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Mappings.Count);
        Assert.Contains("auth-login", response.Mappings.Keys);
        Assert.Contains("auth-logout", response.Mappings.Keys);
        Assert.DoesNotContain("connect-ws", response.Mappings.Keys);
    }

    #endregion

    #region GetHealthAsync Tests

    [Fact]
    public async Task GetHealthAsync_WhenAllHealthy_ShouldReturnHealthyStatus()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Healthy }
        };

        _mockRedisManager
            .Setup(x => x.GetAllEndpointsAsync(null))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetHealthRequest();

        // Act
        var (statusCode, response) = await service.GetHealthAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(EndpointStatus.Healthy, response.Status);
        Assert.True(response.RedisConnected);
        Assert.Equal(2, response.Summary.TotalEndpoints);
        Assert.Equal(2, response.Summary.HealthyCount);
    }

    [Fact]
    public async Task GetHealthAsync_WhenRedisUnhealthy_ShouldReturnDegradedStatus()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((false, "Redis connection failed", (TimeSpan?)null));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy }
        };

        _mockRedisManager
            .Setup(x => x.GetAllEndpointsAsync(null))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetHealthRequest();

        // Act
        var (statusCode, response) = await service.GetHealthAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotEqual(EndpointStatus.Healthy, response.Status); // Should be degraded or unhealthy
        Assert.False(response.RedisConnected);
    }

    [Fact]
    public async Task GetHealthAsync_WithMixedEndpointHealth_ShouldReturnDegraded()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Degraded }
        };

        _mockRedisManager
            .Setup(x => x.GetAllEndpointsAsync(null))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetHealthRequest();

        // Act
        var (statusCode, response) = await service.GetHealthAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(EndpointStatus.Degraded, response.Status);
        Assert.Equal(1, response.Summary.HealthyCount);
        Assert.Equal(1, response.Summary.DegradedCount);
    }

    #endregion

    #region Cache Management Tests

    [Fact]
    public void UpdateMappingsCache_WithNewerVersion_ShouldUpdate()
    {
        // Arrange - Reset cache for clean test isolation
        MeshService.ResetCacheForTesting();

        // Set initial state
        MeshService.UpdateMappingsCache(new Dictionary<string, string>(), version: 10);

        var mappings = new Dictionary<string, string> { ["auth"] = "bannou-auth" };

        // Act - update with newer version
        var result = MeshService.UpdateMappingsCache(mappings, version: 11);

        // Assert
        Assert.True(result);
        Assert.Equal(11, MeshService.GetCacheVersion());
    }

    [Fact]
    public void UpdateMappingsCache_WithOlderVersion_ShouldNotUpdate()
    {
        // Arrange - Reset cache for clean test isolation
        MeshService.ResetCacheForTesting();

        // Set initial state with version 20
        MeshService.UpdateMappingsCache(
            new Dictionary<string, string> { ["auth"] = "bannou" },
            version: 20);

        var oldMappings = new Dictionary<string, string> { ["auth"] = "bannou-old" };

        // Act - try to update with older version (15 < 20)
        var result = MeshService.UpdateMappingsCache(oldMappings, version: 15);

        // Assert
        Assert.False(result);
        Assert.Equal(20, MeshService.GetCacheVersion()); // Version unchanged
    }

    [Fact]
    public void UpdateMappingsCache_WithEqualVersion_ShouldNotUpdate()
    {
        // Arrange - Reset cache for clean test isolation
        MeshService.ResetCacheForTesting();

        // Set initial state with version 10
        MeshService.UpdateMappingsCache(
            new Dictionary<string, string> { ["auth"] = "bannou" },
            version: 10);

        var sameMappings = new Dictionary<string, string> { ["auth"] = "bannou-new" };

        // Act - try to update with same version
        var result = MeshService.UpdateMappingsCache(sameMappings, version: 10);

        // Assert
        Assert.False(result);
        Assert.Equal(10, MeshService.GetCacheVersion()); // Version unchanged
    }

    [Fact]
    public void ResetCacheForTesting_ShouldClearAllState()
    {
        // Arrange - Populate cache with data
        MeshService.UpdateMappingsCache(
            new Dictionary<string, string> { ["auth"] = "bannou", ["connect"] = "bannou-connect" },
            version: 100);

        // Act
        MeshService.ResetCacheForTesting();

        // Assert - Version should be 0 after reset
        Assert.Equal(0, MeshService.GetCacheVersion());

        // Cache should now accept version 1
        var result = MeshService.UpdateMappingsCache(
            new Dictionary<string, string> { ["new-service"] = "new-app" },
            version: 1);
        Assert.True(result);
    }

    #endregion
}

/// <summary>
/// Tests for MeshServiceConfiguration.
/// </summary>
public class MeshConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var config = new MeshServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }
}

/// <summary>
/// Tests for MeshRedisManager.
/// </summary>
public class MeshRedisManagerTests
{
    private readonly Mock<ILogger<MeshRedisManager>> _mockLogger;
    private readonly MeshServiceConfiguration _testConfig;

    public MeshRedisManagerTests()
    {
        _mockLogger = new Mock<ILogger<MeshRedisManager>>();
        _testConfig = new MeshServiceConfiguration
        {
            RedisConnectionString = "localhost:6379"
        };
    }

    [Fact]
    public void Constructor_ShouldNotThrow_WithValidConfiguration()
    {
        // Act & Assert - verify it can be constructed with valid configuration
        var manager = new MeshRedisManager(_testConfig, _mockLogger.Object);
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConfigurationMissingConnectionString()
    {
        // Arrange - configuration with empty connection string
        var configWithoutConnection = new MeshServiceConfiguration
        {
            RedisConnectionString = string.Empty
        };

        // Act & Assert - should throw because connection string is required
        Assert.Throws<InvalidOperationException>(() =>
            new MeshRedisManager(configWithoutConnection, _mockLogger.Object));
    }
}

/// <summary>
/// Tests for MeshInvocationClient - the YARP-based service invocation client
/// that replaces DaprClient.InvokeMethodAsync for service-to-service communication.
/// </summary>
public class MeshInvocationClientTests : IDisposable
{
    private readonly Mock<IMeshClient> _mockMeshClient;
    private readonly Mock<IHttpForwarder> _mockForwarder;
    private readonly Mock<ILogger<MeshInvocationClient>> _mockLogger;
    private MeshInvocationClient? _client;

    public MeshInvocationClientTests()
    {
        _mockMeshClient = new Mock<IMeshClient>();
        _mockForwarder = new Mock<IHttpForwarder>();
        _mockLogger = new Mock<ILogger<MeshInvocationClient>>();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private MeshInvocationClient CreateClient()
    {
        _client = new MeshInvocationClient(
            _mockMeshClient.Object,
            _mockForwarder.Object,
            _mockLogger.Object);
        return _client;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var client = CreateClient();
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithNullMeshClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshInvocationClient(
            null!,
            _mockForwarder.Object,
            _mockLogger.Object));

        Assert.Equal("meshClient", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullForwarder_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshInvocationClient(
            _mockMeshClient.Object,
            null!,
            _mockLogger.Object));

        Assert.Equal("forwarder", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MeshInvocationClient(
            _mockMeshClient.Object,
            _mockForwarder.Object,
            null!));

        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region CreateInvokeMethodRequest Tests

    [Fact]
    public void CreateInvokeMethodRequest_WithValidParameters_ShouldCreateRequest()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var request = client.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", "auth/status");

        // Assert
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.True(request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-app-id"), out var appId));
        Assert.Equal("bannou", appId);
        Assert.True(request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-method"), out var method));
        Assert.Equal("auth/status", method);
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithBody_ShouldSerializeRequestBody()
    {
        // Arrange
        var client = CreateClient();
        var requestBody = new TestRequest { Name = "TestValue", Count = 42 };

        // Act
        var request = client.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", "test/endpoint", requestBody);

        // Assert
        Assert.NotNull(request);
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullHttpMethod_ShouldThrowArgumentNullException()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            client.CreateInvokeMethodRequest(null!, "bannou", "test/endpoint"));
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            client.CreateInvokeMethodRequest(HttpMethod.Get, null!, "test/endpoint"));
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullMethodName_ShouldThrowArgumentNullException()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            client.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", null!));
    }

    #endregion

    #region InvokeMethodWithResponseAsync Tests

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WithoutMeshAppId_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        // Note: Not setting mesh-app-id option

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.InvokeMethodWithResponseAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WhenNoEndpointsAvailable_ShouldThrowMeshInvocationException()
    {
        // Arrange
        var client = CreateClient();

        _mockMeshClient
            .Setup(x => x.GetRouteAsync(It.IsAny<GetRouteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("No endpoints available", 503, null, new Dictionary<string, IEnumerable<string>>(), null));

        var request = client.CreateInvokeMethodRequest(HttpMethod.Get, "non-existent-app", "test/endpoint");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MeshInvocationException>(() =>
            client.InvokeMethodWithResponseAsync(request, CancellationToken.None));

        Assert.Equal("non-existent-app", exception.AppId);
        Assert.Equal(503, exception.StatusCode);
    }

    #endregion

    #region IsServiceAvailableAsync Tests

    [Fact]
    public async Task IsServiceAvailableAsync_WhenEndpointExists_ShouldReturnTrue()
    {
        // Arrange
        var client = CreateClient();

        _mockMeshClient
            .Setup(x => x.GetRouteAsync(It.IsAny<GetRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetRouteResponse
            {
                Endpoint = new MeshEndpoint { AppId = "bannou", Host = "localhost", Port = 8080 }
            });

        // Act
        var result = await client.IsServiceAvailableAsync("bannou", CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsServiceAvailableAsync_WhenNoEndpoints_ShouldReturnFalse()
    {
        // Arrange
        var client = CreateClient();

        _mockMeshClient
            .Setup(x => x.GetRouteAsync(It.IsAny<GetRouteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, new Dictionary<string, IEnumerable<string>>(), null));

        // Act
        var result = await client.IsServiceAvailableAsync("non-existent", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsServiceAvailableAsync_WithNullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.IsServiceAvailableAsync(null!, CancellationToken.None));
    }

    #endregion

    #region MeshInvocationException Tests

    [Fact]
    public void MeshInvocationException_ShouldFormatMessageCorrectly()
    {
        // Act
        var exception = new MeshInvocationException("test-app", "test/method", "Test error");

        // Assert
        Assert.Equal("test-app", exception.AppId);
        Assert.Equal("test/method", exception.MethodName);
        Assert.Contains("test-app", exception.Message);
        Assert.Contains("test/method", exception.Message);
        Assert.Contains("Test error", exception.Message);
    }

    [Fact]
    public void MeshInvocationException_NoEndpointsAvailable_ShouldHave503StatusCode()
    {
        // Act
        var exception = MeshInvocationException.NoEndpointsAvailable("test-app", "test/method");

        // Assert
        Assert.Equal(503, exception.StatusCode);
        Assert.Contains("No healthy endpoints", exception.Message);
    }

    [Fact]
    public void MeshInvocationException_HttpError_ShouldIncludeStatusCodeAndBody()
    {
        // Act
        var exception = MeshInvocationException.HttpError("test-app", "test/method", 404, "Not found response body");

        // Assert
        Assert.Equal(404, exception.StatusCode);
        Assert.Contains("404", exception.Message);
        Assert.Contains("Not found response body", exception.Message);
    }

    #endregion

    /// <summary>
    /// Simple test request class for serialization tests.
    /// </summary>
    private class TestRequest
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Simple test response class for deserialization tests.
    /// </summary>
    private class TestResponse
    {
        public string? Result { get; set; }
        public bool Success { get; set; }
    }
}
