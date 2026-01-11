# Potential Enhancements

> **Created**: 2026-01-08
> **Last Updated**: 2026-01-10
> **Purpose**: Track potential enhancements, their value proposition, and implementation considerations
> **Scope**: New services, features, and SDKs that could improve the Bannou platform

This document tracks potential enhancements for the Bannou platform. Each item includes:
- Description of the enhancement
- Examples of use cases
- External resources that may be helpful
- Effort estimate
- Usefulness analysis (including reasons NOT to implement some features)

---

## Current Platform Capabilities

Before reviewing potential enhancements, here's what Bannou already provides:

| Capability | Service | Status |
|------------|---------|--------|
| Authentication | lib-auth | Complete (Email, OAuth, Steam, JWT) |
| Account Management | lib-account | Complete (CRUD, verification, sessions) |
| WebSocket Gateway | lib-connect | Complete (binary protocol, zero-copy routing) |
| Permissions | lib-permission | Complete (role + session-state based) |
| Game Sessions | lib-game-session | Complete (lobby system, chat, reservations) |
| **Matchmaking** | **lib-matchmaking** | **Complete (skill-based queues, party support, accept/decline)** |
| NPC AI | lib-behavior | Complete (GOAP planner, ABML DSL) |
| Voice Communication | lib-voice | Basic (WebRTC rooms, SIP signaling) |
| Analytics & Skill Rating | lib-analytics | Complete (event ingestion, Glicko-2, summaries) |
| Leaderboards | lib-leaderboard | Complete (seasonal, around-me, Redis sorted sets) |
| Achievements | lib-achievement | Complete (progress, platform sync) |
| Entity Relationships | lib-relationship | Complete (bidirectional, soft-delete) |

### Recently Completed: Matchmaking Service

The **lib-matchmaking** service was implemented in January 2026, delivering all originally-planned features:

**Core Features Implemented:**
- Queue management (create, update, delete, list, get)
- Ticket-based matchmaking with join/leave/status operations
- Skill-based matching using ratings from lib-analytics
- Configurable skill window expansion over wait time
- Match accept/decline flow with timeout handling

**Advanced Features:**
- Party support with skill aggregation (highest, average, weighted)
- Exclusive groups (prevents conflicting concurrent queues)
- Tournament support with registration requirements
- Query-based property matching (region, mode preferences)
- Auto-requeue on match decline
- Reconnection handling via pending match storage

**Integration:**
- Game-session reservation system for matchmade sessions
- Session-state shortcuts for leave/status/accept/decline
- Client events via IClientEventPublisher
- Server events for analytics and monitoring
- Background processing for interval-based matching

**Configuration:** Comprehensive queue-level configuration including interval timing, max wait, skill expansion curves, party limits, and more. See `docs/guides/MATCHMAKING.md` for full documentation.

---

## Tier 1: High-Value Enhancements

### 1. Engine SDKs

**Description**: Native SDK integrations for popular game engines beyond the current C#/.NET and TypeScript support.

#### 1a. Godot SDK

**Description**: GDScript or GDExtension (C++) bindings for Godot 4.x that provide WebSocket client with binary protocol support.

**Examples**:
- GDScript: `bannou.authenticate("email", "pass")`
- Signal-based event handling: `bannou.on_match_found.connect(_on_match)`
- Automatic capability manifest handling

**External Resources**:
- [GDExtension Documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/gdextension/)
- [Godot WebSocket](https://docs.godotengine.org/en/stable/classes/class_websocketclient.html)
- [Godot High-Level Multiplayer](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)

**Effort Estimate**: Medium (1-2 weeks for full SDK)

**Usefulness**: **HIGH**

**Analysis**:
- **PRO**: Godot is growing rapidly and underserved by backend solutions
- **PRO**: Open-source engine aligns with self-hosted philosophy
- **PRO**: Single codebase can target multiple platforms
- **CONSIDERATION**: GDExtension requires C++ expertise; GDScript simpler but slower

#### 1b. Unreal SDK

**Description**: C++ SDK with Blueprints exposure, integrating with Unreal's Online Subsystem.

**Examples**:
- Blueprint node: "Authenticate with Email"
- Async HTTP/WebSocket via UE's systems
- Integration with UE's session management

**External Resources**:
- [Unreal Online Subsystem](https://docs.unrealengine.com/5.0/en-US/online-subsystem-in-unreal-engine/)
- [UE5 HTTP Module](https://docs.unrealengine.com/5.0/en-US/API/Runtime/HTTP/)
- [UE5 WebSockets](https://docs.unrealengine.com/5.0/en-US/API/Runtime/WebSockets/)

**Effort Estimate**: High (2-3 weeks for full SDK)

**Usefulness**: **MEDIUM-HIGH**

**Analysis**:
- **PRO**: Unreal is widely used for AAA and indie games
- **PRO**: Online Subsystem integration provides familiar API surface
- **CON**: High complexity due to UE's build system and C++ requirements
- **CONSIDERATION**: May need separate builds for different UE versions

#### 1c. Unity SDK Polish

**Description**: Improve existing Unity integration with proper package structure, documentation, and examples.

**External Resources**:
- [Unity Package Manager](https://docs.unity3d.com/Manual/CustomPackages.html)
- [Unity Netcode](https://docs-multiplayer.unity3d.com/)

**Effort Estimate**: Low (3-5 days)

**Usefulness**: **MEDIUM**

**Analysis**:
- **PRO**: Already works via NuGet; polish is incremental
- **CONSIDERATION**: Unity has its own backend solutions (Unity Gaming Services)

---

### 2. Cloud Saves Service (`lib-save` or extension to `lib-state`)

**Description**: Player-scoped save data management with multiple slots, versioning, and cross-device sync.

**Examples**:
- Player has 3 save slots; loads slot 2 on new device
- Auto-save creates versioned checkpoint every 5 minutes
- Conflict resolution when same save modified on multiple devices

**Key Features**:
- Multiple save slots per player
- Versioning / rollback capability
- Conflict detection and resolution
- Size limits per slot
- Metadata (last played, playtime, progress %)

**External Resources**:
- [Steam Cloud](https://partner.steamgames.com/doc/features/cloud) - Reference implementation
- [Epic Games Cloud Saves](https://dev.epicgames.com/docs/game-services/cloud-save) - Reference implementation

**Effort Estimate**: Low-Medium (1-2 days; thin wrapper around lib-state)

**Usefulness**: **MEDIUM-HIGH**

**Analysis**:
- **PRO**: Common player expectation for modern games
- **PRO**: lib-state already provides key-value storage; this adds semantic layer
- **PRO**: Cross-device sync enables mobile/PC play
- **CONSIDERATION**: Large saves may need blob storage (MinIO) integration

---

## Tier 2: Moderate-Value Enhancements

### 3. Economy Service (`lib-economy`)

**Description**: Virtual currency management, store system, and in-app purchase validation.

**Examples**:
- Player earns 100 gold from quest completion
- Player purchases cosmetic item for 500 gems
- IAP receipt validated and gems credited

**Key Features**:
- Wallet management (multiple currencies)
- Transaction ledger (audit trail)
- Store catalog and pricing
- IAP validation (Steam, Apple, Google, Xbox)
- Idempotent transactions

**External Resources**:
- [Steam Microtransactions](https://partner.steamgames.com/doc/features/microtransactions)
- [Apple App Store Server API](https://developer.apple.com/documentation/appstoreserverapi)
- [Google Play Developer API](https://developers.google.com/android-publisher)
- [Xbox XStore API](https://docs.microsoft.com/gaming/xbox-live/features/commerce/xstore)

**Effort Estimate**: High (1-2 weeks minimum; ongoing IAP maintenance)

**Usefulness**: **MEDIUM**

**Analysis**:
- **PRO**: Essential for free-to-play monetization
- **PRO**: Centralized economy prevents duplication exploits
- **CON**: High complexity, especially IAP validation per platform
- **CON**: Regulatory considerations (gambling laws, regional restrictions)
- **CON**: Game-specific requirements vary significantly
- **RECOMMENDATION**: Consider third-party integration (Xsolla, etc.) as interim solution

---

### 4. Inventory Service (`lib-inventory`)

**Description**: Item management with stacking, slots, and transfer capabilities.

**Examples**:
- Player picks up sword; added to inventory slot 1
- Player stacks 50 arrows into existing stack of 25
- Player trades item to another player

**Key Features**:
- Item definition catalog
- Per-player inventory with slots
- Stacking rules by item type
- Item transfer between players
- Item consumption/destruction

**External Resources**:
- General game design patterns (no standard references)

**Effort Estimate**: Medium (3-5 days)

**Usefulness**: **MEDIUM**

**Analysis**:
- **PRO**: Common pattern for RPGs and survival games
- **CON**: Highly game-specific (slot count, stacking rules, item types)
- **CONSIDERATION**: Could be combined with Economy service
- **CONSIDERATION**: May be better as game-specific implementation

---

### 5. Clans/Guilds Service (`lib-guild`)

**Description**: Persistent player groups with hierarchy, roles, and shared resources.

**Examples**:
- Player creates guild "Dragon Slayers" with 3-tier rank system
- Guild leader promotes member to officer role
- Guild competes in guild-vs-guild leaderboard

**Key Features**:
- Guild creation and management
- Membership with roles/ranks
- Guild permissions system
- Guild bank/storage (requires Economy)
- Guild leaderboards integration
- Guild chat channels

**External Resources**:
- General game design patterns (no standard references)

**Effort Estimate**: Medium-High (1 week)

**Usefulness**: **MEDIUM**

**Analysis**:
- **PRO**: Important for games with strong social components
- **PRO**: lib-relationship provides foundation for member relationships
- **CON**: Complex permission hierarchies
- **CONSIDERATION**: lib-relationship can model basic guild membership already
- **CONSIDERATION**: Game-specific features (guild halls, territories) vary

---

## Tier 3: Lower Priority / Specialized

### 6. Replay System (`lib-replay`)

**Description**: Game state recording and playback for competitive integrity and content creation.

**Examples**:
- Match replay saved for tournament review
- Player watches their death from opponent's perspective
- Replay shared as spectator content

**Key Features**:
- State snapshot recording
- Compressed storage
- Playback API with seek
- Sharing/publishing

**External Resources**:
- [Riot Games Tech Blog](https://technology.riotgames.com/) - Replay system articles
- Delta compression techniques

**Effort Estimate**: High (1-2 weeks; game-specific integration)

**Usefulness**: **LOW-MEDIUM**

**Analysis**:
- **PRO**: Valuable for esports and competitive games
- **CON**: Extremely game-specific (state format varies per game)
- **CON**: High storage requirements for long matches
- **CON**: Replay format tied to game version (compatibility issues)
- **RECOMMENDATION**: Document integration patterns rather than generic service

---

### 7. Content Moderation (`lib-moderation`)

**Description**: Chat and voice moderation with reporting, filtering, and moderator tools.

**Examples**:
- Profanity filter catches slur in chat; message blocked
- Player reports toxic behavior; creates moderation ticket
- Moderator reviews report; issues 24-hour mute

**Key Features**:
- Profanity/slur filtering
- Player reporting system
- Moderator queue and tools
- Automated detection (ML-based)
- Punishment system (mute, ban, etc.)

**External Resources**:
- [Perspective API](https://perspectiveapi.com/) - Toxicity detection
- [OpenAI Moderation](https://platform.openai.com/docs/guides/moderation) - Content classification
- [Discord Safety](https://discord.com/safety) - Reference patterns

**Effort Estimate**: Medium-High (1-2 weeks for basic; ML integration adds complexity)

**Usefulness**: **MEDIUM**

**Analysis**:
- **PRO**: Important for community health
- **PRO**: Required for games targeting younger audiences
- **CON**: Complex (legal requirements, appeal processes)
- **CON**: ML moderation requires training data and tuning
- **CONSIDERATION**: Third-party services may be more practical
- **CONSIDERATION**: Regional legal requirements vary

---

### 8. Push Notifications

**Description**: Mobile push notifications for re-engagement and real-time alerts.

**Examples**:
- "Your friend invited you to play!" notification on mobile
- "Your tournament match starts in 5 minutes" alert
- "Daily rewards available!" re-engagement

**External Resources**:
- [Firebase Cloud Messaging](https://firebase.google.com/docs/cloud-messaging)
- [Apple Push Notification Service](https://developer.apple.com/documentation/usernotifications)

**Effort Estimate**: Low (integration pattern documentation)

**Usefulness**: **LOW**

**Analysis**:
- **PRO**: Important for mobile games
- **CON**: Most games use Firebase/platform services directly
- **RECOMMENDATION**: Document integration pattern rather than building service
- **RECOMMENDATION**: This is a "non-enhancement" - better to integrate existing solutions

---

## Explicitly NOT Recommended

### 9. Built-in Anti-Cheat

**Description**: Server-side or client-side anti-cheat system.

**Why NOT to build**:
- Anti-cheat is an arms race requiring constant updates
- Dedicated solutions (EasyAntiCheat, BattlEye, Vanguard) have teams focused solely on this
- Server-side validation (which we support) catches many issues without dedicated anti-cheat
- Building anti-cheat would divert resources from core features

**Instead**:
- Document server-side validation patterns
- Provide hooks for integrating external anti-cheat
- Use lib-analytics for anomaly detection (unusually high win rates, impossible actions)

---

### 10. Game Server Hosting

**Description**: Dedicated game server provisioning and management.

**Why NOT to build**:
- Extremely complex (scaling, regions, DDoS protection)
- Existing solutions (Agones, GameLift, Multiplay) are mature
- Requires significant infrastructure investment
- Game servers are game-specific (authoritative logic varies)

**Instead**:
- Document integration with Agones (Kubernetes-based)
- Focus on orchestration (lib-orchestrator) for service management, not game servers

---

### 11. Full CMS/Admin Dashboard

**Description**: Comprehensive web-based admin UI for all services.

**Why NOT to build** (as core feature):
- UI preferences vary significantly between teams
- Web frontend technologies evolve rapidly
- Better served by API-first approach
- Teams can build custom dashboards using APIs

**Instead**:
- Ensure all admin functionality exposed via APIs
- Provide Swagger/OpenAPI documentation
- Consider minimal operational dashboards (Grafana) for monitoring

---

## Implementation Priority Matrix

| Enhancement | Effort | Usefulness | Dependencies | Recommended Order |
|-------------|--------|------------|--------------|-------------------|
| Godot SDK | Medium | High | None | 1st |
| Cloud Saves | Low-Medium | Medium-High | lib-state (done) | 2nd |
| Unity Polish | Low | Medium | Existing NuGet | 3rd |
| Unreal SDK | High | Medium-High | None | 4th |
| Economy | High | Medium | lib-state (done) | Later |
| Inventory | Medium | Medium | Economy (optional) | Later |
| Guilds | Medium-High | Medium | lib-relationship (done) | Later |
| Moderation | Medium-High | Medium | lib-connect (done) | As needed |
| Replay | High | Low-Medium | Game-specific | Not recommended |

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-08 | Document created | Migration from competitive gaps analysis |
| 2026-01-08 | Anti-cheat NOT recommended | Arms race, external solutions better |
| 2026-01-08 | Game hosting NOT recommended | Agones/GameLift more appropriate |
| 2026-01-08 | Push notifications = integration docs | Firebase already ubiquitous |
| 2026-01-10 | **Matchmaking IMPLEMENTED** | Full-featured lib-matchmaking with skill-based queues, party support, match accept/decline, exclusive groups, configurable skill expansion, tournament support, and game-session reservation integration |

---

## Open Questions

1. **Matchmaking regions**: How should cross-region matching work? Latency thresholds?
   - *Current approach*: Region handled via ticket `stringProperties` (e.g., `region:us-west`); game-specific query matching filters by region. Latency thresholds are left to the game client to enforce via queue selection.
2. ~~**Party skill disparity**: How to handle parties with widely different skill levels?~~
   - *RESOLVED*: lib-matchmaking supports `partySkillAggregation` with `highest` (anti-smurf), `average` (casual), and `weighted` (custom weights) modes per queue.
3. **Economy fraud prevention**: What anti-fraud measures are essential vs optional?
4. **SDK versioning**: How to maintain SDKs across engine version changes?

---

*This document should be updated as decisions are made and priorities shift.*
