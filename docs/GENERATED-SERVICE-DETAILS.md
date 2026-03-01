# Generated Service Details Reference

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

This document provides a compact reference of all Bannou services.

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/ACCOUNT.md](plugins/ACCOUNT.md)

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Achievement {#achievement}

**Version**: 1.0.0 | **Schema**: `schemas/achievement-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

The Achievement plugin (L4 GameFeatures) provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service for periodic rarity recalculation.

## Actor {#actor}

**Version**: 1.0.0 | **Schema**: `schemas/actor-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/ACTOR.md](plugins/ACTOR.md)

Distributed actor management and execution (L2 GameFoundation) for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters -- not directly visible to players. Supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale), ABML behavior document execution with hot-reload, GOAP planning integration, bounded perception queues with urgency filtering, and dynamic character binding (actors can start without a character and bind to one at runtime, transitioning from event brain to character brain mode without relaunch). Receives data from L4 services (personality, encounters, history) via the Variable Provider Factory pattern without depending on them.

## Affix {#affix}

**Deep Dive**: [docs/plugins/AFFIX.md](plugins/AFFIX.md)

Item modifier definition, instance management, and stat computation service (L4 GameFeatures). Owns two layers of data: **definitions** (modifier templates with tiers, mod groups, spawn weights, stat grants) and **instances** (per-item applied modifier state stored in Affix's own state store). Any system that needs to answer "what modifiers does this item have?" or "what is this item worth?" queries lib-affix's typed API. Game-agnostic (PoE-style prefix/suffix tiers, Diablo-style legendary affixes, or simple "quality stars" are all valid configurations). Internal-only, never internet-facing.

## Agency {#agency}

**Deep Dive**: [docs/plugins/AGENCY.md](plugins/AGENCY.md)

The Agency service (L4 GameFeatures) manages the guardian spirit's progressive agency system -- the bridge between Seed's abstract capability data and the client's concrete UX module rendering. It answers the question: "Given this guardian spirit's accumulated experience, what can the player perceive and do?" Game-agnostic: domain codes, modules, and influence types are registered per game service, not hardcoded. Internal-only, never internet-facing.

## Analytics {#analytics}

**Version**: 1.0.0 | **Schema**: `schemas/analytics-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/ANALYTICS.md](plugins/ANALYTICS.md)

The Analytics plugin (L4 GameFeatures) is the central event aggregation point for all game-related statistics. Handles event ingestion, entity summary computation, Glicko-2 skill rating calculations, and controller history tracking. Publishes score updates and milestone events consumed by Achievement and Leaderboard for downstream processing. Subscribes to game session lifecycle and character/realm history events for automatic ingestion. Unlike typical L4 services, Analytics only observes via event subscriptions -- it does not invoke L2/L4 service APIs and should not be called by L1/L2/L3 services.

## Arbitration {#arbitration}

**Deep Dive**: [docs/plugins/ARBITRATION.md](plugins/ARBITRATION.md)

Authoritative dispute resolution service (L4 GameFeatures) for competing claims that need jurisdictional ruling and enforcement. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver adjudication game mechanics. Game-agnostic: procedural templates, arbiter selection rules, and cultural attitudes toward litigation are configured through contract templates and faction governance data at deployment time. Internal-only, never internet-facing.

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/ASSET.md](plugins/ASSET.md)

The Asset service (L3 AppFeatures) provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. Issues pre-signed URLs so clients upload/download directly to the storage backend, never routing raw asset data through the WebSocket gateway. Also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding. Used by lib-behavior, lib-save-load, lib-mapping, and lib-documentation for binary storage needs.

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/AUTH.md](plugins/AUTH.md)

The Auth plugin is the internet-facing authentication and session management service (L1 AppFoundation). Handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, TOTP-based MFA, and session lifecycle management. It is the primary gateway between external users and the internal service mesh -- after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections via lib-connect.

## Behavior {#behavior}

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/BEHAVIOR.md](plugins/BEHAVIOR.md)

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime (L4 GameFeatures) for NPC behavior management. Provides three core subsystems: a multi-phase ABML compiler producing portable stack-based bytecode, an A*-based GOAP planner for action sequence generation from world state and goals, and a 5-stage cognition pipeline for NPC perception and intention formation. Compiled bytecode is interpreted by both the server-side ActorRunner (L2) and client SDKs. Supports streaming composition, variant-based model caching with fallback chains, and behavior bundling through the Asset service.

## Broadcast {#broadcast}

**Deep Dive**: [docs/plugins/BROADCAST.md](plugins/BROADCAST.md)

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast. Game-agnostic: which platforms are enabled and how sentiment categories map to game emotions are configured via environment variables and API calls. Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks (justified T15 exception -- platform callbacks, not browser-facing).

## Character {#character}

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER.md](plugins/CHARACTER.md)

The Character service (L2 GameFoundation) manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from lib-relationship), and compression/archival for dead characters via lib-resource. Per the service hierarchy, Character cannot depend on L4 services (personality, history, encounters) -- callers needing that data should aggregate from L4 services directly.

## Character Encounter {#character-encounter}

**Version**: 1.0.0 | **Schema**: `schemas/character-encounter-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

Character encounter tracking service (L4 GameFeatures) for memorable interactions between characters, enabling NPC memory, dialogue triggers, grudges/alliances, and quest hooks. Manages encounters (shared interaction records) with per-participant perspectives, time-based memory decay, weighted sentiment aggregation, and configurable encounter type codes. Features automatic pruning per-character and per-pair limits, and provides `${encounters.*}` ABML variables to the Actor service's behavior system via the Variable Provider Factory pattern.

## Character History {#character-history}

**Version**: 1.0.0 | **Schema**: `schemas/character-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

Historical event participation and backstory management (L4 GameFeatures) for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression via lib-resource. Shares storage helper abstractions with the realm-history service.

## Character Lifecycle {#character-lifecycle}

**Deep Dive**: [docs/plugins/CHARACTER-LIFECYCLE.md](plugins/CHARACTER-LIFECYCLE.md)

Generational cycle orchestration and genetic heritage service (L4 GameFeatures) for character aging, marriage, procreation, death processing, and cross-generational trait inheritance. The temporal engine that drives the content flywheel by ensuring characters are born, live, age, reproduce, and die -- and that each death produces archives feeding future content. Game-agnostic: lifecycle stages, genetic trait definitions, marriage customs, and death processing are configured through lifecycle configuration and seed data at deployment time. Internal-only, never internet-facing.

## Character Personality {#character-personality}

**Version**: 1.0.0 | **Schema**: `schemas/character-personality-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

Machine-readable personality traits and combat preferences (L4 GameFeatures) for NPC behavior decisions. Features probabilistic personality evolution based on character experiences and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes that shift based on experience intensity. Provides `${personality.*}` and `${combat.*}` ABML variables to the Actor service via the Variable Provider Factory pattern.

## Chat {#chat}

**Version**: 1.0.0 | **Schema**: `schemas/chat-api.yaml` | **Endpoints**: 32 | **Deep Dive**: [docs/plugins/CHAT.md](plugins/CHAT.md)

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, typing indicators via Redis sorted set with server-side expiry, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

## Collection {#collection}

**Version**: 1.0.0 | **Schema**: `schemas/collection-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/COLLECTION.md](plugins/COLLECTION.md)

The Collection service (L2 GameFoundation) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic content selection based on unlocked entries and area theme configurations. Collection types are opaque strings (not enums), allowing new types without schema changes. Dispatches unlock notifications to registered `ICollectionUnlockListener` implementations via DI for guaranteed in-process delivery (e.g., Seed growth pipeline). Internal-only, never internet-facing.

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/CONNECT.md](plugins/CONNECT.md)

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, per-session RabbitMQ subscriptions for server-to-client event delivery, and multi-node broadcast relay via a WebSocket mesh between Connect instances. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Endpoints**: 30 | **Deep Dive**: [docs/plugins/CONTRACT.md](plugins/CONTRACT.md)

Binding agreement management (L1 AppFoundation) between entities with milestone-based progression, consent flows, and prebound API execution on state transitions. Contracts are reactive: external systems report condition fulfillment via API calls; contracts store state, emit events, and execute callbacks. Templates define structure (party roles, milestones, terms, enforcement mode); instances track consent, sequential progression, and breach handling. Used as infrastructure by lib-quest (quest objectives map to contract milestones) and lib-escrow (asset-backed contracts via guardian locking).

## Craft {#craft}

**Deep Dive**: [docs/plugins/CRAFT.md](plugins/CRAFT.md)

Recipe-based crafting orchestration service (L4 GameFeatures) for production workflows, item modification, and skill-gated crafting execution. A thin orchestration layer that composes existing Bannou primitives: lib-item for storage, lib-inventory for material consumption and output placement, lib-contract for multi-step session state machines, lib-currency for costs, and lib-affix for modifier operations on existing items. Game-agnostic: recipe types, proficiency domains, station types, tool categories, and quality formulas are all opaque strings defined per game at deployment time through recipe seeding. Internal-only, never internet-facing.

## Currency {#currency}

**Version**: 1.0.0 | **Schema**: `schemas/currency-api.yaml` | **Endpoints**: 33 | **Deep Dive**: [docs/plugins/CURRENCY.md](plugins/CURRENCY.md)

Multi-currency management service (L2 GameFoundation) for game economies. Handles currency definitions with scope/realm restrictions, wallet lifecycle management, balance operations (credit/debit/transfer with idempotency-key deduplication), authorization holds (reserve/capture/release), currency conversion via exchange-rate-to-base pivot, and escrow integration (deposit/release/refund endpoints consumed by lib-escrow). Features a background autogain worker for passive income and transaction history with configurable retention. All mutating balance operations use distributed locks for multi-instance safety.

## Disposition {#disposition}

**Deep Dive**: [docs/plugins/DISPOSITION.md](plugins/DISPOSITION.md)

Emotional synthesis and aspirational drive service (L4 GameFeatures) for NPC inner life. Maintains per-character **feelings** about specific entities (other characters, locations, factions, organizations, and the guardian spirit) and **drives** (long-term aspirational goals that shape behavior priorities). Feelings are the personality-filtered, experience-weighted, hearsay-colored subjective emotional state that a character carries; drives are intrinsic motivations that emerge from personality, circumstance, and accumulated experience. Game-agnostic: feeling axes, drive types, guardian spirit mechanics, and synthesis weights are configured through disposition configuration and seed data at deployment time. Internal-only, never internet-facing.

## Divine {#divine}

**Version**: 1.0.0 | **Schema**: `schemas/divine-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/DIVINE.md](plugins/DIVINE.md)

Pantheon management service (L4 GameFeatures) for deity entities, divinity economy, and blessing orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver divine game mechanics: god identity is owned here, behavior runs via Actor/Puppetmaster, domain power via Seed, divinity resource via Currency, blessings via Collection/Status, and follower bonds via Relationship. Gods influence characters indirectly through the character's own Actor -- a god's Actor monitors event streams and makes decisions, but the character's Actor receives the consequences. Blessings are entity-agnostic (characters, accounts, deities, or any entity type can receive them). All endpoints are currently stubbed (return `NotImplemented`); see the implementation plan at `docs/plans/DIVINE.md` for the full specification.

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml` | **Endpoints**: 27 | **Deep Dive**: [docs/plugins/DOCUMENTATION.md](plugins/DOCUMENTATION.md)

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Three background services handle index rebuilding, periodic repository sync, and trashcan purge.

## Dungeon {#dungeon}

**Deep Dive**: [docs/plugins/DUNGEON.md](plugins/DUNGEON.md)

Dungeon lifecycle orchestration service (L4 GameFeatures) for living dungeon entities that perceive, grow, and act autonomously within the Bannou actor system. A thin orchestration layer (like Divine over Currency/Seed/Collection, Quest over Contract, Escrow over Currency/Item) that composes existing Bannou primitives to deliver dungeon-as-actor game mechanics. Game-agnostic: dungeon personality types, creature species, and narrative manifestation styles are configured through ABML behaviors and seed type definitions at deployment time. Internal-only, never internet-facing.

## Environment {#environment}

**Deep Dive**: [docs/plugins/ENVIRONMENT.md](plugins/ENVIRONMENT.md)

Environmental state service (L4 GameFeatures) providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability for game worlds. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot generation, and player experience. The missing ecological layer between Worldstate's clock and the behavioral systems that already reference environmental data that doesn't exist. Game-agnostic: biome types, weather distributions, temperature curves, and resource availability are configured through climate template seeding at deployment time -- a space game could model atmospheric composition, a survival game could model wind chill, the service stores float values against string-coded condition axes. Internal-only, never internet-facing.

## Escrow {#escrow}

**Version**: 1.0.0 | **Schema**: `schemas/escrow-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ESCROW.md](plugins/ESCROW.md)

Full-custody orchestration layer (L4 GameFeatures) for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes and a 13-state finite state machine. Handles currency, items, contracts, and extensible custom asset types -- calling lib-currency and lib-inventory directly for asset movements. Integrates with lib-contract for conditional releases where contract fulfillment triggers escrow completion. See Release Modes section below for configurable confirmation flows.

## Ethology {#ethology}

**Deep Dive**: [docs/plugins/ETHOLOGY.md](plugins/ETHOLOGY.md)

Species-level behavioral archetype registry and nature resolution service (L4 GameFeatures) for providing structured behavioral defaults to any entity that runs through the Actor behavior system. The missing middle ground between "hardcoded behavior document defaults" (every wolf is identical) and "full character cognitive stack" (9 variable providers, per-entity persistent state). A structured definition of species-level behavioral baselines with hierarchical overrides (realm, location) and per-individual deterministic noise, exposed as a variable provider to the Actor behavior system. Game-agnostic: behavioral axes are opaque strings -- a horror game could define `stalking_patience` and `ambush_preference`, a farming sim could define `tamability` and `herd_cohesion`. Internal-only, never internet-facing.

## Faction {#faction}

**Version**: 1.0.0 | **Schema**: `schemas/faction-api.yaml` | **Endpoints**: 31 | **Deep Dive**: [docs/plugins/FACTION.md](plugins/FACTION.md)

The Faction service (L4 GameFeatures) models factions as seed-based living entities whose capabilities emerge from growth, not static assignment. As a faction's seed grows through phases (nascent, established, influential, dominant), capabilities unlock: norm definition, enforcement tiers, territory claiming, and trade regulation. Its primary consumer is lib-obligation, which queries faction norms to produce GOAP action cost modifiers for NPC cognition -- resolving a hierarchy of guild, location, and realm baseline norms into a merged norm set. Supports guild memberships with role hierarchy, parent/child organizational structure, territory claims, and inter-faction political connections modeled as seed bonds via lib-seed. Internal-only, never internet-facing.

## Game Service {#game-service}

**Version**: 1.0.0 | **Schema**: `schemas/game-service-api.yaml` | **Endpoints**: 5 | **Deep Dive**: [docs/plugins/GAME-SERVICE.md](plugins/GAME-SERVICE.md)

The Game Service is a minimal registry (L2 GameFoundation) that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. Provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing. Referenced by nearly all L2/L4 services for game-scoping operations.

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/GAME-SESSION.md](plugins/GAME-SESSION.md)

Multiplayer session container primitive (L2 GameFoundation) with subscription-driven shortcut publishing for basic game access. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission for `in_game` state tracking and Subscription for account eligibility. Publishes WebSocket shortcuts to connected clients for one-click game join, lifecycle events for session state changes, and supports per-game horizontal scaling via `SupportedGameServices` partitioning.

GameSession is to players what Inventory is to items: a **container primitive**. It owns who is in what multiplayer context, with distributed locking, reservation tokens, and permission state management. Higher-layer services (Gardener, Matchmaking) create and manage these containers for their own purposes.

## Gardener {#gardener}

**Version**: 1.0.0 | **Schema**: `schemas/gardener-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/GARDENER.md](plugins/GARDENER.md)

Player experience orchestration service (L4 GameFeatures) and the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience. A "garden" is an abstract conceptual space (lobby, in-game, housing, void/discovery) that a player inhabits, with Gardener managing their gameplay context, entity associations, and event routing. Provides the APIs and infrastructure that divine actors (running via Puppetmaster on the L2 Actor runtime) use to manipulate player experiences -- behavior-agnostic, providing primitives not policy. Currently implements the void/discovery garden type only; the broader garden concept (multiple types, garden-to-garden transitions) is the architectural target. Internal-only, never internet-facing.

## Hearsay {#hearsay}

**Deep Dive**: [docs/plugins/HEARSAY.md](plugins/HEARSAY.md)

Social information propagation and belief formation service (L4 GameFeatures) for NPC cognition. Maintains per-character **beliefs** about norms, other characters, and locations -- what an NPC *thinks* they know vs. what is objectively true. Beliefs are acquired through information channels (direct observation, official decree, social contact, rumor, cultural osmosis), carry confidence levels, converge toward reality over time, and can be intentionally manipulated by external actors (gods, propagandists, gossip networks). Game-agnostic: propagation speeds, confidence thresholds, convergence rates, and rumor injection patterns are configured through hearsay configuration and seed data at deployment time. Internal-only, never internet-facing.

## Inventory {#inventory}

**Version**: 1.0.0 | **Schema**: `schemas/inventory-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/INVENTORY.md](plugins/INVENTORY.md)

Container and item placement management (L2 GameFoundation) for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly -- delegates to lib-item for all item-level operations. Supports multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, and nesting depth limits. Designed as the placement layer that orchestrates lib-item.

## Item {#item}

**Version**: 1.0.0 | **Schema**: `schemas/item-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/ITEM.md](plugins/ITEM.md)

Dual-model item management (L2 GameFoundation) with templates (definitions/prototypes) and instances (individual occurrences). Templates define item properties (code, game scope, quantity model, stats, effects, rarity); instances represent actual items in the game world with quantity, durability, custom stats, and binding state. Supports multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container placement management.

## Leaderboard {#leaderboard}

**Version**: 1.0.0 | **Schema**: `schemas/leaderboard-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/LEADERBOARD.md](plugins/LEADERBOARD.md)

Real-time leaderboard management (L4 GameFeatures) built on Redis Sorted Sets. Supports polymorphic entity types (Account, Character, Guild, Actor, Custom), multiple score update modes, seasonal rotation with archival, and automatic score ingestion from Analytics events. Definitions are scoped per game service with configurable sort order and entity type restrictions. Provides percentile calculations, neighbor queries, and batch score submission.

## Lexicon {#lexicon}

**Deep Dive**: [docs/plugins/LEXICON.md](plugins/LEXICON.md)

Structured world knowledge ontology (L4 GameFeatures) that defines what things ARE in terms of decomposed, queryable characteristics. Lexicon is the ground truth registry for entity concepts -- species, objects, phenomena, named individuals, abstract ideas -- broken into traits, hierarchical categories, bidirectional associations, and strategy-relevant implications. It answers "what is a wolf?" not with prose, but with structured data that GOAP planners, behavior expressions, and game systems can reason over. Game-agnostic: entries, trait vocabularies, category hierarchies, and strategy implications are all configured through seed data at deployment time (the True Names metaphysical framework is Arcadia's flavor interpretation of lexicon entry codes). Internal-only, never internet-facing.

## License {#license}

**Version**: 1.0.0 | **Schema**: `schemas/license-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/LICENSE.md](plugins/LICENSE.md)

The License service (L4 GameFeatures) provides grid-based progression boards (skill trees, license boards, tech trees) inspired by Final Fantasy XII's License Board system. It is a thin orchestration layer that combines Inventory (containers for license items), Items (license nodes as item instances), and Contracts (unlock behavior via prebound API execution) to manage entity progression across a grid. Boards support polymorphic ownership via `ownerType` + `ownerId` — characters, accounts, guilds, and locations can all own boards. Internal-only, never internet-facing. See [GitHub Issue #281](https://github.com/beyond-immersion/bannou-service/issues/281) for the original design specification.

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/LOCATION.md](plugins/LOCATION.md)

Hierarchical location management (L2 GameFoundation) for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation, circular reference prevention, cascading depth updates, code-based lookups, and bulk seeding with two-pass parent resolution.

## Loot {#loot}

**Deep Dive**: [docs/plugins/LOOT.md](plugins/LOOT.md)

Loot table management and generation service (L4 GameFeatures) for weighted drop determination, contextual modifier application, and group distribution orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver loot acquisition mechanics. Game-agnostic: table structures, entry weights, context modifiers, distribution modes, and pity thresholds are all opaque configuration defined per game at deployment time through table seeding. Internal-only, never internet-facing.

## Mapping {#mapping}

**Version**: 1.0.0 | **Schema**: `schemas/mapping-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/MAPPING.md](plugins/MAPPING.md)

Spatial data management service (L4 GameFeatures) for Arcadia game worlds. Provides authority-based channel ownership for exclusive write access to spatial regions, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with affordance queries, and design-time authoring workflows (checkout/commit/release). Purely a spatial data store -- does not perform rendering or physics. Game servers and NPC brains publish spatial data to and query from it.

## Market {#market}

**Deep Dive**: [docs/plugins/MARKET.md](plugins/MARKET.md)

Marketplace orchestration service (L4 GameFeatures) for auctions, NPC vendor management, and price discovery. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver game economy exchange mechanics. Game-agnostic: auction house rules, vendor personality templates, fee structures, and pricing modes are configured through market definitions, ABML behaviors, and seed type definitions at deployment time. Internal-only, never internet-facing.

## Matchmaking {#matchmaking}

**Version**: 1.0.0 | **Schema**: `schemas/matchmaking-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/MATCHMAKING.md](plugins/MATCHMAKING.md)

Ticket-based matchmaking (L4 GameFeatures) with skill windows, query matching, party support, and configurable accept/decline flow. A background service processes queues at configurable intervals, expanding skill windows over time until matches form or tickets timeout. On full acceptance, creates a matchmade game session via lib-game-session with reservation tokens and publishes join shortcuts via Connect. Supports immediate match checks on ticket creation, auto-requeue on decline, and pending match state restoration on reconnection.

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MESH.md](plugins/MESH.md)

Native service mesh (L0 Infrastructure) providing direct in-process service-to-service calls with YARP-based HTTP routing and Redis-backed service discovery. Provides endpoint registration with TTL-based health tracking, configurable load balancing, a distributed per-appId circuit breaker, and retry logic with exponential backoff. Includes proactive health checking with automatic deregistration and event-driven auto-registration from Orchestrator heartbeats for zero-configuration discovery.

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml` | **Endpoints**: 4 | **Deep Dive**: [docs/plugins/MESSAGING.md](plugins/MESSAGING.md)

The Messaging service (L0 Infrastructure) is the native RabbitMQ pub/sub infrastructure for Bannou. Operates in a dual role: as the `IMessageBus`/`IMessageSubscriber` infrastructure library used by all services for event publishing and subscription, and as an HTTP API providing dynamic subscription management with HTTP callback delivery. Supports in-memory mode for testing, direct RabbitMQ with channel pooling, and aggressive retry buffering with crash-fast philosophy for unrecoverable failures.

## Music {#music}

**Version**: 1.0.0 | **Schema**: `schemas/music-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/MUSIC.md](plugins/MUSIC.md)

Pure computation music generation (L4 GameFeatures) using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning). Generates complete compositions, chord progressions, melodies, and voice-led voicings. Deterministic when seeded, enabling Redis caching for repeat requests. No external service dependencies -- fully self-contained computation.

## Obligation {#obligation}

**Version**: 1.0.0 | **Schema**: `schemas/obligation-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/OBLIGATION.md](plugins/OBLIGATION.md)

Contract-aware obligation tracking for NPC cognition (L4 GameFeatures), bridging the Contract service's behavioral clauses and the GOAP planner's action cost system to enable NPCs to have "second thoughts" before violating obligations. Provides dynamically-updated action cost modifiers based on active contracts (guild charters, trade agreements, quest oaths), working standalone with raw contract penalties or enriched with personality-weighted moral reasoning when character-personality data is available. Implements `IVariableProviderFactory` providing the `${obligations.*}` namespace to Actor (L2) via the Variable Provider Factory pattern. See [GitHub Issue #410](https://github.com/beyond-immersion/bannou-service/issues/410) for the original design specification ("Second Thoughts" feature).

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

Central intelligence (L3 AppFeatures) for Bannou environment management and service orchestration. Manages distributed service deployments including preset-based topologies, live topology updates, processing pools for on-demand worker containers (used by lib-actor for NPC brains), service health monitoring via heartbeats, versioned deployment configurations with rollback, and service-to-app-id routing broadcasts consumed by lib-mesh. Features a pluggable backend architecture supporting Docker Compose, Docker Swarm, Portainer, and Kubernetes. Operates in a secure mode making it inaccessible via WebSocket (admin-only service-to-service calls).

## Organization {#organization}

**Deep Dive**: [docs/plugins/ORGANIZATION.md](plugins/ORGANIZATION.md)

Legal entity management service (L4 GameFeatures) for organizations that own assets, employ characters, enter contracts, and participate in the economy as first-class entities. A structural layer that gives economic and social entities a legal identity — shops, guilds, households, trading companies, temples, military units, criminal enterprises, and any other group that acts as a collective within the game world. Game-agnostic: organization types, role templates, governance relationships, seed growth phases, and charter requirements are all configured through seed type definitions, contract templates, and faction governance data at deployment time. Internal-only, never internet-facing.

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/PERMISSION.md](plugins/PERMISSION.md)

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

## Procedural {#procedural}

**Deep Dive**: [docs/plugins/PROCEDURAL.md](plugins/PROCEDURAL.md)

On-demand procedural 3D asset generation service (L4 GameFeatures) using headless Houdini Digital Assets (HDAs) — self-contained parametric procedural tools packaged as `.hda` files with exposed parameter interfaces (sliders, menus, toggles, ramps) that generate infinite geometry variations from a single authored template — as parametric generation templates. A thin orchestration layer that composes existing Bannou primitives (Asset service for HDA storage and output bundling, Orchestrator for Houdini worker pool management) to deliver procedural geometry generation as an API. Game-agnostic — the service knows nothing about what it generates, it executes HDAs and returns geometry. Internal-only, never internet-facing.

## Puppetmaster {#puppetmaster}

**Version**: 1.0.0 | **Schema**: `schemas/puppetmaster-api.yaml` | **Endpoints**: 6 | **Deep Dive**: [docs/plugins/PUPPETMASTER.md](plugins/PUPPETMASTER.md)

The Puppetmaster service (L4 GameFeatures) orchestrates dynamic behaviors, regional watchers, and encounter coordination for the Arcadia game system. Provides the bridge between the behavior execution runtime (lib-actor at L2) and the asset service (lib-asset at L3), enabling dynamic ABML behavior loading that would otherwise violate the service hierarchy. Implements `IBehaviorDocumentProvider` to supply runtime-loaded behaviors to actors via the provider chain pattern. Also manages regional watcher lifecycle and resource snapshot caching for Event Brain actors. Divine actors launched as regional watchers via Puppetmaster also serve as gardener behavior actors for player experience orchestration -- see [DIVINE.md](DIVINE.md) for the architectural rationale unifying realm-tending and garden-tending under a single divine actor identity.

## Quest {#quest}

**Version**: 1.0.0 | **Schema**: `schemas/quest-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/QUEST.md](plugins/QUEST.md)

The Quest service (L2 GameFoundation) provides objective-based gameplay progression as a thin orchestration layer over lib-contract. Translates game-flavored quest semantics (objectives, rewards, quest givers) into Contract infrastructure (milestones, prebound APIs, parties), leveraging Contract's state machine and cleanup orchestration while presenting a player-friendly API. Agnostic to prerequisite sources: L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` for validation without Quest depending on them. Exposes quest data to the Actor service via the Variable Provider Factory pattern for ABML behavior expressions.

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM.md](plugins/REALM.md)

The Realm service (L2 GameFoundation) manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support. Internal-only.

## Realm History {#realm-history}

**Version**: 1.0.0 | **Schema**: `schemas/realm-history-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM-HISTORY.md](plugins/REALM-HISTORY.md)

Historical event participation and lore management (L4 GameFeatures) for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival via lib-resource. Shares storage helper abstractions with the character-history service.

## Relationship {#relationship}

**Version**: 2.0.0 | **Schema**: `schemas/relationship-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/RELATIONSHIP.md](plugins/RELATIONSHIP.md)

A unified relationship management service (L2 GameFoundation) combining entity-to-entity relationships (character friendships, alliances, rivalries) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement, polymorphic entity types, soft-deletion with recreate capability, type deprecation with merge, and bulk seeding. Used by the Character service for inter-character bonds and family tree categorization, and by the Storyline service for narrative generation. Consolidated from the former separate relationship and relationship-type plugins.

## Resource {#resource}

**Version**: 1.0.0 | **Schema**: `schemas/resource-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/RESOURCE.md](plugins/RESOURCE.md)

Resource reference tracking, lifecycle management, and hierarchical compression service (L1 AppFoundation) for foundational resources. Enables safe deletion of L2 resources by tracking references from higher-layer consumers (L3/L4) without hierarchy violations, coordinates cleanup callbacks with CASCADE/RESTRICT/DETACH policies, and centralizes compression of resources and their dependents into unified MySQL-backed archives. Placed at L1 so all layers can use it; uses opaque string identifiers for resource/source types to avoid coupling to higher layers. Currently integrated by lib-character (L2) for deletion checks, and by lib-actor, lib-character-encounter, lib-character-history, and lib-character-personality (L4) as reference publishers.

## Save Load {#save-load}

**Version**: 1.0.0 | **Schema**: `schemas/save-load-api.yaml` | **Endpoints**: 26 | **Deep Dive**: [docs/plugins/SAVE-LOAD.md](plugins/SAVE-LOAD.md)

Generic save/load system (L4 GameFeatures) for game state persistence with polymorphic ownership (accounts, characters, sessions, realms). Manages save slots, versioned writes with automatic compression, delta/incremental saves via JSON Patch (RFC 6902), schema migration with forward migration paths, and rolling cleanup by save category. Uses a two-tier storage architecture: Redis hot cache for immediate acknowledgment, with async upload to MinIO via the Asset service for durable storage. Supports export/import via ZIP archives and multi-device cloud sync with conflict detection.

## Scene {#scene}

**Version**: 1.0.0 | **Schema**: `schemas/scene-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/SCENE.md](plugins/SCENE.md)

Hierarchical composition storage (L4 GameFeatures) for game worlds. Stores scene documents as node trees with support for multiple node types (group, mesh, marker, volume, emitter, reference, custom), scene-to-scene references with recursive resolution, an exclusive checkout/commit/discard workflow, game-specific validation rules, full-text search, and version history. Does not compute world transforms or interpret node behavior at runtime -- consumers decide what nodes mean.

## Seed {#seed}

**Version**: 1.0.0 | **Schema**: `schemas/seed-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/SEED.md](plugins/SEED.md)

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via the record API or DI provider listeners (e.g., Collection→Seed pipeline), and query capability manifests to gate actions.

## Showtime {#showtime}

**Deep Dive**: [docs/plugins/SHOWTIME.md](plugins/SHOWTIME.md)

In-game streaming metagame service (L4 GameFeatures) for simulated audience pools, hype train mechanics, streamer career progression, and real-simulated audience blending. The game-facing layer of the streaming stack -- everything that makes streaming a game mechanic rather than just a platform integration. Game-agnostic: audience personality types, hype train escalation, and streaming milestones are all configured through seed types, collection types, and configuration. Internal-only, never internet-facing.

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml` | **Endpoints**: 13 | **Deep Dive**: [docs/plugins/SPECIES.md](plugins/SPECIES.md)

Realm-scoped species management (L2 GameFoundation) for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate, merge, delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration and cross-service character reference checking to prevent orphaned data.

## State {#state}

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml` | **Endpoints**: 9 | **Deep Dive**: [docs/plugins/STATE.md](plugins/STATE.md)

The State service (L0 Infrastructure) provides all Bannou services with unified access to Redis and MySQL backends through a repository-pattern API. Operates in a dual role: as the `IStateStoreFactory` infrastructure library used by every service for state persistence, and as an HTTP API for debugging and administration. Supports four backends (Redis for ephemeral/session data, MySQL for durable/queryable data, SQLite for self-hosted durable storage, InMemory for testing) with optimistic concurrency via ETags, TTL support, and specialized interfaces for cache operations, LINQ queries, JSON path queries, and full-text search. See the Interface Hierarchy section for the full interface tree and backend support matrix.

## Status {#status}

**Version**: 1.0.0 | **Schema**: `schemas/status-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/STATUS.md](plugins/STATUS.md)

Unified entity effects query layer (L4 GameFeatures) aggregating temporary contract-managed statuses and passive seed-derived capabilities into a single query point. Any system needing "what effects does this entity have" -- combat buffs, death penalties, divine blessings, subscription benefits -- queries lib-status. Follows the "items in inventories" pattern: status templates define effect definitions, status containers hold per-entity inventory containers, and granting a status creates an item instance in that container. Contract integration is optional per-template for complex lifecycle; simple TTL-based statuses use lib-item's native decay system. Internal-only, never internet-facing.

## Storyline {#storyline}

**Version**: 1.0.0 | **Schema**: `schemas/storyline-api.yaml` | **Endpoints**: 15 | **Deep Dive**: [docs/plugins/STORYLINE.md](plugins/STORYLINE.md)

The Storyline service (L4 GameFeatures) wraps the `storyline-theory` and `storyline-storyteller` SDKs to provide HTTP endpoints for seeded narrative generation from compressed archives. Plans describe narrative arcs with phases, actions, and entity requirements -- callers (gods/regional watchers) decide whether to instantiate them. Internal-only, requires the `developer` role for all endpoints.

## Subscription {#subscription}

**Version**: 1.0.0 | **Schema**: `schemas/subscription-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

The Subscription service (L2 GameFoundation) manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. Publishes `subscription.updated` events consumed by GameSession for real-time shortcut publishing, and pushes `subscription.status_changed` client events to connected players via WebSocket account-session routing. Includes a background expiration worker that periodically deactivates expired subscriptions. Internal-only, serves as the canonical source for subscription state.

## Telemetry {#telemetry}

**Version**: 1.0.0 | **Schema**: `schemas/telemetry-api.yaml` | **Endpoints**: 2 | **Deep Dive**: [docs/plugins/TELEMETRY.md](plugins/TELEMETRY.md)

The Telemetry service (L0 Infrastructure, optional) provides unified observability infrastructure for Bannou using OpenTelemetry standards. Operates in a dual role: as the `ITelemetryProvider` interface that lib-state, lib-messaging, and lib-mesh use for instrumentation, and as an HTTP API providing health and status endpoints. Unique among Bannou services: uses no state stores and publishes no events. When disabled, other L0 services receive a `NullTelemetryProvider` (all methods are no-ops).

## Trade {#trade}

**Deep Dive**: [docs/plugins/TRADE.md](plugins/TRADE.md)

The Trade service (L4 GameFeatures) is the economic logistics and supply orchestration layer for Bannou. It provides the mechanisms for moving goods across distances over game-time, enforcing border policies, calculating supply/demand dynamics, and enabling NPC economic decision-making. Trade is to the economy what Puppetmaster is to NPC behavior -- an orchestration layer that composes lower-level primitives (Transit for movement, Currency for payments, Item/Inventory for cargo, Escrow for custody) into higher-level economic flows. Internal-only, never internet-facing.

## Transit {#transit}

**Version**: 1.0.0 | **Schema**: `schemas/transit-api.yaml` | **Endpoints**: 33 | **Deep Dive**: [docs/plugins/TRANSIT.md](plugins/TRANSIT.md)

The Transit service (L2 GameFoundation) is the geographic connectivity and movement primitive for Bannou. It completes the spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree), then provides a type registry for **how** things move (transit modes) and temporal tracking for **when** they arrive (journeys computed against Worldstate's game clock). Transit is to movement what Seed is to growth and Collection is to unlocks -- a generic, reusable primitive that higher-layer services orchestrate for domain-specific purposes. Internal-only, never internet-facing.

## Utility {#utility}

**Deep Dive**: [docs/plugins/UTILITY.md](plugins/UTILITY.md)

The Utility service (L4 GameFeatures) manages infrastructure networks that continuously distribute resources across location hierarchies. It provides the topology, capacity modeling, and flow calculation that transforms Workshop point-production into location-wide service coverage -- answering "does this location have water, and where does it come from?" Where Workshop produces resources at a single point and Trade moves discrete shipments between locations, Utility models **continuous flow through persistent infrastructure** (aqueducts, sewer systems, power grids, magical conduits, messenger networks). The key gameplay consequence: when infrastructure breaks, downstream locations lose service, and the cascade of discovery, investigation, and repair creates emergent content. Internal-only, never internet-facing.

## Voice {#voice}

**Version**: 2.0.0 | **Schema**: `schemas/voice-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/VOICE.md](plugins/VOICE.md)

Voice room coordination service (L3 AppFeatures) providing pure voice rooms as a platform primitive: P2P mesh topology for small groups, Kamailio/RTPEngine-based SFU for larger rooms, automatic tier upgrade, WebRTC SDP signaling, broadcast consent flows for streaming integration, and participant TTL enforcement via background worker. Agnostic to games, sessions, and subscriptions -- voice rooms are generic containers identified by Connect/Auth session IDs. Part of a planned three-service stack (voice, broadcast, showtime) where each delivers value independently; voice provides audio infrastructure while higher layers decide when and why to use it. Moved from L4 to L3 to eliminate a hierarchy violation where GameSession (L2) previously depended on Voice (L4) for room lifecycle.

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml` | **Endpoints**: 14 | **Deep Dive**: [docs/plugins/WEBSITE.md](plugins/WEBSITE.md)

Public-facing website service (L3 AppFeatures) for browser-based access to news, account profiles, game downloads, CMS pages, and contact forms. Intentionally does NOT access game data (characters, subscriptions, realms) to respect the service hierarchy. Uses traditional REST HTTP methods (GET, PUT, DELETE) with path parameters for browser compatibility, which is an explicit exception to Bannou's POST-only pattern. **Currently a complete stub** -- every endpoint returns `NotImplemented`. When implemented, will require lib-account integration and state stores for CMS data.

## Workshop {#workshop}

**Deep Dive**: [docs/plugins/WORKSHOP.md](plugins/WORKSHOP.md)

Time-based automated production service (L4 GameFeatures) for continuous background item generation: assign workers to blueprints, consume materials from source inventories, place outputs in destination inventories over game time. Uses lazy evaluation with piecewise rate segments for accurate production tracking across worker count changes, and a background materialization worker with fair per-entity scheduling. Game-agnostic: blueprint structures, production categories, worker types, and proficiency domains are all opaque configuration defined per game at deployment time through blueprint seeding. Internal-only, never internet-facing.

## Worldstate {#worldstate}

**Version**: 1.0.0 | **Schema**: `schemas/worldstate-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/WORLDSTATE.md](plugins/WORLDSTATE.md)

Per-realm game time authority, calendar system, and temporal event broadcasting service (L2 GameFoundation). Maps real-world time to configurable game-time progression with per-realm time ratios, calendar templates (configurable days, months, seasons, years), and day-period cycles. Publishes boundary events at game-time transitions consumed by other services for time-aligned processing, and provides the `${world.*}` variable namespace to the Actor behavior system via the Variable Provider Factory pattern. Also provides a time-elapsed query API for lazy evaluation patterns (computing game-time duration between two real timestamps accounting for ratio changes and pauses). Game-agnostic: calendar structures, time ratios, and day-period definitions are configured per game service. Internal-only, never internet-facing.

## Summary

- **Total services**: 75
- **Total endpoints**: 892

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
