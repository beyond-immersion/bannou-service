using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BeyondImmersion.BannouService.Tests.Mesh;

/// <summary>
/// Tests for MeshService event handler logic in MeshServiceEvents.cs.
/// Covers HandleServiceHeartbeatAsync, HandleServiceMappingsAsync,
/// MapHeartbeatStatus, DetermineDegradationReason, and TryPublishDegradationEventAsync.
/// </summary>
public class MeshServiceEventsTests
{
    private readonly Mock<IMessageBus> _messageBus;
    private readonly ILogger<MeshService> _logger;
    private readonly Mock<IMeshStateManager> _stateManager;
    private readonly Mock<IServiceAppMappingResolver> _mappingResolver;
    private readonly Mock<IEventConsumer> _eventConsumer;
    private readonly ITelemetryProvider _telemetryProvider;

    public MeshServiceEventsTests()
    {
        _messageBus = new Mock<IMessageBus>();
        _logger = NullLoggerFactory.Instance.CreateLogger<MeshService>();
        _stateManager = new Mock<IMeshStateManager>();
        _mappingResolver = new Mock<IServiceAppMappingResolver>();
        _eventConsumer = new Mock<IEventConsumer>();
        _telemetryProvider = new NullTelemetryProvider();

        // Default setup for TryPublishAsync (3-param overload used by MeshServiceEvents)
        _messageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<MeshEndpointDegradedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default setup for TryPublishErrorAsync
        _messageBus.Setup(x => x.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Creates a MeshService with default configuration, optionally overriding specific settings.
    /// </summary>
    private MeshService CreateService(
        int endpointPort = 80,
        int defaultMaxConnections = 1000,
        int endpointTtlSeconds = 90,
        int loadThresholdPercent = 80,
        bool enableServiceMappingSync = true,
        int degradationDeduplicationWindowSeconds = 60,
        int maxServiceMappingsDisplayed = 10)
    {
        var config = new MeshServiceConfiguration
        {
            EndpointPort = endpointPort,
            DefaultMaxConnections = defaultMaxConnections,
            EndpointTtlSeconds = endpointTtlSeconds,
            LoadThresholdPercent = loadThresholdPercent,
            EnableServiceMappingSync = enableServiceMappingSync,
            DegradationEventDeduplicationWindowSeconds = degradationDeduplicationWindowSeconds,
            MaxServiceMappingsDisplayed = maxServiceMappingsDisplayed,
        };

        return new MeshService(
            _messageBus.Object,
            _logger,
            config,
            _stateManager.Object,
            _mappingResolver.Object,
            _eventConsumer.Object,
            _telemetryProvider);
    }

    /// <summary>
    /// Creates a ServiceHeartbeatEvent with the given parameters.
    /// Uses unique ServiceId per call to avoid static deduplication cache collisions.
    /// </summary>
    private static ServiceHeartbeatEvent CreateHeartbeatEvent(
        Guid? serviceId = null,
        string appId = "test-app",
        ServiceHeartbeatEventStatus status = ServiceHeartbeatEventStatus.Healthy,
        float cpuUsage = 10f,
        int? currentConnections = 5,
        int? maxConnections = 100)
    {
        return new ServiceHeartbeatEvent
        {
            ServiceId = serviceId ?? Guid.NewGuid(),
            AppId = appId,
            Status = status,
            Capacity = new InstanceCapacity
            {
                CpuUsage = cpuUsage,
                CurrentConnections = currentConnections,
                MaxConnections = maxConnections,
            },
            Services = new List<ServiceStatus>
            {
                new ServiceStatus { ServiceName = "test-service" },
            },
        };
    }

    /// <summary>
    /// Creates a MeshEndpoint representing an existing registered endpoint.
    /// </summary>
    private static MeshEndpoint CreateExistingEndpoint(
        Guid instanceId,
        string appId = "test-app",
        EndpointStatus status = EndpointStatus.Healthy,
        int maxConnections = 100,
        int currentConnections = 5,
        float loadPercent = 10f)
    {
        return new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = appId,
            Host = appId,
            Port = 80,
            Status = status,
            MaxConnections = maxConnections,
            CurrentConnections = currentConnections,
            LoadPercent = loadPercent,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-1),
            RegisteredAt = DateTimeOffset.UtcNow.AddHours(-1),
            Services = new List<string> { "test-service" },
            Issues = new List<string>(),
        };
    }

    /// <summary>
    /// Verifies TryPublishAsync was called for the degradation topic with a matching event.
    /// Uses the 3-param overload (topic, event, CancellationToken) that MeshServiceEvents calls.
    /// </summary>
    private void VerifyDegradationEventPublished(
        Func<MeshEndpointDegradedEvent, bool>? predicate = null,
        Times? times = null)
    {
        _messageBus.Verify(mb => mb.TryPublishAsync(
            "mesh.endpoint.degraded",
            It.Is<MeshEndpointDegradedEvent>(e => predicate == null || predicate(e)),
            It.IsAny<CancellationToken>()),
            times ?? Times.Once());
    }

    /// <summary>
    /// Verifies TryPublishAsync was never called for the degradation topic.
    /// </summary>
    private void VerifyDegradationEventNotPublished()
    {
        _messageBus.Verify(mb => mb.TryPublishAsync(
            "mesh.endpoint.degraded",
            It.IsAny<MeshEndpointDegradedEvent>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // RegisterEventConsumers
    // =========================================================================

    [Fact]
    public void RegisterEventConsumers_AlwaysRegistersHeartbeatHandler()
    {
        CreateService(enableServiceMappingSync: false);

        // RegisterHandler extension calls Register with handlerKey = "IMeshService:{topicName}"
        _eventConsumer.Verify(
            ec => ec.Register<ServiceHeartbeatEvent>(
                "bannou.service-heartbeat",
                "IMeshService:bannou.service-heartbeat",
                It.IsAny<Func<IServiceProvider, ServiceHeartbeatEvent, Task>>()),
            Times.Once);
    }

    [Fact]
    public void RegisterEventConsumers_RegistersMappingHandler_WhenSyncEnabled()
    {
        CreateService(enableServiceMappingSync: true);

        _eventConsumer.Verify(
            ec => ec.Register<FullServiceMappingsEvent>(
                "bannou.full-service-mappings",
                "IMeshService:bannou.full-service-mappings",
                It.IsAny<Func<IServiceProvider, FullServiceMappingsEvent, Task>>()),
            Times.Once);
    }

    [Fact]
    public void RegisterEventConsumers_SkipsMappingHandler_WhenSyncDisabled()
    {
        CreateService(enableServiceMappingSync: false);

        _eventConsumer.Verify(
            ec => ec.Register<FullServiceMappingsEvent>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, FullServiceMappingsEvent, Task>>()),
            Times.Never);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Existing Endpoint
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_ExistingEndpoint_UpdatesHeartbeat()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId);
        var existing = CreateExistingEndpoint(instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            instanceId,
            "test-app",
            EndpointStatus.Healthy,
            10f,  // CpuUsage from CreateHeartbeatEvent default
            5,    // CurrentConnections from CreateHeartbeatEvent default
            existing.Issues,
            90), // EndpointTtlSeconds default
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_ExistingEndpoint_PreservesExistingIssues()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId);
        var existingIssues = new List<string> { "disk-full", "slow-response" };
        var existing = CreateExistingEndpoint(instanceId);
        existing.Issues = existingIssues;

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            instanceId,
            It.IsAny<string>(),
            It.IsAny<EndpointStatus>(),
            It.IsAny<float>(),
            It.IsAny<int>(),
            existingIssues,  // Must pass existing issues through
            It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_ExistingEndpoint_NullCapacity_UsesZeroDefaults()
    {
        var instanceId = Guid.NewGuid();
        var evt = new ServiceHeartbeatEvent
        {
            ServiceId = instanceId,
            AppId = "test-app",
            Status = ServiceHeartbeatEventStatus.Healthy,
            Capacity = null,
        };
        var existing = CreateExistingEndpoint(instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            instanceId,
            "test-app",
            EndpointStatus.Healthy,
            0f,  // CpuUsage defaults to 0
            0,   // CurrentConnections defaults to 0
            It.IsAny<ICollection<string>>(),
            90),
            Times.Once);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — New Endpoint (Auto-Registration)
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_NewEndpoint_AutoRegisters()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            appId: "new-app",
            cpuUsage: 25f,
            currentConnections: 10,
            maxConnections: 200);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService(endpointPort: 9090, defaultMaxConnections: 500);
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.RegisterEndpointAsync(
            It.Is<MeshEndpoint>(ep =>
                ep.InstanceId == instanceId &&
                ep.AppId == "new-app" &&
                ep.Host == "new-app" &&
                ep.Port == 9090 &&
                ep.Status == EndpointStatus.Healthy &&
                ep.MaxConnections == 200 &&
                ep.CurrentConnections == 10 &&
                ep.LoadPercent == 25f &&
                ep.Services != null && ep.Services.Contains("test-service")),
            90),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_NewEndpoint_NullCapacity_UsesConfigDefaults()
    {
        var instanceId = Guid.NewGuid();
        var evt = new ServiceHeartbeatEvent
        {
            ServiceId = instanceId,
            AppId = "new-app",
            Status = ServiceHeartbeatEventStatus.Healthy,
            Capacity = null,
            // Services defaults to empty Collection (non-nullable, [Required])
        };

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService(defaultMaxConnections: 2000);
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.RegisterEndpointAsync(
            It.Is<MeshEndpoint>(ep =>
                ep.MaxConnections == 2000 &&
                ep.CurrentConnections == 0 &&
                ep.LoadPercent == 0f &&
                ep.Services != null && ep.Services.Count == 0),
            It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_NewEndpoint_DoesNotCallUpdateHeartbeat()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync((MeshEndpoint?)null);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<EndpointStatus>(),
            It.IsAny<float>(), It.IsAny<int>(), It.IsAny<ICollection<string>>(),
            It.IsAny<int>()),
            Times.Never);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Status Mapping (MapHeartbeatStatus)
    // =========================================================================

    [Theory]
    [InlineData(ServiceHeartbeatEventStatus.Healthy, EndpointStatus.Healthy)]
    [InlineData(ServiceHeartbeatEventStatus.Degraded, EndpointStatus.Degraded)]
    [InlineData(ServiceHeartbeatEventStatus.Overloaded, EndpointStatus.Degraded)]
    [InlineData(ServiceHeartbeatEventStatus.Unavailable, EndpointStatus.Unavailable)]
    [InlineData(ServiceHeartbeatEventStatus.Shutting_down, EndpointStatus.ShuttingDown)]
    public async Task HandleServiceHeartbeatAsync_MapsHeartbeatStatus_Correctly(
        ServiceHeartbeatEventStatus heartbeatStatus,
        EndpointStatus expectedEndpointStatus)
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId, status: heartbeatStatus);
        var existing = CreateExistingEndpoint(instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            instanceId,
            It.IsAny<string>(),
            expectedEndpointStatus,
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<ICollection<string>>(),
            It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_DefaultStatus_MapsToHealthy()
    {
        var instanceId = Guid.NewGuid();
        // Default enum value (0) is ServiceHeartbeatEventStatus.Healthy
        var evt = CreateHeartbeatEvent(serviceId: instanceId, status: default);
        var existing = CreateExistingEndpoint(instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        _stateManager.Verify(sm => sm.UpdateHeartbeatAsync(
            instanceId,
            It.IsAny<string>(),
            EndpointStatus.Healthy,
            It.IsAny<float>(),
            It.IsAny<int>(),
            It.IsAny<ICollection<string>>(),
            It.IsAny<int>()),
            Times.Once);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Degradation Detection
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_TransitionToDegraded_PublishesDegradationEvent()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 50f);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventPublished(e =>
            e.InstanceId == instanceId &&
            e.AppId == "test-app" &&
            e.EventName == "mesh.endpoint_degraded");
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_AlreadyDegraded_DoesNotPublishDegradationEvent()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded);
        // Previous status is already Degraded
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Degraded);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventNotPublished();
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_TransitionToHealthy_DoesNotPublishDegradationEvent()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Healthy);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Degraded);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventNotPublished();
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — DetermineDegradationReason
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_HighCpuLoad_DegradationReasonIsHighLoad()
    {
        var instanceId = Guid.NewGuid();
        // CPU at 85% exceeds default threshold of 80%
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 85f);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService(loadThresholdPercent: 80);
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventPublished(e =>
            e.Reason == DegradedReason.HighLoad &&
            e.LoadPercent == 85f);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_HighConnectionCount_DegradationReasonIsHighConnectionCount()
    {
        var instanceId = Guid.NewGuid();
        // Connections at max (100 >= 100)
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 10f, // low CPU
            currentConnections: 100);
        var existing = CreateExistingEndpoint(
            instanceId,
            status: EndpointStatus.Healthy,
            maxConnections: 100);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService(loadThresholdPercent: 80);
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventPublished(e =>
            e.Reason == DegradedReason.HighConnectionCount);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_NeitherHighLoadNorHighConnections_DegradationReasonIsMissedHeartbeat()
    {
        var instanceId = Guid.NewGuid();
        // Status is Degraded but CPU and connections are low
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 10f,
            currentConnections: 5);
        var existing = CreateExistingEndpoint(
            instanceId,
            status: EndpointStatus.Healthy,
            maxConnections: 1000);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService(loadThresholdPercent: 80);
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventPublished(e =>
            e.Reason == DegradedReason.MissedHeartbeat);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Deduplication
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_DuplicateDegradation_WithinWindow_PublishesOnlyOnce()
    {
        var instanceId = Guid.NewGuid();

        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 10f,
            currentConnections: 5);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        // Use a large deduplication window
        var service = CreateService(degradationDeduplicationWindowSeconds: 3600);

        // First call should publish
        await service.HandleServiceHeartbeatAsync(evt);

        // Second call with same instance + same reason should be deduplicated
        // Reset mock to get a fresh "Healthy -> Degraded" transition
        var existing2 = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);
        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing2);

        await service.HandleServiceHeartbeatAsync(evt);

        // Should have published exactly once (second call deduplicated)
        VerifyDegradationEventPublished(times: Times.Once());
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_DuplicateDegradation_ExpiredWindow_PublishesTwice()
    {
        var instanceId = Guid.NewGuid();

        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 10f,
            currentConnections: 5);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        // Use a tiny deduplication window (0 seconds = always publish)
        var service = CreateService(degradationDeduplicationWindowSeconds: 0);

        await service.HandleServiceHeartbeatAsync(evt);

        // Reset to get a fresh "Healthy -> Degraded" transition
        var existing2 = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);
        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing2);

        await service.HandleServiceHeartbeatAsync(evt);

        // Both should publish since the window is 0 seconds
        VerifyDegradationEventPublished(times: Times.Exactly(2));
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Error Handling
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_StateManagerThrows_PublishesErrorEvent()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ThrowsAsync(new InvalidOperationException("state error"));

        var service = CreateService();

        // Should not throw — error is caught and published
        await service.HandleServiceHeartbeatAsync(evt);

        _messageBus.Verify(mb => mb.TryPublishErrorAsync(
            "mesh",
            "HandleServiceHeartbeat",
            "unexpected_exception",
            It.Is<string>(msg => msg.Contains("state error")),
            It.IsAny<string?>(),                   // dependency
            It.IsAny<string?>(),                   // endpoint
            It.IsAny<ServiceErrorEventSeverity>(),  // severity
            It.IsAny<object?>(),                    // details
            It.IsAny<string?>(),                    // stack
            It.IsAny<Guid?>(),                      // correlationId
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceHeartbeatAsync_StateManagerThrows_DoesNotRethrow()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(serviceId: instanceId);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ThrowsAsync(new Exception("unexpected"));

        var service = CreateService();

        // Should not throw
        var exception = await Record.ExceptionAsync(() =>
            service.HandleServiceHeartbeatAsync(evt));

        Assert.Null(exception);
    }

    // =========================================================================
    // HandleServiceMappingsAsync — Successful Update
    // =========================================================================

    [Fact]
    public async Task HandleServiceMappingsAsync_WithMappings_CallsReplaceAllMappings()
    {
        var evt = new FullServiceMappingsEvent
        {
            Mappings = new Dictionary<string, string>
            {
                ["auth"] = "auth-node-1",
                ["account"] = "account-node-2",
            },
            DefaultAppId = "my-default",
            Version = 42,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            "my-default",
            42))
            .Returns(true);

        var service = CreateService();
        await service.HandleServiceMappingsAsync(evt);

        _mappingResolver.Verify(mr => mr.ReplaceAllMappings(
            It.Is<Dictionary<string, string>>(d =>
                d.Count == 2 &&
                d["auth"] == "auth-node-1" &&
                d["account"] == "account-node-2"),
            "my-default",
            42),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceMappingsAsync_EmptyMappings_ResetsToDefault()
    {
        var evt = new FullServiceMappingsEvent
        {
            Mappings = new Dictionary<string, string>(), // empty = reset
            DefaultAppId = null, // null defaults to "bannou"
            Version = 10,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            AppConstants.DEFAULT_APP_NAME,
            10))
            .Returns(true);

        var service = CreateService();
        await service.HandleServiceMappingsAsync(evt);

        _mappingResolver.Verify(mr => mr.ReplaceAllMappings(
            It.Is<Dictionary<string, string>>(d => d.Count == 0),
            AppConstants.DEFAULT_APP_NAME,
            10),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceMappingsAsync_DefaultMappings_TreatsAsEmpty()
    {
        // FullServiceMappingsEvent.Mappings defaults to empty dictionary (non-nullable, [Required])
        var evt = new FullServiceMappingsEvent
        {
            DefaultAppId = "custom-default",
            Version = 5,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Returns(true);

        var service = CreateService();
        await service.HandleServiceMappingsAsync(evt);

        _mappingResolver.Verify(mr => mr.ReplaceAllMappings(
            It.Is<Dictionary<string, string>>(d => d.Count == 0),
            "custom-default",
            5),
            Times.Once);
    }

    // =========================================================================
    // HandleServiceMappingsAsync — Stale Version
    // =========================================================================

    [Fact]
    public async Task HandleServiceMappingsAsync_StaleVersion_DoesNotUpdate()
    {
        var evt = new FullServiceMappingsEvent
        {
            Mappings = new Dictionary<string, string> { ["auth"] = "old-node" },
            Version = 1,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            1))
            .Returns(false); // Stale version — resolver rejects

        _mappingResolver.Setup(mr => mr.CurrentVersion).Returns(5L);

        var service = CreateService();
        await service.HandleServiceMappingsAsync(evt);

        // Verify ReplaceAllMappings was called but returned false (stale)
        _mappingResolver.Verify(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            1),
            Times.Once);
    }

    // =========================================================================
    // HandleServiceMappingsAsync — Error Handling
    // =========================================================================

    [Fact]
    public async Task HandleServiceMappingsAsync_ResolverThrows_PublishesErrorEvent()
    {
        var evt = new FullServiceMappingsEvent
        {
            Mappings = new Dictionary<string, string> { ["auth"] = "node" },
            Version = 10,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Throws(new InvalidOperationException("resolver error"));

        var service = CreateService();
        await service.HandleServiceMappingsAsync(evt);

        _messageBus.Verify(mb => mb.TryPublishErrorAsync(
            "mesh",
            "HandleServiceMappings",
            "unexpected_exception",
            It.Is<string>(msg => msg.Contains("resolver error")),
            It.IsAny<string?>(),                   // dependency
            It.IsAny<string?>(),                   // endpoint
            It.IsAny<ServiceErrorEventSeverity>(),  // severity
            It.IsAny<object?>(),                    // details
            It.IsAny<string?>(),                    // stack
            It.IsAny<Guid?>(),                      // correlationId
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleServiceMappingsAsync_ResolverThrows_DoesNotRethrow()
    {
        var evt = new FullServiceMappingsEvent
        {
            Mappings = new Dictionary<string, string> { ["auth"] = "node" },
            Version = 10,
        };

        _mappingResolver.Setup(mr => mr.ReplaceAllMappings(
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<long>()))
            .Throws(new Exception("unexpected"));

        var service = CreateService();

        var exception = await Record.ExceptionAsync(() =>
            service.HandleServiceMappingsAsync(evt));

        Assert.Null(exception);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Degradation Event Content
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_DegradationEvent_IncludesCorrectFields()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            appId: "production-app",
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 95f);
        var lastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);
        var existing = CreateExistingEndpoint(instanceId, appId: "production-app", status: EndpointStatus.Healthy);
        existing.LastSeen = lastSeen;

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        MeshEndpointDegradedEvent? capturedEvent = null;
        _messageBus.Setup(mb => mb.TryPublishAsync(
                "mesh.endpoint.degraded",
                It.IsAny<MeshEndpointDegradedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, MeshEndpointDegradedEvent, CancellationToken>(
                (_, e, _) => capturedEvent = e)
            .ReturnsAsync(true);

        var service = CreateService(loadThresholdPercent: 80);
        await service.HandleServiceHeartbeatAsync(evt);

        Assert.NotNull(capturedEvent);
        Assert.Equal(instanceId, capturedEvent.InstanceId);
        Assert.Equal("production-app", capturedEvent.AppId);
        Assert.Equal(DegradedReason.HighLoad, capturedEvent.Reason);
        Assert.Equal(95f, capturedEvent.LoadPercent);
        Assert.Equal(lastSeen, capturedEvent.LastHeartbeatAt);
        Assert.Equal("mesh.endpoint_degraded", capturedEvent.EventName);
        Assert.NotEqual(Guid.Empty, capturedEvent.EventId);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Degradation Publish Failure
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_DegradationPublishFails_DoesNotRethrow()
    {
        var instanceId = Guid.NewGuid();
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Degraded,
            cpuUsage: 10f);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        _messageBus.Setup(mb => mb.TryPublishAsync(
                "mesh.endpoint.degraded",
                It.IsAny<MeshEndpointDegradedEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("publish failed"));

        var service = CreateService();

        // Should not throw — degradation publish failure is caught
        var exception = await Record.ExceptionAsync(() =>
            service.HandleServiceHeartbeatAsync(evt));

        Assert.Null(exception);

        // Should publish error event about the publish failure
        _messageBus.Verify(mb => mb.TryPublishErrorAsync(
            "mesh",
            "PublishDegradationEvent",
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("publish failed")),
            It.IsAny<string?>(),                   // dependency
            It.IsAny<string?>(),                   // endpoint
            ServiceErrorEventSeverity.Warning,      // severity
            It.IsAny<object?>(),                    // details
            It.IsAny<string?>(),                    // stack
            It.IsAny<Guid?>(),                      // correlationId
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // HandleServiceHeartbeatAsync — Overloaded Status Maps to Degraded
    // =========================================================================

    [Fact]
    public async Task HandleServiceHeartbeatAsync_OverloadedStatus_PublishesDegradationEvent()
    {
        var instanceId = Guid.NewGuid();
        // Overloaded maps to Degraded, which triggers degradation detection
        var evt = CreateHeartbeatEvent(
            serviceId: instanceId,
            status: ServiceHeartbeatEventStatus.Overloaded,
            cpuUsage: 10f);
        var existing = CreateExistingEndpoint(instanceId, status: EndpointStatus.Healthy);

        _stateManager.Setup(sm => sm.GetEndpointByInstanceIdAsync(instanceId))
            .ReturnsAsync(existing);

        var service = CreateService();
        await service.HandleServiceHeartbeatAsync(evt);

        VerifyDegradationEventPublished();
    }
}
