# Broadcast Implementation Map

> **Plugin**: lib-broadcast
> **Schema**: schemas/broadcast-api.yaml
> **Layer**: AppFeatures
> **Deep Dive**: [docs/plugins/BROADCAST.md](../plugins/BROADCAST.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-broadcast |
| Layer | L3 AppFeatures |
| Endpoints | 22 (19 generated + 3 x-controller-only) |
| State Stores | broadcast-platforms (MySQL), broadcast-sessions (Redis), broadcast-sentiment-buffer (Redis), broadcast-outputs (Redis), broadcast-cameras (Redis), broadcast-lock (Redis) |
| Events Published | 10 (broadcast.platform-link.created/updated/deleted, broadcast.platform-session.created/updated/deleted, broadcast.output.created/updated/deleted, broadcast.audience.pulse) |
| Events Consumed | 4 (voice.room.broadcast.approved, voice.room.broadcast.stopped, voice.participant.muted, session.disconnected) |
| Client Events | 5 (broadcast.output.started/stopped/source-changed, broadcast.session.started/ended) |
| Background Services | 6 |

---

## State

**Store**: `broadcast-platforms` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `platform:{linkId}` | `PlatformLinkModel` | Primary lookup by link ID. Account reference, platform type, encrypted OAuth tokens, display name, linked timestamp. |
| `platform-account:{accountId}:{platform}` | `PlatformLinkModel` | Uniqueness index -- one link per account+platform combination |

**Store**: `broadcast-sessions` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sess:{platformSessionId}` | `PlatformSessionModel` | Active platform session state. Link reference, platform stream ID, start time, viewer count, optional streamSessionId. |
| `sess-account:{accountId}` | `PlatformSessionModel` | Active session lookup by account |
| `sess-tracking:{platformSessionId}:{hashedPlatformUserId}` | `TrackingIdEntry` | Tracked viewer mapping. Hashed non-reversible key to opaque tracking GUID. Session-scoped TTL. |

**Store**: `broadcast-sentiment-buffer` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sent:{platformSessionId}:{sequence}` | `BufferedSentimentEntry` | Individual sentiment entries awaiting batch publication. TTL = 2x pulse interval. |
| `webhook-rate:{platformSessionId}` | `string` (counter) | Defense-in-depth rate limit counter for webhook events per platform session. Atomic INCR with 60s TTL. Checked before sentiment processing in all webhook handlers. |

**Store**: `broadcast-outputs` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `out:{broadcastId}` | `BroadcastModel` | Authoritative broadcast state. Source type, encrypted RTMP URL, owning instance ID, FFmpeg PID, current video source, health, fallback config. |

**Store**: `broadcast-cameras` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cam:{cameraId}` | `CameraSourceModel` | Camera sources with TTL heartbeat. RTMP input URL, resolution, codec, last announce timestamp. |

**Store**: `broadcast-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `broadcast:lock:link:{accountId}:{platform}` | Platform linking operation lock |
| `broadcast:lock:session:{platformSessionId}` | Session mutation lock |
| `broadcast:lock:broadcast:{broadcastId}` | Broadcast mutation lock (start/stop/update) |
| `broadcast:lock:sentiment-publisher` | Sentiment batch publisher singleton lock |
| `broadcast:lock:token-refresh:{linkId}` | Per-link OAuth token refresh lock |
| `broadcast:lock:webhook-manager` | Webhook subscription manager singleton lock |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 6 state stores for platforms, sessions, sentiment buffer, outputs, cameras, locks |
| lib-state (IDistributedLockProvider) | L0 | Hard | 6 lock patterns for platform linking, session, broadcast, sentiment, token refresh, webhook |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle events, audience pulse events |
| lib-messaging (IEventConsumer) | L0 | Hard | Consuming voice broadcast and session disconnect events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation |
| lib-account (IAccountClient) | L1 | Hard | Validate account existence before platform linking |
| lib-auth (IAuthClient) | L1 | Hard | OAuth token validation for platform callbacks |
| lib-voice (IVoiceClient) | L3 | Soft | Query voice room RTP audio endpoint for broadcast source (voice room broadcasting unavailable when absent) |

**Resource cleanup**: Declares `x-references` with target `account`, source type `broadcast`, CASCADE policy. lib-resource calls `/broadcast/cleanup-by-account` on account deletion.

**Privacy boundary**: Raw platform data (chat text, usernames, platform user IDs) never leaves this service as identifiable data. Only anonymous sentiment values are published externally.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `broadcast.platform-link.created` | `PlatformLinkCreatedEvent` | Platform linked (OAuth callback or custom RTMP) |
| `broadcast.platform-link.updated` | `PlatformLinkUpdatedEvent` | Token refreshed (TokenRefreshWorker) |
| `broadcast.platform-link.deleted` | `PlatformLinkDeletedEvent` | Platform unlinked or account cleanup |
| `broadcast.platform-session.created` | `PlatformSessionCreatedEvent` | Session started |
| `broadcast.platform-session.updated` | `PlatformSessionUpdatedEvent` | Session associated with in-game session or viewer count update |
| `broadcast.platform-session.deleted` | `PlatformSessionDeletedEvent` | Session stopped or disconnect cleanup |
| `broadcast.output.created` | `OutputCreatedEvent` | Broadcast started (camera, game audio, or voice room) |
| `broadcast.output.updated` | `OutputUpdatedEvent` | Broadcast config changed or fallback cascade triggered |
| `broadcast.output.deleted` | `OutputDeletedEvent` | Broadcast stopped or account cleanup |
| `broadcast.audience.pulse` | `BroadcastAudiencePulseEvent` | SentimentBatchPublisher drains buffer (every SentimentPulseIntervalSeconds) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `voice.room.broadcast.approved` | `HandleVoiceBroadcastApprovedAsync` | Start RTMP output for voice room after consent. Connects to room's RTP audio. Soft -- no-op if lib-voice absent. |
| `voice.room.broadcast.stopped` | `HandleVoiceBroadcastStoppedAsync` | Stop RTMP output for voice room. Consent revoked or room closed. Soft -- no-op if lib-voice absent. |
| `voice.participant.muted` | `HandleVoiceParticipantMutedAsync` | Exclude/include muted participant audio from RTMP output mixing. Soft -- no-op if lib-voice absent. |
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Cleanup platform session on WebSocket disconnect. Prevents orphaned sessions. |

All consumed voice event models are redefined inline in `broadcast-events.yaml` (cannot `$ref` other service event files per Foundation Tenets).

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<BroadcastService>` | Structured logging |
| `BroadcastServiceConfiguration` | Typed configuration access (36 properties) |
| `IStateStoreFactory` | State store access (6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IEventConsumer` | Event handler registration |
| `IAccountClient` | Account validation (L1 hard) |
| `IAuthClient` | OAuth validation (L1 hard) |
| `IServiceProvider` | Runtime resolution of soft L3 dependencies |
| `ITelemetryProvider` | Span instrumentation |
| `IBroadcastCoordinator` | FFmpeg process supervision -- local process cache, NOT authoritative (Redis is truth). Startup reconciliation, fallback cascade, RTMP validation via FFprobe. |
| `ISentimentProcessor` | Raw platform events to sentiment category + intensity. Manages tracked viewer mapping in Redis. |
| `IPlatformWebhookHandler` | Platform-specific webhook HMAC/token validation and event routing |
| `IClientEventPublisher` | WebSocket client event publishing (5 client events: output started/stopped/source-changed, session started/ended) |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| LinkPlatform | POST /broadcast/platform/link | generated | user | platform (Custom only) | broadcast.platform-link.created (Custom only) |
| PlatformCallback | POST /broadcast/platform/callback | generated | user | platform, platform-account | broadcast.platform-link.created |
| UnlinkPlatform | POST /broadcast/platform/unlink | generated | user | platform, platform-account, sess, sess-account, tracking | broadcast.platform-link.deleted, broadcast.platform-session.deleted |
| ListPlatforms | POST /broadcast/platform/list | generated | user | - | - |
| StartSession | POST /broadcast/session/start | generated | user | sess, sess-account | broadcast.platform-session.created |
| StopSession | POST /broadcast/session/stop | generated | user | sess, sess-account, tracking | broadcast.platform-session.deleted |
| AssociateSession | POST /broadcast/session/associate | generated | user | sess, sess-account | broadcast.platform-session.updated |
| GetSessionStatus | POST /broadcast/session/status | generated | user | - | - |
| ListSessions | POST /broadcast/session/list | generated | user | - | - |
| AnnounceCamera | POST /broadcast/camera/announce | generated | developer | cam | - |
| RetireCamera | POST /broadcast/camera/retire | generated | developer | cam | broadcast.output.updated |
| StartOutput | POST /broadcast/output/start | generated | developer | out | broadcast.output.created |
| StopOutput | POST /broadcast/output/stop | generated | developer | out | broadcast.output.deleted |
| UpdateOutput | POST /broadcast/output/update | generated | developer | out | broadcast.output.updated |
| GetOutputStatus | POST /broadcast/output/status | generated | developer | - | - |
| ListOutputs | POST /broadcast/output/list | generated | developer | - | - |
| WebhookTwitch | POST /broadcast/webhook/twitch | x-controller-only | [] | sent | - |
| WebhookYouTube | POST /broadcast/webhook/youtube | x-controller-only | [] | sent | - |
| WebhookCustom | POST /broadcast/webhook/custom | x-controller-only | [] | sent | - |
| GetLatestPulse | POST /broadcast/admin/pulse/latest | generated | developer | - | - |
| TestSentiment | POST /broadcast/admin/sentiment/test | generated | developer | - | - |
| CleanupByAccount | POST /broadcast/cleanup-by-account | generated | [] | platform, sess, out, tracking | broadcast.platform-link.deleted, broadcast.platform-session.deleted, broadcast.output.deleted |

> **T32 note**: All user-facing endpoints (`Roles: [user]`) must NOT accept `accountId` in request bodies. The schema must use `webSocketSessionId` and resolve account server-side. The pseudo-code below uses `accountId` as shorthand for session-resolved identity.

---

## Methods

### LinkPlatform
POST /broadcast/platform/link | Roles: [user]

```
IF NOT config.BroadcastEnabled                        -> 400
CALL _accountClient.GetAccountAsync(body.accountId)   -> 404 if not found
READ platformStore:platform-account:{accountId}:{platform}
                                                      -> 409 if exists (already linked)
LOCK lockStore:broadcast:lock:link:{accountId}:{platform}
                                                      -> 409 if fails
  IF body.platform is Twitch or YouTube
    IF platform credentials not configured             -> 400
    // Generate OAuth state token, store pending link with TTL
    RETURN (200, PlatformLinkResponse { oauthRedirectUrl })

  IF body.platform is Custom
    // No OAuth -- store RTMP URL directly
    WRITE platformStore:platform:{linkId}              <- PlatformLinkModel from request
    WRITE platformStore:platform-account:{accountId}:{platform} <- PlatformLinkModel
    PUBLISH broadcast.platform-link.created { linkId, accountId, platform }
    RETURN (200, PlatformLinkResponse { linkId })
```

### PlatformCallback
POST /broadcast/platform/callback | Roles: [user]

```
// Validate OAuth state token matches pending flow
IF config.TokenEncryptionKey is null                  -> 400
// Exchange authorization code for tokens (external OAuth provider HTTP call)
READ platformStore:platform-account:{accountId}:{platform}
                                                      -> 409 if exists (race condition)
LOCK lockStore:broadcast:lock:link:{accountId}:{platform}
                                                      -> 409 if fails
  // Encrypt tokens with TokenEncryptionKey (AES-256)
  WRITE platformStore:platform:{linkId}               <- PlatformLinkModel { encrypted tokens, displayName }
  WRITE platformStore:platform-account:{accountId}:{platform} <- PlatformLinkModel
PUBLISH broadcast.platform-link.created { linkId, accountId, platform, displayName }
RETURN (200, PlatformCallbackResponse { linkId })
```

### UnlinkPlatform
POST /broadcast/platform/unlink | Roles: [user]

```
READ platformStore:platform:{linkId} [with ETag]      -> 404 if null
IF link.accountId != body.accountId                   -> 403
LOCK lockStore:broadcast:lock:link:{accountId}:{platform}
                                                      -> 409 if fails
  // Stop any active session for this link
  READ sessionStore:sess-account:{accountId}
  IF session exists AND session.linkId == linkId
    DELETE sessionStore:sess:{platformSessionId}
    DELETE sessionStore:sess-account:{accountId}
    // Delete all tracking ID mappings for session (prefix scan)
    PUBLISH broadcast.platform-session.deleted { platformSessionId, duration, peakViewerCount }

  // Revoke OAuth tokens on platform (external HTTP call, best-effort)
  DELETE platformStore:platform:{linkId}
  DELETE platformStore:platform-account:{accountId}:{platform}
PUBLISH broadcast.platform-link.deleted { linkId, accountId, platform }
RETURN (200, UnlinkResponse)
```

### ListPlatforms
POST /broadcast/platform/list | Roles: [user]

```
QUERY platformStore WHERE $.accountId == body.accountId
// Mask token fields in response (never expose encrypted tokens)
FOREACH link in results
  // Omit accessToken, refreshToken from response
RETURN (200, PlatformListResponse { links })
```

### StartSession
POST /broadcast/session/start | Roles: [user]

```
READ platformStore:platform:{linkId}                  -> 404 if null
IF link.accountId != body.accountId                   -> 403
READ sessionStore:sess-account:{accountId}            -> 409 if exists (already active)
LOCK lockStore:broadcast:lock:session:{platformSessionId}
                                                      -> 409 if fails
  // Verify account is live on platform (external platform API, best-effort)
  WRITE sessionStore:sess:{platformSessionId}         <- PlatformSessionModel { linkId, accountId, startTime, state: Active }
  WRITE sessionStore:sess-account:{accountId}         <- PlatformSessionModel
PUBLISH broadcast.platform-session.created { platformSessionId, linkId, accountId }
PUSH account(accountId) BroadcastSessionStartedClientEvent { platformSessionId, platform }
RETURN (200, SessionStartResponse { platformSessionId })
```

### StopSession
POST /broadcast/session/stop | Roles: [user]

```
READ sessionStore:sess:{platformSessionId} [with ETag]
                                                      -> 404 if null
IF session.accountId != body.accountId                -> 403
LOCK lockStore:broadcast:lock:session:{platformSessionId}
                                                      -> 409 if fails
  // Stop platform event ingestion
  // Delete all tracking ID mappings (prefix scan sess-tracking:{platformSessionId}:*)
  DELETE sessionStore:sess:{platformSessionId}
  DELETE sessionStore:sess-account:{accountId}
PUBLISH broadcast.platform-session.deleted { platformSessionId, duration, peakViewerCount }
PUSH account(accountId) BroadcastSessionEndedClientEvent { platformSessionId, duration }
RETURN (200, SessionStopResponse)
```

### AssociateSession
POST /broadcast/session/associate | Roles: [user]

```
READ sessionStore:sess:{platformSessionId} [with ETag]
                                                      -> 404 if null
IF session.accountId != body.accountId                -> 403
// streamSessionId is stored as opaque GUID -- no validation against lib-showtime (L3 cannot call L4)
session.streamSessionId = body.streamSessionId
ETAG-WRITE sessionStore:sess:{platformSessionId}     <- updated session
                                                      -> 409 if ETag mismatch
WRITE sessionStore:sess-account:{accountId}           <- updated session
PUBLISH broadcast.platform-session.updated { platformSessionId, changedFields: ["streamSessionId"] }
RETURN (200, AssociateResponse)
```

### GetSessionStatus
POST /broadcast/session/status | Roles: [user]

```
READ sessionStore:sess:{platformSessionId}            -> 404 if null
IF session.accountId != body.accountId                -> 403
// Compute sentiment distribution from recent data
RETURN (200, SessionStatusResponse { platformSessionId, state, viewerCount, streamSessionId, sentimentDistribution })
```

### ListSessions
POST /broadcast/session/list | Roles: [user]

```
QUERY sessionStore WHERE $.accountId == body.accountId ORDER BY $.startTime DESC PAGED(body.page, body.pageSize)
RETURN (200, SessionListResponse { sessions, totalCount, page, pageSize })
```

### AnnounceCamera
POST /broadcast/camera/announce | Roles: [developer]

```
IF NOT config.OutputEnabled                           -> 400
// Idempotent upsert -- re-announce updates TTL and metadata
WRITE cameraStore:cam:{cameraId}                      <- CameraSourceModel { rtmpInputUrl, resolution, codec, heartbeatAt: now }
// TTL-based eviction (cameras that stop announcing are auto-removed)
RETURN (200, CameraAnnounceResponse { cameraId })
```

### RetireCamera
POST /broadcast/camera/retire | Roles: [developer]

```
READ cameraStore:cam:{cameraId}                       -> 404 if null
DELETE cameraStore:cam:{cameraId}
// If any active broadcast uses this camera, trigger fallback cascade
FOREACH broadcast using this camera
  // Signal IBroadcastCoordinator to cascade to fallback source
  PUBLISH broadcast.output.updated { broadcastId, changedFields: ["videoSource"] }
  PUSH account(initiatorAccountId) BroadcastOutputSourceChangedClientEvent { broadcastId, newSource }
RETURN (200, CameraRetireResponse)
```

### StartOutput
POST /broadcast/output/start | Roles: [developer]

```
IF NOT config.OutputEnabled                           -> 400
// Check concurrent output limit
COUNT broadcastStore WHERE $.state == Active
IF count >= config.MaxConcurrentOutputs               -> 409

IF body.sourceType == Camera
  READ cameraStore:cam:{cameraId}                     -> 404 if null
IF body.sourceType == VoiceRoom
  // Resolve IVoiceClient via IServiceProvider (soft L3)
  IF voiceClient is null                              -> 400 (voice not available)
  CALL voiceClient.GetRoomAsync(body.roomId)          -> 400 if not found

// Validate RTMP URL via FFprobe (timeout: config.RtmpProbeTimeoutSeconds)
// IBroadcastCoordinator.ValidateRtmpUrlAsync                -> 400 if unreachable

LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
                                                      -> 409 if fails
  // Encrypt RTMP URL for storage
  // Start FFmpeg process via IBroadcastCoordinator
  WRITE broadcastStore:out:{broadcastId}              <- BroadcastModel { sourceType, encryptedRtmpUrl, owningInstanceId, ffmpegPid, state: Active, fallbackConfig }
PUBLISH broadcast.output.created { broadcastId, sourceType, maskedRtmpUrl }
PUSH account(initiatorAccountId) BroadcastOutputStartedClientEvent { broadcastId, sourceType }
RETURN (200, OutputStartResponse { broadcastId })
```

### StopOutput
POST /broadcast/output/stop | Roles: [developer]

```
READ broadcastStore:out:{broadcastId} [with ETag]     -> 404 if null
LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
                                                      -> 409 if fails
  // Kill FFmpeg process via IBroadcastCoordinator
  DELETE broadcastStore:out:{broadcastId}
PUBLISH broadcast.output.deleted { broadcastId }
PUSH account(initiatorAccountId) BroadcastOutputStoppedClientEvent { broadcastId, duration }
RETURN (200, OutputStopResponse)
```

### UpdateOutput
POST /broadcast/output/update | Roles: [developer]

```
READ broadcastStore:out:{broadcastId} [with ETag]     -> 404 if null
// Validate new RTMP URL via FFprobe before committing
// IBroadcastCoordinator.ValidateRtmpUrlAsync                -> 400 if unreachable
LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
                                                      -> 409 if fails
  // Restart FFmpeg with new config (causes ~2-3s interruption)
  // Encrypt new RTMP URL if changed
  ETAG-WRITE broadcastStore:out:{broadcastId}         <- updated broadcast
                                                      -> 409 if ETag mismatch
PUBLISH broadcast.output.updated { broadcastId, changedFields }
RETURN (200, OutputUpdateResponse)
```

### GetOutputStatus
POST /broadcast/output/status | Roles: [developer]

```
READ broadcastStore:out:{broadcastId}                 -> 404 if null
// Mask RTMP URL (stream key never exposed)
// Get local process health from IBroadcastCoordinator if this instance owns the broadcast
RETURN (200, OutputStatusResponse { broadcastId, sourceType, maskedRtmpUrl, state, currentVideoSource, duration, health })
```

### ListOutputs
POST /broadcast/output/list | Roles: [developer]

```
QUERY broadcastStore WHERE (NOT body.activeOnly OR $.state == Active) PAGED(body.page, body.pageSize)
FOREACH broadcast in results
  // Mask RTMP URLs
RETURN (200, OutputListResponse { outputs, totalCount, page, pageSize })
```

### WebhookTwitch
POST /broadcast/webhook/twitch | Roles: [] | x-controller-only: true

```
// HMAC validation in generated controller (x-controller-only: raw HttpContext access)
// IPlatformWebhookHandler.ValidateTwitchSignatureAsync      -> 401 if invalid

IF type == "webhook_callback_verification"
  RETURN (200, challenge)

IF type == "stream.offline"
  // Trigger session stop for this broadcaster internally

// Defense-in-depth rate limiting (Redis atomic counter, 60s TTL per session)
// Primary burst protection is architectural: sentiment buffer TTL + max batch size cap
var rateLimitKey = $"webhook-rate:{platformSessionId}"
var currentCount = INCR sentimentBuffer:rateLimitKey (TTL: 60s)
IF currentCount > config.WebhookMaxEventsPerSessionPerMinute
  LOG Debug "Webhook rate limit exceeded for session {PlatformSessionId}: {Count}/{Max}"
  RETURN (200, WebhookResponse)  // Always 200 to platform (per Twitch/YouTube webhook contracts)

IF type == "channel.subscribe" OR "channel.raid"
  CALL _sentimentProcessor.ProcessSubscriptionEventAsync(platformSessionId, eventData)
  // Writes BufferedSentimentEntry to sentimentBuffer:sent:{platformSessionId}:{sequence}

IF type == "channel.chat.message"
  CALL _sentimentProcessor.ProcessChatMessageAsync(platformSessionId, messageText, senderId, senderBadges)
  // Writes BufferedSentimentEntry to sentimentBuffer:sent:{platformSessionId}:{sequence}

RETURN (200, WebhookResponse)
```

### WebhookYouTube
POST /broadcast/webhook/youtube | Roles: [] | x-controller-only: true

```
// YouTube verification token validation in generated controller
// IPlatformWebhookHandler.ValidateYouTubeTokenAsync         -> 401 if invalid

// Defense-in-depth rate limiting (Redis atomic counter, 60s TTL per session)
var rateLimitKey = $"webhook-rate:{platformSessionId}"
var currentCount = INCR sentimentBuffer:rateLimitKey (TTL: 60s)
IF currentCount > config.WebhookMaxEventsPerSessionPerMinute
  LOG Debug "Webhook rate limit exceeded for session {PlatformSessionId}: {Count}/{Max}"
  RETURN (200, WebhookResponse)  // Always 200 to platform (per webhook contracts)

IF type == "liveChatMessage"
  CALL _sentimentProcessor.ProcessChatMessageAsync(platformSessionId, messageText, senderId, memberStatus)

IF type == "superChat"
  CALL _sentimentProcessor.ProcessSuperChatAsync(platformSessionId, amount, senderId)

IF type == "newSubscriber" OR "membershipEvent"
  CALL _sentimentProcessor.ProcessSubscriptionEventAsync(platformSessionId, eventData)

RETURN (200, WebhookResponse)
```

### WebhookCustom
POST /broadcast/webhook/custom | Roles: [] | x-controller-only: true

```
// Configurable HMAC validation in generated controller
// IPlatformWebhookHandler.ValidateCustomSignatureAsync      -> 401 if invalid

// Defense-in-depth rate limiting (Redis atomic counter, 60s TTL per session)
var rateLimitKey = $"webhook-rate:{platformSessionId}"
var currentCount = INCR sentimentBuffer:rateLimitKey (TTL: 60s)
IF currentCount > config.WebhookMaxEventsPerSessionPerMinute
  LOG Debug "Webhook rate limit exceeded for session {PlatformSessionId}: {Count}/{Max}"
  RETURN (200, WebhookResponse)  // Always 200 to platform (per webhook contracts)

CALL _sentimentProcessor.ProcessGenericWebhookAsync(platformSessionId, body)
RETURN (200, WebhookResponse)
```

### GetLatestPulse
POST /broadcast/admin/pulse/latest | Roles: [developer]

```
READ sessionStore:sess:{platformSessionId}            -> 404 if null
// Retrieve most recent published pulse (storage mechanism not yet specified)
RETURN (200, LatestPulseResponse { pulse })
```

### TestSentiment
POST /broadcast/admin/sentiment/test | Roles: [developer]

```
// Stateless computation -- no state access, no events, no locks
CALL _sentimentProcessor.ClassifyAsync(body.text)
RETURN (200, TestSentimentResponse { category, intensity })
```

### CleanupByAccount
POST /broadcast/cleanup-by-account | Roles: [] (internal -- called by lib-resource only)

```
// Resource cleanup (CASCADE on account deletion)

// 1. Stop all active broadcasts initiated by this account
QUERY broadcastStore WHERE $.initiatorAccountId == body.accountId
FOREACH broadcast in results
  LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
    // Kill FFmpeg via IBroadcastCoordinator
    DELETE broadcastStore:out:{broadcastId}
    PUBLISH broadcast.output.deleted { broadcastId }

// 2. Stop active platform session
READ sessionStore:sess-account:{accountId}
IF session exists
  DELETE sessionStore:sess:{platformSessionId}
  DELETE sessionStore:sess-account:{accountId}
  // Delete all tracking ID mappings (prefix scan)
  PUBLISH broadcast.platform-session.deleted { platformSessionId }

// 3. Unlink all platforms
QUERY platformStore WHERE $.accountId == body.accountId
FOREACH link in results
  DELETE platformStore:platform:{linkId}
  DELETE platformStore:platform-account:{accountId}:{platform}
  PUBLISH broadcast.platform-link.deleted { linkId, accountId }

RETURN (200, CleanupResponse)
// Idempotent -- returns 200 even if nothing to clean up
```

---

## Event Handlers

### HandleVoiceBroadcastApprovedAsync
Topic: `voice.room.broadcast.approved` | Source: lib-voice (L3, soft)

```
// All voice room participants have consented to broadcasting
IF NOT config.OutputEnabled                           -> no-op
IF NOT config.BroadcastEnabled                        -> no-op

// Validate RTMP URL via FFprobe
CALL _broadcastCoordinator.ValidateRtmpUrlAsync(body.rtmpDestinationUrl, config.RtmpProbeTimeoutSeconds)
  IF fails                                            -> no-op (log error)

// Check concurrent output limit
COUNT broadcastStore WHERE $.state == Active
IF count >= config.MaxConcurrentOutputs               -> no-op (log warning)

broadcastId = new Guid

LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
  IF fails                                            -> no-op (log warning)

  // Resolve IVoiceClient (soft L3 dependency)
  voiceClient = _serviceProvider.GetService<IVoiceClient>()
  IF null                                             -> no-op (log error, voice unavailable)

  CALL voiceClient.GetRoomAsync(body.roomId)
  IF not found                                        -> no-op (log error)

  // Encrypt RTMP URL, start FFmpeg with RTP audio input from room
  CALL _broadcastCoordinator.StartBroadcastAsync(broadcastId, model)
  WRITE broadcastStore:out:{broadcastId}              <- BroadcastModel { sourceType: VoiceRoom, sourceId: roomId, encryptedRtmpUrl, owningInstanceId, state: Active }
PUBLISH broadcast.output.created { broadcastId, sourceType: VoiceRoom, maskedRtmpUrl }
```

### HandleVoiceBroadcastStoppedAsync
Topic: `voice.room.broadcast.stopped` | Source: lib-voice (L3, soft)

```
// Consent revoked, room closed, or manual stop from voice side
QUERY broadcastStore WHERE $.sourceType == VoiceRoom AND $.sourceId == body.roomId AND $.state == Active
IF no results                                         -> no-op (log debug)

broadcastId = results[0].broadcastId

LOCK lockStore:broadcast:lock:broadcast:{broadcastId}
  IF fails                                            -> no-op (log warning)
  CALL _broadcastCoordinator.StopBroadcastAsync(broadcastId)
  DELETE broadcastStore:out:{broadcastId}
PUBLISH broadcast.output.deleted { broadcastId }
```

### HandleVoiceParticipantMutedAsync
Topic: `voice.participant.muted` | Source: lib-voice (L3, soft)

```
// Participant muted/unmuted in a voice room being broadcast
QUERY broadcastStore WHERE $.sourceType == VoiceRoom AND $.sourceId == body.roomId AND $.state == Active
IF no results                                         -> no-op (log debug)

broadcastId = results[0].broadcastId

// Signal IBroadcastCoordinator to update FFmpeg audio mixing
// Muted participant's RTP stream excluded/included in the mix
CALL _broadcastCoordinator.UpdateParticipantMuteStateAsync(broadcastId, body.participantSessionId, body.isMuted)
// Transient state -- no Redis write, no event published
```

### HandleSessionDisconnectedAsync
Topic: `session.disconnected` | Source: lib-connect (L1, hard)

```
// WebSocket client disconnected -- accelerated cleanup (T28-compliant: sessions have TTL safety net)
READ sessionStore:sess-account:{accountId}
IF null                                               -> no-op (no platform session for this account)
IF session.state != Active                            -> no-op

// Delete all tracking ID mappings (prefix scan sess-tracking:{platformSessionId}:*)
DELETE sessionStore:sess:{platformSessionId}
DELETE sessionStore:sess-account:{accountId}
PUBLISH broadcast.platform-session.deleted { platformSessionId, duration, peakViewerCount }
// No client event push -- client is already disconnected
```

---

## Background Services

### TokenRefreshWorker
**Interval**: Per-platform (`config.TokenRefreshIntervalTwitchSeconds` / `config.TokenRefreshIntervalYouTubeSeconds`)
**Purpose**: Refresh OAuth tokens before expiry for all linked platforms.

```
QUERY platformStore WHERE $.platform == Twitch AND needsRefresh
FOREACH link in results
  LOCK lockStore:broadcast:lock:token-refresh:{linkId}
    IF lock fails -> SKIP (another instance refreshing this link)
    // Decrypt current tokens
    // Exchange refresh token via platform OAuth API
    // Encrypt new tokens
    WRITE platformStore:platform:{linkId}             <- updated encrypted tokens
    PUBLISH broadcast.platform-link.updated { linkId, changedFields: ["accessToken"] }

// Repeat for YouTube links with YouTube-specific interval
```

### WebhookSubscriptionManager
**Interval**: `config.WebhookRenewalIntervalSeconds` (3600s)
**Purpose**: Ensure Twitch EventSub and YouTube webhook subscriptions remain active.

```
LOCK lockStore:broadcast:lock:webhook-manager (TTL: config.WebhookManagerLockTimeoutSeconds)
  IF lock fails -> SKIP (another instance owns webhook management)

  QUERY platformStore WHERE $.platform == Twitch
  FOREACH link in results
    TRY
      // Check EventSub subscription status via Twitch API
      //   If subscription exists and is active -> no-op
      //   If subscription missing or expired -> create new subscription
      //   If subscription in "webhook_callback_verification_pending" -> log, await next cycle
    CATCH rate limit (HTTP 429)
      // Log warning with Retry-After header value, BREAK Twitch loop
      // Remaining links retry next cycle (periodic execution is built-in retry)
    CATCH API error (5xx, timeout, network failure)
      // Log warning per-link, CONTINUE to next link
      // Transient failures are self-healing: next cycle retries

  QUERY platformStore WHERE $.platform == YouTube
  FOREACH link in results
    TRY
      // Check push subscription via YouTube API
      // Re-subscribe if expired or missing
    CATCH rate limit (HTTP 429)
      // Log warning, BREAK YouTube loop (respect rate limit)
    CATCH API error (5xx, timeout, network failure)
      // Log warning per-link, CONTINUE to next link
```

**Error handling rationale**: Per-link try-catch ensures one failed platform call doesn't block remaining links. Rate limit responses (HTTP 429) halt the current platform's loop entirely to respect the platform's backpressure — remaining links retry next cycle. The periodic execution model (default 3600s) provides built-in retry for all transient failures. The singleton lock TTL (`WebhookManagerLockTimeoutSeconds`, default 300s) is configured shorter than the renewal interval to prevent subscription expiration during instance crashes — a crashed instance's lock expires, and another instance picks up webhook management within the lock TTL.

### SentimentBatchPublisher
**Interval**: `config.SentimentPulseIntervalSeconds` (15s)
**Purpose**: Drain sentiment buffer and publish audience pulse events.

```
LOCK lockStore:broadcast:lock:sentiment-publisher
  IF lock fails -> SKIP (another instance publishing this cycle)

  QUERY sessionStore WHERE $.state == Active
  FOREACH session in activeSessions
    QUERY sentimentBuffer WHERE $.platformSessionId == session.platformSessionId ORDER BY $.sequence ASC
    IF results.count < config.SentimentMinBatchSize   -> SKIP

    entries = results[0..config.SentimentMaxBatchSize]
    // Overflow: drop lowest-intensity entries beyond max

    PUBLISH broadcast.audience.pulse {
      eventId, platformSessionId, streamSessionId,
      timestamp, intervalSeconds, approximateViewerCount,
      sentiments: [{ category, intensity, trackingId?, viewerType? }]
    }

    FOREACH entry in published entries
      DELETE sentimentBuffer:sent:{platformSessionId}:{sequence}
```

### SessionCleanupWorker
**Interval**: `config.SessionCleanupIntervalSeconds` (3600s)
**Purpose**: Purge ended session records older than retention period.

```
QUERY sessionStore WHERE $.state == Ended AND $.endedAt < (now - config.SessionHistoryRetentionHours)
FOREACH staleSession in results
  DELETE sessionStore:sess:{platformSessionId}
```

### BroadcastHealthMonitor
**Interval**: `config.OutputHealthCheckIntervalSeconds` (10s)
**Purpose**: Monitor FFmpeg health, auto-restart on crash, detect stale records from crashed instances.

```
// Check locally-owned broadcasts
FOREACH broadcastId in IBroadcastCoordinator.LocalBroadcastIds
  READ broadcastStore:out:{broadcastId}
  IF null -> IBroadcastCoordinator.RemoveLocalHandle(broadcastId)
  ELSE
    health = IBroadcastCoordinator.GetProcessHealth(broadcastId)
    IF health.isDown AND config.OutputRestartOnFailure
      // Restart FFmpeg
      WRITE broadcastStore:out:{broadcastId}          <- updated health
      PUBLISH broadcast.output.updated { broadcastId, changedFields: ["health"] }
    IF health.videoSourceChanged
      WRITE broadcastStore:out:{broadcastId}          <- updated currentVideoSource
      PUBLISH broadcast.output.updated { broadcastId, changedFields: ["videoSource"] }
      PUSH account(initiatorAccountId) BroadcastOutputSourceChangedClientEvent { broadcastId, newSource }

// Detect stale broadcasts from crashed instances
QUERY broadcastStore WHERE $.state == Active AND $.owningInstanceId != thisInstanceId
FOREACH staleCandidate in results
  // If owning instance appears crashed (mesh health check)
  WRITE broadcastStore:out:{broadcastId}              <- state: Failed
  PUBLISH broadcast.output.updated { broadcastId, changedFields: ["state"] }
```

### AutoBroadcastStarter
**Interval**: Once (startup)
**Purpose**: Start auto-broadcast from configuration if both camera ID and RTMP URL are set.

```
IF config.AutoBroadcastCameraId is null               -> no-op
IF config.AutoBroadcastRtmpUrl is null                -> no-op
IF NOT config.OutputEnabled                           -> no-op

READ cameraStore:cam:{config.AutoBroadcastCameraId}
  IF null -> log error, RETURN (camera not yet announced, no retry in v1)

// Inline OutputStart logic (validate RTMP, start FFmpeg, write to Redis)
WRITE broadcastStore:out:{broadcastId}                <- BroadcastModel
PUBLISH broadcast.output.created { broadcastId, sourceType: Camera }
```

---

## Non-Standard Implementation Patterns

### Plugin Lifecycle

#### OnStartAsync

```
// TokenEncryptionKey validation (required when platform linking is enabled)
IF config.TwitchClientId is NOT null OR config.YouTubeClientId is NOT null
  IF config.TokenEncryptionKey is null OR config.TokenEncryptionKey.Length < 32
    THROW InvalidOperationException "BROADCAST_TOKEN_ENCRYPTION_KEY must be >= 32 chars when platform linking is enabled"
```

#### OnRunningAsync (IBroadcastCoordinator startup reconciliation)

```
// Read all active broadcasts from Redis (authoritative state)
QUERY broadcastStore WHERE $.state == Active
FOREACH broadcast in results
  IF broadcast.owningInstanceId == thisInstanceId
    // This instance crashed and restarted -- attempt to rebuild FFmpeg process
    CALL _broadcastCoordinator.RebuildOrRestartAsync(broadcast)
    IF failed
      WRITE broadcastStore:out:{broadcastId}          <- state: Failed
      PUBLISH broadcast.output.updated { broadcastId, changedFields: ["state"] }
  ELSE
    // Owned by different instance -- skip
    // BroadcastHealthMonitor detects stale records from crashed instances
    SKIP
```

### x-controller-only Webhook Endpoints

The three webhook endpoints (`WebhookTwitch`, `WebhookYouTube`, `WebhookCustom`) are declared `x-controller-only: true` in the schema. They require raw `HttpContext` access for platform-specific signature validation (Twitch HMAC-SHA256, YouTube verification token, custom HMAC) before model binding. The `IPlatformWebhookHandler` service performs validation at the controller level; the service body receives pre-validated payloads. These endpoints carry `x-permissions: []` (not exposed to WebSocket clients) and are internet-facing via NGINX — a justified T15 exception for platform callbacks.
