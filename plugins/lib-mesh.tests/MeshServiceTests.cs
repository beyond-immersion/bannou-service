using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
            _mockEventConsumer.Object,
            new NullTelemetryProvider());
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
        var firstEndpointServices = response.Endpoints.First().Services ?? throw new InvalidOperationException("Services should not be null");
        Assert.Contains("auth", firstEndpointServices);
    }

    [Fact]
    public async Task GetEndpointsAsync_WhenRedisThrows_ShouldThrow()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var service = CreateService();
        var request = new GetEndpointsRequest { AppId = "bannou" };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.GetEndpointsAsync(request, CancellationToken.None));
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
        var (statusCode, response) = await service.DeregisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        // HTTP 200 confirms deregistration; response is an empty object
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
        var (statusCode, response) = await service.DeregisterEndpointAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
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
        var alternates = response.Alternates ?? throw new InvalidOperationException("Alternates should not be null");
        Assert.Single(alternates); // One alternate since we have 2 endpoints
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
        var endpointServices = response.Endpoint.Services ?? throw new InvalidOperationException("Endpoint Services should not be null");
        Assert.Contains("auth", endpointServices);
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

    [Fact]
    public async Task GetRouteAsync_WithWeightedAlgorithm_ShouldFavorLessLoadedEndpoints()
    {
        // Arrange - one heavily loaded, one lightly loaded
        var lightEndpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou",
            Status = EndpointStatus.Healthy,
            Host = "light",
            Port = 3500,
            LoadPercent = 10,
            CurrentConnections = 5
        };
        var heavyEndpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou",
            Status = EndpointStatus.Healthy,
            Host = "heavy",
            Port = 3500,
            LoadPercent = 90,
            CurrentConnections = 500
        };

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(new List<MeshEndpoint> { heavyEndpoint, lightEndpoint });

        MeshService.ResetLoadBalancingStateForTesting();
        var service = CreateService();

        // Act - run multiple requests and count selections
        var lightCount = 0;
        var heavyCount = 0;
        for (var i = 0; i < 100; i++)
        {
            var (_, response) = await service.GetRouteAsync(
                new GetRouteRequest { AppId = "bannou", Algorithm = LoadBalancerAlgorithm.Weighted },
                CancellationToken.None);
            if (response?.Endpoint.Host == "light") lightCount++;
            else heavyCount++;
        }

        // Assert - light endpoint should be selected significantly more often
        // With load 10 vs 90, weights are 90 vs 10 => ~90% vs ~10%
        Assert.True(lightCount > heavyCount,
            $"Expected light ({lightCount}) > heavy ({heavyCount}), but it wasn't");
    }

    [Fact]
    public async Task GetRouteAsync_WithRoundRobinAlgorithm_ShouldCycleThroughEndpoints()
    {
        // Arrange
        var endpoint1 = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou-rr",
            Status = EndpointStatus.Healthy,
            Host = "host1",
            Port = 3500
        };
        var endpoint2 = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou-rr",
            Status = EndpointStatus.Healthy,
            Host = "host2",
            Port = 3500
        };

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou-rr", false))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint1, endpoint2 });

        MeshService.ResetLoadBalancingStateForTesting();
        var service = CreateService();
        var request = new GetRouteRequest { AppId = "bannou-rr", Algorithm = LoadBalancerAlgorithm.RoundRobin };

        // Act
        var (_, response1) = await service.GetRouteAsync(request, CancellationToken.None);
        var (_, response2) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert - should alternate between the two endpoints
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.NotEqual(response1.Endpoint.Host, response2.Endpoint.Host);
    }

    [Fact]
    public async Task GetRouteAsync_WithRandomAlgorithm_ShouldReturnValidEndpoint()
    {
        // Arrange
        var endpoints = new List<MeshEndpoint>
        {
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Host = "host1", Port = 3500 },
            new() { InstanceId = Guid.NewGuid(), AppId = "bannou", Status = EndpointStatus.Healthy, Host = "host2", Port = 3500 }
        };

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(endpoints);

        var service = CreateService();
        var request = new GetRouteRequest { AppId = "bannou", Algorithm = LoadBalancerAlgorithm.Random };

        // Act
        var (statusCode, response) = await service.GetRouteAsync(request, CancellationToken.None);

        // Assert - should select one of the available endpoints
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Contains(response.Endpoint.Host, new[] { "host1", "host2" });
    }

    [Fact]
    public async Task GetRouteAsync_WithWeightedRoundRobin_ShouldDistributeByLoad()
    {
        // Arrange
        var lightEndpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou-wrr",
            Status = EndpointStatus.Healthy,
            Host = "light",
            Port = 3500,
            LoadPercent = 10
        };
        var heavyEndpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou-wrr",
            Status = EndpointStatus.Healthy,
            Host = "heavy",
            Port = 3500,
            LoadPercent = 90
        };

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou-wrr", false))
            .ReturnsAsync(new List<MeshEndpoint> { heavyEndpoint, lightEndpoint });

        MeshService.ResetLoadBalancingStateForTesting();
        var service = CreateService();

        // Act - run multiple requests
        var lightCount = 0;
        var heavyCount = 0;
        for (var i = 0; i < 20; i++)
        {
            var (_, response) = await service.GetRouteAsync(
                new GetRouteRequest { AppId = "bannou-wrr", Algorithm = LoadBalancerAlgorithm.WeightedRoundRobin },
                CancellationToken.None);
            if (response?.Endpoint.Host == "light") lightCount++;
            else heavyCount++;
        }

        // Assert - light endpoint gets more selections (deterministic, not random)
        Assert.True(lightCount > heavyCount,
            $"Expected light ({lightCount}) > heavy ({heavyCount})");
    }

    [Fact]
    public async Task GetRouteAsync_WithSingleEndpoint_ShouldAlwaysReturnIt()
    {
        // Arrange
        var endpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "bannou",
            Status = EndpointStatus.Healthy,
            Host = "only-one",
            Port = 3500
        };

        _mockStateManager
            .Setup(x => x.GetEndpointsForAppIdAsync("bannou", false))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint });

        MeshService.ResetLoadBalancingStateForTesting();
        var service = CreateService();

        // Act - try each algorithm with single endpoint
        foreach (var algo in new[] { LoadBalancerAlgorithm.RoundRobin, LoadBalancerAlgorithm.LeastConnections,
                                    LoadBalancerAlgorithm.Weighted, LoadBalancerAlgorithm.Random })
        {
            var (statusCode, response) = await service.GetRouteAsync(
                new GetRouteRequest { AppId = "bannou", Algorithm = algo },
                CancellationToken.None);

            Assert.Equal(StatusCodes.OK, statusCode);
            Assert.NotNull(response);
            Assert.Equal("only-one", response.Endpoint.Host);
            Assert.NotNull(response.Alternates);
            Assert.Empty(response.Alternates);
        }
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
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task InitializeAsync_WithNonFunctionalStateStore_ShouldReturnFalse()
    {
        // Arrange - state store factory throws when getting stores
        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store not available"));

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result = await manager.InitializeAsync();

        // Assert - should return false when initialization fails
        Assert.False(result);
    }

    [Fact]
    public async Task InitializeAsync_WithHealthyStores_ShouldReturnTrue()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEndpointStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshAppidIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAppIdIndexStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshGlobalIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGlobalIndexStore.Object);

        // Health check calls ExistsAsync on global index
        mockGlobalIndexStore
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result = await manager.InitializeAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_ShouldSkipSecondInitialization()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEndpointStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshAppidIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAppIdIndexStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshGlobalIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGlobalIndexStore.Object);

        mockGlobalIndexStore
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result1 = await manager.InitializeAsync();
        var result2 = await manager.InitializeAsync();

        // Assert - both return true, but factory only called once
        Assert.True(result1);
        Assert.True(result2);
        _mockStateStoreFactory.Verify(
            x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNotInitialized_ShouldReturnUnhealthy()
    {
        // Arrange - don't call InitializeAsync
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var (isHealthy, message, operationTime) = await manager.CheckHealthAsync();

        // Assert
        Assert.False(isHealthy);
        Assert.Contains("not initialized", message ?? string.Empty);
        Assert.Null(operationTime);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenInitialized_ShouldReturnHealthy()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEndpointStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshAppidIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAppIdIndexStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshGlobalIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGlobalIndexStore.Object);

        mockGlobalIndexStore
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var (isHealthy, message, operationTime) = await manager.CheckHealthAsync();

        // Assert
        Assert.True(isHealthy);
        Assert.NotNull(message);
        Assert.NotNull(operationTime);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExistsThrows_ShouldReturnUnhealthy()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEndpointStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshAppidIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAppIdIndexStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshGlobalIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGlobalIndexStore.Object);

        // First call succeeds (initialization), second call throws (health check after init)
        var callCount = 0;
        mockGlobalIndexStore
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount > 1) throw new Exception("Redis connection lost");
                return true;
            });

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var (isHealthy, message, _) = await manager.CheckHealthAsync();

        // Assert
        Assert.False(isHealthy);
        Assert.Contains("Health check failed", message ?? string.Empty);
    }

    #region RegisterEndpointAsync Tests

    [Fact]
    public async Task RegisterEndpointAsync_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange - don't call InitializeAsync
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        var endpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "test-app",
            Host = "localhost",
            Port = 5000
        };

        // Act
        var result = await manager.RegisterEndpointAsync(endpoint, 90);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RegisterEndpointAsync_WhenInitialized_ShouldSaveAndIndex()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        var endpoint = new MeshEndpoint
        {
            InstanceId = Guid.NewGuid(),
            AppId = "test-app",
            Host = "localhost",
            Port = 5000
        };

        // Act
        var result = await manager.RegisterEndpointAsync(endpoint, 90);

        // Assert
        Assert.True(result);

        // Verify endpoint was saved
        mockEndpointStore.Verify(x => x.SaveAsync(
            endpoint.InstanceId.ToString(),
            endpoint,
            It.Is<StateOptions>(o => o.Ttl == 90),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify instance was added to app-id index
        mockAppIdIndexStore.Verify(x => x.AddToSetAsync(
            endpoint.AppId,
            endpoint.InstanceId.ToString(),
            It.Is<StateOptions>(o => o.Ttl == 90),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify instance was added to global index
        mockGlobalIndexStore.Verify(x => x.AddToSetAsync(
            "_index",
            endpoint.InstanceId.ToString(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterEndpointAsync_WhenSaveThrows_ShouldReturnFalse()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        mockEndpointStore
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<MeshEndpoint>(), It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis write failed"));

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        var endpoint = new MeshEndpoint { InstanceId = Guid.NewGuid(), AppId = "test", Host = "h", Port = 1 };

        // Act
        var result = await manager.RegisterEndpointAsync(endpoint, 90);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DeregisterEndpointAsync Tests

    [Fact]
    public async Task DeregisterEndpointAsync_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result = await manager.DeregisterEndpointAsync(Guid.NewGuid(), "test-app");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeregisterEndpointAsync_WhenInitialized_ShouldRemoveFromAllStores()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        var instanceId = Guid.NewGuid();
        var appId = "test-app";

        // Act
        var result = await manager.DeregisterEndpointAsync(instanceId, appId);

        // Assert
        Assert.True(result);

        mockEndpointStore.Verify(x => x.DeleteAsync(instanceId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        mockAppIdIndexStore.Verify(x => x.RemoveFromSetAsync(appId, instanceId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        mockGlobalIndexStore.Verify(x => x.RemoveFromSetAsync("_index", instanceId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeregisterEndpointAsync_WhenDeleteThrows_ShouldReturnFalse()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        mockEndpointStore
            .Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis delete failed"));

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var result = await manager.DeregisterEndpointAsync(Guid.NewGuid(), "test");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region UpdateHeartbeatAsync Tests

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result = await manager.UpdateHeartbeatAsync(Guid.NewGuid(), "app", EndpointStatus.Healthy, 0, 0, null, 90);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenEndpointNotFound_ShouldReturnFalse()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        mockEndpointStore
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeshEndpoint?)null);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var result = await manager.UpdateHeartbeatAsync(Guid.NewGuid(), "app", EndpointStatus.Healthy, 50, 100, null, 90);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_WhenEndpointExists_ShouldUpdateAndSave()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var instanceId = Guid.NewGuid();
        var existingEndpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = "test-app",
            Host = "localhost",
            Port = 5000,
            Status = EndpointStatus.Healthy,
            LoadPercent = 10,
            CurrentConnections = 5
        };

        mockEndpointStore
            .Setup(x => x.GetAsync(instanceId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEndpoint);

        MeshEndpoint? savedEndpoint = null;
        mockEndpointStore
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<MeshEndpoint>(), It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, MeshEndpoint, StateOptions?, CancellationToken>((_, ep, _, _) => savedEndpoint = ep)
            .ReturnsAsync("etag");

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        var issues = new List<string> { "high memory" };

        // Act
        var result = await manager.UpdateHeartbeatAsync(
            instanceId, "test-app", EndpointStatus.Degraded, 85f, 200, issues, 120);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedEndpoint);
        Assert.Equal(EndpointStatus.Degraded, savedEndpoint.Status);
        Assert.Equal(85f, savedEndpoint.LoadPercent);
        Assert.Equal(200, savedEndpoint.CurrentConnections);
        Assert.NotNull(savedEndpoint.Issues);
        Assert.Contains("high memory", savedEndpoint.Issues);

        // Verify TTL was refreshed on app-id index
        mockAppIdIndexStore.Verify(x => x.RefreshSetTtlAsync("test-app", 120, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetEndpointsForAppIdAsync Tests

    [Fact]
    public async Task GetEndpointsForAppIdAsync_WhenNotInitialized_ShouldReturnEmpty()
    {
        // Arrange
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var endpoints = await manager.GetEndpointsForAppIdAsync("test-app");

        // Assert
        Assert.Empty(endpoints);
    }

    [Fact]
    public async Task GetEndpointsForAppIdAsync_ShouldReturnHealthyEndpoints()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var healthyId = Guid.NewGuid();
        var unhealthyId = Guid.NewGuid();

        mockAppIdIndexStore
            .Setup(x => x.GetSetAsync<string>("test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { healthyId.ToString(), unhealthyId.ToString() });

        mockEndpointStore
            .Setup(x => x.GetAsync(healthyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = healthyId, AppId = "test-app", Status = EndpointStatus.Healthy });
        mockEndpointStore
            .Setup(x => x.GetAsync(unhealthyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = unhealthyId, AppId = "test-app", Status = EndpointStatus.Unavailable });

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetEndpointsForAppIdAsync("test-app");

        // Assert - only healthy endpoint returned
        Assert.Single(endpoints);
        Assert.Equal(healthyId, endpoints[0].InstanceId);
    }

    [Fact]
    public async Task GetEndpointsForAppIdAsync_WithIncludeUnhealthy_ShouldReturnAll()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var healthyId = Guid.NewGuid();
        var unhealthyId = Guid.NewGuid();

        mockAppIdIndexStore
            .Setup(x => x.GetSetAsync<string>("test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { healthyId.ToString(), unhealthyId.ToString() });

        mockEndpointStore
            .Setup(x => x.GetAsync(healthyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = healthyId, AppId = "test-app", Status = EndpointStatus.Healthy });
        mockEndpointStore
            .Setup(x => x.GetAsync(unhealthyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = unhealthyId, AppId = "test-app", Status = EndpointStatus.Unavailable });

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetEndpointsForAppIdAsync("test-app", includeUnhealthy: true);

        // Assert - both endpoints returned
        Assert.Equal(2, endpoints.Count);
    }

    [Fact]
    public async Task GetEndpointsForAppIdAsync_WithStaleIndex_ShouldCleanup()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var staleId = Guid.NewGuid();

        mockAppIdIndexStore
            .Setup(x => x.GetSetAsync<string>("test-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { staleId.ToString() });

        // Endpoint has expired (returns null)
        mockEndpointStore
            .Setup(x => x.GetAsync(staleId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeshEndpoint?)null);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetEndpointsForAppIdAsync("test-app");

        // Assert - empty result, stale entry cleaned up
        Assert.Empty(endpoints);
        mockAppIdIndexStore.Verify(x => x.RemoveFromSetAsync("test-app", staleId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAllEndpointsAsync Tests

    [Fact]
    public async Task GetAllEndpointsAsync_WhenNotInitialized_ShouldReturnEmpty()
    {
        // Arrange
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var endpoints = await manager.GetAllEndpointsAsync();

        // Assert
        Assert.Empty(endpoints);
    }

    [Fact]
    public async Task GetAllEndpointsAsync_ShouldReturnAllEndpoints()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        mockGlobalIndexStore
            .Setup(x => x.GetSetAsync<string>("_index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { id1.ToString(), id2.ToString() });

        mockEndpointStore
            .Setup(x => x.GetAsync(id1.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = id1, AppId = "app-a", Host = "h1", Port = 1 });
        mockEndpointStore
            .Setup(x => x.GetAsync(id2.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = id2, AppId = "app-b", Host = "h2", Port = 2 });

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetAllEndpointsAsync();

        // Assert
        Assert.Equal(2, endpoints.Count);
    }

    [Fact]
    public async Task GetAllEndpointsAsync_WithPrefix_ShouldFilterByPrefix()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        mockGlobalIndexStore
            .Setup(x => x.GetSetAsync<string>("_index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { id1.ToString(), id2.ToString() });

        mockEndpointStore
            .Setup(x => x.GetAsync(id1.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = id1, AppId = "bannou-auth", Host = "h1", Port = 1 });
        mockEndpointStore
            .Setup(x => x.GetAsync(id2.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeshEndpoint { InstanceId = id2, AppId = "other-service", Host = "h2", Port = 2 });

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetAllEndpointsAsync("bannou");

        // Assert
        Assert.Single(endpoints);
        Assert.Equal("bannou-auth", endpoints[0].AppId);
    }

    [Fact]
    public async Task GetAllEndpointsAsync_WithStaleIndex_ShouldCleanup()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var staleId = Guid.NewGuid();

        mockGlobalIndexStore
            .Setup(x => x.GetSetAsync<string>("_index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)new List<string> { staleId.ToString() });

        mockEndpointStore
            .Setup(x => x.GetAsync(staleId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeshEndpoint?)null);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var endpoints = await manager.GetAllEndpointsAsync();

        // Assert
        Assert.Empty(endpoints);
        mockGlobalIndexStore.Verify(x => x.RemoveFromSetAsync("_index", staleId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetEndpointByInstanceIdAsync Tests

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WhenNotInitialized_ShouldReturnNull()
    {
        // Arrange
        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());

        // Act
        var result = await manager.GetEndpointByInstanceIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WhenFound_ShouldReturnEndpoint()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        var instanceId = Guid.NewGuid();
        var expected = new MeshEndpoint { InstanceId = instanceId, AppId = "test", Host = "h", Port = 1 };

        mockEndpointStore
            .Setup(x => x.GetAsync(instanceId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var result = await manager.GetEndpointByInstanceIdAsync(instanceId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(instanceId, result.InstanceId);
    }

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        mockEndpointStore
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeshEndpoint?)null);

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act
        var result = await manager.GetEndpointByInstanceIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetEndpointByInstanceIdAsync_WhenStoreThrows_ShouldPropagate()
    {
        // Arrange
        var mockEndpointStore = new Mock<IStateStore<MeshEndpoint>>();
        var mockAppIdIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();
        var mockGlobalIndexStore = new Mock<ICacheableStateStore<MeshEndpoint>>();

        SetupInitializedManager(mockEndpointStore, mockAppIdIndexStore, mockGlobalIndexStore);

        mockEndpointStore
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis failure"));

        await using var manager = new MeshStateManager(_mockStateStoreFactory.Object, _mockLogger.Object, new NullTelemetryProvider());
        await manager.InitializeAsync();

        // Act & Assert - state store failures propagate to caller
        await Assert.ThrowsAsync<Exception>(() => manager.GetEndpointByInstanceIdAsync(Guid.NewGuid()));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sets up the mock state store factory to return initialized stores
    /// and configures the global index ExistsAsync for health checks.
    /// </summary>
    private void SetupInitializedManager(
        Mock<IStateStore<MeshEndpoint>> mockEndpointStore,
        Mock<ICacheableStateStore<MeshEndpoint>> mockAppIdIndexStore,
        Mock<ICacheableStateStore<MeshEndpoint>> mockGlobalIndexStore)
    {
        _mockStateStoreFactory
            .Setup(x => x.GetStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshEndpoints, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEndpointStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshAppidIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAppIdIndexStore.Object);
        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStoreAsync<MeshEndpoint>(StateStoreDefinitions.MeshGlobalIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGlobalIndexStore.Object);

        mockGlobalIndexStore
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default return for SaveAsync (returns etag string)
        mockEndpointStore
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<MeshEndpoint>(), It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
    }

    #endregion
}

/// <summary>
/// Tests for MeshInvocationClient - the HTTP-based service invocation client
/// for service-to-service communication via lib-mesh infrastructure.
/// Uses IMeshStateManager for endpoint resolution to avoid circular dependency with generated clients.
/// </summary>
public class MeshInvocationClientTests : IDisposable
{
    private readonly Mock<IMeshStateManager> _mockStateManager;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IMessageSubscriber> _mockMessageSubscriber;
    private readonly Mock<ILogger<MeshInvocationClient>> _mockLogger;
    private readonly ITelemetryProvider _telemetryProvider;
    private MeshInvocationClient? _client;

    public MeshInvocationClientTests()
    {
        _mockStateManager = new Mock<IMeshStateManager>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockMessageSubscriber = new Mock<IMessageSubscriber>();
        _mockLogger = new Mock<ILogger<MeshInvocationClient>>();
        _telemetryProvider = new NullTelemetryProvider();

        // Setup default return values for message bus (never throws)
        _mockMessageBus.Setup(x => x.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<PublishOptions>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private MeshInvocationClient CreateClient()
    {
        _client = new MeshInvocationClient(
            _mockStateManager.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            new MeshServiceConfiguration(),
            _mockLogger.Object,
            _telemetryProvider,
            new DefaultMeshInstanceIdentifier());
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
