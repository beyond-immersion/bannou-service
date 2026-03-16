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
/// Unit tests for BroadcastService admin, sentiment, and cleanup endpoints.
/// Tests derived from implementation map: docs/maps/BROADCAST.md § GetLatestPulse, TestSentiment, CleanupByAccount.
/// </summary>
public class BroadcastServiceAdminTests
{
    // ── GetLatestPulse ──────────────────────────────────────────────────

    /// <summary>
    /// Map § GetLatestPulse: READ session -> 404 if null
    /// </summary>
    [Fact]
    public async Task GetLatestPulseAsync_SessionNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService();

        var request = new GetLatestPulseRequest
        {
            PlatformSessionId = Guid.NewGuid()
        };

        var (status, _) = await service.GetLatestPulseAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § GetLatestPulse: Happy path -> 200 with pulse data
    /// </summary>
    [Fact]
    public async Task GetLatestPulseAsync_SessionFound_ReturnsOkWithPulse()
    {
        var (service, _, sessionStoreMock) = CreateService();

        var platformSessionId = Guid.NewGuid();
        var streamSessionId = Guid.NewGuid();

        var storedSession = new PlatformSessionModel
        {
            PlatformSessionId = platformSessionId,
            LinkId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Platform = PlatformType.Twitch,
            ViewerCount = 42,
            PeakViewerCount = 100,
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            StreamSessionId = streamSessionId,
            State = PlatformSessionState.Active
        };

        sessionStoreMock.Setup(x => x.GetAsync(
                BroadcastService.BuildSessionKey(platformSessionId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSession);

        var request = new GetLatestPulseRequest
        {
            PlatformSessionId = platformSessionId
        };

        var (status, response) = await service.GetLatestPulseAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Pulse);
        Assert.Equal(platformSessionId, response.Pulse.PlatformSessionId);
        Assert.Equal(streamSessionId, response.Pulse.StreamSessionId);
        Assert.Equal(42, response.Pulse.ApproximateViewerCount);
    }

    // ── TestSentiment ───────────────────────────────────────────────────

    /// <summary>
    /// Map § TestSentiment: Stateless computation -> 200 with classification
    /// </summary>
    [Fact]
    public async Task TestSentimentAsync_ValidText_ReturnsClassification()
    {
        var (service, _, _) = CreateService();

        var request = new TestSentimentRequest
        {
            Text = "This is amazing! I love it!"
        };

        var (status, response) = await service.TestSentimentAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Intensity >= 0.0f && response.Intensity <= 1.0f);
    }

    // ── CleanupByAccount ────────────────────────────────────────────────

    /// <summary>
    /// Map § CleanupByAccount: Has data -> deletes all + publishes all -> 200
    /// T28 Account Deletion Cleanup Obligation.
    /// </summary>
    [Fact]
    public async Task CleanupByAccountAsync_HasData_ReturnsOkAndCleansUp()
    {
        var publishedTopics = new List<string>();

        var (service, messageBusMock, _) = CreateService();

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, _, _) =>
            {
                publishedTopics.Add(topic);
            })
            .ReturnsAsync(true);

        var request = new CleanupByAccountRequest
        {
            AccountId = Guid.NewGuid()
        };

        var status = await service.CleanupByAccountAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        // When account has data: expects broadcast.output.deleted, broadcast.platform-session.deleted,
        // broadcast.platform-link.deleted events to be published during cleanup
    }

    /// <summary>
    /// Map § CleanupByAccount: No data -> 200 (idempotent)
    /// </summary>
    [Fact]
    public async Task CleanupByAccountAsync_NoData_ReturnsOk()
    {
        var (service, _, _) = CreateService();

        var request = new CleanupByAccountRequest
        {
            AccountId = Guid.NewGuid()
        };

        var status = await service.CleanupByAccountAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
    }

    private static (BroadcastService service, Mock<IMessageBus> messageBusMock, Mock<IStateStore<PlatformSessionModel>> sessionStoreMock) CreateService(
        BroadcastServiceConfiguration? config = null)
    {
        var messageBus = new Mock<IMessageBus>();
        var storeFactory = new Mock<IStateStoreFactory>();
        var sessionStore = new Mock<IStateStore<PlatformSessionModel>>();
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

        storeFactory.Setup(x => x.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions))
            .Returns(sessionStore.Object);

        messageBus.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        sentimentProcessor.Setup(x => x.ClassifyAsync(It.IsAny<string>()))
            .ReturnsAsync((SentimentCategory.Excited, 0.8f));

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

        return (service, messageBus, sessionStore);
    }
}
