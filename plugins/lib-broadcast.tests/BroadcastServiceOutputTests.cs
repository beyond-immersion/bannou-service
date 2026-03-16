using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;

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
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
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
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
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
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
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
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true
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
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
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

        var (service, messageBusMock, _) = CreateService(new BroadcastServiceConfiguration
        {
            OutputEnabled = true,
            MaxConcurrentOutputs = 10
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
        var (service, _, _) = CreateService();

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

        var (service, messageBusMock, _) = CreateService();

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
            BroadcastId = Guid.NewGuid()
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
        var (service, _, _) = CreateService();

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

        var (service, messageBusMock, _) = CreateService();

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
            BroadcastId = Guid.NewGuid(),
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
        var (service, _, _) = CreateService();

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
        var (service, _, _) = CreateService();

        var request = new GetOutputStatusRequest
        {
            BroadcastId = Guid.NewGuid()
        };

        var (status, response) = await service.GetOutputStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.BroadcastId);
    }

    // ── ListOutputs ─────────────────────────────────────────────────────

    /// <summary>
    /// Map § ListOutputs: QUERY -> 200 with paged results
    /// </summary>
    [Fact]
    public async Task ListOutputsAsync_ValidRequest_ReturnsOkWithOutputs()
    {
        var (service, _, _) = CreateService();

        var request = new ListOutputsRequest();

        var (status, response) = await service.ListOutputsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Outputs);
    }

    private static (BroadcastService service, Mock<IMessageBus> messageBusMock, Mock<IStateStoreFactory> storeFactoryMock) CreateService(
        BroadcastServiceConfiguration? config = null)
    {
        var messageBus = new Mock<IMessageBus>();
        var storeFactory = new Mock<IStateStoreFactory>();
        var logger = new Mock<ILogger<BroadcastService>>();
        var configuration = config ?? new BroadcastServiceConfiguration();
        var eventConsumer = new Mock<IEventConsumer>();

        messageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new BroadcastService(
            messageBus.Object,
            storeFactory.Object,
            logger.Object,
            configuration,
            eventConsumer.Object);

        return (service, messageBus, storeFactory);
    }
}
