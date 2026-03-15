# Broadcast Plugin Deep Dive

> **Plugin**: lib-broadcast (not yet created)
> **Schema**: `schemas/broadcast-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: broadcast-platforms (MySQL), broadcast-sessions (Redis), broadcast-sentiment-buffer (Redis), broadcast-outputs (Redis), broadcast-cameras (Redis), broadcast-lock (Redis) — all planned
> **Layer**: L3 AppFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Implementation Map**: [docs/maps/BROADCAST.md](../maps/BROADCAST.md)
> **Short**: Streaming platform integration for live content broadcasting

## Overview

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast. Game-agnostic: which platforms are enabled and how sentiment categories map to game emotions are configured via environment variables and API calls. Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks (justified exception -- platform callbacks, not browser-facing).

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
 eventId: Guid # Required event identifier (per event schema convention)
 streamSessionId: Guid # The lib-showtime in-game session (if linked)
 platformSessionId: Guid # The lib-broadcast platform session
 timestamp: DateTime # When this pulse was assembled
 intervalSeconds: int # Configured pulse interval
 approximateViewerCount: int # Platform-reported viewer count (approximate)
 sentiments: SentimentEntry[] # The batch
```

Each entry in the batch:

```
SentimentEntry:
 category: SentimentCategory # Enum: Excited, Supportive, Critical, Curious,
 # Surprised, Amused, Bored, Hostile
 intensity: float # 0.0 to 1.0 (strength of sentiment)
 trackingId: Guid? # null = anonymous, non-null = "important" viewer
 viewerType: TrackedViewerType? # null = anonymous, non-null = role category
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
| `Returner` | Has been present across multiple streaming sessions (requires Potential Extension #1 — not available in v1) |

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

Each fallback transition is reported via `x-lifecycle` `OutputUpdatedEvent` with `changedFields: ["videoSource"]` so consumers (lib-showtime L4) can react to degraded broadcast quality.

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
| `WebhookMaxEventsPerSessionPerMinute` | `BROADCAST_WEBHOOK_MAX_EVENTS_PER_SESSION_PER_MINUTE` | int | `600` | `minimum: 1`, `maximum: 100000` | Defense-in-depth rate limit for webhook events per platform session per minute. Redis atomic counter with 60s TTL (same pattern as Chat rate limiting). Events exceeding the limit are dropped silently (logged at Debug). The sentiment buffer's max batch size and TTL provide the primary burst protection; this is a safety net. |
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
| `OutputAudioCodec` | `BROADCAST_OUTPUT_AUDIO_CODEC` | AudioCodec | `Aac` | enum | Audio codec for RTMP output. Enum: `Aac`, `Mp3`, `Opus`. Per FOUNDATION TENETS (licensing), only LGPL-compliant codecs are valid enum values. |
| `OutputAudioBitrate` | `BROADCAST_OUTPUT_AUDIO_BITRATE` | string | `128k` | | Audio bitrate for RTMP output |
| `OutputVideoCodec` | `BROADCAST_OUTPUT_VIDEO_CODEC` | VideoCodec | `LibVpx` | enum | Video codec for RTMP output. Enum: `LibVpx`, `LibVpxVp9`. Per FOUNDATION TENETS (licensing), only LGPL-compliant codecs are valid -- GPL codecs (`libx264`, `libx265`) are excluded from the enum entirely, enforcing compliance at the schema level. |
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
│ Broadcast Service: The Privacy Boundary │
├──────────────────────────────────────────────────────────────────────────┤
│ │
│ EXTERNAL WORLD (PII, text, usernames) │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ │
│ │ Twitch │ │ YouTube │ │ Custom │ │ Game │ │
│ │ EventSub │ │ Webhooks │ │ RTMP │ │ Cameras │ │
│ └─────┬────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘ │
│ │ │ │ │ │
│ ══════╪════════════╪══════════════╪══════════════╪═══════════════════ │
│ ║ ▼ ▼ ▼ ▼ ║ │
│ ║ lib-broadcast (L3) ║ │
│ ║ ┌────────────────────────────────────────────────────────────┐ ║ │
│ ║ │ Platform Webhook Handlers (x-controller-only, exempt) │ ║ │
│ ║ │ Twitch: HMAC signature validation │ ║ │
│ ║ │ YouTube: verification token validation │ ║ │
│ ║ │ Custom: configurable HMAC validation │ ║ │
│ ║ │ Camera API Endpoints (/broadcast/camera/announce|retire) │ ║ │
│ ║ └─────────────────────┬──────────────────────────────────────┘ ║ │
│ ║ │ raw events ║ │
│ ║ ▼ ║ │
│ ║ ┌────────────────────────────────────────────────────────────┐ ║ │
│ ║ │ ISentimentProcessor │ ║ │
│ ║ │ chat text → sentiment category + intensity │ ║ │
│ ║ │ subscriptions → Excited/Supportive sentiment │ ║ │
│ ║ │ raids → Excited sentiment (RaidLeader tracked) │ ║ │
│ ║ │ emotes → mapped to sentiment categories │ ║ │
│ ║ │ │ ║ │
│ ║ │ Redis-backed: hashedPlatformUserId → trackingId │ ║ │
│ ║ │ (session-scoped TTL, non-reversible, tenet-compliant) │ ║ │
│ ║ └─────────────────────┬──────────────────────────────────────┘ ║ │
│ ║ │ anonymous sentiments ║ │
│ ║ ▼ ║ │
│ ║ ┌──────────────────────────┐ ┌─────────────────────────────┐ ║ │
│ ║ │ Sentiment Buffer (Redis) │──▶│ SentimentBatchPublisher │ ║ │
│ ║ │ TTL-based cleanup │ │ Every 15s: drain → publish │ ║ │
│ ║ └──────────────────────────┘ └──────────────┬──────────────┘ ║ │
│ ║ │ ║ │
│ ║ ┌────────────────────────────────────────────────────────────┐ ║ │
│ ║ │ IBroadcastCoordinator (Singleton — local process cache) │ ║ │
│ ║ │ ConcurrentDictionary<Guid, BroadcastContext> │ ║ │
│ ║ │ NON-AUTHORITATIVE — Redis broadcast-outputs is truth │ ║ │
│ ║ │ Startup reconciliation: reads Redis, rebuilds handles │ ║ │
│ ║ │ FFmpeg process lifecycle (start/stop/restart) │ ║ │
│ ║ │ RTMP URL validation via FFprobe │ ║ │
│ ║ │ Fallback cascade (primary→stream→image→default→black) │ ║ │
│ ║ │ Stream key masking in all outputs │ ║ │
│ ║ └────────────────────────────────────────────────────────────┘ ║ │
│ ╚═══════════════════════════════════════════════════════════════════╝ │
│ │ │
│ INTERNAL WORLD (anonymous sentiment values, no PII) │
│ │ │
│ ▼ │
│ broadcast.audience.pulse events │
│ (SentimentCategory enum + float intensity + optional opaque GUID) │
│ │
│ Consumed by: │
│ ┌──────────────────────────────────────────────────────────────┐ │
│ │ lib-showtime (L4) -- blends real sentiments with simulated │ │
│ │ audience members to create the │ │
│ │ in-game streaming metagame │ │
│ └──────────────────────────────────────────────────────────────┘ │
│ │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Schema & Generation
- Create broadcast-api.yaml schema with all endpoints (22 endpoints across 6 groups -- includes 2 camera endpoints)
- **MANDATORY**: All user-facing endpoints (`x-permissions: [user]`) must NOT accept `accountId` in request bodies. Use `webSocketSessionId` and resolve account server-side. This applies to: link, unlink, list, start, stop, associate, status, list sessions. The implementation map pseudo-code currently uses `body.accountId` — the schema must use session-resolved identity instead.
- Declare `x-service-layer: AppFeatures` at schema root (default is GameFeatures -- wrong layer without this)
- Declare `x-references` block for lib-resource cleanup of non-account entity references; for account-owned data, subscribe to `account.deleted` per tenets's Account Deletion Cleanup Obligation
- Create broadcast-events.yaml schema using `x-lifecycle` with `topic_prefix: broadcast` for PlatformLink, PlatformSession, and Output entities; plus 1 custom event (`broadcast.audience.pulse`)
- Define consumed event models inline in `broadcast-events.yaml` (voice events, session events -- cannot `$ref` other service event files per FOUNDATION TENETS)
- Create broadcast-configuration.yaml schema (36 configuration properties with validation ranges)
- Create broadcast-client-events.yaml (5 client events: output started/stopped/source-changed, session started/ended)
- Define `AudioCodec` and `VideoCodec` enums in configuration schema (LGPL-compliant codecs only per tenets)
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

<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/577 -->
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

**Spec gaps** ([#577](https://github.com/beyond-immersion/bannou-service/issues/577)): Implementation map is missing (1) `IBroadcastCoordinator` formal interface definition with complete method signatures, (2) `BroadcastModel`/`BroadcastContext` field-level schemas, (3) FFprobe command/success-criteria specification. *(Event handler pseudo-code and startup reconciliation pseudo-code were added to the map and are no longer gaps.)*

### Phase 5: Webhook Subscription Management
- Implement Twitch EventSub subscription lifecycle (create, renew, delete)
- Implement YouTube webhook subscription lifecycle
- Implement webhook subscription manager background worker with singleton lock and configurable TTL

---

## Potential Extensions

<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/569 -->
1. **Cross-session returner detection**: Currently tracking IDs are per-session only. A hashed-platform-user-ID approach could enable "returner" detection across sessions without storing the actual platform user ID. Hash with a rotating salt, stored encrypted, deleted after configurable retention. **Requires privacy/legal review and cryptographic design decisions before implementation** -- see [GitHub Issue #569](https://github.com/beyond-immersion/bannou-service/issues/569).

<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/570 -->
2. **Broadcast recording**: FFmpeg can simultaneously output to both RTMP and a local file. Recorded broadcasts could be uploaded to the Asset service for archival. Useful for highlight reels and content flywheel integration. The v1 architecture largely accommodates this with localized changes (FFmpeg tee muxer, `BroadcastModel` recording fields, Asset L3-to-L3 dependency with graceful degradation). **Requires design decisions before implementation** on: (a) consent distinction between live broadcasting and persistent recording for VoiceRoom sources, (b) upload lifecycle orchestration (when/how to upload to Asset), (c) local storage management configuration, and (d) recording format/codec selection under licensing constraints -- see [GitHub Issue #570](https://github.com/beyond-immersion/bannou-service/issues/570).

<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/572 -->
3. **Custom sentiment models**: Per-game-service sentiment processing rules (different games care about different emotional categories). Configurable via a sentiment model store rather than hardcoded categories. **Requires design decisions before implementation**: the v1 `ISentimentProcessor` interface already supports swapping *processing logic* (how text maps to categories), but this extension asks for different *output categories* per game, which directly conflicts with Intentional Quirk #1's fixed 8-value `SentimentCategory` enum. Key questions: (a) should `SentimentCategory` become opaque strings (Category B) instead of a fixed enum (Category C), (b) how would lib-showtime (L4) handle unknown categories in its deterministic audience behavior mapping, (c) whether per-game weighting within the existing 8 categories is sufficient (in which case `ISentimentProcessor` already covers it), (d) storage mechanism for category vocabularies (state store vs configuration per tenets) -- see [GitHub Issue #572](https://github.com/beyond-immersion/bannou-service/issues/572).

<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/576 -->
4. **Director-coordinated broadcast priority**: During directed events, Director signals broadcast priority levels indicating event importance (0 = no broadcast, 1-10 = priority). Broadcast uses this to prioritize camera selection, source quality, and RTMP output allocation when multiple events compete for broadcast resources. Requires a priority-aware source selection mechanism and potentially a broadcast scheduling API that Director can call to reserve broadcast capacity ahead of planned events. **Requires design decisions before implementation**: Director's `DirectedEvent` model already has `broadcastPriority` (0-10) and references `IBroadcastClient` as a soft dependency, but the v1 Broadcast architecture has no corresponding APIs, state models, or allocation logic. Key design questions: (a) preemption vs. first-access when higher-priority events compete for `MaxConcurrentOutputs` slots, (b) reservation API design (ahead-of-time capacity holds vs. on-demand allocation at event activation), (c) priority conflict resolution when multiple directed events have equal priority, (d) automated camera selection semantics vs. manual camera assignment per output, (e) source quality differentiation (FFmpeg argument variation per priority level), (f) state model changes to `BroadcastModel` for priority tracking. See [DIRECTOR.md](DIRECTOR.md) Broadcast & Showtime Integration section -- see [GitHub Issue #576](https://github.com/beyond-immersion/bannou-service/issues/576).

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

1. **Sentiment categories are an enum, not opaque strings**: Unlike most Bannou extensibility patterns (seed type codes, collection type codes), sentiment categories are a fixed enum (`Excited`, `Supportive`, `Critical`, `Curious`, `Surprised`, `Amused`, `Bored`, `Hostile`). This is intentional -- lib-showtime (L4) needs to map sentiments to audience behavior deterministically. Adding a new sentiment category requires schema changes, which is acceptable because it's a rare, deliberate extension. No deprecation lifecycle needed (Category C -- enum values with no stored instances referencing them by ID).

2. **Tracking ID mapping is Redis-backed, not in-memory**: The `hashedPlatformUserId → trackingId` mapping is stored in the `broadcast-sessions` Redis store with session-scoped TTL, not in a per-instance `ConcurrentDictionary`. This ensures cross-instance tracking consistency (the same viewer gets the same tracking ID regardless of which instance processes their events) while preserving privacy properties: the mapping is ephemeral (destroyed when the session ends via TTL or explicit delete), uses a hashed non-reversible key, and has no relationship to the original platform user ID. Per IMPLEMENTATION TENETS (multi-instance safety).

3. **Webhook endpoints are internet-facing (justified exception)**: Unlike most Bannou services (POST-only, internal), the webhook endpoints receive callbacks from Twitch/YouTube via NGINX. They validate platform-specific signatures (Twitch HMAC, YouTube verification token) and route events internally. These are the only internet-facing endpoints in lib-broadcast. Declared with `x-permissions: []` (not exposed to WebSocket clients) and `x-controller-only: true` (HMAC validation requires raw body access before model binding).

4. **FFmpeg as a separate process**: FFmpeg runs as a separate OS process, not a linked library. This is a deliberate license compliance decision (LGPL process isolation) and also provides fault isolation -- a crashing FFmpeg process doesn't bring down the Bannou service.

5. **Stream key masking is absolute**: Full RTMP URLs (containing stream keys) are NEVER exposed in API responses, log messages, error events, or state store queries visible via the debug API. The only place the full URL exists is in the encrypted broadcast state store and in the FFmpeg process command-line arguments (which are not logged). This is a security decision, not a convenience trade-off.

6. **Sentiment pulses are fire-and-forget**: If no consumer subscribes to `broadcast.audience.pulse` events, the pulses are published and discarded by the message bus. lib-broadcast does not buffer, retry, or persist pulses beyond the initial publication. This is intentional -- sentiment data is inherently ephemeral.

7. **Platform link tokens are encrypted at rest**: OAuth access tokens and refresh tokens are encrypted with `TokenEncryptionKey` before storage in MySQL. If the encryption key is lost, all platform links must be re-established. There is no key rotation mechanism in v1 -- this is a known limitation, not a bug.

8. **IBroadcastCoordinator is a local process cache, not authoritative state**: The `ConcurrentDictionary<Guid, BroadcastContext>` in `IBroadcastCoordinator` holds local FFmpeg process handles for the current instance only. It is NOT the source of truth -- the `broadcast-outputs` Redis store is authoritative. On startup, the coordinator reads Redis and reconciles: broadcasts owned by this instance have process handles rebuilt; broadcasts owned by crashed instances are marked as failed. This per-instance supervision pattern is tenet-compliant because the coordinator never holds state that other instances need. Per IMPLEMENTATION TENETS (multi-instance safety).

9. **Camera sources are registered via API, not events**: Game engine cameras register via `/broadcast/camera/announce` (with TTL heartbeat) rather than publishing `camera.stream.started` events. This avoids creating orphaned event topics with no Bannou publisher service (per FOUNDATION TENETS -- cross-service communication discipline).

10. **Platform link deletion cascades session records**: When a platform link is hard-deleted (unlink), all `PlatformSessionModel` records in Redis referencing that link are also deleted. Session records are instance data with no cross-service references, so cascade delete is appropriate per IMPLEMENTATION TENETS (-- no deprecation for instance data).

11. **Sentiment processing starts with keyword/emoji matching**: The v1 `ISentimentProcessor` implementation uses keyword matching and emoji-to-category mapping rather than NLP. This is intentionally simple -- fast, no external dependencies, and sufficient for the 8 fixed sentiment categories. The `ISentimentProcessor` interface allows swapping to a lightweight NLP model in the future without changing the rest of the pipeline (see Potential Extensions #1). The 15-second batching window provides latency tolerance if a more expensive classifier is used later.

12. **Webhook burst resilience is architectural, not rate-limit-dependent**: The sentiment buffer is inherently resilient to webhook bursts (raids, sub trains). Three layers provide protection: (a) buffer entries have TTL = 2x pulse interval (30s default), so entries self-expire even without consumption; (b) `SentimentBatchPublisher` caps published entries at `SentimentMaxBatchSize` (200 default), dropping lowest-intensity overflow; (c) individual `BufferedSentimentEntry` values are tiny (enum + float + optional GUID), so even thousands in Redis are negligible. Defense-in-depth rate limiting is added at the webhook handler level via `WebhookMaxEventsPerSessionPerMinute` (default 600, Redis atomic counter with 60s TTL per platform session, following the established Chat/Save-Load pattern). Events exceeding the limit are dropped silently (logged at Debug). This is a safety net, not the primary burst protection -- the architecture handles bursts by design.

### Design Considerations (Requires Planning)

*No active design considerations. All prior items were resolved during the 2026-03-05 audit — see Intentional Quirks #11 and #12 for the resolved designs.*

---

## Work Tracking

### Completed

*All prior completed items (2026-03-05 audit, 2026-03-08 migration) have been processed and removed during the 2026-03-15 maintenance pass.*
