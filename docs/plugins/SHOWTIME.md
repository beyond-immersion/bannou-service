# Showtime Plugin Deep Dive

> **Plugin**: lib-showtime (not yet created)
> **Schema**: `schemas/showtime-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: showtime-sessions (MySQL), showtime-audience-pool (Redis), showtime-audience-followers (MySQL), showtime-hype-trains (Redis), showtime-session-voice (Redis), showtime-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md)

## Overview

In-game streaming metagame service (L4 GameFeatures) for simulated audience pools, hype train mechanics, streamer career progression, and real-simulated audience blending. The game-facing layer of the streaming stack -- everything that makes streaming a game mechanic rather than just a platform integration. Game-agnostic: audience personality types, hype train escalation, and streaming milestones are all configured through seed types, collection types, and configuration. Internal-only, never internet-facing.

---

## The Simulated Audience System

lib-showtime works without lib-broadcast (L3) entirely. When no real platform data is available, the service operates on 100% simulated audiences. When lib-broadcast is available, real audience sentiment pulses are blended seamlessly into the simulated pool. This makes the metagame testable, deployable, and playable without any external platform dependency.

Simulated audience members are lightweight data objects that model viewer behavior through personality flags, interest profiles, and engagement state. They are NOT actors -- they don't run ABML behaviors or consume actor pool resources. They are state objects with deterministic behavior rules evaluated during session ticks.

### Audience Member Anatomy

```
AudienceMember:
  memberId: Guid
  personalityType: string             # Opaque string (e.g., "loyal", "fickle", "lurker")
  interestTags: string[]              # Content interest tags (e.g., "combat", "crafting", "social")
  engagementLevel: float              # 0.0 to 1.0 current engagement
  engagementDecayRate: float          # Per-tick decay when content doesn't match interests
  sentimentBias: SentimentCategory    # Default sentiment when engaged
  isRealDerived: bool                 # True if created from a real sentiment pulse (admin-only visibility)
  trackingId: Guid?                   # For real-derived: matches the lib-broadcast tracking ID
  lastActiveTimestamp: DateTime
```

### Audience Lifecycle

1. **Pool creation**: When a streaming session starts, an initial audience pool is allocated from a reservoir of pre-generated audience member templates. Pool size depends on the streamer's career seed phase.
2. **Interest matching**: Each session tick, audience members evaluate whether the current stream content matches their interest tags. Matching increases engagement; mismatching triggers decay.
3. **Migration**: Disengaged audience members may "leave" (return to the reservoir) and be replaced by new members attracted by current content tags. This creates natural audience churn.
4. **Real blending**: When lib-broadcast sentiment pulses arrive, they are translated into "real-derived" audience members that blend into the pool indistinguishably from simulated members.
5. **Session end**: Audience members with sustained engagement may become followers (persistent cross-session). Others return to the reservoir.

### Interest Matching Algorithm

Content tags for a streaming session are derived from game events during the stream:

| Game Event | Content Tags Generated |
|-----------|----------------------|
| Combat encounter | `combat`, `action`, `danger` |
| Item crafting | `crafting`, `creation`, `skill` |
| Social interaction | `social`, `drama`, `roleplay` |
| Exploration/discovery | `exploration`, `discovery`, `adventure` |
| Trade/economy | `economy`, `trade`, `strategy` |
| World-first event | `worldfirst`, `historic`, `unique` |
| Boss defeat | `combat`, `achievement`, `epic` |

Audience members with matching interest tags increase engagement. The matching algorithm uses configurable weights (not hardcoded) to allow per-game-service tuning.

---

## The Hype Train System

Hype trains are event-driven excitement cascades that build when audience engagement concentrates around a single emotional response. They are the streaming metagame's equivalent of a critical moment -- everything gets more intense.

### Hype Train Mechanics

```
HypeTrain:
  hypeTrainId: Guid
  showtimeSessionId: Guid
  level: int                    # Current hype level (1-5+)
  currentScore: float           # Accumulated hype within current level
  levelThreshold: float         # Score needed to advance to next level
  decayRate: float              # Per-tick decay (hype dies if not fed)
  triggerCategory: SentimentCategory  # What started the train
  startedAt: DateTime
  expiresAt: DateTime           # Hard timeout (hype trains can't last forever)
```

### Hype Train Lifecycle

1. **Trigger**: When a burst of matching sentiments (>N entries of the same category within a pulse interval) occurs, a hype train starts at level 1.
2. **Escalation**: Continued matching sentiments increase the score. When score exceeds `levelThreshold`, the train advances to the next level. Thresholds increase exponentially.
3. **Decay**: Each tick, the score decays by `decayRate`. If the score drops below the previous level's threshold, the train de-escalates. If it drops to zero, the train ends.
4. **Peak**: At high levels (4+), hype trains generate special events that feed into Analytics, Achievement, and the content flywheel.
5. **Completion**: Trains end via decay, timeout, or session end. Completion publishes `showtime.hype.completed` with peak level and duration.

### Hype Train Events → Content Flywheel

High-level hype trains generate events that feed the content flywheel:

| Hype Level | Flywheel Contribution |
|-----------|----------------------|
| Level 1-2 | None (common, unremarkable) |
| Level 3 | `showtime.milestone.reached` (notable audience moment) |
| Level 4 | Feeds into Character History as "memorable performance" |
| Level 5+ | Feeds into Realm History as "legendary performance event" |

---

## The Real vs. Simulated Audience Blending

Simulated audience members behave predictably within their personality parameters. Real-derived audience members inherit the genuine unpredictability of human behavior -- unexpected excitement, inexplicable departures, returning after long absences. The game NEVER reveals which audience members are real. Keen players may develop theories, and that speculation IS the metagame.

When lib-broadcast (L3) publishes `stream.audience.pulse` events, lib-showtime translates them into audience member state changes:

### Translation Rules

| Sentiment Pulse Property | Audience Effect |
|-------------------------|----------------|
| Anonymous sentiment entry | Creates a temporary "real-derived" audience member for the duration of the pulse interval, then removed |
| Tracked viewer entry (with trackingId) | Creates or updates a persistent real-derived audience member for the session duration |
| High-intensity sentiments (>0.8) | Contributes to hype train score |
| Subscriber/Moderator/VIP entries | Weighted higher in hype calculations |
| Bored sentiments | Increases audience churn rate |
| Hostile sentiments | May trigger protective audience reactions (other members rally) |

### Blending Principles

1. **Invisible seam**: The game UI NEVER reveals which audience members are real-derived vs. simulated. The `isRealDerived` flag is admin-only for debugging and analytics.
2. **Real members are unpredictable**: Simulated members follow deterministic personality rules. Real-derived members inherit actual human sentiment data, which is inherently noisy and surprising. This unpredictability IS the metagame.
3. **Proportional blending**: Real-derived members don't replace simulated ones -- they join the pool. A 100-member simulated audience plus 50 real-derived members creates a 150-member blended audience.
4. **Graceful degradation**: If lib-broadcast stops publishing pulses (platform disconnects, service restarts), the simulated audience continues normally. Real-derived members age out via the standard TTL mechanism.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Session records (MySQL), audience pool (Redis), follower records (MySQL), hype trains (Redis), session-voice mapping (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for session mutations, hype train updates, audience pool modifications |
| lib-messaging (`IMessageBus`) | Publishing streaming lifecycle events, hype events, milestone events, audience events |
| lib-messaging (`IEventConsumer`) | Registering handlers for game-session events, platform session events, sentiment pulses, voice room events |
| lib-game-session (`IGameSessionClient`) | Validate game session existence for stream session creation (L2) |
| lib-game-service (`IGameServiceClient`) | Validate game service existence for scoping (L2) |
| lib-character (`ICharacterClient`) | Character existence validation for streamer identity (L2) |
| lib-seed (`ISeedClient`) | Streamer career progression via `streamer` seed type (L2) |
| lib-collection (`ICollectionClient`) | Streaming milestone unlocks -- first stream, follower milestones, hype achievements (L2) |
| lib-currency (`ICurrencyClient`) | Virtual tip economy via `stream_tip` currency type (L2) |
| lib-contract (`IContractClient`) | Sponsorship deals -- NPC sponsors, milestone-based contracts (L1) |
| lib-relationship (`IRelationshipClient`) | Persistent follower bonds -- streamer-to-audience-member entity pairs (L2) |

> **Note**: lib-seed, lib-collection, lib-currency, lib-contract, and lib-relationship are constructor-injected per SERVICE-HIERARCHY.md (L4 services must hard-depend on L1/L2). Not all streaming operations use all of these -- core session lifecycle works without calling them -- but they are guaranteed available and injected at startup.

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-broadcast (`IBroadcastClient`) | Associate platform sessions with in-game streaming sessions | Real audience blending unavailable; 100% simulated audiences |
| lib-voice (`IVoiceClient`) | Create/delete voice rooms for game sessions that want voice | No voice rooms for streaming sessions; streamer has no voice chat |
| lib-analytics (`IAnalyticsClient`) | Report streaming statistics for aggregation | No analytics integration; streaming metrics are local only |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Showtime is a new L4 service with no current consumers. Future dependents: Gardener (streaming as a garden activity type), Achievement (subscribe to `showtime.milestone.reached` for streaming-related trophies), Leaderboard (subscribe to `showtime.session.ended` for streamer rankings) |

---

## State Storage

### Session Store
**Store**: `showtime-sessions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `sess:{showtimeSessionId}` | `ShowtimeSessionModel` | Primary lookup by ID. Stores streamer entity (type+ID), game service scope, status (active/paused/ended), start time, audience metrics, content tags, linked platform session ID (nullable), linked voice room ID (nullable). |
| `sess-streamer:{entityType}:{entityId}` | `ShowtimeSessionModel` | Active session lookup by streamer entity |
| `sess-game:{gameServiceId}` | `ShowtimeSessionModel` | Active sessions by game service (for audience routing) |

### Audience Pool Store
**Store**: `showtime-audience-pool` (Backend: Redis, prefix: `showtime:aud`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `aud:{showtimeSessionId}:{memberId}` | `AudienceMemberModel` | Individual audience member state within a session |
| `aud-counts:{showtimeSessionId}` | `AudienceCountsModel` | Denormalized counts: total, engaged (>0.5), disengaged (<0.2), real-derived, by-personality-type |
| `aud-tags:{showtimeSessionId}` | `ContentTagsModel` | Current session content tags (updated by game event ingestion) |

### Follower Store
**Store**: `showtime-audience-followers` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `follow:{followerId}` | `AudienceFollowerModel` | Persistent follower record: streamer entity, follower member ID, personality type, interest tags, first-followed timestamp, total watch time, engagement history summary, isRealDerived (admin-only). |
| `follow-streamer:{entityType}:{entityId}` | `AudienceFollowerModel` | Followers for a streamer entity (paged query) |

### Hype Train Store
**Store**: `showtime-hype-trains` (Backend: Redis, prefix: `showtime:hype`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `hype:{showtimeSessionId}` | `HypeTrainModel` | Active hype train for a session (at most one per session). Stores level, score, thresholds, decay rate, trigger category, timestamps. |

### Session-Voice Mapping Store
**Store**: `showtime-session-voice` (Backend: Redis, prefix: `showtime:voice`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `voice:{showtimeSessionId}` | `SessionVoiceMappingModel` | Maps streaming session to voice room ID (for voice room lifecycle orchestration) |
| `voice-game:{gameSessionId}` | `SessionVoiceMappingModel` | Maps game session to voice room ID (for event-driven room creation/deletion) |

### Distributed Locks
**Store**: `showtime-lock` (Backend: Redis, prefix: `showtime:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `showtime:lock:session:{showtimeSessionId}` | Session mutation lock (create, update, end) |
| `showtime:lock:hype:{showtimeSessionId}` | Hype train mutation lock |
| `showtime:lock:audience-tick` | Audience simulation tick singleton lock |
| `showtime:lock:career-worker` | Career progression worker singleton lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `showtime.session.started` | `ShowtimeSessionStartedEvent` | In-game streaming session created |
| `showtime.session.ended` | `ShowtimeSessionEndedEvent` | Streaming session ended (with audience metrics, peak viewers, duration, hype summary) |
| `showtime.session.paused` | `ShowtimeSessionPausedEvent` | Streaming session paused (streamer AFK, scene transition) |
| `showtime.session.resumed` | `ShowtimeSessionResumedEvent` | Streaming session resumed |
| `showtime.hype.started` | `ShowtimeHypeStartedEvent` | Hype train started (level 1) |
| `showtime.hype.leveled` | `ShowtimeHypeLeveledEvent` | Hype train advanced to new level |
| `showtime.hype.completed` | `ShowtimeHypeCompletedEvent` | Hype train ended (with peak level, duration, trigger category) |
| `showtime.milestone.reached` | `ShowtimeMilestoneReachedEvent` | Streamer career milestone (first stream, follower count, watch hours, world-first discovery) |
| `showtime.audience.changed` | `ShowtimeAudienceChangedEvent` | Significant audience composition change (burst join, mass exodus, sentiment shift) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `stream.audience.pulse` | `HandleSentimentPulseAsync` | Translate real audience sentiment into real-derived audience members, feed hype train calculations. (Soft -- no-op if lib-broadcast absent) |
| `stream.platform.session.started` | `HandlePlatformSessionStartedAsync` | If associated with an in-game session, start real audience blending. (Soft -- no-op if lib-broadcast absent) |
| `voice.room.created` | `HandleVoiceRoomCreatedAsync` | Track voice room creation for audience context ("streamer is in a voice room with N participants"). (Soft -- no-op if lib-voice absent) |
| `voice.participant.joined` | `HandleVoiceParticipantJoinedAsync` | Adjust audience behavior based on voice room size (more participants = more dynamic content). (Soft -- no-op if lib-voice absent) |
| `voice.participant.left` | `HandleVoiceParticipantLeftAsync` | Adjust audience behavior based on voice room size. (Soft -- no-op if lib-voice absent) |
| `game-session.created` | `HandleGameSessionCreatedAsync` | Create voice room via lib-voice if game session is configured for voice; optionally create streaming session if auto-stream enabled |
| `game-session.ended` | `HandleGameSessionEndedAsync` | End associated streaming sessions and delete voice rooms via lib-voice |
| `showtime.session.ended` | `HandleShowtimeSessionEndedAsync` | Trigger career progression: record seed growth, evaluate milestones, grant collection entries, update follower relationships |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | showtime | CASCADE | `/showtime/cleanup-by-character` |
| game-service | showtime | CASCADE | `/showtime/cleanup-by-game-service` |

### DI Listener Patterns

| Pattern | Interface | Action |
|---------|-----------|--------|
| Seed evolution | `ISeedEvolutionListener` | Receives streamer seed growth and phase change notifications. Updates cached career phase. Evaluates milestone thresholds for career-related milestones. |
| Collection unlock | `ICollectionUnlockListener` | Receives streaming milestone collection unlock confirmations. May trigger hype train bonus or audience morale boost on significant unlocks. |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ShowtimeEnabled` | `SHOWTIME_ENABLED` | `false` | Master feature flag |
| `StreamerSeedTypeCode` | `SHOWTIME_STREAMER_SEED_TYPE_CODE` | `streamer` | Seed type code for streamer career growth |
| `TipCurrencyCode` | `SHOWTIME_TIP_CURRENCY_CODE` | `stream_tip` | Currency code for virtual tip economy |
| `MilestoneCollectionType` | `SHOWTIME_MILESTONE_COLLECTION_TYPE` | `streaming_milestones` | Collection type code for streaming milestone unlocks |
| `HypeCollectionType` | `SHOWTIME_HYPE_COLLECTION_TYPE` | `streaming_hype` | Collection type code for hype achievements |
| `WorldFirstCollectionType` | `SHOWTIME_WORLD_FIRST_COLLECTION_TYPE` | `streaming_world_first` | Collection type code for world-first stream unlocks |
| `SponsorshipContractTemplateCode` | `SHOWTIME_SPONSORSHIP_CONTRACT_TEMPLATE_CODE` | `stream_sponsorship` | Contract template code for NPC sponsorship deals |
| `FollowerRelationshipTypeCode` | `SHOWTIME_FOLLOWER_RELATIONSHIP_TYPE_CODE` | `stream_follower` | Relationship type code for streamer-follower bonds |
| `DefaultAudiencePoolSize` | `SHOWTIME_DEFAULT_AUDIENCE_POOL_SIZE` | `50` | Base audience pool size for new/Unknown phase streamers |
| `AudiencePoolScalePerPhase` | `SHOWTIME_AUDIENCE_POOL_SCALE_PER_PHASE` | `2.0` | Multiplier applied per career phase (Rising=2x, Popular=4x, etc.) |
| `AudienceTickIntervalSeconds` | `SHOWTIME_AUDIENCE_TICK_INTERVAL_SECONDS` | `5` | How often the audience simulation evaluates engagement and migration |
| `EngagementDecayBaseRate` | `SHOWTIME_ENGAGEMENT_DECAY_BASE_RATE` | `0.02` | Per-tick engagement decay when content doesn't match interests |
| `HypeTrainTriggerThreshold` | `SHOWTIME_HYPE_TRAIN_TRIGGER_THRESHOLD` | `10` | Minimum matching sentiments in a pulse interval to trigger a hype train |
| `HypeTrainLevelExponent` | `SHOWTIME_HYPE_TRAIN_LEVEL_EXPONENT` | `1.5` | Exponential scaling factor for level thresholds |
| `HypeTrainDecayRate` | `SHOWTIME_HYPE_TRAIN_DECAY_RATE` | `0.1` | Per-tick decay rate for hype train score |
| `HypeTrainMaxDurationMinutes` | `SHOWTIME_HYPE_TRAIN_MAX_DURATION_MINUTES` | `10` | Hard timeout for hype trains |
| `FollowerEngagementThreshold` | `SHOWTIME_FOLLOWER_ENGAGEMENT_THRESHOLD` | `0.6` | Minimum average engagement for an audience member to become a follower |
| `FollowerWatchTimeMinutes` | `SHOWTIME_FOLLOWER_WATCH_TIME_MINUTES` | `30` | Minimum cumulative watch time for follower conversion |
| `MaxFollowersPerStreamer` | `SHOWTIME_MAX_FOLLOWERS_PER_STREAMER` | `10000` | Maximum followers per streamer entity |
| `RealDerivedMemberTtlSeconds` | `SHOWTIME_REAL_DERIVED_MEMBER_TTL_SECONDS` | `30` | TTL for anonymous real-derived audience members (2x pulse interval) |
| `CareerWorkerIntervalSeconds` | `SHOWTIME_CAREER_WORKER_INTERVAL_SECONDS` | `60` | Career progression worker evaluation frequency |
| `AutoStreamOnGameSession` | `SHOWTIME_AUTO_STREAM_ON_GAME_SESSION` | `false` | Automatically create streaming sessions when game sessions start |
| `AutoVoiceOnGameSession` | `SHOWTIME_AUTO_VOICE_ON_GAME_SESSION` | `true` | Automatically create voice rooms when game sessions start |
| `GrowthDebounceIntervalMs` | `SHOWTIME_GROWTH_DEBOUNCE_INTERVAL_MS` | `5000` | Debounce interval for seed growth contributions (prevent overwhelming lib-seed) |
| `DistributedLockTimeoutSeconds` | `SHOWTIME_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<ShowtimeService>` | Structured logging |
| `ShowtimeServiceConfiguration` | Typed configuration access (25 properties) |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IGameSessionClient` | Game session validation (L2 hard) |
| `IGameServiceClient` | Game service validation (L2 hard) |
| `ICharacterClient` | Character validation (L2 hard) |
| `ISeedClient` | Streamer career seed management (L2 hard) |
| `ICollectionClient` | Streaming milestone collection grants (L2 hard) |
| `ICurrencyClient` | Virtual tip currency operations (L2 hard) |
| `IContractClient` | Sponsorship contract management (L1 hard) |
| `IRelationshipClient` | Follower relationship bonds (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft L3/L4 dependencies (lib-broadcast, lib-voice, lib-analytics) |
| `IAudienceSimulator` | Audience tick processing, interest matching, migration (internal) |
| `IHypeTrainEngine` | Hype train lifecycle management (internal) |
| `IRealAudienceBlender` | Sentiment pulse → real-derived audience member translation (internal) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `AudienceTickWorker` | Evaluates engagement decay, migration, and interest matching for all active sessions | `AudienceTickIntervalSeconds` (5s) | `showtime:lock:audience-tick` |
| `CareerProgressionWorker` | Evaluates milestone thresholds, grants collection entries, records seed growth for ended sessions | `CareerWorkerIntervalSeconds` (60s) | `showtime:lock:career-worker` |

Both workers acquire distributed locks before processing to ensure multi-instance safety.

---

## API Endpoints (Implementation Notes)

**Current status**: Pre-implementation. All endpoints described below are architectural targets.

### Stream Session Management (6 endpoints)

All endpoints require `developer` role.

- **Start** (`/showtime/session/start`): Validates streamer entity and game service exist. Creates `ShowtimeSessionModel` in MySQL. Allocates initial audience pool from template reservoir (size based on career seed phase). Optionally associates with a platform session via lib-broadcast. Optionally creates voice room via lib-voice. Publishes `showtime.session.started`.
- **End** (`/showtime/session/end`): Lock. Ends audience simulation. Calculates session metrics (total watch-time, peak viewers, average engagement, hype summary). Evaluates follower conversions. Destroys audience pool from Redis. Deletes voice room via lib-voice if created. Records seed growth contributions. Publishes `showtime.session.ended`.
- **Pause** (`/showtime/session/pause`): Pauses audience simulation (decay continues but no new engagement). Publishes `showtime.session.paused`.
- **Resume** (`/showtime/session/resume`): Resumes audience simulation. Publishes `showtime.session.resumed`.
- **Get** (`/showtime/session/get`): Returns session state including current audience metrics, active hype train, content tags, linked platform session.
- **List** (`/showtime/session/list`): Active and recent sessions by game service or streamer entity, paginated.

### Audience Management (4 endpoints)

All endpoints require `developer` role.

- **GetAudienceSnapshot** (`/showtime/audience/snapshot`): Returns current audience composition: total count, engaged/disengaged counts, personality type distribution, real-derived count (admin-only), top engaged members.
- **GetAudienceFollowers** (`/showtime/audience/followers`): Paged query of persistent followers for a streamer entity. Includes engagement history, first-followed date, total watch time.
- **InjectAudienceEvent** (`/showtime/audience/inject`): Debug endpoint to manually inject a sentiment or audience event into a session. For testing hype trains and audience dynamics without real platform data.
- **SetContentTags** (`/showtime/audience/set-tags`): Manually set content tags for a session (normally derived from game events). For testing interest matching.

### Hype Train Management (2 endpoints)

All endpoints require `developer` role.

- **GetHypeStatus** (`/showtime/hype/status`): Returns current hype train state for a session (level, score, decay rate, time remaining).
- **InjectHypeEvent** (`/showtime/hype/inject`): Debug endpoint to inject a hype-contributing event. For testing hype train escalation.

### Career / Composability (3 endpoints)

All endpoints require `developer` role.

- **GetCareer** (`/showtime/career/get`): Returns streamer career summary: seed phase, growth domain breakdown, milestone history, follower count, total sessions, total watch hours.
- **GetMilestones** (`/showtime/career/milestones`): Returns all achieved milestones for a streamer entity with timestamps and session context.
- **RecordGrowth** (`/showtime/career/record-growth`): Manually record seed growth contribution. For testing career progression.

### Voice Room Orchestration (2 endpoints)

All endpoints require `developer` role.

- **CreateVoiceRoom** (`/showtime/voice/create`): Creates a voice room via lib-voice and stores the session-voice mapping. Called by event handler on `game-session.created`, or manually for ad-hoc voice rooms.
- **DeleteVoiceRoom** (`/showtime/voice/delete`): Deletes a voice room via lib-voice and clears the mapping. Called by event handler on `game-session.ended`, or manually.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/showtime/cleanup-by-character`): End active sessions where streamer is the character. Archive follower records. Remove streaming collection entries referencing this character.
- **CleanupByGameService** (`/showtime/cleanup-by-game-service`): End all active sessions for the game service. Delete all session records, follower records, and streaming-specific collection entries.

---

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────────┐
│                  Showtime Service: The Audience Metagame                  │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  SHOWTIME SESSION LIFECYCLE                                             │
│                                                                          │
│  game-session.created event                                              │
│      │                                                                   │
│      ▼                                                                   │
│  [lib-showtime event handler]                                           │
│      │                                                                   │
│      ├──► Create voice room (via lib-voice L3, soft)                     │
│      │                                                                   │
│      └──► Create streaming session (if auto-stream enabled)              │
│           │                                                              │
│           ▼                                                              │
│  AUDIENCE POOL (Redis)                                                   │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │  Simulated Members           │  Real-Derived Members          │      │
│  │  (always present)            │  (when lib-broadcast L3 present)  │      │
│  │                              │                                │      │
│  │  ┌─────┐ ┌─────┐ ┌─────┐   │  ┌─────┐ ┌─────┐              │      │
│  │  │loyal│ │fickle│ │lurker│   │  │ ?!? │ │ ?!? │  ◄── created │      │
│  │  │ 0.8 │ │ 0.4 │ │ 0.1 │   │  │ 0.9 │ │ 0.6 │      from   │      │
│  │  └──┬──┘ └──┬──┘ └──┬──┘   │  └──┬──┘ └──┬──┘    sentiment │      │
│  │     │       │       │       │     │       │        pulses    │      │
│  │     └───────┴───────┴───────┴─────┴───────┘                  │      │
│  │              │                                                │      │
│  │              ▼                                                │      │
│  │  Interest Matching Engine                                     │      │
│  │  (game events → content tags → engagement updates)            │      │
│  └───────────────────────────────────────────────────────────────┘      │
│           │                                                              │
│           ▼                                                              │
│  HYPE TRAIN (Redis)                                                      │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │  Level:  ██████████░░░░░░░  (3/5)                              │      │
│  │  Score:  ████████░░░░░░░░░  (68% to next level)                │      │
│  │  Decay:  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  (constant unless fed)              │      │
│  │                                                                │      │
│  │  Level 3+ → showtime.milestone.reached → Character History   │      │
│  │  Level 5+ → showtime.milestone.reached → Realm History       │      │
│  └───────────────────────────────────────────────────────────────┘      │
│           │                                                              │
│           ▼                                                              │
│  SESSION END → Career Progression                                        │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │  Seed Growth        Collection Grants     Currency Credits    │      │
│  │  ┌──────────┐       ┌──────────────┐      ┌──────────┐      │      │
│  │  │ streamer │       │ milestones   │      │ stream_  │      │      │
│  │  │ seed     │       │ hype achiev  │      │ tip      │      │      │
│  │  │ domains  │       │ world firsts │      │ autogain │      │      │
│  │  └──────────┘       └──────────────┘      └──────────┘      │      │
│  │                                                               │      │
│  │  Follower Conversions    Sponsorship Progress                 │      │
│  │  (engagement > 0.6 +     (milestone advancement              │      │
│  │   watch time > 30min)     via Contract)                       │      │
│  └───────────────────────────────────────────────────────────────┘      │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned per [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md):

### Phase 1: Schema & Generation
- Create showtime-api.yaml schema with all endpoints (19 endpoints across 5 groups)
- Create showtime-events.yaml schema (9 published events, 8 consumed events)
- Create showtime-configuration.yaml schema (25 configuration properties)
- Create showtime-client-events.yaml (hype train client events, audience milestone notifications)
- Generate service code
- Verify build succeeds

### Phase 2: Core Session Management
- Implement streaming session CRUD (create, get, list, end, pause, resume)
- Implement game-session event handlers (create voice room, optionally create streaming session)
- Implement session-voice mapping store
- Implement cleanup endpoints

### Phase 3: Audience Simulation Engine
- Implement audience pool management (allocate, migrate, destroy)
- Implement interest matching algorithm (content tags → engagement updates)
- Implement engagement decay and audience churn
- Implement audience tick background worker
- Implement audience snapshot endpoint

### Phase 4: Hype Train System
- Implement hype train lifecycle (trigger, escalate, decay, complete)
- Implement hype train events → content flywheel integration
- Implement hype status and inject endpoints

### Phase 5: Real Audience Blending
- Implement sentiment pulse consumer
- Implement real-derived audience member creation from tracking IDs
- Implement anonymous sentiment → temporary audience member translation
- Implement proportional blending with simulated pool

### Phase 6: Career Progression
- Register `streamer` seed type with growth phases, domains, and capability rules
- Implement session-end career evaluation (seed growth, milestones)
- Implement career endpoint and milestones endpoint
- Implement career progression background worker

### Phase 7: Composability Integration
- Register `streaming_milestones`, `streaming_hype`, `streaming_world_first` collection types
- Register `stream_tip` currency type
- Register `stream_sponsorship` contract template
- Register `stream_follower` relationship type
- Implement follower conversion logic
- Implement virtual tip autogain via Currency
- Implement sponsorship deal contract integration

---

## Potential Extensions

1. **NPC streamers**: NPCs could "stream" in-game performances (arena fights, craft demonstrations, musical performances). Their simulated audiences would behave the same way, and players could "watch" NPC streams, contributing to NPC audience metrics. This creates an economy where NPC performers compete for audience attention.

2. **Streamer raids**: In-game streamers could "raid" other streams -- sending their audience to another streamer's session. This creates a social mechanic where popular streamers can boost emerging ones, mirroring the Twitch raid mechanic within the game world.

3. **Collective audience groups**: For large audiences (1000+), individual audience member tracking becomes expensive. Collective groups ("20 loyal combat fans") could represent clusters, reducing Redis storage by 20x while maintaining behavioral fidelity.

4. **Audience personality evolution**: Audience members could evolve their personality types over time based on exposure to different content. A "fickle" viewer who watches 50 hours of the same streamer might become "loyal." This creates emergent audience dynamics.

5. **Cross-streamer audience migration**: When multiple streamers are live in the same game service, audience members could migrate between streams based on content interest matching. This creates natural competition for audience attention.

6. **Client events for streaming UX**: `showtime-client-events.yaml` for pushing audience reactions, hype train progress, milestone notifications, and follower updates to the streamer's WebSocket client.

7. **Variable Provider Factory**: `IShowtimeVariableProviderFactory` for ABML behavior expressions (`${streaming.audience_size}`, `${streaming.hype_level}`, `${streaming.career_phase}`). Could enable NPCs to react to streaming activity (a bard NPC performs harder when the audience is large).

8. **Realm-specific manifestation**: In Omega (cyberpunk meta-dashboard), streaming is explicit -- players see audience stats, manage their stream, and compete with other streamers. In Arcadia, the same mechanics manifest as "performing for a crowd" -- a bard performing at a tavern, a gladiator entertaining an arena, a craftsman demonstrating mastery. The underlying system is identical; the UX presentation varies by realm. Additionally, audience members could manifest as visible NPCs in the game world (a crowd gathering to watch a gladiator fight in Arcadia, floating avatars in Omega). lib-showtime provides the mechanics; the client renders realm-appropriate UX.

9. **Leaderboard integration**: Streamer rankings by follower count, total watch hours, peak hype level, world-first discoveries. Natural integration with the existing Leaderboard service.

---

## Seed Type: Streamer Career

| Property | Value |
|----------|-------|
| **SeedTypeCode** | `streamer` |
| **DisplayName** | Streamer |
| **MaxPerOwner** | 1 |
| **AllowedOwnerTypes** | `["character", "account"]` |
| **BondCardinality** | 0 |

### Growth Phases (ordered by MinTotalGrowth)

| Phase | MinTotalGrowth | Behavior |
|-------|---------------|----------|
| Unknown | 0.0 | No streaming history. Minimal starting audience pool. |
| Rising | 5.0 | Starting to attract regular audience. Follower system activates. Pool size increases. |
| Popular | 25.0 | Consistent audience with multiple followers. Hype trains become possible. Sponsorship deals available. |
| Celebrity | 100.0 | Large following. Hype trains are common. Audience migration from other streamers possible. |
| Legend | 500.0 | Server-wide recognition. Record-breaking streams generate realm history events. |

### Growth Domains

| Domain | Subdomain | Purpose |
|--------|-----------|---------|
| `audience_growth` | `.followers`, `.peak_viewers` | Total followers gained and peak concurrent viewer milestones |
| `engagement_quality` | `.average`, `.hype_peak` | Average audience engagement across sessions and peak hype train levels |
| `content_diversity` | `.tags`, `.unique_events` | Variety of content tags across sessions and unique game events during streams |
| `discovery_rate` | `.world_firsts`, `.milestones` | World-first discoveries and milestone events per stream hour |

### Growth Contribution Events

| Event | Seed Domain | Amount | Source |
|-------|------------|--------|--------|
| New follower gained | `audience_growth.followers` | +0.1 | Session end processing |
| Peak viewers milestone | `audience_growth.peak_viewers` | +0.5 per milestone tier | Session end processing |
| Session average engagement > 0.6 | `engagement_quality.average` | +0.2 | Session end processing |
| Hype train completed (level 3+) | `engagement_quality.hype_peak` | +0.3 per level above 2 | Hype completion handler |
| New content tag in session | `content_diversity.tags` | +0.05 | During session (debounced) |
| World-first discovery on stream | `discovery_rate.world_firsts` | +2.0 | Game event handler |
| Streaming milestone reached | `discovery_rate.milestones` | +0.5 | Milestone evaluation |

---

## Composability Map

Stream session identity and audience simulation are owned here. Platform integration is lib-broadcast (L3). Voice rooms are lib-voice (L3). Streamer career growth is Seed (`streamer` seed type). Streaming milestone unlocks are Collection. Virtual tips are Currency (`stream_tip` currency type). Sponsorship deals are Contract. Streamer-follower bonds are Relationship. lib-showtime orchestrates the metagame connecting these primitives.

The streaming metagame follows the same structural pattern as lib-divine -- an L4 orchestration layer that composes existing Bannou primitives (Seed, Currency, Collection, Contract, Relationship) to deliver game mechanics. Where lib-divine orchestrates blessings and divinity economy, lib-showtime orchestrates audience dynamics and streamer career. They are parallel orchestration layers composing the same underlying primitives, not the same service. This mirrors how Quest and Escrow both compose Contract but provide different game-flavored APIs.

### Seed: Streamer Career

The `streamer` seed type (detailed above) provides progressive growth that gates audience pool size, hype train availability, sponsorship eligibility, and milestone thresholds.

### Collection: Streaming Unlocks

| Collection Category | Example Entries | Trigger Event |
|-------------------|-----------------|---------------|
| Streaming Milestones | "First Stream", "100 Followers", "1000 Watch Hours", "10 Sessions" | `showtime.milestone.reached` |
| Hype Achievements | "First Hype Train", "Level 3 Hype", "Level 5 Hype", "Legendary Hype" | `showtime.hype.completed` |
| World-First Streams | "Discovered [X] On Stream" | `showtime.milestone.reached` with WorldFirstCount > 0 |

### Currency: Virtual Tips

| Property | Value |
|----------|-------|
| **Currency Type** | `stream_tip` |
| **Scope** | Per game service |
| **Transferable** | No (audience "tips" the streamer) |
| **Source** | Generated by audience engagement events (not real money) |
| **Spending** | Stream customization, audience boosts |
| **Autogain** | Passive tip generation from follower count |

### Contract: Sponsorship Deals

| Property | Value |
|----------|-------|
| **Contract Template** | `stream_sponsorship` |
| **Parties** | Sponsor (NPC merchant, guild, game entity) + Streamer |
| **Milestones** | `stream_hours` (stream X hours with sponsor visible), `audience_reach` (total viewers), `hype_generation` (trigger N hype trains) |
| **Prebound API** | Credit streamer with sponsorship payment (Currency), grant sponsor reputation boost (Seed growth) |

### Relationship: Follower Bonds

| Property | Value |
|----------|-------|
| **Relationship Type** | `stream_follower` |
| **Direction** | Follower → Streamer (unidirectional) |
| **Entity Types** | Polymorphic (audience member entity → streamer character/account) |
| **Lifecycle** | Created when audience member meets follow thresholds; persists across sessions; can unfollow via engagement decay |

---

## The Game-Session → Voice Room Orchestration

lib-showtime (L4) owns the "game sessions have voice" orchestration that previously would have lived in GameSession (L2) → Voice (L3), which would be a hierarchy violation:

### Without lib-showtime (Hierarchy Violation)
```
GameSession (L2) ──calls──► Voice (L3)   ← L2 cannot depend on L3
```

### With lib-showtime (Clean Architecture)
```
GameSession (L2) ──publishes──► game-session.created event
                                        │
                                        ▼
lib-showtime (L4) ──subscribes──► HandleGameSessionCreatedAsync
                                        │
                                        ▼
lib-showtime (L4) ──calls──► lib-voice (L3)   ← VALID (L4 → L3)
    Creates voice room, stores mapping
                                        │
game-session.ended event ───────────────┘
                                        │
                                        ▼
lib-showtime (L4) ──calls──► lib-voice (L3)
    Deletes voice room, clears mapping
```

This eliminates the hierarchy violation entirely. GameSession publishes events; lib-showtime consumes them and orchestrates the voice+streaming stack.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None. Plugin is in pre-implementation phase -- no code exists to contain bugs.*

### Intentional Quirks (Documented Behavior)

1. **Audience personality types are opaque strings**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, faction codes. Different games define different audience personality types. lib-showtime stores whatever personality string is provided and uses configurable matching weights.

2. **Real-derived flag is admin-only**: The `isRealDerived` flag on `AudienceMemberModel` and `AudienceFollowerModel` is NEVER exposed to players. The game UI must not reveal which audience members are real humans vs. simulated. This is a core design principle of the metagame, not a data visibility oversight.

3. **Hype trains are per-session singletons**: Only one hype train can be active per streaming session at a time. If a new trigger event occurs while a hype train is active, it feeds the existing train rather than starting a new one. This is intentional -- hype trains are about sustained collective energy, not rapid-fire micro-events.

4. **Follower conversion is asynchronous**: Audience members become followers during session-end processing, not in real-time during the session. This is deliberate -- follower status is a persistent cross-session state change that requires careful evaluation, not a transient engagement spike.

5. **100% simulated audience is a valid deployment**: lib-showtime works entirely without lib-broadcast (L3). Every feature except real audience blending functions with simulated audiences only. This is not a degraded mode -- it's a fully valid deployment for games that want audience mechanics without external platform integration.

6. **Content tags are derived, not authored**: Content tags for audience interest matching are generated from game events, not manually assigned. This means the audience reacts to what's actually happening in the stream, not to metadata about the stream.

7. **Audience members are NOT actors**: They don't run ABML behaviors, don't consume actor pool resources, and don't have cognition pipelines. They are lightweight state objects with deterministic behavior rules. This is critical for scalability -- a 10,000-member audience is 10,000 Redis entries, not 10,000 actor instances.

8. **Voice room orchestration is a stopgap**: lib-showtime owns the game-session → voice room flow to eliminate the GameSession → Voice hierarchy violation. Long-term, this orchestration may move to a dedicated "session orchestrator" if more cross-service session setup logic accumulates. For now, lib-showtime is the natural home because it already subscribes to game-session events.

### Design Considerations (Requires Planning)

1. **Audience pool sizing**: The relationship between career seed phase and audience pool size (base × 2^phase) needs tuning. Too small and the metagame feels empty; too large and Redis storage becomes expensive. Configurable per game service via `AudiencePoolScalePerPhase`.

2. **Interest matching algorithm weights**: The algorithm mapping content tags to audience engagement needs configurable weights. Should these be stored in configuration (simple, static), in a state store (dynamic, admin-editable), or derived from the game service's seed type definition (emergent)?

3. **Hype train threshold tuning**: The number of matching sentiments needed to trigger a hype train (`HypeTrainTriggerThreshold = 10`) and the level escalation exponent (`HypeTrainLevelExponent = 1.5`) need gameplay testing. Values that work for a 50-member audience may be wrong for a 5000-member audience.

4. **Cross-service follower entity representation**: When a follower is created via `IRelationshipClient`, what entity type represents the audience member? Simulated audience members don't have character IDs. Options: a synthetic entity type (`audience_member`), a character spawned to represent the follower, or a purely local follower record without cross-service relationship integration.

5. **Sentiment pulse timing alignment**: lib-broadcast publishes pulses every 15 seconds; lib-showtime ticks audiences every 5 seconds. Real-derived audience members created from a pulse will be evaluated by 3 ticks before the next pulse arrives. The TTL for anonymous real-derived members (`RealDerivedMemberTtlSeconds = 30`) covers two pulse intervals, but alignment edge cases may cause brief audience count fluctuations.

6. **Career progression when streamer entity changes**: If a character dies (and the streamer seed is character-owned), the career data in MySQL persists but the seed follows character death rules. Should the career history (sessions, milestones) transfer to a new character, or start fresh? This depends on whether the seed is account-owned (persists) or character-owned (archived).

7. **Voice room broadcasting consent UX**: When lib-showtime creates a voice room and a player wants to broadcast it, the consent flow goes through lib-voice. But the UX decision (modal dialog? in-room notification? veto-based?) is a client-side design question that affects how consent is presented and collected. lib-showtime/lib-voice provide the API; the client determines the UX.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [STREAMING-ARCHITECTURE.md](../planning/STREAMING-ARCHITECTURE.md) for the full planning document.*
