using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Broadcast.Tests;

/// <summary>
/// Unit tests for BroadcastService platform linking endpoints.
/// Tests derived from implementation map: docs/maps/BROADCAST.md § LinkPlatform, PlatformCallback, UnlinkPlatform, ListPlatforms.
/// </summary>
public class BroadcastServicePlatformTests
{
    /// <summary>
    /// Map § LinkPlatform: IF NOT config.BroadcastEnabled -> 400
    /// </summary>
    [Fact]
    public async Task LinkPlatformAsync_BroadcastDisabled_ReturnsBadRequest()
    {
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = false
        });

        var request = new LinkPlatformRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            Platform = PlatformType.Custom
        };

        var (status, _) = await service.LinkPlatformAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    /// <summary>
    /// Map § LinkPlatform: IF body.platform is Custom -> WRITE platform + PUBLISH created -> 200
    /// </summary>
    [Fact]
    public async Task LinkPlatformAsync_CustomPlatform_ReturnsOkWithLinkId()
    {
        string? capturedTopic = null;
        object? capturedEvent = null;

        var (service, messageBusMock, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true
        });

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new LinkPlatformRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            Platform = PlatformType.Custom,
            RtmpUrl = "rtmp://example.com/live/key"
        };

        var (status, response) = await service.LinkPlatformAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.LinkId);
        Assert.Equal("broadcast.platform-link.created", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    /// <summary>
    /// Map § LinkPlatform: IF body.platform is Twitch/YouTube -> RETURN (200, { oauthRedirectUrl })
    /// </summary>
    [Fact]
    public async Task LinkPlatformAsync_TwitchPlatform_ReturnsOkWithOAuthUrl()
    {
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true,
            TwitchClientId = "test-client-id",
            TwitchClientSecret = "test-client-secret",
            TokenEncryptionKey = "01234567890123456789012345678901"
        });

        var request = new LinkPlatformRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            Platform = PlatformType.Twitch
        };

        var (status, response) = await service.LinkPlatformAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.OauthRedirectUrl);
    }

    /// <summary>
    /// Map § PlatformCallback: IF config.TokenEncryptionKey is null -> 400
    /// </summary>
    [Fact]
    public async Task PlatformCallbackAsync_NoEncryptionKey_ReturnsBadRequest()
    {
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true,
            TokenEncryptionKey = null
        });

        var request = new PlatformCallbackRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            Platform = PlatformType.Twitch,
            AuthorizationCode = "auth-code",
            State = "state-token"
        };

        var (status, _) = await service.PlatformCallbackAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    /// <summary>
    /// Map § UnlinkPlatform: READ link -> 404 if null
    /// </summary>
    [Fact]
    public async Task UnlinkPlatformAsync_LinkNotFound_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true
        });

        var request = new UnlinkPlatformRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            LinkId = Guid.NewGuid()
        };

        var status = await service.UnlinkPlatformAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    /// <summary>
    /// Map § UnlinkPlatform: Happy path -> delete link + publish deleted -> 200
    /// </summary>
    [Fact]
    public async Task UnlinkPlatformAsync_ValidRequest_ReturnsOkAndPublishesEvent()
    {
        string? capturedTopic = null;
        object? capturedEvent = null;

        var (service, messageBusMock, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true
        });

        messageBusMock.Setup(x => x.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UnlinkPlatformRequest
        {
            WebSocketSessionId = Guid.NewGuid(),
            LinkId = Guid.NewGuid()
        };

        var status = await service.UnlinkPlatformAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal("broadcast.platform-link.deleted", capturedTopic);
        Assert.NotNull(capturedEvent);
    }

    /// <summary>
    /// Map § ListPlatforms: QUERY -> 200 with masked links
    /// </summary>
    [Fact]
    public async Task ListPlatformsAsync_ValidRequest_ReturnsOkWithLinks()
    {
        var (service, _, _) = CreateService(new BroadcastServiceConfiguration
        {
            BroadcastEnabled = true
        });

        var request = new ListPlatformsRequest
        {
            WebSocketSessionId = Guid.NewGuid()
        };

        var (status, response) = await service.ListPlatformsAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Links);
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
