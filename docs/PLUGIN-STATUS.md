# Plugin Production Readiness Status

> **Last Updated**: 2026-02-22
> **Scope**: All Bannou service plugins

---

## What This Document Is

A quick-reference scorecard for every plugin's production readiness. Each entry contains:

- **Production Readiness Score** (0-100%): How close the plugin is to production-ready. 95% is the realistic ceiling (there's always something). 0% means schema-only stub with no implementation.
- **Bug Count**: Number of known bugs from the deep dive's "Bugs (Fix Immediately)" section.
- **Top 3 Bugs**: The most critical bugs, with descriptions and issue links.
- **Top 3 Enhancements**: The most impactful unimplemented features/extensions, with descriptions and issue links.
- **GH Issues Link**: A `gh` command to see all open issues for that plugin.

## What This Document Is NOT

This is **NOT** a code investigation tool. It reports the state depicted in each plugin's [deep dive document](plugins/) and the GitHub issue tracker -- nothing more. It does not discover new bugs, audit tenet compliance, or evaluate code quality. If you're looking for that, use `/audit-plugin`.

## How To Update This Document

1. Read the plugin's deep dive document in `docs/plugins/{PLUGIN}.md`
2. Check the **Bugs (Fix Immediately)** section for the bug count and top 3 (or bugs masquerading in other sections if empty)
3. Check the **Stubs & Unimplemented Features**, **Potential Extensions**, and **Design Considerations** sections for the top 3 enhancements
4. Run `make print-models PLUGIN="{service}"` if you need to verify model completeness
5. Glance at the configuration schema (`schemas/{service}-configuration.yaml`) if you need to verify config coverage
6. Update the score, bugs, and enhancements below
7. **If ONE source code file is read, the update task has failed and must start over.** Deep dives and schemas only.

## Sources

- **Deep Dives**: `docs/plugins/{PLUGIN}.md` -- the authoritative source for known bugs, quirks, stubs, and extensions
- **GitHub Issues**: `gh issue list --search "{Plugin}:" --state open` -- tracked work items
- **Configuration Schemas**: `schemas/{service}-configuration.yaml` -- config property coverage
- **Model Shapes**: `make print-models PLUGIN="{service}"` -- request/response model completeness

---

## Status Table

| Plugin | Layer | Score | Bugs | Summary |
|--------|-------|-------|------|---------|
| [State](#state-status) | L0 | 97% | 0 | L3-hardened. Schema NRT compliance, null-forgiving removal, config cleanup, layer comments fixed. 397 tests, 0 warnings. |
| [Messaging](#messaging-status) | L0 | 97% | 0 | L3-hardened. Dead letter consumer, IMeshInstanceIdentifier, shutdown timeout. 216 tests, 0 warnings. |
| [Mesh](#mesh-status) | L0 | 97% | 0 | L3-hardened. IMeshInstanceIdentifier canonical identity, dead fields removed, no extensions remaining. |
| [Telemetry](#telemetry-status) | L0 | 98% | 0 | L3-hardened. Self-instrumentation, schema fixes, null safety, tail-based sampling. 58 tests, 0 warnings. Only speculative extensions remain. |
| [Account](#account-status) | L1 | 95% | 0 | L3-hardened. Schema NRT, telemetry spans, lock safety, null coercion fixes. 111 tests, 0 warnings. |
| [Auth](#auth-status) | L1 | 95% | 0 | L3-hardened. Schema NRT, telemetry spans, atomic session indexing, email propagation. 168 tests, 0 warnings. |
| [Chat](#chat-status) | L1 | 97% | 0 | L3-hardened. All 30 endpoints, 4 background workers, dual storage, rate limiting, moderation. 0 bugs, 0 stubs. |
| [Connect](#connect-status) | L1 | 93% | 1 | L3-hardened gateway. Zero-copy routing, reconnection, multi-node broadcast. 1 orphaned config property. |
| [Contract](#contract-status) | L1 | 98% | 0 | L3-hardened. All 25 endpoints, 0 stubs. Expiration, payment schedules, clause execution all done. Only extensions remain. |
| [Permission](#permission-status) | L1 | 95% | 0 | L3-hardened RBAC. Heartbeat-driven session TTL, distributed locks, in-memory cache removed. 27 tests. Feature-complete. |
| [Resource](#resource-status) | L1 | 93% | 0 | Feature-complete lifecycle management. Reference tracking, cleanup, compression all done. |
| [Actor](#actor-status) | L2 | 65% | 0 | Solid core architecture. Auto-scale stubbed, many production features TODO. |
| [Character](#character-status) | L2 | 90% | 0 | All 12 endpoints done. Smart field tracking, resource compression. Only batch ops pending. |
| [Collection](#collection-status) | L2 | 78% | 4 | All 20 endpoints done. 4 bugs: grant bypasses limits, cleanup missing events, update ignores fields. |
| [Currency](#currency-status) | L2 | 85% | 0 | Production-hardened (7 bugs fixed, T25/T30 compliant). 8 stubs remain: hold expiration, currency expiration, analytics, pruning. |
| [Game Service](#game-service-status) | L2 | 100% | 0 | Production-hardened registry. All 5 endpoints done. T9/T21/T26/T28/T30 compliant. Resource cleanup on delete. |
| [Game Session](#game-session-status) | L2 | 92% | 0 | Production-hardened. Voice removed, lifecycle events live, T25/T26/T30 compliant. Distributed locks. |
| [Inventory](#inventory-status) | L2 | 85% | 0 | Production-hardened (T8/T25/T26/T29/T30 compliant, 93 tests). 4 stubs remain: grid collision, weight propagation, equipment slots, RemoveItem cleanup. |
| [Item](#item-status) | L2 | 92% | 0 | Production-hardened (T7/T8/T25/T29/T30 compliant, 70 tests). Dual-model + Itemize Anything. Decay system pending (#407). |
| [Location](#location-status) | L2 | 97% | 0 | All 24 endpoints done. Hierarchical management, spatial queries, presence tracking, ${location.*} variable provider. Hardened to L3. |
| [Quest](#quest-status) | L2 | 85% | 0 | Well-architected over Contract. Prerequisites, rewards, caching all done. Extensions only. |
| [Realm](#realm-status) | L2 | 95% | 0 | Fully feature-complete. Complex merge, deprecation, resource integration. No remaining gaps. |
| [Relationship](#relationship-status) | L2 | 90% | 0 | All 21 endpoints done. Bidirectional enforcement, type taxonomy, soft-delete. Index scaling gaps. |
| [Seed](#seed-status) | L2 | 88% | 0 | All 24 endpoints done. Growth, bonds, capabilities, decay worker. Archive cleanup needed. |
| [Species](#species-status) | L2 | 92% | 0 | All 13 endpoints done. Missing distributed locks on concurrent operations. |
| [Subscription](#subscription-status) | L2 | 88% | 0 | All 7 endpoints + expiration worker. Concurrency gaps and index cleanup needed. |
| [Transit](#transit-status) | L2 | 0% | 0 | Pre-implementation. Geographic connectivity graph, transit modes, and declarative journey tracking spec. No schema, no code. |
| [Worldstate](#worldstate-status) | L2 | 0% | 0 | Pre-implementation. Per-realm game clock, calendar system, and temporal event broadcasting spec. No schema, no code. |
| [Orchestrator](#orchestrator-status) | L3 | 58% | 0 | Compose backend works. 3/4 backends stubbed. Pool auto-scale/idle timeout missing. |
| [Asset](#asset-status) | L3 | 82% | 1 | Upload/download pipeline works. 2/3 processors stubbed, cleanup tasks missing. |
| [Documentation](#documentation-status) | L3 | 85% | 0 | All 27 endpoints done. Full-text search, git sync, archive. Semantic search pending. |
| [Voice](#voice-status) | L3 | 87% | 0 | P2P + SFU tiers work. WebRTC signaling, broadcast consent. Single RTP server limitation. |
| [Website](#website-status) | L3 | 5% | 0 | Complete stub. All 14 endpoints return NotImplemented. No state stores, no logic. |
| [Broadcast](#broadcast-status) | L3 | 0% | 0 | Pre-implementation. Aspirational streaming platform integration spec. No schema, no code. |
| [Agency](#agency-status) | L4 | 0% | 0 | Pre-implementation. Guardian spirit progressive agency and UX manifest engine spec. No schema, no code. |
| [Achievement](#achievement-status) | L4 | 75% | 1 | Core CRUD + auto-unlock work. Xbox/PS stubs, rarity calc broken, dead code. |
| [Analytics](#analytics-status) | L4 | 82% | 0 | Robust pipeline. Glicko-2 ratings, event ingestion, summaries. Rating decay missing. |
| [Behavior](#behavior-status) | L4 | 80% | 0 | ABML compiler + GOAP planner work. 6 stubs: cinematics, bundles, embeddings. |
| [Character Encounter](#character-encounter-status) | L4 | 88% | 0 | Feature-complete. Encounters, perspectives, decay, sentiment, pruning. Index growth concerns. |
| [Character History](#character-history-status) | L4 | 90% | 0 | Feature-complete. Participations, backstory, summarization, compression. Minor typing gaps. |
| [Character Lifecycle](#character-lifecycle-status) | L4 | 0% | 0 | Pre-implementation. Generational cycle orchestration and genetic heritage spec. No schema, no code. |
| [Character Personality](#character-personality-status) | L4 | 90% | 0 | Full evolution pipeline. Both variable providers work. Combat style transitions limited. |
| [Divine](#divine-status) | L4 | 25% | 0 | Aspirational. All 22 endpoints return NotImplemented. Detailed plan exists, zero code. |
| [Escrow](#escrow-status) | L4 | 70% | 1 | 13-state FSM works. Validation placeholder, custom handlers inert, status index broken. |
| [Environment](#environment-status) | L4 | 0% | 0 | Pre-implementation. Weather simulation, temperature modeling, and ecological resource availability spec. No schema, no code. |
| [Ethology](#ethology-status) | L4 | 0% | 0 | Pre-implementation. Species-level behavioral archetype registry and nature resolution spec. No schema, no code. |
| [Faction](#faction-status) | L4 | 80% | 0 | All 31 endpoints done. Seed-based growth, norms, territory. Obligation integration missing. |
| [Gardener](#gardener-status) | L4 | 62% | 0 | Void garden works. Broader garden concept unimplemented. No client events, no divine actors. |
| [Leaderboard](#leaderboard-status) | L4 | 78% | 0 | Redis Sorted Set rankings work. IncludeArchived stub, batch UpdateMode ignored. |
| [Lexicon](#lexicon-status) | L4 | 0% | 0 | Pre-implementation. Structured world knowledge ontology and concept decomposition spec. No schema, no code. |
| [License](#license-status) | L4 | 93% | 0 | Feature-complete. 14-step unlock saga, adjacency validation, board cloning. Only respec pending. |
| [Mapping](#mapping-status) | L4 | 80% | 2 | Spatial indexing works. Version counter race, non-atomic index ops. N+1 query pattern. |
| [Matchmaking](#matchmaking-status) | L4 | 73% | 1 | Core loop works. Queue stats all zeros, tournament stub, reconnect shortcut bug. |
| [Music](#music-status) | L4 | 88% | 0 | Full composition pipeline. Storyteller + MusicTheory SDKs. Custom style persistence missing. |
| [Obligation](#obligation-status) | L4 | 85% | 1 | Contract-aware cost modifiers work. Personality weighting operational. Hardcoded trait map bug. |
| [Puppetmaster](#puppetmaster-status) | L4 | 55% | 0 | Architecture designed. Watchers never spawn actors (core purpose stubbed). All state in-memory. |
| [Realm History](#realm-history-status) | L4 | 90% | 0 | Feature-complete. Participations, lore, summarization, compression. Same quality as Character History. |
| [Save-Load](#save-load-status) | L4 | 78% | 0 | Two-tier storage works. Delta saves, migration. Binary deltas stubbed, quota enforcement gap. |
| [Scene](#scene-status) | L4 | 82% | 0 | All 19 endpoints done. Checkout/commit workflow. No version content snapshots or real search. |
| [Status](#status-status) | L4 | 78% | 0 | All 16 endpoints done. Dual-source effects query. Blocked on Item decay system (#407). |
| [Storyline](#storyline-status) | L4 | 55% | 0 | SDK wrapper works for MVP. 3/15 endpoints done. No iterative composition or event integration. |
| [Affix](#affix-status) | L4 | 0% | 0 | Pre-implementation. Item modifier definition and generation spec. No schema, no code. |
| [Arbitration](#arbitration-status) | L4 | 0% | 0 | Pre-implementation. Dispute resolution orchestration spec. No schema, no code. |
| [Craft](#craft-status) | L4 | 0% | 0 | Pre-implementation. Recipe-based crafting orchestration spec. No schema, no code. |
| [Disposition](#disposition-status) | L4 | 0% | 0 | Pre-implementation. Emotional synthesis and aspirational drive spec. No schema, no code. |
| [Dungeon](#dungeon-status) | L4 | 0% | 0 | Pre-implementation. Dungeon-as-actor lifecycle orchestration spec. No schema, no code. |
| [Hearsay](#hearsay-status) | L4 | 0% | 0 | Pre-implementation. Social information propagation and belief formation spec. No schema, no code. |
| [Loot](#loot-status) | L4 | 0% | 0 | Pre-implementation. Loot table management and generation spec. No schema, no code. |
| [Market](#market-status) | L4 | 0% | 0 | Pre-implementation. Marketplace orchestration (auctions + NPC vendors) spec. No schema, no code. |
| [Organization](#organization-status) | L4 | 0% | 0 | Pre-implementation. Legal entity management spec. No schema, no code. |
| [Procedural](#procedural-status) | L4 | 0% | 0 | Pre-implementation. Houdini-based procedural 3D asset generation spec. No schema, no code. |
| [Showtime](#showtime-status) | L4 | 0% | 0 | Pre-implementation. In-game streaming metagame spec. No schema, no code. |
| [Trade](#trade-status) | L4 | 0% | 0 | Pre-implementation. Economic logistics orchestration with trade routes, shipments, tariffs, supply/demand dynamics, and NPC economic profiles spec. No schema, no code. |
| [Utility](#utility-status) | L4 | 0% | 0 | Pre-implementation. Infrastructure network topology, continuous flow calculation, coverage cascading, and maintenance lifecycle spec. No schema, no code. |
| [Workshop](#workshop-status) | L4 | 0% | 0 | Pre-implementation. Time-based automated production with lazy evaluation and worker scaling spec. No schema, no code. |
| [Common](#common-status) | N/A | N/A | 0 | Shared type definitions library. 0 endpoints. No deep dive document exists. |

---

## State {#state-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [STATE.md](plugins/STATE.md)

### Production Readiness: 97%

The bedrock of the entire platform. Provides unified Redis/MySQL/InMemory/SQLite state access to all 54 services via `IStateStoreFactory`. Manages ~107 state stores (~70 Redis, ~37 MySQL). Full interface hierarchy: `IStateStore<T>` (core CRUD), `ICacheableStateStore<T>` (sets, sorted sets, counters, hashes), `ISearchableStateStore<T>` (full-text via RedisSearch), `IQueryableStateStore<T>` / `IJsonQueryableStateStore<T>` (MySQL LINQ/JSON path queries), `IRedisOperations` (Lua scripts), `IDistributedLockProvider` (distributed mutex). Optimistic concurrency via ETags on all backends. Error event publishing with deduplication. No stubs, no bugs, no design considerations. 11 well-documented intentional quirks. L3-hardened (2026-02-22): schema NRT compliance (added `required` arrays to 7 response schemas), `additionalProperties` corrected on generic value objects, redundant nullable config removed, layer comments fixed in state-stores.yaml, null-forgiving operators replaced with safe `.ToString()` in Redis stores, LINQ expression tree null coalescing fixed in MySQL/SQLite stores, T0 violation fixed. 397 tests passing, 0 warnings.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Store migration tooling** | No mechanism to move data between Redis and MySQL backends without downtime. Needed for production scenarios where a store's backend needs to change (e.g., promoting ephemeral Redis data to durable MySQL). | [#190](https://github.com/beyond-immersion/bannou-service/issues/190) |
| 2 | *(No further enhancements identified)* | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "State:" --state open
```

---

## Messaging {#messaging-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [MESSAGING.md](plugins/MESSAGING.md)

### Production Readiness: 97%

Core pub/sub infrastructure is robust: RabbitMQ channel pooling (100 default, 1000 max), publisher confirms for at-least-once delivery, aggressive retry buffer with crash-fast philosophy (500k message / 10 minute threshold), backpressure at 80% buffer fill, dead-letter exchange with configurable limits, poison message handling with retry counting, HTTP callback subscriptions with recovery, event consumer fan-out bridge (`NativeEventConsumerBackend`), in-memory mode for testing, dead letter consumer with structured logging and error event publishing. 30+ configuration properties all wired. 0 stubs, 0 bugs. L3-hardened (2026-02-21): schema NRT compliance, validation constraints, enum consolidation, dead field removal, T9/T10/T25/T30 code fixes. Design considerations resolved (2026-02-22): `Program.ServiceGUID` replaced with `IMeshInstanceIdentifier` injection, shutdown timeout added (`ShutdownTimeoutSeconds`, default 10s). Dead letter consumer implemented (2026-02-22): `DeadLetterConsumerService` subscribes to DLX exchange, logs dead letters with metadata extraction, publishes `service.error` events, uses durable shared queue with competing consumers for multi-instance safety. 3 informational design notes remain (in-memory mode limitations, tap exchange auto-creation, publisher confirms latency). 1 extension identified (Prometheus metrics). 0 warnings, 216 tests passing.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Observable gauge metrics** | Retry buffer depth, per-attempt retry counts, and channel pool utilization not exposed as metrics. Requires `ObservableGauge` support in `ITelemetryProvider`. | [#453](https://github.com/beyond-immersion/bannou-service/issues/453) |
| 2 | *(No further enhancements identified)* | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Messaging:" --state open
```

---

## Mesh {#mesh-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [MESH.md](plugins/MESH.md)

### Production Readiness: 97%

Feature-complete service mesh: YARP-based HTTP routing, Redis-backed service discovery with TTL health tracking, 5 load balancing algorithms (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random), distributed per-appId circuit breaker with Lua-backed atomic state transitions and cross-instance sync via RabbitMQ, retry with exponential backoff, proactive health checking with automatic deregistration, degradation detection, event-driven auto-registration from Orchestrator heartbeats, endpoint caching, canonical `IMeshInstanceIdentifier` for node identity. 27 configuration properties all wired. No stubs, no bugs, no design considerations, no extensions remaining. All production readiness issues closed. L3-hardened (2026-02-21): schema NRT compliance, validation constraints, enum consolidation to `-api.yaml`, T23 async patterns, T26 sentinel value removal, T30 telemetry spans across all helper classes, T7 error event publishing, BuildServiceProvider anti-pattern removed. Dead field cleanup (2026-02-22): removed `lastUpdateTime` (always returned current time — useless), fixed `alternates` nullability (code always returns list, schema lied about nullable). `IMeshInstanceIdentifier` (2026-02-22): canonical mesh node identity with priority chain (env > CLI > random), `InstanceId` exposed on all generated clients and `IServiceNavigator`, replaced all `Program.ServiceGUID` usages. Graceful draining deemed unnecessary (Orchestrator's two-level routing handles managed deployments). 0 warnings, 55 tests passing.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | *(No enhancements identified)* | Graceful draining previously listed here was deemed unnecessary: Orchestrator's two-level routing model handles managed deployments by changing app-id mappings before stopping old nodes. | |
| 2 | | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Mesh:" --state open
```

---

## Account {#account-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [ACCOUNT.md](plugins/ACCOUNT.md)

### Production Readiness: 95%

L3-hardened. All CRUD operations complete with optimistic concurrency. OAuth/Steam account support with nullable email. Server-side paginated listing via MySQL JSON queries. Email change with distributed locking. Bulk operations (batch-get, count, bulk role update). Production hardening pass completed: schema NRT compliance (authMethods required, metadata descriptions, validation constraints), telemetry span instrumentation on all async helpers, `await using` lock disposal, null coercion fix in metadata conversion, state store constructor caching, log level audit. 111 unit tests covering all 18 endpoints, 0 warnings. Only post-launch extension remains (account merge).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Account merge workflow** | No mechanism to merge two accounts (e.g., email registration + OAuth under different email). Data model supports multiple auth methods, but no merge orchestration exists. Complex cross-service operation (40+ services reference accounts). Post-launch compliance feature. | [#137](https://github.com/beyond-immersion/bannou-service/issues/137) |
| 2 | *(No further enhancements identified)* | | |

### GH Issues

```bash
gh issue list --search "Account:" --state open
```

---

## Auth {#auth-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [AUTH.md](plugins/AUTH.md)

### Production Readiness: 95%

L3-hardened. Full authentication suite: email/password, OAuth (Discord/Google/Twitch), Steam tickets, JWT tokens, session management, password reset, login rate limiting, TOTP-based MFA with recovery codes, edge token revocation (CloudFlare/OpenResty). 46 configuration properties with full NRT compliance, validation bounds, and patterns. Schema inline enums extracted to named types. T30 telemetry spans on all 48 async methods across 7 helper services. T25 Provider enum properly threaded (no `Enum.Parse`). T9 atomic session indexing via Redis Set operations (eliminated read-modify-write races). Email change propagation implemented. 168 tests, 0 warnings. Remaining work is downstream integration: audit event consumers (Analytics L4) and account merge session handling (post-launch).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Audit event consumers** | Auth publishes 6 audit event types (login success/fail, registration, OAuth, Steam, password reset) but no service subscribes to them. Per-email rate limiting already exists via Redis counters -- the gap is Analytics (L4) consuming events for IP-level cross-account correlation, anomaly detection, and admin alerting. | [#142](https://github.com/beyond-immersion/bannou-service/issues/142) |
| 2 | **Account merge session handling** | When account merge is implemented, Auth needs to handle a new `account.merged` event: invalidate all sessions for the source account and optionally refresh target account sessions with merged roles. Auth's handler is straightforward; the merge itself is Account-layer orchestration. Low priority -- post-launch. | Auth-side of [#137](https://github.com/beyond-immersion/bannou-service/issues/137) |
| 3 | **Device capture for session info** | `DeviceInfo` on session records always returns "Unknown" placeholders. User-Agent parsing or client-provided device metadata needed for meaningful session listing. | [#449](https://github.com/beyond-immersion/bannou-service/issues/449) |

### GH Issues

```bash
gh issue list --search "Auth:" --state open
```

---

## Telemetry {#telemetry-status}

**Layer**: L0 Infrastructure (Optional) | **Deep Dive**: [TELEMETRY.md](plugins/TELEMETRY.md)

### Production Readiness: 98%

L3-hardened. Feature-complete OpenTelemetry tracing and Prometheus metrics with full instrumentation decorators for all infrastructure libs (state, messaging, mesh, telemetry). Schema issues fixed (events file, NRT annotations, OtlpProtocol enum typed). Null-forgiving operators eliminated. T23/T30 compliant (async pattern, self-instrumentation spans). Tail-based sampling implemented in OTEL Collector (100% error/high-latency retention, 10% probabilistic default). 58 tests, 0 warnings. Only speculative extensions remain (managed exporters, Grafana dashboards).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Managed platform exporters** | Datadog, Azure Application Insights, AWS X-Ray, Elastic APM exporters beyond base OTLP. | [#183](https://github.com/beyond-immersion/bannou-service/issues/183) |
| 2 | **Enhanced Grafana dashboards + SLO alerting** | Per-service dashboards, SLO alerting rules, error monitoring, automated provisioning. | [#185](https://github.com/beyond-immersion/bannou-service/issues/185) |
| 3 | **Metric aggregation views** | Custom histogram bucket boundaries optimized for Bannou latency distributions. | [#457](https://github.com/beyond-immersion/bannou-service/issues/457) |

### GH Issues

```bash
gh issue list --search "Telemetry:" --state open
```

---

## Chat {#chat-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [CHAT.md](plugins/CHAT.md)

### Production Readiness: 97%

All 30 API endpoints complete with no stubs and no bugs. Covers room type management, room lifecycle with contract governance, participant moderation (join/leave/kick/ban/mute with role hierarchy), message operations (send/batch/history/search/pin/delete) with dual storage (ephemeral Redis TTL and persistent MySQL), rate limiting via atomic Redis counters, typing indicators with sorted-set-backed expiry, 14 service events, 12 client events, 7 state stores (4 MySQL, 3 Redis), and 4 background workers (idle room cleanup, typing expiry, ban expiry, message retention). Hardened with: telemetry spans on all async helpers and event handlers, distributed lock on room type registration and idle room cleanup (T9), Regex timeout protection, schema validation keywords on all request fields, T16-compliant event topic naming, `required` string properties replacing `string.Empty` defaults, error event publication on startup failures, and ServiceHierarchyValidator test coverage. Two design considerations remain: O(N) participant counting in AdminGetStats (#455) and mixed data patterns in the participants Redis store (#456).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Message edit support** | Edit messages with version history tracking. | [#450](https://github.com/beyond-immersion/bannou-service/issues/450) |
| 2 | **Threaded replies** | Reply-to-message support via `replyToMessageId` field. | [#451](https://github.com/beyond-immersion/bannou-service/issues/451) |
| 3 | **Message reactions** | Emoji reactions per message with aggregated counts. | [#452](https://github.com/beyond-immersion/bannou-service/issues/452) |

### GH Issues

```bash
gh issue list --search "Chat:" --state open
```

---

## Connect {#connect-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [CONNECT.md](plugins/CONNECT.md)

### Production Readiness: 93%

L3-hardened. WebSocket connection lifecycle, zero-copy binary routing, reconnection windows, session shortcuts, client-to-client routing, three connection modes, per-session RabbitMQ subscriptions, and multi-node broadcast mesh (`InterNodeBroadcastManager`). Major L3 hardening pass (2026-02-22): fixed T9 thread safety (ConcurrentDictionary/ConcurrentQueue in ConnectionState), T26 Guid.Empty sentinels, T5 anonymous objects, T21 dead config wired (MaxChannelNumber), T7 bare catch blocks, T23 sync-over-async (.Wait()), T24 IDisposable/ClientWebSocket leak, T30 telemetry spans, XML docs. Schema consolidated (connect-shortcuts.yaml merged), enums extracted, format:uuid added, validation constraints added, T29 descriptions fixed. One bug remains: orphaned `CompanionRoomMode` config property (defined in schema but never referenced in code).

### Bug Count: 1

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Orphaned CompanionRoomMode config** | `CompanionRoomMode` is defined in the configuration schema and generated config class but never referenced in service code. T21 violation (dead config). | No issue |

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Orphaned CompanionRoomMode config** | Defined in schema but never referenced in code. | No issue |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Cross-instance P2P routing** | Peer-to-peer routing only works when both clients are on the same Connect instance. Requires cross-instance peer registry in Redis. | [#346](https://github.com/beyond-immersion/bannou-service/issues/346) |
| 2 | *(No further enhancements identified)* | Multi-instance broadcast was implemented via `InterNodeBroadcastManager`. | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Connect:" --state open
```

---

## Contract {#contract-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [CONTRACT.md](plugins/CONTRACT.md)

### Production Readiness: 98%

L3-hardened. Comprehensive feature set: template CRUD, instance lifecycle with full state machine (Draft through Fulfilled/Terminated/Expired), consent flows, milestone progression with deadline enforcement (hybrid lazy + background), breach handling with cure periods, guardian custody for escrow integration, clause type system with execution pipeline, prebound API batching, and idempotent operations. L1-to-L2 hierarchy violation (ILocationClient) removed, schema NRT/T25/T26/T29 compliance fixed, telemetry spans added to all async helpers. Contract expiration implemented: `ContractExpirationService` handles both effectiveUntil expiration and milestone deadline enforcement in a single background worker pass with lazy enforcement in `GetContractInstanceStatusAsync`. TemplateName denormalized onto instance model at creation time. Clause handler request/response mappings reclassified as Potential Extension. Payment schedule enforcement added: background worker publishes `ContractPaymentDueEvent` for one-time and recurring schedules with drift prevention. 0 stubs remain.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Clause type handler chaining** | Validate before executing -- currently clause execution is fire-and-forget with no pre-validation step. | [#458](https://github.com/beyond-immersion/bannou-service/issues/458) |
| 2 | **Template inheritance** | Templates extending other templates for shared clause/milestone patterns. | [#459](https://github.com/beyond-immersion/bannou-service/issues/459) |
| 3 | **Per-milestone onApiFailure flag** | Currently prebound API failures are always non-blocking. Adding per-milestone configuration for whether failure should block milestone completion. | [#246](https://github.com/beyond-immersion/bannou-service/issues/246) |

### GH Issues

```bash
gh issue list --search "Contract:" --state open
```

---

## Permission {#permission-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [PERMISSION.md](plugins/PERMISSION.md)

### Production Readiness: 95%

L3-hardened and feature-complete. All 8 endpoints are fully implemented with multi-dimensional permission matrix compilation, configurable role hierarchy, session state management, idempotent service registration, and real-time capability push to WebSocket clients via RabbitMQ. Major hardening pass (2026-02-22): fixed 14 schema NRT violations, removed T8 filler properties, moved inline enums to API schema, removed dead metadata field, added validation keywords, added `SessionLockTimeoutSeconds` config. Code fixes: added distributed locks for session state/role updates (T9), completed RoleHierarchy migration from hardcoded ROLE_ORDER (T21), added telemetry spans throughout (T30), removed duplicate try-catch (T7), fixed sentinel values (T26), extracted magic strings to constants (T13). Session cache invalidation implemented (#392): `ISessionActivityListener` DI listener pattern with heartbeat-driven TTL refresh, in-memory cache removed entirely, `SessionDataTtlSeconds` reduced from 86400 to 600. 27 unit tests, all passing. No bugs, no stubs, no extensions remaining.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | *(No enhancements identified)* | Service is feature-complete for its scope. Session cache invalidation (#392) was implemented via `ISessionActivityListener`. | |
| 2 | | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Permission:" --state open
```

---

## Resource {#resource-status}

**Layer**: L1 AppFoundation | **Deep Dive**: [RESOURCE.md](plugins/RESOURCE.md)

### Production Readiness: 93%

Feature-complete with no stubs, no bugs, and no active design considerations. All 17 endpoints are implemented across four subsystems: reference tracking (register/unregister/check/list), cleanup management (define/execute/list/remove with CASCADE/RESTRICT/DETACH policies), hierarchical compression (define/execute/decompress/list/archive-get with ALL_REQUIRED/BEST_EFFORT policies and priority ordering), and ephemeral snapshots (execute/get with TTL). Seeded resource loading via DI provider pattern is also complete. Six state stores are properly configured. Multiple L4 services are already integrated as consumers.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Batch reference unregistration** | Bulk entity deletion currently requires O(N) individual `UnregisterReferenceAsync` calls. A batch endpoint would reduce this to a single operation for services like character-history and character-encounter. | [#351](https://github.com/beyond-immersion/bannou-service/issues/351) |
| 2 | **Automatic cleanup scheduler** | Background service to periodically scan for resources past grace period and trigger cleanup (opt-in per resource type), rather than relying solely on caller-initiated cleanup. | [#276](https://github.com/beyond-immersion/bannou-service/issues/276) |
| 3 | **Per-resource-type cleanup policies** | Currently `DefaultCleanupPolicy` applies globally. Could add per-resource-type configuration via `DefineCleanupRequest` for finer-grained control. | [#275](https://github.com/beyond-immersion/bannou-service/issues/275) |

### GH Issues

```bash
gh issue list --search "Resource:" --state open
```

---

## Actor {#actor-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [ACTOR.md](plugins/ACTOR.md)

### Production Readiness: 90%

L3-hardened. Core architecture is solid and functional: template CRUD, actor spawn/stop/get lifecycle, behavior loop with two-phase tick execution (cognition pipeline + ABML behavior), ABML document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, encounter management, pool mode with command topics and health monitoring, Variable Provider Factory pattern (personality/combat/backstory/encounters/quest), behavior document provider chain (Puppetmaster dynamic + seeded + fallback), dynamic character binding (event brain → character brain without relaunch), periodic state persistence, character state update publishing. 30+ configuration properties all wired with validation keywords. Schema NRT compliance verified. T30 telemetry spans on ~80 async methods across 22 files. All T8 filler booleans removed from responses. Inline enums consolidated to shared types. ETag retry loops on all index operations. All disposal and lifecycle patterns correct. No implementation gaps.

Two known bugs (T29 violations: `cognitionOverrides` and `initialState` use `additionalProperties: true` but are deserialized to typed objects — both tracked with open issues). Significant production features remain unimplemented: auto-scale deployment mode is declared but stubbed, session-bound actors are stubbed, and 5 extensions (memory decay, cross-node encounters, behavior versioning, actor migration, Phase 2 variable providers) are open. The pool node capacity model is self-reported with no external validation.

### Bug Count: 2

Two T29 violations with open design issues.

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **T29: `cognitionOverrides` metadata bag** | Defined as `additionalProperties: true` but deserialized to typed `CognitionOverrides` with 5 discriminated subtypes. Should be a typed schema with `oneOf`/discriminator pattern. | [#462](https://github.com/beyond-immersion/bannou-service/issues/462) |
| 2 | **T29: `initialState` metadata bag** | Defined as `additionalProperties: true` but cast to `ActorStateSnapshot` with structured fields. Should define typed schema subset. | [#463](https://github.com/beyond-immersion/bannou-service/issues/463) |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Auto-scale deployment mode** | Declared as a valid `DeploymentMode` enum value but no auto-scaling logic is implemented. Pool nodes must be manually managed or pre-provisioned. This is essential for the 100,000+ concurrent NPC target -- manual pool sizing won't work at scale. | [#318](https://github.com/beyond-immersion/bannou-service/issues/318) |
| 2 | **Session-bound actors** | `HandleSessionDisconnectedAsync` is stubbed. Currently all actors are NPC brains that continue running when players disconnect. Future need: actors tied to player sessions that stop on disconnect (e.g., player-controlled summons, temporary companions). | [#191](https://github.com/beyond-immersion/bannou-service/issues/191) |
| 3 | **Actor migration** | No mechanism to move running actors between pool nodes without state loss. Required for production load balancing -- without it, hot nodes stay hot until actors naturally stop. Needs: state snapshot transfer, perception queue draining, subscription re-establishment on target node. | [#393](https://github.com/beyond-immersion/bannou-service/issues/393) |

### GH Issues

```bash
gh issue list --search "Actor:" --state open
```

---

## Character {#character-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [CHARACTER.md](plugins/CHARACTER.md)

### Production Readiness: 97%

L3-hardened. All 12 endpoints fully implemented with no stubs. Schema NRT compliance verified (3 critical, 7 major fixes applied). Event types properly located in events schema with uuid format and enum reason. Telemetry spans on all 13 async helpers. Configuration validation keywords on all properties. RefCountUpdateMaxRetries extracted from hardcoded constant to config. Post-review: fixed missing fields in CharacterCreatedEvent, corrected null vs empty list in CompressCharacterAsync, fixed referenceTypes description, removed L4-owned snapshot types from L2 schema (T29/T2). CRUD operations include smart field tracking, realm-partitioned storage with MySQL JSON queries, enriched retrieval with family tree data (from lib-relationship), and centralized compression via the Resource service. Distributed locking, optimistic concurrency, and lifecycle events all wired. Remaining: 2 design-phase extensions and 1 design consideration (batch ref unregistration).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Delete flow O(N) reference unregistration** | When a character is deleted, cleanup callbacks fire on 4 L4 services, each publishing individual `resource.reference.unregistered` events. For characters with rich data, this creates O(N) message bus traffic. A batch unregistration endpoint in lib-resource would reduce this to a single operation. | [#351](https://github.com/beyond-immersion/bannou-service/issues/351) |
| 2 | **Character purge background service** | Automated purge of characters eligible for cleanup (zero references past grace period). Config removed for T21 compliance; needs redesign when operational need arises. | [#263](https://github.com/beyond-immersion/bannou-service/issues/263) |
| 3 | **Batch compression** | Compress multiple dead characters in one operation via a batch variant of `/resource/compress/execute`. | [#253](https://github.com/beyond-immersion/bannou-service/issues/253) |

### GH Issues

```bash
gh issue list --search "Character:" --state open
```

---

## Collection {#collection-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [COLLECTION.md](plugins/COLLECTION.md)

### Production Readiness: 78%

All 20 endpoints implemented with working integration with lib-inventory and lib-item for the "items in inventories" pattern, DI-based unlock listener dispatch to Seed, and area content selection with weighted random themes. However, the plugin has 4 active bugs (template update ignoring fields, list ignoring request pageSize, grant bypassing max-collections limit, cleanup not publishing delete events) and 1 stub (isFirstGlobal always false). These bugs affect data integrity and schema contract adherence.

### Bug Count: 4

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **GrantEntryAsync bypasses MaxCollectionsPerOwner** | When auto-creating a collection during grant, the max-collections check is skipped, allowing unlimited collections via the grant path. | No issue |
| 2 | **Cleanup handlers don't publish collection.deleted** | Cascading deletions triggered by character.deleted / account.deleted never publish collection.deleted events, causing downstream consumers to miss cleanup signals. | No issue |
| 3 | **UpdateEntryTemplateAsync ignores fields** | `hideWhenLocked` and `discoveryLevels` fields are defined on the update request but never processed, making them immutable after creation despite the API contract. | No issue |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Global first-unlock tracking** | `isFirstGlobal` field on unlock events is always false. Requires a global set of unlocked entry codes per game service. Important for achievement triggers. | No issue |
| 2 | **Client events for real-time unlock notifications** | Define collection-client-events.yaml to push unlock and milestone events to connected WebSocket clients. | No issue |
| 3 | **Event-driven entry template cache invalidation** | When templates are updated or deleted, existing collection caches are not invalidated, serving stale data until TTL expires. | No issue |

### GH Issues

```bash
gh issue list --search "Collection:" --state open
```

---

## Currency {#currency-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [CURRENCY.md](plugins/CURRENCY.md)

### Production Readiness: 85%

Core currency operations are comprehensive and production-hardened after a thorough audit (2026-02-24): 7 bugs fixed (autogain race condition, metadata drop, sentinel values, TOCTOU, index failures), full T25 type safety (no Guid.Parse/ToString in helpers), T30 telemetry on all 25 async methods, schema NRT + validation compliance, dead code/config removed. Definitions, wallets, balance operations (credit/debit/transfer with idempotency and distributed locks), authorization holds (reserve/capture/release), exchange rate conversions, and escrow integration endpoints all work. The 8 remaining stubs are the gap to 100%: analytics endpoints return zeros, currency/hold expiration have no enforcement, global supply cap unchecked, item linkage unenforced, EarnCapResetTime ignored, transaction retention never deletes.

### Bug Count: 0

7 bugs found and fixed during 2026-02-24 audit. No known remaining bugs.

### Top 3 Bugs

*(None -- 7 fixed on 2026-02-24)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Hold expiration background task** | Expired holds remain Active and continue reducing effective balance indefinitely. A background task to auto-release expired holds and publish `currency.hold.expired` is critical for any production economy. | [#222](https://github.com/beyond-immersion/bannou-service/issues/222) |
| 2 | **Currency expiration** | Definition model has full expiration fields (Expires, ExpirationPolicy, ExpirationDate, ExpirationDuration, SeasonId) but no enforcement logic exists -- currencies never actually expire. | [#222](https://github.com/beyond-immersion/bannou-service/issues/222) |
| 3 | **Transaction pruning background task** | Transactions accumulate indefinitely in MySQL; `TransactionRetentionDays` is only enforced at query time. For a live economy with NPC-driven transactions, unbounded growth is a production risk. | No issue |

### GH Issues

```bash
gh issue list --search "Currency:" --state open
```

---

## Game Service {#game-service-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [GAME-SERVICE.md](plugins/GAME-SERVICE.md)

### Production Readiness: 100%

Production-hardened registry with all 5 CRUD endpoints fully implemented and all tenet violations resolved. L3 hardening pass (2026-02-24) addressed: T26 (Guid.Empty sentinels removed), T30 (telemetry spans on all async helpers), T9 (distributed lock on stub name uniqueness), T21 (retry count moved to config, dead code removed), T28 (lib-resource cleanup on delete with 409 Conflict). x-resource-lifecycle declared in schema. Deep dive updated with 5 previously missing dependents. Only speculative extensions remain (metadata validation, service versioning).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Service metadata schema validation** | The metadata field could support schema validation per service type, preventing invalid metadata from being stored. | [#228](https://github.com/beyond-immersion/bannou-service/issues/228) |
| 2 | **Service versioning** | Track deployment versions to inform clients of compatibility, enabling upgrade/migration workflows. | No issue |
| 3 | **Concurrency control on updates** | Last-writer-wins semantics on updates. Acceptable for admin-only low-frequency access, but distributed locking or ETag-based concurrency would prevent data loss in edge cases. | No issue |

### GH Issues

```bash
gh issue list --search "Game Service:" --state open
```

---

## Game Session {#game-session-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [GAME-SESSION.md](plugins/GAME-SESSION.md)

### Production Readiness: 92%

Production-hardened. Core lobby and matchmade session flows work end-to-end with subscription-driven shortcut publishing, reservation cleanup, and horizontal scaling by game. Voice hierarchy violation (L2→L3) fully removed — voice fields stripped from schema, models, and service code. Lifecycle events (`game-session.updated`, `game-session.deleted`) now published at all mutation points. All event models defined in schema (SessionCancelled client/server events). T25 type safety (enums, Guids throughout), T26 no sentinel values (nullable Guid returns), T30 telemetry spans on all async methods, T7 error handling (re-throw after rollback, TryPublishErrorAsync), T9 distributed locks on session-list and lobby creation races. Remaining gaps: actions endpoint is echo-only (no real processing), chat allows messages from non-members, single-key session list won't scale past ~1000 sessions.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Actions endpoint processing** | `PerformGameSessionAction` echoes back the action type without any real processing logic. Needs game-specific action handlers or delegation to a configurable action pipeline. | No issue |
| 2 | **Session list scalability** | All session IDs stored in a single Redis key (`game-session:sessions`). Read-modify-write under lock works for small counts but won't scale past ~1000 sessions. Needs Redis set or per-game partitioning. | No issue |
| 3 | **Chat non-member validation** | `SendChatMessage` does not validate that the sender is actually a member of the session before publishing the message. | No issue |

### GH Issues

```bash
gh issue list --search "Game Session:" --state open
```

---

## Inventory {#inventory-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [INVENTORY.md](plugins/INVENTORY.md)

### Production Readiness: 85%

Production-hardened with comprehensive tenet compliance. All 16 endpoints functional with distributed locking, cache-through patterns, and multi-constraint-model support (slot, weight, volumetric, grid, unlimited). L3 hardening pass applied: T8 filler removal from 7 response schemas, T26 sentinel fix (Guid.Empty -> null), T29 metadata disclaimers, T30 telemetry spans on all 8 async helpers, NRT compliance, event schema `additionalProperties: false`, x-lifecycle model completion, and validation keywords throughout. 93 unit tests (24 added). Four tracked stubs remain: grid collision approximation (#196), nested weight propagation (#226), equipment slot validation (#226), and RemoveItem cleanup (#164). 8 design considerations note performance issues (N+1 queries, serial deletion, in-memory pagination) and lack of lib-item event consumption.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **RemoveItem should clear item's ContainerId** | Currently leaves items referencing their former container after removal, creating a "limbo" state. | [#164](https://github.com/beyond-immersion/bannou-service/issues/164) |
| 2 | **Nested container weight propagation** | `WeightContribution` enum exists and is stored but parent container weight is never updated when child container contents change, breaking the weight system for nested inventories. | [#226](https://github.com/beyond-immersion/bannou-service/issues/226) |
| 3 | **True grid collision detection** | Grid containers approximate space with slot count only. Actual cell-based occupation tracking (SlotX/SlotY/Rotated/GridWidth/GridHeight) is not implemented, meaning grid inventories behave identically to slot-only containers. | [#196](https://github.com/beyond-immersion/bannou-service/issues/196) |

### GH Issues

```bash
gh issue list --search "Inventory:" --state open
```

---

## Item {#item-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [ITEM.md](plugins/ITEM.md)

### Production Readiness: 92%

Production-hardened (T7/T8/T25/T29/T30 compliant, 70 tests). The dual-model item system (templates + instances) is fully operational with robust CRUD, cache read-through patterns, quantity model enforcement, soulbound types, and the "Itemize Anything" contract-delegation pattern (ephemeral, session, and lifecycle bindings). The `/item/use` and `/item/use-step` endpoints enable arbitrary item behaviors via Contract service prebound APIs. Bulk loading optimizations are in place. Schema enums for EntityType, DestroyReason, UnbindReason replace former string fields. Telemetry spans on all 24 async helper methods. Filler properties removed from responses. Only minor gaps remain: deprecation without instance cascade, item decay/expiration ([#407](https://github.com/beyond-immersion/bannou-service/issues/407)), and post-fetch filtering on list queries.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Item Decay/Expiration** | Time-based item lifecycle (template-level decay config, instance `expiresAt`, background worker). Dependency for lib-status. | [#407](https://github.com/beyond-immersion/bannou-service/issues/407) |
| 2 | **Template migration on deprecation** | When deprecating with `migrationTargetId`, existing instances are not automatically upgraded to the new template. Admin must manage instances manually. | No issue |
| 3 | **Item Sockets** | Socket, linking, and gem placement system for item instances. Future L4 plugin. | [#430](https://github.com/beyond-immersion/bannou-service/issues/430) |

### GH Issues

```bash
gh issue list --search "Item:" --state open
```

---

## Location {#location-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [LOCATION.md](plugins/LOCATION.md)

### Production Readiness: 97%

All 24 endpoints are fully implemented with no stubs. Hierarchical location management with circular reference prevention, cascading depth updates, code-based lookups, bulk seeding with two-pass parent resolution, territory constraint validation for Contract integration, realm transfer, and deprecation lifecycle. Includes spatial coordinates with AABB queries (`/location/query/by-position`), entity presence tracking with TTL-based ephemeral Redis storage, a background cleanup worker, arrived/departed events, and a `${location.*}` variable provider for Actor behavior system. Hardened: T30 telemetry spans on all async helpers, T7 compliant error handling (no duplicate try-catch), T8 filler properties removed, T9 lock failures throw instead of silently returning, NRT-compliant schema with validation keywords, x-resource-lifecycle declared.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | *(No enhancements identified -- all prior items resolved)* | | |
| 2 | | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Location:" --state open
```

---

## Quest {#quest-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [QUEST.md](plugins/QUEST.md)

### Production Readiness: 85%

Well-architected orchestration layer over Contract with all previously-stubbed core features now implemented: full prerequisite validation (built-in L2 + dynamic L4 via IPrerequisiteProviderFactory), reward distribution via Contract prebound APIs, configurable quest data caching with event-driven invalidation, and Variable Provider Factory integration for Actor ABML expressions. All known bugs are fixed. The remaining gaps are pure extensions (quest chains, dynamic objectives, shared party progress) that are nice-to-haves rather than production blockers.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Quest chains** | Support for sequential quest chains where completing one quest unlocks the next. Currently requires manual prerequisite management per definition, which is functional but cumbersome for content designers. | No issue |
| 2 | **Dynamic objectives** | Objectives that change based on game state or player choices. The current model uses static objective definitions, limiting emergent narrative possibilities central to the content flywheel. | No issue |
| 3 | **Shared party progress** | Currently each character in a party quest has individual objective progress. Cooperative objectives with shared tracking would enable more engaging group content. | No issue |

### GH Issues

```bash
gh issue list --search "Quest:" --state open
```

---

## Realm {#realm-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [REALM.md](plugins/REALM.md)

### Production Readiness: 95%

Fully feature-complete with no stubs, no bugs, and no outstanding design considerations. All 12 endpoints are implemented including the complex three-phase merge operation (species, locations root-first, characters) with configurable page size, continue-on-failure policy, and optional post-merge deletion. Safe deletion via lib-resource integration is complete. Deprecation lifecycle with undeprecate reversal path works. Ten intentional quirks are well-documented. All prior potential extensions and design considerations have been resolved and closed.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | *(No enhancements identified -- all prior items resolved)* | | |
| 2 | | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "Realm:" --state open
```

---

## Relationship {#relationship-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [RELATIONSHIP.md](plugins/RELATIONSHIP.md)

### Production Readiness: 90%

Feature-complete with no stubs, no bugs, and all 21 endpoints fully implemented. Bidirectional uniqueness enforcement, hierarchical type taxonomy with merge and seed operations, soft-deletion with recreation, and lib-resource cleanup integration. The only gaps are potential extensions (relationship strength/weight, type constraints) and design considerations around in-memory filtering scalability and index cleanup, none of which block production use.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **In-memory filtering before pagination** | All list operations load the full index and bulk-fetch all models before filtering and paginating in memory. For entities with thousands of relationships, this loads everything into memory. | [#341](https://github.com/beyond-immersion/bannou-service/issues/341) |
| 2 | **No index cleanup** | Entity and type indexes accumulate relationship IDs indefinitely (both active and ended), growing large over time and requiring filtering on every query. | [#342](https://github.com/beyond-immersion/bannou-service/issues/342) |
| 3 | **Relationship strength/weight** | Numeric field for weighted relationship graphs -- important for NPC behavior systems that need weighted social networks for the living world vision. | [#335](https://github.com/beyond-immersion/bannou-service/issues/335) |

### GH Issues

```bash
gh issue list --search "Relationship:" --state open
```

---

## Seed {#seed-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [SEED.md](plugins/SEED.md)

### Production Readiness: 88%

Thoroughly implemented foundational primitive with no stubs and no bugs. All 24 endpoints are functional: seed CRUD with exclusive activation, growth recording with bond multipliers and cross-pollination, capability manifests with three fidelity formulas and debounced caching, typed definitions with deprecation lifecycle, bonds with ordered distributed locks and confirmation flow, and a background decay worker with per-type override support. The Collection-to-Seed growth pipeline works via ICollectionUnlockListener DI pattern, and Actor integration is complete via SeedProviderFactory. Remaining work is confined to extensions and design considerations.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **No cleanup of associated data on archive** | Archived seeds retain growth data, capability cache, and bond data indefinitely. Needs cleanup strategy -- immediate deletion, background retention worker, or lib-resource compression integration. | [#366](https://github.com/beyond-immersion/bannou-service/issues/366) |
| 2 | **Bond dissolution endpoint** | No endpoint exists to dissolve or break a bond, despite the `BondPermanent` flag implying some bonds should be dissolvable. Needed for pair system (twin spirits). | [#362](https://github.com/beyond-immersion/bannou-service/issues/362) |
| 3 | **Bond shared growth applied regardless of partner activity** | BondSharedGrowthMultiplier is applied even when the partner seed is dormant or archived, which may produce unintended growth acceleration. | [#367](https://github.com/beyond-immersion/bannou-service/issues/367) |

### GH Issues

```bash
gh issue list --search "Seed:" --state open
```

---

## Species {#species-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [SPECIES.md](plugins/SPECIES.md)

### Production Readiness: 92%

Feature-complete for its core scope: all 13 CRUD, deprecation, merge, realm association, and seed endpoints are implemented. Seed realm code resolution was fixed (2026-02-10). No bugs. Two design considerations remain open: no distributed locks on species operations (concurrent code creation could race) and merge without distributed lock on character list (new characters created during merge could be missed). Three speculative extensions (species inheritance, lifecycle stages, population tracking) would enrich the system but are not production blockers.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Distributed locks for species operations** | Concurrent create operations with the same code could race on the code index. Merge lacks locking on the character list, so characters created during merge could be missed. | [#373](https://github.com/beyond-immersion/bannou-service/issues/373) |
| 2 | **Species inheritance** | Parent species with trait modifier inheritance for subspecies, enabling shared base traits across related species. | [#370](https://github.com/beyond-immersion/bannou-service/issues/370) |
| 3 | **Lifecycle stages** | Age-based lifecycle stages (child, adolescent, adult, elder) with trait modifiers per stage, supporting the generational play system where characters age. | [#371](https://github.com/beyond-immersion/bannou-service/issues/371) |

### GH Issues

```bash
gh issue list --search "Species:" --state open
```

---

## Subscription {#subscription-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

### Production Readiness: 88%

Feature-complete with all 7 endpoints implemented and a background expiration worker with grace period, startup delay, and data integrity validation. No stubs, no bugs. However, three design considerations represent genuine production risks: no optimistic concurrency or distributed locks on subscription mutations (concurrent cancel+renew could produce inconsistent state), no locking on index read-modify-write operations (race conditions could lose index entries), and indefinite growth of account/service indexes with no cleanup mechanism. These concurrency gaps are more concerning here than in lower-frequency services because subscriptions have user-facing write operations.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Concurrency control on subscription operations** | Update, cancel, renew, and expire all perform read-modify-write without distributed locks or ETag concurrency. Two simultaneous operations could produce inconsistent state. | No issue |
| 2 | **Index cleanup for account and service indexes** | The expiration worker only cleans the global subscription-index. Account-subscriptions and service-subscriptions indexes grow indefinitely with cancelled/expired entries. | [#223](https://github.com/beyond-immersion/bannou-service/issues/223) |
| 3 | **Subscription deletion endpoint** | No endpoint exists to permanently delete subscription records. Combined with the index cleanup gap, subscription data accumulates forever. | No issue |

### GH Issues

```bash
gh issue list --search "Subscription:" --state open
```

---

## Transit {#transit-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [TRANSIT.md](plugins/TRANSIT.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a geographic connectivity and movement primitive that completes Location's spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree). Three core capabilities: a **mode registry** (string-coded transit modes like walking, horseback, wagon, teleportation -- registered via API, not hardcoded), a **connectivity graph** (typed edges between locations with distance, terrain, seasonal availability, and mode compatibility), and **declarative journeys** (temporal travel tracking computed against Worldstate's game clock, with depart/advance/arrive driven by the game, not auto-simulated). Route calculation uses Dijkstra's algorithm over the connection graph filtered by mode compatibility. DI-based cost enrichment via `ITransitCostModifierProvider` enables L4 services (Disposition, Environment, Faction) to affect travel costs without hierarchy violations. Placed at L2 so Actor, Quest, Game Session, and Workshop can all compute travel times. Specifies 22 planned endpoints, 5 state stores (2 MySQL, 2 Redis, 1 MySQL archive), 10 published events, 2 consumed events, 2 background workers (seasonal connection, journey archival), 1 variable provider namespace (`${transit.*}`), and 7 design considerations. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Core connectivity graph** | Create schemas, generate code, implement mode registry (CRUD with string codes), connection management (CRUD with terrain types, seasonal availability, bidirectionality), and connection graph caching in Redis for route calculation. | No issue |
| 2 | **Route calculation engine** | Implement Dijkstra-based route calculation over connection graph with mode filtering, seasonal availability, multi-mode journeys, and DI-based cost enrichment via `ITransitCostModifierProvider` for L4 behavioral modifiers. | No issue |
| 3 | **Variable provider (`${transit.*}`)** | Implement `TransitProviderFactory` for the `${transit.*}` ABML namespace, enabling NPC travel decisions in GOAP -- available modes, travel times to known locations, current journey status, and mode preference costs. | No issue |

### GH Issues

```bash
gh issue list --search "Transit:" --state open
```

---

## Worldstate {#worldstate-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [WORLDSTATE.md](plugins/WORLDSTATE.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a per-realm game clock, calendar system, and temporal event broadcasting service. Maps real-world time to configurable game-time progression (default 24:1 ratio: 1 real hour = 1 game day, ~2.6 real years = 1 saeculum). Provides calendar templates (configurable days, months, seasons, years as opaque strings), day-period cycles (dawn, morning, afternoon, evening, night), boundary event publishing at game-time transitions, piecewise time-ratio history for lazy evaluation, and the `${world.*}` variable namespace for ABML behavior expressions via the Variable Provider Factory pattern. Specifies 14 planned endpoints, 8 published events, 2 consumed events, 4 state stores, 11 configuration properties, 1 background worker, and a 5-phase implementation plan. Fills the "ghost clock" gap: dozens of existing services reference game time (`${world.time.period}` in ABML, `TimeOfDay` in Storyline, `inGameTime` in encounters, `seasonalAvailability` in trade routes) with no authoritative provider. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Calendar Infrastructure** | Create schemas, generate code, implement calendar template CRUD with structural validation (period coverage, season-month mapping, consistency checks). | No issue |
| 2 | **Phase 2 - Realm Clock Core** | Implement clock advancement background worker, boundary detection, event publishing, Redis cache for hot reads, and downtime catch-up with configurable policy (advance vs pause). | No issue |
| 3 | **Phase 4 - Variable Provider** | Implement `WorldProviderFactory` and `WorldProvider` for the `${world.*}` ABML namespace, filling the ghost clock gap referenced by ABML behaviors, Storyline, encounters, and trade routes. | No issue |

### GH Issues

```bash
gh issue list --search "Worldstate:" --state open
```

---

## Orchestrator {#orchestrator-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

### Production Readiness: 58%

The Docker Compose backend is functional and solid: preset-based deployment, live topology updates (add/remove/move/scale/update-env), service-to-app-id routing broadcasts consumed by Mesh, processing pool acquire/release with distributed locks, config versioning with rollback, health monitoring with source-filtered reports (control plane vs deployed vs all), container management (restart, status, logs), and infrastructure health checks. 25 configuration properties all wired. No bugs.

However, 3 of 4 container backends are stubs (Swarm, Kubernetes, Portainer -- only Compose is implemented). Processing pool management is missing auto-scaling (thresholds stored but no trigger), idle timeout enforcement (config stored but no timer), lease expiry enforcement (lazy reclamation only on next acquire), and queue depth tracking (hardcoded 0). Design consideration #4 notes the scoped service TTL cache is structurally ineffective. Design consideration #5 notes `_lastKnownDeployment` is in-memory state that would diverge across instances.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Processing pool auto-scaling** | Scale-up/down thresholds (`ScaleUpThreshold`, `ScaleDownThreshold`) are stored in pool config but no background service evaluates them. Pools cannot auto-scale -- only manual `ScalePool` API calls work. Critical for the 100k+ NPC target where actor pool demand is dynamic. | [#252](https://github.com/beyond-immersion/bannou-service/issues/252) (queue design) / [#318](https://github.com/beyond-immersion/bannou-service/issues/318) (actor auto-scale) |
| 2 | **Kubernetes/Swarm/Portainer backends** | Only Docker Compose is implemented. Swarm, Kubernetes, and Portainer backends are stubs (interface methods return NotImplemented or minimal responses). Required for any production deployment beyond single-machine dev. | No issue |
| 3 | **Lease expiry enforcement** | Expired processor leases are only reclaimed lazily during the next `AcquireProcessorAsync` call. No background timer proactively scans for expired leases. Pools with no acquire traffic will hold expired leases indefinitely, wasting worker containers. | No issue |

### GH Issues

```bash
gh issue list --search "Orchestrator:" --state open
```

---

## Asset {#asset-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [ASSET.md](plugins/ASSET.md)

### Production Readiness: 82%

Comprehensive and fully functional upload/download pipeline with pre-signed URL generation, bundle management (creation, versioning, soft-delete, resolution), streaming metabundle assembly, and a working audio processor (FFmpeg). However, two of three content processors (texture and model) are minimal stubs, two cleanup background tasks are missing (deleted bundle purge, ZIP cache cleanup), and two schema-defined events are never emitted. Core asset storage and bundle workflow is production-ready.

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Schema-code event mismatch** | `asset.processing.queued` and `asset.ready` events declared in `asset-events.yaml` are never published anywhere in service code, misleading downstream consumers. | [#227](https://github.com/beyond-immersion/bannou-service/issues/227) |
| 2 | *(No further bugs)* | | |
| 3 | | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Texture and Model Processors** | Both registered processors contain only validation logic with no actual format conversion or optimization. The AudioProcessor is the only fully functional processor. | [#227](https://github.com/beyond-immersion/bannou-service/issues/227) |
| 2 | **Deleted bundle cleanup background task** | `DeletedBundleRetentionDays` is configurable but no background task purges soft-deleted bundles past retention, causing indefinite accumulation. | No issue |
| 3 | **CDN integration** | Extend `StoragePublicEndpoint` rewriting to support CDN-fronted download URLs with cache invalidation, reducing direct MinIO load for frequently accessed assets. | No issue |

### GH Issues

```bash
gh issue list --search "Asset:" --state open
```

---

## Documentation {#documentation-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [DOCUMENTATION.md](plugins/DOCUMENTATION.md)

### Production Readiness: 85%

All 27 endpoints are implemented with working full-text search (Redis Search enabled), CRUD operations, repository binding with git sync, archive create/restore, trashcan with TTL, and two background services (index rebuild, sync scheduler). Functionally complete for its core knowledge base use case. Minor gaps remain: voice summary generation is simple text extraction rather than NLG, search index retains stale terms on document update, and the archive system has a reliability gap when the Asset Service is unavailable.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Semantic search with embeddings** | Implement vector embeddings for document content using Redis Vector Similarity Search (VSS) for true natural language queries, replacing keyword-based inverted index. | No issue |
| 2 | **Search index stale term cleanup** | `NamespaceIndex.AddDocument()` does not remove old terms on document update, causing searches for removed content to still return the document. | No issue |
| 3 | **Webhook-triggered sync** | Add webhook endpoint for git push notifications (GitHub/GitLab) to trigger immediate sync instead of waiting for the scheduler interval. | No issue |

### GH Issues

```bash
gh issue list --search "Documentation:" --state open
```

---

## Voice {#voice-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [VOICE.md](plugins/VOICE.md)

### Production Readiness: 87%

Core voice room lifecycle, P2P mesh topology, scaled SFU with automatic tier upgrade, WebRTC SDP signaling, broadcast consent flow with full privacy enforcement, participant heartbeat eviction, and all 11 API endpoints are fully implemented with no bugs. Production-ready for its current scope. Remaining gaps are primarily around RTP server pool allocation (single-server only), unused RTPEngine publish/subscribe methods, SIP credential expiration not being enforced server-side, and dependency on future services (lib-broadcast, lib-showtime).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **RTP server pool allocation** | `AllocateRtpServerAsync` returns the single configured RTPEngine server. Production deployments need multiple RTPEngine instances with load-based selection. | [#258](https://github.com/beyond-immersion/bannou-service/issues/258) |
| 2 | **SIP credential expiration enforcement** | Credentials have a 24-hour expiration timestamp but no server-side enforcement. No background task rotates credentials or invalidates sessions after expiry. | [#405](https://github.com/beyond-immersion/bannou-service/issues/405) |
| 3 | **Mute state synchronization** | `IsMuted` is tracked per participant but not synchronized across peers. Self-mute vs admin-mute distinction and SFU enforcement need design. | [#402](https://github.com/beyond-immersion/bannou-service/issues/402) |

### GH Issues

```bash
gh issue list --search "Voice:" --state open
```

---

## Website {#website-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [WEBSITE.md](plugins/WEBSITE.md)

### Production Readiness: 5%

Complete stub. All 14 endpoints return `NotImplemented` with zero business logic. No state stores defined, no service client dependencies injected, no configuration properties, no authentication context, no event schemas, and no meaningful test coverage. The schema and generated models exist, but nothing is implemented. Schema-only shell awaiting future development.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **CMS state store implementation** | Add website state stores to `state-stores.yaml` and implement the 7 CMS endpoints (pages CRUD, site settings, theme) with slug-based indexing and persistence. | No issue |
| 2 | **Service client integration** | Inject `IAccountClient` (L1) and `IHttpContextAccessor` to power the authenticated account profile endpoint with JWT claims extraction. | No issue |
| 3 | **Contact form with rate limiting** | Implement the contact form submission endpoint with Redis-backed rate limiting per IP/session and spam prevention. | No issue |

### GH Issues

```bash
gh issue list --search "Website:" --state open
```

---

## Agency {#agency-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [AGENCY.md](plugins/AGENCY.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. Three subsystems: **UX Module Registry** (definitions of available UI elements/interaction modes, their capability requirements, and fidelity curves), **Manifest Engine** (computes per-seed UX manifests from seed capabilities, caches in Redis, pushes updates when capabilities change), and **Influence Registry** (spirit influence types that the player can send to their possessed character, with compliance factors and rate limiting). Domains are opaque string codes (combat, crafting, social, trade, exploration, magic -- extensible without schema changes). Implements `IVariableProviderFactory` providing the `${spirit.*}` namespace for ABML behavior expressions (compliance base, available influences, manifest fidelity). Integrates with Disposition (L4, soft) for compliance computation and Gardener (L4, soft) for manifest push routing. Specifies 22 planned endpoints, 6 state stores, 6 published events, 3 consumed events, 13 configuration properties, 1 background worker (manifest recompute with debouncing), and a 5-phase implementation plan. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 2 - Manifest Engine** | Implement manifest computation from seed capabilities, Redis caching, manifest get/recompute/diff/history endpoints, `seed.capability.updated` event subscription, and ManifestRecomputeWorker with debouncing. | No issue |
| 2 | **Phase 3 - Influence System** | Implement influence register/update/get/list/delete, evaluate and execute endpoints, Redis rate limiting, and influence execution/rejection/resistance event publishing. | No issue |
| 3 | **Phase 4 - Variable Provider Factory** | Implement `SpiritProviderFactory` for the `${spirit.*}` ABML namespace, Disposition integration for compliance computation, and influence history tracking in Redis. | No issue |

### GH Issues

```bash
gh issue list --search "Agency:" --state open
```

---

## Achievement {#achievement-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

### Production Readiness: 75%

Core achievement CRUD, progress tracking, event-driven auto-unlock from Analytics/Leaderboard, prerequisite chains, rarity calculations (background service), and Steam platform sync are all implemented and functional. However, Xbox and PlayStation sync providers are stubs, per-entity sync history returns hardcoded zeros, TotalEligibleEntities is never populated (rarity calculations depend on it), and progressive platform sync is never called. The N+1 query pattern on every analytics/leaderboard event is a scalability concern.

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Dead code: GetAchievementProgressKey** | Method generates keys in a format not used anywhere in the codebase. Minor code bloat/confusion. | No issue |
| 2 | *(No further bugs)* | | |
| 3 | | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **TotalEligibleEntities automation** | The field is never written to; rarity calculations only work when manually populated. Subscribe to subscription/account lifecycle events to maintain accurate counts automatically. | No issue |
| 2 | **Xbox/PlayStation sync providers** | Both exist as stubs (`IsConfigured=false`, return "not implemented"). Configuration properties defined but unused. Required for cross-platform trophy parity. | No issue |
| 3 | **Per-entity sync history tracking** | `GetPlatformSyncStatusAsync` returns hardcoded zeros for synced/pending/failed counts. Needs a dedicated state store for accurate reporting. | No issue |

### GH Issues

```bash
gh issue list --search "Achievement:" --state open
```

---

## Analytics {#analytics-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ANALYTICS.md](plugins/ANALYTICS.md)

### Production Readiness: 82%

Core analytics pipeline is robust: buffered event ingestion with distributed-lock-protected flush, entity summary aggregation in MySQL with server-side filtering/sorting/pagination, full Glicko-2 skill rating implementation with configurable parameters, controller history with retention-based cleanup, 11 event subscriptions across game-session/character-history/realm-history, and resolution caching for cross-service lookups. No bugs. The main gaps are rating period decay (inactive player RD never increases) and milestones being global-only (no per-game customization).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Rating period decay scheduling** | Glicko-2 rating deviation should increase for inactive players over time. No scheduled task triggers it. Players who stop playing retain their last RD indefinitely, leading to overconfident ratings. | [#249](https://github.com/beyond-immersion/bannou-service/issues/249) |
| 2 | **Per-game milestone definitions** | Milestones are a single global comma-separated list. No API exists for game-specific or score-type-specific milestone thresholds. | No issue |
| 3 | **Automatic controller history cleanup** | The cleanup endpoint exists but must be called manually. No background service automatically purges expired records. | No issue |

### GH Issues

```bash
gh issue list --search "Analytics:" --state open
```

---

## Behavior {#behavior-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [BEHAVIOR.md](plugins/BEHAVIOR.md)

### Production Readiness: 80%

Core systems are substantial and functional: a multi-phase ABML compiler producing stack-based bytecode (30+ opcodes), A*-based GOAP planner with urgency-tiered parameters, 5-stage cognition pipeline, keyword-based memory store, behavior model caching with variant fallback chains, streaming composition with continuation points, and comprehensive domain action handlers. All 33 configuration properties are wired. No bugs. However, 6 stubs remain: bundle lifecycle events never published, cinematic extension delivery unimplemented, embedding-based memory store is future, GOAP plan persistence has no retrieval endpoint, compiler optimizations are placeholder, and bundle management lifecycle is partial. IAssetClient hard dependency violates the L3 soft-dependency pattern.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Cinematic extension delivery** | `CinematicExtensionAvailableEvent` and streaming composition opcodes exist but no code publishes the event or implements extension attachment. Critical for the Combat Dream vision of real-time choreographed cinematics. | No issue |
| 2 | **Embedding-based memory store** | `IMemoryStore` interface supports swappable implementations, but only keyword-based matching exists. Semantic similarity via vector embeddings would improve NPC memory relevance at 100k+ scale. | No issue |
| 3 | **Bundle lifecycle events** | Three lifecycle events (created, updated, deleted) are schema-defined and auto-generated but never published by `BehaviorBundleManager`. Breaks event-driven architecture for bundle consumers. | No issue |

### GH Issues

```bash
gh issue list --search "Behavior:" --state open
```

---

## Character Encounter {#character-encounter-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

### Production Readiness: 88%

Feature-complete with no stubs remaining and all 22 configuration properties wired. Encounter recording with duplicate detection, multi-participant perspective system, time-based memory decay (lazy and scheduled modes), weighted sentiment aggregation, configurable encounter type management, automatic pruning, compression support, Variable Provider Factory for `${encounters.*}`, and cache invalidation on all write paths. Three former bugs have been fixed. Remaining concerns are architectural: no transactionality across multi-write recording operations, lazy decay write amplification on read paths, and unbounded global character index growth.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Encounter sentiment aggregation caching** | Pre-compute and cache sentiment values per character pair to avoid O(N) computation on every GetSentiment call. | [#312](https://github.com/beyond-immersion/bannou-service/issues/312) |
| 2 | **Non-linear memory decay curves** | Support exponential/logarithmic decay via configurable decay function, allowing traumatic encounters to persist longer than casual ones. | [#314](https://github.com/beyond-immersion/bannou-service/issues/314) |
| 3 | **Location-based encounter proximity** | Integrate with location hierarchy for ancestor/descendant encounter queries ("encounters near here"). | [#313](https://github.com/beyond-immersion/bannou-service/issues/313) |

### GH Issues

```bash
gh issue list --search "Character Encounter:" --state open
```

---

## Character History {#character-history-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

### Production Readiness: 90%

Feature-complete with no stubs, no bugs, and all configuration properties wired. Participation tracking with dual-indexed CRUD, backstory management with merge semantics, template-based text summarization, compression/restoration support, ABML variable provider factory for `${backstory.*}` expressions, and distributed locking for all write operations. The only extension is batch reference unregistration (blocked on lib-resource), and two design considerations remain (untyped metadata as `object?`, and AddBackstoryElement lacking event distinction between add vs update).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Batch reference unregistration in DeleteAll** | Currently makes N individual `UnregisterReferenceAsync` API calls before bulk deletion. Blocked on lib-resource batch unregister endpoint. | [#351](https://github.com/beyond-immersion/bannou-service/issues/351) |
| 2 | **Typed metadata schema** | Participation metadata accepts any JSON structure via `object?`. No schema validation. Systemic issue affecting 14+ services; violates T25 type safety. | [#308](https://github.com/beyond-immersion/bannou-service/issues/308) |
| 3 | **AddBackstoryElement event distinction** | AddBackstoryElement with the same type+key silently replaces the existing element. No event distinguishes "added new" from "updated existing." | [#311](https://github.com/beyond-immersion/bannou-service/issues/311) |

### GH Issues

```bash
gh issue list --search "Character History:" --state open
```

---

## Character Lifecycle {#character-lifecycle-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-LIFECYCLE.md](plugins/CHARACTER-LIFECYCLE.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for generational cycle orchestration and genetic heritage -- the "ignition switch" for the content flywheel. Two complementary subsystems: **Lifecycle** (aging, marriage, procreation, death processing driven by worldstate year/season events) and **Heritage** (genetic trait inheritance with allele recombination, dominance models, mutation, phenotype expression, aptitude derivation, and bloodline tracking). Orchestrates across 12+ existing services: Character (L2) for CRUD, Relationship (L2) for bonds, Organization (L4) for households, Disposition (L4) for fulfillment calculation, Contract (L1) for marriage ceremonies, Resource (L1) for archive compression, Seed (L2) for guardian spirit growth, Character-Personality (L4) for trait seeding, Character-History (L4) for backstory, and Storyline (L4) for narrative generation from death archives. Specifies 28 planned endpoints, 5 state stores, 10+ published events, 5+ consumed events, 2 background workers (aging/pregnancy), 2 variable provider namespaces (`${lifecycle.*}`, `${heritage.*}`), and an 8-phase implementation plan (Phase 0 requires Worldstate, Organization Phase 5, and Disposition as prerequisites). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Lifecycle Templates and Profiles** | Create schemas, generate code, implement lifecycle template CRUD, profile management, basic aging via `worldstate.year-changed` events, stage transition detection, and resource cleanup/compression callbacks. | No issue |
| 2 | **Phase 3 - Procreation** | Implement fertility calculation, pregnancy tracking with expected birth dates, pregnancy worker (worldstate.day-changed triggered), full procreation flow (heritage computation, character creation, relationships, household, backstory seeding), and child limits. | No issue |
| 3 | **Phase 5 - Death Processing** | Implement fulfillment calculation from Disposition drives, guardian spirit contribution to Seed, archive compression trigger via Resource, inheritance processing, afterlife pathway determination, and content flywheel integration with Storyline. | No issue |

### GH Issues

```bash
gh issue list --search "Character Lifecycle:" --state open
```

---

## Character Personality {#character-personality-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

### Production Readiness: 90%

Feature-complete with no stubs, no bugs, and all 14 configuration properties wired. Full personality evolution pipeline (9 experience types), combat preference evolution (10 combat experience types), compression/restoration support for lib-resource, and both Variable Provider Factory implementations (personality and combat) registered and functional. Remaining gap is in design hardening: combat style transitions are limited and asymmetric (BERSERKER is a trap state), trait direction weights are hardcoded rather than configurable, and several desirable extensions are unimplemented.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Trait decay** | Gradual regression toward neutral (0.0) over time without reinforcing experiences, preventing permanently extreme personality values. | [#201](https://github.com/beyond-immersion/bannou-service/issues/201) |
| 2 | **Pre-defined archetype templates** | Template system mapping archetype codes to pre-configured trait combinations. The `archetypeHint` field exists and is persisted but no template system interprets it. | [#256](https://github.com/beyond-immersion/bannou-service/issues/256) |
| 3 | **Combat style full transition matrix** | Currently limited paths exist (no TACTICAL reversion, BERSERKER only exits via DEFEAT). A full transition matrix would prevent characters from getting trapped in combat modes. | [#264](https://github.com/beyond-immersion/bannou-service/issues/264) |

### GH Issues

```bash
gh issue list --search "Character Personality:" --state open
```

---

## Divine {#divine-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [DIVINE.md](plugins/DIVINE.md)

### Production Readiness: 25%

All 22 endpoints return `NotImplemented`. The deep dive is aspirational -- it thoroughly documents the intended architecture, composability model, dependencies, state stores, events, configuration (18 properties), and background workers, but zero business logic exists. The schema and plugin skeleton are in place with a detailed implementation plan at `docs/plans/DIVINE.md`, but no functional code has been written.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Implement deity CRUD operations** | All 8 deity management endpoints are stubbed. This is the foundational work that everything else depends on. | No issue |
| 2 | **Implement blessing orchestration** | All 5 blessing endpoints including dual-tier storage mechanism (Collection for permanent, Status for temporary) are stubbed. Primary consumer-facing feature. | No issue |
| 3 | **Variable Provider Factory for ABML** | `IDivineVariableProviderFactory` providing `${divine.*}` to Actor (L2) is listed as a potential extension. Required for NPC behavior to react to divine influence. | No issue |

### GH Issues

```bash
gh issue list --search "Divine:" --state open
```

---

## Disposition {#disposition-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [DISPOSITION.md](plugins/DISPOSITION.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "Pre-implementation. No schema, no code". A comprehensive architectural specification for emotional synthesis and aspirational drives -- what characters FEEL about specific entities (other characters, locations, factions, organizations, the guardian spirit) and what long-term goals DRIVE their behavior. Two complementary subsystems: Feelings (base + modifier model synthesizing encounters, personality, history, hearsay, and relationships into directed emotional states with persistent residue) and Drives (intrinsic aspirational goals with intensity/satisfaction/frustration dynamics that modulate GOAP goal priorities). Guardian spirit feelings provide the mechanical implementation of character independence (Design Principle 1). Provides `${disposition.*}` ABML variables via Variable Provider Factory. Integrates with Storyline for content flywheel (archived dispositions become narrative seeds). 17 planned endpoints, 10 published events, 9 consumed events, 4 state stores, 22 configuration properties, 3 background workers, and a 7-phase implementation plan.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Feeling Infrastructure** | Create schemas, generate code, implement feeling CRUD (record, get, query-by-target, get-composite), feeling cache with event-driven invalidation, variable provider factory (`${disposition.*}` namespace), and resource cleanup/compression callbacks. | No issue |
| 2 | **Phase 2 - Drive System** | Implement drive CRUD (set, get, evolve), backstory-seeded drive formation, personality-innate drive derivation, satisfaction/frustration dynamics, GOAP goal priority modulation, and drive intensity decay worker. | No issue |
| 3 | **Phase 4 - Guardian Spirit Relationship** | Implement guardian spirit feeling axes (trust, resentment, autonomy, defiance, gratitude), compliance computation affecting player directive responsiveness, and override-triggered feeling updates. | No issue |

### GH Issues

```bash
gh issue list --search "Disposition:" --state open
```

---

## Escrow {#escrow-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ESCROW.md](plugins/ESCROW.md)

### Production Readiness: 70%

The escrow coordination layer has an impressive 13-state FSM, token-based security, four escrow types, configurable release/refund modes with confirmation flows, two background services (expiration and confirmation timeout), contract integration via event subscriptions, idempotent deposits, and dispute resolution with arbiter support. However, significant gaps remain: `ValidateEscrow` is a placeholder that always passes (no actual cross-service asset verification), custom handler invocation is purely declarative (handlers registered but never called), periodic validation loop has no background processor, and there are no distributed locks around agreement modifications. Status index key pattern is structurally ineffective.

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Status index structurally ineffective** | Uses individual keys (`status:{status}:{escrowId}`) via `SaveAsync`/`DeleteAsync` instead of Redis Sets. Scanning by status requires `QueryAsync` against the agreement store, not the status index. | No issue |
| 2 | *(No further bugs)* | | |
| 3 | | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Cross-service asset validation** | `ValidateEscrowAsync` always passes. Needs actual calls to ICurrencyClient/IItemClient to verify deposited assets are still held. Without this, the "full custody" promise is incomplete. | [#213](https://github.com/beyond-immersion/bannou-service/issues/213) |
| 2 | **Periodic validation background processor** | `ValidationCheckInterval` (PT5M) is configured but no background service exists to trigger validation. `ValidationStore` tracks `NextValidationDue` but nothing reads it. | [#250](https://github.com/beyond-immersion/bannou-service/issues/250) |
| 3 | **Custom handler invocation pipeline** | Handlers are registered but the escrow service never invokes them. Registry is purely declarative. Implementing would enable plug-and-play support for arbitrary asset types. | No issue |

### GH Issues

```bash
gh issue list --search "Escrow:" --state open
```

---

## Environment {#environment-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ENVIRONMENT.md](plugins/ENVIRONMENT.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for an environmental state service providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot, and player experience. Uses deterministic weather: given realm + location + game-day + season, weather is hash-computed consistently across all nodes and restarts without per-instance storage. Supports divine weather manipulation via time-bounded overrides (storm gods, nature deities). Climate templates define per-biome seasonal patterns (temperature curves, weather distributions, precipitation). Three-layer condition resolution: climate template baseline + weather event overrides + deterministic location noise. Fills phantom references across the codebase: `${world.weather.temperature}` in ABML, `TtlWeatherEffects` in Mapping, seasonal availability in economy/trade, environmental overrides in Ethology. Specifies 20 planned endpoints, 5 state stores, 6+ published events, 3+ consumed events, 1 background worker, 1 variable provider namespace (`${environment.*}`), and a 7-phase implementation plan (Phase 0 requires Worldstate as hard prerequisite). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 2 - Deterministic Weather Resolution** | Implement `WeatherResolver` with hash-based deterministic weather from climate templates, weather segment duration/transition dampening, location tree inheritance, day-keyed caching, and `GetConditions` endpoint. | No issue |
| 2 | **Phase 3 - Temperature Computation** | Implement `TemperatureCalculator` with seasonal curve interpolation, altitude/depth modifiers, weather-based modifiers, deterministic location noise, and season transition smoothing. | No issue |
| 3 | **Phase 5 - Variable Provider** | Implement `EnvironmentProviderFactory` for the `${environment.*}` ABML namespace, filling the phantom `${world.weather.temperature}` variable references and enabling weather-aware NPC behavior decisions. | No issue |

### GH Issues

```bash
gh issue list --search "Environment:" --state open
```

---

## Ethology {#ethology-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ETHOLOGY.md](plugins/ETHOLOGY.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a species-level behavioral archetype registry and nature resolution service -- the missing middle ground between "every wolf is identical" (hardcoded ABML context defaults) and "full character cognitive stack" (8+ variable providers with per-entity persistent state). Provides three-layer nature resolution: species archetype (base behavioral template), environmental overrides (realm/location modifications), and deterministic individual noise (hash-based per-entity variation without per-entity storage). Supports 100,000+ creatures without per-entity state entries. Delegates to Character-Lifecycle's Heritage Engine when the entity is a character with genetic data, making `${nature.*}` a universal behavioral baseline that Heritage refines. Specifies 18 planned endpoints, 4 state stores, 4+ published events, 2+ consumed events, 1 background worker (cache warmup), 1 variable provider namespace (`${nature.*}`), and a 6-phase implementation plan (Phase 0 requires only existing Species service and Actor's IVariableProviderFactory pattern). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Archetype Definitions** | Create schemas, generate code, implement archetype CRUD with species-code binding, bulk seed endpoint for world initialization, species event handlers, and resource cleanup. | No issue |
| 2 | **Phase 2 - Nature Resolution** | Implement three-layer resolution algorithm (archetype + overrides + deterministic noise via MurmurHash3), `NatureProviderFactory` as `IVariableProviderFactory`, Redis caching, and ResolveNature/CompareNatures endpoints. | No issue |
| 3 | **Phase 4 - Character Delegation** | Implement Heritage-aware resolution: when entity is a character with Heritage data, use phenotype values for mapped axes (skipping noise), falling back to species archetype for unmapped axes. Graceful degradation when Character-Lifecycle unavailable. | No issue |

### GH Issues

```bash
gh issue list --search "Ethology:" --state open
```

---

## Faction {#faction-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [FACTION.md](plugins/FACTION.md)

### Production Readiness: 80%

All 31 endpoints are fully implemented with business logic -- CRUD, membership management, territory claims, norm definitions, cleanup, and compression all work. The seed-based growth pipeline, norm resolution hierarchy, `ISeedEvolutionListener`/`ICollectionUnlockListener` DI integrations, and `IVariableProviderFactory` (`${faction.*}` namespace) are operational. However, the variable provider is missing critical norm/territory variables, lib-contract integration for guild charters is absent, and the lib-obligation integration (the primary consumer) is not yet wired.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Missing norm/territory variables in Variable Provider** | Provider only exposes membership data. `${faction.has_norm.<type>}`, `${faction.norm_penalty.<type>}`, `${faction.in_controlled_territory}`, `${faction.primary_faction}` are missing -- critical for lib-obligation's cognition stage. | [#410](https://github.com/beyond-immersion/bannou-service/issues/410) |
| 2 | **Missing lib-contract integration for guild charters** | Plan specifies guild membership should create contracts with behavioral clauses. Current implementation manages membership directly without contract backing, breaking the faction-to-obligation pipeline. | [#410](https://github.com/beyond-immersion/bannou-service/issues/410) |
| 3 | **Faction diplomacy system** | Formalized alliance/rivalry mechanics through seed bonds with capability-gated treaty operations. Schema references seed bonds but no API endpoints exist for faction-level bond management. | [#413](https://github.com/beyond-immersion/bannou-service/issues/413) |

### GH Issues

```bash
gh issue list --search "Faction:" --state open
```

---

## Gardener {#gardener-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [GARDENER.md](plugins/GARDENER.md)

### Production Readiness: 62%

The void/discovery garden type is functional with all 24 endpoints implemented -- garden lifecycle, POI interaction with weighted scoring, scenario management with growth awards, template CRUD, phase management, bond features, and two background workers. However, the broader garden concept is unimplemented: no garden-to-garden transitions, no multiple garden types, no per-garden entity associations, no entity session registry, no divine actor integration (uses background workers instead of per-player actors), 10 implementation gaps in the current void garden (missing prerequisite validation, no per-template concurrent instance limits, MinGrowthPhase not functional, no client events, Puppetmaster notification is log-only), and no content flywheel integration.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Client event schema** | No client events exist for real-time POI push to WebSocket clients. POI spawns, expirations, and triggers happen server-side only. Clients must poll to discover changes. | No issue |
| 2 | **Prerequisite validation during scenario entry** | Templates store prerequisites but they are never validated in `EnterScenarioAsync` or `GetEligibleTemplatesAsync`. A player can enter any scenario regardless. | No issue |
| 3 | **Entity Session Registry** | Cross-cutting infrastructure for mapping entities to WebSocket sessions, hosted in Connect (L1). Required for real-time client event routing from entity-based services. | [#426](https://github.com/beyond-immersion/bannou-service/issues/426) |

### GH Issues

```bash
gh issue list --search "Gardener:" --state open
```

---

## Hearsay {#hearsay-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [HEARSAY.md](plugins/HEARSAY.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "Pre-implementation. No schema, no code". A comprehensive architectural specification for social information propagation and belief formation -- what NPCs *think* they know vs. what is objectively true. Three belief domains (norms, characters, locations) acquired through six information channels (direct observation, official decree, trusted/social contact, rumor, cultural osmosis) with confidence mechanics, time-based decay, proximity-based convergence toward ground truth, and rumor injection for divine manipulation. Provides `${hearsay.*}` ABML variables via Variable Provider Factory. Integrates with Storyline for dramatic irony detection (belief vs. reality deltas). 18 planned endpoints, 8 published events, 8 consumed events, 4 state stores, 19 configuration properties, 3 background workers, and a 6-phase implementation plan.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Belief Infrastructure** | Create schemas, generate code, implement belief CRUD (record, correct, query, get-manifest), belief cache with event-driven invalidation, variable provider factory (`${hearsay.*}` namespace), and resource cleanup/compression callbacks. | No issue |
| 2 | **Phase 2 - Propagation Engine** | Implement encounter-triggered belief propagation, faction event-driven belief injection (territory claimed, norm defined), rumor injection API, propagation worker advancing rumor waves through social networks, and telephone-game distortion mechanics. | No issue |
| 3 | **Phase 4 - Storyline Integration** | Implement the dramatic irony endpoint (`QueryBeliefDelta` -- beliefs alongside ground truth with narrative weight classification), belief saturation queries for scenario preconditions, and narrative protection preventing convergence from breaking active storylines. | No issue |

### GH Issues

```bash
gh issue list --search "Hearsay:" --state open
```

---

## Leaderboard {#leaderboard-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LEADERBOARD.md](plugins/LEADERBOARD.md)

### Production Readiness: 78%

Core leaderboard functionality is solid: Redis Sorted Set-backed rankings with O(log N) operations, polymorphic entity types, four score update modes, seasonal rotation, event-driven score ingestion from Analytics, percentile calculations, and neighbor queries. No bugs remain (the archivePrevious bug was fixed). However, `IncludeArchived` filtering returns NotImplemented, batch submit ignores UpdateMode, season timestamps are approximated, an unused MySQL state store exists in the schema, and no score validation/bounds exist (NaN and infinity accepted).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **IncludeArchived filtering** | `ListLeaderboardDefinitions` returns NotImplemented when `IncludeArchived=true`. Archived leaderboard tracking is mentioned but not implemented. | [#232](https://github.com/beyond-immersion/bannou-service/issues/232) |
| 2 | **Batch submit UpdateMode compliance** | `SubmitScoreBatch` always uses Replace mode regardless of the leaderboard's configured UpdateMode, creating inconsistency with individual `SubmitScore`. | No issue |
| 3 | **Per-season timestamps** | Season start/end dates use approximations (definition CreatedAt and UtcNow). No actual per-season timestamp tracking exists for historical queries. | No issue |

### GH Issues

```bash
gh issue list --search "Leaderboard:" --state open
```

---

## Lexicon {#lexicon-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LEXICON.md](plugins/LEXICON.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a structured world knowledge ontology that defines what things ARE in decomposed, queryable characteristics -- the missing NPC world-knowledge layer. Four interconnected pillars: **entries** (things that can be known about: species, objects, phenomena, individuals), **traits** (decomposed observable characteristics: four_legged, pack_hunter, fur), **categories** (hierarchical classification: canine < quadruped_mammal < mammal < animal), and **associations** (bidirectional concept links with asymmetric strength and discovery-tier gating). Also defines **strategies** (trait/category-derived implications for GOAP: "ways to escape a wolf"). Part of a three-service knowledge stack: Lexicon (ground truth) + Collection (discovery tracking) + Hearsay (subjective belief). Discovery-gated via Collection's `discoveryLevels` -- a character only accesses Lexicon data matching their discovery tier for that entry. Specifies 23 planned endpoints, 3 state stores, 5+ published events, 2+ consumed events, 1 variable provider namespace (`${lexicon.*}`), and a 7-phase implementation plan (Phase 0 requires only existing Collection and Actor services). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Ontology Infrastructure** | Create schemas, generate code, implement category tree management (CRUD, reparenting, depth tracking, circular reference prevention), entry management with category assignment, entry manifest computation and caching. | No issue |
| 2 | **Phase 4 - Variable Provider** | Implement `LexiconProviderFactory` for the `${lexicon.*}` ABML namespace with Collection discovery-level gating, per-character caching, and perception-triggered demand loading per perceived entity. | No issue |
| 3 | **Phase 6 - GOAP Integration** | Define strategy-to-GOAP-action mapping conventions, implement strategy viability checking against current world state, threat level computation from trait composition, and document ABML patterns for Lexicon-informed behavior. | No issue |

### GH Issues

```bash
gh issue list --search "Lexicon:" --state open
```

---

## License {#license-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LICENSE.md](plugins/LICENSE.md)

### Production Readiness: 93%

Feature-complete with zero stubs and zero bugs. All 20 endpoints are fully implemented including the sophisticated 14-step unlock flow with saga compensation, distributed locking, adjacency validation, contract integration for LP deduction, board cloning for NPC tooling, and cleanup via lib-resource. Seven configuration properties are all wired. The only identified gap is a single potential extension (board reset/respec) which requires game design decisions rather than engineering work.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Board reset/respec** | Allow owners to reset all unlocked licenses on a board, returning LP and removing items. Requires game design decisions on refund percentage, partial vs full reset, and cooldown. | [#356](https://github.com/beyond-immersion/bannou-service/issues/356) |
| 2 | *(No further enhancements identified)* | | |
| 3 | | | |

### GH Issues

```bash
gh issue list --search "License:" --state open
```

---

## Mapping {#mapping-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [MAPPING.md](plugins/MAPPING.md)

### Production Readiness: 80%

Core functionality is solid -- authority-based channel ownership, spatial indexing with 3D queries, affordance scoring, event aggregation, and authoring workflows are all implemented across 18 endpoints. However, two active bugs (version counter race condition, non-atomic index operations), several meaningful stubs (no MapSnapshotEvent publishing, no large payload support, basic custom affordance scoring), and significant design considerations (N+1 query pattern across all spatial queries, no index compaction, hardcoded affordance scoring weights) prevent a higher score.

### Bug Count: 2

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Version counter race condition** | `IncrementVersionAsync` performs non-atomic read-increment-write, producing duplicate version numbers under concurrent `accept_and_alert` mode publishes. | No issue |
| 2 | **Non-atomic index operations** | Spatial, type, and region index read-modify-write on Redis lists can lose objects when concurrent requests modify the same cell. | No issue |
| 3 | *(No further bugs)* | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **N+1 spatial query optimization** | All spatial queries load objects individually from Redis -- pipelining or MGET would significantly reduce round-trips for large result sets. | No issue |
| 2 | **Spatial index TTL alignment** | Map objects have per-kind TTLs but their index entries never expire, accumulating stale entries until explicit deletion. | No issue |
| 3 | **MapSnapshotEvent publishing** | Events schema defines `MapSnapshotEvent` and its topic but `RequestSnapshot` never broadcasts a snapshot event to consumers. | [#208](https://github.com/beyond-immersion/bannou-service/issues/208) |

### GH Issues

```bash
gh issue list --search "Mapping:" --state open
```

---

## Matchmaking {#matchmaking-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [MATCHMAKING.md](plugins/MATCHMAKING.md)

### Production Readiness: 73%

The core matchmaking loop works: ticket creation, background queue processing with skill window expansion, match formation, accept/decline flow with distributed locks, reconnection support, game session creation, and join shortcut publishing. However, queue statistics are entirely placeholder (all zeros), tournament support is declared but unimplemented, a defined event type is never published, and there is a real bug where reconnecting players do not receive accept/decline shortcuts.

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Reconnection does not republish accept/decline shortcuts** | When a player reconnects to a pending match, `MatchFoundEvent` is sent but `PublishMatchShortcutsAsync` is not called. The player has no way to accept or decline. | No issue |
| 2 | *(No further bugs)* | | |
| 3 | | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Queue statistics computation** | `MatchesFormedLastHour`, `AverageWaitSeconds`, `MedianWaitSeconds`, `TimeoutRatePercent`, `CancelRatePercent` are all placeholder zeros. Critical for operational visibility. | [#225](https://github.com/beyond-immersion/bannou-service/issues/225) |
| 2 | **Tournament support** | `TournamentIdRequired` and `TournamentId` fields exist on tickets but no tournament-specific matching logic is implemented. | No issue |
| 3 | **Skill rating integration** | Currently requires skill ratings in the join request. Could fetch Glicko-2 ratings from Analytics automatically, reducing client-side burden. | No issue |

### GH Issues

```bash
gh issue list --search "Matchmaking:" --state open
```

---

## Music {#music-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [MUSIC.md](plugins/MUSIC.md)

### Production Readiness: 88%

Fully functional composition pipeline: the Generate endpoint produces complete compositions via the Storyteller and MusicTheory SDKs with harmony, melody, voice leading, and MIDI-JSON output. All 8 endpoints work except CreateStyle which does not persist. Deterministic seed-based caching is operational. All configuration tunables have been externalized. The only meaningful gaps are the unpersisted custom styles (CreateStyle is a stub, MySQL store declared but unused) and the lack of rate limiting on CPU-intensive generation.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Persistent custom styles** | Implement CreateStyle to actually save to the declared MySQL `music-styles` store and merge with built-in styles on load. Currently custom styles are lost after the response. | [#188](https://github.com/beyond-immersion/bannou-service/issues/188) |
| 2 | **Rate limiting on generation** | Composition generation is CPU-intensive (Storyteller + Theory + Rendering pipeline). No protection against burst requests exhausting compute resources. | [#206](https://github.com/beyond-immersion/bannou-service/issues/206) |
| 3 | **Multi-instrument arrangement** | Extend MIDI-JSON output to support multiple instrument tracks with orchestration rules for richer compositions. | [#202](https://github.com/beyond-immersion/bannou-service/issues/202) |

### GH Issues

```bash
gh issue list --search "Music:" --state open
```

---

## Obligation {#obligation-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [OBLIGATION.md](plugins/OBLIGATION.md)

### Production Readiness: 85%

All 11 endpoints are fully implemented with complete business logic. Contract-driven obligation extraction, personality-weighted cost computation, violation reporting with idempotency, event-driven cache management (listening to contract lifecycle events), and the `IVariableProviderFactory` (`${obligations.*}` namespace) all work end-to-end. The two-layer design (standalone with raw penalties, enriched with personality when available) is operational. One bug exists (hardcoded personality trait mapping). Remaining work is integration connections (cognition stage in Actor, faction norm flow, post-violation feedback loops).

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Hardcoded personality trait mapping** | A static dictionary (10 entries) maps violation types to personality traits. Conflicts with violation types being opaque strings -- any new type silently falls through to a default, degrading moral reasoning quality. Should be data-driven or configurable. | [#410](https://github.com/beyond-immersion/bannou-service/issues/410) |
| 2 | *(No further bugs)* | | |
| 3 | | | |

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Cognition stage integration (`evaluate_consequences`)** | A 6th cognition stage in the Actor behavior pipeline, opt-in via `conscience: true` ABML metadata. Key remaining piece for the "second thoughts" feature to be active in NPC behavior. | [#410](https://github.com/beyond-immersion/bannou-service/issues/410) |
| 2 | **Faction-to-contract bridge** | Faction norms need to flow into obligation through automatic contract creation when characters join/leave factions. Without this, obligation only sees explicitly authored contracts, not ambient social/cultural norms. | [#410](https://github.com/beyond-immersion/bannou-service/issues/410) |
| 3 | **Post-violation emotional feedback** | When violations occur, downstream personality drift, encounter memories, and divine attention changes should ripple through multiple systems. `obligation.violation.reported` event publishes full context; no consumers wired. | No issue |

### GH Issues

```bash
gh issue list --search "Obligation:" --state open
```

---

## Puppetmaster {#puppetmaster-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [PUPPETMASTER.md](plugins/PUPPETMASTER.md)

### Production Readiness: 55%

The architectural skeleton is well-designed: behavior document caching and provider chain integration, resource snapshot caching for Event Brain actors, ABML action handlers (load_snapshot, prefetch_snapshots, spawn/stop/list_watchers, watch/unwatch), a watch system with dual-indexed registry and dynamic lifecycle event subscriptions, and realm event handling for watcher lifecycle. Configuration is fully wired (5 properties). No bugs. However, the most critical feature -- watcher-actor integration -- is stubbed: watchers are registered as data structures in memory but never spawn actual actors (ActorId is always null). Without this, regional watchers, divine actors, and encounter coordinators cannot execute behavior. All state is in-memory only (lost on restart), and the multi-instance story is broken.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Watcher-Actor integration** | ActorId on WatcherInfo is always null. Watchers do not spawn actual actors, meaning no behavior execution occurs. This is the core purpose of the service and blocks regional watchers, divine actors, and encounter coordinators. | [#388](https://github.com/beyond-immersion/bannou-service/issues/388) |
| 2 | **Distributed watcher state** | All watcher state is in-memory ConcurrentDictionary, lost on restart. Multi-instance deployments have divergent state. Needs Redis/MySQL persistence and recovery-on-startup. | [#395](https://github.com/beyond-immersion/bannou-service/issues/395) |
| 3 | **Watcher health monitoring** | No mechanism to track watcher execution health, restart failed watchers, or expose operational metrics. Required before production deployment of autonomous regional watchers. | [#398](https://github.com/beyond-immersion/bannou-service/issues/398) |

### GH Issues

```bash
gh issue list --search "Puppetmaster:" --state open
```

---

## Realm History {#realm-history-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [REALM-HISTORY.md](plugins/REALM-HISTORY.md)

### Production Readiness: 90%

Feature-complete with all 12 endpoints implemented, no stubs, and no bugs. Shares storage helper abstractions with character-history (DualIndexHelper, BackstoryStorageHelper), has proper distributed locking, resource cleanup/compression integration, and server-side paginated queries. Participation tracking with dual-indexed CRUD, lore management with merge semantics, template-based text summarization, compression/restoration support, ABML variable provider factory for realm-scoped behavior expressions. Same quality and maturity as Character History.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Event-level aggregation** | Compute aggregate impact scores per event across all participating realms. | [#266](https://github.com/beyond-immersion/bannou-service/issues/266) |
| 2 | **AI-powered summarization** | Replace template-based summaries with LLM-generated narrative text. Blocked on shared LLM infrastructure from character-history. | [#269](https://github.com/beyond-immersion/bannou-service/issues/269) |
| 3 | **Typed metadata schemas** | Replace `object?`/`additionalProperties:true` metadata pattern with typed schemas. Systemic issue affecting 14+ services. | [#308](https://github.com/beyond-immersion/bannou-service/issues/308) |

### GH Issues

```bash
gh issue list --search "Realm History:" --state open
```

---

## Save-Load {#save-load-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [SAVE-LOAD.md](plugins/SAVE-LOAD.md)

### Production Readiness: 78%

The architecture is comprehensive and well-designed: two-tier storage (Redis hot cache + async MinIO upload), delta saves with JSON Patch, schema migration with BFS path discovery, circuit breaker, export/import, rolling cleanup, distributed locking, and 40+ configuration properties. No bugs. However, several meaningful stubs reduce readiness: two delta algorithms (BSDIFF/XDELTA) throw NotSupportedException, JSON Schema validation is a no-op, per-owner storage quota enforcement only checks per-slot, thumbnails have config but no implementation, and conflict detection flags are not surfaced to clients.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Per-owner storage quota enforcement** | `MaxTotalSizeBytesPerOwner` only checks the current slot's size, not aggregate across all owner slots -- multi-slot owners can exceed the limit. | No issue |
| 2 | **Binary delta algorithms (BSDIFF/XDELTA)** | Both throw NotSupportedException despite being listed as supported. Needed for binary game state where JSON Patch is inappropriate. | [#193](https://github.com/beyond-immersion/bannou-service/issues/193) |
| 3 | **JSON Schema validation** | `SchemaMigrator.ValidateAgainstSchema` only verifies data is valid JSON, not conformance to the registered JSON Schema (draft-07). | [#229](https://github.com/beyond-immersion/bannou-service/issues/229) |

### GH Issues

```bash
gh issue list --search "Save-Load:" --state open
```

---

## Scene {#scene-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [SCENE.md](plugins/SCENE.md)

### Production Readiness: 82%

All 19 endpoints are implemented with solid structural validation, checkout/commit workflow with optimistic concurrency, reference resolution with circular detection, and secondary indexing. No bugs. However, version-specific content retrieval is a no-op (only latest content stored), soft-delete has no recovery mechanism despite the schema describing one, the SceneCheckoutExpiredEvent is never published, and search is brute-force global scan. Multiple design considerations (unbounded global index, secondary index race conditions, YAML serialization performance, no content versioning) also apply.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Version content snapshots** | Only version metadata is retained -- actual YAML content per version is not preserved, making version-specific retrieval and rollback impossible. | [#187](https://github.com/beyond-immersion/bannou-service/issues/187) |
| 2 | **Background checkout expiry** | No background service monitors stale checkouts or publishes `scene.checkout.expired` events; expiry is only checked lazily on next checkout attempt. | [#254](https://github.com/beyond-immersion/bannou-service/issues/254) |
| 3 | **Full-text search with proper indexing** | Current `SearchScenes` is a brute-force global index scan loading all scene IDs; needs Redis Search or equivalent for sub-millisecond queries at scale. | No issue |

### GH Issues

```bash
gh issue list --search "Scene:" --state open
```

---

## Status {#status-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [STATUS.md](plugins/STATUS.md)

### Production Readiness: 78%

All 16 API endpoints are fully implemented -- template CRUD with seeding, grant flow with 5 stacking behaviors, remove/cleanse operations, unified effects query merging item-based and seed-derived effects, and resource cleanup. The dual-source architecture is operational with lib-divine as the first consumer. However, a blocking dependency on #407 (Item Decay/Expiration System) means TTL-based status expiration relies on lazy cleanup during cache rebuilds rather than proactive event-driven expiration. Missing features include no variable provider factory for ABML, no client events, no cache warming, and hardcoded `EntityType.Character` breaking polymorphic entity support.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Item Decay/Expiration System** | Blocking dependency. Without `item.expired` events from lib-item, TTL-based status expiration relies on lazy cleanup during cache rebuild. Combat buffs and divine blessings may appear active after expiry. | [#407](https://github.com/beyond-immersion/bannou-service/issues/407) |
| 2 | **Variable Provider Factory for ABML** | `IStatusVariableProviderFactory` providing `${status.has_buff.<code>}`, `${status.is_dead}`, `${status.active_count}` is critical. Without it, NPCs cannot react to active effects in ABML behavior logic. | No issue |
| 3 | **Hardcoded EntityType.Character in contract ops** | Both `CreateNewStatusInstanceAsync` and `RemoveInstanceInternalAsync` hardcode Character as the party entity type. Breaks polymorphic entity support for non-character entities. | [#415](https://github.com/beyond-immersion/bannou-service/issues/415) |

### GH Issues

```bash
gh issue list --search "Status:" --state open
```

---

## Storyline {#storyline-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [STORYLINE.md](plugins/STORYLINE.md)

### Production Readiness: 55%

The core composition endpoint works -- it fetches archives, builds actant assignments, calls the SDK's GOAP planner, calculates confidence/risk scores, caches plans, and publishes events. However, this is a thin wrapper around two SDKs with only 3 of 15 total service endpoints implemented (compose, get plan, list plans), four meaningful stubs (ContinuePhase not exposed, entitiesToSpawn always null, links always null, no event subscriptions), no consumers of its events yet, and several design considerations. The service is functional for MVP composition but lacks the iterative generation, entity spawning, and event integration needed for the content flywheel.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Streaming/iterative composition (ContinuePhase)** | The SDK supports multi-phase iterative generation but no HTTP endpoint exposes it -- critical for long-running narrative compositions by regional watchers. | No issue |
| 2 | **EntitiesToSpawn population** | The response field is always null; callers cannot know what entities need to be created to instantiate the storyline plan. | No issue |
| 3 | **Event-driven archive discovery** | Subscribing to `resource.compressed` events would allow automatic composition when new character archives become available, closing the content flywheel loop. | No issue |

### GH Issues

```bash
gh issue list --search "Storyline:" --state open
```

---

## Affix {#affix-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [AFFIX.md](plugins/AFFIX.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." This is a detailed architectural specification for an item modifier definition and generation service (weighted random affix generation, mod group exclusivity, tier gating, stat computation, variable provider factory for NPC GOAP) based on the Path of Exile item system as a complexity benchmark. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Definition Infrastructure** | Create schemas, generate code, implement affix definition CRUD and implicit mapping management as the foundational layer. | No issue |
| 2 | **Phase 2 - Generation Engine** | Implement weighted random pool generation with cached pools for 100K NPC item evaluation scale. | No issue |
| 3 | **Phase 3 - Affix Application** | Implement validated apply/remove/reroll primitives with mod group rules, slot limits, and item state checks. | No issue |

### GH Issues

```bash
gh issue list --search "Affix:" --state open
```

---

## Arbitration {#arbitration-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ARBITRATION.md](plugins/ARBITRATION.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." A detailed architectural specification describing a dispute resolution orchestration layer (like Quest over Contract), with 23 planned endpoints across 6 groups, 14 published events, 6 consumed events, 6 state stores, and a 6-phase implementation plan. Depends on prerequisites that do not yet exist (Faction sovereignty `authorityLevel` field, Obligation multi-channel costs). No schema file, no generated code, and no service implementation exist.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Faction Sovereignty (Phase 0 prerequisite)** | Add `authorityLevel` enum to Faction for jurisdiction determination -- without this, Arbitration cannot function at all. | No issue |
| 2 | **Core Arbitration Infrastructure (Phase 2)** | Create schemas, generate code, implement case management CRUD with jurisdiction resolution and contract template integration. | No issue |
| 3 | **Precedent System** | Accumulated rulings per case type form case law that NPC arbiters reference in cognition, creating emergent legal tradition and content flywheel material. | No issue |

### GH Issues

```bash
gh issue list --search "Arbitration:" --state open
```

---

## Broadcast {#broadcast-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [BROADCAST.md](plugins/BROADCAST.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." A detailed architectural specification for the privacy boundary between external streaming platforms (Twitch, YouTube) and internal Bannou services, with 20 planned endpoints across 6 groups, 8 published events, 4 consumed events, 6 state stores, 30 configuration properties, 6 background workers, and a 5-phase implementation plan.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Platform Session Management (Phase 3)** | Implement Twitch EventSub/YouTube webhook handlers, sentiment processor for converting raw chat to anonymous sentiment pulses, and tracked viewer management. | No issue |
| 2 | **Broadcast Management (Phase 4)** | Implement FFmpeg-based RTMP broadcast coordination with fallback cascade, health monitoring, and voice room consent integration. | No issue |
| 3 | **Sentiment Processing Sophistication** | Upgrade from keyword/emoji matching to lightweight NLP for more nuanced sentiment categorization via the swappable `ISentimentProcessor` interface. | No issue |

### GH Issues

```bash
gh issue list --search "Broadcast:" --state open
```

---

## Craft {#craft-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CRAFT.md](plugins/CRAFT.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a recipe-based crafting orchestration service covering production (material-to-item), modification (lib-affix operations), and extraction (item destruction for components), with Contract-backed multi-step sessions, seed-integrated proficiency tracking, station/tool validation, quality formulas, and recipe discovery. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Recipe Infrastructure** | Create schemas, generate code, implement recipe definition CRUD as the foundation for all crafting workflows. | No issue |
| 2 | **Phase 2 - Session Management (Production)** | Implement Contract-backed crafting sessions with material validation, step advancement, and item creation. | No issue |
| 3 | **Phase 4 - Proficiency and Discovery** | Implement seed-backed proficiency tracking and recipe discovery system for NPC economic behavior. | No issue |

### GH Issues

```bash
gh issue list --search "Craft:" --state open
```

---

## Common {#common-status}

**Layer**: N/A (shared type definitions, not a runtime service) | **Deep Dive**: None (no deep dive exists)

### Production Readiness: N/A

Common is not a service plugin in the traditional sense. It is a shared type definitions library (`common-api.yaml`) with 0 endpoints, providing system-wide consistency for cross-service concepts. No deep dive document (`docs/plugins/COMMON.md`) exists. Common's "production readiness" is a function of whether its shared type definitions compile and are correctly consumed by other services, which is implicitly validated by the fact that 53 other services build successfully against it.

### Bug Count: 0 (unknown -- no deep dive document exists)

### Top 3 Bugs

*(None identifiable -- no deep dive document exists)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Create a deep dive document** | Common lacks a `docs/plugins/COMMON.md` deep dive, making it impossible to track known quirks, type coverage gaps, or planned shared type additions in a structured format. | No issue |
| 2 | **Type coverage audit** | Evaluate whether all cross-service concepts that should be shared types are properly consolidated in `common-api.yaml` rather than duplicated across individual service schemas. | No issue |
| 3 | **Shared type consumer documentation** | Document which services consume each shared type to understand the blast radius of changes and prevent accidental breaking changes. | No issue |

### GH Issues

```bash
gh issue list --search "Common:" --state open
```

---

## Dungeon {#dungeon-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [DUNGEON.md](plugins/DUNGEON.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. An exceptionally detailed architectural specification for a dungeon lifecycle orchestration service implementing the "dungeon-as-actor" vision -- living dungeon entities with dual mastery patterns (full split vs bonded role), seed-based progressive growth, mana economy via Currency, Contract-backed master bonds, memory capture/manifestation, and integration with Actor, Puppetmaster, Gardener, Mapping, Scene, and Save-Load. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Infrastructure** | Create schemas, generate code, implement dungeon core CRUD with Seed/Currency/Actor provisioning and variable provider factories. | No issue |
| 2 | **Phase 2 - Dungeon Master Bond** | Implement Contract-backed bond formation flow with master seed creation and perception injection via character Actor. | No issue |
| 3 | **Phase 4 - Memory System** | Implement memory capture with significance scoring and physical manifestation (items, scene decorations, environmental effects) gated by seed capability. | No issue |

### GH Issues

```bash
gh issue list --search "Dungeon:" --state open
```

---

## Loot {#loot-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LOOT.md](plugins/LOOT.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A comprehensive architectural specification for a loot table management and generation service with hierarchical weighted tables, three generation tiers (lightweight preview, standard instance, enriched with affixes), five distribution modes (personal, need/greed, round-robin, free-for-all, leader-assign), pity counter system, context-sensitive modifiers, sub-table composition, and integration with the content flywheel for archive-seeded dynamic tables. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Infrastructure** | Create schemas, generate code, implement table definition CRUD with sub-table cycle detection and cache warming. | No issue |
| 2 | **Phase 2 - Generation Engine** | Implement the full generation pipeline (roll count, guaranteed entries, weighted pool, context modifiers, quantity curves) with Tier 1/2 support. | No issue |
| 3 | **Phase 4 - Distribution** | Implement all five distribution modes (personal, need/greed, round-robin, free-for-all, leader-assign) with claiming and declaration endpoints. | No issue |

### GH Issues

```bash
gh issue list --search "Loot:" --state open
```

---

## Market {#market-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [MARKET.md](plugins/MARKET.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." A marketplace orchestration service with two subsystems (auction houses and NPC vendor catalogs), 28 planned endpoints across 6 groups, 14 published events, 1 consumed event, 9 state stores, 22 configuration properties, 3 background workers, and 2 Variable Provider Factories. Depends on lib-escrow completing asset movement operations (Phase 0 prerequisite).

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Bidding & Settlement (Phase 2)** | Implement bid placement with Currency hold reservation, outbid flow, buyout, and the MarketSettlementService background worker for expired auction settlement. | No issue |
| 2 | **Variable Provider Integration (Phase 5)** | Implement `${market.*}` and `${market.price.*}` ABML variable namespaces enabling NPC vendors to make autonomous GOAP-driven pricing and restocking decisions. | No issue |
| 3 | **Vendor Negotiation API** | Expose a `/market/vendor/negotiate` endpoint for dynamic haggling where the vendor's ABML behavior decides to accept, counter-offer, or refuse buyer proposals. | No issue |

### GH Issues

```bash
gh issue list --search "Market:" --state open
```

---

## Organization {#organization-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ORGANIZATION.md](plugins/ORGANIZATION.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." The largest planned service in this batch, describing a legal entity management service with 36 planned endpoints across 8 groups, 15 published events, 8 consumed events, 7 state stores, 15 configuration properties, 2 background workers, 1 Variable Provider Factory, and a 7-phase implementation plan. Has extensive dependency requirements (12 hard, 5 soft) and depends on Faction sovereignty as a prerequisite.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Core Organization Infrastructure (Phase 1)** | Create schemas, generate code, implement organization CRUD that provisions seed + wallet + inventory, member management, role definitions, and asset registration. | No issue |
| 2 | **Household Pattern (Phase 5)** | Implement households as organizations with family-specific succession modes (primogeniture, matrilineal), lifecycle integration with character events (marriage, coming of age), and dissolution via arbitration. | No issue |
| 3 | **Payroll and Recurring Expenses** | Background worker for periodic salary payments and recurring expenses (rent, tax), with delinquent payroll triggering obligation events and charter breach. | No issue |

### GH Issues

```bash
gh issue list --search "Organization:" --state open
```

---

## Procedural {#procedural-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [PROCEDURAL.md](plugins/PROCEDURAL.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation (architectural specification)" with "No schema, no code" and "Everything is unimplemented." A thorough specification and feasibility study for on-demand Houdini-based procedural 3D asset generation, with template management, synchronous/async generation via containerized Houdini workers, content-addressed caching, batch generation, and integration with the content flywheel to generate physical content (terrain changes, buildings, dungeon chambers) alongside narrative content. No schema file, no generated code, and no service implementation exist.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1-2: Core service creation** | Create OpenAPI schema, generate service code, implement template registration with HDA introspection, synchronous generation with Asset upload, and generation cache with deduplication. | No issue |
| 2 | **Async job queue with batch generation** | Implement asynchronous generation pipeline with job status tracking, batch generation for multiple parameter sets, and metabundle creation for batch outputs. | No issue |
| 3 | **Composite generation / PDG integration** | Chain multiple HDAs for complex environment generation (terrain then buildings then vegetation) and integrate Houdini's PDG for massive parallel generation. | No issue |

### GH Issues

```bash
gh issue list --search "Procedural:" --state open
```

---

## Showtime {#showtime-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [SHOWTIME.md](plugins/SHOWTIME.md)

### Production Readiness: 0%

Entirely pre-implementation. The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." An in-game streaming metagame service with simulated audience pools, hype train mechanics, and real/simulated audience blending. Specifies 19 planned endpoints, 9 published events, 8 consumed events, 6 state stores, 25 configuration properties, 2 background workers, and a 7-phase implementation plan. Also resolves the GameSession (L2) to Voice (L3) hierarchy violation by owning the voice room orchestration flow.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Audience Simulation Engine (Phase 3)** | Implement core audience pool management with interest matching, engagement decay, audience churn, and the AudienceTickWorker -- the foundational metagame mechanic. | No issue |
| 2 | **Real Audience Blending (Phase 5)** | Implement sentiment pulse consumption from lib-broadcast, translating real platform audience data into indistinguishable real-derived audience members -- the "natural Turing test." | No issue |
| 3 | **NPC Streamers** | Enable NPCs to "stream" in-game performances (arena fights, craft demonstrations) with simulated audiences, creating an economy where NPC performers compete for audience attention. | No issue |

### GH Issues

```bash
gh issue list --search "Showtime:" --state open
```

---

## Trade {#trade-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [TRADE.md](plugins/TRADE.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for economic logistics orchestration -- the "over time" layer that creates geographic price differentials from Transit distances, Worldstate game-time, and Currency primitives. Absorbs the trade route, shipment, tariff, tax, NPC economic profile, and velocity monitoring concepts from the Economy-Currency Architecture planning document. Core capabilities: **trade routes** (pre-calculated corridors with legs mapped to Transit connections, aggregate cost/risk/duration), **shipments** (physical goods-in-transit with incident tracking and financial settlement), **tariffs and taxes** (location-scoped trade policies with exemptions, brackets, and enforcement), **NPC economic profiles** (per-NPC production/consumption preferences and trading personality), **supply/demand snapshots** (periodic aggregation of NPC economic activity into location-scoped market signals), and **velocity metrics** (economy health monitoring with configurable alerts). Three-tier usage: divine oversight (full visibility for god actors), NPC governance (bounded rationality for merchant/governor NPCs), and external management (data queries for monitoring). DI-based cost enrichment via Transit's `ITransitCostModifierProvider` and `ICollectionUnlockListener` integration. Specifies 38 planned endpoints, 12 state stores (8 MySQL, 4 Redis), 15 published events, 8 consumed events, 4 background workers (route recalculation, tax assessment, supply/demand aggregation, velocity monitoring), 1 variable provider namespace (`${trade.*}`), and 8 potential extensions. No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Trade routes and shipments** | Create schemas, generate code, implement trade route management (CRUD with legs mapped to Transit connections, aggregate cost/risk/duration calculation), and shipment lifecycle (create with goods/currency manifest, depart/advance/arrive driven by game events, incident recording, financial settlement on completion). | No issue |
| 2 | **NPC economic profiles and supply/demand** | Implement per-NPC production/consumption/trading personality profiles, periodic supply/demand snapshot aggregation from NPC economic activity into location-scoped market signals, and the `SupplyDemandAggregationWorker` background service. | No issue |
| 3 | **Variable provider (`${trade.*}`)** | Implement `TradeProviderFactory` for the `${trade.*}` ABML namespace, enabling NPC economic decisions in GOAP -- local supply/demand, best trade routes, profit margins, tariff costs, and market opportunity detection. | No issue |

### GH Issues

```bash
gh issue list --search "Trade:" --state open
```

---

## Utility {#utility-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [UTILITY.md](plugins/UTILITY.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for an infrastructure network topology and continuous flow calculation service. Fills the gap between Workshop (point production) and Trade (discrete shipments) by modeling persistent infrastructure (aqueducts, power grids, sewer systems, magical conduits) as a graph of connections between locations. Core capabilities: **network type registry** (opaque string-coded types like water, power, sewer -- extensible without schema changes), **connection graph** (capacity-limited, condition-tracked edges between locations with ownership and maintenance state), **flow calculation** (BFS-based graph traversal computing steady-state service levels per location from production sources through the connection graph), **coverage snapshots** (cached per-location service levels with status classification: full/partial/critical/none), and **failure cascading** (when a connection fails, downstream locations lose service, publishing coverage events that trigger NPC awareness and emergent investigation/repair chains). Integrates with Workshop for production source rates, Organization for infrastructure operators, Faction for jurisdiction, and Environment for weather-based damage. Provides `${utility.*}` ABML variables via Variable Provider Factory for NPC infrastructure-awareness in GOAP decisions. Specifies 23 planned endpoints, 5 state stores (3 MySQL, 2 Redis), 15 published events, 4 consumed events, 2 background workers (condition decay, maintenance expiration), 1 variable provider namespace (`${utility.*}`), 11 configuration properties, and a 7-phase implementation plan (Phase 0 requires Worldstate and Workshop). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 2-3: Connection Graph + Flow Engine** | Create schemas, generate code, implement connection CRUD with location validation, BFS-based `FlowCalculator` with capacity-limited flow and condition-based reduction, `CoverageManager` with snapshot caching and debounced recalculation. | No issue |
| 2 | **Phase 4: Coverage Events + Variable Provider** | Implement coverage change event publishing (degraded/restored) with configurable thresholds, `UtilityProviderFactory` for the `${utility.*}` ABML namespace, and coverage query endpoints (GetCoverage, GetCoveragePath, GetNetworkHealth, CompareCoverage). | No issue |
| 3 | **Phase 7: DI Flow Modifier Pattern** | Implement `IUtilityFlowModifierProvider` interface in `bannou-service/Providers/` allowing Environment, Faction, and other L4 services to affect flow calculations without Utility depending on them. | No issue |

### GH Issues

```bash
gh issue list --search "Utility:" --state open
```

---

## Workshop {#workshop-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [WORKSHOP.md](plugins/WORKSHOP.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for a time-based automated production service that transforms inputs from source inventories into outputs placed in destination inventories over game time. Uses the Currency autogain pattern generalized: lazy evaluation with background materialization instead of real-time simulation. Supports variable worker counts that dynamically adjust production rate via piecewise rate segment history, enabling accurate calculation across rate changes. Blueprints support both recipe-referenced (from lib-craft) and custom input/output definitions. Features fair per-owner scheduling in the background worker to prevent heavy users from affecting others. Covers crafting automation, farming, mining, resource extraction, manufacturing, training, and any time-based production loop. Specifies 22 planned endpoints, 12 published events, 1 consumed event, 6 state stores, 14 configuration properties, 1 background worker, and a 7-phase implementation plan. Explicitly rejects actors for automation tasks (deterministic production doesn't need cognitive overhead) and rejects extending lib-craft (different interaction paradigms). No endpoints, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 2 - Task Lifecycle** | Implement task creation with blueprint snapshotting, status management (pause/resume/cancel), source/destination inventory validation, and initial rate segment creation. | No issue |
| 2 | **Phase 4 - Lazy Evaluation & Materialization** | Implement `ProductionCalculator` with rate segment integration, material consumption, output creation via lib-item/lib-inventory, fractional progress tracking, and auto-pause on exhaustion/capacity. | No issue |
| 3 | **Phase 5 - Background Worker** | Implement `WorkshopMaterializationWorkerService` with fair per-owner scheduling, distributed locking, lazy materialization on read, and `worldstate.day-changed` event-triggered processing. | No issue |

### GH Issues

```bash
gh issue list --search "Workshop:" --state open
```

---

*This document reports plugin status from deep dive documents and the GitHub issue tracker. For code-level auditing, use `/audit-plugin`.*
