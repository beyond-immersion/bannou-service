# Environment Implementation Map

> **Plugin**: lib-environment
> **Schema**: schemas/environment-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/ENVIRONMENT.md](../plugins/ENVIRONMENT.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-environment |
| Layer | L4 GameFeatures |
| Endpoints | 32 generated |
| State Stores | environment-climate (MySQL), environment-overrides (MySQL), environment-weather (Redis), environment-conditions (Redis), environment-lock (Redis) |
| Events Published | 7 (`environment.conditions.changed`, `environment.weather-event.started`, `environment.weather-event.ended`, `environment.resource-availability.changed`, `environment.climate-template.created`, `environment.climate-template.updated`, `environment.climate-template.deleted`) |
| Events Consumed | 8 (`worldstate.period-changed`, `worldstate.season-changed`, `worldstate.day-changed`, `location.created`, `location.updated`, + 3 self-subscriptions for cache invalidation) |
| Client Events | 1 planned (`WeatherTransitionClientEvent` — not yet specified in schema) |
| Background Services | 2 (`EnvironmentConditionWorkerService`, `EnvironmentEventExpirationWorkerService`) |

---

## State

**Store**: `environment-climate` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `climate:{templateId}` | `ClimateTemplateModel` | Primary climate template lookup |
| `climate:biome:{gameServiceId}:{biomeCode}` | `ClimateTemplateModel` | Biome-based lookup within game scope |
| `climate:game:{gameServiceId}` | `string` (index) | All template IDs for a game service (listing/admin) |
| `binding:{bindingId}` | `LocationClimateBindingModel` | Primary location-climate binding lookup |
| `binding:location:{locationId}` | `LocationClimateBindingModel` | Binding lookup by locationId |
| `realm-config:{realmId}` | `RealmEnvironmentConfigModel` | Per-realm default biome configuration |

**Store**: `environment-overrides` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `event:{eventId}` | `WeatherEventModel` | Primary weather event lookup |
| `event:scope:{scopeType}:{scopeId}` | `WeatherEventListModel` | Active events for a scope (override resolution) |
| `event:source:{sourceType}:{sourceId}` | `WeatherEventListModel` | Events by source (actor cleanup) |

**Store**: `environment-weather` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `resolved:{realmId}:{locationId}:{gameDay}` | `ResolvedWeatherModel` | Cached resolved weather per location per game-day |

**Store**: `environment-conditions` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `snapshot:{realmId}:{locationId}` | `ConditionSnapshotModel` | Latest computed condition snapshot (short TTL) |
| `environment:active:{realmId}` | sorted set (score=timestamp) | Active location tracking for ActiveOnly refresh mode |

**Distributed Locks**: `environment-lock` (Backend: Redis)

| Lock Resource ID | Purpose |
|-----------------|---------|
| `environment:climate:{gameServiceId}:{biomeCode}` | Climate template mutations |
| `environment:event:{scopeType}:{scopeId}` | Weather event creation/cancellation per scope |
| `environment:refresh:{realmId}` | Per-realm condition refresh cycle exclusion |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 4 stores: environment-climate, environment-overrides, environment-weather, environment-conditions |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Locks for template mutations, event operations, realm refresh |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all environment events |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation for helpers and workers |
| lib-resource (`IResourceClient`) | L1 | Hard | Reference registration/unregistration for realm, game-service, location targets |
| lib-worldstate (`IWorldstateClient`) | L2 | Hard | Game time, season, season progress queries — foundational dependency |
| lib-location (`ILocationClient`) | L2 | Hard | Parent hierarchy for binding inheritance, existence validation, spatial coordinates |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service existence validation for template scoping |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm existence validation for SetRealmConfig |
| lib-character (`ICharacterClient`) | L2 | Hard | Character-to-location resolution in variable provider (fallback when actor context lacks locationId) |
| lib-mapping (`IMappingClient`) | L4 | Soft | Publishing weather data to Mapping's `weather_effects` spatial layer; graceful skip when absent |
| lib-analytics (`IAnalyticsClient`) | L4 | Soft | Environmental statistics publication; silent skip when absent |

**Notes:**
- `IServiceProvider` retained as field for runtime resolution of soft dependencies (`IMappingClient`, `IAnalyticsClient`)
- No account boundary involvement — no T28 account deletion obligation, no T32 concern
- Resource cleanup via 3 `x-references` entries (realm CASCADE, game-service CASCADE, location CASCADE)
- Self-event subscriptions for cross-node cache invalidation per Implementation Tenets (multi-instance safety)

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `environment.conditions.changed` | `EnvironmentConditionsChangedEvent` | ConditionWorker at weather segment boundaries; CreateWeatherEvent/CancelWeatherEvent (condition recompute) |
| `environment.weather-event.started` | `EnvironmentWeatherEventStartedEvent` | CreateWeatherEvent |
| `environment.weather-event.ended` | `EnvironmentWeatherEventEndedEvent` | CancelWeatherEvent, CancelWeatherEventsBySource (reason: "cancelled"/"source_removed"), ExpirationWorker (reason: "expired") |
| `environment.resource-availability.changed` | `EnvironmentResourceAvailabilityChangedEvent` | HandleSeasonChangedAsync; ConditionWorker when availability crosses `ResourceAvailabilityChangeThreshold` |
| `environment.climate-template.created` | `ClimateTemplateCreatedEvent` | SeedClimate, BulkSeedClimates (x-lifecycle) |
| `environment.climate-template.updated` | `ClimateTemplateUpdatedEvent` | UpdateClimate, DeprecateClimate, UndeprecateClimate (x-lifecycle, deprecation via changedFields) |
| `environment.climate-template.deleted` | `ClimateTemplateDeletedEvent` | DeleteClimate, CleanupByGameService (x-lifecycle) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.period-changed` | `HandlePeriodChangedAsync` | Primary trigger: acquires realm lock, re-computes condition snapshots per ConditionRefreshMode, publishes conditions-changed, publishes to Mapping |
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | Recalculates resource availability for realm, publishes resource-availability.changed, invalidates weather distributions and ClimateTemplateCache |
| `worldstate.day-changed` | `HandleDayChangedAsync` | Lightweight: invalidates day-keyed weather cache entries for realm; no recomputation |
| `location.created` | `HandleLocationCreatedAsync` | Creates inherited binding if parent has one; seeds initial condition snapshot |
| `location.updated` | `HandleLocationUpdatedAsync` | Re-evaluates inherited bindings on parentLocationId change |
| `environment.climate-template.updated` | ClimateTemplateCache self-sub | Cross-node in-memory cache invalidation |
| `environment.climate-template.deleted` | ClimateTemplateCache self-sub | Cross-node in-memory cache invalidation |
| `environment.conditions.changed` | ConditionSnapshotCache self-sub | Cross-node in-memory cache invalidation |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<EnvironmentService>` | Structured logging |
| `EnvironmentServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (4 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration |
| `IDistributedLockProvider` | Distributed locks |
| `ITelemetryProvider` | Span instrumentation |
| `IWorldstateClient` | Game time queries |
| `ILocationClient` | Location hierarchy and validation |
| `IGameServiceClient` | Game service validation |
| `IRealmClient` | Realm validation |
| `IResourceClient` | Reference tracking |
| `ICharacterClient` | Character-to-location resolution (variable provider fallback) |
| `IServiceProvider` | Soft dependency resolution |
| `IWeatherResolver` | Deterministic weather computation helper |
| `ITemperatureCalculator` | Temperature computation helper |
| `IClimateTemplateCache` | In-memory climate template cache |
| `IConditionSnapshotCache` | In-memory condition snapshot cache |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` | `Singleton` (as `EnvironmentProviderFactory`) | L4→L2 pull | Actor (L2) — provides `${environment.*}` variables |
| `ITransitCostModifierProvider` | `Singleton` (as `EnvironmentTransitCostModifierProvider`) | L4→L2 pull | Transit (L2) — weather-based travel cost modifiers |
| `IActionHandler` | `Singleton` (3 handlers: `RegisterWeatherEventActionHandler`, `CancelWeatherEventActionHandler`, `ExtendWeatherEventActionHandler`) | L4→L2 pull | Actor (L2) — ABML action handlers for divine weather manipulation |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| GetConditions | POST /environment/conditions/get | generated | user | snapshot (on miss) | - |
| GetConditionsByCode | POST /environment/conditions/get-by-code | generated | user | snapshot (on miss) | - |
| BatchGetConditions | POST /environment/conditions/batch-get | generated | user | snapshot (on miss) | - |
| GetTemperature | POST /environment/conditions/get-temperature | generated | user | snapshot (on miss) | - |
| SeedClimate | POST /environment/climate/seed | generated | developer | climate, biome-index, game-index | environment.climate-template.created |
| GetClimate | POST /environment/climate/get | generated | developer | - | - |
| ListClimates | POST /environment/climate/list | generated | developer | - | - |
| UpdateClimate | POST /environment/climate/update | generated | developer | climate, biome-index, conditions, weather | environment.climate-template.updated |
| DeprecateClimate | POST /environment/climate/deprecate | generated | developer | climate, biome-index | environment.climate-template.updated |
| UndeprecateClimate | POST /environment/climate/undeprecate | generated | developer | climate, biome-index | environment.climate-template.updated |
| DeleteClimate | POST /environment/climate/delete | generated | developer | climate, biome-index, bindings | environment.climate-template.deleted |
| BulkSeedClimates | POST /environment/climate/bulk-seed | generated | developer | climate, biome-index, game-index | environment.climate-template.created/updated |
| CreateWeatherEvent | POST /environment/weather-event/create | generated | developer | event, scope-index, source-index, conditions, weather | environment.weather-event.started |
| GetWeatherEvent | POST /environment/weather-event/get | generated | developer | - | - |
| ListWeatherEvents | POST /environment/weather-event/list | generated | developer | - | - |
| CancelWeatherEvent | POST /environment/weather-event/cancel | generated | developer | event, scope-index, conditions, weather | environment.weather-event.ended |
| ExtendWeatherEvent | POST /environment/weather-event/extend | generated | developer | event | - |
| CancelWeatherEventsBySource | POST /environment/weather-event/cancel-by-source | generated | developer | event, scope-index | environment.weather-event.ended |
| GetRealmWeatherSummary | POST /environment/weather/realm-summary | generated | developer | - | - |
| GetWeatherByRegion | POST /environment/weather/by-region | generated | developer | - | - |
| GetResourceAvailability | POST /environment/resource/get-availability | generated | user | - | - |
| GetRealmResourceSummary | POST /environment/resource/realm-summary | generated | user | - | - |
| CreateClimateBinding | POST /environment/climate-binding/create | generated | developer | binding, location-index, conditions, weather | - |
| GetClimateBinding | POST /environment/climate-binding/get | generated | developer | - | - |
| UpdateClimateBinding | POST /environment/climate-binding/update | generated | developer | binding, location-index, conditions, weather | - |
| DeleteClimateBinding | POST /environment/climate-binding/delete | generated | developer | binding, location-index, conditions, weather | - |
| BulkSeedBindings | POST /environment/climate-binding/bulk-seed | generated | developer | binding, location-index | - |
| SetRealmConfig | POST /environment/realm-config/set | generated | developer | realm-config | - |
| GetRealmConfig | POST /environment/realm-config/get | generated | developer | - | - |
| CleanupByRealm | POST /environment/cleanup-by-realm | generated | [] | events, conditions, bindings, realm-config, active-set | environment.weather-event.ended |
| CleanupByGameService | POST /environment/cleanup-by-game-service | generated | [] | climate, biome-index, game-index, bindings | environment.climate-template.deleted |
| CleanupByLocation | POST /environment/cleanup-by-location | generated | [] | binding, events, conditions, weather | environment.weather-event.ended |

---

## Methods

### GetConditions
POST /environment/conditions/get | Roles: [user]

```
READ _conditionCache (in-memory, 5s TTL) for snapshot:{realmId}:{locationId}
IF cache hit
  RETURN (200, ConditionSnapshotResponse from cache)
READ _conditionsStore snapshot:{realmId}:{locationId}        // Redis, 60s TTL
IF Redis hit
  UPDATE _conditionCache (in-memory)
  RETURN (200, ConditionSnapshotResponse from Redis)
// Full computation on double cache miss
READ _climateStore binding:location:{locationId}             -> resolve binding
IF no binding: walk parent hierarchy via ILocationClient
IF no binding in hierarchy: READ _climateStore realm-config:{realmId}
IF no realm config: use config.DefaultBiomeCode
IF no resolution possible                                    -> 404
READ _climateCache for resolved climate template
CALL IWorldstateClient.GetGameTimeSnapshotAsync(realmId)
CALL IWeatherResolver.ResolveAsync(binding, template, gameTime)
CALL ITemperatureCalculator.ComputeAsync(binding, template, gameTime, weather)
// Compose resource availability from seasonal baseline + weather impact + event modifiers
WRITE _conditionsStore snapshot:{realmId}:{locationId} <- ConditionSnapshotModel with TTL
UPDATE _conditionCache (in-memory)
IF config.ConditionRefreshMode == ActiveOnly
  UPDATE sorted set environment:active:{realmId} with locationId score=now
RETURN (200, ConditionSnapshotResponse)
```

### GetConditionsByCode
POST /environment/conditions/get-by-code | Roles: [user]

```
CALL ILocationClient.GetLocationByCodeAsync(realmCode, locationCode)  -> 404 if not found
// Delegate to same resolution path as GetConditions
// (see GetConditions for full computation flow)
RETURN (200, ConditionSnapshotResponse)
```

### BatchGetConditions
POST /environment/conditions/batch-get | Roles: [user]

```
// Group by realmId to share Worldstate calls
FOREACH unique realmId in request.LocationIds
  CALL IWorldstateClient.GetGameTimeSnapshotAsync(realmId)   // once per realm
FOREACH locationId in request.LocationIds
  READ _conditionCache / _conditionsStore snapshot:{realmId}:{locationId}
  IF miss: compute conditions (same path as GetConditions)
  WRITE _conditionsStore on miss with TTL
RETURN (200, BatchConditionSnapshotResponse with per-location snapshots)
```

### GetTemperature
POST /environment/conditions/get-temperature | Roles: [user]

```
READ _conditionCache / _conditionsStore snapshot:{realmId}:{locationId}
IF hit: extract temperature fields
IF miss: compute via TemperatureCalculator (lighter than full condition)
RETURN (200, TemperatureResponse { temperature, feelsLike, isFreezing, isHot })
RETURN (404, null)                                           // no resolvable template
```

### SeedClimate
POST /environment/climate/seed | Roles: [developer]

```
CALL IGameServiceClient.GameServiceExistsAsync(gameServiceId)  -> 404 if not found
LOCK environment-lock environment:climate:{gameServiceId}:{biomeCode}  -> 409 if fails
  READ _climateStore climate:biome:{gameServiceId}:{biomeCode}
  IF exists AND not deprecated                                 -> 409 duplicate
  COUNT templates in _climateStore climate:game:{gameServiceId}
  IF count >= config.MaxClimateTemplatesPerGameService         -> 400 at capacity
  // Validate: curves cover all seasons, weights positive, baselines complete
  IF validation fails                                          -> 400
  WRITE _climateStore climate:{templateId} <- new ClimateTemplateModel
  WRITE _climateStore climate:biome:{gameServiceId}:{biomeCode} <- index
  WRITE _climateStore climate:game:{gameServiceId} <- updated index
  CALL IResourceClient.RegisterReferenceAsync(templateId, "environment", gameServiceId, "game-service")
  PUBLISH environment.climate-template.created { full model }
RETURN (200, ClimateTemplateResponse)
```

### GetClimate
POST /environment/climate/get | Roles: [developer]

```
IF request.TemplateId provided
  READ _climateStore climate:{templateId}                      -> 404 if null
ELSE IF request.GameServiceId + request.BiomeCode provided
  READ _climateStore climate:biome:{gameServiceId}:{biomeCode} -> 404 if null
RETURN (200, ClimateTemplateResponse)
```

### ListClimates
POST /environment/climate/list | Roles: [developer]

```
QUERY _climateStore WHERE climate:game:{gameServiceId} PAGED(page, pageSize)
IF NOT request.IncludeDeprecated
  FILTER OUT isDeprecated == true
RETURN (200, PagedClimateTemplateResponse)
```

### UpdateClimate
POST /environment/climate/update | Roles: [developer]

```
LOCK environment-lock environment:climate:{gameServiceId}:{biomeCode}  -> 409 if fails
  READ _climateStore climate:{templateId} [with ETag]          -> 404 if null
  // Validate structural consistency of changed fields
  IF validation fails                                          -> 400
  // Apply partial update to model
  ETAG-WRITE _climateStore climate:{templateId}                -> 409 if ETag conflict
  WRITE _climateStore climate:biome:{gameServiceId}:{biomeCode} <- updated index
  // Invalidate caches for affected locations
  DELETE _conditionsStore snapshots for locations using this template
  DELETE _weatherCache entries for locations using this template
  PUBLISH environment.climate-template.updated { changedFields, full model }
RETURN (200, ClimateTemplateResponse)
```

### DeprecateClimate
POST /environment/climate/deprecate | Roles: [developer]

```
READ _climateStore climate:{templateId}                        -> 404 if null
IF already deprecated
  RETURN (200, ClimateTemplateResponse)                        // idempotent
WRITE _climateStore climate:{templateId} <- isDeprecated=true, deprecatedAt=now, deprecationReason=request.Reason
WRITE _climateStore climate:biome:{gameServiceId}:{biomeCode} <- updated index
PUBLISH environment.climate-template.updated { changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, ClimateTemplateResponse)
```

### UndeprecateClimate
POST /environment/climate/undeprecate | Roles: [developer]

```
READ _climateStore climate:{templateId}                        -> 404 if null
IF not deprecated
  RETURN (200, ClimateTemplateResponse)                        // idempotent
WRITE _climateStore climate:{templateId} <- isDeprecated=false, deprecatedAt=null, deprecationReason=null
WRITE _climateStore climate:biome:{gameServiceId}:{biomeCode} <- updated index
PUBLISH environment.climate-template.updated { changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, ClimateTemplateResponse)
```

### DeleteClimate
POST /environment/climate/delete | Roles: [developer]

```
READ _climateStore climate:{templateId}                        -> 404 if null
IF isDeprecated == false                                       -> 400 must deprecate first
// Internal cascade: delete all bindings referencing this template
// No lib-resource call needed — bindings are Environment's own internal data, not cross-service references
QUERY _climateStore for bindings WHERE climateTemplateId == templateId
FOREACH binding
  DELETE _climateStore binding:{bindingId}
  DELETE _climateStore binding:location:{locationId}
  DELETE _conditionsStore snapshot:{realmId}:{locationId}
DELETE _climateStore climate:{templateId}
DELETE _climateStore climate:biome:{gameServiceId}:{biomeCode}
// Remove from game index
PUBLISH environment.climate-template.deleted { full model, deletedReason }
RETURN (200, null)
```

### BulkSeedClimates
POST /environment/climate/bulk-seed | Roles: [developer]

```
CALL IGameServiceClient.GameServiceExistsAsync(gameServiceId)  -> 400 if not found
FOREACH template in request.Templates
  try
    READ _climateStore climate:biome:{gameServiceId}:{biomeCode}
    IF exists AND NOT request.UpdateExisting: skip
    IF exists AND request.UpdateExisting: update + PUBLISH updated
    IF not exists: create (same as SeedClimate) + PUBLISH created
    LOCK per biome code during write
  catch -> log Warning, increment failCount
RETURN (200, BulkSeedClimatesResponse { created, updated, skipped, failed })
```

### CreateWeatherEvent
POST /environment/weather-event/create | Roles: [developer]

```
// Validate scope exists
IF scopeType == Realm: CALL IRealmClient.RealmExistsAsync(scopeId)      -> 404
IF scopeType == Location/LocationSubtree: CALL ILocationClient.LocationExistsAsync(scopeId) -> 404
LOCK environment-lock environment:event:{scopeType}:{scopeId}           -> 409 if fails
  COUNT active events in _overridesStore event:scope:{scopeType}:{scopeId}
  IF count >= config.MaxActiveEventsPerScope                             -> 400 at capacity
  IF startGameTime == null
    CALL IWorldstateClient.GetCurrentGameTimeAsync(realmId)
    // Fill startGameTime from current game time (no sentinel values)
  // Validate: endGameTime > startGameTime if both set
  WRITE _overridesStore event:{eventId} <- new WeatherEventModel
  WRITE _overridesStore event:scope:{scopeType}:{scopeId} <- updated scope index
  WRITE _overridesStore event:source:{sourceType}:{sourceId} <- updated source index
  // Immediate condition cache invalidation for affected scope
  DELETE _conditionsStore snapshots for affected locations
  DELETE _weatherCache for affected locations
  PUBLISH environment.weather-event.started { eventId, eventCode, severity, scopeType, scopeId, sourceType }
RETURN (200, CreateWeatherEventResponse { eventId })
```

### GetWeatherEvent
POST /environment/weather-event/get | Roles: [developer]

```
READ _overridesStore event:{eventId}                           -> 404 if null
RETURN (200, WeatherEventResponse)
```

### ListWeatherEvents
POST /environment/weather-event/list | Roles: [developer]

```
READ _overridesStore event:scope:{scopeType}:{scopeId}
// Apply filters: activeOnly, eventCode, sourceType
RETURN (200, PagedWeatherEventResponse)
```

### CancelWeatherEvent
POST /environment/weather-event/cancel | Roles: [developer]

```
LOCK environment-lock environment:event:{scopeType}:{scopeId}  -> 409 if fails
  READ _overridesStore event:{eventId}                          -> 404 if null
  IF NOT isActive                                               -> 400 already inactive
  WRITE _overridesStore event:{eventId} <- isActive=false
  WRITE _overridesStore event:scope:{scopeType}:{scopeId} <- updated index
  DELETE _conditionsStore snapshots for affected locations
  DELETE _weatherCache for affected locations
  PUBLISH environment.weather-event.ended { eventId, eventCode, scopeType, scopeId, reason: "cancelled" }
RETURN (200, null)
```

### ExtendWeatherEvent
POST /environment/weather-event/extend | Roles: [developer]

```
READ _overridesStore event:{eventId}                           -> 404 if null
IF NOT isActive                                                -> 400 inactive
IF newEndGameTime <= current endGameTime                       -> 400 cannot shorten
WRITE _overridesStore event:{eventId} <- endGameTime=newEndGameTime
RETURN (200, WeatherEventResponse)
```

### CancelWeatherEventsBySource
POST /environment/weather-event/cancel-by-source | Roles: [developer]

```
READ _overridesStore event:source:{sourceType}:{sourceId}
FOREACH active event
  try
    WRITE _overridesStore event:{eventId} <- isActive=false
    WRITE _overridesStore event:scope:{scopeType}:{scopeId} <- updated index
    PUBLISH environment.weather-event.ended { eventId, eventCode, scopeType, scopeId, reason: "source_removed" }
  catch -> log Warning, increment failCount
// Cache invalidation for each affected scope
RETURN (200, CancelWeatherEventsBySourceResponse { cancelledCount })
```

### GetRealmWeatherSummary
POST /environment/weather/realm-summary | Roles: [developer]

```
CALL ILocationClient.ListLocationsByRealmAsync(realmId)
FOREACH locationId
  READ _conditionsStore snapshot:{realmId}:{locationId}
// Aggregate: location count per weatherCode, avg temperature, extremes, active event count
READ _overridesStore event:scope:Realm:{realmId}              // active event count
RETURN (200, RealmWeatherSummaryResponse)
RETURN (404, null)                                             // realm not found
```

### GetWeatherByRegion
POST /environment/weather/by-region | Roles: [developer]

```
CALL ILocationClient.GetLocationSubtreeAsync(locationId)       -> 404 if not found
FOREACH descendant locationId
  READ _conditionsStore snapshot:{realmId}:{locationId}
// Group by weatherCode with locationId lists
RETURN (200, WeatherByRegionResponse)
```

### GetResourceAvailability
POST /environment/resource/get-availability | Roles: [user]

```
READ _conditionCache / _conditionsStore snapshot:{realmId}:{locationId}
IF miss: compute via resource availability algorithm
  // baseSeasonalLevel + weatherImpactModifier + event modifiers -> clamp [0.0, 1.0]
RETURN (200, ResourceAvailabilityResponse { abundanceLevel, baseSeasonalLevel, weatherEventModifier, netResult })
RETURN (404, null)                                             // no resolvable template
```

### GetRealmResourceSummary
POST /environment/resource/realm-summary | Roles: [user]

```
CALL ILocationClient.ListLocationsByRealmAsync(realmId)        -> 404 if not found
FOREACH locationId
  READ _conditionsStore snapshot:{realmId}:{locationId}
  // Resolve biome from binding for grouping
// Group by biome: avg availability, min/max, location count
RETURN (200, RealmResourceSummaryResponse)
```

### CreateClimateBinding
POST /environment/climate-binding/create | Roles: [developer]

```
CALL ILocationClient.LocationExistsAsync(locationId)           -> 404 if not found
READ _climateStore binding:location:{locationId}
IF exists                                                      -> 409 already bound
READ _climateStore climate:biome:{gameServiceId}:{biomeCode}   -> 404 if template not found
IF template.IsDeprecated                                       -> 400 deprecated template
WRITE _climateStore binding:{bindingId} <- new LocationClimateBindingModel { altitude from Location }
WRITE _climateStore binding:location:{locationId} <- index
CALL IResourceClient.RegisterReferenceAsync(bindingId, "environment", locationId, "location")
DELETE _conditionsStore snapshot:{realmId}:{locationId}         // cache invalidation
DELETE _weatherCache resolved:{realmId}:{locationId}:*
RETURN (200, ClimateBindingResponse)
```

### GetClimateBinding
POST /environment/climate-binding/get | Roles: [developer]

```
READ _climateStore binding:location:{locationId}               -> 404 if not found
RETURN (200, ClimateBindingResponse)
```

### UpdateClimateBinding
POST /environment/climate-binding/update | Roles: [developer]

```
READ _climateStore binding:location:{locationId}               -> 404 if not found
IF biomeCode changed
  READ _climateStore climate:biome:{gameServiceId}:{biomeCode} -> 404 if not found
  IF template.IsDeprecated                                     -> 400 deprecated template
WRITE _climateStore binding:{bindingId} <- updated
WRITE _climateStore binding:location:{locationId} <- updated
// Invalidate caches for this location AND inheriting children
DELETE _conditionsStore snapshots for location + children
DELETE _weatherCache for location
RETURN (200, ClimateBindingResponse)
```

### DeleteClimateBinding
POST /environment/climate-binding/delete | Roles: [developer]

```
READ _climateStore binding:location:{locationId}               -> 404 if not found
DELETE _climateStore binding:{bindingId}
DELETE _climateStore binding:location:{locationId}
CALL IResourceClient.UnregisterReferenceAsync(bindingId)
DELETE _conditionsStore snapshot:{realmId}:{locationId}
DELETE _weatherCache resolved:{realmId}:{locationId}:*
// Inheriting children fall back to next ancestor or realm default
RETURN (200, null)
```

### BulkSeedBindings
POST /environment/climate-binding/bulk-seed | Roles: [developer]

```
FOREACH binding in request.Bindings
  try
    CALL ILocationClient.LocationExistsAsync(locationId)
    READ _climateStore binding:location:{locationId}
    IF exists AND NOT request.UpdateExisting: skip
    IF exists AND request.UpdateExisting: update
    IF not exists: create + register reference
  catch -> log Warning, increment failCount
RETURN (200, BulkSeedBindingsResponse { created, updated, skipped, failed })
```

### SetRealmConfig
POST /environment/realm-config/set | Roles: [developer]

```
CALL IRealmClient.RealmExistsAsync(realmId)                    -> 404 if not found
READ _climateStore climate:biome:{gameServiceId}:{biomeCode}   -> 400 if not found or deprecated
READ _climateStore realm-config:{realmId}
IF exists: update
IF not exists: create + CALL IResourceClient.RegisterReferenceAsync(realmId, "environment", realmId, "realm")
WRITE _climateStore realm-config:{realmId} <- RealmEnvironmentConfigModel
RETURN (200, RealmEnvironmentConfigResponse)                   // idempotent
```

### GetRealmConfig
POST /environment/realm-config/get | Roles: [developer]

```
READ _climateStore realm-config:{realmId}                      -> 404 if not set
// Does NOT return DefaultBiomeCode fallback — only explicitly set configs
RETURN (200, RealmEnvironmentConfigResponse)
```

### CleanupByRealm
POST /environment/cleanup-by-realm | Roles: []

```
// Called by lib-resource CASCADE callback
READ _overridesStore event:scope:Realm:{realmId}
FOREACH active event
  try
    DELETE event, publish environment.weather-event.ended { reason: "source_removed" }
  catch -> log Warning
// Delete all location-scoped events within this realm
DELETE _conditionsStore all snapshot:{realmId}:*
DELETE sorted set environment:active:{realmId}
// Delete all bindings in this realm
FOREACH binding in realm
  try
    DELETE _climateStore binding:{bindingId} and binding:location:{locationId}
    CALL IResourceClient.UnregisterReferenceAsync(bindingId)
  catch -> log Warning
DELETE _climateStore realm-config:{realmId}
// Climate templates are NOT removed (game-scoped, not realm-scoped)
RETURN (200, null)
```

### CleanupByGameService
POST /environment/cleanup-by-game-service | Roles: []

```
// Called by lib-resource CASCADE callback
READ _climateStore climate:game:{gameServiceId}
FOREACH template
  try
    DELETE _climateStore climate:{templateId}
    DELETE _climateStore climate:biome:{gameServiceId}:{biomeCode}
    PUBLISH environment.climate-template.deleted { templateId, deletedReason: "game-service-cleanup" }
  catch -> log Warning
// Delete all bindings and derived data for this game service
RETURN (200, null)
```

### CleanupByLocation
POST /environment/cleanup-by-location | Roles: []

```
// Called by lib-resource CASCADE callback — NOT via location.deleted event
READ _climateStore binding:location:{locationId}
IF binding exists
  DELETE _climateStore binding:{bindingId}
  DELETE _climateStore binding:location:{locationId}
  CALL IResourceClient.UnregisterReferenceAsync(bindingId)
// Delete location-scoped weather events
READ _overridesStore event:scope:Location:{locationId}
READ _overridesStore event:scope:LocationSubtree:{locationId}
FOREACH active event
  try
    WRITE event isActive=false
    PUBLISH environment.weather-event.ended { reason: "source_removed" }
  catch -> log Warning
DELETE _conditionsStore snapshot:{realmId}:{locationId}
DELETE _weatherCache resolved:{realmId}:{locationId}:*
RETURN (200, null)
```

---

## Background Services

### EnvironmentConditionWorkerService
**Interval**: `config.ConditionUpdateIntervalSeconds` (default 30s)
**Startup Delay**: `config.ConditionUpdateStartupDelaySeconds` (default 15s)
**Purpose**: Per-realm periodic condition refresh with distributed lock exclusion

```
// Canonical BackgroundService polling loop per IMPLEMENTATION TENETS
FOREACH realm in active realms
  LOCK environment-lock environment:refresh:{realmId}
    CALL IWorldstateClient.GetGameTimeSnapshotAsync(realmId)   // once per realm per cycle
    IF config.ConditionRefreshMode == AllLocations
      locations = all locations in realm (via ILocationClient or index)
    ELSE // ActiveOnly
      locations = sorted set environment:active:{realmId} within ActiveLocationWindowMinutes
    FOREACH location in locations
      try
        previousSnapshot = READ _conditionsStore snapshot:{realmId}:{locationId}
        newSnapshot = compute conditions (WeatherResolver + TemperatureCalculator)
        WRITE _conditionsStore snapshot:{realmId}:{locationId} <- newSnapshot with TTL
        UPDATE _conditionCache (in-memory)
        IF weather or temperature changed meaningfully vs. previousSnapshot
          PUBLISH environment.conditions.changed { realmId, locationId, previous, current }
        IF resourceAvailability changed > config.ResourceAvailabilityChangeThreshold
          PUBLISH environment.resource-availability.changed { realmId, previous, current, season }
      catch -> log Warning, continue
    // Mapping publication (soft dependency)
    IF IMappingClient available AND config.MappingPublishEnabled
      changedSnapshots = snapshots that changed this cycle (limit: config.MappingPublishBatchSize)
      CALL IMappingClient.IngestBatchAsync(channel: "weather_effects", changedSnapshots)
```

### EnvironmentEventExpirationWorkerService
**Interval**: `config.EventExpirationCheckIntervalSeconds` (default 60s)
**Purpose**: Scan and deactivate expired weather events

```
// Canonical BackgroundService polling loop per IMPLEMENTATION TENETS
CALL IWorldstateClient.GetCurrentGameTimeAsync() for each realm
QUERY _overridesStore for active events with endGameTime < currentGameTime
FOREACH expired event
  try
    WRITE _overridesStore event:{eventId} <- isActive=false
    WRITE _overridesStore event:scope:{scopeType}:{scopeId} <- updated index
    DELETE _conditionsStore snapshots for affected scope
    DELETE _weatherCache for affected scope
    PUBLISH environment.weather-event.ended { eventId, eventCode, scopeType, scopeId, reason: "expired" }
  catch -> log Warning, continue
```

---

## Non-Standard Implementation Patterns

#### Plugin Lifecycle (OnRunningAsync)

```
// Register resource cleanup callbacks with lib-resource (generated RegisterResourceCleanupCallbacksAsync)
CALL RegisterResourceCleanupCallbacksAsync()
  // Registers: /environment/cleanup-by-realm, /environment/cleanup-by-game-service, /environment/cleanup-by-location

// Validate Worldstate is reachable (hard dependency)
// Background workers begin after their respective startup delays
```

#### DI Registrations (ConfigureServices)

- `EnvironmentProviderFactory` as `IVariableProviderFactory` (Singleton)
- `EnvironmentTransitCostModifierProvider` as `ITransitCostModifierProvider` (Singleton)
- `RegisterWeatherEventActionHandler` as `IActionHandler` (Singleton)
- `CancelWeatherEventActionHandler` as `IActionHandler` (Singleton)
- `ExtendWeatherEventActionHandler` as `IActionHandler` (Singleton)
- `IWeatherResolver` / `WeatherResolver` (Scoped)
- `ITemperatureCalculator` / `TemperatureCalculator` (Scoped)
- `IClimateTemplateCache` / `ClimateTemplateCache` (Singleton)
- `IConditionSnapshotCache` / `ConditionSnapshotCache` (Singleton)
- `EnvironmentConditionWorkerService` as `IHostedService`
- `EnvironmentEventExpirationWorkerService` as `IHostedService`
