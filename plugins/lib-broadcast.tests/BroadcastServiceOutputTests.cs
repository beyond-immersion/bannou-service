using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Broadcast.Tests;

/// <summary>
/// Unit tests for BroadcastService camera and output endpoints.
/// Tests derived from implementation map: docs/maps/BROADCAST.md § AnnounceCamera, RetireCamera, StartOutput, StopOutput, UpdateOutput, GetOutputStatus, ListOutputs.
/// </summary>
public class BroadcastServiceOutputTests
{
    // ── AnnounceCamera ──────────────────────────────────────────────────

    /// <summary>
    /// Map § AnnounceCamera: IF NOT config.OutputEnabled -> 400
    /// </summary>
    [Fact]
    public async Task AnnounceCameraAsync_OutputDisabled_ReturnsBadRequest()
    {
        var (service, _, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = false
        });

        var request = new AnnounceCameraRequest
        {
            CameraId = "camera-01",
            RtmpInputUrl = "rtmp://localhost:1935/live/camera-01"
        };

        var (status, _) = await service.AnnounceCameraAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    /// <summary>
    /// Map § AnnounceCamera: Idempotent upsert -> WRITE cam + 200
    /// </summary>
    [Fact]
    public async Task AnnounceCameraAsync_OutputEnabled_ReturnsOk()
    {
        var (service, _, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true
        });

        var request = new AnnounceCameraRequest
        {
            CameraId = "camera-01",
            RtmpInputUrl = "rtmp://localhost:1935/live/camera-01"
        };

        var (status, response) = await service.AnnounceCameraAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    // ── RetireCamera ────────────────────────────────────────────────────

    /// <summary>
    /// Map § RetireCamera: READ cam -> 404 if null
    /// </summary>
    [Fact]
    public async Task RetireCameraAsync_CameraNotFound_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true
        });

        var request = new RetireCameraRequest
        {
            CameraId = "nonexistent-camera"
        };

        var status = await service.RetireCameraAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § RetireCamera: Happy path -> DELETE cam + trigger fallback cascade -> 200
    /// </summary>
    [Fact]
    public async Task RetireCameraAsync_CameraFound_ReturnsOk()
    {
        var (service, _, cameraStoreMock, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true
        });

        cameraStoreMock.Setup(x => x.GetAsync(
                BroadcastService.BuildCameraKey("camera-01"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CameraSourceModel
            {
                CameraId = "camera-01",
                RtmpInputUrl = "rtmp://localhost:1935/live/camera-01",
                HeartbeatAt = DateTimeOffset.UtcNow
            });

        var request = new RetireCameraRequest
        {
            CameraId = "camera-01"
        };

        var status = await service.RetireCameraAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
    }

    // ── StartOutput ─────────────────────────────────────────────────────

    /// <summary>
    /// Map § StartOutput: IF NOT config.OutputEnabled -> 400
    /// </summary>
    [Fact]
    public async Task StartOutputAsync_OutputDisabled_ReturnsBadRequest()
    {
        var (service, _, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = false
        });

        var request = new StartOutputRequest
        {
            SourceType = BroadcastSourceType.Camera,
            RtmpUrl = "rtmp://example.com/live/key",
            CameraId = "camera-01"
        };

        var (status, _) = await service.StartOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    /// <summary>
    /// Map § StartOutput: Happy path Camera source -> WRITE broadcast + PUBLISH created + PUSH client event -> 200
    /// </summary>
    [Fact]
    public async Task StartOutputAsync_CameraSource_ReturnsOkAndPublishesEvent()
    {
        string? capturedTopic = null;
        object? capturedEvent = null;

        var (service, messageBusMock, cameraStoreMock, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true,
            MaxConcurrentOutputs = 10
        });

        cameraStoreMock.Setup(x => x.GetAsync(
                BroadcastService.BuildCameraKey("camera-01"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CameraSourceModel
            {
                CameraId = "camera-01",
                RtmpInputUrl = "rtmp://localhost:1935/live/camera-01",
                HeartbeatAt = DateTimeOffset.UtcNow
            });

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new StartOutputRequest
        {
            SourceType = BroadcastSourceType.Camera,
            RtmpUrl = "rtmp://example.com/live/key",
            CameraId = "camera-01"
        };

        var (status, response) = await service.StartOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.BroadcastId);
        Assert.Equal("broadcast.output.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    // ── StopOutput ──────────────────────────────────────────────────────

    /// <summary>
    /// Map § StopOutput: READ broadcast -> 404 if null
    /// </summary>
    [Fact]
    public async Task StopOutputAsync_OutputNotFound_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var request = new StopOutputRequest
        {
            BroadcastId = Guid.NewGuid()
        };

        var status = await service.StopOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § StopOutput: Happy path -> DELETE broadcast + PUBLISH deleted + PUSH client event -> 200
    /// </summary>
    [Fact]
    public async Task StopOutputAsync_ValidRequest_ReturnsOkAndPublishesEvent()
    {
        string? capturedTopic = null;
        object? capturedEvent = null;

        var broadcastId = Guid.NewGuid();

        var (service, messageBusMock, _, outputStoreMock) = CreateService();

        outputStoreMock.Setup(x => x.GetAsync(
                BroadcastService.BuildOutputKey(broadcastId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BroadcastOutputModel
            {
                BroadcastId = broadcastId,
                SourceType = BroadcastSourceType.Camera,
                SourceId = "camera-01",
                EncryptedRtmpUrl = "rtmp://example.com/live/key",
                MaskedRtmpUrl = "rtmp://example.com/live/***",
                OwningInstanceId = "test-instance",
                State = BroadcastState.Active,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Health = BroadcastHealth.Healthy
            });

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new StopOutputRequest
        {
            BroadcastId = broadcastId
        };

        var status = await service.StopOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("broadcast.output.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    // ── UpdateOutput ────────────────────────────────────────────────────

    /// <summary>
    /// Map § UpdateOutput: READ broadcast -> 404 if null
    /// </summary>
    [Fact]
    public async Task UpdateOutputAsync_OutputNotFound_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var request = new UpdateOutputRequest
        {
            BroadcastId = Guid.NewGuid()
        };

        var status = await service.UpdateOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § UpdateOutput: Happy path -> ETAG-WRITE broadcast + PUBLISH updated -> 200
    /// </summary>
    [Fact]
    public async Task UpdateOutputAsync_ValidRequest_ReturnsOkAndPublishesEvent()
    {
        string? capturedTopic = null;
        object? capturedEvent = null;

        var broadcastId = Guid.NewGuid();

        var (service, messageBusMock, _, outputStoreMock) = CreateService();

        outputStoreMock.Setup(x => x.GetWithETagAsync(
                BroadcastService.BuildOutputKey(broadcastId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new BroadcastOutputModel
            {
                BroadcastId = broadcastId,
                SourceType = BroadcastSourceType.Camera,
                SourceId = "camera-01",
                EncryptedRtmpUrl = "rtmp://example.com/live/old-key",
                MaskedRtmpUrl = "rtmp://example.com/live/***",
                OwningInstanceId = "test-instance",
                State = BroadcastState.Active,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Health = BroadcastHealth.Healthy
            }, "etag-1"));

        outputStoreMock.Setup(x => x.TrySaveAsync(
                BroadcastService.BuildOutputKey(broadcastId),
                It.IsAny<BroadcastOutputModel>(),
                "etag-1",
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateOutputRequest
        {
            BroadcastId = broadcastId,
            RtmpUrl = "rtmp://example.com/live/new-key"
        };

        var status = await service.UpdateOutputAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("broadcast.output.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    // ── GetOutputStatus ─────────────────────────────────────────────────

    /// <summary>
    /// Map § GetOutputStatus: READ broadcast -> 404 if null
    /// </summary>
    [Fact]
    public async Task GetOutputStatusAsync_OutputNotFound_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var request = new GetOutputStatusRequest
        {
            BroadcastId = Guid.NewGuid()
        };

        var (status, _) = await service.GetOutputStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § GetOutputStatus: Happy path -> 200 with masked RTMP URL
    /// </summary>
    [Fact]
    public async Task GetOutputStatusAsync_OutputFound_ReturnsOkWithStatus()
    {
        var broadcastId = Guid.NewGuid();

        var (service, _, _, outputStoreMock) = CreateService();

        outputStoreMock.Setup(x => x.GetAsync(
                BroadcastService.BuildOutputKey(broadcastId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BroadcastOutputModel
            {
                BroadcastId = broadcastId,
                SourceType = BroadcastSourceType.Camera,
                SourceId = "camera-01",
                EncryptedRtmpUrl = "rtmp://example.com/live/key",
                MaskedRtmpUrl = "rtmp://example.com/live/***",
                OwningInstanceId = "test-instance",
                State = BroadcastState.Active,
                CurrentVideoSource = "camera-01",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Health = BroadcastHealth.Healthy
            });

        var request = new GetOutputStatusRequest
        {
            BroadcastId = broadcastId
        };

        var (status, response) = await service.GetOutputStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(broadcastId, response.BroadcastId);
    }

    // ── ListOutputs ─────────────────────────────────────────────────────

    /// <summary>
    /// Map § ListOutputs: QUERY -> 200 with paged results
    /// </summary>
    [Fact]
    public async Task ListOutputsAsync_ValidRequest_ReturnsOkWithOutputs()
    {
        var (service, _, _, _) = CreateService();

        var request = new ListOutputsRequest();

        var (status, response) = await service.ListOutputsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Outputs);
    }

    private static (BroadcastService service, Mock<IMessageBus> messageBusMock, Mock<IStateStore<CameraSourceModel>> cameraStoreMock, Mock<IStateStore<BroadcastOutputModel>> outputStoreMock) CreateService(
        BroadcastServiceConfiguration? config = null)
    {
        var messageBus = new Mock<IMessageBus>();
        var storeFactory = new Mock<IStateStoreFactory>();
        var cameraStore = new Mock<IStateStore<CameraSourceModel>>();
        var outputStore = new Mock<IStateStore<BroadcastOutputModel>>();
        var logger = new Mock<ILogger<BroadcastService>>();
        var configuration = config ?? new BroadcastServiceConfiguration();
        var eventConsumer = new Mock<IEventConsumer>();
        var lockProvider = new Mock<IDistributedLockProvider>();
        var accountClient = new Mock<IAccountClient>();
        var authClient = new Mock<IAuthClient>();
        var serviceProvider = new Mock<IServiceProvider>();
        var telemetryProvider = new Mock<ITelemetryProvider>();
        var broadcastCoordinator = new Mock<IBroadcastCoordinator>();
        var sentimentProcessor = new Mock<ISentimentProcessor>();
        var webhookHandler = new Mock<IPlatformWebhookHandler>();
        var clientEventPublisher = new Mock<IClientEventPublisher>();

        storeFactory.Setup(x => x.GetStore<CameraSourceModel>(StateStoreDefinitions.BroadcastCameras))
            .Returns(cameraStore.Object);
        storeFactory.Setup(x => x.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs))
            .Returns(outputStore.Object);

        messageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var lockResponse = new Mock<ILockResponse>();
        lockResponse.Setup(x => x.Success).Returns(true);
        lockProvider.Setup(x => x.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockResponse.Object);

        broadcastCoordinator.Setup(x => x.ValidateRtmpUrlAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new BroadcastService(
            messageBus.Object,
            storeFactory.Object,
            logger.Object,
            configuration,
            eventConsumer.Object,
            lockProvider.Object,
            accountClient.Object,
            authClient.Object,
            serviceProvider.Object,
            telemetryProvider.Object,
            broadcastCoordinator.Object,
            sentimentProcessor.Object,
            webhookHandler.Object,
            clientEventPublisher.Object);

        return (service, messageBus, cameraStore, outputStore);
    }
}
