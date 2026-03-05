# Broadcast Plugin Deep Dive

> **Plugin**: lib-broadcast (not yet created)
> **Schema**: `schemas/broadcast-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: broadcast-platforms (MySQL), broadcast-sessions (Redis), broadcast-sentiment-buffer (Redis), broadcast-outputs (Redis), broadcast-cameras (Redis), broadcast-lock (Redis) — all planned
> **Layer**: L3 AppFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Implementation Map**: [docs/maps/BROADCAST.md](../maps/BROADCAST.md)

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

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-showtime (L4) | Subscribes to `broadcast.audience.pulse` for real audience blending with simulated audiences |
| lib-showtime (L4) | Subscribes to `x-lifecycle` platform-session and broadcast-output events for session/broadcast awareness |
| lib-director *(planned)* | During directed events, Director coordinates broadcast output: signaling priority levels for camera/source selection, associating platform sessions with directed events for metrics capture, and timing broadcast start/stop around event lifecycle phases. See [DIRECTOR.md](DIRECTOR.md) Broadcast & Showtime Integration |

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
- Create broadcast-configuration.yaml schema (35 configuration properties with validation ranges)
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

8. **Director-coordinated broadcast priority**: During directed events, Director signals broadcast priority levels indicating event importance (0 = no broadcast, 1-10 = priority). Broadcast uses this to prioritize camera selection, source quality, and RTMP output allocation when multiple events compete for broadcast resources. Requires a priority-aware source selection mechanism and potentially a broadcast scheduling API that Director can call to reserve broadcast capacity ahead of planned events. See [DIRECTOR.md](DIRECTOR.md).

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

5. **T32 Account Identity Boundary compliance**: Platform management and session management endpoints (link, unlink, list, start, stop, associate, status, list sessions) are declared with `x-permissions: [user]`, meaning they are exposed to WebSocket clients. Per T32, client-facing endpoints must NOT accept `accountId` in request bodies — the account should be resolved server-side from the WebSocket session. The schema and implementation map pseudo-code currently pass `accountId` explicitly. When creating the schema, these endpoints should use session-resolved identity instead.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase.*
