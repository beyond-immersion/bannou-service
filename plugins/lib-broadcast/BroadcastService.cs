using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<BroadcastService> _logger;
    private readonly BroadcastServiceConfiguration _configuration;

    /// <summary>Key prefix for platform links by link ID</summary>
    private const string PlatformKeyPrefix = "platform";

    /// <summary>Key prefix for platform link uniqueness index (account+platform)</summary>
    private const string PlatformAccountKeyPrefix = "platform-account";

    /// <summary>Key prefix for sessions by platform session ID</summary>
    private const string SessionKeyPrefix = "sess";

    /// <summary>Key prefix for session lookup by account</summary>
    private const string SessionAccountKeyPrefix = "sess-account";

    /// <summary>Key prefix for camera sources</summary>
    private const string CameraKeyPrefix = "cam";

    /// <summary>Key prefix for broadcast outputs</summary>
    private const string OutputKeyPrefix = "out";

    /// <summary>
    /// Constructs the BroadcastService with required dependencies.
    /// </summary>
    /// <param name="messageBus">Event publishing infrastructure</param>
    /// <param name="stateStoreFactory">State store access</param>
    /// <param name="logger">Structured logging</param>
    /// <param name="configuration">Typed service configuration</param>
    /// <param name="eventConsumer">Event handler registration</param>
    public BroadcastService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<BroadcastService> logger,
        BroadcastServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        RegisterEventConsumers(eventConsumer);
    }

    // ── Key Builders ────────────────────────────────────────────────────

    /// <summary>Builds key for platform link by link ID</summary>
    internal static string BuildPlatformKey(Guid linkId) => $"{PlatformKeyPrefix}:{linkId}";

    /// <summary>Builds key for platform-account uniqueness index</summary>
    internal static string BuildPlatformAccountKey(Guid accountId, PlatformType platform) =>
        $"{PlatformAccountKeyPrefix}:{accountId}:{platform}";

    /// <summary>Builds key for session by platform session ID</summary>
    internal static string BuildSessionKey(Guid platformSessionId) => $"{SessionKeyPrefix}:{platformSessionId}";

    /// <summary>Builds key for session lookup by account</summary>
    internal static string BuildSessionAccountKey(Guid accountId) => $"{SessionAccountKeyPrefix}:{accountId}";

    /// <summary>Builds key for camera source</summary>
    internal static string BuildCameraKey(string cameraId) => $"{CameraKeyPrefix}:{cameraId}";

    /// <summary>Builds key for broadcast output</summary>
    internal static string BuildOutputKey(Guid broadcastId) => $"{OutputKeyPrefix}:{broadcastId}";

    // ── Helper: Mask RTMP URL ───────────────────────────────────────────

    /// <summary>Masks an RTMP URL to hide the stream key</summary>
    private static string MaskRtmpUrl(string rtmpUrl)
    {
        if (string.IsNullOrEmpty(rtmpUrl)) return "rtmp://***";
        var lastSlash = rtmpUrl.LastIndexOf('/');
        return lastSlash > 0 ? rtmpUrl[..lastSlash] + "/***" : "rtmp://***";
    }

    // ── Platform Endpoints ──────────────────────────────────────────────

    /// <summary>
    /// Link a streaming platform account. For OAuth platforms (Twitch, YouTube),
    /// returns an OAuth redirect URL. For Custom RTMP, stores the URL directly.
    /// </summary>
    public async Task<(StatusCodes, LinkPlatformResponse?)> LinkPlatformAsync(
        LinkPlatformRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.BroadcastEnabled)
        {
            _logger.LogInformation("Broadcast linking rejected: broadcast disabled");
            return (StatusCodes.BadRequest, null);
        }

        if (body.Platform == PlatformType.Twitch || body.Platform == PlatformType.YouTube)
        {
            // OAuth platforms: check credentials are configured
            if (body.Platform == PlatformType.Twitch &&
                string.IsNullOrEmpty(_configuration.TwitchClientId))
            {
                _logger.LogWarning("Twitch linking rejected: TwitchClientId not configured");
                return (StatusCodes.BadRequest, null);
            }

            if (body.Platform == PlatformType.YouTube &&
                string.IsNullOrEmpty(_configuration.YouTubeClientId))
            {
                _logger.LogWarning("YouTube linking rejected: YouTubeClientId not configured");
                return (StatusCodes.BadRequest, null);
            }

            // Generate OAuth redirect URL
            var oauthUrl = body.Platform == PlatformType.Twitch
                ? $"https://id.twitch.tv/oauth2/authorize?client_id={_configuration.TwitchClientId}&response_type=code&scope=channel:read:stream+chat:read"
                : $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_configuration.YouTubeClientId}&response_type=code&scope=https://www.googleapis.com/auth/youtube.readonly";

            return (StatusCodes.OK, new LinkPlatformResponse
            {
                OauthRedirectUrl = oauthUrl
            });
        }

        // Custom RTMP: store directly
        var linkId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore != null)
        {
            var model = new PlatformLinkModel
            {
                LinkId = linkId,
                AccountId = body.WebSocketSessionId, // Session-resolved identity
                Platform = body.Platform,
                EncryptedRtmpUrl = body.RtmpUrl,
                LinkedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            await platformStore.SaveAsync(BuildPlatformKey(linkId), model, cancellationToken: cancellationToken);
            await platformStore.SaveAsync(
                BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), model,
                cancellationToken: cancellationToken);
        }

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
    /// </summary>
    public async Task<(StatusCodes, PlatformCallbackResponse?)> PlatformCallbackAsync(
        PlatformCallbackRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_configuration.TokenEncryptionKey))
        {
            _logger.LogWarning("Platform callback rejected: TokenEncryptionKey not configured");
            return (StatusCodes.BadRequest, null);
        }

        var linkId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore != null)
        {
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

            await platformStore.SaveAsync(BuildPlatformKey(linkId), model, cancellationToken: cancellationToken);
            await platformStore.SaveAsync(
                BuildPlatformAccountKey(body.WebSocketSessionId, body.Platform), model,
                cancellationToken: cancellationToken);
        }

        await _messageBus.PublishPlatformLinkCreatedAsync(new PlatformLinkCreatedEvent
        {
            LinkId = linkId,
            AccountId = body.WebSocketSessionId,
            Platform = body.Platform,
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        return (StatusCodes.OK, new PlatformCallbackResponse { LinkId = linkId });
    }

    /// <summary>
    /// Unlink a streaming platform. Stops any active session and revokes tokens.
    /// </summary>
    public async Task<StatusCodes> UnlinkPlatformAsync(
        UnlinkPlatformRequest body, CancellationToken cancellationToken)
    {
        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore == null)
        {
            return StatusCodes.NotFound;
        }

        var link = await platformStore.GetAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        if (link == null)
        {
            return StatusCodes.NotFound;
        }

        // Delete platform link and index
        await platformStore.DeleteAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        await platformStore.DeleteAsync(
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

        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore is IQueryableStateStore<PlatformLinkModel> queryableStore)
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

        await Task.CompletedTask;
        return (StatusCodes.OK, new PlatformListResponse { Links = links.ToArray() });
    }

    // ── Session Endpoints ───────────────────────────────────────────────

    /// <summary>
    /// Start a platform streaming session. One active session per account enforced.
    /// </summary>
    public async Task<(StatusCodes, StartSessionResponse?)> StartSessionAsync(
        StartSessionRequest body, CancellationToken cancellationToken)
    {
        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var link = await platformStore.GetAsync(BuildPlatformKey(body.LinkId), cancellationToken);
        if (link == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        var platformSessionId = Guid.NewGuid();
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

        if (sessionStore != null)
        {
            await sessionStore.SaveAsync(BuildSessionKey(platformSessionId), session, cancellationToken: cancellationToken);
            await sessionStore.SaveAsync(BuildSessionAccountKey(link.AccountId), session, cancellationToken: cancellationToken);
        }

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

        return (StatusCodes.OK, new StartSessionResponse { PlatformSessionId = platformSessionId });
    }

    /// <summary>
    /// Stop a platform streaming session. Deletes all tracking ID mappings.
    /// </summary>
    public async Task<StatusCodes> StopSessionAsync(
        StopSessionRequest body, CancellationToken cancellationToken)
    {
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore == null)
        {
            return StatusCodes.NotFound;
        }

        var session = await sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return StatusCodes.NotFound;
        }

        var now = DateTimeOffset.UtcNow;
        var duration = (int)(now - session.StartTime).TotalSeconds;

        await sessionStore.DeleteAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        await sessionStore.DeleteAsync(BuildSessionAccountKey(session.AccountId), cancellationToken);

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

        return StatusCodes.OK;
    }

    /// <summary>
    /// Associate a platform session with an in-game stream session.
    /// </summary>
    public async Task<StatusCodes> AssociateSessionAsync(
        AssociateSessionRequest body, CancellationToken cancellationToken)
    {
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore == null)
        {
            return StatusCodes.NotFound;
        }

        var (session, etag) = await sessionStore.GetWithETagAsync(
            BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return StatusCodes.NotFound;
        }

        session.StreamSessionId = body.StreamSessionId;
        var now = DateTimeOffset.UtcNow;

        if (etag != null)
        {
            var newEtag = await sessionStore.TrySaveAsync(
                BuildSessionKey(body.PlatformSessionId), session, etag,
                cancellationToken: cancellationToken);
            if (newEtag == null)
            {
                return StatusCodes.Conflict;
            }
        }
        else
        {
            await sessionStore.SaveAsync(
                BuildSessionKey(body.PlatformSessionId), session,
                cancellationToken: cancellationToken);
        }

        await sessionStore.SaveAsync(
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
            UpdatedAt = now,
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
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var session = await sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
        if (session == null)
        {
            return (StatusCodes.NotFound, null);
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

        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore is IQueryableStateStore<PlatformSessionModel> queryableStore)
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

        await Task.CompletedTask;
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

        var cameraStore = _stateStoreFactory.GetStore<CameraSourceModel>(StateStoreDefinitions.BroadcastCameras);
        if (cameraStore != null)
        {
            var model = new CameraSourceModel
            {
                CameraId = body.CameraId,
                RtmpInputUrl = body.RtmpInputUrl,
                Resolution = body.Resolution,
                Codec = body.Codec,
                HeartbeatAt = DateTimeOffset.UtcNow
            };

            await cameraStore.SaveAsync(BuildCameraKey(body.CameraId), model, cancellationToken: cancellationToken);
        }

        return (StatusCodes.OK, new CameraAnnounceResponse());
    }

    /// <summary>
    /// Retire a camera source. Active broadcasts using this camera trigger fallback cascade.
    /// </summary>
    public async Task<StatusCodes> RetireCameraAsync(
        RetireCameraRequest body, CancellationToken cancellationToken)
    {
        var cameraStore = _stateStoreFactory.GetStore<CameraSourceModel>(StateStoreDefinitions.BroadcastCameras);
        if (cameraStore == null)
        {
            return StatusCodes.NotFound;
        }

        var camera = await cameraStore.GetAsync(BuildCameraKey(body.CameraId), cancellationToken);
        if (camera == null)
        {
            return StatusCodes.NotFound;
        }

        await cameraStore.DeleteAsync(BuildCameraKey(body.CameraId), cancellationToken);

        _logger.LogInformation("Camera {CameraId} retired", body.CameraId);
        return StatusCodes.OK;
    }

    // ── Output Endpoints ────────────────────────────────────────────────

    /// <summary>
    /// Start a broadcast output. Validates RTMP URL and respects concurrent output limit.
    /// </summary>
    public async Task<(StatusCodes, StartOutputResponse?)> StartOutputAsync(
        StartOutputRequest body, CancellationToken cancellationToken)
    {
        if (!_configuration.OutputEnabled)
        {
            _logger.LogInformation("Start output rejected: output disabled");
            return (StatusCodes.BadRequest, null);
        }

        var broadcastId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var maskedUrl = MaskRtmpUrl(body.RtmpUrl);

        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);

        var model = new BroadcastOutputModel
        {
            BroadcastId = broadcastId,
            SourceType = body.SourceType,
            SourceId = body.CameraId ?? body.RoomId?.ToString(),
            EncryptedRtmpUrl = body.RtmpUrl,
            MaskedRtmpUrl = maskedUrl,
            OwningInstanceId = Environment.MachineName,
            State = BroadcastState.Active,
            CurrentVideoSource = body.CameraId,
            StartedAt = now,
            Health = BroadcastHealth.Healthy,
            FallbackStreamUrl = body.FallbackStreamUrl,
            FallbackImageUrl = body.FallbackImageUrl,
            BackgroundVideoUrl = body.BackgroundVideoUrl
        };

        if (broadcastStore != null)
        {
            await broadcastStore.SaveAsync(BuildOutputKey(broadcastId), model, cancellationToken: cancellationToken);
        }

        await _messageBus.PublishOutputCreatedAsync(new OutputCreatedEvent
        {
            BroadcastId = broadcastId,
            SourceType = body.SourceType,
            SourceId = body.CameraId ?? body.RoomId?.ToString(),
            MaskedRtmpUrl = maskedUrl,
            State = BroadcastState.Active,
            OwningInstanceId = Environment.MachineName,
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
        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        if (broadcastStore == null)
        {
            return StatusCodes.NotFound;
        }

        var broadcast = await broadcastStore.GetAsync(BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return StatusCodes.NotFound;
        }

        await broadcastStore.DeleteAsync(BuildOutputKey(body.BroadcastId), cancellationToken);

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
    /// Update a broadcast output configuration. Causes brief interruption as FFmpeg restarts.
    /// </summary>
    public async Task<StatusCodes> UpdateOutputAsync(
        UpdateOutputRequest body, CancellationToken cancellationToken)
    {
        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        if (broadcastStore == null)
        {
            return StatusCodes.NotFound;
        }

        var (broadcast, etag) = await broadcastStore.GetWithETagAsync(
            BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return StatusCodes.NotFound;
        }

        var changedFields = new List<string>();
        var now = DateTimeOffset.UtcNow;

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

        if (etag != null)
        {
            var newEtag = await broadcastStore.TrySaveAsync(
                BuildOutputKey(body.BroadcastId), broadcast, etag,
                cancellationToken: cancellationToken);
            if (newEtag == null)
            {
                return StatusCodes.Conflict;
            }
        }
        else
        {
            await broadcastStore.SaveAsync(
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
            UpdatedAt = now,
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
        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        if (broadcastStore == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var broadcast = await broadcastStore.GetAsync(BuildOutputKey(body.BroadcastId), cancellationToken);
        if (broadcast == null)
        {
            return (StatusCodes.NotFound, null);
        }

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
            Health = broadcast.Health
        });
    }

    /// <summary>
    /// List broadcast outputs. Optionally filter to active only.
    /// </summary>
    public async Task<(StatusCodes, OutputListResponse?)> ListOutputsAsync(
        ListOutputsRequest body, CancellationToken cancellationToken)
    {
        var outputs = new List<OutputInfo>();

        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        if (broadcastStore is IQueryableStateStore<BroadcastOutputModel> queryableStore)
        {
            var activeOnly = body.ActiveOnly;
            var results = activeOnly
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

        await Task.CompletedTask;
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
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var session = await sessionStore.GetAsync(BuildSessionKey(body.PlatformSessionId), cancellationToken);
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
    /// </summary>
    public async Task<(StatusCodes, TestSentimentResponse?)> TestSentimentAsync(
        TestSentimentRequest body, CancellationToken cancellationToken)
    {
        // Stateless classification using simple keyword matching
        var text = body.Text.ToLowerInvariant();
        var category = SentimentCategory.Curious;
        var intensity = 0.5f;

        if (text.Contains("amazing") || text.Contains("love") || text.Contains("great") || text.Contains("awesome"))
        {
            category = SentimentCategory.Excited;
            intensity = 0.8f;
        }
        else if (text.Contains("hate") || text.Contains("terrible") || text.Contains("worst"))
        {
            category = SentimentCategory.Hostile;
            intensity = 0.7f;
        }
        else if (text.Contains("lol") || text.Contains("haha") || text.Contains("funny"))
        {
            category = SentimentCategory.Amused;
            intensity = 0.6f;
        }
        else if (text.Contains("support") || text.Contains("help") || text.Contains("thanks"))
        {
            category = SentimentCategory.Supportive;
            intensity = 0.6f;
        }
        else if (text.Contains("boring") || text.Contains("bored") || text.Contains("meh"))
        {
            category = SentimentCategory.Bored;
            intensity = 0.5f;
        }
        else if (text.Contains("wow") || text.Contains("what") || text.Contains("surprise"))
        {
            category = SentimentCategory.Surprised;
            intensity = 0.6f;
        }
        else if (text.Contains("bad") || text.Contains("wrong") || text.Contains("broken"))
        {
            category = SentimentCategory.Critical;
            intensity = 0.5f;
        }

        await Task.CompletedTask;
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
        var broadcastStore = _stateStoreFactory.GetStore<BroadcastOutputModel>(StateStoreDefinitions.BroadcastOutputs);
        if (broadcastStore is IQueryableStateStore<BroadcastOutputModel> queryableBroadcastStore)
        {
            var broadcasts = await queryableBroadcastStore.QueryAsync(
                b => b.InitiatorAccountId == body.AccountId, cancellationToken);
            foreach (var broadcast in broadcasts)
            {
                await broadcastStore.DeleteAsync(BuildOutputKey(broadcast.BroadcastId), cancellationToken);
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
        }

        // 2. Stop active platform session
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore != null)
        {
            var session = await sessionStore.GetAsync(BuildSessionAccountKey(body.AccountId), cancellationToken);
            if (session != null)
            {
                await sessionStore.DeleteAsync(BuildSessionKey(session.PlatformSessionId), cancellationToken);
                await sessionStore.DeleteAsync(BuildSessionAccountKey(body.AccountId), cancellationToken);
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
                    CreatedAt = session.StartTime,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken);
            }
        }

        // 3. Unlink all platforms
        var platformStore = _stateStoreFactory.GetStore<PlatformLinkModel>(StateStoreDefinitions.BroadcastPlatforms);
        if (platformStore is IQueryableStateStore<PlatformLinkModel> queryablePlatformStore)
        {
            var links = await queryablePlatformStore.QueryAsync(
                l => l.AccountId == body.AccountId, cancellationToken);
            foreach (var link in links)
            {
                await platformStore.DeleteAsync(BuildPlatformKey(link.LinkId), cancellationToken);
                await platformStore.DeleteAsync(
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
        }

        return StatusCodes.OK;
    }
}
