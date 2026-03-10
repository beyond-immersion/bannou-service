# Plugin Production Readiness Status

> **Last Updated**: 2026-03-06
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
| [Actor](#actor-status) | L2 | 90% | 2 | L3-hardened. Two-phase tick, dynamic character binding, pool mode, ~80 telemetry spans. 2 bugs. Auto-scale stubbed. |
| [Character](#character-status) | L2 | 97% | 0 | L3-hardened. All 12 endpoints, schema NRT, telemetry spans, client events, MySQL JSON queries. Only extensions remain. |
| [Collection](#collection-status) | L2 | 93% | 0 | Production-hardened. All 20 endpoints, global first-unlock, client events, ETag concurrency, lib-resource cleanup. 0 bugs, 0 stubs. Only design extensions remain (#475, #476). |
| [Currency](#currency-status) | L2 | 85% | 0 | Production-hardened (7 bugs fixed, ). 8 stubs remain: hold expiration, currency expiration, analytics, pruning. |
| [Game Service](#game-service-status) | L2 | 100% | 0 | Production-hardened registry. All 5 endpoints done. Resource cleanup on delete. |
| [Game Session](#game-session-status) | L2 | 92% | 0 | Production-hardened. Voice removed, lifecycle events live, Distributed locks. |
| [Inventory](#inventory-status) | L2 | 85% | 0 | Production-hardened (93 tests). 4 stubs remain: grid collision, weight propagation, equipment slots, RemoveItem cleanup. |
| [Item](#item-status) | L2 | 92% | 0 | Production-hardened (70 tests). Dual-model + Itemize Anything. Decay system pending (#407). |
| [Location](#location-status) | L2 | 97% | 0 | All 24 endpoints done. Hierarchical management, spatial queries, presence tracking, ${location.*} variable provider. Hardened to L3. |
| [Quest](#quest-status) | L2 | 95% | 0 | Production-hardened (46 tests). Orchestration over Contract. Prerequisites, rewards, caching done. Extensions only. |
| [Realm](#realm-status) | L2 | 100% | 0 | Production-hardened (). ETag concurrency, distributed merge lock, telemetry spans, event coverage. |
| [Relationship](#relationship-status) | L2 | 95% | 0 | All 21 endpoints done. Hardened: telemetry, constructor caching, deprecation lifecycle, sentinel elimination. Variable Provider Factory pending (#147). |
| [Seed](#seed-status) | L2 | 95% | 0 | Production-hardened (). Constructor caching, telemetry spans, sentinel elimination, schema validation. Archive cleanup needed. |
| [Species](#species-status) | L2 | 92% | 0 | All 13 endpoints done. Missing distributed locks on concurrent operations. |
| [Subscription](#subscription-status) | L2 | 95% | 0 | L3-hardened. Distributed locks, telemetry spans, constructor caching, type safety, worker delegation. 33 tests, 0 warnings. |
| [Transit](#transit-status) | L2 | 95% | 0 | Fully implemented. All 33 endpoints, 8 state stores, 2 background workers, variable provider, DI cost enrichment. Only extensions remain. |
| [Worldstate](#worldstate-status) | L2 | 95% | 0 | Fully implemented. All 18 endpoints, clock worker, variable provider, client events, cross-node cache invalidation. Only extensions remain. |
| [Orchestrator](#orchestrator-status) | L3 | 70% | 0 | L3-hardened. Lease expiry enforcement, multi-version rollback, log timestamp fix, dead config cleanup done. Compose backend solid. 3/4 backends stubbed. Pool auto-scale, idle timeout missing (#550). |
| [Asset](#asset-status) | L3 | 92% | 0 | L3-hardened. Schema/enum consolidation, background workers (bundle cleanup, ZIP cache), transactional indexes, all events published. 117 tests. |
| [Documentation](#documentation-status) | L3 | 92% | 0 | All 27 endpoints done. Full-text search, git sync, archive. Full TENET audit complete. Semantic search pending. |
| [Voice](#voice-status) | L3 | 95% | 0 | L3-hardened. Full TENET audit (2 rounds): NRT-compliant schemas, distributed locks on all read-modify-write paths (6 methods), dead code removed. 89 tests. |
| [Website](#website-status) | L3 | 5% | 0 | Complete stub. All 14 endpoints return NotImplemented. No state stores, no logic. |
| [Broadcast](#broadcast-status) | L3 | 0% | 0 | Pre-implementation. Deep dive L3-audited: x-lifecycle events, Redis tracking IDs, camera API endpoints, nullable configs, codec enums, worker intervals, webhook exception. No schema, no code. |
| [Agency](#agency-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-03): 5 critical schema/tenet findings, 16 warnings, 7/9 design considerations resolved. No schema, no code. |
| [Achievement](#achievement-status) | L4 | 90% | 1 | Production-hardened. Typed fields, client events, Category B deprecation. 1 Pattern A topic bug. Xbox/PS stubs remain. |
| [Analytics](#analytics-status) | L4 | 88% | 0 | Production-hardened. NRT-clean schemas, validation bounds on all properties. Glicko-2, event ingestion, summaries, controller history. Rating decay missing. |
| [Behavior](#behavior-status) | L4 | 88% | 0 | L3-hardened. Audit fixes, 929 tests. IAssetClient soft dependency, async emitters, sentinel elimination. 6 stubs remain. |
| [Character Encounter](#character-encounter-status) | L4 | 92% | 0 | Production-hardened. NRT-clean schemas, validation bounds on all properties. ETag retry configurable, event outcome typed. Index growth concerns remain. |
| [Character History](#character-history-status) | L4 | 95% | 0 | Feature-complete. Participations, backstory, summarization, compression. Hardened: filler removed, EntityType enum, enriched events, expanded tests. |
| [Character Lifecycle](#character-lifecycle-status) | L4 | 0% | 0 | Pre-implementation. Spec audited: event topics, type classifications, state stores, deprecation lifecycle, compliance, polygamy support all corrected. No schema, no code. |
| [Character Personality](#character-personality-status) | L4 | 95% | 0 | Hardened. x-lifecycle, filler removal, permissions, type safety, config validation, null safety. Both variable providers work. |
| [Divine](#divine-status) | L4 | 25% | 0 | L4-audited (2026-03-06): Schema allOf event compliance, topic_prefix, constructor dependencies, store caching, event consumer, internal models, telemetry spans, structured logging. Skeleton tenet-compliant. All 22 endpoints still stubbed. 3 design decisions, 2 external blockers (#383/#388). |
| [Escrow](#escrow-status) | L4 | 75% | 1 | 13-state FSM works. Hardened: sentinel values→nullable, typed config enums, telemetry spans, additionalProperties:false, filler booleans removed. Validation placeholder, custom handlers inert, status index broken. |
| [Environment](#environment-status) | L4 | 0% | 0 | L4-audited 3x (2026-03-06): Pre-implementation spec hardened. 3 audit passes fixed: cleanup, topics, Category A, types, permissions, thresholds, sentinel values, binding/realm-config endpoints added to API section, IEventConsumer, cleanup completeness, cross-reference consistency. No schema, no code. |
| [Ethology](#ethology-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): cleanup (location/species event subs→lib-resource x-references), Category A deprecation (undeprecate+delete endpoints), x-permissions (nature query/cleanup→[]), PascalCase enums (ActivityPattern, DietType, SocialStructure, OverrideScopeType), x-lifecycle adoption, ITelemetryProvider, IRealmClient/IResourceClient added, variable-providers.yaml registration, speciesCode metadata concern (#308). 6 design considerations remain. No schema, no code. |
| [Faction](#faction-status) | L4 | 85% | 0 | All 31 endpoints done. L4-audited (2026-03-06): filler removed (5 responses), deprecation lifecycle (triple-field, idempotent, delete guard, includeDeprecated), collection growth configurable, event schemas corrected (allOf + BaseServiceEvent). Obligation integration missing. |
| [Gardener](#gardener-status) | L4 | 65% | 0 | Void garden works (23 endpoints). L4-audited (2026-03-06): filler removed (6 responses), Category B (delete endpoint removed, idempotent deprecation, includeDeprecated), GameType configurable, Guid.Empty sentinel eliminated, event schemas corrected (allOf + BaseServiceEvent), lifecycle topic prefix added. Broader garden concept unimplemented. No client events, no divine actors. |
| [Leaderboard](#leaderboard-status) | L4 | 85% | 0 | Production-hardened. Typed scoreType/ratingType, lifecycle events, NRT-clean schemas. IncludeArchived stub, batch UpdateMode ignored, 6 design decisions deferred. |
| [Lexicon](#lexicon-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): deep dive hardened — Category A deprecation (triple-field, 3 new endpoints), typed StrategyPrecondition (replaced object? preconditions), ICollectionClient hard dependency, ITelemetryProvider, x-lifecycle events annotated, bidirectional resource cleanup documented. 6 design decisions deferred (AUDIT:NEEDS_DESIGN). No schema, no code. |
| [License](#license-status) | L4 | 95% | 0 | L4-hardened. Zero code tenet violations. Schema validation constraints, PascalCase enum fixes, x-permissions corrected, currentLp type fixed. 59 tests, 0 warnings. Only respec pending (#356). |
| [Mapping](#mapping-status) | L4 | 80% | 2 | Spatial indexing works. Version counter race, non-atomic index ops. N+1 query pattern. |
| [Matchmaking](#matchmaking-status) | L4 | 85% | 0 | Production-hardened. NRT compliant, configurable lock timeouts, client event account IDs removed. Queue stats stub, tournament stub, _sessionAccountMap design decision open. |
| [Music](#music-status) | L4 | 88% | 0 | Full composition pipeline. Storyteller + MusicTheory SDKs. Custom style persistence missing. |
| [Obligation](#obligation-status) | L4 | 92% | 0 | L4-audited (2026-03-06): fixes. All 11 endpoints production-ready. 2 feature gaps (event templates, ref tracking), 4 design considerations. |
| [Puppetmaster](#puppetmaster-status) | L4 | 55% | 0 | Architecture designed. Watchers never spawn actors (core purpose stubbed). All state in-memory. |
| [Realm History](#realm-history-status) | L4 | 95% | 0 | **Hardened.** Feature-complete. PascalCase enums, tenet-compliant responses, NRT-clean. Participations, lore, summarization, compression. |
| [Save-Load](#save-load-status) | L4 | 92% | 0 | **Hardened (3x)**. Two-tier storage, delta saves, migration all working. Sentinel elimination, config-first (lock timeouts, URL expiry, migration steps), typed key builders, L3 soft deps in all helpers. Binary deltas stubbed, quota enforcement gap. |
| [Scene](#scene-status) | L4 | 97% | 0 | **Hardened (2x).** All 19 endpoints done. Schema NRT-clean, tenet-compliant. Opaque string types (SceneType, AffordanceType, MarkerType). Build*Key() pattern. 97 unit tests. |
| [Status](#status-status) | L4 | 97% | 0 | **Hardened (4x).** All 19 endpoints. ISeedClient constructor injection, echoed fields removed, lazy expiration bug, Pattern C topics, enum safety, 12 unit tests. 1 design decision deferred (#412 dead config). Blocked on #407. |
| [Storyline](#storyline-status) | L4 | 85% | 0 | **Hardened.** All 15 endpoints implemented. Build*Key pattern, topic constants + typed event publishers, narrowed try-catch, filler removal, enum type safety with A2 boundary mapping, topic rename, dead config removed, Category B lifecycle. Iterative composition and content flywheel integration not yet wired. |
| [Affix](#affix-status) | L4 | 0% | 0 | Pre-implementation. Spec L4-audited: Pattern C topics, Category B deprecation, instance guard, typed spawnTagModifiers, hard IInventoryClient, orphan reconciliation worker, x-permissions on all groups. No schema, no code. |
| [Arbitration](#arbitration-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-05): 6 critical spec violations found and fixed in-document, schema creation guidance added, work tracking with 6 related GH issues, 4 resource cleanup targets. 8 design considerations remain. 2 unmet prerequisites (Faction sovereignty, Obligation multi-channel). No schema, no code. |
| [Craft](#craft-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): Pattern C topics, typed models (requiredStates, affixOperationConfig), Category C affixOperation enum, Category B with deprecation guard, isActive removed, local proficiency fallback removed, x-permissions all groups, location cleanup target. 7 design considerations remain. No schema, no code. |
| [Director](#director-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): accountId→sessionId, PascalCase enums, x-lifecycle adoption, metrics removal (→Analytics), phase events collapsed to `*.updated`, cleanup endpoint reduced. 8 design considerations remain. No schema, no code. |
| [Disposition](#disposition-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): targetType→$ref:EntityType, targetId→Guid (no "self" sentinel), IRelationshipClient→hard dep, typed SynthesisBreakdown, DriveOriginType PascalCase enum, past-tense topics, x-permissions all 17 endpoints, x-event-publications, x-resource-mapping, compression priority 40→20, guilt-as-composite-feeling (GH#410), seed.phase.changed subscription (GH#497), hearsay deferred to Phase 4+. No schema, no code. |
| [Dungeon](#dungeon-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): personalityType→DungeonCoreModel (not seed metadata), IItemClient/IInventoryClient→hard deps, Pattern A topics (trap-triggered, layout-changed, phase-changed), DungeonStatus enum added, game-service cleanup target, ITelemetryProvider in DI, domain management permissions, config validation constraints, GrowthContributionDebounceMs config, no-deprecation classification, inhabitant durability note, DC#3 resolved (#422). 10 design considerations remain, 7 missing GH issues. No schema, no code. |
| [Hearsay](#hearsay-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): BeliefDomain+SourceChannel enums (not strings), subjectEntityId→Guid (no composite GUID-in-string), PascalCase enum values, factual event topic fix (encounter.recorded not character-encounter.created), Obligation→Soft Dependencies, ITelemetryProvider in DI, x-archive-type noted, Phase 1 prerequisites (state-stores.yaml, variable-providers.yaml). 9 design considerations (3 from audit). No schema, no code. |
| [Loot](#loot-status) | L4 | 0% | 0 | Pre-implementation. Deep dive audited and hardened (tenet-compliant event topics, PascalCase enums, typed models, x-permissions, dependency classification). No schema, no code. |
| [Market](#market-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-06): Pattern C topics, PascalCase enums, x-lifecycle (MarketDefinition + VendorCatalog), x-permissions on all 6 groups, configuration entity, stock locking, filler removed, enum definitions, ITelemetryProvider, namespace overlap fixed (market-price), x-references with field+payloadTemplate. 4 design decisions deferred. No schema, no code. |
| [Organization](#organization-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-07): kebab-case topics, PascalCase enums (SuccessionMode), x-lifecycle adoption (topic_prefix, custom event distinction), EntityType committed (ownerType/entityType), resource registration + x-references, ITelemetryProvider, enum config type, x-event-publications/subscriptions, lock key prefix fix, 8 AUDIT:NEEDS_DESIGN decisions (#11-#18). 9 GH issues referenced. No schema, no code. |
| [Procedural](#procedural-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-07): Asset/Orchestrator→soft deps (L3 graceful degradation), Category B deprecation (DeactivateTemplate→DeprecateTemplate, triple-field, includeDeprecated, no delete/undeprecate), x-lifecycle for template entity, ITelemetryProvider added, nullable seed. 6 design decisions deferred (AUDIT:NEEDS_DESIGN). No schema, no code. |
| [Showtime](#showtime-status) | L4 | 0% | 0 | Pre-implementation. Deep dive fully hardened (2026-03-09): account.deleted handler, compensation/self-healing docs, allOf/eventName requirements, Build*Key() pattern, worker per-item isolation, x-references complete, NRT/additionalProperties notes. 6 design decisions resolved (DC#4 local followers, DC#8 client events deferred, DC#9 inline career progression, DC#10 ShowtimeEnabled removed, DC#11 Showtime prefix, DC#12 custom events). 3 deferred (DC#13 voice conflict #382, DC#14 SentimentCategory #572). No schema, no code. |
| [Trade](#trade-status) | L4 | 0% | 0 | Pre-implementation. L4-audited (2026-03-10): deep dive hardened with PascalCase enums, kebab-case topics, x-permissions, x-lifecycle, x-references, deprecation lifecycle, distributed locks, typed schemas, NRT compliance, validation constraints. 4 design decisions deferred. Blocker: #153. No schema, no code. |
| [Utility](#utility-status) | L4 | 0% | 0 | Pre-implementation. Infrastructure network topology, continuous flow calculation, coverage cascading, and maintenance lifecycle spec. No schema, no code. |
| [Workshop](#workshop-status) | L4 | 0% | 0 | Pre-implementation. Time-based automated production with lazy evaluation and worker scaling spec. No schema, no code. |
| [Common](#common-status) | N/A | N/A | 0 | Shared type definitions library. 0 endpoints. No deep dive document exists. |

---

## State {#state-status}

**Layer**: L0 Infrastructure | **Deep Dive**: [STATE.md](plugins/STATE.md)

### Production Readiness: 97%

The bedrock of the entire platform. Provides unified Redis/MySQL/InMemory/SQLite state access to all 54 services via `IStateStoreFactory`. Manages ~107 state stores (~70 Redis, ~37 MySQL). Full interface hierarchy: `IStateStore<T>` (core CRUD), `ICacheableStateStore<T>` (sets, sorted sets, counters, hashes), `ISearchableStateStore<T>` (full-text via RedisSearch), `IQueryableStateStore<T>` / `IJsonQueryableStateStore<T>` (MySQL LINQ/JSON path queries), `IRedisOperations` (Lua scripts), `IDistributedLockProvider` (distributed mutex). Optimistic concurrency via ETags on all backends. Error event publishing with deduplication. No stubs, no bugs, no design considerations. 11 well-documented intentional quirks. L3-hardened (2026-02-22): schema NRT compliance (added `required` arrays to 7 response schemas), `additionalProperties` corrected on generic value objects, redundant nullable config removed, layer comments fixed in state-stores.yaml, null-forgiving operators replaced with safe `.ToString()` in Redis stores, LINQ expression tree null coalescing fixed in MySQL/SQLite stores, violation fixed. 397 tests passing, 0 warnings.

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

Core pub/sub infrastructure is robust: RabbitMQ channel pooling (100 default, 1000 max), publisher confirms for at-least-once delivery, aggressive retry buffer with crash-fast philosophy (500k message / 10 minute threshold), backpressure at 80% buffer fill, dead-letter exchange with configurable limits, poison message handling with retry counting, HTTP callback subscriptions with recovery, event consumer fan-out bridge (`NativeEventConsumerBackend`), in-memory mode for testing, dead letter consumer with structured logging and error event publishing. 30+ configuration properties all wired. 0 stubs, 0 bugs. L3-hardened (2026-02-21): schema NRT compliance, validation constraints, enum consolidation, dead field removal, code fixes. Design considerations resolved (2026-02-22): `Program.ServiceGUID` replaced with `IMeshInstanceIdentifier` injection, shutdown timeout added (`ShutdownTimeoutSeconds`, default 10s). Dead letter consumer implemented (2026-02-22): `DeadLetterConsumerService` subscribes to DLX exchange, logs dead letters with metadata extraction, publishes `service.error` events, uses durable shared queue with competing consumers for multi-instance safety. 3 informational design notes remain (in-memory mode limitations, tap exchange auto-creation, publisher confirms latency). 1 extension identified (Prometheus metrics). 0 warnings, 216 tests passing.

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

Feature-complete service mesh: YARP-based HTTP routing, Redis-backed service discovery with TTL health tracking, 5 load balancing algorithms (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random), distributed per-appId circuit breaker with Lua-backed atomic state transitions and cross-instance sync via RabbitMQ, retry with exponential backoff, proactive health checking with automatic deregistration, degradation detection, event-driven auto-registration from Orchestrator heartbeats, endpoint caching, canonical `IMeshInstanceIdentifier` for node identity. 27 configuration properties all wired. No stubs, no bugs, no design considerations, no extensions remaining. All production readiness issues closed. L3-hardened (2026-02-21): schema NRT compliance, validation constraints, enum consolidation to `-api.yaml`, async patterns, sentinel value removal, telemetry spans across all helper classes, error event publishing, BuildServiceProvider anti-pattern removed. Dead field cleanup (2026-02-22): removed `lastUpdateTime` (always returned current time — useless), fixed `alternates` nullability (code always returns list, schema lied about nullable). `IMeshInstanceIdentifier` (2026-02-22): canonical mesh node identity with priority chain (env > CLI > random), `InstanceId` exposed on all generated clients and `IServiceNavigator`, replaced all `Program.ServiceGUID` usages. Graceful draining deemed unnecessary (Orchestrator's two-level routing handles managed deployments). 0 warnings, 55 tests passing.

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

L3-hardened. Full authentication suite: email/password, OAuth (Discord/Google/Twitch), Steam tickets, JWT tokens, session management, password reset, login rate limiting, TOTP-based MFA with recovery codes, edge token revocation (CloudFlare/OpenResty). 46 configuration properties with full NRT compliance, validation bounds, and patterns. Schema inline enums extracted to named types. Telemetry spans on all 48 async methods across 7 helper services. Provider enum properly threaded (no `Enum.Parse`). Atomic session indexing via Redis Set operations (eliminated read-modify-write races). Email change propagation implemented. 168 tests, 0 warnings. Remaining work is downstream integration: audit event consumers (Analytics L4) and account merge session handling (post-launch).

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

L3-hardened. Feature-complete OpenTelemetry tracing and Prometheus metrics with full instrumentation decorators for all infrastructure libs (state, messaging, mesh, telemetry). Schema issues fixed (events file, NRT annotations, OtlpProtocol enum typed). Null-forgiving operators eliminated. (async pattern, self-instrumentation spans). Tail-based sampling implemented in OTEL Collector (100% error/high-latency retention, 10% probabilistic default). 58 tests, 0 warnings. Only speculative extensions remain (managed exporters, Grafana dashboards).

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

All 30 API endpoints complete with no stubs and no bugs. Covers room type management, room lifecycle with contract governance, participant moderation (join/leave/kick/ban/mute with role hierarchy), message operations (send/batch/history/search/pin/delete) with dual storage (ephemeral Redis TTL and persistent MySQL), rate limiting via atomic Redis counters, typing indicators with sorted-set-backed expiry, 14 service events, 12 client events, 7 state stores (4 MySQL, 3 Redis), and 4 background workers (idle room cleanup, typing expiry, ban expiry, message retention). Hardened with: telemetry spans on all async helpers and event handlers, distributed lock on room type registration and idle room cleanup, Regex timeout protection, schema validation keywords on all request fields,-compliant event topic naming, `required` string properties replacing `string.Empty` defaults, error event publication on startup failures, and ServiceHierarchyValidator test coverage. Two design considerations remain: O(N) participant counting in AdminGetStats (#455) and mixed data patterns in the participants Redis store (#456).

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

L3-hardened. WebSocket connection lifecycle, zero-copy binary routing, reconnection windows, session shortcuts, client-to-client routing, three connection modes, per-session RabbitMQ subscriptions, and multi-node broadcast mesh (`InterNodeBroadcastManager`). Major L3 hardening pass (2026-02-22): fixed thread safety (ConcurrentDictionary/ConcurrentQueue in ConnectionState), Guid.Empty sentinels, anonymous objects, dead config wired (MaxChannelNumber), bare catch blocks, sync-over-async (.Wait()), IDisposable/ClientWebSocket leak, telemetry spans, XML docs. Schema consolidated (connect-shortcuts.yaml merged), enums extracted, format:uuid added, validation constraints added, descriptions fixed. One bug remains: orphaned `CompanionRoomMode` config property (defined in schema but never referenced in code).

### Bug Count: 1

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **Orphaned CompanionRoomMode config** | `CompanionRoomMode` is defined in the configuration schema and generated config class but never referenced in service code. Violation (dead config). | No issue |

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

L3-hardened. Comprehensive feature set: template CRUD, instance lifecycle with full state machine (Draft through Fulfilled/Terminated/Expired), consent flows, milestone progression with deadline enforcement (hybrid lazy + background), breach handling with cure periods, guardian custody for escrow integration, clause type system with execution pipeline, prebound API batching, and idempotent operations. L1-to-L2 hierarchy violation (ILocationClient) removed, schema NRT/compliance fixed, telemetry spans added to all async helpers. Contract expiration implemented: `ContractExpirationService` handles both effectiveUntil expiration and milestone deadline enforcement in a single background worker pass with lazy enforcement in `GetContractInstanceStatusAsync`. TemplateName denormalized onto instance model at creation time. Clause handler request/response mappings reclassified as Potential Extension. Payment schedule enforcement added: background worker publishes `ContractPaymentDueEvent` for one-time and recurring schedules with drift prevention. 0 stubs remain.

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

L3-hardened and feature-complete. All 8 endpoints are fully implemented with multi-dimensional permission matrix compilation, configurable role hierarchy, session state management, idempotent service registration, and real-time capability push to WebSocket clients via RabbitMQ. Major hardening pass (2026-02-22): fixed 14 schema NRT violations, removed filler properties, moved inline enums to API schema, removed dead metadata field, added validation keywords, added `SessionLockTimeoutSeconds` config. Code fixes: added distributed locks for session state/role updates, completed RoleHierarchy migration from hardcoded ROLE_ORDER, added telemetry spans throughout, removed duplicate try-catch, fixed sentinel values, extracted magic strings to constants. Session cache invalidation implemented (#392): `ISessionActivityListener` DI listener pattern with heartbeat-driven TTL refresh, in-memory cache removed entirely, `SessionDataTtlSeconds` reduced from 86400 to 600. 27 unit tests, all passing. No bugs, no stubs, no extensions remaining.

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

L3-hardened. Core architecture is solid and functional: template CRUD, actor spawn/stop/get lifecycle, behavior loop with two-phase tick execution (cognition pipeline + ABML behavior), ABML document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, encounter management, pool mode with command topics and health monitoring, Variable Provider Factory pattern (personality/combat/backstory/encounters/quest), behavior document provider chain (Puppetmaster dynamic + seeded + fallback), dynamic character binding (event brain → character brain without relaunch), periodic state persistence, character state update publishing. 30+ configuration properties all wired with validation keywords. Schema NRT compliance verified. Telemetry spans on ~80 async methods across 22 files. All filler booleans removed from responses. Inline enums consolidated to shared types. ETag retry loops on all index operations. All disposal and lifecycle patterns correct. No implementation gaps.

Two known bugs (violations: `cognitionOverrides` and `initialState` use `additionalProperties: true` but are deserialized to typed objects — both tracked with open issues). Significant production features remain unimplemented: auto-scale deployment mode is declared but stubbed, session-bound actors are stubbed, and 5 extensions (memory decay, cross-node encounters, behavior versioning, actor migration, Phase 2 variable providers) are open. The pool node capacity model is self-reported with no external validation.

### Bug Count: 2

Two violations with open design issues.

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | **`cognitionOverrides` metadata bag** | Defined as `additionalProperties: true` but deserialized to typed `CognitionOverrides` with 5 discriminated subtypes. Should be a typed schema with `oneOf`/discriminator pattern. | [#462](https://github.com/beyond-immersion/bannou-service/issues/462) |
| 2 | **`initialState` metadata bag** | Defined as `additionalProperties: true` but cast to `ActorStateSnapshot` with structured fields. Should define typed schema subset. | [#463](https://github.com/beyond-immersion/bannou-service/issues/463) |

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

L3-hardened. All 12 endpoints fully implemented with no stubs. Schema NRT compliance verified (3 critical, 7 major fixes applied). Event types properly located in events schema with uuid format and enum reason. Telemetry spans on all 13 async helpers. Configuration validation keywords on all properties. RefCountUpdateMaxRetries extracted from hardcoded constant to config. Post-review: fixed missing fields in CharacterCreatedEvent, corrected null vs empty list in CompressCharacterAsync, fixed referenceTypes description, removed L4-owned snapshot types from L2 schema. CRUD operations include smart field tracking, realm-partitioned storage with MySQL JSON queries, enriched retrieval with family tree data (from lib-relationship), and centralized compression via the Resource service. Distributed locking, optimistic concurrency, and lifecycle events all wired. Remaining: 2 design-phase extensions and 1 design consideration (batch ref unregistration).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Delete flow O(N) reference unregistration** | When a character is deleted, cleanup callbacks fire on 4 L4 services, each publishing individual `resource.reference.unregistered` events. For characters with rich data, this creates O(N) message bus traffic. A batch unregistration endpoint in lib-resource would reduce this to a single operation. | [#351](https://github.com/beyond-immersion/bannou-service/issues/351) |
| 2 | **Character purge background service** | Automated purge of characters eligible for cleanup (zero references past grace period). Config removed for compliance; needs redesign when operational need arises. | [#263](https://github.com/beyond-immersion/bannou-service/issues/263) |
| 3 | **Batch compression** | Compress multiple dead characters in one operation via a batch variant of `/resource/compress/execute`. | [#253](https://github.com/beyond-immersion/bannou-service/issues/253) |

### GH Issues

```bash
gh issue list --search "Character:" --state open
```

---

## Collection {#collection-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [COLLECTION.md](plugins/COLLECTION.md)

### Production Readiness: 93%

Production-hardened. All 20 endpoints implemented with working integration with lib-inventory and lib-item for the "items in inventories" pattern, DI-based unlock listener dispatch (Seed, Faction), area content selection with weighted random themes, global first-unlock tracking via Redis SADD, real-time client events via IEntitySessionRegistry, ETag-based optimistic concurrency on cache updates, lib-resource cleanup for character-owned collections, and distributed locking on all mutation paths. All previously reported bugs fixed (grant limit bypass, cleanup events, template update fields, list pageSize). Cache invalidation bounded by TTL (intentional quirk). Only two design extensions remain: expiring/seasonal collections (#475) and collection sharing/trading (#476).

### Bug Count: 0

No known bugs. 4 previously reported bugs all fixed in prior audits.

### Top 3 Bugs

*(None -- 4 fixed in prior audits)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Expiring/seasonal collections** | Support time-limited collection types that expire or rotate on a schedule. Requires design decisions on temporal mechanics and Worldstate integration. | [#475](https://github.com/beyond-immersion/bannou-service/issues/475) |
| 2 | **Collection sharing/trading** | Allow owners to share or trade unlocked entries between collections. Requires design decisions on ownership transfer and Escrow integration. | [#476](https://github.com/beyond-immersion/bannou-service/issues/476) |
| 3 | *(No further enhancements identified)* | | |

### GH Issues

```bash
gh issue list --search "Collection:" --state open
```

---

## Currency {#currency-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [CURRENCY.md](plugins/CURRENCY.md)

### Production Readiness: 85%

Core currency operations are comprehensive and production-hardened after a thorough audit (2026-02-24): 7 bugs fixed (autogain race condition, metadata drop, sentinel values, TOCTOU, index failures), full type safety (no Guid.Parse/ToString in helpers), telemetry on all 25 async methods, schema NRT + validation compliance, dead code/config removed. Definitions, wallets, balance operations (credit/debit/transfer with idempotency and distributed locks), authorization holds (reserve/capture/release), exchange rate conversions, and escrow integration endpoints all work. The 8 remaining stubs are the gap to 100%: analytics endpoints return zeros, currency/hold expiration have no enforcement, global supply cap unchecked, item linkage unenforced, EarnCapResetTime ignored, transaction retention never deletes.

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

Production-hardened registry with all 5 CRUD endpoints fully implemented and all tenet violations resolved. L3 hardening pass (2026-02-24) addressed: (Guid.Empty sentinels removed), (telemetry spans on all async helpers), (distributed lock on stub name uniqueness), (retry count moved to config, dead code removed), (lib-resource cleanup on delete with 409 Conflict). x-resource-lifecycle declared in schema. Deep dive updated with 5 previously missing dependents. Only speculative extensions remain (metadata validation, service versioning).

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

Production-hardened. Core lobby and matchmade session flows work end-to-end with subscription-driven shortcut publishing, reservation cleanup, and horizontal scaling by game. Voice hierarchy violation (L2→L3) fully removed — voice fields stripped from schema, models, and service code. Lifecycle events (`game-session.updated`, `game-session.deleted`) now published at all mutation points. All event models defined in schema (SessionCancelled client/server events). Type safety (enums, Guids throughout), no sentinel values (nullable Guid returns), telemetry spans on all async methods, error handling (re-throw after rollback, TryPublishErrorAsync), distributed locks on session-list and lobby creation races. Remaining gaps: actions endpoint is echo-only (no real processing), chat allows messages from non-members, single-key session list won't scale past ~1000 sessions.

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

Production-hardened with comprehensive tenet compliance. All 16 endpoints functional with distributed locking, cache-through patterns, and multi-constraint-model support (slot, weight, volumetric, grid, unlimited). L3 hardening pass applied: filler removal from 7 response schemas, sentinel fix (Guid.Empty -> null), metadata disclaimers, telemetry spans on all 8 async helpers, NRT compliance, event schema `additionalProperties: false`, x-lifecycle model completion, and validation keywords throughout. 93 unit tests (24 added). Four tracked stubs remain: grid collision approximation (#196), nested weight propagation (#226), equipment slot validation (#226), and RemoveItem cleanup (#164). 8 design considerations note performance issues (N+1 queries, serial deletion, in-memory pagination) and lack of lib-item event consumption.

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

Production-hardened (70 tests). The dual-model item system (templates + instances) is fully operational with robust CRUD, cache read-through patterns, quantity model enforcement, soulbound types, and the "Itemize Anything" contract-delegation pattern (ephemeral, session, and lifecycle bindings). The `/item/use` and `/item/use-step` endpoints enable arbitrary item behaviors via Contract service prebound APIs. Bulk loading optimizations are in place. Schema enums for EntityType, DestroyReason, UnbindReason replace former string fields. Telemetry spans on all 24 async helper methods. Filler properties removed from responses. Only minor gaps remain: deprecation without instance cascade, item decay/expiration ([#407](https://github.com/beyond-immersion/bannou-service/issues/407)), and post-fetch filtering on list queries.

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

All 24 endpoints are fully implemented with no stubs. Hierarchical location management with circular reference prevention, cascading depth updates, code-based lookups, bulk seeding with two-pass parent resolution, territory constraint validation for Contract integration, realm transfer, and deprecation lifecycle. Includes spatial coordinates with AABB queries (`/location/query/by-position`), entity presence tracking with TTL-based ephemeral Redis storage, a background cleanup worker, arrived/departed events, and a `${location.*}` variable provider for Actor behavior system. Hardened: telemetry spans on all async helpers, error handling (no duplicate try-catch), filler properties removed, lock failures throw instead of silently returning, NRT-compliant schema with validation keywords, x-resource-lifecycle declared.

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

### Production Readiness: 95%

Production-hardened orchestration layer over Contract. Full prerequisite validation (built-in L2 + dynamic L4 via IPrerequisiteProviderFactory), reward distribution via Contract prebound APIs, configurable quest data caching with event-driven invalidation, and Variable Provider Factory integration for Actor ABML expressions. Hardened to L3: schema fixes (consolidated RewardType enum, NRT-compliant events, validation keywords on all integer fields, dead types removed), code fixes (ETag concurrency on CharacterIndex writes, enum-typed PrerequisiteValidationMode, nullable Guid for QuestGiverCharacterId, telemetry spans on all async helpers, DefaultRewardContainerMaxSlots wired to config), and 46 passing unit tests including contract event handler coverage. The remaining gaps are pure extensions (quest chains, dynamic objectives, shared party progress).

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

### Production Readiness: 100%

Production-hardened with comprehensive tenet compliance. All 12 endpoints implemented including complex three-phase merge with distributed locking. ETag-based optimistic concurrency on Update/Deprecate/Undeprecate. Distributed lock on Merge with deterministic key ordering. Full telemetry span coverage (10 async methods). Seed updates now publish events with changedFields tracking. Deprecation lifecycle fully compliant (BadRequest for non-deprecated delete, mandatory reason, idempotent operations). Redundant error handling removed. Three typed state stores resolved in constructor. Two new configuration properties (OptimisticRetryAttempts, MergeLockTimeoutSeconds) replace hardcoded values. No stubs, no bugs, no outstanding design considerations.

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

### Production Readiness: 95%

Feature-complete with no stubs, no bugs, and all 21 endpoints fully implemented. Bidirectional uniqueness enforcement, hierarchical type taxonomy with merge and seed operations, soft-deletion with recreation, and lib-resource cleanup integration. Hardened: telemetry spans on all async helpers, constructor-cached state store references, deprecation lifecycle compliance (BadRequest for non-deprecated delete, creation guard against deprecated types), sentinel value elimination (nullable Depth/EndedAt), filler property removal from responses, and error event publishing in seed loop. Remaining work: Variable Provider Factory for ABML `${relationship.*}` expressions (#147) and optional type constraint enforcement (#338).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Variable Provider Factory** | Implement `RelationshipProviderFactory` (`IVariableProviderFactory`) for ABML `${relationship.*}` variable namespace -- enables NPC behavior system access to relationship data. | [#147](https://github.com/beyond-immersion/bannou-service/issues/147) |
| 2 | **Type constraints** | Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds). | [#338](https://github.com/beyond-immersion/bannou-service/issues/338) |

### GH Issues

```bash
gh issue list --search "Relationship:" --state open
```

---

## Seed {#seed-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [SEED.md](plugins/SEED.md)

### Production Readiness: 95%

Production-hardened foundational primitive with no stubs and no bugs. All 24 endpoints are functional: seed CRUD with exclusive activation, growth recording with bond multipliers and cross-pollination, capability manifests with three fidelity formulas and debounced caching, typed definitions with deprecation lifecycle, bonds with ordered distributed locks and confirmation flow, and a background decay worker with per-type override support. The Collection-to-Seed growth pipeline works via ICollectionUnlockListener DI pattern (rewritten to use ISeedClient mesh call), and Actor integration is complete via SeedProviderFactory. Hardened: constructor-cached state stores, telemetry spans on all async methods, Guid.Empty sentinel eliminated for cross-game types, schema validation (additionalProperties, minLength, minimum/maximum, minItems, nullable $ref wrappers, required deprecation reason). Remaining work is confined to extensions and design considerations.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **No cleanup of associated data on archive** | Archived seeds retain growth data, capability cache, and bond data indefinitely. Needs cleanup strategy -- immediate deletion, background retention worker, or lib-resource compression integration. | [#366](https://github.com/beyond-immersion/bannou-service/issues/366) |
| 2 | **Bond dissolution endpoint** | No endpoint exists to dissolve or break a bond, despite the `BondPermanent` flag implying some bonds should be dissolvable. Needed for pair system (twin spirits). | [#362](https://github.com/beyond-immersion/bannou-service/issues/362) |
| 3 | **Client events for guardian spirit progression** | Push seed phase/capability/growth/bond/activation events to connected clients via IClientEventPublisher using Entity Session Registry. | [#497](https://github.com/beyond-immersion/bannou-service/issues/497) |

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

### Production Readiness: 95%

L3-hardened. All 7 endpoints + background expiration worker fully production-ready. Distributed locking on all mutating operations, telemetry spans on all async methods, constructor-cached state store references, type-safe enum/Guid parameters throughout, background worker delegates to service for consistent locking and event publishing. Schema NRT-compliant with validation keywords. 33 unit tests, 0 warnings. Only remaining concerns: account/service index indefinite growth, and no subscription deletion endpoint.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Index cleanup for account and service indexes** | The expiration worker only cleans the global subscription-index. Account-subscriptions and service-subscriptions indexes grow indefinitely with cancelled/expired entries. | [#223](https://github.com/beyond-immersion/bannou-service/issues/223) |
| 2 | **Subscription deletion endpoint** | No endpoint exists to permanently delete subscription records. Combined with the index cleanup gap, subscription data accumulates forever. | No issue |
| 3 | **Client events for real-time subscription status** | Push SubscriptionStatusChanged client event via IClientEventPublisher when subscriptions change state, especially important for background expiration. | [#500](https://github.com/beyond-immersion/bannou-service/issues/500) |

### GH Issues

```bash
gh issue list --search "Subscription:" --state open
```

---

## Transit {#transit-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [TRANSIT.md](plugins/TRANSIT.md)

### Production Readiness: 95%

Fully implemented. All 33 endpoints complete with no stubs: mode management (8 endpoints with Category A deprecation lifecycle), connection management (7 endpoints with optimistic concurrency status transitions, seasonal availability, bulk seeding), journey lifecycle (12 endpoints with full state machine — preparing/in_transit/at_waypoint/arrived/interrupted/abandoned — including batch advance for NPC scale), route calculation (Dijkstra with multi-modal, seasonal, and discovery filtering), and connection discovery (3 endpoints with automatic reveal on travel). 8 state stores (3 MySQL: modes, connections, journeys-archive; 4 Redis: journeys, connection-graph, discovery-cache, discovery; 1 lock). 14 published events (7 journey lifecycle, 3 mode lifecycle, 3 connection lifecycle via x-lifecycle, 1 discovery), 1 consumed event (worldstate.season-changed), 3 client events via IEntitySessionRegistry. 2 background workers (Seasonal Connection Worker for auto open/close, Journey Archival Worker for Redis→MySQL migration with retention enforcement). Variable provider (`${transit.*}` namespace with 13+ variables for NPC GOAP travel decisions). DI enrichment interface (`ITransitCostModifierProvider`) defined in bannou-service for L4 cost modifiers with aggregation and clamping. Telemetry spans on all async methods. Distributed locks on all mutations. Resource cleanup callbacks for location and character deletion. 20 configuration properties all wired. 0 bugs, 4 intentional quirks documented.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Caravan formations (Phase 2)** | Multiple entities traveling as a group with group speed = slowest member. Data model accommodates this via `entityType: "caravan"` and `partySize`, but Phase 2 needs `partyMembers` tracking, batch departure/advance, and party leader concept. | [#524](https://github.com/beyond-immersion/bannou-service/issues/524) |
| 2 | **Fatigue and rest** | Long journeys accumulate fatigue with configurable thresholds. Auto-pause at next waypoint, rest duration based on mode and stamina. Creates natural stopping points at inns/camps. | [#527](https://github.com/beyond-immersion/bannou-service/issues/527) |
| 3 | **Transit fares** | Monetary cost for certain modes/connections (ferries, toll roads, teleportation). Open design question: Transit stores fares and calls Currency (both L2), or fares are an L4 Trade overlay. | [#535](https://github.com/beyond-immersion/bannou-service/issues/535) |

### GH Issues

```bash
gh issue list --search "Transit:" --state open
```

---

## Worldstate {#worldstate-status}

**Layer**: L2 GameFoundation | **Deep Dive**: [WORLDSTATE.md](plugins/WORLDSTATE.md)

### Production Readiness: 95%

Fully implemented. All 18 endpoints complete with no stubs: clock queries (4 endpoints including batch and elapsed game-time computation with piecewise integration over ratio history), client sync (1 endpoint for on-demand time sync via IEntitySessionRegistry), clock administration (3 endpoints — initialize, set ratio, advance with boundary event batching), calendar management (5 endpoints with structural validation — day period gap/overlap detection, month-season consistency, per-game-service limits), realm configuration (3 endpoints with partial update and distributed locking), and cleanup (2 endpoints via lib-resource callbacks for realm and game-service deletion). Background clock worker advances realm clocks every `ClockTickIntervalSeconds` (default 5s) with per-realm distributed locks, boundary event detection and publishing (hour/period/day/month/season/year), downtime catch-up with configurable policy (Advance/Pause), and `MaxCatchUpGameDays` safety cap. Variable provider (`WorldProviderFactory`) implements `IVariableProviderFactory` providing 14 `${world.*}` variables to Actor (L2) via in-memory TTL cache. Client events (`WorldstateTimeSyncEvent`) pushed to realm-bound sessions on period boundaries, ratio changes, and admin advances. 5 self-subscriptions for cross-node cache invalidation (calendar template, ratio, realm config, clock advance). 12 configuration properties all wired with validation. Reference registration with lib-resource for realm and game-service targets. Telemetry spans on all async methods. 0 bugs. Cross-service integration with lib-realm implemented (optional auto-initialize clock on realm creation).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Ratio history compaction** | Ratio history segments accumulate indefinitely. `RatioHistoryRetentionDays` config was planned but not implemented. Long-running servers could see unbounded growth. | [#529](https://github.com/beyond-immersion/bannou-service/issues/529) |
| 2 | **Calendar events/holidays** | Named dates that repeat annually (harvest festival, winter solstice). Publishable as events when the date is reached. | [#538](https://github.com/beyond-immersion/bannou-service/issues/538) |
| 3 | **Cross-service game-time migration** | Currency autogain (#433), Seed decay (#434), and Character-Encounter memory decay need transition from real-time to game-time via `GetElapsedGameTime`. | [#433](https://github.com/beyond-immersion/bannou-service/issues/433), [#434](https://github.com/beyond-immersion/bannou-service/issues/434) |

### GH Issues

```bash
gh issue list --search "Worldstate:" --state open
```

---

## Orchestrator {#orchestrator-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

### Production Readiness: 70%

L3-hardened with two full audit passes (2026-03-02). The Docker Compose backend is functional and solid: preset-based deployment, live topology updates (add/remove/move/scale/update-env), service-to-app-id routing broadcasts consumed by Mesh, processing pool acquire/release with distributed locks, config versioning with multi-version rollback, health monitoring with source-filtered reports (control plane vs deployed vs all), container management (restart, status, logs), infrastructure health checks, and background lease expiry enforcement. 33 configuration properties all wired. No bugs.

Schema hardened: events moved to orchestrator-events.yaml (schema-first), inline enums extracted to named schemas, NRT compliance (required arrays, nullable: true), filler removal (success booleans, echoed fields, message strings), validation keywords added, health status enums unified in common-api.yaml (ServiceHealthStatus 3-value, InstanceHealthStatus 5-value). Code hardened: telemetry spans on all 27 async methods across 6 files, multi-instance safety (mappings version counter moved to Redis, in-memory routing-changed flag removed), anonymous objects replaced with typed dictionaries, sentinel string.Empty defaults fixed to nullable, DisposeAsync compliance, hardcoded tunables moved to configuration schema. Second audit pass (2026-03-02): lease expiry enforcement via background timer in ServiceHealthMonitor, multi-version rollback (targetVersion field), log timestamp parsing fix (continuation lines inherit preceding timestamp), dead config cleanup (CacheTtlMinutes removed), two design considerations resolved.

Remaining functional gaps: 3 of 4 container backends are stubs (Swarm, Kubernetes, Portainer -- only Compose is implemented). Processing pool management is missing auto-scaling and idle timeout enforcement (#550), and queue depth tracking (hardcoded 0). New design extensions tracked: deploy validation (#551), blue-green deployment (#552), canary deployments (#553), priority queue (#554).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Processing pool auto-scaling + idle timeout** | Scale-up/down thresholds and idle timeout are stored in pool config but no background service evaluates them. Pools cannot auto-scale -- only manual `ScalePool` API calls work. Critical for the 100k+ NPC target where actor pool demand is dynamic. | [#550](https://github.com/beyond-immersion/bannou-service/issues/550) |
| 2 | **Kubernetes/Swarm/Portainer backends** | Only Docker Compose is implemented. Swarm, Kubernetes, and Portainer backends are stubs (interface methods return NotImplemented or minimal responses). Required for any production deployment beyond single-machine dev. | No issue |
| 3 | **Blue-green / canary deployments** | No mechanism for zero-downtime deployments. Blue-green (deploy alongside, switch routing atomically) and canary (percentage-based traffic routing with health monitoring) are both tracked as design extensions. | [#552](https://github.com/beyond-immersion/bannou-service/issues/552) / [#553](https://github.com/beyond-immersion/bannou-service/issues/553) |

### GH Issues

```bash
gh issue list --search "Orchestrator:" --state open
```

---

## Asset {#asset-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [ASSET.md](plugins/ASSET.md)

### Production Readiness: 92%

L3-hardened. Full upload/download pipeline with pre-signed URL generation, bundle management (creation, versioning, soft-delete, resolution, transactional index updates), streaming metabundle assembly, working audio processor (FFmpeg), and all 10 schema-declared events now emitted. Two background cleanup workers implemented (BundleCleanupWorker for expired soft-deleted bundles, ZipCacheCleanupWorker for expired ZIP cache entries). Schema hardened: enum consolidation (8 inline enums → 3 consolidated), NRT compliance, filler removal, validation keywords. Code hardened: sentinel elimination, config extraction, defensive bundle resolution, key format consistency. 117 tests passing, 0 warnings. Remaining: texture/model processors are validation-only stubs, no CDN integration.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Texture and Model Processors** | Both registered processors contain only validation logic with no actual format conversion or optimization. The AudioProcessor is the only fully functional processor. | [#227](https://github.com/beyond-immersion/bannou-service/issues/227) |
| 2 | **CDN integration** | Extend `StoragePublicEndpoint` rewriting to support CDN-fronted download URLs with cache invalidation, reducing direct MinIO load for frequently accessed assets. | No issue |
| 3 | **Content-addressable deduplication** | Asset IDs are SHA-256 derived. Could detect duplicate uploads and deduplicate storage while maintaining separate metadata records. | No issue |

### GH Issues

```bash
gh issue list --search "Asset:" --state open
```

---

## Documentation {#documentation-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [DOCUMENTATION.md](plugins/DOCUMENTATION.md)

### Production Readiness: 92%

All 27 endpoints are implemented with working full-text search (Redis Search enabled), CRUD operations, repository binding with git sync, archive create/restore, trashcan with TTL, and two background services (index rebuild, sync scheduler). Functionally complete for its core knowledge base use case. Full TENET compliance audit completed (2026-03-01): schemas consolidated (5 inline enums, 2 duplicated event enums, 7 filler properties removed, NRT compliance, validation keywords), code hardened (type safety for DocumentCategory enum, nullable sentinels, config extraction, event topic naming, soft dependency for IAssetClient, null guards, fire-and-forget safety). Minor gaps remain: voice summary generation is simple text extraction rather than NLG, search index retains stale terms on document update, and the archive system has a reliability gap when the Asset Service is unavailable.

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

### Production Readiness: 95%

L3-hardened with two rounds of full TENET audit. Core voice room lifecycle, P2P mesh topology, scaled SFU with automatic tier upgrade, WebRTC SDP signaling, broadcast consent flow, participant heartbeat eviction, and all 11 API endpoints are fully implemented with no bugs. All read-modify-write paths protected by distributed locks (6 methods: CreateVoiceRoom, JoinVoiceRoom ad-hoc, RequestBroadcastConsent, RespondBroadcastConsent, StopBroadcast, LeaveVoiceRoom/DeleteVoiceRoom broadcast-stop). Schema NRT-compliant, enum casing standardized (PascalCase), dead Kamailio client code fully deleted, filler properties removed, exception handling narrowed to ApiException for inter-service calls, state stores constructor-cached, fire-and-forget uses CancellationToken.None, event topics follow naming, DeleteVoiceRoomRequest.reason uses proper enum type. 89 unit tests passing including lock failure path coverage. Remaining gaps are primarily around RTP server pool allocation (single-server only), unused RTPEngine publish/subscribe methods, SIP credential expiration not being enforced server-side, and dependency on future services (lib-broadcast, lib-showtime).

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

Pre-implementation. No schema, no code. L4-audited (2026-03-03): deep dive spec is comprehensive and well-designed, but has 5 critical findings and 16 warnings that must be addressed before schema creation. 7 of 9 design considerations resolved (manifest push, influence execution path, module ownership, fidelity curve format, compliance computation, Entity Session Registry, influence persistence). 1 open design question (cross-seed pollination redundancy with Seed's internal `SameOwnerGrowthMultiplier`). 1 deferred (realm-specific module sets). No blocking GitHub issues — Entity Session Registry (#426) prerequisite is complete. 10 cross-cutting issues inform design; 5 new issues should be created.

**Critical findings (must resolve before writing schemas):** (1) Missing `x-lifecycle` events for 3 CRUD entity types (domains, modules, influences), (2) missing `x-permissions` on all 22 endpoints, (3) ComplianceFactors must use typed array not `additionalProperties: true`, (4) Actor→Agency event hierarchy violation fixed (Actor publishes `actor.spirit-nudge.resisted`, Agency relays), (5) missing lib-resource integration for seed-keyed persistent data (`agency-manifest-history`).

**Key warnings:** Redis-based debouncing for ManifestRecomputeWorker, compliance formula magic numbers extracted to config, rolling window made configurable, influence counters must use Redis, Category A deprecation lifecycle on definition entities, consistent naming (register/unregister vs create/delete per), telemetry spans on all helpers.

### Bug Count: 0

No implementation exists to have bugs. 5 critical design findings documented for pre-implementation resolution.

### Top 3 Bugs

*(None -- pre-implementation. 5 critical pre-implementation audit findings documented in deep dive.)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Full implementation (5 phases)** | All 22 endpoints, 6+ state stores, manifest engine, influence system, variable provider factory, Gardener/Disposition integration. | No issue (create tracking issue) |
| 2 | **Cross-seed pollination clarification** | Resolve overlap between Agency's `CrossSeedPollinationFactor` and Seed's internal `SameOwnerGrowthMultiplier`. May be redundant for same-type scenarios. | No issue |
| 3 | **Client events design** | Define how manifest updates and influence outcomes route through Gardener to clients via Entity Session Registry. Coordinate with #497 (Seed client events) and #502 (Meta client event rollout). | No issue |

### GH Issues

```bash
gh issue list --search "Agency:" --state open
gh issue list --search "guardian spirit" --state open # Cross-cutting
```

---

## Achievement {#achievement-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

### Production Readiness: 90%

Production-hardened achievement system. Core CRUD, progress tracking, event-driven auto-unlock from Analytics/Leaderboard (via typed fields — ), prerequisite chains, rarity calculations (background service), Steam platform sync, and Category B deprecation lifecycle. Typed PlatformMapping array replaces Dict<string,string>. Client events for unlock and progress milestones delivered via IEntitySessionRegistry. Constructor-cached state stores, telemetry spans on helpers/handlers, no sentinel values, no filler response properties. Dead code removed. Xbox/PS sync providers remain stubs. Per-entity sync history returns hardcoded zeros. TotalEligibleEntities never populated. N+1 event handler query pattern is a documented design consideration.

### Bug Count: 1

### Top 3 Bugs

| # | Bug | Description | Issue |
|---|-----|-------------|-------|
| 1 | *(No known bugs)* | | |
| 2 | | | |
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

### Production Readiness: 88%

Production-hardened (2026-03-05): schema NRT compliance fixed, filler properties removed (accepted, matchId echo, dryRun echo), metadata description corrected, validation bounds added to all schema properties across API/events/configuration, tenet number references removed from source comments, log levels corrected (query entry logs → LogDebug), controller event gap closed (AnalyticsControllerRecordedEvent now published). Core pipeline is robust: buffered event ingestion with distributed-lock-protected flush, entity summary aggregation in MySQL with server-side filtering/sorting/pagination, full Glicko-2 skill rating implementation with configurable parameters, controller history with retention-based cleanup, 11 event subscriptions across game-session/character-history/realm-history, and resolution caching for cross-service lookups. No bugs. The main gaps are rating period decay (inactive player RD never increases) and milestones being global-only (no per-game customization).

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

### Production Readiness: 88%

L3-hardened. Multi-phase ABML compiler producing stack-based bytecode (30+ opcodes), A*-based GOAP planner with urgency-tiered parameters, 5-stage cognition pipeline, keyword-based memory store, behavior model caching with variant fallback chains, streaming composition with continuation points, and comprehensive domain action handlers (actor_command, actor_query, load_snapshot, emit_event, watcher management). All 33 configuration properties wired and validated. 929 tests, 0 warnings. Audit fixes:-compliant async patterns across all emitters and layers, soft L3 dependency for IAssetClient, sentinel elimination (Guid.Empty, string.Empty), filler response field removal, schema validation keywords added, inline enums extracted to named schemas, event topic namespace corrected (behavior.cinematic-extension). 6 stubs remain: cinematic extension delivery, bundle lifecycle events, embedding memory store, GOAP plan persistence, compiler optimizations, and bundle management lifecycle.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Cinematic extension delivery** | `CinematicExtensionAvailableEvent` and streaming composition opcodes exist but no code publishes the event or implements extension attachment. Critical for the Combat Dream vision of real-time choreographed cinematics. | [#573](https://github.com/beyond-immersion/bannou-service/issues/573) |
| 2 | **GOAP planning failure diagnostics** | `PlanAsync` returns null without indicating cause (timeout vs no path vs node limit). Failure response discards actual search metrics. PlanResult wrapper needed. | [#575](https://github.com/beyond-immersion/bannou-service/issues/575) |
| 3 | **Bundle lifecycle events** | Three lifecycle events (created, updated, deleted) are schema-defined and auto-generated but never published by `BehaviorBundleManager`. Breaks event-driven architecture for bundle consumers. | [#571](https://github.com/beyond-immersion/bannou-service/issues/571) |

### GH Issues

```bash
gh issue list --search "Behavior:" --state open
```

---

## Character Encounter {#character-encounter-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

### Production Readiness: 92%

Production-hardened with no stubs remaining and all 23 configuration properties wired (including ETagRetryMaxAttempts). Filler removal (echoed request fields, success booleans, error messages removed from 5 response types), configurable ETag retry attempts (was hardcoded to 3), event outcome typed as EncounterOutcome enum (was string), null-forgiving operator elimination, and validation bounds on all schema properties. Encounter recording with duplicate detection, multi-participant perspective system, time-based memory decay (lazy and scheduled modes), weighted sentiment aggregation, configurable encounter type management, automatic pruning, compression support, Variable Provider Factory for `${encounters.*}`, and cache invalidation on all write paths. Remaining concerns are architectural: no transactionality across multi-write recording operations, lazy decay write amplification on read paths, and unbounded global character index growth.

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

### Production Readiness: 95%

Feature-complete with no stubs, no bugs, and all configuration properties wired with validation constraints. Participation tracking with dual-indexed CRUD, backstory management with merge semantics, template-based text summarization, compression/restoration support, ABML variable provider factory for `${backstory.*}` expressions, and distributed locking for all write operations. Hardened: filler properties removed from 4 response schemas, `relatedEntityType` upgraded from string to `EntityType` enum, `CharacterParticipationRecordedEvent` enriched with consumer-needed fields (`historicalEventName`, `eventCategory`, `significance`), entry-point logging downgraded to Debug, error returns use null payload, configuration properties have `minimum: 1` validation, NRT compliance fixed across schemas, event assertions use capture pattern, and 4 new tests added (38 total). The only extension is batch reference unregistration (blocked on lib-resource).

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Batch reference unregistration in DeleteAll** | Currently makes N individual `UnregisterReferenceAsync` API calls before bulk deletion. Blocked on lib-resource batch unregister endpoint. | [#351](https://github.com/beyond-immersion/bannou-service/issues/351) |
| 2 | **Typed metadata schema** | Participation metadata accepts any JSON structure via `object?`. No schema validation. Systemic issue affecting 14+ services; violates type safety. | [#308](https://github.com/beyond-immersion/bannou-service/issues/308) |

### GH Issues

```bash
gh issue list --search "Character History:" --state open
```

---

## Character Lifecycle {#character-lifecycle-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-LIFECYCLE.md](plugins/CHARACTER-LIFECYCLE.md)

### Production Readiness: 0%

Aspirational/planned only. Deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. A detailed architectural specification for generational cycle orchestration and genetic heritage — the "ignition switch" for the content flywheel. Two complementary subsystems: **Lifecycle** (aging, marriage, procreation, death processing driven by worldstate year/season events) and **Heritage** (genetic trait inheritance with allele recombination, dominance models, mutation, phenotype expression, aptitude derivation, and bloodline tracking). Orchestrates across 12+ existing services. Specifies 28 planned endpoints, 5 state stores, 9 domain events + x-lifecycle CRUD events, 5 consumed events, 3 background workers (aging/pregnancy/bloodline), 2 variable provider namespaces (`${lifecycle.*}`, `${heritage.*}`), and an 8-phase implementation plan.

**Spec audit completed** (2026-03-05): Event topics corrected to `character-lifecycle.*` prefix with Pattern C for sub-entities. Type field classifications formalized (4 system enums: `LifecycleStatus`, `CreationCause`, `DominanceModel`, `DeathDistribution`; opaque strings for game-extensible codes). State store names corrected to `character-lifecycle-*` prefix. compliance enforced (removed `character.deleted` event subscription, uses lib-resource cleanup callbacks exclusively). deprecation lifecycle added (Category B for all template entities and bloodlines). guardian spirit resolution path documented (characterId → household → account → seed, no accountId in requests). Polygamy support confirmed (`spouseCharacterIds: Guid[]`). `x-references` YAML spec added. `x-permissions` documented on all endpoint sections. Variable provider sentinel values corrected to null.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None — pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Lifecycle Templates and Profiles** | Create schemas, generate code, implement lifecycle template CRUD, profile management, basic aging via `worldstate.year-changed` events, stage transition detection, resource cleanup/compression callbacks. Prerequisite: Worldstate must publish year-changed events. | #436 |
| 2 | **Phase 3 - Procreation** | Implement fertility calculation, pregnancy tracking with expected birth dates, pregnancy worker (worldstate.day-changed triggered), full procreation flow (heritage computation, character creation, relationships, household, backstory seeding), and child limits. Prerequisite: Organization Phase 5 (#385). | #436 |
| 3 | **Phase 5 - Death Processing** | Implement fulfillment calculation from Disposition drives, guardian spirit contribution to Seed (via characterId → household → account resolution chain), archive compression trigger via Resource, inheritance processing, afterlife pathway determination, and content flywheel integration with Storyline. | #436 |

### GH Issues

```bash
gh issue list --search "Character Lifecycle:" --state open
# #436 - Character-Lifecycle service implementation
# #385 - Organization Phase 5 (household pattern prerequisite)
```

---

## Character Personality {#character-personality-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

### Production Readiness: 95%

Hardened to production quality. Feature-complete with no stubs, no bugs, and all 14 configuration properties wired with validation constraints. Full personality evolution pipeline (9 experience types), combat preference evolution (10 combat experience types), compression/restoration support for lib-resource, and both Variable Provider Factory implementations (personality and combat) registered and functional. Hardening pass applied: x-lifecycle for 6 lifecycle events, filler field removal from 4 response models, developer-role permissions on 3 previously anonymous mutation endpoints, affectedTraits type safety (string→TraitAxis enum), null-forgiving operator elimination, NRT compliance, and min/max validation on all config probabilities/integers. Remaining gap is design extensions: combat style transitions are limited (BERSERKER trap state), trait direction weights are hardcoded, and several desirable extensions are tracked by GH issues.

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

L4-audited (2026-03-06). Skeleton is now tenet-compliant: schema events use flat structure with inline eventId/timestamp (S1/S2 fixed), `topic_prefix: divine` added for Pattern C lifecycle topics (S3 fixed), constructor has all 9 hard dependencies + IEventConsumer + constructor-cached state stores (C1-C3 fixed), internal data models defined (DeityModel, BlessingModel, AttentionSlotModel, DivinityEventModel) with proper types (C4 fixed), event handler uses async/await with telemetry span (C5/STD-2 fixed), structured logging on all 22 stub methods (STD-6 fixed). All 22 endpoints still return `NotImplemented`. Detailed implementation plan at `docs/plans/DIVINE.md`. 3 design decisions need human input (domain-to-analytics mapping, blessing template registration strategy, entity validation strategy). 2 external blockers: Puppetmaster watcher-actor integration (#383/#388) needed for god-actors. Critical upstream dependency: Status service EntityType.Character hardcoding (#415) blocks entity-agnostic blessings.

### Bug Count: 0

No known bugs (skeleton-only; bugs will emerge during implementation).

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Implement deity CRUD operations** | All 8 deity management endpoints are stubbed. This is the foundational work that everything else depends on. | No issue |
| 2 | **Implement blessing orchestration** | All 5 blessing endpoints including dual-tier storage mechanism (Collection for permanent, Status for temporary) are stubbed. Primary consumer-facing feature. Blocked on #415 (Status EntityType.Character hardcoding) for entity-agnostic blessings. | [#415](https://github.com/beyond-immersion/bannou-service/issues/415) |
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

## Director {#director-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [DIRECTOR.md](plugins/DIRECTOR.md)

### Production Readiness: 0%

Entirely pre-implementation. L4-audited (2026-03-06). Human-in-the-loop orchestration service for developer-driven event coordination, actor observation, and player audience management. Three control tiers: Observe (tap actor perception/cognitive state), Steer (inject perceptions, GOAP priority overrides, action gates), Drive (replace actor cognition with human commands via same IActionHandler pipeline). Directed events coordinate multi-actor productions with player targeting through existing Gardener/Hearsay/Quest mechanisms. Audit fixes applied: accountId→webSocketSessionId throughout (identity boundary compliance), PascalCase for all 5 enums, x-lifecycle adoption for DirectorSession and DirectedEvent CRUD events, phase transition events collapsed to `director.event.updated` with `changedFields`, metrics data removed from responses (→Analytics events), cleanup endpoint reduced from 2 to 1 (no account cleanup needed with session-based identity), config worker intervals added. 24 planned endpoints across 7 groups, 9 published events (6 x-lifecycle + 3 custom), 3 consumed events, 6 state stores, 17 configuration properties, 2 background workers, and a 7-phase implementation plan.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Schema & Generation** | Create `director-api.yaml` (24 endpoints), `director-events.yaml` (9 published, 3 consumed), `director-configuration.yaml` (17 properties), `director-client-events.yaml` (tap data relay, approval requests). Generate service code. | No issue |
| 2 | **Phase 2 - Director Session & Actor Observation** | Implement session lifecycle (start/get/end), actor tap/untap with RabbitMQ relay, tap data delivery via IClientEventPublisher, session timeout background worker. | No issue |
| 3 | **Phase 3 - Actor Steering** | Implement perception injection wrapper, `DirectorOverrideProviderFactory` as standard `IVariableProviderFactory`, GOAP override store, action gate mechanism with approval flow. | No issue |

### GH Issues

```bash
gh issue list --search "Director:" --state open
```

---

## Escrow {#escrow-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ESCROW.md](plugins/ESCROW.md)

### Production Readiness: 75%

L4-hardened (2026-03-06). The escrow coordination layer has an impressive 13-state FSM, token-based security, four escrow types, configurable release/refund modes with confirmation flows, two background services (expiration and confirmation timeout), contract integration via event subscriptions, idempotent deposits, and dispute resolution with arbiter support. Remaining gaps: `ValidateEscrow` is a placeholder that always passes (no actual cross-service asset verification), custom handler invocation is purely declarative (handlers registered but never called), periodic validation loop has no background processor, and there are no distributed locks around agreement modifications. Status index key pattern is structurally ineffective.

**Hardening fixes applied**: violations (sentinel `-1` → nullable `int?` for `RequiredConsentsForRelease`, `Guid.Empty` → `null` for system-initiated disputes), violation (config `confirmationTimeoutBehavior` → typed `ConfirmationTimeoutBehavior` enum, PascalCase defaults for `ReleaseMode`/`RefundMode`), violations (removed filler booleans `consentRecorded`/`registered`/`deregistered` from response schemas), violations (hardcoded startup delays → configurable `ExpirationStartupDelaySeconds`/`ConfirmationStartupDelaySeconds`, `ParseDuration` silent fallback → throw on invalid config, removed `ParseConfirmationTimeoutBehavior` string parser), violation (added `StartActivity` telemetry span to `EscrowExpirationService.ExecuteAsync`), NRT compliance (`additionalProperties: false` added to 62 object schemas, nullable `disputedBy`/`disputedByType` on `EscrowDisputedEvent`), W7 (added `minimum` constraints to 11 integer config properties).

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

L4-audited (2026-03-06). Pre-implementation spec hardened for tenet compliance. Not listed in GENERATED-SERVICE-DETAILS.md (no schema/code yet). A detailed architectural specification for an environmental state service providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability. Consumes temporal data from Worldstate (L2) and translates it into environmental conditions for NPC behavior, production, trade, loot, and player experience. Uses deterministic hash-based weather computation. Specifies 22 planned endpoints (was 20, +2 for undeprecate/delete per Category A), 5 state stores, 6+ published events, 3+ consumed events, 2 background workers, 1 variable provider namespace (`${environment.*}`), and a 7-phase implementation plan.

**Audit findings fixed**: violation (removed `location.deleted` event subscription, use lib-resource cleanup only), violations (lifecycle topics → Pattern C with `topic_prefix`, `conditions-changed` → `conditions.changed`), violation (climate templates declared Category A with undeprecate/delete endpoints, deprecation-guarded binding creation), violations (all `DateTime` → `DateTimeOffset`), violations (cleanup/binding/config endpoints → `[{ role: developer }]`), violations (added `DroughtThreshold`, `AbundanceThreshold`, `WindChillFactor` config properties), model improvements (`heatThreshold` field, `DefaultBiomeCode` validation constraints), schema compliance notes (`additionalProperties: false`, named `$ref` types), informational notes (ITelemetryProvider, ITransitCostModifierProvider creation, variable-providers.yaml registration, client events structure), Worldstate time dependency tracking (#532/#534/#543).

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

Pre-implementation. L4-audited (2026-03-06): deep dive specification hardened through 3 parallel audit agents (schema rules, all tenets, GitHub issues). 19 findings applied: cleanup pattern corrected (replaced `location.deleted`/`species.deleted`/`location.deprecated` event subscriptions with 3 lib-resource `x-references` cleanup endpoints), Category A deprecation lifecycle (added `undeprecate` + `delete` endpoints, `includeDeprecated` on list, cross-service deprecation checks on register/create-override), x-permissions explicitly declared (nature query + cleanup endpoints `[]` service-to-service only), PascalCase enums (`ActivityPattern`, `DietType`, `SocialStructure`, `OverrideScopeType`), `x-lifecycle` with `topic_prefix: ethology` adoption (manual event definitions removed), `ITelemetryProvider` added to DI, `IRealmClient`/`IResourceClient` added as hard dependencies, `variable-providers.yaml` registration noted, per-archetype distributed locking on seed endpoint, override deactivation/activation events collapsed to `ethology.override.updated` with `changedFields`. concern documented for `speciesCode` metadata bag risk on actor templates (GH #308). 22 planned endpoints (10 archetype + 6 override + 3 nature query + 3 cleanup), 4 state stores, 6 x-lifecycle events, 1 consumed event (informational only), 1 background worker, 1 variable provider (`${nature.*}`). 6 design considerations remain. No schema, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Archetype Definitions** | Create schemas (`x-lifecycle`, `x-references`, PascalCase enums, `x-permissions`), generate code, implement archetype CRUD with Category A deprecation lifecycle (deprecate/undeprecate/delete), bulk seed with per-archetype locking, species.deprecated informational handler, 3 lib-resource cleanup endpoints. | No issue |
| 2 | **Phase 2 - Nature Resolution** | Implement three-layer resolution algorithm (archetype + overrides + deterministic noise via MurmurHash3), `NatureProviderFactory` as `IVariableProviderFactory`, Redis caching, ResolveNature/ResolveNatureBatch/CompareNatures endpoints. | No issue |
| 3 | **Phase 4 - Character Delegation** | Implement Heritage-aware resolution: when entity is a character with Heritage data, use phenotype values for mapped axes (skipping noise), falling back to species archetype for unmapped axes. Graceful degradation when Character-Lifecycle unavailable. | No issue |

### GH Issues

```bash
gh issue list --search "Ethology:" --state open
```

---

## Faction {#faction-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [FACTION.md](plugins/FACTION.md)

### Production Readiness: 85%

All 31 endpoints are fully implemented with business logic -- CRUD, membership management, territory claims, norm definitions, cleanup, and compression all work. The seed-based growth pipeline, norm resolution hierarchy, `ISeedEvolutionListener`/`ICollectionUnlockListener` DI integrations, and `IVariableProviderFactory` (`${faction.*}` namespace) are operational. L4 production audit (2026-03-06) applied comprehensive corrections: filler removal from 5 response models, deprecation lifecycle (triple-field model with `deprecatedAt`/`deprecationReason`, idempotent deprecate/undeprecate, delete guard requiring deprecation, `includeDeprecated` filter on list), collection growth amount made configurable, event schemas corrected to allOf + BaseServiceEvent per SCHEMA-RULES, tenet number removed from events schema. Variable provider is missing critical norm/territory variables, lib-contract integration for guild charters is absent, and lib-obligation integration (the primary consumer) is not yet wired.

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

### Audit Log

**L4 Production Audit (2026-03-06)**:
- **Schema (faction-events.yaml)**: Flattened all 9 custom events (removed `allOf` with `BaseServiceEvent`, added inline `eventId`/`timestamp`), added `deprecatedAt`/`deprecationReason` to x-lifecycle Faction model, added `minLength`/`maxLength` to `violationType` in norm events, removed tenet number reference, added `x-event-subscriptions: []`
- **Schema (faction-api.yaml)**: filler removed from `QueryApplicableNormsResponse` (characterId, realmId, locationId), `CheckMembershipResponse` (factionId, characterId), `ListMembershipsByCharacterResponse` (characterId), `CleanupByCharacterResponse`/`CleanupByRealmResponse`/`CleanupByLocationResponse` (success boolean), `RestoreFromArchiveResponse` (characterId, success, boolean→count). deprecation fields (`deprecatedAt`, `deprecationReason`) added to `FactionResponse` and `DeprecateFactionRequest`. `includeDeprecated` filter added to `ListFactionsRequest`. Validation added to `CharacterMembershipEntry`, `ApplicableNormEntry`, `RestoreFromArchiveRequest.data`
- **Schema (faction-configuration.yaml)**: Added `CollectionGrowthAmount` config property, `minLength`/`maxLength` on `SeedTypeCode`
- **Code (FactionService.cs)**: deprecation lifecycle (deprecatedAt/deprecationReason in model+response, idempotent deprecate/undeprecate with dissolved guard, delete requires deprecation, includeDeprecated filter). filler field assignments removed from 7 response constructions. RestoreFromArchive changed from boolean to count tracking
- **Code (FactionCollectionUnlockListener.cs)**: Replaced hardcoded `1.0f` growth amount with `_configuration.CollectionGrowthAmount`. Fixed `TryPublishErrorAsync` to use `ex.GetType().Name` instead of custom string. Replaced hardcoded key strings with `FactionService.*Key()` builders
- **Code (FactionProviderFactory.cs)**: Replaced 4 hardcoded key strings with shared `FactionService.*Key()` builders (consistency risk elimination)
- **Code (FactionSeedEvolutionListener.cs)**: Added missing `ChangedFields` to `FactionUpdatedEvent` published from `OnPhaseChangedAsync`. Added `DeprecatedAt`/`DeprecationReason` to event payload. Replaced hardcoded key strings with shared builders
- **Code (FactionService.cs event helpers)**: Added `DeprecatedAt`/`DeprecationReason` to `PublishCreatedEventAsync` and `PublishUpdatedEventAsync` event payloads. Key builders changed from `private static` to `internal static` for provider access
- **telemetry**: Already compliant -- 31 controller spans + 7 private helper spans + 5 provider spans

### GH Issues

```bash
gh issue list --search "Faction:" --state open
```

---

## Gardener {#gardener-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [GARDENER.md](plugins/GARDENER.md)

### Production Readiness: 65%

The void/discovery garden type is functional with 23 endpoints (delete removed per Category B) -- garden lifecycle, POI interaction with weighted scoring, scenario management with growth awards, template CRUD with deprecation lifecycle, phase management, bond features, and two background workers. L4-audited (2026-03-06): filler removed from 6 responses (acknowledged booleans, echoed request fields), Category B compliance (delete endpoint removed, idempotent deprecation, includeDeprecated on list), GameType extracted to config, Guid.Empty sentinel replaced with null check, event schemas corrected to allOf + BaseServiceEvent (12 custom events), lifecycle topic prefix added (`gardener.scenario-template.*`). However, the broader garden concept is unimplemented: no garden-to-garden transitions, no multiple garden types, no per-garden entity associations, no entity session registry, no divine actor integration (uses background workers instead of per-player actors), implementation gaps in the current void garden (missing prerequisite validation, no per-template concurrent instance limits, MinGrowthPhase not functional, no client events, Puppetmaster notification is log-only), and no content flywheel integration.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Client event schema** | No client events exist for real-time POI push to WebSocket clients. POI spawns, expirations, and triggers happen server-side only. Clients must poll to discover changes. | No issue |
| 2 | **Prerequisite validation during scenario entry** | Templates store prerequisites but they are never validated in `EnterScenarioAsync` or `GetEligibleTemplatesAsync`. A player can enter any scenario regardless. | No issue |
| 3 | **Entity Session Registry** | Cross-cutting infrastructure for mapping entities to WebSocket sessions, hosted in Connect (L1). Required for real-time client event routing from entity-based services. | [#426](https://github.com/beyond-immersion/bannou-service/issues/426) (CLOSED) / [#502](https://github.com/beyond-immersion/bannou-service/issues/502) (rollout) |

### L4 Audit Changes (2026-03-06)

| Change | Category | Description |
|--------|----------|-------------|
| filler removed | Schema + Code | Removed `Acknowledged`, echoed `PoiId`, `ScenarioInstanceId`, `AccountId` from 6 responses |
| Category B | Schema + Code | Removed delete endpoint (23 endpoints), idempotent deprecation, `includeDeprecated` on list |
| config | Schema + Code | Extracted `GameType` to configuration (3 hardcoded strings replaced) |
| sentinel | Code | Replaced `Guid.Empty` check with null check in lifecycle worker |
| Event schemas | Schema | Flattened 12 custom events (inline eventId/timestamp, no eventName) |
| Lifecycle topics | Schema + Code | Added `topic_prefix: gardener` to x-lifecycle; updated topic strings in service code |

### GH Issues

```bash
gh issue list --search "Gardener:" --state open
```

---

## Hearsay {#hearsay-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [HEARSAY.md](plugins/HEARSAY.md)

### Production Readiness: 0%

Entirely pre-implementation. L4-audited (2026-03-06): 8 tenet-mandated fixes applied to spec. `BeliefDomain` enum (Norm/Character/Location) and `SourceChannel` enum (DirectObservation/OfficialDecree/TrustedContact/SocialContact/Rumor/CulturalOsmosis) replace strings (Category C,). `subjectId` composite GUID-in-string split to typed `subjectEntityId: Guid`. Consumed event topic corrected from `character-encounter.created` to `encounter.recorded` (factual error — actual topic verified in schema). Obligation added to Soft Dependencies (event-only). ITelemetryProvider added to DI Services. Phase 1 prerequisites (state-stores.yaml, variable-providers.yaml) noted. `x-archive-type: true` noted on HearsayArchive. 9 design considerations remain (6 original + 3 from audit: x-permissions per endpoint, config distance units, GH issue cross-references). Three belief domains acquired through six information channels with confidence mechanics, time-based decay, proximity-based convergence toward ground truth, and rumor injection. Provides `${hearsay.*}` ABML variables via Variable Provider Factory. 18 planned endpoints, 8 published events, 8 consumed events, 4 state stores, 20 configuration properties, 3 background workers, and a 6-phase implementation plan. No schema, no code.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Belief Infrastructure** | Add stores to state-stores.yaml/variable-providers.yaml, create schemas (with BeliefDomain/SourceChannel enums, x-archive-type), generate code, implement belief CRUD, belief cache with Redis invalidation, variable provider factory (`${hearsay.*}`), and resource cleanup/compression callbacks. | No issue |
| 2 | **Phase 2 - Propagation Engine** | Implement encounter-triggered belief propagation (via `encounter.recorded` event), faction event-driven belief injection (territory claimed, norm defined), rumor injection API, propagation worker advancing rumor waves through social networks, and telephone-game distortion mechanics. | No issue |
| 3 | **Phase 4 - Storyline Integration** | Implement the dramatic irony endpoint (`QueryBeliefDelta` -- beliefs alongside ground truth with narrative weight classification), belief saturation queries for scenario preconditions, and narrative protection preventing convergence from breaking active storylines. | No issue |

### GH Issues

```bash
gh issue list --search "Hearsay:" --state open
```

---

## Leaderboard {#leaderboard-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LEADERBOARD.md](plugins/LEADERBOARD.md)

### Production Readiness: 85%

Production-hardened (2026-03-06). Core leaderboard functionality is solid: Redis Sorted Set-backed rankings with O(log N) operations, polymorphic entity types, four score update modes, seasonal rotation, event-driven score ingestion from Analytics via typed `scoreType`/`ratingType` fields (no metadata bag reads), percentile calculations, neighbor queries, and full lifecycle event coverage (definition created/updated/deleted). All schemas are NRT-compliant with proper validation constraints. Remaining gaps: `IncludeArchived` filtering returns NotImplemented, batch submit ignores UpdateMode, season timestamps are approximated, an unused MySQL state store exists, no score validation/bounds, no distributed locks for non-atomic read-calculate-write, and batch submit has no event publishing. 6 design decisions deferred with `AUDIT:NEEDS_DESIGN` markers.

### Bug Count: 0

No known bugs. metadata bag violation fixed (2026-03-06).

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

Pre-implementation. L4-audited (2026-03-06) against SCHEMA-RULES and all developer tenets. The deep dive is a detailed architectural specification for a structured world knowledge ontology — the missing NPC world-knowledge layer. Four interconnected pillars: **entries** (things that can be known), **traits** (decomposed observable characteristics), **categories** (hierarchical classification), and **associations** (bidirectional concept links with discovery-tier gating). Also defines **strategies** (trait/category-derived implications for GOAP). Part of a three-service knowledge stack: Lexicon (ground truth) + Collection (discovery tracking) + Hearsay (subjective belief). Specifies 26 planned endpoints (was 23, +3 from deprecation), 3 state stores, 8+ published events, 2+ consumed events, 1 variable provider namespace (`${lexicon.*}`), and a 7-phase implementation plan.

**Audit fixes applied to deep dive**:
- Category A deprecation lifecycle added to LexiconEntry (isDeprecated/deprecatedAt/deprecationReason fields, DeprecateEntry/UndeprecateEntry/updated DeleteEntry endpoints, includeDeprecated query parameter)
- Replaced `preconditions: object?` with typed `StrategyPrecondition[]?` model (Lexicon reads precondition keys = violates metadata bag contract)
- Added `AppliesToType` enum (PascalCase: Trait, Category) replacing raw string
- ICollectionClient moved from soft to hard dependency (Collection is L2 since hierarchy v2.6)
- ITelemetryProvider added to DI services table
- x-lifecycle requirement annotated on all CRUD published events; missing events added (strategy.deleted, category.updated, category.deleted)
- Bidirectional resource cleanup documented (Lexicon as resource target and cleanup implementor)
- **Quirks #11-14 added**: Trait/strategy no-deprecation rationale, no client events, all endpoints x-permissions: [], manifest cache invalidation pattern

**6 design decisions deferred** (marked AUDIT:NEEDS_DESIGN in deep dive DC#12-17):
- DC#12: sourceType typing (string vs enum — depends on whether non-service sources exist)
- DC#13: sourceId typing (string vs Guid — depends on whether non-Guid sources exist)
- DC#14: metadata: object? on associations (ambiguous for same-service opaque pass-through)
- DC#15: Category deprecation (ambiguous for same-service sub-entities)
- DC#16: discoveryLevels rename to match Bannou naming conventions
- DC#17: Compression callback (whether Lexicon data participates in lib-resource compression)

No schema, no generated code, no service implementation exists.

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

### Production Readiness: 95%

L4-hardened (2026-03-06). Zero code tenet violations across all manual source files. Full audit: schema NRT validation constraints added (config min/max, GridPosition bounds, code/name minLength, seed minItems), PascalCase enum descriptions, defaultAdjacencyMode default fixed to EightWay, cleanup-by-owner x-permissions corrected to [] (service-to-service), currentLp type fixed from number to integer, state-store purpose text corrected for polymorphic ownership, stale comment fixed. 59 tests passing, 0 warnings. Only extension remaining is board reset/respec (#356, needs game design).

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

### Production Readiness: 85%

The core matchmaking loop works: ticket creation, background queue processing with skill window expansion, match formation, accept/decline flow with distributed locks, reconnection support, game session creation, and join shortcut publishing. Queue statistics are placeholder (stub #225), tournament support is declared but unimplemented, and a defined event type is never published. Two hardening passes completed: initial pass fixed account identity in events, sentinel values, hardcoded lock timeouts, silent exception swallowing, NRT compliance; validation pass fixed client event account leak (`partyMembersMatched` → `partyId`), echoed fields (`queueId` in JoinMatchmakingResponse, `matchId` in AcceptMatchResponse). One design decision remains open: `_sessionAccountMap` in-memory state.

**Last audit**: 2026-03-06

### Bug Count: 0

All bugs resolved. Reconnection shortcut bug fixed 2026-03-03. Violations fixed 2026-03-06. Validation pass fixes 2026-03-06.

### Top 3 Bugs

*(None — all resolved)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Queue statistics computation** | `MatchesFormedLastHour`, `AverageWaitSeconds`, `MedianWaitSeconds`, `TimeoutRatePercent`, `CancelRatePercent` are all placeholder zeros. Critical for operational visibility. | [#225](https://github.com/beyond-immersion/bannou-service/issues/225) |
| 2 | **Tournament support** | `TournamentIdRequired` and `TournamentId` fields exist on tickets but no tournament-specific matching logic is implemented. | No issue |
| 3 | **Fix `_sessionAccountMap` violation** | `ConcurrentDictionary` in `MatchmakingServiceEvents.cs` is authoritative in-memory state not loaded at startup. Needs design decision on fix approach. | See deep dive B2 |

### GH Issues

```bash
gh issue list --search "Matchmaking:" --state open
```

---

## Music {#music-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [MUSIC.md](plugins/MUSIC.md)

### Production Readiness: 92%

Fully functional composition pipeline with full tenet compliance (hardened 2026-03-06, x-sdk-type remediated 2026-03-10). All 8 endpoints work except CreateStyle which does not persist. Schema enums extracted to named PascalCase types with `$ref` reuse. All filler properties removed. Type safety enforced (CompositionId is Guid, Contour is enum). NRT compliance verified. A2 boundary mapper (`MusicServiceMapper.cs`) cleanly separates generated API types from SDK computation types. 210 unit tests pass (including 8 enum boundary coverage tests). Zero build warnings. The remaining gaps are the unpersisted custom styles (CreateStyle is a stub, MySQL store declared but unused) and no rate limiting on CPU-intensive generation.

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

### Production Readiness: 92%

All 11 endpoints are fully implemented with complete business logic. L4-audited (2026-03-06): filler removed (Success booleans from 3 responses, CharacterId echoes from 5 responses), x-permissions fixed on 3 service-to-service endpoints, sentinel values eliminated (4x `?? "unknown"` replaced with nullable types across API/events/internal models), hardcoded tunables moved to config (CleanupBatchSize, MaxCompressionQueryResults, PersonalityWeightMultiplier), ApiException catch added on inter-service call, schema validation improved (maxItems on 6 arrays, minLength on archive data). Contract-driven obligation extraction, personality-weighted cost computation, violation reporting with idempotency, event-driven cache management, and the `IVariableProviderFactory` (`${obligations.*}` namespace) all work end-to-end. Remaining work: 2 feature gaps (event template registration, reference tracking helpers), 4 design considerations (ViolationTypeTraitMap mechanism, contract clause format, faction bridge, multi-channel costs), and integration connections.

### Bug Count: 0

### Top 3 Bugs

*(None — former hardcoded trait map bug reclassified as design consideration after PersonalityWeightMultiplier made configurable)*

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

### Production Readiness: 60%

The architectural skeleton is well-designed: behavior document caching and provider chain integration, resource snapshot caching for Event Brain actors, ABML action handlers (load_snapshot, prefetch_snapshots, spawn/stop/list_watchers, watch/unwatch), a watch system with dual-indexed registry and dynamic lifecycle event subscriptions, and realm event handling for watcher lifecycle. Configuration is fully wired (5 properties). No bugs. Schema and code hardened: event schemas flattened (no allOf/BaseServiceEvent), filler response properties removed (isHealthy, stopped), configuration validation constraints added, async patterns fixed, telemetry spans added to handlers, ConcurrentDictionary enforced on ResourceEventMapping, DateTimeOffset on WatchPerception, GetRequiredService for guaranteed IPuppetmasterClient. However, the most critical feature -- watcher-actor integration -- is stubbed: watchers are registered as data structures in memory but never spawn actual actors (ActorId is always null). Without this, regional watchers, divine actors, and encounter coordinators cannot execute behavior. All state is in-memory only (lost on restart), and the multi-instance story is broken.

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

### Production Readiness: 95%

**Hardened.** Feature-complete with all 12 endpoints implemented, no stubs, and no bugs. 40 unit tests with capture-pattern event/state verification and lock failure path coverage. Hardening pass (2026-03-07) migrated all enums to PascalCase, removed filler response properties (realmId from RealmLoreResponse/archive, success/errorMessage), fixed 409 schema description (lock contention, not deduplication), NRT nullable array compliance, config validation constraints, compression callback priority fix, private method XML docs. Shares storage helper abstractions with character-history (DualIndexHelper, BackstoryStorageHelper), has proper distributed locking, resource cleanup/compression integration, and server-side paginated queries.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Event-level aggregation** | Compute aggregate impact scores per event across all participating realms. | [#266](https://github.com/beyond-immersion/bannou-service/issues/266) |
| 2 | **Typed metadata schemas** | Replace `object?`/`additionalProperties:true` metadata pattern with typed schemas. Systemic issue affecting 14+ services. | [#308](https://github.com/beyond-immersion/bannou-service/issues/308) |
| 3 | **Timeline visualization** | Chronological event data suitable for timeline UI rendering. May be satisfied by existing sorted query. | [#270](https://github.com/beyond-immersion/bannou-service/issues/270) |

### GH Issues

```bash
gh issue list --search "Realm History:" --state open
```

---

## Save-Load {#save-load-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [SAVE-LOAD.md](plugins/SAVE-LOAD.md)

### Production Readiness: 92%

**Hardened (3x)** (2026-03-09). Phase 3 hardening: sentinel elimination (all nullable int/Guid/enum fields use `.HasValue` instead of `> 0`/`== 0`/`== default`; `FindVersionByCheckpointAsync` returns `int?` not `int`), config-first (4 hardcoded tunables → config: `SlotMetadataLockTimeoutSeconds`, `SlotWriteLockTimeoutSeconds`, `ExportUrlExpiryMinutes`, `MaxMigrationSteps`), removed echoed `dryRun` from `AdminCleanupResponse`, fixed `PendingUploadEntry.SlotName` (was using `SlotId.ToString()`), IAssetClient soft dependency extended to all 4 helper services (`VersionDataLoader`, `VersionCleanupManager`, `SaveExportImportManager`, `SaveMigrationHandler`), typed `BuildStateKey` overload accepting `EntityType`+`Guid` directly (updated ~15 call sites). Phase 2 (2026-03-09): account cleanup, L3 soft deps in main service, spans, key builders, thumbnail config, metadata, NRT, maxItems, gameId additions, background worker safety. Phase 1 (2026-03-07): schema cleaned, code cleaned, Pattern C topics. Architecture: two-tier storage (Redis+MinIO), delta saves, schema migration with BFS, circuit breaker, export/import, rolling cleanup, distributed locking, 45+ config properties. 100/100 unit tests passing. No bugs. Remaining stubs: BSDIFF/XDELTA throw NotSupportedException, JSON Schema validation is a no-op, per-owner quota enforcement only checks per-slot.

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

### Production Readiness: 97%

**Hardened (2x)** (2026-03-09). All 19 endpoints implemented with solid structural validation, checkout/commit workflow with optimistic concurrency, reference resolution with circular detection, and secondary indexing. No bugs. Second hardening pass: converted SceneType/AffordanceType/MarkerType from enums to opaque strings (Category B), made gameId nullable throughout, replaced all ~60 inline key interpolations with Build*Key() methods, removed "unknown" EditorId sentinel with IMeshInstanceIdentifier default, removed dead key prefix constants, replaced string.Empty defaults with `required` keyword on internal models, added constructor validator and key builder tests (97/97 passing). Events schema uses $ref to API spatial types (no duplication). Issues #254 and #257 commented as stale (referenced events no longer exist). Remaining gaps: version-specific content retrieval is a no-op, soft-delete has no recovery mechanism, search is brute-force global scan.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Version content snapshots** | Only version metadata is retained -- actual YAML content per version is not preserved, making version-specific retrieval and rollback impossible. | [#187](https://github.com/beyond-immersion/bannou-service/issues/187) |
| 2 | **Background checkout expiry** | No background service monitors stale checkouts; expiry is only checked lazily on next checkout attempt. Event type would need to be added when implementing. | [#254](https://github.com/beyond-immersion/bannou-service/issues/254) |
| 3 | **Full-text search with proper indexing** | Current `SearchScenes` is a brute-force global index scan loading all scene IDs; needs Redis Search or equivalent for sub-millisecond queries at scale. | No issue |

### GH Issues

```bash
gh issue list --search "Scene:" --state open
```

---

## Status {#status-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [STATUS.md](plugins/STATUS.md)

### Production Readiness: 93%

All 19 API endpoints fully implemented -- template CRUD with seeding, Category A deprecation lifecycle (deprecate, undeprecate, delete with grant guard), grant flow with 5 stacking behaviors, remove/cleanse operations, unified effects query merging item-based and seed-derived effects, client events via Entity Session Registry, and resource cleanup. The dual-source architecture is operational with lib-divine as the first consumer. Hardening pass (2026-03-07) fixed cache key casing mismatch, hardcoded EntityType.Character in contract ops, metadata type-narrowing, and client event naming. Blocking dependency on #407 (Item Decay/Expiration) means TTL-based status expiration relies on lazy cleanup during cache rebuilds.

### Bug Count: 0

No known bugs.

### Top 3 Bugs

*(None)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Item Decay/Expiration System** | Blocking dependency. Without `item.expired` events from lib-item, TTL-based status expiration relies on lazy cleanup during cache rebuild. Combat buffs and divine blessings may appear active after expiry. | [#407](https://github.com/beyond-immersion/bannou-service/issues/407) |
| 2 | **Variable Provider Factory for ABML** | `IStatusVariableProviderFactory` providing `${status.has_buff.<code>}`, `${status.is_dead}`, `${status.active_count}` is critical. Without it, NPCs cannot react to active effects in ABML behavior logic. | No issue |
| 3 | **Cache warming implementation** | `CacheWarmingEnabled` config exists but has no functional effect. Would proactively populate active status cache for recently active entities on startup. | [#412](https://github.com/beyond-immersion/bannou-service/issues/412) |

### GH Issues

```bash
gh issue list --search "Status:" --state open
```

---

## Storyline {#storyline-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [STORYLINE.md](plugins/STORYLINE.md) | **Map**: [STORYLINE.md](maps/STORYLINE.md)

### Production Readiness: 85%

**Hardened (2026-03-09).** All 15 endpoints implemented across composition (compose, get plan, list plans, delete plan), scenario definitions (CRUD, deprecate, list with filtering), scenario discovery (find available, test trigger, evaluate fit), and scenario execution (trigger with mutations/quest hooks, get active, get history, get compress data). Wraps `storyline-theory` and `storyline-storyteller` SDKs for seeded narrative generation from compressed archives.

**Audit fixes applied**: Build*Key() pattern for all state store keys (plan, scenario definition, execution, cooldown, active, lock, plan index). all inline topic strings replaced with `StorylinePublishedTopics` constants; lifecycle event names fixed (no service prefix). try-catch narrowed to SDK call sites + ApiException for inter-service calls. `Found` filler boolean removed from GetPlanResponse and GetScenarioDefinitionResponse. enum `.ToString()` removed from event publishing (goal, arcType, primarySpectrum now use `$ref` types); ScenarioMutation `experienceType` and `backstoryElementType` changed from `type: string` to Storyline-owned enums (`StorylineExperienceType`, `StorylineBackstoryElementType`) with A2 boundary `MapByName` mapping to CharacterPersonality/CharacterHistory enums — EnumMappingValidator subset tests added. topic rename `storyline.composed` → `storyline.plan.composed` (Pattern C multi-entity naming). dead config property `ScenarioFitScoreRecommendThreshold` removed. Category B deprecation lifecycle for ScenarioDefinition. x-permissions on /storyline/get-compress-data corrected to `[]`. ServiceConstructorValidator test added. 62 unit tests passing.

**Remaining gaps**: Iterative composition (ContinuePhase) not exposed via HTTP. EntitiesToSpawn always null. No event subscriptions for content flywheel integration (resource.compressed).

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

**Spec audit complete (2026-03-03)**: Deep dive L4-audited for TENET compliance. Fixes applied: all event topics normalized to Pattern C (`affix.modifier.applied`, not `affix.applied`), Category B deprecation lifecycle (no delete, no undeprecate), instance creation guard on ApplyAffix (rejects deprecated definitions), `spawnTagModifiers` typed as array (not freeform object), `isActive` removed (redundant with deprecation), `effectiveRarity` documented as Category B opaque string, IInventoryClient reclassified from soft to hard dependency (L2), ITelemetryProvider and IResourceClient added to hard dependencies, orphan reconciliation background worker added for event-based cleanup durability, `x-permissions` specified on all endpoint groups, consumed event topics fixed (`item.instance.destroyed` not `item-instance.destroyed`).

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Definition Infrastructure** | Create schemas, generate code, implement affix definition CRUD and implicit mapping management as the foundational layer. | [#490](https://github.com/beyond-immersion/bannou-service/issues/490) |
| 2 | **Phase 2 - Instance Store and Core Operations** | Implement instance MySQL store, InitializeItemAffixes, ApplyAffix with full validation, event-based cleanup with orphan reconciliation worker. | [#490](https://github.com/beyond-immersion/bannou-service/issues/490) |
| 3 | **Phase 3 - Generation Engine** | Implement weighted random pool generation with cached pools for 100K NPC item evaluation scale. | [#490](https://github.com/beyond-immersion/bannou-service/issues/490) |

### GH Issues

```bash
gh issue list --search "Affix:" --state open
```

---

## Arbitration {#arbitration-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ARBITRATION.md](plugins/ARBITRATION.md)

### Production Readiness: 0%

Entirely pre-implementation. L4-audited (2026-03-05): 6 critical spec violations found and all fixed in-document. Deep dive describes a dispute resolution orchestration layer (like Quest over Contract) with 25 planned endpoints (8 case + 3 evidence + 4 arbiter + 4 ruling + 2 jurisdiction + 4 cleanup), 17 published events (3 x-lifecycle + 14 domain-specific), 6 consumed events, 6 state stores, and a 6-phase plan. Well-designed orchestration patterns (correct compliance, no accountId, comprehensive locking). Schema creation guidance section added with all tenet-compliant requirements. 8 design considerations remain open (evidence model, household split boundary, sovereignty transfer, deadline granularity, multi-game portability). 2 unmet prerequisites with no GH tracking issues.

**Audit fixes applied:**
1. Faction moved from hard to soft dependency (L4→L4 per SERVICE-HIERARCHY.md)
2. Governance parameters clarified as genuinely opaque pass-through (); timeline computation derives from Contract milestone deadlines, not governance parameter inspection
3. All 6 enums (14+ values) fixed to PascalCase
4. Event topic `divine_requested` → `divine-requested` (kebab-case)
5. Event topic `arbitration.service.confirmed` → `arbitration.notice.confirmed` (disambiguated)
6. x-lifecycle added for ArbitrationCase entity; allOf + BaseServiceEvent event structure (Quest pattern)
7. Resource cleanup expanded to 4 targets (added faction, location)
8. Broken planning doc reference removed
9. Work tracking populated with 6 related GH issues
10. EntityType committed (removed "or string" hedge)

**Prerequisites (both unmet, neither has a GH issue):** Faction sovereignty (`authorityLevel` field, governance data, delegation, `QueryGovernanceData`) and Obligation multi-channel costs (legal/social/personal tagging).

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Faction Sovereignty (Phase 0 prerequisite)** | Add `authorityLevel` enum to Faction for jurisdiction determination -- without this, Arbitration cannot function at all. No GH issue tracks this critical prerequisite. | No issue |
| 2 | **Core Arbitration Infrastructure (Phase 2)** | Create schemas, generate code, implement case management CRUD with jurisdiction resolution and contract template integration. No master tracking issue exists. | No issue |
| 3 | **Precedent System** | Accumulated rulings per case type form case law that NPC arbiters reference in cognition, creating emergent legal tradition and content flywheel material. | No issue |

### GH Issues

Related open issues (no Arbitration-specific issues exist yet): #435 (sovereignty transfer), #436 (household split), #410 (Second Thoughts prerequisites), #362 (seed bond dissolution), #560 (contract hierarchy violation), #153 (escrow integration).

```bash
gh issue list --search "Arbitration:" --state open
```

---

## Broadcast {#broadcast-status}

**Layer**: L3 AppFeatures | **Deep Dive**: [BROADCAST.md](plugins/BROADCAST.md)

### Production Readiness: 0%

Pre-implementation. Deep dive L3-audited (2026-03-01): ~20 design violations against current tenets and schema rules resolved in-document. Key fixes: `x-lifecycle` for all CRUD lifecycle events (platform-link, platform-session, broadcast-output), Redis-backed tracking ID mapping (multi-instance safety), camera events replaced with API endpoints (no orphaned events), empty-string config defaults changed to `nullable: true`, codec configs changed to enums enforcing LGPL compliance, 8 background worker intervals promoted to config properties, webhook endpoints documented as justified exception with `x-permissions: []` and `x-controller-only: true`, `SentimentPulse.pulseId` renamed to `eventId`, `x-service-layer: AppFeatures` added to Phase 1 checklist, `x-references` block specified for lib-resource cleanup, `associate` endpoint clarified as opaque GUID storage (no L4 validation), `IBroadcastCoordinator` redesigned as non-authoritative local process cache with Redis as source of truth, `TokenRefreshWorker` distributed lock resolved, 2 missing consumed events added (`voice.participant.muted`, `session.disconnected`), 5 client events designed. 22 planned endpoints across 6 groups, 38 configuration properties, 7 state stores, 6 background workers, 5-phase implementation plan. No schema, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1: Schema & Generation** | Create all schema files (api, events with x-lifecycle, configuration with 38 properties, client-events with 5 events), define AudioCodec/VideoCodec enums, add x-references, generate service code. Foundation for all subsequent phases. | No issue |
| 2 | **Phase 3: Platform Session Management** | Implement Twitch EventSub/YouTube webhook handlers (x-controller-only, HMAC validation), sentiment processor (keyword/emoji matching via ISentimentProcessor), Redis-backed tracked viewer mapping, sentiment batch publisher. | No issue |
| 3 | **Phase 4: Output Management** | Implement IBroadcastCoordinator as per-instance process supervisor with Redis-authoritative state, startup reconciliation, fallback cascade, camera announce/retire API, voice mute event handling, health monitor with stale record detection. | No issue |

### GH Issues

```bash
gh issue list --search "Broadcast:" --state open
```

---

## Craft {#craft-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [CRAFT.md](plugins/CRAFT.md)

### Production Readiness: 0%

Pre-implementation. L4-audited (2026-03-06). Detailed architectural specification for recipe-based crafting orchestration: production (material-to-item), modification (lib-affix operations via typed `AffixOperationType` enum), and extraction (item destruction for components). Contract-backed multi-step sessions, seed-integrated proficiency tracking (lib-seed only, no local fallback), station/tool validation, quality formulas, and recipe discovery. 28 endpoints across 7 groups, 6 state stores, variable provider factory (`${craft.*}`), 3 resource cleanup targets (game-service, character, location). Audit fixes: Pattern C event topics (10 topics), typed models replacing `object?` fields, Category C enum for affix operations, Category B with deprecation guard, `isActive` removed, local proficiency fallback removed, x-permissions on all groups. 7 design considerations remain (quality-to-affix mapping, concurrent modification, real-time durations, seed type registration, cross-entity crafting, material quality propagation, offline NPC crafting). New "Archive Shape" sub-tenet added to FOUNDATION.md. No schema, no generated code, no service implementation exists.

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

Pre-implementation architectural specification. L4-audited (2026-03-06). 25 endpoints planned (8 core + 4 bond + 4 inhabitant + 4 memory + 2 domain + 3 cleanup). Dungeon-as-actor lifecycle orchestration: living dungeon entities with dual mastery patterns (full split vs bonded role), seed-based progressive growth (`dungeon_core` + `dungeon_master`), mana economy via Currency, Contract-backed master bonds, dynamic character binding (3-stage cognitive progression: Dormant → Event Brain → Character Brain), memory capture/manifestation, and integration with Actor, Puppetmaster, Gardener, Mapping, Scene, Save-Load, Item, Inventory. Audit fixes applied: (personalityType → DungeonCoreModel, not seed metadata), (IItemClient/IInventoryClient → hard deps), (3 event topics corrected to Pattern A), DungeonStatus enum added, game-service cleanup target added, ITelemetryProvider in DI, domain management permissions, config validation constraints, GrowthContributionDebounceMs config property, no-deprecation classification, inhabitant store durability note, Design Consideration #3 resolved (#422 creature_base exists). 3 blocking dependencies (#436 household split, #437 seed promotion, #362 bond dissolution — Pattern A only for first two). 10 design considerations remain (3 resolved), 7 missing GH issues need creation. No schema, no generated code, no implementation.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Phase 1 - Core Infrastructure** | Create schemas, generate code, implement dungeon core CRUD with Seed/Currency/Actor provisioning and variable provider factories. Requires design decisions on mana economy model and system realm provisioning. | No issue |
| 2 | **Phase 1.5 - Cognitive Progression** | Implement `HandleSeedPhaseChangedAsync` for Stirring (actor spawn) and Awakened (character creation + dynamic binding) transitions. Requires character creation timing design. | No issue |
| 3 | **Phase 2 - Dungeon Master Bond** | Implement Contract-backed bond formation flow with master seed creation and perception injection via character Actor. Blocked by [#362](https://github.com/beyond-immersion/bannou-service/issues/362). | [#362](https://github.com/beyond-immersion/bannou-service/issues/362) |

### GH Issues

```bash
gh issue list --search "Dungeon:" --state open
```

---

## Loot {#loot-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [LOOT.md](plugins/LOOT.md)

### Production Readiness: 0%

Aspirational/planned only. The deep dive explicitly states "Pre-implementation. No schema, no code." Not listed in GENERATED-SERVICE-DETAILS.md. Deep dive specification has been audited and hardened for tenet compliance: event topics converted to Pattern C (`loot.table.created`, `loot.pity.triggered`), all enum values converted to PascalCase (`GenerationTier`, `EntryType`, `RollMode`, `DistributionMode`, `PityCounterScope`, `QuantityCurve`, `NeedGreedDeclaration`), untyped `object` fields replaced with typed models (`map<string, double>`, `LootItemOverrides`), `decimal` types corrected to `double`, x-permissions declared on all endpoint groups, hard/soft dependency classification corrected (Currency and Character moved to hard L2 deps), consumed event corrected from `item-template.deprecated` to `item-template.updated`, Tier 3 affix flow updated per (lib-affix owns its own state), and 19 design considerations documented for implementation-time decisions. No endpoints, no generated code, no service implementation exists.

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

Entirely pre-implementation. L4-audited (2026-03-06): deep dive specification hardened through 3 parallel audit agents (schema rules, all tenets, GitHub issues) plus a validation agent for mechanical tenet application. 14 tenet-mandated fixes applied: Pattern C event topics (`market.definition.*` not `market-definition.*`, `topic_prefix: market` on x-lifecycle), PascalCase enum values across all 10 enum types (CatalogType, ListingStatus, MarketDefinitionStatus, VendorStatus, BidStatus, PriceGranularity, AuctionSortOrder, PriceTrend, SupplySignal + sort order values), MarketDefinition classified as configuration entity (no deprecation), filler removed from vendor buy response (`item + receipt` → `itemInstanceId`), distributed lock added to SetStock endpoint, ITelemetryProvider added to DI services, variable provider namespace overlap fixed (`${market.price.*}` → `${market-price.*}`), x-lifecycle specification added for MarketDefinition and VendorCatalog entities, x-event-subscriptions/publications declared, x-permissions specified on all 6 endpoint groups, x-references corrected with `field` and `payloadTemplate` columns, vendor lifecycle events added (3 x-lifecycle events). VendorWalletOwnerType config removed (Currency uses EntityType enum). 4 design decisions deferred (DC#9-12: MarketEntityType classification, player-facing x-permissions, vendor wallet EntityType, requirementsMet trust boundary). Two subsystems (auction houses + NPC vendor catalogs), 28 planned endpoints, 17 published events (6 x-lifecycle + 11 custom), 1 consumed event, 9 state stores, 21 configuration properties, 3 background workers, 2 Variable Provider Factories. Phase 0 blocked on lib-escrow asset movement (#153, #222). No schema, no generated code, no service implementation.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Bidding & Settlement (Phase 2)** | Implement bid placement with Currency hold reservation, outbid flow, buyout, and the MarketSettlementService background worker for expired auction settlement. | [#427](https://github.com/beyond-immersion/bannou-service/issues/427) |
| 2 | **Variable Provider Integration (Phase 5)** | Implement `${market.*}` and `${market-price.*}` ABML variable namespaces enabling NPC vendors to make autonomous GOAP-driven pricing and restocking decisions. | [#427](https://github.com/beyond-immersion/bannou-service/issues/427) |
| 3 | **Vendor Negotiation API** | Expose a `/market/vendor/negotiate` endpoint for dynamic haggling where the vendor's ABML behavior decides to accept, counter-offer, or refuse buyer proposals. | [#428](https://github.com/beyond-immersion/bannou-service/issues/428) |

### L4 Audit Changes (2026-03-06)

| Change | Category | Description |
|--------|----------|-------------|
| Pattern C topics | Spec | `market-definition.*` → `market.definition.*`; `topic_prefix: market` on x-lifecycle |
| PascalCase enums | Spec | All 10 enum types: `static`→`Static`, `dynamic`→`Dynamic`, `personality_driven`→`PersonalityDriven`, etc. |
| configuration entity | Spec | MarketDefinition classified as configuration (no deprecation lifecycle) |
| filler removed | Spec | Vendor buy response: `item + receipt` → `itemInstanceId` |
| distributed lock | Spec | SetStock endpoint: lock acquisition on `stock:{vendorId}:{templateId}` |
| telemetry | Spec | ITelemetryProvider added to DI services table |
| Namespace overlap | Spec | `${market.price.*}` → `${market-price.*}` (avoids prefix collision with `${market.*}`) |
| x-lifecycle | Spec | MarketDefinition + VendorCatalog entities with model fields specified |
| x-event-subscriptions | Spec | `currency.hold.expired` consumption declared |
| x-permissions | Spec | All 6 endpoint groups: developer, service-to-service, or mixed |
| x-references | Spec | Added `field` and `payloadTemplate` columns to cleanup table |
| config removed | Spec | `VendorWalletOwnerType` removed (Currency uses EntityType enum) |
| Vendor lifecycle events | Spec | 3 x-lifecycle events added (created/updated/deleted) |
| Design decisions | Spec | DC#9-12 added for genuine design choices requiring human judgment |

### GH Issues

```bash
gh issue list --search "Market:" --state open
```

---

## Organization {#organization-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [ORGANIZATION.md](plugins/ORGANIZATION.md)

### Production Readiness: 0%

Pre-implementation. L4-audited (2026-03-07). Deep dive hardened: kebab-case event topics fixed (`role_changed`→`role-changed`, `legal_status`→`legal-status`), PascalCase succession mode enums (`Primogeniture`, `EqualDivision`, `Designated`, `Testament`, `Elective`, `Conquest`, `Dissolution`), x-lifecycle adoption with `topic_prefix: organization` (Created/Updated/Deleted auto-generated; dissolved/archived/member/succession/legal-status/asset/phase events classified as custom), EntityType committed for `ownerType` and `entityType` (removed "(or string pending schema)" hedge), resource reference registration added to Create endpoint + x-references declarations, ITelemetryProvider added to DI Services, DefaultLegalStatus noted as `$ref: LegalStatus` enum, x-event-publications/subscriptions schema declarations added, x-compression-callback design decision documented, worker lock key prefix double-count fixed, x-service-layer and variable-providers.yaml schema requirements noted. 8 AUDIT:NEEDS_DESIGN decisions documented (DC#11-#18): deprecation vs instance lifecycle, realm cleanup target, seed event+listener dedup, x-permissions model, assetType classification, dissolved/archived event design, compression flow, guild membership model vs Issue #284. 9 GH issues referenced in Work Tracking. 36 planned endpoints, 7 state stores, 12 hard + 5 soft dependencies, 7-phase implementation plan. No schema, no code.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Core Organization Infrastructure (Phase 1)** | Create schemas, generate code, implement organization CRUD that provisions seed + wallet + inventory, member management, role definitions, and asset registration. Prerequisite issues: #556 (wallet cleanup), #564 (relationship x-references). | [#284](https://github.com/beyond-immersion/bannou-service/issues/284) |
| 2 | **Household Pattern (Phase 5)** | Implement households as organizations with family-specific succession modes (Primogeniture, matrilineal, EqualDivision), lifecycle integration with character events (marriage, coming of age), and dissolution via arbitration. | [#436](https://github.com/beyond-immersion/bannou-service/issues/436) |
| 3 | **Legal Status System (Phase 3)** | Charter-as-contract pattern, sovereignty change re-evaluation with grace periods, faction territory event handling. | [#435](https://github.com/beyond-immersion/bannou-service/issues/435) |

### GH Issues

```bash
gh issue list --search "Organization:" --state open
```

---

## Procedural {#procedural-status}

**Layer**: L4 GameFeatures | **Deep Dive**: [PROCEDURAL.md](plugins/PROCEDURAL.md)

### Production Readiness: 0%

Pre-implementation. L4-audited (2026-03-07). Deep dive hardened: Asset and Orchestrator dependencies reclassified from hard to soft (L3 requires graceful degradation per SERVICE-HIERARCHY.md), template "deactivation" reclassified as Category B deprecation (DeprecateTemplate endpoint, triple-field model, includeDeprecated on list, no delete/undeprecate, idempotent), manual `procedural.template.registered` event replaced with x-lifecycle auto-generation (`topic_prefix: procedural`), ITelemetryProvider added to hard dependencies, seed parameter documented as `nullable: true`. 6 design decisions deferred (AUDIT:NEEDS_DESIGN): lib-resource vs event subscription for asset deletion cleanup (DC#6), lib-resource integration scope (DC#7), OutputFormat enum vs string (DC#8), worker pool tunables ownership (DC#9), x-permissions model for generation endpoints (DC#10), documentation wording for HDA parameters (DC#11). No schema, no generated code, no service implementation.

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

Entirely pre-implementation. L4-audited (2026-03-07). The deep dive explicitly states "No schema, no code" and "Everything is unimplemented." An in-game streaming metagame service with simulated audience pools, hype train mechanics, streamer career progression, and real/simulated audience blending. Specifies 19 planned endpoints, 9 published events, 8 consumed events, 6 state stores, 25 configuration properties (now with explicit types and validation constraints), 2 background workers, and a 7-phase implementation plan. Also resolves the GameSession (L2) to Voice (L3) hierarchy violation by owning the voice room orchestration flow. Composes Seed, Currency, Collection, Contract, and Relationship (L1/L2 hard deps) with soft deps on Broadcast, Voice, Analytics (L3/L4).

**L4 audit fixes applied** (2026-03-07): `DateTime`→`DateTimeOffset` on all timestamps; `ShowtimeSessionStatus` enum defined (`Active`/`Paused`/`Ended`); `entityType`→`$ref: EntityType` for streamer identity; cleanup endpoints→`x-permissions: []`; `SentimentCategory`→`$ref: common-api.yaml`; `x-event-publications` note added to Phase 1; `ITelemetryProvider` added to DI table; `game-session.ended` handler documented as-compliant live state reaction; config table enriched with types and constraints; GH issue cross-references added (#572, #382, #437, #366).

**Deep dive hardening** (2026-03-09): `account.deleted` handler added (Account Deletion Cleanup Obligation); multi-service orchestration compensation/self-healing documented for session start/end; Phase 1 requirements updated (allOf with `BaseServiceEvent`, `eventName` with `default:`, `additionalProperties: false`, NRT compliance); `Build*Key()` pattern section added; background worker per-item error isolation noted; `x-references` fully specified with target/sourceType/field/onDelete/cleanup fields; hype train cross-service mechanism clarified (L4→L4 event subscription). 6 design decisions resolved: DC#4 (local-only followers, `IRelationshipClient` removed), DC#8 (client events deferred), DC#9 (inline career progression), DC#10 (`ShowtimeEnabled` removed), DC#11 (`Showtime` prefix for events), DC#12 (custom events, no x-lifecycle).

**3 deferred design decisions**: DC#13 voice room conflict with Connect `ICompanionRoomProvider` (#382), DC#14 SentimentCategory extensibility (#572), DC#7 voice consent UX (client-side design question).

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

Pre-implementation. Comprehensive L4 audit completed (2026-03-10): three independent audit passes (schema-rules compliance, full tenet compliance, GitHub issues review). Deep dive hardened to tenet compliance across all dimensions: 17+ enum fields typed with PascalCase values, event topics fixed to kebab-case, x-permissions on all ~48 endpoints, x-lifecycle for 5 entities, x-event-publications/subscriptions, x-references with 3 cleanup endpoints, Category A deprecation on 4 entities (8 new endpoints), distributed lock specification, multi-service compensation strategies, T6 canonical worker patterns, 8 untyped object fields replaced with named schemas, NRT nullable annotations, validation constraints on all fields. 4 design decisions deferred (AUDIT:NEEDS_DESIGN). 7 GH issues tracked. Critical blocker: #153 (Escrow). No schema, no generated code, no service implementation exists.

### Bug Count: 0

No implementation exists to have bugs.

### Top 3 Bugs

*(None -- pre-implementation)*

### Top 3 Enhancements

| # | Enhancement | Description | Issue |
|---|-------------|-------------|-------|
| 1 | **Schema creation + code generation** | Create trade-api.yaml, trade-events.yaml, trade-configuration.yaml per hardened deep dive | #427 |
| 2 | **Resolve design decisions** | 4 AUDIT:NEEDS_DESIGN items block schema creation | — |
| 3 | **Escrow integration fix** | Escrow asset transfer broken — blocks custodyMode: Escrow | #153 |

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
