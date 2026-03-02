# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [0.16.0] - 2026-03-02

### Added

#### New Services

- **Transit Service (L2)**: Full implementation of the geographic connectivity and movement primitive — 33 endpoints, 8 state stores, Dijkstra-based route calculation with risk assessment, multi-modal journey tracking, discovery management, seasonal connection availability, `${transit.*}` variable provider for ABML, two background workers (journey archival, seasonal connection updates), and DI-based `ITransitCostModifierProvider` for L4 cost enrichment
- **Worldstate Service (L2)**: Full implementation of per-realm game-time authority and calendar system — 18 endpoints, configurable time ratios, calendar templates with period/season/month structures, clock advancement worker with boundary event publishing, downtime catch-up, `${world.*}` variable provider (14 variables), client events for time sync, cross-node cache invalidation, and `GetElapsedGameTime` API for lazy evaluation patterns

#### Cross-Cutting: Client Events

- **9 new client event schemas**: Auth, Character, Collection, Currency, Inventory, Location, Status, Subscription, Transit — enabling real-time WebSocket push notifications for state changes across the platform
- **Collection client events**: `collection.entry.unlocked`, `collection.milestone-reached`, `collection.discovery-advanced`
- **Currency client events**: Balance changes, transfer notifications, hold lifecycle
- **Subscription client events**: `SubscriptionStatusChanged` for background expiration awareness
- **Inventory client events**: Item movement and container change notifications
- **Location client events**: Presence and location state updates

#### Cross-Cutting: Telemetry Instrumentation

- **T30 compliance across L2-L4**: Added `ITelemetryProvider` with `StartActivity` spans to all async methods in Transit, Worldstate, Quest, Asset, Voice, Orchestrator, Game Session, Collection, Subscription, Seed, Location, Realm, Relationship, Currency, Inventory, Item, and first six L4 plugins

#### Cross-Cutting: Production Hardening

- **StateStore reference normalization**: Migrated all services to use generated `StateStoreDefinitions` constant properties instead of string literals
- **Deprecation lifecycle normalization**: Standardized deprecation patterns (T31) across all services with triple-field model (`IsDeprecated`, `DeprecatedAt`, `DeprecationReason`)
- **Sentinel value elimination**: Replaced `Guid.Empty` and empty string sentinels with nullable types (T26) across Quest, Game Session, Inventory, Item, Currency, and others
- **Type safety enforcement**: Replaced string fields with proper enum/Guid types (T25) in Currency, Inventory, Item, Game Session, and Collection

#### Game Session (L2) Hardening

- **Hierarchy violation fix**: Removed `IVoiceClient` dependency (L2→L3 violation); voice lifecycle now managed by L4 services
- **Lifecycle event publishing**: Implemented `game-session.updated` (with `changedFields`) and `game-session.deleted` (with `deletedReason`) events
- **Player membership validation**: `PerformGameActionAsync` now validates player membership before allowing actions
- **`autoLobbyEnabled` gating**: Respects GameService `autoLobbyEnabled` flag before publishing lobby shortcuts
- **Distributed locks**: Added lock coverage for session-list read-modify-write operations

#### Collection (L2) Hardening

- **Global first-unlock tracking**: Redis set operations for atomic first-unlock detection per game service + collection type; `IsFirstGlobal` now reports correctly
- **Resource cleanup migration**: Migrated character-owned collection cleanup from event-based to lib-resource `x-references` pattern
- **ICollectionUnlockListener**: DI-based unlock notification dispatch for in-process delivery (Seed growth pipeline, Faction integration)
- **ETag concurrency**: Added optimistic concurrency control

#### Quest (L2) Hardening

- **CHARACTER_LEVEL prerequisite**: Migrated from stub to dynamic `IPrerequisiteProviderFactory` with graceful degradation
- **Quest log category filtering**: Added `category` filter parameter to `GetQuestLogRequest`
- **ETag-based concurrency**: Retry loop for all CharacterIndex mutations (accept, complete, abandon, expire)
- **Configuration bounds**: Added min/max constraints to all 12 integer config properties

#### Asset (L3) Hardening

- **BundleCleanupWorker**: New background service auto-purging soft-deleted bundles past retention window
- **ZipCacheCleanupWorker**: New background service purging expired ZIP cache entries
- **Event publishing fix**: `asset.processing.queued` and `asset.ready` events now actually published (were declared but unused)
- **Index failure observability**: Upgraded retry exhaustion from silent logging to error events via `TryPublishErrorAsync`
- **23 new configuration properties**: MinIO retry behavior, processing defaults, cleanup intervals, cache management

#### Voice (L3) Hardening

- **Distributed locks**: Added `IDistributedLockProvider` for broadcast consent flow atomicity
- **Kamailio code removal**: Deleted orphaned `IKamailioClient` and `KamailioClient` infrastructure
- **Event topic normalization**: Renamed 5 event topics per naming conventions (`voice.room.broadcast.*` → `voice.broadcast.*`)

#### Orchestrator (L3) Hardening

- **Pool state store**: New isolated Redis store for processing pool data
- **Background lease cleanup**: Timer-based `ServiceHealthMonitor` for proactive expired lease reclamation
- **Multi-version config rollback**: Optional `targetVersion` field for rolling back to any historical version
- **Log timestamp parsing fix**: Continuation lines inherit preceding line's parsed timestamp
- **8 new configuration properties**: Pool management, timeouts, cleanup intervals

#### Other L2 Service Improvements

- **Subscription**: Distributed locks, telemetry spans, constructor caching, type safety, worker delegation (88%→95%)
- **Seed**: Constructor caching, telemetry spans, sentinel elimination, schema validation (88%→95%)
- **Location**: `${location.*}` variable provider, presence tracking, hardened to L3 (92%→97%)
- **Character**: Schema NRT compliance, telemetry spans, MySQL JSON queries (90%→97%)
- **Realm**: ETag concurrency, distributed merge lock, full event coverage (95%→100%)
- **Game Service**: Resource cleanup on delete, T9/T21/T26/T28/T30 compliant (93%→100%)
- **Relationship**: Telemetry, constructor caching, deprecation lifecycle, sentinel elimination (90%→95%)
- **Inventory**: T8/T25/T26/T29/T30 compliant, 93 tests (80%→85%)
- **Item**: T7/T8/T25/T29/T30 compliant, 70 tests (88%→92%)
- **Currency**: 7 bugs fixed, T25/T30 compliant (78%→85%)
- **Actor**: Transit and Worldstate variable providers integrated, event topic normalization (65%→90%)

#### Documentation

- **Director deep dive**: New L4 GameFeatures service for human-in-the-loop orchestration
- **Bannou Aspirations**: New aspirational architecture document
- **Planning documents**: Bannou Embedded, Cryptic Trails, Logos Resonance Items
- **Tenet reorganization**: Split implementation tenets into IMPLEMENTATION-BEHAVIOR.md and IMPLEMENTATION-DATA.md

#### SDKs & Tooling

- **TypeScript SDK regeneration**: Updated generated clients for all schema changes
- **Game-service attribute fix**: Fixed `x-service-layer` attribute bug in game-service schema
- **TypeScript operationId conflict resolution**: Fixed generation script for conflicting operationIds across services

### Fixed

- **Collection**: `GrantEntryAsync` bypassing `MaxCollectionsPerOwner`, cleanup handlers not publishing `collection.deleted`, `UpdateEntryTemplateAsync` ignoring `hideWhenLocked`/`discoveryLevels`, `ListEntryTemplatesAsync` ignoring `pageSize`
- **Game Session**: Voice hierarchy violation (L2→L3), missing lifecycle events, session-list cleanup lacking distributed lock, actions endpoint not validating player membership
- **Asset**: Schema-code event mismatch (`asset.processing.queued`/`asset.ready` never published), silent index retry exhaustion, model initialization safety
- **Voice**: Dead `IKamailioClient` infrastructure, `VoiceRoomStateEvent` never published (removed), event topic naming violations
- **Quest**: `Guid.Empty` sentinel for quest giver, hardcoded reward container slots, layer classification comments
- **Orchestrator**: Expired lease lazy-only reclamation, dead TTL-based cache invalidation logic, log timestamp continuation lines
- **Currency**: 7 bugs fixed during production hardening pass

---

## [0.15.0] - 2026-02-23

### Added

#### Infrastructure & Messaging

- **Dead Letter Consumer**: New `IDeadLetterConsumer` service with logging for undeliverable RabbitMQ messages in lib-messaging
- **MeshInstanceId Pattern**: New `IMeshInstanceIdentifier` interface replacing static property calls for instance identity across all services
- **Message Tap System**: New infrastructure for message inspection/debugging in lib-messaging

#### Chat Service Buildout (L1)

- **Session Tracking**: Chat sessions now tracked with proper lifecycle management
- **Client Eventing**: Real-time WebSocket push for chat events (new messages, typing, moderation)
- **Typing Indicators**: Redis sorted set-based typing status with server-side expiry
- **Ban Expiry Worker**: Background service for automatic ban expiration
- **Message Retention Worker**: Background service for configurable message retention cleanup
- **Idle Room Cleanup**: Distributed lock-protected background cleanup of abandoned rooms
- **Bulk Message Failures**: Failure details included in bulk message responses
- **Paginated Contract Room Queries**: New paginated query support for contract-governed rooms

#### Connect Service Overhaul (L1)

- **Inter-Node Broadcast Relay**: WebSocket mesh between Connect instances for multi-node message delivery
- **Typed Models**: Anonymous types replaced with proper typed models throughout Connect
- **Entity Session Registry**: `IEntitySessionRegistry` for "find which session owns this entity" lookups

#### Actor Service Leveling (L2)

- **Handler Refactoring**: Separated command, query, perception, encounter, and state update handlers
- **Pool Health Monitoring**: Improved actor pool health tracking and diagnostics
- **Fallback Behavior Provider**: Graceful fallback when primary behavior documents unavailable
- **Behavior Configuration Error Fix**: Race condition fix in behavior document loading

#### Permission Enhancements (L1)

- **Session Heartbeat Listener**: Efficient session activity tracking via heartbeat events instead of polling
- **Session Activity Listener Pattern**: DI-based listener for session state changes

#### Documentation

- **Extension Plugin Guide**: New `docs/guides/EXTENSION-PLUGINS.md` for third-party plugin development
- **Deployment FAQ**: New deployment planning documents
- **Planning Documents**: Predator ecology, sanctuaries, self-hosted deployment planning docs
- **Updated Plugin Deep Dives**: 17 service deep dives updated to reflect hardening changes

### Changed

#### Service/Plugin Enabling Simplification

- **Removed `_DISABLED` checks**: All services now use `_ENABLED` pattern exclusively (no more `_DISABLED` env vars)
- **Layer-level configuration**: Added `BANNOU_ENABLE_APP_FOUNDATION`, `BANNOU_ENABLE_GAME_FOUNDATION`, `BANNOU_ENABLE_APP_FEATURES`, `BANNOU_ENABLE_GAME_FEATURES`, `BANNOU_ENABLE_EXTENSIONS` (all default `true`)
- **Simplified PluginLoader**: Resolution order: required infrastructure → individual override → master kill switch → layer control

#### TENET Compliance Hardening (L0–L2)

- **lib-mesh** (L0): Circuit breaker improvements, health check hardening, invocation client fixes
- **lib-messaging** (L0): RabbitMQ connection manager hardening, channel pooling improvements, retry buffer enhancements
- **lib-telemetry** (L0): Provider pattern improvements, span instrumentation consistency
- **lib-state** (L0): Redis and MySQL store hardening, state metadata improvements
- **lib-account** (L1): TENET compliance audit fixes, model standardization
- **lib-auth** (L1): Session registry helper for client event publishing, edge provider improvements
- **lib-chat** (L1): Full buildout from stub to production-quality implementation
- **lib-connect** (L1): Major overhaul — anonymous types eliminated, broadcast relay added, session management hardened
- **lib-permission** (L1): Heartbeat-based session tracking, RBAC improvements
- **lib-resource** (L1): Reference tracking enhancements
- **lib-contract** (L1): Paginated queries, room integration improvements
- **lib-actor** (L2): Handler decomposition, pool monitoring, behavior loading fixes
- **lib-character** (L2): Test fixes, model standardization

#### Schema & Code Generation

- **Service layer fixes**: Corrected `x-service-layer` declarations across multiple schemas
- **Metadata bag documentation**: Generated metadata bag contracts into documentation for visibility
- **Full regeneration**: All 48+ service clients, models, controllers, and meta files regenerated

#### SDKs

- **SDK documentation fixes**: Small corrections across C#, TypeScript, and Unreal SDKs

### Fixed

- **Actor**: Behavior configuration race condition causing intermittent load failures
- **Connect**: Anonymous type usage throughout the plugin replaced with proper typed models
- **Connect**: Broadcast relay between nodes now functional for multi-instance deployments
- **Chat**: Multiple fixes for session tracking and event delivery
- **Chat**: Bulk message operation now reports per-message failures
- **Permission**: Efficiency improvement via heartbeat listener (reduced Redis polling)
- **Contract**: Paginated room queries for large contract-governed room sets
- **Actor/Character Tests**: Multiple test stabilization fixes
- **Service Layer Schemas**: Corrected layer declarations that were mismatched
- **Metadata Bags**: Several `additionalProperties: true` misuses corrected in schemas
- **Dead Method Removal**: Removed unused method that had unintended side effects

### Removed

- **`_DISABLED` environment variables**: All `{SERVICE}_SERVICE_DISABLED` patterns removed in favor of `{SERVICE}_SERVICE_ENABLED`
- **Dead method**: Removed unused method with unintended side effects from service code

---

## [0.14.0] - 2026-02-21

### Added

#### New Services (15 plugins, 271 new endpoints)

- **Telemetry Service** (`lib-telemetry`, L0 Infrastructure): Unified observability via OpenTelemetry (2 endpoints)
  - `ITelemetryProvider` abstraction for lib-state, lib-messaging, lib-mesh instrumentation
  - `NullTelemetryProvider` fallback when disabled; loads first in plugin ordering
- **Chat Service** (`lib-chat`, L1 AppFoundation): Universal typed message channel primitives (28 endpoints)
  - Room types determine valid message formats (text, sentiment, emoji, custom-validated)
  - Optional Contract governance for room lifecycle
  - Ephemeral (Redis TTL) and persistent (MySQL) message storage
  - Participant moderation (kick/ban/mute), atomic Redis rate limiting, idle room cleanup
- **Resource Service** (`lib-resource`, L1 AppFoundation): Reference tracking, lifecycle management, and hierarchical compression (17 endpoints)
  - Enables safe L2 resource deletion by tracking L3/L4 references without hierarchy violations
  - CASCADE/RESTRICT/DETACH cleanup policies with coordinated callbacks
  - Unified MySQL-backed archive compression for resources and dependents
  - Integrated by lib-character, lib-actor, lib-character-encounter, lib-character-history, lib-character-personality, lib-realm-history
- **Quest Service** (`lib-quest`, L2 GameFoundation): Objective-based progression as thin orchestration over lib-contract (17 endpoints)
  - Quest semantics (objectives, rewards, quest givers) translated to Contract milestones and prebound APIs
  - Agnostic to prerequisite sources via `IPrerequisiteProviderFactory` DI pattern
  - `${quest.*}` ABML variable namespace for Actor behavior expressions
- **Seed Service** (`lib-seed`, L2 GameFoundation): Generic progressive growth primitive (24 endpoints)
  - Seeds accumulate metadata across named domains, gaining capabilities at configurable thresholds
  - Polymorphic ownership (accounts, actors, realms, characters, relationships)
  - Growth decay with per-type/per-domain configuration
  - Cross-seed growth sharing, bond mechanics, phase transition events
  - Seed types are string codes allowing new types without schema changes
  - `ISeedEvolutionListener` DI pattern for phase transition notifications
- **Collection Service** (`lib-collection`, L2 GameFoundation): Universal content unlock and archive system (20 endpoints)
  - "Items in inventories" pattern: entry templates, collection instances, granted entries
  - Dynamic content selection based on unlocked entries and area theme configs
  - `ICollectionUnlockListener` DI pattern for Seed growth pipeline integration
  - Collection types are opaque strings (not enums)
- **Worldstate Service** (`lib-worldstate`, L2 GameFoundation): Per-realm game time authority and calendar system (18 endpoints)
  - Real-to-game time mapping with configurable per-realm ratios
  - Calendar templates (configurable days, months, seasons, years) and day-period cycles
  - Boundary event publishing at game-time transitions
  - `${world.*}` ABML variable namespace via Variable Provider Factory
  - Time-elapsed query API for lazy evaluation patterns
- **Puppetmaster Service** (`lib-puppetmaster`, L4 GameFeatures): Dynamic behavior orchestration and regional watchers (6 endpoints)
  - Bridges Actor (L2) and Asset (L3) for runtime ABML behavior loading
  - `IBehaviorDocumentProvider` implementation for actor provider chain
  - Regional watcher lifecycle management and resource snapshot caching
- **Divine Service** (`lib-divine`, L4 GameFeatures): Pantheon management, divinity economy, blessing orchestration (22 endpoints)
  - Thin orchestration layer composing Currency, Seed, Relationship, Collection, Status, Puppetmaster
  - God-actors as character brains bound to divine system realm characters
  - Avatar manifestation with divinity economy cost scaling
  - Blessing tiers: Minor/Standard (temporary via Status) and Greater/Supreme (permanent via Collection)
- **Faction Service** (`lib-faction`, L4 GameFeatures): Seed-based living factions (31 endpoints)
  - Capabilities emerge from seed growth phases (nascent, established, influential, dominant)
  - Norm definition, enforcement tiers, territory claiming, trade regulation
  - Guild memberships with role hierarchy, parent/child organizational structure
  - Inter-faction political connections modeled as seed bonds
- **Gardener Service** (`lib-gardener`, L4 GameFeatures): Player experience orchestration (24 endpoints)
  - Player-side counterpart to Puppetmaster: manages abstract "garden" spaces
  - Gameplay context, entity associations, and event routing for divine actor manipulation
  - Currently implements void/discovery garden type
- **License Service** (`lib-license`, L4 GameFeatures): Grid-based progression boards (20 endpoints)
  - Inspired by FF12 License Board: skill trees, tech trees via Inventory + Items + Contracts
  - Polymorphic ownership via `ownerType` + `ownerId` (characters, accounts, guilds, locations)
  - Board cloning for NPC progression
- **Obligation Service** (`lib-obligation`, L4 GameFeatures): Contract-aware NPC cognition (11 endpoints)
  - "Second thoughts" before violating obligations via GOAP action cost modifiers
  - Three-layer enrichment: raw contract penalties, personality-weighted, belief-filtered
  - `${obligations.*}` ABML variable namespace
- **Status Service** (`lib-status`, L4 GameFeatures): Unified entity effects query layer (16 endpoints)
  - Aggregates temporary contract-managed statuses and passive seed-derived capabilities
  - "Items in inventories" pattern for combat buffs, death penalties, divine blessings
  - Optional Contract integration for complex lifecycle per-template
- **Storyline Service** (`lib-storyline`, L4 GameFeatures): Seeded narrative generation from compressed archives (15 endpoints)
  - Wraps `storyline-theory` and `storyline-storyteller` SDKs
  - Plans describe narrative arcs with phases, actions, entity requirements

#### DI Inversion Patterns (Provider/Listener Interfaces)

- **`IVariableProviderFactory`** / **`IVariableProvider`**: L4 services provide typed variable namespaces to Actor (L2) behavior execution without hierarchy violations
  - `${personality.*}` and `${combat.*}` from lib-character-personality
  - `${encounters.*}` from lib-character-encounter
  - `${backstory.*}` from lib-character-history
  - `${location.*}` from lib-location
  - `${world.*}` from lib-worldstate
  - `${quest.*}` from lib-quest
  - `${obligations.*}` from lib-obligation
- **`IPrerequisiteProviderFactory`**: L4 services provide prerequisite validation to Quest (L2)
- **`IBehaviorDocumentProvider`** / **`IBehaviorDocumentLoader`**: Puppetmaster (L4) supplies runtime-loaded behaviors to Actor (L2)
- **`ICollectionUnlockListener`**: Collection (L2) notifies L4 services of unlocks (e.g., Seed growth pipeline)
- **`ISeedEvolutionListener`**: Seed (L2) notifies L4 services of phase transitions
- **`ISeededResourceProvider`**: L4 services provide seeded resource definitions to Resource (L1)
- **`IEntitySessionRegistry`**: Connect provides entity-to-session mapping for "find which session owns this character"

#### Infrastructure Hardening

- **lib-state**: SQLite backend (`SqliteStateStore`), Redis atomic operations via Lua scripts (`TryCreate.lua`, `TryUpdate.lua`), expanded `ICacheableStateStore` for Redis sets/sorted sets, `IRedisOperations` for Lua scripts and atomic operations, `IQueryableStateStore` for MySQL LINQ queries
- **lib-mesh**: Distributed circuit breaker backed by Redis Lua scripts, request-level timeouts, endpoint degradation events for monitoring, proactive health checking with automatic deregistration
- **lib-messaging**: Channel pooling, aggressive retry buffering, crash-fast philosophy for unrecoverable failures, new `IChannelManager`/`IRetryBuffer` interfaces
- **Auth**: TOTP-based MFA (`IMfaService`), edge token revocation with pluggable providers (Cloudflare, OpenResty), built-in email providers (Console, SMTP, SendGrid, AWS SES)
- **Connect**: Automatic configurable payload compression for WebSocket messages, entity session registry
- **Contract**: Background milestone expiration worker, escrow integration overhaul
- **Escrow**: Two new background workers (confirmation timeout enforcement, TTL expiration)
- **Voice**: Participant TTL enforcement via background eviction worker
- **Location**: Entity presence tracking, presence cleanup worker, data caching layer

#### Observability Stack

- Complete local observability infrastructure: Grafana + Prometheus + OpenTelemetry Collector + Tempo
- Bannou overview dashboard (`provisioning/dashboards/bannou-overview.json`)
- OpenResty JWT revocation checking at the reverse proxy layer (`validate_jwt_revocation.lua`)

#### Documentation (105 new files)

- **VISION.md** and **PLAYER-VISION.md**: High-level architectural north-star documents
- **SERVICE-HIERARCHY.md** (v2.7): Authoritative 6-layer service dependency hierarchy with enforcement rules
- **ORCHESTRATION-PATTERNS.md**: How decomposed services form living gameplay loops via god-actors
- **27 FAQ documents** (`docs/faqs/`): Architectural decision explanations for developer onboarding
- **36 plugin deep dives** (`docs/plugins/`): Comprehensive service documentation including aspirational services
- **10 new guides**: Behavior System, Behavioral Bootstrap, Character Communication, Economy System, Meta Endpoints, Morality System, Scene System, SDK Overview, Seed System, Story System
- **13 planning documents**: Cinematic System (4-phase plan), Actor-Bound Entities, Death and Plot Armor, Dungeon Extensions, Self-Hosted Deployment, and more
- **PLAN-EXAMPLE.md**: Preserved real implementation plan (Seed service) as a template
- **COMPRESSION-CHARTS.md**: Resource compression flow documentation
- **DEEP-DIVE-TEMPLATE.md**: Standardized template for plugin documentation

#### Testing

- **ServiceHierarchyValidator**: Reflection-based automated enforcement of service layer dependencies
- **PermissionMatrixValidator**: Automated permission matrix registration validation
- 6 new HTTP integration test handlers (CharacterEncounter, CharacterPersonality, Escrow, Item, Scene, Telemetry)
- State HTTP test coverage (+604 lines)
- SQLite test support for lib-state
- Edge tester `BinaryMessageHelper` for improved binary protocol testing
- Assembly inspector `ConstructorCommand` for external library inspection

#### Schema & Code Generation

- `x-compression-callback` schema extension with automated code generation
- `x-event-template` schema extension for automated event template registration
- `x-resource-mapping` schema extension for lifecycle events
- `x-clause-provider` schema extension for automated context resolver generation
- Lifecycle events auto-generation from `x-lifecycle` in event schemas
- DTO model container generation pipeline
- 86 new state store definitions across all new and existing services
- 16 new event schemas, 15 new configuration schemas

#### TENETS v8.0 (4 new tenets)

- **T27**: Cross-Service Communication Discipline (direct API for higher-to-lower; DI interfaces for lower-to-higher; events for broadcast only)
- **T28**: Resource-Managed Cleanup (dependent data cleanup via lib-resource only; never subscribe to lifecycle events for destruction)
- **T29**: No Metadata Bag Contracts (`additionalProperties: true` is never a data contract between services)
- **T30**: Telemetry Span Instrumentation (all async methods get `StartActivity` spans)

### Changed

- **Behavior Service decomposition**: Cognition pipeline moved to shared code (`bannou-service/Abml/Cognition/`); compiler, runtime, GOAP system relocated to Actor (L2) runtime; lib-behavior now focused on ABML compilation and document merging (~13,000 lines restructured)
- **Relationship consolidation**: `lib-relationship-type` absorbed into `lib-relationship` -- single plugin now manages both entity-to-entity relationships and the hierarchical relationship type taxonomy
- **Actor moved to L2**: Actor service reclassified from L4 GameFeatures to L2 GameFoundation with Variable Provider Factory pattern for receiving L4 data without hierarchy violations
- **Collection moved to L2**: Collection service reclassified from L4 to L2 with `ICollectionUnlockListener` DI pattern
- **Service Hierarchy enforcement**: Layer-based plugin loading (PluginLoader sorts by `ServiceLayer` attribute), constructor injection now standard for L0/L1/L2 dependencies, `GetService<T>()` with null checks for L3/L4 soft dependencies
- **Orchestrator pluggable backends**: Four implementations (Docker Compose, Docker Swarm, Kubernetes, Portainer) via `IContainerOrchestrator` interface
- **Permission system**: Migrated from event-based registration to DI Provider interfaces; removed dead permission registration artifacts
- **Cross-service cleanup**: Migrated from lifecycle event subscriptions to lib-resource callbacks (character-encounter, character-history, character-personality, realm-history, relationship)
- **Seed/Resource growth**: Migrated from inverted event subscriptions to direct API calls
- **`*ServiceModels.cs` standardization**: Nearly every plugin gained a dedicated internal models file, separating internal data models from service logic
- **SDK regeneration**: Full Unreal SDK regeneration (+33,598 lines in `BannouTypes.h`) and TypeScript SDK regeneration reflecting all 15 new services
- **`.env.example`**: Expanded by ~736 lines to cover all new service configurations
- **Solution file**: +670 lines reflecting all new plugin projects
- **`make list` command**: Added for easier grepping of Makefile targets
- **CLAUDE.md**: Expanded with generation script selection guide, testing workflow clarifications, additional constraints (+214 lines)

### Fixed

- `additionalProperties: true` misuse audit and systematic correction across multiple schemas
- CustomTerms type fixes to use proper types instead of strings
- AWS/MinIO configuration fixes for S3 SDK integration
- Contract: `ClauseValidationCacheStalenessSeconds` dead config removed (T21 violation)
- Contract: `MaxActiveContractsPerEntity` now actually counts active contracts
- Contract: `ParseClauseAmount` fails on missing `base_amount` instead of silently returning 0
- Escrow: Fixed unreachable `Releasing` state in state machine
- Escrow: Fixed 3 critical state machine bugs
- Connect: Fixed subsumed connection publishing spurious `session.disconnected` events
- Connect: Removed dead session mappings and unused Redis persistence
- Character-Encounter: Fixed N+1 query pattern in all query operations
- Character-History: `AddBackstoryElement` event now distinguishes element added vs updated
- Realm-History: Added configurable lore element count limit
- Mesh: `ServiceNavigator.RawApi` now uses `IMeshInvocationClient` instead of direct `HttpClient`
- Mesh: Removed hardcoded values from hierarchy validator
- State: `MySqlStateStore.QueryAsync` no longer loads all entries into memory
- State: `StateMetadata` now properly populated in `GetStateResponse`
- Naming case enforcement for realms and actor types
- Over-generation of instruction comments in service implementations
- Silent failure patterns across multiple plugins (systematic audit and fix)
- Multiple flaky actor test stabilization attempts
- Sentinel value removal from `x-lifecycle` event publishing

### Removed

- `lib-relationship-type` plugin (consolidated into `lib-relationship`)
- Permission registration event system (replaced by DI Provider interfaces)
- Actor L4 event subscriptions for cache invalidation (belongs to provider owners)
- Auth subscription management endpoints (moved to dedicated Subscription service)
- Dead configuration properties across multiple services (T21 compliance)
- ABML merger test fixtures relocated from `bannou-service/Abml/fixtures/`
- `.claude/settings.json`

### Resolved Issues

131 GitHub issues closed in this release (22 additional closed as "won't do"). Key categories:

- **Architecture decisions** (14): Resource lifecycle management (#259), Actor provider pattern dependency inversion (#287), Actor/Puppetmaster L2/L4 split (#288), Quest to L2 with provider factory (#320), Collection-Seed-Status cross-pollination pipeline (#375)
- **Architecture migrations** (7): Relationship-type consolidation (#331, #333), Seed/Resource/Permission migration from events to direct APIs (#376-#380), Actor L4 event subscription removal (#380)
- **Schema extensions** (6): Compression callbacks (#302), event templates (#303), resource mapping (#304), resource template codegen (#305), clause provider (#321), lifecycle event audit (#163)
- **Infrastructure production readiness** (12): lib-state fixes (#177, #251, #255, #325-#329), lib-mesh circuit breaker and timeouts (#219, #322-#324), lib-messaging 100K NPC scale (#328)
- **New features** (4): Itemize Anything (#280, #330), lib-license (#281), lib-collection (#286)
- **Service hardening** (30+): Account production hardening (#332), Auth production hardening (#334), Contract lifecycle fixes (#221, #241-#245), Escrow state machine (#210, #214), and more
- **ABML actions** (8): Snapshot loading (#291-#294), actor communication (#297), watcher orchestration (#298), event publishing (#299), resource watching (#301)

---

## [0.13.0] - 2026-01-29

### Added
- **Currency Service** (`lib-currency`): Multi-currency management for game economies (32 endpoints)
  - Currency definitions with precision, scope, caps, autogain, expiration
  - Wallet management (create, freeze, close with balance transfer)
  - Balance operations (credit, debit, transfer with deadlock-free locking)
  - Authorization holds (reserve/capture/release pattern)
  - Currency conversion via exchange rates
  - Escrow integration wrappers
  - Background autogain task service
  - Idempotency-key deduplication throughout
- **Contract Service** (`lib-contract`): Binding agreements with milestone-based progression (30 endpoints)
  - Reactive design: external systems report conditions; contracts store state and execute prebound APIs
  - Template-based structure (party roles, milestones, terms)
  - Four enforcement modes (advisory, event_only, consequence_based, community)
  - Breach handling with ISO 8601 cure periods
  - Guardian custody for asset-backed contracts (escrow integration)
  - Prebound API execution with template substitution
- **Escrow Service** (`lib-escrow`): Multi-party asset exchange orchestration (20 endpoints)
  - 13-state finite state machine
  - Four escrow types (two_party, multi_party, conditional, auction)
  - Three trust modes (full_consent with tokens, initiator_trusted, single_party_trusted)
  - SHA-256 token authorization
  - Arbiter-mediated dispute resolution
  - Custom asset type handler registration
- **Inventory Service** (`lib-inventory`): Container and item placement management (16 endpoints)
  - Multiple constraint models (slot, weight, grid, volumetric, unlimited)
  - Item placement, movement, stacking operations
  - Equipment slots as specialized containers
  - Nested containers with weight propagation
  - Graceful degradation when item service unavailable
- **Item Service** (`lib-item`): Dual-model item management (13 endpoints)
  - Templates (immutable definitions) and Instances (individual occurrences)
  - Multiple quantity models (discrete, continuous, unique)
  - Soulbound/binding system (none, on_pickup, on_equip, on_use)
  - Redis read-through caching
- **Character Encounter Service** (`lib-character-encounter`): Memorable interaction tracking (19 endpoints)
  - Multi-participant encounter/perspective design
  - Time-based memory decay applied lazily
  - Weighted sentiment aggregation
- **TENETS v6.0**: Major documentation restructuring
  - New T25 (Type Safety): ALL models must use proper types (enums, GUIDs) - no string representations
  - T21 expanded: No dead configuration, no hardcoded tunables
  - T0 added: Never reference tenet numbers in source code
- **SCHEMA-RULES.md**: Comprehensive schema authoring reference (899 lines)
  - 10-step generation pipeline with dependencies
  - Extension attributes documentation
  - 30+ anti-patterns with explanations
- **Plugin Deep-Dives**: 41 comprehensive service documentation files at `docs/plugins/{SERVICE}.md`
- **Configuration Validation Framework**: New attributes translating OpenAPI keywords
  - `ConfigRangeAttribute`, `ConfigPatternAttribute`, `ConfigStringLengthAttribute`, `ConfigMultipleOfAttribute`
  - Startup validation via `IServiceConfiguration.Validate()`
- **Prebound API Infrastructure**: Template substitution and response validation for contract execution
- **Claude Safety Hooks**: 4 PreToolUse hooks enforcing development standards
- **Assembly Inspector** (`tools/bannou-inspect`): CLI for external API research
- **Code Generation Improvements**: `scripts/common.sh` (420 lines), `scripts/resolve-event-refs.py` (383 lines)

### Changed
- **Testing Infrastructure Reorganization**: HTTP and Edge testers moved to `tools/` directory
  - Edge tester updated to use strongly typed models (critical fix for type safety)
  - Typed proxy requirement enforces compile-time validation
  - TypeScript SDK parity validation via embedded harness
- **Client SDK Enhancements**: New helpers for detecting capability additions/removals on reconnection
- **SDK Updates**: 5 new service proxies across C#, TypeScript, and Unreal platforms
  - C#: Lazy-loaded proxy properties, ClientEndpointMetadata registry
  - TypeScript: 29,000+ line type definitions, discriminated union support
  - Unreal: 1,019+ line endpoint registry, 13,221+ line type definitions
- **State Store Expansion**: 23 new state stores across the 5 new services

### Fixed
- Type safety enforcement across all models (no more string-typed enums or GUIDs)

---

## [0.12.0] - 2026-01-19

### Added
- **Music Theory SDK** (`sdks/music-theory`): Pure computation music generation using formal music theory rules
  - Chord progression generation with voice leading
  - Melody generation over harmony
  - Scale and mode support
- **Music Storyteller SDK** (`sdks/music-storyteller`): Narrative-driven music composition
  - Story arc to musical structure mapping
  - Emotional trajectory support
- **global.json**: Pin .NET SDK version to 9.0.x for consistent builds across local and CI environments

### Changed
- **lib-state Infrastructure**: Consolidated state management with improved async metabundle processing
- **Meta Endpoint Schema Separation**: Cleaner API organization

### Fixed
- **CA2000 Warnings**: Fixed disposable object handling across edge-tester and SDK projects
- **CS8618 Warnings**: Fixed non-nullable field initialization warnings
- **IDE0031 Suppression**: Restored pragma for null-check patterns where null propagation isn't valid C#
- **SDK Release Process**: Fixed version baseline drift issues
- **CI Build Issues**: Resolved missing files and preview feature alignment

---

## [0.11.0] - 2026-01-16

### Added
- **SDK Architecture Overhaul**: Complete reorganization under `sdks/` directory
  - SDK Conventions document (`sdks/CONVENTIONS.md`) following Azure SDK naming guidelines
  - Separate `bannou-sdks.sln` solution for SDK development
  - Shared `SDK_VERSION` file for synchronized package versions
- **Client SDK Typed Proxies**: Generated proxy classes for all 33 services
  - Type-safe API calls with `client.Account.GetAsync()` pattern
  - Full IntelliSense support with XML documentation
  - Automatic request/response serialization
- **Client Event Registry**: Centralized event type registration and dispatch
  - Generated `ClientEventRegistry` for all service events
  - Type-safe event subscription via `client.Events.Subscribe<T>()`
- **Asset Metabundles**: Compose optimized bundles from multiple source bundles
  - Provenance metadata tracking source bundle origins
  - Asset deduplication across source bundles
- **Asset Bundler SDKs**: Engine-specific asset processing pipelines
  - `asset-bundler-godot`: Godot 4.x resource processing
  - `asset-bundler-stride`: Stride engine asset compilation
  - `asset-loader-godot`: Godot runtime asset loading
  - `asset-loader-stride`: Stride runtime asset loading
- **Client Integration Tests**: New `client.integration.tests` project for tests requiring both compiler and client runtime

### Changed
- **ServiceLib.targets Refactor**: Improved build infrastructure for service plugins
- **SDK Directory Structure**: All SDKs moved from `Bannou.SDK/` to `sdks/` with consistent naming
- **Transport Layer**: Extracted to `sdks/transport/` as shared internal library
- **Bundle Format**: Extracted to `sdks/bundle-format/` as shared internal library

### Fixed
- MinIO SDK signed header issues resolved by switching to Amazon S3 SDK
- GUID normalization allowing dashes in Redis keys
- Asset upload presigned URL generation with correct service domain
- Namespace collision in client SDK tests (round-trip tests moved to integration project)

---

## [0.10.0] - 2026-01-13

### Added
- **Godot SceneComposer SDK** with unit tests for scene composition in Godot projects
- **OpenResty Configuration System**: Template-based nginx config generation
  - SSL termination templates for HTTPS endpoints
  - MinIO storage proxy templates (port 9000 and subdomain patterns)
  - Dynamic config generation via `generate-configs.sh`
  - Override directory for environment-specific configs
- **Production Deployment Tooling**:
  - ACME challenge configuration for Let's Encrypt certificate renewal
  - Certbot webroot integration with OpenResty
  - Production quickstart guide (`docs/guides/PRODUCTION-QUICKSTART.md`)
- **Release Management**: `scripts/prepare-release.sh` for semantic version releases
- **Releasing Guide** (`docs/operations/RELEASING.md`): Complete release workflow documentation
- **Permission Registration Tests**: Verify all plugins register permissions correctly
- **TestConfigurationHelper**: Shared test utilities for configuration testing

### Changed
- **Configuration Defaults**: All services now work out-of-box without secrets
  - JWT, salts, and credentials have development defaults
  - MinIO accepts host:port format (not full URLs)
  - RabbitMQ queue names have sensible defaults
- **JWT Centralized**: Token configuration moved to AppConfiguration (web host access)
- **Asset Service**: Pre-signed URLs now use `BANNOU_SERVICE_DOMAIN` when set
- **Permission System**: More secure defaults, reasonable role assignments
- **OpenResty Networking**: Only OpenResty exposes ports; services communicate via Docker network
- **Environment Variables**: Removed hardcoded overrides from Makefile and docker-compose files
- Save-load plugin uses Redis instead of SQL for pending upload state

### Fixed
- Asset upload tests failing due to incorrect service domain in presigned URLs
- Permission registration missing for several plugins
- References to planning docs cleaned up
- Nullable reference fixes throughout codebase

---

## [0.9.0] - 2026-01-10

### Added
- **Save-Load Plugin** (`lib-save-load`): Complete cloud saves system with 26 endpoints
  - Polymorphic ownership (Account, Character, Session, Realm)
  - Versioned saves with rolling cleanup and pinnable checkpoints
  - Delta saves using JSON Patch (RFC 6902) for incremental changes
  - Schema migration support
  - Export/import for backup and disaster recovery
  - Integrity verification via SHA-256 content hashing
  - Circuit breaker and async upload queue for storage protection
- Stride SceneComposer SDK for scene composition
- Save/load developer guide (`docs/guides/SAVING_AND_LOADING.md`)

### Changed
- Improved integration test infrastructure

---

## [0.8.0] - 2026-01-08

### Added
- **Matchmaking Service** (`lib-matchmaking`): Full-featured matchmaking system
  - Skill-based matching with configurable expansion curves
  - Party support with skill aggregation (highest, average, weighted)
  - Match accept/decline flow with timeout handling
  - Exclusive groups to prevent conflicting concurrent queues
  - Tournament support with registration requirements
  - Auto-requeue on match decline
  - Game-session reservation integration
- **Scene Service** (`lib-scene`): Hierarchical composition storage for game worlds
- **Realm History Service** (`lib-realm-history`): Historical event and lore management
- **Mapping Service** (`lib-mapping`): Spatial data management with affordance queries
- Matchmaking developer guide (`docs/guides/MATCHMAKING.md`)
- Behavior system improvements and additional ABML capabilities

---

## [0.7.0] - 2026-01-05

### Added
- **Actor Service** (`lib-actor`): Distributed actor management for NPC brains
  - Actor templates and spawning
  - Encounter system for Event Brain orchestration
  - Perception injection for testing
- **Game Transport Layer**: UDP game state protocol with LiteNetLib
- SceneComposer SDK for scene composition tooling
- Configurable service parameters via environment variables

### Changed
- NuGet publishing improvements and SDK stabilization

---

## [0.6.0] - 2025-12-28

### Added
- **Behavior Service** (`lib-behavior`): ABML YAML DSL and GOAP planner
  - Bytecode compiler for behavior definitions
  - 414+ behavior system tests
  - Behavior cache with invalidation
- **GOAP Planner**: Goal-Oriented Action Planning for autonomous NPCs
- Actor system developer guide (`docs/guides/ACTOR_SYSTEM.md`)
- ABML reference guide (`docs/guides/ABML.md`)
- GOAP developer guide (`docs/guides/GOAP.md`)
- Comprehensive schema and documentation overhaul

---

## [0.5.0] - 2025-12-20

### Added
- **Documentation Service** (`lib-documentation`): Knowledge base API for AI agents
  - Git repository binding for automatic documentation sync
  - Full-text search and natural language queries
  - Archive and restore functionality
- TENETS reorganization into Foundation, Implementation, and Quality categories
- Infrastructure plugins for lib-state, lib-messaging, lib-mesh

### Changed
- Removed Dapr dependency in favor of native infrastructure libs
- Asset management system improvements

---

## [0.4.0] - 2025-12-15

### Added
- **Voice Service** (`lib-voice`): P2P and room-based voice communication
  - WebRTC peer connections
  - SIP signaling integration
  - Scaled tier support for large rooms
- Meta endpoints for runtime schema introspection via WebSocket
- Session shortcuts for pre-bound API calls with secure routing

---

## [0.3.0] - 2025-12-10

### Added
- **Character Service** (`lib-character`): Character entities with stats and capabilities
- **Character History Service** (`lib-character-history`): Backstory and event participation
- **Character Personality Service** (`lib-character-personality`): Machine-readable personality traits
- **Relationship Service** (`lib-relationship`): Bidirectional entity relationships
- **Relationship Type Service** (`lib-relationship-type`): Relationship taxonomy
- **Species Service** (`lib-species`): Character species management
- **Realm Service** (`lib-realm`): World/realm instances
- **Location Service** (`lib-location`): Named locations and zones
- Client event delivery system with session-specific WebSocket push
- Compact response protocol for reduced bandwidth

---

## [0.2.0] - 2025-12-05

### Added
- **Game Session Service** (`lib-game-session`): Lobby system with chat and reservations
- **Game Service** (`lib-game-service`): Game configuration and registry
- **Subscription Service** (`lib-subscription`): Premium accounts and time-limited access
- **Orchestrator Service** (`lib-orchestrator`): Dynamic instance provisioning
- **Permission Service** (`lib-permission`): Redis-backed role-based access control
- Complete OAuth implementation (Discord, Twitch, Google, Steam)
- Service event system with error eventing
- Idempotent permission registration

---

## [0.1.0] - 2025-11-28

### Added
- Initial release of Bannou platform
- **Auth Service** (`lib-auth`): Authentication with Email, OAuth, Steam, JWT
- **Account Service** (`lib-account`): Account management, verification, profiles
- **Connect Service** (`lib-connect`): WebSocket edge gateway with binary protocol
- **State Service** (`lib-state`): State store abstraction (Redis, MySQL)
- **Messaging Service** (`lib-messaging`): Pub/Sub abstraction (RabbitMQ)
- **Mesh Service** (`lib-mesh`): Service-to-service invocation (YARP)
- **Analytics Service** (`lib-analytics`): Event ingestion and Glicko-2 skill ratings
- **Leaderboard Service** (`lib-leaderboard`): Seasonal leaderboards with Redis sorted sets
- **Achievement Service** (`lib-achievement`): Achievement tracking with platform sync
- **Asset Service** (`lib-asset`): Asset distribution via MinIO/S3
- **Website Service** (`lib-website`): Public-facing website with CMS
- Schema-first development with OpenAPI code generation
- Zero-copy WebSocket routing with client-salted GUIDs
- Monoservice architecture (deploy as monolith or microservices)
- Comprehensive test infrastructure (unit, HTTP, WebSocket edge tests)

---

[Unreleased]: https://github.com/beyond-immersion/bannou-service/compare/v0.16.0...HEAD
[0.16.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.15.0...v0.16.0
[0.15.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.14.0...v0.15.0
[0.14.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.13.0...v0.14.0
[0.13.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/beyond-immersion/bannou-service/releases/tag/v0.1.0
