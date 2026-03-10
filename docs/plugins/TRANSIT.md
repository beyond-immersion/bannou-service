# Transit Plugin Deep Dive

> **Plugin**: lib-transit
> **Schema**: schemas/transit-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: transit-modes (MySQL), transit-connections (MySQL), transit-journeys (Redis), transit-journeys-archive (MySQL), transit-connection-graph (Redis), transit-discovery (MySQL), transit-discovery-cache (Redis), transit-lock (Redis)
> **Implementation Map**: [docs/maps/TRANSIT.md](../maps/TRANSIT.md)
> **Short**: Geographic connectivity graph, transit modes, and game-time journey tracking

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
LOCATION SERVICE (L2) TRANSIT SERVICE (L2)
Containment Hierarchy Connectivity Graph

Realm: Arcadia Connection: Eldoria ←→ Iron Mines
 └─ Region: Heartlands distance: 120 km
 ├─ City: Eldoria terrain: mountain_road
 │ ├─ District: Market modes: [walking, horseback, wagon]
 │ └─ District: Docks seasonal: {winter: closed}
 ├─ Village: Riverside
 │ └─ Building: Inn Connection: Eldoria ←→ Riverside
 └─ Landmark: Iron Mines distance: 30 km
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
│ TRANSIT MODES (Registry) │
│ "walking": speed 5, capacity 1, terrain [any] │
│ "horseback": speed 25, capacity 2, terrain [road,trail,plain] │
│ terrain_mods: {road: 1.2, trail: 0.8, forest: 0.5, mountain: 0.3}│
│ "wagon": speed 10, capacity 500, terrain [road], requires [item] │
│ "river_boat": speed 15, capacity 100, terrain [river] │
│ "ocean_vessel": speed 20, capacity 2000, terrain [ocean] │
│ valid_entities: [caravan, army], cargo_penalty: 0.1 │
│ "flying_mount": speed 40, capacity 2, terrain [any] │
└─────────────────────┬───────────────────────────────────────────┘
 │ defines capabilities
 ▼
┌─────────────────────────────────────────────────────────────────┐
│ CONNECTIONS (Graph Edges) │
│ Eldoria → Iron Mines: 120km, mountain_road, [walk,horse,wagon] │
│ Eldoria → Riverside: 30km, river_path, [walk,horse,river_boat] │
│ Riverside → Iron Mines: 80km, forest_trail, [walk,horse] │
│ Docks → Harbor Island: 15km, ocean, [ocean_vessel] │
└─────────────────────┬───────────────────────────────────────────┘
 │ traversed by
 ▼
┌─────────────────────────────────────────────────────────────────┐
│ JOURNEYS (Active Transits) │
│ Journey #1: Merchant Galen, horseback │
│ Eldoria → Riverside → Iron Mines │
│ Departed: Game-Day 142, Hour 8 │
│ Current: Riverside (waypoint reached) │
│ ETA Iron Mines: Game-Day 143, Hour 4 │
│ Status: in_transit │
└─────────────────────────────────────────────────────────────────┘
```

### Route Calculation

Route calculation is a **pure computation endpoint** -- no state mutation. Given a source location, destination location, preferred mode (or "best available"), and current worldstate, Transit performs a graph search over connections filtered by mode compatibility and seasonal availability, returning ranked route options.

```
/transit/route/calculate
 Input: Eldoria → Iron Mines, mode: any, worldstate: winter

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

Interruption record within a journey. Fields: `legIndex`, `gameTime`, `reason` (e.g. "bandit_attack", "storm", "breakdown", "encounter"), `durationGameHours` (min 0 — 0 if resolved immediately, e.g. instantly repelled attack), `resolved` (boolean — NOT filler: an interruption can be resolved with 0 duration, so this flag is stored entity state distinguishing active interruptions from resolved ones, not derivable from `durationGameHours`).

### TransitRouteOption (Not Persisted)

Returned by `/transit/route/calculate`. Key fields: `waypoints` (ordered location IDs), `connections` (ordered connection IDs), `legCount`, `primaryModeCode`, `legModes` (per-leg mode codes for multi-modal routes), `totalDistanceKm`, `totalGameHours` (includes `waypointTransferTimeGameHours` for each leg), `totalRealMinutes` (approximate — time ratios can change during travel), `averageRisk`/`maxLegRisk`, `allLegsOpen`, `seasonalWarnings` (nullable `SeasonalRouteWarning` array — structured warnings for legs with upcoming seasonal closures, typed sub-model so consumers can render specific UI or make programmatic decisions), `rank` (1 = best option by sort criteria).

### SeasonalRouteWarning

Warning sub-model: `connectionId`, `connectionName` (nullable), `legIndex`, `currentSeason`, `closingSeason`, `closingSeasonIndex` (how many season transitions until closure, 1 = next season).

### TerrainSpeedModifier

Sub-model: `terrainType` (Category B content code), `multiplier` (min 0.01 — speed multiplier applied to base speed, 1.0 = no change).

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
| `maximumEntitySizeCategory` (mode requirements) | B (Content Code) | Opaque string | Entity size categories (e.g., "small", "medium", "large") are game-defined at deployment time per IMPLEMENTATION TENETS Test 1. False positive — not a system enum |
| `resolved` (TransitInterruption) | — (Stored State) | boolean | NOT filler. An interruption can be resolved with 0 durationGameHours (immediately repelled). The `resolved` flag is stored entity state distinguishing active interruptions from resolved ones, not derivable from other fields |
| mode `isDeprecated` / `deprecatedAt` / `deprecationReason` | C (System State) | Standard triple-field deprecation model (`boolean`, `timestamp?`, `string?`) | Category A per IMPLEMENTATION TENETS: connections store `compatibleModes` and journeys store `primaryModeCode`/`legModes` referencing mode codes → deprecate/undeprecate/delete required |

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
| `MinPreferenceCost` | decimal | 0.0 | `TRANSIT_MIN_PREFERENCE_COST` | Floor for aggregated DI cost modifier preference cost (min 0) |
| `MaxPreferenceCost` | decimal | 2.0 | `TRANSIT_MAX_PREFERENCE_COST` | Ceiling for aggregated DI cost modifier preference cost (min 0) |
| `MinSpeedMultiplier` | decimal | 0.1 | `TRANSIT_MIN_SPEED_MULTIPLIER` | Floor for aggregated DI cost modifier speed multiplier (min 0.01) |
| `MaxSpeedMultiplier` | decimal | 3.0 | `TRANSIT_MAX_SPEED_MULTIPLIER` | Ceiling for aggregated DI cost modifier speed multiplier (min 0.01) |
| `LockTimeoutSeconds` | int | 10 | `TRANSIT_LOCK_TIMEOUT_SECONDS` | Distributed lock acquisition timeout for mode/connection/journey mutations (1-60) |
| `MinimumEffectiveSpeedKmPerGameHour` | decimal | 0.01 | `TRANSIT_MINIMUM_EFFECTIVE_SPEED_KM_PER_GAME_HOUR` | Speed floor after terrain/cargo modifiers, prevents division-by-zero (min 0.001) |
| `SeasonalWorkerStartupDelaySeconds` | int | 5 | `TRANSIT_SEASONAL_WORKER_STARTUP_DELAY_SECONDS` | Delay before seasonal connection worker starts first check (0-300) |
| `JourneyArchivalWorkerStartupDelaySeconds` | int | 10 | `TRANSIT_JOURNEY_ARCHIVAL_WORKER_STARTUP_DELAY_SECONDS` | Delay before journey archival worker starts first sweep (0-300) |
| `DefaultFallbackModeCode` | string | "walking" | `TRANSIT_DEFAULT_FALLBACK_MODE_CODE` | Fallback mode code when no compatible mode found during route calculation |

**Cargo speed penalty formula**: `speed_reduction = (cargo - CargoSpeedPenaltyThresholdKg) / (mode.cargoCapacityKg - threshold) × rate` where `rate` = mode's `cargoSpeedPenaltyRate` ?? `DefaultCargoSpeedPenaltyRate`. Max penalty = `rate` (e.g., 0.3 = 30% speed reduction at full capacity).

---

## Visual Aid

### Journey State Machine

```
 ┌─────────────┐
 │ preparing │
 └──────┬──────┘
 │ /journey/depart
 ▼
 ┌──────────┐ ┌─────────────┐ ┌─────────────┐
 │abandoned │◄───│ in_transit │───►│at_waypoint │
 └──────────┘ └──────┬──────┘ └──────┬──────┘
 ▲ /abandon│ │ /interrupt │ /advance
 │ │ ▼ │ (next leg)
 │ ┌──────────────┐ │
 ├──────────│ interrupted │ │
 │ └──────┬───────┘ │
 │ │ /resume │
 │ └──►in_transit │
 │ │
 │ /arrive (force) │ /advance
 │ ┌─────────────┐ │ (final leg)
 └────│ arrived │◄──────────────────┘
 └─────────────┘

Status transitions:
 preparing → in_transit (depart) | abandoned (abandon)
 in_transit → at_waypoint (advance) | arrived (advance final/arrive)
 | interrupted (interrupt) | abandoned (abandon)
 at_waypoint → in_transit (advance next leg) | arrived (arrive)
 | abandoned (abandon)
 interrupted → in_transit (resume) | abandoned (abandon)
```

Note: All transitions publish corresponding journey events. The `arrive` endpoint
force-completes a journey (marking remaining legs as "skipped"), used for
teleportation, fast-travel, or narrative skip.

---

## Stubs & Unimplemented Features

No stubs remain. All 33 endpoints are fully implemented with no `NotImplemented` returns or TODO markers. Background workers (Seasonal Connection Worker, Journey Archival Worker) are implemented. The `ITransitCostModifierProvider` interface exists in `bannou-service/Providers/`. Variable provider factory (`TransitVariableProviderFactory`) is implemented.

### Client Events

Three client events are defined in `schemas/transit-client-events.yaml` (see [implementation map](../maps/TRANSIT.md) for full event schemas):

| Client Event | Entity Mapping | Trigger |
|-------------|----------------|---------|
| `TransitJourneyUpdatedClientEvent` | `("character", characterId)` via Gardener registration | Journey depart, waypoint, arrive, interrupt, resume, abandon — single event with `JourneyStatus` discriminator |
| `TransitDiscoveryRevealedClientEvent` | `("character", characterId)` via Gardener registration | Connection discovered by entity — personal notification only |
| `TransitConnectionStatusChangedClientEvent` | `("realm", realmId)` via Agency registration | Road closures, blockades, seasonal changes — broadcast to all players in realm |

---

## Potential Extensions

1. **Caravan formations (Phase 2)**: Multiple entities traveling together as a group. Group speed = slowest member. Group risk = reduced (safety in numbers). Caravan as an entity type for journeys, with member tracking and formation bonuses. Faction merchant guilds could manage NPC caravans. **Promoted to Phase 2** -- the data model already accommodates this via `entityType: "caravan"` and `partySize`, but Phase 2 needs: a `partyMembers: [uuid]` field on `TransitJourney` for tracking individual members, batch departure/advance for all party members, and a party leader concept for GOAP decisions. The `validEntityTypes` restriction on modes already supports caravan-only modes (e.g., `wagon` with `validEntityTypes: ["caravan", "army"]`).
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/524 -->

2. **Fatigue and rest**: Long journeys accumulate fatigue based on per-mode `fatigueRatePerGameHour`. Transit passively tracks accumulated fatigue on journeys and publishes threshold events — it does NOT auto-pause or add a "resting" journey status (per Resolved Decision #2: no autonomous progression). The Actor/L4 layer decides when and where to rest. Fatigue thresholds come from an L4 DI provider (`IFatigueThresholdProvider`), following the established `ITransitCostModifierProvider` pattern — different entities have different stamina. Rest duration and behavior are entirely outside Transit's scope.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/527 -->

3. **Mount bonding**: Integration with Relationship (L2) for character-mount relationships. A well-bonded mount has higher effective speed and lower fatigue. Bond strength grows with travel distance. Follows the existing Relationship entity-to-entity model.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/533 -->

4. ~~**Transit fares**~~: **Resolved** -- see Resolved Design Decision #9. Transit remains a pure movement primitive; fares are owned by Trade (L4).
<!-- AUDIT:RESOLVED:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/535 -->


---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently known.

### Intentional Quirks

1. **Double-to-decimal casting throughout service**: Generated configuration properties are `double` (from OpenAPI `number` format), but all internal models and calculations use `decimal` for precision. This requires `(decimal)_configuration.PropertyName` casts at 11 call sites across 4 files (TransitService.cs, JourneyArchivalWorker.cs, TransitRouteCalculator.cs, TransitVariableProviderFactory.cs). The pattern is consistent and correct — `decimal` prevents floating-point rounding in distance/speed calculations.

2. **Location presence TTL gap during transit**: When an entity departs on a journey, their location presence is updated to the departure location. The entity doesn't "exist" at intermediate locations during transit — they arrive at the destination when `journey/advance` is called. This means entities are effectively invisible to location-based queries while in transit. This is by design: Transit is a discrete graph system, not a continuous simulation.

3. **Journey archival is lazy, not event-driven**: Completed journeys are moved from Redis to MySQL by the `JourneyArchivalWorker` on a periodic timer (`JourneyArchivalWorkerIntervalSeconds`, default 300s). This means recently completed journeys remain in Redis for up to 5 minutes before archival. Queries for historical journeys should check both active (`journey/get`) and archived (`journey/query-archive`) stores.

4. **Discovery is per-entity, not per-account**: Connection discovery (`transit-discovery` store) is tracked per entity, meaning each character on an account discovers connections independently. There is no account-level discovery sharing. This is consistent with Transit being a game-primitive — account-level features would be an L4 concern.

5. **Mid-journey seasonal closure**: When a season changes and a connection closes for the new season, the Seasonal Connection Worker publishes warnings but does NOT interrupt travelers already in transit on that connection. The game/Actor layer decides how to handle entities caught by seasonal closure (e.g., the actor might choose to press on, turn back, or wait). This is consistent with the declarative lifecycle pattern — Transit does not autonomously alter journey state.

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
 ${transit.nearest.iron_mines.hours}: "< 48" # Reachable within 2 game-days
 effects:
 at_location: "iron_mines"
 cost: |
 base_travel_cost (2.0)
 + ${transit.nearest.iron_mines.hours} * time_weight (0.1)
 + ${transit.mode.${transit.nearest.iron_mines.best_mode}.preference_cost} * fear_weight (3.0)
 = total GOAP cost for this action

# If the blacksmith is afraid of horses (preference_cost = 0.7)
# and horseback is the fastest mode:
# cost = 2.0 + 4.4 * 0.1 + 0.7 * 3.0 = 4.54
#
# If walking (preference_cost = 0.0) is the alternative:
# cost = 2.0 + 22.0 * 0.1 + 0.0 * 3.0 = 4.20
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
 Option B (wagon): time_cost(12h) + preference_cost(0.0×3) = 3.20
 Option C (walking): time_cost(22h) + preference_cost(0.0×3) = 4.20

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

Subscribes to Worldstate season-change events. When a season boundary occurs, scans all connections with `seasonalAvailability` restrictions and updates their status accordingly. Connections closing for the season transition to `seasonal_closed`; connections opening transition to `open`. Also checks for active journeys on newly-closed connections and publishes warnings (but does NOT interrupt them -- the game decides how to handle travelers caught by seasonal closure). Publishes `transit.connection.status-changed` for each connection that changes. Runs periodic checks every `SeasonalConnectionCheckIntervalSeconds`.

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

8. **One active journey per entity**: An entity may have at most one active journey (status `preparing`, `in_transit`, `at_waypoint`, or `interrupted`) at any time. Creating a new journey while one is active returns `Conflict`. Games needing parallel movement (e.g., a character riding a ship while the ship follows its own route) model this via separate entity types — the ship has its own journey, the character is a passenger (tracked via `partySize` or the ship's own entity record). This keeps the journey state machine simple and prevents ambiguous "current location" semantics.

9. **Transit fares**: No. Transit is a pure movement primitive. Monetary costs for transit modes and connections (tolls, fares, carriage fees, teleportation costs) are owned by Trade (L4), which wraps transit journeys with economic logic. Trade's deep dive already models `totalTransitCost` in `ShipmentFinancialSummary` and treats transit cost as one of four economic factors. "NPCs can't cheat tolls" is enforced behaviorally — guard NPCs at waypoints interrupt journeys via the existing interruption mechanism, without Transit needing to know about currency. Keeping Transit decoupled from Currency avoids cross-service atomicity complexity (Currency debit + journey save = two-phase commit) for no meaningful benefit. See [#535](https://github.com/beyond-immersion/bannou-service/issues/535).

---

## Work Tracking

### Completed

(No completed items — processed entries cleared during maintenance 2026-03-05.)
