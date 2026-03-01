# Transit Plugin Deep Dive

> **Plugin**: lib-transit
> **Schema**: `schemas/transit-api.yaml`
> **Version**: 1.0.0
> **State Store**: transit-modes (MySQL), transit-connections (MySQL), transit-journeys (Redis), transit-journeys-archive (MySQL), transit-connection-graph (Redis), transit-discovery (MySQL), transit-discovery-cache (Redis), transit-lock (Redis)
> **Layer**: GameFoundation
> **Status**: Schemas created, code generated, service implementation in progress.

---

## Overview

The Transit service (L2 GameFoundation) is the geographic connectivity and movement primitive for Bannou. It completes the spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree), then provides a type registry for **how** things move (transit modes) and temporal tracking for **when** they arrive (journeys computed against Worldstate's game clock). Transit is to movement what Seed is to growth and Collection is to unlocks -- a generic, reusable primitive that higher-layer services orchestrate for domain-specific purposes. Internal-only, never internet-facing.

---

## Design Philosophy

**What Transit IS**: A movement capability registry, a connectivity graph, and a travel time calculator.

**What Transit is NOT**: A trade system, a cargo manager, a pathfinding engine, or a combat movement controller. It doesn't know about goods, tariffs, supply chains, or real-time spatial positioning. Those concerns belong to Trade (L4), Inventory (L2), and Mapping (L4) respectively.

### Geography Needs Edges, Not Just Nodes

Location manages a **containment hierarchy**: realms contain regions, regions contain cities, cities contain districts, districts contain buildings. This models "what is inside what" but says nothing about "how do you get from A to B." A city can contain two districts without any information about the road connecting them, the river separating them, or the mountain pass required to reach either from outside.

Transit provides the **connectivity graph** that sits alongside Location's containment tree. Every connection between two locations is an explicit, typed edge with distance, terrain, and mode compatibility. Without Transit, the game world is a tree of nested containers with no concept of proximity, traversal difficulty, or travel time. With Transit, it becomes a navigable geography.

### Movement Is a Primitive, Not a Feature

Any game with locations needs movement between them. A dungeon crawler needs "it takes 2 game-hours to walk from the village to the cave." A strategy game needs "armies move at different speeds on different terrain." An economy simulation needs "goods shipped by wagon take 3 game-days." A social simulation needs "the merchant travels between towns weekly."

By placing Transit at L2, all these use cases are served without hierarchy violations. Actor (L2) can compute NPC travel times. Game Session (L2) can estimate session boundaries. Quest (L2) can set travel-based objectives. Workshop (L2) can factor transport lag into production chains. If Transit were L4, none of these integrations would be possible.

### Declarative Journeys

Transit follows the established Bannou pattern for lifecycle tracking: the **game drives events**, the **plugin records state**. Transit does not automatically progress journeys. It does not move entities. It does not update positions. The game server (or an NPC's Actor brain via game engine SDK) calls `/transit/journey/depart`, `/transit/journey/advance` (or `/transit/journey/advance-batch` for NPC-scale efficiency), and `/transit/journey/arrive` as the entity actually moves through the world. Transit records the temporal commitment, computes ETAs against Worldstate, and publishes events. At 10K+ concurrent journeys, the game engine advances entities in batches per tick rather than making individual API calls.

This is the same pattern used by Scene (checkout/commit/discard), Workshop (lazy evaluation with materialization), and the planned Shipment lifecycle in Trade. The plugin is a state machine and calculator, not an autonomous simulation engine.

### Mode Registry with String Codes

Transit modes are string codes, not enums. `walking`, `horseback`, `river_boat`, `flying_mount`, `teleportation`, `wagon`, `ocean_vessel` -- all registered via API, not hardcoded in schemas. This follows the pattern established by Seed (seed type codes), Collection (collection type codes), and Chat (room type codes). New modes can be added without schema changes, enabling game-specific transportation without platform modification.

A transit mode defines abstract movement capability. A specific horse is an Item instance in an Inventory container. The mode's requirements specify what items, skills, or species are needed to use it. Transit checks compatibility; it doesn't manage the items themselves.

Modes may benefit from **proficiency bonuses via the DI enrichment pattern**. An L4 cost modifier provider queries Status (L4) for seed-derived capabilities and translates them into speed multipliers. A character who has ridden horses their entire life (high riding proficiency from Seed growth, surfaced via Status's unified effects layer) is genuinely faster than a novice. Transit doesn't know about Seed or proficiency directly -- it receives a `SpeedMultiplier` from the cost modifier provider, keeping the boundary clean. This connects Transit to the content flywheel indirectly: an NPC's lifetime of travel proficiency data flows through Status and enrichment providers without Transit coupling to growth mechanics.

### Connection Discovery (Configurable)

Not all connections are common knowledge. A main road between two cities is known to everyone, but a smuggler's tunnel through the mountains, a hidden forest shortcut, or a recently opened pass may only be known to those who have traveled them or heard about them.

Connections have a `discoverable: boolean` flag (default `false` = common knowledge). When `true`, the connection is filtered from route calculations unless the requesting entity has discovered it. Discovery happens through:

1. **Travel**: Completing a journey that uses the connection reveals it permanently
2. **Explicit grant**: The game (or an NPC guide) calls the discovery API to reveal a connection
3. **Hearsay integration**: When available, Hearsay (L4) can reveal connections through rumor propagation

This is configurable per-connection, not per-realm. Most connections (roads, rivers, trade routes) are non-discoverable. Special connections (hidden passages, smuggler routes, ancient paths) are discoverable. Route calculation accepts an optional `entityId`; when provided and connections along the path are discoverable, only discovered connections are included.

### Status Transitions with Optimistic Concurrency

Connection status changes support two modes via `update-status`:

1. **Optimistic concurrency (default)**: The caller specifies `currentStatus` (what they believe the connection's status is) and `newStatus`. If the actual status doesn't match `currentStatus`, the request returns `Bad Request` with the actual status, forcing the caller to refresh and retry. This prevents silent status conflicts between independent actors.

2. **Force update**: When `forceUpdate: true`, `currentStatus` is nullable and ignored. The status is set unconditionally. Use for administrative overrides or when the caller has already resolved conflicts.

This prevents status conflicts between independent actors. Environment (L4) closing a road for a storm while Faction (L4) simultaneously blocks it for a war -- whichever writes second sees a mismatch and retries with awareness of the current state. No transition is silently lost.

### DI-Based Cost Enrichment

Transit is L2. It cannot depend on Disposition (L4 aspirational), Environment (L4 aspirational), Faction (L4), or Hearsay (L4 aspirational). But these services profoundly affect movement cost -- a character afraid of horses has a higher GOAP cost to ride; a storm doubles travel time; hostile territory adds risk.

Transit defines `ITransitCostModifierProvider` in `bannou-service/Providers/`, following the Variable Provider Factory pattern. L4 services implement this interface to enrich transit cost calculations at runtime. Transit discovers providers via `IEnumerable<ITransitCostModifierProvider>` with graceful degradation. If no enrichment providers are registered (minimal deployment), transit costs are base values only.

---

## Core Architecture

### How Transit Completes Geography

```
LOCATION SERVICE (L2)                    TRANSIT SERVICE (L2)
Containment Hierarchy                    Connectivity Graph

Realm: Arcadia                          Connection: Eldoria ←→ Iron Mines
  └─ Region: Heartlands                   distance: 120 km
      ├─ City: Eldoria                     terrain: mountain_road
      │   ├─ District: Market              modes: [walking, horseback, wagon]
      │   └─ District: Docks              seasonal: {winter: closed}
      ├─ Village: Riverside
      │   └─ Building: Inn               Connection: Eldoria ←→ Riverside
      └─ Landmark: Iron Mines              distance: 30 km
                                           terrain: river_path
                                           modes: [walking, horseback, river_boat]
                                           seasonal: {all: open}

                                         Connection: Riverside ←→ Iron Mines
                                           distance: 80 km
                                           terrain: forest_trail
                                           modes: [walking, horseback]
                                           seasonal: {all: open}
```

Location provides the **what** (places exist). Transit provides the **how** (places connect). Together they form a complete spatial model that supports both containment queries ("what buildings are in Eldoria?") and connectivity queries ("how do I get from Eldoria to the Iron Mines?").

### The Three Primitives

```
┌─────────────────────────────────────────────────────────────────┐
│                    TRANSIT MODES (Registry)                       │
│  "walking": speed 5, capacity 1, terrain [any]                      │
│  "horseback": speed 25, capacity 2, terrain [road,trail,plain]      │
│     terrain_mods: {road: 1.2, trail: 0.8, forest: 0.5, mountain: 0.3}│
│  "wagon": speed 10, capacity 500, terrain [road], requires [item]   │
│  "river_boat": speed 15, capacity 100, terrain [river]              │
│  "ocean_vessel": speed 20, capacity 2000, terrain [ocean]           │
│     valid_entities: [caravan, army], cargo_penalty: 0.1              │
│  "flying_mount": speed 40, capacity 2, terrain [any]                │
└─────────────────────┬───────────────────────────────────────────┘
                      │ defines capabilities
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CONNECTIONS (Graph Edges)                      │
│  Eldoria → Iron Mines: 120km, mountain_road, [walk,horse,wagon]  │
│  Eldoria → Riverside: 30km, river_path, [walk,horse,river_boat]  │
│  Riverside → Iron Mines: 80km, forest_trail, [walk,horse]        │
│  Docks → Harbor Island: 15km, ocean, [ocean_vessel]              │
└─────────────────────┬───────────────────────────────────────────┘
                      │ traversed by
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    JOURNEYS (Active Transits)                     │
│  Journey #1: Merchant Galen, horseback                           │
│    Eldoria → Riverside → Iron Mines                              │
│    Departed: Game-Day 142, Hour 8                                │
│    Current: Riverside (waypoint reached)                          │
│    ETA Iron Mines: Game-Day 143, Hour 4                          │
│    Status: in_transit                                             │
└─────────────────────────────────────────────────────────────────┘
```

### Route Calculation

Route calculation is a **pure computation endpoint** -- no state mutation. Given a source location, destination location, preferred mode (or "best available"), and current worldstate, Transit performs a graph search over connections filtered by mode compatibility and seasonal availability, returning ranked route options.

```
/transit/route/calculate
  Input:  Eldoria → Iron Mines, mode: any, worldstate: winter

  Graph search:
    Path 1: Eldoria →(mountain_road)→ Iron Mines
            120km, mountain_road, winter: CLOSED → skip

    Path 2: Eldoria →(river_path)→ Riverside →(forest_trail)→ Iron Mines
            30km + 80km = 110km
            walking: 110km / 5 km/gh = 22 game-hours
            horseback: (30 / 25×0.8) + (80 / 25×0.5) = 1.5 + 6.4 = 7.9 game-hours
              (terrain modifiers: river_path ×0.8, forest_trail ×0.5)
            river_boat (leg 1 only): 30/15 + (80 / 25×0.5) = 2.0 + 6.4 = 8.4 game-hours

  Output: [
    { route: [Eldoria, Riverside, Iron Mines], mode: horseback,
      hours: 7.9, distance: 110, legs: 2, risk: 0.15 },
    { route: [Eldoria, Riverside, Iron Mines], mode: walking,
      hours: 22.0, distance: 110, legs: 2, risk: 0.08 },
  ]

  Note: Mountain road direct route unavailable in winter.
  Horseback has terrain modifiers: {road: 1.2, trail: 0.8, forest: 0.5, mountain: 0.3}
  In summer, the mountain road would appear as:
    { route: [Eldoria, Iron Mines], mode: wagon,
      hours: 12.0, distance: 120, legs: 1, risk: 0.2 }
```

Route calculation does NOT factor in DI cost modifiers (those are entity-specific). It returns objective travel data. The variable provider applies entity-specific modifiers when GOAP evaluates the results.

---

## Data Models

Full schemas in `schemas/transit-api.yaml`. Use `make print-models PLUGIN="transit"` for compact field shapes.

### TransitMode

Movement capability definition registered via API. Key fields: `code` (unique string identifier), `baseSpeedKmPerGameHour` (min 0.1), `terrainSpeedModifiers` (nullable array of `TerrainSpeedModifier`), `passengerCapacity` (min 1), `cargoCapacityKg` (min 0), `cargoSpeedPenaltyRate` (nullable, 0-1), `compatibleTerrainTypes` (string array), `validEntityTypes` (nullable string array), `requirements` (TransitModeRequirements), `fatigueRatePerGameHour` (min 0), `noiseLevelNormalized` (0-1), `realmRestrictions` (nullable uuid array), deprecation triple (`isDeprecated`/`deprecatedAt`?/`deprecationReason`?), `tags` (nullable string array).

**Design notes**:
- `terrainSpeedModifiers`: `effectiveSpeed = baseSpeed × multiplier` for matching terrain type. Terrain type not in the list defaults to multiplier 1.0 (base speed). Null = base speed applies uniformly across all compatible terrain.
- `cargoSpeedPenaltyRate`: Overrides plugin-level `DefaultCargoSpeedPenaltyRate` when set. `0.0` = no cargo penalty (e.g., dedicated cargo vessel, pack mule). Null = use plugin config default.
- `compatibleTerrainTypes`: Empty array = all terrain (e.g., walking, flying).
- `validEntityTypes`: Null = no entity type restriction. Characters are NOT exempt — a mode for armies/caravans may validly exclude characters.
- `requirements`: Item, species, and physical requirements only. Proficiency-based speed bonuses are NOT a mode requirement — they flow through `ITransitCostModifierProvider` from L4 services that query Status for seed-derived capabilities.
- Category A deprecation per IMPLEMENTATION TENETS: connections store `compatibleModes` and journeys store `primaryModeCode`/`legModes` referencing mode codes, so persistent data in OTHER entities references this entity's ID.

### TransitModeRequirements

Sub-model of TransitMode. Fields: `requiredItemTag` (nullable — null = no item needed, e.g. `"mount_horse"` = need item with this tag), `allowedSpeciesCodes` (nullable — null = any species), `excludedSpeciesCodes` (nullable — these species CANNOT use this mode), `minimumPartySize` (default 1, min 1 — some modes need crew, e.g. ocean_vessel: 3), `maximumEntitySizeCategory` (nullable — Category B content code, game-defined at deployment time, e.g. "small"/"medium"/"large" — centaurs can't ride horses).

### TransitConnection

Graph edge between two locations. Key fields: `fromLocationId`/`toLocationId` (Location entities), `bidirectional`, `distanceKm` (min 0.01), `terrainType` (Category B content code), `compatibleModes` (string array — empty = walking only), `seasonalAvailability` (nullable array of `SeasonalAvailabilityEntry`), `baseRiskLevel` (0-1), `riskDescription` (nullable), `status` (enum: `open`|`closed`|`dangerous`|`blocked`|`seasonal_closed`), `statusReason` (nullable), `statusChangedAt`, `discoverable` (default false), `name`/`code` (nullable), `tags` (nullable string array), `fromRealmId`/`toRealmId`/`crossRealm` (derived from locations — never caller-specified).

**Design notes**:
- `seasonalAvailability`: Season strings MUST match the realm's Worldstate season codes. Worldstate uses opaque calendar templates — season names are configurable per realm (a desert realm may have "wet"/"dry", not "winter"/"spring"). The Seasonal Connection Worker queries Worldstate for the realm's current season name and matches against these entries.
- `fromRealmId`/`toRealmId`/`crossRealm`: Derived from endpoint locations via Location service. Cross-realm connections are fully supported at the platform level. Games control cross-realm travel by policy: if a game's world design treats realms as isolated, it simply never creates cross-realm connections.
- `discoverable`: When `true`, filtered from route queries unless the requesting entity has discovered it. Hidden passages, smuggler routes, ancient paths, shortcuts.

### SeasonalAvailabilityEntry

Sub-model: `season` (string — season code matching the realm's Worldstate calendar template, validated against realm's Worldstate season codes on creation), `available` (boolean).

### TransitJourney

Active or completed travel record. Key fields: `entityId`/`entityType` (polymorphic traveler — "character", "npc", "caravan", "army", "creature"), `legs` (TransitJourneyLeg array), `currentLegIndex` (0-based), `primaryModeCode`, `effectiveSpeedKmPerGameHour`, game-time timestamps (`plannedDepartureGameTime`, `actualDepartureGameTime`?, `estimatedArrivalGameTime`, `actualArrivalGameTime`?), `originLocationId`/`destinationLocationId`/`currentLocationId`, `status` (enum: `preparing`|`in_transit`|`at_waypoint`|`arrived`|`interrupted`|`abandoned`), `statusReason` (nullable), `interruptions` (TransitInterruption array), `partySize` (min 1), `cargoWeightKg` (min 0).

### TransitJourneyLeg

Per-leg route segment. Key fields: `connectionId`, `fromLocationId`/`toLocationId`, `modeCode` (overrides journey's `primaryModeCode` when the connection supports a different mode — enables multi-modal journeys: ride horse to river, boat downstream, walk to cave), `distanceKm`/`terrainType` (copied from connection for journey record), `estimatedDurationGameHours`, `waypointTransferTimeGameHours` (nullable — models intra-location transfer time: disembarking a river boat, waiting at a dock, crossing a city to reach the next departure point, changing mounts at a stable; factored into journey ETA and totalGameHours calculations), `status` (enum: `pending`|`in_progress`|`completed`|`skipped`), `completedAtGameTime` (nullable).

### TransitInterruption

Interruption record within a journey. Fields: `legIndex`, `gameTime`, `reason` (e.g. "bandit_attack", "storm", "breakdown", "encounter"), `durationGameHours` (min 0 — 0 if resolved immediately, e.g. instantly repelled attack), `resolved` (boolean — NOT T8 filler: an interruption can be resolved with 0 duration, so this flag is stored entity state distinguishing active interruptions from resolved ones, not derivable from `durationGameHours`).

### TransitRouteOption (Not Persisted)

Returned by `/transit/route/calculate`. Key fields: `waypoints` (ordered location IDs), `connections` (ordered connection IDs), `legCount`, `primaryModeCode`, `legModes` (per-leg mode codes for multi-modal routes), `totalDistanceKm`, `totalGameHours` (includes `waypointTransferTimeGameHours` for each leg), `totalRealMinutes` (approximate — time ratios can change during travel), `averageRisk`/`maxLegRisk`, `allLegsOpen`, `seasonalWarnings` (nullable `SeasonalRouteWarning` array — structured warnings for legs with upcoming seasonal closures, typed sub-model so consumers can render specific UI or make programmatic decisions), `rank` (1 = best option by sort criteria).

### SeasonalRouteWarning

Warning sub-model: `connectionId`, `connectionName` (nullable), `legIndex`, `currentSeason`, `closingSeason`, `closingSeasonIndex` (how many season transitions until closure, 1 = next season).

### TerrainSpeedModifier

Sub-model: `terrainType` (Category B content code), `multiplier` (min 0.01 — speed multiplier applied to base speed, 1.0 = no change).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Type | Usage |
|------------|------|-------|
| lib-state (IStateStoreFactory) | L0 Hard | Persistence for modes, connections, journeys, discoveries, graph cache |
| lib-messaging (IMessageBus) | L0 Hard | Publishing journey lifecycle, connection status, and discovery events |
| lib-mesh | L0 Hard | Service-to-service calls to Location, Worldstate, Character, Species, Inventory |
| lib-location (ILocationClient) | L2 Hard | Validates location existence for connections; reports entity position on journey transitions |
| lib-worldstate (IWorldstateClient) | L2 Hard | Game-time calculations for journey ETAs; seasonal connection status via events |
| lib-character (ICharacterClient) | L2 Hard | Species code lookup for mode compatibility checks |
| lib-species (ISpeciesClient) | L2 Hard | Species property lookup for mode restrictions |
| lib-inventory (IInventoryClient) | L2 Hard | Item tag checks for mode requirements (e.g., mount items) |
| lib-resource (IResourceClient) | L1 Hard | Cleanup callback registration for location/character deletion |
| ITransitCostModifierProvider (DI) | L4 Soft | Optional cost enrichment from Disposition, Environment, Faction, Hearsay, Ethology |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-trade (L4) | Calls route/calculate for shipment cost estimation; creates transit journeys for carrier entities; queries journey archives for velocity calculations |
| lib-actor (L2) | Reads `${transit.*}` variables for NPC travel decisions via Variable Provider Factory |
| lib-quest (L2) | Subscribes to journey departed/arrived/discovery events for travel-based quest objectives |
| lib-collection (L2) | Subscribes to `transit.discovery.revealed` for route discovery collection unlocks |
| lib-character-history (L4) | Subscribes to journey arrived/abandoned events for travel backstory generation |
| lib-analytics (L4) | Subscribes to all journey events for travel pattern aggregation |
| lib-puppetmaster (L4) | Subscribes to connection status-changed events for regional watcher awareness |

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `modeCode` / `primaryModeCode` / `legModes` | B (Content Code) | Opaque string | Transit mode identifier (e.g., "walking", "horseback", "river_boat", "flying_mount", "wagon"). Registered via API, not hardcoded. New modes added without schema changes. Follows the pattern established by Seed type codes and Collection type codes |
| `terrainType` | B (Content Code) | Opaque string | Terrain classification for connections (e.g., "road", "trail", "forest", "mountain", "river", "ocean", "swamp", "desert", "underground", "aerial"). Game-configurable, extensible without schema changes |
| `entityType` (journey traveler) | B (Content Code) | Opaque string | Type of entity traveling (e.g., "character", "npc", "caravan", "army", "creature"). Game-configurable, not restricted to first-class Bannou entities (includes composite game concepts like caravans and armies) |
| `tags` (mode/connection) | B (Content Code) | Opaque string array | Freeform classification tags (e.g., "mount", "vehicle", "magical", "aquatic", "trade_route", "military"). Game-configurable, extensible |
| connection `status` | C (System State) | Service-specific enum (`open`, `closed`, `dangerous`, `blocked`, `seasonal_closed`) | Finite set of system-owned connection operational states; supports optimistic concurrency transitions |
| journey `status` | C (System State) | Service-specific enum (`preparing`, `in_transit`, `at_waypoint`, `arrived`, `interrupted`, `abandoned`) | Finite journey lifecycle state machine; system-owned transitions |
| journey leg `status` | C (System State) | Service-specific enum (`pending`, `in_progress`, `completed`, `skipped`) | Finite leg lifecycle state machine; system-owned transitions |
| `source` (discovery) | B (Content Code) | Opaque string | How a connection was discovered (e.g., "travel", "guide", "hearsay", "map", "quest_reward"). Game-configurable, extensible without schema changes |
| `maximumEntitySizeCategory` (mode requirements) | B (Content Code) | Opaque string | Entity size categories (e.g., "small", "medium", "large") are game-defined at deployment time per IMPLEMENTATION TENETS T14 Test 1. False positive — not a system enum |
| `resolved` (TransitInterruption) | — (Stored State) | boolean | NOT T8 filler. An interruption can be resolved with 0 durationGameHours (immediately repelled). The `resolved` flag is stored entity state distinguishing active interruptions from resolved ones, not derivable from other fields |
| mode `isDeprecated` / `deprecatedAt` / `deprecationReason` | C (System State) | Standard triple-field deprecation model (`boolean`, `timestamp?`, `string?`) | Category A per IMPLEMENTATION TENETS: connections store `compatibleModes` and journeys store `primaryModeCode`/`legModes` referencing mode codes → deprecate/undeprecate/delete required |

---

## State Storage

**Store**: `transit-modes` (Backend: MySQL)
- Purpose: Transit mode definitions (durable registry)

**Store**: `transit-connections` (Backend: MySQL)
- Purpose: Transit connections between locations (durable graph edges)

**Store**: `transit-journeys` (Backend: Redis, prefix: `transit:journey`)
- Purpose: Active transit journeys (hot state, archived to MySQL by background worker)

**Store**: `transit-journeys-archive` (Backend: MySQL)
- Purpose: Archived completed/abandoned journeys (historical record for Trade velocity, Analytics)

**Store**: `transit-connection-graph` (Backend: Redis, prefix: `transit:graph`)
- Purpose: Cached per-realm connection adjacency lists for Dijkstra route calculation

**Store**: `transit-discovery` (Backend: MySQL)
- Purpose: Per-entity connection discovery tracking (permanent world knowledge)

**Store**: `transit-discovery-cache` (Backend: Redis, prefix: `transit:discovery`)
- Purpose: Per-entity discovery set cache for fast route calculation filtering

**Store**: `transit-lock` (Backend: Redis, prefix: `transit:lock`)
- Purpose: Distributed locks for journey state transitions and connection status updates

---

## Events

Full event schemas in `schemas/transit-events.yaml` and `schemas/transit-client-events.yaml`.

### Published Events (Service Bus)

| Topic | Type | Key Payload | Consumers |
|-------|------|-------------|-----------|
| `transit-connection.status-changed` | Custom | connectionId, from/to LocationId, previousStatus, newStatus, reason, forceUpdated, realm fields, crossRealm | Trade (route viability), Actor (NPC replan), Puppetmaster (watcher awareness), Quest (objectives) |
| `transit.journey.departed` | Custom | journeyId, entity, origin/dest, primaryModeCode, ETA, partySize, realm fields, crossRealm | Trade (shipment), Analytics, Puppetmaster, Character History, Quest |
| `transit.journey.waypoint-reached` | Custom | journeyId, entity, waypointLocationId, nextLocationId, legIndex, remainingLegs, connectionId, realmId, crossedRealmBoundary | Trade (border checks), Location (presence), Quest (waypoint objectives) |
| `transit.journey.arrived` | Custom | journeyId, entity, origin/dest, primaryModeCode, totalGameHours, totalDistanceKm, interruptionCount, legsCompleted, realm fields, crossRealm | Trade (delivery), Location (presence), Analytics, Character History, Quest |
| `transit.journey.interrupted` | Custom | journeyId, entity, currentLocationId, currentLegIndex, reason, realmId | Trade (delay), Puppetmaster (rescue/encounter), Analytics (hotspots) |
| `transit.journey.resumed` | Custom | journeyId, entity, current/dest LocationId, currentLegIndex, remainingLegs, modeCode, realmId | Trade, Analytics |
| `transit.journey.abandoned` | Custom | journeyId, entity, origin/dest, abandonedAtLocationId, reason, completed/total legs, realm fields, crossRealm | Trade (stranded), Character History, Analytics |
| `transit.discovery.revealed` | Custom | entityId, connectionId, from/to LocationId, source, realm fields, crossRealm | Collection (unlocks), Quest (objectives), Analytics |
| `transit-mode.registered` | Custom | Full TransitMode entity data | — |
| `transit-mode.updated` | Custom | Full TransitMode + `changedFields` (deprecation includes isDeprecated, deprecatedAt, deprecationReason) | — |
| `transit-mode.deleted` | Custom | code, deletedReason (full entity no longer exists) | — |
| `transit-connection.created` | x-lifecycle | Full TransitConnection entity data | — |
| `transit-connection.updated` | x-lifecycle | Full TransitConnection + changedFields | — |
| `transit-connection.deleted` | x-lifecycle | Full TransitConnection + deletedReason | — |

All events include standard `eventId` (uuid) and `timestamp` (date-time) fields per FOUNDATION TENETS event schema pattern. Mode events are custom (not x-lifecycle) since modes use register/update semantics, not standard CRUD. Connection lifecycle events use `x-lifecycle` for auto-generation. `x-event-publications` in `transit-events.yaml` lists all 14 published events as the authoritative registry.

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.season-changed` | `HandleSeasonChangedAsync` (via `IEventConsumer`) | Triggers the Seasonal Connection Worker to scan connections with `seasonalAvailability` restrictions and update their status for the new season. Connections closing for the new season transition to `seasonal_closed`; connections opening transition to `open`. |

Transit does not subscribe to `location.deleted` or `character.deleted` events for cleanup — this is handled exclusively through lib-resource cleanup callbacks per FOUNDATION TENETS.

### Resource Cleanup (FOUNDATION TENETS)

Transit declares `x-references` in its API schema for lib-resource cleanup coordination. The code generator produces cleanup callback registration from these declarations.

| Target Resource | Source Type | On Delete | Cleanup Action |
|----------------|-------------|-----------|----------------|
| location | transit | CASCADE | Close all connections referencing the deleted location, interrupt active journeys passing through it |
| character | transit | CASCADE | Clear discovery data for the deleted entity, abandon any active journeys |

### Client Events (via IClientEventPublisher)

Per IMPLEMENTATION TENETS (T17), server→client push events use `IClientEventPublisher`, not `IMessageBus`. These target session bindings via the Entity Session Registry ([#426](https://github.com/beyond-immersion/bannou-service/issues/426)). Full schemas in `schemas/transit-client-events.yaml`.

| Event | Target | Key Payload |
|-------|--------|-------------|
| `TransitJourneyUpdated` | character → session | journeyId, entityId, status (discriminator: JourneyStatus enum), currentLocationId, destinationLocationId, estimatedArrivalGameTime, currentLegIndex, remainingLegs, primaryModeCode |
| `TransitDiscoveryRevealed` | character → session | connectionId, from/to LocationId, source, connectionName |
| `TransitConnectionStatusChanged` | realm → session | connectionId, from/to LocationId, previousStatus, newStatus, reason |

Design reference: [#501](https://github.com/beyond-immersion/bannou-service/issues/501)

---

## Configuration

Full schema in `schemas/transit-configuration.yaml`.

| Property | Type | Default | Env Var | Purpose |
|----------|------|---------|---------|---------|
| `DefaultWalkingSpeedKmPerGameHour` | decimal | 5.0 | `TRANSIT_DEFAULT_WALKING_SPEED_KM_PER_GAME_HOUR` | Speed when no mode specified (min 0.1) |
| `MaxRouteCalculationLegs` | int | 8 | `TRANSIT_MAX_ROUTE_CALCULATION_LEGS` | Prevents combinatorial explosion (1-50) |
| `MaxRouteOptions` | int | 5 | `TRANSIT_MAX_ROUTE_OPTIONS` | Max route options returned (1-20) |
| `ConnectionGraphCacheSeconds` | int | 300 | `TRANSIT_CONNECTION_GRAPH_CACHE_SECONDS` | Redis graph cache TTL (0 = no caching) |
| `JourneyArchiveAfterGameHours` | decimal | 168.0 | `TRANSIT_JOURNEY_ARCHIVE_AFTER_GAME_HOURS` | Game-hours after arrival/abandonment before archival to MySQL (0 = immediate) |
| `CargoSpeedPenaltyThresholdKg` | decimal | 100.0 | `TRANSIT_CARGO_SPEED_PENALTY_THRESHOLD_KG` | Cargo weight above this reduces speed |
| `SeasonalConnectionCheckIntervalSeconds` | int | 60 | `TRANSIT_SEASONAL_CONNECTION_CHECK_INTERVAL_SECONDS` | Background worker check interval |
| `DiscoveryCacheTtlSeconds` | int | 600 | `TRANSIT_DISCOVERY_CACHE_TTL_SECONDS` | Per-entity discovery cache TTL (0 = no caching) |
| `DefaultCargoSpeedPenaltyRate` | decimal | 0.3 | `TRANSIT_DEFAULT_CARGO_SPEED_PENALTY_RATE` | Default cargo penalty rate (0-1). Overridden by per-mode `cargoSpeedPenaltyRate` |
| `JourneyArchiveRetentionDays` | int | 365 | `TRANSIT_JOURNEY_ARCHIVE_RETENTION_DAYS` | MySQL archive retention in real days (0 = indefinite) |
| `JourneyArchivalWorkerIntervalSeconds` | int | 300 | `TRANSIT_JOURNEY_ARCHIVAL_WORKER_INTERVAL_SECONDS` | Archival sweep interval in real seconds |
| `AutoUpdateLocationOnTransition` | bool | true | `TRANSIT_AUTO_UPDATE_LOCATION_ON_TRANSITION` | Auto-call Location's `/location/report-entity-position` on journey departure, waypoint arrival, and final arrival. Location's presence has 30s TTL so Transit must re-report on each transition. Both are L2 so direct API calls are used — not events. |

**Cargo speed penalty formula**: `speed_reduction = (cargo - CargoSpeedPenaltyThresholdKg) / (mode.cargoCapacityKg - threshold) × rate` where `rate` = mode's `cargoSpeedPenaltyRate` ?? `DefaultCargoSpeedPenaltyRate`. Max penalty = `rate` (e.g., 0.3 = 30% speed reduction at full capacity).

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<TransitService>` | Structured logging |
| `TransitServiceConfiguration` | Typed configuration access |
| `ITelemetryProvider` | Distributed tracing spans for all async methods per IMPLEMENTATION TENETS |
| `IStateStoreFactory` | State store access (creates 8 stores: transit-modes, transit-connections, transit-journeys, transit-journeys-archive, transit-connection-graph, transit-discovery, transit-discovery-cache, transit-lock) |
| `IMessageBus` | Event publishing (journey lifecycle, connection status, discovery, mode registered/updated/deleted, connection lifecycle) |
| `IEventConsumer` | Subscribes to `worldstate.season-changed` for seasonal connection status updates |
| `ILocationClient` | Location existence validation, entity position reporting via `AutoUpdateLocationOnTransition` (L2 hard dependency) |
| `IWorldstateClient` | Game-time calculations for journey ETAs, seasonal context for connection status (L2 hard dependency) |
| `ICharacterClient` | Species code lookup for mode compatibility checks (L2 hard dependency) |
| `ISpeciesClient` | Species property lookup for mode restrictions (L2 hard dependency) |
| `IInventoryClient` | Item tag checks for mode requirements (L2 hard dependency) |
| `IResourceClient` | Cleanup callback registration for location/character deletion (L1 hard dependency) |
| `IDistributedLockProvider` | Distributed locks for journey state transitions and connection status updates (via transit-lock store) |
| `IEnumerable<ITransitCostModifierProvider>` | DI collection of L4 cost enrichment providers (graceful degradation if none registered) |
| `TransitVariableProviderFactory` | Implements `IVariableProviderFactory` to provide `${transit.*}` variables to Actor (L2) |
| `ITransitRouteCalculator` / `TransitRouteCalculator` | Internal helper for Dijkstra-based route calculation over cached connection graphs |
| `ITransitConnectionGraphCache` / `TransitConnectionGraphCache` | Manages per-realm connection graph cache in Redis. Rebuilds from MySQL on cache miss. Invalidated on connection create/delete/status-change. |
| `SeasonalConnectionWorker` | Background `HostedService` that responds to `worldstate.season-changed` events. Scans connections with `seasonalAvailability` restrictions and updates status. Runs periodic checks every `SeasonalConnectionCheckIntervalSeconds`. |
| `JourneyArchivalWorker` | Background `HostedService` that archives completed/abandoned journeys from Redis to MySQL every `JourneyArchivalWorkerIntervalSeconds`. Also enforces `JourneyArchiveRetentionDays` retention policy. |

---

## API Endpoints

Full request/response schemas in `schemas/transit-api.yaml`. Use `make print-models PLUGIN="transit"` for model shapes.

### Mode Management

| Endpoint | Access | Description | Errors | Events |
|----------|--------|-------------|--------|--------|
| `/transit/mode/register` | developer | Register a new transit mode type | `MODE_CODE_ALREADY_EXISTS` | `transit-mode.registered` |
| `/transit/mode/get` | user | Get a transit mode by code | `MODE_NOT_FOUND` | — |
| `/transit/mode/list` | user | List modes (filters: realmId, terrainType, tags, includeDeprecated) | — | — |
| `/transit/mode/update` | developer | Update a transit mode's properties | `MODE_NOT_FOUND` | `transit-mode.updated` |
| `/transit/mode/deprecate` | developer | Deprecate a mode (Category A, idempotent) | `MODE_NOT_FOUND` | `transit-mode.updated` |
| `/transit/mode/undeprecate` | developer | Reverse deprecation (Category A, idempotent) | `MODE_NOT_FOUND` | `transit-mode.updated` |
| `/transit/mode/delete` | developer | Delete a deprecated mode | `MODE_NOT_FOUND`, `NOT_DEPRECATED`, `ACTIVE_JOURNEYS_EXIST`, `CONNECTIONS_REFERENCE_MODE` | `transit-mode.deleted` |
| `/transit/mode/check-availability` | user | Check which modes an entity can currently use | — | — |

**Key behaviors**:
- **deprecate**: Sets triple-field deprecation model. Existing journeys continue; new journeys cannot use it. Idempotent: returns OK if already deprecated.
- **undeprecate**: Clears deprecation fields. Idempotent: returns OK if not currently deprecated.
- **delete**: Must be deprecated first (Category A). Rejects if active journeys use this mode OR connections list it in `compatibleModes`. Archived journeys retain the mode code as historical data.
- **check-availability**: Checks entity type against mode's `validEntityTypes` (if set), calls lib-inventory for required item tags, calls lib-character/lib-species for species compatibility. Also applies `ITransitCostModifierProvider` enrichment to compute `preferenceCost` and `effectiveSpeed` adjustments (including proficiency-based speed bonuses from Status providers). Returns per-mode: `available` (bool), `unavailableReason` (null if available — "missing_item", "wrong_species", etc.), `effectiveSpeed` (adjusted for cargo), `preferenceCost` (from DI providers, 0.0 = neutral, 1.0 = dreads it). This is the endpoint the variable provider calls internally.

### Connection Management

| Endpoint | Access | Description | Errors | Events |
|----------|--------|-------------|--------|--------|
| `/transit/connection/create` | developer | Create a connection between two locations | `LOCATIONS_NOT_FOUND`, `CONNECTION_ALREADY_EXISTS`, `SAME_LOCATION`, `INVALID_MODE_CODE`, `INVALID_SEASON_KEY` | `transit-connection.created` |
| `/transit/connection/get` | user | Get by ID or code (one of connectionId/code required) | `CONNECTION_NOT_FOUND` | — |
| `/transit/connection/query` | user | Query by location, terrain, mode, status, realm, tags | — | — |
| `/transit/connection/update` | developer | Update properties (not status — use update-status) | `CONNECTION_NOT_FOUND` | `transit-connection.updated` |
| `/transit/connection/update-status` | user | Status transition with optimistic concurrency | `CONNECTION_NOT_FOUND`, `STATUS_MISMATCH` | `transit-connection.status-changed` |
| `/transit/connection/delete` | developer | Delete a connection | `CONNECTION_NOT_FOUND`, `ACTIVE_JOURNEYS_EXIST` | `transit-connection.deleted` |
| `/transit/connection/bulk-seed` | developer | Seed connections from configuration | — | `transit-connection.created` (per connection) |

**Key behaviors**:
- **create**: `fromRealmId`, `toRealmId`, and `crossRealm` are derived from the locations via Location service — callers never specify them. Cross-realm connections fully supported at the platform level; games control cross-realm travel by never creating them if unwanted. Season keys in `seasonalAvailability` are validated against the realm's Worldstate season codes. If the realm's calendar defines seasons `["wet", "dry"]` but the caller provides `{"winter": false}`, the request fails with `INVALID_SEASON_KEY`. For cross-realm connections, season keys are validated against both realms' calendars.
- **update-status**: Separated from general update because status changes are operational (game-driven) while property changes are administrative (developer-driven). Status changes publish events; property changes do not (except via x-lifecycle updated). `STATUS_MISMATCH` returns the actual status so the caller can refresh and retry. The `seasonal_closed` status is managed by the Seasonal Connection Worker using `forceUpdate=true` since it is the authoritative source for seasonal state. `newStatus` cannot be `seasonal_closed` — only the worker sets that.
- **query**: Filters include `fromLocationId`, `toLocationId`, `locationId` (either end), `realmId` (touching this realm), `crossRealm` (filter to cross-realm or intra-realm only), `terrainType`, `modeCode`, `status`, `tags`, `includeSeasonalClosed` (default true), pagination.
- **bulk-seed**: Two-pass resolution: first pass creates connections with location code lookup, second pass validates all mode codes exist. Follows Location's bulk-seed pattern. `realmId` scopes location code resolution and `replaceExisting` deletion. Cross-realm connections can be seeded by omitting `realmId` and using globally-unique location codes.

### Journey Lifecycle

| Endpoint | Access | Description | Errors | Events |
|----------|--------|-------------|--------|--------|
| `/transit/journey/create` | user | Plan a journey (status: preparing) | `ORIGIN_NOT_FOUND`, `DESTINATION_NOT_FOUND`, `MODE_NOT_FOUND`, `MODE_DEPRECATED`, `NO_ROUTE_AVAILABLE`, `MODE_REQUIREMENTS_NOT_MET`, `ENTITY_TYPE_NOT_ALLOWED` | — |
| `/transit/journey/depart` | user | Start travel (preparing → in_transit) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS`, `CONNECTION_CLOSED` | `transit.journey.departed` |
| `/transit/journey/resume` | user | Resume interrupted journey (interrupted → in_transit) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS`, `CONNECTION_CLOSED` | `transit.journey.resumed` |
| `/transit/journey/advance` | user | Mark waypoint reached (completes leg, starts next or arrives) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS`, `NO_CURRENT_LEG` | `transit.journey.waypoint-reached` or `transit.journey.arrived` |
| `/transit/journey/advance-batch` | user | Batch advance for NPC scale | `PARTIAL_FAILURE` | Per-journey events |
| `/transit/journey/arrive` | user | Force-arrive at destination (skip remaining legs) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS` | `transit.journey.arrived` |
| `/transit/journey/interrupt` | user | Interrupt journey (combat, event, breakdown) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS` | `transit.journey.interrupted` |
| `/transit/journey/abandon` | user | Abandon journey (entity stays at current location) | `JOURNEY_NOT_FOUND`, `INVALID_STATUS` | `transit.journey.abandoned` |
| `/transit/journey/get` | user | Get a journey by ID | `JOURNEY_NOT_FOUND` | — |
| `/transit/journey/query-by-connection` | user | List active journeys traversing a specific connection | `CONNECTION_NOT_FOUND` | — |
| `/transit/journey/list` | user | List journeys by entity, realm, status | — | — |
| `/transit/journey/query-archive` | user | Query archived journeys from MySQL | — | — |

**Key behaviors**:
- **create**: Internally calls route/calculate to find the best path and populate legs. Checks mode compatibility via check-availability. Does NOT start the journey — call depart for that. When `preferMultiModal` is true, each leg gets the fastest compatible mode the entity can use on that connection; `primaryModeCode` is set to the mode used for the most legs (plurality).
- **depart vs resume**: Distinct endpoints — depart is for initial departure (preparing → in_transit), resume is for continuing after interruption (interrupted → in_transit). Consumers (Trade, Analytics) can distinguish "started" from "resumed" via different events.
- **advance**: Takes `arrivedAtGameTime` and optional `incidents` array (reason, durationGameHours, description). If this completes the final leg, journey transitions to "arrived" and emits `transit.journey.arrived` instead of `waypoint-reached`. Also auto-reveals discoverable connections traversed during the completed leg.
- **advance-batch**: Batch version for game SDK efficiency at NPC scale (10,000+ concurrent journeys). Each advance processed independently — failure of one does not roll back others. Results include per-journey success/failure (null/non-null journey indicates success per IMPLEMENTATION TENETS). Ordering within batch preserved for same-journeyId multiple advances (advancing through multiple waypoints in a single tick). Events emitted per-journey.
- **arrive (force)**: Marks remaining legs as "skipped" rather than "completed". Used for narrative fast-travel, teleportation, or game-driven skip. Valid from `in_transit` or `at_waypoint`.
- **abandon**: Valid from `preparing`, `in_transit`, `at_waypoint`, or `interrupted`.
- **query-by-connection**: Returns journeys whose current leg uses this connection. Enables "who's on this road?" queries for encounter generation, bandit ambush targeting, caravan interception, and road traffic monitoring. Provides transit-specific context (mode, direction, progress, ETA) that Location's "entities at location" query cannot.
- **list**: Filters include `entityId`, `entityType`, `realmId` (origin OR destination), `crossRealm`, `status`, `activeOnly` (default true — excludes arrived, abandoned), pagination.
- **query-archive**: Reads from MySQL archive store. Filters include `entityId`, `entityType`, `realmId`, `crossRealm`, `originLocationId`, `destinationLocationId`, `modeCode`, `status`, `fromGameTime`/`toGameTime`, pagination. Used by Trade (velocity calculations), Analytics (travel patterns), Character History (travel biography).

### Route Calculation

| Endpoint | Access | Description | Errors | Events |
|----------|--------|-------------|--------|--------|
| `/transit/route/calculate` | user | Calculate route options (pure computation, no state mutation) | `LOCATIONS_NOT_FOUND`, `NO_ROUTE_AVAILABLE` | — |

**Key behaviors**:
- Dijkstra's algorithm with configurable cost function: `fastest` (cost = estimated_hours using `baseSpeed × terrainSpeedModifiers`), `safest` (cost = cumulative_risk), `shortest` (cost = distance_km). Default: `fastest`.
- Multiple connections between the same location pair (parallel edges) are supported. Route calculation evaluates each independently, choosing the best per mode.
- Does NOT apply entity-specific DI cost modifiers — those are applied by the variable provider when GOAP evaluates results. Returns objective travel data.
- When `preferMultiModal` is true, each leg selects the fastest compatible mode. Route options include per-leg mode codes in `legModes[]`.
- Discovery filtering: when `entityId` is provided, discoverable connections are filtered to only those the entity has discovered. When null, discoverable connections are excluded entirely (conservative).
- Cross-realm routing: merges connection graphs for all relevant realms. Cross-realm connections appear in both realms' cached graphs. Games that never create cross-realm connections will never see cross-realm routes.
- `includeSeasonalClosed` (default false) excludes currently closed routes.

### Connection Discovery

| Endpoint | Access | Description | Errors | Events |
|----------|--------|-------------|--------|--------|
| `/transit/discovery/reveal` | user | Reveal a discoverable connection to an entity | `CONNECTION_NOT_FOUND`, `NOT_DISCOVERABLE` | `transit.discovery.revealed` |
| `/transit/discovery/list` | user | List connections an entity has discovered (filter: realmId) | — | — |
| `/transit/discovery/check` | user | Check if entity has discovered specific connections | — | — |

**Key behaviors**:
- **reveal**: Also called internally by journey/advance when a leg uses a discoverable connection. Hearsay (L4) calls this when propagating route knowledge via rumor. Quest rewards can include connection reveals. Response includes `isNew` boolean — NOT a filler property; stored entity state distinguishing first discovery from re-revelation, needed by Collection (only new discoveries trigger unlock events).
- **check**: Returns per-connection: `discovered` (bool), `discoveredAt` (null if not), `source` (how it was discovered, null if not).

**Total endpoints: 33**

---

## Visual Aid

### Journey State Machine

```
                    ┌─────────────┐
                    │  preparing  │
                    └──────┬──────┘
                           │ /journey/depart
                           ▼
    ┌──────────┐    ┌─────────────┐    ┌─────────────┐
    │abandoned │◄───│ in_transit  │───►│at_waypoint  │
    └──────────┘    └──────┬──────┘    └──────┬──────┘
         ▲          /abandon│  │ /interrupt     │ /advance
         │                  │  ▼                │ (next leg)
         │          ┌──────────────┐            │
         ├──────────│ interrupted  │            │
         │          └──────┬───────┘            │
         │                 │ /resume            │
         │                 └──►in_transit       │
         │                                      │
         │    /arrive (force)                   │ /advance
         │    ┌─────────────┐                   │ (final leg)
         └────│  arrived    │◄──────────────────┘
              └─────────────┘

Status transitions:
  preparing    → in_transit (depart) | abandoned (abandon)
  in_transit   → at_waypoint (advance) | arrived (advance final/arrive)
               | interrupted (interrupt) | abandoned (abandon)
  at_waypoint  → in_transit (advance next leg) | arrived (arrive)
               | abandoned (abandon)
  interrupted  → in_transit (resume) | abandoned (abandon)
```

Note: All transitions publish corresponding journey events. The `arrive` endpoint
force-completes a journey (marking remaining legs as "skipped"), used for
teleportation, fast-travel, or narrative skip.

---

## Implementation Status

Schemas created and code generated. Remaining implementation:
1. Implement `TransitService.cs`, `TransitServiceEvents.cs`, `TransitServiceModels.cs`
2. Create `bannou-service/Providers/ITransitCostModifierProvider.cs`
3. Implement background workers (Seasonal Connection Worker, Journey Archival Worker)
4. Write unit tests in `plugins/lib-transit.tests/`

---

## Potential Extensions

1. **Caravan formations (Phase 2)**: Multiple entities traveling together as a group. Group speed = slowest member. Group risk = reduced (safety in numbers). Caravan as an entity type for journeys, with member tracking and formation bonuses. Faction merchant guilds could manage NPC caravans. **Promoted to Phase 2** -- the data model already accommodates this via `entityType: "caravan"` and `partySize`, but Phase 2 needs: a `partyMembers: [uuid]` field on `TransitJourney` for tracking individual members, batch departure/advance for all party members, and a party leader concept for GOAP decisions. The `validEntityTypes` restriction on modes already supports caravan-only modes (e.g., `wagon` with `validEntityTypes: ["caravan", "army"]`).

2. **Fatigue and rest**: Long journeys accumulate fatigue. After a configurable threshold, the entity must rest (journey auto-pauses at next waypoint). Rest duration depends on mode and entity stamina. Creates natural stopping points at inns and camps.

3. **Weather-reactive connections**: Environment (L4) automatically calls `/transit/connection/update-status` when weather conditions make connections impassable. Storms close mountain passes; floods close river crossings; drought closes river boat routes. This is a pure consumer relationship -- Transit exposes the API, Environment calls it.

4. **Mount bonding**: Integration with Relationship (L2) for character-mount relationships. A well-bonded mount has higher effective speed and lower fatigue. Bond strength grows with travel distance. Follows the existing Relationship entity-to-entity model.

5. **Transit fares**: Monetary cost for using certain transit modes or connections. Ferries, toll roads, carriage services, and teleportation portals could have a fare. Two options: (a) Transit stores fare data per-connection/mode and calls Currency (L2) during `journey/depart` to debit the fare -- feasible since both are L2. (b) Fares are purely a Trade (L4) concern that wraps Transit journeys with economic logic. Option (a) keeps fare enforcement at the primitive level (NPCs can't cheat tolls), while (b) keeps Transit purely about movement physics. **Open design question**: should Transit know about money, or should fares be an L4 overlay?

6. ~~**Client events for journey tracking and route discovery**~~ — **Promoted to core spec.** See Client Events section above. ([#501](https://github.com/beyond-immersion/bannou-service/issues/501))

---

## Variable Provider (`${transit.*}`)

Transit implements `IVariableProviderFactory` providing the `${transit}` namespace to Actor (L2) via the Variable Provider Factory pattern.

### Provider Implementation

```csharp
// In plugin registration
services.AddSingleton<IVariableProviderFactory, TransitVariableProviderFactory>();
```

### Available Variables

| Variable | Type | Description | Notes |
|----------|------|-------------|-------|
| `${transit.mode.<code>.available}` | boolean | Can this entity currently use this transit mode? | Checks item requirements, species restrictions, skill requirements |
| `${transit.mode.<code>.speed}` | decimal | Effective speed in km/game-hour for this entity on this mode | Accounts for cargo weight penalty and DI cost modifier speed multipliers (including proficiency bonuses from Status providers) |
| `${transit.mode.<code>.preference_cost}` | decimal | GOAP cost modifier from DI enrichment providers. 0.0 = neutral, 1.0 = extreme aversion | Aggregated from Disposition (dread/comfort), Hearsay (believed danger), Ethology (species-level affinity) |
| `${transit.journey.active}` | boolean | Is this entity currently on a journey? | — |
| `${transit.journey.mode}` | string | Transit mode of the active journey | Null if no active journey |
| `${transit.journey.destination_code}` | string | Location code of the journey destination | — |
| `${transit.journey.eta_hours}` | decimal | Estimated game-hours until arrival | — |
| `${transit.journey.progress}` | decimal | Journey progress 0.0 (just departed) to 1.0 (arrived) | — |
| `${transit.journey.remaining_legs}` | integer | Number of route legs remaining | — |
| `${transit.nearest.<location_code>.hours}` | decimal | Best-case travel time in game-hours to the named location | Uses best available mode. Cached per-entity, refreshed on location change. |
| `${transit.nearest.<location_code>.best_mode}` | string | Fastest available transit mode to reach the named location | — |
| `${transit.discovered_connections}` | integer | Total number of discoverable connections this entity has revealed | — |
| `${transit.connection.<code>.discovered}` | boolean | Has this entity discovered the named connection? | Only meaningful for connections with `discoverable=true`. Always true for non-discoverable. |

### GOAP Integration Example

```yaml
# NPC Blacksmith needs iron ore from the mines
# GOAP evaluates travel options as part of "buy_raw_materials" action

goap_action: travel_to_iron_mines
  preconditions:
    has_raw_materials: "== false"
    ${transit.nearest.iron_mines.hours}: "< 48"    # Reachable within 2 game-days
  effects:
    at_location: "iron_mines"
  cost: |
    base_travel_cost (2.0)
    + ${transit.nearest.iron_mines.hours} * time_weight (0.1)
    + ${transit.mode.${transit.nearest.iron_mines.best_mode}.preference_cost} * fear_weight (3.0)
    = total GOAP cost for this action

# If the blacksmith is afraid of horses (preference_cost = 0.7)
# and horseback is the fastest mode:
#   cost = 2.0 + 4.4 * 0.1 + 0.7 * 3.0 = 4.54
#
# If walking (preference_cost = 0.0) is the alternative:
#   cost = 2.0 + 22.0 * 0.1 + 0.0 * 3.0 = 4.20
#
# GOAP prefers walking despite taking 5x longer, because the fear cost outweighs time.
# Unless the blacksmith is DESPERATE for iron (goal urgency multiplier > fear).
```

---

## DI Enrichment Pattern (`ITransitCostModifierProvider`)

### Interface Definition

```csharp
// bannou-service/Providers/ITransitCostModifierProvider.cs

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Provides cost modifiers for transit calculations.
/// Higher-layer services (L4) implement this to enrich transit costs
/// without creating hierarchy violations.
/// </summary>
/// <remarks>
/// DISTRIBUTED SAFETY: This is a pull-based provider pattern.
/// The consumer (Transit, L2) initiates the request on whichever node
/// needs the data. The provider runs co-located and reads from distributed
/// state. Always safe in multi-node deployments.
/// </remarks>
public interface ITransitCostModifierProvider
{
    /// <summary>Provider name for logging and debugging (e.g., "weather", "disposition").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Get the cost modifier for a specific entity, mode, and connection.
    /// Returns a modifier that affects GOAP preference cost.
    /// </summary>
    Task<TransitCostModifier> GetModifierAsync(
        Guid entityId,
        string entityType,
        string modeCode,
        Guid connectionId,
        CancellationToken ct);
}

/// <summary>
/// A cost modifier returned by an ITransitCostModifierProvider.
/// </summary>
public record TransitCostModifier(
    /// <summary>Additive preference cost (0.0 = no effect, positive = aversion).</summary>
    decimal PreferenceCostDelta,

    /// <summary>Speed multiplier (1.0 = no effect, 0.5 = half speed, 1.5 = 50% faster).</summary>
    decimal SpeedMultiplier,

    /// <summary>Risk additive (0.0 = no effect, positive = more dangerous).</summary>
    decimal RiskDelta,

    /// <summary>Human-readable reason for this modifier.</summary>
    string Reason
);
```

### Expected Implementations

| Provider | Service | What It Modifies | Example |
|----------|---------|------------------|---------|
| `"proficiency"` | lib-proficiency or game-specific L4 | Speed bonus from seed-derived travel proficiency via Status | Experienced rider → `SpeedMultiplier: 1.3` (30% faster) |
| `"disposition"` | lib-disposition (L4) | Preference cost from feelings toward transit modes | `dread: 0.7` toward horseback → `PreferenceCostDelta: 0.7` |
| `"hearsay"` | lib-hearsay (L4) | Risk perception from believed dangers along connections | Believes "mountain road is cursed" → `RiskDelta: 0.4` |
| `"weather"` | lib-environment (L4) | Speed reduction from weather conditions on connections | Storm on connection → `SpeedMultiplier: 0.5` |
| `"faction"` | lib-faction (L4) | Risk from hostile territory | Enemy faction controls this road → `RiskDelta: 0.6` |
| `"ethology"` | lib-ethology (L4) | Species-level mode affinity | Centaur species → horseback `PreferenceCostDelta: -0.2` (bonus) |

### Aggregation

Transit aggregates all provider results:
- `PreferenceCostDelta`: summed (clamped to [0.0, 2.0])
- `SpeedMultiplier`: multiplied (clamped to [0.1, 3.0])
- `RiskDelta`: summed (clamped to [0.0, 1.0] total risk)
- Graceful degradation: if a provider throws, log warning and skip it

---

## Integration Points (Detailed)

### Location (L2) -- Hard Dependency

Transit connections reference Location entities by ID. On startup, Transit validates that referenced locations exist. Location deletion is handled via Resource cleanup callbacks (see Resource integration below), not event subscriptions.

Location knows nothing about Transit (correct hierarchy direction).

**Automatic location updates**: When `AutoUpdateLocationOnTransition` is enabled (default), Transit calls Location's `/location/report-entity-position` endpoint to update the entity's current location on journey departure (entity leaves origin), waypoint arrival (entity is at waypoint), and final arrival (entity is at destination). Location's entity presence system uses ephemeral Redis storage with TTL-based expiry and bidirectional lookups (`entity→location` and `location→entities`). Both services are L2 and always co-deployed on game nodes, so this uses direct API calls via lib-mesh -- not events. Neither service subscribes to the other's events for presence tracking.

**Entities in transit**: While traveling between waypoints, the entity's Location presence intentionally expires after the 30-second TTL. An entity physically between towns is genuinely not at any location node -- they are on the road. This is correct behavior: Transit reports position on each state transition (depart, advance, arrive), and the entity reappears at the next node when it arrives. Between transitions, use Transit's `${transit.journey.active}` variable and the `query-by-connection` endpoint for "who's on this road" queries -- these provide transit-specific context (mode, direction, progress, ETA) that Location's containment model cannot.

### Worldstate (L2) -- Hard Dependency

Transit uses Worldstate for:
1. **Travel time calculation**: Converts distance/speed into game-hours using `GetElapsedGameTime`
2. **Seasonal connection status**: Subscribes to `worldstate.season-changed` events to automatically open/close seasonal connections
3. **Journey ETA computation**: All timestamps are in game-time, not real-time
4. **Real-time conversion**: Route options include `totalRealMinutes` computed from current time ratio

### Actor (L2) -- Consumer

Actor's behavior execution reads `${transit.*}` variables for NPC travel decisions. The `travel_to` GOAP action uses transit data to evaluate cost, time, and feasibility of movement between locations. Transit provides the data; Actor makes the decision.

### The "Afraid of Horses" Integration

A complete worked example of how Transit connects with the behavioral stack:

```
CHARACTER HISTORY (L4):
  Backstory element: {type: "TRAUMA", code: "trampled_by_horse", intensity: 0.8}
    ↓ feeds
DISPOSITION (L4):
  Synthesizes feeling toward transit mode "horseback":
  Sources: trauma(0.8) × personality.neuroticism(0.6) = base_dread(0.48)
  Modifier: reinforced by recent near-miss → total dread = 0.72
    ↓ implements ITransitCostModifierProvider
TRANSIT (L2):
  check-availability for this character + "horseback":
    available: true (has riding skill, has horse item)
    preferenceCost: 0.72 (from disposition provider)
    effectiveSpeed: 25 km/gh (no physical limitation)
    ↓ exposes via variable provider
ACTOR (L2):
  GOAP planning evaluates "travel to iron mines":
    Option A (horseback): time_cost(4.4h) + preference_cost(0.72×3) = 4.60
    Option B (wagon):     time_cost(12h)  + preference_cost(0.0×3)  = 3.20
    Option C (walking):   time_cost(22h)  + preference_cost(0.0×3)  = 4.20

    GOAP selects: wagon (lowest total cost)

    But if iron is urgently needed (goal urgency multiplier = 2.0):
    Option A: 4.60 vs urgency bonus 8.0 → net: -3.40 (viable despite fear)
    → Reluctantly rides horse in emergency
```

### Character (L2), Species (L2), Item (L2), Inventory (L2) -- Hard Dependencies

Transit queries these services during mode compatibility checks (`check-availability`):
- **Inventory**: Checks entity has required item tag (e.g., mount item for horseback)
- **Character/Species**: Checks species compatibility for species-restricted modes
- These are synchronous L2 service calls via lib-mesh, valid under the hierarchy

### Trade (L4) -- Consumer

Trade uses Transit for:
- **Shipment travel time**: Trade routes are economic overlays on the transit connection graph
- **Route cost estimation**: `/trade/route/estimate-cost` calls `/transit/route/calculate` internally
- **Shipment journeys**: Each shipment leg creates a transit journey for the carrier entity
- **Connection status**: Trade routes are affected by connection closures (winter, war, destruction)
- **Journey archives**: Trade queries `transit/journey/query-archive` for historical velocity calculations

### Quest (L2) -- Consumer

Quest consumes transit events for travel-based objectives:
- **Departure objectives**: `transit.journey.departed` can fulfill "leave the village" milestones
- **Arrival objectives**: `transit.journey.arrived` can fulfill "reach the Iron Mines" milestones
- **Discovery objectives**: `transit.discovery.revealed` can fulfill "discover the hidden pass" milestones
- Quest uses Contract infrastructure for milestone tracking; transit events report condition fulfillment

### Collection (L2) -- Consumer

Collection consumes `transit.discovery.revealed` for route discovery unlocks:
- An "Explorer's Atlas" collection type could grant entries for discovered connections
- A "Realm Cartographer" collection tracks percentage of realm connections discovered
- Collection is a consumer only -- Transit publishes, Collection subscribes

### Character History (L4) -- Consumer

Character History consumes transit journey events for backstory generation:
- **Significant journeys**: Long-distance arrivals, multi-leg completions
- **Journey failures**: Abandoned journeys with high interruption counts
- Significance thresholds are Character History's concern, not Transit's

### Analytics (L4) -- Consumer

Analytics consumes all transit journey events for aggregation:
- Travel pattern analysis, mode popularity, connection utilization
- Interruption frequency per connection (bandit hotspots, dangerous terrain)
- Average travel times vs estimated times (route quality feedback)

### Resource (L1) -- Cleanup Integration

Transit declares `x-references` in its API schema for lib-resource cleanup coordination:
- **Location deletion (CASCADE)**: When a location is deleted, Resource calls Transit's cleanup callback. Transit closes all connections referencing the deleted location (`status: closed`, `reason: "location_deleted"`) and interrupts active journeys passing through it.
- **Character deletion (CASCADE)**: Transit's cleanup callback clears discovery data for the deleted entity from the `transit-discovery` store and abandons any active journeys for that entity.
- Transit does NOT subscribe to `location.deleted` or `character.deleted` events for cleanup -- this is handled exclusively through the Resource cleanup callback pattern per FOUNDATION TENETS (Resource-Managed Cleanup).

---

## Background Services

### Seasonal Connection Worker

Subscribes to Worldstate season-change events. When a season boundary occurs, scans all connections with `seasonalAvailability` restrictions and updates their status accordingly. Connections closing for the season transition to `seasonal_closed`; connections opening transition to `open`. Also checks for active journeys on newly-closed connections and publishes warnings (but does NOT interrupt them -- the game decides how to handle travelers caught by seasonal closure). Publishes `transit-connection.status-changed` for each connection that changes. Runs periodic checks every `SeasonalConnectionCheckIntervalSeconds`.

### Journey Archival Worker

Scans Redis for completed/abandoned journeys older than `JourneyArchiveAfterGameHours` and moves them to MySQL archive. Frees Redis memory while preserving historical travel data for analytics and NPC memory. Also enforces `JourneyArchiveRetentionDays`: deletes archived journeys older than the retention threshold from MySQL. When set to 0, archives are retained indefinitely. Runs every `JourneyArchivalWorkerIntervalSeconds` (default 300 = every 5 minutes real-time).

---

## Scale Considerations

### Connection Graph Size

For a realm with 500 locations and an average of 4 connections per location, the graph has ~2000 edges. Dijkstra's algorithm on this graph completes in microseconds. Even with 10,000 locations and 40,000 connections, route calculation remains sub-millisecond.

The connection graph is cached in Redis as a serialized adjacency list per realm, rebuilt on cache miss from MySQL. Cache invalidation occurs on connection create/delete/status-change. Cross-realm connections appear in both realms' graphs. Cross-realm route calculations merge two realm graphs -- for typical cross-realm scenarios with a handful of border connections, the merged graph is negligibly larger than a single realm's graph.

### Journey Volume

With 100,000+ concurrent NPCs, a significant fraction may be traveling at any time. If 10% are in transit, that's 10,000 active journeys in Redis. Each journey is a small JSON document (~1KB). Total Redis footprint: ~10MB -- negligible.

Journey events (departed, waypoint, arrived) flow through the standard lib-messaging pipeline. At 10,000 journeys with an average of 3 events per journey per game-day, that's ~30,000 events per game-day, or roughly 1 event every 3 seconds at 24:1 time ratio. Well within RabbitMQ capacity.

### Mode Compatibility Checks

`check-availability` calls may need to query lib-inventory (for item requirements) and lib-character (for species). These are L2 service calls that go through lib-mesh. To avoid per-NPC overhead, the variable provider uses lazy cache invalidation with a configurable TTL (via `DiscoveryCacheTtlSeconds`). Mode availability is cached per-entity and expires after TTL, at which point the next access triggers a fresh query. This means changes to an entity's inventory or species won't be reflected until the cache expires -- an acceptable tradeoff given worlds are large and transit decisions are not frame-rate-sensitive. If eager invalidation is needed later (e.g., equipment changes that immediately affect available modes), event-based cache busting can be added as a targeted optimization.

---

## Resolved Design Decisions

1. **Connection granularity**: Allow arbitrary connections but tag them appropriately (`tags: ["portal"]`, `tags: ["trade_route"]`). Enables teleportation portals and long-distance shipping routes alongside physically meaningful roads.

2. **Journey auto-progression**: No. This violates the declarative lifecycle pattern. The game/Actor drives progression. Transit records and calculates but doesn't simulate.

3. **Intra-location transit**: No. Intra-location movement is Mapping's domain (spatial positioning) or the game client's domain (local navigation). Transit handles inter-location movement only.

4. **Multi-mode journeys**: Yes, core feature. Each leg specifies a `modeCode` that can override the journey's `primaryModeCode`. `preferMultiModal` flag on create/calculate selects best mode per leg automatically.

5. **Reverse connections**: Bidirectional connections share properties. For asymmetric connections (upstream/downstream), create two one-way connections with different speeds. Keeps the data model simple.

6. **Transit and Mapping relationship**: Complementary, not overlapping. Mapping is continuous spatial data ("where exactly am I"), Transit is discrete connectivity ("how do I get from town A to town B"). No integration needed beyond both referencing Location entities.

7. **Cross-realm connections**: Fully supported at the platform level. Connections store `fromRealmId` and `toRealmId` (derived from their endpoint locations), with a convenience `crossRealm` flag. Route calculation merges per-realm graphs when origin and destination are in different realms. Games control cross-realm travel by policy: Arcadia's realms are intentionally isolated (no cross-realm connections are ever created), but other games on the platform may freely connect locations across realms. The platform provides the primitive; game design decides whether to use it.
