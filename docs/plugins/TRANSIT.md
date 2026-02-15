# Transit Service (lib-transit)

> **Status**: Aspirational (Pre-Implementation)
> **Layer**: L2 GameFoundation
> **Schema**: `schemas/transit-api.yaml` (not yet created)
> **Hard Dependencies**: Location (L2), Worldstate (L2), Character (L2), Species (L2), Item (L2), Inventory (L2), Seed (L2)
> **Soft Dependencies**: None at the service client level (L4 services enrich via DI provider pattern)
> **No schema, no code.**

---

## Service Overview

The Transit service (L2 GameFoundation) is the geographic connectivity and movement primitive for Bannou. It completes the spatial model by adding **edges** (connections between locations) to Location's **nodes** (the hierarchical place tree), then provides a type registry for **how** things move (transit modes) and temporal tracking for **when** they arrive (journeys computed against Worldstate's game clock). Transit is to movement what Seed is to growth and Collection is to unlocks -- a generic, reusable primitive that higher-layer services orchestrate for domain-specific purposes. Internal-only, never internet-facing.

**What Transit IS**: A movement capability registry, a connectivity graph, and a travel time calculator.

**What Transit is NOT**: A trade system, a cargo manager, a pathfinding engine, or a combat movement controller. It doesn't know about goods, tariffs, supply chains, or real-time spatial positioning. Those concerns belong to Trade (L4), Inventory (L2), and Mapping (L4) respectively.

---

## Design Philosophy

### Geography Needs Edges, Not Just Nodes

Location manages a **containment hierarchy**: realms contain regions, regions contain cities, cities contain districts, districts contain buildings. This models "what is inside what" but says nothing about "how do you get from A to B." A city can contain two districts without any information about the road connecting them, the river separating them, or the mountain pass required to reach either from outside.

Transit provides the **connectivity graph** that sits alongside Location's containment tree. Every connection between two locations is an explicit, typed edge with distance, terrain, and mode compatibility. Without Transit, the game world is a tree of nested containers with no concept of proximity, traversal difficulty, or travel time. With Transit, it becomes a navigable geography.

### Movement Is a Primitive, Not a Feature

Any game with locations needs movement between them. A dungeon crawler needs "it takes 2 game-hours to walk from the village to the cave." A strategy game needs "armies move at different speeds on different terrain." An economy simulation needs "goods shipped by wagon take 3 game-days." A social simulation needs "the merchant travels between towns weekly."

By placing Transit at L2, all these use cases are served without hierarchy violations. Actor (L2) can compute NPC travel times. Game Session (L2) can estimate session boundaries. Quest (L2) can set travel-based objectives. Workshop (L2) can factor transport lag into production chains. If Transit were L4, none of these integrations would be possible.

### Declarative Journeys

Transit follows the established Bannou pattern for lifecycle tracking: the **game drives events**, the **plugin records state**. Transit does not automatically progress journeys. It does not move entities. It does not update positions. The game server (or an NPC's Actor brain) calls `/transit/journey/depart`, `/transit/journey/advance`, and `/transit/journey/arrive` as the entity actually moves through the world. Transit records the temporal commitment, computes ETAs against Worldstate, and publishes events.

This is the same pattern used by Scene (checkout/commit/discard), Workshop (lazy evaluation with materialization), and the planned Shipment lifecycle in Trade. The plugin is a state machine and calculator, not an autonomous simulation engine.

### Mode Registry with String Codes

Transit modes are string codes, not enums. `walking`, `horseback`, `river_boat`, `flying_mount`, `teleportation`, `wagon`, `ocean_vessel` -- all registered via API, not hardcoded in schemas. This follows the pattern established by Seed (seed type codes), Collection (collection type codes), and Chat (room type codes). New modes can be added without schema changes, enabling game-specific transportation without platform modification.

A transit mode defines abstract movement capability. A specific horse is an Item instance in an Inventory container. The mode's requirements specify what items, skills, or species are needed to use it. Transit checks compatibility; it doesn't manage the items themselves.

Modes also support **proficiency via Seed integration**. A mode can optionally reference a `proficiencySeedTypeCode` -- when set, the entity's Seed growth level for that type modifies effective speed: `baseSpeed × (1 + proficiencyBonus)`. A character who has ridden horses their entire life (high `riding_proficiency` seed growth) is genuinely faster than a novice. This connects Transit to the content flywheel: an NPC's lifetime of travel proficiency data becomes generative input when their archive is compressed.

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
│  "walking": speed 5, capacity 1, terrain [any], requires []      │
│  "horseback": speed 25, capacity 2, terrain [road,trail,plain]   │
│  "wagon": speed 10, capacity 500, terrain [road], requires [item]│
│  "river_boat": speed 15, capacity 100, terrain [river]           │
│  "ocean_vessel": speed 20, capacity 2000, terrain [ocean]        │
│  "flying_mount": speed 40, capacity 2, terrain [any]             │
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
            horseback: 30/25 + 80/25 = 4.4 game-hours
            river_boat (leg 1 only): 30/15 + 80/25 = 5.2 game-hours

  Output: [
    { route: [Eldoria, Riverside, Iron Mines], mode: horseback,
      hours: 4.4, distance: 110, legs: 2, risk: 0.15 },
    { route: [Eldoria, Riverside, Iron Mines], mode: walking,
      hours: 22.0, distance: 110, legs: 2, risk: 0.08 },
  ]

  Note: Mountain road direct route unavailable in winter.
  In summer, it would appear as:
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
  baseSpeedKmPerGameHour: decimal   # Base speed in game-kilometers per game-hour
  passengerCapacity: integer        # How many entities can ride (1 for horse, 20 for ship)
  cargoCapacityKg: decimal          # Weight capacity in game-kg (0 for walking, 500 for wagon)

  # Terrain compatibility
  compatibleTerrainTypes: [string]  # Which terrain types this mode traverses
                                    # Empty = all terrain (e.g., walking, flying)

  # Requirements (all must be met to use this mode)
  requirements: TransitModeRequirements

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

  # Proficiency via Seed (core -- not optional)
  proficiencySeedTypeCode: string   # null = no proficiency needed. "riding_proficiency" = Seed type
                                    # Entity's Seed growth level modifies effective speed:
                                    # effectiveSpeed = baseSpeed × (1 + proficiencyBonus)
                                    # proficiencyBonus = seedGrowthLevel × maxProficiencyBonus
  maxProficiencyBonus: decimal      # Maximum speed bonus at full Seed growth (e.g., 0.5 = +50%)
                                    # Default 0.3 if proficiencySeedTypeCode is set

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
  seasonalAvailability: object      # Keys MUST match the realm's Worldstate season codes.
                                    # Worldstate uses opaque calendar templates -- season names are
                                    # configurable per realm (a desert realm may have "wet"/"dry",
                                    # not "winter"/"spring"). The Seasonal Connection Worker queries
                                    # Worldstate for the realm's current season name and matches
                                    # against these keys.
                                    # Example: { "winter": false, "spring": true, "summer": true, "autumn": true }
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

  # Realm (derived from location, stored for query efficiency)
  # All connections are realm-scoped. Cross-realm travel is not handled at this level.
  realmId: uuid

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
  status: enum                      # pending | in_progress | completed | skipped
  completedAtGameTime: decimal      # When this leg was completed (null if not)

TransitInterruption:
  legIndex: integer                 # Which leg was interrupted
  gameTime: decimal                 # When the interruption occurred
  reason: string                    # "bandit_attack", "storm", "breakdown", "encounter"
  durationGameHours: decimal        # How long the interruption lasted (0 if unresolved)
  resolved: boolean                 # Whether travel resumed
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
  totalGameHours: decimal           # Estimated total travel time in game-hours
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
    passengerCapacity: integer
    cargoCapacityKg: decimal
    compatibleTerrainTypes: [string]
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
    passengerCapacity: integer      # Optional
    cargoCapacityKg: decimal        # Optional
    compatibleTerrainTypes: [string] # Optional
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
    seasonalAvailability: object    # Optional
    baseRiskLevel: decimal
    riskDescription: string         # Optional
    name: string                    # Optional
    code: string                    # Optional
    tags: [string]                  # Optional
  response: { connection: TransitConnection }
  errors:
    - LOCATIONS_NOT_FOUND           # One or both locations don't exist
    - CONNECTION_ALREADY_EXISTS     # Duplicate from→to connection
    - SAME_LOCATION                 # from and to are the same
    - INVALID_MODE_CODE             # Referenced mode doesn't exist

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
    realmId: uuid                   # Optional - connections within this realm
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
    seasonalAvailability: object    # Optional
    baseRiskLevel: decimal          # Optional
    riskDescription: string         # Optional
    name: string                    # Optional
    code: string                    # Optional
    tags: [string]                  # Optional
  response: { connection: TransitConnection }
  errors:
    - CONNECTION_NOT_FOUND

/transit/connection/update-status:
  access: authenticated
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
  emits: transit.connection.status_changed
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
    realmId: uuid
    connections: [
      {
        fromLocationCode: string    # Location codes (resolved via Location service)
        toLocationCode: string
        bidirectional: boolean
        distanceKm: decimal
        terrainType: string
        compatibleModes: [string]
        seasonalAvailability: object
        baseRiskLevel: decimal
        name: string
        code: string
        tags: [string]
      }
    ]
    replaceExisting: boolean        # If true, delete existing connections for this realm first
  response: { created: integer, updated: integer, errors: [string] }
  notes: |
    Two-pass resolution: first pass creates connections with location code lookup,
    second pass validates all mode codes exist. Follows Location's bulk-seed pattern.
```

### Journey Lifecycle

```yaml
/transit/journey/create:
  access: authenticated
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
  notes: |
    Internally calls route/calculate to find the best path and populate legs.
    Checks mode compatibility via check-availability.
    Does NOT start the journey -- call /transit/journey/depart for that.

    When preferMultiModal is true, each leg gets the fastest compatible mode
    the entity can use on that connection. The journey's primaryModeCode is
    set to the mode used for the most legs (plurality). Proficiency bonuses
    are computed per-leg based on each leg's mode.

/transit/journey/depart:
  access: authenticated
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
  access: authenticated
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
  access: authenticated
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
  emits: transit.journey.waypoint_reached   # If more legs remain
  emits: transit.journey.arrived             # If this was the last leg
  errors:
    - JOURNEY_NOT_FOUND
    - INVALID_STATUS                # Must be "in_transit"
    - NO_CURRENT_LEG                # Already at final destination
  notes: |
    If this completes the final leg, journey status transitions to "arrived"
    and transit.journey.arrived is emitted instead of waypoint_reached.
    The arrivedAtGameTime is recorded for historical accuracy.

/transit/journey/arrive:
  access: authenticated
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
  access: authenticated
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
  access: authenticated
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

/transit/journey/list:
  access: user
  description: "List journeys for an entity or within a realm"
  request:
    entityId: uuid                  # Optional - journeys for this entity
    entityType: string              # Optional - filter by entity type
    realmId: uuid                   # Optional - journeys within this realm
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
    realmId: uuid                   # Optional
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
    - "fastest": cost = estimated_hours (distance / mode_speed per leg)
    - "safest": cost = cumulative_risk
    - "shortest": cost = distance_km

    Multiple connections between the same location pair (parallel edges) are
    supported. Route calculation evaluates each connection independently,
    choosing the best per mode. E.g., a road and a river between two towns
    are separate connections with different mode compatibility.

    When preferMultiModal is true, each leg selects the fastest compatible mode
    the entity can use. Route options include per-leg mode codes in legModes[].
    Proficiency bonuses (from Seed integration) are applied per-leg per-mode.
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
    - Item requirements: calls lib-inventory to check if entity has required item tag (L2 hard)
    - Species requirements: calls lib-character or lib-species for species code (L2 hard)
    - Proficiency: queries lib-seed for seed growth level (L2 hard) when mode has
      proficiencySeedTypeCode set. Proficiency bonus applied to effectiveSpeed.

    Also applies ITransitCostModifierProvider enrichment to compute preferenceCost.
    This is the endpoint the variable provider calls internally.
```

### Connection Discovery

```yaml
/transit/discovery/reveal:
  access: authenticated
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
    realmId: uuid                   # Optional - filter by realm
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

**Total endpoints: 28**

---

## Events

```yaml
# Published via lib-messaging (IMessageBus)

# Connection events
transit.connection.status_changed:
  description: "A connection's operational status changed"
  payload:
    connectionId: uuid
    fromLocationId: uuid
    toLocationId: uuid
    previousStatus: string
    newStatus: string
    reason: string
    forceUpdated: boolean         # Whether this was a forceUpdate override
    realmId: uuid
  consumers:
    - Trade (L4): recalculates trade route viability
    - Actor (L2): NPCs may need to replan travel
    - Puppetmaster (L4): regional watchers notice road closures
    - Quest (L2): connection-state objectives ("clear the blocked road")

# Journey lifecycle events
transit.journey.departed:
  description: "An entity has begun traveling"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    originLocationId: uuid
    destinationLocationId: uuid
    primaryModeCode: string
    estimatedArrivalGameTime: decimal
    partySize: integer
    realmId: uuid
  consumers:
    - Trade (L4): tracks shipment progress
    - Analytics (L4): travel pattern aggregation
    - Puppetmaster (L4): regional watchers notice travelers
    - Character History (L4): significant journeys become backstory elements
    - Quest (L2): travel departure objectives ("leave the village")

transit.journey.waypoint_reached:
  description: "A traveling entity reached an intermediate waypoint"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    waypointLocationId: uuid
    nextLocationId: uuid
    legIndex: integer
    remainingLegs: integer
    connectionId: uuid           # Connection of the leg just completed
    realmId: uuid
  consumers:
    - Trade (L4): triggers border crossing checks at checkpoints
    - Location (L2): presence tracking updates
    - Quest (L2): waypoint-based objectives ("pass through Riverside")

transit.journey.arrived:
  description: "A traveling entity reached its destination"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    originLocationId: uuid
    destinationLocationId: uuid
    primaryModeCode: string
    totalGameHours: decimal
    totalDistanceKm: decimal
    interruptionCount: integer
    legsCompleted: integer
    realmId: uuid
  consumers:
    - Trade (L4): completes shipment delivery
    - Location (L2): presence tracking updates
    - Analytics (L4): travel time statistics
    - Character History (L4): significant journey completion for backstory
    - Quest (L2): travel arrival objectives ("reach the Iron Mines")
    - Seed (L2): travel proficiency growth (if mode has proficiencySeedTypeCode)

transit.journey.interrupted:
  description: "A journey was interrupted (combat, event, breakdown)"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    currentLocationId: uuid
    currentLegIndex: integer
    reason: string
    realmId: uuid
  consumers:
    - Trade (L4): shipment delay notification
    - Puppetmaster (L4): may orchestrate rescue/encounter
    - Analytics (L4): interruption hotspot tracking

transit.journey.resumed:
  description: "An interrupted journey was resumed"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    currentLocationId: uuid
    destinationLocationId: uuid
    currentLegIndex: integer
    remainingLegs: integer
    modeCode: string
    realmId: uuid
  consumers:
    - Trade (L4): shipment back in transit
    - Analytics (L4): interruption duration statistics

transit.journey.abandoned:
  description: "A journey was abandoned before reaching destination"
  payload:
    journeyId: uuid
    entityId: uuid
    entityType: string
    originLocationId: uuid
    destinationLocationId: uuid
    abandonedAtLocationId: uuid
    reason: string
    completedLegs: integer
    totalLegs: integer
    realmId: uuid
  consumers:
    - Trade (L4): handles stranded shipments
    - Character History (L4): significant travel failure for backstory
    - Analytics (L4): abandonment pattern aggregation

# Discovery events
transit.discovery.revealed:
  description: "A discoverable connection was revealed to an entity"
  payload:
    entityId: uuid
    connectionId: uuid
    fromLocationId: uuid
    toLocationId: uuid
    source: string              # "travel", "guide", "hearsay", "map", "quest_reward"
    realmId: uuid
  consumers:
    - Collection (L2): route discovery → collection unlocks (e.g., "Explorer's Atlas")
    - Quest (L2): travel-based objectives ("discover the hidden pass")
    - Analytics (L4): discovery pattern tracking
```

### Lifecycle Events

```yaml
# Auto-generated via x-lifecycle in events schema
transit.mode.registered:
  description: "A new transit mode was registered"
  payload:
    code: string
    name: string

transit.mode.deprecated:
  description: "A transit mode was deprecated"
  payload:
    code: string
    reason: string

transit.connection.created:
  description: "A new connection was created between locations"
  payload:
    connectionId: uuid
    fromLocationId: uuid
    toLocationId: uuid
    realmId: uuid

transit.connection.deleted:
  description: "A connection was removed"
  payload:
    connectionId: uuid
    fromLocationId: uuid
    toLocationId: uuid
    realmId: uuid
```

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
      description: "Cargo weight above this threshold reduces speed. Speed reduction = (cargo - threshold) / (capacity - threshold) * CargoSpeedPenaltyMaxRate"
      env: TRANSIT_CARGO_SPEED_PENALTY_THRESHOLD_KG

    CargoSpeedPenaltyMaxRate:
      type: number
      default: 0.3
      description: "Maximum speed reduction rate from cargo weight (0.3 = 30% speed reduction at full capacity)"
      env: TRANSIT_CARGO_SPEED_PENALTY_MAX_RATE

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

    DefaultProficiencyMaxBonus:
      type: number
      default: 0.3
      description: "Default value for maxProficiencyBonus on modes when proficiencySeedTypeCode is set but maxProficiencyBonus is not explicitly configured"
      env: TRANSIT_DEFAULT_PROFICIENCY_MAX_BONUS
```

---

## State Stores

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
    - realmId
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
    - realmId
    - status
    - originLocationId
    - destinationLocationId

transit-connection-graph:
  backend: redis
  description: "Cached connection graph for route calculation"
  key_format: "transit:graph:{realmId}"
  ttl_source: config.ConnectionGraphCacheSeconds
  notes: |
    Serialized adjacency list for Dijkstra's algorithm.
    Rebuilt from MySQL on cache miss.
    Invalidated when connections are created/deleted/status-changed.

transit-discovery:
  backend: redis
  description: "Per-entity connection discovery tracking"
  key_format: "transit:discovery:{entityId}"
  ttl_source: none (permanent until entity is deleted)
  notes: |
    Redis set per entity containing connection IDs they have discovered.
    Checked during route calculation when entityId is provided to filter
    discoverable connections. Written by discovery/reveal endpoint and
    automatically on journey leg completion for discoverable connections.
    Cleaned up via Resource CASCADE when the entity is deleted.
```

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
  notes: "Accounts for cargo weight penalty and Seed proficiency bonus if applicable"

${transit.mode.<code>.proficiency}:
  type: decimal
  description: "Proficiency bonus from Seed growth (0.0 = novice, up to maxProficiencyBonus)"
  example: "${transit.mode.horseback.proficiency}"
  notes: "Only available when mode has proficiencySeedTypeCode set. Queries Seed service."

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

## Integration Points

### Location (L2) -- Hard Dependency

Transit connections reference Location entities by ID. On startup, Transit validates that referenced locations exist. Location deletion is handled via Resource cleanup callbacks (see Resource integration below), not event subscriptions.

Location knows nothing about Transit (correct hierarchy direction).

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

### Seed (L2) -- Hard Dependency

Transit queries Seed for proficiency bonuses when a mode has `proficiencySeedTypeCode` set:
- **Speed bonus**: `effectiveSpeed = baseSpeed × (1 + proficiencyBonus)` where bonus is derived from Seed growth level
- **Journey arrival**: Publishes `transit.journey.arrived` which Seed consumes to record travel experience growth
- This is a direct L2-to-L2 dependency, not a DI provider pattern

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
- Transit does NOT subscribe to `location.deleted` or `character.deleted` events for cleanup -- this is handled exclusively through the Resource cleanup callback pattern per T28.

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
    - transit.connection.status_changed (for each connection that changes)
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
```

---

## Scale Considerations

### Connection Graph Size

For a realm with 500 locations and an average of 4 connections per location, the graph has ~2000 edges. Dijkstra's algorithm on this graph completes in microseconds. Even with 10,000 locations and 40,000 connections, route calculation remains sub-millisecond.

The connection graph is cached in Redis as a serialized adjacency list, rebuilt on cache miss from MySQL. Cache invalidation occurs on connection create/delete/status-change.

### Journey Volume

With 100,000+ concurrent NPCs, a significant fraction may be traveling at any time. If 10% are in transit, that's 10,000 active journeys in Redis. Each journey is a small JSON document (~1KB). Total Redis footprint: ~10MB -- negligible.

Journey events (departed, waypoint, arrived) flow through the standard lib-messaging pipeline. At 10,000 journeys with an average of 3 events per journey per game-day, that's ~30,000 events per game-day, or roughly 1 event every 3 seconds at 24:1 time ratio. Well within RabbitMQ capacity.

### Mode Compatibility Checks

`check-availability` calls may need to query lib-inventory (for item requirements) and lib-character (for species). These are L2 service calls that go through lib-mesh. To avoid per-NPC overhead, the variable provider caches mode availability per entity with a TTL that invalidates on inventory change or character update events.

---

## Work Tracking

### Potential Extensions

1. **Caravan formations**: Multiple entities traveling together as a group. Group speed = slowest member. Group risk = reduced (safety in numbers). Caravan as an entity type for journeys, with member tracking and formation bonuses. Faction merchant guilds could manage NPC caravans.

2. **Terrain speed modifiers**: Rather than a single speed per mode, modes could have per-terrain speed multipliers: `horseback.speed.road = 30, horseback.speed.trail = 20, horseback.speed.forest = 10`. This adds granularity to route calculation without changing the core architecture.

3. **Fatigue and rest**: Long journeys accumulate fatigue. After a configurable threshold, the entity must rest (journey auto-pauses at next waypoint). Rest duration depends on mode and entity stamina. Creates natural stopping points at inns and camps.

4. **Weather-reactive connections**: Environment (L4) automatically calls `/transit/connection/update-status` when weather conditions make connections impassable. Storms close mountain passes; floods close river crossings; drought closes river boat routes. This is a pure consumer relationship -- Transit exposes the API, Environment calls it.

5. **Mount bonding**: Integration with Relationship (L2) for character-mount relationships. A well-bonded mount has higher effective speed and lower fatigue. Bond strength grows with travel distance. Follows the existing Relationship entity-to-entity model.

### Resolved Design Decisions

1. **Connection granularity**: Allow arbitrary connections but tag them appropriately (`tags: ["portal"]`, `tags: ["trade_route"]`). Enables teleportation portals and long-distance shipping routes alongside physically meaningful roads.

2. **Journey auto-progression**: No. This violates the declarative lifecycle pattern. The game/Actor drives progression. Transit records and calculates but doesn't simulate.

3. **Intra-location transit**: No. Intra-location movement is Mapping's domain (spatial positioning) or the game client's domain (local navigation). Transit handles inter-location movement only.

4. **Multi-mode journeys**: Yes, core feature. Each leg specifies a `modeCode` that can override the journey's `primaryModeCode`. `preferMultiModal` flag on create/calculate selects best mode per leg automatically. Proficiency bonuses apply per-leg per-mode.

5. **Reverse connections**: Bidirectional connections share properties. For asymmetric connections (upstream/downstream), create two one-way connections with different speeds. Keeps the data model simple.

6. **Transit and Mapping relationship**: Complementary, not overlapping. Mapping is continuous spatial data ("where exactly am I"), Transit is discrete connectivity ("how do I get from town A to town B"). No integration needed beyond both referencing Location entities.

7. **Cross-realm connections**: Not supported. All connections are realm-scoped. Cross-realm travel (if it ever exists) would be modeled as a destination within the originating realm. NPCs and transit never span realms.

8. **Connection discovery**: Core feature. Connections have `discoverable: boolean` flag. Per-entity discovery tracking in Redis. Route calculation filters by discovery status. Discovery via travel, explicit grant, or Hearsay integration.

9. **Seed proficiency**: Core feature. Modes optionally reference `proficiencySeedTypeCode`. Entity's Seed growth level modifies effective speed. Connects Transit to the content flywheel.

10. **Status transition concurrency**: Optimistic concurrency pattern with `currentStatus`/`newStatus` and `forceUpdate` bypass. Prevents silent status conflicts between independent actors while supporting administrative overrides.

---

*This document is an aspirational deep dive for a service that does not yet exist. No schema files or implementation code have been created. The design follows established Bannou patterns and respects the service hierarchy. Implementation requires schemas, code generation, service implementation, and testing -- in that order.*
