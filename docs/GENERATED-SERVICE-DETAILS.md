# Generated Service Details Reference

> **Source**: `schemas/*-api.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document provides a compact reference of all Bannou services.

## Service Overview

| Service | Version | Endpoints | Description |
|---------|---------|-----------|-------------|
| [Account](#account) | 2.0.0 | 16 | Internal account management service (CRUD operations only, n... |
| [Achievement](#achievement) | 1.0.0 | 11 | Achievement and trophy system with progress tracking and pla... |
| [Actor](#actor) | 1.0.0 | 16 | Distributed actor management and execution for NPC brains, e... |
| [Analytics](#analytics) | 1.0.0 | 9 | Event ingestion, entity statistics, skill ratings (Glicko-2)... |
| [Asset](#asset) | 1.0.0 | 20 | Asset management service for storage, versioning, and distri... |
| [Auth](#auth) | 4.0.0 | 14 | Authentication and session management service (Internet-faci... |
| [Behavior](#behavior) | 3.0.0 | 6 | Arcadia Behavior Markup Language (ABML) API for character be... |
| [Character](#character) | 1.0.0 | 12 | Character management service for game worlds. |
| [Character Encounter](#character-encounter) | 1.0.0 | 21 | Character encounter tracking service for memorable interacti... |
| [Character History](#character-history) | 1.0.0 | 12 | Historical event participation and backstory management for ... |
| [Character Personality](#character-personality) | 1.0.0 | 12 | Machine-readable personality traits for NPC behavior decisio... |
| [Common](#common) | 1.0.0 | 0 | Shared type definitions used across multiple Bannou services... |
| [Connect](#connect) | 2.0.0 | 5 | Real-time communication and WebSocket connection management ... |
| [Contract](#contract) | 1.0.0 | 30 | Binding agreements between entities with milestone-based pro... |
| [Currency](#currency) | 1.0.0 | 32 | Multi-currency management service for game economies. |
| [Documentation](#documentation) | 1.0.0 | 27 | Knowledge base API for AI agents to query documentation.
Des... |
| [Escrow](#escrow) | 1.0.0 | 22 | Full-custody orchestration layer for multi-party asset excha... |
| [Game Service](#game-service) | 1.0.0 | 5 | Registry service for game services that users can subscribe ... |
| [Game Session](#game-session) | 2.0.0 | 11 | Minimal game session management for games. |
| [Inventory](#inventory) | 1.0.0 | 16 | Container and inventory management service for games. |
| [Item](#item) | 1.0.0 | 14 | Item template and instance management service. |
| [Leaderboard](#leaderboard) | 1.0.0 | 12 | Real-time leaderboard management using Redis Sorted Sets for... |
| [Location](#location) | 1.0.0 | 18 | Location management service for game worlds. |
| [Mapping](#mapping) | 1.0.0 | 18 | Spatial data management service for game worlds. |
| [Matchmaking](#matchmaking) | 1.0.0 | 11 | Matchmaking service for competitive and casual game matching... |
| [Mesh](#mesh) | 1.0.0 | 8 | Native service mesh plugin providing direct service-to-servi... |
| [Messaging](#messaging) | 1.0.0 | 4 | Native RabbitMQ pub/sub messaging with native serialization. |
| [Music](#music) | 1.0.0 | 8 | Pure computation music generation using formal music theory ... |
| [Orchestrator](#orchestrator) | 3.0.0 | 22 | Central intelligence for Bannou environment management and s... |
| [Permission](#permission) | 3.0.0 | 8 | Redis-backed high-performance permission system for WebSocke... |
| [Puppetmaster](#puppetmaster) | 1.0.0 | 6 | Orchestration service for dynamic behaviors, regional watche... |
| [Quest](#quest) | 1.0.0 | 17 | Quest system providing objective-based gameplay progression ... |
| [Realm](#realm) | 1.0.0 | 11 | Realm management service for game worlds. |
| [Realm History](#realm-history) | 1.0.0 | 12 | Historical event participation and lore management for realm... |
| [Relationship](#relationship) | 1.0.0 | 7 | Generic relationship management service for entity-to-entity... |
| [Relationship Type](#relationship-type) | 2.0.0 | 13 | Relationship type management service for game worlds. |
| [Resource](#resource) | 1.0.0 | 17 | Resource reference tracking and lifecycle management. |
| [Save Load](#save-load) | 1.0.0 | 26 | Generic save/load system for game state persistence.
Support... |
| [Scene](#scene) | 1.0.0 | 19 | Hierarchical composition storage for game worlds. |
| [Species](#species) | 2.0.0 | 13 | Species management service for game worlds. |
| [State](#state) | 1.0.0 | 9 | Repository pattern state management with Redis and MySQL bac... |
| [Storyline](#storyline) | 1.0.0 | 15 | Seeded narrative generation from compressed archives using t... |
| [Subscription](#subscription) | 1.0.0 | 7 | Manages user subscriptions to game services.
Tracks which ac... |
| [Telemetry](#telemetry) | 1.0.0 | 2 | Unified observability plugin providing distributed tracing, ... |
| [Voice](#voice) | 1.1.0 | 7 | Voice communication coordination service for P2P and room-ba... |
| [Website](#website) | 1.0.0 | 14 | Public-facing website service for registration, information,... |

---

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Deep Dive**: [docs/plugins/ACCOUNT.md](plugins/ACCOUNT.md)

The Account plugin is an internal-only CRUD service for managing user accounts. It is never exposed directly to the internet - all external account operations go through the Auth service, which calls Account via lib-mesh. The plugin handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional - accounts created via OAuth or Steam may have no email address, in which case they are identified solely by their linked authentication methods.

---

## Achievement {#achievement}

**Version**: 1.0.0 | **Schema**: `schemas/achievement-api.yaml` | **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

The Achievement plugin provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service that periodically recalculates rarity percentages.

---

## Actor {#actor}

**Version**: 1.0.0 | **Schema**: `schemas/actor-api.yaml` | **Deep Dive**: [docs/plugins/ACTOR.md](plugins/ACTOR.md)

Distributed actor management and execution for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters - NOT directly visible to players. Features multiple deployment modes (local `bannou`, `pool-per-type`, `shared-pool`, `auto-scale`), ABML behavior document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, encounter management for Event Brain actors, and pool-based distributed execution with heartbeat monitoring. The runtime (ActorRunner) executes configurable tick-based behavior loops with periodic state persistence and character state publishing.

---

## Analytics {#analytics}

**Version**: 1.0.0 | **Schema**: `schemas/analytics-api.yaml` | **Deep Dive**: [docs/plugins/ANALYTICS.md](plugins/ANALYTICS.md)

The Analytics plugin is the central event aggregation point for all game-related statistics. It handles event ingestion (buffered via Redis sorted sets), entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. It publishes score updates and milestone events that are consumed by the Achievement and Leaderboard services for downstream processing. It subscribes to game session lifecycle events and character/realm history events to automatically ingest analytics data, resolving game service context via cached realm/character lookups.

---

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml` | **Deep Dive**: [docs/plugins/ASSET.md](plugins/ASSET.md)

The Asset service provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. It never routes raw asset data through the WebSocket gateway; instead, it issues pre-signed URLs so clients upload/download directly to the storage backend. The service also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding and optimization.

---

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Deep Dive**: [docs/plugins/AUTH.md](plugins/AUTH.md)

The Auth plugin is the internet-facing authentication and session management service. It handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, and session lifecycle management. It is the primary gateway between external users and the internal service mesh - after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections.

---

## Behavior {#behavior}

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml` | **Deep Dive**: [docs/plugins/BEHAVIOR.md](plugins/BEHAVIOR.md)

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime for NPC behavior management. The plugin provides three core subsystems: (1) a multi-phase ABML compiler pipeline (YAML parse, semantic analysis, variable registration, flow compilation, bytecode emission) that produces stack-based bytecode for a custom instruction set with 50+ opcodes across 7 categories, (2) an A* GOAP planner with urgency-tiered search parameters (low/medium/high) producing action sequences from world state and goal conditions, and (3) a 5-stage cognition pipeline (attention filtering, significance assessment, memory formation, goal impact evaluation, intention formation) with keyword-based memory retrieval. Supports streaming composition via continuation points and extension attachment, variant-based model caching with fallback chains, and behavior bundling through the asset service. The compiler outputs portable bytecode interpreted by both server and client SDKs.

---

## Character {#character}

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER.md](plugins/CHARACTER.md)

The Character service manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from Relationship service, L2), and a compression/archival system for dead characters that generates text summaries and tracks reference counts for cleanup eligibility. Per SERVICE_HIERARCHY, Character (L2) cannot depend on L4 services like CharacterPersonality or CharacterHistory - callers needing that data should aggregate from L4 services directly.

---

## Character Encounter {#character-encounter}

**Version**: 1.0.0 | **Schema**: `schemas/character-encounter-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

Character encounter tracking service for memorable interactions between characters. Manages the lifecycle of encounters (shared interaction records) and perspectives (individual participant views), enabling dialogue triggers ("We've met before..."), grudges/alliances ("You killed my brother!"), quest hooks ("The merchant you saved has a job"), and NPC memory. Implements a multi-participant design where each encounter has one shared record with N perspectives (one per participant), scaling linearly O(N) for group events. Features time-based memory decay (configurable lazy-on-access or scheduled background modes), weighted sentiment aggregation across encounter histories, configurable encounter type codes (6 built-in + custom), automatic encounter pruning per-character and per-pair limits, and ETag-based optimistic concurrency for perspective updates. All state is maintained via manual index management (character, pair, location, global, custom-type) since the state store does not support prefix queries.

---

## Character History {#character-history}

**Version**: 1.0.0 | **Schema**: `schemas/character-history-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

Historical event participation and backstory management for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression. Uses helper abstractions (`IDualIndexHelper`, `IBackstoryStorageHelper`) for storage patterns shared with the realm-history service.

---

## Character Personality {#character-personality}

**Version**: 1.0.0 | **Schema**: `schemas/character-personality-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

Machine-readable personality traits and combat preferences for NPC behavior decisions. Features probabilistic personality evolution based on character experiences (trauma, victory, corruption, etc.) and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes (e.g., -1.0 pacifist to +1.0 confrontational) that shift probabilistically based on experience intensity. Used by the Actor service's behavior system for decision-making.

---

## Common {#common}

**Version**: 1.0.0 | **Schema**: `schemas/common-api.yaml` | **Deep Dive**: [docs/plugins/COMMON.md](plugins/COMMON.md)

Shared type definitions used across multiple Bannou services.
These types are not owned by any specific plugin and provide
system-wide consistency for cross-service concepts.

---

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Deep Dive**: [docs/plugins/CONNECT.md](plugins/CONNECT.md)

WebSocket-first edge gateway service providing zero-copy binary message routing between game clients and backend Bannou services. Manages persistent WebSocket connections with a 31-byte binary protocol header for request routing and a 16-byte response header. Implements client-salted GUID generation (SHA256-based, version 5/6/7 UUIDs) to prevent cross-session security exploits. Supports three connection modes (external, relayed, internal) with per-mode behavior differences for broadcast, auth, and capability handling. Features session shortcuts (pre-bound payload routing for game-specific flows), reconnection windows with token-based session restoration, per-session RabbitMQ subscriptions for server-to-client event delivery, rate limiting, meta endpoint introspection, peer-to-peer client routing, broadcast messaging, internal proxy for stateless HTTP forwarding, and admin notification forwarding for service error events. Registered as Singleton lifetime (unusual for Bannou services) because it maintains in-memory WebSocket connection state across all requests.

---

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Deep Dive**: [docs/plugins/CONTRACT.md](plugins/CONTRACT.md)

Binding agreement management between entities with milestone-based progression, consent flows, prebound API execution, breach handling, guardian custody, and clause-type extensibility. Contracts follow a reactive design principle: external systems tell contracts when conditions are met or failed via API calls; contracts store state, emit events, and execute prebound APIs on state transitions. Templates define the structure (party roles, milestones, terms, enforcement mode), and instances are created from templates with merged terms, party consent tracking, and sequential milestone progression. Integrates with lib-escrow for asset-backed contracts through the guardian locking system, template value substitution, and clause execution. Supports four enforcement modes (advisory, event_only, consequence_based, community), configurable consent deadlines with lazy expiration, ISO 8601 duration-based cure periods for breaches, and batched prebound API execution with configurable parallelism and timeouts.

---

## Currency {#currency}

**Version**: 1.0.0 | **Schema**: `schemas/currency-api.yaml` | **Deep Dive**: [docs/plugins/CURRENCY.md](plugins/CURRENCY.md)

Multi-currency management service for game economies. Handles the full lifecycle of currency definitions (CRUD with scope/realm restrictions, precision, caps, autogain, expiration, item linkage, exchange rates), wallet management (create, get-or-create, freeze, unfreeze, close with balance transfer), balance operations (credit with earn/wallet cap enforcement, debit with negative-balance control, transfer with deterministic deadlock-free locking, batch credit), authorization holds (reserve/capture/release pattern for pre-auth scenarios), currency conversion via exchange-rate-to-base pivot, escrow integration (deposit/release/refund as thin wrappers around debit/credit with earn-cap bypass), transaction history with retention enforcement, and analytics stubs (global supply, wealth distribution). Features idempotency-key deduplication on all mutating balance operations, Redis cache layers for balances and holds, and a configurable CurrencyAutogainTaskService background worker that proactively applies passive income to all eligible wallets. The service uses distributed locks throughout for multi-instance safety and ETag-based optimistic concurrency for wallet/hold state transitions.

---

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml` | **Deep Dive**: [docs/plugins/DOCUMENTATION.md](plugins/DOCUMENTATION.md)

Knowledge base API designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD operations and automated git repository synchronization. Features a trashcan (soft-delete with TTL-based expiration), namespace-scoped search indexes (dual implementation: Redis Search FT.* when available, in-memory ConcurrentDictionary fallback), YAML frontmatter parsing for git-synced content, archive creation via Asset Service bundle uploads, and browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Two background services handle startup index rebuilding and periodic repository sync scheduling. All mutations to repository-bound namespaces are rejected (403 Forbidden) unless the binding is disabled, enforcing git as the single source of truth for bound namespaces.

---

## Escrow {#escrow}

**Version**: 1.0.0 | **Schema**: `schemas/escrow-api.yaml` | **Deep Dive**: [docs/plugins/ESCROW.md](plugins/ESCROW.md)

Full-custody orchestration layer for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes (full-consent requiring cryptographic tokens, initiator-trusted, single-party-trusted). Features a 13-state finite state machine, SHA-256-based token generation for deposit and release authorization, idempotent deposit handling, contract-bound conditional releases, per-party pending count tracking, custom asset type handler registration for extensibility, periodic validation with reaffirmation flow, and arbiter-mediated dispute resolution with split allocation support. Handles currency, items, item stacks, contracts, and custom asset types. Escrow calls lib-currency and lib-inventory APIs directly for asset movements; events are published for observability only.

**Release/Refund Modes**: Configurable confirmation flows via `ReleaseMode` and `RefundMode` enums. For unbound escrows, `ReleaseMode` controls whether releases complete immediately or require downstream service and/or party confirmations. Contract-bound escrows skip release mode logic entirely - they rely on contract fulfillment verification.

---

## Game Service {#game-service}

**Version**: 1.0.0 | **Schema**: `schemas/game-service-api.yaml` | **Deep Dive**: [docs/plugins/GAME-SERVICE.md](plugins/GAME-SERVICE.md)

The Game Service is a minimal registry that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. It provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing.

---

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml` | **Deep Dive**: [docs/plugins/GAME-SESSION.md](plugins/GAME-SESSION.md)

Hybrid lobby/matchmade game session management with subscription-driven shortcut publishing, voice integration, and real-time chat. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission service for `in_game` state tracking, Voice service for room lifecycle, and Subscription service for account eligibility. Features distributed subscriber session tracking via ETag-based optimistic concurrency, publishes WebSocket shortcuts to connected clients enabling one-click game join, **per-game horizontal scaling** via `SupportedGameServices` partitioning, and **generic lobbies** for open catch-all entry points without subscription requirements.

---

## Inventory {#inventory}

**Version**: 1.0.0 | **Schema**: `schemas/inventory-api.yaml` | **Deep Dive**: [docs/plugins/INVENTORY.md](plugins/INVENTORY.md)

Container and item placement management for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly - delegates to lib-item for all item-level operations. Features distributed lock-protected modifications, Redis cache with MySQL backing, multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, nesting depth limits, and graceful degradation when the item service is unavailable. Designed as the placement layer that orchestrates lib-item.

---

## Item {#item}

**Version**: 1.0.0 | **Schema**: `schemas/item-api.yaml` | **Deep Dive**: [docs/plugins/ITEM.md](plugins/ITEM.md)

Dual-model item management with templates (definitions/prototypes) and instances (individual occurrences). Templates define immutable properties (code, game scope, quantity model, soulbound type) and mutable properties (name, description, stats, effects, rarity). Instances represent actual items in the game world with quantity, slot placement, durability, custom stats, and binding state. Features Redis read-through caching with configurable TTLs, optimistic concurrency for distributed list operations, and multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container management.

---

## Leaderboard {#leaderboard}

**Version**: 1.0.0 | **Schema**: `schemas/leaderboard-api.yaml` | **Deep Dive**: [docs/plugins/LEADERBOARD.md](plugins/LEADERBOARD.md)

Real-time leaderboard management built on Redis Sorted Sets for O(log N) ranking operations. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), four score update modes (Replace, Increment, Max, Min), seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order, entity type restrictions, and public/private visibility. Provides percentile calculations, neighbor queries (entries around a given entity), and batch score submission.

---

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml` | **Deep Dive**: [docs/plugins/LOCATION.md](plugins/LOCATION.md)

Hierarchical location management for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation (soft-delete), circular reference prevention during parent reassignment, cascading depth updates, code-based lookups (uppercase-normalized per realm), and bulk seeding with two-pass parent resolution. Features Redis read-through caching with configurable TTL for frequently-accessed locations.

---

## Mapping {#mapping}

**Version**: 1.0.0 | **Schema**: `schemas/mapping-api.yaml` | **Deep Dive**: [docs/plugins/MAPPING.md](plugins/MAPPING.md)

Spatial data management service for Arcadia game worlds. Handles authority-based channel ownership for exclusive write access, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with configurable cell sizes, affordance queries with multi-factor scoring, design-time authoring workflows (checkout/commit/release), and map definition templates. Uses a deterministic channel ID scheme (SHA-256 of region+kind) to ensure one authority per region/kind combination. Features per-kind TTL policies (durable for terrain/navigation/ownership, ephemeral for combat/visual effects), large payload offloading to lib-asset, event aggregation buffering to coalesce rapid object changes, configurable non-authority handling modes (reject_silent, reject_and_alert, accept_and_alert), and three takeover policies for authority succession (preserve_and_diff, require_consume, reset). Does NOT perform client-facing rendering or physics -- it is purely a spatial data store that game servers and NPC brains publish to and query from.

---

## Matchmaking {#matchmaking}

**Version**: 1.0.0 | **Schema**: `schemas/matchmaking-api.yaml` | **Deep Dive**: [docs/plugins/MATCHMAKING.md](plugins/MATCHMAKING.md)

Ticket-based matchmaking with skill windows, query matching, party support, and configurable accept/decline flow. Players join queues by creating tickets with optional skill ratings, string/numeric properties, and query filters. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. Formed matches enter an accept/decline phase with configurable deadline. On full acceptance, creates a matchmade game session via `IGameSessionClient` with reservation tokens and publishes join shortcuts. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

---

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml` | **Deep Dive**: [docs/plugins/MESH.md](plugins/MESH.md)

Native service mesh providing YARP-based HTTP routing and Redis-backed service discovery. Replaces Dapr-style sidecar invocation with direct in-process service-to-service calls. Provides endpoint registration with TTL-based health tracking, five load balancing algorithms (RoundRobin, LeastConnections, Weighted, WeightedRoundRobin, Random), a distributed per-appId circuit breaker with cross-instance synchronization, and configurable retry logic with exponential backoff. Includes a background health check service for proactive failure detection with automatic deregistration after consecutive failures. Event-driven auto-registration from service heartbeats enables zero-configuration discovery.

---

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml` | **Deep Dive**: [docs/plugins/MESSAGING.md](plugins/MESSAGING.md)

The Messaging service is the native RabbitMQ pub/sub infrastructure for Bannou. It operates in a dual role: (1) as an internal infrastructure library (`IMessageBus`/`IMessageSubscriber`/`IMessageTap`) used by all services for event publishing, subscription, and tapping, and (2) as an HTTP API service providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, aggressive retry buffering, and crash-fast philosophy for unrecoverable failures.

---

## Music {#music}

**Version**: 1.0.0 | **Schema**: `schemas/music-api.yaml` | **Deep Dive**: [docs/plugins/MUSIC.md](plugins/MUSIC.md)

Pure computation music generation using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, time, style, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning, contour/density guidance). Generates complete compositions, chord progressions, melodies, and voice-led chord voicings. All generation is deterministic when a seed is provided, enabling Redis-based caching for repeat requests. No external service dependencies - fully self-contained computation.

---

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml` | **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

Central intelligence for Bannou environment management and service orchestration. The Orchestrator manages the full lifecycle of distributed service deployments: deploying preset-based topologies with per-node service enablement, tearing down containers with infrastructure-level control, performing live topology updates (add/remove nodes, move/scale services, update environment), managing processing pools for on-demand worker containers (acquire/release/scale/cleanup), monitoring service health via heartbeat ingestion, maintaining versioned deployment configurations with rollback capability, resolving container orchestration backends (Docker Compose, Docker Swarm, Portainer, Kubernetes), retrieving container logs, cleaning up orphaned resources, listing deployment presets, broadcasting service-to-app-id routing mappings via pub/sub, and invalidating OpenResty routing caches on topology changes. The plugin features a pluggable backend architecture with `IContainerOrchestrator` abstraction, index-based state store patterns (avoiding KEYS/SCAN), ETag-based optimistic concurrency for heartbeat/routing indexes, and a secure mode that makes the orchestrator inaccessible via WebSocket (admin-only service-to-service calls).

---

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Deep Dive**: [docs/plugins/PERMISSION.md](plugins/PERMISSION.md)

Redis-backed RBAC permission system for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via `IClientEventPublisher`. Features idempotent registration (SHA-256 hash comparison), distributed locks for concurrent registration safety, and in-memory caching (`ConcurrentDictionary`) for compiled session capabilities.

---

## Puppetmaster {#puppetmaster}

**Version**: 1.0.0 | **Schema**: `schemas/puppetmaster-api.yaml` | **Deep Dive**: [docs/plugins/PUPPETMASTER.md](plugins/PUPPETMASTER.md)

The Puppetmaster service orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. It "pulls the strings" while actors perform on stage. As an L4 (Game Features) service, it provides the missing link between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3) - enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. The service implements `IBehaviorDocumentProvider` (priority 100) to supply runtime-loaded behaviors to actors via the provider chain pattern.

**Key Responsibilities**:
- Dynamic behavior document caching and loading from Asset service
- Regional watcher lifecycle management (create/stop/list)
- Resource snapshot caching for Event Brain actors
- Automatic watcher startup on realm creation events

---

## Quest {#quest}

**Version**: 1.0.0 | **Schema**: `schemas/quest-api.yaml` | **Deep Dive**: [docs/plugins/QUEST.md](plugins/QUEST.md)

The Quest service provides objective-based gameplay progression as a thin orchestration layer over lib-contract. It translates game-flavored quest semantics (objectives, rewards, quest givers) into the Contract service's infrastructure (milestones, prebound APIs, parties). This design leverages Contract's robust state machine, consent flows, and cleanup orchestration while presenting a player-friendly quest API.

**Layer**: **L2 (GameFoundation)** - Quest is a core game primitive alongside Character, Currency, and Items. It is agnostic to prerequisite sources; L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` to provide prerequisite validation without Quest depending on them. Quest calls L2 services directly for built-in prerequisites (currency, items, character level). The service is internal-only and integrates with the Actor service via the Variable Provider Factory pattern to expose quest data for ABML behavior expressions.

---

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml` | **Deep Dive**: [docs/plugins/REALM.md](plugins/REALM.md)

The Realm service manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support.

---

## Realm History {#realm-history}

**Version**: 1.0.0 | **Schema**: `schemas/realm-history-api.yaml` | **Deep Dive**: [docs/plugins/REALM-HISTORY.md](plugins/REALM-HISTORY.md)

Historical event participation and lore management for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival. Uses shared History infrastructure helpers (`IDualIndexHelper`, `IBackstoryStorageHelper`) for the dual-index pattern and backstory storage, matching the character-history implementation.

---

## Relationship {#relationship}

**Version**: 1.0.0 | **Schema**: `schemas/relationship-api.yaml` | **Deep Dive**: [docs/plugins/RELATIONSHIP.md](plugins/RELATIONSHIP.md)

A generic relationship management service for entity-to-entity relationships (character friendships, alliances, rivalries, etc.). Supports bidirectional uniqueness enforcement via composite keys, polymorphic entity types, and soft-deletion with the ability to recreate ended relationships. Used by the Character service for managing inter-character bonds and by the RelationshipType service for type merge migrations.

---

## Relationship Type {#relationship-type}

**Version**: 2.0.0 | **Schema**: `schemas/relationship-type-api.yaml` | **Deep Dive**: [docs/plugins/RELATIONSHIP-TYPE.md](plugins/RELATIONSHIP-TYPE.md)

Hierarchical relationship type definitions for entity-to-entity relationships in the Arcadia game world. Defines the taxonomy of possible relationships (e.g., PARENT → FATHER/MOTHER, FRIEND, RIVAL) with parent-child hierarchy, inverse type tracking, and bidirectional flags. Supports deprecation with merge capability via `IRelationshipClient` to migrate existing relationships. Provides hierarchy queries (ancestors, children, `matchesHierarchy` for polymorphic matching), code-based lookups, and bulk seeding with dependency-ordered creation. Internal-only service (not internet-facing).

---

## Resource {#resource}

**Version**: 1.0.0 | **Schema**: `schemas/resource-api.yaml` | **Deep Dive**: [docs/plugins/RESOURCE.md](plugins/RESOURCE.md)

Resource reference tracking, lifecycle management, and hierarchical compression for foundational resources. Provides three core capabilities:

1. **Reference Tracking**: Enables foundational services (L2) to safely delete resources by tracking references from higher-layer consumers (L3/L4) without violating the service hierarchy. Higher-layer services publish reference events when they create/delete references to foundational resources.

2. **Cleanup Coordination**: Maintains reference counts using Redis sets and coordinates cleanup callbacks when resources are deleted. Supports CASCADE, RESTRICT, and DETACH deletion policies.

3. **Hierarchical Compression**: Centralizes compression of resources and their dependents. Higher-layer services register compression callbacks that gather data for archival. The Resource service orchestrates callback execution, bundles data into unified archives stored in MySQL, and supports full decompression for data recovery.

**Key Design Principle**: lib-resource (L1) uses opaque string identifiers for `resourceType` and `sourceType`. It does NOT enumerate or validate these against any service registry - that would create implicit coupling to higher layers. The strings are just identifiers that consumers self-report.

**Why L1**: Any layer can depend on L1. Resources being tracked are at L2 or higher, and their consumers are at L3/L4. By placing this service at L1, all layers can use it without hierarchy violations.

---

## Save Load {#save-load}

**Version**: 1.0.0 | **Schema**: `schemas/save-load-api.yaml` | **Deep Dive**: [docs/plugins/SAVE-LOAD.md](plugins/SAVE-LOAD.md)

Generic save/load system for game state persistence with polymorphic ownership, versioned saves, and schema migration. Handles the full lifecycle of save data: slot creation (namespaced by game+owner), writing save data with automatic compression, loading with hot cache acceleration, delta/incremental saves via JSON Patch (RFC 6902), schema version registration with forward migration paths, version pinning/promotion, rolling cleanup based on category-specific retention limits, export/import via ZIP archives through the Asset service, and content hash integrity verification. Features a two-tier storage architecture where saves are immediately acknowledged in Redis hot cache and asynchronously uploaded to MinIO via the Asset service through a background worker with circuit breaker protection. Supports five save categories (QUICK_SAVE, AUTO_SAVE, MANUAL_SAVE, CHECKPOINT, STATE_SNAPSHOT) each with distinct compression and retention defaults. Designed for multi-device cloud sync with conflict detection windowing. Owners can be accounts, characters, sessions, or realms (polymorphic association pattern).

---

## Scene {#scene}

**Version**: 1.0.0 | **Schema**: `schemas/scene-api.yaml` | **Deep Dive**: [docs/plugins/SCENE.md](plugins/SCENE.md)

Hierarchical composition storage for game worlds. Stores and retrieves scene documents as YAML-serialized node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow with heartbeat-extended TTL locks, game-specific validation rules registered per gameId+sceneType, full-text search across names/descriptions/tags, reverse reference and asset usage tracking via secondary indexes, scene duplication with regenerated node IDs, and version history with configurable retention. Does NOT compute world transforms, determine affordances, push data to other services, or interpret node behavior at runtime -- consumers decide what nodes mean. Scene content is serialized to YAML using YamlDotNet and stored in a single MySQL-backed state store under multiple key prefixes. The Scene Composer SDK extensions (attachment points, affordances, asset slots, marker types, volume shapes) are stored as node properties but not interpreted by the service.

---

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml` | **Deep Dive**: [docs/plugins/SPECIES.md](plugins/SPECIES.md)

Realm-scoped species management for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate → merge → delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration, code-based lookups (uppercase-normalized), and cross-service character reference checking to prevent orphaned data. Integrates with Character and Realm services for validation.

---

## State {#state}

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml` | **Deep Dive**: [docs/plugins/STATE.md](plugins/STATE.md)

The State service is the infrastructure abstraction layer that provides all Bannou services with access to Redis and MySQL backends through a unified API. It operates in a dual role: (1) as the `IStateStoreFactory` infrastructure library used by all services for state persistence, and (2) as an HTTP API providing direct state access for debugging and administration. Supports Redis (ephemeral/session data), MySQL (durable/queryable data), and InMemory (testing) backends with optimistic concurrency via ETags, TTL support, sorted sets, and JSON path queries.

### Interface Hierarchy (as of 2026-02-03)

```
IStateStore<T>                    - Core CRUD (all backends)
├── ICacheableStateStore<T>       - Sets, Sorted Sets, Counters, Hashes (Redis + InMemory)
│   └── ISearchableStateStore<T>  - Full-text search (extends Cacheable)
├── IQueryableStateStore<T>       - LINQ queries (MySQL only)
│   └── IJsonQueryableStateStore<T> - JSON path queries (MySQL only)

IRedisOperations                  - Low-level Redis access (Lua scripts, transactions)
```

**Key Design**: `ISearchableStateStore<T>` extends `ICacheableStateStore<T>` because all searchable stores are Redis-based and therefore support all cacheable operations (sets, sorted sets, counters, hashes). This ensures proper telemetry instrumentation for all operations when using searchable stores.

**Backend Support Matrix**:

| Interface | Redis | MySQL | InMemory | RedisSearch |
|-----------|:-----:|:-----:|:--------:|:-----------:|
| `IStateStore<T>` | ✅ | ✅ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sets) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Sorted Sets) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Counters) | ✅ | ❌ | ✅ | ✅ |
| `ICacheableStateStore<T>` (Hashes) | ✅ | ❌ | ✅ | ✅ |
| `IQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `IJsonQueryableStateStore<T>` | ❌ | ✅ | ❌ | ❌ |
| `ISearchableStateStore<T>` | ❌ | ❌ | ❌ | ✅ |
| `IRedisOperations` | ✅ | ❌ | ❌ | ❌ |

---

## Storyline {#storyline}

**Version**: 1.0.0 | **Schema**: `schemas/storyline-api.yaml` | **Deep Dive**: [docs/plugins/STORYLINE.md](plugins/STORYLINE.md)

The Storyline service wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements - callers (gods/regional watchers) decide whether to instantiate them. The service is internal-only (Layer 4 Game Features) and requires the `developer` role for all endpoints.

---

## Subscription {#subscription}

**Version**: 1.0.0 | **Schema**: `schemas/subscription-api.yaml` | **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

The Subscription service manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. It publishes `subscription.updated` events that GameSession service consumes for real-time shortcut publishing. Includes a background expiration worker (`SubscriptionExpirationService`) that periodically checks for expired subscriptions and deactivates them. The service is internal-only (never internet-facing) and serves as the canonical source for subscription state.

> **Note**: Auth service previously consumed subscription events incorrectly. This was an architectural error - Auth should only manage JWTs and roles.

---

## Telemetry {#telemetry}

**Version**: 1.0.0 | **Schema**: `schemas/telemetry-api.yaml` | **Deep Dive**: [docs/plugins/TELEMETRY.md](plugins/TELEMETRY.md)

The Telemetry service provides unified observability infrastructure for Bannou services using OpenTelemetry standards. It operates in a dual role: (1) as the `ITelemetryProvider` infrastructure interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and (2) as an HTTP API providing health and status endpoints for observability configuration. The service is internal-only (empty x-permissions) and unique in that it does not use state stores or publish events - it purely configures and exposes OpenTelemetry SDK settings.

---

## Voice {#voice}

**Version**: 1.1.0 | **Schema**: `schemas/voice-api.yaml` | **Deep Dive**: [docs/plugins/VOICE.md](plugins/VOICE.md)

The Voice service provides WebRTC-based voice communication for game sessions, supporting both P2P mesh topology (small groups) and scaled tier via SFU (Selective Forwarding Unit) for large rooms. Integrates with Kamailio (SIP proxy) and RTPEngine (media relay) for the scaled tier. Features automatic tier upgrade when P2P rooms exceed capacity, permission-state-gated SDP answer exchange, and session-based participant tracking for privacy.

---

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml` | **Deep Dive**: [docs/plugins/WEBSITE.md](plugins/WEBSITE.md)

Public-facing website service for browser-based access to news, account profile viewing, game downloads, CMS page management, and contact forms. This is a **pure CMS service** in Layer 3 (App Features) that intentionally does NOT access game data (characters, subscriptions, realms) to respect the service hierarchy. This is a unique service in Bannou because it uses traditional REST HTTP methods (GET, PUT, DELETE) with path parameters for browser compatibility (bookmarkable URLs, SEO, caching), which is an explicit exception to the POST-only API pattern used by all other services. The service is currently a complete stub -- every endpoint logs a debug message and returns `StatusCodes.NotImplemented`. No business logic, state storage, or cross-service calls are implemented. The schema defines a CMS-oriented API with page management, theme configuration, site settings, news, downloads, contact forms, and authenticated account profile viewing. When implemented, this service will require integration with the account service via generated clients, plus state stores for CMS data.

---

## Summary

- **Total services**: 46
- **Total endpoints**: 615

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
