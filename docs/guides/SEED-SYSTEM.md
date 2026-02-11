# Seed System - Progressive Growth for Living Worlds

> **Version**: 1.0
> **Status**: Seed service implemented; consumers in progress
> **Location**: `plugins/lib-seed/`, `schemas/seed-*.yaml`
> **Related**: [Seed Deep Dive](../plugins/SEED.md), [Gardener Plan](../plans/GARDENER.md), [Behavior System](./BEHAVIOR-SYSTEM.md), [Service Hierarchy](../reference/SERVICE-HIERARCHY.md)

The Seed System provides the foundational growth primitive for Arcadia's progressive mastery model. Seeds are entities that start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. They power guardian spirits, dungeon cores, combat archetypes, crafting specializations, and any future system that needs "progressive growth in a role."

---

## Table of Contents

1. [Core Insight](#1-core-insight)
2. [Architecture](#2-architecture)
3. [How Seeds Work](#3-how-seeds-work)
4. [The Consumer Pattern](#4-the-consumer-pattern)
5. [Planned Consumers](#5-planned-consumers)
6. [External Growth Contributions](#6-external-growth-contributions)
7. [The Gardener: Player Experience Orchestration](#7-the-gardener-player-experience-orchestration)
8. [Capability Push: L4 Consumer Responsibility](#8-capability-push-l4-consumer-responsibility)
9. [Integration with Other Systems](#9-integration-with-other-systems)
10. [Open Design Questions](#10-open-design-questions)

---

## 1. Core Insight

**Seeds track growth in roles, not growth in entities.** A seed doesn't represent an entity -- it represents an entity's progressive mastery of a specific role. The same character can hold a `guardian` seed (growth as a player-controlled spirit), a `dungeon_master` seed (growth in the dungeon mastery role), and a `warrior` seed (growth in the combat archetype). Each tracks independent growth with its own domains, phases, and capabilities. The entity is the owner; the seed is the role.

This is analogous to how Item (L2) is a generic data container -- it doesn't know if it's a sword, a potion, or a currency token. Seeds don't know if they're tracking a guardian spirit's UX agency or a dungeon's spawning capability. The interpretation lives entirely in the consumer.

---

## 2. Architecture

```
                     Seed (L2)
                   /     |      \
                  /      |       \
          Character  Game Session  Relationship
          (L2)       (L2)          (L2)
                                              ┌── Gardener (L4, planned)
                                              │   Player experience orchestrator
                  Consumers ──────────────────┤── Dungeon plugin (L4, planned)
                  (register types,            │   Dungeon core + master seeds
                   contribute growth,         ├── Combat archetypes (future)
                   query capabilities)        ├── Crafting specializations (future)
                                              └── Governance roles (future)
```

### Layer Placement

Seed is **L2 Game Foundation** because it is as fundamental to the game model as Character or Item. Every Arcadia deployment needs seeds -- players have guardian spirits (seeds), dungeons are seeds, combat archetypes are seeds. The seed is a generic primitive that higher-layer services build on.

Consumers live at whatever layer makes sense for their domain. Gardener (player experience) is L4. Dungeon plugins are L4. Nothing about the consumer pattern requires a specific layer -- only that consumers depend downward to Seed at L2.

---

## 3. How Seeds Work

### Seed Types

Seed types are **string codes**, not a fixed enum. New types are introduced by registering a type definition via `seed/type/register` -- no schema changes needed. Each type defines:

- **Growth phases**: Labels and thresholds (e.g., Nascent → Awakening → Aware → Attuned)
- **Capability rules**: Domain-to-capability mappings with fidelity formulas
- **Bond configuration**: Cardinality (pair, group, none) and permanence
- **Owner types**: Which entities can hold seeds of this type
- **Per-owner limits**: Maximum seeds of this type per entity

### Growth Domains

A flat key-value map with dot-separated domain paths and floating-point depth scores:

```
combat              → 5.0
combat.melee        → 3.2
combat.melee.sword  → 1.8
crafting            → 8.0
crafting.smithing   → 6.5
```

Domains are fully dynamic -- created on first contribution. Seed doesn't define what domains exist; consumers do. Any service can contribute growth to any domain.

### Capability Manifests

Computed from growth domains using the seed type's capability rules. The manifest is a generic structured document -- Seed doesn't know what capabilities mean. For guardian spirits, Gardener interprets them as UX modules. For dungeon cores, the dungeon plugin interprets them as spawning permissions.

Three fidelity formulas are supported:
- **linear**: 0 at threshold, 1 at 2x threshold
- **logarithmic**: `log(1 + normalized) / log(2)`, capped at 1.0
- **step**: 0 below threshold, 0.5 at 1-2x, 1.0 at 2x+

### Bonds

Bonds connect seeds with configurable semantics. For guardian spirits, this is the pair bond (Rynax-inspired, permanent, 1:1). For dungeon cores, it could be a dungeon-master bond. Bond strength grows with shared experience. Bonded seeds receive a configurable growth multiplier.

For full implementation details, see the [Seed Deep Dive](../plugins/SEED.md).

---

## 4. The Consumer Pattern

Every seed consumer follows the same pattern:

1. **Register a seed type** via `seed/type/register` (on startup or via API)
2. **Create seeds** via `seed/create` when entities enter roles
3. **Contribute growth** via `seed/growth/record` or `seed/growth/record-batch` when game events occur
4. **Query capabilities** via `seed/capability/get-manifest` to gate actions
5. **Interpret capabilities** in a domain-specific way (UX modules, spawning permissions, faction actions, etc.)

Consumers provide their own orchestration logic on top of the shared Seed foundation. No changes to lib-seed are required to add new consumers -- only new seed type registrations.

### The Right Analogy

Item (L2) is a generic data primitive. Inventory (L2), Escrow (L4), and Save-Load (L4) all consume it, each with their own orchestration. Nobody proposes merging Escrow's multi-party exchange orchestration into Item because it's domain-specific. Similarly, each seed consumer's orchestration is domain-specific and stays at its own layer.

---

## 5. Planned Consumers

lib-seed is designed to be reused by any system that needs progressive growth tracking. The following table shows known and anticipated consumers:

| Consumer | Seed Type Code | Seed Owner | Growth Domains | Capability Output |
|----------|---------------|-----------|----------------|-------------------|
| **Gardener** (player spirits) | `guardian` | Account | combat.\*, crafting.\*, social.\*, trade.\*, magic.\*, exploration.\* | UX capability modules for client |
| **Dungeon plugin** (dungeon consciousness) | `dungeon_core` | Actor | mana_reserves.\*, genetic_library.\*, trap_complexity.\*, domain_expansion.\*, memory_depth.\* | Dungeon spawning/trap/manifestation capabilities |
| **Dungeon plugin** (mastery role) | `dungeon_master` | Character or Actor | perception.\*, command.\*, channeling.\*, coordination.\* | Bond communication/command capabilities |
| **Combat archetypes** (future) | `warrior`, `mage`, `ranger` | Character | archetype-specific combat domains | Class-specific combat abilities and UX |
| **Crafting specializations** (future) | `smith`, `alchemist`, `enchanter` | Character | trade-specific technique/material domains | Recipe unlocks, technique mastery |
| **Governance roles** (future) | `governor`, `guild_leader` | Character or Realm | diplomacy.\*, logistics.\*, taxation.\*, military_command.\* | Political actions and policy capabilities |
| **Faction system** (future) | `faction` | Realm or character group | military.\*, trade.\*, culture.\* | Faction actions and policies |
| **Apprenticeship** (future) | `apprenticeship` | Relationship | technique.\*, lore.\*, material_science.\* | Craftable items, teachable skills |
| **Genetic lineage** (future) | `lineage` | Character household | trait.strength, trait.magical_affinity... | Inheritable character traits |

**Key validation**: The dungeon plugin demonstrates the design's generality by requiring two independent seed types (`dungeon_core` + `dungeon_master`) for a single system. The two seeds grow independently in parallel, connected by a Contract rather than a seed bond. This proves that seeds cleanly model asymmetric role growth within a partnership.

---

## 6. External Growth Contributions

Any service can contribute growth to seeds by calling the Seed service's record API. This is an open integration point -- Seed defines the contract, contributors publish to it. Seed never depends on the contributor.

### Growth Contribution Map

| Contributing Service | Growth Domains | Trigger |
|---------------------|---------------|---------|
| **Character Personality** (L4) | `combat.*`, `social.*` based on trait evolution | Personality trait shift events |
| **Character History** (L4) | `exploration.*`, `survival.*`, `stewardship.*` based on historical participation | History entry creation |
| **Character Encounter** (L4) | `social.*`, `combat.*` based on encounter type | Encounter resolution |
| **Analytics** (L4) | Any domain, based on aggregated statistics | Milestone events |
| **Behavior** (L4) | `magic.*`, `crafting.*` based on ABML execution context | Actor action completion |
| **Quest** (L2) | Domain matching quest category | Quest completion |
| **Collection** (L2) | Domains mapped via seed type `collectionGrowthMappings` | Entry unlock (via `ICollectionUnlockListener` DI pattern) |
| **Gardener** (L4, planned) | Scenario completion domains from template `domainWeights` | Scenario completion events |

Each service knows which domains its data contributes to and calls `seed/growth/record` or `seed/growth/record-batch` with the appropriate domain key and amount. Seed doesn't need to know about any contributing service.

### The Collection Pipeline (Implemented)

Collection (L2) dispatches entry unlock notifications to `SeedCollectionUnlockListener` via the `ICollectionUnlockListener` DI interface. The listener matches entry tags against the seed type's `collectionGrowthMappings` to determine which growth domains to feed and by how much. This is a DI listener pattern (in-process, local-only fan-out) for guaranteed delivery. See the [DI Provider vs Listener](../reference/SERVICE-HIERARCHY.md#di-provider-vs-listener-distributed-safety-mandatory) section in SERVICE-HIERARCHY.md for distributed safety implications.

---

## 7. The Gardener: Player Experience Orchestration

> **Status**: Planned. See [Implementation Plan](../plans/GARDENER.md) for full details.

Gardener is the first and primary consumer of lib-seed. It is the player-side counterpart to Puppetmaster: where Puppetmaster orchestrates what NPCs experience, Gardener orchestrates what players experience.

### Why Gardener is a Separate Service (Not Part of Seed)

Gardener's responsibilities are entirely player-experience-specific:

- **Void navigation**: Personal ambient spaces between game sessions
- **POI spawning**: Dynamic points of interest based on seed growth and drift patterns
- **Scenario routing**: Isolated sandboxes, world slices, or persistent realm entry
- **Deployment phase management**: Alpha (isolated) → Beta (world slices) → Release (persistent)
- **Bond shared void**: Paired players see merged void instances

None of these concepts apply to dungeons, factions, archetypes, or any other seed consumer:

| Consumer | Orchestration | Why It Doesn't Generalize |
|----------|--------------|--------------------------|
| **Gardener** | Void/POI/scenario system | Purely player UX; no other system has a "void" |
| **Dungeon plugin** | ABML cognition + actor state | Dungeon-specific; runs inside Actor runtime |
| **Combat archetypes** | Class ability gating per character | Character-level; integrated with combat system |
| **Governance** | Political simulation per realm | Realm-level; integrated with territorial systems |

### Gardener's Relationship to Seed

Gardener does NOT own seeds. It creates and grows them via lib-seed's API, just as lib-escrow doesn't own currencies or items but orchestrates them via lib-currency and lib-item:

- Player first login → Gardener calls `seed/create` with `ownerType="account"`, `seedTypeCode="guardian"`
- Scenario selection → Gardener calls `seed/growth/get` and `seed/capability/get-manifest`
- Scenario completion → Gardener calls `seed/growth/record-batch` with domain amounts from template weights
- Void entry → Gardener calls `seed/get-by-owner` to find the player's active seed

### When to Extract Shared Orchestration

Not until at least three seed consumers demonstrate genuinely shared orchestration logic beyond seed CRUD (which is already at L2). The abstraction should be extracted from working code, not pre-designed.

---

## 8. Capability Push: L4 Consumer Responsibility

Capabilities are currently pull-only -- consumers call `seed/capability/get-manifest`. Real-time UI updates when capabilities unlock are the **responsibility of L4 consumers**, not Seed.

The pattern: L4 consumers subscribe to `seed.capability.updated` events, filter for their seed type, interpret the capabilities, and forward to connected sessions via `IClientEventPublisher`. Adding client events directly to Seed (L2) would violate the service hierarchy by introducing session/WebSocket awareness into a foundational service.

For guardian spirits, this means Gardener will subscribe to `seed.capability.updated`, interpret the capabilities as UX modules, and push to clients via the Connect service's per-session RabbitMQ channel -- the same mechanism used for permission capability updates.

See [#365](https://github.com/BeyondImmersion/bannou-service/issues/365) for design tracking.

---

## 9. Integration with Other Systems

### Seed → Actor (Variable Provider)

Seed could expose `${seed.*}` variables to the Actor service's behavior system (e.g., `${seed.phase}`, `${seed.capabilities.combat.fidelity}`). This would follow the Variable Provider Factory pattern established by character-personality and character-encounter. See [#361](https://github.com/BeyondImmersion/bannou-service/issues/361).

### Seed Bonds → Connect (Pair Communication)

For bonded seeds, Connect would establish a shared communication channel using the bond ID as the subscription key. The bond exists in lib-seed; the communication channel is managed by Connect. This enables paired players to communicate without needing to unlock in-game social systems. See [#386](https://github.com/BeyondImmersion/bannou-service/issues/386).

### Gardener → Game Session

When a player enters a scenario, Gardener creates a Game Session (matchmade type) with a reservation token. The full service stack becomes available to the scenario instance through the Game Session. For WorldSlice and Persistent modes, the session connects to actual realm state; for Isolated mode, it spins up a sandboxed instance.

### Gardener → Puppetmaster

For scenarios involving NPCs, Gardener delegates NPC orchestration to Puppetmaster. Gardener decides WHAT scenario to run; Puppetmaster decides HOW NPCs behave. Clean separation of concerns.

### Gardener → Matchmaking

For group scenarios, Gardener submits matchmaking tickets to group players into shared scenario instances. Matchmaking handles queuing and acceptance; Gardener handles scenario lifecycle once matched.

---

## 10. Open Design Questions

### Seed-Level

| Question | Status | Reference |
|----------|--------|-----------|
| Per-type growth decay configuration | Open issue | [#352](https://github.com/BeyondImmersion/bannou-service/issues/352) |
| Cross-seed-type growth transfer matrix | Open issue | [#354](https://github.com/BeyondImmersion/bannou-service/issues/354) |
| Bond dissolution endpoint | Open issue | [#362](https://github.com/BeyondImmersion/bannou-service/issues/362) |
| Seed type merge | Open issue | [#374](https://github.com/BeyondImmersion/bannou-service/issues/374) |
| Archived seed cleanup strategy | Open issue | [#366](https://github.com/BeyondImmersion/bannou-service/issues/366) |
| Bond growth multiplier when partner inactive | Open issue | [#367](https://github.com/BeyondImmersion/bannou-service/issues/367) |
| Variable provider for Actor behavior system | Open issue | [#361](https://github.com/BeyondImmersion/bannou-service/issues/361) |
| Capability push notifications (L4 consumer) | Open issue | [#365](https://github.com/BeyondImmersion/bannou-service/issues/365) |

### Gardener-Level

| Question | Status | Reference |
|----------|--------|-----------|
| Void position protocol (POST JSON vs binary) | Deferred | [Gardener Plan](../plans/GARDENER.md) -- start with POST JSON, optimize if needed |
| Growth phase as gate vs convenience | Resolved | Soft filter with configurable strictness |
| Orchestration extraction trigger | Deferred | Wait for 3+ consumers with shared logic |
| Client event pushing for POI updates | Deferred | Requires `gardener-client-events.yaml` |
| Analytics milestone integration | Deferred | Add when Analytics publishes milestone events |

---

*This guide covers the seed system as a whole. For implementation details of the Seed service itself, see the [Seed Deep Dive](../plugins/SEED.md). For the Gardener implementation plan, see [docs/plans/GARDENER.md](../plans/GARDENER.md).*
