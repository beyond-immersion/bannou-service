# Broadcast Plugin Deep Dive

> **Plugin**: lib-broadcast
> **Schema**: schemas/broadcast-api.yaml
> **Version**: 1.0.0
> **State Stores**: broadcast-platforms (MySQL), broadcast-sessions (Redis), broadcast-sentiment-buffer (Redis), broadcast-outputs (Redis), broadcast-cameras (Redis), broadcast-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Planning**: [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md), [VOICE-STREAMING.md](../planning/VOICE-STREAMING.md)

## Overview

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast.

**Privacy-first architecture**: This is a load-bearing design decision. Real audience data (chat messages, usernames, platform IDs) NEVER leaves lib-broadcast's process boundary as identifiable data. Raw platform events are reduced to **batched sentiment pulses** -- arrays of anonymous sentiment values with optional opaque tracking GUIDs for consistency. No platform user IDs, no message content, no personally identifiable information enters the event system. This eliminates GDPR/CCPA data deletion obligations for downstream consumers entirely.

**Two distinct broadcast modes**: Server-side content broadcasting (game cameras, game audio) requires no player consent -- it's game content. Voice room broadcasting to external platforms requires explicit consent from ALL room participants via lib-voice's broadcast consent flow. lib-broadcast subscribes to voice consent events and acts accordingly; it never initiates voice broadcasting directly.

**Composability**: Platform identity linking is owned here. Sentiment processing is owned here. RTMP output management is owned here. Audience behavior and the in-game metagame are lib-showtime (L4). Voice room management is lib-voice (L3). lib-broadcast is the privacy boundary and platform integration layer -- it touches external APIs so nothing else has to.

**The three-service principle**: lib-broadcast delivers value independently. It can broadcast game content to Twitch whether or not there's voice involved (lib-voice) or an in-game metagame (lib-showtime). It can ingest platform audience data and publish sentiment pulses whether or not anything consumes them. Each service in the voice/broadcast/showtime trio composes beautifully but never requires the others.

**Zero Arcadia-specific content**: lib-broadcast is a generic platform integration service. Which platforms are enabled, how sentiment categories map to game emotions, and what content gets broadcast are all configured via environment variables and API calls, not baked into lib-broadcast.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md). Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks.

---

## The Privacy Boundary

This deserves its own section because it is the architectural keystone of the entire streaming stack. Every design decision in lib-broadcast flows from one principle: **real audience data creates compliance liability; anonymous sentiment data does not.**

### Why No Text Content Leaves lib-broadcast

1. **Data deletion compliance**: If a Twitch user requests data deletion under GDPR, we'd need to purge their messages from every downstream system (analytics, state stores, event logs). With sentiment-only data, there's nothing to delete -- the original text never left lib-broadcast.
2. **Analytics cache problem**: Analytics ingests events for aggregation. Flushing specific user data from analytical stores is operationally expensive and error-prone. Sentiment values have no user association.
3. **Legal exposure**: Storing third-party platform user content creates licensing and liability questions. Sentiment approximations are derived data, not reproductions.

### The Sentiment Pulse Model

lib-broadcast processes raw platform events (chat messages, subscriptions, raids, emotes) into periodic **sentiment pulses** -- batched arrays of anonymous sentiment data points.

```
SentimentPulse:
  pulseId: Guid                       # Unique pulse identifier
  streamSessionId: Guid               # The lib-showtime in-game session (if linked)
  platformSessionId: Guid             # The lib-broadcast platform session
  timestamp: DateTime                 # When this pulse was assembled
  intervalSeconds: int                # Configured pulse interval
  approximateViewerCount: int         # Platform-reported viewer count (approximate)
  sentiments: SentimentEntry[]        # The batch
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

1. lib-broadcast maintains an **ephemeral, in-memory** mapping: `platformUserId → trackingId`
2. This mapping exists ONLY during an active platform session
3. When the session ends, the mapping is destroyed -- the tracking IDs become orphaned
4. The tracking IDs are Bannou-generated GUIDs with NO relationship to platform user IDs
5. The same real viewer gets the SAME tracking ID across pulses within a session (consistency)
6. But across sessions, they get a NEW tracking ID (no cross-session tracking)
7. There is NO way to reverse a tracking ID back to a platform user

**Timing and batching rationale**: 15-second pulse intervals create enough delay that individual messages can't be correlated to specific sentiment entries by timing alone. Combined with batching (minimum 5, maximum 200 entries per pulse), this makes de-anonymization impractical even for someone monitoring both the platform chat and the sentiment stream.

---

## The RTMP Broadcast System

lib-broadcast manages FFmpeg processes for broadcasting content to RTMP endpoints (Twitch, YouTube, custom). This subsystem was originally designed as part of lib-voice (VOICE-STREAMING.md) and moved to lib-broadcast in the three-service architecture redesign.

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

Each fallback transition publishes a `broadcast.output.source-changed` event so consumers (lib-showtime L4) can react to degraded broadcast quality.

### Stream Key Security

RTMP URLs contain stream keys (e.g., `rtmp://live.twitch.tv/app/YOUR_SECRET_KEY`). lib-broadcast masks stream keys in ALL responses and log messages. The full URL is stored encrypted in the broadcast state store and only passed to FFmpeg process arguments. API responses show masked URLs (e.g., `rtmp://live.twitch.tv/app/****`).

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Platform link records (MySQL), active session tracking (Redis), sentiment buffer (Redis), broadcast state (Redis), camera sources (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for platform linking, session management, broadcast mutations |
| lib-messaging (`IMessageBus`) | Publishing sentiment pulses, platform session events, broadcast lifecycle events |
| lib-messaging (`IEventConsumer`) | Registering handlers for voice broadcast consent events, camera discovery events |
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
| lib-showtime (L4) | Subscribes to `broadcast.platform.session.started`/`ended` for platform session awareness |
| lib-showtime (L4) | Subscribes to `broadcast.output.started`/`stopped` for broadcast state tracking |

---

## State Storage

### Platform Link Store
**Store**: `broadcast-platforms` (Backend: MySQL)

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

### Sentiment Buffer Store
**Store**: `broadcast-sentiment-buffer` (Backend: Redis, prefix: `broadcast:sent`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sent:{platformSessionId}:{sequence}` | `BufferedSentimentEntry` | Individual sentiment entries awaiting batch publication. TTL-based cleanup (2x pulse interval). |

### Broadcast Store
**Store**: `broadcast-outputs` (Backend: Redis, prefix: `broadcast:out`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `out:{broadcastId}` | `BroadcastModel` | Active broadcast state. Stores source type, encrypted RTMP URL, FFmpeg process ID, current video source, health status, start time, fallback configuration. |

### Camera Source Store
**Store**: `broadcast-cameras` (Backend: Redis, prefix: `broadcast:cam`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cam:{cameraId}` | `CameraSourceModel` | Discovered camera sources from game engines. TTL-based (cameras must re-announce periodically). Stores RTMP input URL, resolution, codec, heartbeat timestamp. |

### Distributed Locks
**Store**: `broadcast-lock` (Backend: Redis, prefix: `broadcast:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `broadcast:lock:link:{accountId}:{platform}` | Platform linking operation lock (prevent duplicate OAuth flows) |
| `broadcast:lock:session:{platformSessionId}` | Session mutation lock |
| `broadcast:lock:broadcast:{broadcastId}` | Broadcast mutation lock (start/stop/update) |
| `broadcast:lock:sentiment-publisher` | Sentiment batch publisher singleton lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `broadcast.platform.linked` | `BroadcastPlatformLinkedEvent` | Account linked to a streaming platform |
| `broadcast.platform.unlinked` | `BroadcastPlatformUnlinkedEvent` | Platform link removed |
| `broadcast.platform.session.started` | `BroadcastPlatformSessionStartedEvent` | Platform session monitoring started (account went live) |
| `broadcast.platform.session.ended` | `BroadcastPlatformSessionEndedEvent` | Platform session ended (with duration and peak viewer count) |
| `broadcast.audience.pulse` | `SentimentPulse` | Batched sentiment data from real audience (privacy-safe, no PII) |
| `broadcast.output.started` | `BroadcastOutputStartedEvent` | RTMP broadcast started (FFmpeg process running) |
| `broadcast.output.stopped` | `BroadcastOutputStoppedEvent` | RTMP broadcast stopped (manual, error, source disconnected, consent revoked) |
| `broadcast.output.source-changed` | `BroadcastOutputSourceChangedEvent` | Broadcast video source changed (fallback cascade) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `voice.room.broadcast.approved` | `HandleVoiceBroadcastApprovedAsync` | Start RTMP output for a voice room after all participants consented. Connects to the room's RTP audio endpoint. (Soft -- no-op if lib-voice absent) |
| `voice.room.broadcast.stopped` | `HandleVoiceBroadcastStoppedAsync` | Stop RTMP output for a voice room. Consent revoked or room closed. (Soft -- no-op if lib-voice absent) |
| `camera.stream.started` | `HandleCameraStreamStartedAsync` | Register a game engine camera as an available video source for broadcasts |
| `camera.stream.ended` | `HandleCameraStreamEndedAsync` | Remove camera from available sources |

### Resource Cleanup (per FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| account | broadcast | CASCADE | `/broadcast/cleanup-by-account` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `BroadcastEnabled` | `BROADCAST_ENABLED` | `false` | Master feature flag for the entire broadcast service |
| `TwitchClientId` | `BROADCAST_TWITCH_CLIENT_ID` | `""` | Twitch OAuth application client ID |
| `TwitchClientSecret` | `BROADCAST_TWITCH_CLIENT_SECRET` | `""` | Twitch OAuth application client secret |
| `TwitchWebhookSecret` | `BROADCAST_TWITCH_WEBHOOK_SECRET` | `""` | Twitch EventSub webhook signing secret |
| `YouTubeClientId` | `BROADCAST_YOUTUBE_CLIENT_ID` | `""` | YouTube OAuth application client ID |
| `YouTubeClientSecret` | `BROADCAST_YOUTUBE_CLIENT_SECRET` | `""` | YouTube OAuth application client secret |
| `YouTubeWebhookVerificationToken` | `BROADCAST_YOUTUBE_WEBHOOK_TOKEN` | `""` | YouTube webhook verification token |
| `SentimentPulseIntervalSeconds` | `BROADCAST_SENTIMENT_PULSE_INTERVAL_SECONDS` | `15` | How often sentiment pulses are published |
| `SentimentMinBatchSize` | `BROADCAST_SENTIMENT_MIN_BATCH_SIZE` | `5` | Minimum sentiments before publishing a pulse |
| `SentimentMaxBatchSize` | `BROADCAST_SENTIMENT_MAX_BATCH_SIZE` | `200` | Maximum sentiments per pulse (overflow drops lowest-intensity) |
| `MaxTrackedViewersPerSession` | `BROADCAST_MAX_TRACKED_VIEWERS_PER_SESSION` | `50` | Maximum tracked viewers with opaque GUIDs per session |
| `TrackedViewerEngagementThreshold` | `BROADCAST_TRACKED_VIEWER_ENGAGEMENT_THRESHOLD` | `0.7` | Minimum engagement score for HighEngager tracking |
| `TrackSubscribers` | `BROADCAST_TRACK_SUBSCRIBERS` | `true` | Whether platform subscribers get tracking GUIDs |
| `TrackModerators` | `BROADCAST_TRACK_MODERATORS` | `true` | Whether platform moderators get tracking GUIDs |
| `TrackRaidLeaders` | `BROADCAST_TRACK_RAID_LEADERS` | `true` | Whether raid leaders get tracking GUIDs |
| `TokenEncryptionKey` | `BROADCAST_TOKEN_ENCRYPTION_KEY` | `""` | AES encryption key for stored OAuth tokens |
| `SessionHistoryRetentionHours` | `BROADCAST_SESSION_HISTORY_RETENTION_HOURS` | `168` | Hours to retain ended session records (1 week) |
| `OutputEnabled` | `BROADCAST_OUTPUT_ENABLED` | `false` | Feature flag for RTMP output capabilities |
| `FfmpegPath` | `BROADCAST_FFMPEG_PATH` | `/usr/bin/ffmpeg` | Path to FFmpeg binary |
| `DefaultBackgroundVideo` | `BROADCAST_DEFAULT_BACKGROUND_VIDEO` | `/opt/bannou/backgrounds/default.mp4` | Default video background for audio-only outputs |
| `MaxConcurrentOutputs` | `BROADCAST_MAX_CONCURRENT_OUTPUTS` | `10` | Maximum simultaneous FFmpeg output processes |
| `OutputAudioCodec` | `BROADCAST_OUTPUT_AUDIO_CODEC` | `aac` | Audio codec for RTMP output |
| `OutputAudioBitrate` | `BROADCAST_OUTPUT_AUDIO_BITRATE` | `128k` | Audio bitrate for RTMP output |
| `OutputVideoCodec` | `BROADCAST_OUTPUT_VIDEO_CODEC` | `libvpx` | Video codec for RTMP output |
| `OutputRestartOnFailure` | `BROADCAST_OUTPUT_RESTART_ON_FAILURE` | `true` | Auto-restart FFmpeg on crash |
| `OutputHealthCheckIntervalSeconds` | `BROADCAST_OUTPUT_HEALTH_CHECK_INTERVAL_SECONDS` | `10` | FFmpeg health check frequency |
| `RtmpProbeTimeoutSeconds` | `BROADCAST_RTMP_PROBE_TIMEOUT_SECONDS` | `5` | FFprobe timeout for RTMP URL validation |
| `AutoBroadcastCameraId` | `BROADCAST_AUTO_BROADCAST_CAMERA_ID` | `""` | Camera ID to auto-broadcast on startup (empty = disabled) |
| `AutoBroadcastRtmpUrl` | `BROADCAST_AUTO_BROADCAST_RTMP_URL` | `""` | RTMP destination for auto-broadcast (empty = disabled) |
| `DistributedLockTimeoutSeconds` | `BROADCAST_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<BroadcastService>` | Structured logging |
| `BroadcastServiceConfiguration` | Typed configuration access (30 properties) |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IAccountClient` | Account existence validation (L1 hard) |
| `IAuthClient` | OAuth token validation (L1 hard) |
| `IServiceProvider` | Runtime resolution of soft L3 dependencies |
| `IBroadcastCoordinator` | FFmpeg process management (internal singleton) |
| `ISentimentProcessor` | Raw platform events → sentiment values (internal) |
| `IPlatformWebhookHandler` | Platform-specific webhook processing (internal) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `TokenRefreshWorker` | Periodically refreshes OAuth tokens for linked platforms before they expire | Platform-specific (Twitch: 4hr, YouTube: 1hr) | None (per-link, not global) |
| `WebhookSubscriptionManager` | Ensures webhook subscriptions are active for all linked platforms (Twitch EventSub requires periodic renewal) | 1 hour | `broadcast:lock:webhook-manager` |
| `SentimentBatchPublisher` | Drains the sentiment buffer at the configured pulse interval and publishes `broadcast.audience.pulse` events | `SentimentPulseIntervalSeconds` (15s) | `broadcast:lock:sentiment-publisher` |
| `SessionCleanupWorker` | Purges ended session records older than the configured retention period | 1 hour | None (idempotent) |
| `BroadcastHealthMonitor` | Monitors FFmpeg process health via stderr parsing, auto-restarts on crash, publishes health events | `OutputHealthCheckIntervalSeconds` (10s) | None (local process monitoring) |
| `AutoBroadcastStarter` | On startup, checks `AutoBroadcastCameraId` and `AutoBroadcastRtmpUrl` config; if both set, starts a broadcast automatically | Once (startup) | None |

---

## API Endpoints (Implementation Notes)

**Current status**: Pre-implementation. All endpoints described below are architectural targets.

### Platform Account Management (4 endpoints)

All endpoints require `user` role.

- **Link** (`/broadcast/platform/link`): Initiates OAuth flow for Twitch, YouTube, or custom RTMP. Validates account exists. Checks no duplicate link for this account+platform. Generates OAuth state token, stores pending link with TTL. Returns OAuth redirect URL for the platform.
- **Callback** (`/broadcast/platform/callback`): Handles OAuth redirect callback. Validates state token, exchanges authorization code for tokens, encrypts tokens with `TokenEncryptionKey`, stores `PlatformLinkModel` in MySQL. Publishes `broadcast.platform.linked`. For custom RTMP: no OAuth; stores provided RTMP URL directly.
- **Unlink** (`/broadcast/platform/unlink`): Lock. Revokes OAuth tokens on the platform side. Stops any active session for this link. Deletes link record. Publishes `broadcast.platform.unlinked`.
- **List** (`/broadcast/platform/list`): Returns all linked platforms for an account. Token fields are masked in response.

### Platform Session Management (5 endpoints)

All endpoints require `user` role.

- **Start** (`/broadcast/session/start`): Validates platform link exists and account is currently live on the platform (queries platform API). Creates `PlatformSessionModel` in Redis. Starts ingesting platform events (chat, subs, raids) via the platform's real-time API (Twitch IRC/EventSub, YouTube Live Chat API). Publishes `broadcast.platform.session.started`.
- **Stop** (`/broadcast/session/stop`): Stops event ingestion. Records duration and peak viewer count. Destroys in-memory tracking ID mappings. Publishes `broadcast.platform.session.ended`.
- **Associate** (`/broadcast/session/associate`): Links a platform session to an in-game streaming session (lib-showtime L4). Updates the `streamSessionId` field on sentiment pulses so lib-showtime knows which in-game session the real audience belongs to.
- **Status** (`/broadcast/session/status`): Returns current session state including viewer count, sentiment category distribution, and linked in-game session ID.
- **List** (`/broadcast/session/list`): Returns active and recent sessions for an account, paginated.

### Broadcast Management (5 endpoints)

All endpoints require `admin` role (server-side content broadcasting is an admin operation).

- **Start** (`/broadcast/broadcast/start`): Validates source availability (camera exists, game audio reachable, or voice room consent granted). Validates RTMP URL connectivity via FFprobe. Starts FFmpeg process via `IBroadcastCoordinator`. Stores `BroadcastModel` in Redis. Publishes `broadcast.output.started`. For `VoiceRoom` source type: this is called internally in response to `voice.room.broadcast.approved`, not directly by clients.
- **Stop** (`/broadcast/broadcast/stop`): Kills FFmpeg process. Publishes `broadcast.output.stopped`.
- **Update** (`/broadcast/broadcast/update`): Changes RTMP URL or fallback configuration. Causes ~2-3s interruption (FFmpeg restart). Validates new URL via FFprobe before committing.
- **Status** (`/broadcast/broadcast/status`): Returns broadcast health, current video source (primary/fallback/black), duration, and source type.
- **List** (`/broadcast/broadcast/list`): Returns all active broadcasts with optional filter for active-only.

### Webhook Endpoints (3 endpoints)

No authentication required (platform-verified via signatures). Internet-facing.

- **Twitch** (`/broadcast/webhook/twitch`): Validates Twitch HMAC signature. Handles EventSub notification types: stream.online, stream.offline, channel.subscribe, channel.raid, channel.chat.message. Routes to `ISentimentProcessor` for chat messages.
- **YouTube** (`/broadcast/webhook/youtube`): Validates YouTube verification token. Handles push notifications for live chat messages, super chats, new subscribers, and membership events.
- **Custom** (`/broadcast/webhook/custom`): Validates configurable HMAC signature. Generic webhook format for custom RTMP platforms. Routes to `ISentimentProcessor`.

### Admin / Debug (2 endpoints)

All endpoints require `developer` role.

- **Latest Pulse** (`/broadcast/admin/pulse/latest`): Returns the most recent sentiment pulse for a platform session. Debug tool for validating sentiment processing.
- **Test Sentiment** (`/broadcast/admin/sentiment/test`): Accepts a raw text input and returns the sentiment processing result (category + intensity). Testing tool for sentiment algorithm tuning.

### Cleanup Endpoints (1 endpoint)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByAccount** (`/broadcast/cleanup-by-account`): Unlinks all platforms for the account, stops all active sessions, stops all broadcasts initiated by the account.

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
│  ║  │ Platform Webhook Handlers                                   │   ║   │
│  ║  │   Twitch: HMAC signature validation                         │   ║   │
│  ║  │   YouTube: verification token validation                    │   ║   │
│  ║  │   Custom: configurable HMAC validation                      │   ║   │
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
│  ║  │   In-memory ONLY: platformUserId → trackingId               │   ║   │
│  ║  │   (destroyed when session ends, non-reversible)             │   ║   │
│  ║  └─────────────────────┬──────────────────────────────────────┘   ║   │
│  ║                        │ anonymous sentiments                     ║   │
│  ║                        ▼                                          ║   │
│  ║  ┌──────────────────────────┐   ┌─────────────────────────────┐  ║   │
│  ║  │ Sentiment Buffer (Redis) │──▶│ SentimentBatchPublisher      │  ║   │
│  ║  │ TTL-based cleanup        │   │ Every 15s: drain → publish   │  ║   │
│  ║  └──────────────────────────┘   └──────────────┬──────────────┘  ║   │
│  ║                                                 │                 ║   │
│  ║  ┌────────────────────────────────────────────────────────────┐   ║   │
│  ║  │ IBroadcastCoordinator (Singleton)                           │   ║   │
│  ║  │   ConcurrentDictionary<Guid, BroadcastContext>              │   ║   │
│  ║  │   FFmpeg process lifecycle (start/stop/restart)             │   ║   │
│  ║  │   RTMP URL validation via FFprobe                           │   ║   │
│  ║  │   Fallback cascade (primary→stream→image→default→black)     │   ║   │
│  ║  │   Stream key masking in all outputs                         │   ║   │
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

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned per [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md):

### Phase 1: Schema & Generation
- Create broadcast-api.yaml schema with all endpoints (20 endpoints across 5 groups)
- Create broadcast-events.yaml schema (8 published events, 4 consumed events)
- Create broadcast-configuration.yaml schema (30 configuration properties)
- Create broadcast-client-events.yaml (broadcast status client events)
- Generate service code
- Verify build succeeds

### Phase 2: Platform Account Linking
- Implement OAuth flow for Twitch (client ID, client secret, authorization code exchange)
- Implement OAuth flow for YouTube
- Implement custom RTMP link storage (no OAuth, just URL)
- Implement token encryption/decryption
- Implement token refresh background worker
- Implement platform unlink with token revocation

### Phase 3: Platform Session Management
- Implement session start with platform live-status verification
- Implement Twitch EventSub webhook handler (signature validation, event routing)
- Implement YouTube webhook handler (verification token, push notifications)
- Implement sentiment processor (text → category + intensity)
- Implement sentiment batch publisher (buffer drain → pulse events)
- Implement tracked viewer management (in-memory mapping lifecycle)

### Phase 4: Broadcast Management
- Implement BroadcastCoordinator (FFmpeg process lifecycle)
- Implement RTMP URL validation via FFprobe
- Implement fallback cascade
- Implement voice room broadcast via consent event subscription
- Implement camera discovery event subscription
- Implement broadcast health monitor background worker
- Implement auto-broadcast from ENV config

### Phase 5: Webhook Subscription Management
- Implement Twitch EventSub subscription lifecycle (create, renew, delete)
- Implement YouTube webhook subscription lifecycle
- Implement webhook subscription manager background worker

---

## Potential Extensions

1. **Sentiment processing sophistication**: Start with keyword/emoji matching. Future: lightweight NLP model for more nuanced sentiment categorization. The `ISentimentProcessor` interface allows swapping implementations without changing the rest of the pipeline.

2. **Cross-session returner detection**: Currently tracking IDs are per-session only. A hashed-platform-user-ID approach could enable "returner" detection across sessions without storing the actual platform user ID. Hash with a rotating salt, stored encrypted, deleted after configurable retention.

3. **Multi-platform simultaneous broadcasting**: A single voice room or camera could broadcast to multiple RTMP endpoints simultaneously (Twitch AND YouTube). Requires multiple FFmpeg processes per source.

4. **Broadcast overlay composition**: FFmpeg filter graphs could composite overlays (viewer count, chat sentiment visualization, game HUD elements) onto the broadcast video. Currently out of scope -- overlays are a client/OBS concern.

5. **Client events for broadcast status**: `broadcast-client-events.yaml` for pushing broadcast health, source changes, and sentiment pulse summaries to connected WebSocket clients.

6. **Platform-specific enrichment**: Twitch Prediction and Poll events could map to special sentiment categories. YouTube Super Chat amounts could influence intensity scores. Extensible via the `ISentimentProcessor` interface.

7. **Broadcast recording**: FFmpeg can simultaneously output to both RTMP and a local file. Recorded broadcasts could be uploaded to the Asset service for archival. Useful for highlight reels and content flywheel integration.

8. **Custom sentiment models**: Per-game-service sentiment processing rules (different games care about different emotional categories). Configurable via a sentiment model store rather than hardcoded categories.

---

## License Compliance (per FOUNDATION TENETS)

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **FFmpeg** | LGPL v2.1+ | Compliant | Process isolation (separate OS process, not linked). Must use LGPL-compliant build (no `--enable-gpl`, `--enable-nonfree`, `libx264`, `libx265`). |
| **MediaMTX** | MIT | Approved | Optional test RTMP server for development/testing |

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None. Plugin is in pre-implementation phase -- no code exists to contain bugs.*

### Intentional Quirks (Documented Behavior)

1. **Sentiment categories are an enum, not opaque strings**: Unlike most Bannou extensibility patterns (seed type codes, collection type codes), sentiment categories are a fixed enum (`Excited`, `Supportive`, `Critical`, `Curious`, `Surprised`, `Amused`, `Bored`, `Hostile`). This is intentional -- lib-showtime (L4) needs to map sentiments to audience behavior deterministically. Adding a new sentiment category requires schema changes, which is acceptable because it's a rare, deliberate extension.

2. **In-memory tracking ID mapping**: The `platformUserId → trackingId` mapping is deliberately NOT persisted. It exists in a `ConcurrentDictionary` for the duration of a platform session. This is a privacy decision, not a multi-instance safety oversight. In multi-instance deployments, a tracked viewer's requests may hit different instances, resulting in different tracking IDs across pulses for the same viewer. This is acceptable -- tracking consistency is best-effort, not guaranteed.

3. **Webhook endpoints are internet-facing**: Unlike most Bannou services (POST-only, internal), the webhook endpoints receive callbacks from Twitch/YouTube. They validate platform-specific signatures (Twitch HMAC, YouTube verification token) and route events internally. These are the only internet-facing endpoints in lib-broadcast.

4. **FFmpeg as a separate process**: FFmpeg runs as a separate OS process, not a linked library. This is a deliberate license compliance decision (LGPL process isolation) and also provides fault isolation -- a crashing FFmpeg process doesn't bring down the Bannou service.

5. **Stream key masking is absolute**: Full RTMP URLs (containing stream keys) are NEVER exposed in API responses, log messages, error events, or state store queries visible via the debug API. The only place the full URL exists is in the encrypted broadcast state store and in the FFmpeg process command-line arguments (which are not logged). This is a security decision, not a convenience trade-off.

6. **Sentiment pulses are fire-and-forget**: If no consumer subscribes to `broadcast.audience.pulse` events, the pulses are published and discarded by the message bus. lib-broadcast does not buffer, retry, or persist pulses beyond the initial publication. This is intentional -- sentiment data is inherently ephemeral.

7. **Platform link tokens are encrypted at rest**: OAuth access tokens and refresh tokens are encrypted with `TokenEncryptionKey` before storage in MySQL. If the encryption key is lost, all platform links must be re-established. There is no key rotation mechanism in v1 -- this is a known limitation, not a bug.

### Design Considerations (Requires Planning)

1. **Sentiment processing approach**: The architecture specifies `ISentimentProcessor` as an interface, but the implementation strategy (keyword matching, emoji mapping, lightweight NLP) is unresolved. Keyword matching is fast but crude; NLP models are accurate but add latency to the processing pipeline. The 15-second batching window provides some tolerance for processing latency.

2. **Cross-session tracking ID persistence**: The current design destroys tracking IDs when sessions end. "Returner" detection (tracking viewers who appear in multiple sessions) would require some form of cross-session identity, which conflicts with the no-PII-persistence principle. A hashed approach is proposed in Potential Extensions but needs privacy review.

3. **Twitch EventSub subscription management**: Twitch requires active management of webhook subscriptions (creation, renewal, callback verification). The subscription lifecycle is complex and failure-prone. The `WebhookSubscriptionManager` background worker needs robust error handling for common failure modes (expired subscriptions, rate limits, temporary API outages).

4. **FFmpeg process monitoring on multi-instance deployments**: `BroadcastCoordinator` tracks FFmpeg processes in a `ConcurrentDictionary`, which is per-instance. If an instance crashes, its FFmpeg processes become orphaned. The `BroadcastHealthMonitor` on the new instance can detect this via stale Redis broadcast records, but process cleanup requires either PID files or a process naming convention.

5. **OAuth token refresh race conditions**: If `TokenRefreshWorker` is running on multiple instances, they may attempt to refresh the same platform's tokens simultaneously. Platform APIs vary in their handling of concurrent refresh requests. Need either distributed locking per-link or leader election for the worker.

6. **Rate limiting for webhook endpoints**: Platform webhooks can deliver bursts of events (e.g., during a raid or sub train). The webhook handlers need rate limiting or backpressure to prevent overwhelming the sentiment buffer. Redis-backed rate limiting per platform session is the likely approach.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md) for the full planning document.*
