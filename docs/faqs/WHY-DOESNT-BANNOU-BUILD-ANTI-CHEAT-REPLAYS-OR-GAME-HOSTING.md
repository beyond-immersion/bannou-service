# Why Doesn't Bannou Build Anti-Cheat, Replays, or Game Hosting?

> **Short Answer**: Because these are commodity concerns better served by specialist providers. Bannou focuses on composable primitives that create emergent gameplay systems. Anti-cheat is an arms race (use EasyAntiCheat/BattlEye). Game server hosting requires massive infrastructure (use Agones/GameLift). Replays are deeply game-specific (use Save-Load patterns). Content moderation needs ML and legal expertise (use Perspective API or third-party services). Push notifications are a solved platform problem (use Firebase/APNs). Admin dashboards are taste-dependent (use the APIs directly with Swagger + Grafana). Bannou provides the primitives and integration hooks; specialist providers handle the commodity layer.

---

## The Principle: Build Primitives, Not Commodity Infrastructure

Bannou's architecture is built on composable primitives -- Seed, License, Status, Collection, Currency, Item, Inventory, Contract, Actor -- that combine to create emergent gameplay systems. This philosophy extends to infrastructure: Bannou builds what composes uniquely (the service mesh, event system, WebSocket gateway, behavior runtime) and delegates what is better maintained by specialists.

The test for whether something belongs in Bannou:

1. **Does it compose with other Bannou primitives to create emergent behavior?** If yes, build it.
2. **Is it a standalone commodity that specialists maintain better?** If yes, integrate it.
3. **Does it require domain expertise Bannou cannot sustain?** (ML models, security research, platform API maintenance) If yes, delegate it.

Every item below fails test 1 and passes tests 2 and 3.

---

## Anti-Cheat

### Why NOT to Build

Anti-cheat is an arms race. Dedicated solutions (EasyAntiCheat, BattlEye, Vanguard) employ teams whose sole purpose is detecting and countering new cheat methods. Building in-house anti-cheat means:

- Committing to constant updates as cheat methods evolve
- Maintaining client-side detection (kernel drivers, memory scanning) -- a fundamentally different engineering discipline from backend services
- Accepting that any in-house solution will always be behind dedicated providers

### What Bannou Provides Instead

Bannou's architecture inherently prevents many exploit categories:

| Defense | How It Works |
|---------|-------------|
| **Server-authoritative state** | All state changes go through service APIs with schema-enforced validation. Clients cannot directly modify server state. |
| **Permission system** | Per-session capability manifests prevent unauthorized API access. Clients only see endpoints their role allows. |
| **Idempotency keys** | Currency operations use deduplication to prevent double-spend exploits. |
| **Distributed locks** | Multi-instance safety prevents race condition exploits on balance operations. |
| **Anomaly detection** | Analytics (L4) ingests event streams -- unusual win rates, impossible action sequences, and statistical outliers can trigger investigation. |
| **Client-salted GUIDs** | Each client gets unique endpoint GUIDs, preventing cross-client replay attacks. |

For client-side cheat detection (aimbots, wallhacks, speed hacks), integrate a specialist provider. Bannou's Server SDK provides the hooks for game servers to validate client state against server-authoritative state.

---

## Replay System

### Why NOT to Build

Replay systems are the most game-specific feature imaginable:

- **State format varies per game**: A fighting game records input frames. An RTS records commands. An MMO records position snapshots. There is no generic "replay format."
- **Tied to game version**: A replay from version 1.2 may not play back correctly in version 1.3 if physics or ability values changed.
- **High storage requirements**: Long matches at high tick rates produce large recordings. Compression is game-specific (knowing which state changes matter).
- **Playback requires the game engine**: Server-side replay is just data; meaningful replay requires the game client to interpret it.

### What Bannou Provides Instead

| Need | Bannou Primitive |
|------|-----------------|
| State snapshots | **Save-Load** -- versioned state persistence with delta encoding (JSON Patch RFC 6902). Games can save state at tick boundaries and reconstruct sequences. |
| Post-game summaries | **Analytics** -- event ingestion, entity summaries, and score tracking. "What happened" without full replay. |
| Spatial data | **Mapping** -- 3D spatial indexing with high-throughput ingest. Position histories can be queried after the fact. |
| Event history | **lib-messaging** -- all state changes publish typed events. A game-specific replay service could subscribe and record. |

The recommended approach: build a game-specific replay service as an L5 extension plugin that subscribes to relevant events and stores recordings in a game-appropriate format via Save-Load or Asset.

---

## Game Server Hosting

### Why NOT to Build

Game server hosting is a fundamentally different problem from backend services:

- **Scale**: Hundreds to thousands of dedicated game server instances across global regions.
- **Latency**: Game servers need sub-10ms placement decisions based on player geography.
- **DDoS protection**: Game servers are prime DDoS targets requiring dedicated mitigation.
- **Authoritative logic**: Each game server runs game-specific physics, simulation, and rules. This is custom code, not a generic service.
- **Mature solutions exist**: Agones (Kubernetes-native), GameLift (AWS), Multiplay (Unity), and Hathora are battle-tested at scale.

### What Bannou Provides Instead

| Need | Bannou Primitive |
|------|-----------------|
| Service orchestration | **Orchestrator** (L3) -- manages Bannou service deployments, processing pools, and topology. |
| Service discovery | **Mesh** (L0) -- service-to-service routing with Redis-backed discovery. |
| Session management | **Game Session** (L2) -- lobby and matchmade session lifecycle with reservation tokens. |
| Worker pools | **Orchestrator processing pools** -- on-demand worker containers for NPC brains and computation. |
| Game transport | **Client SDK** -- LiteNetLib UDP transport with MessagePack serialization for authoritative game servers. |

Bannou handles the backend services (matchmaking, sessions, economy, NPCs). Game servers connect to Bannou via the Server SDK and handle the game-specific authoritative simulation. Agones or GameLift handle the game server provisioning and scaling.

---

## Content Moderation

### Why NOT to Build

Content moderation is a legal, ethical, and ML engineering challenge:

- **ML models require training data and constant tuning** -- toxicity detection drifts as language evolves.
- **Regional legal requirements vary** -- GDPR, COPPA, regional censorship laws, right-to-be-forgotten.
- **Appeal processes need dedicated UX** -- moderation without appeals is worse than no moderation.
- **Third-party services are mature** -- Perspective API (Google), OpenAI Moderation, and dedicated gaming moderation services have teams focused on this.

### What Bannou Provides Instead

| Need | Bannou Primitive |
|------|-----------------|
| Chat moderation | **Chat** (L1) -- participant kick/ban/mute, rate limiting via atomic Redis counters, room-level moderation. |
| Access control | **Permission** (L1) -- role-based endpoint access. Muted users can have chat endpoints removed from their capability manifest. |
| Reporting infrastructure | **Contract** (L1) -- moderation reports can be modeled as contracts with investigation milestones. |
| Audit trail | **Analytics** (L4) -- event ingestion provides a queryable record of player actions. |

For ML-based content filtering, integrate Perspective API or a similar service at the Chat service's message validation layer. For comprehensive moderation tooling, use a dedicated moderation platform (e.g., Community Sift) with Bannou providing the data via APIs.

---

## Push Notifications

### Why NOT to Build

Push notifications are a solved platform problem:

- Firebase Cloud Messaging (FCM) handles Android and web.
- Apple Push Notification Service (APNs) handles iOS.
- Both are free at scale and maintained by platform owners.
- Platform-specific APIs change frequently -- maintaining wrappers is pure cost.

### What Bannou Provides Instead

| Need | Bannou Primitive |
|------|-----------------|
| Real-time connected push | **Connect** (L1) -- WebSocket gateway pushes events to connected clients via per-session RabbitMQ subscriptions. |
| Typed event delivery | **IClientEventPublisher** -- services push typed events to connected clients without knowing transport details. |
| Offline state tracking | **Permission** (L1) -- session state tracks whether a user is connected, enabling "offline since" queries. |

For offline push: game clients integrate Firebase/APNs directly. A game-specific notification service (L5 extension) can subscribe to Bannou events and forward relevant ones to FCM/APNs for offline users.

---

## Admin Dashboard / CMS

### Why NOT to Build

Admin dashboards are taste-dependent:

- UI preferences vary significantly between teams (React vs Vue vs native).
- Web frontend technologies evolve rapidly -- a built-in dashboard becomes tech debt.
- Generic dashboards cannot anticipate game-specific admin needs (character editing, economy tuning, event triggering).
- An API-first architecture is more flexible than any pre-built UI.

### What Bannou Provides Instead

| Need | Bannou Primitive |
|------|-----------------|
| API documentation | **Swagger/OpenAPI** -- every service endpoint is documented and explorable. |
| Knowledge management | **Documentation** (L3) -- knowledge base with full-text search, git sync, and AI agent integration. |
| Monitoring dashboards | **Telemetry** (L0) -- OpenTelemetry export to Grafana/Prometheus for operational dashboards. |
| Public pages | **Website** (L3) -- registration, news, and status pages with traditional REST patterns. |
| Service management | **Orchestrator** (L3) -- deployment, topology, and health management via API. |

Teams build custom admin tools using generated service clients (available in .NET and TypeScript). The Server SDK provides typed access to every service endpoint. This is more powerful and maintainable than any generic admin UI.

---

## The Litmus Test for Commodity Features

Before proposing that Bannou build a commodity feature:

1. **Is there a specialist provider?** If yes, integration is cheaper and better maintained than building in-house.
2. **Is it game-specific?** If the implementation varies per game (replay format, cheat detection methods, moderation rules), Bannou's primitives are the right layer -- the game-specific logic belongs in the game or an L5 extension.
3. **Does it require ongoing specialist maintenance?** Security updates, ML model tuning, platform API changes, legal compliance -- if yes, a specialist provider will maintain it better.
4. **Does it compose with existing primitives?** If the proposed feature is isolated (doesn't interact with Seed, Contract, Actor, etc.), it is commodity infrastructure, not a Bannou primitive.

Bannou's value is in the composable primitives that create emergent gameplay -- not in reimplementing commodity infrastructure that exists elsewhere. The architecture provides the hooks (SDKs, APIs, events, WebSocket gateway) for integration with specialist providers. Build what composes; integrate what commoditizes.
