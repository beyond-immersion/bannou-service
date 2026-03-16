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
        var (service, _, _, _) = CreateService();

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

        var (service, messageBusMock, platformStoreMock, _) = CreateService();

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var linkId = Guid.NewGuid();
        platformStoreMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformLinkModel
            {
                LinkId = linkId,
                AccountId = Guid.NewGuid(),
                Platform = PlatformType.Twitch,
                LinkedAt = DateTimeOffset.UtcNow
            });

        var request = new StartSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            LinkId = linkId
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
        var (service, _, _, _) = CreateService();

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

        var (service, messageBusMock, _, sessionStoreMock) = CreateService();

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var platformSessionId = Guid.NewGuid();
        sessionStoreMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformSessionModel
            {
                PlatformSessionId = platformSessionId,
                LinkId = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                Platform = PlatformType.Twitch,
                State = PlatformSessionState.Active,
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-10)
            });

        var request = new StopSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = platformSessionId
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
        var (service, _, _, _) = CreateService();

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

        var (service, messageBusMock, _, sessionStoreMock) = CreateService();

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var platformSessionId = Guid.NewGuid();
        sessionStoreMock.Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PlatformSessionModel
            {
                PlatformSessionId = platformSessionId,
                LinkId = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                Platform = PlatformType.YouTube,
                State = PlatformSessionState.Active,
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-5)
            }, "etag-1"));

        sessionStoreMock.Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<PlatformSessionModel>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        var request = new AssociateSessionRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = platformSessionId,
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
        var (service, _, _, _) = CreateService();

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
        var (service, _, _, sessionStoreMock) = CreateService();

        var platformSessionId = Guid.NewGuid();
        sessionStoreMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformSessionModel
            {
                PlatformSessionId = platformSessionId,
                LinkId = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                Platform = PlatformType.Twitch,
                State = PlatformSessionState.Active,
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-15),
                ViewerCount = 42
            });

        var request = new GetSessionStatusRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            PlatformSessionId = platformSessionId
        };

        var (status, response) = await service.GetSessionStatusAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(platformSessionId, response.PlatformSessionId);
        Assert.Equal(PlatformSessionState.Active, response.State);
        Assert.Equal(42, response.ViewerCount);
    }

    /// <summary>
    /// Map § ListSessions: QUERY -> 200 with paged results
    /// </summary>
    [Fact]
    public async Task ListSessionsAsync_ValidRequest_ReturnsOkWithSessions()
    {
        var (service, _, _, _) = CreateService();

        var request = new ListSessionsRequest
        {
            WebSocketSessionId = Guid.NewGuid()
        };

        var (status, response) = await service.ListSessionsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Sessions);
    }

    private static (BroadcastService service, Mock<IMessageBus> messageBusMock, Mock<IStateStore<PlatformLinkModel>> platformStoreMock, Mock<IStateStore<PlatformSessionModel>> sessionStoreMock) CreateService(
        BroadcastServiceConfiguration? config = null)
    {
        var messageBus = new Mock<IMessageBus>();
        var storeFactory = new Mock<IStateStoreFactory>();
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

        var platformStore = new Mock<IStateStore<PlatformLinkModel>>();
        var sessionStore = new Mock<IStateStore<PlatformSessionModel>>();

        storeFactory.Setup(f => f.GetStore<PlatformLinkModel>(It.IsAny<string>()))
            .Returns(platformStore.Object);
        storeFactory.Setup(f => f.GetStore<PlatformSessionModel>(It.IsAny<string>()))
            .Returns(sessionStore.Object);

        messageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var lockResponse = new Mock<ILockResponse>();
        lockResponse.Setup(x => x.Success).Returns(true);
        lockProvider.Setup(x => x.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockResponse.Object);

        clientEventPublisher.Setup(x => x.PublishToSessionAsync(
                It.IsAny<string>(), It.IsAny<BeyondImmersion.Bannou.Core.BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
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

        return (service, messageBus, platformStore, sessionStore);
    }
}
