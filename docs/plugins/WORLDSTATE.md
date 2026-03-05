# Worldstate Plugin Deep Dive

> **Plugin**: lib-worldstate
> **Schema**: `schemas/worldstate-api.yaml`
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: worldstate-realm-clock (Redis), worldstate-calendar (MySQL), worldstate-ratio-history (MySQL), worldstate-lock (Redis)
> **Status**: Fully implemented.
> **Implementation Map**: [docs/maps/WORLDSTATE.md](../maps/WORLDSTATE.md)

---

## Overview

Per-realm game time authority, calendar system, and temporal event broadcasting service (L2 GameFoundation). Maps real-world time to configurable game-time progression with per-realm time ratios, calendar templates (configurable days, months, seasons, years), and day-period cycles. Publishes boundary events at game-time transitions consumed by other services for time-aligned processing, and provides the `${world.*}` variable namespace to the Actor behavior system via the Variable Provider Factory pattern. Also provides a time-elapsed query API for lazy evaluation patterns (computing game-time duration between two real timestamps accounting for ratio changes and pauses). Game-agnostic: calendar structures, time ratios, and day-period definitions are configured per game service. Internal-only, never internet-facing.

---

## Core Concepts

Worldstate is the single authoritative source of "what time is it in the game world?" ABML behaviors reference `${world.time.period}` via the `WorldProviderFactory`. Transit validates `seasonalAvailability` against calendar season codes. Environment computes weather from season and time-of-day. Services performing lazy evaluation (Currency autogain, Seed decay, Workshop production) can compute game-time elapsed via `GetElapsedGameTime` instead of using real-time.

### Realm Clock

Each realm has an independent clock that advances continuously based on a configurable time ratio. The clock is the single source of truth for "what time is it?" within a realm. Clock state (current game time, synchronization timestamps, time ratio, downtime policy, epoch) is cached in Redis for hot reads and updated every `ClockTickIntervalSeconds`.

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

Calendars are configurable structures that define how game-seconds translate into named time units. All names are opaque strings (not enums), following the same extensibility pattern as seed type codes, collection type codes, and violation type codes. See `worldstate-api.yaml` `components/schemas` for the full schema definitions (`CalendarTemplateResponse`, `DayPeriodDefinition`, `MonthDefinition`, `SeasonDefinition`, `EraLabel`).

**Key design decisions**:

1. **Day periods are configurable, not hardcoded**: Arcadia might have 5 periods (dawn, morning, afternoon, evening, night). A game set in a tidally locked world might have 2 (sunside, darkside). A game with no day/night cycle might have 1 (always).

2. **Months have variable lengths**: Arcadia might use 12 months of 24 days each (288 days/year). A simpler game might use 4 months of 90 days. The calendar defines the structure.

3. **Seasons are per-month, not per-day-range**: Each month belongs to exactly one season. This avoids complex "season starts on day 172" calculations. Seasons change at month boundaries.

4. **Calendar templates are shared**: Multiple realms can reference the same calendar template code. Different realms can reference different templates (Arcadia uses `arcadia_standard`, Fantasia uses `fantasia_primal` with different season names).

### Time Ratio and History

The time ratio (`currentTimeRatio`) defines how many game-seconds pass per real-second. Changes to the ratio are recorded in a history (`TimeRatioSegment` entries with `segmentStartRealTime`, `timeRatio`, and `reason`) for accurate elapsed-time computation via piecewise integration.

**Enums** (defined in `worldstate-api.yaml`, referenced via `$ref` from events and configuration):

| Enum | Values | Purpose |
|------|--------|---------|
| `DowntimePolicy` | `Advance`, `Pause` | How to handle clock gaps after service downtime |
| `TimeRatioChangeReason` | `Initial`, `AdminAdjustment`, `Event`, `Pause`, `Resume` | Why the time ratio was changed |
| `TimeSyncReason` | `PeriodChanged`, `RatioChanged`, `AdminAdvance`, `TriggerSync` | Why a time sync was published to a client |

**Elapsed game-time computation**: `GetElapsedGameTime` integrates over ratio history segments between two real timestamps:

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

**Pause as ratio 0**: Setting `timeRatio = 0.0` freezes a realm's clock. This is recorded as a segment with reason `Pause`. When resumed, a new segment with the original ratio starts (reason `Resume`). `GetElapsedGameTime` correctly returns 0 game-seconds for the paused period.

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

All boundary events include a full `GameTimeSnapshot` (see `worldstate-api.yaml`). Day/month/year events include a `*Crossed` count field for catch-up batch processing. Day events include an `isCatchUp` flag.

**Catch-up event batching**: During catch-up after downtime with `Advance` policy, `hour-changed` events are **skipped entirely** (too noisy to process retroactively). Only coarser boundaries are published. The `isCatchUp: true` flag signals consumers that fine-grained boundaries were elided. Services needing exact elapsed time should use `GetElapsedGameTime` instead of counting boundary events.

---

## Design Philosophy

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

Arcadia's specific calendar (month names, season names, day-period boundaries), time ratio, and saeculum concept are configured through calendar template seeding and configuration at deployment time. A mobile farming game might use a 1:1 ratio with 4 real-time seasons. An idle game might use 1000:1 with 2-minute "days." All equally valid configurations.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-realm (L2) | Realm optionally calls `IWorldstateClient.InitializeRealmClockAsync()` after creating a new realm, controlled by `AutoInitializeWorldstateClock` configuration (default: false). When enabled, Realm passes the new realmId and its configured `DefaultCalendarTemplateCode` to worldstate. If the call fails, Realm logs a warning but does not fail realm creation (the clock can be initialized manually later). This is the correct pattern per IMPLEMENTATION TENETS: same-layer direct API call instead of inverted event subscription. `IWorldstateClient` is a hard dependency (constructor injection) with two config properties (`AutoInitializeWorldstateClock`, `DefaultCalendarTemplateCode`) in `realm-configuration.yaml`. |
| lib-actor (L2) | Actor discovers `WorldProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates `WorldProvider` instances per entity for ABML behavior execution (`${world.*}` variables). The Actor runtime always provides `realmId` directly in the `CreateAsync` call (every actor belongs to a realm), so the factory simply reads the realm clock from `IRealmClockCache` -- no service calls required. |
| lib-currency (L2, **required migration**) | Currency autogain worker MUST use `GetElapsedGameTime` for game-time-based passive income. Real-time autogain under-credits by the time ratio factor (24x at default). Requires `AutogainTimeSource` config property (default: `GameTime`). Both `Lazy` and `Task` autogain modes need this transition. The NPC-driven economy requires game-time parity. |
| lib-seed (L2, **required migration**) | Seed decay worker MUST use `GetElapsedGameTime` for game-time-based growth decay. Real-time decay is 24x slower than intended per game-day. Requires `DecayTimeSource` config property (default: `GameTime`). Seeds representing guardian spirits, dungeon cores, and faction growth all evolve in simulated world time. |
| lib-transit (L2) | Hard dependency on `IWorldstateClient` for journey ETA computation via `GetElapsedGameTime`, travel time calculation from distance/speed into game-hours. `SeasonalConnectionWorker` subscribes to `worldstate.season-changed` for automatic seasonal connection open/close. Validates `seasonalAvailability` keys against the realm's Worldstate calendar season codes on connection creation. |
| lib-character-lifecycle (L4) | Subscribes to `worldstate.year-changed` for aging checks, generational milestone evaluation (turning boundaries, saeculum transitions), and death processing triggers. At 24:1 ratio, `year-changed` fires every ~12 real days -- appropriate cadence for lifecycle events that operate on year boundaries. |
| lib-character-encounter (L4, **migration candidate**) | `MemoryDecaySchedulerService` currently uses real-time for memory decay. Since memory decay is a narrative concept, it should use game-time. Requires `DecayTimeSource` config property (default: `GameTime`). |
| lib-workshop (L4, planned) | Uses `GetElapsedGameTime` for computing production output over game-time intervals. Subscribes to boundary events for materialization triggers. |
| lib-craft (L4, planned) | Uses game-time for recipe step `durationSeconds` timing instead of real-time. |
| lib-storyline (L4) | Evaluates `TimeOfDay` trigger conditions against worldstate to determine scenario availability. |
| lib-puppetmaster (L4) | Regional watchers query worldstate for seasonal context when selecting behavior documents. |
| lib-environment (L4) | Hard dependency on `IWorldstateClient` for environmental condition computation -- temperature curves, weather resolution, and resource availability all require current game time, season, and season progress. Subscribes to `worldstate.period-changed`, `worldstate.day-changed`, and `worldstate.season-changed` for condition refresh and cache invalidation. |
| lib-agency (L4) | Calls `TriggerTimeSync` endpoint when a player enters a realm for immediate client time sync without waiting for the next period boundary (~12 real minutes away at 24:1). |
| lib-market (L4, future) | Can use game-time for price history bucketing instead of server time. |
| lib-loot (L4, future) | Seasonal loot modifiers query current season from worldstate. |
| Any service with `inGameTime` fields | Encounter, Relationship, and future services can populate `inGameTime` from worldstate instead of caller-provided values. |

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `DowntimePolicy` | C (System State) | Service-specific enum (`Advance`, `Pause`) | Finite set of two system-owned policies for handling clock gaps after service downtime |
| `TimeRatioChangeReason` | C (System State) | Service-specific enum (`Initial`, `AdminAdjustment`, `Event`, `Pause`, `Resume`) | Finite set of system-owned reasons for time ratio changes; used in ratio history tracking |
| `TimeSyncReason` | C (System State) | Service-specific enum (`PeriodChanged`, `RatioChanged`, `AdminAdvance`, `TriggerSync`) | Finite set of system-owned reasons for client time sync events |
| day period codes (e.g., `dawn`, `morning`, `night`) | B (Content Code) | Opaque string | Game-configurable day period names defined in calendar templates. Different games define different period structures (5 periods for Arcadia, 2 for a tidally locked world). Extensible without schema changes |
| month codes (e.g., `frostmere`, `greenleaf`) | B (Content Code) | Opaque string | Game-configurable month names defined in calendar templates. Calendar structures vary per game service. Extensible without schema changes |
| season codes (e.g., `winter`, `spring`, `wet`, `dry`) | B (Content Code) | Opaque string | Game-configurable season names defined in calendar templates. Referenced by Transit for seasonal connection availability. Extensible without schema changes |
| era label codes | B (Content Code) | Opaque string | Game-configurable era names for year-range labeling in calendar templates. Optional, extensible without schema changes |
| `calendarTemplateCode` | B (Content Code) | Opaque string | Unique identifier for calendar template definitions, registered via API per game service. Extensible without schema changes |

---

## Client Time Synchronization

Worldstate pushes `WorldstateTimeSyncEvent` (defined in `worldstate-client-events.yaml`) to connected WebSocket sessions via the Entity Session Registry pattern. Worldstate calls `IEntitySessionRegistry.PublishToEntitySessionsAsync("realm", realmId, event)` which resolves realm→session mappings and publishes to those sessions. Agency (L4) registers `("realm", realmId) → sessionId` bindings when a player enters a realm.

Syncs are published on period-changed boundaries (~5 per game day, ~12 real minutes at 24:1), on ratio changes, on admin clock advancement, and on-demand via the `TriggerTimeSync` endpoint.

The event includes a full game time snapshot (all `GameTimeSnapshot` fields inlined), `previousPeriod` (nullable), and `syncReason` (`TimeSyncReason` enum). Between syncs, the client advances its local clock: `localGameSeconds += realDelta * timeRatio`. The `timestamp` field (inherited from `BaseClientEvent`) provides the reference for drift correction.

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
| `MaxBatchRealmTimeQueries` | `WORLDSTATE_MAX_BATCH_REALM_TIME_QUERIES` | `100` | Maximum realms in a single `BatchGetRealmTimes` request (range: 10-500). |
| `DefaultCalendarTemplateCode` | `WORLDSTATE_DEFAULT_CALENDAR_TEMPLATE_CODE` | *(nullable)* | Default calendar template code used as fallback when `InitializeRealmClock` is called without specifying a template. Null = no default (caller must specify). |
| `DistributedLockTimeoutSeconds` | `WORLDSTATE_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `10` | Timeout for distributed lock acquisition (range: 5-60). |

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
│  │  1. Acquire lock: worldstate:clock:{realmId}                        │  │
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
│  ┌─────────────────────────────┐  ┌─────────────────────────────────┐    │
│  │ Variable Provider           │  │ Service-Level Subscribers        │    │
│  │ (Actor L2, per-tick)        │  │ (Workshop, Craft, Seed, Currency)│    │
│  │                             │  │                                 │    │
│  │ ${world.time.period}        │  │ worldstate.day-changed →        │    │
│  │ ${world.season}             │  │   Workshop: materialize prod.   │    │
│  │ ${world.time.hour}          │  │   Currency: process autogain    │    │
│  │ etc.                        │  │   Seed: apply growth decay      │    │
│  │                             │  │                                 │    │
│  │ Reads from Redis cache      │  │ worldstate.season-changed →     │    │
│  │ (IRealmClockCache)          │  │   Loot: rotate seasonal tables  │    │
│  │ No event subscription       │  │   Market: activate seasonal     │    │
│  │ Natural load spreading      │  │   Storyline: unlock seasonal    │    │
│  └─────────────────────────────┘  └─────────────────────────────────┘    │
│                                                                           │
│  Actors do NOT subscribe to boundary events.                              │
│  They query ${world.*} on their own 100-500ms tick.                       │
│  This naturally distributes 100,000+ NPC time-checks                      │
│  across the tick interval instead of thundering-herding                    │
│  on a single boundary event.                                              │
└───────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

All 18 endpoints are fully implemented. No stubs remain.

1. **Ratio history compaction**: The `RatioHistoryRetentionDays` configuration property was planned but is not implemented. The configuration property does not exist in the generated config class. Ratio history segments accumulate indefinitely. For long-running servers, this could grow unbounded.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/529 -->

---

## Potential Extensions

1. **Location-specific time zones**: Locations within a realm could have time offsets (eastern regions experience dawn before western regions). The calendar template could define optional time zone offsets per location or location subtree.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/532 -->

2. **Magical time dilation zones**: Locations where time flows differently (a fairy realm where 1 game hour = 10 game hours, or a cursed zone where time is frozen). Connects to the `ITemporalManager` interface already defined in `bannou-service/Behavior/` for cinematic time dilation.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/534 -->

3. **Calendar events/holidays**: Named dates in the calendar that repeat annually (harvest festival on Greenleaf 15, winter solstice on Frostmere 1). Publishable as events when the date is reached.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/538 -->

4. **Lunar cycles / celestial events**: Additional cyclical phenomena beyond seasons (moon phases, eclipses, celestial alignments). Configurable as additional cycles with their own variable namespace (`${world.moon.phase}`).
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/540 -->

5. **Historical time queries**: "What season was it on game-year 47, month 3?" Useful for Storyline's retrospective narrative generation. Pure calendar math, no state required.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/542 -->

6. **Variable-rate time**: Time ratio that changes based on player population. When no players are in a realm, time accelerates to advance the simulation faster.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/543 -->

---

## Variable Provider: `${world.*}` Namespace

Implements `IVariableProviderFactory` (via `WorldProviderFactory`) providing the following variables to Actor (L2) via the Variable Provider Factory pattern. Loads from the in-memory realm clock cache (`IRealmClockCache`).

**Provider initialization**: When `CreateAsync(characterId, realmId, locationId, ct)` is called, the factory:
1. Uses the `realmId` parameter directly (always provided by the Actor runtime -- every actor belongs to a realm)
2. Reads the realm clock from `IRealmClockCache` (TTL-based, avoids Redis per-resolution)
3. Returns a `WorldProvider` populated with the current game time snapshot

Note: `characterId` and `locationId` are ignored -- worldstate is realm-scoped, not entity-scoped.

### Time Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${world.time.hour}` | int | Current game hour (0 to gameHoursPerDay-1) |
| `${world.time.minute}` | int | Current game minute (0-59) |
| `${world.time.period}` | string | Current day period code (e.g., "dawn", "morning", "afternoon", "evening", "night") |
| `${world.time.is_day}` | bool | True when the current day period's `isDaylight` flag is true in the calendar template |
| `${world.time.is_night}` | bool | Inverse of `is_day` -- true when the current day period's `isDaylight` flag is false |

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

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No active bugs.)*

### Intentional Quirks (Documented Behavior)

1. **ABML behavior namespace cleanup needed**: Several example behavior files reference variables under the `${world.*}` namespace that do NOT belong to worldstate: `${world.weather.temperature}` and `${world.weather.raining}` (these are `${environment.*}` variables from lib-environment L4), and `${world.patrol_routes[...]}` (operational data, not temporal). These example files predate the final namespace design and need updating. The `${world.*}` namespace owned by worldstate is strictly temporal: time, calendar, and season data.

2. **Realm clocks are independent**: Two realms running on the same server can be in different seasons, years, or even different calendar systems entirely. There is no global game time. This is intentional -- Arcadia, Fantasia, and Omega are peer worlds that may have different temporal scales.

3. **Hour-changed events are frequent**: At 24:1 ratio, `worldstate.hour-changed` fires every ~2.5 real minutes. Services should subscribe only to the granularity they need. Most services want `day-changed` (hourly real-time) or `season-changed` (~every 3 real days).

4. **Variable provider returns snapshot, not live values**: The `WorldProvider` created for an actor captures a snapshot of the realm clock at creation time. If the actor's tick runs for more than `ClockCacheTtlSeconds`, the time data could be slightly stale. This is acceptable -- NPC decisions don't require sub-second time accuracy.

5. **Calendar changes affect running clocks immediately**: Updating a calendar template takes effect on the next clock tick. Running realm clocks don't "notice" the change until they're advanced. Calendar changes on active realms should be done during maintenance windows.

6. **Catch-up boundary events are batched, not individual**: After downtime with `Advance` policy, the service publishes up to `BoundaryEventBatchSize` events per tick with catch-up metadata. Consumers must handle `daysCrossed > 1` (and similar) in catch-up events.

7. **Ratio of 0.0 is a valid pause**: Setting a realm's time ratio to 0.0 freezes its clock. `GetElapsedGameTime` returns 0 game-seconds for any real-time range that falls within a 0.0-ratio segment.

8. **Multi-node clock advancement uses distributed locks**: The background worker runs on every node, but only one node can advance a given realm's clock at a time (distributed lock on `clock:{realmId}`). If the lock-holding node dies mid-tick, the lock TTL must expire before another node can advance. The gap is bounded by `DistributedLockTimeoutSeconds` (default 10s) and handled naturally by catch-up logic on the next successful tick.

9. **CalendarLookup uses precomputed lookup tables**: `WorldstateTimeCalculator` builds `CalendarLookup` instances with precomputed arrays for O(1) hour→period and month→season resolution. These are cached in a `ConcurrentDictionary` keyed by calendar identity and invalidated when calendar templates change.

10. **All lock operations use a single lock store**: All distributed locks (`clock:{realmId}`, `calendar:{gsId}:{templateCode}`) share the single `WorldstateLock` store with different resource keys, rather than separate lock stores per entity type.

11. **Self-subscriptions for cross-node cache invalidation**: The 5 consumed events are all self-subscriptions — worldstate subscribes to its own published events to invalidate local in-memory caches on other nodes. This follows the DI Provider vs Listener pattern from SERVICE-HIERARCHY.md: events guarantee distributed delivery while local caches are the optimization layer.

### Design Considerations (Requires Planning)

1. **Game-time in existing schemas**: Encounter's `inGameTime` and Relationship's `inGameTimestamp` fields are currently caller-provided. Should these services auto-populate from worldstate? Auto-population requires those L2 services to depend on worldstate (same layer -- allowed). The migration path needs consideration.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/544 -->

2. **Currency/Seed migration to game-time**: Adding optional game-time support requires a configuration flag per service and careful handling of the transition (existing data uses real-time timestamps; switching mid-stream requires conversion).
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/545 -->

3. **Background worker scalability**: With many realms (100+), a single worker iteration may not complete within the tick interval. The current implementation processes realms sequentially. Options: (a) parallel realm advancement with configurable concurrency, (b) partitioning realms across nodes via `SupportedRealms` configuration (similar to GameSession's `SupportedGameServices` pattern), (c) staggered advancement.
<!-- AUDIT:NEEDS_DESIGN:2026-03-01:https://github.com/beyond-immersion/bannou-service/issues/546 -->

---

## Work Tracking

*Remaining stubs: ratio history compaction (issue #529).*
