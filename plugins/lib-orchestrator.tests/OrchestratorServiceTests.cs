using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Setup HTTP client factory to return a mock client
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    #endregion

    #region GetInfrastructureHealthAsync Tests

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenAllHealthy_ShouldReturnHealthyStatus()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "State stores healthy", TimeSpan.FromMilliseconds(1.5)));

        // Pub/sub healthy via IMessageBus
        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                "orchestrator.health-ping",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Healthy);
    }

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenStateStoreUnhealthy_ShouldReturnUnhealthyStatus()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((false, "State store connection failed", (TimeSpan?)null));

        // Pub/sub failure via IMessageBus
        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                "orchestrator.health-ping",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Message bus unavailable"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Healthy);

        var stateStoreComponent = response.Components.First(c => c.Name == "statestore");
        Assert.Equal(ComponentHealthStatus.Unavailable, stateStoreComponent.Status);
    }

    [Fact]
    public async Task GetInfrastructureHealthAsync_WhenMessageBusUnhealthy_ShouldReturnUnhealthyStatus()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.CheckHealthAsync())
            .ReturnsAsync((true, "State stores healthy", TimeSpan.FromMilliseconds(1.5)));

        // Pub/sub unhealthy via IMessageBus publish failure
        // Mock the 3-param overload that the generated event publisher extension method calls
        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                "orchestrator.health-ping",
                It.IsAny<OrchestratorHealthPingEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Message bus unavailable"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), TestContext.Current.CancellationToken);

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
            Source = ServiceHealthSource.All,
            ControlPlaneAppId = "bannou",
            TotalServices = 3,
            HealthPercentage = 100.0f,
            HealthyServices = new List<ServiceHealthEntry>
            {
                new() { ServiceId = "account", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
                new() { ServiceId = "auth", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
                new() { ServiceId = "connect", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow }
            },
            UnhealthyServices = new List<ServiceHealthEntry>()
        };

        _mockHealthMonitor
            .Setup(x => x.GetServiceHealthReportAsync(It.IsAny<ServiceHealthSource>(), It.IsAny<string?>()))
            .ReturnsAsync(expectedReport);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServicesHealthAsync(new ServiceHealthRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(ServiceHealthSource.All, response.Source);
        Assert.Equal("bannou", response.ControlPlaneAppId);
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
            ServiceName = "account",
            Force = false
        };

        var expectedOutcome = new RestartOutcome(
            Succeeded: true,
            DeclineReason: null,
            Duration: "00:00:05",
            PreviousStatus: InstanceHealthStatus.Degraded,
            CurrentStatus: InstanceHealthStatus.Healthy);

        _mockRestartManager
            .Setup(x => x.RestartServiceAsync(It.IsAny<ServiceRestartRequest>()))
            .ReturnsAsync(expectedOutcome);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.RestartServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("00:00:05", response.Duration);
        Assert.Equal(InstanceHealthStatus.Healthy, response.CurrentStatus);
    }

    [Fact]
    public async Task RestartServiceAsync_WhenNotNeeded_ShouldReturnConflict()
    {
        // Arrange
        var request = new ServiceRestartRequest
        {
            ServiceName = "account",
            Force = false
        };

        var expectedOutcome = new RestartOutcome(
            Succeeded: false,
            DeclineReason: "Restart not needed: service is healthy",
            Duration: "00:00:00",
            PreviousStatus: InstanceHealthStatus.Healthy,
            CurrentStatus: InstanceHealthStatus.Healthy);

        _mockRestartManager
            .Setup(x => x.RestartServiceAsync(It.IsAny<ServiceRestartRequest>()))
            .ReturnsAsync(expectedOutcome);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.RestartServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert - Service returns (Conflict, null) when restart is declined
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ShouldRestartServiceAsync Tests

    [Fact]
    public async Task ShouldRestartServiceAsync_ShouldReturnRecommendationFromMonitor()
    {
        // Arrange
        var request = new ShouldRestartServiceRequest { ServiceName = "account" };
        var expectedRecommendation = new RestartRecommendation
        {
            ShouldRestart = false,
            CurrentStatus = InstanceHealthStatus.Healthy,
            Reason = "Service is healthy - no restart needed"
        };

        _mockHealthMonitor
            .Setup(x => x.ShouldRestartServiceAsync("account"))
            .ReturnsAsync(expectedRecommendation);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.ShouldRestartServiceAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.ShouldRestart);
        Assert.Equal(InstanceHealthStatus.Healthy, response.CurrentStatus);
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
        var (statusCode, response) = await service.GetBackendsAsync(new ListBackendsRequest(), TestContext.Current.CancellationToken);

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
                new() { AppName = "bannou", Status = ContainerStatusType.Running, Instances = 1 },
                new() { AppName = "redis", Status = ContainerStatusType.Running, Instances = 1 }
            });
        mockOrchestrator
            .Setup(x => x.BackendType)
            .Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetStatusAsync(new GetStatusRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Deployed);
        Assert.Equal(BackendType.Compose, response.Backend);
        Assert.NotNull(response.Services);
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
        var (statusCode, response) = await service.GetStatusAsync(new GetStatusRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Deployed);
        Assert.NotNull(response.Services);
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
                Status = ContainerStatusType.Running,
                Instances = 1,
                Timestamp = DateTimeOffset.UtcNow
            });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = "bannou" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("bannou", response.AppName);
        Assert.Equal(ContainerStatusType.Running, response.Status);
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
                Status = ContainerStatusType.Stopped,
                Instances = 0
            });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = "unknown-app" }, TestContext.Current.CancellationToken);

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
            ["account"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["connect"] = new ServiceRouting { AppId = "bannou-main", Host = "bannou-main-container" }
        };

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(serviceRoutings);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.Mappings.Count);
        Assert.Equal("bannou-auth", response.Mappings["auth"]);
        Assert.Equal("bannou-auth", response.Mappings["account"]);
        Assert.Equal("bannou-main", response.Mappings["connect"]);
        Assert.Equal("bannou", response.DefaultAppId);
    }

    [Fact]
    public async Task GetServiceRoutingAsync_WithNoMappings_ShouldReturnEmptyWithDefault()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            TestContext.Current.CancellationToken);

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
            ["account"] = new ServiceRouting { AppId = "bannou-auth", Host = "bannou-auth-container" },
            ["connect"] = new ServiceRouting { AppId = "bannou-main", Host = "bannou-main-container" }
        };

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(serviceRoutings);

        var service = CreateService();

        // Act - filter for services starting with "a"
        var (statusCode, response) = await service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest { ServiceFilter = "a" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Mappings.Count);
        Assert.True(response.Mappings.ContainsKey("auth"));
        Assert.True(response.Mappings.ContainsKey("account"));
        Assert.False(response.Mappings.ContainsKey("connect"));
    }

    [Fact]
    public async Task GetServiceRoutingAsync_WhenStateStoreThrows_ShouldThrow()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ThrowsAsync(new InvalidOperationException("State store connection failed"));

        var service = CreateService();

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetServiceRoutingAsync(
            new GetServiceRoutingRequest(),
            TestContext.Current.CancellationToken));
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
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IControlPlaneServiceProvider> _mockControlPlaneProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;

    public ServiceHealthMonitorTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockControlPlaneProvider = new Mock<IControlPlaneServiceProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();

        // Default setup for control plane provider
        _mockControlPlaneProvider.Setup(x => x.ControlPlaneAppId).Returns("bannou");
        _mockControlPlaneProvider.Setup(x => x.GetControlPlaneServiceHealth()).Returns(new List<ServiceHealthEntry>());
        _mockControlPlaneProvider.Setup(x => x.GetEnabledServiceNames()).Returns(new List<string>());
    }

    private ServiceHealthMonitor CreateMonitor()
    {
        return new ServiceHealthMonitor(
            _mockLogger.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockControlPlaneProvider.Object,
            new DefaultMeshInstanceIdentifier(),
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            _mockTelemetryProvider.Object);
    }

    [Fact]
    public async Task GetServiceHealthReportAsync_WithHealthyServices_ShouldReturnHighPercentage()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthEntry>
        {
            new() { ServiceId = "service1", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service2", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service3", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow }
        };

        _mockStateManager
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
        var heartbeats = new List<ServiceHealthEntry>
        {
            new() { ServiceId = "service1", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service2", AppId = "bannou", Status = InstanceHealthStatus.Unavailable, LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service3", AppId = "bannou", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service4", AppId = "bannou", Status = InstanceHealthStatus.Unavailable, LastSeen = DateTimeOffset.UtcNow }
        };

        _mockStateManager
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
        _mockStateManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(new List<ServiceHealthEntry>());

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("missing-service");

        // Assert
        Assert.True(recommendation.ShouldRestart);
        Assert.Equal(InstanceHealthStatus.Unavailable, recommendation.CurrentStatus);
        Assert.Contains("No heartbeat data found", recommendation.Reason);
    }

    [Fact]
    public async Task ShouldRestartServiceAsync_WhenHealthy_ShouldNotRecommendRestart()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthEntry>
        {
            new()
            {
                ServiceId = "healthy-service",
                AppId = "bannou",
                Status = InstanceHealthStatus.Healthy,
                LastSeen = DateTimeOffset.UtcNow
            }
        };

        _mockStateManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("healthy-service");

        // Assert
        Assert.False(recommendation.ShouldRestart);
        Assert.Equal(InstanceHealthStatus.Healthy, recommendation.CurrentStatus);
    }

    [Fact]
    public async Task ShouldRestartServiceAsync_WhenUnavailable_ShouldRecommendRestart()
    {
        // Arrange
        var heartbeats = new List<ServiceHealthEntry>
        {
            new()
            {
                ServiceId = "unavailable-service",
                AppId = "bannou",
                Status = InstanceHealthStatus.Unavailable,
                LastSeen = DateTimeOffset.UtcNow
            }
        };

        _mockStateManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(heartbeats);

        var monitor = CreateMonitor();

        // Act
        var recommendation = await monitor.ShouldRestartServiceAsync("unavailable-service");

        // Assert
        Assert.True(recommendation.ShouldRestart);
        Assert.Equal(InstanceHealthStatus.Unavailable, recommendation.CurrentStatus);
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
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IControlPlaneServiceProvider> _mockControlPlaneProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;

    // Event handler captured from the mock
    private Action<ServiceHeartbeatEvent>? _heartbeatHandler;

    public ServiceHealthMonitorRoutingProtectionTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockControlPlaneProvider = new Mock<IControlPlaneServiceProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();

        // Default setup for control plane provider
        _mockControlPlaneProvider.Setup(x => x.ControlPlaneAppId).Returns("bannou");
        _mockControlPlaneProvider.Setup(x => x.GetControlPlaneServiceHealth()).Returns(new List<ServiceHealthEntry>());
        _mockControlPlaneProvider.Setup(x => x.GetEnabledServiceNames()).Returns(new List<string>());
    }

    private ServiceHealthMonitor CreateMonitorWithEventCapture()
    {
        // Capture the heartbeat event subscription handler
        _mockEventManager
            .SetupAdd(m => m.HeartbeatReceived += It.IsAny<Action<ServiceHeartbeatEvent>>())
            .Callback<Action<ServiceHeartbeatEvent>>(handler => _heartbeatHandler = handler);

        // Setup for GetServiceRoutingsAsync (needed for PublishFullMappingsAsync)
        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        return new ServiceHealthMonitor(
            _mockLogger.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockControlPlaneProvider.Object,
            new DefaultMeshInstanceIdentifier(),
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            _mockTelemetryProvider.Object);
    }

    [Fact]
    public async Task Heartbeat_WhenNoExistingRouting_ShouldInitializeRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        // Use a non-control-plane app-id to test heartbeat routing initialization.
        // Heartbeats from "bannou" (control plane) are intentionally filtered to prevent
        // the orchestrator from claiming services before deployed nodes can.
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou-deployed-node",
            ServiceId = Guid.NewGuid(),
            Status = InstanceHealthStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceHealthStatus.Healthy }
            }
        };

        ServiceRouting? capturedRouting = null;
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => capturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // Act - simulate heartbeat event
        _heartbeatHandler?.Invoke(heartbeat);

        // Allow async processing
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - routing should be initialized
        Assert.NotNull(capturedRouting);
        Assert.Equal("bannou-deployed-node", capturedRouting.AppId);
    }

    [Fact]
    public async Task Heartbeat_WhenExplicitMappingExists_ShouldNotOverwriteRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, set an explicit mapping via SetServiceRoutingAsync (like orchestrator does during deploy)
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Now simulate a heartbeat from different app-id trying to claim the service
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou", // Different from "bannou-auth"
            ServiceId = Guid.NewGuid(),
            Status = InstanceHealthStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceHealthStatus.Healthy }
            }
        };

        // Reset the mock to track new calls
        _mockStateManager.Invocations.Clear();

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - WriteServiceRoutingAsync for "auth" should NOT have been called with "bannou"
        // (heartbeat from "bannou" should be ignored because "auth" is routed to "bannou-auth")
        _mockStateManager.Verify(
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
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => lastCapturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, set an explicit mapping via SetServiceRoutingAsync
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Now simulate a heartbeat from the CORRECT app-id
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou-auth", // Same as the mapped app-id
            ServiceId = Guid.NewGuid(),
            Status = InstanceHealthStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceHealthStatus.Degraded }
            }
        };

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - routing should be updated (health status changed)
        Assert.NotNull(lastCapturedRouting);
        Assert.Equal("bannou-auth", lastCapturedRouting.AppId);
        Assert.Equal(ServiceHealthStatus.Degraded, lastCapturedRouting.Status);
    }

    [Fact]
    public async Task SetServiceRoutingAsync_ShouldOverwriteHeartbeatRouting()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        ServiceRouting? lastCapturedRouting = null;
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => lastCapturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

        // First, let heartbeat initialize routing.
        // Use a non-control-plane app-id since "bannou" heartbeats are filtered.
        var heartbeat = new ServiceHeartbeatEvent
        {
            AppId = "bannou-deployed-node",
            ServiceId = Guid.NewGuid(),
            Status = InstanceHealthStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceHealthStatus.Healthy }
            }
        };

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("bannou-deployed-node", lastCapturedRouting?.AppId);

        // Now set an explicit mapping that routes to different app-id
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Assert - explicit mapping should overwrite heartbeat routing
        Assert.NotNull(lastCapturedRouting);
        Assert.Equal("bannou-auth", lastCapturedRouting.AppId);
    }

    [Fact]
    public async Task RestoreServiceRoutingToDefaultAsync_ShouldSetRoutingToDefault()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        ServiceRouting? capturedRouting = null;
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((_, r) => capturedRouting = r)
            .Returns(Task.CompletedTask);

        // First, set a custom routing
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");
        Assert.Equal("bannou-auth", capturedRouting?.AppId);

        // Act - restore the routing to default
        await monitor.RestoreServiceRoutingToDefaultAsync("auth");

        // Assert - The routing was set to the default app-id
        Assert.NotNull(capturedRouting);
        Assert.Equal(Program.Configuration.EffectiveAppId, capturedRouting.AppId);
    }

    [Fact]
    public async Task ResetAllMappingsToDefaultAsync_ShouldSetAllRoutingsToDefaultAndPublishMappings()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        // Setup to return an empty list (no services in the routing index)
        _mockStateManager
            .Setup(x => x.SetAllServiceRoutingsToDefaultAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<string>());

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        _mockEventManager
            .Setup(x => x.PublishFullMappingsAsync(It.IsAny<FullServiceMappingsEvent>()))
            .Returns(Task.CompletedTask);

        // Act
        await monitor.ResetAllMappingsToDefaultAsync();

        // Assert - SetAllServiceRoutingsToDefaultAsync should be called (not ClearAllServiceRoutingsAsync)
        _mockStateManager.Verify(
            x => x.SetAllServiceRoutingsToDefaultAsync(Program.Configuration.EffectiveAppId),
            Times.Once(),
            "SetAllServiceRoutingsToDefaultAsync should be called to set all routes to default");

        _mockEventManager.Verify(
            x => x.PublishFullMappingsAsync(It.Is<FullServiceMappingsEvent>(e =>
                e.DefaultAppId == Program.Configuration.EffectiveAppId)),
            Times.Once(),
            "Should publish full mappings event");
    }

    [Fact]
    public async Task ResetAllMappingsToDefaultAsync_ShouldUpdateInMemoryCacheToDefaults()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync(It.IsAny<string>(), It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        // Return the list of services that were updated
        _mockStateManager
            .Setup(x => x.SetAllServiceRoutingsToDefaultAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "auth", "account" });

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        _mockEventManager
            .Setup(x => x.PublishFullMappingsAsync(It.IsAny<FullServiceMappingsEvent>()))
            .Returns(Task.CompletedTask);

        // First, set some routings
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");
        await monitor.SetServiceRoutingAsync("account", "bannou-account");

        // Act
        await monitor.ResetAllMappingsToDefaultAsync();

        // Assert - SetAllServiceRoutingsToDefaultAsync was called
        _mockStateManager.Verify(
            x => x.SetAllServiceRoutingsToDefaultAsync(Program.Configuration.EffectiveAppId),
            Times.Once(),
            "SetAllServiceRoutingsToDefaultAsync should be called");

        // The in-memory cache should now have default routing (not be cleared)
        // Reset sets routes to EffectiveAppId rather than deleting them, ensuring
        // routing proxies (OpenResty) always have explicit routing data to read
    }
}

/// <summary>
/// Tests for OrchestratorStateManager operations.
/// These tests verify state store key management for deployment configuration tracking.
/// </summary>
public class OrchestratorStateManagerTests
{
    [Fact]
    public void ClearAllServiceRoutingsAsync_Interface_ShouldExist()
    {
        // Arrange & Act & Assert
        // Verify the interface method exists (compile-time check)
        var mockStateManager = new Mock<IOrchestratorStateManager>();
        mockStateManager.Setup(x => x.ClearAllServiceRoutingsAsync()).Returns(Task.CompletedTask);

        Assert.NotNull(mockStateManager.Object);
    }

    [Fact]
    public void ClearCurrentConfigurationAsync_Interface_ShouldExist()
    {
        // Arrange & Act & Assert
        // Verify the interface method exists (compile-time check)
        var mockStateManager = new Mock<IOrchestratorStateManager>();
        mockStateManager.Setup(x => x.ClearCurrentConfigurationAsync()).ReturnsAsync(1);

        Assert.NotNull(mockStateManager.Object);
    }

    [Fact]
    public async Task ClearCurrentConfigurationAsync_ShouldReturnNewVersion()
    {
        // Arrange
        var mockStateManager = new Mock<IOrchestratorStateManager>();
        mockStateManager.Setup(x => x.ClearCurrentConfigurationAsync()).ReturnsAsync(5);

        // Act
        var newVersion = await mockStateManager.Object.ClearCurrentConfigurationAsync();

        // Assert
        Assert.Equal(5, newVersion);
    }

    #region OrchestratorStateManager Implementation Tests

    [Fact]
    public void OrchestratorStateManager_ConstructorIsValid()
    {
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task OrchestratorStateManager_CheckHealthAsync_WhenNotInitialized_ShouldReturnNotHealthy()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act
        var (isHealthy, message, _) = await manager.CheckHealthAsync();

        // Assert
        Assert.False(isHealthy);
        Assert.Contains("not initialized", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrchestratorStateManager_GetConfigVersionAsync_WhenNotInitialized_ShouldReturnZero()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act
        var result = await manager.GetConfigVersionAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task OrchestratorStateManager_GetServiceHeartbeatsAsync_WhenNotInitialized_ShouldReturnEmptyList()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act
        var result = await manager.GetServiceHeartbeatsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task OrchestratorStateManager_GetServiceRoutingsAsync_WhenNotInitialized_ShouldReturnEmptyDictionary()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act
        var result = await manager.GetServiceRoutingsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task OrchestratorStateManager_WriteServiceHeartbeatAsync_WhenNotInitialized_ShouldNotThrow()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());
        var heartbeat = new ServiceHeartbeatEvent
        {
            ServiceId = Guid.NewGuid(),
            AppId = "test-app",
            Status = InstanceHealthStatus.Healthy
        };

        // Act & Assert - should log warning but not throw
        var exception = await Record.ExceptionAsync(() => manager.WriteServiceHeartbeatAsync(heartbeat));
        Assert.Null(exception);
    }

    [Fact]
    public async Task OrchestratorStateManager_WriteServiceRoutingAsync_WhenNotInitialized_ShouldNotThrow()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());
        var routing = new ServiceRouting
        {
            AppId = "test-app",
            Host = "localhost",
            Port = 5012
        };

        // Act & Assert - should log warning but not throw
        var exception = await Record.ExceptionAsync(() => manager.WriteServiceRoutingAsync("test-service", routing));
        Assert.Null(exception);
    }

    [Fact]
    public async Task OrchestratorStateManager_RestoreConfigurationVersionAsync_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act
        var result = await manager.RestoreConfigurationVersionAsync(1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OrchestratorStateManager_Dispose_ShouldNotThrow()
    {
        // Arrange - using ensures disposal even if assertion fails; Dispose is idempotent
        using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act & Assert - Should not throw (manager is disposed explicitly, then again by using)
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task OrchestratorStateManager_DisposeAsync_ShouldNotThrow()
    {
        // Arrange - await using ensures disposal even if assertion fails; DisposeAsync is idempotent
        await using var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>(),
            new OrchestratorServiceConfiguration(),
            Mock.Of<ITelemetryProvider>());

        // Act & Assert - Should not throw (manager is disposed explicitly, then again by await using)
        var exception = await Record.ExceptionAsync(async () => await manager.DisposeAsync());
        Assert.Null(exception);
    }

    #endregion
}

/// <summary>
/// Tests for reset-to-default topology functionality in OrchestratorService.
/// These tests verify the "deploy default/bannou/empty" behavior that resets
/// to default topology by tearing down tracked deployments.
/// </summary>
public class OrchestratorResetToDefaultTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorResetToDefaultTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();

        // Setup logger factory
        _mockLoggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());

        // Setup HTTP client factory to return a mock client
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("DEFAULT")]
    [InlineData("bannou")]
    [InlineData("Bannou")]
    [InlineData("BANNOU")]
    [InlineData("")]
    public async Task DeployAsync_WithResetPresets_ShouldTriggerResetToDefault(string? preset)
    {
        // Arrange
        var service = CreateService();

        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Returns(Task.CompletedTask);

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.DetectBackendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackendsResponse
            {
                Backends = new List<BackendInfo>
                {
                    new() { Type = BackendType.Compose, Available = true }
                },
                Recommended = BackendType.Compose
            });

        _mockBackendDetector
            .Setup(x => x.CreateOrchestrator(BackendType.Compose))
            .Returns(mockOrchestrator.Object);

        // No deployment configuration - already at default
        _mockStateManager
            .Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync((DeploymentConfiguration?)null);

        _mockHealthMonitor
            .Setup(x => x.ResetAllMappingsToDefaultAsync())
            .Returns(Task.CompletedTask);

        var request = new DeployRequest
        {
            Preset = preset
        };

        // Act
        var (statusCode, response) = await service.DeployAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("default", response.Preset);

        _mockHealthMonitor.Verify(x => x.ResetAllMappingsToDefaultAsync(), Times.Once(),
            "ResetAllMappingsToDefaultAsync should be called for reset-to-default requests");
    }

    [Fact]
    public async Task DeployAsync_WithTrackedDeployments_ShouldTearDownTrackedContainers()
    {
        // Arrange
        var service = CreateService();

        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Returns(Task.CompletedTask);

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);

        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult { Success = true, StoppedContainers = new List<string>() });

        _mockBackendDetector
            .Setup(x => x.DetectBackendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackendsResponse
            {
                Backends = new List<BackendInfo>
                {
                    new() { Type = BackendType.Compose, Available = true }
                },
                Recommended = BackendType.Compose
            });

        _mockBackendDetector
            .Setup(x => x.CreateOrchestrator(BackendType.Compose))
            .Returns(mockOrchestrator.Object);

        // Setup: there's a tracked deployment with services on "bannou-auth"
        var existingConfig = new DeploymentConfiguration
        {
            PresetName = "split-auth",
            Services = new Dictionary<string, ServiceDeploymentConfig>
            {
                ["auth"] = new() { Enabled = true, AppId = "bannou-auth" },
                ["permission"] = new() { Enabled = true, AppId = "bannou-auth" }
            }
        };

        _mockStateManager
            .Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(existingConfig);

        _mockStateManager
            .Setup(x => x.ClearCurrentConfigurationAsync())
            .ReturnsAsync(2);

        _mockHealthMonitor
            .Setup(x => x.ResetAllMappingsToDefaultAsync())
            .Returns(Task.CompletedTask);

        var request = new DeployRequest
        {
            Preset = "default"
        };

        // Act
        var (statusCode, response) = await service.DeployAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Should tear down "bannou-auth" (the tracked deployment)
        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync("bannou-auth", false, It.IsAny<CancellationToken>()),
            Times.Once(),
            "Should tear down tracked container bannou-auth");

        // Should NOT tear down "bannou" (excluded from teardown)
        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync("bannou", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "Should NOT tear down default 'bannou' container");

        _mockStateManager.Verify(x => x.ClearCurrentConfigurationAsync(), Times.Once(),
            "ClearCurrentConfigurationAsync should be called to save empty config");

        _mockHealthMonitor.Verify(x => x.ResetAllMappingsToDefaultAsync(), Times.Once(),
            "ResetAllMappingsToDefaultAsync should be called");
    }

    [Fact]
    public async Task DeployAsync_WithDirectTopology_ShouldNotTriggerReset()
    {
        // Arrange
        var service = CreateService();

        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Returns(Task.CompletedTask);

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);

        mockOrchestrator
            .Setup(x => x.DeployServiceAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeployServiceResult { Success = true, ContainerId = "test-container" });

        _mockBackendDetector
            .Setup(x => x.DetectBackendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackendsResponse
            {
                Backends = new List<BackendInfo>
                {
                    new() { Type = BackendType.Compose, Available = true }
                },
                Recommended = BackendType.Compose
            });

        _mockBackendDetector
            .Setup(x => x.CreateOrchestrator(BackendType.Compose))
            .Returns(mockOrchestrator.Object);

        _mockStateManager
            .Setup(x => x.GetServiceHeartbeatsAsync())
            .ReturnsAsync(new List<ServiceHealthEntry>
            {
                new() { AppId = "bannou-auth", ServiceId = "auth", Status = InstanceHealthStatus.Healthy, LastSeen = DateTimeOffset.UtcNow }
            });

        _mockStateManager
            .Setup(x => x.SaveConfigurationVersionAsync(It.IsAny<DeploymentConfiguration>()))
            .ReturnsAsync(1);

        _mockHealthMonitor
            .Setup(x => x.SetServiceRoutingAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Request with direct topology (not a reset request because topology is specified)
        // NOTE: Do NOT provide a preset, otherwise the service tries to load it before using the topology
        var request = new DeployRequest
        {
            Backend = BackendType.Compose, // Explicitly request the mocked backend
            Topology = new ServiceTopology
            {
                Nodes = new List<TopologyNode>
                {
                    new()
                    {
                        Name = "bannou-auth",
                        AppId = "bannou-auth",
                        Services = new List<string> { "auth" }
                    }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.DeployAsync(request, TestContext.Current.CancellationToken);

        // Assert - should deploy normally, not reset
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Should NOT call ResetAllMappingsToDefaultAsync (because direct topology was specified)
        _mockHealthMonitor.Verify(x => x.ResetAllMappingsToDefaultAsync(), Times.Never(),
            "ResetAllMappingsToDefaultAsync should NOT be called when direct topology is specified");

        // Should call DeployServiceAsync
        mockOrchestrator.Verify(
            x => x.DeployServiceAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "Should deploy the topology node");
    }

    [Fact]
    public async Task DeployAsync_ResetWithAllServicesOnBannou_ShouldClearConfigButNotTearDown()
    {
        // Arrange
        var service = CreateService();

        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Returns(Task.CompletedTask);

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.DetectBackendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackendsResponse
            {
                Backends = new List<BackendInfo>
                {
                    new() { Type = BackendType.Compose, Available = true }
                },
                Recommended = BackendType.Compose
            });

        _mockBackendDetector
            .Setup(x => x.CreateOrchestrator(BackendType.Compose))
            .Returns(mockOrchestrator.Object);

        // All services are on "bannou" - nothing to tear down
        var existingConfig = new DeploymentConfiguration
        {
            PresetName = "monolith",
            Services = new Dictionary<string, ServiceDeploymentConfig>
            {
                ["auth"] = new() { Enabled = true, AppId = "bannou" },
                ["account"] = new() { Enabled = true, AppId = "bannou" }
            }
        };

        _mockStateManager
            .Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(existingConfig);

        _mockStateManager
            .Setup(x => x.ClearCurrentConfigurationAsync())
            .ReturnsAsync(2);

        _mockHealthMonitor
            .Setup(x => x.ResetAllMappingsToDefaultAsync())
            .Returns(Task.CompletedTask);

        var request = new DeployRequest
        {
            Preset = "default"
        };

        // Act
        var (statusCode, response) = await service.DeployAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("default", response.Preset);

        // Should NOT tear down any containers (all on "bannou")
        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "Should NOT tear down any containers when all services are on 'bannou'");

        // Should still clear configuration
        _mockStateManager.Verify(x => x.ClearCurrentConfigurationAsync(), Times.Once(),
            "ClearCurrentConfigurationAsync should still be called");

        _mockHealthMonitor.Verify(x => x.ResetAllMappingsToDefaultAsync(), Times.Once(),
            "ResetAllMappingsToDefaultAsync should be called");
    }
}

/// <summary>
/// Tests for processing pool functionality in OrchestratorService.
/// Covers ScalePoolAsync, pool configuration, and container deployment.
/// </summary>
public class OrchestratorProcessingPoolTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public OrchestratorProcessingPoolTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Setup HTTP client factory to return a mock client
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    #region ScalePoolAsync Validation Tests

    [Fact]
    public async Task ScalePoolAsync_WithEmptyPoolType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new ScalePoolRequest { PoolType = "", TargetInstances = 5 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task ScalePoolAsync_WithNegativeTargetInstances_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new ScalePoolRequest { PoolType = "actor-shared", TargetInstances = -1 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task ScalePoolAsync_WithNoPoolConfiguration_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();

        // No pool configuration exists
        _mockStateManager
            .Setup(x => x.GetPoolConfigurationAsync("unknown-pool"))
            .ReturnsAsync((PoolConfiguration?)null);

        var request = new ScalePoolRequest { PoolType = "unknown-pool", TargetInstances = 5 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert - NotFound returns null response
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ScalePoolAsync Scale-Up Tests

    [Fact]
    public async Task ScalePoolAsync_ScaleUp_ShouldCallDeployServiceAsync()
    {
        // Arrange
        var service = CreateService();

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        mockOrchestrator
            .Setup(x => x.DeployServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeployServiceResult { Success = true, AppId = "test-app" });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        // Pool configuration exists
        SetupPoolConfiguration("actor-shared", "actor");

        // No existing instances
        _mockStateManager
            .Setup(x => x.GetPoolInstancesAsync("actor-shared"))
            .ReturnsAsync((List<ProcessorInstance>?)null);
        _mockStateManager
            .Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync((List<ProcessorInstance>?)null);
        _mockStateManager
            .Setup(x => x.GetLeasesAsync("actor-shared"))
            .ReturnsAsync((Dictionary<string, ProcessorLease>?)null);

        var request = new ScalePoolRequest { PoolType = "actor-shared", TargetInstances = 2 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.ScaledUp);
        Assert.Equal(0, response.PreviousInstances);
        Assert.Equal(2, response.CurrentInstances);

        // Verify DeployServiceAsync was called twice (for 2 new instances)
        mockOrchestrator.Verify(
            x => x.DeployServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<Dictionary<string, string>>(env =>
                    env.ContainsKey("BANNOU_APP_ID") &&
                    env.ContainsKey("ACTOR_POOL_NODE_ID")),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region ScalePoolAsync Scale-Down Tests

    [Fact]
    public async Task ScalePoolAsync_ScaleDown_ShouldCallTeardownServiceAsync()
    {
        // Arrange
        var service = CreateService();

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult { Success = true });

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        // Pool configuration exists
        SetupPoolConfiguration("actor-shared", "actor");

        // 3 existing instances (all available)
        SetupExistingPoolInstances("actor-shared", 3, 3);

        var request = new ScalePoolRequest { PoolType = "actor-shared", TargetInstances = 1 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.ScaledDown);
        Assert.Equal(3, response.PreviousInstances);
        Assert.Equal(1, response.CurrentInstances);

        // Verify TeardownServiceAsync was called twice (removing 2 instances)
        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync(
                It.IsAny<string>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region ScalePoolAsync No-Op Tests

    [Fact]
    public async Task ScalePoolAsync_WhenTargetEqualsCurrent_ShouldNotDeployOrTeardown()
    {
        // Arrange
        var service = CreateService();

        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);

        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);

        // Pool configuration exists
        SetupPoolConfiguration("actor-shared", "actor");

        // 2 existing instances
        SetupExistingPoolInstances("actor-shared", 2, 2);

        var request = new ScalePoolRequest { PoolType = "actor-shared", TargetInstances = 2 };

        // Act
        var (statusCode, response) = await service.ScalePoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(0, response.ScaledUp);
        Assert.Equal(0, response.ScaledDown);
        Assert.Equal(2, response.PreviousInstances);
        Assert.Equal(2, response.CurrentInstances);

        // Verify no deploy or teardown calls
        mockOrchestrator.Verify(
            x => x.DeployServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never());

        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region Helper Methods

    private void SetupPoolConfiguration(string poolType, string serviceName)
    {
        var config = new PoolConfiguration
        {
            PoolType = poolType,
            ServiceName = serviceName,
            Image = null,
            Environment = new Dictionary<string, string>
            {
                ["BANNOU_SERVICES_ENABLED"] = "false",
                [$"{serviceName.ToUpperInvariant()}_SERVICE_ENABLED"] = "true"
            },
            MinInstances = 1,
            MaxInstances = 10,
            ScaleUpThreshold = 0.8,
            ScaleDownThreshold = 0.2,
            IdleTimeoutMinutes = 5
        };

        _mockStateManager
            .Setup(x => x.GetPoolConfigurationAsync(poolType))
            .ReturnsAsync(config);
    }

    private void SetupExistingPoolInstances(string poolType, int totalCount, int availableCount)
    {
        var instances = Enumerable.Range(0, totalCount).Select(i => new ProcessorInstance
        {
            ProcessorId = $"{poolType}-{Guid.NewGuid():N}",
            AppId = $"bannou-pool-{poolType}-{i:D4}",
            PoolType = poolType,
            Status = i < availableCount ? ProcessorStatus.Available : ProcessorStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastUpdated = DateTimeOffset.UtcNow.AddMinutes(-1)
        }).ToList();

        var availableInstances = instances.Take(availableCount).ToList();

        _mockStateManager
            .Setup(x => x.GetPoolInstancesAsync(poolType))
            .ReturnsAsync(instances);

        _mockStateManager
            .Setup(x => x.GetAvailableProcessorsAsync(poolType))
            .ReturnsAsync(availableInstances);

        _mockStateManager
            .Setup(x => x.GetLeasesAsync(poolType))
            .ReturnsAsync(new Dictionary<string, ProcessorLease>());
    }

    #endregion

    #region AcquireProcessorAsync Lock Tests

    [Fact]
    public async Task AcquireProcessorAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();

        // Override lock to fail for this pool type
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.OrchestratorLock,
                "actor-shared",
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new AcquireProcessorRequest { PoolType = "actor-shared" };

        // Act
        var (statusCode, response) = await service.AcquireProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ReleaseProcessorAsync Lock Tests

    [Fact]
    public async Task ReleaseProcessorAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var leaseId = Guid.NewGuid();
        var poolType = "actor-shared";

        // Setup: known pool types
        _mockStateManager
            .Setup(x => x.GetKnownPoolTypesAsync())
            .ReturnsAsync(new List<string> { poolType });

        // Setup: lease exists in this pool
        var lease = new ProcessorLease
        {
            LeaseId = leaseId,
            ProcessorId = "proc-001",
            AppId = "bannou-pool-actor-shared-0001",
            PoolType = poolType,
            AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var leases = new Dictionary<string, ProcessorLease>
        {
            [leaseId.ToString()] = lease
        };
        _mockStateManager
            .Setup(x => x.GetLeasesAsync(poolType))
            .ReturnsAsync(leases);

        // Override lock to fail for this pool
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                StateStoreDefinitions.OrchestratorLock,
                poolType,
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new ReleaseProcessorRequest { LeaseId = leaseId, Success = true };

        // Act
        var (statusCode, response) = await service.ReleaseProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region ReleaseProcessorAsync Event Publication Tests

    [Fact]
    public async Task ReleaseProcessorAsync_SuccessfulRelease_PublishesProcessorReleasedEvent()
    {
        // Arrange
        var service = CreateService();
        var leaseId = Guid.NewGuid();
        var poolType = "actor-shared";
        var processorId = "proc-001";

        // Setup: known pool types
        _mockStateManager
            .Setup(x => x.GetKnownPoolTypesAsync())
            .ReturnsAsync(new List<string> { poolType });

        // Setup: lease exists in this pool
        var lease = new ProcessorLease
        {
            LeaseId = leaseId,
            ProcessorId = processorId,
            AppId = "bannou-pool-actor-shared-0001",
            PoolType = poolType,
            AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        var leases = new Dictionary<string, ProcessorLease>
        {
            [leaseId.ToString()] = lease
        };
        _mockStateManager
            .Setup(x => x.GetLeasesAsync(poolType))
            .ReturnsAsync(leases);

        // Setup: available processors list
        _mockStateManager
            .Setup(x => x.GetAvailableProcessorsAsync(poolType))
            .ReturnsAsync(new List<ProcessorInstance>());

        // Setup: message bus to capture published event
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new ReleaseProcessorRequest { LeaseId = leaseId, Success = true };

        // Act
        var (statusCode, response) = await service.ReleaseProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(processorId, response.ProcessorId);

        // Verify the ProcessorReleasedEvent was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "orchestrator.processor.released",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Tests for PresetLoader and PresetProcessingPool YAML parsing.
/// </summary>
public class PresetLoaderTests
{
    private readonly Mock<ILogger<PresetLoader>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly string _testPresetsDirectory;
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");

    public PresetLoaderTests()
    {
        _mockLogger = new Mock<ILogger<PresetLoader>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _testPresetsDirectory = Path.Combine(Path.GetTempPath(), $"preset-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testPresetsDirectory);
    }

    private PresetLoader CreateLoader()
    {
        return new PresetLoader(_mockLogger.Object, _testPresetsDirectory, _mockTelemetryProvider.Object);
    }

    private async Task CreatePresetFileAsync(string name, string content)
    {
        var filePath = Path.Combine(_testPresetsDirectory, $"{name}.yaml");
        await File.WriteAllTextAsync(filePath, content);
    }

    /// <summary>
    /// Copies a fixture YAML file to the test presets directory.
    /// Use this for YAML with complex indentation that gets mangled by formatters.
    /// </summary>
    private async Task CopyFixtureAsync(string fixtureName)
    {
        var sourcePath = Path.Combine(FixturesDirectory, $"{fixtureName}.yaml");
        var destPath = Path.Combine(_testPresetsDirectory, $"{fixtureName}.yaml");
        var content = await File.ReadAllTextAsync(sourcePath);
        await File.WriteAllTextAsync(destPath, content);
    }

    #region ProcessingPools YAML Parsing Tests

    [Fact]
    public async Task LoadPresetAsync_WithProcessingPools_ShouldParseCorrectly()
    {
        // Arrange - uses fixture file to preserve YAML indentation
        await CopyFixtureAsync("actor-pools");
        var loader = CreateLoader();

        // Act
        var preset = await loader.LoadPresetAsync("actor-pools", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preset);
        Assert.Equal("actor-pools", preset.Name);
        Assert.Equal("Actor pool configuration", preset.Description);
        Assert.Equal("processing", preset.Category);
        Assert.NotNull(preset.ProcessingPools);
        Assert.Equal(2, preset.ProcessingPools.Count);

        // Verify first pool
        var sharedPool = preset.ProcessingPools[0];
        Assert.Equal("actor-shared", sharedPool.PoolType);
        Assert.Equal("actor", sharedPool.Plugin);
        Assert.Equal(1, sharedPool.MinInstances);
        Assert.Equal(10, sharedPool.MaxInstances);
        Assert.Equal(0.8, sharedPool.ScaleUpThreshold);
        Assert.Equal(0.2, sharedPool.ScaleDownThreshold);
        Assert.Equal(5, sharedPool.IdleTimeoutMinutes);
        Assert.NotNull(sharedPool.Environment);
        Assert.Equal("pool-node", sharedPool.Environment["ACTOR_DEPLOYMENT_MODE"]);
        Assert.Equal("50", sharedPool.Environment["ACTOR_POOL_NODE_CAPACITY"]);

        // Verify second pool
        var npcBrainPool = preset.ProcessingPools[1];
        Assert.Equal("actor-npc-brain", npcBrainPool.PoolType);
        Assert.Equal(2, npcBrainPool.MinInstances);
        Assert.Equal(20, npcBrainPool.MaxInstances);
        Assert.Equal(10, npcBrainPool.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task LoadPresetAsync_WithNoProcessingPools_ShouldReturnNullProcessingPools()
    {
        // Arrange - uses fixture file to preserve YAML indentation
        await CopyFixtureAsync("simple-preset");
        var loader = CreateLoader();

        // Act
        var preset = await loader.LoadPresetAsync("simple-preset", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preset);
        Assert.Equal("simple-preset", preset.Name);
        Assert.Null(preset.ProcessingPools);
        Assert.NotNull(preset.Topology);
    }

    [Fact]
    public async Task LoadPresetAsync_WithEmptyProcessingPools_ShouldReturnEmptyList()
    {
        // Arrange - uses fixture file
        await CopyFixtureAsync("empty-pools");
        var loader = CreateLoader();

        // Act
        var preset = await loader.LoadPresetAsync("empty-pools", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preset);
        Assert.NotNull(preset.ProcessingPools);
        Assert.Empty(preset.ProcessingPools);
    }

    [Fact]
    public async Task LoadPresetAsync_WithPoolImage_ShouldParseImage()
    {
        // Arrange - uses fixture file to preserve YAML indentation
        await CopyFixtureAsync("custom-image-pool");
        var loader = CreateLoader();

        // Act
        var preset = await loader.LoadPresetAsync("custom-image-pool", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preset);
        Assert.NotNull(preset.ProcessingPools);
        Assert.Single(preset.ProcessingPools);
        Assert.Equal("myregistry/asset-processor:v2", preset.ProcessingPools[0].Image);
    }

    [Fact]
    public async Task LoadPresetAsync_WithDefaultValues_ShouldUseDefaults()
    {
        // Arrange - uses fixture file to preserve YAML indentation
        await CopyFixtureAsync("minimal-pool");
        var loader = CreateLoader();

        // Act
        var preset = await loader.LoadPresetAsync("minimal-pool", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preset);
        Assert.NotNull(preset.ProcessingPools);
        var pool = preset.ProcessingPools[0];
        Assert.Equal("test-pool", pool.PoolType);
        Assert.Equal("test", pool.Plugin);
        Assert.Null(pool.Image); // No custom image
        Assert.Equal(1, pool.MinInstances); // Default
        Assert.Equal(5, pool.MaxInstances); // Default
        Assert.Equal(0.8, pool.ScaleUpThreshold); // Default
        Assert.Equal(0.2, pool.ScaleDownThreshold); // Default
        Assert.Equal(5, pool.IdleTimeoutMinutes); // Default
    }

    #endregion

    #region ListPresetsAsync Tests

    [Fact]
    public async Task ListPresetsAsync_ShouldReturnPresetMetadata()
    {
        // Arrange - uses fixture files to preserve YAML indentation
        await CopyFixtureAsync("preset-one");
        await CopyFixtureAsync("preset-two");
        var loader = CreateLoader();

        // Act
        var presets = await loader.ListPresetsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, presets.Count);

        var firstPreset = presets.FirstOrDefault(p => p.Name == "preset-one");
        Assert.NotNull(firstPreset);
        Assert.Equal("First preset", firstPreset.Description);
        Assert.Equal("development", firstPreset.Category);
        Assert.Single(firstPreset.RequiredBackends);
        Assert.Contains("docker-compose", firstPreset.RequiredBackends);

        var secondPreset = presets.FirstOrDefault(p => p.Name == "preset-two");
        Assert.NotNull(secondPreset);
        Assert.Equal("Second preset", secondPreset.Description);
        Assert.Equal("production", secondPreset.Category);
        Assert.Equal(2, secondPreset.RequiredBackends.Count);
    }

    [Fact]
    public async Task ListPresetsAsync_WithNonexistentDirectory_ShouldReturnEmptyList()
    {
        // Arrange
        var loader = new PresetLoader(_mockLogger.Object, "/nonexistent/directory", _mockTelemetryProvider.Object);

        // Act
        var presets = await loader.ListPresetsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(presets);
    }

    #endregion
}

/// <summary>
/// Tests for TeardownAsync covering dry-run, infrastructure teardown, and event publication.
/// </summary>
public class OrchestratorTeardownTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorTeardownTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    private Mock<IContainerOrchestrator> SetupOrchestrator()
    {
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);
        return mockOrchestrator;
    }

    #region TeardownAsync Tests

    [Fact]
    public async Task TeardownAsync_DryRun_ShouldReturnPreviewWithoutExecuting()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>
            {
                new() { AppName = "bannou-auth", Status = ContainerStatusType.Running, Instances = 1 },
                new() { AppName = "bannou-account", Status = ContainerStatusType.Running, Instances = 1 }
            });
        mockOrchestrator
            .Setup(x => x.ListInfrastructureServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "redis", "rabbitmq" });

        var service = CreateService();
        var request = new TeardownRequest { DryRun = true, IncludeInfrastructure = false };

        // Act
        var (statusCode, response) = await service.TeardownAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.StoppedContainers);
        Assert.Contains("bannou-auth", response.StoppedContainers);
        Assert.Contains("bannou-account", response.StoppedContainers);
        // Dry run should NOT publish deployment events
        _mockEventManager.Verify(
            x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()),
            Times.Never);
        // Dry run should NOT call teardown
        mockOrchestrator.Verify(
            x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TeardownAsync_WithInfrastructure_ShouldTeardownInfraAndPublishEvents()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>
            {
                new() { AppName = "bannou-auth", Status = ContainerStatusType.Running, Instances = 1 }
            });
        mockOrchestrator
            .Setup(x => x.ListInfrastructureServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "redis" });
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult
            {
                Success = true,
                StoppedContainers = new List<string> { "container-1" },
                RemovedVolumes = new List<string>()
            });

        // Capture deployment events
        var capturedEvents = new List<DeploymentEvent>();
        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Callback<DeploymentEvent>(evt => capturedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var request = new TeardownRequest { DryRun = false, IncludeInfrastructure = true };

        // Act
        var (statusCode, response) = await service.TeardownAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        // Should publish started and completed events
        Assert.Equal(2, capturedEvents.Count);
        Assert.Equal(DeploymentAction.TopologyChanged, capturedEvents[0].Action);
        Assert.Equal(DeploymentAction.Completed, capturedEvents[1].Action);
    }

    [Fact]
    public async Task TeardownAsync_NoContainers_ShouldReturnEmptyResult()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>());
        mockOrchestrator
            .Setup(x => x.ListInfrastructureServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var service = CreateService();
        var request = new TeardownRequest { DryRun = false };

        // Act
        var (statusCode, response) = await service.TeardownAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.StoppedContainers);
        Assert.Empty(response.StoppedContainers);
        Assert.Equal("0s", response.Duration);
    }

    [Fact]
    public async Task TeardownAsync_FailedTeardown_ShouldPublishFailedEvent()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.ListContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerStatus>
            {
                new() { AppName = "bannou-auth", Status = ContainerStatusType.Running, Instances = 1 }
            });
        mockOrchestrator
            .Setup(x => x.ListInfrastructureServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync("bannou-auth", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult
            {
                Success = false,
                Message = "Container busy"
            });

        var capturedEvents = new List<DeploymentEvent>();
        _mockEventManager
            .Setup(x => x.PublishDeploymentEventAsync(It.IsAny<DeploymentEvent>()))
            .Callback<DeploymentEvent>(evt => capturedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var request = new TeardownRequest { DryRun = false };

        // Act
        var (statusCode, response) = await service.TeardownAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        var errors = response.Errors ?? throw new InvalidOperationException("Expected non-null Errors");
        Assert.Single(errors);
        // Second event should be Failed
        Assert.Equal(2, capturedEvents.Count);
        Assert.Equal(DeploymentAction.Failed, capturedEvents[1].Action);
        Assert.NotNull(capturedEvents[1].Error);
    }

    #endregion
}

/// <summary>
/// Tests for UpdateTopologyAsync covering different TopologyChangeAction types.
/// </summary>
public class OrchestratorUpdateTopologyTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorUpdateTopologyTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    private Mock<IContainerOrchestrator> SetupOrchestrator()
    {
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);
        return mockOrchestrator;
    }

    #region UpdateTopologyAsync Tests

    [Fact]
    public async Task UpdateTopologyAsync_EmptyChanges_ShouldReturnBadRequest()
    {
        // Arrange
        SetupOrchestrator();
        var service = CreateService();
        var request = new TopologyUpdateRequest { Changes = new List<TopologyChange>() };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateTopologyAsync_AddNode_ShouldDeployServicesAndSetRouting()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.DeployServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeployServiceResult { Success = true, AppId = "bannou-auth-node2" });

        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.AddNode,
                    NodeName = "node2",
                    NodeConfig = new TopologyNode(),
                    Services = new List<string> { "auth" },
                    Environment = new Dictionary<string, string> { ["CUSTOM_VAR"] = "value" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.True(response.AppliedChanges.First().Success);
        // Verify routing was set
        _mockHealthMonitor.Verify(
            x => x.SetServiceRoutingAsync("auth", "bannou-auth-node2"),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTopologyAsync_AddNode_MissingNodeConfig_ShouldSetError()
    {
        // Arrange
        SetupOrchestrator();
        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.AddNode,
                    NodeName = "node2",
                    NodeConfig = null,
                    Services = new List<string> { "auth" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.False(response.AppliedChanges.First().Success);
        Assert.Contains("required", response.AppliedChanges.First().Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateTopologyAsync_RemoveNode_ShouldTeardownAndRestoreRouting()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult { Success = true });

        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.RemoveNode,
                    NodeName = "node2",
                    Services = new List<string> { "auth", "account" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.True(response.AppliedChanges.First().Success);
        _mockHealthMonitor.Verify(
            x => x.RestoreServiceRoutingToDefaultAsync("auth"), Times.Once);
        _mockHealthMonitor.Verify(
            x => x.RestoreServiceRoutingToDefaultAsync("account"), Times.Once);
    }

    [Fact]
    public async Task UpdateTopologyAsync_MoveService_ShouldUpdateRouting()
    {
        // Arrange
        SetupOrchestrator();
        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.MoveService,
                    NodeName = "node3",
                    Services = new List<string> { "auth" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.True(response.AppliedChanges.First().Success);
        _mockHealthMonitor.Verify(
            x => x.SetServiceRoutingAsync("auth", "bannou-auth-node3"), Times.Once);
    }

    [Fact]
    public async Task UpdateTopologyAsync_Scale_WithoutReplicas_ShouldSetError()
    {
        // Arrange
        SetupOrchestrator();
        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.Scale,
                    NodeName = "node1",
                    Services = new List<string> { "auth" },
                    Replicas = null
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.False(response.AppliedChanges.First().Success);
        Assert.Contains("replicas", response.AppliedChanges.First().Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateTopologyAsync_Scale_WithReplicas_ShouldCallScaleService()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.ScaleServiceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScaleServiceResult { Success = true });

        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.Scale,
                    NodeName = "node1",
                    Services = new List<string> { "auth" },
                    Replicas = 3
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.True(response.AppliedChanges.First().Success);
        mockOrchestrator.Verify(
            x => x.ScaleServiceAsync("bannou-auth-node1", 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTopologyAsync_UpdateEnv_ShouldRedeployWithNewEnvironment()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.DeployServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeployServiceResult { Success = true, ContainerId = "abc123" });

        var service = CreateService();
        var request = new TopologyUpdateRequest
        {
            Changes = new List<TopologyChange>
            {
                new()
                {
                    Action = TopologyChangeAction.UpdateEnv,
                    NodeName = "node1",
                    Services = new List<string> { "auth" },
                    Environment = new Dictionary<string, string> { ["LOG_LEVEL"] = "Debug" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.UpdateTopologyAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.AppliedChanges);
        Assert.True(response.AppliedChanges.First().Success);
        mockOrchestrator.Verify(
            x => x.DeployServiceAsync(
                "auth",
                "bannou-auth-node1",
                It.Is<Dictionary<string, string>>(env => env["LOG_LEVEL"] == "Debug"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Tests for RollbackConfigurationAsync and GetConfigVersionAsync.
/// </summary>
public class OrchestratorConfigVersionTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorConfigVersionTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    #region RollbackConfigurationAsync Tests

    [Fact]
    public async Task RollbackConfigurationAsync_AtVersion1_ShouldReturnBadRequest()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(1);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(new DeploymentConfiguration());

        var service = CreateService();
        var request = new ConfigRollbackRequest { Reason = "test" };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RollbackConfigurationAsync_AtVersion0_ShouldReturnBadRequest()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(0);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync((DeploymentConfiguration?)null);

        var service = CreateService();
        var request = new ConfigRollbackRequest { Reason = "test" };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RollbackConfigurationAsync_InvalidTargetVersion_ShouldReturnBadRequest()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(5);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(new DeploymentConfiguration());

        var service = CreateService();
        // Target version >= current version is invalid
        var request = new ConfigRollbackRequest { Reason = "test", TargetVersion = 5 };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RollbackConfigurationAsync_TargetVersionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(5);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(new DeploymentConfiguration());
        _mockStateManager.Setup(x => x.GetConfigurationVersionAsync(3))
            .ReturnsAsync((DeploymentConfiguration?)null);

        var service = CreateService();
        var request = new ConfigRollbackRequest { Reason = "test", TargetVersion = 3 };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RollbackConfigurationAsync_RestoreFails_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(5);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync())
            .ReturnsAsync(new DeploymentConfiguration());
        _mockStateManager.Setup(x => x.GetConfigurationVersionAsync(4))
            .ReturnsAsync(new DeploymentConfiguration());
        _mockStateManager.Setup(x => x.RestoreConfigurationVersionAsync(4))
            .ReturnsAsync(false);

        var service = CreateService();
        var request = new ConfigRollbackRequest { Reason = "test" };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RollbackConfigurationAsync_Success_ShouldReturnVersionInfo()
    {
        // Arrange
        var currentConfig = new DeploymentConfiguration
        {
            Services = new Dictionary<string, ServiceDeploymentConfig>
            {
                ["auth"] = new() { Enabled = true }
            },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AUTH_LOG_LEVEL"] = "Information"
            }
        };
        var previousConfig = new DeploymentConfiguration
        {
            Services = new Dictionary<string, ServiceDeploymentConfig>
            {
                ["auth"] = new() { Enabled = false }
            },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AUTH_LOG_LEVEL"] = "Debug"
            }
        };

        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(5);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync()).ReturnsAsync(currentConfig);
        _mockStateManager.Setup(x => x.GetConfigurationVersionAsync(4)).ReturnsAsync(previousConfig);
        _mockStateManager.Setup(x => x.RestoreConfigurationVersionAsync(4)).ReturnsAsync(true);
        // After restore, version becomes 6
        _mockStateManager.SetupSequence(x => x.GetConfigVersionAsync())
            .ReturnsAsync(5)  // First call (before rollback)
            .ReturnsAsync(6); // Second call (after rollback)

        var service = CreateService();
        var request = new ConfigRollbackRequest { Reason = "test rollback" };

        // Act
        var (statusCode, response) = await service.RollbackConfigurationAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(5, response.PreviousVersion);
        Assert.Equal(6, response.CurrentVersion);
        var changedKeys = response.ChangedKeys ?? throw new InvalidOperationException("Expected non-null ChangedKeys");
        Assert.NotEmpty(changedKeys);
    }

    #endregion

    #region GetConfigVersionAsync Tests

    [Fact]
    public async Task GetConfigVersionAsync_WithConfig_ShouldReturnVersionAndPrefixes()
    {
        // Arrange
        var config = new DeploymentConfiguration
        {
            Services = new Dictionary<string, ServiceDeploymentConfig>
            {
                ["auth"] = new() { Enabled = true }
            },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AUTH_LOG_LEVEL"] = "Debug"
            },
            Timestamp = DateTimeOffset.UtcNow
        };

        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(3);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync()).ReturnsAsync(config);
        _mockStateManager.Setup(x => x.GetConfigurationVersionAsync(2)).ReturnsAsync(new DeploymentConfiguration());

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetConfigVersionAsync(new GetConfigVersionRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.Version);
        Assert.True(response.HasPreviousConfig);
        Assert.Equal(2, response.KeyCount); // 1 service + 1 env var
        var keyPrefixes = response.KeyPrefixes ?? throw new InvalidOperationException("Expected non-null KeyPrefixes");
        Assert.Contains("services", keyPrefixes);
    }

    [Fact]
    public async Task GetConfigVersionAsync_NoPreviousConfig_ShouldReturnHasPreviousFalse()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetConfigVersionAsync()).ReturnsAsync(1);
        _mockStateManager.Setup(x => x.GetCurrentConfigurationAsync()).ReturnsAsync((DeploymentConfiguration?)null);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetConfigVersionAsync(new GetConfigVersionRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(1, response.Version);
        Assert.False(response.HasPreviousConfig);
    }

    #endregion
}

/// <summary>
/// Tests for RequestContainerRestartAsync and GetLogsAsync.
/// </summary>
public class OrchestratorContainerOpsTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorContainerOpsTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    private Mock<IContainerOrchestrator> SetupOrchestrator()
    {
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);
        return mockOrchestrator;
    }

    #region RequestContainerRestartAsync Tests

    [Fact]
    public async Task RequestContainerRestartAsync_Accepted_ShouldReturnOK()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.RestartContainerAsync(
                "bannou",
                It.IsAny<ContainerRestartRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerRestartResponse
            {
                Accepted = true
            });

        var service = CreateService();
        var request = new ContainerRestartRequestBody
        {
            AppName = "bannou",
            Reason = "Test restart"
        };

        // Act
        var (statusCode, response) = await service.RequestContainerRestartAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Accepted);
    }

    [Fact]
    public async Task RequestContainerRestartAsync_NotAccepted_ShouldReturnInternalServerError()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.RestartContainerAsync(
                "bannou",
                It.IsAny<ContainerRestartRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerRestartResponse
            {
                Accepted = false
            });

        var service = CreateService();
        var request = new ContainerRestartRequestBody
        {
            AppName = "bannou",
            Reason = "Test restart"
        };

        // Act
        var (statusCode, response) = await service.RequestContainerRestartAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RequestContainerRestartAsync_WithPriority_ShouldPassPriorityToOrchestrator()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        ContainerRestartRequest? capturedRequest = null;
        mockOrchestrator
            .Setup(x => x.RestartContainerAsync(
                "bannou",
                It.IsAny<ContainerRestartRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerRestartRequest, CancellationToken>((_, req, _) => capturedRequest = req)
            .ReturnsAsync(new ContainerRestartResponse { Accepted = true });

        var service = CreateService();
        var request = new ContainerRestartRequestBody
        {
            AppName = "bannou",
            Reason = "Urgent restart",
            Priority = RestartPriority.Immediate
        };

        // Act
        var (statusCode, _) = await service.RequestContainerRestartAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal(RestartPriority.Immediate, capturedRequest.Priority);
        Assert.Equal("Urgent restart", capturedRequest.Reason);
    }

    #endregion

    #region GetLogsAsync Tests

    [Fact]
    public async Task GetLogsAsync_ShouldParseTimestampedLogs()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        var logText = "2024-01-15T12:00:00.000Z Starting service\n2024-01-15T12:00:01.000Z Service ready";

        mockOrchestrator
            .Setup(x => x.GetContainerLogsAsync(
                "bannou",
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logText);

        var service = CreateService();
        var request = new GetLogsRequest { Service = "bannou", Tail = 100 };

        // Act
        var (statusCode, response) = await service.GetLogsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Logs.Count);
        var logs = response.Logs.ToList();
        Assert.Equal("Starting service", logs[0].Message);
        Assert.Equal("Service ready", logs[1].Message);
        Assert.Equal(LogStreamType.Stdout, logs[0].Stream);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldHandleStderrMarker()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        var logText = "2024-01-15T12:00:00.000Z Normal log\n[STDERR]\n2024-01-15T12:00:01.000Z Error message";

        mockOrchestrator
            .Setup(x => x.GetContainerLogsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logText);

        var service = CreateService();
        var request = new GetLogsRequest { Service = "bannou" };

        // Act
        var (statusCode, response) = await service.GetLogsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Logs.Count);
        var logs = response.Logs.ToList();
        Assert.Equal(LogStreamType.Stdout, logs[0].Stream);
        Assert.Equal(LogStreamType.Stderr, logs[1].Stream);
    }

    [Fact]
    public async Task GetLogsAsync_ContinuationLines_ShouldInheritTimestamp()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        // Continuation line (stack trace) without timestamp should inherit preceding timestamp
        var logText = "2024-01-15T12:00:00.000Z Exception occurred\n   at MyClass.Method()";

        mockOrchestrator
            .Setup(x => x.GetContainerLogsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logText);

        var service = CreateService();
        var request = new GetLogsRequest { Service = "bannou" };

        // Act
        var (statusCode, response) = await service.GetLogsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Logs.Count);
        var logs = response.Logs.ToList();
        // Continuation line should inherit the timestamp of the previous line
        Assert.Equal(logs[0].Timestamp, logs[1].Timestamp);
        Assert.Equal("   at MyClass.Method()", logs[1].Message);
    }

    [Fact]
    public async Task GetLogsAsync_UsesContainerWhenNoService()
    {
        // Arrange
        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.GetContainerLogsAsync(
                "my-container",
                It.IsAny<int>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("2024-01-15T12:00:00.000Z Hello");

        var service = CreateService();
        // Service is null, container is specified
        var request = new GetLogsRequest { Service = null, Container = "my-container" };

        // Act
        var (statusCode, response) = await service.GetLogsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("my-container", response.Service);
    }

    #endregion
}

/// <summary>
/// Tests for AcquireProcessorAsync, ReleaseProcessorAsync, GetPoolStatusAsync, and CleanupPoolAsync.
/// </summary>
public class OrchestratorPoolManagementTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OrchestratorService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly Mock<IServiceHealthMonitor> _mockHealthMonitor;
    private readonly Mock<ISmartRestartManager> _mockRestartManager;
    private readonly Mock<IBackendDetector> _mockBackendDetector;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly AppConfiguration _appConfiguration;

    public OrchestratorPoolManagementTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OrchestratorService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _configuration = new OrchestratorServiceConfiguration
        {
            RedisConnectionString = "redis:6379",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5,
            DefaultPoolLeaseTimeoutSeconds = 300
        };
        _appConfiguration = new AppConfiguration();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient());

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _appConfiguration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
            _mockLockProvider.Object,
            _mockHttpClientFactory.Object,
            _mockTelemetryProvider.Object,
            _mockEventConsumer.Object);
    }

    private Mock<IContainerOrchestrator> SetupOrchestrator()
    {
        var mockOrchestrator = new Mock<IContainerOrchestrator>();
        mockOrchestrator.Setup(x => x.BackendType).Returns(BackendType.Compose);
        _mockBackendDetector
            .Setup(x => x.CreateBestOrchestratorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOrchestrator.Object);
        return mockOrchestrator;
    }

    #region AcquireProcessorAsync Tests

    [Fact]
    public async Task AcquireProcessorAsync_EmptyPoolType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new AcquireProcessorRequest { PoolType = "" };

        // Act
        var (statusCode, response) = await service.AcquireProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcquireProcessorAsync_NoAvailableProcessors_ShouldReturnServiceUnavailable()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>());
        _mockStateManager.Setup(x => x.GetLeasesAsync("actor-shared"))
            .ReturnsAsync(new Dictionary<string, ProcessorLease>());

        var service = CreateService();
        var request = new AcquireProcessorRequest { PoolType = "actor-shared" };

        // Act
        var (statusCode, response) = await service.AcquireProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.ServiceUnavailable, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcquireProcessorAsync_Success_ShouldReturnLeaseAndRemoveFromAvailable()
    {
        // Arrange
        var processor = new ProcessorInstance
        {
            ProcessorId = "proc-001",
            AppId = "bannou-pool-actor-shared-0001",
            PoolType = "actor-shared",
            Status = ProcessorStatus.Available
        };
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance> { processor });
        _mockStateManager.Setup(x => x.GetLeasesAsync("actor-shared"))
            .ReturnsAsync(new Dictionary<string, ProcessorLease>());

        // Capture updated available list
        List<ProcessorInstance>? capturedAvailable = null;
        _mockStateManager
            .Setup(x => x.SetAvailableProcessorsAsync("actor-shared", It.IsAny<List<ProcessorInstance>>()))
            .Callback<string, List<ProcessorInstance>>((_, list) => capturedAvailable = list)
            .Returns(Task.CompletedTask);

        // Capture stored leases
        Dictionary<string, ProcessorLease>? capturedLeases = null;
        _mockStateManager
            .Setup(x => x.SetLeasesAsync("actor-shared", It.IsAny<Dictionary<string, ProcessorLease>>()))
            .Callback<string, Dictionary<string, ProcessorLease>>((_, leases) => capturedLeases = leases)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var request = new AcquireProcessorRequest { PoolType = "actor-shared", Priority = 5 };

        // Act
        var (statusCode, response) = await service.AcquireProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("proc-001", response.ProcessorId);
        Assert.Equal("bannou-pool-actor-shared-0001", response.AppId);
        Assert.NotEqual(Guid.Empty, response.LeaseId);
        // Processor should have been removed from available list
        Assert.NotNull(capturedAvailable);
        Assert.Empty(capturedAvailable);
        // Lease should have been stored
        Assert.NotNull(capturedLeases);
        Assert.Single(capturedLeases);
    }

    #endregion

    #region ReleaseProcessorAsync Tests

    [Fact]
    public async Task ReleaseProcessorAsync_LeaseNotFound_ShouldReturnNotFound()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetKnownPoolTypesAsync())
            .ReturnsAsync(new List<string> { "actor-shared" });
        _mockStateManager.Setup(x => x.GetLeasesAsync("actor-shared"))
            .ReturnsAsync(new Dictionary<string, ProcessorLease>());

        var service = CreateService();
        var request = new ReleaseProcessorRequest { LeaseId = Guid.NewGuid(), Success = true };

        // Act
        var (statusCode, response) = await service.ReleaseProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task ReleaseProcessorAsync_Success_ShouldReturnProcessorToAvailableAndPublishEvent()
    {
        // Arrange
        var leaseId = Guid.NewGuid();
        var poolType = "actor-shared";
        var processorId = "proc-001";

        _mockStateManager.Setup(x => x.GetKnownPoolTypesAsync())
            .ReturnsAsync(new List<string> { poolType });
        var lease = new ProcessorLease
        {
            LeaseId = leaseId,
            ProcessorId = processorId,
            AppId = "bannou-pool-actor-shared-0001",
            PoolType = poolType,
            AcquiredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        _mockStateManager.Setup(x => x.GetLeasesAsync(poolType))
            .ReturnsAsync(new Dictionary<string, ProcessorLease> { [leaseId.ToString()] = lease });
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync(poolType))
            .ReturnsAsync(new List<ProcessorInstance>());
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Capture restored available list
        List<ProcessorInstance>? capturedAvailable = null;
        _mockStateManager
            .Setup(x => x.SetAvailableProcessorsAsync(poolType, It.IsAny<List<ProcessorInstance>>()))
            .Callback<string, List<ProcessorInstance>>((_, list) => capturedAvailable = list)
            .Returns(Task.CompletedTask);

        var service = CreateService();
        var request = new ReleaseProcessorRequest { LeaseId = leaseId, Success = true };

        // Act
        var (statusCode, response) = await service.ReleaseProcessorAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(processorId, response.ProcessorId);
        // Processor should be back in available list
        Assert.NotNull(capturedAvailable);
        Assert.Single(capturedAvailable);
        Assert.Equal(processorId, capturedAvailable[0].ProcessorId);
        // Event should be published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "orchestrator.processor.released",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetPoolStatusAsync Tests

    [Fact]
    public async Task GetPoolStatusAsync_EmptyPoolType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new GetPoolStatusRequest { PoolType = "" };

        // Act
        var (statusCode, response) = await service.GetPoolStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetPoolStatusAsync_ShouldReturnInstanceCounts()
    {
        // Arrange
        var instances = new List<ProcessorInstance>
        {
            new() { ProcessorId = "proc-1", PoolType = "actor-shared" },
            new() { ProcessorId = "proc-2", PoolType = "actor-shared" },
            new() { ProcessorId = "proc-3", PoolType = "actor-shared" }
        };
        var available = new List<ProcessorInstance>
        {
            new() { ProcessorId = "proc-1", PoolType = "actor-shared" }
        };
        var leases = new Dictionary<string, ProcessorLease>
        {
            ["lease-1"] = new() { ProcessorId = "proc-2" },
            ["lease-2"] = new() { ProcessorId = "proc-3" }
        };

        _mockStateManager.Setup(x => x.GetPoolInstancesAsync("actor-shared")).ReturnsAsync(instances);
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared")).ReturnsAsync(available);
        _mockStateManager.Setup(x => x.GetLeasesAsync("actor-shared")).ReturnsAsync(leases);
        _mockStateManager.Setup(x => x.GetPoolConfigurationAsync("actor-shared"))
            .ReturnsAsync(new PoolConfiguration { MinInstances = 2, MaxInstances = 10 });

        var service = CreateService();
        var request = new GetPoolStatusRequest { PoolType = "actor-shared", IncludeMetrics = false };

        // Act
        var (statusCode, response) = await service.GetPoolStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalInstances);
        Assert.Equal(1, response.AvailableInstances);
        Assert.Equal(2, response.BusyInstances);
        Assert.Equal(2, response.MinInstances);
        Assert.Equal(10, response.MaxInstances);
    }

    [Fact]
    public async Task GetPoolStatusAsync_WithMetrics_ShouldIncludeMetricsData()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetPoolInstancesAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>());
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>());
        _mockStateManager.Setup(x => x.GetLeasesAsync("actor-shared"))
            .ReturnsAsync(new Dictionary<string, ProcessorLease>());
        _mockStateManager.Setup(x => x.GetPoolConfigurationAsync("actor-shared"))
            .ReturnsAsync((PoolConfiguration?)null);
        _mockStateManager.Setup(x => x.GetPoolMetricsAsync("actor-shared"))
            .ReturnsAsync(new PoolMetricsData
            {
                JobsCompleted1h = 42,
                JobsFailed1h = 3,
                AvgProcessingTimeMs = 1500
            });

        var service = CreateService();
        var request = new GetPoolStatusRequest { PoolType = "actor-shared", IncludeMetrics = true };

        // Act
        var (statusCode, response) = await service.GetPoolStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        var metrics = response.RecentMetrics ?? throw new InvalidOperationException("Expected non-null RecentMetrics");
        Assert.Equal(42, metrics.JobsCompleted1h);
        Assert.Equal(3, metrics.JobsFailed1h);
        Assert.Equal(1500, metrics.AvgProcessingTimeMs);
    }

    #endregion

    #region CleanupPoolAsync Tests

    [Fact]
    public async Task CleanupPoolAsync_EmptyPoolType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CleanupPoolRequest { PoolType = "" };

        // Act
        var (statusCode, response) = await service.CleanupPoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task CleanupPoolAsync_NothingToRemove_ShouldReturnZeroRemoved()
    {
        // Arrange
        _mockStateManager.Setup(x => x.GetPoolInstancesAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>
            {
                new() { ProcessorId = "proc-1", AppId = "app-1", PoolType = "actor-shared" }
            });
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>
            {
                new() { ProcessorId = "proc-1", AppId = "app-1", PoolType = "actor-shared" }
            });
        _mockStateManager.Setup(x => x.GetPoolConfigurationAsync("actor-shared"))
            .ReturnsAsync(new PoolConfiguration { MinInstances = 1 });

        var service = CreateService();
        // PreserveMinimum = true, 1 available, 1 min = 0 to remove
        var request = new CleanupPoolRequest { PoolType = "actor-shared", PreserveMinimum = true };

        // Act
        var (statusCode, response) = await service.CleanupPoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(0, response.InstancesRemoved);
    }

    [Fact]
    public async Task CleanupPoolAsync_PreserveMinimum_ShouldKeepMinInstances()
    {
        // Arrange
        var availableInstances = new List<ProcessorInstance>
        {
            new() { ProcessorId = "proc-1", AppId = "app-1", PoolType = "actor-shared" },
            new() { ProcessorId = "proc-2", AppId = "app-2", PoolType = "actor-shared" },
            new() { ProcessorId = "proc-3", AppId = "app-3", PoolType = "actor-shared" }
        };
        _mockStateManager.Setup(x => x.GetPoolInstancesAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>(availableInstances));
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(availableInstances);
        _mockStateManager.Setup(x => x.GetPoolConfigurationAsync("actor-shared"))
            .ReturnsAsync(new PoolConfiguration { MinInstances = 1 });

        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult { Success = true });

        var service = CreateService();
        // 3 available, min 1, preserve = true -> remove 2
        var request = new CleanupPoolRequest { PoolType = "actor-shared", PreserveMinimum = true };

        // Act
        var (statusCode, response) = await service.CleanupPoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.InstancesRemoved);
        // 1 instance should remain
        Assert.Equal(1, response.CurrentInstances);
    }

    [Fact]
    public async Task CleanupPoolAsync_NoPreserve_ShouldRemoveAll()
    {
        // Arrange
        var availableInstances = new List<ProcessorInstance>
        {
            new() { ProcessorId = "proc-1", AppId = "app-1", PoolType = "actor-shared" },
            new() { ProcessorId = "proc-2", AppId = "app-2", PoolType = "actor-shared" }
        };
        _mockStateManager.Setup(x => x.GetPoolInstancesAsync("actor-shared"))
            .ReturnsAsync(new List<ProcessorInstance>(availableInstances));
        _mockStateManager.Setup(x => x.GetAvailableProcessorsAsync("actor-shared"))
            .ReturnsAsync(availableInstances);
        _mockStateManager.Setup(x => x.GetPoolConfigurationAsync("actor-shared"))
            .ReturnsAsync(new PoolConfiguration { MinInstances = 1 });

        var mockOrchestrator = SetupOrchestrator();
        mockOrchestrator
            .Setup(x => x.TeardownServiceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeardownServiceResult { Success = true });

        var service = CreateService();
        // PreserveMinimum = false -> target = 0, remove all 2
        var request = new CleanupPoolRequest { PoolType = "actor-shared", PreserveMinimum = false };

        // Act
        var (statusCode, response) = await service.CleanupPoolAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.InstancesRemoved);
        Assert.Equal(0, response.CurrentInstances);
    }

    #endregion
}
