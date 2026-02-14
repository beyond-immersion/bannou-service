# Environment Plugin Deep Dive

> **Plugin**: lib-environment
> **Schema**: schemas/environment-api.yaml
> **Version**: 1.0.0
> **State Stores**: environment-climate (MySQL), environment-weather (Redis), environment-conditions (Redis), environment-overrides (MySQL), environment-lock (Redis)
> **Status**: Pre-implementation (architectural specification)

---

## Overview

Environmental state service (L4 GameFeatures) providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability for game worlds. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot generation, and player experience. The missing ecological layer between Worldstate's clock and the behavioral systems that already reference environmental data that doesn't exist.

**The problem this solves**: The codebase references environmental data everywhere with no provider. ABML behaviors check `${world.weather.temperature}` -- phantom variable, no provider. Mapping has a `TtlWeatherEffects` spatial layer (600s TTL) ready to receive weather data that nobody publishes. Ethology's environmental overrides were designed to compose with ecological conditions that don't exist. Loot tables reference seasonal resource availability with no authority to consult. Trade routes reference environmental conditions for route activation. Market's economy planning documents reference seasonal trade and harvest cycles. Workshop's production modifiers reference seasonal rates. All of these point at a service that was never built.

**What this service IS**:
1. A **weather simulation** -- per-realm, per-location weather computed from climate templates, season, time of day, and deterministic noise
2. A **temperature model** -- base temperature from season + time-of-day curves + altitude/biome modifiers + weather modifiers
3. An **atmospheric condition provider** -- precipitation type/intensity, wind speed/direction, humidity, visibility, cloud cover
4. A **resource availability authority** -- seasonal ecological abundance per biome, affected by weather and divine intervention
5. A **variable provider** -- `${environment.*}` namespace for ABML behavior expressions
6. A **weather event system** -- time-bounded environmental phenomena (storms, droughts, blizzards, heat waves) registered by divine actors or scheduled from climate templates

**What this service is NOT**:
- **Not a clock** -- time is Worldstate's concern. Environment consumes time, it doesn't define it.
- **Not a spatial engine** -- where things are is Location and Mapping's concern. Environment answers "what are conditions like HERE?"
- **Not a physics simulation** -- rain doesn't pool, wind doesn't blow objects, snow doesn't accumulate. Those are client-side rendering concerns. Environment provides the DATA that clients render.
- **Not a biome definition service** -- biome types are Location metadata. Environment computes conditions WITHIN biomes, it doesn't define biome boundaries.
- **Not Ethology** -- Ethology provides behavioral archetypes and nature values. Environment provides the conditions that Ethology's environmental overrides react to. They compose, they don't overlap.

**Deterministic weather**: Weather is not random. Given a realm, a location, a game-day, and a season, the weather is deterministic -- hash the realm ID + game-day number + climate template to produce consistent weather. The same location on the same game-day always has the same weather across all server nodes, across restarts, across lazy evaluation windows. This follows the same principle as Ethology's deterministic individual noise: consistency without per-instance storage.

**Why deterministic?** Random weather would produce different conditions on different nodes in a multi-instance deployment, break lazy evaluation (weather between two timestamps must be reproducible), and make NPC weather-aware decisions inconsistent. Deterministic weather means a farmer NPC deciding whether to plant today gets the same answer regardless of which server node processes the request.

**Divine weather manipulation**: Gods (via Puppetmaster/Actor) can register weather overrides that replace or modify the deterministic baseline for a location or realm. A storm god summons a thunderstorm. A nature deity blesses a drought-stricken region with rain. These overrides are time-bounded and stored as explicit records, layered on top of the deterministic baseline. When the override expires, weather returns to the deterministic pattern.

**Zero game-specific content**: lib-environment is a generic environmental state engine. Arcadia's specific biome types (temperate_forest, alpine, desert), weather distributions (70% chance of rain in spring forests), and temperature curves are configured through climate template seeding at deployment time. A space game could model atmospheric composition and radiation levels. A survival game could model wind chill and hypothermia risk. The service stores float values against string-coded condition axes -- it doesn't care what the conditions mean.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on analysis of the environmental gap across the codebase -- the phantom `${world.weather.temperature}` ABML references, the `TtlWeatherEffects` Mapping layer, the seasonal availability flags in economy architecture, the environmental overrides in Ethology designed to compose with data that doesn't exist, and Worldstate's explicit exclusion of weather from its scope. Internal-only, never internet-facing.

---

## Core Concepts

### Climate Template

A climate template defines the baseline environmental patterns for a biome type within a game service. Climate templates are the environmental equivalent of Worldstate's calendar templates -- they define the rules, not the instances.

```
ClimateTemplate:
  templateId:        Guid
  gameServiceId:     Guid
  biomeCode:         string              # "temperate_forest", "alpine", "desert", "tropical", "tundra", etc.
  displayName:       string
  description:       string

  # Temperature curves (game-hour indexed, per season)
  temperatureCurves:
    - seasonCode:    string              # References Worldstate calendar season code
      baseMinTemp:   float               # Minimum daily temperature (arbitrary units, typically Celsius-like)
      baseMaxTemp:   float               # Maximum daily temperature
      hourlyShape:   string              # "sinusoidal" (default), "plateau", "spike" -- curve shape for intra-day variation
      peakHour:      int                 # Game-hour of maximum temperature (default: 14)
      troughHour:    int                 # Game-hour of minimum temperature (default: 4)

  # Weather distributions (probability weights per season)
  weatherDistributions:
    - seasonCode:    string
      patterns:
        - weatherCode: string            # "clear", "cloudy", "overcast", "light_rain", "heavy_rain", "storm", "snow", "fog", "haze"
          weight:      float             # Relative probability weight for this season
          durationGameHours: int         # Typical duration in game-hours before re-rolling
          temperatureModifier: float     # Additive temp modifier while this weather is active
          visibilityMultiplier: float    # 1.0 = normal, 0.3 = heavy fog, 0.1 = blizzard
          precipitationIntensity: float  # 0.0 = none, 1.0 = maximum

  # Atmospheric baselines (per season)
  atmosphericBaselines:
    - seasonCode:    string
      baseHumidity:  float               # 0.0-1.0
      baseWindSpeed: float               # 0.0-1.0 (normalized; games interpret scale)
      windDirectionBias: float           # 0.0-360.0 degrees (prevailing wind direction)
      windVariance:  float               # Degrees of random deviation from bias

  # Resource availability (per season)
  resourceAvailability:
    - seasonCode:    string
      abundanceLevel: float              # 0.0 (barren) to 1.0 (abundant)
      forageModifier: float              # Multiplier for forageable resource generation
      huntModifier:   float              # Multiplier for wildlife activity

  # Altitude and depth modifiers
  altitudeTemperatureRate: float         # Temperature change per altitude unit (default: -0.006, lapse rate)
  depthTemperatureRate:   float          # Temperature change per depth unit (default: +0.003, geothermal)

  isDeprecated:      bool
  createdAt:         DateTime
  updatedAt:         DateTime
```

**Key design decisions**:

1. **Temperature curves are per-season, not per-month**: Seasons are the natural ecological boundary. A temperate forest has broadly similar temperature patterns throughout spring, with gradual transition handled by the season-progress interpolation (see Temperature Computation below).

2. **Weather patterns have durations**: Weather doesn't change every game-hour. A clear day stays clear for 8-16 game-hours. A storm lasts 4-8 game-hours. The duration prevents unrealistic weather flip-flopping and enables meaningful NPC planning ("it's been raining for 6 hours, it should clear soon").

3. **Resource availability is abstract**: `abundanceLevel` is a float, not a list of specific resources. Loot tables, Workshop blueprints, and economy services interpret abundance in their own domain. A "lean" season (0.3) means Loot generates fewer forage items, Workshop farming blueprints produce at reduced rate, and Market vendors raise food prices. The environment doesn't know about specific resources -- it provides the ecological context.

4. **Altitude/depth modifiers are continuous**: Rather than discrete altitude bands, temperature changes linearly with altitude at a configurable rate. A mountain peak at altitude 3000 is `3000 * -0.006 = -18` degrees colder than sea level. A deep cavern at depth 500 is `500 * +0.003 = +1.5` degrees warmer. Location's spatial coordinates provide the altitude; Environment applies the modifier.

### Location Climate Binding

Each location in the game world is bound to a climate template via its biome type. The binding is resolved hierarchically:

```
LocationClimateBinding:
  locationId:        Guid
  realmId:           Guid
  biomeCode:         string              # From Location's metadata
  climateTemplateId: Guid                # Resolved from biomeCode + gameServiceId
  altitude:          float               # From Location's spatial coordinates (Y axis or explicit)
  depth:             float               # For underground locations (0 for surface)
```

**Resolution hierarchy**:
1. Location has an explicit `biomeCode` in its metadata → use matching climate template
2. Location inherits biome from parent location → walk up the location tree
3. No biome found → use realm's default biome (set via realm configuration)
4. No realm default → use a game service-level fallback template

This means a city location inherits the biome of its parent region, but a specific cave system within the city can override to `underground`. New locations automatically inherit appropriate climate without explicit binding.

### Temperature Computation

Temperature is computed, not stored. Given a location and a game-time snapshot, the temperature is:

```
ComputeTemperature(location, gameTimeSnapshot):
  template = resolveClimateTemplate(location)

  # 1. Get seasonal base temperature
  currentSeason = gameTimeSnapshot.season
  nextSeason = getNextSeason(gameTimeSnapshot, template)
  seasonProgress = gameTimeSnapshot.seasonProgress  # 0.0-1.0 from Worldstate

  currentCurve = template.temperatureCurves[currentSeason]
  nextCurve = template.temperatureCurves[nextSeason]

  # Interpolate between current and next season for smooth transitions
  baseMin = lerp(currentCurve.baseMinTemp, nextCurve.baseMinTemp, seasonProgress * 0.5)
  baseMax = lerp(currentCurve.baseMaxTemp, nextCurve.baseMaxTemp, seasonProgress * 0.5)

  # 2. Apply hourly curve (sinusoidal by default)
  gameHour = gameTimeSnapshot.hour
  hourFraction = sinusoidalInterpolation(gameHour, currentCurve.peakHour, currentCurve.troughHour)
  baseTemp = lerp(baseMin, baseMax, hourFraction)

  # 3. Apply altitude/depth modifier
  baseTemp += location.altitude * template.altitudeTemperatureRate
  baseTemp += location.depth * template.depthTemperatureRate

  # 4. Apply weather modifier
  currentWeather = resolveWeather(location, gameTimeSnapshot)
  baseTemp += currentWeather.temperatureModifier

  # 5. Apply divine overrides (if any)
  override = getActiveOverride(location, gameTimeSnapshot)
  if override != null && override.temperatureOverride != null:
    baseTemp = override.temperatureOverride  # Hard override
  elif override != null && override.temperatureModifier != null:
    baseTemp += override.temperatureModifier  # Additive modifier

  # 6. Apply deterministic location noise (subtle per-location variation)
  noise = hash(location.locationId + "temperature" + gameTimeSnapshot.dayOfYear)
  baseTemp += normalizeToRange(noise, -1.5, +1.5)

  return baseTemp
```

**Season transition smoothing**: The `seasonProgress * 0.5` factor means temperature begins transitioning to the next season's values halfway through the current season. At season start (progress=0.0), temperature is 100% current season. At season midpoint (progress=0.5), it starts blending with next season. At season end (progress=1.0), it's 50/50. The full transition to the next season's baseline completes at the NEW season's midpoint. This prevents jarring temperature jumps at season boundaries.

### Deterministic Weather Resolution

Weather is deterministic per location per game-day, with optional divine overrides:

```
ResolveWeather(location, gameTimeSnapshot):
  # 1. Check for active divine weather override
  override = getActiveWeatherOverride(location, gameTimeSnapshot)
  if override != null:
    return override.weatherPattern

  # 2. Deterministic weather from hash
  template = resolveClimateTemplate(location)
  distribution = template.weatherDistributions[gameTimeSnapshot.season]

  # Day-level weather seed (same day = same weather)
  daySeed = hash(
    location.realmId +
    gameTimeSnapshot.year +
    gameTimeSnapshot.dayOfYear +
    "weather"
  )

  # Select weather pattern from weighted distribution using seed
  selectedPattern = weightedSelect(distribution.patterns, daySeed)

  # 3. Check if weather has changed since last period
  # Weather has a duration; once selected for a day-segment, it persists
  segmentIndex = gameTimeSnapshot.totalGameHours / selectedPattern.durationGameHours
  segmentSeed = hash(daySeed + segmentIndex)

  # Re-roll at segment boundaries (weather changes every durationGameHours)
  return weightedSelect(distribution.patterns, segmentSeed)
```

**Why per-day-segment, not per-hour?** Weather that changes every game-hour (2.5 real minutes at 24:1) would feel chaotic. Real weather has inertia. A rainstorm lasts hours, not minutes. The `durationGameHours` field on each weather pattern defines its persistence. Clear weather might last 12-24 game-hours (30-60 real minutes). A storm might last 4-8 game-hours (10-20 real minutes). This creates weather patterns that feel natural and that NPCs can meaningfully plan around.

**Location-scoped weather inheritance**: Weather is resolved per-location but inherits upward. A building inside a city experiences the same weather as the city. The city experiences the same weather as its parent region. Only locations with different biome codes get independent weather rolls. This prevents adjacent rooms in a dungeon from having different weather while allowing a mountain peak and a valley floor to have independent weather systems.

### Weather Events

Weather events are extraordinary environmental phenomena that override normal weather patterns for a bounded time period. They are registered by divine actors (via Puppetmaster ABML actions), scheduled from climate templates (annual monsoon season, winter solstice blizzard), or triggered by game events (volcanic eruption darkens the sky).

```
WeatherEvent:
  eventId:           Guid
  realmId:           Guid
  scopeType:         string              # "realm", "location", "location_subtree"
  scopeId:           Guid

  # Event definition
  eventCode:         string              # "thunderstorm", "drought", "blizzard", "heat_wave", "eclipse", "volcanic_ash"
  displayName:       string
  severity:          float               # 0.0-1.0 (intensity multiplier)

  # Weather overrides
  weatherCode:       string?             # Override weather pattern (null = keep existing)
  temperatureModifier: float?            # Additive temperature change
  temperatureOverride: float?            # Hard temperature override (takes precedence over modifier)
  visibilityMultiplier: float?           # Override visibility
  precipitationIntensity: float?         # Override precipitation
  windSpeedMultiplier: float?            # Multiply base wind speed
  resourceAvailabilityMultiplier: float? # Multiply seasonal resource availability

  # Timing
  startGameTime:     long                # Game-seconds-since-epoch when event begins
  endGameTime:       long?               # null = indefinite (must be ended explicitly)

  # Source tracking
  sourceType:        string              # "divine", "scheduled", "game_event", "admin"
  sourceId:          Guid?               # Actor ID, schedule ID, etc.

  isActive:          bool
  createdAt:         DateTime
```

**Event stacking**: Multiple events can affect the same location. They stack additively for modifiers and use last-wins for overrides. A drought (resource -0.5) and a divine blessing (resource +0.3) stack to net -0.2. A hard temperature override from a blizzard event takes precedence over seasonal temperature.

**Event scope types**:
- `realm`: Affects all locations in the realm (realm-wide drought, volcanic winter)
- `location`: Affects a single location only (localized storm at a mountain peak)
- `location_subtree`: Affects a location and all its descendants (forest fire in a region and all contained areas)

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Climate templates (MySQL), weather event overrides (MySQL), condition cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for climate template mutations, weather event creation/cancellation |
| lib-messaging (`IMessageBus`) | Publishing environment events (conditions-changed, weather-event-started, etc.), error event publication |
| lib-worldstate (`IWorldstateClient`) | Querying current game time, season, season progress for a realm. The foundational dependency -- Environment cannot compute anything without knowing what time it is. (L2) |
| lib-location (`ILocationClient`) | Resolving location metadata (biome code, altitude, parent hierarchy) for climate template binding and weather inheritance. (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scope validation for climate templates. (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-mapping (`IMappingClient`) | Publishing weather data to Mapping's `weather_effects` spatial layer for spatial weather queries. (L4) | Weather data is not published to the spatial index. Spatial weather queries return nothing. Environmental conditions still resolve for direct queries. |
| lib-realm (`IRealmClient`) | Querying realm default biome for locations without explicit biome metadata. (L2, but accessed via soft pattern because fallback is acceptable) | Locations without explicit biome metadata and without inherited biome from parent fall through to the game service-level fallback climate template. |
| lib-analytics (`IAnalyticsClient`) | Publishing environmental statistics (weather distribution accuracy, override frequency, resource availability trends). (L4) | No analytics. Silent skip. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `EnvironmentProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates provider instances per entity for ABML behavior execution (`${environment.*}` variables). NPCs make weather-aware decisions: stay indoors during storms, wear warm clothes in cold, migrate during droughts. |
| lib-ethology (L4) | Ethology's seasonal behavior modifier extension consumes `environment.conditions-changed` events to apply automatic environmental overrides to creature archetypes. Wolves are more aggressive in harsh winter; deer are more vigilant during storms. |
| lib-workshop (L4) | Workshop's seasonal production modifier extension queries current resource availability to adjust production rates. Farming blueprints produce at reduced rate during drought, increased rate during abundant seasons. |
| lib-loot (L4, planned) | Loot table seasonal modifiers query current season + resource availability from Environment to adjust drop rates. Forageable items are scarcer in winter, abundant in autumn harvest. |
| lib-market (L4, planned) | Market price modifiers adjust NPC vendor pricing based on resource availability. Scarcity drives prices up; abundance drives them down. |
| lib-mapping (L4) | Mapping receives weather data published to its `weather_effects` spatial layer, enabling spatial weather queries for game clients and NPC perception. |
| lib-puppetmaster (L4) | Regional watcher behaviors (storm gods, nature deities) create and manage weather events through ABML actions (`register_weather_event`, `cancel_weather_event`). Puppetmaster provides the ABML action handlers that call Environment's API. |
| lib-storyline (L4) | Storyline's scenario preconditions can reference environmental state (drought conditions trigger famine storylines, volcanic winter enables survival narratives). |
| lib-craft (L4, planned) | Certain crafting recipes could reference environmental conditions (outdoor forge requires non-rain weather, ice sculpting requires freezing temperature). |
| lib-disposition (L4, planned) | Environmental conditions could feed into NPC mood as a modifier source. Persistent rain lowers mood; warm sunny days improve it. |

---

## State Storage

### Climate Template Store
**Store**: `environment-climate` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `climate:{templateId}` | `ClimateTemplateModel` | Primary lookup. Full climate template with all curves, distributions, and baselines. |
| `climate:biome:{gameServiceId}:{biomeCode}` | `ClimateTemplateModel` | Biome-based lookup within game scope. Primary resolution path for the variable provider. |
| `climate:game:{gameServiceId}` | `ClimateTemplateModel` | All climate templates for a game service (for listing/admin). |

### Weather Event Store
**Store**: `environment-overrides` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `event:{eventId}` | `WeatherEventModel` | Primary lookup. Full weather event definition. |
| `event:scope:{scopeType}:{scopeId}` | `WeatherEventListModel` | Active events for a specific realm/location scope. Used during weather resolution to check for overrides. |
| `event:source:{sourceType}:{sourceId}` | `WeatherEventListModel` | Events created by a specific source (for cleanup when a divine actor is stopped). |

### Weather Cache
**Store**: `environment-weather` (Backend: Redis, prefix: `environment:weather`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `resolved:{realmId}:{locationId}:{gameDay}` | `ResolvedWeatherModel` | Cached resolved weather for a location on a game-day. Contains weather code, temperature range, precipitation, visibility, wind, resource availability. Keyed by game-day so the cache self-invalidates on day boundaries. TTL matches Worldstate's day duration in real-time. |

### Condition Cache
**Store**: `environment-conditions` (Backend: Redis, prefix: `environment:conditions`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `snapshot:{realmId}:{locationId}` | `ConditionSnapshotModel` | Latest computed environmental conditions for a location. Short TTL (default 60 seconds). Used by the variable provider for fast reads during behavior ticks. Contains the full snapshot: temperature, weather, precipitation, wind, humidity, visibility, resource availability. |

### Distributed Locks
**Store**: `environment-lock` (Backend: Redis, prefix: `environment:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `climate:{gameServiceId}:{biomeCode}` | Climate template mutations |
| `event:{scopeType}:{scopeId}` | Weather event creation/cancellation for a scope |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `environment.conditions-changed` | `EnvironmentConditionsChangedEvent` | Weather pattern changed for a location (new weather segment, weather event started/ended). Includes realmId, locationId, previousWeather, currentWeather, temperature, full condition snapshot. Published at weather segment boundaries, not continuously. |
| `environment.weather-event.started` | `EnvironmentWeatherEventStartedEvent` | Weather event activated. Includes eventId, eventCode, severity, scopeType, scopeId, sourceType, duration. |
| `environment.weather-event.ended` | `EnvironmentWeatherEventEndedEvent` | Weather event ended (expired or cancelled). Includes eventId, eventCode, scopeType, scopeId, reason ("expired", "cancelled", "superseded"). |
| `environment.resource-availability.changed` | `EnvironmentResourceAvailabilityChangedEvent` | Seasonal resource availability changed for a realm (on season boundary or weather event). Includes realmId, previousAbundance, currentAbundance, season. |
| `environment-climate.created` | `EnvironmentClimateCreatedEvent` | Climate template created (lifecycle). |
| `environment-climate.updated` | `EnvironmentClimateUpdatedEvent` | Climate template updated (lifecycle). |
| `environment-climate.deprecated` | `EnvironmentClimateDeprecatedEvent` | Climate template deprecated (lifecycle). |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.period-changed` | `HandlePeriodChangedAsync` | Re-compute and cache environmental conditions for all locations in the affected realm. Publish `conditions-changed` events where weather or temperature changed meaningfully. Primary trigger for environmental state updates. |
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | Trigger resource availability recalculation for all locations in the affected realm. Publish `resource-availability.changed`. Invalidate cached weather distributions (new season = new weather probabilities). |
| `worldstate.day-changed` | `HandleDayChangedAsync` | Invalidate day-keyed weather cache entries for the realm (new game-day = new deterministic weather roll). Lightweight -- just cache invalidation. |
| `location.created` | `HandleLocationCreatedAsync` | Resolve climate template binding for the new location (inherit biome from parent or use explicit biome metadata). |
| `location.deleted` | `HandleLocationDeletedAsync` | Clean up any location-scoped weather events and cached conditions. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| realm | environment | CASCADE | `/environment/cleanup-by-realm` |
| game-service | environment | CASCADE | `/environment/cleanup-by-game-service` |
| location | environment | CASCADE | `/environment/cleanup-by-location` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ConditionCacheTtlSeconds` | `ENVIRONMENT_CONDITION_CACHE_TTL_SECONDS` | `60` | TTL for cached condition snapshots in Redis. Variable providers read from this cache. (range: 10-300) |
| `WeatherCacheTtlSeconds` | `ENVIRONMENT_WEATHER_CACHE_TTL_SECONDS` | `600` | TTL for cached resolved weather per location per game-day in Redis. (range: 60-3600) |
| `ClimateCacheTtlMinutes` | `ENVIRONMENT_CLIMATE_CACHE_TTL_MINUTES` | `60` | TTL for in-memory climate template cache. Templates change rarely. (range: 5-1440) |
| `ConditionUpdateIntervalSeconds` | `ENVIRONMENT_CONDITION_UPDATE_INTERVAL_SECONDS` | `30` | Real-time seconds between background condition refresh cycles. (range: 5-300) |
| `ConditionUpdateStartupDelaySeconds` | `ENVIRONMENT_CONDITION_UPDATE_STARTUP_DELAY_SECONDS` | `15` | Seconds to wait after startup before first condition refresh, allowing Worldstate to initialize. (range: 0-120) |
| `MaxClimateTemplatesPerGameService` | `ENVIRONMENT_MAX_CLIMATE_TEMPLATES_PER_GAME_SERVICE` | `50` | Safety limit on climate templates per game service. (range: 5-200) |
| `MaxActiveEventsPerScope` | `ENVIRONMENT_MAX_ACTIVE_EVENTS_PER_SCOPE` | `10` | Maximum concurrent weather events per realm or location scope. (range: 1-50) |
| `DefaultBiomeCode` | `ENVIRONMENT_DEFAULT_BIOME_CODE` | `temperate` | Fallback biome code when location has no biome metadata and no parent with biome. |
| `TemperatureNoiseAmplitude` | `ENVIRONMENT_TEMPERATURE_NOISE_AMPLITUDE` | `1.5` | Maximum per-location deterministic temperature noise in degrees. (range: 0.0-5.0) |
| `WeatherTransitionDampening` | `ENVIRONMENT_WEATHER_TRANSITION_DAMPENING` | `0.3` | Probability of weather pattern repeating from previous segment, creating weather persistence. 0.0 = no dampening (pure distribution), 1.0 = weather never changes. (range: 0.0-0.9) |
| `MappingPublishEnabled` | `ENVIRONMENT_MAPPING_PUBLISH_ENABLED` | `true` | Whether to publish weather data to Mapping's weather_effects spatial layer. Disable if Mapping is not deployed. |
| `MappingPublishBatchSize` | `ENVIRONMENT_MAPPING_PUBLISH_BATCH_SIZE` | `100` | Maximum location condition updates published to Mapping per refresh cycle. (range: 10-1000) |
| `EventExpirationCheckIntervalSeconds` | `ENVIRONMENT_EVENT_EXPIRATION_CHECK_INTERVAL_SECONDS` | `60` | Real-time seconds between weather event expiration checks. (range: 10-600) |
| `DistributedLockTimeoutSeconds` | `ENVIRONMENT_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `10` | Timeout for distributed lock acquisition. (range: 5-60) |
| `QueryPageSize` | `ENVIRONMENT_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries. (range: 1-100) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<EnvironmentService>` | Structured logging |
| `EnvironmentServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 5 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IWorldstateClient` | Game time queries: current time, season, season progress (L2 hard) |
| `ILocationClient` | Location metadata: biome, altitude, parent hierarchy (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft dependencies (lib-mapping, lib-realm) |
| `EnvironmentConditionWorkerService` | Background `HostedService` that periodically refreshes environmental conditions for active realms. Publishes `conditions-changed` events when weather transitions occur. |
| `EnvironmentEventExpirationWorkerService` | Background `HostedService` that scans for expired weather events and deactivates them. Publishes `weather-event.ended` events. |
| `EnvironmentProviderFactory` | Implements `IVariableProviderFactory` to provide `${environment.*}` variables to Actor's behavior system. |
| `IWeatherResolver` / `WeatherResolver` | Internal helper for deterministic weather computation from climate templates, season, and location. |
| `ITemperatureCalculator` / `TemperatureCalculator` | Internal helper for temperature computation with seasonal curves, altitude, weather modifiers, and noise. |
| `IClimateTemplateCache` / `ClimateTemplateCache` | In-memory TTL cache for climate templates. Templates change rarely; caching avoids MySQL queries on every condition computation. |

### Variable Provider Factory

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `EnvironmentProviderFactory` | `${environment.*}` | Reads from condition snapshot cache (Redis). Resolves character's locationId and realmId via `ICharacterClient` or actor metadata. Provides computed temperature, weather, atmospheric conditions, and resource availability. | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Condition Queries (4 endpoints)

- **GetConditions** (`/environment/conditions/get`): Returns the full environmental condition snapshot for a location: temperature, weather code, precipitation type/intensity, wind speed/direction, humidity, visibility, cloud cover, resource availability. Reads from condition cache (hot path). If cache is empty, computes from climate template + Worldstate + weather resolution.

- **GetConditionsByCode** (`/environment/conditions/get-by-code`): Convenience endpoint accepting realm code + location code instead of GUIDs.

- **BatchGetConditions** (`/environment/conditions/batch-get`): Returns condition snapshots for multiple locations in a single call. Optimized to share climate template and Worldstate lookups for locations in the same realm.

- **GetTemperature** (`/environment/conditions/get-temperature`): Lightweight endpoint returning only temperature for a location. Cheaper than full condition computation. Used by services that only need temperature (e.g., crafting weather checks).

### Climate Template Management (6 endpoints)

All require `developer` role.

- **SeedClimate** (`/environment/climate/seed`): Creates a climate template for a biome within a game service. Validates: game service exists, biome code unique within game scope, temperature curves cover all seasons in the game's calendar, weather distribution weights are positive, seasonal baselines reference valid season codes. Enforces `MaxClimateTemplatesPerGameService`.

- **GetClimate** (`/environment/climate/get`): Returns full climate template by ID or by gameServiceId + biomeCode.

- **ListClimates** (`/environment/climate/list`): Paged list of climate templates within a game service.

- **UpdateClimate** (`/environment/climate/update`): Acquires distributed lock. Partial update. Validates structural consistency. Invalidates climate cache and all condition caches for locations using this template. Active weather computations use updated template starting next refresh cycle.

- **DeprecateClimate** (`/environment/climate/deprecate`): Marks deprecated with reason. Existing locations continue resolving. New location bindings with this biome code emit a warning.

- **BulkSeedClimates** (`/environment/climate/bulk-seed`): Bulk creation for world initialization. Idempotent with `updateExisting` flag.

### Weather Event Management (5 endpoints)

- **CreateWeatherEvent** (`/environment/weather-event/create`): Creates a time-bounded weather event. Validates: scope exists (realm or location), event code is non-empty, start time is in the future or "now", end time (if set) is after start time. Enforces `MaxActiveEventsPerScope`. Invalidates condition cache for affected scope. Publishes `weather-event.started`. Returns eventId.

- **GetWeatherEvent** (`/environment/weather-event/get`): Returns event by ID.

- **ListWeatherEvents** (`/environment/weather-event/list`): Lists events for a scope. Filterable by active/inactive, event code, source type.

- **CancelWeatherEvent** (`/environment/weather-event/cancel`): Cancels an active event. Invalidates condition cache. Publishes `weather-event.ended` with reason "cancelled".

- **ExtendWeatherEvent** (`/environment/weather-event/extend`): Extends an active event's end time. Cannot shorten (use cancel + create). Useful for divine actors prolonging a storm.

### Resource Availability (2 endpoints)

- **GetResourceAvailability** (`/environment/resource/get-availability`): Returns the current resource availability for a location, accounting for seasonal baseline, weather event modifiers, and divine overrides. Returns the float abundance level and contributing factors (base seasonal, event modifiers, net result).

- **GetRealmResourceSummary** (`/environment/resource/realm-summary`): Returns aggregate resource availability across all locations in a realm, grouped by biome. Used by Market for realm-wide economic analysis and by regional watchers for ecological assessments.

### Cleanup (3 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByRealm** (`/environment/cleanup-by-realm`): Removes all weather events, condition caches, and location bindings for a realm. Climate templates are NOT removed (they are game-scoped, not realm-scoped).
- **CleanupByGameService** (`/environment/cleanup-by-game-service`): Removes climate templates, weather events, and all derived data for a game service.
- **CleanupByLocation** (`/environment/cleanup-by-location`): Removes location-scoped weather events and cached conditions for a deleted location.

---

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────────┐
│                    ENVIRONMENTAL CONDITION RESOLUTION                      │
│                                                                          │
│  INPUT: locationId, realmId                                              │
│                                                                          │
│  STEP 1: RESOLVE CLIMATE TEMPLATE                                        │
│  ┌──────────────────────────────────────────────────────────┐           │
│  │ Location "Ironpeak Summit"                                │           │
│  │   biomeCode: "alpine"  (from location metadata)           │           │
│  │   altitude: 2800                                          │           │
│  │   climateTemplate: "alpine" for game "arcadia"            │           │
│  └───────────────────────┬──────────────────────────────────┘           │
│                           │                                               │
│  STEP 2: QUERY WORLDSTATE                                                │
│  ┌───────────────────────▼──────────────────────────────────┐           │
│  │ Worldstate says:                                          │           │
│  │   season: "winter"          seasonProgress: 0.7           │           │
│  │   time.hour: 3              time.period: "night"          │           │
│  │   dayOfYear: 267            year: 5                       │           │
│  └───────────────────────┬──────────────────────────────────┘           │
│                           │                                               │
│  STEP 3: COMPUTE TEMPERATURE                                             │
│  ┌───────────────────────▼──────────────────────────────────┐           │
│  │ Winter alpine curve: baseMin=-15, baseMax=-2              │           │
│  │ Spring alpine curve: baseMin=-5, baseMax=8                │           │
│  │ Season transition (progress=0.7): blending toward spring  │           │
│  │   interpolated min=-11.5, max=1.5                         │           │
│  │                                                            │           │
│  │ Hourly curve (hour=3, trough=4): near daily minimum       │           │
│  │   baseTemp = -10.8                                        │           │
│  │                                                            │           │
│  │ Altitude modifier: 2800 * -0.006 = -16.8                 │           │
│  │   afterAltitude = -27.6                                   │           │
│  │                                                            │           │
│  │ Weather modifier (snow): -2.0                             │           │
│  │   afterWeather = -29.6                                    │           │
│  │                                                            │           │
│  │ Location noise: hash("ironpeak"+"temp"+267) = +0.8        │           │
│  │   final temperature = -28.8                               │           │
│  └───────────────────────┬──────────────────────────────────┘           │
│                           │                                               │
│  STEP 4: RESOLVE WEATHER                                                 │
│  ┌───────────────────────▼──────────────────────────────────┐           │
│  │ Check divine overrides: none active                       │           │
│  │                                                            │           │
│  │ Deterministic roll:                                       │           │
│  │   hash(realmId + year=5 + day=267 + "weather") → seed     │           │
│  │   Winter alpine distribution:                             │           │
│  │     snow: 40%, clear: 25%, blizzard: 15%,                │           │
│  │     overcast: 15%, fog: 5%                                │           │
│  │   seed selects: "snow"                                    │           │
│  │   duration: 8 game-hours → segment persists               │           │
│  │                                                            │           │
│  │ Weather dampening (0.3): 30% chance previous weather      │           │
│  │   repeats → checked, new roll wins → "snow"               │           │
│  └───────────────────────┬──────────────────────────────────┘           │
│                           │                                               │
│  STEP 5: COMPOSE SNAPSHOT                                                │
│  ┌───────────────────────▼──────────────────────────────────┐           │
│  │ ConditionSnapshot:                                        │           │
│  │   temperature:        -28.8                               │           │
│  │   weatherCode:        "snow"                              │           │
│  │   precipitationType:  "snow"                              │           │
│  │   precipitationIntensity: 0.6                             │           │
│  │   windSpeed:          0.7                                 │           │
│  │   windDirection:      225.0  (southwest prevailing)       │           │
│  │   humidity:           0.85                                │           │
│  │   visibility:         0.4   (reduced by snow)             │           │
│  │   cloudCover:         0.9                                 │           │
│  │   resourceAvailability: 0.15 (winter alpine = scarce)     │           │
│  │                                                            │           │
│  │ → Cached in environment:conditions:snapshot:{locationId}   │           │
│  │ → Returned to Actor as IVariableProvider                   │           │
│  │ → Published to Mapping weather_effects layer               │           │
│  │ → Available as ${environment.*} in ABML expressions        │           │
│  └──────────────────────────────────────────────────────────┘           │
│                                                                          │
│  CONSUMER EXAMPLES:                                                      │
│  ┌───────────────────────────┐  ┌─────────────────────────────────┐    │
│  │ NPC Farmer ABML:          │  │ Ethology Seasonal Override:      │    │
│  │                           │  │                                   │    │
│  │ when: temp < 0            │  │ worldstate.season-changed →       │    │
│  │   → stay_indoors          │  │   query environment for realm     │    │
│  │                           │  │   if resourceAvail < 0.3:         │    │
│  │ when: weather == "rain"   │  │     wolf aggression += 0.15       │    │
│  │   → delay_planting        │  │     (scarce food = more aggro)   │    │
│  │                           │  │                                   │    │
│  │ when: resourceAvail > 0.7 │  │ Loot Seasonal Modifier:          │    │
│  │   → expand_operations     │  │   forageDropRate *= resourceAvail │    │
│  └───────────────────────────┘  └─────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Variable Provider: `${environment.*}` Namespace

Implements `IVariableProviderFactory` (via `EnvironmentProviderFactory`). Loads from the cached condition snapshot or computes on demand.

**Provider initialization**: When `CreateAsync(characterId, ct)` is called, the factory:
1. Resolves the character's `locationId` and `realmId` via `ICharacterClient` (cached per-character)
2. Reads the condition snapshot from `environment:conditions:snapshot:{locationId}` (TTL-based)
3. If cache miss, computes conditions from climate template + Worldstate + weather resolution
4. Returns an `EnvironmentProvider` populated with the current condition snapshot

### Temperature Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${environment.temperature}` | float | Current temperature at this location (computed from seasonal curve + altitude + weather + noise) |
| `${environment.temperature.feels_like}` | float | Wind-chill adjusted temperature (temperature - wind_speed * wind_chill_factor) |
| `${environment.temperature.is_freezing}` | bool | True when temperature < 0 |
| `${environment.temperature.is_hot}` | bool | True when temperature > climate template's heat threshold (configurable per biome) |

### Weather Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${environment.weather}` | string | Current weather code ("clear", "cloudy", "rain", "snow", "storm", "fog", etc.) |
| `${environment.weather.is_precipitation}` | bool | True when precipitation intensity > 0 |
| `${environment.weather.precipitation_type}` | string | "none", "rain", "snow", "sleet", "hail" |
| `${environment.weather.precipitation_intensity}` | float | 0.0-1.0 |
| `${environment.weather.is_storm}` | bool | True during storm-class weather patterns |
| `${environment.weather.is_clear}` | bool | True during clear weather |

### Atmospheric Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${environment.wind.speed}` | float | 0.0-1.0 normalized wind speed |
| `${environment.wind.direction}` | float | 0.0-360.0 degrees |
| `${environment.humidity}` | float | 0.0-1.0 |
| `${environment.visibility}` | float | 0.0-1.0 (1.0 = perfect, 0.0 = zero visibility) |
| `${environment.cloud_cover}` | float | 0.0-1.0 |

### Ecological Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${environment.resource_availability}` | float | 0.0-1.0 composite resource abundance for this location (seasonal + weather event modifiers) |
| `${environment.is_drought}` | bool | True when resource_availability < 0.2 (configurable threshold) |
| `${environment.is_abundant}` | bool | True when resource_availability > 0.8 (configurable threshold) |

### Event Variables

| Variable | Type | Description |
|----------|------|-------------|
| `${environment.has_active_event}` | bool | Whether any weather event affects this location |
| `${environment.active_event}` | string | Event code of the highest-severity active event (empty if none) |
| `${environment.active_event.severity}` | float | Severity of the active event (0.0 if none) |
| `${environment.active_event.source}` | string | Source type of the active event ("divine", "scheduled", etc.) |

### ABML Usage Examples

```yaml
flows:
  weather_aware_routine:
    # NPC adjusts daily routine based on weather
    - cond:
        # Terrible storm -- everyone stays inside
        - when: "${environment.weather.is_storm
                  && environment.wind.speed > 0.7}"
          then:
            - call: seek_shelter
            - set: { mood_modifier: -0.2 }

        # Rain -- do indoor work, postpone farming
        - when: "${environment.weather.is_precipitation
                  && !environment.weather.is_storm}"
          then:
            - call: indoor_work
            - set: { outdoor_preference: 0.2 }

        # Clear and warm -- great day for outdoor activities
        - when: "${environment.weather.is_clear
                  && environment.temperature > 15}"
          then:
            - call: outdoor_work
            - set: { mood_modifier: 0.1 }

        # Freezing -- bundle up, shorter outdoor time
        - when: "${environment.temperature.is_freezing}"
          then:
            - call: dress_warm
            - set: { outdoor_time_limit: 0.3 }

  survival_decisions:
    # Creature survival behavior in harsh conditions
    - cond:
        # Drought -- need to find water, may migrate
        - when: "${environment.is_drought
                  && nature.persistence < 0.5}"
          then:
            - call: seek_water_source
            - call: consider_migration

        # Low visibility -- predators have advantage
        - when: "${environment.visibility < 0.3
                  && nature.category == 'predator'}"
          then:
            - call: hunt_opportunistically
            - set: { stealth_bonus: "${1.0 - environment.visibility}" }

        # Low visibility -- prey is more cautious
        - when: "${environment.visibility < 0.3
                  && nature.category == 'prey'}"
          then:
            - call: stay_in_herd
            - set: { vigilance_boost: "${1.0 - environment.visibility}" }

  economic_decisions:
    # NPC merchant adjusts behavior based on ecology
    - cond:
        # Resources are scarce -- raise prices, reduce stock
        - when: "${environment.resource_availability < 0.3}"
          then:
            - set: { price_multiplier: "${1.5 + (0.3 - environment.resource_availability)}" }
            - call: reduce_stock_orders

        # Resources abundant -- lower prices, stock up
        - when: "${environment.resource_availability > 0.7}"
          then:
            - set: { price_multiplier: "${0.8}" }
            - call: increase_stock_orders

        # Divine event active -- acknowledge it in dialogue
        - when: "${environment.has_active_event
                  && environment.active_event.source == 'divine'}"
          then:
            - call: comment_on_divine_weather
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites (changes to existing services)

- **Worldstate (L2)**: Must be implemented and operational. Environment's entire computation depends on game time, season, and season progress from Worldstate. Worldstate is the hard L2 prerequisite.
- **Location (L2)**: Must support `biomeCode` and `altitude` metadata fields on locations. Location already stores arbitrary metadata; this is a convention for Environment to consume, not a schema change.

### Phase 1: Climate Template Infrastructure

- Create `environment-api.yaml` schema with all endpoints
- Create `environment-events.yaml` schema
- Create `environment-configuration.yaml` schema
- Generate service code
- Implement climate template CRUD (seed, get, list, update, deprecate, bulk-seed)
- Implement climate template validation (seasonal curve coverage, weather distribution weights, baseline completeness)

### Phase 2: Deterministic Weather Resolution

- Implement `WeatherResolver` with hash-based deterministic weather from climate templates
- Implement weather segment duration and transition dampening
- Implement location-scoped weather inheritance (walk up location tree)
- Implement day-keyed weather cache with TTL
- Implement `GetConditions` endpoint with full condition snapshot computation

### Phase 3: Temperature Computation

- Implement `TemperatureCalculator` with seasonal curve interpolation
- Implement altitude/depth modifiers
- Implement weather-based temperature modifiers
- Implement deterministic location noise
- Implement season transition smoothing
- Implement `GetTemperature` lightweight endpoint

### Phase 4: Weather Events

- Implement weather event CRUD (create, get, list, cancel, extend)
- Implement event stacking and override resolution
- Implement event expiration background worker
- Implement event-triggered condition cache invalidation
- Implement `weather-event.started` and `weather-event.ended` event publishing

### Phase 5: Variable Provider

- Implement `EnvironmentProviderFactory` and `EnvironmentProvider`
- Implement condition snapshot cache with TTL
- Implement character-to-location resolution caching
- Integration testing with Actor runtime (verify `${environment.temperature}` resolves)

### Phase 6: Integrations & Background Workers

- Implement `EnvironmentConditionWorkerService` for periodic condition refresh
- Implement Worldstate event handlers (period-changed, season-changed, day-changed)
- Implement Mapping weather layer publishing (soft dependency)
- Implement resource availability computation and events
- Implement resource cleanup endpoints
- Integration testing with Ethology, Workshop, Loot consumers

---

## Potential Extensions

1. **Microclimate zones**: Locations tagged with microclimate modifiers that deviate from their biome template. A volcanic hot spring in an alpine region has elevated temperature. A sheltered valley in a tundra is warmer than exposed ridgelines. Implemented as per-location overrides similar to Ethology's environmental overrides, but for climate parameters.

2. **Weather fronts**: Multi-location weather systems that move across realms over game-time. A storm front that starts in the western mountains and moves east over 3 game-days, affecting each location in sequence. Requires spatial awareness (which locations are adjacent) and scheduled weather event creation along the path.

3. **Ecological succession**: Resource availability that changes over long time scales based on environmental history. A forest that burned (divine fire event) has low resource availability for several game-years, then gradually recovers. Implemented as decay-based resource modifiers that trend toward the seasonal baseline over time.

4. **Weather prediction**: A `PredictWeather` endpoint that uses the deterministic weather algorithm to predict future weather for a location. Since weather is deterministic, prediction is just forward computation. NPCs could query "will it rain tomorrow?" to plan farming, travel, or military operations. Exposed as `${environment.forecast.*}` variables.

5. **Atmospheric phenomena**: Beyond basic weather -- auroras, meteor showers, volcanic ash clouds, sandstorms, magical storms. Registered as weather event types with custom visual and mechanical effects. The service stores and resolves them; client-side rendering interprets them.

6. **Climate change over game-years**: Climate template parameter drift over long time scales. A realm experiencing deforestation (tracked by Realm-History or Storyline) gradually shifts temperature curves warmer and resource availability lower. Implemented as per-realm climate modifier history similar to Worldstate's ratio segments.

7. **Indoor/outdoor detection**: Locations tagged as "indoor" skip weather computation entirely (buildings, caves, enclosed structures). The condition snapshot for indoor locations returns the biome's base temperature modified by depth (caves get warmer) with no weather, no wind, and full visibility. Simple boolean in location metadata.

8. **Client events for weather display**: `environment-client-events.yaml` for pushing weather transitions to connected WebSocket clients. Published when weather changes at a player's location (not continuously). Low-bandwidth: one event per weather transition per player location, not per game-tick.

9. **Biome transition zones**: Locations at the boundary between two biomes blend climate templates rather than using one or the other. A forest-edge location between temperate forest and alpine gets 60/40 blended temperature curves and weather distributions. Implemented as a secondary biome code with a blend weight.

10. **Seasonal migration triggers**: Publish advisory events when environmental conditions cross species-relevant thresholds (first frost, sustained drought, first snow). Ethology and Actor behaviors can subscribe to these for migration decision support. Not weather events (those are overrides) -- these are observations about the deterministic weather crossing significant boundaries.

---

## Why Not Extend Worldstate or Use Mapping?

**Worldstate (L2) is deliberately scoped as a clock, not a simulation.** Its deep dive explicitly excludes weather: "weather, temperature, atmospheric conditions are L4 concerns that consume season/time-of-day data from worldstate." Adding weather to Worldstate would violate the L2/L4 boundary -- weather simulation is a game feature, not game foundation. A deployment with `GAME_FEATURES=false` shouldn't need a weather engine. Worldstate answers "what time is it?" Environment answers "what's the weather like?"

**Mapping (L4) is a spatial data store, not a condition computer.** Mapping stores and queries 3D spatial data -- it's the "where" layer. Environment is the "what conditions" layer. They compose naturally: Environment computes conditions per-location, then publishes weather data to Mapping's `weather_effects` spatial layer for spatial queries. Mapping doesn't know what weather IS; it just stores and indexes spatial data that Environment provides. The `TtlWeatherEffects` configuration in Mapping (600s TTL) was designed for exactly this integration.

**Ethology (L4) provides behavioral defaults, not environmental state.** Ethology's environmental overrides modify creature behavior axes based on environmental CONDITIONS. But those conditions must come from somewhere. Without Environment, Ethology's overrides are manually registered and static. With Environment, Ethology can subscribe to `environment.conditions-changed` and automatically create/update behavioral overrides based on actual environmental state. Ethology answers "how does this creature behave HERE?" Environment provides the HERE.

| Service | Question It Answers | Domain |
|---------|--------------------|--------------------|
| **Worldstate** | "What time is it?" | Temporal (L2, clock + calendar) |
| **Location** | "Where is this place in the world hierarchy?" | Spatial structure (L2, tree + coordinates) |
| **Mapping** | "What spatial data exists at these coordinates?" | Spatial data (L4, 3D index) |
| **Ethology** | "How does this creature type behave?" | Behavioral nature (L4, archetype + noise) |
| **Environment** | "What are conditions like at this place right now?" | Ecological state (L4, weather + temperature + resources) |

The pipeline with Environment:
```
Worldstate (what time)  ────────────────────────────────────┐
Location (where)  ──────────────────────────────────────────┤
                                                             ▼
                                               ENVIRONMENT SERVICE
                                               Computes conditions from
                                               time + season + biome +
                                               altitude + weather events
                                                             │
              ┌────────────────┬──────────────┬──────────────┤
              ▼                ▼              ▼              ▼
         Actor ABML        Ethology       Workshop        Mapping
         ${environment.*}  Seasonal       Seasonal        weather_effects
         NPC decisions     behavior       production      spatial layer
                           overrides      modifiers       client rendering
```

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Weather is deterministic, not random**: Given the same realm, location, game-day, and season, the weather is always the same. This is essential for multi-node consistency, lazy evaluation reproducibility, and NPC decision consistency. If a game designer wants "surprising" weather, they should use weather events (divine storms, scheduled anomalies), not random rolls.

2. **Temperature units are abstract**: The service stores and computes float values that games interpret as their own temperature scale. Arcadia might treat them as Celsius-like. A fantasy game might use an entirely abstract "warmth" scale from -100 to +100. The service doesn't convert units or validate ranges against real-world physics.

3. **Weather inherits upward through location tree, not sideways**: A room in a building has the same weather as the building, which has the same weather as its parent district, which has the same weather as its parent city. But two sibling locations with different biome codes get independent weather. This models indoor/outdoor hierarchy correctly but doesn't model geographic proximity -- two adjacent cities at the same level of the location tree compute weather independently.

4. **Resource availability is a single float, not per-resource**: Environment provides one composite abundance value (0.0-1.0) per location. It doesn't track "iron availability" vs "food availability" separately. Consumers (Loot, Workshop, Market) interpret the abundance value in their own domain. If per-resource ecology is needed, it's a future extension or a separate service.

5. **Weather transition dampening is global, not per-pattern**: The `WeatherTransitionDampening` config applies equally to all weather patterns. A storm doesn't have more inertia than clear weather. This keeps the weather resolution algorithm simple and predictable. If per-pattern persistence is needed, extend the weather distribution model.

6. **Divine overrides are absolute**: A weather event with `weatherCode: "storm"` replaces the deterministic weather completely for the duration. There's no "divine influence" weighting where the override blends with natural weather. The god either controls the weather or doesn't. This prevents confusing states where the weather is "half storm, half clear."

7. **Condition cache is eventually consistent**: When weather transitions occur (new weather segment, weather event starts/ends), the condition cache is invalidated. But entities mid-behavior-tick use their cached condition snapshot until the next tick. At the default 60-second cache TTL, environmental changes propagate within ~1 minute. This is acceptable for weather changes (which are gradual) but means an instantaneous divine storm takes up to a minute to affect NPC behavior.

8. **No wind simulation**: Wind speed and direction are looked up from climate template baselines + weather modifiers. Wind doesn't flow, interact with terrain, or create downstream effects. It's a data value that behaviors reference (`${environment.wind.speed}` for "is it too windy to fire arrows?"), not a physics simulation.

9. **Altitude is from Location metadata, not Mapping**: Temperature altitude modifiers use the location's metadata altitude value, not the Mapping service's 3D coordinates. This is intentional -- Mapping is a soft dependency, and altitude for temperature purposes is a coarse location-level property, not a per-coordinate value. A mountaintop location has altitude 3000 regardless of where within that location an entity stands.

10. **Weather segment boundaries can cause brief inconsistencies**: When a weather segment expires and a new one is computed, entities querying conditions during the transition may briefly see different weather depending on whether their cached snapshot pre-dates the transition. The `ConditionCacheTtlSeconds` bounds this window. In practice, weather transitions are every 4-24 game-hours (10-60 real minutes), so this is rarely observable.

### Design Considerations (Requires Planning)

1. **Scale of condition computation for large worlds**: A realm with 10,000 locations needs condition snapshots for all of them. The background worker refreshes conditions on Worldstate period-changed events (~every 12 real minutes at 24:1 ratio). Processing 10,000 locations per refresh at 30 seconds interval is feasible with batch processing and caching, but the Mapping publishing step (pushing weather data to spatial index) may bottleneck. May need location-scoped refresh (only refresh locations with active entities) rather than realm-wide refresh.

2. **Climate template complexity vs. performance**: Rich climate templates with many weather patterns, complex temperature curves, and multiple seasonal baselines require non-trivial computation on every condition resolution. The `ClimateTemplateCache` mitigates MySQL queries, but the computation itself (seasonal interpolation, hourly curve evaluation, altitude adjustment) runs on every cache miss. Pre-computing daily condition tables per location (one computation per game-day per location) would amortize the cost.

3. **Weather event cleanup after divine actor death**: When a divine actor (via Puppetmaster) creates weather events and then the actor is stopped (realm deleted, god defeated), the weather events should be cleaned up. The `sourceType`/`sourceId` fields enable this -- the actor cleanup callback should cancel all events created by that actor. Need to ensure the cleanup path is wired through Puppetmaster's actor lifecycle.

4. **Integration ordering with Ethology**: Ethology's potential extension #1 (seasonal behavioral modifiers) subscribes to `environment.conditions-changed` to create automatic ethology overrides. This creates a publish-subscribe chain: Worldstate → Environment → Ethology → Actor. The latency through this chain should be bounded. At TTL defaults (60s environment cache, 60s ethology manifest cache), changes propagate within ~2 minutes.

5. **Offline/downtime weather catch-up**: When the service starts after downtime, Worldstate catches up game-time (advance policy). Environment should NOT recompute conditions for every missed weather segment during catch-up. Instead, compute current conditions from the current game-time snapshot (deterministic weather is reproducible from the current state) and publish a single `conditions-changed` event. Consumers handle catch-up via their own mechanisms (Workshop materializes missed production, Loot regenerates tables).

6. **Multi-realm climate template sharing**: Multiple realms can reference the same climate templates (both use `temperate_forest`). This is fine for read operations but means updating a template affects all realms using it. Climate template updates should be admin-only and infrequent. Per-realm template overrides would add complexity; prefer creating separate templates for realms that need different climate parameters.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Worldstate (L2) is the hard prerequisite -- Environment cannot compute anything without game time and season data. Phase 1 (climate template infrastructure) is self-contained. Phase 2 (deterministic weather) is self-contained once Phase 1 is done. Phase 5 (variable provider) depends on Phase 2-3 and can be integration-tested with Actor runtime. Phase 6 (integrations) connects to Ethology, Workshop, Mapping, and other consumers.*
