# Environment Plugin Deep Dive

> **Plugin**: lib-environment
> **Schema**: schemas/environment-api.yaml
> **Version**: 1.0.0
> **State Stores**: environment-climate (MySQL), environment-weather (Redis), environment-conditions (Redis), environment-overrides (MySQL)
> **Status**: Pre-implementation (architectural specification)

---

## Overview

Environmental state service (L4 GameFeatures) providing weather simulation, temperature modeling, atmospheric conditions, and ecological resource availability for game worlds. Consumes temporal data from Worldstate (L2) -- season, time of day, calendar boundaries -- and translates it into environmental conditions that affect NPC behavior, production, trade, loot generation, and player experience. The missing ecological layer between Worldstate's clock and the behavioral systems that already reference environmental data that doesn't exist. Game-agnostic: biome types, weather distributions, temperature curves, and resource availability are configured through climate template seeding at deployment time -- a space game could model atmospheric composition, a survival game could model wind chill, the service stores float values against string-coded condition axes. Internal-only, never internet-facing.

---

## Core Concepts

The codebase references environmental data everywhere with no provider. ABML behaviors check `${world.weather.temperature}` -- phantom variable, no provider. Mapping has a `TtlWeatherEffects` spatial layer (600s TTL) ready to receive weather data that nobody publishes. Ethology's environmental overrides were designed to compose with ecological conditions that don't exist. Loot tables reference seasonal resource availability with no authority to consult. Trade routes reference environmental conditions for route activation. Market's economy planning documents reference seasonal trade and harvest cycles. Workshop's production modifiers reference seasonal rates. All of these point at a service that was never built.

This service provides six capabilities:
1. A **weather simulation** -- per-realm, per-location weather computed from climate templates, season, time of day, and deterministic noise
2. A **temperature model** -- base temperature from season + time-of-day curves + altitude/biome modifiers + weather modifiers
3. An **atmospheric condition provider** -- precipitation type/intensity, wind speed/direction, humidity, visibility, cloud cover
4. A **resource availability authority** -- seasonal ecological abundance per biome, affected by weather and divine intervention
5. A **variable provider** -- `${environment.*}` namespace for ABML behavior expressions
6. A **weather event system** -- time-bounded environmental phenomena (storms, droughts, blizzards, heat waves) registered by divine actors or scheduled from climate templates

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
      hourlyShape:   TemperatureCurveShape  # Sinusoidal (default), Plateau, Spike -- curve shape for intra-day variation. Enum defined in environment-api.yaml.
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
          precipitationType: PrecipitationType  # None, Rain, Snow, Sleet, Hail. Enum defined in environment-api.yaml. Determines precipitation form. For temperature-sensitive types (Rain/Snow), the resolver applies a crossover rule: Rain below 0°C becomes Snow, Snow above 5°C becomes Rain, between 0-5°C becomes Sleet.
          cloudCover: float              # 0.0-1.0. Cloud coverage during this weather pattern. clear=0.1, cloudy=0.6, overcast=0.95, storm=1.0, etc.
          resourceImpactModifier: float  # Additive impact on resource availability. rain=+0.05, storm=-0.15, drought=-0.3, clear=0.0. See Resource Availability Computation.

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

4. **Altitude/depth modifiers are continuous**: Rather than discrete altitude bands, temperature changes linearly with altitude at a configurable rate. A mountain peak at altitude 3000 is `3000 * -0.006 = -18` degrees colder than sea level. A deep cavern at depth 500 is `500 * +0.003 = +1.5` degrees warmer. Altitude is stored in Environment's own location-climate binding (captured at binding creation time); Environment applies the modifier.

### Location Climate Binding

Each location in the game world is bound to a climate template via Environment's own binding records. Bindings are **owned by Environment** in Environment's own state store — NOT stored in Location's metadata (per FOUNDATION TENETS: no metadata bag contracts between services).

```
LocationClimateBinding:
  bindingId:         Guid
  locationId:        Guid
  realmId:           Guid
  gameServiceId:     Guid
  biomeCode:         string              # Environment's own domain data
  climateTemplateId: Guid                # Resolved from biomeCode + gameServiceId
  altitude:          float               # Stored by Environment at binding creation (queried from Location)
  depth:             float               # For underground locations (0 for surface)
  isIndoor:          bool                # True for enclosed locations (buildings, caves, taverns). Indoor locations skip weather computation entirely: no weather, no wind, full visibility. Temperature uses biome base modified by depth only. Default: false.
  isInherited:       bool                # True if inherited from parent binding
  createdAt:         DateTime
  updatedAt:         DateTime
```

**Binding management**: Environment provides its own API endpoints for managing location-climate bindings:
- `/environment/climate-binding/create` — Creates a binding for a location. Validates location exists via `ILocationClient`, validates biomeCode matches an existing climate template.
- `/environment/climate-binding/get` — Returns binding for a locationId.
- `/environment/climate-binding/update` — Changes the biomeCode or altitude for a location.
- `/environment/climate-binding/delete` — Removes a binding.
- `/environment/climate-binding/bulk-seed` — Bulk binding creation for world initialization, called alongside climate template seeding in Phase 1.

**Resolution hierarchy** (all within Environment's own data):
1. Location has an explicit binding in Environment → use it
2. Location's parent has a binding in Environment → inherit (walk up the location tree via `ILocationClient`)
3. No binding found in hierarchy → use realm's default biome from `RealmEnvironmentConfig` (Environment's own per-realm config, managed via `/environment/realm-config/set`)
4. No realm default → use the global `DefaultBiomeCode` configuration fallback

**RealmEnvironmentConfig** (Environment-owned per-realm configuration):
```
RealmEnvironmentConfig:
  realmId:            Guid
  gameServiceId:      Guid
  defaultBiomeCode:   string              # Fallback biome for locations with no binding in hierarchy
  createdAt:          DateTime
  updatedAt:          DateTime
```
Managed via `/environment/realm-config/set` (developer role). Seeded alongside climate templates during world initialization. Different realms need different defaults (Arcadia=temperate, Fantasia=tropical). Stored in `environment-climate` (MySQL) alongside climate templates.

**Why Environment owns this data**: Biome codes are an ecological concept. They belong in the service that defines ecology (Environment, L4). Storing them in Location (L2) metadata would mean: non-game deployments carry biome data for no reason, Location's storage/migration must preserve data it doesn't understand, no referential integrity between biome codes and climate templates, no lifecycle management when biome codes change, and invisible cross-service coupling that no tooling can detect. See FOUNDATION TENETS (no metadata bag contracts) for the full rationale.

This means a city location inherits the biome of its parent region (via Environment's parent-walking resolution), but a specific cave system within the city can have its own binding to `underground`. New locations can be auto-bound via the `location.created` event handler.

### Temperature Computation

Temperature is computed, not stored. Given a location and a game-time snapshot, the temperature is:

```
ComputeTemperature(location, binding, gameTimeSnapshot):
  # 0. Indoor short-circuit: enclosed locations skip weather entirely
  if binding.isIndoor:
    template = resolveClimateTemplate(location)
    currentCurve = template.temperatureCurves[gameTimeSnapshot.season]
    baseTemp = (currentCurve.baseMinTemp + currentCurve.baseMaxTemp) / 2
    baseTemp += binding.depth * template.depthTemperatureRate  # caves warm with depth
    return baseTemp

  template = resolveClimateTemplate(location)

  # 1. Get seasonal base temperature
  currentSeason = gameTimeSnapshot.season
  nextSeason = getNextSeason(gameTimeSnapshot, template)
  seasonProgress = gameTimeSnapshot.seasonProgress  # 0.0-1.0 from Worldstate's GameTimeSnapshot

  currentCurve = template.temperatureCurves[currentSeason]
  nextCurve = template.temperatureCurves[nextSeason]

  # Interpolate between current and next season for smooth transitions
  baseMin = lerp(currentCurve.baseMinTemp, nextCurve.baseMinTemp, seasonProgress * 0.5)
  baseMax = lerp(currentCurve.baseMaxTemp, nextCurve.baseMaxTemp, seasonProgress * 0.5)

  # 2. Apply hourly curve (sinusoidal by default)
  gameHour = gameTimeSnapshot.hour
  hourFraction = sinusoidalInterpolation(gameHour, currentCurve.peakHour, currentCurve.troughHour)
  baseTemp = lerp(baseMin, baseMax, hourFraction)

  # 3. Apply altitude/depth modifier (from Environment's LocationClimateBinding)
  baseTemp += binding.altitude * template.altitudeTemperatureRate
  baseTemp += binding.depth * template.depthTemperatureRate

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

Random weather would produce different conditions on different nodes in a multi-instance deployment, break lazy evaluation (weather between two timestamps must be reproducible), and make NPC weather-aware decisions inconsistent. Deterministic weather means a farmer NPC deciding whether to plant today gets the same answer regardless of which server node processes the request.

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
  scopeType:         WeatherEventScopeType  # Realm, Location, LocationSubtree. Enum defined in environment-api.yaml.
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

  # Timing (always game-time, sourced from Worldstate)
  startGameTime:     long?               # Game-seconds-since-epoch when event begins. null = starts immediately (handler fills from Worldstate's current game time per IMPLEMENTATION TENETS: no sentinel values).
  endGameTime:       long?               # null = indefinite (must be ended explicitly)

  # Source tracking
  sourceType:        WeatherEventSourceType  # Divine, Scheduled, GameEvent, Admin. Enum defined in environment-api.yaml.
  sourceId:          Guid?               # Actor ID, schedule ID, etc.

  isActive:          bool
  createdAt:         DateTime
```

**Event stacking**: Multiple events can affect the same location. They stack additively for modifiers. For hard overrides (weatherCode, temperatureOverride), **scope specificity wins**: `Location` overrides `LocationSubtree` overrides `Realm`. Within the same scope level, the most recently created event wins. A drought (resource -0.5) and a divine blessing (resource +0.3) stack to net -0.2. A location-specific divine storm's weatherCode override takes precedence over a realm-wide drought's weatherCode override at that location, regardless of creation order. A hard temperature override from a blizzard event takes precedence over seasonal temperature.

**Event scope types** (`WeatherEventScopeType` enum):
- `Realm`: Affects all locations in the realm (realm-wide drought, volcanic winter)
- `Location`: Affects a single location only (localized storm at a mountain peak)
- `LocationSubtree`: Affects a location and all its descendants (forest fire in a region and all contained areas)

### Resource Availability Computation

Resource availability is a **single composite float** (0.0-1.0) per location representing ecological abundance. It is NOT per-resource-type -- Environment provides one aggregate value, and consumers (Loot, Workshop, Market) interpret it in their own domain. Resource types (food, timber, ore, etc.) are consumer-side concepts; Environment's abundance is biome-level ecological health.

```
ComputeResourceAvailability(location, gameTimeSnapshot):
  template = resolveClimateTemplate(location)

  # 1. Seasonal baseline from climate template
  seasonalAvail = template.resourceAvailability[gameTimeSnapshot.season]
  baseAbundance = seasonalAvail.abundanceLevel  # e.g., spring=0.8, winter=0.3

  # 2. Season transition smoothing (same approach as temperature)
  nextSeasonAvail = template.resourceAvailability[getNextSeason(gameTimeSnapshot, template)]
  progress = gameTimeSnapshot.seasonProgress
  smoothedAbundance = lerp(baseAbundance, nextSeasonAvail.abundanceLevel, progress * 0.5)

  # 3. Weather impact modifier (from climate template's weather distribution)
  currentWeather = resolveWeather(location, gameTimeSnapshot)
  # Each weather pattern defines its own resourceImpactModifier in the climate template
  # e.g., "storm" = -0.15, "drought" = -0.3, "clear" = 0.0, "rain" = +0.05
  smoothedAbundance += currentWeather.resourceImpactModifier

  # 4. Weather event modifiers (stacked additively)
  activeEvents = getActiveWeatherEvents(location, gameTimeSnapshot)
  for event in activeEvents:
    if event.resourceAvailabilityMultiplier != null:
      smoothedAbundance *= event.resourceAvailabilityMultiplier

  # 5. Clamp to [0.0, 1.0]
  return clamp(smoothedAbundance, 0.0, 1.0)
```

**Weather-to-resource impact mapping**: The impact of weather on resource availability is defined per-climate-template as part of each weather distribution pattern's `resourceImpactModifier` field. Each weather pattern declares its own ecological impact: rain is generally positive (+0.05, crops grow), storms are negative (-0.15, damage to environment), drought is strongly negative (-0.3, ecological stress), clear is neutral (0.0). These are NOT hardcoded weather-code-to-impact lookups -- each climate template's weather patterns define their own `resourceImpactModifier` alongside `temperatureModifier` and other weather-specific fields, allowing the same weather code to have different ecological impacts in different biomes.

**Persistence model**: Resource availability is NOT independently stored. It is computed as part of the condition snapshot and cached in `environment:conditions:snapshot:{locationId}` alongside temperature, weather, and atmospheric data. The `environment.resource-availability.changed` event is published when the computed availability crosses a configurable significance threshold (default: 0.1 change) during a refresh cycle, preventing event noise from minor fluctuations.

**Consumer interpretation examples**:
- **Loot** (L4): `forageDropRate = baseRate * environment.resource_availability * climate.forageModifier`
- **Workshop** (L4): `farmingProductionRate = blueprintRate * environment.resource_availability`
- **Market** (L4): `vendorFoodPriceMultiplier = 1.0 + (1.0 - environment.resource_availability) * scarcityPriceFactor`

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Climate templates (MySQL), weather event overrides (MySQL), condition cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for climate template mutations, weather event creation/cancellation |
| lib-messaging (`IMessageBus`) | Publishing environment events (conditions-changed, weather-event-started, etc.), error event publication |
| lib-worldstate (`IWorldstateClient`) | Querying current game time, season, season progress for a realm. The foundational dependency -- Environment cannot compute anything without knowing what time it is. (L2) |
| lib-location (`ILocationClient`) | Resolving location parent hierarchy for climate binding inheritance, and querying location existence for binding validation. (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scope validation for climate templates. (L2) |
| lib-realm (`IRealmClient`) | Querying realm default biome code for the binding resolution fallback (step 3: no binding in location hierarchy → use realm default). Required because realms are L2 — guaranteed available when Environment (L4) runs. (L2) |
| lib-resource (`IResourceClient`) | Reference registration/unregistration for realm, game-service, and location targets (L1). Called during climate template creation, binding creation, and cleanup operations. |
| lib-character (`ICharacterClient`) | Character-to-location resolution in the `EnvironmentProviderFactory` variable provider. Actors pass entityId (characterId) to the factory; the factory needs the character's locationId and realmId to query the correct condition snapshot. Cached per-character with short TTL. Only used as fallback when Actor runtime context doesn't include locationId. (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-mapping (`IMappingClient`) | Publishing weather data to Mapping's `weather_effects` spatial layer for spatial weather queries. Resolved via `GetService<IMappingClient>()` + null check. See [Mapping Integration](#mapping-integration) below for the full API pattern, data model, and publication trigger. (L4) | Weather data is not published to the spatial index. Spatial weather queries return nothing. Environmental conditions still resolve correctly for direct API queries and variable provider reads. |
| lib-analytics (`IAnalyticsClient`) | Publishing environmental statistics (weather distribution accuracy, override frequency, resource availability trends). (L4) | No analytics. Silent skip. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `EnvironmentProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates provider instances per entity for ABML behavior execution (`${environment.*}` variables). NPCs make weather-aware decisions: stay indoors during storms, wear warm clothes in cold, migrate during droughts. |
| lib-ethology (L4) | Ethology's seasonal behavior modifier extension consumes `environment.conditions-changed` events to apply automatic environmental overrides to creature archetypes. Wolves are more aggressive in harsh winter; deer are more vigilant during storms. **Note**: This subscription is an Ethology-side concern -- Ethology must declare `x-event-subscriptions` for `environment.conditions-changed` in its own events schema. Environment just publishes the event; it has no awareness of Ethology as a consumer. |
| lib-workshop (L4) | Workshop's seasonal production modifier extension queries current resource availability to adjust production rates. Farming blueprints produce at reduced rate during drought, increased rate during abundant seasons. |
| lib-loot (L4, planned) | Loot table seasonal modifiers query current season + resource availability from Environment to adjust drop rates. Forageable items are scarcer in winter, abundant in autumn harvest. |
| lib-market (L4, planned) | Market price modifiers adjust NPC vendor pricing based on resource availability. Scarcity drives prices up; abundance drives them down. |
| lib-mapping (L4) | Mapping receives weather data published to its `weather_effects` spatial layer, enabling spatial weather queries for game clients and NPC perception. |
| lib-puppetmaster (L4) | Regional watcher behaviors (storm gods, nature deities) create and manage weather events through ABML actions (`register_weather_event`, `cancel_weather_event`, `extend_weather_event`). **Environment provides the ABML action handlers** (implementing `IActionHandler` in `bannou-service/Abml/Execution/`) that Actor's `IActionHandlerRegistry` discovers and invokes during behavior execution. Puppetmaster launches the divine actors; Environment provides the weather manipulation actions those actors can use. |
| lib-storyline (L4) | Storyline's scenario preconditions can reference environmental state (drought conditions trigger famine storylines, volcanic winter enables survival narratives). |
| lib-transit (L2) | Transit discovers `EnvironmentTransitCostModifierProvider` via `IEnumerable<ITransitCostModifierProvider>` DI injection. Environment implements the `ITransitCostModifierProvider` interface (defined in `bannou-service/Providers/`) to provide weather-based travel cost modifiers: speed reduction during storms, risk increase during blizzards, impassability during extreme weather events. Transit (L2) cannot depend on Environment (L4) events — the DI provider pattern is the correct upward data flow mechanism, same as Variable Provider Factory. Transit is always enabled on game nodes. |
| lib-utility (L4, planned) | Utility scoring for NPC decision-making incorporates environmental conditions as utility modifiers. Weather-aware NPCs weigh outdoor actions lower during storms, value shelter higher in cold, and prefer travel in clear weather. |
| lib-craft (L4, planned) | Certain crafting recipes could reference environmental conditions (outdoor forge requires non-rain weather, ice sculpting requires freezing temperature). |
| lib-disposition (L4, planned) | Environmental conditions could feed into NPC mood as a modifier source. Persistent rain lowers mood; warm sunny days improve it. |
| lib-character-lifecycle (L4, planned) | Character lifecycle events (aging, health, death risk) could incorporate environmental stress. Prolonged exposure to extreme cold/heat, drought-related famine risk, storm injury chance. Environment provides the ecological pressure; lifecycle determines consequences. |

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

### Distributed Locks (via `IDistributedLockProvider`)

Distributed locks use `IDistributedLockProvider` (L0 infrastructure), not a separate state store. Lock keys are documented here for reference:

| Lock Resource ID | Purpose |
|-----------------|---------|
| `environment:climate:{gameServiceId}:{biomeCode}` | Climate template mutations |
| `environment:event:{scopeType}:{scopeId}` | Weather event creation/cancellation for a scope |
| `environment:refresh:{realmId}` | Per-realm condition refresh cycle (one worker instance per realm) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `environment.conditions-changed` | `EnvironmentConditionsChangedEvent` | Weather pattern changed for a location (new weather segment, weather event started/ended). Includes realmId, locationId, previousWeather, currentWeather, temperature, full condition snapshot. Published at weather segment boundaries, not continuously. |
| `environment.weather-event.started` | `EnvironmentWeatherEventStartedEvent` | Weather event activated. Includes eventId, eventCode, severity, scopeType, scopeId, sourceType, duration. |
| `environment.weather-event.ended` | `EnvironmentWeatherEventEndedEvent` | Weather event ended (expired or cancelled). Includes eventId, eventCode, scopeType, scopeId, reason ("expired", "cancelled", "superseded"). |
| `environment.resource-availability.changed` | `EnvironmentResourceAvailabilityChangedEvent` | Seasonal resource availability changed for a realm (on season boundary or weather event). Includes realmId, previousAbundance, currentAbundance, season. |
| `environment-climate.created` | `EnvironmentClimateCreatedEvent` | Climate template created. Auto-generated via `x-lifecycle` in `environment-events.yaml`. |
| `environment-climate.updated` | `EnvironmentClimateUpdatedEvent` | Climate template updated. Auto-generated via `x-lifecycle` with `changedFields`. |
| `environment-climate.deleted` | `EnvironmentClimateDeletedEvent` | Climate template deleted (via cleanup). Auto-generated via `x-lifecycle` with `deletedReason`. |
| `environment-climate.deprecated` | `EnvironmentClimateDeprecatedEvent` | Climate template deprecated. Manually-defined custom event (not part of standard lifecycle). |

**`x-event-publications`**: ALL published events (condition changes, weather events, resource availability, and lifecycle) must be listed in `x-event-publications` in `environment-events.yaml` per SCHEMA-RULES, serving as the authoritative registry of what environment emits.

### Consumed Events

These MUST be declared as `x-event-subscriptions` in `environment-events.yaml` so the event controller is generated. Without schema-level subscription declarations, the handlers won't be wired up.

| Topic | Handler | Event Model Fields | Action |
|-------|---------|-------------------|--------|
| `worldstate.period-changed` | `HandlePeriodChangedAsync` | `realmId`, `gameServiceId`, `period` (current period code), `previousPeriod`, `gameHour`, `gameDay`, `season`, `seasonProgress` | Primary trigger for environmental state updates. Acquires realm-scoped distributed lock (`refresh:{realmId}`), then re-computes condition snapshots for locations within the affected realm (scoped by `ConditionRefreshMode` -- all locations or active-only). Publishes `conditions-changed` events where weather or temperature changed meaningfully vs. the previous cached snapshot. Publishes to Mapping weather layer if enabled. |
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | `realmId`, `gameServiceId`, `season` (new season code), `previousSeason`, `year` | Trigger resource availability recalculation for all locations in the affected realm. Publish `resource-availability.changed`. Invalidate cached weather distributions (new season = new weather probability weights from climate template). Invalidate `ClimateTemplateCache` seasonal lookups. |
| `worldstate.day-changed` | `HandleDayChangedAsync` | `realmId`, `gameServiceId`, `gameDay` (new day number), `year` | Invalidate day-keyed weather cache entries for the realm (`environment:weather:resolved:{realmId}:*:{previousGameDay}`). Lightweight -- cache invalidation only, no condition recomputation. New weather rolls will be computed lazily on next query or proactively on next worker refresh cycle. |
| `location.created` | `HandleLocationCreatedAsync` | `locationId`, `realmId`, `parentLocationId` | Check if parent location has a climate binding in Environment's store. If so, create an inherited binding for the new location. Does NOT read Location metadata — biome data is Environment's own domain. Initial condition snapshot is created if the new location resolves a climate template through the inherited binding. |
| `location.updated` | `HandleLocationUpdatedAsync` | `locationId`, `realmId`, `parentLocationId`, `previousParentLocationId` | If `parentLocationId` changed (location reparented), re-evaluate inherited climate bindings: if the binding was inherited from the old parent's subtree, re-resolve from the new parent's subtree. Explicit (non-inherited) bindings are unaffected. Location reparenting is rare but must not leave stale inherited bindings. |

**Note**: Environment does NOT subscribe to `location.deleted` for cleanup. Per FOUNDATION TENETS (resource-managed cleanup), location cleanup is handled exclusively via lib-resource's `/environment/cleanup-by-location` callback (see Resource Cleanup below). The cleanup endpoint handles weather events, cached conditions, and active location tracking set removal.

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
| `ConditionCacheTtlSeconds` | `ENVIRONMENT_CONDITION_CACHE_TTL_SECONDS` | `60` | TTL for cached condition snapshots in Redis. Background worker writes here; in-memory cache reads from here on miss. (range: 10-300) |
| `ConditionSnapshotCacheTtlSeconds` | `ENVIRONMENT_CONDITION_SNAPSHOT_CACHE_TTL_SECONDS` | `5` | TTL for in-memory condition snapshot cache (`IConditionSnapshotCache`). Variable providers read from this cache first, falling back to Redis, then computation. Short TTL (5-10s) balances freshness against Redis load at NPC scale. (range: 1-30) |
| `WeatherCacheTtlSeconds` | `ENVIRONMENT_WEATHER_CACHE_TTL_SECONDS` | `600` | TTL for cached resolved weather per location per game-day in Redis. (range: 60-3600) |
| `ClimateCacheTtlMinutes` | `ENVIRONMENT_CLIMATE_CACHE_TTL_MINUTES` | `60` | TTL for in-memory climate template cache. Templates change rarely. (range: 5-1440) |
| `ConditionRefreshMode` | `ENVIRONMENT_CONDITION_REFRESH_MODE` | `AllLocations` | Which locations the background worker refreshes per realm cycle. `$ref` to `ConditionRefreshMode` enum from `environment-api.yaml`. `AllLocations`: every location in the realm is refreshed (safe default, ensures all condition caches are warm). `ActiveOnly`: only locations with active entities (actors, game sessions, or queries within `ActiveLocationWindowMinutes`) are refreshed -- dormant locations compute conditions lazily on first query. See Design Consideration #1 for trade-offs. |
| `ActiveLocationWindowMinutes` | `ENVIRONMENT_ACTIVE_LOCATION_WINDOW_MINUTES` | `30` | When `ConditionRefreshMode` is `ActiveOnly`, locations are considered active if they had an entity or query within this window. (range: 5-1440) |
| `ConditionUpdateIntervalSeconds` | `ENVIRONMENT_CONDITION_UPDATE_INTERVAL_SECONDS` | `30` | Real-time seconds between background condition refresh cycles per realm. (range: 5-300) |
| `ConditionUpdateStartupDelaySeconds` | `ENVIRONMENT_CONDITION_UPDATE_STARTUP_DELAY_SECONDS` | `15` | Seconds to wait after startup before first condition refresh, allowing Worldstate to initialize. (range: 0-120) |
| `MaxClimateTemplatesPerGameService` | `ENVIRONMENT_MAX_CLIMATE_TEMPLATES_PER_GAME_SERVICE` | `50` | Safety limit on climate templates per game service. (range: 5-200) |
| `MaxActiveEventsPerScope` | `ENVIRONMENT_MAX_ACTIVE_EVENTS_PER_SCOPE` | `10` | Maximum concurrent weather events per realm or location scope. (range: 1-50) |
| `DefaultBiomeCode` | `ENVIRONMENT_DEFAULT_BIOME_CODE` | `temperate` | Fallback biome code when location has no climate binding and no parent with a binding. |
| `ResourceAvailabilityChangeThreshold` | `ENVIRONMENT_RESOURCE_AVAILABILITY_CHANGE_THRESHOLD` | `0.1` | Minimum change in computed resource availability to trigger a `resource-availability.changed` event. Prevents event noise from minor fluctuations during weather transitions. (range: 0.01-0.5) |
| `TemperatureNoiseAmplitude` | `ENVIRONMENT_TEMPERATURE_NOISE_AMPLITUDE` | `1.5` | Maximum per-location deterministic temperature noise in degrees. (range: 0.0-5.0) |
| `WeatherTransitionDampening` | `ENVIRONMENT_WEATHER_TRANSITION_DAMPENING` | `0.3` | Deterministic dampening factor for weather persistence. The segment hash is compared against this threshold: if the hash's fractional value falls below the dampening factor, the previous segment's weather repeats. 0.0 = no dampening (pure distribution), 1.0 = weather never changes. This is NOT probabilistic randomness — the hash output is deterministic given the same inputs, so dampening produces the same result across all nodes. (range: 0.0-0.9) |
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
| `IStateStoreFactory` | State store access (creates 4 stores: environment-climate, environment-weather, environment-conditions, environment-overrides) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IWorldstateClient` | Game time queries: current time, season, season progress (L2 hard) |
| `ILocationClient` | Location parent hierarchy for binding inheritance, existence validation (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IRealmClient` | Realm default biome code for binding fallback (L2 hard) |
| `IResourceClient` | Reference registration/unregistration with lib-resource (L1 hard). Called during climate template creation, binding creation, and cleanup. |
| `ICharacterClient` | Character-to-location resolution for variable provider (L2 hard). Used by `EnvironmentProviderFactory` to resolve entityId → locationId/realmId when the Actor runtime does not provide locationId in context. |
| `IServiceProvider` | Runtime resolution of soft dependencies (lib-mapping, lib-analytics) |
| `EnvironmentConditionWorkerService` | Background `HostedService` that refreshes environmental conditions **per-realm** on a periodic cycle. Acquires a distributed lock per realm (`refresh:{realmId}`), iterates locations within that realm, computes condition snapshots, and publishes `conditions-changed` events where weather or temperature changed meaningfully. Each realm's refresh is independently locked for multi-instance safety -- two nodes can refresh different realms concurrently, but only one node processes a given realm at a time. Supports configurable `ConditionRefreshMode` (see Configuration): `AllLocations` (default, refresh every location in the realm) or `ActiveOnly` (refresh only locations with active entities -- actors, game sessions, or recent queries -- skipping dormant locations). **Optimization**: The worker should fetch the `GameTimeSnapshot` from Worldstate once per realm per refresh cycle, then pass it to all location condition computations within that cycle. Worldstate time doesn't change between location iterations within a single refresh — fetching per-location would be wasteful network traffic for identical results. |
| `EnvironmentEventExpirationWorkerService` | Background `HostedService` that scans for expired weather events and deactivates them. Publishes `weather-event.ended` events. |
| `EnvironmentProviderFactory` | Implements `IVariableProviderFactory` to provide `${environment.*}` variables to Actor's behavior system. |
| `EnvironmentTransitCostModifierProvider` | Implements `ITransitCostModifierProvider` (defined in `bannou-service/Providers/`) to provide weather-based travel cost modifiers to Transit (L2). Returns speed multiplier (storms slow travel), risk modifier (blizzards increase danger), and impassability flag (extreme events block routes). Transit discovers via `IEnumerable<ITransitCostModifierProvider>`. |
| `RegisterWeatherEventActionHandler` | ABML action handler (`register_weather_event`) enabling divine actors to create weather events via behavior expressions. Implements `IActionHandler` (in `bannou-service/Abml/Execution/`) and is discovered by Actor's `IActionHandlerRegistry`. Calls Environment's `CreateWeatherEvent` API internally. Used by Puppetmaster-launched divine actors. |
| `CancelWeatherEventActionHandler` | ABML action handler (`cancel_weather_event`) implementing `IActionHandler`. Enables divine actors to cancel weather events they created. Validates `sourceId` matches the calling actor. |
| `ExtendWeatherEventActionHandler` | ABML action handler (`extend_weather_event`) implementing `IActionHandler`. Enables divine actors to extend weather event durations. |
| `IWeatherResolver` / `WeatherResolver` | Internal helper for deterministic weather computation from climate templates, season, and location. Applies indoor short-circuit for `isIndoor` bindings. |
| `ITemperatureCalculator` / `TemperatureCalculator` | Internal helper for temperature computation with seasonal curves, altitude, weather modifiers, and noise. Applies indoor short-circuit for `isIndoor` bindings. |
| `IClimateTemplateCache` / `ClimateTemplateCache` | In-memory TTL cache for climate templates. Templates change rarely; caching avoids MySQL queries on every condition computation. |
| `IConditionSnapshotCache` / `ConditionSnapshotCache` | In-memory TTL cache (5-10s) for condition snapshots, layered on top of the Redis condition cache. Backed by `ConcurrentDictionary<(Guid realmId, Guid locationId), ConditionSnapshotModel>` with `ConditionSnapshotCacheTtlSeconds` expiry. Critical for NPC scale: at 100,000+ actors ticking every 100-500ms, the variable provider would otherwise hit Redis 200K-1M times/second. This cache absorbs that load — actors at the same location within the same TTL window share the same in-memory snapshot. Follows the same pattern as Worldstate's `IRealmClockCache`. The background worker writes to both Redis and this in-memory cache on each refresh cycle. |

### Variable Provider Factory

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `EnvironmentProviderFactory` | `${environment.*}` | Reads from condition snapshot cache (Redis). Resolves character's locationId and realmId via `ICharacterClient` or actor metadata. Provides computed temperature, weather, atmospheric conditions, and resource availability. | `IVariableProviderFactory` (DI singleton) |

### Mapping Integration

Environment publishes weather condition data to Mapping's `weather_effects` spatial layer (600s TTL) so that game clients and NPC perception can query environmental conditions spatially rather than by location ID.

**Soft dependency pattern**:
```csharp
// In EnvironmentConditionWorkerService, after computing a batch of condition snapshots:
var mappingClient = _serviceProvider.GetService<IMappingClient>();
if (mappingClient == null)
{
    _logger.LogDebug("Mapping not enabled, skipping weather layer publication");
    return; // Graceful degradation -- spatial queries unavailable, direct queries still work
}
```

**Publication trigger**: The `EnvironmentConditionWorkerService` publishes to Mapping at the end of each realm's refresh cycle, batched by `MappingPublishBatchSize`. Only locations whose conditions actually changed since the last publication are included (compare current snapshot hash to previous).

**Data model published per location**:
```
WeatherEffectSpatialData:
  locationId:              Guid
  realmId:                 Guid
  weatherCode:             string     # Current weather pattern code
  temperature:             float      # Computed temperature
  precipitationType:       PrecipitationType  # None, Rain, Snow, Sleet, Hail
  precipitationIntensity:  float      # 0.0-1.0
  visibility:              float      # 0.0-1.0
  windSpeed:               float      # 0.0-1.0 normalized
  windDirection:           float      # 0.0-360.0 degrees
  resourceAvailability:    float      # 0.0-1.0
  hasActiveWeatherEvent:   bool       # Whether a divine/scheduled event is active
  activeEventCode:         string?    # Event code if applicable
  ttlSeconds:              int        # Matches ConditionCacheTtlSeconds * 2 (buffer for refresh cycles)
```

**API call**: Uses Mapping's spatial ingest endpoint (`/mapping/ingest/batch`) with channel `weather_effects` and the location's spatial coordinates queried from Location via `ILocationClient`. The `TtlWeatherEffects` Mapping configuration (600s TTL) auto-expires stale weather data if Environment stops publishing (e.g., during downtime or when `MappingPublishEnabled` is disabled).

---

## API Endpoints (Implementation Notes)

### Condition Queries (4 endpoints) — `x-permissions: [{ role: user }]`

- **GetConditions** (`/environment/conditions/get`): Returns the full environmental condition snapshot for a location: temperature, weather code, precipitation type/intensity, wind speed/direction, humidity, visibility, cloud cover, resource availability. Reads from condition cache (hot path). If cache is empty, computes from climate template + Worldstate + weather resolution.

- **GetConditionsByCode** (`/environment/conditions/get-by-code`): Convenience endpoint accepting realm code + location code instead of GUIDs.

- **BatchGetConditions** (`/environment/conditions/batch-get`): Returns condition snapshots for multiple locations in a single call. Optimized to share climate template and Worldstate lookups for locations in the same realm.

- **GetTemperature** (`/environment/conditions/get-temperature`): Lightweight endpoint returning only temperature for a location. Cheaper than full condition computation. Used by services that only need temperature (e.g., crafting weather checks).

### Climate Template Management (6 endpoints) — `x-permissions: [{ role: developer }]`

- **SeedClimate** (`/environment/climate/seed`): Creates a climate template for a biome within a game service. Validates: game service exists, biome code unique within game scope, temperature curves cover all seasons in the game's calendar, weather distribution weights are positive, seasonal baselines reference valid season codes. Enforces `MaxClimateTemplatesPerGameService`.

- **GetClimate** (`/environment/climate/get`): Returns full climate template by ID or by gameServiceId + biomeCode.

- **ListClimates** (`/environment/climate/list`): Paged list of climate templates within a game service.

- **UpdateClimate** (`/environment/climate/update`): Acquires distributed lock. Partial update. Validates structural consistency. Invalidates climate cache and all condition caches for locations using this template. Active weather computations use updated template starting next refresh cycle.

- **DeprecateClimate** (`/environment/climate/deprecate`): Marks deprecated with reason. Existing locations continue resolving. New location bindings with this biome code emit a warning.

- **BulkSeedClimates** (`/environment/climate/bulk-seed`): Bulk creation for world initialization. Idempotent with `updateExisting` flag. Accepts an array of climate template definitions. This is the primary path from "service deployed" to "climates exist" -- called during world initialization alongside Location's bulk seeding. Follows Location's two-pass seeding pattern: first pass creates templates, second pass resolves any cross-references (templates referencing other templates' season codes). Designed for configuration-driven initialization where climate data is defined in YAML seed files and loaded via admin API at deployment time, the same pattern as Species and Location bulk seeding.

### Weather Event Management (6 endpoints) — `x-permissions: [{ role: developer }]`

- **CreateWeatherEvent** (`/environment/weather-event/create`): Creates a time-bounded weather event. Validates: scope exists (realm or location), event code is non-empty, start time is in the future or "now", end time (if set) is after start time. Enforces `MaxActiveEventsPerScope`. Invalidates condition cache for affected scope. Publishes `weather-event.started`. Returns eventId.

- **GetWeatherEvent** (`/environment/weather-event/get`): Returns event by ID.

- **ListWeatherEvents** (`/environment/weather-event/list`): Lists events for a scope. Filterable by active/inactive, event code, source type.

- **CancelWeatherEvent** (`/environment/weather-event/cancel`): Cancels an active event. Invalidates condition cache. Publishes `weather-event.ended` with reason "cancelled".

- **ExtendWeatherEvent** (`/environment/weather-event/extend`): Extends an active event's end time. Cannot shorten (use cancel + create). Useful for divine actors prolonging a storm.

- **CancelWeatherEventsBySource** (`/environment/weather-event/cancel-by-source`): Bulk cancels all active weather events created by a specific source (`sourceType` + `sourceId`). Used by actor cleanup callbacks when a divine actor is stopped — cancels all weather events that actor created, returning affected locations to deterministic weather. Publishes `weather-event.ended` with reason "source_removed" for each cancelled event. This is the primary path for Design Consideration #3 (weather event cleanup after divine actor death).

### Weather Monitoring (2 endpoints) — `x-permissions: [{ role: developer }]`

These endpoints exist for divine actors (via Puppetmaster ABML actions) and regional watchers to observe environmental conditions and decide when to intervene. Without monitoring, divine weather manipulation is one-way (gods can override but can't observe).

- **GetRealmWeatherSummary** (`/environment/weather/realm-summary`): Returns aggregated weather statistics for an entire realm: location count per weather pattern, average temperature, extremes (coldest/hottest location), active weather event count, overall resource availability. Used by divine actors to make realm-level decisions ("is my realm in drought?", "how many locations are experiencing storms?"). Computed by reading cached condition snapshots, not re-computing.

- **GetWeatherByRegion** (`/environment/weather/by-region`): Returns condition snapshots for all locations within a location subtree (a parent location and all descendants). Used by regional watchers monitoring their assigned territory. Accepts a `locationId` (the region root) and returns child location conditions grouped by weather pattern. Enables spatial monitoring patterns like "what percentage of my forest region is on fire/in drought/experiencing storms?"

**Divine monitoring flow**: A storm god's ABML behavior uses `${environment.resource_availability}` and `${environment.weather}` variables for its own location context (via the variable provider), then calls `GetRealmWeatherSummary` or `GetWeatherByRegion` via Puppetmaster ABML actions for broader awareness. The god-actor also subscribes to `environment.conditions-changed` events (via actor event subscriptions in Puppetmaster) to reactively detect weather transitions across its domain. This provides both pull (query endpoints) and push (event subscription) monitoring paths.

### Resource Availability (2 endpoints) — `x-permissions: [{ role: user }]`

- **GetResourceAvailability** (`/environment/resource/get-availability`): Returns the current resource availability for a location, accounting for seasonal baseline, weather event modifiers, and divine overrides. Returns the float abundance level and contributing factors (base seasonal, event modifiers, net result). See [Resource Availability Computation](#resource-availability-computation) below for the algorithm.

- **GetRealmResourceSummary** (`/environment/resource/realm-summary`): Returns aggregate resource availability across all locations in a realm, grouped by biome. Used by Market for realm-wide economic analysis and by regional watchers for ecological assessments.

### Cleanup (3 endpoints) — `x-permissions: []` (service-to-service only, not exposed to WebSocket clients)

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
│  │   biomeCode: "alpine"  (from Environment's own binding)   │           │
│  │   altitude: 2800       (stored in binding at creation)    │           │
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

**Provider initialization**: When `CreateAsync(entityId, ct)` is called, the factory:
1. Resolves the entity's `locationId` and `realmId`. For characters: via `ICharacterClient` (cached per-entity). For actors: the Actor runtime passes `locationId` as actor context metadata — Environment reads it from the provider context rather than making a service call. This is critical because actor ticks are high-frequency; a service call per tick per actor would be prohibitive.
2. Reads the condition snapshot from `IConditionSnapshotCache` (in-memory, 5s TTL)
3. On in-memory cache miss, reads from `environment:conditions:snapshot:{locationId}` (Redis, 60s TTL)
4. On Redis cache miss, computes conditions from climate template + Worldstate + weather resolution, writes to both Redis and in-memory cache
5. Returns an `EnvironmentProvider` populated with the current condition snapshot

**Three-tier cache rationale**: At 100,000+ NPCs with 100-500ms ticks, actors at the same location share the same in-memory snapshot within the 5s window. This reduces Redis operations from ~1M/sec to ~10K/sec (one per unique location per TTL window). The 5s TTL means weather changes propagate within 5 seconds to NPC behavior — well within acceptable latency for weather phenomena that last minutes to hours.

**Actor context metadata dependency**: The Actor runtime (L2) must include `locationId` in the actor's execution context metadata so that variable provider factories can resolve spatial state without making per-tick service calls. This is an Actor-side contract: when Actor calls `CreateAsync` on `IVariableProviderFactory` implementations, the execution context should carry the actor's current locationId. This pattern benefits all spatially-aware providers (Environment, Faction territory, Transit routes), not just Environment.

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
- **Location (L2)**: Must be operational. Environment queries Location for hierarchy (parent walking) and entity existence validation. No changes to Location required — Environment owns its own climate binding data in its own state stores (per FOUNDATION TENETS: no metadata bag contracts).

### Phase 1: Climate Template Infrastructure

- Create `environment-api.yaml` schema with all endpoints
- Create `environment-events.yaml` schema with `x-event-subscriptions` for all consumed events (worldstate.period-changed, worldstate.season-changed, worldstate.day-changed, location.created, location.updated, location.deleted)
- Create `environment-configuration.yaml` schema
- Generate service code
- Implement climate template CRUD (seed, get, list, update, deprecate, bulk-seed)
- Implement climate template validation (seasonal curve coverage, weather distribution weights, baseline completeness)
- Implement location-climate binding CRUD (create, get, update, delete, bulk-seed) — Environment owns its own biome-to-location mappings per FOUNDATION TENETS (no metadata bag contracts). The `isIndoor` flag is part of the binding model from the start (default: false), enabling indoor short-circuit in weather/temperature computation
- Implement binding inheritance resolution (walk up location tree via `ILocationClient` to find nearest ancestor binding)
- Implement `RealmEnvironmentConfig` CRUD (`/environment/realm-config/set`, `/environment/realm-config/get`) for per-realm default biome codes. Stored in `environment-climate` (MySQL) alongside climate templates. Seeded alongside climate templates during world initialization
- Create Arcadia seed data files for default climate templates (temperate_forest, alpine, desert, tropical, tundra, underground, oceanic), default location-climate bindings, and per-realm `RealmEnvironmentConfig` entries in `provisioning/` alongside existing seed data, loaded via `BulkSeedClimates`, `BulkSeedBindings`, and `SetRealmConfig` during world initialization

### Phase 2: Deterministic Weather Resolution

- Implement `WeatherResolver` with hash-based deterministic weather from climate templates
- Implement indoor short-circuit: `isIndoor` bindings skip weather computation entirely (no weather, no wind, full visibility; temperature uses biome base + depth modifier only)
- Implement weather segment duration and transition dampening (deterministic hash-based, not probabilistic — see `WeatherTransitionDampening` config)
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

### Phase 6: Integrations, Background Workers & Client Events

- Implement `EnvironmentConditionWorkerService` for periodic condition refresh (cache GameTimeSnapshot per-realm per-cycle)
- Implement Worldstate event handlers (period-changed, season-changed, day-changed)
- Implement Location event handlers (location.created, location.updated, location.deleted)
- Implement Mapping weather layer publishing (soft dependency)
- Implement resource availability computation and events (using `ResourceAvailabilityChangeThreshold` to gate event publication)
- Implement resource cleanup endpoints
- Implement ABML action handlers (`register_weather_event`, `cancel_weather_event`, `extend_weather_event`) as `IActionHandler` DI services discovered by `IActionHandlerRegistry`
- Implement `EnvironmentTransitCostModifierProvider` for `ITransitCostModifierProvider` DI integration with Transit (L2)
- Create `environment-client-events.yaml` schema for weather transition push events via `IClientEventPublisher`. Published when weather changes at a player's location (not continuously). One event per weather transition per player location — detect transition → publish service event → publish client event
- Integration testing with Ethology, Workshop, Loot consumers

### Bootstrap Sequence

Environment's startup sequence follows the behavioral bootstrap lifecycle:

1. **ConfigureServices**: Register DI services — `EnvironmentProviderFactory` as `IVariableProviderFactory`, `EnvironmentTransitCostModifierProvider` as `ITransitCostModifierProvider`, ABML action handlers as `IActionHandler`, internal helpers (`IWeatherResolver`, `ITemperatureCalculator`, `IClimateTemplateCache`, `IConditionSnapshotCache`), background workers (`EnvironmentConditionWorkerService`, `EnvironmentEventExpirationWorkerService`)
2. **ConfigureApplication**: Middleware registration (none expected for Environment)
3. **OnStartAsync**: State store initialization — create 4 stores via `IStateStoreFactory`. Load `ClimateTemplateCache` from MySQL. No cross-service calls at this phase
4. **OnRunningAsync**: Cross-service API calls safe here. Register resource cleanup callbacks with lib-resource (`/environment/cleanup-by-realm`, `/environment/cleanup-by-game-service`, `/environment/cleanup-by-location`). Validate Worldstate is reachable (hard dependency). Background workers begin their periodic cycles after `ConditionUpdateStartupDelaySeconds` delay

---

## Potential Extensions

1. **Microclimate zones**: Locations tagged with microclimate modifiers that deviate from their biome template. A volcanic hot spring in an alpine region has elevated temperature. A sheltered valley in a tundra is warmer than exposed ridgelines. Implemented as per-location overrides similar to Ethology's environmental overrides, but for climate parameters.

2. **Weather fronts**: Multi-location weather systems that move across realms over game-time. A storm front that starts in the western mountains and moves east over 3 game-days, affecting each location in sequence. Requires spatial awareness (which locations are adjacent) and scheduled weather event creation along the path.

3. **Ecological succession**: Resource availability that changes over long time scales based on environmental history. A forest that burned (divine fire event) has low resource availability for several game-years, then gradually recovers. Implemented as decay-based resource modifiers that trend toward the seasonal baseline over time.

4. **Weather prediction**: A `PredictWeather` endpoint that uses the deterministic weather algorithm to predict future weather for a location. Since weather is deterministic, prediction is just forward computation. NPCs could query "will it rain tomorrow?" to plan farming, travel, or military operations. Exposed as `${environment.forecast.*}` variables.

5. **Atmospheric phenomena**: Beyond basic weather -- auroras, meteor showers, volcanic ash clouds, sandstorms, magical storms. Registered as weather event types with custom visual and mechanical effects. The service stores and resolves them; client-side rendering interprets them.

6. **Climate change over game-years**: Climate template parameter drift over long time scales. A realm experiencing deforestation (tracked by Realm-History or Storyline) gradually shifts temperature curves warmer and resource availability lower. Implemented as per-realm climate modifier history similar to Worldstate's ratio segments.

7. **Biome transition zones**: Locations at the boundary between two biomes blend climate templates rather than using one or the other. A forest-edge location between temperate forest and alpine gets 60/40 blended temperature curves and weather distributions. Implemented as a secondary biome code with a blend weight.

8. **Seasonal migration triggers**: Publish advisory events when environmental conditions cross species-relevant thresholds (first frost, sustained drought, first snow). Ethology and Actor behaviors can subscribe to these for migration decision support. Not weather events (those are overrides) -- these are observations about the deterministic weather crossing significant boundaries.

---

## Why Not Extend Worldstate or Use Mapping?

Environment is **not a clock** (time is Worldstate's concern), **not a spatial engine** (where things are is Location and Mapping's concern), **not a physics simulation** (rain doesn't pool, wind doesn't blow objects -- those are client rendering concerns; Environment provides the DATA), **not a biome definition service** (biome types are Environment's own domain data stored as location-climate bindings, not Location's), and **not Ethology** (Ethology provides behavioral archetypes and nature values; Environment provides the conditions that Ethology's environmental overrides react to). They compose, they don't overlap.

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

7. **Three-tier condition cache is eventually consistent**: Condition snapshots flow through three cache tiers: in-memory (`IConditionSnapshotCache`, 5s TTL) → Redis (`environment:conditions:snapshot`, 60s TTL) → full computation. When weather transitions occur, the background worker writes to both Redis and in-memory caches. Entities mid-behavior-tick use their cached snapshot until the next tick. At the in-memory 5s TTL, environmental changes propagate within ~5 seconds to NPC behavior. The 60s Redis TTL bounds the worst case for cold-start reads. This is acceptable for weather changes (which last minutes to hours) but means an instantaneous divine storm takes up to 5 seconds (not 60) to affect NPC behavior at the in-memory tier.

8. **No wind simulation**: Wind speed and direction are looked up from climate template baselines + weather modifiers. Wind doesn't flow, interact with terrain, or create downstream effects. It's a data value that behaviors reference (`${environment.wind.speed}` for "is it too windy to fire arrows?"), not a physics simulation.

9. **Altitude is from Environment's own binding, not Mapping**: Temperature altitude modifiers use the altitude stored in Environment's location-climate binding (captured from Location's spatial coordinates at binding creation time), not the Mapping service's 3D coordinates. This is intentional -- Mapping is a soft dependency, and altitude for temperature purposes is a coarse location-level property, not a per-coordinate value. A mountaintop location has altitude 3000 regardless of where within that location an entity stands.

10. **Weather segment boundaries can cause brief inconsistencies**: When a weather segment expires and a new one is computed, entities querying conditions during the transition may briefly see different weather depending on whether their cached snapshot pre-dates the transition. The `ConditionCacheTtlSeconds` bounds this window. In practice, weather transitions are every 4-24 game-hours (10-60 real minutes), so this is rarely observable.

11. **Indoor locations are weather-exempt by design**: Locations with `isIndoor: true` on their `LocationClimateBinding` skip weather computation entirely. Their condition snapshot returns: no weather code, no precipitation, no wind, full visibility (1.0), and temperature computed solely from the biome's seasonal base modified by depth (caves warm with depth). This means ABML expressions like `${environment.weather.is_storm}` are always false for indoor locations — NPCs inside buildings never "check weather" because there is no weather to check. If a game needs indoor weather effects (a magical storm inside a castle), use a weather event scoped to that specific location, which overrides the indoor short-circuit.

### Design Considerations (Requires Planning)

1. **Scale of condition computation for large worlds**: The background worker is **realm-scoped** -- one distributed lock per realm, one refresh cycle per realm, independently parallelizable across nodes. A realm with 10,000 locations processes them in batches within its refresh cycle (~every 30 real seconds at default interval). This is architecturally fixed: per-realm locks, per-realm iteration, per-realm Worldstate queries.

   **Active-region-only optimization** (`ConditionRefreshMode` config): For realms with thousands of locations where most are dormant (no players, no active NPCs), the `ActiveOnly` mode skips locations that haven't been queried or had entity activity within the `ActiveLocationWindowMinutes` window. Active locations are tracked via a Redis sorted set (`environment:active:{realmId}` with timestamps as scores), updated whenever a condition query or variable provider reads a location's snapshot. Dormant locations still resolve conditions correctly -- they just compute lazily on first query rather than being pre-cached by the worker. Trade-offs:
   - `AllLocations` (default): All condition caches are always warm. Mapping weather layer is always current for all locations. Higher CPU/Redis cost for large realms with many dormant areas.
   - `ActiveOnly`: Only active locations pay refresh cost. Dormant locations have a cold-cache penalty on first query (~5-10ms compute vs <1ms cache hit). Mapping weather layer may be stale for dormant areas until queried. Better scaling for large worlds where <10% of locations are active at any given time.

   The Mapping publishing step (pushing weather data to spatial index) is the primary bottleneck at scale. `MappingPublishBatchSize` bounds the per-cycle publish volume. In `ActiveOnly` mode, only active locations publish to Mapping, significantly reducing spatial index write pressure.

2. **Climate template complexity vs. performance**: Rich climate templates with many weather patterns, complex temperature curves, and multiple seasonal baselines require non-trivial computation on every condition resolution. The `ClimateTemplateCache` mitigates MySQL queries, but the computation itself (seasonal interpolation, hourly curve evaluation, altitude adjustment) runs on every cache miss. Pre-computing daily condition tables per location (one computation per game-day per location) would amortize the cost.

3. **Weather event cleanup after divine actor death**: When a divine actor (via Puppetmaster) creates weather events and then the actor is stopped (realm deleted, god defeated), the weather events should be cleaned up. The `sourceType`/`sourceId` fields enable this -- the actor cleanup callback calls `CancelWeatherEventsBySource` to cancel all events created by that actor (publishes `weather-event.ended` with reason "source_removed" for each). This cleanup path must be wired through Puppetmaster's actor lifecycle: when an actor is stopped, Puppetmaster should call Environment's bulk cancel endpoint as part of actor teardown.

4. **Condition → behavior cascade latency**: Environmental changes ripple through a publish-subscribe chain: Worldstate publishes `period-changed` → Environment recomputes conditions and publishes `conditions-changed` → Ethology creates/updates behavioral overrides → Actor reads new variable values on next tick. With three-tier caching, the Environment leg propagates in ~5 seconds (in-memory TTL). The Ethology leg depends on its own cache TTL. The full chain latency is bounded by the sum of cache TTLs in the path. At defaults, changes propagate within ~1-2 minutes end-to-end. This is acceptable for weather phenomena that persist for 10-60+ real minutes. Sudden divine events (instant storm) propagate faster because they invalidate condition caches immediately rather than waiting for the background worker cycle.

5. **Offline/downtime weather catch-up**: When the service starts after downtime, Worldstate catches up game-time (advance policy). Environment should NOT recompute conditions for every missed weather segment during catch-up. Instead, compute current conditions from the current game-time snapshot (deterministic weather is reproducible from the current state) and publish a single `conditions-changed` event. Consumers handle catch-up via their own mechanisms (Workshop materializes missed production, Loot regenerates tables).

6. **Multi-realm climate template sharing**: Multiple realms can reference the same climate templates (both use `temperate_forest`). This is fine for read operations but means updating a template affects all realms using it. Climate template updates should be admin-only and infrequent. Per-realm template overrides would add complexity; prefer creating separate templates for realms that need different climate parameters.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Worldstate (L2) is the hard prerequisite -- Environment cannot compute anything without game time and season data. Phase 1 (climate template infrastructure) is self-contained. Phase 2 (deterministic weather) is self-contained once Phase 1 is done. Phase 5 (variable provider) depends on Phase 2-3 and can be integration-tested with Actor runtime. Phase 6 (integrations) connects to Ethology, Workshop, Mapping, and other consumers.*
