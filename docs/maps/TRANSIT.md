# Transit Implementation Map

> **Plugin**: lib-transit
> **Schema**: schemas/transit-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/TRANSIT.md](../plugins/TRANSIT.md)

| Field | Value |
|-------|-------|
| Plugin | lib-transit |
| Layer | L2 GameFoundation |
| Endpoints | 33 |
| State Stores | transit-modes (MySQL), transit-connections (MySQL), transit-journeys (Redis), transit-journeys-archive (MySQL), transit-connection-graph (Redis), transit-discovery (MySQL), transit-discovery-cache (Redis), transit-lock (Redis) |
| Events Published | 14 (transit.mode.registered, transit.mode.updated, transit.mode.deleted, transit.connection.created, transit.connection.updated, transit.connection.deleted, transit.connection.status-changed, transit.journey.departed, transit.journey.waypoint-reached, transit.journey.arrived, transit.journey.interrupted, transit.journey.resumed, transit.journey.abandoned, transit.discovery.revealed) |
| Events Consumed | 1 (worldstate.season-changed) |
| Client Events | 3 (transit.journey.updated, transit.discovery.revealed, transit.connection.status-changed) |
| Background Services | 2 (SeasonalConnectionWorker, JourneyArchivalWorker) |

---

## State

**Store**: `transit-modes` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `mode:{code}` | `TransitModeModel` | Transit mode definitions |

**Store**: `transit-connections` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `connection:{id}` | `TransitConnectionModel` | Graph edges between locations |

**Store**: `transit-journeys` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `journey:{id}` | `TransitJourneyModel` | Active journey state |
| `journey-index` | `List<Guid>` | Index of all active journey IDs (optimistic concurrency via ETag) |

**Store**: `transit-journeys-archive` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `archive:{id}` | `JourneyArchiveModel` | Archived completed/abandoned journeys |

**Store**: `transit-connection-graph` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `graph:{realmId}` | `List<ConnectionGraphEntry>` | Cached per-realm adjacency list for route calculation (TTL from config) |

**Store**: `transit-discovery` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `discovery:{entityId}:{connectionId}` | `TransitDiscoveryModel` | Per-entity connection discovery records |

**Store**: `transit-discovery-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `discovery-cache:{entityId}` | `HashSet<Guid>` | Per-entity discovered connection IDs (TTL from config) |

**Store**: `transit-lock` (Backend: Redis)

| Lock Key Pattern | Purpose |
|------------------|---------|
| `mode:{code}` | Mode registration/deletion uniqueness |
| `connection:code:{code}` | Connection code uniqueness |
| `connection:pair:{fromId}:{toId}` | Connection directionality uniqueness |
| `connection-status:{id}` | Connection status mutation |
| `journey:{id}` | Journey state machine transitions |
| `discovery:{entityId}:{connectionId}` | Discovery uniqueness |
| `journey-archival-sweep` | Single-node archival worker coordination |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 8 state stores for modes, connections, journeys, archive, graph cache, discovery, discovery cache |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks for mode/connection/journey/discovery mutations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 14 event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on all async methods |
| lib-location (ILocationClient) | L2 | Hard | Validates location existence; derives realm IDs; reports entity position on transitions |
| lib-worldstate (IWorldstateClient) | L2 | Hard | Game-time calculations for ETAs; seasonal context for connection status |
| lib-character (ICharacterClient) | L2 | Hard | Species code lookup for mode compatibility |
| lib-species (ISpeciesClient) | L2 | Hard | Species property lookup for mode restriction checks |
| lib-inventory (IInventoryClient) | L2 | Hard | Item tag checks for mode requirements |
| lib-resource (IResourceClient) | L1 | Hard | Cleanup callback registration for location/character deletion |
| lib-connect (IClientEventPublisher) | L1 | Hard | WebSocket push for journey/discovery/connection-status client events |
| lib-connect (IEntitySessionRegistry) | L1 | Hard | Entity-to-session routing for client events |
| ITransitCostModifierProvider (DI collection) | L4 | Soft | Optional cost enrichment from Disposition, Environment, Faction, Hearsay, Ethology |

**DI Provider Interfaces:**
- **Implements** `IVariableProviderFactory` via `TransitVariableProviderFactory` -- provides `${transit.*}` variables to Actor (L2)
- **Consumes** `ITransitCostModifierProvider` via `IEnumerable<T>` -- L4 services enrich cost calculations

**Resource Cleanup:** Transit declares `x-references` for `location` (CASCADE) and `character` (CASCADE). Cleanup endpoints (`/transit/cleanup-by-location`, `/transit/cleanup-by-character`) are called by lib-resource, not by event subscriptions.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `transit.mode.registered` | `TransitModeRegisteredEvent` | RegisterMode |
| `transit.mode.updated` | `TransitModeUpdatedEvent` | UpdateMode, DeprecateMode, UndeprecateMode (includes changedFields) |
| `transit.mode.deleted` | `TransitModeDeletedEvent` | DeleteMode |
| `transit.connection.created` | `TransitConnectionCreatedEvent` (x-lifecycle) | CreateConnection, BulkSeedConnections (new) |
| `transit.connection.updated` | `TransitConnectionUpdatedEvent` (x-lifecycle) | UpdateConnection, BulkSeedConnections (updated) |
| `transit.connection.deleted` | `TransitConnectionDeletedEvent` (x-lifecycle) | DeleteConnection |
| `transit.connection.status-changed` | `TransitConnectionStatusChangedEvent` | UpdateConnectionStatus, HandleSeasonChanged, CleanupByLocation |
| `transit.journey.departed` | `TransitJourneyDepartedEvent` | DepartJourney |
| `transit.journey.waypoint-reached` | `TransitJourneyWaypointReachedEvent` | AdvanceJourney (non-final leg) |
| `transit.journey.arrived` | `TransitJourneyArrivedEvent` | AdvanceJourney (final leg), ArriveJourney |
| `transit.journey.interrupted` | `TransitJourneyInterruptedEvent` | InterruptJourney, CleanupByLocation |
| `transit.journey.resumed` | `TransitJourneyResumedEvent` | ResumeJourney |
| `transit.journey.abandoned` | `TransitJourneyAbandonedEvent` | AbandonJourney, CleanupByCharacter |
| `transit.discovery.revealed` | `TransitDiscoveryRevealedEvent` | RevealDiscovery, AdvanceJourney (auto-reveal) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | Scans realm connections with seasonal restrictions; transitions SeasonalClosed/Open; invalidates graph cache; publishes status-changed events |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<TransitService>` | Structured logging |
| `TransitServiceConfiguration` | Typed configuration (21 properties) |
| `IStateStoreFactory` | State store access (8 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed locks for mutations |
| `ILocationClient` | Location validation and position reporting |
| `IWorldstateClient` | Game-time calculations |
| `ICharacterClient` | Species code lookup |
| `ISpeciesClient` | Species restriction checks |
| `IInventoryClient` | Item tag checks |
| `IResourceClient` | cleanup registration |
| `IClientEventPublisher` | WebSocket client events |
| `IEntitySessionRegistry` | Entity-to-session routing |
| `ITransitConnectionGraphCache` | Per-realm connection graph cache (Singleton helper) |
| `ITransitRouteCalculator` | Yen's k-shortest paths route calculation (Singleton helper) |
| `IEnumerable<ITransitCostModifierProvider>` | L4 cost enrichment providers |
| `IEventConsumer` | Event subscription registration |
| `TransitVariableProviderFactory` | `${transit.*}` variable provider for Actor |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| RegisterMode | POST /transit/mode/register | developer | mode | transit.mode.registered |
| GetMode | POST /transit/mode/get | user | - | - |
| ListModes | POST /transit/mode/list | user | - | - |
| UpdateMode | POST /transit/mode/update | developer | mode | transit.mode.updated |
| DeprecateMode | POST /transit/mode/deprecate | developer | mode | transit.mode.updated |
| UndeprecateMode | POST /transit/mode/undeprecate | developer | mode | transit.mode.updated |
| DeleteMode | POST /transit/mode/delete | developer | mode | transit.mode.deleted |
| CheckModeAvailability | POST /transit/mode/check-availability | user | - | - |
| CreateConnection | POST /transit/connection/create | developer | connection, graph-cache | transit.connection.created |
| GetConnection | POST /transit/connection/get | user | - | - |
| QueryConnections | POST /transit/connection/query | user | - | - |
| UpdateConnection | POST /transit/connection/update | developer | connection, graph-cache | transit.connection.updated |
| UpdateConnectionStatus | POST /transit/connection/update-status | user | connection, graph-cache | transit.connection.status-changed |
| DeleteConnection | POST /transit/connection/delete | developer | connection, graph-cache | transit.connection.deleted |
| BulkSeedConnections | POST /transit/connection/bulk-seed | developer | connection, graph-cache | transit.connection.created, transit.connection.updated |
| CreateJourney | POST /transit/journey/create | user | journey, journey-index | - |
| DepartJourney | POST /transit/journey/depart | user | journey | transit.journey.departed |
| ResumeJourney | POST /transit/journey/resume | user | journey | transit.journey.resumed |
| AdvanceJourney | POST /transit/journey/advance | user | journey, discovery, discovery-cache | transit.journey.waypoint-reached or transit.journey.arrived, transit.discovery.revealed |
| AdvanceBatchJourneys | POST /transit/journey/advance-batch | user | journey, discovery, discovery-cache | per-journey events |
| ArriveJourney | POST /transit/journey/arrive | user | journey | transit.journey.arrived |
| InterruptJourney | POST /transit/journey/interrupt | user | journey | transit.journey.interrupted |
| AbandonJourney | POST /transit/journey/abandon | user | journey | transit.journey.abandoned |
| GetJourney | POST /transit/journey/get | user | - | - |
| QueryJourneysByConnection | POST /transit/journey/query-by-connection | user | - | - |
| ListJourneys | POST /transit/journey/list | user | - | - |
| QueryJourneyArchive | POST /transit/journey/query-archive | user | - | - |
| CalculateRoute | POST /transit/route/calculate | user | - | - |
| RevealDiscovery | POST /transit/discovery/reveal | user | discovery, discovery-cache | transit.discovery.revealed |
| ListDiscoveries | POST /transit/discovery/list | user | - | - |
| CheckDiscoveries | POST /transit/discovery/check | user | - | - |
| CleanupByLocation | POST /transit/cleanup-by-location | [] | connection, journey, graph-cache | transit.connection.status-changed, transit.journey.interrupted, transit.journey.abandoned |
| CleanupByCharacter | POST /transit/cleanup-by-character | [] | journey, discovery, discovery-cache | transit.journey.abandoned |

---

## Methods

### RegisterMode
POST /transit/mode/register | Roles: [developer]

```
LOCK transit-lock:"mode:{code}" -> 409 if fails
 READ mode-store:"mode:{code}" -> 409 if exists
 WRITE mode-store:"mode:{code}" <- TransitModeModel from request
 PUBLISH transit.mode.registered { full mode entity }
RETURN (200, ModeResponse)
```

### GetMode
POST /transit/mode/get | Roles: [user]

```
READ mode-store:"mode:{code}" -> 404 if null
RETURN (200, ModeResponse)
```

### ListModes
POST /transit/mode/list | Roles: [user]

```
QUERY mode-store WHERE true // all modes, filtered in memory
IF !includeDeprecated
 // filter out deprecated
IF realmId provided
 // filter by realm restrictions
IF terrainType provided
 // filter by compatible terrain
IF tags provided
 // filter by ALL specified tags
RETURN (200, ListModesResponse)
```

### UpdateMode
POST /transit/mode/update | Roles: [developer]

```
READ mode-store:"mode:{code}" [with ETag] -> 404 if null
// apply non-null field updates, track changedFields
IF changedFields.Count > 0
 ETAG-WRITE mode-store:"mode:{code}" -> 409 if ETag mismatch
 PUBLISH transit.mode.updated { mode, changedFields }
RETURN (200, ModeResponse)
```

### DeprecateMode
POST /transit/mode/deprecate | Roles: [developer]

```
READ mode-store:"mode:{code}" [with ETag] -> 404 if null
IF already deprecated
 RETURN (200, ModeResponse) // idempotent
// set IsDeprecated=true, DeprecatedAt, DeprecationReason
ETAG-WRITE mode-store:"mode:{code}" -> 409 if ETag mismatch
PUBLISH transit.mode.updated { mode, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, ModeResponse)
```

### UndeprecateMode
POST /transit/mode/undeprecate | Roles: [developer]

```
READ mode-store:"mode:{code}" [with ETag] -> 404 if null
IF not deprecated
 RETURN (200, ModeResponse) // idempotent
// clear IsDeprecated, DeprecatedAt, DeprecationReason
ETAG-WRITE mode-store:"mode:{code}" -> 409 if ETag mismatch
PUBLISH transit.mode.updated { mode, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, ModeResponse)
```

### DeleteMode
POST /transit/mode/delete | Roles: [developer]

```
LOCK transit-lock:"mode:{code}" -> 409 if fails
 READ mode-store:"mode:{code}" -> 404 if null
 IF not deprecated -> 400 (Category A: must deprecate first)
 QUERY connection-store WHERE compatibleModes contains code
 IF connections found -> 400 (connections reference mode)
 QUERY journey-archive WHERE primaryModeCode == code AND active status
 IF archived journeys found -> 400 (active journeys use mode)
 // scan Redis journey index for active journey conflicts
 IF Redis journey conflict found -> 400 (active journeys use mode)
 DELETE mode-store:"mode:{code}"
 PUBLISH transit.mode.deleted { code, deletedReason }
RETURN (200, DeleteModeResponse)
```

### CheckModeAvailability
POST /transit/mode/check-availability | Roles: [user]

```
QUERY mode-store WHERE !deprecated
IF modeCode filter provided
 // filter to specific mode
FOREACH mode in modes
 // check validEntityTypes against request.entityType
 IF entityType == "character"
 CALL ICharacterClient.GetCharacterAsync(entityId)
 CALL ISpeciesClient.GetSpeciesAsync(speciesCode)
 // check allowedSpeciesCodes / excludedSpeciesCodes / maximumEntitySizeCategory
 IF requiredItemTag set
 CALL IInventoryClient.CheckHasItemTagAsync(entityId, requiredItemTag)
 // compute effectiveSpeed with cargo penalty
 // aggregate DI cost modifier providers (graceful degradation)
 FOREACH provider in _costModifierProviders
 CALL provider.GetModifierAsync(entityId, entityType, modeCode, connectionId)
 // sum preferenceCost, multiply speedMultiplier, clamp to config bounds
RETURN (200, CheckModeAvailabilityResponse { availableModes })
```

### CreateConnection
POST /transit/connection/create | Roles: [developer]

```
IF fromLocationId == toLocationId -> 400
CALL ILocationClient.GetLocationAsync(fromLocationId) -> 400 if not found
CALL ILocationClient.GetLocationAsync(toLocationId) -> 400 if not found
// derive fromRealmId, toRealmId, crossRealm from locations
FOREACH modeCode in compatibleModes
 READ mode-store:"mode:{modeCode}" -> 400 if not found
IF seasonalAvailability provided
 CALL IWorldstateClient.GetRealmTimeAsync(realmId)
 // validate season keys against realm's calendar -> 400 if invalid season key
IF code provided
 LOCK transit-lock:"connection:code:{code}" -> 409 if fails
LOCK transit-lock:"connection:pair:{fromId}:{toId}" -> 409 if fails
 // check uniqueness of pair (and reverse for bidirectional)
 QUERY connection-store WHERE from/to match -> 409 if exists
 WRITE connection-store:"connection:{newId}" <- TransitConnectionModel from request
 // invalidate graph cache for affected realms
 CALL IResourceClient.RegisterReferenceAsync (fromLocationId, toLocationId)
 PUBLISH transit.connection.created { full connection entity }
RETURN (200, ConnectionResponse)
```

### GetConnection
POST /transit/connection/get | Roles: [user]

```
IF connectionId provided
 READ connection-store:"connection:{connectionId}" -> 404 if null
ELSE IF code provided
 QUERY connection-store WHERE code == code -> 404 if empty
ELSE -> 400
RETURN (200, ConnectionResponse)
```

### QueryConnections
POST /transit/connection/query | Roles: [user]

```
QUERY connection-store WHERE filters (fromLocationId, toLocationId, locationId, realmId, crossRealm, terrainType, modeCode, status, tags, includeSeasonalClosed) PAGED(page, pageSize)
RETURN (200, QueryConnectionsResponse { connections, totalCount })
```

### UpdateConnection
POST /transit/connection/update | Roles: [developer]

```
READ connection-store:"connection:{connectionId}" [with ETag] -> 404 if null
// apply non-null field updates, track changedFields
IF changedFields.Count > 0
 ETAG-WRITE connection-store:"connection:{connectionId}" -> 409 if ETag mismatch
 // invalidate graph cache for affected realms
 PUBLISH transit.connection.updated { connection, changedFields }
RETURN (200, ConnectionResponse)
```

### UpdateConnectionStatus
POST /transit/connection/update-status | Roles: [user]

```
LOCK transit-lock:"connection-status:{connectionId}" -> 409 if fails
 READ connection-store:"connection:{connectionId}" [with ETag] -> 404 if null
 IF !forceUpdate AND currentStatus != actual status -> 400 (STATUS_MISMATCH)
 // update status, statusReason, statusChangedAt
 ETAG-WRITE connection-store:"connection:{connectionId}" -> 409 if ETag mismatch
 // invalidate graph cache for affected realms
 PUBLISH transit.connection.status-changed { connectionId, previousStatus, newStatus, reason, forceUpdated }
 PUSH TransitConnectionStatusChangedClientEvent -> realm sessions (both realms if cross-realm)
RETURN (200, ConnectionResponse)
```

### DeleteConnection
POST /transit/connection/delete | Roles: [developer]

```
READ connection-store:"connection:{connectionId}" -> 404 if null
QUERY journey-archive WHERE legs contain connectionId AND active status
IF active archived journeys found -> 400
// scan Redis journey index for connection conflicts
IF Redis journey conflict found -> 400
DELETE connection-store:"connection:{connectionId}"
// invalidate graph cache for affected realms
PUBLISH transit.connection.deleted { full connection, deletedReason }
RETURN (200, DeleteConnectionResponse)
```

### BulkSeedConnections
POST /transit/connection/bulk-seed | Roles: [developer]

```
// Pass 1: Resolve all unique location codes to IDs via ILocationClient
FOREACH locationCode in allUniqueCodes
 CALL ILocationClient.GetLocationByCodeAsync(code)
 // map code -> locationId, realmId

// Pass 2: Create/update each connection
FOREACH entry in connections
 // validate location codes resolved, same-location check, realm filter
 FOREACH modeCode in entry.compatibleModes
 READ mode-store:"mode:{modeCode}" -> error if not found
 LOCK transit-lock on code or pair key -> skip if fails
 IF code provided AND existing connection found
 IF replaceExisting
 READ connection-store [with ETag]
 ETAG-WRITE connection-store <- updated fields
 PUBLISH transit.connection.updated { changedFields }
 // increment updated count
 ELSE
 // skip (already exists)
 ELSE
 WRITE connection-store:"connection:{newId}" <- new model
 PUBLISH transit.connection.created { full entity }
 // increment created count

// invalidate graph cache for all affected realms
RETURN (200, BulkSeedConnectionsResponse { created, updated, errors })
```

### CreateJourney
POST /transit/journey/create | Roles: [user]

```
READ mode-store:"mode:{primaryModeCode}" -> 404 if null
IF mode deprecated -> 400
// check mode compatibility via internal check-availability logic
// validate entity type against mode's validEntityTypes
CALL ILocationClient.GetLocationAsync(originLocationId) -> 400 if not found
CALL ILocationClient.GetLocationAsync(destinationLocationId) -> 400 if not found
CALL IWorldstateClient.GetRealmTimeAsync(realmId)
// invoke route calculator for best path
CALL ITransitRouteCalculator.CalculateAsync(...)
IF no routes found -> 400 (NO_ROUTE_AVAILABLE)
// build TransitJourneyModel from best route option
WRITE journey-store:"journey:{newId}" <- TransitJourneyModel
// add to journey index (optimistic concurrency retry loop)
READ journey-index-store:"journey-index" [with ETag]
// append newId to list
ETAG-WRITE journey-index-store:"journey-index" // retry on ETag conflict
// no event published (first event is departed)
RETURN (200, JourneyResponse)
```

### DepartJourney
POST /transit/journey/depart | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status != Preparing -> 400
 // validate first leg's connection is open
 READ connection-store:"connection:{firstLeg.connectionId}"
 IF connection closed/blocked -> 400
 // transition: Preparing -> InTransit, set actualDepartureGameTime
 // mark first leg as InProgress
 WRITE journey-store:"journey:{journeyId}"
 // report departure position via ILocationClient
 IF config.AutoUpdateLocationOnTransition
 CALL ILocationClient.ReportEntityPositionAsync(entityId, originLocationId)
 PUBLISH transit.journey.departed { journeyId, entity, origin, destination, mode, ETA, partySize, realms }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### ResumeJourney
POST /transit/journey/resume | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status != Interrupted -> 400
 // validate current leg's connection is open
 READ connection-store:"connection:{currentLeg.connectionId}"
 IF connection closed/blocked -> 400
 // resolve last unresolved interruption, set durationGameHours
 // transition: Interrupted -> InTransit
 WRITE journey-store:"journey:{journeyId}"
 PUBLISH transit.journey.resumed { journeyId, entity, currentLocation, destination, legIndex, remainingLegs, modeCode }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### AdvanceJourney
POST /transit/journey/advance | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status != InTransit AND status != AtWaypoint -> 400
 // add incidents if provided
 FOREACH incident in incidents
 // add TransitInterruptionModel to journey.Interruptions
 // complete current leg: status = Completed, completedAtGameTime
 // auto-reveal discoverable connections on completed leg
 // see helper: TryAutoRevealDiscoveryAsync
 // report waypoint position via ILocationClient
 IF config.AutoUpdateLocationOnTransition
 CALL ILocationClient.ReportEntityPositionAsync(entityId, waypointLocationId)
 IF final leg
 // transition: -> Arrived, set actualArrivalGameTime
 WRITE journey-store:"journey:{journeyId}"
 PUBLISH transit.journey.arrived { journeyId, entity, origin, destination, mode, totalGameHours, distance, interruptions, legsCompleted }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
 ELSE
 // advance to next leg: currentLegIndex++, status = AtWaypoint
 WRITE journey-store:"journey:{journeyId}"
 PUBLISH transit.journey.waypoint-reached { journeyId, entity, waypointLocation, nextLocation, legIndex, remainingLegs, connectionId, crossedRealmBoundary }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### AdvanceBatchJourneys
POST /transit/journey/advance-batch | Roles: [user]

```
FOREACH entry in advances // sequential to preserve same-journeyId ordering
 // delegate to AdvanceJourneyAsync per entry
 CALL AdvanceJourneyAsync({ journeyId, arrivedAtGameTime, incidents })
 // collect BatchAdvanceResult (journeyId, error, journey)
 // exceptions caught per entry, reported as error string
RETURN (200, AdvanceBatchResponse { results })
// Note: 200 always returned; inspect per-entry results for failures
```

### ArriveJourney
POST /transit/journey/arrive | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status != InTransit AND status != AtWaypoint -> 400
 // mark remaining legs as Skipped (force-arrive)
 // transition: -> Arrived, set actualArrivalGameTime, statusReason
 // set currentLocationId = destinationLocationId
 WRITE journey-store:"journey:{journeyId}"
 IF config.AutoUpdateLocationOnTransition
 CALL ILocationClient.ReportEntityPositionAsync(entityId, destinationLocationId)
 PUBLISH transit.journey.arrived { journeyId, entity, origin, destination, mode, totalGameHours, distance, interruptions, legsCompleted }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### InterruptJourney
POST /transit/journey/interrupt | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status != InTransit -> 400
 // add interruption record (legIndex, gameTime, reason, resolved=false)
 // transition: InTransit -> Interrupted, statusReason
 WRITE journey-store:"journey:{journeyId}"
 PUBLISH transit.journey.interrupted { journeyId, entity, currentLocation, legIndex, reason }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### AbandonJourney
POST /transit/journey/abandon | Roles: [user]

```
LOCK transit-lock:"journey:{journeyId}" -> 409 if fails
 READ journey-store:"journey:{journeyId}" -> 404 if null
 IF status == Arrived OR status == Abandoned -> 400 (terminal)
 // transition: -> Abandoned, statusReason
 WRITE journey-store:"journey:{journeyId}"
 IF config.AutoUpdateLocationOnTransition
 CALL ILocationClient.ReportEntityPositionAsync(entityId, currentLocationId)
 PUBLISH transit.journey.abandoned { journeyId, entity, origin, destination, abandonedAtLocation, completedLegs, totalLegs, reason }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions
RETURN (200, JourneyResponse)
```

### GetJourney
POST /transit/journey/get | Roles: [user]

```
// try Redis first (active journeys)
READ journey-store:"journey:{journeyId}"
IF found
 RETURN (200, JourneyResponse)
// fallback to MySQL archive
READ journey-archive:"archive:{journeyId}" -> 404 if null
RETURN (200, JourneyResponse)
```

### QueryJourneysByConnection
POST /transit/journey/query-by-connection | Roles: [user]

```
READ connection-store:"connection:{connectionId}" -> 404 if null
QUERY journey-archive WHERE legs contain connectionId [AND status filter] PAGED(page, pageSize)
RETURN (200, ListJourneysResponse { journeys, totalCount })
// Note: queries archive store only; active Redis journeys not included
```

### ListJourneys
POST /transit/journey/list | Roles: [user]

```
QUERY journey-archive WHERE filters (entityId, entityType, status, activeOnly) PAGED(page, pageSize)
RETURN (200, ListJourneysResponse { journeys, totalCount })
// Note: queries archive store only; realm/crossRealm filters not yet supported
```

### QueryJourneyArchive
POST /transit/journey/query-archive | Roles: [user]

```
QUERY journey-archive WHERE filters (entityId, entityType, originLocationId, destinationLocationId, modeCode, status, fromGameTime, toGameTime) PAGED(page, pageSize)
RETURN (200, ListJourneysResponse { journeys, totalCount })
```

### CalculateRoute
POST /transit/route/calculate | Roles: [user]

```
CALL ILocationClient.GetLocationAsync(fromLocationId) -> 400 if not found
CALL ILocationClient.GetLocationAsync(toLocationId) -> 400 if not found
CALL IWorldstateClient.GetRealmTimeAsync(fromLocation.realmId)
 // non-fatal: if unavailable, totalRealMinutes=0, no seasonal warnings
// build RouteCalculationRequest with config defaults
CALL ITransitRouteCalculator.CalculateAsync(request)
 // internally: loads graph from cache, runs Yen's k-shortest paths
 // applies terrain speed modifiers, cargo penalties, seasonal filtering
 // discovery filtering when entityId provided
IF no routes found -> 400 (NO_ROUTE_AVAILABLE)
// map results to TransitRouteOption with rank
RETURN (200, CalculateRouteResponse { options })
// Note: pure computation, no state mutation, no DI cost modifiers applied
```

### RevealDiscovery
POST /transit/discovery/reveal | Roles: [user]

```
READ connection-store:"connection:{connectionId}" -> 404 if null
IF !connection.discoverable -> 400
LOCK transit-lock:"discovery:{entityId}:{connectionId}" -> 409 if fails
 READ discovery-store:"discovery:{entityId}:{connectionId}"
 IF already discovered
 RETURN (200, RevealDiscoveryResponse { isNew: false })
 WRITE discovery-store:"discovery:{entityId}:{connectionId}" <- new TransitDiscoveryModel
 // update Redis discovery cache
 READ discovery-cache:"discovery-cache:{entityId}"
 // add connectionId to set, save with TTL
 WRITE discovery-cache:"discovery-cache:{entityId}"
 PUBLISH transit.discovery.revealed { entityId, connectionId, fromLocationId, toLocationId, source, realms }
 PUSH TransitDiscoveryRevealedClientEvent -> entity sessions
RETURN (200, RevealDiscoveryResponse { isNew: true })
```

### ListDiscoveries
POST /transit/discovery/list | Roles: [user]

```
QUERY discovery-store WHERE entityId == entityId
IF realmId filter provided
 FOREACH discovery in results
 READ connection-store:"connection:{connectionId}"
 // include only connections touching the filtered realm
RETURN (200, ListDiscoveriesResponse { connectionIds })
```

### CheckDiscoveries
POST /transit/discovery/check | Roles: [user]

```
FOREACH connectionId in connectionIds
 READ discovery-store:"discovery:{entityId}:{connectionId}"
 // build DiscoveryCheckResult { discovered, discoveredAt, source }
RETURN (200, CheckDiscoveriesResponse { results })
```

### CleanupByLocation
POST /transit/cleanup-by-location | Roles: [] (internal, lib-resource callback)

```
// Close all connections referencing the deleted location
QUERY connection-store WHERE fromLocationId == locationId OR toLocationId == locationId
FOREACH connection in results
 // set status = Closed, reason = "location_deleted"
 WRITE connection-store:"connection:{id}"
 // invalidate graph cache
 PUBLISH transit.connection.status-changed { forceUpdated: true }
 PUSH TransitConnectionStatusChangedClientEvent -> realm sessions
 CALL IResourceClient.UnregisterReferenceAsync (location references)

// Interrupt active archived journeys passing through deleted location
QUERY journey-archive WHERE legs contain locationId AND active status
FOREACH journey in results
 // set status = Interrupted, add interruption record
 WRITE journey-archive:"archive:{id}"
 PUBLISH transit.journey.interrupted { reason: "location_deleted" }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions

// Scan Redis active journeys for the same
// see helper: ScanRedisJourneysForLocationCleanupAsync
RETURN (200, null)
```

### CleanupByCharacter
POST /transit/cleanup-by-character | Roles: [] (internal, lib-resource callback)

```
// Clear discovery data
QUERY discovery-store WHERE entityId == characterId
FOREACH discovery in results
 DELETE discovery-store:"discovery:{entityId}:{connectionId}"
DELETE discovery-cache:"discovery-cache:{entityId}"

// Abandon active archived journeys for this entity
QUERY journey-archive WHERE entityId == characterId AND entityType == "character" AND active status
FOREACH journey in results
 // set status = Abandoned, reason = "character_deleted"
 WRITE journey-archive:"archive:{id}"
 PUBLISH transit.journey.abandoned { reason: "character_deleted" }
 PUSH TransitJourneyUpdatedClientEvent -> entity sessions

// Scan Redis active journeys for the same
// see helper: ScanRedisJourneysForCharacterCleanupAsync
RETURN (200, null)
```

---

## Background Services

### SeasonalConnectionWorker
**Interval**: `config.SeasonalConnectionCheckIntervalSeconds` (default 60s)
**Startup Delay**: `config.SeasonalWorkerStartupDelaySeconds` (default 5s)
**Purpose**: Responds to `worldstate.season-changed` events and periodically scans connections with seasonal availability restrictions, updating their status for the current season.

```
// event-driven: HandleSeasonChangedAsync fires on worldstate.season-changed
// periodic: safety-net scan every SeasonalConnectionCheckIntervalSeconds
QUERY connection-store WHERE seasonalAvailability != null
// group connections by realm
FOREACH realm in groups
 CALL IWorldstateClient.GetRealmTimeAsync(realmId)
 FOREACH connection in realmConnections
 // find matching season entry
 IF !available AND status != SeasonalClosed
 // transition to SeasonalClosed (with ETag in worker, bare save in event handler)
 WRITE connection-store:"connection:{id}"
 // invalidate graph cache
 PUBLISH transit.connection.status-changed { forceUpdated: true }
 ELSE IF available AND status == SeasonalClosed
 // transition to Open
 WRITE connection-store:"connection:{id}"
 // invalidate graph cache
 PUBLISH transit.connection.status-changed { forceUpdated: true }
```

### JourneyArchivalWorker
**Interval**: `config.JourneyArchivalWorkerIntervalSeconds` (default 300s)
**Startup Delay**: `config.JourneyArchivalWorkerStartupDelaySeconds` (default 10s)
**Purpose**: Archives completed/abandoned journeys from Redis to MySQL. Enforces retention policy on archived journeys.

```
LOCK transit-lock:"journey-archival-sweep" // single-node coordination
 READ journey-index-store:"journey-index" [with ETag]
 FOREACH journeyId in index
 READ journey-store:"journey:{journeyId}"
 IF status is terminal (Arrived or Abandoned)
 // check game-time age against JourneyArchiveAfterGameHours
 CALL IWorldstateClient.GetRealmTimeAsync(realmId)
 IF old enough for archival
 WRITE journey-archive:"archive:{journeyId}" <- JourneyArchiveModel
 DELETE journey-store:"journey:{journeyId}"
 // remove from index list
 ETAG-WRITE journey-index-store:"journey-index" // save cleaned index

 // Enforce retention policy
 IF JourneyArchiveRetentionDays > 0
 QUERY journey-archive WHERE createdAt < (now - retentionDays)
 FOREACH expired in results
 DELETE journey-archive:"archive:{id}"
```
