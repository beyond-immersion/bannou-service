# Competitive Gap Analysis & Roadmap

> **Created**: 2026-01-05
> **Purpose**: Strategic roadmap to close feature gaps with competitors (Nakama, PlayFab, Photon, Colyseus)
> **Scope**: New services and features needed to make Bannou a complete game backend solution

---

## Executive Summary

Bannou has strong foundations (auth, accounts, WebSocket gateway, schema-first architecture) and unique differentiators (GOAP + ABML NPC AI). However, it lacks several features that game developers expect from modern backends. This document prioritizes those gaps and outlines implementation strategies.

### Current State

| Category | Status | Notes |
|----------|--------|-------|
| Authentication | ✅ Complete | Email, OAuth, Steam, JWT |
| Account Management | ✅ Complete | Full CRUD, verification, sessions |
| WebSocket Gateway | ✅ Complete | Binary protocol, zero-copy routing |
| Permissions | ✅ Complete | Role + session-state based |
| Game Sessions | ✅ Complete | Lobby system, chat, basic actions |
| NPC AI | ✅ Complete | GOAP planner, ABML DSL (unique) |
| Voice | ⚠️ Basic | WebRTC rooms, SIP signaling |
| **Matchmaking** | ❌ Missing | Critical gap |
| **Analytics** | ❌ Missing | Blocks intelligent matchmaking |
| **Leaderboards** | ❌ Missing | Common expectation |
| **Achievements** | ❌ Missing | Common expectation |
| **Economy** | ❌ Missing | Complex, longer-term |
| **Engine SDKs** | ⚠️ Partial | C#/.NET only, need Godot/Unreal |

---

## Gap Analysis by Priority

### Tier 1: Critical Path (Blocks Core Gameplay Loops)

These features are prerequisites for most multiplayer games and should be prioritized.

#### 1. Analytics Service (`lib-analytics`)

**Why Critical**: Analytics is a **prerequisite for intelligent matchmaking**. Without player data (skill ratings, play patterns, session history), matchmaking is just random assignment.

**What Competitors Offer**:
- **PlayFab**: Player events, custom telemetry, dashboards, segmentation
- **Nakama**: Basic analytics, integrates with external tools
- **Colyseus**: No built-in analytics

**Implementation Strategy**:

```yaml
# schemas/analytics-api.yaml
paths:
  /analytics/event:
    post:
      summary: Record a player event
      x-permissions: [{ role: authenticated }]

  /analytics/query:
    post:
      summary: Query aggregated analytics
      x-permissions: [{ role: admin }]

  /analytics/player/{player_id}/summary:
    post:
      summary: Get player analytics summary (for matchmaking)
      x-permissions: [{ role: service }]
```

**Key Features**:
1. **Event Ingestion**: High-throughput event recording (Redis streams → batch to MySQL/ClickHouse)
2. **Player Summaries**: Aggregated stats per player (games played, win rate, avg session length)
3. **Skill Metrics**: Track performance data that feeds into matchmaking
4. **Session Analytics**: What do players do? Where do they drop off?
5. **Real-time Aggregates**: Live player counts, active sessions, popular modes

**Data Model**:
```
PlayerEvent:
  - event_id: GUID
  - player_id: GUID
  - event_type: string (game_start, game_end, achievement, purchase, etc.)
  - event_data: JSON
  - timestamp: datetime
  - session_id: GUID
  - realm_id: GUID (optional)

PlayerSummary (materialized view / cache):
  - player_id: GUID
  - games_played: int
  - games_won: int
  - total_playtime_minutes: int
  - skill_rating: float (ELO/Glicko-2/TrueSkill)
  - skill_confidence: float
  - last_active: datetime
  - preferred_modes: string[]
  - avg_session_length_minutes: float
```

**Infrastructure Considerations**:
- High write throughput → Redis Streams for buffering
- Batch writes to persistent store (MySQL or ClickHouse for analytics scale)
- Player summaries cached in Redis for fast matchmaking queries
- Consider time-series DB (TimescaleDB, InfluxDB) for metrics

**Estimated Complexity**: Medium-High (2-3 days for basic, 1 week for full)

---

#### 2. Matchmaking Service (`lib-matchmaking`)

**Why Critical**: Players expect to find opponents/teammates of similar skill quickly. Random matching leads to frustration.

**What Competitors Offer**:
- **Nakama**: Customizable matchmaker with properties, skill-based, party support
- **PlayFab**: Matchmaking queues, rules-based, server allocation
- **Photon**: Basic room matching, custom properties
- **Open Match** (Google): Sophisticated K8s-based matchmaking framework

**Current Bannou State**:
- ✅ Game sessions exist (lobby creation, join/leave)
- ✅ Peer discovery via session queries
- ✅ Voice P2P and relay working
- ❌ No skill-based matching
- ❌ No queue system
- ❌ No party/group matching

**Implementation Strategy**:

```yaml
# schemas/matchmaking-api.yaml
paths:
  /matchmaking/queue/join:
    post:
      summary: Join a matchmaking queue
      x-permissions: [{ role: authenticated }]

  /matchmaking/queue/leave:
    post:
      summary: Leave matchmaking queue
      x-permissions: [{ role: authenticated }]

  /matchmaking/queue/status:
    post:
      summary: Get queue status for player
      x-permissions: [{ role: authenticated }]

  /matchmaking/ticket/{ticket_id}:
    post:
      summary: Get match ticket details
      x-permissions: [{ role: authenticated }]
```

**Key Features**:
1. **Queue System**: Players join queues with preferences (mode, region, etc.)
2. **Skill-Based Matching**: Use analytics-derived skill ratings
3. **Ticket Lifecycle**: Ticket created → searching → matched → expired
4. **Party Support**: Groups queue together, matched as unit
5. **Backfill**: Fill partially-empty sessions with compatible players
6. **Region Awareness**: Prefer low-latency matches

**Matching Algorithm Options**:

| Algorithm | Pros | Cons | Best For |
|-----------|------|------|----------|
| **ELO** | Simple, well-understood | Binary (win/lose only) | 1v1 games |
| **Glicko-2** | Handles uncertainty, rating periods | More complex | Competitive games |
| **TrueSkill** | Team games, multiple outcomes | Microsoft patent (expired 2025?) | Team-based |
| **OpenSkill** | Open-source TrueSkill alternative | Less proven | Team-based, safe choice |

**Recommended**: Start with **Glicko-2** for flexibility, or **OpenSkill** if team games are primary.

**Queue Matching Logic**:
```
1. Player joins queue with:
   - skill_rating (from analytics)
   - skill_confidence
   - preferences (mode, map, etc.)
   - party_id (optional)
   - region

2. Matchmaker runs periodically (every 1-5 seconds):
   - Group tickets by mode/region
   - Expand skill window over time (start tight, widen)
   - Form matches when enough compatible players
   - Create game session, notify players via client event

3. Match formed:
   - Publish MatchFoundEvent to all participants
   - Players have N seconds to accept
   - If all accept → start game session
   - If any decline → return others to queue
```

**Dependency**: Requires `lib-analytics` for skill ratings.

**Estimated Complexity**: Medium (2-3 days for basic queue + skill matching)

---

### Tier 2: Common Expectations (Most Games Want These)

These features are table stakes for many games but don't block core gameplay.

#### 3. Leaderboards Service (`lib-leaderboard`)

**Why Important**: Players want to see rankings. Competitive games need them.

**What Competitors Offer**:
- **Nakama**: Seasonal, around-me, tournaments, multiple leaderboards
- **PlayFab**: Statistics-based leaderboards, automatic

**Implementation Strategy**:

With schema-first development, this is straightforward:

```yaml
# schemas/leaderboard-api.yaml
paths:
  /leaderboard/create:
    post:
      summary: Create a new leaderboard
      x-permissions: [{ role: admin }]

  /leaderboard/{leaderboard_id}/submit:
    post:
      summary: Submit a score
      x-permissions: [{ role: authenticated }]

  /leaderboard/{leaderboard_id}/top:
    post:
      summary: Get top N entries
      x-permissions: [{ role: anonymous }]

  /leaderboard/{leaderboard_id}/around-me:
    post:
      summary: Get entries around the player
      x-permissions: [{ role: authenticated }]

  /leaderboard/{leaderboard_id}/rank:
    post:
      summary: Get player's rank
      x-permissions: [{ role: authenticated }]
```

**Data Model**:
```
Leaderboard:
  - leaderboard_id: GUID
  - name: string
  - sort_order: asc | desc
  - reset_schedule: none | daily | weekly | monthly | seasonal
  - metadata: JSON

LeaderboardEntry:
  - leaderboard_id: GUID
  - player_id: GUID
  - score: float
  - metadata: JSON (extra display data)
  - submitted_at: datetime
```

**Implementation Notes**:
- Redis Sorted Sets (`ZADD`, `ZRANK`, `ZRANGE`) are perfect for this
- "Around me" = `ZRANK` to get position, then `ZRANGE` for neighbors
- Seasonal = new sorted set key per period, archive old ones
- Consider write-through to MySQL for durability

**Estimated Complexity**: Low (4-8 hours including tests)

---

#### 4. Achievements Service (`lib-achievement`)

**Why Important**: Achievements drive engagement and give players goals.

**What Competitors Offer**:
- **PlayFab**: Rules-based achievements tied to statistics
- **Steam**: External achievement system (integrate, don't replace)

**Implementation Strategy**:

```yaml
# schemas/achievement-api.yaml
paths:
  /achievement/list:
    post:
      summary: List all achievements
      x-permissions: [{ role: anonymous }]

  /achievement/player/{player_id}:
    post:
      summary: Get player's achievements
      x-permissions: [{ role: authenticated }]

  /achievement/unlock:
    post:
      summary: Unlock an achievement for a player
      x-permissions: [{ role: service }]  # Only services can unlock

  /achievement/progress:
    post:
      summary: Update progress toward an achievement
      x-permissions: [{ role: service }]
```

**Data Model**:
```
Achievement:
  - achievement_id: GUID
  - name: string
  - description: string
  - icon_url: string
  - points: int
  - hidden: bool (secret achievements)
  - category: string

PlayerAchievement:
  - player_id: GUID
  - achievement_id: GUID
  - unlocked_at: datetime
  - progress: float (0.0 - 1.0)
  - progress_data: JSON (e.g., {"kills": 47, "required": 100})
```

**Unlock Patterns**:
1. **Direct unlock**: Service calls `/achievement/unlock` when condition met
2. **Progress-based**: Service updates progress, auto-unlocks at 100%
3. **Event-driven**: Achievement service subscribes to events, evaluates rules

**Recommended**: Start with direct unlock (simplest), add event-driven later.

**Platform Integration**:
- When unlocking, also call Steam/Xbox/PlayStation achievement APIs
- Store platform sync status to handle failures

**Estimated Complexity**: Low (4-8 hours including tests)

---

### Tier 3: Differentiators & Polish

These features round out the platform but aren't urgent.

#### 5. Economy Service (`lib-economy`)

**Why Important**: Free-to-play games need virtual currencies, stores, IAP.

**What Competitors Offer**:
- **Nakama**: Wallets, virtual currencies, IAP validation
- **PlayFab**: Full economy system, stores, bundles, real-money

**Why This Is Hard**:
- Financial systems require bulletproof consistency
- IAP validation varies by platform (Apple, Google, Steam)
- Anti-fraud measures needed
- Regulatory considerations (gambling laws, etc.)

**Implementation Strategy** (Long-term):

```yaml
# schemas/economy-api.yaml
paths:
  /economy/wallet:
    post:
      summary: Get player's wallet (all currencies)
      x-permissions: [{ role: authenticated }]

  /economy/grant:
    post:
      summary: Grant currency to player
      x-permissions: [{ role: service }]

  /economy/spend:
    post:
      summary: Spend currency
      x-permissions: [{ role: authenticated }]

  /economy/transfer:
    post:
      summary: Transfer between players
      x-permissions: [{ role: authenticated }]

  /economy/store/list:
    post:
      summary: List store items
      x-permissions: [{ role: anonymous }]

  /economy/store/purchase:
    post:
      summary: Purchase store item
      x-permissions: [{ role: authenticated }]

  /economy/iap/validate:
    post:
      summary: Validate IAP receipt and grant items
      x-permissions: [{ role: authenticated }]
```

**Critical Requirements**:
- **Idempotency**: All transactions must be idempotent (receipt ID as key)
- **Audit Trail**: Every currency change logged with reason
- **Consistency**: Use database transactions, not eventually-consistent
- **Rate Limiting**: Prevent abuse

**IAP Validation Complexity**:
| Platform | Validation Method | Difficulty |
|----------|-------------------|------------|
| Steam | Web API with app ticket | Medium |
| Apple | App Store Server API v2 | High (JWT signing) |
| Google | Play Developer API | Medium |
| Xbox | XStore API | Medium |

**Estimated Complexity**: High (1-2 weeks minimum, ongoing maintenance)

**Recommendation**: Defer until specific game needs it. Can integrate third-party (e.g., Xsolla) as interim.

---

#### 6. Cloud Saves (`lib-state` extension)

**Current State**: `lib-state` provides key-value storage but lacks player-scoped save semantics.

**What's Needed**:
```yaml
paths:
  /save/slots:
    post:
      summary: List player's save slots
      x-permissions: [{ role: authenticated }]

  /save/load:
    post:
      summary: Load a save slot
      x-permissions: [{ role: authenticated }]

  /save/write:
    post:
      summary: Write to a save slot
      x-permissions: [{ role: authenticated }]

  /save/delete:
    post:
      summary: Delete a save slot
      x-permissions: [{ role: authenticated }]
```

**Features**:
- Multiple save slots per player
- Versioning / conflict resolution
- Size limits per slot
- Cross-device sync

**Estimated Complexity**: Low-Medium (1 day, mostly schema + thin wrapper around lib-state)

---

#### 7. Push Notifications

**Options**:
1. **Integrate Firebase Cloud Messaging**: Most games use this anyway
2. **Build `lib-notifications`**: If self-hosted requirement is strict

**Recommendation**: Document FCM integration pattern rather than building. Most games already use Firebase for mobile.

---

### Tier 4: Engine SDKs

Current state: C#/.NET SDK and TypeScript NPM package.

**Priority Order**:

| Engine | Importance | Effort | Notes |
|--------|------------|--------|-------|
| **Godot** | High | Medium | Growing rapidly, underserved by competitors |
| **Unreal** | High | High | C++ SDK, complex build system |
| **Unity** | Medium | Low | Already works via NuGet, polish needed |
| **Defold** | Low | Medium | Lua SDK |

**Godot SDK Strategy**:
- GDScript bindings or GDExtension (C++)
- WebSocket client with binary protocol
- Automatic capability manifest handling
- Match Unity SDK feature parity

**Unreal SDK Strategy**:
- C++ with Blueprints exposure
- Async HTTP/WebSocket via UE's systems
- Integration with UE's Online Subsystem

---

## Dependency Graph

```
                    ┌─────────────────┐
                    │   Analytics     │
                    │  (lib-analytics)│
                    └────────┬────────┘
                             │
                             │ skill ratings
                             ▼
┌─────────────────┐    ┌─────────────────┐
│  Leaderboards   │    │   Matchmaking   │
│ (lib-leaderboard│    │ (lib-matchmaking│
└─────────────────┘    └─────────────────┘
         │                      │
         │ scores               │ match results
         ▼                      ▼
    ┌─────────────────────────────────┐
    │         Game Sessions           │
    │      (lib-game-session)         │
    │         [EXISTS]                │
    └─────────────────────────────────┘
                    │
                    │ game events
                    ▼
            ┌───────────────┐
            │  Achievements │
            │(lib-achievement│
            └───────────────┘

Economy is independent (can be built anytime)
Cloud Saves are independent (can be built anytime)
SDKs are independent (can be built anytime)
```

**Critical Path**: Analytics → Matchmaking

---

## Implementation Phases

### Phase 1: Analytics Foundation (Week 1)

**Goal**: Enable data collection that feeds matchmaking.

1. Create `schemas/analytics-api.yaml` and `schemas/analytics-events.yaml`
2. Generate `lib-analytics` service
3. Implement event ingestion (Redis streams)
4. Implement player summary aggregation
5. Add skill rating calculation (Glicko-2)
6. Expose `/analytics/player/{id}/summary` for matchmaking

**Success Criteria**:
- Events recorded from game sessions
- Player skill ratings calculated and queryable
- < 10ms latency for summary queries

### Phase 2: Matchmaking (Week 2)

**Goal**: Players can queue and be matched by skill.

1. Create `schemas/matchmaking-api.yaml` and `schemas/matchmaking-events.yaml`
2. Generate `lib-matchmaking` service
3. Implement queue system (Redis sorted sets by queue time + skill)
4. Implement matching algorithm (skill window expansion)
5. Integrate with game-session creation
6. Add client events for match found/cancelled

**Success Criteria**:
- Players can join queues
- Matches formed within 30 seconds (configurable)
- Skill difference within bounds
- Party queuing works

### Phase 3: Leaderboards + Achievements (Week 2-3)

**Goal**: Core engagement features.

1. Create schemas for both services
2. Implement leaderboards (Redis sorted sets)
3. Implement achievements (simple unlock model)
4. Add platform integration stubs (Steam, etc.)

**Success Criteria**:
- Leaderboards display top players
- Achievements unlock and persist
- "Around me" queries work

### Phase 4: Engine SDKs (Ongoing)

**Goal**: Expand engine support.

1. Godot SDK (GDScript or GDExtension)
2. Unreal SDK (C++ with Blueprints)
3. Improve Unity integration docs

### Phase 5: Economy (Future)

**Goal**: Virtual currency and store.

Defer until specific game requirements known.

---

## Competitive Positioning After Implementation

| Feature | Nakama | PlayFab | Bannou (Current) | Bannou (After) |
|---------|--------|---------|------------------|----------------|
| Auth | ✅ | ✅ | ✅ | ✅ |
| Matchmaking | ✅ | ✅ | ❌ | ✅ |
| Leaderboards | ✅ | ✅ | ❌ | ✅ |
| Achievements | ❌ | ✅ | ❌ | ✅ |
| Analytics | ⚠️ | ✅ | ❌ | ✅ |
| Economy | ✅ | ✅ | ❌ | ⚠️ (later) |
| NPC AI | ❌ | ❌ | ✅ | ✅ |
| Schema-First | ❌ | ❌ | ✅ | ✅ |
| Self-Hosted | ✅ | ❌ | ✅ | ✅ |
| Engine SDKs | 10+ | 5+ | 2 | 4+ |

---

## Additional Gaps Identified

### Not Yet Discussed

#### Inventory System

Most games need inventory management. Could be part of economy or standalone.

```yaml
paths:
  /inventory/list:
    post:
      summary: List player's inventory
  /inventory/add:
    post:
      summary: Add item to inventory (service only)
  /inventory/remove:
    post:
      summary: Remove item from inventory
  /inventory/transfer:
    post:
      summary: Transfer item between players
```

**Complexity**: Medium (stacking, slots, item definitions)

#### Friends / Social

Currently no friend system. Players may want to:
- Add/remove friends
- See friend online status
- Invite friends to games
- Block players

Could extend `lib-relationship` or create `lib-social`.

**Complexity**: Medium

#### Clans / Guilds

Team-based games need persistent groups:
- Create/join/leave clan
- Clan roles and permissions
- Clan leaderboards
- Clan chat

**Complexity**: Medium-High

#### Replay System

Competitive games benefit from replays:
- Record game state snapshots
- Store compressed replay data
- Playback API

**Complexity**: High (storage, compression, playback)

#### Anti-Cheat Hooks

Not building anti-cheat, but should:
- Document integration patterns for EasyAntiCheat/BattlEye
- Provide server-side validation hooks
- Anomaly detection via analytics

**Complexity**: Documentation only (Low)

#### Content Moderation

Chat/voice moderation needs:
- Profanity filters
- Report system
- Moderator tools
- Automated detection

**Complexity**: Medium-High

---

## Research Sources

### Competitor Documentation

| Competitor | Documentation URL | Notes |
|------------|-------------------|-------|
| Nakama | https://heroiclabs.com/docs/ | Comprehensive, good examples |
| Nakama GitHub | https://github.com/heroiclabs/nakama | Open source reference |
| Colyseus | https://docs.colyseus.io/ | Node.js patterns |
| Photon Fusion | https://doc.photonengine.com/fusion/current | Real-time sync patterns |
| PlayFab | https://docs.microsoft.com/gaming/playfab/ | Full BaaS reference |
| Agones | https://agones.dev/site/docs/ | K8s game server patterns |
| Open Match | https://open-match.dev/site/docs/ | Matchmaking reference |

### Skill Rating Algorithms

| Algorithm | Resource |
|-----------|----------|
| ELO | https://en.wikipedia.org/wiki/Elo_rating_system |
| Glicko-2 | http://www.glicko.net/glicko/glicko2.pdf |
| TrueSkill | https://www.microsoft.com/en-us/research/project/trueskill-ranking-system/ |
| OpenSkill | https://github.com/philihp/openskill.js |

### Infrastructure Patterns

| Topic | Resource |
|-------|----------|
| Redis Sorted Sets | https://redis.io/docs/data-types/sorted-sets/ |
| Redis Streams | https://redis.io/docs/data-types/streams/ |
| Time-Series DBs | https://www.timescale.com/ , https://www.influxdata.com/ |

### Engine SDK References

| Engine | SDK Patterns |
|--------|--------------|
| Godot | https://docs.godotengine.org/en/stable/tutorials/plugins/gdextension/ |
| Unreal | https://docs.unrealengine.com/5.0/en-US/online-subsystem-in-unreal-engine/ |
| Unity | https://docs.unity3d.com/Manual/UNetUsingHLAPI.html |

### Industry Articles

| Topic | URL |
|-------|-----|
| Game Server Comparison 2025 | https://medevel.com/game-server-2025/ |
| Nakama Self-Hosting | https://www.snopekgames.com/tutorial/2021/how-host-nakama-server-10mo/ |
| Unity Networking 2025 | https://appwill.co/multiplayer-in-unity-best-networking-solutions-2025/ |
| Heroic Cloud Pricing | https://heroiclabs.com/pricing/ |

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-05 | Analytics before matchmaking | Can't do skill-based matching without skill data |
| 2026-01-05 | Glicko-2 for skill rating | Handles uncertainty, open algorithm, team-extensible |
| 2026-01-05 | Defer economy | Complex, game-specific, can use third-party interim |
| 2026-01-05 | Godot SDK priority | Growing engine, competitors underserve it |
| 2026-01-05 | Redis for leaderboards | Sorted sets are perfect fit, already in stack |

---

## Open Questions

1. **Analytics storage**: MySQL vs ClickHouse vs TimescaleDB for event storage at scale?
2. **Matchmaking regions**: How to handle cross-region matching? Latency thresholds?
3. **Party matching**: How to handle parties with disparate skill levels?
4. **Seasonal resets**: How to archive old leaderboard/achievement data?
5. **Platform achievements**: Sync strategy for Steam/Xbox/PlayStation?

---

*This document should be updated as implementation progresses and decisions are made.*
