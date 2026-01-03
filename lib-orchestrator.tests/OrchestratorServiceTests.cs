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
    private readonly Mock<IEventConsumer> _mockEventConsumer;

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
            RabbitMqConnectionString = "amqp://guest:guest@rabbitmq:5672",
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };
        _mockStateManager = new Mock<IOrchestratorStateManager>();
        _mockEventManager = new Mock<IOrchestratorEventManager>();
        _mockHealthMonitor = new Mock<IServiceHealthMonitor>();
        _mockRestartManager = new Mock<ISmartRestartManager>();
        _mockBackendDetector = new Mock<IBackendDetector>();
        _mockEventConsumer = new Mock<IEventConsumer>();
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
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
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void OrchestratorService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<OrchestratorService>();

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
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), CancellationToken.None);

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
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Message bus unavailable"));

        var service = CreateService();

        // Act
        var (statusCode, response) = await service.GetInfrastructureHealthAsync(new InfrastructureHealthRequest(), CancellationToken.None);

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
        _mockMessageBus
            .Setup(x => x.TryPublishAsync(
                "orchestrator-health",
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Message bus unavailable"));

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

        _mockStateManager
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
        _mockStateManager
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

        _mockStateManager
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
    public async Task GetServiceRoutingAsync_WhenStateStoreThrows_ShouldReturnInternalServerError()
    {
        // Arrange
        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ThrowsAsync(new InvalidOperationException("State store connection failed"));

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
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly OrchestratorServiceConfiguration _configuration;

    public ServiceHealthMonitorTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
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
            _mockStateManager.Object,
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
        var heartbeats = new List<ServiceHealthStatus>
        {
            new() { ServiceId = "service1", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service2", Status = "unavailable", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service3", Status = "healthy", LastSeen = DateTimeOffset.UtcNow },
            new() { ServiceId = "service4", Status = "shutting_down", LastSeen = DateTimeOffset.UtcNow }
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

        _mockStateManager
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

        _mockStateManager
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
    private readonly Mock<IOrchestratorStateManager> _mockStateManager;
    private readonly Mock<IOrchestratorEventManager> _mockEventManager;
    private readonly OrchestratorServiceConfiguration _configuration;

    // Event handler captured from the mock
    private Action<ServiceHeartbeatEvent>? _heartbeatHandler;

    public ServiceHealthMonitorRoutingProtectionTests()
    {
        _mockLogger = new Mock<ILogger<ServiceHealthMonitor>>();
        _mockStateManager = new Mock<IOrchestratorStateManager>();
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
        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        return new ServiceHealthMonitor(
            _mockLogger.Object,
            _configuration,
            _mockStateManager.Object,
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
            Status = ServiceHeartbeatEventStatus.Healthy,
            Services = new List<ServiceStatus>
            {
                new() { ServiceName = "auth", Status = ServiceStatusStatus.Healthy }
            }
        };

        // Reset the mock to track new calls
        _mockStateManager.Invocations.Clear();

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100);

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
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => lastCapturedRouting = routing)
            .Returns(Task.CompletedTask);

        _mockStateManager
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

        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.RemoveServiceRoutingAsync("auth"))
            .Returns(Task.CompletedTask);

        // First, set a routing
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");

        // Act - remove the routing
        await monitor.RemoveServiceRoutingAsync("auth");

        // Assert - RemoveServiceRoutingAsync was called on redis
        _mockStateManager.Verify(
            x => x.RemoveServiceRoutingAsync("auth"),
            Times.Once(),
            "RemoveServiceRoutingAsync should be called on redis manager");
    }

    [Fact]
    public async Task ResetAllMappingsToDefaultAsync_ShouldClearAllRoutingsAndPublishMappings()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockStateManager
            .Setup(x => x.ClearAllServiceRoutingsAsync())
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        _mockEventManager
            .Setup(x => x.PublishFullMappingsAsync(It.IsAny<FullServiceMappingsEvent>()))
            .Returns(Task.CompletedTask);

        // Act
        await monitor.ResetAllMappingsToDefaultAsync();

        // Assert
        _mockStateManager.Verify(x => x.ClearAllServiceRoutingsAsync(), Times.Once(),
            "ClearAllServiceRoutingsAsync should be called to clear Redis routing keys");

        _mockEventManager.Verify(
            x => x.PublishFullMappingsAsync(It.Is<FullServiceMappingsEvent>(e =>
                e.DefaultAppId == "bannou" && e.TotalServices == 0)),
            Times.Once(),
            "Should publish full mappings event with empty mappings");
    }

    [Fact]
    public async Task ResetAllMappingsToDefaultAsync_ShouldClearInMemoryCache()
    {
        // Arrange
        var monitor = CreateMonitorWithEventCapture();

        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync(It.IsAny<string>(), It.IsAny<ServiceRouting>()))
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.ClearAllServiceRoutingsAsync())
            .Returns(Task.CompletedTask);

        _mockStateManager
            .Setup(x => x.GetServiceRoutingsAsync())
            .ReturnsAsync(new Dictionary<string, ServiceRouting>());

        _mockEventManager
            .Setup(x => x.PublishFullMappingsAsync(It.IsAny<FullServiceMappingsEvent>()))
            .Returns(Task.CompletedTask);

        // First, set some routings
        await monitor.SetServiceRoutingAsync("auth", "bannou-auth");
        await monitor.SetServiceRoutingAsync("accounts", "bannou-accounts");

        // Act
        await monitor.ResetAllMappingsToDefaultAsync();

        // Now simulate a heartbeat - it should be able to initialize routing again
        // (because in-memory cache was cleared)
        _mockStateManager
            .Setup(x => x.WriteServiceHeartbeatAsync(It.IsAny<ServiceHeartbeatEvent>()))
            .Returns(Task.CompletedTask);

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
        _mockStateManager
            .Setup(x => x.WriteServiceRoutingAsync("auth", It.IsAny<ServiceRouting>()))
            .Callback<string, ServiceRouting>((name, routing) => capturedRouting = routing)
            .Returns(Task.CompletedTask);

        _heartbeatHandler?.Invoke(heartbeat);
        await Task.Delay(100);

        // Assert - heartbeat should initialize routing (cache was cleared)
        Assert.NotNull(capturedRouting);
        Assert.Equal("bannou", capturedRouting.AppId);
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
    public void OrchestratorStateManager_Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new OrchestratorStateManager(
            null!,
            Mock.Of<ILogger<OrchestratorStateManager>>()));
        Assert.Equal("stateStoreFactory", ex.ParamName);
    }

    [Fact]
    public void OrchestratorStateManager_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void OrchestratorStateManager_Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task OrchestratorStateManager_CheckHealthAsync_WhenNotInitialized_ShouldReturnNotHealthy()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

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
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act
        var result = await manager.GetConfigVersionAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task OrchestratorStateManager_GetServiceHeartbeatsAsync_WhenNotInitialized_ShouldReturnEmptyList()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act
        var result = await manager.GetServiceHeartbeatsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task OrchestratorStateManager_GetServiceRoutingsAsync_WhenNotInitialized_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act
        var result = await manager.GetServiceRoutingsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task OrchestratorStateManager_WriteServiceHeartbeatAsync_WhenNotInitialized_ShouldNotThrow()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());
        var heartbeat = new ServiceHeartbeatEvent
        {
            ServiceId = Guid.NewGuid(),
            AppId = "test-app",
            Status = ServiceHeartbeatEventStatus.Healthy
        };

        // Act & Assert - should log warning but not throw
        var exception = await Record.ExceptionAsync(() => manager.WriteServiceHeartbeatAsync(heartbeat));
        Assert.Null(exception);
    }

    [Fact]
    public async Task OrchestratorStateManager_WriteServiceRoutingAsync_WhenNotInitialized_ShouldNotThrow()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());
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
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act
        var result = await manager.RestoreConfigurationVersionAsync(1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OrchestratorStateManager_Dispose_ShouldNotThrow()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => manager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task OrchestratorStateManager_DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var manager = new OrchestratorStateManager(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<ILogger<OrchestratorStateManager>>());

        // Act & Assert - Should not throw
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
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly OrchestratorServiceConfiguration _configuration;

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
        _mockEventConsumer = new Mock<IEventConsumer>();
        _configuration = new OrchestratorServiceConfiguration
        {
            HeartbeatTimeoutSeconds = 90,
            DegradationThresholdMinutes = 5
        };

        // Setup logger factory
        _mockLoggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());
    }

    private OrchestratorService CreateService()
    {
        return new OrchestratorService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _configuration,
            _mockStateManager.Object,
            _mockEventManager.Object,
            _mockHealthMonitor.Object,
            _mockRestartManager.Object,
            _mockBackendDetector.Object,
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
            Preset = preset ?? string.Empty
        };

        // Act
        var (statusCode, response) = await service.DeployAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("default", response.Preset);
        Assert.Contains("default topology", response.Message, StringComparison.OrdinalIgnoreCase);

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
                ["permissions"] = new() { Enabled = true, AppId = "bannou-auth" }
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
        var (statusCode, response) = await service.DeployAsync(request, CancellationToken.None);

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
            .ReturnsAsync(new List<ServiceHealthStatus>
            {
                new() { AppId = "bannou-auth", ServiceId = "auth", Status = "healthy", LastSeen = DateTimeOffset.UtcNow }
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
        var (statusCode, response) = await service.DeployAsync(request, CancellationToken.None);

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
                ["accounts"] = new() { Enabled = true, AppId = "bannou" }
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
        var (statusCode, response) = await service.DeployAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Contains("'bannou'", response.Message);

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
