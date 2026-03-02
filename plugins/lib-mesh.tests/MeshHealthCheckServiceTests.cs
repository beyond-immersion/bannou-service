using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeyondImmersion.BannouService.Tests.Mesh;

/// <summary>
/// Tests for MeshHealthCheckService failure counting, threshold-based deregistration,
/// and health check event behavior. Uses real HTTP calls to unreachable endpoints
/// (instant connection refused) to trigger the failure handling paths.
/// </summary>
public class MeshHealthCheckServiceTests : IDisposable
{
    private readonly Mock<IMeshStateManager> _mockStateManager;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly ILogger<MeshHealthCheckService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private ServiceProvider? _serviceProvider;
    private MeshHealthCheckService? _service;

    public MeshHealthCheckServiceTests()
    {
        _mockStateManager = new Mock<IMeshStateManager>();
        _mockMessageBus = new Mock<IMessageBus>();
        _logger = NullLoggerFactory.Instance.CreateLogger<MeshHealthCheckService>();
        _telemetryProvider = new NullTelemetryProvider();

        // Default setups for TryPublishAsync overloads used by the health check service
        _mockMessageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<MeshEndpointDeregisteredEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockMessageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<MeshEndpointHealthCheckFailedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockMessageBus.Setup(x => x.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Creates a MeshHealthCheckService with the specified configuration and a real DI container
    /// containing mocked services. Uses zero startup delay and very long interval to ensure
    /// only one probe cycle runs during each test.
    /// </summary>
    private MeshHealthCheckService CreateService(
        bool healthCheckEnabled = true,
        int startupDelaySeconds = 0,
        int intervalSeconds = 999,
        int timeoutSeconds = 2,
        int failureThreshold = 3,
        int deduplicationWindowSeconds = 60)
    {
        var config = new MeshServiceConfiguration
        {
            HealthCheckEnabled = healthCheckEnabled,
            HealthCheckStartupDelaySeconds = startupDelaySeconds,
            HealthCheckIntervalSeconds = intervalSeconds,
            HealthCheckTimeoutSeconds = timeoutSeconds,
            HealthCheckFailureThreshold = failureThreshold,
            HealthCheckEventDeduplicationWindowSeconds = deduplicationWindowSeconds,
        };

        var services = new ServiceCollection();
        services.AddSingleton(_mockStateManager.Object);
        services.AddSingleton(_mockMessageBus.Object);
        _serviceProvider = services.BuildServiceProvider();

        _service = new MeshHealthCheckService(
            _serviceProvider,
            _logger,
            config,
            _telemetryProvider);

        return _service;
    }

    /// <summary>
    /// Creates a test MeshEndpoint at a port that will cause instant connection refused.
    /// Port 19999 is chosen as an unlikely-to-be-listening port on localhost.
    /// </summary>
    private static MeshEndpoint CreateEndpoint(
        Guid? instanceId = null,
        string appId = "test-app",
        string host = "localhost",
        int port = 19999,
        EndpointStatus status = EndpointStatus.Healthy)
    {
        return new MeshEndpoint
        {
            InstanceId = instanceId ?? Guid.NewGuid(),
            AppId = appId,
            Host = host,
            Port = port,
            Status = status,
            LoadPercent = 10f,
            CurrentConnections = 5,
        };
    }

    public void Dispose()
    {
        _service?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region HealthCheck Disabled

    /// <summary>
    /// Verifies that when HealthCheckEnabled is false, the service exits immediately
    /// without probing any endpoints.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_HealthCheckDisabled_DoesNotProbe()
    {
        // Arrange
        var service = CreateService(healthCheckEnabled: false);

        // Act
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        // Assert - GetAllEndpointsAsync should never be called
        _mockStateManager.Verify(
            s => s.GetAllEndpointsAsync(It.IsAny<string?>()),
            Times.Never);
    }

    #endregion

    #region ShuttingDown Skip

    /// <summary>
    /// Verifies that endpoints with ShuttingDown status are skipped during probing.
    /// Uses a second endpoint to provide a deterministic completion signal.
    /// </summary>
    [Fact]
    public async Task ProbeEndpoint_ShuttingDown_SkippedNoUpdates()
    {
        // Arrange - two endpoints: one ShuttingDown (should be skipped), one Healthy (will fail probe)
        var shuttingDownEndpoint = CreateEndpoint(status: EndpointStatus.ShuttingDown);
        var healthyEndpoint = CreateEndpoint();
        var probeDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint> { shuttingDownEndpoint, healthyEndpoint });

        _mockStateManager.Setup(s => s.UpdateHeartbeatAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => probeDone.TrySetResult(true));

        var service = CreateService(failureThreshold: 99);

        // Act
        await service.StartAsync(CancellationToken.None);
        await probeDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert - healthy endpoint was probed (failed and marked unavailable)
        _mockStateManager.Verify(
            s => s.UpdateHeartbeatAsync(
                healthyEndpoint.InstanceId, healthyEndpoint.AppId,
                EndpointStatus.Unavailable, It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()),
            Times.Once);

        // ShuttingDown endpoint should have NO state updates
        _mockStateManager.Verify(
            s => s.UpdateHeartbeatAsync(
                shuttingDownEndpoint.InstanceId, It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()),
            Times.Never);
        _mockStateManager.Verify(
            s => s.DeregisterEndpointAsync(shuttingDownEndpoint.InstanceId, It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region Failure Counting and Deregistration

    /// <summary>
    /// Verifies that a single failure below the threshold marks the endpoint as Unavailable
    /// without deregistering it.
    /// </summary>
    [Fact]
    public async Task HandleFailedProbe_BelowThreshold_MarksUnavailable()
    {
        // Arrange - threshold=99, so 1 failure won't trigger deregistration
        var endpoint = CreateEndpoint();
        var heartbeatDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint });

        _mockStateManager.Setup(s => s.UpdateHeartbeatAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => heartbeatDone.TrySetResult(true));

        var service = CreateService(failureThreshold: 99);

        // Act
        await service.StartAsync(CancellationToken.None);
        await heartbeatDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert - marked as Unavailable, not deregistered
        _mockStateManager.Verify(
            s => s.UpdateHeartbeatAsync(
                endpoint.InstanceId, endpoint.AppId,
                EndpointStatus.Unavailable, endpoint.LoadPercent,
                endpoint.CurrentConnections, endpoint.Issues, It.IsAny<int>()),
            Times.Once);
        _mockStateManager.Verify(
            s => s.DeregisterEndpointAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when the failure count reaches the threshold, the endpoint is deregistered
    /// and a deregistration event is published with HealthCheckFailed reason.
    /// </summary>
    [Fact]
    public async Task HandleFailedProbe_AtThreshold_Deregisters()
    {
        // Arrange - threshold=1, first failure triggers deregistration
        var endpoint = CreateEndpoint();
        var deregisterDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint });

        _mockStateManager.Setup(s => s.DeregisterEndpointAsync(
                endpoint.InstanceId, endpoint.AppId))
            .ReturnsAsync(true)
            .Callback(() => deregisterDone.TrySetResult(true));

        var service = CreateService(failureThreshold: 1);

        // Act
        await service.StartAsync(CancellationToken.None);
        await deregisterDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert - deregistered
        _mockStateManager.Verify(
            s => s.DeregisterEndpointAsync(endpoint.InstanceId, endpoint.AppId),
            Times.Once);

        // Assert - deregistration event published with correct reason
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "mesh.endpoint.deregistered",
                It.Is<MeshEndpointDeregisteredEvent>(e =>
                    e.InstanceId == endpoint.InstanceId &&
                    e.AppId == endpoint.AppId &&
                    e.Reason == DeregistrationReason.HealthCheckFailed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when HealthCheckFailureThreshold is 0, deregistration is disabled.
    /// Endpoints are marked Unavailable but never deregistered regardless of failure count.
    /// </summary>
    [Fact]
    public async Task HandleFailedProbe_ThresholdZero_NeverDeregisters()
    {
        // Arrange - threshold=0 disables deregistration
        var endpoint = CreateEndpoint();
        var heartbeatDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint });

        _mockStateManager.Setup(s => s.UpdateHeartbeatAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => heartbeatDone.TrySetResult(true));

        var service = CreateService(failureThreshold: 0);

        // Act
        await service.StartAsync(CancellationToken.None);
        await heartbeatDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert - marked unavailable but NOT deregistered
        _mockStateManager.Verify(
            s => s.UpdateHeartbeatAsync(
                endpoint.InstanceId, endpoint.AppId,
                EndpointStatus.Unavailable, It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()),
            Times.Once);
        _mockStateManager.Verify(
            s => s.DeregisterEndpointAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region Health Check Event Publishing

    /// <summary>
    /// Verifies that a failed probe publishes a MeshEndpointHealthCheckFailedEvent
    /// with correct consecutive failure count and threshold.
    /// </summary>
    [Fact]
    public async Task HandleFailedProbe_PublishesHealthCheckFailedEvent()
    {
        // Arrange
        var endpoint = CreateEndpoint();
        var heartbeatDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint> { endpoint });

        _mockStateManager.Setup(s => s.UpdateHeartbeatAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()))
            .ReturnsAsync(true)
            .Callback(() => heartbeatDone.TrySetResult(true));

        var service = CreateService(failureThreshold: 99);

        // Act
        await service.StartAsync(CancellationToken.None);
        await heartbeatDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert - health check failed event published with correct data
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "mesh.endpoint.health.failed",
                It.Is<MeshEndpointHealthCheckFailedEvent>(e =>
                    e.InstanceId == endpoint.InstanceId &&
                    e.AppId == endpoint.AppId &&
                    e.ConsecutiveFailures == 1 &&
                    e.FailureThreshold == 99),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region No Endpoints

    /// <summary>
    /// Verifies that when no endpoints are registered, the probe cycle completes
    /// without any state updates or event publishing.
    /// </summary>
    [Fact]
    public async Task ProbeAllEndpoints_NoEndpoints_DoesNothing()
    {
        // Arrange
        var probeCycleDone = new TaskCompletionSource<bool>();

        _mockStateManager.Setup(s => s.GetAllEndpointsAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<MeshEndpoint>())
            .Callback(() => probeCycleDone.TrySetResult(true));

        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);
        await probeCycleDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100); // Ensure no further processing
        await service.StopAsync(CancellationToken.None);

        // Assert - no state updates or event publishing
        _mockStateManager.Verify(
            s => s.UpdateHeartbeatAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EndpointStatus>(), It.IsAny<float>(),
                It.IsAny<int>(), It.IsAny<ICollection<string>?>(), It.IsAny<int>()),
            Times.Never);
        _mockStateManager.Verify(
            s => s.DeregisterEndpointAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    #endregion
}
