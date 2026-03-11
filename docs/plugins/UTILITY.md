# Utility Plugin Deep Dive

> **Plugin**: lib-utility (not yet created)
> **Schema**: `schemas/utility-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: utility-networks (MySQL), utility-coverage (Redis), utility-sources (MySQL), utility-maintenance (MySQL), utility-locks (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Short**: Infrastructure network topology, flow calculation, and coverage cascading (aqueducts, power grids)

---

## Overview

The Utility service (L4 GameFeatures) manages infrastructure networks that continuously distribute resources across location hierarchies. It provides the topology, capacity modeling, and flow calculation that transforms Workshop point-production into location-wide service coverage -- answering "does this location have water, and where does it come from?" Where Workshop produces resources at a single point and Trade moves discrete shipments between locations, Utility models **continuous flow through persistent infrastructure** (aqueducts, sewer systems, power grids, magical conduits, messenger networks). The key gameplay consequence: when infrastructure breaks, downstream locations lose service, and the cascade of discovery, investigation, and repair creates emergent content. Internal-only, never internet-facing.

---

## Design Philosophy

**What Utility IS**: Infrastructure network topology, continuous flow calculation, service coverage per location, capacity constraints, failure cascading, maintenance lifecycle.

**What Utility is NOT**: A production system (that's Workshop), a goods transport system (that's Trade), a movement system (that's Transit), a regulatory authority (that's Faction), an infrastructure operator (that's Organization). Utility provides the network graph and flow mechanics that these services compose around.

### Infrastructure as Network, Not Attribute

The critical design decision: utility coverage is a **graph computation**, not a **location attribute**.

A Seed domain on Location (`infrastructure.water = 5.0`) would model coverage as a property of the location itself. This creates a perfect-knowledge system where every NPC immediately knows the coverage level. When supply drops, the Seed value drops, and every NPC in the location simultaneously adjusts behavior -- there is no discovery, no investigation, no "who noticed first?"

A network graph creates **information asymmetry**:

```
Mountain Spring (Workshop produces 100 water/gh)
       │
       ▼
   Aqueduct A (capacity: 80, condition: 0.95)
       │
       ├──► Reservoir at Hilltop (80 water/gh arrives)
       │         │
       │         ├──► Pipe B to Market District (capacity: 50, condition: 0.7)
       │         │         └──► Market District receives 35 water/gh
       │         │
       │         └──► Pipe C to Temple District (capacity: 40, condition: 1.0)
       │                   └──► Temple District receives 40 water/gh
       │
       └──► (remaining 20 water/gh: production exceeds aqueduct capacity)

EVENT: Pipe B condition drops to 0.0 (earthquake, sabotage, decay)
  → Market District: water stops flowing
  → Temple District: unaffected (separate branch)
  → Reservoir: water backs up (surplus increases)
  → NPCs in Market District: "why is there no water?"
  → Investigation: someone walks the pipe route, discovers the break
  → Repair: Organization sends workers, Workshop-like time to fix
  → Meanwhile: Market District NPCs haul water from Temple District manually
  → Meanwhile: Faction governor redirects policy, raises emergency taxes
  → Meanwhile: God actor (Puppetmaster) notices infrastructure crisis → narrative event
```

None of this emerges from a Seed attribute. The network topology IS the content generator.

### Declarative Infrastructure Lifecycle

Like Trade and Transit, Utility follows the Bannou pattern: the **game drives events**, the **plugin records state**. Utility does not automatically build infrastructure, repair damage, or upgrade capacity. The game server (or NPC Actor brains via Organization) calls the APIs to indicate what happened.

- `/utility/connection/create` is called when infrastructure is built (by NPC workers, player-directed construction, or seeded world state)
- `/utility/connection/update-condition` is called when damage occurs (earthquake event, sabotage, decay timer)
- `/utility/connection/repair` is called when workers fix infrastructure
- Coverage recalculation happens automatically when network topology or production sources change

The exception is background workers for **periodic computation** (condition decay, coverage snapshot caching, maintenance alerting).

### Flow Is Computed, Not Simulated

Utility does NOT simulate fluid dynamics or electrical circuits. Flow is a **graph traversal computation**:

1. Identify all production sources (Workshop tasks producing the utility's resource type at locations on the network)
2. Traverse the network graph from sources outward
3. At each connection: `flow = min(upstream_available, connection_capacity × condition)`
4. At each junction (location with multiple outbound connections): distribute proportionally or by priority
5. Result: each location on the network has a computed `serviceLevelRate` (units per game-hour arriving)

This computation runs on-demand (when queried) with caching, and is invalidated when network topology, connection conditions, or production sources change. It does NOT tick every game-second.

### Three-Tier Usage

Like Trade, Utility serves three usage patterns through the same APIs:

```
┌─────────────────────────────────────────────────────────────────┐
│ Tier 1: Divine/System Oversight                                  │
│ God actors and system processes with full network visibility      │
│ Use: Infrastructure crisis detection, narrative intervention     │
│ Example: Poseidon detects drought + aqueduct failure → quest    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 2: NPC Governance                                           │
│ Governors, engineers, organization managers                      │
│ Use: Bounded-rational infrastructure decisions                   │
│ Example: Guild engineer inspects pipe, reports condition         │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 3: External Management                                      │
│ Admin dashboards, monitoring                                     │
│ Use: "Which locations have degraded water service?"              │
│ Example: Admin views infrastructure health map                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Architecture

### Orchestration Over Primitives

```
┌─────────────────────────────────────────────────────────────────┐
│                       UTILITY (L4)                                │
│  Network Types • Connections • Flow Calc • Coverage • Decay      │
│  Failure Cascade • Maintenance Alerts • Variable Provider        │
└────────┬──────────┬──────────┬──────────────────────────────────┘
         │          │          │
    uses │     uses │     uses │
         ▼          ▼          ▼
┌────────────┐ ┌────────────┐ ┌──────────┐
│  Location  │ │ Worldstate │ │ Resource │  ← L1/L2 Foundation (hard)
│  (L2)      │ │ (L2)       │ │ (L1)     │
│  network   │ │ game clock │ │ cleanup  │
│  nodes     │ │ season     │ │ refs     │
│  hierarchy │ │ decay time │ │          │
└────────────┘ └────────────┘ └──────────┘
         │
    optional │ (soft — graceful degradation if unavailable)
         ▼
┌────────────┐ ┌────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│ Workshop   │ │Faction │ │Environm. │ │  Trade   │ │Organizat.│  ← L4 Peers
│  (L4)      │ │ (L4)   │ │ (L4)     │ │ (L4)     │ │ (L4)     │
│  source    │ │jurisd. │ │weather   │ │supply    │ │ infra.   │
│  production│ │norms   │ │damage    │ │demand    │ │ operators│
│  rates     │ │        │ │modifier  │ │signals   │ │          │
└────────────┘ └────────┘ └──────────┘ └──────────┘ └──────────┘
```

### The Cascade Emergence

Utility doesn't script emergent events -- it provides the topology that makes them inevitable:

```
INITIAL STATE:
  Water Plant A (Workshop) → Aqueduct → Town Reservoir → 3 district pipes
  All connections condition 1.0, all districts at full coverage

DAY 14: Earthquake event (Environment or game server)
  → Game calls /utility/connection/update-condition
    { connectionId: "pipe-to-market", condition: 0.0 }
  → Utility recalculates coverage:
    Market District: 35 water/gh → 0 water/gh
  → Publishes: utility.coverage.degraded
    { locationId: "market-district", networkType: "water",
      previousRate: 35.0, currentRate: 0.0, cause: "connection_failure" }

DOWNSTREAM EFFECTS (driven by OTHER services, not Utility):
  → Environment: incorporates coverage into local conditions
  → Actor NPCs in Market District: ${utility.water.coverage} = 0.0
    GOAP plans shift: "find alternative water source" becomes top goal
  → Faction governor NPC: perceives utility.coverage.degraded event
    GOAP plans: "dispatch engineers", "declare emergency", "raise taxes for repair"
  → Organization (water guild): perceives event
    Dispatches workers (Workshop repair task on the connection)
  → Puppetmaster god-actor: perceives infrastructure crisis
    Evaluates narrative potential → spawns "The Great Drought" quest
  → Market merchants: adjust pricing (water becomes valuable commodity)
  → Trade: supply/demand snapshot shows water scarcity at Market District

REPAIR (driven by Organization workers + Workshop):
  → Workers arrive, begin repair (Workshop task with connection as target)
  → Game calls /utility/connection/update-condition
    { connectionId: "pipe-to-market", condition: 0.6 }  (partial repair)
  → Utility recalculates: Market District now receives 21 water/gh (reduced but functional)
  → Publishes: utility.coverage.restored (partial)
  → NPCs adjust: "water is back but rationed" -- different behavioral state than zero

FULL REPAIR:
  → condition: 1.0 → Market District back to 35 water/gh
  → utility.coverage.restored (full)
  → Economy normalizes, emergency taxes lifted, quest resolves
```

---

## Data Models

### Network Type

Network types are opaque string codes (like seed types, collection types). New utility types require zero schema changes.

```yaml
UtilityNetworkType:
  id: uuid

  # Identification
  code: string                      # "water", "power", "sewer", "magical_conduit", "messenger"
  name: string                      # "Water Supply Network"
  description: string

  # Scope
  gameServiceId: uuid               # Game this network type belongs to
  realmId: uuid?                    # null = all realms, or realm-specific

  # Flow configuration
  defaultCapacityUnitsPerGameHour: decimal   # Default capacity for new connections
  flowLossPerKm: decimal            # Loss rate per km of connection (leakage, resistance)
  conditionFlowMultiplier: boolean  # If true, flow = capacity × condition; if false, binary (works or doesn't)

  # Decay configuration
  baseDecayRatePerGameDay: decimal  # How fast condition degrades without maintenance (0.0 = no decay)
  minimumConditionBeforeFailure: decimal  # Below this, connection is non-functional (default 0.1)

  # Display
  connectionNoun: string            # "pipe", "cable", "conduit", "road" (for log messages/events)
  resourceNoun: string              # "water", "power", "sewage", "mana" (for log messages/events)

  # Deprecation (Category B per IMPLEMENTATION TENETS — instances persist independently)
  isDeprecated: boolean             # default: false
  deprecatedAt: timestamp?          # When deprecated (null if not deprecated)
  deprecationReason: string?        # Why deprecated (maxLength: 500, nullable)

  createdAt: timestamp
  modifiedAt: timestamp
```

### Connection

A connection represents a single piece of infrastructure linking two locations within a network.

```yaml
UtilityConnection:
  id: uuid

  # Network membership
  networkTypeCode: string           # Which network type this connection belongs to

  # Endpoints
  fromLocationId: uuid              # Upstream location (closer to production source)
  toLocationId: uuid                # Downstream location (closer to consumers)
  bidirectional: boolean            # Can flow go both ways? (power grids yes, sewer no)

  # Capacity
  capacityUnitsPerGameHour: decimal # Max flow through this connection
  distanceKm: decimal               # Physical distance (affects loss calculation)

  # Condition (0.0 = destroyed, 1.0 = perfect)
  condition: decimal                # Current structural condition
  lastMaintenanceAt: timestamp?     # When last maintained (for decay calculation)

  # Ownership
  ownerId: uuid?                    # Organization or faction that owns this infrastructure
  ownerType: EntityType?            # $ref: EntityType from common-api.yaml (Organization, Faction, Realm, System)

  # Status
  status: ConnectionStatus          # ConnectionStatus enum: Active, Disabled, UnderConstruction, Demolished
  disabledReason: string?           # Game-configurable reason text (type classification pending #632)

  # Metadata
  terrainType: string?              # Game-configurable terrain classification (underground, surface, elevated, submerged)
  constructionCost: decimal?        # Original build cost (for repair cost estimation)
  tags: [string]                    # Caller-provided classification tags for filtering and display. The service stores and returns these values but does not use them in flow calculations or business logic.

  createdAt: timestamp
  modifiedAt: timestamp
```

### Coverage Snapshot

Computed and cached per-location coverage for each network type. Invalidated when topology or production changes.

```yaml
UtilityCoverageSnapshot:
  locationId: uuid
  networkTypeCode: string

  # Computed flow data
  serviceLevelRate: decimal          # Units per game-hour arriving at this location
  demandRate: decimal?               # Expected consumption rate (from Trade supply/demand or config)
  coverageRatio: decimal             # serviceLevelRate / demandRate (>1.0 = surplus, <1.0 = deficit)

  # Source tracing
  primarySourceLocationId: uuid?     # Where most of the supply originates
  primarySourceConnectionId: uuid?   # First connection in the supply chain
  pathLength: integer                # Hops from nearest production source
  totalLossPercent: decimal          # Cumulative loss across the supply chain

  # Status classification
  coverageStatus: CoverageStatus     # CoverageStatus enum: Full, Partial, Critical, None
  # Full: coverageRatio >= 1.0
  # Partial: 0.5 <= coverageRatio < 1.0
  # Critical: 0.0 < coverageRatio < 0.5
  # None: coverageRatio == 0.0

  computedAt: timestamp
  invalidatedAt: timestamp?          # When topology change invalidated this snapshot
```

### Production Source Registration

Links Workshop production to the utility network. When a Workshop task produces a resource matching a utility network type at a location on the network, it becomes a source.

```yaml
UtilityProductionSource:
  id: uuid

  # Network binding
  networkTypeCode: string           # Which utility network this source feeds

  # Location
  locationId: uuid                  # Where production happens (must be on the network)

  # Production reference
  workshopTaskId: uuid?             # Optional link to Workshop task (for rate queries). Workshop is L4 soft dependency — when unavailable, Workshop-linked sources report zero production rate.
  manualRateOverride: decimal?      # If no Workshop task, manually specified production rate. Only source of production data when Workshop is unavailable.

  # Computed
  currentProductionRate: decimal    # Units per game-hour (from Workshop or manual override)

  # Ownership
  ownerId: uuid?
  ownerType: EntityType?            # $ref: EntityType from common-api.yaml

  status: ProductionSourceStatus    # ProductionSourceStatus enum: Active, Paused, Depleted
  createdAt: timestamp
  modifiedAt: timestamp
```

### Maintenance Request

Tracks infrastructure repair/upgrade work orders. Utility records the request; Organization/Workshop execute the work.

```yaml
UtilityMaintenanceRequest:
  id: uuid

  # Target
  connectionId: uuid                # Which connection needs work
  networkTypeCode: string

  # Type
  maintenanceType: MaintenanceType  # MaintenanceType enum: Repair, UpgradeCapacity, UpgradeCondition, Demolish

  # Current state
  currentCondition: decimal         # Condition when request was filed
  targetCondition: decimal?         # Desired condition after repair (null = full repair to 1.0)
  targetCapacity: decimal?          # For upgrade_capacity requests

  # Assignment
  assignedOrganizationId: uuid?     # Which organization is handling this
  assignedAt: timestamp?
  estimatedCompletionAt: timestamp? # Game-time estimate

  # Status
  status: MaintenanceRequestStatus  # MaintenanceRequestStatus enum: Open, Assigned, InProgress, Completed, Cancelled
  completedAt: timestamp?

  createdAt: timestamp
  modifiedAt: timestamp
```

---

## Dependencies (What This Plugin Relies On)

| Dependency | Type | Usage |
|------------|------|-------|
| lib-state (IStateStoreFactory) | L0 Hard | Persistence for network types, connections, coverage snapshots, sources, maintenance requests |
| lib-messaging (IMessageBus) | L0 Hard | Publishing coverage events, failure alerts, maintenance events |
| lib-resource (IResourceClient) | L1 Hard | Reference tracking and cleanup callback registration for location references on connections and sources |
| lib-location (ILocationClient) | L2 Hard | Validating connection endpoints are real locations, querying location hierarchy for coverage propagation |
| lib-worldstate (IWorldstateClient) | L2 Hard | Game clock for decay calculations, seasonal modifiers on flow rates |
| lib-workshop (IWorkshopClient) | L4 Soft | Querying production task rates at source locations. When Workshop is unavailable, only manually-registered sources with explicit `manualRateOverride` values contribute to flow calculation. Workshop-linked sources report zero production. Design decision: #628 |
| lib-faction (IFactionClient) | L4 Soft | Jurisdiction queries for regulatory checks, norm enforcement on infrastructure operators (graceful: no regulation if unavailable) |
| lib-organization (IOrganizationClient) | L4 Soft | Infrastructure operator queries, maintenance assignment routing (graceful: maintenance requests unassigned if unavailable) |
| lib-environment (IEnvironmentClient) | L4 Soft | Weather-based condition modifiers (storms damage infrastructure), seasonal demand modifiers (graceful: no weather effects if unavailable) |
| lib-trade (ITradeClient) | L4 Soft | Demand rate data from supply/demand snapshots for coverage ratio computation (graceful: demand unknown, coverage ratio not computed) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-environment (L4) | Consumes `utility.coverage.degraded`/`restored` events to adjust environmental conditions at affected locations (water scarcity, sanitation quality) |
| lib-actor (L2) | Consumes `${utility.*}` variables via Variable Provider Factory for NPC infrastructure-awareness in GOAP decisions |
| lib-faction (L4) | May consume coverage events to trigger governance responses (emergency declarations, policy changes) |
| lib-organization (L4) | Consumes maintenance request events to dispatch workers; listens to coverage events for service-level monitoring |
| lib-puppetmaster (L4) | God-actors perceive infrastructure crisis events for narrative orchestration |
| lib-trade (L4) | Supply/demand snapshots may incorporate utility coverage as a demand modifier |
| lib-gardener (L4) | Player experience orchestration may reference infrastructure state for scenario spawning |
| lib-obligation (L4) | Infrastructure service contracts create behavioral obligations for maintenance crews |

---

## State Storage

### Store: `utility-networks` (Backend: MySQL)

Network type definitions and connection topology. MySQL for queryable graph structure.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{code}` | `UtilityNetworkType` | Network type definitions |
| `type-index:{gameServiceId}` | `List<string>` | Network type codes per game service |
| `conn:{connectionId}` | `UtilityConnection` | Individual infrastructure connections |
| `conn-index:from:{locationId}:{networkTypeCode}` | `List<Guid>` | Outbound connections from a location |
| `conn-index:to:{locationId}:{networkTypeCode}` | `List<Guid>` | Inbound connections to a location |
| `conn-index:network:{networkTypeCode}` | `List<Guid>` | All connections in a network |
| `conn-index:owner:{ownerType}:{ownerId}` | `List<Guid>` | Connections owned by an entity |

### Store: `utility-coverage` (Backend: Redis)

Computed coverage snapshots. Redis for fast reads (NPCs query coverage frequently via variable provider).

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `coverage:{locationId}:{networkTypeCode}` | `UtilityCoverageSnapshot` | Cached coverage computation per location per network |
| `coverage-index:{networkTypeCode}` | `Set<string>` | All location IDs with computed coverage for a network type |
| `coverage-status:{networkTypeCode}:{status}` | `Set<string>` | Location IDs by coverage status (for "find all locations with no water" queries) |

### Store: `utility-sources` (Backend: MySQL)

Production source registrations linking Workshop output to utility networks.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `source:{sourceId}` | `UtilityProductionSource` | Production source registration |
| `source-index:{networkTypeCode}:{locationId}` | `List<Guid>` | Sources at a location for a network type |
| `source-index:network:{networkTypeCode}` | `List<Guid>` | All sources in a network |

### Store: `utility-maintenance` (Backend: MySQL)

Maintenance request tracking.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `maint:{requestId}` | `UtilityMaintenanceRequest` | Individual maintenance request |
| `maint-index:conn:{connectionId}` | `List<Guid>` | Maintenance requests for a connection |
| `maint-index:status:{status}` | `List<Guid>` | Requests by status |
| `maint-index:org:{organizationId}` | `List<Guid>` | Requests assigned to an organization |

### Store: `utility-locks` (Backend: Redis)

Distributed locks for topology mutations.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `lock:conn:{connectionId}` | Lock | Distributed lock for connection mutations |
| `lock:coverage:{networkTypeCode}` | Lock | Distributed lock for coverage recalculation |
| `lock:source:{sourceId}` | Lock | Distributed lock for source registration mutations |

**Schema requirement**: All 5 state stores (utility-networks, utility-coverage, utility-sources, utility-maintenance, utility-locks) must be added to `schemas/state-stores.yaml` when schemas are created. Backend assignments: utility-networks (MySQL), utility-coverage (Redis), utility-sources (MySQL), utility-maintenance (MySQL), utility-locks (Redis).

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `networkTypeCode` | B (Content Code) | Opaque string | Game-configurable utility network types (water, power, sewer, magical_conduit, messenger, etc.). New utility types require zero schema changes. Follows same extensibility pattern as seed types and collection types. |
| `ownerType` (on connection) | A (Entity Reference) | `$ref: EntityType` | Identifies the entity that owns a utility connection. All values (Organization, Faction, Realm, System) exist in EntityType from common-api.yaml. |
| `status` (on connection) | C (System State) | `ConnectionStatus` | Finite set: Active, Disabled, UnderConstruction, Demolished. System-owned; drives flow calculation inclusion/exclusion. |
| `coverageStatus` | C (System State) | `CoverageStatus` | Finite set: Full, Partial, Critical, None. System-owned; computed from coverage rate thresholds. Drives NPC awareness events. |
| `terrainType` (on connection) | B (Content Code) | Opaque string | Game-configurable terrain classifications for connections (underground, surface, elevated, submerged). |
| `maintenanceType` | C (System State) | `MaintenanceType` | Finite set: Repair, UpgradeCapacity, UpgradeCondition, Demolish. System-owned; each type has distinct processing logic. |
| `status` (on maintenance request) | C (System State) | `MaintenanceRequestStatus` | Finite set: Open, Assigned, InProgress, Completed, Cancelled. System-owned state machine. |
| `ownerType` (on source) | A (Entity Reference) | `$ref: EntityType` | Identifies the entity that owns a production source. Same EntityType as connection ownerType. |
| `status` (on source) | C (System State) | `ProductionSourceStatus` | Finite set: Active, Paused, Depleted. System-owned; drives production rate contribution to flow calculation. |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `utility.network-type.created` | `UtilityNetworkTypeCreatedEvent` | New network type registered |
| `utility.network-type.updated` | `UtilityNetworkTypeUpdatedEvent` | Network type configuration changed |
| `utility.connection.created` | `UtilityConnectionCreatedEvent` | New infrastructure connection built |
| `utility.connection.updated` | `UtilityConnectionUpdatedEvent` | Connection properties changed (capacity, status) |
| `utility.connection.condition-changed` | `UtilityConnectionConditionChangedEvent` | Connection condition changed (damage, repair, decay). Includes `previousCondition`, `newCondition`, `cause`. |
| `utility.connection.failed` | `UtilityConnectionFailedEvent` | Connection condition dropped below failure threshold. The "pipe broke" event. |
| `utility.connection.restored` | `UtilityConnectionRestoredEvent` | Connection condition rose above failure threshold after being failed. |
| `utility.connection.deleted` | `UtilityConnectionDeletedEvent` | Infrastructure demolished or removed |
| `utility.coverage.degraded` | `UtilityCoverageDegradedEvent` | Location's service level dropped. Includes `locationId`, `networkTypeCode`, `previousRate`, `currentRate`, `previousStatus`, `currentStatus`, `cause` (connection_failure, source_depleted, capacity_reduced). The primary event that triggers NPC awareness. |
| `utility.coverage.restored` | `UtilityCoverageRestoredEvent` | Location's service level improved. Same fields as degraded. |
| `utility.coverage.snapshot-updated` | `UtilityCoverageSnapshotUpdatedEvent` | Coverage snapshot recomputed (bulk, after topology change). Low-priority informational event. |
| `utility.source.registered` | `UtilitySourceRegisteredEvent` | Production source added to network |
| `utility.source.deregistered` | `UtilitySourceDeregisteredEvent` | Production source removed from network |
| `utility.maintenance.requested` | `UtilityMaintenanceRequestedEvent` | New maintenance work order filed |
| `utility.maintenance.completed` | `UtilityMaintenanceCompletedEvent` | Maintenance work completed |
| `utility.maintenance.cancelled` | `UtilityMaintenanceCancelledEvent` | Maintenance request auto-expired or manually cancelled |

**Schema requirements**: The eventual `utility-events.yaml` must declare `x-lifecycle` for all four entity types (NetworkType, Connection, ProductionSource, MaintenanceRequest) with `topic_prefix: utility` to generate Pattern C topics. Without `topic_prefix`, entities like `UtilityConnection` would produce Pattern B topics (`utility-connection.created`) which are forbidden per QUALITY TENETS naming conventions. NetworkType x-lifecycle must include `deprecation: true` for Category B lifecycle event generation.

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `workshop.task.status-changed` | `HandleWorkshopTaskStatusChangedAsync` | When a Workshop task that's registered as a production source pauses/resumes/completes, update the source's production rate and trigger coverage recalculation |
| `worldstate.day-changed` | `HandleWorldstateDayChangedAsync` | Trigger maintenance request expiration check (auto-cancel unassigned requests older than MaintenanceRequestAutoExpireDays game-days) |
| `environment.conditions-changed` | `HandleEnvironmentConditionsChangedAsync` | Apply weather-based damage modifiers to connections in affected locations (storms, earthquakes) |

**Schema requirements**: All consumed events must be declared in `x-event-subscriptions` in the eventual `utility-events.yaml`. Event types referenced: `WorkshopTaskStatusChangedEvent` (from `workshop-events.yaml`), `WorldstateDayChangedEvent` (from `worldstate-events.yaml`), `EnvironmentConditionsChangedEvent` (from `environment-events.yaml`).

### Resource References (x-references)

Location cleanup is handled via lib-resource (per FOUNDATION TENETS Resource-Managed Cleanup), not event subscriptions.

| Source Field | Target Service | Target Entity | Policy | Cleanup Action |
|-------------|---------------|---------------|--------|----------------|
| `UtilityConnection.fromLocationId` | Location | location | CASCADE | Delete connection, trigger coverage recalculation for affected network |
| `UtilityConnection.toLocationId` | Location | location | CASCADE | Delete connection, trigger coverage recalculation for affected network |
| `UtilityProductionSource.locationId` | Location | location | CASCADE | Deregister source, trigger coverage recalculation for affected network |

Cleanup callbacks registered via generated `RegisterResourceCleanupCallbacksAsync()` at startup. CASCADE policy: when a Location is deleted, all connections to/from it and all sources at it are automatically removed, and coverage is recalculated for affected networks.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxNetworkTypesPerGameService` | `UTILITY_MAX_NETWORK_TYPES_PER_GAME_SERVICE` | `20` | Maximum distinct utility network types per game |
| `MaxConnectionsPerNetwork` | `UTILITY_MAX_CONNECTIONS_PER_NETWORK` | `10000` | Maximum connections in a single network (performance guard) |
| `MaxSourcesPerLocation` | `UTILITY_MAX_SOURCES_PER_LOCATION` | `10` | Maximum production sources at a single location per network type |
| `CoverageSnapshotTtlSeconds` | `UTILITY_COVERAGE_SNAPSHOT_TTL_SECONDS` | `300` | How long a coverage snapshot is valid before recomputation (5 minutes) |
| `CoverageRecalculationDebounceMs` | `UTILITY_COVERAGE_RECALCULATION_DEBOUNCE_MS` | `2000` | Debounce window for coverage recalculation after topology changes (prevents cascade of recalcs during bulk operations) |
| `ConditionDecayEnabled` | `UTILITY_CONDITION_DECAY_ENABLED` | `true` | Whether connections decay over time without maintenance |
| `ConditionDecayCheckIntervalMinutes` | `UTILITY_CONDITION_DECAY_CHECK_INTERVAL_MINUTES` | `60` | How often the decay worker runs |
| `FailureThreshold` | `UTILITY_FAILURE_THRESHOLD` | `0.1` | Connection condition below which it is considered non-functional (overridden by network type's `minimumConditionBeforeFailure`) |
| `MaxFlowCalculationDepth` | `UTILITY_MAX_FLOW_CALCULATION_DEPTH` | `50` | Maximum hops in a flow path calculation (prevents infinite loops in misconfigured bidirectional networks) |
| `MaintenanceRequestAutoExpireDays` | `UTILITY_MAINTENANCE_REQUEST_AUTO_EXPIRE_DAYS` | `30` | Unassigned maintenance requests auto-cancel after this many game-days |
| `CoverageEventThreshold` | `UTILITY_COVERAGE_EVENT_THRESHOLD` | `0.1` | Minimum coverage ratio change to publish a coverage event (prevents noise from tiny fluctuations) |
| `ConditionDecayWorkerStartupDelaySeconds` | `UTILITY_CONDITION_DECAY_WORKER_STARTUP_DELAY_SECONDS` | `60` | Delay before ConditionDecayWorker begins first execution after service startup |
| `MaintenanceExpirationWorkerStartupDelaySeconds` | `UTILITY_MAINTENANCE_EXPIRATION_WORKER_STARTUP_DELAY_SECONDS` | `120` | Delay before MaintenanceExpirationWorker begins first execution after service startup |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<UtilityService>` | Structured logging |
| `UtilityServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for all 5 stores |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Span creation for all async methods per IMPLEMENTATION TENETS |
| `IResourceClient` | Reference registration/unregistration for location references |
| `FlowCalculator` | Graph traversal engine for computing flow from sources through connections to locations. Receives ITelemetryProvider for span instrumentation. |
| `CoverageManager` | Manages coverage snapshot computation, caching, invalidation, and event publishing. Receives ITelemetryProvider for span instrumentation. |
| `ConditionDecayCalculator` | Computes condition decay based on elapsed game-time and network type decay rates. Receives ITelemetryProvider for span instrumentation. |
| `ILocationClient` | Location validation and hierarchy queries |
| `IWorldstateClient` | Game clock for decay and seasonal calculations |
| `IWorkshopClient` | Production rate queries for registered sources (soft L4 dependency, resolved via service provider) |

---

## API Endpoints (Implementation Notes)

### Network Type Management (5 endpoints)

- **Create**: Validates uniqueness of code within game service scope.
- **Update**: Triggers coverage recalculation if flow configuration properties changed.
- **List**: Returns network types with `includeDeprecated` parameter (default: false). When false, only non-deprecated types returned.
- **Deprecate**: `POST /utility/network-type/deprecate`. Sets `isDeprecated=true`, `deprecatedAt=now`, `deprecationReason=request.reason`. **Idempotent**: returns OK if already deprecated. Publishes `utility.network-type.updated` event with `changedFields` containing deprecation fields. One-way (Category B — no undeprecate).
- **Clean-Deprecated**: `POST /utility/network-type/clean-deprecated`. Uses shared `CleanDeprecatedRequest`/`CleanDeprecatedResponse` from common-api.yaml. Requires `x-permissions: [role: admin]`. Uses `DeprecationCleanupHelper.ExecuteCleanupSweepAsync`. Only permanently removes network types where zero connections and zero sources reference the `networkTypeCode`. Publishes `utility.network-type.deleted` event for each removed type. Service implements `ICleanDeprecatedEntity` marker interface.

**Category B lifecycle**: No per-entity delete endpoint. No undeprecate endpoint. Deprecation is one-way; permanent removal only via clean-deprecated sweep.

### Connection Management (7 endpoints)

- **Create**: Validates both location endpoints exist, validates no duplicate connection between same locations for same network type, rejects if `networkTypeCode` references a deprecated network type, initializes condition to 1.0, registers `fromLocationId` and `toLocationId` references with lib-resource, triggers coverage recalculation for all downstream locations.
- **Update**: Modifies capacity, status, ownership, metadata. Triggers coverage recalculation if capacity or status changed.
- **UpdateCondition**: The primary mutation endpoint for damage/repair. Accepts `condition` (0.0-1.0) and `cause` string. Publishes `condition-changed` event. If condition crosses the failure threshold in either direction, publishes `connection.failed` or `connection.restored`. Triggers downstream coverage recalculation.
- **Repair**: Convenience endpoint that increments condition by a specified amount (capped at 1.0), updates `lastMaintenanceAt`. Equivalent to `UpdateCondition` but with repair semantics.
- **Get/List/ListByLocation**: Standard read operations with filtering by network type, status, owner, and location.

### Production Source Management (4 endpoints)

- **Register**: Links a Workshop task (or manual rate) to a location on the utility network. Validates the location has at least one connection in the specified network. Rejects if `networkTypeCode` references a deprecated network type. Registers `locationId` reference with lib-resource. Triggers coverage recalculation.
- **Deregister**: Removes a production source. Unregisters `locationId` reference with lib-resource. Triggers coverage recalculation (downstream locations may lose coverage).
- **UpdateRate**: For manual-rate sources, updates the production rate. Workshop-linked sources auto-update via event subscription.
- **ListByNetwork**: Lists all production sources for a network type with current rates.

### Coverage Queries (5 endpoints)

- **GetCoverage**: Returns the coverage snapshot for a specific location and network type. Uses cached snapshot if valid; recomputes if invalidated or expired.
- **GetCoverageByNetwork**: Returns coverage for all locations in a network, filterable by coverage status. Paginated.
- **GetCoveragePath**: Traces the supply path from a location back to its production source(s), returning each connection and the flow at each hop. The "where does my water come from?" query.
- **GetNetworkHealth**: Aggregate health metrics for an entire network: total connections, average condition, failed count, total production, total coverage, locations at each status level.
- **CompareCoverage**: Given a hypothetical topology change (add/remove/modify a connection), returns the projected coverage impact without making the change. The "what if we build a pipe here?" planning tool.

### Maintenance Management (4 endpoints)

- **RequestMaintenance**: Files a maintenance work order for a connection. Validates the connection exists and the request type is appropriate (can't upgrade capacity on a demolished connection).
- **AssignMaintenance**: Assigns a maintenance request to an Organization. Publishes assignment event.
- **CompleteMaintenance**: Marks maintenance complete. For repair types, calls `UpdateCondition` with the target condition. For capacity upgrades, updates connection capacity. Publishes completion event.
- **ListMaintenance**: Lists maintenance requests filterable by connection, status, organization, network type. Paginated.

**Total: 25 planned endpoints.** (Network Type 5 + Connection 7 + Source 4 + Coverage 5 + Maintenance 4)

---

## Visual Aid: Flow Calculation Algorithm

```
FLOW CALCULATION (triggered by topology/source change):

Step 1: Identify sources                    Step 2: BFS from sources
┌─────────────────────┐                    ┌─────────────────────────────┐
│ For networkType "water":                 │ Queue: [(Spring, 100)]       │
│   Spring: 100 units/gh                   │                             │
│   Well:    20 units/gh                   │ Process Spring:             │
│                                          │   → Aqueduct (cap:80, cond:0.95)│
│ Total production: 120 units/gh           │     flow = min(100, 80×0.95) │
│                                          │          = 76 units/gh       │
│                                          │     loss = 76 × 0.01 × 5km  │
│                                          │          = 3.8 units/gh      │
└─────────────────────┘                    │     arriving = 72.2 units/gh │
                                           │   → Queue: [(Reservoir, 72.2)]│
                                           └─────────────────────────────┘

Step 3: Junction distribution               Step 4: Cache snapshots
┌─────────────────────────────┐            ┌─────────────────────────────┐
│ Process Reservoir (72.2 avail):          │ coverage:market:water       │
│   → Pipe B (cap:50, cond:0.7)           │   rate: 25.2, status: partial│
│     flow = min(72.2, 50×0.7)            │                             │
│          = 35 units/gh                   │ coverage:temple:water       │
│     loss = 35 × 0.01 × 2km = 0.7       │   rate: 36.5, status: full  │
│     → Market: 34.3 units/gh             │                             │
│                                          │ Publish events for any     │
│   Remaining: 72.2 - 35 = 37.2          │ locations whose status      │
│   → Pipe C (cap:40, cond:1.0)           │ changed since last snapshot │
│     flow = min(37.2, 40×1.0) = 37.2    │                             │
│     loss = 37.2 × 0.01 × 3km = 1.1     │                             │
│     → Temple: 36.1 units/gh             │                             │
└─────────────────────────────┘            └─────────────────────────────┘

JUNCTION DISTRIBUTION STRATEGY:
  If total downstream capacity > available:
    Distribute proportionally by capacity
    (Pipe B wants 35, Pipe C wants 40, total want 75, available 72.2)
    (Pipe B gets 72.2 × 35/75 = 33.7, Pipe C gets 72.2 × 40/75 = 38.5)
  If total downstream capacity <= available:
    Each connection gets its full flow
    Surplus remains at the junction location (stored in reservoir)
```

---

## Stubs & Unimplemented Features

Everything. This is a pre-implementation specification. No schema, no generated code, no service implementation exists.

---

## Potential Extensions

1. **Seasonal flow modifiers**: Network types could define seasonal multipliers (e.g., aqueducts carry more water during spring snowmelt, less during summer drought). Consume `worldstate.season-changed` events to apply multipliers to flow calculations.

2. **Cascading failure simulation**: When a connection fails under load, excess pressure could damage adjacent connections (domino effect). Configurable per network type.

3. **Capacity upgrade progression**: Track upgrade history per connection, with diminishing returns on capacity upgrades (first upgrade: +50%, second: +30%, third: +15%). Encourage building new connections rather than endlessly upgrading existing ones.

4. **Network visualization data endpoint**: Return graph data in a format suitable for rendering infrastructure maps (node positions from Location coordinates, edge weights from capacity/condition).

5. **Maintenance scheduling**: Background worker that projects condition decay forward and auto-files maintenance requests before connections reach failure threshold. Proactive maintenance vs. reactive repair.

6. **Cross-realm infrastructure**: Connections spanning realm boundaries, subject to diplomatic agreements (Contract-backed inter-realm infrastructure treaties). Realm sovereignty changes could sever cross-realm connections.

7. **Magical infrastructure**: Network types with zero decay (`baseDecayRatePerGameDay: 0.0`) but vulnerability to specific disruption types (anti-magic fields, ley line shifts). Different failure modes from physical infrastructure.

8. **DI-based flow modifiers via `IUtilityFlowModifierProvider`**: Allow L4 services to affect flow calculations without Utility depending on them. Environment could reduce flow during storms, Faction could impose flow restrictions in disputed territory, Divine could boost flow as a blessing effect.

---

## Variable Provider Factory

Utility implements `IVariableProviderFactory` providing the `${utility.*}` namespace to Actor (L2) via the Variable Provider Factory pattern.

### Variables Provided

```yaml
# Per-network-type coverage at the actor's current location
${utility.<networkType>.coverage}          # "full", "partial", "critical", "none"
${utility.<networkType>.rate}              # Numeric service level rate (units/gh)
${utility.<networkType>.ratio}             # Coverage ratio (0.0-N, >1.0 = surplus)

# Aggregate infrastructure awareness
${utility.any_critical}                    # true if ANY network type is critical/none at location
${utility.any_failed}                      # true if ANY connection at location is failed
${utility.maintenance_needed}              # true if any connection at location below maintenance threshold

# For infrastructure-aware NPCs (engineers, governors)
${utility.<networkType>.connections_failed}   # Count of failed connections in local network
${utility.<networkType>.nearest_source_hops}  # Hops to nearest production source
${utility.<networkType>.network_health}       # "healthy", "degraded", "critical", "collapsed"
```

### GOAP Integration Examples

```yaml
# Civilian NPC: seek water when coverage drops
goap_goal: find_water
  preconditions:
    ${utility.water.coverage}: "none|critical"
  actions:
    - travel_to_well        # If well location has coverage
    - travel_to_river       # Fallback: natural water source
    - beg_for_water         # Desperate: ask other NPCs

# Governor NPC: dispatch repair crew
goap_goal: repair_infrastructure
  preconditions:
    ${utility.any_failed}: "true"
    ${organization.primary.has_capability.employ.basic}: "true"
  actions:
    - file_maintenance_request
    - assign_workers
    - authorize_emergency_funds

# Engineer NPC: proactive maintenance
goap_goal: preventive_maintenance
  preconditions:
    ${utility.water.network_health}: "degraded"
    ${utility.water.connections_failed}: "0"    # Not yet broken, but degrading
  actions:
    - inspect_connections
    - file_maintenance_request
    - perform_repair
```

---

## Interaction with Transit

Utility and Transit both model connections between locations, but with fundamentally different semantics:

| Aspect | Transit | Utility |
|--------|---------|---------|
| **Purpose** | Entity movement between locations | Continuous resource flow through infrastructure |
| **Flow type** | Discrete journeys (depart → arrive) | Continuous stream (always flowing) |
| **Capacity meaning** | Max entities traversing simultaneously | Max resource units per game-hour |
| **Condition effect** | Travel speed/risk modifier | Flow rate multiplier or binary cutoff |
| **Consumer** | Characters, NPCs, shipments (via Trade) | Locations (passive recipients) |
| **Time model** | Journey duration computed per-traversal | Flow rate computed as steady-state |

**Deliberate separation**: Transit connections could theoretically model utility infrastructure, but the semantics would be wrong. A Transit journey "uses" a connection for a duration and then releases it. A utility flow "occupies" a connection permanently. Attempting to model both in Transit would conflate movement capacity with throughput capacity, and Transit's mode system (walking, horseback, wagon) is meaningless for infrastructure.

**Interaction point**: Transit's `ITransitCostModifierProvider` pattern could be mirrored in Utility as `IUtilityFlowModifierProvider`, allowing Environment to reduce flow during storms without Utility depending on Environment.

---

## Interaction with Workshop

Workshop produces resources at points. Utility distributes them across networks. The interaction is:

1. **An Organization builds a water plant** → Workshop task created at Location A producing "water" items
2. **The Organization registers the plant as a utility source** → `/utility/source/register` with `workshopTaskId`
3. **Utility queries Workshop for production rate** → `IWorkshopClient.GetTaskStatus` returns current rate
4. **Utility includes this rate in flow calculation** → Location A injects `currentProductionRate` units/gh into the network
5. **Workshop task pauses** (no workers, no materials) → publishes `workshop.task.status-changed`
6. **Utility consumes the event** → updates source rate to 0, recalculates coverage → downstream locations lose service

Workshop doesn't know about Utility. Utility reads Workshop's output. The dependency is one-directional.

---

## Interaction with Organization and Faction

```
FACTION (L4): Governance layer
  "Water infrastructure is a chartered activity in this jurisdiction"
  "Only licensed organizations may operate water networks"
  "Infrastructure damage in sovereign territory triggers emergency response"
       │
       ▼
ORGANIZATION (L4): Operator layer
  "Ironhold Water Guild" (type: "utility_company")
  Owns: water plant (Workshop task), aqueduct connections, repair crews
  Seed growth: infrastructure.water domain grows as coverage expands
  Capabilities unlock: build longer pipes, repair faster, serve more locations
       │
       ▼
UTILITY (L4): Network layer
  Tracks the connections, computes the flow, publishes coverage events
  Doesn't know WHO operates infrastructure, just WHAT exists and WHERE
       │
       ▼
LOCATION + ENVIRONMENT + ACTOR: Consumer layer
  Locations receive coverage, Environment incorporates it,
  NPCs react to coverage state via ${utility.*} variables
```

Utility is deliberately **ownership-agnostic**. A connection has an optional `ownerId`/`ownerType` for reference, but Utility does not enforce ownership rules. Faction norms and Organization charters determine who CAN build/maintain infrastructure. Utility just tracks what EXISTS.

---

## Background Workers

### ConditionDecayWorker

**Interval**: Real-time configurable interval (`ConditionDecayCheckIntervalMinutes`, default 60 minutes). Startup delay: `ConditionDecayWorkerStartupDelaySeconds` (default 60s). The worker runs on a real-time schedule but uses elapsed **game-time** (queried from IWorldstateClient) for decay calculations.

On each tick:
1. Query all active connections across all network types
2. For each connection with `baseDecayRatePerGameDay > 0`:
   a. Calculate elapsed game-time since `lastMaintenanceAt` (or `createdAt` if never maintained)
   b. Apply decay: `newCondition = condition - (decayRate × elapsedDays)`
   c. If `newCondition` changed beyond threshold, call `UpdateCondition` internally
   d. If connection crossed failure threshold, the `UpdateCondition` flow handles events
   e. **Per-item error isolation**: If decay calculation fails for one connection (corrupt data, missing network type config, calculation overflow), log at Warning level with connection ID and error details, and continue processing remaining connections. One corrupt connection record must not block decay processing for the entire network. Cycle-level failures (e.g., unable to query state store) are logged at Error level and abort the tick.
3. Uses distributed locks per network type to prevent concurrent decay runs

### MaintenanceExpirationWorker

**Trigger**: `worldstate.day-changed` event (game-time based). Startup delay: `MaintenanceExpirationWorkerStartupDelaySeconds` (default 120s). Counts game-days for expiration threshold.

On each tick:
1. Query all maintenance requests with status `Open` older than `MaintenanceRequestAutoExpireDays` game-days
2. Transition to `Cancelled` status
3. Publish `utility.maintenance.cancelled` event
4. **Per-item error isolation**: If status transition fails for one request, log at Warning level and continue. One corrupt maintenance request must not block expiration processing.

---

## Testing Strategy

### Unit Tests (lib-utility.tests/)

- **FlowCalculator**: Linear paths, branching junctions, proportional distribution, capacity constraints, condition multipliers, distance loss, bidirectional cycle detection, MaxFlowCalculationDepth safety bound, empty network, single source, multiple sources
- **CoverageManager**: Snapshot caching, TTL expiration, invalidation on topology change, debounced recalculation, coverage status classification thresholds, event publishing on status change, CoverageEventThreshold filtering
- **ConditionDecayCalculator**: Decay rate application, elapsed game-time calculation, failure threshold crossing, zero-decay network types, per-item error isolation
- **Endpoint methods**: All 25 endpoints with success/failure scenarios, validation (duplicate connections, deprecated network type guards, location existence), distributed locking, pagination, filtering

### HTTP Integration Tests (http-tester/)

- End-to-end flow: Create network type -> Create connections -> Register source -> Query coverage -> Verify flow calculation
- Topology mutation: Update connection condition -> Verify coverage recalculation -> Verify events published
- Failure cascade: Set connection condition to 0.0 -> Verify downstream coverage degrades -> Repair -> Verify restoration
- Deprecation lifecycle: Create -> Deprecate -> Guard rejects new connections -> Clean-deprecated sweep

### Edge Tests (edge-tester/)

- Coverage event delivery via WebSocket when topology changes cause coverage status transitions

---

## Implementation Plan

### Phase 0: Prerequisites

- **Worldstate (L2)**: Must exist for game-clock-based decay calculations
- **Workshop (L4)**: Must exist for production source rate queries
- **Location (L2)**: Already exists (92% production-ready)

### Phase 1: Network Type Registry

Create schemas, generate code, implement network type CRUD with validation (code uniqueness, flow configuration bounds). Seed endpoint for bulk initialization from game configuration.

### Phase 2: Connection Graph

Implement connection CRUD with location validation, duplicate prevention, bidirectional support, distributed locking, and all connection indexes. No flow calculation yet -- just the topology data layer.

### Phase 3: Flow Calculation Engine

Implement `FlowCalculator` with BFS graph traversal, capacity-limited flow, condition-based flow reduction, distance-based loss, junction distribution (proportional by capacity), and cycle detection for bidirectional networks. Implement `CoverageManager` with snapshot caching, invalidation, and debounced recalculation.

### Phase 4: Coverage Events and Variable Provider

Implement coverage change event publishing (degraded/restored) with configurable threshold. Implement `UtilityProviderFactory` as `IVariableProviderFactory` for the `${utility.*}` ABML namespace. Wire `GetCoverage`, `GetCoverageByNetwork`, `GetCoveragePath`, `GetNetworkHealth` endpoints.

### Phase 5: Production Source Integration

Implement source registration/deregistration with Workshop task linking. Wire `workshop.task.status-changed` event handler. Implement `CompareCoverage` planning endpoint.

### Phase 6: Maintenance and Decay

Implement maintenance request CRUD. Implement `ConditionDecayWorker` consuming `worldstate.day-changed`. Implement `MaintenanceExpirationWorker`. Wire Environment event handler for weather-based damage.

### Phase 7: DI Flow Modifier Pattern

Implement `IUtilityFlowModifierProvider` interface in `bannou-service/Providers/`. Allow Environment, Faction, and other L4 services to register flow modifiers without Utility depending on them. Integrate modifiers into `FlowCalculator`.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(None -- pre-implementation)*

### Intentional Quirks (Documented Behavior)

*(None -- pre-implementation)*

### Design Considerations (Requires Planning)

1. **Flow calculation is O(V + E) per network**: For a network with 10,000 connections and 5,000 locations, recalculation is ~15,000 operations. With debouncing (2s default) and caching (5min TTL), this should handle frequent topology changes. At scale, consider per-subgraph recalculation (only recompute the affected subtree, not the entire network). **Multi-instance safety**: `CoverageRecalculationDebounceMs` operates per-instance as a best-effort optimization. In multi-instance deployments, multiple instances may independently trigger recalculation after the debounce window. The distributed lock on `lock:coverage:{networkTypeCode}` serializes actual recalculation execution, ensuring correctness. The debounce reduces unnecessary lock contention and redundant computation but is not a correctness mechanism. This is IMPLEMENTATION TENETS compliant: the authoritative serialization is the distributed lock, not the in-memory timer. Design decision on local vs Redis-based debounce tracked in #633. AUDIT:NEEDS_DESIGN

2. **Workshop dependency mode**: Workshop is now soft (L4→L4 per service hierarchy) with manual-rate fallback when unavailable. Whether this degradation mode is optimal or whether Utility/Workshop should be operationally co-required is tracked in #628. AUDIT:NEEDS_DESIGN

3. **Junction distribution fairness**: Proportional-by-capacity is simple but may not match game design intent. Some games want priority-based distribution (military district gets water first, slums get remainder). The `IUtilityFlowModifierProvider` pattern (Phase 7) could address this by letting Faction inject priority weights. Tracked in #629. AUDIT:NEEDS_DESIGN

4. **Coverage ratio without demand data**: If Trade (which provides demand data) is unavailable, `coverageRatio` cannot be computed. The snapshot should clearly distinguish "supply rate is 35 units/gh, demand unknown" from "supply rate is 35 units/gh, demand is 50 units/gh, ratio is 0.7." The `coverageStatus` classification should still work on absolute thresholds when demand is unknown. Tracked in #630. AUDIT:NEEDS_DESIGN

5. **Bidirectional network cycles**: Power grids are naturally bidirectional with cycles. The BFS flow calculator must handle this (track visited nodes, detect cycles, prevent infinite traversal). `MaxFlowCalculationDepth` provides a safety bound.

6. **Connection condition vs. binary status**: Some network types should have proportional flow reduction (water pipe at 50% condition delivers 50% flow). Others should be binary (a bridge either works or doesn't). The `conditionFlowMultiplier` flag on network type controls this.

7. **Seeding infrastructure for world initialization**: New realms need initial infrastructure. Consider a bulk seed endpoint that accepts a list of connections + sources in one call, creates them all, then triggers a single coverage recalculation. Avoids N recalculations during world setup.

8. **No direct player interaction**: Utility is internal-only. Players experience infrastructure through its effects (water available, power out) and through NPCs reacting to infrastructure state. Players may direct construction via Organization management, but they don't call Utility APIs directly.

9. **Connection demolition semantics**: Whether demolished connections are soft-deleted (status=Demolished, record retained) or hard-deleted (record removed). Affects maintenance cleanup and event naming. Tracked in #631. AUDIT:NEEDS_DESIGN

10. **disabledReason type classification**: Whether `disabledReason` on connections is a finite enum (`ConnectionDisableReason`) or game-configurable opaque string. Tracked in #632. AUDIT:NEEDS_DESIGN

---

## Work Tracking

| Issue | Title | Status |
|-------|-------|--------|
| #628 | Workshop dependency mode | Open — AUDIT:NEEDS_DESIGN |
| #629 | Junction distribution fairness | Open — AUDIT:NEEDS_DESIGN |
| #630 | Coverage ratio without demand data | Open — AUDIT:NEEDS_DESIGN |
| #631 | Connection demolition semantics | Open — AUDIT:NEEDS_DESIGN |
| #632 | disabledReason type classification | Open — AUDIT:NEEDS_DESIGN |
| #633 | Debounce implementation | Open — AUDIT:NEEDS_DESIGN |
