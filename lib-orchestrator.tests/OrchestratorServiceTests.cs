using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Orchestrator.Tests;

/// <summary>
/// Tests for OrchestratorService.
/// Uses interface-based mocking for all dependencies.
/// </summary>
public class OrchestratorServiceTests
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorRedisManager> _mockRedisManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;

    public OrchestratorServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            RabbitMqConnectionString = "amqp://guest:guest@rabbitmq:5672",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _mockRedisManager = new Mock<IOrchestratorRedisManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object);
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
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            null!,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("daprClient", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            null!,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("loggerFactory", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            null!,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullRedisManager_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            null!,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("redisManager", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullEventManager_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            null!,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("eventManager", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullHealthMonitor_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            null!,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("healthMonitor", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullRestartManager_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            null!,
            _mockBackendDetector.Object,
            _mockErrorEventEmitter.Object));

        Assert.Equal("restartManager", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullBackendDetector_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new OrchestratorService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            null!,
            _mockErrorEventEmitter.Object));

        Assert.Equal("backendDetector", exception.ParamName);
    }

    #endregion

    #region GetInfrastructureHealthAsync Tests

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenAllHealthy_ShouldReturnHealthyStatus()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        // Pub/sub healthy
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                "bannou-pubsub",
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Healthy);
        Assert.Equal(3, response.Components.Count);
    }

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenRedisUnhealthy_ShouldReturnUnhealthyStatus()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((false, "Redis connection failed", (TimeSpan?)null));

        // Simulate pub/sub failure via Dapr publish failure
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                "bannou-pubsub",
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dapr sidecar unavailable"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Healthy);

        var redisComponent = response.Components.First(c => c.Name == "redis");
        Assert.Equal(ComponentHealthStatus.Unavailable, redisComponent.Status);
    }

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenRabbitMQUnhealthy_ShouldReturnUnhealthyStatus()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "Redis connected", TimeSpan.FromMilliseconds(1.5)));

        // Pub/sub unhealthy via publish failure
        _mockDaprClient
            .Setup(x => x.PublishEventAsync(
                "bannou-pubsub",
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dapr sidecar unavailable"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Healthy);

        var pubsubComponent = response.Components.First(c => c.Name == "pubsub");
        Assert.Equal(ComponentHealthStatus.Unavailable, pubsubComponent.Status);
    }

    #endregion

    #region GetServicesHealthAsync Tests

    [Fact]
    public async Task GetServicesHealthAsync_ShouldReturnHealthReportFromMonitor()
    {
        // Arrange
        var expectedReport = new ServiceHealthReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalServices = 3,
            HealthPercentage = 100.0f,
            HealthyServices = new List<ServiceHealthStatus>
            {
                new() { ServiceId = "accounts", Status = "healthy" },
                new() { ServiceId = "auth", Status = "healthy" },
                new() { ServiceId = "connect", Status = "healthy" }
            },
            UnhealthyServices = new List<ServiceHealthStatus>()
        };

        _mockHealthMonitor
            .Setup(x => x.GetServiceHealthReportAsync())
            .ReturnsAsync(expectedReport);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServicesHealthAsync(new ServiceHealthRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalServices);
        Assert.Equal(100.0f, response.HealthPercentage);
        Assert.Equal(3, response.HealthyServices.Count);
        Assert.Empty(response.UnhealthyServices);
    }

    #endregion

    #region RestartServiceAsync Tests

    [Fact]
    public async Task RestartServiceAsync_WhenSuccessful_ShouldReturnOK()
    {
        // Arrange
        var request = new ServiceRestartRequest
        {
            ServiceName = "accounts",
            Force = false
        };

        var expectedResult = new ServiceRestartResult
        {
            Success = true,
            ServiceName = "accounts",
            Duration = "00:00:05",
            PreviousStatus = "degraded",
            CurrentStatus = "healthy",
            Message = "Service restarted successfully"
        };

        _mockRestartManager
            .Setup(x => x.RestartServiceAsync(It.IsAny<ServiceRestartRequest>()))
            .ReturnsAsync(expectedResult);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.RestartServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("accounts", response.ServiceName);
    }

    [Fact]
    public async Task RestartServiceAsync_WhenNotNeeded_ShouldReturnConflict()
    {
        // Arrange
        var request = new ServiceRestartRequest
        {
            ServiceName = "accounts",
            Force = false
        };

        var expectedResult = new ServiceRestartResult
        {
            Success = false,
            ServiceName = "accounts",
            Message = "Restart not needed: service is healthy"
        };

        _mockRestartManager
            .Setup(x => x.RestartServiceAsync(It.IsAny<ServiceRestartRequest>()))
            .ReturnsAsync(expectedResult);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.RestartServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    #endregion

    #region ShouldRestartServiceAsync Tests

    [Fact]
    public async Task ShouldRestartServiceAsync_ShouldReturnRecommendationFromMonitor()
    {
        // Arrange
        var request = new ShouldRestartServiceRequest { ServiceName = "accounts" };
        var expectedRecommendation = new RestartRecommendation
        {
            ShouldRestart = false,
            ServiceName = "accounts",
            CurrentStatus = "healthy",
            Reason = "Service is healthy - no restart needed"
        };

        _mockHealthMonitor
            .Setup(x => x.ShouldRestartServiceAsync("accounts"))
            .ReturnsAsync(expectedRecommendation);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.ShouldRestartServiceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.ShouldRestart);
        Assert.Equal("healthy", response.CurrentStatus);
    }

    #endregion

    #region GetBackendsAsync Tests

    [Fact]
    public async Task GetBackendsAsync_ShouldReturnBackendsFromDetector()
    {
        // Arrange
        var expectedResponse = new BackendsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            Backends = new List<BackendInfo>
            {
                new() { Type = BackendType.Compose, Available = true, Priority = 4 },
                new() { Type = BackendType.Kubernetes, Available = false, Priority = 1, Error = "Not configured" }
            },
            Recommended = BackendType.Compose
        };

        _mockBackendDetector
            .Setup(x => x.DetectBackendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetBackendsAsync(new ListBackendsRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Backends.Count);
        Assert.Equal(BackendType.Compose, response.Recommended);
    }

    #endregion

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_WhenContainersRunning_ShouldReturnDeployedTrue()
    {
        // Arrange
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>
            {
                new() { AppName = "bannou", Status = ContainerStatusStatus.Running, Instances = 1 },
                new() { AppName = "redis", Status = ContainerStatusStatus.Running, Instances = 1 }
            });
        mockOrchestrator
            .Setup(x => x.BackendType)
            .Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetStatusAsync(new GetStatusRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Deployed);
        Assert.Equal(BackendType.Compose, response.Backend);
        Assert.Equal(2, response.Services.Count);
    }

    [Fact]
    public async Task GetStatusAsync_WhenNoContainers_ShouldReturnDeployedFalse()
    {
        // Arrange
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>());
        mockOrchestrator
            .Setup(x => x.BackendType)
            .Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetStatusAsync(new GetStatusRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Deployed);
        Assert.Empty(response.Services);
    }

    #endregion

    #region GetContainerStatusAsync Tests

    [Fact]
    public async Task GetContainerStatusAsync_WhenFound_ShouldReturnStatus()
    {
        // Arrange
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator
            .Setup(x => x.GetContainerStatusAsync("bannou", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerStatus
            {
                AppName = "bannou",
                Status = ContainerStatusStatus.Running,
                Instances = 1,
                Timestamp = DateTimeOffset.UtcNow
            });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = "bannou" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.AppName);
        Assert.Equal(ContainerStatusStatus.Running, response.Status);
    }

    [Fact]
    public async Task GetContainerStatusAsync_WhenNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator
            .Setup(x => x.GetContainerStatusAsync("unknown-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerStatus
            {
                AppName = "unknown-app",
                Status = ContainerStatusStatus.Stopped,
                Instances = 0
            });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = "unknown-app" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region GetServiceRoutingAsync Tests

    [Fact]
    public async Task GetServiceRoutingAsync_WithServiceMappingsInRedis_ShouldReturnMappings()
    {
        // Arrange
        var serviceRoutings = new Dictionary<string, ServiceRouting>
        {
            ["auth"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["accounts"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["connect"] = new ServiceRouting { AppId = "bannou-main", Host = "bannou-main-container" }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(serviceRoutings);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.Mappings.Count);
        Assert.Equal("bannou-auth", response.Mappings["auth"]);
        Assert.Equal("bannou-auth", response.Mappings["accounts"]);
        Assert.Equal("bannou-main", response.Mappings["connect"]);
        Assert.Equal("bannou", response.DefaultAppId);
    }

    [Fact]
    public async Task GetServiceRoutingAsync_WithNoMappings_ShouldReturnEmptyWithDefault()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Mappings);
        Assert.Equal("bannou", response.DefaultAppId);
        Assert.Equal(0, response.TotalServices);
    }

    [Fact]
    public async Task GetServiceRoutingAsync_WithServiceFilter_ShouldFilterResults()
    {
        // Arrange
        var serviceRoutings = new Dictionary<string, ServiceRouting>
        {
            ["auth"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["accounts"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["connect"] = new ServiceRouting { AppId = "bannou-main", Host = "bannou-main-container" }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(serviceRoutings);

        var service = CreateService();

        // Act - filter for services starting with "a"
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest { ServiceFilter = "a" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Mappings.Count);
        Assert.True(response.Mappings.ContainsKey("auth"));
        Assert.True(response.Mappings.ContainsKey("accounts"));
        Assert.False(response.Mappings.ContainsKey("connect"));
    }

    [Fact]
    public async Task GetServiceRoutingAsync_WhenRedisThrows_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);
    }

    #endregion
}

/// <summary>
/// Tests for OrchestratorServiceConfiguration.
/// </summary>
public class OrchestratorConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var config = new OrchestratorServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_DefaultValues_ShouldBeReasonable()
    {
        // Arrange & Act
        var config = new OrchestratorServiceConfiguration();

        // Assert - check that defaults are set appropriately
        Assert.Equal(90, config.HeartbeatTimeoutSeconds);
        Assert.Equal(5, config.DegradationThresholdMinutes);
    }
}

/// <summary>
/// Tests for ServiceHealthMonitor logic (tested through interface mocks).
/// Note: For comprehensive testing of the DetermineWorstStatus logic,
/// we test through the ShouldRestartServiceAsync method behavior.
/// </summary>
public class ServiceHealthMonitorTests
{
    private readonly Mock<ILogger<ServiceHealthMonitor>> _mockLogger;
    private readonly Mock<IOrchestratorRedisManager> _mockRedisManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly OrchestratorServiceConfiguration _configuration;

    public ServiceHealthMonitorTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockRedisManager = new Mock<IOrchestratorRedisManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
    }

    private ServiceHealthMonitor CreateMonitor()
    {
        return new ServiceHealthMonitor(
            _mockLogger.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object);
    }

    [Fact]
    public async Task GetServiceHealthReportAsync_WithHealthyServices_ShouldReturnHighPercentage()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthStatus>
        {
            new() { ServiceId = "service1", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service2", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service3", Status = "healthy", LastSeen = DateTimeOffset.UtcNow }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var report = await monitor.GetServiceHealthReportAsync();

        // Assert
        Assert.Equal(3, report.TotalServices);
        Assert.Equal(100.0f, report.HealthPercentage);
        Assert.Equal(3, report.HealthyServices.Count);
        Assert.Empty(report.UnhealthyServices);
    }

    [Fact]
    public async Task GetServiceHealthReportAsync_WithMixedHealth_ShouldCalculateCorrectPercentage()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthStatus>
        {
            new() { ServiceId = "service1", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service2", Status = "unavailable", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service3", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service4", Status = "shutting_down", LastSeen = DateTimeOffset.UtcNow }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var report = await monitor.GetServiceHealthReportAsync();

        // Assert
        Assert.Equal(4, report.TotalServices);
        Assert.Equal(50.0f, report.HealthPercentage);
        Assert.Equal(2, report.HealthyServices.Count);
        Assert.Equal(2, report.UnhealthyServices.Count);
    }

    [Fact]
    public async Task ShouldRestartServiceAsync_WhenNoHeartbeats_ShouldRecommendRestart()
    {
        // Arrange
        _mockRedisManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(new List<ServiceHealthStatus>());

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("missing-service");

        // Assert
        Assert.True(recommendation.ShouldRestart);
        Assert.Equal("unavailable", recommendation.CurrentStatus);
        Assert.Contains("No heartbeat data found", recommendation.Reason);
    }

    [Fact]
    public async Task ShouldRestartServiceAsync_WhenHealthy_ShouldNotRecommendRestart()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthStatus>
        {
            new()
            {
                ServiceId = "healthy-service",
                AppId = "bannou",
                Status = "healthy",
                LastSeen = DateTimeOffset.UtcNow
            }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("healthy-service");

        // Assert
        Assert.False(recommendation.ShouldRestart);
        Assert.Equal("healthy", recommendation.CurrentStatus);
    }

    [Fact]
    public async Task ShouldRestartServiceAsync_WhenUnavailable_ShouldRecommendRestart()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthStatus>
        {
            new()
            {
                ServiceId = "unavailable-service",
                AppId = "bannou",
                Status = "unavailable",
                LastSeen = DateTimeOffset.UtcNow
            }
        };

        _mockRedisManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("unavailable-service");

        // Assert
        Assert.True(recommendation.ShouldRestart);
        Assert.Equal("unavailable", recommendation.CurrentStatus);
    }
}

/// <summary>
/// Tests for the routing protection logic in ServiceHealthMonitor.
/// These tests verify that heartbeats cannot overwrite explicit service mappings
/// set via SetServiceRoutingAsync.
/// </summary>
public class ServiceHealthMonitorRoutingProtectionTests
{
    private readonly Mock<ILogger<ServiceHealthMonitor>> _mockLogger;
    private readonly Mock<IOrchestratorRedisManager> _mockRedisManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly OrchestratorServiceConfiguration _configuration;

    // Event handler captured from the mock
    private Action<ServiceHeartbeatEvent>? _heartbeatHandler;

    public ServiceHealthMonitorRoutingProtectionTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockRedisManager = new Mock<IOrchestratorRedisManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
    }

    private ServiceHealthMonitor CreateMonitorWithEventCapture()
    {
        // Capture the heartbeat event subscription handler
        _mockEventManager
            .SetupAdd(m => m.HeartbeatReceived += It.IsAny<Action<ServiceHeartbeatEvent>>())
            .Callback<Action<ServiceHeartbeatEvent>>(handler => _heartbeatHandler = handler);

        // Setup for GetServiceRoutingsAsync (needed for PublishFullMappingsAsync)
        _mockRedisManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        return new ServiceHealthMonitor(
            _mockLogger.Object,
            _configuration,
            _mockRedisManager.Object,
            _mockEventManager.Object);
    }

    [Fact]
    public async Task Heartbeat_WhenNoExistingRouting_ShouldInitializeRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou",
            ServiceId = Guid.NewGuid(),
            Status = ServiceHeartbeatEventStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceStatusStatus.Healthy }
            }
        };

        ServiceRouting? capturedRouting = null;
        _mockRedisManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => capturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockRedisManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // Act - simulate heartbeat event
        _heartbeatHandler?.Invoke(heartbeat);

        // Allow async processing
        await Task.Delay(100);

        // Assert - routing should be initialized
        Assert.NotNull(capturedRouting);
        Assert.Equal("bannou", capturedRouting.AppId);
    }

    [Fact]
    public async Task Heartbeat_WhenExplicitMappingExists_ShouldNotOverwriteRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockRedisManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        _mockRedisManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, set an explicit mapping via SetServiceRoutingAsync (like orchestrator does during deploy)
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Now simulate a heartbeat from different app-id trying to claim the service
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou", // Different from "bannou-auth"
            ServiceId = Guid.NewGuid(),
            Status = ServiceHeartbeatEventStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceStatusStatus.Healthy }
            }
        };

        // Reset the mock to track new calls
        _mockRedisManager.Invocations.Clear();

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100);

        // Assert - WriteServiceRoutingAsync for "auth" should NOT have been called with "bannou"
        // (heartbeat from "bannou" should be ignored because "auth" is routed to "bannou-auth")
        _mockRedisManager.Verify(
            x => x.WriteServiceRoutingAsync("auth", It.Is<ServiceRouting>(r => r.AppId == "bannou")),
            Times.Never(),
            "Heartbeat from wrong app-id should not overwrite explicit routing");
    }

    [Fact]
    public async Task Heartbeat_WhenFromCorrectAppId_ShouldUpdateHealthStatus()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        ServiceRouting? lastCapturedRouting = null;
        _mockRedisManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => lastCapturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockRedisManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, set an explicit mapping via SetServiceRoutingAsync
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Now simulate a heartbeat from the CORRECT app-id
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou-auth", // Same as the mapped app-id
            ServiceId = Guid.NewGuid(),
            Status = ServiceHeartbeatEventStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceStatusStatus.Degraded }
            }
        };

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100);

        // Assert - routing should be updated (health status changed)
        Assert.NotNull(lastCapturedRouting);
        Assert.Equal("bannou-auth", lastCapturedRouting.AppId);
        Assert.Equal("degraded", lastCapturedRouting.Status);
    }

    [Fact]
    public async Task SetServiceRoutingAsync_ShouldOverwriteHeartbeatRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        ServiceRouting? lastCapturedRouting = null;
        _mockRedisManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => lastCapturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockRedisManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, let heartbeat initialize routing
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou",
            ServiceId = Guid.NewGuid(),
            Status = ServiceHeartbeatEventStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceStatusStatus.Healthy }
            }
        };

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(50);

        Assert.Equal("bannou", lastCapturedRouting?.AppId);

        // Now set an explicit mapping that routes to different app-id
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Assert - explicit mapping should overwrite heartbeat routing
        Assert.NotNull(lastCapturedRouting);
        Assert.Equal("bannou-auth", lastCapturedRouting.AppId);
    }

    [Fact]
    public async Task RemoveServiceRoutingAsync_ShouldRemoveRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockRedisManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        _mockRedisManager
            .Setup(x => x.RemoveServiceRoutingAsync("auth"))
            .Returns(Task.CompletedTask);

        // First, set a routing
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Act - remove the routing
        await monitor.RemoveServiceRoutingAsync("auth");

        // Assert - RemoveServiceRoutingAsync was called on redis
        _mockRedisManager.Verify(
            x => x.RemoveServiceRoutingAsync("auth"),
            Times.Once(),
            "RemoveServiceRoutingAsync should be called on redis manager");
    }
}
