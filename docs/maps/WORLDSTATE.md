# Worldstate Implementation Map

> **Plugin**: lib-worldstate
> **Schema**: schemas/worldstate-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/WORLDSTATE.md](../plugins/WORLDSTATE.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-worldstate |
| Layer | L2 GameFoundation |
| Endpoints | 18 |
| State Stores | worldstate-realm-clock (Redis), worldstate-calendar (MySQL), worldstate-ratio-history (MySQL), worldstate-lock (Redis) |
| Events Published | 15 (6 boundary, 3 administrative, 6 lifecycle) |
| Events Consumed | 5 (all self-subscriptions for cross-node cache invalidation) |
| Client Events | 1 (worldstate.time-sync) |
| Background Services | 1 (WorldstateClockWorkerService) |

---

## State

**Store**: `worldstate-realm-clock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmClockModel` | Current game time for a realm. Hot-read path for variable provider and all clock queries. Updated every tick by the clock worker. |

**Store**: `worldstate-calendar` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `calendar:{gameServiceId}:{templateCode}` | `CalendarTemplateModel` | Calendar structure definition (day periods, months, seasons, era labels). Cached in-memory via `ICalendarTemplateCache`. |
| `realm-config:{realmId}` | `RealmWorldstateConfigModel` | Per-realm configuration: calendar template, time ratio, downtime policy, epoch. Links realm to its calendar. |

**Store**: `worldstate-ratio-history` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ratio:{realmId}` | `TimeRatioHistoryModel` | Ordered list of `TimeRatioSegment` entries. Used by `GetElapsedGameTime` for piecewise integration across ratio changes and pauses. Append-only. |

**Locks**: `worldstate-lock` (Backend: Redis)

| Lock Resource Key | Purpose |
|-------------------|---------|
| `clock:{realmId}` | Serializes clock advancement (worker ticks), ratio changes, admin advances, clock initialization, and realm config updates. |
| `calendar:{gameServiceId}:{templateCode}` | Calendar template mutation lock (update, delete). |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Realm clocks (Redis), calendars + realm configs (MySQL), ratio history (MySQL) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Per-realm clock locks, per-calendar mutation locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | All boundary, administrative, and lifecycle events |
| lib-messaging (`IEventConsumer`) | L0 | Hard | 5 self-subscriptions for cross-node cache invalidation |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| lib-resource (`IResourceClient`) | L1 | Hard | Reference registration/unregistration for realm and game-service targets (T28 cleanup) |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Publishes `WorldstateTimeSyncClientEvent` to realm-observing sessions |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm existence validation and code-to-ID resolution |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service existence validation during calendar seeding |

**DI Provider**: Implements `IVariableProviderFactory` via `WorldProviderFactory` (singleton). Provides `${world.*}` variables to Actor (L2) via the Variable Provider Factory pattern. Reads from `IRealmClockCache` and `ICalendarTemplateCache` -- no service API calls required.

**No soft dependencies.** All consumers depend on Worldstate, not the other way around.

---

## Events Published

### Boundary Events (clock worker + AdvanceClock)

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.hour-changed` | `WorldstateHourChangedEvent` | Game-hour boundary crossed. Suppressed during catch-up. |
| `worldstate.period-changed` | `WorldstatePeriodChangedEvent` | Day-period boundary crossed (e.g., dawn to morning). |
| `worldstate.day-changed` | `WorldstateDayChangedEvent` | Game-day boundary crossed. Includes `daysCrossed` and `isCatchUp`. |
| `worldstate.month-changed` | `WorldstateMonthChangedEvent` | Month boundary crossed. Includes `monthsCrossed`. |
| `worldstate.season-changed` | `WorldstateSeasonChangedEvent` | Season boundary crossed. Includes `currentYear`. |
| `worldstate.year-changed` | `WorldstateYearChangedEvent` | Year boundary crossed. Includes `yearsCrossed`. |

### Administrative Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.ratio-changed` | `WorldstateRatioChangedEvent` | `SetTimeRatio` -- includes previous/new ratio and reason. |
| `worldstate.realm-clock.initialized` | `WorldstateRealmClockInitializedEvent` | `InitializeRealmClock` -- includes template code and initial ratio. |
| `worldstate.clock-advanced` | `WorldstateClockAdvancedEvent` | `AdvanceClock` -- signals cross-node cache invalidation. |

### Lifecycle Events (x-lifecycle)

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.calendar-template.created` | `CalendarTemplateCreatedEvent` | `SeedCalendar` |
| `worldstate.calendar-template.updated` | `CalendarTemplateUpdatedEvent` | `UpdateCalendar` -- includes `changedFields`. |
| `worldstate.calendar-template.deleted` | `CalendarTemplateDeletedEvent` | `DeleteCalendar`, `CleanupByGameService` |
| `worldstate.realm-config.created` | `RealmConfigCreatedEvent` | `InitializeRealmClock` |
| `worldstate.realm-config.updated` | `RealmConfigUpdatedEvent` | `SetTimeRatio`, `UpdateRealmConfig` -- includes `changedFields`. |
| `worldstate.realm-config.deleted` | `RealmConfigDeletedEvent` | `CleanupByRealm` |

---

## Events Consumed

All consumed events are self-subscriptions for cross-node in-memory cache invalidation.

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.calendar-template.updated` | `HandleCalendarTemplateUpdatedAsync` | Invalidates `ICalendarTemplateCache` entry |
| `worldstate.calendar-template.deleted` | `HandleCalendarTemplateDeletedAsync` | Invalidates `ICalendarTemplateCache` entry |
| `worldstate.ratio-changed` | `HandleRatioChangedAsync` | Invalidates `IRealmClockCache` entry |
| `worldstate.realm-config.deleted` | `HandleRealmConfigDeletedAsync` | Invalidates `IRealmClockCache` entry |
| `worldstate.clock-advanced` | `HandleClockAdvancedAsync` | Invalidates `IRealmClockCache` entry |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<WorldstateService>` | Structured logging |
| `WorldstateServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (constructor-consumed, not stored) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Self-subscription registration (constructor-consumed, not stored) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ITelemetryProvider` | Span instrumentation |
| `IResourceClient` | Reference registration/unregistration (L1) |
| `IEntitySessionRegistry` | Client event publishing to realm sessions (L1) |
| `IRealmClient` | Realm validation and code resolution (L2) |
| `IGameServiceClient` | Game service validation (L2) |
| `IWorldstateTimeCalculator` | Pure calendar math (advance, resolve periods/seasons, decompose seconds) |
| `ICalendarTemplateCache` | In-memory TTL cache for calendar templates (MySQL) |
| `IRealmClockCache` | In-memory TTL cache for realm clock state (Redis) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetRealmTime | POST /worldstate/clock/get-realm-time | user | - | - |
| GetRealmTimeByCode | POST /worldstate/clock/get-realm-time-by-code | user | - | - |
| BatchGetRealmTimes | POST /worldstate/clock/batch-get-realm-times | user | - | - |
| GetElapsedGameTime | POST /worldstate/clock/get-elapsed-game-time | user | - | - |
| TriggerTimeSync | POST /worldstate/clock/trigger-sync | user | - | - (pushes client event) |
| InitializeRealmClock | POST /worldstate/clock/initialize | developer | clock, realm-config, ratio-history | worldstate.realm-clock.initialized, worldstate.realm-config.created |
| SetTimeRatio | POST /worldstate/clock/set-ratio | developer | clock, realm-config, ratio-history | boundary events, worldstate.ratio-changed, worldstate.realm-config.updated |
| AdvanceClock | POST /worldstate/clock/advance | developer | clock | boundary events, worldstate.clock-advanced |
| SeedCalendar | POST /worldstate/calendar/seed | developer | calendar | worldstate.calendar-template.created |
| GetCalendar | POST /worldstate/calendar/get | developer | - | - |
| ListCalendars | POST /worldstate/calendar/list | developer | - | - |
| UpdateCalendar | POST /worldstate/calendar/update | developer | calendar | worldstate.calendar-template.updated |
| DeleteCalendar | POST /worldstate/calendar/delete | developer | calendar | worldstate.calendar-template.deleted |
| GetRealmConfig | POST /worldstate/realm-config/get | developer | - | - |
| UpdateRealmConfig | POST /worldstate/realm-config/update | developer | realm-config, clock | worldstate.realm-config.updated |
| ListRealmClocks | POST /worldstate/realm-config/list | developer | - | - |
| CleanupByRealm | POST /worldstate/cleanup-by-realm | [] | clock, ratio-history, realm-config | worldstate.realm-config.deleted |
| CleanupByGameService | POST /worldstate/cleanup-by-game-service | [] | calendar | worldstate.calendar-template.deleted |

---

## Methods

### GetRealmTime
POST /worldstate/clock/get-realm-time | Roles: [user]

```
READ _clockStore:realm:{realmId}                     -> 404 if null
RETURN (200, GameTimeSnapshot from clock)
```

### GetRealmTimeByCode
POST /worldstate/clock/get-realm-time-by-code | Roles: [user]

```
CALL IRealmClient.GetRealmByCodeAsync(realmCode)     -> propagate upstream error
// Delegates to GetRealmTime with resolved realmId
READ _clockStore:realm:{realmId}                     -> 404 if null
RETURN (200, GameTimeSnapshot from clock)
```

### BatchGetRealmTimes
POST /worldstate/clock/batch-get-realm-times | Roles: [user]

```
IF realmIds.Count > config.MaxBatchRealmTimeQueries  -> 400
FOREACH realmId in realmIds
  READ _clockStore:realm:{realmId}
  IF null: add to notFoundRealmIds
  ELSE: add snapshot to results
RETURN (200, BatchGetRealmTimesResponse { snapshots, notFoundRealmIds })
```

### GetElapsedGameTime
POST /worldstate/clock/get-elapsed-game-time | Roles: [user]

```
IF fromRealTime >= toRealTime                        -> 400
READ _ratioHistoryStore:ratio:{realmId}              -> 404 if null
READ _realmConfigStore:realm-config:{realmId}        -> 404 if null
READ _calendarStore:calendar:{gsId}:{templateCode}   -> 404 if null
// Piecewise integration over ratio segments
totalGameSeconds = timeCalculator.ComputeElapsedGameTime(segments, from, to)
decomposed = timeCalculator.DecomposeGameSeconds(calendar, totalGameSeconds)
RETURN (200, GetElapsedGameTimeResponse { totalGameSeconds, gameDays, gameHours, gameMinutes })
```

### TriggerTimeSync
POST /worldstate/clock/trigger-sync | Roles: [user]

```
READ _clockStore:realm:{realmId}                     -> 404 if null
// Build time sync client event from clock snapshot
PUSH entitySessionRegistry.PublishToEntitySessionsAsync("realm", entityId, timeSyncEvent)
RETURN (200, TriggerTimeSyncResponse { sessionsNotified })
```

### InitializeRealmClock
POST /worldstate/clock/initialize | Roles: [developer]

```
CALL IRealmClient.GetRealmAsync(realmId)             -> propagate upstream error
// Resolve template code: request ?? config.DefaultCalendarTemplateCode
IF no template code available                        -> 400
READ _calendarStore:calendar:{gsId}:{templateCode}   -> 404 if null
LOCK worldstate-lock:clock:{realmId}                 -> 409 if fails
  READ _clockStore:realm:{realmId}
  IF clock exists                                    -> 409 (already initialized)
  // Build RealmWorldstateConfigModel, RealmClockModel, TimeRatioHistoryModel
  // with fallbacks: timeRatio ?? config.DefaultTimeRatio, epoch ?? UtcNow,
  // startingYear ?? 1, downtimePolicy ?? config.DefaultDowntimePolicy
  WRITE _realmConfigStore:realm-config:{realmId} <- RealmWorldstateConfigModel
  WRITE _clockStore:realm:{realmId} <- RealmClockModel
  WRITE _ratioHistoryStore:ratio:{realmId} <- TimeRatioHistoryModel (initial segment)
  CALL IResourceClient.RegisterReferenceAsync(realm target)
  PUBLISH worldstate.realm-clock.initialized { realmId, calendarTemplateCode, initialTimeRatio }
  PUBLISH worldstate.realm-config.created { realmId, gameServiceId, calendarTemplateCode, ... }
RETURN (200, InitializeRealmClockResponse { gameServiceId, calendarTemplateCode, timeRatio, downtimePolicy, epoch, startingYear })
```

### SetTimeRatio
POST /worldstate/clock/set-ratio | Roles: [developer]

```
IF newRatio < 0.0                                    -> 400
LOCK worldstate-lock:clock:{realmId}                 -> 409 if fails
  READ _clockStore:realm:{realmId}                   -> 404 if null
  READ _calendarStore:calendar:{gsId}:{templateCode} -> 404 if null
  // Materialize current clock: compute game-seconds since last advance
  // Advance clock to "now" before applying new ratio
  boundaries = timeCalculator.AdvanceGameTime(clock, calendar, elapsedGameSeconds)
  // Publish any boundary events from materialization
  IF boundaries exist
    PUBLISH boundary events (hour/period/day/month/season/year as applicable)
  // Apply new ratio
  previousRatio = clock.TimeRatio
  clock.TimeRatio = newRatio
  clock.IsActive = (newRatio > 0.0)
  // Append new segment to ratio history
  READ _ratioHistoryStore:ratio:{realmId}
  IF ratioHistory != null
    // Append TimeRatioSegmentEntry
    WRITE _ratioHistoryStore:ratio:{realmId} <- updated history
  READ _realmConfigStore:realm-config:{realmId}
  IF realmConfig != null
    realmConfig.CurrentTimeRatio = newRatio
    WRITE _realmConfigStore:realm-config:{realmId} <- updated config
    PUBLISH worldstate.realm-config.updated { changedFields: ["currentTimeRatio"] }
  WRITE _clockStore:realm:{realmId} <- updated clock
  PUBLISH worldstate.ratio-changed { realmId, previousRatio, newRatio, reason }
  PUSH entitySessionRegistry.PublishToEntitySessionsAsync("realm", realmId, timeSyncEvent { RatioChanged })
  // Invalidate local cache
RETURN (200, SetTimeRatioResponse { previousRatio })
```

### AdvanceClock
POST /worldstate/clock/advance | Roles: [developer]

```
LOCK worldstate-lock:clock:{realmId}                 -> 409 if fails
  READ _clockStore:realm:{realmId}                   -> 404 if null
  READ _realmConfigStore:realm-config:{realmId}      -> 404 if null
  READ _calendarStore:calendar:{gsId}:{templateCode} -> 404 if null
  // Convert gameYears/gameMonths/gameDays to game-seconds; add gameSeconds
  IF no time parameters provided                     -> 400
  IF totalGameSeconds <= 0                           -> 400
  previousSnapshot = current GameTimeSnapshot
  boundaries = timeCalculator.AdvanceGameTime(clock, calendar, totalGameSeconds)
  // Publish boundary events (capped at config.BoundaryEventBatchSize)
  FOREACH boundary in boundaries (up to batch limit)
    PUBLISH worldstate.{boundary-type}-changed { ... }
  WRITE _clockStore:realm:{realmId} <- updated clock (LastAdvancedRealTime = UtcNow)
  PUBLISH worldstate.clock-advanced { realmId }
  PUSH entitySessionRegistry.PublishToEntitySessionsAsync("realm", realmId, timeSyncEvent { AdminAdvance })
  // Invalidate local cache
RETURN (200, AdvanceClockResponse { previousTime, newTime, boundaryEventsPublished })
```

### SeedCalendar
POST /worldstate/calendar/seed | Roles: [developer]

```
CALL IGameServiceClient.GetServiceAsync(gameServiceId)   -> propagate upstream error
READ _calendarStore:calendar:{gsId}:{templateCode}       -> 409 if exists
QUERY _queryableCalendarStore WHERE GameServiceId == gsId
IF count >= config.MaxCalendarsPerGameService            -> 400
// Validate calendar structure: day period coverage, season ordinals, month-season refs
IF validation fails                                      -> 400
// Compute derived fields: daysPerYear, monthsPerYear, seasonsPerYear
WRITE _calendarStore:calendar:{gsId}:{templateCode} <- CalendarTemplateModel
CALL IResourceClient.RegisterReferenceAsync(game-service target)
PUBLISH worldstate.calendar-template.created { templateCode, gameServiceId, ... }
RETURN (200, CalendarTemplateResponse)
```

### GetCalendar
POST /worldstate/calendar/get | Roles: [developer]

```
READ _calendarStore:calendar:{gsId}:{templateCode}   -> 404 if null
RETURN (200, CalendarTemplateResponse)
```

### ListCalendars
POST /worldstate/calendar/list | Roles: [developer]

```
QUERY _queryableCalendarStore WHERE GameServiceId == gameServiceId
RETURN (200, ListCalendarsResponse { templates })
```

### UpdateCalendar
POST /worldstate/calendar/update | Roles: [developer]

```
LOCK worldstate-lock:calendar:{gsId}:{templateCode}  -> 409 if fails
  READ _calendarStore:calendar:{gsId}:{templateCode} -> 404 if null
  // Apply non-null fields from request, track changedFields
  IF changedFields.Count == 0
    RETURN (200, CalendarTemplateResponse)            // no-op
  // Recompute derived fields if structure changed
  IF validation fails                                -> 400
  WRITE _calendarStore:calendar:{gsId}:{templateCode} <- updated model
  PUBLISH worldstate.calendar-template.updated { ..., changedFields }
RETURN (200, CalendarTemplateResponse)
```

### DeleteCalendar
POST /worldstate/calendar/delete | Roles: [developer]

```
LOCK worldstate-lock:calendar:{gsId}:{templateCode}  -> 409 if fails
  READ _calendarStore:calendar:{gsId}:{templateCode} -> 404 if null
  QUERY _queryableRealmConfigStore WHERE CalendarTemplateCode == templateCode AND GameServiceId == gsId
  IF referencing configs exist                       -> 409 (in use)
  CALL IResourceClient.UnregisterReferenceAsync(game-service target)
  DELETE _calendarStore:calendar:{gsId}:{templateCode}
  PUBLISH worldstate.calendar-template.deleted { templateCode, gameServiceId, ... }
RETURN (200, DeleteCalendarResponse)
```

### GetRealmConfig
POST /worldstate/realm-config/get | Roles: [developer]

```
READ _realmConfigStore:realm-config:{realmId}        -> 404 if null
READ _clockStore:realm:{realmId}                     // enrichment for IsActive
// If clock null: warn, IsActive = false
RETURN (200, RealmConfigResponse { ..., isActive: clock?.IsActive ?? false })
```

### UpdateRealmConfig
POST /worldstate/realm-config/update | Roles: [developer]

```
LOCK worldstate-lock:clock:{realmId}                 -> 409 if fails
  READ _realmConfigStore:realm-config:{realmId}      -> 404 if null
  // Apply non-null fields: downtimePolicy, calendarTemplateCode
  IF calendarTemplateCode changed
    READ _calendarStore:calendar:{gsId}:{newCode}    -> 404 if null
  IF changedFields.Count == 0
    READ _clockStore:realm:{realmId}                 // for IsActive enrichment
    RETURN (200, RealmConfigResponse)                // no-op
  WRITE _realmConfigStore:realm-config:{realmId} <- updated config
  // Sync changed fields to Redis clock model
  READ _clockStore:realm:{realmId}
  IF clock != null
    WRITE _clockStore:realm:{realmId} <- synced clock
  PUBLISH worldstate.realm-config.updated { realmId, ..., changedFields }
RETURN (200, RealmConfigResponse)
```

### ListRealmClocks
POST /worldstate/realm-config/list | Roles: [developer]

```
IF gameServiceId provided
  QUERY _queryableRealmConfigStore WHERE GameServiceId == gsId PAGED(page, pageSize)
ELSE
  QUERY _queryableRealmConfigStore PAGED(page, pageSize)
FOREACH config in pagedResult.Items
  READ _clockStore:realm:{config.RealmId}
  IF clock null: skip with warning
  ELSE: build RealmClockSummary
RETURN (200, ListRealmClocksResponse { items, totalCount })
```

### CleanupByRealm
POST /worldstate/cleanup-by-realm | Roles: [] (service-to-service)

```
READ _realmConfigStore:realm-config:{realmId}        // for event payload; nullable
DELETE _clockStore:realm:{realmId}
DELETE _ratioHistoryStore:ratio:{realmId}
DELETE _realmConfigStore:realm-config:{realmId}
CALL IResourceClient.UnregisterReferenceAsync(realm target)
IF realmConfig != null
  PUBLISH worldstate.realm-config.deleted { realmId, ... }
// Invalidate local caches
RETURN (200, CleanupByRealmResponse)
```

### CleanupByGameService
POST /worldstate/cleanup-by-game-service | Roles: [] (service-to-service)

```
QUERY _queryableCalendarStore WHERE GameServiceId == gameServiceId
FOREACH template in calendars
  DELETE _calendarStore:calendar:{gsId}:{template.TemplateCode}
  CALL IResourceClient.UnregisterReferenceAsync(game-service target)
  PUBLISH worldstate.calendar-template.deleted { templateCode, gameServiceId, ... }
  // Invalidate calendar cache entry
RETURN (200, CleanupByGameServiceResponse)
```

---

## Background Services

### WorldstateClockWorkerService
**Interval**: `config.ClockTickIntervalSeconds` (default: 5 seconds)
**Startup Delay**: `config.ClockTickStartupDelaySeconds` (default: 10 seconds)
**Purpose**: Advances all active realm clocks and publishes boundary events on game-time transitions.

```
// On each tick:
QUERY _queryableRealmConfigStore WHERE _ => true     // all realm configs
IF none found: return

FOREACH config in realmConfigs
  // ProcessRealmTickAsync (per-realm, failure isolated)
  LOCK worldstate-lock:clock:{config.RealmId}
    // Skip if lock fails (another node processing)
    READ _clockStore:realm:{config.RealmId}
    IF clock null or not active or ratio <= 0: skip

    calendar = calendarCache.GetOrLoadAsync(gsId, templateCode)
    IF calendar null: skip

    // Compute elapsed game-seconds
    gameSeconds = (now - clock.LastAdvancedRealTime).TotalSeconds * clock.TimeRatio
    IF gameSeconds <= 0: skip

    // Handle catch-up (large gap after downtime)
    IF gameSeconds > maxCatchUpGameSeconds
      IF DowntimePolicy.Advance: cap at max
      IF DowntimePolicy.Pause: reset LastAdvancedRealTime = now, WRITE clock, skip

    // Advance clock via time calculator
    boundaries = timeCalculator.AdvanceGameTime(clock, calendar, gameSeconds)

    // Filter boundaries: suppress hour-changed during catch-up
    // Cap at config.BoundaryEventBatchSize

    // Publish boundary events
    FOREACH boundary (up to batch limit)
      PUBLISH worldstate.{type}-changed { realmId, snapshot, ... }

    // Push client time sync on period change
    IF period boundary crossed
      PUSH entitySessionRegistry.PublishToEntitySessionsAsync("realm", realmId,
        timeSyncEvent { SyncReason: PeriodChanged, previousPeriod })

    // Update state
    clock.LastAdvancedRealTime = now
    WRITE _clockStore:realm:{config.RealmId} <- updated clock
    // Update in-memory cache for variable provider
    clockCache.Update(config.RealmId, clock)
```
