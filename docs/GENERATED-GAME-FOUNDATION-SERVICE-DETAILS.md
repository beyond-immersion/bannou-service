# Generated Game Foundation (L2) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **Game Foundation (L2)** layer.

## Actor {#actor}

**Version**: 1.0.0 | **Schema**: `schemas/actor-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/ACTOR.md](plugins/ACTOR.md)

Distributed actor management and execution (L2 GameFoundation) for NPC brains, event coordinators, and long-running behavior loops. Actors output behavioral state (feelings, goals, memories) to characters -- not directly visible to players. Supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale), ABML behavior document execution with hot-reload, GOAP planning integration, and bounded perception queues with urgency filtering. Receives data from L4 services (personality, encounters, history) via the Variable Provider Factory pattern without depending on them.

## Character {#character}

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/CHARACTER.md](plugins/CHARACTER.md)

The Character service (L2 GameFoundation) manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from lib-relationship), and compression/archival for dead characters via lib-resource. Per the service hierarchy, Character cannot depend on L4 services (personality, history, encounters) -- callers needing that data should aggregate from L4 services directly.

## Collection {#collection}

**Version**: 1.0.0 | **Schema**: `schemas/collection-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/COLLECTION.md](plugins/COLLECTION.md)

The Collection service (L2 GameFoundation) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic content selection based on unlocked entries and area theme configurations. Collection types are opaque strings (not enums), allowing new types without schema changes. Dispatches unlock notifications to registered `ICollectionUnlockListener` implementations via DI for guaranteed in-process delivery (e.g., Seed growth pipeline). Internal-only, never internet-facing.

## Currency {#currency}

**Version**: 1.0.0 | **Schema**: `schemas/currency-api.yaml` | **Endpoints**: 32 | **Deep Dive**: [docs/plugins/CURRENCY.md](plugins/CURRENCY.md)

Multi-currency management service (L2 GameFoundation) for game economies. Handles currency definitions with scope/realm restrictions, wallet lifecycle management, balance operations (credit/debit/transfer with idempotency-key deduplication), authorization holds (reserve/capture/release), currency conversion via exchange-rate-to-base pivot, and escrow integration (deposit/release/refund endpoints consumed by lib-escrow). Features a background autogain worker for passive income and transaction history with configurable retention. All mutating balance operations use distributed locks for multi-instance safety.

## Game Service {#game-service}

**Version**: 1.0.0 | **Schema**: `schemas/game-service-api.yaml` | **Endpoints**: 5 | **Deep Dive**: [docs/plugins/GAME-SERVICE.md](plugins/GAME-SERVICE.md)

The Game Service is a minimal registry (L2 GameFoundation) that maintains a catalog of available games/applications (e.g., Arcadia, Fantasia) that users can subscribe to. Provides simple CRUD operations for managing service definitions, with stub-name-based lookup for human-friendly identifiers. Internal-only, never internet-facing. Referenced by nearly all L2/L4 services for game-scoping operations.

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/GAME-SESSION.md](plugins/GAME-SESSION.md)

Hybrid lobby/matchmade game session management (L2 GameFoundation) with subscription-driven shortcut publishing and voice integration. Manages two session types: **lobby** sessions (persistent, per-game-service entry points auto-created for subscribed accounts) and **matchmade** sessions (pre-created by matchmaking with reservation tokens and TTL-based expiry). Integrates with Permission for `in_game` state tracking, Voice for room lifecycle, and Subscription for account eligibility. Publishes WebSocket shortcuts to connected clients for one-click game join and supports per-game horizontal scaling via `SupportedGameServices` partitioning.

## Inventory {#inventory}

**Version**: 1.0.0 | **Schema**: `schemas/inventory-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/INVENTORY.md](plugins/INVENTORY.md)

Container and item placement management (L2 GameFoundation) for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly -- delegates to lib-item for all item-level operations. Supports multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, and nesting depth limits. Designed as the placement layer that orchestrates lib-item.

## Item {#item}

**Version**: 1.0.0 | **Schema**: `schemas/item-api.yaml` | **Endpoints**: 16 | **Deep Dive**: [docs/plugins/ITEM.md](plugins/ITEM.md)

Dual-model item management (L2 GameFoundation) with templates (definitions/prototypes) and instances (individual occurrences). Templates define item properties (code, game scope, quantity model, stats, effects, rarity); instances represent actual items in the game world with quantity, durability, custom stats, and binding state. Supports multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container placement management.

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/LOCATION.md](plugins/LOCATION.md)

Hierarchical location management (L2 GameFoundation) for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation, circular reference prevention, cascading depth updates, code-based lookups, and bulk seeding with two-pass parent resolution.

## Quest {#quest}

**Version**: 1.0.0 | **Schema**: `schemas/quest-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/QUEST.md](plugins/QUEST.md)

The Quest service (L2 GameFoundation) provides objective-based gameplay progression as a thin orchestration layer over lib-contract. Translates game-flavored quest semantics (objectives, rewards, quest givers) into Contract infrastructure (milestones, prebound APIs, parties), leveraging Contract's state machine and cleanup orchestration while presenting a player-friendly API. Agnostic to prerequisite sources: L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` for validation without Quest depending on them. Exposes quest data to the Actor service via the Variable Provider Factory pattern for ABML behavior expressions.

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/REALM.md](plugins/REALM.md)

The Realm service (L2 GameFoundation) manages top-level persistent worlds in the Arcadia game system. Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world with distinct species populations and cultural contexts. Provides CRUD with deprecation lifecycle and seed-from-configuration support. Internal-only.

## Relationship {#relationship}

**Version**: 2.0.0 | **Schema**: `schemas/relationship-api.yaml` | **Endpoints**: 21 | **Deep Dive**: [docs/plugins/RELATIONSHIP.md](plugins/RELATIONSHIP.md)

A unified relationship management service (L2 GameFoundation) combining entity-to-entity relationships (character friendships, alliances, rivalries) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement, polymorphic entity types, soft-deletion with recreate capability, type deprecation with merge, and bulk seeding. Used by the Character service for inter-character bonds and family tree categorization, and by the Storyline service for narrative generation. Consolidated from the former separate relationship and relationship-type plugins.

## Seed {#seed}

**Version**: 1.0.0 | **Schema**: `schemas/seed-api.yaml` | **Endpoints**: 24 | **Deep Dive**: [docs/plugins/SEED.md](plugins/SEED.md)

Generic progressive growth primitive (L2 GameFoundation) for game entities. Seeds start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. Seeds are polymorphically owned (accounts, actors, realms, characters, relationships) and agnostic to what they represent -- guardian spirits, dungeon cores, combat archetypes, crafting specializations, and governance roles are all equally valid seed types. Seed types are string codes (not enums), allowing new types without schema changes. Each seed type defines its own growth phase labels, capability computation rules, and bond semantics. Consumers register seed types via API, contribute growth via the record API or DI provider listeners (e.g., Collection→Seed pipeline), and query capability manifests to gate actions.

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml` | **Endpoints**: 13 | **Deep Dive**: [docs/plugins/SPECIES.md](plugins/SPECIES.md)

Realm-scoped species management (L2 GameFoundation) for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate, merge, delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration and cross-service character reference checking to prevent orphaned data.

## Subscription {#subscription}

**Version**: 1.0.0 | **Schema**: `schemas/subscription-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

The Subscription service (L2 GameFoundation) manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. Publishes `subscription.updated` events consumed by GameSession for real-time shortcut publishing. Includes a background expiration worker that periodically deactivates expired subscriptions. Internal-only, serves as the canonical source for subscription state.

## Transit {#transit}

**Deep Dive**: [docs/plugins/TRANSIT.md](plugins/TRANSIT.md)

The Transit service (L2 GameFoundation) is the geographic connectivity and movement primitive for Bannou. It completes the spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree), then provides a type registry for **how** things move (transit modes) and temporal tracking for **when** they arrive (journeys computed against Worldstate's game clock). Transit is to movement what Seed is to growth and Collection is to unlocks -- a generic, reusable primitive that higher-layer services orchestrate for domain-specific purposes. Internal-only, never internet-facing.

## Worldstate {#worldstate}

**Version**: 1.0.0 | **Schema**: `schemas/worldstate-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/WORLDSTATE.md](plugins/WORLDSTATE.md)

Per-realm game time authority, calendar system, and temporal event broadcasting service (L2 GameFoundation). Maps real-world time to configurable game-time progression with per-realm time ratios, calendar templates (configurable days, months, seasons, years), and day-period cycles. Publishes boundary events at game-time transitions consumed by other services for time-aligned processing, and provides the `${world.*}` variable namespace to the Actor behavior system via the Variable Provider Factory pattern. Also provides a time-elapsed query API for lazy evaluation patterns (computing game-time duration between two real timestamps accounting for ratio changes and pauses). Game-agnostic: calendar structures, time ratios, and day-period definitions are configured per game service. Internal-only, never internet-facing.

## Summary

- **Services in layer**: 17
- **Endpoints in layer**: 264

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
