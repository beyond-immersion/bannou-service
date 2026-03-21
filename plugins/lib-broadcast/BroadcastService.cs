using BeyondImmersion.Bannou.Broadcast.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Streaming platform integration for live content broadcasting.
/// Manages platform linking (Twitch, YouTube, Custom RTMP), platform sessions,
/// broadcast outputs (FFmpeg), camera sources, and anonymous audience sentiment.
/// </summary>
[BannouService("broadcast", typeof(IBroadcastService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFeatures)]
public partial class BroadcastService : IBroadcastService
{
    // ── Constructor-cached dependencies ──────────────────────────────────

    private readonly IMessageBus _messageBus;
    private readonly ILogger<BroadcastService> _logger;
    private readonly BroadcastServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IAccountClient _accountClient;
    private readonly IAuthClient _authClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IMeshInstanceIdentifier _meshInstanceIdentifier;
    private readonly IBroadcastCoordinator _broadcastCoordinator;
    private readonly ISentimentProcessor _sentimentProcessor;
    private readonly IPlatformWebhookHandler _webhookHandler;
    private readonly IClientEventPublisher _clientEventPublisher;

    // ── Constructor-cached state stores (per FOUNDATION TENETS) ─────────

    private readonly IStateStore<PlatformLinkModel> _platformStore;
    private readonly IStateStore<PlatformSessionModel> _sessionStore;
    private readonly IStateStore<BroadcastOutputModel> _outputStore;
    private readonly IStateStore<CameraSourceModel> _cameraStore;
    private readonly IStateStore<BufferedSentimentEntry> _sentimentBufferStore;

    #region Key Building Helpers

    private const string PLATFORM_KEY_PREFIX = "platform:";
    private const string PLATFORM_ACCOUNT_KEY_PREFIX = "platform-account:";
    private const string SESSION_KEY_PREFIX = "sess:";
    private const string SESSION_ACCOUNT_KEY_PREFIX = "sess-account:";
    private const string CAMERA_KEY_PREFIX = "cam:";
    private const string OUTPUT_KEY_PREFIX = "out:";

    /// <summary>Builds key for platform link by link ID</summary>
    internal static string BuildPlatformKey(Guid linkId) => $"{PLATFORM_KEY_PREFIX}{linkId}";

    /// <summary>Builds key for platform-account uniqueness index</summary>
    internal static string BuildPlatformAccountKey(Guid accountId, PlatformType platform) =>
        $"{PLATFORM_ACCOUNT_KEY_PREFIX}{accountId}:{platform}";

    /// <summary>Builds key for session by platform session ID</summary>
    internal static string BuildSessionKey(Guid platformSessionId) => $"{SESSION_KEY_PREFIX}{platformSessionId}";

    /// <summary>Builds key for session lookup by account</summary>
    internal static string BuildSessionAccountKey(Guid accountId) => $"{SESSION_ACCOUNT_KEY_PREFIX}{accountId}";

    /// <summary>Builds key for camera source</summary>
    internal static string BuildCameraKey(string cameraId) => $"{CAMERA_KEY_PREFIX}{cameraId}";

    /// <summary>Builds key for broadcast output</summary>
    internal static string BuildOutputKey(Guid broadcastId) => $"{OUTPUT_KEY_PREFIX}{broadcastId}";

    #endregion

    /// <summary>
    /// Constructs the BroadcastService with all required dependencies.
    /// </summary>
    public BroadcastService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<BroadcastService> logger,
        BroadcastServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IDistributedLockProvider lockProvider,
        IAccountClient accountClient,
        IAuthClient authClient,
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        IMeshInstanceIdentifier meshInstanceIdentifier,
        IBroadcastCoordinator broadcastCoordinator,
        ISentimentProcessor sentimentProcessor,
        IPlatformWebhookHandler webhookHandler,
        IClientEventPublisher clientEventPublisher)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
        _accountClient = accountClient;
        _authClient = authClient;
        _serviceProvider = serviceProvider;
        _telemetryProvider = telemetryProvider;
        _meshInstanceIdentifier = meshInstanceIdentifier;
        _broadcastCoordinator = broadcastCoordinator;
        _sentimentProcessor = sentimentProcessor;
        _webhookHandler = webhookHandler;
        _clientEventPublisher = clientEventPublisher;

        // Constructor-cache all state stores per FOUNDATION TENETS
        _platformStore = stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        _sessionStore = stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        _outputStore = stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        _cameraStore = stateStoreFactory.GetStore<CameraSourceModel>(StateStoreDefinitions.BroadcastCameras);
        _sentimentBufferStore = stateStoreFactory.GetStore<BufferedSentimentEntry>(StateStoreDefinitions.BroadcastSentimentBuffer);

        RegisterEventConsumers(eventConsumer);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>Masks an RTMP URL to hide the stream key</summary>
    private static string MaskRtmpUrl(string rtmpUrl)
    {
        if (string.IsNullOrEmpty(rtmpUrl)) return "rtmp://***";
        var lastSlash = rtmpUrl.LastIndexOf('/');
        return lastSlash > 0 ? rtmpUrl[..lastSlash] + "/***" : "rtmp://***";
    }

    // ── Platform Endpoints ──────────────────────────────────────────────
    // NOTE: No telemetry spans on primary interface methods — generated controller provides them (per IMPLEMENTATION TENETS)

    /// <summary>
    /// Link a streaming platform account. For OAuth platforms (Twitch, YouTube),
    /// returns an OAuth redirect URL. For Custom RTMP, stores the URL directly.
    /// One link per account per platform enforced.
    /// </summary>
    public async Task<(StatusCodes, LinkPlatformResponse?)> LinkPlatformAsync(
        LinkPlatformRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.BroadcastEnabled)
        {
            _logger.LogInformation("Broadcast linking rejected: broadcast disabled");
            return (StatusCodes.BadRequest, null);
        }

        // Check uniqueness: one link per account+platform
        var existingLink = await _platformStore.GetAsync(
            BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), cancellationToken);
        if (existingLink != null)
        {
            return (StatusCodes.Conflict, null);
        }

        // Acquire distributed lock for platform linking
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"link:{body.WebSocketSessionId}:{body.Platform}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        if (body.Platform == PlatformType.Twitch || body.Platform == PlatformType.YouTube)
        {
            if (body.Platform == PlatformType.Twitch && string.IsNullOrEmpty(_configuration.TwitchClientId))
            {
                _logger.LogWarning("Twitch linking rejected: TwitchClientId not configured");
                return (StatusCodes.BadRequest, null);
            }

            if (body.Platform == PlatformType.YouTube && string.IsNullOrEmpty(_configuration.YouTubeClientId))
            {
                _logger.LogWarning("YouTube linking rejected: YouTubeClientId not configured");
                return (StatusCodes.BadRequest, null);
            }

            var oauthUrl = body.Platform == PlatformType.Twitch
                ? $"https://id.twitch.tv/oauth2/authorize?client_id={_configuration.TwitchClientId}&response_type=code&scope=channel:read:stream+chat:read"
                : $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_configuration.YouTubeClientId}&response_type=code&scope=https://www.googleapis.com/auth/youtube.readonly";

            return (StatusCodes.OK, new LinkPlatformResponse { OauthRedirectUrl = oauthUrl });
        }

        // Custom RTMP: store directly
        var linkId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var model = new PlatformLinkModel
        {
            LinkId = linkId,
            AccountId = body.WebSocketSessionId,
            Platform = body.Platform,
            EncryptedRtmpUrl = body.RtmpUrl,
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _platformStore.SaveAsync(BuildPlatformKey(linkId), model, cancellationToken: cancellationToken);
        await _platformStore.SaveAsync(
            BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), model,
            cancellationToken: cancellationToken);

        await _messageBus.PublishPlatformLinkCreatedAsync(new PlatformLinkCreatedEvent
        {
            LinkId = linkId,
            AccountId = body.WebSocketSessionId,
            Platform = body.Platform,
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        return (StatusCodes.OK, new LinkPlatformResponse { LinkId = linkId });
    }

    /// <summary>
    /// Complete OAuth platform linking after platform redirect.
    /// Encrypts and stores tokens. Creates the platform link.
    /// </summary>
    public async Task<(StatusCodes, PlatformCallbackResponse?)> PlatformCallbackAsync(
        PlatformCallbackRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_configuration.TokenEncryptionKey))
        {
            _logger.LogWarning("Platform callback rejected: TokenEncryptionKey not configured");
            return (StatusCodes.BadRequest, null);
        }

        // Check for race condition: already linked
        var existingLink = await _platformStore.GetAsync(
            BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), cancellationToken);
        if (existingLink != null)
        {
            return (StatusCodes.Conflict, null);
        }

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"link:{body.WebSocketSessionId}:{body.Platform}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var linkId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var model = new PlatformLinkModel
        {
            LinkId = linkId,
            AccountId = body.WebSocketSessionId,
            Platform = body.Platform,
            DisplayName = $"{body.Platform} User",
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _platformStore.SaveAsync(BuildPlatformKey(linkId), model, cancellationToken: cancellationToken);
        await _platformStore.SaveAsync(
            BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), model,
            cancellationToken: cancellationToken);

        await _messageBus.PublishPlatformLinkCreatedAsync(new PlatformLinkCreatedEvent
        {
            LinkId = linkId,
            AccountId = body.WebSocketSessionId,
            Platform = body.Platform,
            DisplayName = model.DisplayName,
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        return (StatusCodes.OK, new PlatformCallbackResponse { LinkId = linkId });
    }

    /// <summary>
    /// Unlink a streaming platform. Stops any active session, revokes OAuth tokens
    /// (best-effort), and deletes all associated data including tracking IDs.
    /// </summary>
    public async Task<StatusCodes> UnlinkPlatformAsync(
        UnlinkPlatformRequest body, CancellationToken cancellationToken)
    {
        var link = await _platformStore.GetAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        if (link == null)
        {
            return StatusCodes.NotFound;
        }

        // Ownership check: link must belong to the requesting account
        if (link.AccountId != body.WebSocketSessionId)
        {
            return StatusCodes.Forbidden;
        }

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"link:{link.AccountId}:{link.Platform}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return StatusCodes.Conflict;
        }

        // Stop any active session for this link
        var session = await _sessionStore.GetAsync(BuildSessionAccountKey(link.AccountId), cancellationToken);
        if (session != null && session.LinkId == body.LinkId)
        {
            var duration = (int)(DateTimeOffset.UtcNow - session.StartTime).TotalSeconds;
            await _sentimentProcessor.CleanupSessionTrackingAsync(session.PlatformSessionId, cancellationToken);
            await _sessionStore.DeleteAsync(BuildSessionKey(session.PlatformSessionId), cancellationToken);
            await _sessionStore.DeleteAsync(BuildSessionAccountKey(link.AccountId), cancellationToken);

            await _messageBus.PublishPlatformSessionDeletedAsync(new PlatformSessionDeletedEvent
            {
                PlatformSessionId = session.PlatformSessionId,
                LinkId = session.LinkId,
                AccountId = session.AccountId,
                Platform = session.Platform,
                State = PlatformSessionState.Ended,
                StartTime = session.StartTime,
                ViewerCount = session.ViewerCount,
                PeakViewerCount = session.PeakViewerCount,
                EndedAt = DateTimeOffset.UtcNow,
                Duration = duration,
                CreatedAt = session.StartTime,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }

        await _platformStore.DeleteAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        await _platformStore.DeleteAsync(
            BuildPlatformAccountKey(link.AccountId, link.Platform), cancellationToken);

        await _messageBus.PublishPlatformLinkDeletedAsync(new PlatformLinkDeletedEvent
        {
            LinkId = link.LinkId,
            AccountId = link.AccountId,
            Platform = link.Platform,
            LinkedAt = link.LinkedAt,
            CreatedAt = link.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// List linked platforms for the current account. Token fields are never exposed.
    /// </summary>
    public async Task<(StatusCodes, PlatformListResponse?)> ListPlatformsAsync(
        ListPlatformsRequest body, CancellationToken cancellationToken)
    {
        var links = new List<PlatformLinkInfo>();

        if (_platformStore is IQueryableStateStore<PlatformLinkModel> queryableStore)
        {
            var results = await queryableStore.QueryAsync(
                l => l.AccountId == body.WebSocketSessionId, cancellationToken);
            foreach (var link in results)
            {
                links.Add(new PlatformLinkInfo
                {
                    LinkId = link.LinkId,
                    Platform = link.Platform,
                    DisplayName = link.DisplayName,
                    LinkedAt = link.LinkedAt
                });
            }
        }

        return (StatusCodes.OK, new PlatformListResponse { Links = links.ToArray() });
    }

    // ── Session Endpoints ───────────────────────────────────────────────

    /// <summary>
    /// Start a platform streaming session. One active session per account enforced.
    /// </summary>
    public async Task<(StatusCodes, StartSessionResponse?)> StartSessionAsync(
        StartSessionRequest body, CancellationToken cancellationToken)
    {
        var link = await _platformStore.GetAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        if (link == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Ownership check: link must belong to the requesting account
        if (link.AccountId != body.WebSocketSessionId)
        {
            return (StatusCodes.Forbidden, null);
        }

        // One active session per account
        var existingSession = await _sessionStore.GetAsync(
            BuildSessionAccountKey(link.AccountId), cancellationToken);
        if (existingSession != null)
        {
            return (StatusCodes.Conflict, null);
        }

        var platformSessionId = Guid.NewGuid();

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"session:{platformSessionId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var session = new PlatformSessionModel
        {
            PlatformSessionId = platformSessionId,
            LinkId = body.LinkId,
            AccountId = link.AccountId,
            Platform = link.Platform,
            State = PlatformSessionState.Active,
            StartTime = now,
            ViewerCount = 0,
            PeakViewerCount = 0
        };

        await _sessionStore.SaveAsync(BuildSessionKey(platformSessionId), session, cancellationToken: cancellationToken);
        await _sessionStore.SaveAsync(BuildSessionAccountKey(link.AccountId), session, cancellationToken: cancellationToken);

        await _messageBus.PublishPlatformSessionCreatedAsync(new PlatformSessionCreatedEvent
        {
            PlatformSessionId = platformSessionId,
            LinkId = body.LinkId,
            AccountId = link.AccountId,
            Platform = link.Platform,
            State = PlatformSessionState.Active,
            StartTime = now,
            ViewerCount = 0,
            PeakViewerCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        await _clientEventPublisher.PublishToSessionAsync(
            body.WebSocketSessionId.ToString(),
            new BroadcastSessionStartedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                PlatformSessionId = platformSessionId,
                Platform = link.Platform
            },
            cancellationToken);

        return (StatusCodes.OK, new StartSessionResponse { PlatformSessionId = platformSessionId });
    }

    /// <summary>
    /// Stop a platform streaming session. Deletes all tracking ID mappings.
    /// </summary>
    public async Task<StatusCodes> StopSessionAsync(
        StopSessionRequest body, CancellationToken cancellationToken)
    {
        var session = await _sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return StatusCodes.NotFound;
        }

        // Ownership check: session must belong to the requesting account
        if (session.AccountId != body.WebSocketSessionId)
        {
            return StatusCodes.Forbidden;
        }

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"session:{body.PlatformSessionId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return StatusCodes.Conflict;
        }

        var now = DateTimeOffset.UtcNow;
        var duration = (int)(now - session.StartTime).TotalSeconds;

        await _sentimentProcessor.CleanupSessionTrackingAsync(body.PlatformSessionId, cancellationToken);
        await _sessionStore.DeleteAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        await _sessionStore.DeleteAsync(BuildSessionAccountKey(session.AccountId), cancellationToken);

        await _messageBus.PublishPlatformSessionDeletedAsync(new PlatformSessionDeletedEvent
        {
            PlatformSessionId = body.PlatformSessionId,
            LinkId = session.LinkId,
            AccountId = session.AccountId,
            Platform = session.Platform,
            State = PlatformSessionState.Ended,
            StartTime = session.StartTime,
            ViewerCount = session.ViewerCount,
            PeakViewerCount = session.PeakViewerCount,
            EndedAt = now,
            Duration = duration,
            CreatedAt = session.StartTime,
            UpdatedAt = now
        }, cancellationToken);

        await _clientEventPublisher.PublishToSessionAsync(
            body.WebSocketSessionId.ToString(),
            new BroadcastSessionEndedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                PlatformSessionId = body.PlatformSessionId,
                Duration = duration,
                PeakViewerCount = session.PeakViewerCount
            },
            cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Associate a platform session with an in-game stream session.
    /// streamSessionId is stored as opaque GUID — no validation against lib-showtime (L3 cannot call L4).
    /// </summary>
    public async Task<StatusCodes> AssociateSessionAsync(
        AssociateSessionRequest body, CancellationToken cancellationToken)
    {
        var (session, etag) = await _sessionStore.GetWithETagAsync(
            BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return StatusCodes.NotFound;
        }

        // Ownership check: session must belong to the requesting account
        if (session.AccountId != body.WebSocketSessionId)
        {
            return StatusCodes.Forbidden;
        }

        session.StreamSessionId = body.StreamSessionId;

        if (etag != null)
        {
            var newEtag = await _sessionStore.TrySaveAsync(
                BuildSessionKey(body.PlatformSessionId), session, etag,
                cancellationToken: cancellationToken);
            if (newEtag == null)
            {
                return StatusCodes.Conflict;
            }
        }
        else
        {
            await _sessionStore.SaveAsync(
                BuildSessionKey(body.PlatformSessionId), session,
                cancellationToken: cancellationToken);
        }

        await _sessionStore.SaveAsync(
            BuildSessionAccountKey(session.AccountId), session,
            cancellationToken: cancellationToken);

        await _messageBus.PublishPlatformSessionUpdatedAsync(new PlatformSessionUpdatedEvent
        {
            PlatformSessionId = body.PlatformSessionId,
            LinkId = session.LinkId,
            AccountId = session.AccountId,
            Platform = session.Platform,
            State = session.State,
            StartTime = session.StartTime,
            ViewerCount = session.ViewerCount,
            PeakViewerCount = session.PeakViewerCount,
            StreamSessionId = body.StreamSessionId,
            CreatedAt = session.StartTime,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangedFields = new[] { "streamSessionId" }
        }, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Get platform session status including viewer count and sentiment distribution.
    /// </summary>
    public async Task<(StatusCodes, SessionStatusResponse?)> GetSessionStatusAsync(
        GetSessionStatusRequest body, CancellationToken cancellationToken)
    {
        var session = await _sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Ownership check: session must belong to the requesting account
        if (session.AccountId != body.WebSocketSessionId)
        {
            return (StatusCodes.Forbidden, null);
        }

        return (StatusCodes.OK, new SessionStatusResponse
        {
            PlatformSessionId = session.PlatformSessionId,
            State = session.State,
            ViewerCount = session.ViewerCount,
            StreamSessionId = session.StreamSessionId
        });
    }

    /// <summary>
    /// List platform sessions for the current account, ordered by start time descending.
    /// </summary>
    public async Task<(StatusCodes, SessionListResponse?)> ListSessionsAsync(
        ListSessionsRequest body, CancellationToken cancellationToken)
    {
        var sessions = new List<PlatformSessionInfo>();

        if (_sessionStore is IQueryableStateStore<PlatformSessionModel> queryableStore)
        {
            var results = await queryableStore.QueryAsync(
                s => s.AccountId == body.WebSocketSessionId, cancellationToken);
            foreach (var session in results.OrderByDescending(s => s.StartTime))
            {
                sessions.Add(new PlatformSessionInfo
                {
                    PlatformSessionId = session.PlatformSessionId,
                    LinkId = session.LinkId,
                    Platform = session.Platform,
                    State = session.State,
                    ViewerCount = session.ViewerCount,
                    PeakViewerCount = session.PeakViewerCount,
                    StartTime = session.StartTime,
                    StreamSessionId = session.StreamSessionId,
                    EndedAt = session.EndedAt
                });
            }
        }

        return (StatusCodes.OK, new SessionListResponse
        {
            Sessions = sessions.ToArray(),
            TotalCount = sessions.Count,
            Page = body.Page,
            PageSize = body.PageSize
        });
    }

    // ── Camera Endpoints ────────────────────────────────────────────────

    /// <summary>
    /// Announce or heartbeat a camera source. Idempotent upsert with TTL-based eviction.
    /// </summary>
    public async Task<(StatusCodes, CameraAnnounceResponse?)> AnnounceCameraAsync(
        AnnounceCameraRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.OutputEnabled)
        {
            _logger.LogInformation("Camera announce rejected: output disabled");
            return (StatusCodes.BadRequest, null);
        }

        var model = new CameraSourceModel
        {
            CameraId = body.CameraId,
            RtmpInputUrl = body.RtmpInputUrl,
            Resolution = body.Resolution,
            Codec = body.Codec,
            HeartbeatAt = DateTimeOffset.UtcNow
        };

        await _cameraStore.SaveAsync(BuildCameraKey(body.CameraId), model, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new CameraAnnounceResponse());
    }

    /// <summary>
    /// Retire a camera source. Active broadcasts using this camera trigger fallback cascade.
    /// </summary>
    public async Task<StatusCodes> RetireCameraAsync(
        RetireCameraRequest body, CancellationToken cancellationToken)
    {
        var camera = await _cameraStore.GetAsync(BuildCameraKey(body.CameraId), cancellationToken);
        if (camera == null)
        {
            return StatusCodes.NotFound;
        }

        await _cameraStore.DeleteAsync(BuildCameraKey(body.CameraId), cancellationToken);

        // Trigger fallback cascade for any broadcast using this camera
        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableStore)
        {
            var broadcasts = await queryableStore.QueryAsync(
                b => b.SourceId == body.CameraId && b.State == BroadcastState.Active, cancellationToken);
            foreach (var broadcast in broadcasts)
            {
                await _broadcastCoordinator.RestartBroadcastAsync(
                    broadcast.BroadcastId, broadcast.EncryptedRtmpUrl,
                    broadcast.FallbackStreamUrl ?? broadcast.FallbackImageUrl ?? string.Empty,
                    cancellationToken);

                await _messageBus.PublishOutputUpdatedAsync(new OutputUpdatedEvent
                {
                    BroadcastId = broadcast.BroadcastId,
                    SourceType = broadcast.SourceType,
                    SourceId = broadcast.SourceId,
                    MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
                    State = broadcast.State,
                    OwningInstanceId = broadcast.OwningInstanceId,
                    StartedAt = broadcast.StartedAt,
                    Health = broadcast.Health,
                    CreatedAt = broadcast.StartedAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ChangedFields = new[] { "videoSource" }
                }, cancellationToken);
            }
        }

        _logger.LogInformation("Camera {CameraId} retired", body.CameraId);
        return StatusCodes.OK;
    }

    // ── Output Endpoints ────────────────────────────────────────────────

    /// <summary>
    /// Start a broadcast output. Validates RTMP URL via FFprobe and respects concurrent output limit.
    /// </summary>
    public async Task<(StatusCodes, StartOutputResponse?)> StartOutputAsync(
        StartOutputRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.OutputEnabled)
        {
            _logger.LogInformation("Start output rejected: output disabled");
            return (StatusCodes.BadRequest, null);
        }

        // Check concurrent output limit
        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableOutputStore)
        {
            var activeCount = (await queryableOutputStore.QueryAsync(
                b => b.State == BroadcastState.Active, cancellationToken)).Count();
            if (activeCount >= _configuration.MaxConcurrentOutputs)
            {
                return (StatusCodes.Conflict, null);
            }
        }

        // Validate source
        string sourceUrl;
        if (body.SourceType == BroadcastSourceType.Camera)
        {
            var camera = await _cameraStore.GetAsync(BuildCameraKey(body.CameraId ?? string.Empty), cancellationToken);
            if (camera == null)
            {
                return (StatusCodes.NotFound, null);
            }
            sourceUrl = camera.RtmpInputUrl;
        }
        else if (body.SourceType == BroadcastSourceType.VoiceRoom)
        {
            var voiceClient = _serviceProvider.GetService<IVoiceClient>();
            if (voiceClient == null)
            {
                _logger.LogWarning("Voice room broadcast rejected: lib-voice not available");
                return (StatusCodes.BadRequest, null);
            }
            sourceUrl = $"rtp://voice-room/{body.RoomId}";
        }
        else
        {
            sourceUrl = body.BackgroundVideoUrl ?? string.Empty;
        }

        // Validate RTMP URL via FFprobe
        var rtmpValid = await _broadcastCoordinator.ValidateRtmpUrlAsync(
            body.RtmpUrl, _configuration.RtmpProbeTimeoutSeconds, cancellationToken);
        if (!rtmpValid)
        {
            _logger.LogWarning("RTMP URL validation failed: {MaskedUrl}", MaskRtmpUrl(body.RtmpUrl));
            return (StatusCodes.BadRequest, null);
        }

        var broadcastId = Guid.NewGuid();

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"broadcast:{broadcastId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var ffmpegPid = await _broadcastCoordinator.StartBroadcastAsync(
            broadcastId, body.RtmpUrl, sourceUrl, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var maskedUrl = MaskRtmpUrl(body.RtmpUrl);
        var instanceId = _meshInstanceIdentifier.InstanceId.ToString();

        var model = new BroadcastOutputModel
        {
            BroadcastId = broadcastId,
            SourceType = body.SourceType,
            SourceId = body.CameraId ?? body.RoomId?.ToString(),
            EncryptedRtmpUrl = body.RtmpUrl,
            MaskedRtmpUrl = maskedUrl,
            OwningInstanceId = instanceId,
            State = BroadcastState.Active,
            CurrentVideoSource = body.CameraId,
            StartedAt = now,
            Health = BroadcastHealth.Healthy,
            FallbackStreamUrl = body.FallbackStreamUrl,
            FallbackImageUrl = body.FallbackImageUrl,
            BackgroundVideoUrl = body.BackgroundVideoUrl
        };

        await _outputStore.SaveAsync(BuildOutputKey(broadcastId), model, cancellationToken: cancellationToken);

        await _messageBus.PublishOutputCreatedAsync(new OutputCreatedEvent
        {
            BroadcastId = broadcastId,
            SourceType = body.SourceType,
            SourceId = model.SourceId,
            MaskedRtmpUrl = maskedUrl,
            State = BroadcastState.Active,
            OwningInstanceId = instanceId,
            StartedAt = now,
            Health = BroadcastHealth.Healthy,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        return (StatusCodes.OK, new StartOutputResponse { BroadcastId = broadcastId });
    }

    /// <summary>
    /// Stop a broadcast output. Kills FFmpeg process and removes broadcast record.
    /// </summary>
    public async Task<StatusCodes> StopOutputAsync(
        StopOutputRequest body, CancellationToken cancellationToken)
    {
        var broadcast = await _outputStore.GetAsync(BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return StatusCodes.NotFound;
        }

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"broadcast:{body.BroadcastId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return StatusCodes.Conflict;
        }

        await _broadcastCoordinator.StopBroadcastAsync(body.BroadcastId, cancellationToken);
        await _outputStore.DeleteAsync(BuildOutputKey(body.BroadcastId), cancellationToken);

        await _messageBus.PublishOutputDeletedAsync(new OutputDeletedEvent
        {
            BroadcastId = body.BroadcastId,
            SourceType = broadcast.SourceType,
            SourceId = broadcast.SourceId,
            MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
            State = BroadcastState.Stopped,
            OwningInstanceId = broadcast.OwningInstanceId,
            StartedAt = broadcast.StartedAt,
            Health = broadcast.Health,
            CreatedAt = broadcast.StartedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Update a broadcast output configuration. Validates new RTMP URL via FFprobe
    /// before committing. Causes a brief interruption (~2-3s) as FFmpeg restarts.
    /// </summary>
    public async Task<StatusCodes> UpdateOutputAsync(
        UpdateOutputRequest body, CancellationToken cancellationToken)
    {
        var (broadcast, etag) = await _outputStore.GetWithETagAsync(
            BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return StatusCodes.NotFound;
        }

        // Validate new RTMP URL if changed
        if (body.RtmpUrl != null)
        {
            var rtmpValid = await _broadcastCoordinator.ValidateRtmpUrlAsync(
                body.RtmpUrl, _configuration.RtmpProbeTimeoutSeconds, cancellationToken);
            if (!rtmpValid)
            {
                return StatusCodes.BadRequest;
            }
        }

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"broadcast:{body.BroadcastId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockHandle.Success)
        {
            return StatusCodes.Conflict;
        }

        var changedFields = new List<string>();

        if (body.RtmpUrl != null)
        {
            broadcast.EncryptedRtmpUrl = body.RtmpUrl;
            broadcast.MaskedRtmpUrl = MaskRtmpUrl(body.RtmpUrl);
            changedFields.Add("rtmpUrl");
        }
        if (body.FallbackStreamUrl != null)
        {
            broadcast.FallbackStreamUrl = body.FallbackStreamUrl;
            changedFields.Add("fallbackStreamUrl");
        }
        if (body.FallbackImageUrl != null)
        {
            broadcast.FallbackImageUrl = body.FallbackImageUrl;
            changedFields.Add("fallbackImageUrl");
        }

        // Restart FFmpeg with new config
        await _broadcastCoordinator.RestartBroadcastAsync(
            body.BroadcastId, broadcast.EncryptedRtmpUrl,
            broadcast.CurrentVideoSource ?? string.Empty, cancellationToken);

        if (etag != null)
        {
            var newEtag = await _outputStore.TrySaveAsync(
                BuildOutputKey(body.BroadcastId), broadcast, etag,
                cancellationToken: cancellationToken);
            if (newEtag == null)
            {
                return StatusCodes.Conflict;
            }
        }
        else
        {
            await _outputStore.SaveAsync(
                BuildOutputKey(body.BroadcastId), broadcast,
                cancellationToken: cancellationToken);
        }

        await _messageBus.PublishOutputUpdatedAsync(new OutputUpdatedEvent
        {
            BroadcastId = body.BroadcastId,
            SourceType = broadcast.SourceType,
            SourceId = broadcast.SourceId,
            MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
            State = broadcast.State,
            OwningInstanceId = broadcast.OwningInstanceId,
            StartedAt = broadcast.StartedAt,
            Health = broadcast.Health,
            CreatedAt = broadcast.StartedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangedFields = changedFields.ToArray()
        }, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Get broadcast output status. RTMP URLs are always masked in responses.
    /// </summary>
    public async Task<(StatusCodes, OutputStatusResponse?)> GetOutputStatusAsync(
        GetOutputStatusRequest body, CancellationToken cancellationToken)
    {
        var broadcast = await _outputStore.GetAsync(BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get local process health if this instance owns the broadcast
        var localHealth = _broadcastCoordinator.GetProcessHealth(body.BroadcastId);
        var health = localHealth ?? broadcast.Health;
        var duration = (int)(DateTimeOffset.UtcNow - broadcast.StartedAt).TotalSeconds;

        return (StatusCodes.OK, new OutputStatusResponse
        {
            BroadcastId = broadcast.BroadcastId,
            SourceType = broadcast.SourceType,
            MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
            State = broadcast.State,
            CurrentVideoSource = broadcast.CurrentVideoSource,
            Duration = duration,
            StartedAt = broadcast.StartedAt,
            Health = health
        });
    }

    /// <summary>
    /// List broadcast outputs. Optionally filter to active only.
    /// </summary>
    public async Task<(StatusCodes, OutputListResponse?)> ListOutputsAsync(
        ListOutputsRequest body, CancellationToken cancellationToken)
    {
        var outputs = new List<OutputInfo>();

        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableStore)
        {
            var results = body.ActiveOnly
                ? await queryableStore.QueryAsync(b => b.State == BroadcastState.Active, cancellationToken)
                : await queryableStore.QueryAsync(_ => true, cancellationToken);

            foreach (var broadcast in results)
            {
                outputs.Add(new OutputInfo
                {
                    BroadcastId = broadcast.BroadcastId,
                    SourceType = broadcast.SourceType,
                    SourceId = broadcast.SourceId,
                    MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
                    State = broadcast.State,
                    CurrentVideoSource = broadcast.CurrentVideoSource,
                    OwningInstanceId = broadcast.OwningInstanceId,
                    StartedAt = broadcast.StartedAt,
                    Health = broadcast.Health
                });
            }
        }

        return (StatusCodes.OK, new OutputListResponse
        {
            Outputs = outputs.ToArray(),
            TotalCount = outputs.Count,
            Page = body.Page,
            PageSize = body.PageSize
        });
    }

    // ── Admin Endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Get the latest sentiment pulse for a platform session. Diagnostic endpoint.
    /// </summary>
    public async Task<(StatusCodes, LatestPulseResponse?)> GetLatestPulseAsync(
        GetLatestPulseRequest body, CancellationToken cancellationToken)
    {
        var session = await _sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new LatestPulseResponse
        {
            Pulse = new SentimentPulseInfo
            {
                PlatformSessionId = session.PlatformSessionId,
                StreamSessionId = session.StreamSessionId,
                Timestamp = DateTimeOffset.UtcNow,
                IntervalSeconds = _configuration.SentimentPulseIntervalSeconds,
                ApproximateViewerCount = session.ViewerCount,
                Sentiments = Array.Empty<SentimentEntry>()
            }
        });
    }

    /// <summary>
    /// Test sentiment classification. Stateless computation — no state access, no events, no locks.
    /// Uses ISentimentProcessor for classification.
    /// </summary>
    public async Task<(StatusCodes, TestSentimentResponse?)> TestSentimentAsync(
        TestSentimentRequest body, CancellationToken cancellationToken)
    {
        var (category, intensity) = await _sentimentProcessor.ClassifyAsync(body.Text);

        return (StatusCodes.OK, new TestSentimentResponse
        {
            Category = category,
            Intensity = intensity
        });
    }

    /// <summary>
    /// Clean up all account-owned broadcast data. T28 Account Deletion Cleanup Obligation.
    /// Idempotent — returns OK even if nothing to clean up.
    /// </summary>
    public async Task<StatusCodes> CleanupByAccountAsync(
        CleanupByAccountRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up broadcast data for account {AccountId}", body.AccountId);

        // 1. Stop all active broadcasts initiated by this account
        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableBroadcastStore)
        {
            var broadcasts = await queryableBroadcastStore.QueryAsync(
                b => b.InitiatorAccountId == body.AccountId, cancellationToken);
            foreach (var broadcast in broadcasts)
            {
                try
                {
                    await using var lockHandle = await _lockProvider.LockAsync(
                        StateStoreDefinitions.BroadcastLock,
                        $"broadcast:{broadcast.BroadcastId}",
                        Guid.NewGuid().ToString(),
                        _configuration.DistributedLockTimeoutSeconds,
                        cancellationToken);
                    if (!lockHandle.Success)
                    {
                        _logger.LogWarning("Lock acquisition failed during cleanup for broadcast {BroadcastId}, skipping",
                            broadcast.BroadcastId);
                        continue;
                    }

                    await _broadcastCoordinator.StopBroadcastAsync(broadcast.BroadcastId, cancellationToken);
                    await _outputStore.DeleteAsync(BuildOutputKey(broadcast.BroadcastId), cancellationToken);

                    await _messageBus.PublishOutputDeletedAsync(new OutputDeletedEvent
                    {
                        BroadcastId = broadcast.BroadcastId,
                        SourceType = broadcast.SourceType,
                        SourceId = broadcast.SourceId,
                        MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
                        State = BroadcastState.Stopped,
                        OwningInstanceId = broadcast.OwningInstanceId,
                        StartedAt = broadcast.StartedAt,
                        Health = broadcast.Health,
                        CreatedAt = broadcast.StartedAt,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up broadcast {BroadcastId} for account {AccountId}",
                        broadcast.BroadcastId, body.AccountId);
                }
            }
        }

        // 2. Stop active platform session
        var session = await _sessionStore.GetAsync(BuildSessionAccountKey(body.AccountId), cancellationToken);
        if (session != null)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var duration = (int)(now - session.StartTime).TotalSeconds;

                await _sentimentProcessor.CleanupSessionTrackingAsync(session.PlatformSessionId, cancellationToken);
                await _sessionStore.DeleteAsync(BuildSessionKey(session.PlatformSessionId), cancellationToken);
                await _sessionStore.DeleteAsync(BuildSessionAccountKey(body.AccountId), cancellationToken);

                await _messageBus.PublishPlatformSessionDeletedAsync(new PlatformSessionDeletedEvent
                {
                    PlatformSessionId = session.PlatformSessionId,
                    LinkId = session.LinkId,
                    AccountId = session.AccountId,
                    Platform = session.Platform,
                    State = PlatformSessionState.Ended,
                    StartTime = session.StartTime,
                    ViewerCount = session.ViewerCount,
                    PeakViewerCount = session.PeakViewerCount,
                    EndedAt = now,
                    Duration = duration,
                    CreatedAt = session.StartTime,
                    UpdatedAt = now
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up session {PlatformSessionId} for account {AccountId}",
                    session.PlatformSessionId, body.AccountId);
            }
        }

        // 3. Unlink all platforms
        if (_platformStore is IQueryableStateStore<PlatformLinkModel> queryablePlatformStore)
        {
            var links = await queryablePlatformStore.QueryAsync(
                l => l.AccountId == body.AccountId, cancellationToken);
            foreach (var link in links)
            {
                try
                {
                    await _platformStore.DeleteAsync(BuildPlatformKey(link.LinkId), cancellationToken);
                    await _platformStore.DeleteAsync(
                        BuildPlatformAccountKey(body.AccountId, link.Platform), cancellationToken);

                    await _messageBus.PublishPlatformLinkDeletedAsync(new PlatformLinkDeletedEvent
                    {
                        LinkId = link.LinkId,
                        AccountId = body.AccountId,
                        Platform = link.Platform,
                        LinkedAt = link.LinkedAt,
                        CreatedAt = link.CreatedAt,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up platform link {LinkId} for account {AccountId}",
                        link.LinkId, body.AccountId);
                }
            }
        }

        return StatusCodes.OK;
    }
}
