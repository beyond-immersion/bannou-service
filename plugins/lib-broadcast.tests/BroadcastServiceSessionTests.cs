using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Broadcast.Tests;

/// <summary>
/// Unit tests for BroadcastService session endpoints.
/// Tests derived from implementation map: docs/maps/BROADCAST.md § StartSession, StopSession, AssociateSession, GetSessionStatus, ListSessions.
/// </summary>
public class BroadcastServiceSessionTests
{
    /// <summary>
    /// Map § StartSession: READ link -> 404 if null
    /// </summary>
    [Fact]
    public async Task StartSessionAsync_LinkNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService();

        var request = new StartSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            LinkId = Guid.NewGuid()
        };

        var (status, _) = await service.StartSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § StartSession: Happy path -> WRITE session + PUBLISH created + PUSH client event -> 200
    /// </summary>
    [Fact]
    public async Task StartSessionAsync_ValidRequest_ReturnsOkAndPublishesEvent()
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

        var request = new StartSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            LinkId = Guid.NewGuid()
        };

        var (status, response) = await service.StartSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.PlatformSessionId);
        Assert.Equal("broadcast.platform-session.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    /// <summary>
    /// Map § StopSession: READ session -> 404 if null
    /// </summary>
    [Fact]
    public async Task StopSessionAsync_SessionNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService();

        var request = new StopSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid()
        };

        var status = await service.StopSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § StopSession: Happy path -> DELETE session + PUBLISH deleted + PUSH client event -> 200
    /// </summary>
    [Fact]
    public async Task StopSessionAsync_ValidRequest_ReturnsOkAndPublishesEvent()
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

        var request = new StopSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid()
        };

        var status = await service.StopSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("broadcast.platform-session.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    /// <summary>
    /// Map § AssociateSession: READ session -> 404 if null
    /// </summary>
    [Fact]
    public async Task AssociateSessionAsync_SessionNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService();

        var request = new AssociateSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid(),
            StreamSessionId = Guid.NewGuid()
        };

        var status = await service.AssociateSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § AssociateSession: Happy path -> ETAG-WRITE session + PUBLISH updated -> 200
    /// </summary>
    [Fact]
    public async Task AssociateSessionAsync_ValidRequest_ReturnsOkAndPublishesEvent()
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

        var request = new AssociateSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid(),
            StreamSessionId = Guid.NewGuid()
        };

        var status = await service.AssociateSessionAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("broadcast.platform-session.updated", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    /// <summary>
    /// Map § GetSessionStatus: READ session -> 404 if null
    /// </summary>
    [Fact]
    public async Task GetSessionStatusAsync_SessionNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService();

        var request = new GetSessionStatusRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid()
        };

        var (status, _) = await service.GetSessionStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § GetSessionStatus: Happy path -> 200 with session data
    /// </summary>
    [Fact]
    public async Task GetSessionStatusAsync_ValidRequest_ReturnsOkWithStatus()
    {
        var (service, _, _) = CreateService();

        var request = new GetSessionStatusRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = Guid.NewGuid()
        };

        var (status, response) = await service.GetSessionStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.PlatformSessionId);
    }

    /// <summary>
    /// Map § ListSessions: QUERY -> 200 with paged results
    /// </summary>
    [Fact]
    public async Task ListSessionsAsync_ValidRequest_ReturnsOkWithSessions()
    {
        var (service, _, _) = CreateService();

        var request = new ListSessionsRequest
        {
            WebSocketSessionId = Guid.NewGuid()
        };

        var (status, response) = await service.ListSessionsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Sessions);
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
