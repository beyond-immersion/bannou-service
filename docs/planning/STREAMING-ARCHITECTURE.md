# Streaming Architecture: Platform Integration (L3) + In-Game Metagame (L4)

> **Status**: Design Draft
> **Last Updated**: 2026-02-10
> **Depends On**: Voice Plugin (lib-voice), Collection Plugin (lib-collection), Seed Plugin (lib-seed), Contract Plugin (lib-contract), Currency Plugin (lib-currency), Relationship Plugin (lib-relationship)
> **Related**: [VOICE-STREAMING.md](VOICE-STREAMING.md) (RTMP I/O for scaled voice rooms)
> **Source Context**: arcadia-kb Audience Service Architecture, Advanced Audience Dynamics, Systems Mining Synthesis (2026-02-10)

---

## Executive Summary

Two new services that together create a streaming metagame where players experience "audience participation" in-game -- with or without a real external audience.

| Service | Layer | Role |
|---------|-------|------|
| **lib-stream** | L3 (AppFeatures) | Platform streaming integration: link Twitch/YouTube accounts, ingest real audience data, emit privacy-safe sentiment events |
| **lib-streaming** | L4 (GameFeatures) | In-game streaming metagame: simulated audience pools, hype trains, streamer careers, real-simulated audience blending |

**The core design principle**: lib-streaming (L4) provides a complete, fully functional streaming metagame using simulated audiences. lib-stream (L3) is an optional enrichment layer that feeds real audience data into the simulation. The game works identically with or without L3 -- real audience data is an invisible seasoning, not a required ingredient.

**Privacy-first**: Real audience data never enters the event system as text, usernames, or PII. lib-stream processes raw platform events into **batched sentiment pulses** -- arrays of anonymous sentiment values with optional opaque tracking GUIDs for consistency. No platform user IDs, no message content, no personally identifiable information leaves lib-stream's boundary.

---

## Vision Alignment

### Which North Stars This Serves

**Living Game Worlds**: The streaming metagame creates another layer of autonomous behavior -- simulated audiences watching, reacting, following, and migrating between in-game streamers. The audience IS part of the world, not a UI overlay.

**The Content Flywheel**: Streaming sessions generate events (hype trains, world-first discoveries, audience milestones) that feed into character history, realm history, and analytics. A legendary streaming moment becomes part of the world's lore.

**Emergent Over Authored**: Audience behavior emerges from personality matching, interest decay, and competitive attention dynamics -- not scripted reactions. When real audiences blend in, they add genuine unpredictability that no algorithm can replicate.

### Omega Realm Integration

The Omega realm (cyberpunk meta-dashboard) provides the diegetic context for streaming. The player's "full-dive VR machine" has a streaming module. In Omega, the streaming metagame is explicit -- you can see your audience stats, manage your stream, and compete with other streamers. In other realms, the streaming concept might manifest differently (e.g., a bard performing for a crowd in Arcadia).

---

## Architecture Overview

```
                    EXTERNAL PLATFORMS
                    ┌──────────┐  ┌──────────┐
                    │  Twitch  │  │  YouTube  │
                    └────┬─────┘  └─────┬────┘
                         │              │
              Webhooks / OAuth API      │
                         │              │
    ┌────────────────────▼──────────────▼──────────────────┐
    │  lib-stream (L3 AppFeatures)                          │
    │                                                       │
    │  Platform Account Linking (OAuth)                     │
    │  Platform Session Detection (webhooks)                │
    │  Raw Event Ingestion (chat, subs, raids)              │
    │  Sentiment Processing (text → sentiment values)       │
    │  Tracking ID Generation (ephemeral, non-PII)          │
    │  ─────────────────────────────────────────────        │
    │  Publishes: stream.audience.pulse (batched)           │
    │  Publishes: stream.platform.session.started/ended     │
    │  No game knowledge. No L2 dependencies.               │
    └───────────────────────┬──────────────────────────────┘
                            │
              stream.audience.pulse events
              (sentiment arrays, no PII)
                            │
                            ▼
    ┌───────────────────────────────────────────────────────┐
    │  lib-streaming (L4 GameFeatures)                      │
    │                                                       │
    │  Simulated Audience Pool (always available)           │
    │  Interest Matching Engine                             │
    │  Hype Train Mechanics                                 │
    │  Streamer Career (Seed type: streamer)                │
    │  Follow/Subscribe Dynamics                            │
    │  ─────────────────────────────────────────────        │
    │  Real Audience Blending (when L3 available):          │
    │    Sentiment pulses → "real-derived" audience members │
    │    Tracking GUIDs → consistent phantom identities     │
    │    Blended seamlessly with simulated members          │
    │  ─────────────────────────────────────────────        │
    │  Depends on: L0, L1, L2 (game-session, character)    │
    │  Soft depends on: L3 (lib-stream, optional)           │
    │  Composable with: Seed, Collection, Currency,         │
    │                    Contract, Relationship              │
    └───────────────────────────────────────────────────────┘
```

---

## Privacy-First Sentiment Architecture

This is the load-bearing design decision. Real audience data must never create compliance liabilities (GDPR, CCPA, data deletion orders). The solution: real audience events are reduced to anonymous sentiment values before leaving lib-stream's process boundary.

### Why No Text Content

1. **Data deletion compliance**: If a Twitch user requests data deletion under GDPR, we'd need to purge their messages from every downstream system (analytics, state stores, event logs). With sentiment-only data, there's nothing to delete -- the original text never left lib-stream.
2. **Analytics cache problem**: Analytics ingests events for aggregation. Flushing specific user data from analytical stores is operationally expensive and error-prone. Sentiment values have no user association.
3. **Legal exposure**: Storing third-party platform user content creates licensing and liability questions. Sentiment approximations are derived data, not reproductions.

### The Sentiment Pulse Model

lib-stream processes raw platform events (chat messages, subscriptions, raids, emotes) into periodic **sentiment pulses** -- batched arrays of anonymous sentiment data points.

```
SentimentPulse:
  pulseId: Guid                       # Unique pulse identifier
  streamSessionId: Guid               # The lib-streaming in-game session (if linked)
  platformSessionId: Guid             # The lib-stream platform session
  timestamp: DateTime                 # When this pulse was assembled
  intervalSeconds: int                # Configured pulse interval
  approximateViewerCount: int         # Platform-reported viewer count (approximate)
  sentiments: SentimentEntry[]        # The batch (see below)
```

Each entry in the batch:

```
SentimentEntry:
  category: SentimentCategory         # Enum: see below
  intensity: float                    # 0.0 to 1.0 (strength of sentiment)
  trackingId: Guid?                   # null = anonymous, non-null = "important" viewer
  viewerType: TrackedViewerType?      # null = anonymous, non-null = role category
```

### Sentiment Categories

These are broad emotional categories derived from platform signals. The categories are deliberately coarse -- fine-grained sentiment analysis would require storing context that approaches PII territory.

```
SentimentCategory enum:
  Excited       # High-energy positive (hype, celebration, spam of positive emotes)
  Supportive    # Calm positive (encouragement, constructive comments)
  Critical      # Negative feedback (complaints, dissatisfaction)
  Curious       # Engagement without clear valence (questions, "what happened?")
  Surprised     # Unexpected reaction (plot twists, world-first discoveries)
  Amused        # Entertainment response (jokes, funny moments)
  Bored         # Low engagement signals (AFK, minimal interaction)
  Hostile       # Aggressive negativity (distinct from critical -- toxicity signals)
```

### Tracked Viewers ("Important" Sentiments)

Most sentiments in a pulse are anonymous -- just a category + intensity with no tracking information. A configurable subset of "important" viewers receive opaque tracking GUIDs:

```
TrackedViewerType enum:
  Subscriber    # Platform subscriber (Twitch sub, YouTube member)
  Moderator     # Platform moderator
  RaidLeader    # Led a raid into the stream
  VIP           # Platform VIP designation
  HighEngager   # Algorithmically determined high-engagement viewer
  Returner      # Has been present across multiple streaming sessions
```

**How tracking IDs work**:

1. lib-stream maintains an **ephemeral, in-memory** mapping: `platformUserId → trackingId`
2. This mapping exists ONLY during an active platform session
3. When the session ends, the mapping is destroyed -- the tracking IDs become orphaned
4. The tracking IDs are Bannou-generated GUIDs with NO relationship to platform user IDs
5. The same real viewer gets the SAME tracking ID across pulses within a session (consistency)
6. But across sessions, they get a NEW tracking ID (no cross-session tracking)
7. There is NO way to reverse a tracking ID back to a platform user

**Configuration controls**:

| Config | Purpose | Default |
|--------|---------|---------|
| MaxTrackedViewersPerSession | Cap on simultaneous tracked viewers | 50 |
| TrackedViewerEngagementThreshold | Minimum engagement score to earn tracking | 0.7 |
| TrackSubscribers | Auto-track platform subscribers | true |
| TrackModerators | Auto-track platform moderators | true |
| TrackRaidLeaders | Auto-track raid leaders | true |

**What this enables in lib-streaming (L4)**:

- "Your stream has attracted a dedicated fan!" (tracked Returner appearing across multiple pulses)
- "A critic has been watching your last 3 sessions..." (tracked HighEngager with Critical sentiment)
- "A VIP viewer just tuned in!" (tracked VIP appearing in pulse)
- Players can perceive behavioral differences between simulated audience members and real-derived ones
- But they can never CONFIRM which are real -- creating a natural Turing test metagame

### Batch Timing and Size

Pulses are published at configurable intervals with minimum batch requirements:

| Config | Purpose | Default |
|--------|---------|---------|
| SentimentPulseIntervalSeconds | How often pulses are published | 15 |
| SentimentMinBatchSize | Minimum sentiments before publishing a pulse | 5 |
| SentimentMaxBatchSize | Maximum sentiments per pulse (overflow drops lowest-intensity) | 200 |

**Timing rationale**: 15-second intervals create enough delay that individual messages can't be correlated to specific sentiment entries by timing alone. Combined with batching, this makes de-anonymization impractical even for someone monitoring both the platform chat and the sentiment stream.

---

## lib-stream: L3 App Feature (Platform Streaming Integration)

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L3 (AppFeatures) |
| **Plugin** | `plugins/lib-stream/` |
| **Schema prefix** | `stream` |
| **Service name** | `stream` |
| **Hard dependencies** | L0 (state, messaging), L1 (account, auth) |
| **Soft dependencies** | None |
| **Cannot depend on** | L2, L4 |
| **When absent** | lib-streaming runs on 100% simulated audiences |

### What This Service Does

1. **Platform Account Linking**: OAuth flows to connect Bannou accounts to Twitch, YouTube, or custom RTMP endpoints
2. **Platform Session Detection**: Webhook subscriptions (Twitch EventSub, YouTube webhooks) detect when linked accounts go live
3. **Raw Event Ingestion**: Consumes platform events (chat messages, subscriptions, raids, follows, emote usage) via platform APIs/webhooks
4. **Sentiment Processing**: Converts raw events into sentiment values using configurable processing rules
5. **Sentiment Pulse Publishing**: Batches sentiments and publishes `stream.audience.pulse` events on the message bus
6. **Stream Key Security**: Manages RTMP credentials with masking in all responses and logs

### What This Service Does NOT Do

- Does not know about games, characters, realms, or any L2 concepts
- Does not manage simulated audiences (that's lib-streaming L4)
- Does not handle FFmpeg/RTMP output (that stays in lib-voice per VOICE-STREAMING.md)
- Does not store message content or platform usernames beyond the ephemeral processing window
- Does not persist the platformUserId→trackingId mapping (in-memory only)

### API Endpoints

All endpoints use POST-only pattern for zero-copy WebSocket routing.

#### Platform Account Management

```yaml
/stream/platform/link:
  post:
    operationId: linkPlatform
    summary: Initiate OAuth flow to link a streaming platform account
    description: >
      Returns an OAuth authorization URL for the specified platform.
      The user completes authorization on the platform, which redirects
      back to the callback URL with an authorization code. The callback
      handler exchanges this for access/refresh tokens and stores the link.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/LinkPlatformRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/LinkPlatformResponse'
      '400':
        description: Invalid platform or already linked
      '409':
        description: Platform account already linked to another Bannou account

/stream/platform/callback:
  post:
    operationId: platformCallback
    summary: Handle OAuth callback from streaming platform
    description: >
      Called by the auth redirect flow after the user authorizes on the
      platform. Exchanges the authorization code for tokens and creates
      the platform link. This is an internal endpoint called by the
      website service redirect handler.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/PlatformCallbackRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformLinkDetail'
      '400':
        description: Invalid or expired authorization code
      '404':
        description: No pending link request found

/stream/platform/unlink:
  post:
    operationId: unlinkPlatform
    summary: Unlink a streaming platform account
    description: >
      Removes the platform link and revokes access tokens. If a platform
      session is currently active, it will be detached (sentiment processing
      stops but the external stream continues on the platform).
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/UnlinkPlatformRequest'
    responses:
      '200':
        description: Platform unlinked
      '404':
        description: Platform link not found

/stream/platform/list:
  post:
    operationId: listPlatforms
    summary: List linked streaming platforms for an account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListPlatformsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListPlatformsResponse'
```

#### Platform Session Management

```yaml
/stream/session/start:
  post:
    operationId: startPlatformSession
    summary: Start monitoring a platform streaming session
    description: >
      Begins active sentiment processing for a linked platform that has
      gone live. Can be triggered automatically by platform webhooks or
      manually by the user. Associates the platform session with an
      optional lib-streaming in-game session ID for event routing.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StartPlatformSessionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformSessionStatus'
      '400':
        description: Platform not linked or not currently live
      '404':
        description: Platform link not found
      '409':
        description: Session already active for this platform link

/stream/session/stop:
  post:
    operationId: stopPlatformSession
    summary: Stop monitoring a platform streaming session
    description: >
      Ends sentiment processing. Destroys the ephemeral tracking ID
      mapping. The external stream continues on the platform -- this
      only affects Bannou's ingestion.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StopPlatformSessionRequest'
    responses:
      '200':
        description: Session monitoring stopped
      '404':
        description: No active session found

/stream/session/associate:
  post:
    operationId: associateSession
    summary: Associate a platform session with an in-game streaming session
    description: >
      Links a currently-active platform session to a lib-streaming in-game
      session ID. Sentiment pulses will include this association so
      lib-streaming knows which in-game session the real audience data
      belongs to. Can be called mid-session to change or set the association.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/AssociateSessionRequest'
    responses:
      '200':
        description: Association updated
      '404':
        description: Platform session not found

/stream/session/status:
  post:
    operationId: getPlatformSessionStatus
    summary: Get current platform session status
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/PlatformSessionStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/PlatformSessionStatus'
      '404':
        description: No active session found

/stream/session/list:
  post:
    operationId: listPlatformSessions
    summary: List active and recent platform sessions for an account
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListPlatformSessionsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListPlatformSessionsResponse'
```

#### Webhook Endpoints

```yaml
/stream/webhook/twitch:
  post:
    operationId: handleTwitchWebhook
    summary: Handle Twitch EventSub webhook notifications
    description: >
      Receives Twitch EventSub notifications for stream.online,
      stream.offline, channel.subscribe, channel.raid, and other
      subscribed event types. Validates the webhook signature using
      the configured secret. This is the primary ingestion point for
      Twitch audience data.
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/TwitchWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid signature or payload

/stream/webhook/youtube:
  post:
    operationId: handleYouTubeWebhook
    summary: Handle YouTube webhook notifications
    description: >
      Receives YouTube Data API push notifications for live stream
      events. Validates using the configured verification token.
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/YouTubeWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid verification token

/stream/webhook/custom:
  post:
    operationId: handleCustomWebhook
    summary: Handle custom RTMP platform webhook notifications
    description: >
      Generic webhook handler for custom streaming platforms that
      support webhook notifications. Uses HMAC signature verification
      with a per-platform-link secret.
    x-permissions: []
    requestBody:
      schema:
        $ref: '#/components/schemas/CustomWebhookPayload'
    responses:
      '200':
        description: Webhook processed
      '400':
        description: Invalid signature
```

#### Admin / Debug

```yaml
/stream/admin/pulse/latest:
  post:
    operationId: getLatestPulse
    summary: Get the most recent sentiment pulse for a platform session
    description: >
      Admin endpoint for debugging sentiment processing. Returns the
      last published pulse for the specified session.
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/LatestPulseRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/SentimentPulse'
      '404':
        description: No active session or no pulses yet

/stream/admin/sentiment/test:
  post:
    operationId: testSentimentProcessing
    summary: Test sentiment processing with sample input
    description: >
      Processes sample text through the sentiment pipeline and returns
      the resulting sentiment entries WITHOUT publishing them. For
      testing and calibration only.
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/TestSentimentRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/TestSentimentResponse'
```

### Request/Response Models

```yaml
# Platform Linking
StreamPlatform:
  type: string
  enum: [Twitch, YouTube, Custom]
  description: Supported streaming platforms

LinkPlatformRequest:
  type: object
  required: [accountId, platform]
  properties:
    accountId:
      type: string
      format: uuid
    platform:
      $ref: '#/components/schemas/StreamPlatform'
    callbackUrl:
      type: string
      nullable: true
      description: Override callback URL (defaults to configured per-platform callback)
    customRtmpEndpoint:
      type: string
      nullable: true
      description: Required when platform is Custom; the RTMP ingest URL

LinkPlatformResponse:
  type: object
  required: [authorizationUrl, linkRequestId]
  properties:
    authorizationUrl:
      type: string
      description: URL to redirect the user to for OAuth authorization
    linkRequestId:
      type: string
      format: uuid
      description: Identifier for this pending link request

PlatformCallbackRequest:
  type: object
  required: [linkRequestId, authorizationCode]
  properties:
    linkRequestId:
      type: string
      format: uuid
    authorizationCode:
      type: string

PlatformLinkDetail:
  type: object
  required: [linkId, accountId, platform, platformDisplayName, linkedAt, isActive]
  properties:
    linkId:
      type: string
      format: uuid
    accountId:
      type: string
      format: uuid
    platform:
      $ref: '#/components/schemas/StreamPlatform'
    platformDisplayName:
      type: string
      description: Display name on the platform (for user identification only)
    linkedAt:
      type: string
      format: date-time
    isActive:
      type: boolean

UnlinkPlatformRequest:
  type: object
  required: [accountId, linkId]
  properties:
    accountId:
      type: string
      format: uuid
    linkId:
      type: string
      format: uuid

ListPlatformsRequest:
  type: object
  required: [accountId]
  properties:
    accountId:
      type: string
      format: uuid

ListPlatformsResponse:
  type: object
  required: [platforms]
  properties:
    platforms:
      type: array
      items:
        $ref: '#/components/schemas/PlatformLinkDetail'

# Platform Sessions
StartPlatformSessionRequest:
  type: object
  required: [linkId]
  properties:
    linkId:
      type: string
      format: uuid
    inGameSessionId:
      type: string
      format: uuid
      nullable: true
      description: Optional lib-streaming session ID for event routing

StopPlatformSessionRequest:
  type: object
  required: [platformSessionId]
  properties:
    platformSessionId:
      type: string
      format: uuid

AssociateSessionRequest:
  type: object
  required: [platformSessionId, inGameSessionId]
  properties:
    platformSessionId:
      type: string
      format: uuid
    inGameSessionId:
      type: string
      format: uuid

PlatformSessionStatusRequest:
  type: object
  required: [platformSessionId]
  properties:
    platformSessionId:
      type: string
      format: uuid

PlatformSessionStatus:
  type: object
  required: [platformSessionId, linkId, platform, isActive, startedAt, approximateViewerCount]
  properties:
    platformSessionId:
      type: string
      format: uuid
    linkId:
      type: string
      format: uuid
    platform:
      $ref: '#/components/schemas/StreamPlatform'
    isActive:
      type: boolean
    startedAt:
      type: string
      format: date-time
    endedAt:
      type: string
      format: date-time
      nullable: true
    approximateViewerCount:
      type: integer
    inGameSessionId:
      type: string
      format: uuid
      nullable: true
    trackedViewerCount:
      type: integer
      description: How many viewers currently have tracking IDs
    totalPulsesPublished:
      type: integer

ListPlatformSessionsRequest:
  type: object
  required: [accountId]
  properties:
    accountId:
      type: string
      format: uuid
    includeEnded:
      type: boolean
      default: false
    limit:
      type: integer
      default: 10

ListPlatformSessionsResponse:
  type: object
  required: [sessions]
  properties:
    sessions:
      type: array
      items:
        $ref: '#/components/schemas/PlatformSessionStatus'

# Sentiment Models (also used in events)
SentimentCategory:
  type: string
  enum: [Excited, Supportive, Critical, Curious, Surprised, Amused, Bored, Hostile]
  description: >
    Broad emotional category derived from platform signals. Deliberately
    coarse to prevent reconstruction of original content.

TrackedViewerType:
  type: string
  enum: [Subscriber, Moderator, RaidLeader, VIP, HighEngager, Returner]
  description: >
    Role category for tracked viewers. Derived from platform signals
    but abstracted into generic categories. Does NOT identify individuals.

SentimentEntry:
  type: object
  required: [category, intensity]
  properties:
    category:
      $ref: '#/components/schemas/SentimentCategory'
    intensity:
      type: number
      format: float
      minimum: 0.0
      maximum: 1.0
      description: Strength of the sentiment (0 = barely perceptible, 1 = overwhelming)
    trackingId:
      type: string
      format: uuid
      nullable: true
      description: >
        Opaque tracking identifier for "important" viewers. Consistent
        within a session, destroyed when session ends. Cannot be mapped
        back to a platform user. Null for anonymous sentiments.
    viewerType:
      $ref: '#/components/schemas/TrackedViewerType'
      nullable: true
      description: >
        Role category for tracked viewers. Null for anonymous sentiments.
        Only present when trackingId is non-null.

SentimentPulse:
  type: object
  required: [pulseId, platformSessionId, timestamp, intervalSeconds, approximateViewerCount, sentiments]
  properties:
    pulseId:
      type: string
      format: uuid
    platformSessionId:
      type: string
      format: uuid
    inGameSessionId:
      type: string
      format: uuid
      nullable: true
      description: The lib-streaming in-game session, if associated
    timestamp:
      type: string
      format: date-time
    intervalSeconds:
      type: integer
      description: Configured pulse interval at time of emission
    approximateViewerCount:
      type: integer
      description: Platform-reported viewer count (approximate, may lag)
    sentiments:
      type: array
      items:
        $ref: '#/components/schemas/SentimentEntry'
      description: >
        Batched sentiment entries. Most are anonymous (trackingId=null).
        Some have tracking IDs for consistent cross-pulse tracking of
        "important" viewers. Array size varies based on audience activity.

# Webhook Models (opaque -- platform-specific handling in service code)
TwitchWebhookPayload:
  type: object
  description: Raw Twitch EventSub webhook payload (validated via signature)

YouTubeWebhookPayload:
  type: object
  description: Raw YouTube Data API webhook payload (validated via token)

CustomWebhookPayload:
  type: object
  description: Generic webhook payload (validated via HMAC signature)

# Admin Models
LatestPulseRequest:
  type: object
  required: [platformSessionId]
  properties:
    platformSessionId:
      type: string
      format: uuid

TestSentimentRequest:
  type: object
  required: [sampleMessages]
  properties:
    sampleMessages:
      type: array
      items:
        type: string
      description: Sample messages to process through the sentiment pipeline

TestSentimentResponse:
  type: object
  required: [entries]
  properties:
    entries:
      type: array
      items:
        $ref: '#/components/schemas/SentimentEntry'
```

### Service Events

```yaml
# Published by lib-stream
stream.platform.linked:
  description: A streaming platform was linked to a Bannou account
  schema:
    StreamPlatformLinkedEvent:
      type: object
      required: [eventId, timestamp, accountId, linkId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        accountId: { type: string, format: uuid }
        linkId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }

stream.platform.unlinked:
  description: A streaming platform was unlinked from a Bannou account
  schema:
    StreamPlatformUnlinkedEvent:
      type: object
      required: [eventId, timestamp, accountId, linkId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        accountId: { type: string, format: uuid }
        linkId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }

stream.platform.session.started:
  description: A linked account went live on a streaming platform
  schema:
    StreamPlatformSessionStartedEvent:
      type: object
      required: [eventId, timestamp, platformSessionId, accountId, platform]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        platformSessionId: { type: string, format: uuid }
        accountId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }
        inGameSessionId:
          type: string
          format: uuid
          nullable: true

stream.platform.session.ended:
  description: A linked account's stream ended on the platform
  schema:
    StreamPlatformSessionEndedEvent:
      type: object
      required: [eventId, timestamp, platformSessionId, accountId, platform, durationSeconds]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        platformSessionId: { type: string, format: uuid }
        accountId: { type: string, format: uuid }
        platform: { $ref: '#/components/schemas/StreamPlatform' }
        durationSeconds: { type: integer }
        peakViewerCount: { type: integer }

stream.audience.pulse:
  description: >
    Batched sentiment data from a real audience. Privacy-safe: contains
    no text content, no platform usernames, no PII. Published at
    configured intervals during active platform sessions. This is the
    PRIMARY event that lib-streaming (L4) consumes for real audience
    blending.
  schema:
    $ref: '#/components/schemas/SentimentPulse'
```

### State Stores

```yaml
# In state-stores.yaml additions
stream-platforms:
  backend: mysql
  description: >
    Platform link records including encrypted OAuth tokens.
    Durable storage for account-platform associations.

stream-sessions:
  backend: redis
  description: >
    Active platform session tracking. Ephemeral -- sessions are
    cleaned up on end. Used for fast lookup of active sessions
    and routing webhook events to the correct processing pipeline.

stream-sentiment-buffer:
  backend: redis
  description: >
    Buffered sentiments awaiting batch publication. Written to
    continuously during active sessions, flushed every
    SentimentPulseIntervalSeconds. TTL-based cleanup ensures
    no stale data survives session end.
```

### Internal Models (ServiceModels)

These models are NOT in the API schema -- they're internal to the service implementation.

```csharp
/// <summary>
/// Stored in stream-platforms (MySQL). Contains encrypted OAuth tokens.
/// </summary>
internal record PlatformLinkRecord
{
    public Guid LinkId { get; init; }
    public Guid AccountId { get; init; }
    public StreamPlatform Platform { get; init; }
    public string PlatformUserId { get; init; }      // Platform's user ID (for API calls)
    public string PlatformDisplayName { get; init; }
    public string EncryptedAccessToken { get; init; } // Encrypted at rest
    public string EncryptedRefreshToken { get; init; }
    public DateTime TokenExpiresAt { get; init; }
    public DateTime LinkedAt { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Stored in stream-sessions (Redis). Active session tracking.
/// </summary>
internal record PlatformSessionRecord
{
    public Guid PlatformSessionId { get; init; }
    public Guid LinkId { get; init; }
    public Guid AccountId { get; init; }
    public StreamPlatform Platform { get; init; }
    public string PlatformStreamId { get; init; }    // Platform's stream identifier
    public DateTime StartedAt { get; init; }
    public int ApproximateViewerCount { get; set; }
    public Guid? InGameSessionId { get; set; }       // lib-streaming association
    public int TotalPulsesPublished { get; set; }
}

/// <summary>
/// In-memory ONLY. NEVER persisted. Destroyed when session ends.
/// Maps platform user IDs to opaque tracking GUIDs.
/// </summary>
internal class TrackingIdMap
{
    // ConcurrentDictionary for multi-instance safety
    // Key: platform user ID, Value: tracking GUID
    // Cleared entirely when session ends
}
```

### Configuration

```yaml
# stream-configuration.yaml
x-service-configuration:
  # Feature Flags
  StreamEnabled:
    type: boolean
    env: STREAM_ENABLED
    default: false
    description: Master feature flag for the stream service

  # Platform OAuth Configuration
  TwitchClientId:
    type: string
    env: STREAM_TWITCH_CLIENT_ID
    default: ""
    description: Twitch application client ID for OAuth

  TwitchClientSecret:
    type: string
    env: STREAM_TWITCH_CLIENT_SECRET
    default: ""
    description: Twitch application client secret for OAuth

  TwitchWebhookSecret:
    type: string
    env: STREAM_TWITCH_WEBHOOK_SECRET
    default: ""
    description: Secret for Twitch EventSub webhook signature verification

  YouTubeClientId:
    type: string
    env: STREAM_YOUTUBE_CLIENT_ID
    default: ""
    description: YouTube application client ID for OAuth

  YouTubeClientSecret:
    type: string
    env: STREAM_YOUTUBE_CLIENT_SECRET
    default: ""
    description: YouTube application client secret for OAuth

  YouTubeWebhookVerificationToken:
    type: string
    env: STREAM_YOUTUBE_WEBHOOK_TOKEN
    default: ""
    description: Token for YouTube webhook verification

  # Sentiment Processing Configuration
  SentimentPulseIntervalSeconds:
    type: integer
    env: STREAM_SENTIMENT_PULSE_INTERVAL_SECONDS
    default: 15
    description: How often to batch and publish sentiment pulses

  SentimentMinBatchSize:
    type: integer
    env: STREAM_SENTIMENT_MIN_BATCH_SIZE
    default: 5
    description: Minimum sentiments before publishing a pulse

  SentimentMaxBatchSize:
    type: integer
    env: STREAM_SENTIMENT_MAX_BATCH_SIZE
    default: 200
    description: Maximum sentiments per pulse (overflow drops lowest-intensity)

  # Tracked Viewer Configuration
  MaxTrackedViewersPerSession:
    type: integer
    env: STREAM_MAX_TRACKED_VIEWERS_PER_SESSION
    default: 50
    description: Maximum simultaneous tracked viewers per platform session

  TrackedViewerEngagementThreshold:
    type: number
    format: float
    env: STREAM_TRACKED_VIEWER_ENGAGEMENT_THRESHOLD
    default: 0.7
    description: Minimum engagement score (0-1) for a viewer to earn a tracking ID

  TrackSubscribers:
    type: boolean
    env: STREAM_TRACK_SUBSCRIBERS
    default: true
    description: Automatically assign tracking IDs to platform subscribers

  TrackModerators:
    type: boolean
    env: STREAM_TRACK_MODERATORS
    default: true
    description: Automatically assign tracking IDs to platform moderators

  TrackRaidLeaders:
    type: boolean
    env: STREAM_TRACK_RAID_LEADERS
    default: true
    description: Automatically assign tracking IDs to raid leaders

  # Token Encryption
  TokenEncryptionKey:
    type: string
    env: STREAM_TOKEN_ENCRYPTION_KEY
    default: ""
    description: AES-256 key for encrypting OAuth tokens at rest

  # Session Cleanup
  SessionHistoryRetentionHours:
    type: integer
    env: STREAM_SESSION_HISTORY_RETENTION_HOURS
    default: 168
    description: How long to retain ended session records (default 7 days)
```

### Background Services

1. **TokenRefreshWorker**: Periodically refreshes OAuth tokens for linked platforms before they expire. Runs on a configurable interval (default: every 30 minutes). Checks token expiry and refreshes proactively.

2. **WebhookSubscriptionManager**: Ensures webhook subscriptions are active for all linked platforms. Twitch EventSub subscriptions expire and need renewal. YouTube push subscriptions need periodic verification. Runs on startup and then periodically (default: every 6 hours).

3. **SentimentBatchPublisher**: Drains the sentiment buffer at the configured pulse interval and publishes `stream.audience.pulse` events. This is the heartbeat of the service -- it's what produces the batched, privacy-safe events that lib-streaming consumes.

4. **SessionCleanupWorker**: Purges ended session records older than the configured retention period. Also cleans up orphaned platform sessions (where the webhook notification was missed).

---

## lib-streaming: L4 Game Feature (In-Game Streaming Metagame)

### Service Identity

| Property | Value |
|----------|-------|
| **Layer** | L4 (GameFeatures) |
| **Plugin** | `plugins/lib-streaming/` |
| **Schema prefix** | `streaming` |
| **Service name** | `streaming` |
| **Hard dependencies** | L0 (state, messaging), L1 (account, auth, permission), L2 (game-session, game-service, character) |
| **Soft dependencies** | L3 (lib-stream -- graceful degradation when absent), L4 (lib-seed for career progression, lib-collection for unlocks, lib-currency for virtual tips, lib-relationship for follow relationships, lib-analytics for event reporting) |
| **When absent** | No streaming metagame; other services unaffected |

### What This Service Does

1. **Simulated Audience Pool Management**: Maintains a pool of lightweight audience member data objects with personality, interest, and engagement flags
2. **In-Game Stream Sessions**: Creates and manages streaming sessions within the game world (a character "starts streaming" in Omega, or a bard performs for a crowd in Arcadia)
3. **Audience Matching & Assignment**: Matches audience members to stream sessions based on personality, interest, and engagement
4. **Hype Train Mechanics**: Event-driven excitement generation with escalating levels, triggered by in-game achievements and world-first discoveries
5. **Follow/Subscribe Dynamics**: Audience members follow streamers based on sustained interest, creating persistent relationships
6. **Real Audience Blending**: When lib-stream (L3) is available, ingests sentiment pulses and creates "real-derived" audience members that blend seamlessly with simulated ones
7. **Streamer Career Progression**: Manages streamer growth via Seed composability
8. **Audience Check-Out System**: Stream sessions "check out" audience members from the pool for the duration of the session, preventing double-allocation

### What This Service Does NOT Do

- Does not connect to external platforms (that's lib-stream L3)
- Does not handle RTMP/FFmpeg (that's lib-voice per VOICE-STREAMING.md)
- Does not process raw chat messages or platform events
- Does not store any PII -- all audience members are either simulated or derived from anonymous sentiment data

### API Endpoints

#### Audience Pool Management

```yaml
/streaming/audience/pool/seed:
  post:
    operationId: seedAudiencePool
    summary: Seed the audience pool for a game service
    description: >
      Creates a batch of simulated audience members with randomized
      personality/interest/engagement profiles for the specified game
      service. Typically called once during game service setup, then
      the pool grows organically. Audience members are lightweight
      data objects, not actors.
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/SeedAudiencePoolRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/SeedAudiencePoolResponse'
      '400':
        description: Invalid count or game service

/streaming/audience/pool/status:
  post:
    operationId: getAudiencePoolStatus
    summary: Get audience pool statistics for a game service
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/AudiencePoolStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/AudiencePoolStatus'
```

#### Stream Session Lifecycle

```yaml
/streaming/session/create:
  post:
    operationId: createStreamSession
    summary: Create an in-game streaming session
    description: >
      Creates a new streaming session for a game entity (character,
      account, NPC). The session starts in Pending state until
      explicitly started. Can optionally be associated with a
      lib-stream platform session for real audience blending.
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/CreateStreamSessionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamSessionDetail'
      '400':
        description: Invalid streamer or game service
      '409':
        description: Streamer already has an active session

/streaming/session/start:
  post:
    operationId: startStreamSession
    summary: Start an in-game streaming session (begin audience matching)
    description: >
      Transitions the session from Pending to Live. Begins audience
      matching: checks out audience members from the pool based on
      personality/interest matching. If an L3 platform session is
      associated, begins processing sentiment pulses for real
      audience blending.
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/StartStreamSessionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamSessionDetail'
      '400':
        description: Session not in Pending state
      '404':
        description: Session not found

/streaming/session/end:
  post:
    operationId: endStreamSession
    summary: End an in-game streaming session
    description: >
      Transitions the session to Ended. Returns all checked-out
      audience members to the pool. Publishes session summary events.
      Triggers Seed growth events for streamer career progression.
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/EndStreamSessionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamSessionSummary'
      '404':
        description: Session not found

/streaming/session/status:
  post:
    operationId: getStreamSessionStatus
    summary: Get current stream session status with audience breakdown
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/StreamSessionStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamSessionDetail'
      '404':
        description: Session not found

/streaming/session/list:
  post:
    operationId: listStreamSessions
    summary: List active or recent stream sessions for a game service
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListStreamSessionsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListStreamSessionsResponse'
```

#### Audience Interaction

```yaml
/streaming/audience/query:
  post:
    operationId: queryAudience
    summary: Query current audience composition for a stream session
    description: >
      Returns the current audience breakdown: total count, simulated
      vs real-derived ratio (admin only), personality distribution,
      engagement levels, and notable audience members (those with
      tracked identities from L3 sentiment pulses).
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/QueryAudienceRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/AudienceComposition'
      '404':
        description: Session not found

/streaming/audience/react:
  post:
    operationId: triggerAudienceReaction
    summary: Trigger an audience reaction based on an in-game event
    description: >
      Notifies the streaming system that an in-game event occurred
      (achievement, discovery, combat moment, etc.). The service
      calculates audience reactions based on personality matching
      and publishes appropriate audience behavior events. This is
      how game events become audience hype.
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/TriggerReactionRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/AudienceReactionResult'
      '404':
        description: Session not found
```

#### Hype Train

```yaml
/streaming/hype/status:
  post:
    operationId: getHypeStatus
    summary: Get current hype train status for a stream session
    x-permissions:
      - role: user
        states:
          - in_game
    requestBody:
      schema:
        $ref: '#/components/schemas/HypeStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/HypeTrainStatus'
      '404':
        description: Session not found or no active hype train

/streaming/hype/history:
  post:
    operationId: getHypeHistory
    summary: Get hype train history for a streamer
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/HypeHistoryRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/HypeHistoryResponse'
```

#### Streamer Career

```yaml
/streaming/career/status:
  post:
    operationId: getCareerStatus
    summary: Get streamer career status (backed by Seed)
    description: >
      Returns the streamer's career progression, which is implemented
      as a Seed of type "streamer". Includes growth phase, domain
      progress, and capabilities.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/CareerStatusRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamerCareerStatus'
      '404':
        description: No streamer career found (create by starting first stream)

/streaming/career/leaderboard:
  post:
    operationId: getStreamerLeaderboard
    summary: Get streamer leaderboard for a game service
    description: >
      Returns ranked streamers by follower count, total watch time,
      or hype train count. Backed by lib-leaderboard integration.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/StreamerLeaderboardRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/StreamerLeaderboardResponse'

/streaming/follow/list:
  post:
    operationId: listFollowers
    summary: List followers for a streamer
    description: >
      Returns the simulated audience members that follow this streamer.
      Backed by the audience relationship system (not lib-relationship
      directly -- audience members are not full game entities).
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/ListFollowersRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/ListFollowersResponse'
```

#### Discovery & Browse

```yaml
/streaming/browse:
  post:
    operationId: browseStreams
    summary: Browse currently active in-game streams
    description: >
      Returns a list of currently live in-game streams, sorted by
      audience size. Used for the "stream browser" UI in Omega.
    x-permissions:
      - role: user
        states: {}
    requestBody:
      schema:
        $ref: '#/components/schemas/BrowseStreamsRequest'
    responses:
      '200':
        schema:
          $ref: '#/components/schemas/BrowseStreamsResponse'
```

### Request/Response Models

```yaml
# Audience Pool
SeedAudiencePoolRequest:
  type: object
  required: [gameServiceId, count]
  properties:
    gameServiceId:
      type: string
      format: uuid
    count:
      type: integer
      minimum: 1
      maximum: 100000
      description: Number of simulated audience members to create

SeedAudiencePoolResponse:
  type: object
  required: [created, totalPoolSize]
  properties:
    created:
      type: integer
    totalPoolSize:
      type: integer

AudiencePoolStatusRequest:
  type: object
  required: [gameServiceId]
  properties:
    gameServiceId:
      type: string
      format: uuid

AudiencePoolStatus:
  type: object
  required: [gameServiceId, totalMembers, availableMembers, checkedOutMembers, collectiveGroups]
  properties:
    gameServiceId:
      type: string
      format: uuid
    totalMembers:
      type: integer
    availableMembers:
      type: integer
      description: Members not currently checked out to a stream
    checkedOutMembers:
      type: integer
    collectiveGroups:
      type: integer
      description: Number of collective audience groups (single objects representing many viewers)

# Audience Personality and Interest Flags
AudiencePersonality:
  type: string
  enum:
    - HighExcitement
    - LowExcitement
    - GroupOriented
    - SoloViewer
    - Vocal
    - Lurker
    - Loyal
    - Explorer
    - Confrontational
    - Supportive
  description: >
    Behavioral tendency flags. Audience members can have multiple
    personality traits that influence their engagement patterns.

AudienceInterest:
  type: string
  enum:
    - Combat
    - Exploration
    - Social
    - Crafting
    - Technical
    - PvP
    - Discovery
    - Narrative
    - Speedrunning
    - Creative
  description: >
    Content preference flags. Audience members can have multiple
    interests that affect which streams attract them.

# Stream Session
StreamSessionStatus:
  type: string
  enum: [Pending, Live, Ending, Ended]
  description: In-game stream session lifecycle state

StreamerEntityType:
  type: string
  enum: [Character, Account, Actor]
  description: >
    Type of entity doing the streaming. Characters stream in-game,
    Accounts stream from Omega meta-dashboard, Actors are NPC streamers.

CreateStreamSessionRequest:
  type: object
  required: [gameServiceId, streamerId, streamerType]
  properties:
    gameServiceId:
      type: string
      format: uuid
    streamerId:
      type: string
      format: uuid
    streamerType:
      $ref: '#/components/schemas/StreamerEntityType'
    title:
      type: string
      nullable: true
      description: Optional stream title
    contentTags:
      type: array
      items:
        $ref: '#/components/schemas/AudienceInterest'
      description: Content tags for audience matching
    platformSessionId:
      type: string
      format: uuid
      nullable: true
      description: Optional L3 platform session for real audience blending

StartStreamSessionRequest:
  type: object
  required: [sessionId]
  properties:
    sessionId:
      type: string
      format: uuid

EndStreamSessionRequest:
  type: object
  required: [sessionId]
  properties:
    sessionId:
      type: string
      format: uuid
    reason:
      type: string
      enum: [Manual, SessionEnded, Disconnected, Error]
      nullable: true

StreamSessionDetail:
  type: object
  required: [sessionId, gameServiceId, streamerId, streamerType, status, startedAt, audienceCount]
  properties:
    sessionId:
      type: string
      format: uuid
    gameServiceId:
      type: string
      format: uuid
    streamerId:
      type: string
      format: uuid
    streamerType:
      $ref: '#/components/schemas/StreamerEntityType'
    status:
      $ref: '#/components/schemas/StreamSessionStatus'
    title:
      type: string
      nullable: true
    contentTags:
      type: array
      items:
        $ref: '#/components/schemas/AudienceInterest'
    startedAt:
      type: string
      format: date-time
    endedAt:
      type: string
      format: date-time
      nullable: true
    audienceCount:
      type: integer
    peakAudienceCount:
      type: integer
    followerCount:
      type: integer
    activeHypeTrain:
      type: boolean
    platformSessionLinked:
      type: boolean
      description: Whether real audience data is flowing from L3

StreamSessionSummary:
  type: object
  required: [sessionId, durationSeconds, peakAudienceCount, averageAudienceCount, newFollowers, hypeTrainsTriggered]
  properties:
    sessionId:
      type: string
      format: uuid
    durationSeconds:
      type: integer
    peakAudienceCount:
      type: integer
    averageAudienceCount:
      type: integer
    newFollowers:
      type: integer
    hypeTrainsTriggered:
      type: integer
    topMoments:
      type: array
      items:
        $ref: '#/components/schemas/StreamMoment'
      description: Most notable moments during the stream

StreamMoment:
  type: object
  required: [timestamp, momentType, audienceReaction]
  properties:
    timestamp:
      type: string
      format: date-time
    momentType:
      type: string
      enum: [WorldFirst, Achievement, CombatHighlight, Discovery, SocialMoment, HypeTrainPeak]
    description:
      type: string
      nullable: true
    audienceReaction:
      type: number
      format: float
      description: Aggregate audience excitement level (0-1) at the moment

StreamSessionStatusRequest:
  type: object
  required: [sessionId]
  properties:
    sessionId:
      type: string
      format: uuid

ListStreamSessionsRequest:
  type: object
  required: [gameServiceId]
  properties:
    gameServiceId:
      type: string
      format: uuid
    streamerType:
      $ref: '#/components/schemas/StreamerEntityType'
      nullable: true
    status:
      $ref: '#/components/schemas/StreamSessionStatus'
      nullable: true
    limit:
      type: integer
      default: 20
    offset:
      type: integer
      default: 0

ListStreamSessionsResponse:
  type: object
  required: [sessions, total]
  properties:
    sessions:
      type: array
      items:
        $ref: '#/components/schemas/StreamSessionDetail'
    total:
      type: integer

# Audience Composition
QueryAudienceRequest:
  type: object
  required: [sessionId]
  properties:
    sessionId:
      type: string
      format: uuid

AudienceComposition:
  type: object
  required: [sessionId, totalCount, personalityDistribution, interestDistribution, engagementBreakdown]
  properties:
    sessionId:
      type: string
      format: uuid
    totalCount:
      type: integer
    personalityDistribution:
      type: object
      additionalProperties:
        type: integer
      description: >
        Map of AudiencePersonality -> count. Shows how many viewers
        have each personality trait.
    interestDistribution:
      type: object
      additionalProperties:
        type: integer
      description: Map of AudienceInterest -> count.
    engagementBreakdown:
      $ref: '#/components/schemas/EngagementBreakdown'
    notableViewers:
      type: array
      items:
        $ref: '#/components/schemas/NotableViewer'
      description: >
        Audience members with tracked identities (from L3 sentiment
        pulses or from high-engagement simulated members). Limited
        set for game UI display.

EngagementBreakdown:
  type: object
  required: [highEngagement, mediumEngagement, lowEngagement, lurking]
  properties:
    highEngagement:
      type: integer
      description: Actively reacting, chatting equivalent
    mediumEngagement:
      type: integer
      description: Watching attentively
    lowEngagement:
      type: integer
      description: Present but distracted
    lurking:
      type: integer
      description: Present but minimal interaction

NotableViewer:
  type: object
  required: [viewerId, viewerLabel, engagementLevel, dominantSentiment]
  properties:
    viewerId:
      type: string
      format: uuid
      description: >
        Opaque identifier. For real-derived viewers this is the
        L3 tracking ID. For notable simulated viewers this is
        the simulated member ID. Never a platform user ID.
    viewerLabel:
      type: string
      description: >
        Generated label like "Enthusiastic Explorer" or "Loyal Critic".
        Based on personality + interest + sentiment patterns. Not a
        real username.
    viewerType:
      $ref: '#/components/schemas/TrackedViewerType'
      nullable: true
      description: Only present for real-derived viewers from L3
    engagementLevel:
      type: number
      format: float
    dominantSentiment:
      $ref: '#/components/schemas/SentimentCategory'
    sessionsWatched:
      type: integer
      description: How many of this streamer's sessions they've attended

# Audience Reaction
TriggerReactionRequest:
  type: object
  required: [sessionId, eventType]
  properties:
    sessionId:
      type: string
      format: uuid
    eventType:
      type: string
      enum: [Achievement, WorldFirst, CombatKill, CombatDeath, Discovery, SocialEvent, CraftComplete, QuestComplete, LevelUp, RareItem, CustomEvent]
    significance:
      type: number
      format: float
      minimum: 0.0
      maximum: 1.0
      default: 0.5
      description: How significant this event is (affects audience reaction intensity)
    description:
      type: string
      nullable: true
      description: Human-readable description of the event (for stream moment logging)

AudienceReactionResult:
  type: object
  required: [excitementDelta, audienceRetained, audienceGained, audienceLost, hypeContribution]
  properties:
    excitementDelta:
      type: number
      format: float
      description: Change in overall audience excitement (-1 to 1)
    audienceRetained:
      type: integer
      description: Viewers who stayed because of this event
    audienceGained:
      type: integer
      description: New viewers attracted by this event
    audienceLost:
      type: integer
      description: Viewers who left (bored by this content type)
    hypeContribution:
      type: number
      format: float
      description: How much this event contributed to the hype train (0-1)
    triggeredHypeTrain:
      type: boolean
      description: Whether this event triggered a new hype train

# Hype Train
HypeTrainLevel:
  type: string
  enum: [Level1, Level2, Level3, Level4, Level5, Legendary]
  description: >
    Hype train escalation levels. Higher levels require more
    sustained excitement and yield bigger rewards.

HypeStatusRequest:
  type: object
  required: [sessionId]
  properties:
    sessionId:
      type: string
      format: uuid

HypeTrainStatus:
  type: object
  required: [sessionId, isActive]
  properties:
    sessionId:
      type: string
      format: uuid
    isActive:
      type: boolean
    currentLevel:
      $ref: '#/components/schemas/HypeTrainLevel'
      nullable: true
    progress:
      type: number
      format: float
      nullable: true
      description: Progress toward next level (0-1)
    startedAt:
      type: string
      format: date-time
      nullable: true
    expiresAt:
      type: string
      format: date-time
      nullable: true
      description: When the hype train expires if no new contributions
    triggerEvent:
      type: string
      nullable: true
      description: What event triggered this hype train

HypeHistoryRequest:
  type: object
  required: [streamerId]
  properties:
    streamerId:
      type: string
      format: uuid
    limit:
      type: integer
      default: 10

HypeHistoryResponse:
  type: object
  required: [hypeTrains]
  properties:
    hypeTrains:
      type: array
      items:
        $ref: '#/components/schemas/HypeTrainSummary'

HypeTrainSummary:
  type: object
  required: [hypeId, sessionId, peakLevel, durationSeconds, triggerEvent, startedAt]
  properties:
    hypeId:
      type: string
      format: uuid
    sessionId:
      type: string
      format: uuid
    peakLevel:
      $ref: '#/components/schemas/HypeTrainLevel'
    durationSeconds:
      type: integer
    triggerEvent:
      type: string
    startedAt:
      type: string
      format: date-time

# Streamer Career
StreamerCareerPhase:
  type: string
  enum: [Unknown, Rising, Popular, Celebrity, Legend]
  description: >
    Career progression phases, backed by Seed growth phases.
    Phase transitions are determined by accumulated growth across
    audience_growth, engagement_quality, content_diversity, and
    discovery_rate domains.

CareerStatusRequest:
  type: object
  required: [streamerId]
  properties:
    streamerId:
      type: string
      format: uuid

StreamerCareerStatus:
  type: object
  required: [streamerId, phase, totalFollowers, totalStreamSessions, totalStreamHours, totalHypeTrains]
  properties:
    streamerId:
      type: string
      format: uuid
    seedId:
      type: string
      format: uuid
      nullable: true
      description: The underlying Seed entity ID (for direct Seed queries)
    phase:
      $ref: '#/components/schemas/StreamerCareerPhase'
    totalFollowers:
      type: integer
    totalStreamSessions:
      type: integer
    totalStreamHours:
      type: number
      format: float
    totalHypeTrains:
      type: integer
    worldFirstCount:
      type: integer
    domainProgress:
      type: object
      additionalProperties:
        type: number
        format: float
      description: >
        Map of growth domain -> progress (0-1). Domains:
        audience_growth, engagement_quality, content_diversity,
        discovery_rate.

# Streamer Leaderboard
StreamerLeaderboardRequest:
  type: object
  required: [gameServiceId]
  properties:
    gameServiceId:
      type: string
      format: uuid
    sortBy:
      type: string
      enum: [Followers, WatchHours, HypeTrains, WorldFirsts]
      default: Followers
    limit:
      type: integer
      default: 20

StreamerLeaderboardResponse:
  type: object
  required: [entries]
  properties:
    entries:
      type: array
      items:
        $ref: '#/components/schemas/StreamerLeaderboardEntry'

StreamerLeaderboardEntry:
  type: object
  required: [rank, streamerId, streamerType, phase, value]
  properties:
    rank:
      type: integer
    streamerId:
      type: string
      format: uuid
    streamerType:
      $ref: '#/components/schemas/StreamerEntityType'
    streamerName:
      type: string
      nullable: true
    phase:
      $ref: '#/components/schemas/StreamerCareerPhase'
    value:
      type: number
      format: float
      description: The metric being ranked (followers, hours, etc.)

# Follow Management
ListFollowersRequest:
  type: object
  required: [streamerId]
  properties:
    streamerId:
      type: string
      format: uuid
    limit:
      type: integer
      default: 50
    offset:
      type: integer
      default: 0

ListFollowersResponse:
  type: object
  required: [followers, total]
  properties:
    followers:
      type: array
      items:
        $ref: '#/components/schemas/AudienceFollower'
    total:
      type: integer

AudienceFollower:
  type: object
  required: [memberId, followedSince, loyaltyLevel, dominantInterest]
  properties:
    memberId:
      type: string
      format: uuid
    followedSince:
      type: string
      format: date-time
    loyaltyLevel:
      type: number
      format: float
      description: 0-1 loyalty score based on attendance consistency
    dominantInterest:
      $ref: '#/components/schemas/AudienceInterest'
    isRealDerived:
      type: boolean
      description: >
        Whether this follower originated from a real audience sentiment
        pulse (admin-only visibility). Game UI never shows this.

# Browse
BrowseStreamsRequest:
  type: object
  required: [gameServiceId]
  properties:
    gameServiceId:
      type: string
      format: uuid
    contentFilter:
      type: array
      items:
        $ref: '#/components/schemas/AudienceInterest'
      nullable: true
    limit:
      type: integer
      default: 20

BrowseStreamsResponse:
  type: object
  required: [streams]
  properties:
    streams:
      type: array
      items:
        $ref: '#/components/schemas/StreamSessionDetail'
```

### Service Events

```yaml
# Published by lib-streaming
streaming.session.created:
  schema:
    StreamingSessionCreatedEvent:
      type: object
      required: [eventId, timestamp, sessionId, gameServiceId, streamerId, streamerType]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        gameServiceId: { type: string, format: uuid }
        streamerId: { type: string, format: uuid }
        streamerType: { $ref: '#/components/schemas/StreamerEntityType' }

streaming.session.started:
  schema:
    StreamingSessionStartedEvent:
      type: object
      required: [eventId, timestamp, sessionId, initialAudienceCount]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        initialAudienceCount: { type: integer }

streaming.session.ended:
  schema:
    StreamingSessionEndedEvent:
      type: object
      required: [eventId, timestamp, sessionId, durationSeconds, peakAudienceCount, newFollowers]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        durationSeconds: { type: integer }
        peakAudienceCount: { type: integer }
        averageAudienceCount: { type: integer }
        newFollowers: { type: integer }
        hypeTrainsTriggered: { type: integer }

streaming.audience.joined:
  schema:
    StreamingAudienceJoinedEvent:
      type: object
      required: [eventId, timestamp, sessionId, memberId, isCollective, memberCount]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        memberId: { type: string, format: uuid }
        isCollective: { type: boolean, description: True if this is a group object }
        memberCount: { type: integer, description: 1 for individual, N for collective }

streaming.audience.left:
  schema:
    StreamingAudienceLeftEvent:
      type: object
      required: [eventId, timestamp, sessionId, memberId, reason]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        memberId: { type: string, format: uuid }
        reason:
          type: string
          enum: [Bored, Competed, SessionEnd, Unavailable]

streaming.hype.triggered:
  schema:
    StreamingHypeTriggeredEvent:
      type: object
      required: [eventId, timestamp, sessionId, hypeId, triggerEvent]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        hypeId: { type: string, format: uuid }
        triggerEvent: { type: string }

streaming.hype.level-up:
  schema:
    StreamingHypeLevelUpEvent:
      type: object
      required: [eventId, timestamp, sessionId, hypeId, newLevel]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        hypeId: { type: string, format: uuid }
        newLevel: { $ref: '#/components/schemas/HypeTrainLevel' }

streaming.hype.completed:
  schema:
    StreamingHypeCompletedEvent:
      type: object
      required: [eventId, timestamp, sessionId, hypeId, peakLevel, durationSeconds]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        sessionId: { type: string, format: uuid }
        hypeId: { type: string, format: uuid }
        peakLevel: { $ref: '#/components/schemas/HypeTrainLevel' }
        durationSeconds: { type: integer }

streaming.follow.gained:
  schema:
    StreamingFollowGainedEvent:
      type: object
      required: [eventId, timestamp, streamerId, memberId]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        streamerId: { type: string, format: uuid }
        memberId: { type: string, format: uuid }

streaming.follow.lost:
  schema:
    StreamingFollowLostEvent:
      type: object
      required: [eventId, timestamp, streamerId, memberId]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        streamerId: { type: string, format: uuid }
        memberId: { type: string, format: uuid }

streaming.milestone.reached:
  description: >
    Published when a streamer reaches a career milestone. Consumed by
    lib-collection for unlock tracking and lib-analytics for reporting.
  schema:
    StreamingMilestoneReachedEvent:
      type: object
      required: [eventId, timestamp, streamerId, milestoneType, milestoneValue]
      properties:
        eventId: { type: string, format: uuid }
        timestamp: { type: string, format: date-time }
        streamerId: { type: string, format: uuid }
        milestoneType:
          type: string
          enum: [FollowerCount, StreamHours, HypeTrainCount, WorldFirstCount, PhaseAdvance]
        milestoneValue: { type: integer }
```

### Event Subscriptions (lib-streaming consumes)

```yaml
x-event-subscriptions:
  # From lib-stream (L3, optional)
  - topic: stream.audience.pulse
    event: SentimentPulse
    description: >
      Real audience sentiment data for blending with simulated audience.
      Graceful degradation: if this event never arrives (L3 absent),
      streaming operates on 100% simulated audiences.

  # From game services (L2)
  - topic: game-session.ended
    event: GameSessionEndedEvent
    description: >
      When a game session ends, end any associated streaming sessions.

  # Self-generated events for background processing
  - topic: streaming.session.ended
    event: StreamingSessionEndedEvent
    description: Trigger career progression and cleanup on session end.
```

### Client Events

```yaml
# Published to the streamer's WebSocket session via IClientEventPublisher

streaming.audience_update:
  description: Periodic audience composition snapshot for the streamer's UI
  interval: Every 10 seconds during active session
  schema:
    StreamingAudienceUpdateClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, session_id, audience_count, engagement_breakdown]
      properties:
        event_name:
          type: string
          enum: ["streaming.audience_update"]
        session_id:
          type: string
          format: uuid
        audience_count:
          type: integer
        peak_audience_count:
          type: integer
        engagement_breakdown:
          $ref: '#/components/schemas/EngagementBreakdown'
        notable_viewer_count:
          type: integer

streaming.hype_started:
  description: A hype train has started
  schema:
    StreamingHypeStartedClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, session_id, hype_id, trigger_event]
      properties:
        event_name:
          type: string
          enum: ["streaming.hype_started"]
        session_id:
          type: string
          format: uuid
        hype_id:
          type: string
          format: uuid
        trigger_event:
          type: string
        expires_at:
          type: string
          format: date-time

streaming.hype_progress:
  description: Hype train level up or progress update
  schema:
    StreamingHypeProgressClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, session_id, hype_id, level, progress]
      properties:
        event_name:
          type: string
          enum: ["streaming.hype_progress"]
        session_id:
          type: string
          format: uuid
        hype_id:
          type: string
          format: uuid
        level:
          $ref: '#/components/schemas/HypeTrainLevel'
        progress:
          type: number
          format: float

streaming.hype_completed:
  description: Hype train ended
  schema:
    StreamingHypeCompletedClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, session_id, hype_id, peak_level]
      properties:
        event_name:
          type: string
          enum: ["streaming.hype_completed"]
        session_id:
          type: string
          format: uuid
        hype_id:
          type: string
          format: uuid
        peak_level:
          $ref: '#/components/schemas/HypeTrainLevel'

streaming.audience_reaction:
  description: Notable audience reaction for game integration (UI effects, sound)
  schema:
    StreamingAudienceReactionClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, session_id, reaction_type, intensity]
      properties:
        event_name:
          type: string
          enum: ["streaming.audience_reaction"]
        session_id:
          type: string
          format: uuid
        reaction_type:
          $ref: '#/components/schemas/SentimentCategory'
        intensity:
          type: number
          format: float

streaming.milestone_notification:
  description: Career milestone reached
  schema:
    StreamingMilestoneClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required: [event_name, event_id, timestamp, milestone_type, milestone_value]
      properties:
        event_name:
          type: string
          enum: ["streaming.milestone"]
        milestone_type:
          type: string
          enum: [FollowerCount, StreamHours, HypeTrainCount, WorldFirstCount, PhaseAdvance]
        milestone_value:
          type: integer
```

### State Stores

```yaml
# In state-stores.yaml additions
streaming-audience-pool:
  backend: redis
  description: >
    Simulated audience member data objects. Redis for fast matching
    and check-out operations during live sessions. Audience members
    are lightweight (personality/interest flags + engagement float).

streaming-audience-relationships:
  backend: mysql
  description: >
    Persistent follow/subscribe relationships between audience members
    and streamers. Durable for long-term audience growth tracking.

streaming-sessions:
  backend: mysql
  description: >
    In-game stream session records with summaries. Queryable for
    leaderboard data, career statistics, and historical browsing.

streaming-hype:
  backend: redis
  description: >
    Active hype train state. Ephemeral -- cleaned up when hype train
    completes or expires. Needs fast read/write for real-time progress.

streaming-streamer-stats:
  backend: mysql
  description: >
    Streamer career statistics (aggregate data). Durable for
    leaderboard backing and career progression queries.
```

### Configuration

```yaml
# streaming-configuration.yaml
x-service-configuration:
  # Feature Flags
  StreamingEnabled:
    type: boolean
    env: STREAMING_ENABLED
    default: false
    description: Master feature flag for the streaming metagame

  RealAudienceBlendingEnabled:
    type: boolean
    env: STREAMING_REAL_AUDIENCE_BLENDING_ENABLED
    default: true
    description: >
      Whether to process sentiment pulses from lib-stream (L3) when
      available. Can be disabled to run purely on simulated audiences
      even when L3 is present.

  # Audience Pool
  DefaultPoolSize:
    type: integer
    env: STREAMING_DEFAULT_POOL_SIZE
    default: 10000
    description: Default audience pool size when seeding for a game service

  MaxCheckedOutPerSession:
    type: integer
    env: STREAMING_MAX_CHECKED_OUT_PER_SESSION
    default: 5000
    description: Maximum audience members a single stream can check out

  # Audience Matching
  AudienceCheckInIntervalSeconds:
    type: integer
    env: STREAMING_AUDIENCE_CHECK_IN_INTERVAL_SECONDS
    default: 60
    description: How often to recalculate audience interest and migration

  HighLoadCheckInIntervalSeconds:
    type: integer
    env: STREAMING_HIGH_LOAD_CHECK_IN_INTERVAL_SECONDS
    default: 300
    description: Extended check-in interval during high server load

  InterestDecayRate:
    type: number
    format: float
    env: STREAMING_INTEREST_DECAY_RATE
    default: 0.05
    description: Per-interval interest decay for audience members (0-1)

  InterestThresholdForFollow:
    type: number
    format: float
    env: STREAMING_INTEREST_THRESHOLD_FOR_FOLLOW
    default: 0.8
    description: Sustained interest level required to trigger a follow

  FollowConversionSessionCount:
    type: integer
    env: STREAMING_FOLLOW_CONVERSION_SESSION_COUNT
    default: 3
    description: >
      Number of consecutive sessions an audience member must maintain
      high interest before converting to a follower

  # Hype Train
  HypeTrainDurationSeconds:
    type: integer
    env: STREAMING_HYPE_TRAIN_DURATION_SECONDS
    default: 300
    description: How long a hype train lasts without new contributions

  HypeTrainLevels:
    type: integer
    env: STREAMING_HYPE_TRAIN_LEVELS
    default: 5
    description: Number of hype train levels before Legendary

  WorldFirstHypeMultiplier:
    type: number
    format: float
    env: STREAMING_WORLD_FIRST_HYPE_MULTIPLIER
    default: 10.0
    description: Multiplier for world-first discovery events on audience interest

  # Real Audience Blending
  RealDerivedViewerDecayRate:
    type: number
    format: float
    env: STREAMING_REAL_DERIVED_VIEWER_DECAY_RATE
    default: 0.1
    description: >
      How quickly real-derived audience members fade when sentiment
      pulses stop (e.g., when L3 session ends). Higher = faster fade.

  MaxRealDerivedViewers:
    type: integer
    env: STREAMING_MAX_REAL_DERIVED_VIEWERS
    default: 500
    description: Maximum real-derived audience members per session

  # Client Events
  AudienceUpdateIntervalSeconds:
    type: integer
    env: STREAMING_AUDIENCE_UPDATE_INTERVAL_SECONDS
    default: 10
    description: How often to push audience composition updates to the streamer client

  # Session Cleanup
  SessionRetentionDays:
    type: integer
    env: STREAMING_SESSION_RETENTION_DAYS
    default: 90
    description: How long to retain ended session records
```

### Background Services

1. **AudienceMatchingWorker**: The heartbeat of the streaming system. Runs at `AudienceCheckInIntervalSeconds` (default 60s). For each active stream session:
   - Recalculates interest scores for checked-out audience members
   - Applies interest decay to audience members who haven't been excited recently
   - Resolves multi-stream competition (if audience members are watching multiple streams)
   - Migrates audience members between streams based on interest changes
   - Checks out new audience members attracted by recent events
   - Processes real audience sentiment pulses from the event buffer
   - Scales interval to `HighLoadCheckInIntervalSeconds` under high server load

2. **HypeTrainProcessor**: Monitors active hype trains. Runs every second for active trains:
   - Checks if hype train has expired (no contributions within duration window)
   - Calculates level-up progress from accumulated contributions
   - Publishes level-up and completion events
   - Triggers Collection unlocks for hype train milestones

3. **InterestDecayWorker**: Runs periodically (every `AudienceCheckInIntervalSeconds`) to decay interest levels for audience members not actively engaged. Prevents audience members from being permanently locked to streams.

4. **CareerProgressionWorker**: Runs on `streaming.session.ended` events. Calculates Seed growth contributions for the streamer based on session performance (audience size, engagement, hype trains, follows gained). Publishes Seed growth events.

5. **RealAudienceBlender**: Runs on `stream.audience.pulse` event arrival. Converts sentiment entries into real-derived audience members:
   - Anonymous sentiments → influence overall audience mood
   - Tracked sentiments → create or update specific real-derived audience members
   - Tracked members persist across pulses within a session (consistent via tracking ID)
   - Tracked members fade when pulses stop arriving (decay rate configurable)

---

## Composability Map

### Seed: Streamer Career

```yaml
Seed Type: "streamer"
Growth Domains:
  audience_growth:     # Total followers gained
  engagement_quality:  # Average audience engagement across sessions
  content_diversity:   # Variety of content tags across sessions
  discovery_rate:      # World-first discoveries per stream hour
Phase Labels:
  - Unknown     # No streams yet
  - Rising      # Starting to attract regular audience
  - Popular     # Consistent audience, multiple followers
  - Celebrity   # Large following, hype trains common
  - Legend       # Server-wide recognition, record-breaking streams
Capabilities Per Phase:
  Rising:     [custom_stream_title, basic_audience_stats]
  Popular:    [stream_schedule, audience_composition_view, hype_train_tracking]
  Celebrity:  [cross_stream_raids, audience_loyalty_insights, stream_highlights]
  Legend:     [server_announcements, legendary_hype_trains, streamer_mentoring]
```

### Collection: Streaming Unlocks

lib-collection handles all reward/unlock tracking for streaming milestones:

| Collection Category | Example Entries | Trigger |
|-------------------|-----------------|---------|
| Streaming Milestones | "First Stream", "100 Followers", "1000 Watch Hours" | `streaming.milestone.reached` events |
| Hype Achievements | "First Hype Train", "Level 5 Hype", "Legendary Hype" | `streaming.hype.completed` events |
| World-First Streams | "Discovered [X] On Stream", "First [Achievement] On Stream" | `streaming.milestone.reached` with WorldFirstCount |
| Audience Badges | "Loyal Audience", "Diverse Viewers", "Critics' Choice" | Career progression checkpoints |

### Currency: Virtual Tips

```yaml
Currency Type: "stream_tip"
Scope: Per game service
Properties:
  - Non-transferable (audience members "tip" the streamer)
  - Generated by audience engagement events (not real money)
  - Spent on stream customization (overlays, effects, emotes)
  - Autogain: passive tip generation from follower count
```

### Contract: Sponsorship Deals

```yaml
Contract Template: "stream_sponsorship"
Parties:
  - Role: sponsor (NPC merchant, guild, game entity)
  - Role: streamer
Milestones:
  - "stream_hours": Stream X hours with sponsor's product visible
  - "audience_reach": Reach X total viewers during sponsored streams
  - "hype_generation": Trigger X hype trains during sponsored streams
Prebound API on completion:
  - Credit streamer with sponsorship payment (Currency)
  - Grant sponsor reputation boost (Seed growth)
```

---

## Integration with VOICE-STREAMING.md

The existing VOICE-STREAMING.md plan for RTMP output from voice rooms is **complementary, not overlapping**:

| Concern | Where It Lives | Why |
|---------|----------------|-----|
| FFmpeg process management | lib-voice (StreamingCoordinator) | Tightly coupled to voice room audio mixing |
| RTMP output to Twitch/YouTube | lib-voice (StreamingCoordinator) | Output is voice room specific |
| Platform account OAuth linking | lib-stream (L3) | Account management, not room-specific |
| Real audience ingestion | lib-stream (L3) | Webhook/API processing, not voice-specific |
| Sentiment processing | lib-stream (L3) | Privacy boundary enforcement |
| Simulated audience management | lib-streaming (L4) | Game feature, not infrastructure |
| Camera discovery events | common-events (unchanged) | Cross-service eventing |

**How they connect**: A player in Omega starts a voice room (lib-voice), links it to their Twitch account (lib-stream L3), and starts an in-game stream (lib-streaming L4). Voice handles the audio/video output, stream handles the platform integration, and streaming handles the audience metagame. Three services, cleanly separated.

---

## The Real vs. Simulated Audience Metagame

The blending creates a natural Turing test:

**Simulated audience members** are generated algorithmically with personality/interest flags. Their behavior is predictable within their personality parameters -- a "Loyal, Combat-focused" simulated viewer always reacts positively to combat moments and always comes back.

**Real-derived audience members** are created from L3 sentiment pulses. Their behavior is unpredictable because it reflects actual human reactions. A real viewer might:
- Get excited about something niche that no simulated personality profile would predict
- Leave at an unexpected moment (bathroom break, real-life interruption)
- Return after a long absence with no algorithmic explanation
- React to meta-game context (knowing the streamer's reputation from out-of-game sources)

**The metagame**: Players may start noticing that some audience members behave "differently" -- more erratically, more surprisingly, more humanly. The game never labels which are real. But keen players might develop theories, and that speculation IS the metagame. "I think my most loyal viewer is actually a real person" becomes a conversation topic in the Omega social scene.

**Design rule**: The game UI NEVER reveals the real/simulated distinction to players. The `isRealDerived` flag on `AudienceFollower` is admin-only visibility for debugging and analytics. In the player-facing experience, all audience members are presented identically.

---

## Open Design Questions

These need human judgment before schema finalization:

1. **Sentiment processing approach**: Simple keyword/emoji matching, or integrate a lightweight NLP model? Keyword matching is deterministic and fast but coarse. An NLP model provides better sentiment quality but adds a dependency and latency.

2. **Cross-session tracking ID persistence**: Currently, tracking IDs are destroyed when a platform session ends. Should there be an option for "returner" detection across sessions? This could be done by hashing the platform user ID with a session-independent salt -- the hash can't be reversed to a user ID but can detect "same viewer returned." Privacy implications need assessment.

3. **NPC streamers**: The `Actor` entity type in `StreamerEntityType` allows NPCs to be streamers. Should NPC streams have simulated audiences by default? Should players be able to "watch" NPC streams in-game? This creates content (NPC streamers generating moments) but needs careful UX design.

4. **Collective audience groups**: The KB describes "collective" audience members (one object representing 20 people). Implementation complexity vs. performance benefit tradeoff needs quantification. Could start with individual members only and add collectives when pool size demands it.

5. **Interest matching algorithm weights**: The KB proposes specific weights (general engagement 0.2, follower bonus 0.3, content match 0.25, reputation 0.15, context 0.1). Should these be configurable or hardcoded? Making them configurable adds config complexity but enables tuning.

6. **Streamer raids**: Should in-game streamers be able to "raid" other streams (send their audience to another streamer)? This is a core Twitch mechanic that translates naturally to the metagame but adds API complexity.

7. **Voice room integration depth**: Beyond VOICE-STREAMING.md's RTMP output, should lib-streaming be aware of voice room state? For example, "streamer is in a voice room with 5 participants" could affect audience behavior. This creates a soft L4→L4 dependency (lib-streaming → lib-voice).

8. **Realm-specific manifestation**: In Omega, streaming is explicit (cyberpunk streaming studio). In Arcadia, the same mechanics could manifest as "performing for a crowd" (bard, gladiator arena, market stall). How much realm-specific logic should lib-streaming contain vs. delegating to client rendering?

---

## Implementation Priority

### Phase 1: lib-streaming (L4) Standalone

Build the in-game streaming metagame with 100% simulated audiences. This delivers the full game feature without requiring any platform integration.

- Audience pool management
- Stream session lifecycle
- Audience matching engine
- Hype train mechanics
- Basic client events
- Seed integration for streamer career

### Phase 2: lib-stream (L3) Core

Build the platform integration layer.

- Platform account linking (Twitch first, YouTube second)
- Webhook ingestion for Twitch EventSub
- Sentiment processing pipeline
- Sentiment pulse publishing
- Session management

### Phase 3: Real Audience Blending

Connect L3 to L4.

- Sentiment pulse consumer in lib-streaming
- Real-derived audience member creation
- Tracking ID consistency across pulses
- Blending with simulated audience behavior
- Admin visibility for debugging

### Phase 4: Polish & Enrichment

- Collection integration for streaming milestones
- Currency integration for virtual tips
- Contract integration for sponsorship deals
- Leaderboard integration for streamer rankings
- NPC streamer support (if design question resolved)

---

*This document is self-contained for schema generation. All model shapes, event schemas, configuration properties, state stores, and API endpoint signatures are specified at sufficient detail to produce YAML schemas without referencing external documentation.*
