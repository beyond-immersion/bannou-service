# Transit Plugin Deep Dive

> **Plugin**: lib-transit (not yet created)
> **Schema**: `schemas/transit-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: transit-modes (MySQL), transit-connections (MySQL), transit-journeys (Redis), transit-journeys-archive (MySQL), transit-connection-graph (Redis), transit-discovery (MySQL), transit-discovery-cache (Redis) — all planned
> **Layer**: GameFoundation
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

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

### Transit Mode

```yaml
TransitMode:
  code: string                      # Unique identifier ("walking", "horseback", "wagon")

  # Display
  name: string                      # Human-readable ("Horseback", "River Boat")
  description: string               # Detailed description

  # Movement properties
  baseSpeedKmPerGameHour: decimal   # Base speed in game-kilometers per game-hour (used when no
                                    # terrain-specific speed is defined for the current terrain)
  terrainSpeedModifiers:            # Optional per-terrain speed multipliers. Typed array of
    [TerrainSpeedModifier]          # {terrainType: string, multiplier: decimal} pairs.
                                    # Example: [{terrainType: "road", multiplier: 1.2},
                                    #           {terrainType: "trail", multiplier: 0.8},
                                    #           {terrainType: "forest", multiplier: 0.5}]
                                    # null = base speed applies uniformly across all compatible terrain.
                                    # effectiveSpeed = baseSpeed × multiplier for matching terrainType.
                                    # Terrain type not in the list → defaults to 1.0 (base speed).
  passengerCapacity: integer        # How many entities can ride (1 for horse, 20 for ship)
  cargoCapacityKg: decimal          # Weight capacity in game-kg (0 for walking, 500 for wagon)
  cargoSpeedPenaltyRate: decimal    # Optional per-mode cargo speed penalty rate. When set, overrides
                                    # the plugin-level DefaultCargoSpeedPenaltyRate. null = use plugin
                                    # config default. 0.0 = no cargo penalty (e.g., pack mule, dedicated
                                    # cargo vessel). See CargoSpeedPenaltyRate in Configuration.

  # Terrain compatibility
  compatibleTerrainTypes: [string]  # Which terrain types this mode traverses
                                    # Empty = all terrain (e.g., walking, flying)

  # Entity type restrictions
  validEntityTypes: [string]        # Optional. When set, only these entity types may use this mode.
                                    # Examples: ["character", "npc"] for horseback (caravans can't ride),
                                    # ["caravan", "army"] for wagon (individuals walk).
                                    # null = no entity type restriction (caller's responsibility to
                                    # enforce any game-specific rules for non-character entity types).
                                    # Note: characters are NOT exempt -- a mode for armies/caravans
                                    # may validly exclude characters.

  # Requirements (all must be met to use this mode -- applies to character entities only)
  requirements: TransitModeRequirements   # Item, species, and physical requirements only.
                                          # Proficiency-based speed bonuses are NOT a mode requirement --
                                          # they flow through ITransitCostModifierProvider from L4 services
                                          # that query Status for seed-derived capabilities.

  # Behavioral properties
  fatigueRatePerGameHour: decimal   # Stamina cost per game-hour of travel (0 = no fatigue)
  noiseLevelNormalized: decimal     # 0.0 (silent) to 1.0 (loud) -- affects detection risk

  # Scope
  realmRestrictions: [uuid]         # Empty = available in all realms. If set, only these realms.

  # Status
  status: enum                      # active | deprecated

  # Metadata
  tags: [string]                    # Freeform tags ("mount", "vehicle", "magical", "aquatic")

  createdAt: timestamp
  modifiedAt: timestamp
```

### Transit Mode Requirements

```yaml
TransitModeRequirements:
  # Item requirement (e.g., horseback requires a mount item)
  requiredItemTag: string           # null = no item needed. "mount_horse" = need item with this tag

  # Species restriction
  allowedSpeciesCodes: [string]     # Empty = any species. If set, only these species can use mode
  excludedSpeciesCodes: [string]    # These species CANNOT use this mode

  # Physical requirements
  minimumPartySize: integer         # Some modes need crew (ocean_vessel: 3)
  maximumEntitySizeCategory: string # "small", "medium", "large" -- centaurs can't ride horses
```

### Transit Connection

```yaml
TransitConnection:
  id: uuid

  # Endpoints
  fromLocationId: uuid              # Location service entity
  toLocationId: uuid                # Location service entity

  # Directionality
  bidirectional: boolean            # true = traversable both ways. false = one-way only

  # Geography
  distanceKm: decimal               # Distance in game-kilometers
  terrainType: string               # "road", "trail", "forest", "mountain", "river",
                                    # "ocean", "swamp", "desert", "underground", "aerial"

  # Mode compatibility
  compatibleModes: [string]         # Which transit mode codes can use this connection
                                    # Empty = walking only

  # Seasonal availability
  seasonalAvailability:             # Typed array of {season: string, available: boolean} pairs.
    [SeasonalAvailabilityEntry]     # Season strings MUST match the realm's Worldstate season codes.
                                    # Worldstate uses opaque calendar templates -- season names are
                                    # configurable per realm (a desert realm may have "wet"/"dry",
                                    # not "winter"/"spring"). The Seasonal Connection Worker queries
                                    # Worldstate for the realm's current season name and matches
                                    # against these entries.
                                    # Example: [{season: "winter", available: false},
                                    #           {season: "spring", available: true}]
                                    # null = always available

  # Risk
  baseRiskLevel: decimal            # 0.0 (safe) to 1.0 (extremely dangerous)
  riskDescription: string           # "Bandit territory", "Avalanche prone", etc.

  # Status (see "Status Transitions with Optimistic Concurrency" in Design Philosophy)
  status: enum                      # open | closed | dangerous | blocked | seasonal_closed
  statusReason: string              # Why the connection is in this status
  statusChangedAt: timestamp

  # Discovery
  discoverable: boolean             # false (default) = common knowledge, visible to all route queries.
                                    # true = only visible in route queries for entities that have
                                    # discovered this connection (via travel, explicit grant, or hearsay).
                                    # Hidden passages, smuggler routes, ancient paths, shortcuts.

  # Metadata
  name: string                      # Optional human name ("The King's Road", "Serpent River")
  code: string                      # Optional code for lookup ("kings_road")
  tags: [string]                    # Freeform ("trade_route", "military", "smuggler_path")

  # Realms (derived from endpoint locations, stored for query efficiency)
  fromRealmId: uuid                 # Realm of fromLocationId (derived, not caller-specified)
  toRealmId: uuid                   # Realm of toLocationId (derived, not caller-specified)
  crossRealm: boolean              # Derived: fromRealmId != toRealmId. Convenience flag for queries.
                                    # Games that don't want cross-realm travel (e.g., Arcadia) simply
                                    # never create cross-realm connections. The platform supports it;
                                    # usage is game-specific.

  createdAt: timestamp
  modifiedAt: timestamp
```

### Transit Journey

```yaml
TransitJourney:
  id: uuid

  # Traveler (the entity that physically moves -- not a business abstraction like "shipment")
  entityId: uuid                    # Who is traveling
  entityType: string                # "character", "npc", "caravan", "army", "creature"

  # Route
  legs: [TransitJourneyLeg]         # Ordered list of connections to traverse
  currentLegIndex: integer          # Which leg the entity is currently on (0-based)

  # Mode (primary -- individual legs can override for multi-modal journeys)
  primaryModeCode: string           # Primary transit mode for this journey
  effectiveSpeedKmPerGameHour: decimal  # Current effective speed (may differ from base due to modifiers)

  # Timing (game-time via Worldstate)
  plannedDepartureGameTime: decimal # Game-time timestamp of planned departure
  actualDepartureGameTime: decimal  # Game-time timestamp of actual departure (null if not departed)
  estimatedArrivalGameTime: decimal # Computed ETA based on route + speed + worldstate
  actualArrivalGameTime: decimal    # Game-time timestamp of actual arrival (null if not arrived)

  # Locations
  originLocationId: uuid            # Starting location
  destinationLocationId: uuid       # Final destination
  currentLocationId: uuid           # Where the entity currently is (last known position)

  # Status
  status: enum                      # preparing | in_transit | at_waypoint | arrived
                                    # interrupted | abandoned
  statusReason: string              # Reason for interruption/abandonment

  # Interruption tracking
  interruptions: [TransitInterruption]

  # Metadata
  partySize: integer                # How many entities are traveling together
  cargoWeightKg: decimal            # Total cargo weight (affects speed on some modes)

  createdAt: timestamp
  modifiedAt: timestamp

TransitJourneyLeg:
  connectionId: uuid                # Which transit connection this leg follows
  fromLocationId: uuid
  toLocationId: uuid
  modeCode: string                  # Transit mode for THIS leg. Overrides journey's primaryModeCode
                                    # when the connection supports a different mode than the primary.
                                    # Enables multi-modal journeys: ride horse to river, boat downstream,
                                    # walk to the cave. Each leg uses the mode appropriate to its terrain.
  distanceKm: decimal               # Copied from connection for journey record
  terrainType: string               # Copied from connection
  estimatedDurationGameHours: decimal
  waypointTransferTimeGameHours: decimal  # Optional. Delay at the waypoint BEFORE starting this leg.
                                    # Models intra-location transfer time: disembarking a river boat,
                                    # waiting at a dock, crossing a city to reach the next departure
                                    # point, changing mounts at a stable. Analogous to elevator wait
                                    # time -- you arrived at the location but need time before
                                    # continuing on the next connection.
                                    # null or 0.0 = no transfer delay (immediate continuation).
                                    # Factored into journey ETA and totalGameHours calculations.
                                    # Set by the game on journey creation or dynamically via advance.
  status: enum                      # pending | in_progress | completed | skipped
  completedAtGameTime: decimal      # When this leg was completed (null if not)

TransitInterruption:
  legIndex: integer                 # Which leg was interrupted
  gameTime: decimal                 # When the interruption occurred
  reason: string                    # "bandit_attack", "storm", "breakdown", "encounter"
  durationGameHours: decimal        # How long the interruption lasted (0 if unresolved)
  resolved: boolean                 # Whether travel resumed

TerrainSpeedModifier:
  terrainType: string               # Terrain type code (e.g., "road", "trail", "forest")
  multiplier: decimal               # Speed multiplier applied to base speed (1.0 = no change)

SeasonalAvailabilityEntry:
  season: string                    # Season code matching the realm's Worldstate calendar template
  available: boolean                # Whether the connection is open during this season
```

### Transit Route Option

```yaml
# Returned by /transit/route/calculate -- not persisted
TransitRouteOption:
  # Path
  waypoints: [uuid]                 # Ordered location IDs from source to destination
  connections: [uuid]               # Ordered connection IDs
  legCount: integer                 # Number of legs

  # Mode (per-leg for multi-modal routes)
  primaryModeCode: string           # Dominant transit mode for this option
  legModes: [string]                # Per-leg mode codes (may differ from primary on multi-modal routes)

  # Distance and time
  totalDistanceKm: decimal          # Sum of all leg distances
  totalGameHours: decimal           # Estimated total travel time in game-hours (includes
                                    # waypointTransferTimeGameHours for each leg when set)
  totalRealMinutes: decimal         # Converted to real-time at current time ratio (approximate --
                                    # time ratios can change during travel)

  # Risk
  averageRisk: decimal              # Weighted average risk across legs
  maxLegRisk: decimal               # Highest risk on any single leg

  # Seasonal status
  allLegsOpen: boolean              # Are all legs currently open?
  seasonalWarnings: [string]        # Any legs that may close soon

  # Comparison helpers
  rank: integer                     # 1 = best option by sort criteria
  sortCriteria: string              # What criteria was used to rank ("fastest", "safest", "shortest")
```

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

## State Storage

```yaml
# schemas/state-stores.yaml additions

transit-modes:
  backend: mysql
  description: "Transit mode definitions (durable registry)"
  key_format: "transit:mode:{code}"

transit-connections:
  backend: mysql
  description: "Transit connections between locations (durable graph edges)"
  key_format: "transit:conn:{connectionId}"
  indexes:
    - fromLocationId
    - toLocationId
    - fromRealmId
    - toRealmId
    - crossRealm
    - status

transit-journeys:
  backend: redis
  description: "Active transit journeys (hot state with TTL)"
  key_format: "transit:journey:{journeyId}"
  ttl_source: config.JourneyArchiveAfterGameHours
  notes: |
    Active and recently-completed journeys live in Redis for fast access.
    Background worker archives completed/abandoned journeys to MySQL after TTL.

transit-journeys-archive:
  backend: mysql
  description: "Archived transit journeys (historical record)"
  key_format: "transit:journey:archive:{journeyId}"
  indexes:
    - entityId
    - entityType
    - originRealmId
    - destinationRealmId
    - status
    - originLocationId
    - destinationLocationId

transit-connection-graph:
  backend: redis
  description: "Cached connection graph for route calculation (per-realm)"
  key_format: "transit:graph:{realmId}"
  ttl_source: config.ConnectionGraphCacheSeconds
  notes: |
    Serialized adjacency list for Dijkstra's algorithm. One graph per realm.
    Each realm's graph includes ALL connections touching that realm -- both
    intra-realm connections and cross-realm connections where fromRealmId or
    toRealmId matches this realm. Cross-realm connections therefore appear in
    both realms' cached graphs.

    Rebuilt from MySQL on cache miss.
    Invalidated when connections are created/deleted/status-changed.

    Cross-realm route calculation merges the graphs for the origin and
    destination realms. Since cross-realm connections appear in both graphs,
    the merge naturally discovers realm-spanning paths without a separate
    cross-realm index.

transit-discovery:
  backend: mysql
  description: "Per-entity connection discovery tracking (durable)"
  key_format: "transit:discovery:{entityId}:{connectionId}"
  indexes:
    - entityId
    - connectionId
  notes: |
    MySQL-backed durable store of which entities have discovered which
    connections. Each record is an (entityId, connectionId, source, discoveredAt)
    tuple. Durability is critical: discovery represents permanent world knowledge
    that must survive Redis restarts and redeployments.
    Cleaned up via Resource CASCADE when the entity is deleted.

transit-discovery-cache:
  backend: redis
  description: "Per-entity discovery cache for fast route calculation lookups"
  key_format: "transit:discovery:cache:{entityId}"
  ttl_source: config.DiscoveryCacheTtlSeconds
  notes: |
    Redis set per entity containing connection IDs they have discovered.
    Populated on cache miss from MySQL (transit-discovery store).
    Checked during route calculation when entityId is provided to filter
    discoverable connections. Invalidated on new discovery reveal.
    This is a read-through cache -- MySQL is the source of truth.
```

---

## Events

### Published Events

**Connection events** (published on status changes):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.connection.status-changed` | `TransitConnectionStatusChangedEvent` | A connection's operational status changed. Includes connectionId, fromLocationId, toLocationId, previousStatus (`ConnectionStatus` enum), newStatus (`ConnectionStatus` enum), reason, forceUpdated flag, realm IDs, crossRealm flag. |

**Journey lifecycle events** (published on journey state transitions):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.journey.departed` | `TransitJourneyDepartedEvent` | An entity has begun traveling. Includes journeyId, entityId, entityType, origin/destination locationIds, primaryModeCode, estimatedArrivalGameTime, partySize, realm IDs, crossRealm flag. |
| `transit.journey.waypoint-reached` | `TransitJourneyWaypointReachedEvent` | A traveling entity reached an intermediate waypoint. Includes journeyId, entityId, entityType, waypointLocationId, nextLocationId, legIndex, remainingLegs, connectionId, realmId, crossedRealmBoundary flag. |
| `transit.journey.arrived` | `TransitJourneyArrivedEvent` | A traveling entity reached its destination. Includes journeyId, entityId, entityType, origin/destination locationIds, primaryModeCode, totalGameHours, totalDistanceKm, interruptionCount, legsCompleted, realm IDs, crossRealm flag. |
| `transit.journey.interrupted` | `TransitJourneyInterruptedEvent` | A journey was interrupted (combat, event, breakdown). Includes journeyId, entityId, entityType, currentLocationId, currentLegIndex, reason, realmId. |
| `transit.journey.resumed` | `TransitJourneyResumedEvent` | An interrupted journey was resumed. Includes journeyId, entityId, entityType, currentLocationId, destinationLocationId, currentLegIndex, remainingLegs, modeCode, realmId. |
| `transit.journey.abandoned` | `TransitJourneyAbandonedEvent` | A journey was abandoned before reaching destination. Includes journeyId, entityId, entityType, origin/destination locationIds, abandonedAtLocationId, reason, completedLegs, totalLegs, realm IDs, crossRealm flag. |

**Discovery events**:

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.discovery.revealed` | `TransitDiscoveryRevealedEvent` | A discoverable connection was revealed to an entity. Includes entityId, connectionId, fromLocationId, toLocationId, source (how discovered), realm IDs, crossRealm flag. |

**Mode status events** (custom -- modes use register/deprecate semantics, not standard CRUD lifecycle):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.mode.registered` | `TransitModeRegisteredEvent` | A new transit mode was registered. Includes code, name. |
| `transit.mode.deprecated` | `TransitModeDeprecatedEvent` | A transit mode was deprecated. Includes code, reason. |

**Connection lifecycle events** (auto-generated via `x-lifecycle` in events schema):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.connection.created` | `TransitConnectionCreatedEvent` | A new connection was created between locations. Full entity data. |
| `transit.connection.updated` | `TransitConnectionUpdatedEvent` | A connection's properties were updated. Full entity data + changedFields. |
| `transit.connection.deleted` | `TransitConnectionDeletedEvent` | A connection was removed. Full entity data + deletedReason. |

All events include standard `eventId` (uuid) and `timestamp` (date-time) fields per FOUNDATION TENETS event schema pattern. `x-event-publications` in `transit-events.yaml` must list all published events as the authoritative registry.

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.season-changed` | `HandleSeasonChangedAsync` (via `IEventConsumer`) | Triggers the Seasonal Connection Worker to scan connections with `seasonalAvailability` restrictions and update their status for the new season. Connections closing for the new season transition to `seasonal_closed`; connections opening transition to `open`. |

Transit does not subscribe to `location.deleted` or `character.deleted` events for cleanup — this is handled exclusively through lib-resource cleanup callbacks per FOUNDATION TENETS.

### Resource Cleanup (FOUNDATION TENETS)

Transit registers as an `ISeededResourceProvider` with lib-resource for cleanup coordination. These are declared via `x-references` in the API schema.

| Target Resource | Source Type | On Delete | Cleanup Action |
|----------------|-------------|-----------|----------------|
| location | transit | CASCADE | Close all connections referencing the deleted location, interrupt active journeys passing through it |
| character | transit | CASCADE | Clear discovery data for the deleted entity, abandon any active journeys |

---

## Configuration

```yaml
# schemas/transit-configuration.yaml
TransitServiceConfiguration:
  properties:
    DefaultWalkingSpeedKmPerGameHour:
      type: number
      default: 5.0
      description: "Default walking speed used when no mode is specified"
      env: TRANSIT_DEFAULT_WALKING_SPEED_KM_PER_GAME_HOUR

    MaxRouteCalculationLegs:
      type: integer
      default: 8
      description: "Maximum number of legs to consider in route calculation (prevents combinatorial explosion)"
      env: TRANSIT_MAX_ROUTE_CALCULATION_LEGS

    MaxRouteOptions:
      type: integer
      default: 5
      description: "Maximum number of route options to return from calculate endpoint"
      env: TRANSIT_MAX_ROUTE_OPTIONS

    ConnectionGraphCacheSeconds:
      type: integer
      default: 300
      description: "TTL for cached connection graph in Redis (seconds). Graph is rebuilt on cache miss."
      env: TRANSIT_CONNECTION_GRAPH_CACHE_SECONDS

    JourneyArchiveAfterGameHours:
      type: number
      default: 168.0
      description: "Game-hours after arrival/abandonment before journey is archived from Redis to MySQL"
      env: TRANSIT_JOURNEY_ARCHIVE_AFTER_GAME_HOURS

    CargoSpeedPenaltyThresholdKg:
      type: number
      default: 100.0
      description: "Cargo weight above this threshold reduces speed. Speed reduction = (cargo - threshold) / (capacity - threshold) * DefaultCargoSpeedPenaltyRate"
      env: TRANSIT_CARGO_SPEED_PENALTY_THRESHOLD_KG

    SeasonalConnectionCheckIntervalSeconds:
      type: integer
      default: 60
      description: "How often the background worker checks Worldstate for seasonal connection status changes"
      env: TRANSIT_SEASONAL_CONNECTION_CHECK_INTERVAL_SECONDS

    DiscoveryCacheTtlSeconds:
      type: integer
      default: 600
      description: "TTL for per-entity discovery cache in Redis (seconds). Route calculation checks this cache to filter discoverable connections."
      env: TRANSIT_DISCOVERY_CACHE_TTL_SECONDS

    DefaultCargoSpeedPenaltyRate:
      type: number
      default: 0.3
      description: "Default cargo speed penalty rate applied when a mode's cargoSpeedPenaltyRate is null. Speed reduction = (cargo - threshold) / (capacity - threshold) * rate. 0.3 = 30% max speed reduction at full capacity."
      env: TRANSIT_DEFAULT_CARGO_SPEED_PENALTY_RATE

    JourneyArchiveRetentionDays:
      type: integer
      default: 365
      description: "Number of real-time days to retain archived journeys in MySQL before cleanup. 0 = retain indefinitely. The archival worker deletes records older than this threshold during each sweep."
      env: TRANSIT_JOURNEY_ARCHIVE_RETENTION_DAYS

    JourneyArchivalWorkerIntervalSeconds:
      type: integer
      default: 300
      description: "Real-time interval in seconds between journey archival worker sweeps. The worker scans Redis for completed/abandoned journeys older than JourneyArchiveAfterGameHours and moves them to MySQL."
      env: TRANSIT_JOURNEY_ARCHIVAL_WORKER_INTERVAL_SECONDS

    AutoUpdateLocationOnTransition:
      type: boolean
      default: true
      description: "When true, Transit automatically calls Location's /location/report-entity-position endpoint to update the entity's current location on journey departure, waypoint arrival, and final arrival. Location's presence system uses ephemeral Redis storage with 30s TTL, so Transit must re-report on each state transition. When false, the caller is responsible for updating Location separately. Both Transit and Location are L2 (always co-deployed on game nodes), so direct API calls are used -- not events."
      env: TRANSIT_AUTO_UPDATE_LOCATION_ON_TRANSITION
```

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<TransitService>` | Structured logging |
| `TransitServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 7 stores: transit-modes, transit-connections, transit-journeys, transit-journeys-archive, transit-connection-graph, transit-discovery, transit-discovery-cache) |
| `IMessageBus` | Event publishing (journey lifecycle, connection status, discovery, mode status, connection lifecycle) |
| `IEventConsumer` | Subscribes to `worldstate.season-changed` for seasonal connection status updates |
| `ILocationClient` | Location existence validation, entity position reporting via `AutoUpdateLocationOnTransition` (L2 hard dependency) |
| `IWorldstateClient` | Game-time calculations for journey ETAs, seasonal context for connection status (L2 hard dependency) |
| `ICharacterClient` | Species code lookup for mode compatibility checks (L2 hard dependency) |
| `ISpeciesClient` | Species property lookup for mode restrictions (L2 hard dependency) |
| `IInventoryClient` | Item tag checks for mode requirements (L2 hard dependency) |
| `IResourceClient` | Cleanup callback registration for location/character deletion (L1 hard dependency) |
| `IEnumerable<ITransitCostModifierProvider>` | DI collection of L4 cost enrichment providers (graceful degradation if none registered) |
| `TransitVariableProviderFactory` | Implements `IVariableProviderFactory` to provide `${transit.*}` variables to Actor (L2) |
| `ITransitRouteCalculator` / `TransitRouteCalculator` | Internal helper for Dijkstra-based route calculation over cached connection graphs |
| `ITransitConnectionGraphCache` / `TransitConnectionGraphCache` | Manages per-realm connection graph cache in Redis. Rebuilds from MySQL on cache miss. Invalidated on connection create/delete/status-change. |
| `SeasonalConnectionWorker` | Background `HostedService` that responds to `worldstate.season-changed` events. Scans connections with `seasonalAvailability` restrictions and updates status. Runs periodic checks every `SeasonalConnectionCheckIntervalSeconds`. |
| `JourneyArchivalWorker` | Background `HostedService` that archives completed/abandoned journeys from Redis to MySQL every `JourneyArchivalWorkerIntervalSeconds`. Also enforces `JourneyArchiveRetentionDays` retention policy. |

---

## API Endpoints

### Mode Management

```yaml
/transit/mode/register:
  access: developer
  description: "Register a new transit mode type"
  request:
    code: string                    # Unique mode code
    name: string
    description: string
    baseSpeedKmPerGameHour: decimal
    terrainSpeedModifiers: [TerrainSpeedModifier]  # Optional - per-terrain speed multipliers
    passengerCapacity: integer
    cargoCapacityKg: decimal
    cargoSpeedPenaltyRate: decimal  # Optional - overrides plugin config default
    compatibleTerrainTypes: [string]
    validEntityTypes: [string]      # Optional - entity type restrictions
    requirements: TransitModeRequirements
    fatigueRatePerGameHour: decimal
    noiseLevelNormalized: decimal
    realmRestrictions: [uuid]       # Optional
    tags: [string]
  response: { mode: TransitMode }
  errors:
    - MODE_CODE_ALREADY_EXISTS

/transit/mode/get:
  access: user
  description: "Get a transit mode by code"
  request:
    code: string
  response: { mode: TransitMode }
  errors:
    - MODE_NOT_FOUND

/transit/mode/list:
  access: user
  description: "List all registered transit modes"
  request:
    realmId: uuid                   # Optional - filter by realm availability
    terrainType: string             # Optional - filter by terrain compatibility
    tags: [string]                  # Optional - filter by tags
    includeDeprecated: boolean      # Default false
  response: { modes: [TransitMode] }

/transit/mode/update:
  access: developer
  description: "Update a transit mode's properties"
  request:
    code: string
    name: string                    # Optional
    description: string             # Optional
    baseSpeedKmPerGameHour: decimal # Optional
    terrainSpeedModifiers: [TerrainSpeedModifier]  # Optional - per-terrain speed multipliers
    passengerCapacity: integer      # Optional
    cargoCapacityKg: decimal        # Optional
    cargoSpeedPenaltyRate: decimal  # Optional - per-mode cargo penalty override
    compatibleTerrainTypes: [string] # Optional
    validEntityTypes: [string]      # Optional - entity type restrictions
    requirements: TransitModeRequirements # Optional
    fatigueRatePerGameHour: decimal # Optional
    noiseLevelNormalized: decimal   # Optional
    realmRestrictions: [uuid]       # Optional
    tags: [string]                  # Optional
  response: { mode: TransitMode }
  errors:
    - MODE_NOT_FOUND

/transit/mode/deprecate:
  access: developer
  description: "Deprecate a transit mode (existing journeys continue, new journeys cannot use it)"
  request:
    code: string
    reason: string
  response: { mode: TransitMode }
  errors:
    - MODE_NOT_FOUND
    - ALREADY_DEPRECATED
```

### Connection Management

```yaml
/transit/connection/create:
  access: developer
  description: "Create a connection between two locations"
  request:
    fromLocationId: uuid
    toLocationId: uuid
    bidirectional: boolean
    distanceKm: decimal
    terrainType: string
    compatibleModes: [string]
    seasonalAvailability: [SeasonalAvailabilityEntry]  # Optional
    baseRiskLevel: decimal
    riskDescription: string         # Optional
    discoverable: boolean           # Default false. When true, filtered from route queries
                                    # unless the requesting entity has discovered it.
    name: string                    # Optional
    code: string                    # Optional
    tags: [string]                  # Optional
  response: { connection: TransitConnection }
  errors:
    - LOCATIONS_NOT_FOUND           # One or both locations don't exist
    - CONNECTION_ALREADY_EXISTS     # Duplicate from→to connection
    - SAME_LOCATION                 # from and to are the same
    - INVALID_MODE_CODE             # Referenced mode doesn't exist
    - INVALID_SEASON_KEY            # seasonalAvailability key doesn't match realm's Worldstate seasons
  notes: |
    fromRealmId, toRealmId, and crossRealm are derived from the locations via
    the Location service -- callers never specify them. Cross-realm connections
    (where from and to are in different realms) are fully supported at the
    platform level. Games control cross-realm travel by policy: if a game's
    world design treats realms as isolated, it simply never creates connections
    between locations in different realms.

    When seasonalAvailability is provided, keys are validated against the realm's
    Worldstate season codes. If the realm's calendar template defines seasons
    ["wet", "dry"] but the caller provides {"winter": false}, the request fails
    with INVALID_SEASON_KEY. This prevents silent misconfiguration where seasonal
    connections never close because the key doesn't match any actual season.
    For cross-realm connections, season keys are validated against both realms'
    calendars (since each realm may use different season names).

/transit/connection/get:
  access: user
  description: "Get a connection by ID or code"
  request:
    connectionId: uuid              # One of these required
    code: string                    # One of these required
  response: { connection: TransitConnection }
  errors:
    - CONNECTION_NOT_FOUND

/transit/connection/query:
  access: user
  description: "Query connections by location, terrain, mode, or status"
  request:
    fromLocationId: uuid            # Optional - connections FROM this location
    toLocationId: uuid              # Optional - connections TO this location
    locationId: uuid                # Optional - connections involving this location (either end)
    realmId: uuid                   # Optional - connections touching this realm (fromRealmId OR toRealmId)
    crossRealm: boolean             # Optional - filter to only cross-realm (true) or intra-realm (false)
    terrainType: string             # Optional - filter by terrain
    modeCode: string                # Optional - filter by mode compatibility
    status: string                  # Optional - filter by status
    tags: [string]                  # Optional - filter by tags
    includeSeasonalClosed: boolean  # Default true
    page: integer
    pageSize: integer
  response: { connections: [TransitConnection], totalCount: integer }

/transit/connection/update:
  access: developer
  description: "Update a connection's properties (not status -- use update-status for that)"
  request:
    connectionId: uuid
    distanceKm: decimal             # Optional
    terrainType: string             # Optional
    compatibleModes: [string]       # Optional
    seasonalAvailability: [SeasonalAvailabilityEntry]  # Optional
    baseRiskLevel: decimal          # Optional
    riskDescription: string         # Optional
    discoverable: boolean           # Optional
    name: string                    # Optional
    code: string                    # Optional
    tags: [string]                  # Optional
  response: { connection: TransitConnection }
  errors:
    - CONNECTION_NOT_FOUND

/transit/connection/update-status:
  access: user
  description: "Transition a connection's operational status with optimistic concurrency"
  request:
    connectionId: uuid
    currentStatus: enum             # What caller believes current status is. Required unless forceUpdate.
                                    # open | closed | dangerous | blocked | seasonal_closed
    newStatus: enum                 # Target status: open | closed | dangerous | blocked
    reason: string
    forceUpdate: boolean            # Default false. When true, currentStatus is ignored (nullable).
                                    # Use for administrative overrides.
  response: { connection: TransitConnection }
  emits: transit.connection.status-changed
  errors:
    - CONNECTION_NOT_FOUND
    - STATUS_MISMATCH               # currentStatus doesn't match actual. Response includes actual
                                    # status so caller can refresh and retry. Only when forceUpdate=false.
  notes: |
    Separated from general update because status changes are operational (game-driven)
    while property changes are administrative (developer-driven).
    Status changes publish events; property changes do not.

    Optimistic concurrency prevents silent status conflicts between independent actors.
    Environment (L4) closing a road for a storm while Faction (L4) blocks it for a war:
    whichever writes second sees STATUS_MISMATCH and retries with awareness of the
    current state. Use forceUpdate=true for admin overrides that don't need concurrency.

    The seasonal_closed status is managed by the Seasonal Connection Worker -- it uses
    forceUpdate=true since it is the authoritative source for seasonal state.

/transit/connection/delete:
  access: developer
  description: "Delete a connection between locations"
  request:
    connectionId: uuid
  response: { deleted: boolean }
  errors:
    - CONNECTION_NOT_FOUND
    - ACTIVE_JOURNEYS_EXIST         # Cannot delete while journeys are using this connection

/transit/connection/bulk-seed:
  access: developer
  description: "Seed connections from configuration (two-pass: create connections, then validate modes)"
  request:
    realmId: uuid                   # Optional - scope location code resolution to this realm.
                                    # When provided, all location codes are resolved within this realm
                                    # and replaceExisting deletes connections for this realm only.
                                    # When null, location codes must be globally unique or qualified.
    connections: [
      {
        fromLocationCode: string    # Location codes (resolved via Location service)
        toLocationCode: string
        bidirectional: boolean
        distanceKm: decimal
        terrainType: string
        compatibleModes: [string]
        seasonalAvailability: [SeasonalAvailabilityEntry]
        baseRiskLevel: decimal
        name: string
        code: string
        tags: [string]
      }
    ]
    replaceExisting: boolean        # If true, delete existing connections scoped by realmId first
  response: { created: integer, updated: integer, errors: [string] }
  notes: |
    Two-pass resolution: first pass creates connections with location code lookup,
    second pass validates all mode codes exist. Follows Location's bulk-seed pattern.

    Cross-realm connections can be seeded by omitting realmId and using globally-unique
    location codes, or by providing location codes from different realms in separate
    bulk-seed calls and linking them with individual connection/create calls.
```

### Journey Lifecycle

```yaml
/transit/journey/create:
  access: user
  description: "Plan a journey (status: preparing). Does not start travel."
  request:
    entityId: uuid
    entityType: string              # "character", "npc", "caravan", "army", "creature"
    originLocationId: uuid
    destinationLocationId: uuid
    primaryModeCode: string         # Preferred transit mode. Used for all legs unless overridden.
    preferMultiModal: boolean       # Default false. When true, route calculation selects the
                                    # best mode per leg (e.g., horseback on roads, river_boat on
                                    # rivers, walking on trails). Each leg's modeCode may differ.
    plannedDepartureGameTime: decimal  # Optional - defaults to current game-time
    partySize: integer              # Default 1
    cargoWeightKg: decimal          # Default 0
  response: { journey: TransitJourney }
  errors:
    - ORIGIN_NOT_FOUND
    - DESTINATION_NOT_FOUND
    - MODE_NOT_FOUND
    - MODE_DEPRECATED
    - NO_ROUTE_AVAILABLE            # No connected path for this mode
    - MODE_REQUIREMENTS_NOT_MET     # Entity doesn't meet mode requirements
    - ENTITY_TYPE_NOT_ALLOWED       # Entity type not in mode's validEntityTypes
  notes: |
    Internally calls route/calculate to find the best path and populate legs.
    Checks mode compatibility via check-availability.
    Does NOT start the journey -- call /transit/journey/depart for that.

    When preferMultiModal is true, each leg gets the fastest compatible mode
    the entity can use on that connection. The journey's primaryModeCode is
    set to the mode used for the most legs (plurality).

/transit/journey/depart:
  access: user
  description: "Start a prepared journey (preparing → in_transit)"
  request:
    journeyId: uuid
  response: { journey: TransitJourney }
  emits: transit.journey.departed
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "preparing"
    - CONNECTION_CLOSED             # First leg's connection is now closed

/transit/journey/resume:
  access: user
  description: "Resume an interrupted journey (interrupted → in_transit)"
  request:
    journeyId: uuid
  response: { journey: TransitJourney }
  emits: transit.journey.resumed
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "interrupted"
    - CONNECTION_CLOSED             # Current leg's connection is now closed
  notes: |
    Distinct from depart: depart is for initial departure (preparing → in_transit),
    resume is for continuing after interruption (interrupted → in_transit).
    Consumers (Trade, Analytics) can distinguish "started" from "resumed" via events.

/transit/journey/advance:
  access: user
  description: "Mark arrival at next waypoint (completes current leg, starts next or arrives)"
  request:
    journeyId: uuid
    arrivedAtGameTime: decimal      # Game-time when waypoint was reached
    incidents: [                    # Optional - things that happened during this leg
      {
        reason: string              # "bandit_attack", "storm_delay", "detour"
        durationGameHours: decimal  # How much time the incident added
        description: string         # Optional narrative description
      }
    ]
  response: { journey: TransitJourney }
  emits: transit.journey.waypoint-reached   # If more legs remain
  emits: transit.journey.arrived             # If this was the last leg
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "in_transit"
    - NO_CURRENT_LEG                # Already at final destination
  notes: |
    If this completes the final leg, journey status transitions to "arrived"
    and transit.journey.arrived is emitted instead of waypoint-reached.
    The arrivedAtGameTime is recorded for historical accuracy.

/transit/journey/advance-batch:
  access: user
  description: "Advance multiple journeys in a single call (SDK efficiency for NPC scale)"
  request:
    advances: [
      {
        journeyId: uuid
        arrivedAtGameTime: decimal
        incidents: [               # Optional - per-journey incidents
          {
            reason: string
            durationGameHours: decimal
            description: string
          }
        ]
      }
    ]
  response:
    results: [
      {
        journeyId: uuid
        success: boolean
        error: string              # null on success. Error code on failure.
        journey: TransitJourney    # Updated journey (null on failure)
      }
    ]
  emits: transit.journey.waypoint-reached   # Per journey, if more legs remain
  emits: transit.journey.arrived             # Per journey, if final leg completed
  errors:
    - PARTIAL_FAILURE              # Some advances failed -- check individual results
  notes: |
    Batch version of /transit/journey/advance for game SDK efficiency.
    Journey progression is game-engine-driven: the game server (or NPC Actor
    brain via SDK) must explicitly call advance when entities reach waypoints.
    At NPC scale (10,000+ concurrent journeys), the game engine advances
    entities in batches per tick rather than making individual API calls.

    Each advance in the batch is processed independently -- failure of one
    does not roll back others. Results include per-journey success/failure
    with error details. Events are emitted per-journey as with the single
    advance endpoint.

    Ordering within the batch is preserved: advances are applied sequentially
    to handle cases where the same journeyId appears multiple times (advancing
    through multiple waypoints in a single tick).

/transit/journey/arrive:
  access: user
  description: "Force-arrive a journey at destination (skips remaining legs)"
  request:
    journeyId: uuid
    arrivedAtGameTime: decimal
    reason: string                  # Why legs were skipped ("teleported", "fast_travel", "narrative")
  response: { journey: TransitJourney }
  emits: transit.journey.arrived
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "in_transit" or "at_waypoint"
  notes: |
    Used for narrative fast-travel, teleportation, or game-driven skip.
    Remaining legs are marked as "skipped" rather than "completed".

/transit/journey/interrupt:
  access: user
  description: "Interrupt an active journey (combat, event, breakdown)"
  request:
    journeyId: uuid
    reason: string
    gameTime: decimal               # When the interruption occurred
  response: { journey: TransitJourney }
  emits: transit.journey.interrupted
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "in_transit"
  notes: |
    Journey status transitions to "interrupted". To resume, call /transit/journey/resume
    (which transitions interrupted → in_transit and emits transit.journey.resumed).
    To give up, call /transit/journey/abandon.

/transit/journey/abandon:
  access: user
  description: "Abandon a journey (entity stays at current location)"
  request:
    journeyId: uuid
    reason: string
  response: { journey: TransitJourney }
  emits: transit.journey.abandoned
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "preparing", "in_transit", "at_waypoint", or "interrupted"

/transit/journey/get:
  access: user
  description: "Get a journey by ID"
  request:
    journeyId: uuid
  response: { journey: TransitJourney }
  errors:
    - JOURNEY_NOT_FOUND

/transit/journey/query-by-connection:
  access: user
  description: "List active journeys currently traversing a specific connection"
  request:
    connectionId: uuid
    status: string                  # Optional - filter by status (default: in_transit)
    page: integer
    pageSize: integer
  response: { journeys: [TransitJourney], totalCount: integer }
  errors:
    - CONNECTION_NOT_FOUND
  notes: |
    Returns journeys whose current leg uses this connection. Enables "who's on
    this road?" queries for encounter generation, bandit ambush targeting,
    caravan interception, and road traffic monitoring. Overlaps with Location's
    "entities at location" query but provides transit-specific context (mode,
    direction, progress, ETA) that Location cannot.

/transit/journey/list:
  access: user
  description: "List journeys for an entity or within a realm"
  request:
    entityId: uuid                  # Optional - journeys for this entity
    entityType: string              # Optional - filter by entity type
    realmId: uuid                   # Optional - journeys touching this realm (origin OR destination)
    crossRealm: boolean             # Optional - filter to cross-realm journeys only
    status: string                  # Optional - filter by status
    activeOnly: boolean             # Default true (excludes arrived, abandoned)
    page: integer
    pageSize: integer
  response: { journeys: [TransitJourney], totalCount: integer }

/transit/journey/query-archive:
  access: user
  description: "Query archived (completed/abandoned) journeys from MySQL historical store"
  request:
    entityId: uuid                  # Optional
    entityType: string              # Optional
    realmId: uuid                   # Optional - journeys touching this realm (origin OR destination)
    crossRealm: boolean             # Optional - filter to cross-realm journeys only
    originLocationId: uuid          # Optional
    destinationLocationId: uuid     # Optional
    modeCode: string                # Optional
    status: string                  # Optional (typically "arrived" or "abandoned")
    fromGameTime: decimal           # Optional - journey departed after this game-time
    toGameTime: decimal             # Optional - journey departed before this game-time
    page: integer
    pageSize: integer
  response: { journeys: [TransitJourney], totalCount: integer }
  notes: |
    Reads from the MySQL archive store (transit-journeys-archive).
    Used by Trade (velocity calculations), Analytics (travel patterns),
    and Character History (travel biography generation).
```

### Route Calculation

```yaml
/transit/route/calculate:
  access: user
  description: "Calculate route options between two locations (pure computation, no state mutation)"
  request:
    fromLocationId: uuid
    toLocationId: uuid
    modeCode: string                # Optional - specific mode. Null = try all modes
    preferMultiModal: boolean       # Default false. When true, selects best mode per leg.
    entityId: uuid                  # Optional - for discovery filtering. When provided,
                                    # discoverable connections are filtered to only those
                                    # this entity has discovered. When null, discoverable
                                    # connections are excluded entirely (conservative).
    maxLegs: integer                # Optional - maximum legs to consider (default from config)
    sortBy: string                  # "fastest" (default), "safest", "shortest"
    maxOptions: integer             # How many options to return (default from config)
    includeSeasonalClosed: boolean  # Default false - exclude currently closed routes
  response: { options: [TransitRouteOption] }
  errors:
    - LOCATIONS_NOT_FOUND
    - NO_ROUTE_AVAILABLE            # No connected path exists
  notes: |
    Pure computation endpoint. Performs graph search over connection graph.
    Does NOT apply entity-specific DI cost modifiers (those are applied by
    the variable provider when GOAP evaluates the results).
    Returns objective travel data: distance, time, risk, mode.

    The graph search uses Dijkstra's algorithm with configurable cost function:
    - "fastest": cost = estimated_hours (distance / effective_speed per leg,
      where effective_speed = baseSpeed × terrainSpeedModifiers[legTerrainType])
    - "safest": cost = cumulative_risk
    - "shortest": cost = distance_km

    Multiple connections between the same location pair (parallel edges) are
    supported. Route calculation evaluates each connection independently,
    choosing the best per mode. E.g., a road and a river between two towns
    are separate connections with different mode compatibility.

    When preferMultiModal is true, each leg selects the fastest compatible mode
    the entity can use. Route options include per-leg mode codes in legModes[].

    Cross-realm routing: When fromLocationId and toLocationId are in different
    realms, the graph search merges the connection graphs for all relevant realms.
    Cross-realm connections appear in both realms' cached graphs, so the merge
    naturally discovers realm-spanning paths. Games that never create cross-realm
    connections will never see cross-realm routes -- the platform supports it,
    but the graph content determines what's reachable.
```

### Mode Compatibility

```yaml
/transit/mode/check-availability:
  access: user
  description: "Check which transit modes an entity can currently use"
  request:
    entityId: uuid
    entityType: string
    locationId: uuid                # Optional - only modes available at this location
    modeCode: string                # Optional - check specific mode only
  response:
    availableModes: [
      {
        code: string
        available: boolean
        unavailableReason: string   # null if available. "missing_item", "wrong_species", etc.
        effectiveSpeed: decimal     # Adjusted for cargo weight if applicable
        preferenceCost: decimal     # From DI cost modifier providers (0.0 = neutral, 1.0 = dreads it)
      }
    ]
  notes: |
    Checks mode requirements against entity state:
    - Entity type: checks entityType against mode's validEntityTypes (if set).
      Modes with null validEntityTypes skip this check for non-character entities.
    - Item requirements: calls lib-inventory to check if entity has required item tag (L2 hard)
    - Species requirements: calls lib-character or lib-species for species code (L2 hard)
    - Terrain speed: effectiveSpeed accounts for terrainSpeedModifiers when locationId
      is provided and the entity's current connection terrain is known.

    Also applies ITransitCostModifierProvider enrichment to compute preferenceCost
    and effectiveSpeed adjustments (e.g., proficiency-based speed bonuses from Status).
    This is the endpoint the variable provider calls internally.
```

### Connection Discovery

```yaml
/transit/discovery/reveal:
  access: user
  description: "Reveal a discoverable connection to an entity (explicit grant)"
  request:
    entityId: uuid
    connectionId: uuid
    source: string                  # How discovered: "travel", "guide", "hearsay", "map", "quest_reward"
  response: { discovered: boolean, alreadyKnown: boolean }
  errors:
    - CONNECTION_NOT_FOUND
    - NOT_DISCOVERABLE              # Connection is not marked as discoverable (already public)
  notes: |
    Also called internally by journey/advance when a leg uses a discoverable connection.
    Hearsay (L4) calls this when propagating route knowledge via rumor.
    Quest rewards can include connection reveals.

/transit/discovery/list:
  access: user
  description: "List connections an entity has discovered"
  request:
    entityId: uuid
    realmId: uuid                   # Optional - filter by realm (fromRealmId OR toRealmId)
  response: { connectionIds: [uuid] }

/transit/discovery/check:
  access: user
  description: "Check if an entity has discovered specific connections"
  request:
    entityId: uuid
    connectionIds: [uuid]
  response:
    results: [
      {
        connectionId: uuid
        discovered: boolean
        discoveredAt: timestamp     # null if not discovered
        source: string              # How it was discovered (null if not)
      }
    ]
```

**Total endpoints: 30**

---

## Variable Provider (`${transit.*}`)

Transit implements `IVariableProviderFactory` providing the `${transit}` namespace to Actor (L2) via the Variable Provider Factory pattern.

### Provider Implementation

```csharp
// In plugin registration
services.AddSingleton<IVariableProviderFactory, TransitVariableProviderFactory>();
```

### Available Variables

```yaml
# Mode availability and preference (per entity, computed via check-availability)
${transit.mode.<code>.available}:
  type: boolean
  description: "Can this entity currently use this transit mode?"
  example: "${transit.mode.horseback.available}"
  notes: "Checks item requirements, species restrictions, skill requirements"

${transit.mode.<code>.speed}:
  type: decimal
  description: "Effective speed in km/game-hour for this entity on this mode"
  example: "${transit.mode.wagon.speed}"
  notes: "Accounts for cargo weight penalty and DI cost modifier speed multipliers (including proficiency bonuses from Status providers)"

${transit.mode.<code>.preference_cost}:
  type: decimal
  description: "GOAP cost modifier from DI enrichment providers. 0.0 = neutral, 1.0 = extreme aversion"
  example: "${transit.mode.horseback.preference_cost}"
  notes: |
    Aggregated from ITransitCostModifierProvider implementations:
    - Disposition: dread/comfort feelings toward this mode
    - Hearsay: believed danger of mode (e.g., "ships are cursed")
    - Ethology: species-level affinity (wolves don't ride horses)

# Active journey state
${transit.journey.active}:
  type: boolean
  description: "Is this entity currently on a journey?"

${transit.journey.mode}:
  type: string
  description: "Transit mode of the active journey (null if no active journey)"

${transit.journey.destination_code}:
  type: string
  description: "Location code of the journey destination"

${transit.journey.eta_hours}:
  type: decimal
  description: "Estimated game-hours until arrival"

${transit.journey.progress}:
  type: decimal
  description: "Journey progress from 0.0 (just departed) to 1.0 (arrived)"

${transit.journey.remaining_legs}:
  type: integer
  description: "Number of route legs remaining"

# Nearest location queries (cached, refreshed periodically)
${transit.nearest.<location_code>.hours}:
  type: decimal
  description: "Best-case travel time in game-hours to the named location from current position"
  example: "${transit.nearest.eldoria.hours}"
  notes: "Uses best available mode for this entity. Cached per-entity, refreshed on location change."

${transit.nearest.<location_code>.best_mode}:
  type: string
  description: "Fastest available transit mode to reach the named location"
  example: "${transit.nearest.iron_mines.best_mode}"

# Discovery state (per entity)
${transit.discovered_connections}:
  type: integer
  description: "Total number of discoverable connections this entity has revealed"

${transit.connection.<code>.discovered}:
  type: boolean
  description: "Has this entity discovered the named connection?"
  example: "${transit.connection.smuggler_tunnel.discovered}"
  notes: "Only meaningful for connections with discoverable=true. Always true for non-discoverable."
```

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

**Entities in transit**: While traveling between waypoints, the entity's last known location (as tracked by Location's ephemeral presence system) is the most recently departed-from or arrived-at location. The entity still "exists" at that location from Location's perspective -- `/location/get-entity-location` will return them there. Transit's `${transit.journey.active}` variable and the `query-by-connection` endpoint provide the transit-specific context that Location cannot (mode, direction, progress, ETA). The entity occupies a location in the containment sense even while physically in transit between nodes in the connectivity graph. Location's presence entries have a 30-second TTL, so Transit must periodically re-report position (or upon each state transition) to keep the entity visible.

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

Transit registers as an `ISeededResourceProvider` with lib-resource for cleanup coordination:
- **Location deletion (CASCADE)**: When a location is deleted, Resource calls Transit's cleanup callback. Transit closes all connections referencing the deleted location (`status: closed`, `reason: "location_deleted"`) and interrupts active journeys passing through it.
- **Character deletion (CASCADE)**: Transit's cleanup callback clears discovery data for the deleted entity from the `transit-discovery` store and abandons any active journeys for that entity.
- Transit does NOT subscribe to `location.deleted` or `character.deleted` events for cleanup -- this is handled exclusively through the Resource cleanup callback pattern per FOUNDATION TENETS (Resource-Managed Cleanup).

---

## Background Services

### Seasonal Connection Worker

```yaml
SeasonalConnectionWorker:
  interval: config.SeasonalConnectionCheckIntervalSeconds
  description: |
    Subscribes to Worldstate season-change events. When a season boundary occurs,
    scans all connections with seasonalAvailability restrictions and updates their
    status accordingly. Connections closing for the season transition to
    "seasonal_closed"; connections opening transition to "open".

    Also checks for active journeys on newly-closed connections and publishes
    warnings (but does NOT interrupt them -- the game decides how to handle
    travelers caught by seasonal closure).

  publishes:
    - transit.connection.status-changed (for each connection that changes)
```

### Journey Archival Worker

```yaml
JourneyArchivalWorker:
  interval: 300  # Every 5 minutes (real-time)
  description: |
    Scans Redis for completed/abandoned journeys older than
    JourneyArchiveAfterGameHours and moves them to MySQL archive.
    Frees Redis memory while preserving historical travel data
    for analytics and NPC memory.

    Also enforces JourneyArchiveRetentionDays: deletes archived journeys
    older than the retention threshold from MySQL. When set to 0, archives
    are retained indefinitely.
```

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

## Stubs & Unimplemented Features

This service is entirely aspirational -- no schema, code, or tests exist. Implementation requires:
1. Create `schemas/transit-api.yaml`, `transit-events.yaml`, `transit-configuration.yaml`
2. Add state store definitions to `schemas/state-stores.yaml`
3. Run `cd scripts && ./generate-service.sh transit`
4. Implement `TransitService.cs`, `TransitServiceEvents.cs`, `TransitServiceModels.cs`
5. Create `bannou-service/Providers/ITransitCostModifierProvider.cs`
6. Implement background workers (Seasonal Connection Worker, Journey Archival Worker)
7. Write unit tests in `plugins/lib-transit.tests/`

---

## Potential Extensions

1. **Caravan formations (Phase 2)**: Multiple entities traveling together as a group. Group speed = slowest member. Group risk = reduced (safety in numbers). Caravan as an entity type for journeys, with member tracking and formation bonuses. Faction merchant guilds could manage NPC caravans. **Promoted to Phase 2** -- the data model already accommodates this via `entityType: "caravan"` and `partySize`, but Phase 2 needs: a `partyMembers: [uuid]` field on `TransitJourney` for tracking individual members, batch departure/advance for all party members, and a party leader concept for GOAP decisions. The `validEntityTypes` restriction on modes already supports caravan-only modes (e.g., `wagon` with `validEntityTypes: ["caravan", "army"]`).

2. **Fatigue and rest**: Long journeys accumulate fatigue. After a configurable threshold, the entity must rest (journey auto-pauses at next waypoint). Rest duration depends on mode and entity stamina. Creates natural stopping points at inns and camps.

3. **Weather-reactive connections**: Environment (L4) automatically calls `/transit/connection/update-status` when weather conditions make connections impassable. Storms close mountain passes; floods close river crossings; drought closes river boat routes. This is a pure consumer relationship -- Transit exposes the API, Environment calls it.

4. **Mount bonding**: Integration with Relationship (L2) for character-mount relationships. A well-bonded mount has higher effective speed and lower fatigue. Bond strength grows with travel distance. Follows the existing Relationship entity-to-entity model.

5. **Transit fares**: Monetary cost for using certain transit modes or connections. Ferries, toll roads, carriage services, and teleportation portals could have a fare. Two options: (a) Transit stores fare data per-connection/mode and calls Currency (L2) during `journey/depart` to debit the fare -- feasible since both are L2. (b) Fares are purely a Trade (L4) concern that wraps Transit journeys with economic logic. Option (a) keeps fare enforcement at the primitive level (NPCs can't cheat tolls), while (b) keeps Transit purely about movement physics. **Open design question**: should Transit know about money, or should fares be an L4 overlay?

---

## Resolved Design Decisions

1. **Connection granularity**: Allow arbitrary connections but tag them appropriately (`tags: ["portal"]`, `tags: ["trade_route"]`). Enables teleportation portals and long-distance shipping routes alongside physically meaningful roads.

2. **Journey auto-progression**: No. This violates the declarative lifecycle pattern. The game/Actor drives progression. Transit records and calculates but doesn't simulate.

3. **Intra-location transit**: No. Intra-location movement is Mapping's domain (spatial positioning) or the game client's domain (local navigation). Transit handles inter-location movement only.

4. **Multi-mode journeys**: Yes, core feature. Each leg specifies a `modeCode` that can override the journey's `primaryModeCode`. `preferMultiModal` flag on create/calculate selects best mode per leg automatically.

5. **Reverse connections**: Bidirectional connections share properties. For asymmetric connections (upstream/downstream), create two one-way connections with different speeds. Keeps the data model simple.

6. **Transit and Mapping relationship**: Complementary, not overlapping. Mapping is continuous spatial data ("where exactly am I"), Transit is discrete connectivity ("how do I get from town A to town B"). No integration needed beyond both referencing Location entities.

7. **Cross-realm connections**: Fully supported at the platform level. Connections store `fromRealmId` and `toRealmId` (derived from their endpoint locations), with a convenience `crossRealm` flag. Route calculation merges per-realm graphs when origin and destination are in different realms. Games control cross-realm travel by policy: Arcadia's realms are intentionally isolated (no cross-realm connections are ever created), but other games on the platform may freely connect locations across realms. The platform provides the primitive; game design decides whether to use it.

8. **Connection discovery**: Core feature. Connections have `discoverable: boolean` flag. Per-entity discovery tracking in Redis. Route calculation filters by discovery status. Discovery via travel, explicit grant, or Hearsay integration.

9. **Proficiency via DI enrichment**: Proficiency-based speed bonuses flow through `ITransitCostModifierProvider`, not through direct Seed integration. An L4 provider queries Status (which aggregates seed-derived capabilities) and returns a `SpeedMultiplier` to Transit. This keeps Transit decoupled from growth mechanics while still supporting the content flywheel: experienced riders are faster, but Transit doesn't know why -- it just receives a multiplier from the enrichment layer.

10. **Status transition concurrency**: Optimistic concurrency pattern with `currentStatus`/`newStatus` and `forceUpdate` bypass. Prevents silent status conflicts between independent actors while supporting administrative overrides.

11. **Terrain speed modifiers**: Core feature. Modes define optional `terrainSpeedModifiers` (per-terrain multipliers on base speed). Route calculation uses `baseSpeed × terrainSpeedModifiers[terrainType]` per-leg, making terrain a first-class factor in path cost evaluation. Missing keys default to 1.0 (base speed).

12. **Per-mode cargo speed penalty**: Core feature. Each mode can optionally set `cargoSpeedPenaltyRate` to override the plugin-level `DefaultCargoSpeedPenaltyRate`. Null falls back to the plugin config. 0.0 disables the penalty entirely (pack mules, cargo ships). This avoids unrealistic penalties on modes designed for cargo.

13. **Entity type validation**: Core feature. Modes define optional `validEntityTypes` (string array). When set, only those entity types may use the mode. Null disables validation for non-character entities (caller's responsibility). Characters are not exempt -- modes for armies/caravans may validly exclude characters.

14. **Seasonal key validation**: Core feature. Connection `seasonalAvailability` keys are validated against the realm's Worldstate season codes on creation. Invalid keys produce `INVALID_SEASON_KEY` error. Prevents silent misconfiguration where seasonal closures never trigger.

15. **Discovery store durability**: MySQL-backed with Redis cache. Discovery is permanent world knowledge that must survive Redis restarts. The Redis cache (`transit-discovery-cache`) provides fast route calculation lookups with lazy population from MySQL on cache miss.

16. **Location presence tracking**: API-based, not event-based. `AutoUpdateLocationOnTransition` config (default true) makes Transit call `/location/report-entity-position` on departure, waypoint arrival, and final arrival. Location's presence entries are ephemeral (30s TTL in Redis) with bidirectional lookups, so Transit must re-report on each state transition to keep entries alive. Both services are L2 and always co-deployed, so direct API calls are correct. Entities remain at their last known location while in transit between graph nodes.

17. **Journey archive retention**: Configurable via `JourneyArchiveRetentionDays` (default 365). The archival worker deletes archives older than the threshold. 0 = retain indefinitely.

18. **Variable provider cache strategy**: Lazy invalidation with configurable TTL. Mode availability is cached per-entity and expires after TTL. Acceptable tradeoff given worlds are large and transit decisions are not frame-rate-sensitive. Eager invalidation deferred until empirical evidence of staleness issues.

19. **Journey progression is game-engine-driven**: The game server (or NPC Actor brain via game engine SDK) must explicitly call `advance` (or `advance-batch` at scale) when entities reach waypoints. Transit cannot auto-progress journeys because real game models running into proper obstacles (terrain collision, NPC encounters, physics) cannot be tracked in real-time by backend services. The game engine runs the simulation; Transit records the outcomes. This is architecturally consistent with the declarative lifecycle pattern (resolved decision #2) but specifically addresses the "who drives the clock" question: the game engine does, not Transit, and the batch endpoint exists to make this efficient at 10K+ concurrent journey scale.

20. **Entity presence during transit is game-driven**: While an entity is traveling between waypoints, its physical position in the game world is the game engine's concern, not Transit's. Transit updates Location's ephemeral presence on each state transition (departure, waypoint, arrival) via `AutoUpdateLocationOnTransition`, but between transitions the entity's moment-to-moment position is tracked by the game engine. There is no TTL-based "entity in transit" heartbeat from Transit -- the game engine's own tick loop is responsible for keeping entities visible in the world. This is the same principle as journey progression: the game simulates, Transit records.

---

## Known Quirks & Caveats

N/A -- This service is aspirational (pre-implementation). Quirks will be documented as the service is implemented.

---

## Work Tracking

No active work items. This document is an aspirational design specification. Implementation has not begun.

---

*This document is an aspirational deep dive for a service that does not yet exist. No schema files or implementation code have been created. The design follows established Bannou patterns and respects the service hierarchy. Implementation requires schemas, code generation, service implementation, and testing -- in that order.*
