# Worldstate Plugin Deep Dive

> **Plugin**: lib-worldstate
> **Schema**: schemas/worldstate-api.yaml
> **Version**: 1.0.0
> **State Stores**: worldstate-realm-clock (Redis), worldstate-calendar (MySQL), worldstate-ratio-history (MySQL)
> **Status**: Pre-implementation (architectural specification)

---

## Overview

Per-realm game time authority, calendar system, and temporal event broadcasting service (L2 GameFoundation). Maps real-world time to configurable game-time progression with per-realm time ratios, calendar templates (configurable days, months, seasons, years as opaque strings), and day-period cycles (dawn, morning, afternoon, evening, night). Publishes boundary events at game-time transitions (new day, season, year, period) consumed by other services for time-aligned processing. Provides the `${world.*}` variable namespace to the Actor service's behavior system via the Variable Provider Factory pattern, enabling NPCs to make time-aware decisions. Also provides a time-elapsed query API for lazy evaluation patterns (computing game-time duration between two real timestamps accounting for ratio changes and pauses). Internal-only, never internet-facing.

**The problem this solves**: The codebase is haunted by a clock that doesn't exist. ABML behaviors reference `${world.time.period}` with no provider. Storyline declares `TimeOfDay` trigger conditions with nothing to evaluate them. Encounters and relationships have `inGameTime` fields that callers provide with no authority to consult. Trade routes have `seasonalAvailability` flags with no seasons. Market explicitly punts game-time analysis to callers. Economy planning documents reference seasonal trade, deity harvest cycles, and tick-based processing -- all assuming a clock service that was never built. Worldstate fills this gap as the single authoritative source of "what time is it in the game world?"

**Note on weather variables**: Some early ABML behavior examples (e.g., `humanoid-base.abml.yml`) reference `${world.weather.temperature}` and `${world.weather.raining}`. These references are **incorrect** -- weather and atmospheric conditions are the `${environment.*}` namespace owned by lib-environment (L4), not the `${world.*}` namespace owned by worldstate. Similarly, `guard-patrol.abml.yml` references `${world.patrol_routes[...]}` which is not temporal data and does not belong in the `world` namespace. These example behavior files predate the final namespace design and need updating as a cleanup item.

**What this service IS**:
1. A **game clock** -- per-realm time that advances as a configurable multiple of real time
2. A **calendar** -- days, months, seasons, years as configurable templates (opaque strings, not hardcoded)
3. A **variable provider** -- `${world.*}` namespace for ABML behavior expressions
4. A **boundary event broadcaster** -- publishes `worldstate.day-changed`, `worldstate.season-changed`, etc. for service-level tick processing
5. A **time-elapsed calculator** -- answers "how much game-time passed between these two real timestamps?" for lazy evaluation patterns used by autogain, seed decay, workshop production, and similar time-based processing

**What this service is NOT**:
- **Not weather simulation** -- weather, temperature, atmospheric conditions are L4 concerns that consume season/time-of-day data from worldstate
- **Not ecology** -- resource availability, deforestation, biome state are L4 concerns
- **Not a world simulation engine** -- it's a clock, a calendar, and a broadcast system
- **Not a spatial service** -- "where" things happen is Location and Mapping's concern

**The default Arcadia time scale** (configurable per realm, per game):

| Real Time | Game Time | Ratio |
|-----------|-----------|-------|
| 1 real second | 24 game seconds | 24:1 |
| 1 real minute | 24 game minutes | 24:1 |
| 1 real hour | 1 game day (24 game hours) | 24:1 |
| 1 real day | 24 game days ≈ 1 game month | 24:1 |
| 12 real days | 1 game year (12 months) | 24:1 |
| ~2.6 real years (960 days) | 80 game years (1 saeculum) | 24:1 |
| ~8 real months | 1 turning (20 game years) | 24:1 |

At a ratio of 24:1, a server running for 5 real years experiences nearly 2 full saeculums (160 game years). Generational play, seasonal cycles, and the content flywheel all operate at viable cadences without acceleration tricks.

**Zero game-specific content**: lib-worldstate is a generic temporal service. Arcadia's specific calendar (month names, season names, day-period boundaries), time ratio, and saeculum concept are configured through calendar template seeding and configuration at deployment time. A mobile farming game might use a 1:1 ratio with 4 real-time seasons. An idle game might use 1000:1 with 2-minute "days." All equally valid configurations.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the temporal gap across the entire codebase -- the `${world.*}` variable references in ABML behaviors, the `TimeOfDay` trigger in Storyline, the `inGameTime` fields in Encounter and Relationship schemas, the `seasonalAvailability` in economy architecture, and the Currency autogain/Seed decay workers that currently use real-world time exclusively. Internal-only, never internet-facing.

---

## Core Concepts

### Realm Clock

Each realm has an independent clock that advances continuously based on a configurable time ratio. The clock is the single source of truth for "what time is it?" within a realm.

```
RealmClock:
  realmId: Guid
  gameServiceId: Guid
  calendarTemplateCode: string         # References the calendar structure for this realm

  # Current game time (computed, cached in Redis)
  currentGameYear: int                 # Game year (0-based from realm creation)
  currentMonthIndex: int               # 0-based index into calendar months
  currentDayOfMonth: int               # 1-based day within month
  currentGameHour: int                 # 0-24 game hours
  currentGameMinute: int               # 0-60 game minutes
  currentPeriod: string                # Current day period ("dawn", "morning", etc.)
  currentSeason: string                # Current season name from calendar

  # Synchronization
  lastAdvancedRealTime: DateTimeOffset # Real-world UTC timestamp of last clock advancement
  lastAdvancedGameTime: long           # Total game-seconds since realm epoch at last advancement

  # Configuration
  currentTimeRatio: float              # Game-seconds per real-second (default: 24.0)
  downtimePolicy: DowntimePolicy       # Advance (catch up) or Pause (resume from where we left off)

  # Metadata
  realmEpoch: DateTimeOffset           # Real-world timestamp when this realm's clock started
  totalGameSecondsSinceEpoch: long     # Running total for absolute time queries
```

**Clock advancement**: A background worker advances realm clocks every `ClockTickIntervalSeconds` (default: 5 real seconds = 120 game seconds at 24:1). On each tick:
1. Compute elapsed real seconds since `lastAdvancedRealTime`
2. Multiply by `currentTimeRatio` to get game-seconds elapsed
3. Apply game-seconds to the calendar to advance hour/day/month/season/year
4. Publish boundary events for any boundaries crossed
5. Update Redis cache with new game time

**Downtime handling**: When the service starts, it checks the gap between `lastAdvancedRealTime` and current real time. Based on `downtimePolicy` (`DowntimePolicy` enum):
- **`Advance`** (default): Compute catch-up game-seconds, advance the clock, publish all boundary events that were missed (batched, not one-per-boundary). This is the "the world keeps turning while you're away" behavior.
- **`Pause`**: Resume from `lastAdvancedRealTime` as if no real time passed. The gap is ignored. Useful for maintenance windows where you don't want game time to jump forward.

### Calendar Template

Calendars are configurable structures that define how game-seconds translate into named time units. All names are opaque strings (not enums), following the same extensibility pattern as seed type codes, collection type codes, and violation type codes.

```
CalendarTemplate:
  templateCode: string                 # Unique identifier (e.g., "arcadia_standard", "fantasia_primal")
  gameServiceId: Guid

  # Day structure
  gameHoursPerDay: int                 # Game hours in a day (default: 24)
  dayPeriods:                          # Named time-of-day periods
    - code: string                     # "dawn", "morning", "afternoon", "evening", "night"
      startHour: int                   # Game hour this period begins (0-23)
      endHour: int                     # Game hour this period ends (exclusive)

  # Month structure
  months:                              # Ordered list of months in a year
    - code: string                     # "frostmere", "greenleaf", "sunpeak", etc.
      name: string                     # Display name
      daysInMonth: int                 # Game days in this month
      seasonCode: string               # Which season this month belongs to

  # Season structure
  seasons:                             # Ordered list of seasons in a year
    - code: string                     # "winter", "spring", "summer", "autumn"
      name: string                     # Display name
      ordinal: int                     # Position in annual cycle (0-based)

  # Year structure
  daysPerYear: int                     # Computed: sum of all months' daysInMonth
  monthsPerYear: int                   # Computed: count of months
  seasonsPerYear: int                  # Computed: count of seasons

  # Optional: era/epoch naming
  eraLabels:                           # Named time spans (for display)
    - code: string                     # "first_age", "era_of_strife", etc.
      startYear: int
      endYear: int?                    # null = ongoing
```

**Key design decisions**:

1. **Day periods are configurable, not hardcoded**: Arcadia might have 5 periods (dawn, morning, afternoon, evening, night). A game set in a tidally locked world might have 2 (sunside, darkside). A game with no day/night cycle might have 1 (always).

2. **Months have variable lengths**: Arcadia might use 12 months of 24 days each (288 days/year). A simpler game might use 4 months of 90 days. The calendar defines the structure.

3. **Seasons are per-month, not per-day-range**: Each month belongs to exactly one season. This avoids complex "season starts on day 172" calculations. Seasons change at month boundaries.

4. **Calendar templates are shared**: Multiple realms can reference the same calendar template code. Different realms can reference different templates (Arcadia uses `arcadia_standard`, Fantasia uses `fantasia_primal` with different season names).

### Time Ratio and History

The time ratio (`currentTimeRatio`) defines how many game-seconds pass per real-second. Changes to the ratio are recorded in a history for accurate elapsed-time computation:

```
TimeRatioSegment:
  realmId: Guid
  segmentStartRealTime: DateTimeOffset # When this ratio became effective (real time)
  timeRatio: float                     # Game-seconds per real-second
  reason: TimeRatioChangeReason        # Initial, AdminAdjustment, Event, Pause, Resume
```

**Enums** (defined in `worldstate-api.yaml`, referenced via `$ref` from events and configuration):

| Enum | Values | Purpose |
|------|--------|---------|
| `DowntimePolicy` | `Advance`, `Pause` | How to handle clock gaps after service downtime |
| `TimeRatioChangeReason` | `Initial`, `AdminAdjustment`, `Event`, `Pause`, `Resume` | Why the time ratio was changed |

**Computing elapsed game-time between two real timestamps**:

```
GetElapsedGameTime(realmId, fromRealTime, toRealTime):
  totalGameSeconds = 0
  for each segment overlapping [fromRealTime, toRealTime]:
    segmentRealSeconds = min(segment.next.start, toRealTime)
                       - max(segment.start, fromRealTime)
    totalGameSeconds += segmentRealSeconds.TotalSeconds * segment.timeRatio
  return totalGameSeconds
```

This handles ratio changes, pauses (ratio = 0.0 during maintenance), and acceleration events cleanly. Services performing lazy evaluation (Currency autogain, Seed decay, Workshop production) call this API instead of computing real-time elapsed directly.

**Pause as ratio 0**: Setting `timeRatio = 0.0` freezes a realm's clock. This is recorded as a segment in the history with reason `Pause`. When resumed, a new segment with the original ratio starts (reason `Resume`). `GetElapsedGameTime` correctly returns 0 game-seconds for the paused period.

### Boundary Events

When the clock crosses a named boundary (new hour, new period, new day, new season, new year), events are published to the message bus. These are **service-level triggers**, not actor-level -- individual actors query `${world.*}` variables on their own tick and don't subscribe to these events.

**Event granularity and publishing frequency** (at default 24:1 ratio):

| Boundary | Frequency (real time) | Use Case |
|----------|----------------------|----------|
| `worldstate.hour-changed` | ~2.5 real minutes | Workshop materialization, fine-grained scheduling |
| `worldstate.period-changed` | ~12 real minutes (5/day) | NPC routine transitions, lighting changes |
| `worldstate.day-changed` | ~1 real hour | Daily processing, market restocking, crafting queue advancement |
| `worldstate.month-changed` | ~1 real day | Monthly economic reports, tax collection |
| `worldstate.season-changed` | ~3 real days | Trade route activation, agricultural cycles, loot table rotation |
| `worldstate.year-changed` | ~12 real days | Annual festivals, aging, generational checks |

### GameTimeSnapshot (Reusable Schema Component)

Defined once in `worldstate-api.yaml` under `components/schemas` and referenced via `$ref` from all event schemas and response models that carry game time data.

```
GameTimeSnapshot:
  realmId: Guid
  gameServiceId: Guid
  year: int                            # Current game year (0-based from realm epoch)
  monthIndex: int                      # 0-based index into calendar months
  monthCode: string                    # Month code from calendar template (e.g., "greenleaf")
  dayOfMonth: int                      # 1-based day within current month
  dayOfYear: int                       # 1-based day within current year
  hour: int                            # Current game hour (0-23)
  minute: int                          # Current game minute (0-59)
  period: string                       # Current day period code from calendar (e.g., "dawn")
  season: string                       # Current season code from calendar (e.g., "spring")
  seasonIndex: int                     # Current season ordinal (0-based)
  seasonProgress: float                # 0.0-1.0 progress through the current season. Computed from day-of-season / days-in-season using the calendar template's season-to-month mappings. Useful for smooth interpolation (temperature curves, resource availability) rather than discrete season boundaries. Already exposed as ${world.season_progress} in the variable provider; including it in the snapshot avoids consumers recomputing it.
  totalGameSecondsSinceEpoch: long     # Absolute game-seconds since realm epoch
  timeRatio: float                     # Current game-seconds per real-second
  timestamp: DateTimeOffset            # Real-world UTC timestamp of this snapshot
```

**Catch-up event batching**: When the service starts after downtime with `Advance` policy, potentially many boundaries were crossed. Rather than publishing hundreds of individual events, the service publishes **summary events** with the number of boundaries crossed:

```
WorldstateDayChangedEvent:
  realmId: Guid
  gameServiceId: Guid
  previousDay: int
  currentDay: int
  daysCrossed: int                     # 1 for normal, >1 for catch-up
  currentGameTime: GameTimeSnapshot    # Full snapshot for convenience
  isCatchUp: bool                      # True if this was a post-downtime catch-up
```

Services receiving catch-up events with `daysCrossed > 1` can process the time gap in bulk rather than iterating per-day.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Realm clocks (Redis), calendar templates (MySQL), ratio history (MySQL) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for clock advancement, ratio changes, and calendar mutations |
| lib-messaging (`IMessageBus`) | Publishing boundary events (day-changed, season-changed, etc.), lifecycle events, error event publication |
| lib-messaging (`IEventConsumer`) | Subscribing to own `CalendarTemplateUpdatedEvent` for cross-node cache invalidation |
| lib-resource (`IResourceClient`) | Reference registration/unregistration for realm and game-service targets (L1). Called during clock initialization, calendar creation, and cleanup operations. |
| lib-realm (`IRealmClient`) | Validating realm existence during clock initialization (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence during calendar template creation (L2) |
| lib-character (`ICharacterClient`) | Resolving characterId → realmId in the `WorldProviderFactory` variable provider. Actors pass entityId (characterId) to the factory; the factory needs to know which realm the character is in to query the correct realm clock. Cached per-character with short TTL. (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

None. Worldstate is a pure temporal authority with no optional dependencies. All consumers depend on worldstate, not the other way around.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-realm (L2) | Realm optionally calls `IWorldstateClient.InitializeRealmClockAsync()` after creating a new realm, controlled by `AutoInitializeWorldstateClock` configuration (default: false). When enabled, Realm passes the new realmId and its configured `DefaultCalendarTemplateCode` to worldstate. If the call fails, Realm logs a warning but does not fail realm creation (the clock can be initialized manually later). This is the correct pattern per IMPLEMENTATION TENETS: same-layer direct API call instead of inverted event subscription. Requires adding `IWorldstateClient` as a hard dependency and two new config properties (`AutoInitializeWorldstateClock`, `DefaultCalendarTemplateCode`) to `realm-configuration.yaml`. |
| lib-actor (L2) | Actor discovers `WorldProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates `WorldProvider` instances per entity for ABML behavior execution (`${world.*}` variables). The Actor runtime can optionally provide `realmId` in the actor's context metadata (avoiding a service call); when not provided, the factory resolves the character's realm via `ICharacterClient` (cached with short TTL), then queries worldstate for that realm's current time. |
| lib-currency (L2, future) | Currency autogain worker can optionally use `GetElapsedGameTime` for game-time-based passive income instead of real-time. Requires configuration flag (`AutogainTimeSource: "game"`) to opt in. |
| lib-seed (L2, future) | Seed decay worker can optionally use `GetElapsedGameTime` for game-time-based growth decay instead of real-time. |
| lib-workshop (L4, planned) | Uses `GetElapsedGameTime` for computing production output over game-time intervals. Subscribes to boundary events for materialization triggers. |
| lib-craft (L4, planned) | Uses game-time for recipe step `durationSeconds` timing instead of real-time. |
| lib-storyline (L4) | Evaluates `TimeOfDay` trigger conditions against worldstate to determine scenario availability. |
| lib-puppetmaster (L4) | Regional watchers query worldstate for seasonal context when selecting behavior documents. |
| lib-market (L4, future) | Can use game-time for price history bucketing instead of "server time, not game time" (current quirk). |
| lib-loot (L4, future) | Seasonal loot modifiers query current season from worldstate. |
| Any service with `inGameTime` fields | Encounter, Relationship, and future services can populate `inGameTime` from worldstate instead of caller-provided values. |

---

## State Storage

### Realm Clock Store
**Store**: `worldstate-realm-clock` (Backend: Redis, prefix: `worldstate:clock`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmClockModel` | Current game time for a realm. Hot reads -- queried by every `${world.*}` variable resolution and every `GetRealmTime` call. Updated every `ClockTickIntervalSeconds`. |

### Calendar Store
**Store**: `worldstate-calendar` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `calendar:{gameServiceId}:{templateCode}` | `CalendarTemplateModel` | Calendar structure definition. Read frequently (on clock advancement to resolve month/season names), cached in-memory after first load. |
| `realm-config:{realmId}` | `RealmWorldstateConfigModel` | Per-realm configuration: which calendar template, time ratio, downtime policy, epoch. Links realm to its calendar. |

### Ratio History Store
**Store**: `worldstate-ratio-history` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ratio:{realmId}` | `TimeRatioHistoryModel` | Ordered list of `TimeRatioSegment` entries for a realm. Used by `GetElapsedGameTime` to compute game-time across ratio changes and pauses. Append-only; old segments are never modified. |

### Distributed Locks (via `IDistributedLockProvider`)

Distributed locks use `IDistributedLockProvider` (L0 infrastructure), not a separate state store. Lock keys are documented here for reference:

| Lock Resource ID | Purpose |
|-----------------|---------|
| `worldstate:clock:{realmId}` | Clock advancement lock (serializes background worker ticks per realm) |
| `worldstate:ratio:{realmId}` | Ratio change lock (prevents concurrent ratio modifications) |
| `worldstate:calendar:{gameServiceId}:{templateCode}` | Calendar template mutation lock |

---

## Events

### Published Events

**Boundary events** (published by clock worker on game-time transitions):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.hour-changed` | `WorldstateHourChangedEvent` | Game-hour boundary crossed. Includes realmId, previousHour, currentHour, full `GameTimeSnapshot`. |
| `worldstate.period-changed` | `WorldstatePeriodChangedEvent` | Day-period boundary crossed (e.g., dawn→morning). Includes realmId, previousPeriod, currentPeriod, full `GameTimeSnapshot`. |
| `worldstate.day-changed` | `WorldstateDayChangedEvent` | Game-day boundary crossed. Includes realmId, previousDay, currentDay, daysCrossed (for catch-up), isCatchUp flag, full `GameTimeSnapshot`. |
| `worldstate.month-changed` | `WorldstateMonthChangedEvent` | Month boundary crossed. Includes realmId, previousMonth, currentMonth, monthsCrossed, full `GameTimeSnapshot`. |
| `worldstate.season-changed` | `WorldstateSeasonChangedEvent` | Season boundary crossed. Includes realmId, previousSeason, currentSeason, currentYear, full `GameTimeSnapshot`. |
| `worldstate.year-changed` | `WorldstateYearChangedEvent` | Year boundary crossed. Includes realmId, previousYear, currentYear, yearsCrossed, full `GameTimeSnapshot`. |

**Administrative events** (published on explicit mutations):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.ratio-changed` | `WorldstateRatioChangedEvent` | Time ratio changed for a realm. Includes realmId, previousRatio, newRatio, reason (`TimeRatioChangeReason` enum). |
| `worldstate.realm-clock.initialized` | `WorldstateRealmClockInitializedEvent` | A realm clock was initialized for the first time. Includes realmId, calendarTemplateCode, initialTimeRatio. |

**Lifecycle events** (auto-generated via `x-lifecycle` in `worldstate-events.yaml`):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `worldstate.calendar-template.created` | `CalendarTemplateCreatedEvent` | Calendar template seeded. Full template data. |
| `worldstate.calendar-template.updated` | `CalendarTemplateUpdatedEvent` | Calendar template modified. Full template data + `changedFields`. Enables cross-node `CalendarTemplateCache` invalidation. |
| `worldstate.calendar-template.deleted` | `CalendarTemplateDeletedEvent` | Calendar template deleted. Template identity + `deletedReason`. |
| `worldstate.realm-config.created` | `RealmWorldstateConfigCreatedEvent` | Realm worldstate config created (during clock initialization). Full config data. |
| `worldstate.realm-config.updated` | `RealmWorldstateConfigUpdatedEvent` | Realm worldstate config modified. Full config data + `changedFields`. |
| `worldstate.realm-config.deleted` | `RealmWorldstateConfigDeletedEvent` | Realm worldstate config deleted (during cleanup). Config identity + `deletedReason`. |

**`x-resource-mapping`**: Boundary events should include `x-resource-mapping` with `resource_type: realm` and `resource_id_field: realmId` so Puppetmaster's watch system can observe per-realm temporal transitions for regional watcher behavior selection.

**`x-event-publications`**: ALL published events (boundary, administrative, and lifecycle) must be listed in `x-event-publications` in `worldstate-events.yaml` per SCHEMA-RULES, serving as the authoritative registry of what worldstate emits.

### Consumed Events

None. Worldstate does not subscribe to events from other services.

- **Realm creation**: Handled by lib-realm directly calling `IWorldstateClient.InitializeRealmClockAsync()` when configured (see Dependents). This follows IMPLEMENTATION TENETS: same-layer direct API call instead of inverted event subscription.
- **Realm deletion cleanup**: Handled exclusively by lib-resource calling `/worldstate/cleanup-by-realm` (see Resource Cleanup below). Per FOUNDATION TENETS, event-based cleanup for persistent dependent data is forbidden.

### Resource Cleanup (FOUNDATION TENETS)

Worldstate registers as a cleanup callback provider via `x-references` in the API schema. When lib-resource coordinates deletion of a referenced resource, it calls the cleanup endpoints below. These are declared in `worldstate-api.yaml` via the `x-references` extension attribute.

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| realm | worldstate | CASCADE | `/worldstate/cleanup-by-realm` |
| game-service | worldstate | CASCADE | `/worldstate/cleanup-by-game-service` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ClockTickIntervalSeconds` | `WORLDSTATE_CLOCK_TICK_INTERVAL_SECONDS` | `5` | Real-time seconds between clock advancement ticks (range: 1-60). At 24:1 ratio, 5 seconds = 120 game-seconds = 2 game-minutes. |
| `ClockTickStartupDelaySeconds` | `WORLDSTATE_CLOCK_TICK_STARTUP_DELAY_SECONDS` | `10` | Seconds to wait after startup before first clock tick, allowing dependent services to initialize (range: 0-120). |
| `DefaultTimeRatio` | `WORLDSTATE_DEFAULT_TIME_RATIO` | `24.0` | Default game-seconds per real-second for new realm clocks (range: 0.1-10000.0). 24.0 means 1 real hour = 1 game day. |
| `DefaultDowntimePolicy` | `WORLDSTATE_DEFAULT_DOWNTIME_POLICY` | `Advance` | Default downtime handling for new realm clocks. `$ref` to `DowntimePolicy` enum from `worldstate-api.yaml`. |
| `MaxCatchUpGameDays` | `WORLDSTATE_MAX_CATCH_UP_GAME_DAYS` | `365` | Maximum game-days to advance during catch-up on startup. Prevents massive event storms after extended downtime (range: 1-3650). If exceeded, clock jumps to cap and logs a warning. |
| `BoundaryEventBatchSize` | `WORLDSTATE_BOUNDARY_EVENT_BATCH_SIZE` | `50` | Maximum boundary events published per tick. Prevents event flooding during catch-up. Remaining events are published on subsequent ticks (range: 10-500). |
| `ClockCacheTtlSeconds` | `WORLDSTATE_CLOCK_CACHE_TTL_SECONDS` | `10` | TTL for in-memory clock cache used by the variable provider (range: 1-60). Variable providers query game time frequently; this prevents Redis round-trips on every ABML variable resolution. |
| `CalendarCacheTtlMinutes` | `WORLDSTATE_CALENDAR_CACHE_TTL_MINUTES` | `60` | TTL for in-memory calendar template cache (range: 1-1440). Calendar structures change rarely. |
| `MaxCalendarsPerGameService` | `WORLDSTATE_MAX_CALENDARS_PER_GAME_SERVICE` | `10` | Safety limit on calendar templates per game service (range: 1-100). |
| `RatioHistoryRetentionDays` | `WORLDSTATE_RATIO_HISTORY_RETENTION_DAYS` | `90` | Days of ratio history to retain. Older segments are compacted (merged into single segments). Range: 7-365. |
| `DefaultCalendarTemplateCode` | `WORLDSTATE_DEFAULT_CALENDAR_TEMPLATE_CODE` | *(nullable)* | Default calendar template code used as fallback when `InitializeRealmClock` is called without specifying a template. Null = no default (caller must specify). When set, `InitializeRealmClock` with null `calendarTemplateCode` falls back to this value. Declared `nullable: true` with no default per IMPLEMENTATION TENETS (no sentinel values). |
| `DistributedLockTimeoutSeconds` | `WORLDSTATE_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `10` | Timeout for distributed lock acquisition (range: 5-60). |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<WorldstateService>` | Structured logging |
| `WorldstateServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 3 stores: realm-clock, calendar, ratio-history) |
| `IMessageBus` | Event publishing (boundary events, lifecycle events, administrative events) |
| `IEventConsumer` | Subscribes to own `CalendarTemplateUpdatedEvent` for cross-node cache invalidation |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IRealmClient` | Realm existence validation (L2 hard dependency) |
| `IGameServiceClient` | Game service existence validation (L2 hard dependency) |
| `IResourceClient` | Reference registration/unregistration with lib-resource (L1 hard dependency). Called during clock initialization, calendar creation, and cleanup. |
| `ICharacterClient` | Character-to-realm resolution for variable provider (L2 hard dependency). Used by `WorldProviderFactory` to resolve entityId → realmId when the Actor runtime does not provide realmId in context. |
| `WorldstateClockWorkerService` | Background `HostedService` that advances realm clocks on `ClockTickIntervalSeconds` interval. Acquires per-realm distributed locks to prevent concurrent advancement in multi-node deployments. |
| `WorldProviderFactory` | Implements `IVariableProviderFactory` to provide `${world.*}` variables to Actor's behavior system. Creates `WorldProvider` instances from cached realm clock data. Accepts optional `realmId` from actor context; falls back to `ICharacterClient` lookup when not provided. |
| `IWorldstateTimeCalculator` / `WorldstateTimeCalculator` | Internal helper for calendar math: converting total game-seconds to year/month/day/hour, resolving day periods and seasons, computing elapsed game-time across ratio segments. |
| `IRealmClockCache` / `RealmClockCache` | In-memory TTL cache for realm clock data used by the variable provider. Prevents Redis round-trips on every ABML variable resolution. Backed by `ConcurrentDictionary` with `ClockCacheTtlSeconds` expiry. |
| `ICalendarTemplateCache` / `CalendarTemplateCache` | In-memory TTL cache for calendar templates. Calendars change rarely; caching avoids MySQL queries on every clock tick. |

### Variable Provider Factory

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `WorldProviderFactory` | `${world.*}` | Reads realm clock from Redis (via `IRealmClockCache`). Accepts optional `realmId` from actor context metadata; when not provided, resolves character's realmId via `ICharacterClient` (cached). Provides computed time variables. | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

**Total: 17 endpoints** (4 clock queries + 3 clock admin + 5 calendar management + 3 realm configuration + 2 cleanup)

### Clock Queries (4 endpoints) — `x-permissions: [{ role: user }]`

- **GetRealmTime** (`/worldstate/clock/get-realm-time`): Returns full `GameTimeSnapshot` for a realm: year, month, day, hour, minute, period, season, total game-seconds since epoch, current time ratio. Reads from Redis cache (hot path). If cache is empty (first call after initialization), computes from epoch + elapsed time.

- **GetRealmTimeByCode** (`/worldstate/clock/get-realm-time-by-code`): Convenience endpoint accepting realm code string instead of GUID. Resolves to realm ID via `IRealmClient`, then delegates to `GetRealmTime`.

- **BatchGetRealmTimes** (`/worldstate/clock/batch-get-realm-times`): Returns `GameTimeSnapshot` for multiple realms in a single call. Used by services that operate across realms (Orchestrator, Analytics).

- **GetElapsedGameTime** (`/worldstate/clock/get-elapsed-game-time`): Given a realmId, `fromRealTime`, and `toRealTime`, computes the total game-seconds elapsed between those real timestamps. Integrates over the ratio history segments. Returns result as both raw game-seconds and decomposed calendar units (days, hours, minutes). Critical for lazy evaluation patterns: "how much game time passed since I last checked?"

### Clock Administration (3 endpoints) — `x-permissions: [{ role: developer }]`

- **InitializeRealmClock** (`/worldstate/clock/initialize`): Initializes a clock for a realm. The primary path from "realm exists" to "realm has a clock." Can be called directly by admin tooling, or automatically by lib-realm when `AutoInitializeWorldstateClock` is enabled. Request includes `realmId` (required), `calendarTemplateCode` (nullable -- falls back to `DefaultCalendarTemplateCode` config if null; returns `BadRequest` if both are null), optional `epoch` (defaults to current real time), optional `startingYear` (defaults to 0), optional `timeRatio` (falls back to `DefaultTimeRatio` config), optional `downtimePolicy` (falls back to `DefaultDowntimePolicy` config). Validates: realm exists, calendar template exists for the realm's game service, realm doesn't already have a clock (Conflict). Creates realm config, initial ratio history segment, and Redis clock cache entry. **Registers reference with lib-resource** (realm target, worldstate source). Publishes `realm-clock.initialized` + `RealmWorldstateConfigCreatedEvent`.

- **SetTimeRatio** (`/worldstate/clock/set-ratio`): Changes the time ratio for a realm. Materializes current clock state (flush to latest game time), records new ratio segment in history (with `TimeRatioChangeReason` enum), updates the realm clock. Publishes `worldstate.ratio-changed`. Setting ratio to 0.0 pauses the clock.

- **AdvanceClock** (`/worldstate/clock/advance`): Manually advances a realm's clock by a specified number of game-seconds (or game-days/months/years for convenience). Used for testing and administrative fast-forward. Publishes all boundary events crossed during advancement. Respects `BoundaryEventBatchSize`.

### Calendar Management (5 endpoints) — `x-permissions: [{ role: developer }]`

- **SeedCalendar** (`/worldstate/calendar/seed`): Creates a calendar template. Validates: game service exists, template code is unique within game service, day periods cover all 24 hours without gaps or overlaps, month season codes reference defined seasons, days-per-year sum matches month day counts. Enforces `MaxCalendarsPerGameService`. **Registers reference with lib-resource** (game-service target, worldstate source). Publishes `CalendarTemplateCreatedEvent`.

- **GetCalendar** (`/worldstate/calendar/get`): Returns a calendar template by game service ID + template code.

- **ListCalendars** (`/worldstate/calendar/list`): Lists all calendar templates for a game service.

- **UpdateCalendar** (`/worldstate/calendar/update`): Acquires distributed lock. Partial update (only non-null fields applied). Validates structural consistency (same rules as seed). Invalidates local calendar cache; other nodes receive invalidation via `CalendarTemplateUpdatedEvent` subscription. **Cannot change**: template code, game service ID (identity-level). Active realm clocks referencing this template will use the updated structure starting next tick. Publishes `CalendarTemplateUpdatedEvent`.

- **DeleteCalendar** (`/worldstate/calendar/delete`): Deletes a calendar template. Fails with `Conflict` if any active realm clocks reference this template. Acquires distributed lock. **Unregisters reference with lib-resource** (game-service target). Publishes `CalendarTemplateDeletedEvent`.

### Realm Configuration (3 endpoints) — `x-permissions: [{ role: developer }]`

- **GetRealmConfig** (`/worldstate/realm-config/get`): Returns the realm's worldstate configuration: calendar template code, current time ratio, downtime policy (`DowntimePolicy` enum), epoch, whether the clock is initialized and active. If no clock is initialized for the realm, returns `NotFound`. Used by admin dashboards and diagnostic tools.

- **UpdateRealmConfig** (`/worldstate/realm-config/update`): Updates realm-level configuration. Acquires distributed lock. Partial update (only non-null fields applied). Supports changing: `downtimePolicy` (`DowntimePolicy` enum, safe to change at any time), `calendarTemplateCode` (validates template exists; takes effect on next clock tick -- see Quirk #4). **Cannot change via this endpoint**: time ratio (use `SetTimeRatio` which records history), epoch (immutable after initialization). Publishes `RealmWorldstateConfigUpdatedEvent`.

- **ListRealmClocks** (`/worldstate/realm-config/list`): Lists all active realm clocks with summary info: realmId, calendarTemplateCode, currentTimeRatio, current season, current year, last advanced timestamp. Supports pagination and optional `gameServiceId` filter. Used by admin dashboards and monitoring.

### Cleanup (2 endpoints) — `x-permissions: []` (service-to-service only, not exposed to WebSocket clients)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS). These endpoints are called exclusively by lib-resource when coordinating deletion of referenced resources, declared via `x-references` in the API schema. **Worldstate does NOT subscribe to `realm.deleted` or `game-service.deleted` events for cleanup** -- lib-resource callbacks are the sole cleanup mechanism.

- **CleanupByRealm** (`/worldstate/cleanup-by-realm`): Removes realm clock, ratio history, and realm configuration for a realm. **Unregisters reference with lib-resource** (realm target). Publishes `RealmWorldstateConfigDeletedEvent`.
- **CleanupByGameService** (`/worldstate/cleanup-by-game-service`): Removes all calendar templates and realm configurations for a game service. **Unregisters references with lib-resource** (game-service target). Publishes `CalendarTemplateDeletedEvent` for each removed template.

---

## Visual Aid

```
┌───────────────────────────────────────────────────────────────────────────┐
│                     Clock Advancement & Event Publishing                    │
│                                                                           │
│  WorldstateClockWorkerService (runs every ClockTickIntervalSeconds)        │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │  for each realm with active clock:                                   │  │
│  │                                                                     │  │
│  │  1. Acquire lock: worldstate:lock:clock:{realmId}                   │  │
│  │                                                                     │  │
│  │  2. Compute elapsed:                                                │  │
│  │     realElapsed = now - lastAdvancedRealTime                        │  │
│  │     gameSeconds = realElapsed.TotalSeconds * currentTimeRatio        │  │
│  │                                                                     │  │
│  │  3. Calendar math (via IWorldstateTimeCalculator):                   │  │
│  │     oldSnapshot = { year: 3, month: "greenleaf", day: 12,           │  │
│  │                     hour: 22, period: "night" }                      │  │
│  │     newSnapshot = advanceBy(oldSnapshot, gameSeconds, calendar)      │  │
│  │                 = { year: 3, month: "greenleaf", day: 13,           │  │
│  │                     hour: 3, period: "dawn" }                       │  │
│  │                                                                     │  │
│  │  4. Detect boundaries crossed:                                      │  │
│  │     hour:   22→23→0→1→2→3 = 5 hours crossed                        │  │
│  │     period: night→dawn     = 1 period crossed                       │  │
│  │     day:    12→13          = 1 day crossed                          │  │
│  │     month:  (none)                                                  │  │
│  │     season: (none)                                                  │  │
│  │     year:   (none)                                                  │  │
│  │                                                                     │  │
│  │  5. Publish boundary events:                                        │  │
│  │     → worldstate.hour-changed    (×5, batched)                      │  │
│  │     → worldstate.period-changed  (×1: night→dawn)                   │  │
│  │     → worldstate.day-changed     (×1: day 12→13)                    │  │
│  │                                                                     │  │
│  │  6. Update Redis cache:                                             │  │
│  │     worldstate:clock:realm:{realmId} = newSnapshot                  │  │
│  │                                                                     │  │
│  │  7. Release lock                                                    │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  CONSUMERS:                                                               │
│  ┌─────────────────────────┐  ┌─────────────────────────────────────┐    │
│  │ Variable Provider       │  │ Service-Level Subscribers            │    │
│  │ (Actor L2, per-tick)    │  │ (Workshop, Craft, Seed, Currency)    │    │
│  │                         │  │                                     │    │
│  │ ${world.time.period}    │  │ worldstate.day-changed →            │    │
│  │ ${world.season}         │  │   Workshop: materialize production  │    │
│  │ ${world.time.hour}      │  │   Currency: process autogain batch  │    │
│  │ etc.                    │  │   Seed: apply growth decay          │    │
│  │                         │  │                                     │    │
│  │ Reads from Redis cache  │  │ worldstate.season-changed →         │    │
│  │ (IRealmClockCache)      │  │   Loot: rotate seasonal tables     │    │
│  │ No event subscription   │  │   Market: activate seasonal routes  │    │
│  │ Natural load spreading  │  │   Storyline: unlock seasonal arcs   │    │
│  └─────────────────────────┘  └─────────────────────────────────────┘    │
│                                                                           │
│  Actors do NOT subscribe to boundary events.                              │
│  They query ${world.*} on their own 100-500ms tick.                       │
│  This naturally distributes 100,000+ NPC time-checks                      │
│  across the tick interval instead of thundering-herding                    │
│  on a single boundary event.                                              │
└───────────────────────────────────────────────────────────────────────────┘
```

---

## Variable Provider: `${world.*}` Namespace

Implements `IVariableProviderFactory` (via `WorldProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern. Loads from the in-memory realm clock cache (`IRealmClockCache`).

**Provider initialization**: When `CreateAsync(entityId, ct)` is called, the factory:
1. Checks if `realmId` is available in the actor's context metadata (Actor runtime can provide this directly, avoiding a service call)
2. If not available, resolves the character's `realmId` via `ICharacterClient` (cached per-character with short TTL)
3. Reads the realm clock from `IRealmClockCache` (TTL-based, avoids Redis per-resolution)
4. Returns a `WorldProvider` populated with the current game time snapshot

### Time Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${world.time.hour}` | int | Current game hour (0-23) |
| `${world.time.minute}` | int | Current game minute (0-59) |
| `${world.time.period}` | string | Current day period code (e.g., "dawn", "morning", "afternoon", "evening", "night") |
| `${world.time.is_day}` | bool | True during non-night periods (configurable per calendar) |
| `${world.time.is_night}` | bool | True during night period |

### Calendar Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${world.day}` | int | Current day of month (1-based) |
| `${world.day_of_year}` | int | Current day of year (1-based) |
| `${world.month}` | string | Current month code (e.g., "greenleaf") |
| `${world.month_index}` | int | Current month index (0-based) |
| `${world.season}` | string | Current season code (e.g., "spring") |
| `${world.season_index}` | int | Current season ordinal (0-based) |
| `${world.year}` | int | Current game year |

### Derived Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${world.season_progress}` | float | How far through the current season (0.0 = start, 1.0 = end). Computed from day-of-season / days-in-season. |
| `${world.year_progress}` | float | How far through the current year (0.0 = start, 1.0 = end). |
| `${world.time_ratio}` | float | Current game-time ratio (game-seconds per real-second). |

### ABML Usage Examples

```yaml
flows:
  daily_routine:
    # NPC wakes up at dawn, works during day, returns home at evening
    - cond:
        - when: "${world.time.period == 'dawn'}"
          then:
            - call: wake_up
            - call: prepare_for_day

        - when: "${world.time.period == 'morning' || world.time.period == 'afternoon'}"
          then:
            - call: go_to_work
            - call: perform_occupation

        - when: "${world.time.period == 'evening'}"
          then:
            - call: return_home
            - call: socialize

        - when: "${world.time.period == 'night'}"
          then:
            - call: sleep

  seasonal_behavior:
    # Farmer plants in spring, tends in summer, harvests in autumn, repairs in winter
    - cond:
        - when: "${world.season == 'spring' && craft.can_craft.plant_seeds}"
          then:
            - call: go_to_fields
            - call: plant_crops

        - when: "${world.season == 'summer'}"
          then:
            - call: tend_crops
            - call: water_fields

        - when: "${world.season == 'autumn' && world.season_progress > 0.3}"
          then:
            - call: harvest_crops
            - call: bring_to_market

        - when: "${world.season == 'winter'}"
          then:
            - call: repair_tools
            - call: plan_next_season

  guard_shift:
    # Guards change shifts based on game hour
    - cond:
        - when: "${world.time.hour >= 6 && world.time.hour < 14}"
          then:
            - set: { shift: "day" }
            - call: patrol_main_gate

        - when: "${world.time.hour >= 14 && world.time.hour < 22}"
          then:
            - set: { shift: "evening" }
            - call: patrol_market_district

        - when: "${world.time.hour >= 22 || world.time.hour < 6}"
          then:
            - set: { shift: "night" }
            - call: patrol_walls
            - set: { alertness: "${alertness + 0.2}" }  # More alert at night
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Calendar Infrastructure
- Create `worldstate-api.yaml` schema with all endpoints
- Create `worldstate-events.yaml` schema
- Create `worldstate-configuration.yaml` schema
- Generate service code
- Implement calendar template CRUD (seed, get, list, update, delete)
- Implement calendar validation (period coverage, season-month mapping, structural consistency)

### Phase 2: Realm Clock Core
- Implement realm clock initialization (explicit via `InitializeRealmClock` endpoint with nullable calendarTemplateCode + config fallback)
- Implement lib-resource reference registration in `InitializeRealmClock` (realm target)
- Implement clock advancement background worker
- Implement Redis cache for hot clock reads
- Implement boundary detection and event publishing
- Implement downtime catch-up with configurable `DowntimePolicy` enum
- Implement catch-up event batching

### Phase 3: Time Ratio & Elapsed Time
- Implement ratio history storage and management
- Implement `SetTimeRatio` with history recording (`TimeRatioChangeReason` enum)
- Implement `GetElapsedGameTime` with piecewise integration over ratio segments
- Implement ratio history retention/compaction
- Implement pause/resume via ratio 0.0

### Phase 4: Variable Provider
- Implement `WorldProviderFactory` and `WorldProvider`
- Implement realm clock in-memory cache (`IRealmClockCache`) with TTL
- Implement character-to-realm resolution caching
- Integration testing with Actor runtime (verify `${world.time.period}` resolves)

### Phase 5: Administrative, Configuration & Cleanup
- Implement `AdvanceClock` for testing/admin
- Implement `BatchGetRealmTimes`
- Implement `GetRealmConfig`, `UpdateRealmConfig`, `ListRealmClocks` (realm configuration management)
- Implement resource cleanup endpoints (declared via `x-references` in API schema)
- Integration testing with other L2 services

### Phase 6: Cross-Service Integration
- Add `IWorldstateClient` hard dependency and `AutoInitializeWorldstateClock` + `DefaultCalendarTemplateCode` config to lib-realm
- Add `worldstate` to SERVICE-HIERARCHY.md L2 Quick Reference table and GENERATED-SERVICE-DETAILS.md
- Update ABML example behavior files to fix incorrect `${world.weather.*}` and `${world.patrol_routes[...]}` namespace references

---

## Potential Extensions

1. **Location-specific time zones**: Locations within a realm could have time offsets (eastern regions experience dawn before western regions). The calendar template could define optional time zone offsets per location or location subtree. Complex but adds geographic realism.

2. **Magical time dilation zones**: Locations where time flows differently (a fairy realm where 1 game hour = 10 game hours, or a cursed zone where time is frozen). This would require per-location time ratio overrides that the variable provider checks when creating a `WorldProvider` for a character at that location. Connects to the `ITemporalManager` interface already defined in `bannou-service/Behavior/` for cinematic time dilation.

3. **Calendar events/holidays**: Named dates in the calendar that repeat annually (harvest festival on Greenleaf 15, winter solstice on Frostmere 1). Publishable as events when the date is reached. Games could schedule content around these dates.

4. **Lunar cycles / celestial events**: Additional cyclical phenomena beyond seasons (moon phases, eclipses, celestial alignments). Configurable as additional cycles in the calendar template with their own variable namespace (`${world.moon.phase}`). Affects magic systems, werewolves, tides.

5. **Time-dependent resource availability**: While ecology is an L4 concern, worldstate could publish a standardized `${world.resource_season}` variable that represents the general abundance level per season (lean, moderate, abundant, harvest). L4 services use this as a baseline modifier.

6. **Historical time queries**: "What season was it on game-year 47, month 3?" Useful for Storyline's retrospective narrative generation and Character-History's temporal context. Pure calendar math, no state required.

7. **Client events for time display**: `worldstate-client-events.yaml` for pushing periodic time snapshots to connected WebSocket clients, enabling client-side time display without polling. Published on period-changed boundaries (5 per game day) for low-bandwidth time sync.

8. **Variable-rate time**: Time ratio that changes based on player population. When no players are in a realm, time accelerates (e.g., 100:1) to advance the simulation faster. When players are present, it returns to normal (24:1). This enables "the world continued while you were away" to be more dramatic for realms with intermittent activity.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

0. **ABML behavior namespace cleanup needed**: Several example behavior files reference variables under the `${world.*}` namespace that do NOT belong to worldstate: `${world.weather.temperature}` and `${world.weather.raining}` (these are `${environment.*}` variables from lib-environment L4), and `${world.patrol_routes[...]}` (operational data, not temporal). These example files predate the final namespace design and need updating. The `${world.*}` namespace owned by worldstate is strictly temporal: time, calendar, and season data.

1. **Realm clocks are independent**: Two realms running on the same server can be in different seasons, years, or even different calendar systems entirely. There is no global game time. This is intentional -- Arcadia, Fantasia, and Omega are peer worlds that may have different temporal scales.

2. **Hour-changed events are frequent**: At 24:1 ratio, `worldstate.hour-changed` fires every ~2.5 real minutes. Services should subscribe only to the granularity they need. Most services want `day-changed` (hourly real-time) or `season-changed` (~every 3 real days).

3. **Variable provider returns snapshot, not live values**: The `WorldProvider` created for an actor captures a snapshot of the realm clock at creation time. If the actor's tick runs for more than `ClockCacheTtlSeconds`, the time data could be slightly stale. This is acceptable -- NPC decisions don't require sub-second time accuracy.

4. **Calendar changes affect running clocks immediately**: Updating a calendar template (e.g., adding a month, changing season assignments) takes effect on the next clock tick. Running realm clocks don't "notice" the change until they're advanced. This could cause a month boundary to be missed or a season to change early if the calendar is restructured while the clock is running. Calendar changes on active realms should be done during maintenance windows.

5. **Catch-up boundary events are batched, not individual**: After downtime with `Advance` policy, if 48 game-hours passed, the service does NOT publish 48 individual `hour-changed` events. It publishes up to `BoundaryEventBatchSize` events per tick, with catch-up metadata. Consumers must handle `daysCrossed > 1` (and similar) in catch-up events.

6. **Ratio of 0.0 is a valid pause**: Setting a realm's time ratio to 0.0 freezes its clock. `GetElapsedGameTime` returns 0 game-seconds for any real-time range that falls within a 0.0-ratio segment. This is the canonical way to pause a realm's temporal progression.

7. **Character-to-realm resolution is cached (when used)**: The variable provider factory supports two paths for realm resolution: (a) the Actor runtime provides `realmId` directly in context metadata (preferred -- zero service calls), or (b) the factory resolves characterId → realmId via `ICharacterClient` (cached per-character with `ClockCacheTtlSeconds` TTL, default 10s). When using path (b), if a character changes realms, the cache serves stale data until TTL expires. The TTL is short enough that this resolves quickly, but there is a brief window where a character who just teleported between realms sees the old realm's time. Path (a) avoids this entirely.

### Design Considerations (Requires Planning)

1. **Multi-node clock advancement**: In a multi-node deployment, the background worker runs on every node but only one should advance a given realm's clock (distributed lock ensures this). If the lock-holding node dies mid-tick, the lock TTL must expire before another node can advance. The gap between ticks is bounded by `ClockTickIntervalSeconds` + lock TTL. Need to ensure this gap doesn't cause noticeable time stutter.

2. **Game-time in existing schemas**: Encounter's `inGameTime` and Relationship's `inGameTimestamp` fields are currently caller-provided. Should these services auto-populate from worldstate, or should callers continue to provide them? Auto-population requires those L2 services to depend on worldstate (also L2, same layer -- allowed). The migration path needs consideration.

3. **Currency/Seed migration to game-time**: The Currency autogain worker and Seed decay worker currently use real-time exclusively. Adding optional game-time support requires a configuration flag per service and careful handling of the transition (existing data uses real-time timestamps; switching mid-stream requires conversion). Both workers need a "time source" abstraction that can be backed by either `DateTimeOffset.UtcNow` differences or `GetElapsedGameTime`.

4. **Ratio history compaction**: Over months of operation, the ratio history can accumulate many segments (especially if admin frequently adjusts ratios for events). The `RatioHistoryRetentionDays` config enables compaction, but the compaction algorithm (merging adjacent same-ratio segments, time-weighted averaging of different-ratio segments) needs careful design to maintain accuracy for `GetElapsedGameTime` queries that span compacted ranges.

5. **Calendar complexity vs. performance**: Rich calendars with many months, variable-length months, and multiple seasons require non-trivial math on every clock tick. The `IWorldstateTimeCalculator` should precompute lookup tables from the calendar template (cumulative days per month, season boundaries) and cache them, rather than iterating the calendar structure on every advancement.

6. **Background worker scalability**: The clock worker iterates over all active realm clocks every `ClockTickIntervalSeconds`. Each realm requires a distributed lock acquisition, Redis read/write, and potential event publishing. With many realms (100+), a single worker iteration may not complete within the tick interval. Options: (a) parallel realm advancement with configurable concurrency limits, (b) partitioning realms across nodes via a `SupportedRealms` configuration (similar to GameSession's `SupportedGameServices` pattern), (c) staggered advancement where realms are distributed across tick windows to smooth load. The partitioning approach (b) also reduces lock contention in multi-node deployments.

7. **Multi-node calendar cache invalidation**: `CalendarTemplateCache` uses a 60-minute TTL (`CalendarCacheTtlMinutes`). After a calendar update on Node A, Node B serves stale data until its cache TTL expires. The `CalendarTemplateUpdatedEvent` lifecycle event (added above) enables event-based cache invalidation across nodes -- worldstate can subscribe to its own lifecycle events via `IEventConsumer` to invalidate the local `ConcurrentDictionary` on all nodes immediately. This should be implemented alongside the lifecycle events in Phase 1.

8. **SERVICE-HIERARCHY.md integration**: When worldstate is implemented, it must be added to the L2 Quick Reference table in SERVICE-HIERARCHY.md (alongside realm, character, species, etc.) and to GENERATED-SERVICE-DETAILS.md. The `x-service-layer: GameFoundation` declaration in `worldstate-api.yaml` handles the generated `[BannouService]` attribute and plugin load ordering.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Calendar infrastructure (Phase 1) is self-contained. Realm clock core (Phase 2) depends on Phase 1. Variable provider (Phase 4) depends on Phase 2 and can be integration-tested with Actor runtime. Phase 6 (cross-service integration) requires coordination with lib-realm for `IWorldstateClient` dependency and with SERVICE-HIERARCHY.md for L2 registration.*
