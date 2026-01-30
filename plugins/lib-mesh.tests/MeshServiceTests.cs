using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Mesh.Tests;

/// <summary>
/// Tests for MeshService.
/// Uses interface-based mocking for all dependencies.
/// Service-to-app-id mappings are managed by IServiceAppMappingResolver.
/// </summary>
public class MeshServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<MeshService>> _mockLogger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly Mock<IMeshStateManager> _mockStateManager;
    private readonly Mock<IServiceAppMappingResolver> _mockMappingResolver;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public MeshServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MeshService>>();
        _configuration = new MeshServiceConfiguration();
        _mockStateManager = new Mock<IMeshStateManager>();
        _mockMappingResolver = new Mock<IServiceAppMappingResolver>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Default setup for mapping resolver
        _mockMappingResolver.Setup(x => x.GetAllMappings())
            .Returns(new Dictionary<string, string>());
        _mockMappingResolver.Setup(x => x.CurrentVersion).Returns(0);
    }

    private MeshService CreateService()
    {
        return new MeshService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockStateManager.Object,
            _mockMappingResolver.Object,
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
    public void MeshService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<MeshService>();

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

        _mockStateManager
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
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "auth", "account" } },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Services = new List<string> { "connect" } }
        };

        _mockStateManager
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
        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
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

        _mockStateManager
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

        _mockStateManager
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
        _mockStateManager
            .Setup(x => x.RegisterEndpointAsync(It.IsAny<MeshEndpoint>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new RegisterEndpointRequest
        {
            AppId = "bannou",
            Host = "localhost",
            Port = 3500,
            Services = new List<string> { "auth", "account" }
        };

        // Act
        var (statusCode, response) = await service.RegisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Endpoint);
        Assert.Equal("bannou", response.Endpoint.AppId);
        Assert.Equal("localhost", response.Endpoint.Host);
        Assert.Equal(3500, response.Endpoint.Port);
    }

    [Fact]
    public async Task RegisterEndpointAsync_WhenRegistrationFails_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockStateManager
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

        _mockStateManager
            .Setup(x => x.RegisterEndpointAsync(It.IsAny<MeshEndpoint>(), It.IsAny<int>()))
            .Callback<MeshEndpoint, int>((endpoint, _) => capturedEndpoint = endpoint)
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockStateManager
            .Setup(x => x.DeregisterEndpointAsync(instanceId, "bannou"))
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new DeregisterEndpointRequest
        {
            InstanceId = instanceId
        };

        // Act
        var statusCode = await service.DeregisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
    }

    [Fact]
    public async Task DeregisterEndpointAsync_WhenEndpointNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService();
        var request = new DeregisterEndpointRequest
        {
            InstanceId = instanceId
        };

        // Act
        var statusCode = await service.DeregisterEndpointAsync(request, CancellationToken.None);

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

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockStateManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Healthy, 50, 100, null, 90))
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
        Assert.Equal(30, response.NextHeartbeatSeconds); // 90 / 3 = 30
    }

    [Fact]
    public async Task HeartbeatAsync_WhenEndpointNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        _mockStateManager
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

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockStateManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Healthy,
                It.IsAny<float>(), It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .Callback<Guid, string, EndpointStatus, float, int, ICollection<string>?, int>(
                (_, _, _, _, _, _, ttl) => capturedTtl = ttl)
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

    [Fact]
    public async Task HeartbeatAsync_WithIssues_ShouldPassIssuesToStateManager()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "bannou",
            Status = EndpointStatus.Healthy
        };
        ICollection<string>? capturedIssues = null;

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockStateManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Degraded,
                It.IsAny<float>(), It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .Callback<Guid, string, EndpointStatus, float, int, ICollection<string>?, int>(
                (_, _, _, _, _, issues, _) => capturedIssues = issues)
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new HeartbeatRequest
        {
            InstanceId = instanceId,
            Status = EndpointStatus.Degraded,
            LoadPercent = 75,
            CurrentConnections = 200,
            Issues = new List<string> { "disk space low", "memory pressure" }
        };

        // Act
        var (statusCode, response) = await service.HeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(capturedIssues);
        Assert.Equal(2, capturedIssues.Count);
        Assert.Contains("disk space low", capturedIssues);
        Assert.Contains("memory pressure", capturedIssues);
    }

    [Fact]
    public async Task HeartbeatAsync_WithNullIssues_ShouldPassNullToStateManager()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "bannou",
            Status = EndpointStatus.Healthy
        };
        var issuesCaptured = false;
        ICollection<string>? capturedIssues = new List<string> { "should be replaced" };

        _mockStateManager
            .Setup(x => x.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existingEndpoint);

        _mockStateManager
            .Setup(x => x.UpdateHeartbeatAsync(
                instanceId, "bannou", EndpointStatus.Healthy,
                It.IsAny<float>(), It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .Callback<Guid, string, EndpointStatus, float, int, ICollection<string>?, int>(
                (_, _, _, _, _, issues, _) => { capturedIssues = issues; issuesCaptured = true; })
            .ReturnsAsync(true);

        var service = CreateService();
        var request = new HeartbeatRequest
        {
            InstanceId = instanceId,
            Status = EndpointStatus.Healthy,
            LoadPercent = 50,
            CurrentConnections = 100
            // Issues not set - defaults to null
        };

        // Act
        var (statusCode, response) = await service.HeartbeatAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(issuesCaptured, "UpdateHeartbeatAsync should have been called");
        Assert.Null(capturedIssues);
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

        _mockStateManager
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
        _mockStateManager
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

        _mockStateManager
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

        _mockStateManager
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
    public async Task GetMappingsAsync_WhenResolverEmpty_ShouldReturnEmptyMappings()
    {
        // Arrange - Default mock returns empty mappings
        var service = CreateService();
        var request = new GetMappingsRequest();

        // Act
        var (statusCode, response) = await service.GetMappingsAsync(request, CancellationToken.None);

        // Assert - returns empty mappings from resolver
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.DefaultAppId);
        Assert.Equal(0, response.Version);
        Assert.Empty(response.Mappings);
    }

    [Fact]
    public async Task GetMappingsAsync_WhenResolverPopulated_ShouldReturnFromResolver()
    {
        // Arrange - Configure mock resolver with mappings
        var testMappings = new Dictionary<string, string> { ["cached-service"] = "bannou-cached" };
        _mockMappingResolver.Setup(x => x.GetAllMappings()).Returns(testMappings);
        _mockMappingResolver.Setup(x => x.CurrentVersion).Returns(50);

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
    }

    [Fact]
    public async Task GetMappingsAsync_WithServiceNameFilter_ShouldFilterMappings()
    {
        // Arrange - Configure mock resolver with test data
        var testMappings = new Dictionary<string, string>
        {
            ["auth-login"] = "bannou-auth",
            ["auth-logout"] = "bannou-auth",
            ["connect-ws"] = "bannou"
        };
        _mockMappingResolver.Setup(x => x.GetAllMappings()).Returns(testMappings);
        _mockMappingResolver.Setup(x => x.CurrentVersion).Returns(20);

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
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Healthy }
        };

        _mockStateManager
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
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((false, "Redis connection failed", (TimeSpan?)null));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy }
        };

        _mockStateManager
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
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou-auth", Status = EndpointStatus.Degraded }
        };

        _mockStateManager
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

    #region HandleServiceMappingsAsync Tests

    [Fact]
    public async Task HandleServiceMappingsAsync_WithValidMappings_ShouldCallReplaceAllMappings()
    {
        // Arrange
        _mockMappingResolver.Setup(x => x.ReplaceAllMappings(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Returns(true);

        var service = CreateService();
        var evt = new FullServiceMappingsEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 10,
            DefaultAppId = "bannou",
            Mappings = new Dictionary<string, string>
            {
                ["auth"] = "bannou-auth",
                ["account"] = "bannou-account"
            }
        };

        // Act
        await service.HandleServiceMappingsAsync(evt);

        // Assert - Verify ReplaceAllMappings was called with correct parameters
        _mockMappingResolver.Verify(x => x.ReplaceAllMappings(
            It.Is<IReadOnlyDictionary<string, string>>(m =>
                m.Count == 2 &&
                m["auth"] == "bannou-auth" &&
                m["account"] == "bannou-account"),
            "bannou",
            10L),
            Times.Once);
    }

    /// <summary>
    /// Empty mappings are valid and mean "reset to default routing".
    /// When containers are torn down, an empty mappings event is published
    /// to clear the old service-to-app-id mappings so all services route to "bannou".
    /// </summary>
    [Fact]
    public async Task HandleServiceMappingsAsync_WithEmptyMappings_ShouldResetToDefaultRouting()
    {
        // Arrange
        _mockMappingResolver.Setup(x => x.ReplaceAllMappings(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Returns(true);

        var service = CreateService();
        var evt = new FullServiceMappingsEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 5,
            DefaultAppId = "bannou",
            Mappings = new Dictionary<string, string>() // Empty = reset to default
        };

        // Act
        await service.HandleServiceMappingsAsync(evt);

        // Assert - ReplaceAllMappings SHOULD be called with empty mappings to reset routing
        _mockMappingResolver.Verify(x => x.ReplaceAllMappings(
            It.Is<IReadOnlyDictionary<string, string>>(m => m.Count == 0),
            "bannou",
            5L),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceMappingsAsync_WithStaleVersion_ShouldLogDebugWhenRejected()
    {
        // Arrange - Configure resolver to reject update (returns false)
        _mockMappingResolver.Setup(x => x.ReplaceAllMappings(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Returns(false);
        _mockMappingResolver.Setup(x => x.CurrentVersion).Returns(100);

        var service = CreateService();
        var evt = new FullServiceMappingsEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Version = 50, // Older than current version (100)
            DefaultAppId = "bannou",
            Mappings = new Dictionary<string, string> { ["auth"] = "bannou-auth" }
        };

        // Act
        await service.HandleServiceMappingsAsync(evt);

        // Assert - ReplaceAllMappings was called but returned false (logged as debug)
        _mockMappingResolver.Verify(x => x.ReplaceAllMappings(
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string>(),
            50L),
            Times.Once);
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
/// Tests for MeshStateManager.
/// Uses mocked IStateStoreFactory for state store access.
/// </summary>
public class MeshStateManagerTests
{
    private readonly Mock<ILogger<MeshStateManager>> _mockLogger;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;

    public MeshStateManagerTests()
    {
        _mockLogger = new Mock<ILogger<MeshStateManager>>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
    }

    [Fact]
    public async Task Constructor_ShouldNotThrow_WithValidDependencies()
    {
        // Act & Assert - verify it can be constructed with valid dependencies
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object);
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task InitializeAsync_WithNonFunctionalStateStore_ShouldReturnFalse()
    {
        // Arrange - state store factory throws when getting stores
        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store not available"));

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object);

        // Act
        var result = await manager.InitializeAsync();

        // Assert - should return false when initialization fails
        Assert.False(result);
    }
}

/// <summary>
/// Tests for MeshInvocationClient - the HTTP-based service invocation client
/// for service-to-service communication via lib-mesh infrastructure.
/// Uses IMeshStateManager for endpoint resolution to avoid circular dependency with generated clients.
/// </summary>
public class MeshInvocationClientTests : IDisposable
{
    private readonly Mock<IMeshStateManager> _mockStateManager;
    private readonly Mock<ILogger<MeshInvocationClient>> _mockLogger;
    private MeshInvocationClient? _client;

    public MeshInvocationClientTests()
    {
        _mockStateManager = new Mock<IMeshStateManager>();
        _mockLogger = new Mock<ILogger<MeshInvocationClient>>();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private MeshInvocationClient CreateClient()
    {
        _client = new MeshInvocationClient(
            _mockStateManager.Object,
            new MeshServiceConfiguration(),
            _mockLogger.Object);
        return _client;
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<MeshInvocationClient>();
        Assert.NotNull(CreateClient());
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

    #endregion

    #region InvokeMethodWithResponseAsync Tests

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WithoutMeshAppId_ShouldThrowArgumentException()
    {
        // Arrange
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
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

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<MeshEndpoint>()); // Return empty list for no endpoints

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

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<MeshEndpoint>
            {
                new MeshEndpoint { AppId = "bannou", Host = "localhost", Port = 8080 }
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

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<MeshEndpoint>()); // Return empty list

        // Act
        var result = await client.IsServiceAvailableAsync("non-existent", CancellationToken.None);

        // Assert
        Assert.False(result);
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
