# Broadcast Plugin Deep Dive

> **Plugin**: lib-broadcast (not yet created)
> **Schema**: `schemas/broadcast-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: broadcast-platforms (MySQL), broadcast-sessions (Redis), broadcast-sentiment-buffer (Redis), broadcast-outputs (Redis), broadcast-cameras (Redis), broadcast-lock (Redis) — all planned
> **Layer**: AppFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: N/A

## Overview

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast. Game-agnostic: which platforms are enabled and how sentiment categories map to game emotions are configured via environment variables and API calls. Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks (justified T15 exception -- platform callbacks, not browser-facing).

---

## The Privacy Boundary

Real audience data (chat messages, usernames, platform IDs) NEVER leaves lib-broadcast's process boundary as identifiable data. Raw platform events are reduced to **batched sentiment pulses** -- arrays of anonymous sentiment values with optional opaque tracking GUIDs for consistency. No platform user IDs, no message content, no personally identifiable information enters the event system. This eliminates GDPR/CCPA data deletion obligations for downstream consumers entirely.

Every design decision in lib-broadcast flows from one principle: **real audience data creates compliance liability; anonymous sentiment data does not.**

### Why No Text Content Leaves lib-broadcast

1. **Data deletion compliance**: If a Twitch user requests data deletion under GDPR, we'd need to purge their messages from every downstream system (analytics, state stores, event logs). With sentiment-only data, there's nothing to delete -- the original text never left lib-broadcast.
2. **Analytics cache problem**: Analytics ingests events for aggregation. Flushing specific user data from analytical stores is operationally expensive and error-prone. Sentiment values have no user association.
3. **Legal exposure**: Storing third-party platform user content creates licensing and liability questions. Sentiment approximations are derived data, not reproductions.

### The Sentiment Pulse Model

lib-broadcast processes raw platform events (chat messages, subscriptions, raids, emotes) into periodic **sentiment pulses** -- batched arrays of anonymous sentiment data points.

```
SentimentPulse:
  eventId: Guid                      # Required event identifier (per event schema convention)
  streamSessionId: Guid              # The lib-showtime in-game session (if linked)
  platformSessionId: Guid            # The lib-broadcast platform session
  timestamp: DateTime                # When this pulse was assembled
  intervalSeconds: int               # Configured pulse interval
  approximateViewerCount: int        # Platform-reported viewer count (approximate)
  sentiments: SentimentEntry[]       # The batch
```

Each entry in the batch:

```
SentimentEntry:
  category: SentimentCategory         # Enum: Excited, Supportive, Critical, Curious,
                                      #        Surprised, Amused, Bored, Hostile
  intensity: float                    # 0.0 to 1.0 (strength of sentiment)
  trackingId: Guid?                   # null = anonymous, non-null = "important" viewer
  viewerType: TrackedViewerType?      # null = anonymous, non-null = role category
```

### Tracked Viewers ("Important" Sentiments)

Most sentiments in a pulse are anonymous -- just a category + intensity with no tracking information. A configurable subset of "important" viewers receive opaque tracking GUIDs:

| Tracked Viewer Type | Description |
|---------------------|-------------|
| `Subscriber` | Platform subscriber (Twitch sub, YouTube member) |
| `Moderator` | Platform moderator |
| `RaidLeader` | Led a raid into the stream |
| `VIP` | Platform VIP designation |
| `HighEngager` | Algorithmically determined high-engagement viewer |
| `Returner` | Has been present across multiple streaming sessions |

**How tracking IDs work**:

1. lib-broadcast maintains a **Redis-backed, session-scoped** mapping: `platformUserId → trackingId` in the `broadcast-sessions` store with session-scoped TTL
2. This mapping exists ONLY during an active platform session
3. When the session ends, the mapping is deleted from Redis -- the tracking IDs become orphaned
4. The tracking IDs are Bannou-generated GUIDs with NO relationship to platform user IDs
5. The same real viewer gets the SAME tracking ID across pulses within a session (consistency), even across multiple Bannou instances
6. But across sessions, they get a NEW tracking ID (no cross-session tracking)
7. There is NO way to reverse a tracking ID back to a platform user
8. Redis persistence preserves privacy properties: the mapping is ephemeral (TTL-bounded), non-reversible, and destroyed when the session ends -- identical privacy guarantees to in-memory, but with cross-instance consistency per IMPLEMENTATION TENETS

**Timing and batching rationale**: 15-second pulse intervals create enough delay that individual messages can't be correlated to specific sentiment entries by timing alone. Combined with batching (minimum 5, maximum 200 entries per pulse), this makes de-anonymization impractical even for someone monitoring both the platform chat and the sentiment stream.

---

## The RTMP Broadcast System

lib-broadcast manages FFmpeg processes for broadcasting content to RTMP endpoints (Twitch, YouTube, custom). This subsystem was originally designed as part of lib-voice and moved to lib-broadcast in the three-service architecture redesign.

**Two distinct broadcast modes**: Server-side content broadcasting (game cameras, game audio) requires no player consent -- it's game content. Voice room broadcasting to external platforms requires explicit consent from ALL room participants via lib-voice's broadcast consent flow. lib-broadcast subscribes to voice consent events and acts accordingly; it never initiates voice broadcasting directly.

### Broadcast Source Types

| Source Type | Input | Consent Required | Initiated By |
|-------------|-------|------------------|-------------|
| `Camera` | RTMP input from a game camera/engine | No (game content) | Admin API or ENV auto-broadcast config |
| `GameAudio` | Audio source from a game server | No (game content) | Admin API |
| `VoiceRoom` | RTP audio from a lib-voice room | Yes (all participants) | lib-voice broadcast consent event |

### Fallback Cascade

When the primary video source fails, lib-broadcast cascades through configured fallbacks:

```
Primary Video (backgroundVideoUrl)
  └─ Failed → Fallback Stream (fallbackStreamUrl)
       └─ Failed → Fallback Image (fallbackImageUrl)
            └─ Failed → Default Background (BROADCAST_DEFAULT_BACKGROUND_VIDEO)
                 └─ Failed → Black Video (lavfi color=black)
```

Each fallback transition is reported via `x-lifecycle` `BroadcastOutputUpdatedEvent` with `changedFields: ["videoSource"]` so consumers (lib-showtime L4) can react to degraded broadcast quality.

### Stream Key Security

RTMP URLs contain stream keys (e.g., `rtmp://live.twitch.tv/app/YOUR_SECRET_KEY`). lib-broadcast masks stream keys in ALL responses and log messages. The full URL is stored encrypted in the broadcast state store and only passed to FFmpeg process arguments. API responses show masked URLs (e.g., `rtmp://live.twitch.tv/app/****`).

---

## The Three-Service Architecture

Platform identity linking is owned here. Sentiment processing is owned here. RTMP output management is owned here. Audience behavior and the in-game metagame are lib-showtime (L4). Voice room management is lib-voice (L3). lib-broadcast is the privacy boundary and platform integration layer -- it touches external APIs so nothing else has to.

lib-broadcast delivers value independently. It can broadcast game content to Twitch whether or not there's voice involved (lib-voice) or an in-game metagame (lib-showtime). It can ingest platform audience data and publish sentiment pulses whether or not anything consumes them. Each service in the voice/broadcast/showtime trio composes beautifully but never requires the others.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Platform link records (MySQL), active session tracking (Redis), sentiment buffer (Redis), broadcast state (Redis), camera sources (Redis), distributed locks (Redis), tracking ID mapping (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for platform linking, session management, broadcast mutations, token refresh |
| lib-messaging (`IMessageBus`) | Publishing sentiment pulses, platform lifecycle events, broadcast lifecycle events |
| lib-messaging (`IEventConsumer`) | Registering handlers for voice broadcast consent events, voice mute events, session disconnect events |
| lib-account (`IAccountClient`) | Validate account existence for platform linking (L1) |
| lib-auth (`IAuthClient`) | OAuth token validation for platform callbacks (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-voice (`IVoiceClient`) | Query voice room RTP audio endpoint for broadcast source | Voice room broadcasting unavailable; camera/game audio broadcasting still works |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-showtime (L4) | Subscribes to `broadcast.audience.pulse` for real audience blending with simulated audiences |
| lib-showtime (L4) | Subscribes to `x-lifecycle` platform-session and broadcast-output events for session/broadcast awareness |

---

## State Storage

### Platform Link Store
**Store**: `broadcast-platforms` (Backend: MySQL, Table: `broadcast_platforms`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `platform:{linkId}` | `PlatformLinkModel` | Primary lookup by link ID. Stores account reference, platform type, encrypted OAuth tokens (access + refresh), platform display name, linked timestamp. |
| `platform-account:{accountId}:{platform}` | `PlatformLinkModel` | Uniqueness lookup per account+platform combination |

### Session Store
**Store**: `broadcast-sessions` (Backend: Redis, prefix: `broadcast:sess`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sess:{platformSessionId}` | `PlatformSessionModel` | Active platform session tracking. Stores link reference, platform stream ID, start time, viewer count, associated in-game session ID (nullable), session state. |
| `sess-account:{accountId}` | `PlatformSessionModel` | Active session lookup by account |
| `sess-tracking:{platformSessionId}:{hashedPlatformUserId}` | `TrackingIdEntry` | Tracked viewer mapping (hashed platform user ID → opaque tracking GUID). Session-scoped TTL. Privacy-safe: hashed input is non-reversible, TTL ensures cleanup. Per IMPLEMENTATION TENETS (multi-instance safety). |

### Sentiment Buffer Store
**Store**: `broadcast-sentiment-buffer` (Backend: Redis, prefix: `broadcast:sent`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sent:{platformSessionId}:{sequence}` | `BufferedSentimentEntry` | Individual sentiment entries awaiting batch publication. TTL-based cleanup (2x pulse interval). |

### Broadcast Store
**Store**: `broadcast-outputs` (Backend: Redis, prefix: `broadcast:out`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `out:{broadcastId}` | `BroadcastModel` | Authoritative broadcast state. Stores source type, encrypted RTMP URL, owning instance ID (from `IMeshInstanceIdentifier`), FFmpeg PID, current video source, health status, start time, fallback configuration. This is the source of truth for broadcast existence -- `IBroadcastCoordinator`'s in-memory process handle map is a non-authoritative local cache. |

### Camera Source Store
**Store**: `broadcast-cameras` (Backend: Redis, prefix: `broadcast:cam`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cam:{cameraId}` | `CameraSourceModel` | Camera sources registered via the `/broadcast/camera/announce` API endpoint. TTL-based (cameras must re-announce periodically). Stores RTMP input URL, resolution, codec, heartbeat timestamp. |

### Distributed Locks
**Store**: `broadcast-lock` (Backend: Redis, prefix: `"broadcast:lock"`)

| Key Pattern | Purpose |
|-------------|---------|
| `broadcast:lock:link:{accountId}:{platform}` | Platform linking operation lock (prevent duplicate OAuth flows) |
| `broadcast:lock:session:{platformSessionId}` | Session mutation lock |
| `broadcast:lock:broadcast:{broadcastId}` | Broadcast mutation lock (start/stop/update) |
| `broadcast:lock:sentiment-publisher` | Sentiment batch publisher singleton lock |
| `broadcast:lock:token-refresh:{linkId}` | Per-link token refresh lock (prevents concurrent refresh across instances per IMPLEMENTATION TENETS) |
| `broadcast:lock:webhook-manager` | Webhook subscription manager singleton lock (TTL: `WebhookManagerLockTimeoutSeconds`) |

---

## Events

### Published Events (via `x-lifecycle`)

Platform links, platform sessions, and broadcast outputs use `x-lifecycle` for CRUD lifecycle events (per FOUNDATION TENETS -- lifecycle events must never be manually defined). The `topic_prefix: broadcast` is required because this is a multi-entity service.

| Entity | Generated Events | Topic Pattern |
|--------|-----------------|---------------|
| `PlatformLink` | `PlatformLinkCreatedEvent`, `PlatformLinkUpdatedEvent`, `PlatformLinkDeletedEvent` | `broadcast.platform-link.created`, `broadcast.platform-link.updated`, `broadcast.platform-link.deleted` |
| `PlatformSession` | `PlatformSessionCreatedEvent`, `PlatformSessionUpdatedEvent`, `PlatformSessionDeletedEvent` | `broadcast.platform-session.created`, `broadcast.platform-session.updated`, `broadcast.platform-session.deleted` |
| `BroadcastOutput` | `BroadcastOutputCreatedEvent`, `BroadcastOutputUpdatedEvent`, `BroadcastOutputDeletedEvent` | `broadcast.broadcast-output.created`, `broadcast.broadcast-output.updated`, `broadcast.broadcast-output.deleted` |

**Note**: Fallback source changes publish via `BroadcastOutputUpdatedEvent` with `changedFields: ["videoSource"]` (per IMPLEMENTATION TENETS -- use `*.updated` with changedFields, not a separate event).

### Published Events (custom -- not lifecycle)

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `broadcast.audience.pulse` | `BroadcastAudiencePulseEvent` | Batched sentiment data from real audience (privacy-safe, no PII). Custom event because pulse publication is not CRUD lifecycle. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `voice.room.broadcast.approved` | `HandleVoiceBroadcastApprovedAsync` | Start RTMP output for a voice room after all participants consented. Connects to the room's RTP audio endpoint. (Soft -- no-op if lib-voice absent). Event model redefined inline in `broadcast-events.yaml` (per FOUNDATION TENETS -- events schemas cannot `$ref` other service event files). |
| `voice.room.broadcast.stopped` | `HandleVoiceBroadcastStoppedAsync` | Stop RTMP output for a voice room. Consent revoked or room closed. (Soft -- no-op if lib-voice absent). Event model redefined inline. |
| `voice.participant.muted` | `HandleVoiceParticipantMutedAsync` | Exclude/include muted participant's audio from RTMP output mixing. (Soft -- no-op if lib-voice absent). Event model redefined inline. |
| `session.disconnected` | `HandleSessionDisconnectedAsync` | Cleanup platform session when user drops WebSocket connection. Stops event ingestion, publishes session ended event. Prevents orphaned platform sessions. |

### Resource Cleanup (per FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| account | broadcast | CASCADE | `/broadcast/cleanup-by-account` |

**Schema requirement**: `broadcast-api.yaml` must declare `x-references` in the `info:` section:

```yaml
info:
  x-references:
    - target: account
      sourceType: broadcast
      field: accountId
      onDelete: cascade
      cleanup:
        endpoint: /broadcast/cleanup-by-account
        payloadTemplate: '{"accountId": "{{resourceId}}"}'
```

---

## Configuration

| Property | Env Var | Type | Default | Validation | Purpose |
|----------|---------|------|---------|------------|---------|
| `BroadcastEnabled` | `BROADCAST_ENABLED` | bool | `false` | | Master feature flag for the entire broadcast service |
| `TwitchClientId` | `BROADCAST_TWITCH_CLIENT_ID` | string? | *(null)* | | Twitch OAuth application client ID. Null = Twitch integration disabled. |
| `TwitchClientSecret` | `BROADCAST_TWITCH_CLIENT_SECRET` | string? | *(null)* | | Twitch OAuth application client secret |
| `TwitchWebhookSecret` | `BROADCAST_TWITCH_WEBHOOK_SECRET` | string? | *(null)* | | Twitch EventSub webhook signing secret |
| `YouTubeClientId` | `BROADCAST_YOUTUBE_CLIENT_ID` | string? | *(null)* | | YouTube OAuth application client ID. Null = YouTube integration disabled. |
| `YouTubeClientSecret` | `BROADCAST_YOUTUBE_CLIENT_SECRET` | string? | *(null)* | | YouTube OAuth application client secret |
| `YouTubeWebhookVerificationToken` | `BROADCAST_YOUTUBE_WEBHOOK_TOKEN` | string? | *(null)* | | YouTube webhook verification token |
| `TokenEncryptionKey` | `BROADCAST_TOKEN_ENCRYPTION_KEY` | string? | *(null)* | `minLength: 32` | AES-256 encryption key for OAuth token storage. Null = token encryption disabled (startup failure if platform linking is attempted). Must be 32+ characters. |
| `SentimentPulseIntervalSeconds` | `BROADCAST_SENTIMENT_PULSE_INTERVAL_SECONDS` | int | `15` | `minimum: 1`, `maximum: 300` | How often sentiment pulses are published |
| `SentimentMinBatchSize` | `BROADCAST_SENTIMENT_MIN_BATCH_SIZE` | int | `5` | `minimum: 1` | Minimum sentiments before publishing a pulse |
| `SentimentMaxBatchSize` | `BROADCAST_SENTIMENT_MAX_BATCH_SIZE` | int | `200` | `minimum: 1`, `maximum: 10000` | Maximum sentiments per pulse (overflow drops lowest-intensity) |
| `MaxTrackedViewersPerSession` | `BROADCAST_MAX_TRACKED_VIEWERS_PER_SESSION` | int | `50` | `minimum: 0`, `maximum: 10000` | Maximum tracked viewers with opaque GUIDs per session |
| `TrackedViewerEngagementThreshold` | `BROADCAST_TRACKED_VIEWER_ENGAGEMENT_THRESHOLD` | float | `0.7` | `minimum: 0.0`, `maximum: 1.0` | Minimum engagement score for HighEngager tracking |
| `TrackSubscribers` | `BROADCAST_TRACK_SUBSCRIBERS` | bool | `true` | | Whether platform subscribers get tracking GUIDs |
| `TrackModerators` | `BROADCAST_TRACK_MODERATORS` | bool | `true` | | Whether platform moderators get tracking GUIDs |
| `TrackRaidLeaders` | `BROADCAST_TRACK_RAID_LEADERS` | bool | `true` | | Whether raid leaders get tracking GUIDs |
| `SessionHistoryRetentionHours` | `BROADCAST_SESSION_HISTORY_RETENTION_HOURS` | int | `168` | `minimum: 1` | Hours to retain ended session records (1 week) |
| `OutputEnabled` | `BROADCAST_OUTPUT_ENABLED` | bool | `false` | | Feature flag for RTMP output capabilities |
| `FfmpegPath` | `BROADCAST_FFMPEG_PATH` | string | `/usr/bin/ffmpeg` | | Path to FFmpeg binary |
| `DefaultBackgroundVideo` | `BROADCAST_DEFAULT_BACKGROUND_VIDEO` | string | `/opt/bannou/backgrounds/default.mp4` | | Default video background for audio-only outputs |
| `MaxConcurrentOutputs` | `BROADCAST_MAX_CONCURRENT_OUTPUTS` | int | `10` | `minimum: 1`, `maximum: 100` | Maximum simultaneous FFmpeg output processes |
| `OutputAudioCodec` | `BROADCAST_OUTPUT_AUDIO_CODEC` | AudioCodec | `Aac` | enum | Audio codec for RTMP output. Enum: `Aac`, `Mp3`, `Opus`. Per FOUNDATION TENETS (T18 licensing), only LGPL-compliant codecs are valid enum values. |
| `OutputAudioBitrate` | `BROADCAST_OUTPUT_AUDIO_BITRATE` | string | `128k` | | Audio bitrate for RTMP output |
| `OutputVideoCodec` | `BROADCAST_OUTPUT_VIDEO_CODEC` | VideoCodec | `LibVpx` | enum | Video codec for RTMP output. Enum: `LibVpx`, `LibVpxVp9`. Per FOUNDATION TENETS (T18 licensing), only LGPL-compliant codecs are valid -- GPL codecs (`libx264`, `libx265`) are excluded from the enum entirely, enforcing compliance at the schema level. |
| `OutputRestartOnFailure` | `BROADCAST_OUTPUT_RESTART_ON_FAILURE` | bool | `true` | | Auto-restart FFmpeg on crash |
| `OutputHealthCheckIntervalSeconds` | `BROADCAST_OUTPUT_HEALTH_CHECK_INTERVAL_SECONDS` | int | `10` | `minimum: 1`, `maximum: 3600` | FFmpeg health check frequency |
| `RtmpProbeTimeoutSeconds` | `BROADCAST_RTMP_PROBE_TIMEOUT_SECONDS` | int | `5` | `minimum: 1`, `maximum: 60` | FFprobe timeout for RTMP URL validation |
| `AutoBroadcastCameraId` | `BROADCAST_AUTO_BROADCAST_CAMERA_ID` | string? | *(null)* | | Camera ID to auto-broadcast on startup. Null = disabled. |
| `AutoBroadcastRtmpUrl` | `BROADCAST_AUTO_BROADCAST_RTMP_URL` | string? | *(null)* | | RTMP destination for auto-broadcast. Null = disabled. |
| `DistributedLockTimeoutSeconds` | `BROADCAST_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | int | `30` | `minimum: 1`, `maximum: 300` | Timeout for distributed lock acquisition |
| `TokenRefreshIntervalTwitchSeconds` | `BROADCAST_TOKEN_REFRESH_INTERVAL_TWITCH_SECONDS` | int | `14400` | `minimum: 60` | Twitch OAuth token refresh interval (default 4 hours) |
| `TokenRefreshIntervalYouTubeSeconds` | `BROADCAST_TOKEN_REFRESH_INTERVAL_YOUTUBE_SECONDS` | int | `3600` | `minimum: 60` | YouTube OAuth token refresh interval (default 1 hour) |
| `WebhookRenewalIntervalSeconds` | `BROADCAST_WEBHOOK_RENEWAL_INTERVAL_SECONDS` | int | `3600` | `minimum: 60` | How often to verify/renew platform webhook subscriptions |
| `SessionCleanupIntervalSeconds` | `BROADCAST_SESSION_CLEANUP_INTERVAL_SECONDS` | int | `3600` | `minimum: 60` | How often to purge expired session records |
| `WebhookManagerLockTimeoutSeconds` | `BROADCAST_WEBHOOK_MANAGER_LOCK_TIMEOUT_SECONDS` | int | `300` | `minimum: 10`, `maximum: 1800` | TTL for the webhook manager singleton lock. Must be less than the webhook renewal deadline to prevent subscription expiration during a crash. |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<BroadcastService>` | Structured logging |
| `BroadcastServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 7 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IAccountClient` | Account existence validation (L1 hard) |
| `IAuthClient` | OAuth token validation (L1 hard) |
| `IServiceProvider` | Runtime resolution of soft L3 dependencies |
| `IBroadcastCoordinator` | FFmpeg process supervision (internal singleton). Per-instance process handle cache -- NOT authoritative state. The authoritative broadcast record lives in the `broadcast-outputs` Redis store. On startup, the coordinator reads Redis and reconciles: stale broadcasts owned by crashed instances are marked failed, local process handles are rebuilt for broadcasts owned by this instance. |
| `ISentimentProcessor` | Raw platform events → sentiment values (internal) |
| `IPlatformWebhookHandler` | Platform-specific webhook processing (internal) |
| `ITelemetryProvider` | Span instrumentation for all async methods (per QUALITY TENETS) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `TokenRefreshWorker` | Periodically refreshes OAuth tokens for linked platforms before they expire. Acquires `broadcast:lock:token-refresh:{linkId}` per-link before refreshing (per IMPLEMENTATION TENETS -- prevents concurrent refresh across instances). | `TokenRefreshIntervalTwitchSeconds` (14400), `TokenRefreshIntervalYouTubeSeconds` (3600) | `broadcast:lock:token-refresh:{linkId}` (per-link) |
| `WebhookSubscriptionManager` | Ensures webhook subscriptions are active for all linked platforms (Twitch EventSub requires periodic renewal) | `WebhookRenewalIntervalSeconds` (3600) | `broadcast:lock:webhook-manager` (TTL: `WebhookManagerLockTimeoutSeconds`) |
| `SentimentBatchPublisher` | Drains the sentiment buffer at the configured pulse interval and publishes `broadcast.audience.pulse` events | `SentimentPulseIntervalSeconds` (15s) | `broadcast:lock:sentiment-publisher` |
| `SessionCleanupWorker` | Purges ended session records older than the configured retention period | `SessionCleanupIntervalSeconds` (3600) | None (idempotent) |
| `BroadcastHealthMonitor` | Monitors FFmpeg process health via stderr parsing, auto-restarts on crash, detects stale Redis broadcast records from crashed instances and marks them failed. Publishes health events. | `OutputHealthCheckIntervalSeconds` (10s) | None (local process monitoring + stale record detection) |
| `AutoBroadcastStarter` | On startup, checks `AutoBroadcastCameraId` and `AutoBroadcastRtmpUrl` config; if both non-null, starts a broadcast automatically | Once (startup) | None |

---

## API Endpoints (Implementation Notes)

**Current status**: Pre-implementation. All endpoints described below are architectural targets.

### Platform Account Management (4 endpoints)

All endpoints require `x-permissions: [user]`.

- **Link** (`/broadcast/platform/link`): Initiates OAuth flow for Twitch, YouTube, or custom RTMP. Validates account exists. Checks no duplicate link for this account+platform. Generates OAuth state token, stores pending link with TTL. Returns OAuth redirect URL for the platform.
- **Callback** (`/broadcast/platform/callback`): Handles OAuth redirect callback. Validates state token, exchanges authorization code for tokens, encrypts tokens with `TokenEncryptionKey`, stores `PlatformLinkModel` in MySQL. `x-lifecycle` publishes `PlatformLinkCreatedEvent`. For custom RTMP: no OAuth; stores provided RTMP URL directly.
- **Unlink** (`/broadcast/platform/unlink`): Lock. Revokes OAuth tokens on the platform side. Stops any active session for this link. Deletes link record (session records referencing this link are cascade-deleted from Redis). `x-lifecycle` publishes `PlatformLinkDeletedEvent`.
- **List** (`/broadcast/platform/list`): Returns all linked platforms for an account. Token fields are masked in response.

### Platform Session Management (5 endpoints)

All endpoints require `x-permissions: [user]`.

- **Start** (`/broadcast/session/start`): Validates platform link exists and account is currently live on the platform (queries platform API). Creates `PlatformSessionModel` in Redis. Starts ingesting platform events (chat, subs, raids) via the platform's real-time API (Twitch IRC/EventSub, YouTube Live Chat API). `x-lifecycle` publishes `PlatformSessionCreatedEvent`.
- **Stop** (`/broadcast/session/stop`): Stops event ingestion. Records duration and peak viewer count. Deletes tracking ID mappings from Redis. `x-lifecycle` publishes `PlatformSessionDeletedEvent`.
- **Associate** (`/broadcast/session/associate`): Links a platform session to an in-game streaming session (lib-showtime L4). Updates the `streamSessionId` field on sentiment pulses so lib-showtime knows which in-game session the real audience belongs to. **Important**: `streamSessionId` is stored as an opaque GUID with NO validation against lib-showtime -- Broadcast (L3) cannot call lib-showtime (L4) per the service hierarchy. lib-showtime validates the ID against its own state when it receives `broadcast.audience.pulse` events containing this ID.
- **Status** (`/broadcast/session/status`): Returns current session state including viewer count, sentiment category distribution, and linked in-game session ID.
- **List** (`/broadcast/session/list`): Returns active and recent sessions for an account, paginated.

### Camera Management (2 endpoints)

Camera sources are registered via API endpoints (not events) because game engines are external systems that call Broadcast's HTTP API directly. This avoids creating orphaned event topics with no Bannou publisher (per FOUNDATION TENETS -- events require a publishing service).

All endpoints require `x-permissions: [admin]`.

- **Announce** (`/broadcast/camera/announce`): Registers or heartbeats a game engine camera as an available video source. Stores `CameraSourceModel` in Redis with TTL (cameras must re-announce periodically). Replaces the previously-designed `camera.stream.started` event subscription.
- **Retire** (`/broadcast/camera/retire`): Removes a camera from available sources. Replaces the previously-designed `camera.stream.ended` event subscription.

### Output Management (5 endpoints)

All endpoints require `x-permissions: [admin]` (RTMP output management is an admin operation).

- **Start** (`/broadcast/output/start`): Validates source availability (camera exists, game audio reachable, or voice room consent granted). Validates RTMP URL connectivity via FFprobe. Starts FFmpeg process via `IBroadcastCoordinator`. Stores `BroadcastModel` in Redis (authoritative state). `x-lifecycle` publishes `BroadcastOutputCreatedEvent`. For `VoiceRoom` source type: this is called internally in response to `voice.room.broadcast.approved`, not directly by clients.
- **Stop** (`/broadcast/output/stop`): Kills FFmpeg process. `x-lifecycle` publishes `BroadcastOutputDeletedEvent`.
- **Update** (`/broadcast/output/update`): Changes RTMP URL or fallback configuration. Causes ~2-3s interruption (FFmpeg restart). Validates new URL via FFprobe before committing.
- **Status** (`/broadcast/output/status`): Returns broadcast health, current video source (primary/fallback/black), duration, and source type.
- **List** (`/broadcast/output/list`): Returns all active broadcasts with optional filter for active-only.

### Webhook Endpoints (3 endpoints)

Internet-facing via NGINX (justified T15 exception -- platform callbacks, not browser-facing). Authentication is provided by platform-specific HMAC signature validation (Twitch) and verification token validation (YouTube), not Bannou JWT. All three endpoints declare `x-permissions: []` (empty -- not exposed to WebSocket clients) and `x-controller-only: true` (webhook validation requires raw `HttpContext` access for HMAC computation before model binding).

- **Twitch** (`/broadcast/webhook/twitch`): Validates Twitch HMAC signature. Handles EventSub notification types: stream.online, stream.offline, channel.subscribe, channel.raid, channel.chat.message. Routes to `ISentimentProcessor` for chat messages.
- **YouTube** (`/broadcast/webhook/youtube`): Validates YouTube verification token. Handles push notifications for live chat messages, super chats, new subscribers, and membership events.
- **Custom** (`/broadcast/webhook/custom`): Validates configurable HMAC signature. Generic webhook format for custom RTMP platforms. Routes to `ISentimentProcessor`.

### Admin / Debug (2 endpoints)

All endpoints require `x-permissions: [developer]`.

- **Latest Pulse** (`/broadcast/admin/pulse/latest`): Returns the most recent sentiment pulse for a platform session. Debug tool for validating sentiment processing.
- **Test Sentiment** (`/broadcast/admin/sentiment/test`): Accepts a raw text input and returns the sentiment processing result (category + intensity). Testing tool for sentiment algorithm tuning.

### Cleanup Endpoints (1 endpoint)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByAccount** (`/broadcast/cleanup-by-account`): Unlinks all platforms for the account, stops all active sessions, stops all broadcasts initiated by the account.

---

## Client Events

Broadcast pushes real-time status updates to connected WebSocket clients via `IClientEventPublisher` (per IMPLEMENTATION TENETS -- never `IMessageBus` for client events). All client events use Pattern C naming for multi-entity services: `broadcast.{entity}.{action}`.

| Event Name | Model | Target | Trigger |
|------------|-------|--------|---------|
| `broadcast.output.started` | `BroadcastOutputStartedClientEvent` | Account session (admin) | RTMP broadcast started |
| `broadcast.output.stopped` | `BroadcastOutputStoppedClientEvent` | Account session (admin) | RTMP broadcast stopped |
| `broadcast.output.source-changed` | `BroadcastOutputSourceChangedClientEvent` | Account session (admin) | Fallback cascade triggered |
| `broadcast.session.started` | `BroadcastSessionStartedClientEvent` | Account session (user) | Platform session monitoring started |
| `broadcast.session.ended` | `BroadcastSessionEndedClientEvent` | Account session (user) | Platform session monitoring ended |

---

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────────┐
│                    Broadcast Service: The Privacy Boundary                    │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  EXTERNAL WORLD (PII, text, usernames)                                   │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐                │
│  │  Twitch   │  │ YouTube  │  │  Custom  │  │  Game    │                │
│  │  EventSub │  │ Webhooks │  │  RTMP    │  │ Cameras  │                │
│  └─────┬────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘                │
│        │            │              │              │                       │
│  ══════╪════════════╪══════════════╪══════════════╪═══════════════════   │
│  ║     ▼            ▼              ▼              ▼                  ║   │
│  ║  lib-broadcast (L3)                                                  ║   │
│  ║  ┌────────────────────────────────────────────────────────────┐   ║   │
│  ║  │ Platform Webhook Handlers (x-controller-only, T15 exempt) │   ║   │
│  ║  │   Twitch: HMAC signature validation                         │   ║   │
│  ║  │   YouTube: verification token validation                    │   ║   │
│  ║  │   Custom: configurable HMAC validation                      │   ║   │
│  ║  │ Camera API Endpoints (/broadcast/camera/announce|retire)    │   ║   │
│  ║  └─────────────────────┬──────────────────────────────────────┘   ║   │
│  ║                        │ raw events                               ║   │
│  ║                        ▼                                          ║   │
│  ║  ┌────────────────────────────────────────────────────────────┐   ║   │
│  ║  │ ISentimentProcessor                                         │   ║   │
│  ║  │   chat text → sentiment category + intensity                │   ║   │
│  ║  │   subscriptions → Excited/Supportive sentiment              │   ║   │
│  ║  │   raids → Excited sentiment (RaidLeader tracked)            │   ║   │
│  ║  │   emotes → mapped to sentiment categories                   │   ║   │
│  ║  │                                                             │   ║   │
│  ║  │   Redis-backed: hashedPlatformUserId → trackingId           │   ║   │
│  ║  │   (session-scoped TTL, non-reversible, T9-compliant)        │   ║   │
│  ║  └─────────────────────┬──────────────────────────────────────┘   ║   │
│  ║                        │ anonymous sentiments                     ║   │
│  ║                        ▼                                          ║   │
│  ║  ┌──────────────────────────┐   ┌─────────────────────────────┐  ║   │
│  ║  │ Sentiment Buffer (Redis) │──▶│ SentimentBatchPublisher      │  ║   │
│  ║  │ TTL-based cleanup        │   │ Every 15s: drain → publish   │  ║   │
│  ║  └──────────────────────────┘   └──────────────┬──────────────┘  ║   │
│  ║                                                 │                 ║   │
│  ║  ┌────────────────────────────────────────────────────────────┐   ║   │
│  ║  │ IBroadcastCoordinator (Singleton — local process cache)    │   ║   │
│  ║  │   ConcurrentDictionary<Guid, BroadcastContext>             │   ║   │
│  ║  │   NON-AUTHORITATIVE — Redis broadcast-outputs is truth     │   ║   │
│  ║  │   Startup reconciliation: reads Redis, rebuilds handles    │   ║   │
│  ║  │   FFmpeg process lifecycle (start/stop/restart)            │   ║   │
│  ║  │   RTMP URL validation via FFprobe                          │   ║   │
│  ║  │   Fallback cascade (primary→stream→image→default→black)    │   ║   │
│  ║  │   Stream key masking in all outputs                        │   ║   │
│  ║  └────────────────────────────────────────────────────────────┘   ║   │
│  ╚═══════════════════════════════════════════════════════════════════╝   │
│        │                                                                 │
│  INTERNAL WORLD (anonymous sentiment values, no PII)                     │
│        │                                                                 │
│        ▼                                                                 │
│  broadcast.audience.pulse events                                            │
│  (SentimentCategory enum + float intensity + optional opaque GUID)       │
│                                                                          │
│  Consumed by:                                                            │
│  ┌──────────────────────────────────────────────────────────────┐       │
│  │ lib-showtime (L4) -- blends real sentiments with simulated  │       │
│  │                        audience members to create the         │       │
│  │                        in-game streaming metagame             │       │
│  └──────────────────────────────────────────────────────────────┘       │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Schema & Generation
- Create broadcast-api.yaml schema with all endpoints (22 endpoints across 6 groups -- includes 2 camera endpoints)
- Declare `x-service-layer: AppFeatures` at schema root (default is GameFeatures -- wrong layer without this)
- Declare `x-references` block for lib-resource account cleanup
- Create broadcast-events.yaml schema using `x-lifecycle` with `topic_prefix: broadcast` for PlatformLink, PlatformSession, and BroadcastOutput entities; plus 1 custom event (`broadcast.audience.pulse`)
- Define consumed event models inline in `broadcast-events.yaml` (voice events, session events -- cannot `$ref` other service event files per FOUNDATION TENETS)
- Create broadcast-configuration.yaml schema (38 configuration properties with validation ranges)
- Create broadcast-client-events.yaml (5 client events: output started/stopped/source-changed, session started/ended)
- Define `AudioCodec` and `VideoCodec` enums in configuration schema (LGPL-compliant codecs only per T18)
- Generate service code
- Verify build succeeds

### Phase 2: Platform Account Linking
- Implement OAuth flow for Twitch (client ID, client secret, authorization code exchange)
- Implement OAuth flow for YouTube
- Implement custom RTMP link storage (no OAuth, just URL)
- Implement token encryption/decryption (validate `TokenEncryptionKey` non-null and >= 32 chars at startup when platform linking is enabled)
- Implement token refresh background worker with per-link distributed locking
- Implement platform unlink with token revocation and session record cascade cleanup

### Phase 3: Platform Session Management
- Implement session start with platform live-status verification
- Implement Twitch EventSub webhook handler (HMAC signature validation, event routing, `x-controller-only`)
- Implement YouTube webhook handler (verification token, push notifications, `x-controller-only`)
- Implement sentiment processor (text → category + intensity via keyword/emoji matching)
- Implement sentiment batch publisher (buffer drain → pulse events)
- Implement tracked viewer management (Redis-backed mapping with session-scoped TTL)

### Phase 4: Output Management
- Implement BroadcastCoordinator as per-instance process supervisor (Redis is authoritative, coordinator is local cache)
- Implement startup reconciliation (read Redis, mark stale broadcasts from crashed instances as failed)
- Implement RTMP URL validation via FFprobe
- Implement fallback cascade
- Implement voice room broadcast via consent event subscription
- Implement voice participant mute event subscription for audio mixing
- Implement camera announce/retire API endpoints
- Implement broadcast health monitor background worker with stale record detection
- Implement auto-broadcast from config (null check, not empty-string check)

### Phase 5: Webhook Subscription Management
- Implement Twitch EventSub subscription lifecycle (create, renew, delete)
- Implement YouTube webhook subscription lifecycle
- Implement webhook subscription manager background worker with singleton lock and configurable TTL

---

## Potential Extensions

1. **Sentiment processing sophistication**: Start with keyword/emoji matching. Future: lightweight NLP model for more nuanced sentiment categorization. The `ISentimentProcessor` interface allows swapping implementations without changing the rest of the pipeline.

2. **Cross-session returner detection**: Currently tracking IDs are per-session only. A hashed-platform-user-ID approach could enable "returner" detection across sessions without storing the actual platform user ID. Hash with a rotating salt, stored encrypted, deleted after configurable retention.

3. **Multi-platform simultaneous broadcasting**: A single voice room or camera could broadcast to multiple RTMP endpoints simultaneously (Twitch AND YouTube). Requires multiple FFmpeg processes per source.

4. **Broadcast overlay composition**: FFmpeg filter graphs could composite overlays (viewer count, chat sentiment visualization, game HUD elements) onto the broadcast video. Currently out of scope -- overlays are a client/OBS concern.

5. **Platform-specific enrichment**: Twitch Prediction and Poll events could map to special sentiment categories. YouTube Super Chat amounts could influence intensity scores. Extensible via the `ISentimentProcessor` interface.

6. **Broadcast recording**: FFmpeg can simultaneously output to both RTMP and a local file. Recorded broadcasts could be uploaded to the Asset service for archival. Useful for highlight reels and content flywheel integration.

7. **Custom sentiment models**: Per-game-service sentiment processing rules (different games care about different emotional categories). Configurable via a sentiment model store rather than hardcoded categories.

---

## License Compliance (per FOUNDATION TENETS)

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **FFmpeg** | LGPL v2.1+ | Compliant | Process isolation (separate OS process, not linked). Must use LGPL-compliant build (no `--enable-gpl`, `--enable-nonfree`, `libx264`, `libx265`). LGPL compliance is enforced at the schema level: `AudioCodec` and `VideoCodec` enums contain only LGPL-compliant codec identifiers. |
| **MediaMTX** | MIT | Approved | Optional test RTMP server for development/testing |

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None. Plugin is in pre-implementation phase -- no code exists to contain bugs.*

### Intentional Quirks (Documented Behavior)

1. **Sentiment categories are an enum, not opaque strings**: Unlike most Bannou extensibility patterns (seed type codes, collection type codes), sentiment categories are a fixed enum (`Excited`, `Supportive`, `Critical`, `Curious`, `Surprised`, `Amused`, `Bored`, `Hostile`). This is intentional -- lib-showtime (L4) needs to map sentiments to audience behavior deterministically. Adding a new sentiment category requires schema changes, which is acceptable because it's a rare, deliberate extension. No deprecation lifecycle needed (T31 Category C -- enum values with no stored instances referencing them by ID).

2. **Tracking ID mapping is Redis-backed, not in-memory**: The `hashedPlatformUserId → trackingId` mapping is stored in the `broadcast-sessions` Redis store with session-scoped TTL, not in a per-instance `ConcurrentDictionary`. This ensures cross-instance tracking consistency (the same viewer gets the same tracking ID regardless of which instance processes their events) while preserving privacy properties: the mapping is ephemeral (destroyed when the session ends via TTL or explicit delete), uses a hashed non-reversible key, and has no relationship to the original platform user ID. Per IMPLEMENTATION TENETS (multi-instance safety).

3. **Webhook endpoints are internet-facing (justified T15 exception)**: Unlike most Bannou services (POST-only, internal), the webhook endpoints receive callbacks from Twitch/YouTube via NGINX. They validate platform-specific signatures (Twitch HMAC, YouTube verification token) and route events internally. These are the only internet-facing endpoints in lib-broadcast. Declared with `x-permissions: []` (not exposed to WebSocket clients) and `x-controller-only: true` (HMAC validation requires raw body access before model binding).

4. **FFmpeg as a separate process**: FFmpeg runs as a separate OS process, not a linked library. This is a deliberate license compliance decision (LGPL process isolation) and also provides fault isolation -- a crashing FFmpeg process doesn't bring down the Bannou service.

5. **Stream key masking is absolute**: Full RTMP URLs (containing stream keys) are NEVER exposed in API responses, log messages, error events, or state store queries visible via the debug API. The only place the full URL exists is in the encrypted broadcast state store and in the FFmpeg process command-line arguments (which are not logged). This is a security decision, not a convenience trade-off.

6. **Sentiment pulses are fire-and-forget**: If no consumer subscribes to `broadcast.audience.pulse` events, the pulses are published and discarded by the message bus. lib-broadcast does not buffer, retry, or persist pulses beyond the initial publication. This is intentional -- sentiment data is inherently ephemeral.

7. **Platform link tokens are encrypted at rest**: OAuth access tokens and refresh tokens are encrypted with `TokenEncryptionKey` before storage in MySQL. If the encryption key is lost, all platform links must be re-established. There is no key rotation mechanism in v1 -- this is a known limitation, not a bug.

8. **IBroadcastCoordinator is a local process cache, not authoritative state**: The `ConcurrentDictionary<Guid, BroadcastContext>` in `IBroadcastCoordinator` holds local FFmpeg process handles for the current instance only. It is NOT the source of truth -- the `broadcast-outputs` Redis store is authoritative. On startup, the coordinator reads Redis and reconciles: broadcasts owned by this instance have process handles rebuilt; broadcasts owned by crashed instances are marked as failed. This per-instance supervision pattern is T9-compliant because the coordinator never holds state that other instances need. Per IMPLEMENTATION TENETS (multi-instance safety).

9. **Camera sources are registered via API, not events**: Game engine cameras register via `/broadcast/camera/announce` (with TTL heartbeat) rather than publishing `camera.stream.started` events. This avoids creating orphaned event topics with no Bannou publisher service (per FOUNDATION TENETS -- T27 cross-service communication discipline).

10. **Platform link deletion cascades session records**: When a platform link is hard-deleted (unlink), all `PlatformSessionModel` records in Redis referencing that link are also deleted. Session records are instance data with no cross-service references, so cascade delete is appropriate per IMPLEMENTATION TENETS (T31 -- no deprecation for instance data).

### Design Considerations (Requires Planning)

1. **Sentiment processing approach**: The architecture specifies `ISentimentProcessor` as an interface, but the implementation strategy (keyword matching, emoji mapping, lightweight NLP) is unresolved. Keyword matching is fast but crude; NLP models are accurate but add latency to the processing pipeline. The 15-second batching window provides some tolerance for processing latency.

2. **Cross-session tracking ID persistence**: The current design destroys tracking IDs when sessions end. "Returner" detection (tracking viewers who appear in multiple sessions) would require some form of cross-session identity, which conflicts with the no-PII-persistence principle. A hashed approach is proposed in Potential Extensions but needs privacy review.

3. **Twitch EventSub subscription management**: Twitch requires active management of webhook subscriptions (creation, renewal, callback verification). The subscription lifecycle is complex and failure-prone. The `WebhookSubscriptionManager` background worker needs robust error handling for common failure modes (expired subscriptions, rate limits, temporary API outages).

4. **Rate limiting for webhook endpoints**: Platform webhooks can deliver bursts of events (e.g., during a raid or sub train). The webhook handlers need rate limiting or backpressure to prevent overwhelming the sentiment buffer. Redis-backed rate limiting per platform session is the likely approach.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase.*
