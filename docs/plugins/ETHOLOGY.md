# Ethology Plugin Deep Dive

> **Plugin**: lib-ethology (not yet created)
> **Schema**: `schemas/ethology-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: ethology-archetypes (MySQL), ethology-overrides (MySQL), ethology-cache (Redis), ethology-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

## Overview

Species-level behavioral archetype registry and nature resolution service (L4 GameFeatures) for providing structured behavioral defaults to any entity that runs through the Actor behavior system. The missing middle ground between "hardcoded behavior document defaults" (every wolf is identical) and "full character cognitive stack" (9 variable providers, per-entity persistent state). A structured definition of species-level behavioral baselines with hierarchical overrides (realm, location) and per-individual deterministic noise, exposed as a variable provider to the Actor behavior system. Game-agnostic: behavioral axes are opaque strings -- a horror game could define `stalking_patience` and `ambush_preference`, a farming sim could define `tamability` and `herd_cohesion`. Internal-only, never internet-facing.

---

## Core Mechanics: Behavioral Archetypes

The Actor behavior system has a clean, rich cognitive stack for characters:

| Provider | Service | What It Answers |
|----------|---------|----------------|
| `${personality.*}` | Character-Personality (L4) | "What kind of temperament does this character have?" |
| `${combat.*}` | Character-Personality (L4) | "What are this character's combat preferences?" |
| `${encounters.*}` | Character-Encounter (L4) | "What memorable interactions has this character had?" |
| `${backstory.*}` | Character-History (L4) | "What biographical events shaped this character?" |
| `${obligations.*}` | Obligation (L4) | "What contractual commitments constrain this character?" |
| `${faction.*}` | Faction (L4) | "What faction memberships and status does this character have?" |
| `${location.*}` | Location (L2) | "What location context does this entity have?" |
| `${quest.*}` | Quest (L2) | "What objectives is this character pursuing?" |
| `${seed.*}` | Seed (L2) | "What capabilities has this entity grown into?" |

For non-character entities -- wolves, bears, monsters, dungeon creatures, wildlife -- **none of these providers fire.** The actor loads `creature_base.yaml` and gets hardcoded context defaults:

```yaml
variables:
  hunger_threshold: 0.7     # Same for every creature
  fear_threshold: 0.5       # Same for every creature
  territory_radius: 50.0    # Same for every creature
  aggression_level: 0.3     # Same for every creature
```

Every wolf is identical. Every bear is identical. A wolf and a bear sharing the same behavior document have the same defaults. The only differentiation mechanism is which ABML document loads (via the variant chain), not what values feed into that document.

Nature values are computed from three layers:

| Layer | Source | Example |
|-------|--------|---------|
| **Species archetype** (base) | Species-level behavioral template | "Wolves are aggressive (0.7), pack-social (0.9)" |
| **Environmental override** | Realm and location modifications | "Ironpeak wolves: aggression +0.15 (harsh environment)" |
| **Individual noise** | Deterministic hash from entity ID | "This wolf: aggression +0.03 (consistent per-entity)" |

From the [Vision](../../arcadia-kb/VISION.md): "The world is alive whether or not a player is watching." For this to feel true at the ecosystem level, animals and creatures need perceptible individuality. The alpha wolf that's slightly more aggressive and territorial than the omega. The old bear that's less curious and more cautious. The pack of wild dogs where each has distinct behavioral tendencies. Without per-individual variation, ecosystems feel like tiled patterns rather than living populations.

### Archetype Definition

Every species with behavioral actors has an archetype definition:

```
BehavioralArchetype:
  archetypeId:       Guid
  gameServiceId:     Guid          # Game service scope
  speciesCode:       string        # Links to Species service entity
  archetypeCode:     string        # Unique code within game scope (e.g., "wolf", "cave-bear")
  displayName:       string
  description:       string
  category:          string        # "predator", "prey", "apex", "scavenger", "ambient", "pack", "solitary", "custom"
  axes:              ArchetypeAxis[]  # Behavioral axis definitions with base values
  activityPattern:   string        # "diurnal", "nocturnal", "crepuscular", "cathemeral"
  dietType:          string        # "carnivore", "herbivore", "omnivore", "detritivore"
  socialStructure:   string        # "solitary", "pair", "pack", "herd", "swarm", "colony"
  noiseAmplitude:    float         # Max individual noise offset (default 0.1, range 0.0-0.3)
  isDeprecated:      bool
  deprecatedAt:      DateTime?
  deprecationReason: string?
  createdAt:         DateTime
  updatedAt:         DateTime
```

### Behavioral Axes

Axes are the core data structure -- named behavioral dimensions with float values:

```
ArchetypeAxis:
  axisCode:       string      # "aggression", "territoriality", "curiosity", etc.
  displayName:    string      # "Aggression"
  baseValue:      float       # Species-level default (0.0-1.0)
  minValue:       float       # Lower bound after overrides + noise (default 0.0)
  maxValue:       float       # Upper bound after overrides + noise (default 1.0)
  description:    string      # "How readily this entity initiates hostile action"
```

Axis codes are **opaque strings** (not enums), following the same extensibility pattern as seed type codes, collection type codes, and lifecycle stage codes. These are Arcadia conventions, not constraints:

| Axis Code | Typical Base | Description |
|-----------|-------------|-------------|
| `aggression` | 0.0-1.0 | Readiness to initiate hostility |
| `territoriality` | 0.0-1.0 | Strength of territorial defense response |
| `curiosity` | 0.0-1.0 | Tendency to investigate novel stimuli vs. avoid |
| `fear_threshold` | 0.0-1.0 | Stimulus level required to trigger flight (low = skittish, high = brave) |
| `sociality` | 0.0-1.0 | Pack/herd cohesion tendency (0.0 = solitary, 1.0 = always in group) |
| `persistence` | 0.0-1.0 | How long entity pursues a goal before abandoning |
| `vigilance` | 0.0-1.0 | Baseline alertness and perception sensitivity |
| `docility` | 0.0-1.0 | Susceptibility to taming or calming influence |

A game with different needs defines entirely different axes. A survival horror might use `stalking_patience`, `ambush_preference`, `noise_sensitivity`. The service doesn't care -- it stores and resolves float values against string codes.

### Example Archetypes (Arcadia)

```
Wolf (predator, pack):
  aggression:     0.7     # Aggressive, especially in packs
  territoriality: 0.8     # Strongly territorial
  curiosity:      0.4     # Moderately curious
  fear_threshold: 0.5     # Mid-range -- fights or flees depending on odds
  sociality:      0.9     # Highly social, pack-oriented
  persistence:    0.7     # Persistent in pursuit
  vigilance:      0.6     # Alert but not hypervigilant
  docility:       0.2     # Difficult to tame

Deer (prey, herd):
  aggression:     0.1     # Almost never aggressive (males in rut: override)
  territoriality: 0.2     # Weak territorial instinct
  curiosity:      0.5     # Moderately curious
  fear_threshold: 0.2     # Very skittish -- flees at low threat
  sociality:      0.7     # Herd animal
  persistence:    0.3     # Gives up quickly when pursued
  vigilance:      0.8     # Highly vigilant
  docility:       0.4     # Somewhat tameable

Cave Bear (apex, solitary):
  aggression:     0.6     # Aggressive when threatened, not actively predatory
  territoriality: 0.9     # Extremely territorial about den
  curiosity:      0.3     # Low curiosity -- suspicious of novelty
  fear_threshold: 0.8     # Very brave -- rarely flees
  sociality:      0.1     # Solitary except during mating
  persistence:    0.5     # Moderate chase persistence
  vigilance:      0.4     # Moderate (apex has fewer threats)
  docility:       0.1     # Nearly impossible to tame
```

---

## Core Mechanics: Environmental Overrides

### Override Model

Environmental overrides modify archetype base values for specific realms and locations:

```
EnvironmentalOverride:
  overrideId:      Guid
  gameServiceId:   Guid
  archetypeCode:   string        # Which archetype this modifies
  scopeType:       string        # "realm" or "location"
  scopeId:         Guid          # RealmId or LocationId
  reason:          string        # "harsh_climate", "plentiful_food", "heavy_predation", etc.
  axisModifiers:   AxisModifier[]
  isActive:        bool          # Can be disabled without deletion
  createdAt:       DateTime
  updatedAt:       DateTime
```

```
AxisModifier:
  axisCode:        string        # Which axis to modify
  modifier:        float         # Additive modifier (can be negative)
```

### Override Resolution

When computing an entity's nature values, overrides are resolved hierarchically:

```
1. Start with species archetype base values
2. Apply realm-level overrides (if any exist for this archetype + realm)
3. Apply location-level overrides (if any exist for this archetype + location)
   - Location overrides stack ON TOP of realm overrides (additive)
   - More specific scope wins for the same axis (location replaces realm modifier for that axis)
4. Apply individual noise (deterministic hash from entity ID + axis code)
5. Clamp all values to [minValue, maxValue] range per axis
```

### Resolution Examples

```
Wolf in Ironpeak Mountains (harsh, high-altitude realm):
  Base archetype:        aggression = 0.7
  Realm override:        aggression +0.15  (harsh climate breeds aggression)
  Location override:     (none for this location)
  Individual noise:      aggression +0.03  (this wolf is slightly above average)
  Final:                 aggression = 0.88

Wolf in Verdant Meadows (gentle, resource-rich realm):
  Base archetype:        aggression = 0.7
  Realm override:        aggression -0.2   (plentiful food reduces aggression)
  Location override:     (none)
  Individual noise:      aggression -0.05  (this wolf is slightly below average)
  Final:                 aggression = 0.45

Deer near Wolf Den (location-specific override):
  Base archetype:        vigilance = 0.8
  Realm override:        (none)
  Location override:     vigilance +0.15   (proximity to predator den)
                         fear_threshold -0.1 (more skittish near predators)
  Individual noise:      vigilance +0.02
  Final:                 vigilance = 0.97, fear_threshold = 0.12
```

### Why Location-Level Matters

Location overrides enable **emergent ecological storytelling**. A deity actor (Silvanus/Forest watcher) could register overrides through its behavior:

```yaml
# Regional watcher behavior: after a wolf pack moves into the forest
flows:
  ecosystem_adjustment:
    - action: register_ethology_override
        archetype_code: "deer"
        scope_type: "location"
        scope_id: "${watch_region.location_id}"
        axis_modifiers:
          - axis: "vigilance"
            modifier: 0.15
          - axis: "fear_threshold"
            modifier: -0.1
        reason: "wolf_pack_established"
```

The deer in that area become more vigilant and skittish -- not because someone scripted it, but because the ecosystem simulation produced it. This is the "emergent over authored" principle applied to ecology.

---

## Core Mechanics: Individual Noise

Individual noise is **deterministic** -- hash the entity ID + trait code to get a consistent offset within a configured noise amplitude. No per-entity storage needed. The same wolf always has the same noise. This is cheap, stateless, and supports 100,000+ creatures without per-entity state store entries.

### Deterministic Noise Function

Individual noise provides per-entity variation without per-entity storage:

```
noise(entityId, axisCode, amplitude) =
  hash(entityId + axisCode) normalized to [-amplitude, +amplitude]

Where:
  - entityId: the entity's Guid (character, actor, creature instance)
  - axisCode: the behavioral axis being computed
  - amplitude: the archetype's noiseAmplitude setting (default 0.1)
  - hash: deterministic hash function (e.g., MurmurHash3 on concatenated bytes)
  - normalization: hash output mapped to [-amplitude, +amplitude] range
```

**Properties**:
- **Deterministic**: The same entity always gets the same noise for the same axis
- **Uncorrelated across axes**: An entity that's high-noise on aggression is not necessarily high-noise on curiosity (different hash inputs)
- **Consistent across nodes**: Any server node computes the same noise for the same entity (no distributed state needed)
- **Cheap**: Single hash computation per axis per entity, no I/O

**Why not random?** Random variation would make the same wolf behave differently on different server ticks, different nodes, or after service restarts. Deterministic noise means this wolf is ALWAYS slightly more aggressive than average -- it has a consistent identity without needing persistent per-entity state.

### Noise Amplitude Per Archetype

Different species have different amounts of individual variation:

| Species | Noise Amplitude | Rationale |
|---------|----------------|-----------|
| Wolf | 0.10 | Moderate variation -- pack coordination requires behavioral consistency |
| Cat | 0.25 | High variation -- cats are famously individualistic |
| Ant | 0.02 | Near-zero variation -- colony members are behaviorally uniform |
| Dragon | 0.20 | High variation -- ancient, intelligent creatures with distinct personalities |

This is configured per archetype, not globally. The service doesn't decide how much variation a species has -- the game designer does via the archetype definition.

---

## Core Mechanics: Character Delegation

When the nature provider encounters a character ID, it checks whether Heritage data is available. If Heritage is loaded (the full cognitive stack is active), Heritage values take precedence -- genetics are more specific than species archetypes. If Heritage is unavailable (character created without lifecycle tracking, or Character-Lifecycle plugin not enabled), the provider falls back to species archetype + noise. This makes `${nature.*}` the universal baseline that Heritage refines for characters.

### Heritage-Aware Resolution

When `NatureProviderFactory.CreateAsync()` receives an entity ID, it must determine whether the entity is a character with Heritage data or a simpler creature:

```
Resolution Flow:
  1. Query archetype for the entity's species code
     - Species code comes from the actor template's metadata
     - If no archetype exists for this species: return empty provider (graceful degradation)

  2. Check if Heritage provider data is available:
     - Attempt to read from the lifecycle cache (heritage phenotype)
     - If Heritage data exists AND is non-empty:
       → Use Heritage phenotype values for axes that have personality/aptitude mappings
       → Fall back to archetype base + overrides + noise for axes WITHOUT Heritage mappings
     - If Heritage data is unavailable:
       → Use full archetype resolution (base + overrides + noise)

  3. Apply environmental overrides:
     - Heritage-sourced values are NOT overridden (genetics > environment for characters)
     - Archetype-sourced values ARE overridden by realm/location modifiers

  4. Apply individual noise:
     - Heritage-sourced values: NO noise applied (Heritage already provides individual variation)
     - Archetype-sourced values: noise applied as normal

  5. Return computed NatureProfile as IVariableProvider
```

### Why Characters Need This Too

Even with the full cognitive stack, characters benefit from `${nature.*}`:

1. **Universal baseline**: Behavior documents can reference `${nature.aggression}` for both character NPCs and creatures. For characters, this resolves to their Heritage-derived aggression baseline. For creatures, it resolves to species archetype + noise. Same variable, different resolution paths, same ABML code.

2. **Graceful degradation**: A newly created character that hasn't been processed by Character-Lifecycle yet still gets species-level defaults rather than nothing.

3. **Species-level axes without Heritage mappings**: Some behavioral axes (like `activity_pattern` or `diet_type`) are species-level concerns that Heritage doesn't cover. `${nature.activity_pattern}` provides these regardless of character vs. creature status.

---

## Variable Provider: `${nature.*}` Namespace

Implements `IVariableProviderFactory` (via `NatureProviderFactory`). Loads from the cached nature manifest or computes on demand.

### Behavioral Axes

| Variable | Type | Description |
|----------|------|-------------|
| `${nature.<axisCode>}` | float | Resolved value for any behavioral axis defined in the archetype (0.0-1.0). Three-layer resolution: archetype base + environmental overrides + individual noise. For characters with Heritage, heritage-mapped axes use phenotype values instead. |
| `${nature.aggression}` | float | Example: readiness to initiate hostility |
| `${nature.territoriality}` | float | Example: strength of territorial defense |
| `${nature.curiosity}` | float | Example: tendency to investigate vs. avoid |
| `${nature.fear_threshold}` | float | Example: stimulus level to trigger flight |
| `${nature.sociality}` | float | Example: pack/herd cohesion tendency |
| `${nature.persistence}` | float | Example: pursuit duration before abandoning |
| `${nature.vigilance}` | float | Example: baseline alertness |
| `${nature.docility}` | float | Example: susceptibility to taming |

### Metadata

| Variable | Type | Description |
|----------|------|-------------|
| `${nature.species}` | string | Species code from archetype |
| `${nature.category}` | string | Archetype category (predator, prey, apex, etc.) |
| `${nature.activity_pattern}` | string | Diurnal, nocturnal, crepuscular, cathemeral |
| `${nature.diet}` | string | Carnivore, herbivore, omnivore, detritivore |
| `${nature.social_structure}` | string | Solitary, pair, pack, herd, swarm, colony |
| `${nature.has_archetype}` | bool | Whether an archetype was found for this entity's species |

### ABML Usage Examples

```yaml
flows:
  creature_decision:
    - cond:
        # Territorial species -- defend territory aggressively
        - when: "${nature.territoriality > 0.7
                  && perception.intruder_in_territory}"
          then:
            - call: territorial_defense
            - set:
                response_intensity: "${nature.aggression * nature.persistence}"

        # Prey species -- flee early
        - when: "${nature.fear_threshold < 0.3
                  && perception.threat_detected}"
          then:
            - call: flee_response
            - set:
                flee_urgency: "${1.0 - nature.fear_threshold}"

        # Curious creature encountering novelty
        - when: "${nature.curiosity > 0.6
                  && perception.novel_stimulus
                  && !perception.threat_detected}"
          then:
            - call: investigate_stimulus

  pack_behavior:
    - cond:
        # Highly social -- seek group
        - when: "${nature.sociality > 0.7
                  && !perception.pack_nearby}"
          then:
            - call: seek_pack_members
            - set:
                loneliness: "${nature.sociality * 0.8}"

        # Alpha behavior -- this individual is more aggressive than pack average
        - when: "${nature.aggression > 0.8
                  && nature.sociality > 0.5
                  && perception.pack_nearby}"
          then:
            - call: assert_dominance

  # Same variable works for characters too
  character_combat_style:
    - cond:
        # Character inherits aggressive nature from heritage/species
        - when: "${nature.aggression > 0.7
                  && nature.fear_threshold > 0.6}"
          then:
            - call: aggressive_combat_opener
        # Cautious nature -- wait and observe
        - when: "${nature.aggression < 0.3
                  && nature.vigilance > 0.6}"
          then:
            - call: defensive_stance
```

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Archetype definitions (MySQL), environmental overrides (MySQL), nature profile cache (Redis), distributed locks (Redis) |
| lib-messaging (`IMessageBus`) | Publishing archetype events (created, updated, deprecated), error event publication |
| lib-messaging (`IEventConsumer`) | Subscribing to species events (deprecated, deleted), location events (deprecated, deleted) |
| lib-species (`ISpeciesClient`) | Validating species existence when creating archetypes, querying species data for trait modifier cross-reference (L2) |
| lib-game-service (`IGameServiceClient`) | Game service scope validation for archetypes (L2) |
| lib-location (`ILocationClient`) | Validating location existence when creating location-scoped overrides, querying location hierarchy for override stacking (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-character-lifecycle (`ICharacterLifecycleClient`) | Querying Heritage phenotype data for character delegation. When available, Heritage-derived values take precedence over species archetypes for characters (L4). | All entities (including characters) resolve via species archetype + overrides + noise. Characters lose genetic individuality but still get species-level behavioral defaults. |
| lib-analytics (`IAnalyticsClient`) | Publishing archetype usage statistics (which archetypes are most active, population distribution per archetype) (L4). | No analytics. Silent skip. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (L2) | Actor discovers `NatureProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection; creates provider instances per entity for ABML behavior execution (`${nature.*}` variables). When nature provider is unavailable, actors continue with behavior document defaults (existing graceful degradation). |
| lib-puppetmaster (L4) | Regional watcher behaviors (god-of-monsters, Silvanus/Forest) can create and modify environmental overrides through ABML actions, dynamically adjusting creature behavior in their territories. Overrides are the mechanism through which gods shape local ecology. |
| lib-character-lifecycle (L4) | Character-Lifecycle's Heritage Engine defines per-individual genetic traits; Ethology provides the species baseline that Heritage refines. The two services are complementary: Heritage provides NATURE for characters specifically; Ethology provides NATURE for everything. |
| lib-actor (L2, creature behaviors) | Creature ABML behavior documents (`creature_base.yaml`, combat behaviors, territorial behaviors) reference `${nature.*}` variables for species-appropriate decision making. Without ethology, these variables return null and behaviors fall back to hardcoded defaults. |
| lib-disposition (L4, planned) | Disposition's drive formation could use `${nature.*}` as input for species-influenced drive tendencies. A species with high `curiosity` baseline might have a higher probability of forming `explore_world` drives. Future extension. |
| lib-obligation (L4) | Obligation's action cost modifiers could incorporate species-level behavioral norms: a wolf's obligation system treats `hunt_prey` as low-cost and `flee_from_weaker` as high-cost. The nature provider gives obligation the species context to compute these costs. |

---

## State Storage

### Archetype Store
**Store**: `ethology-archetypes` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `archetype:{archetypeId}` | `BehavioralArchetypeModel` | Primary lookup. Full archetype definition with all axes. |
| `archetype:code:{gameServiceId}:{archetypeCode}` | `BehavioralArchetypeModel` | Code-based lookup within game scope. |
| `archetype:species:{gameServiceId}:{speciesCode}` | `BehavioralArchetypeModel` | Species-based lookup (one archetype per species per game). |
| `archetype:game:{gameServiceId}` | `BehavioralArchetypeModel` | All archetypes for a game service (for listing/admin). |

### Override Store
**Store**: `ethology-overrides` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `override:{overrideId}` | `EnvironmentalOverrideModel` | Primary lookup. Full override definition. |
| `override:archetype:{archetypeCode}:realm:{realmId}` | `EnvironmentalOverrideModel` | Realm-scoped overrides for an archetype. |
| `override:archetype:{archetypeCode}:location:{locationId}` | `EnvironmentalOverrideModel` | Location-scoped overrides for an archetype. |
| `override:scope:{scopeType}:{scopeId}` | `EnvironmentalOverrideModel` | All overrides for a specific realm or location (for admin/cleanup). |

### Nature Cache
**Store**: `ethology-cache` (Backend: Redis, prefix: `ethology:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `resolved:{archetypeCode}:{scopeHash}` | `ResolvedArchetypeModel` | Cached archetype + realm/location overrides (pre-noise). ScopeHash is a hash of realmId + locationId. TTL-based (default 5 minutes). Individual noise is applied at read time on top of this cached base. |
| `manifest:{entityId}` | `NatureManifestModel` | Cached per-entity nature profile (full resolution including noise). Short TTL (default 60 seconds). Invalidated when overrides change for the entity's scope. Used by the variable provider for fast reads during behavior ticks. |

### Distributed Locks
**Store**: `ethology-lock` (Backend: Redis, prefix: `ethology:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `archetype:{archetypeCode}` | Archetype definition mutations (create, update, deprecate) |
| `override:{scopeType}:{scopeId}:{archetypeCode}` | Override mutations for a specific scope + archetype |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `ethology.archetype.created` | `EthologyArchetypeCreatedEvent` | New behavioral archetype registered. Includes archetypeCode, speciesCode, gameServiceId, axis codes and base values. |
| `ethology.archetype.updated` | `EthologyArchetypeUpdatedEvent` | Archetype definition modified. Includes archetypeCode, changedFields list, previous and new values for changed axes. Triggers cache invalidation for all entities using this archetype. |
| `ethology.archetype.deprecated` | `EthologyArchetypeDeprecatedEvent` | Archetype marked deprecated. Includes archetypeCode, reason. Existing entities continue using cached data; new entity creation with this archetype is rejected. |
| `ethology.override.created` | `EthologyOverrideCreatedEvent` | Environmental override registered. Includes archetypeCode, scopeType, scopeId, axis modifiers. Triggers cache invalidation for affected scope. |
| `ethology.override.updated` | `EthologyOverrideUpdatedEvent` | Override modified. Includes overrideId, changedFields, previous/new modifiers. Triggers cache invalidation. |
| `ethology.override.removed` | `EthologyOverrideRemovedEvent` | Override deactivated or deleted. Includes overrideId, archetypeCode, scopeType, scopeId. Triggers cache invalidation. |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `species.deprecated` | `HandleSpeciesDeprecatedAsync` | When a species is deprecated, log a warning if active archetypes reference it. Do NOT auto-deprecate the archetype -- archetypes may outlive species deprecation for migration purposes. |
| `species.deleted` | `HandleSpeciesDeletedAsync` | When a species is hard-deleted, deprecate any archetypes that reference it. Existing cached nature profiles remain valid until TTL expiry. |
| `location.deprecated` | `HandleLocationDeprecatedAsync` | Deactivate location-scoped overrides for the deprecated location. Publish override.removed events for cache invalidation. |
| `location.deleted` | `HandleLocationDeletedAsync` | Remove all overrides scoped to the deleted location. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| realm | ethology | CASCADE | `/ethology/cleanup-by-realm` |

When a realm is deleted, all realm-scoped overrides for that realm are removed. Archetypes are NOT deleted (they are game-scoped, not realm-scoped).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultNoiseAmplitude` | `ETHOLOGY_DEFAULT_NOISE_AMPLITUDE` | `0.1` | Default noise amplitude for archetypes that don't specify one (range: 0.0-0.3) |
| `ResolvedArchetypeCacheTtlSeconds` | `ETHOLOGY_RESOLVED_ARCHETYPE_CACHE_TTL_SECONDS` | `300` | TTL for cached archetype + overrides (pre-noise) in Redis (range: 30-3600) |
| `EntityManifestCacheTtlSeconds` | `ETHOLOGY_ENTITY_MANIFEST_CACHE_TTL_SECONDS` | `60` | TTL for cached per-entity nature manifests (range: 10-600) |
| `MaxAxesPerArchetype` | `ETHOLOGY_MAX_AXES_PER_ARCHETYPE` | `20` | Maximum behavioral axes per archetype definition (range: 1-50) |
| `MaxOverridesPerScope` | `ETHOLOGY_MAX_OVERRIDES_PER_SCOPE` | `100` | Maximum overrides per realm or location (range: 10-500) |
| `MaxArchetypesPerGameService` | `ETHOLOGY_MAX_ARCHETYPES_PER_GAME_SERVICE` | `200` | Maximum archetypes per game service (range: 10-1000) |
| `NoiseHashSeed` | `ETHOLOGY_NOISE_HASH_SEED` | `0` | Seed for the noise hash function. Changing this recomputes all individual noise values (range: 0-2147483647) |
| `DistributedLockTimeoutSeconds` | `ETHOLOGY_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `15` | Timeout for distributed lock acquisition (range: 5-60) |
| `QueryPageSize` | `ETHOLOGY_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries (range: 1-100) |
| `CacheWarmupEnabled` | `ETHOLOGY_CACHE_WARMUP_ENABLED` | `true` | Whether the cache warmup worker pre-populates archetype cache on startup |
| `CacheWarmupDelaySeconds` | `ETHOLOGY_CACHE_WARMUP_DELAY_SECONDS` | `10` | Delay before cache warmup begins (range: 0-120) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<EthologyService>` | Structured logging |
| `EthologyServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 4 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Species, location event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ISpeciesClient` | Species validation (L2) |
| `IGameServiceClient` | Game service scope validation (L2) |
| `ILocationClient` | Location validation for override scoping (L2) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Background Workers

| Worker | Trigger | Lock Key | Purpose |
|--------|---------|----------|---------|
| `EthologyCacheWarmupWorkerService` | Startup (one-shot) | `ethology:lock:warmup` | Pre-populates Redis cache with resolved archetypes (base + realm overrides) for all active archetypes. Runs once at startup after configured delay. Prevents cold-cache latency on first behavior ticks. |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `NatureProviderFactory` | `${nature.*}` | Reads from ethology cache (resolved archetype + noise). For characters with Heritage data, delegates heritage-mapped axes to Heritage phenotype values. Provides species-level behavioral baselines, environmental modifications, and individual variation. | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Archetype Management (8 endpoints)

All endpoints require `developer` role.

- **RegisterArchetype** (`/ethology/archetype/register`): Creates a new behavioral archetype for a species within a game service. Validates species exists via `ISpeciesClient`. Validates archetype code uniqueness within game scope. Validates axis codes are unique within the archetype. Publishes `ethology.archetype.created`.

- **GetArchetype** (`/ethology/archetype/get`): Returns full archetype definition by ID.

- **GetArchetypeBySpecies** (`/ethology/archetype/get-by-species`): Returns archetype for a species within a game service. Primary lookup path for the variable provider.

- **GetArchetypeByCode** (`/ethology/archetype/get-by-code`): Code-based lookup within game scope.

- **UpdateArchetype** (`/ethology/archetype/update`): Partial update of archetype definition. Can modify axis base values, add/remove axes, change noise amplitude, update metadata. Changed axes invalidate all cached nature profiles for entities using this archetype. Publishes `ethology.archetype.updated` with changed fields.

- **ListArchetypes** (`/ethology/archetype/list`): Paged list of archetypes within a game service. Filterable by category, deprecated status.

- **DeprecateArchetype** (`/ethology/archetype/deprecate`): Marks archetype deprecated with reason. Existing entities continue using cached data. New entity creation with this species is warned (not blocked -- the entity still needs SOME behavioral defaults). Publishes `ethology.archetype.deprecated`.

- **SeedArchetypes** (`/ethology/archetype/seed`): Bulk creation/update of archetypes from configuration. Idempotent with `updateExisting` flag. Used during world initialization to define all species behavioral baselines at once.

### Override Management (6 endpoints)

All endpoints require `developer` role.

- **CreateOverride** (`/ethology/override/create`): Creates an environmental override for an archetype within a realm or location scope. Validates the archetype exists. Validates realm/location exists via soft dependency (if available). Validates axis codes exist in the archetype definition. Publishes `ethology.override.created`. Invalidates cached resolved archetypes for affected scope.

- **GetOverride** (`/ethology/override/get`): Returns full override definition by ID.

- **UpdateOverride** (`/ethology/override/update`): Partial update of axis modifiers. Publishes `ethology.override.updated`. Invalidates affected caches.

- **ListOverrides** (`/ethology/override/list`): Paged list of overrides. Filterable by archetype code, scope type, scope ID.

- **DeactivateOverride** (`/ethology/override/deactivate`): Soft-deactivates an override (sets `isActive = false`). Override remains in storage but is excluded from resolution. Publishes `ethology.override.removed`. Useful for temporary ecosystem adjustments.

- **ActivateOverride** (`/ethology/override/activate`): Re-activates a deactivated override. Publishes `ethology.override.created` (treated as a new override for cache purposes).

### Nature Query (3 endpoints)

- **ResolveNature** (`/ethology/nature/resolve`): Computes the full nature profile for an entity given its species code, realm ID, location ID, and entity ID. Returns all axis values after three-layer resolution (archetype + overrides + noise). If Heritage data is available for the entity, Heritage-mapped values are included. This is the endpoint the variable provider calls internally, but it can also be called directly for debugging or by services that need nature data outside the Actor behavior system.

- **ResolveNatureBatch** (`/ethology/nature/resolve-batch`): Batch resolution for multiple entities. Optimized for bulk queries (regional watcher evaluating all creatures in an area). Shares archetype and override lookups across entities of the same species in the same scope.

- **CompareNatures** (`/ethology/nature/compare`): Given two entity IDs with the same species, returns a diff of their nature profiles. Shows where individual noise creates differentiation. Useful for debugging and for behaviors that need to evaluate "who in the pack is most aggressive?"

### Cleanup Endpoints (1 endpoint)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByRealm** (`/ethology/cleanup-by-realm`): Removes all realm-scoped overrides for the deleted realm. Location-scoped overrides within the realm are also removed (location deletion events handle this independently, but cleanup ensures no orphans).

---

## Visual Aid

When a dungeon spawns a creature, the creature needs behavioral defaults. lib-ethology provides those defaults via the species archetype. The dungeon core can optionally apply its own modifications (empowered monsters, mutated behaviors) as overrides registered at the dungeon-instance level, layering dungeon aesthetics on top of species baselines.

### Nature Resolution Pipeline

```
┌──────────────────────────────────────────────────────────────────────┐
│                    NATURE RESOLUTION PIPELINE                          │
│                                                                      │
│  INPUT: entityId, speciesCode, realmId, locationId                   │
│                                                                      │
│  STEP 1: ARCHETYPE LOOKUP                                            │
│  ┌──────────────────────────────────────────────────────┐           │
│  │ Query: ethology-archetypes by speciesCode + gameId    │           │
│  │                                                        │           │
│  │ Wolf Archetype:                                        │           │
│  │   aggression:     0.70                                │           │
│  │   territoriality: 0.80                                │           │
│  │   curiosity:      0.40                                │           │
│  │   fear_threshold: 0.50                                │           │
│  │   sociality:      0.90                                │           │
│  └───────────────────────┬──────────────────────────────┘           │
│                           │                                           │
│  STEP 2: ENVIRONMENTAL OVERRIDES                                     │
│  ┌───────────────────────▼──────────────────────────────┐           │
│  │ Realm: "Ironpeak Mountains"                           │           │
│  │   aggression:     +0.15  (harsh climate)              │           │
│  │   fear_threshold: +0.10  (survival of the bold)       │           │
│  │                                                        │           │
│  │ Location: "Wolf Den Ridge"                            │           │
│  │   territoriality: +0.10  (established den)            │           │
│  │                                                        │           │
│  │ After overrides:                                       │           │
│  │   aggression:     0.85                                │           │
│  │   territoriality: 0.90                                │           │
│  │   curiosity:      0.40  (no override)                 │           │
│  │   fear_threshold: 0.60                                │           │
│  │   sociality:      0.90  (no override)                 │           │
│  └───────────────────────┬──────────────────────────────┘           │
│                           │                                           │
│  STEP 3: HERITAGE CHECK (characters only)                            │
│  ┌───────────────────────▼──────────────────────────────┐           │
│  │ Is this a character with Heritage data?                │           │
│  │                                                        │           │
│  │ YES: Heritage phenotype replaces mapped axes           │           │
│  │   aggression: 0.72 (from heritage, not archetype)     │           │
│  │   curiosity:  0.55 (from heritage)                    │           │
│  │   Other axes: keep archetype + override values        │           │
│  │   Skip noise for heritage-sourced axes                │           │
│  │                                                        │           │
│  │ NO (creature): keep all archetype + override values   │           │
│  │   Apply noise to all axes                              │           │
│  └───────────────────────┬──────────────────────────────┘           │
│                           │                                           │
│  STEP 4: INDIVIDUAL NOISE                                            │
│  ┌───────────────────────▼──────────────────────────────┐           │
│  │ For each non-heritage axis:                            │           │
│  │   noise = hash(entityId + axisCode) → [-0.10, +0.10] │           │
│  │                                                        │           │
│  │ Entity "wolf-7a3f":                                   │           │
│  │   aggression:     +0.03                               │           │
│  │   territoriality: -0.05                               │           │
│  │   curiosity:      +0.08                               │           │
│  │   fear_threshold: -0.02                               │           │
│  │   sociality:      +0.01                               │           │
│  └───────────────────────┬──────────────────────────────┘           │
│                           │                                           │
│  STEP 5: CLAMP & OUTPUT                                              │
│  ┌───────────────────────▼──────────────────────────────┐           │
│  │ Final values (clamped to [min, max]):                  │           │
│  │   aggression:     0.88                                │           │
│  │   territoriality: 0.85                                │           │
│  │   curiosity:      0.48                                │           │
│  │   fear_threshold: 0.58                                │           │
│  │   sociality:      0.91                                │           │
│  │                                                        │           │
│  │ → Stored in ethology-cache as NatureManifest           │           │
│  │ → Returned to Actor as IVariableProvider               │           │
│  │ → Available as ${nature.*} in ABML expressions         │           │
│  └──────────────────────────────────────────────────────┘           │
└──────────────────────────────────────────────────────────────────────┘
```

### Ecosystem Individuality

```
┌──────────────────────────────────────────────────────────────────────┐
│                    PACK OF WOLVES IN IRONPEAK                          │
│                                                                      │
│  Same species, same realm, same location -- different individuals.   │
│  All values from archetype (0.7) + realm override (+0.15) + noise.   │
│                                                                      │
│  Wolf A (entity-id: 7a3f...)        Wolf B (entity-id: b2c8...)      │
│  ┌───────────────────────┐         ┌───────────────────────┐        │
│  │ aggression:  0.88     │         │ aggression:  0.82     │        │
│  │ territory:   0.85     │         │ territory:   0.92     │        │
│  │ fear_thresh: 0.58     │         │ fear_thresh: 0.63     │        │
│  │ sociality:   0.91     │         │ sociality:   0.87     │        │
│  │                       │         │                       │        │
│  │ → More aggressive     │         │ → More territorial    │        │
│  │ → Less territorial    │         │ → Slightly braver     │        │
│  │ → Pack-oriented       │         │ → Still social        │        │
│  └───────────────────────┘         └───────────────────────┘        │
│                                                                      │
│  Wolf C (entity-id: 4e91...)        Wolf D (entity-id: d5a2...)      │
│  ┌───────────────────────┐         ┌───────────────────────┐        │
│  │ aggression:  0.79     │         │ aggression:  0.90     │        │
│  │ territory:   0.88     │         │ territory:   0.83     │        │
│  │ fear_thresh: 0.54     │         │ fear_thresh: 0.68     │        │
│  │ sociality:   0.93     │         │ sociality:   0.84     │        │
│  │                       │         │                       │        │
│  │ → Least aggressive    │         │ → MOST aggressive     │        │
│  │ → More pack-loyal     │         │ → Bravest of the pack │        │
│  │ → More skittish       │         │ → Less social         │        │
│  └───────────────────────┘         └───────────────────────┘        │
│                                                                      │
│  Behavior implications:                                              │
│  - Wolf D is likely the alpha (highest aggression + fear threshold)  │
│  - Wolf C is the most pack-oriented but least assertive              │
│  - Wolf A and B are mid-pack with different specializations          │
│  - All are MORE aggressive than the species base (0.7) due to the   │
│    Ironpeak realm override (+0.15). Ironpeak breeds tough wolves.   │
│  - The SAME species in Verdant Meadows would be LESS aggressive     │
│    (realm override: -0.2), creating regionally distinct populations. │
│                                                                      │
│  Zero per-entity storage. All computed from:                         │
│    archetype (MySQL, cached) + override (MySQL, cached) +            │
│    hash(entityId + axisCode) → deterministic noise                   │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no code. The following phases are planned:

### Phase 0: Prerequisites (changes to existing services)

- **Species**: Must provide species codes that archetypes reference. Already implemented and operational.
- **Actor**: Must support the `NatureProviderFactory` as a variable provider. The `IVariableProviderFactory` pattern is already implemented -- ethology registers its factory in DI, Actor discovers it automatically. No changes to Actor needed.
- **creature_base.yaml**: Existing behavior document should be updated to reference `${nature.*}` variables instead of hardcoded context defaults. This is an ABML authoring change, not a service change.

### Phase 1: Archetype Definitions

- Create `ethology-api.yaml` schema with all endpoints
- Create `ethology-events.yaml` schema
- Create `ethology-configuration.yaml` schema
- Generate service code
- Implement archetype CRUD (register, get, get-by-species, get-by-code, update, list, deprecate)
- Implement bulk seed endpoint for world initialization
- Implement species event handlers (deprecated, deleted)
- Implement resource cleanup endpoint

### Phase 2: Nature Resolution

- Implement three-layer resolution algorithm (archetype + overrides + noise)
- Implement deterministic noise function (MurmurHash3)
- Implement `NatureProviderFactory` and register as `IVariableProviderFactory`
- Implement Redis caching for resolved archetypes and entity manifests
- Implement cache invalidation on archetype/override changes
- Implement ResolveNature and ResolveNatureBatch endpoints
- Implement CompareNatures endpoint

### Phase 3: Environmental Overrides

- Implement override CRUD (create, get, update, list, deactivate, activate)
- Implement realm-scoped overrides with cache invalidation
- Implement location-scoped overrides with hierarchical stacking
- Implement location event handlers (deprecated, deleted)
- Wire cache invalidation: override changes invalidate resolved archetypes for affected scope

### Phase 4: Character Delegation

- Implement Heritage-aware resolution in `NatureProviderFactory`
- Soft-dependency on Character-Lifecycle for Heritage phenotype data
- Heritage-mapped axes use phenotype values; unmapped axes use archetype
- Skip noise for Heritage-sourced axes (Heritage provides its own individual variation)
- Graceful degradation when Character-Lifecycle unavailable

### Phase 5: Cache Warmup and Optimization

- Implement cache warmup background worker
- Optimize batch resolution for large creature populations
- Profile and tune cache TTLs for 100,000+ creature scale
- Implement per-realm archetype resolution batching

---

## Potential Extensions

1. **Seasonal behavioral modifiers**: Integration with Worldstate's season system. Wolves are more aggressive in winter (scarce food). Deer are more vigilant during fawning season. Bears are more docile in spring (abundant food). Implemented as automatic overrides keyed to `worldstate.season-changed` events, applied and removed by a background worker tracking seasonal cycles.

2. **Age-based behavioral drift**: Young creatures are more curious and less territorial. Old creatures are more cautious and less aggressive. A simple age-based modifier applied during nature resolution, using the entity's creation timestamp to estimate age. Not a full lifecycle system (that's Character-Lifecycle for characters) -- just a lightweight behavioral drift for creature variety.

3. **Pack role assignment**: For social species, the nature provider could assign pack roles based on individual noise. The wolf with the highest aggression + fear_threshold combination is the alpha. The one with highest sociality + lowest aggression is the omega. Exposed as `${nature.pack_role}`. Emergent hierarchy from noise values, not explicit assignment.

4. **Taming integration**: The `docility` axis could feed into a future taming/domestication system. A creature with high docility is easier to tame. Successfully tamed creatures get a persistent override that shifts their behavioral axes (reduced aggression, increased docility, added loyalty axis). This creates a progression arc for creature bonding.

5. **Creature mood system**: A lightweight analogue to Disposition for creatures. Recent events (successful hunt, injury, pack member death) apply temporary modifiers to nature axes. Implemented as short-TTL Redis overrides scoped to individual entities. More than static nature, less than full Disposition. Blurs the line between nature and state -- use judiciously.

6. **ABML action for override registration**: Regional watcher behaviors could register/modify overrides via ABML actions (`register_ethology_override`, `remove_ethology_override`). This lets god actors dynamically shape ecology: "Silvanus blesses this forest, reducing creature aggression" or "Typhon's corruption spreads, increasing monster aggression." Implemented as Puppetmaster-provided ABML action handlers that call Ethology API.

7. **Migration behavior from override changes**: When a realm-level override makes an area significantly more hostile, creatures with low `fear_threshold` might migrate to adjacent locations. This is a behavior-level response (ABML document checks `${nature.fear_threshold}` against perceived danger), not a service-level feature -- but the override system provides the data that makes migration decisions meaningful.

8. **Archetype inheritance**: Parent archetypes with child specializations. "Canine" defines baseline axes; "Wolf", "Dog", "Fox" inherit and override specific values. Reduces duplication across similar species. Implemented as an optional `parentArchetypeCode` field with additive modifier semantics (child values are parent + child deltas).

9. **Client events for observable behavior**: `ethology-client-events.yaml` for pushing nature observations to WebSocket clients: "This creature seems unusually aggressive," "The animals in this area are more skittish than normal." Makes the ethology system visible to players through the Gardener system without exposing raw numbers.

10. **Cross-species interaction modifiers**: Predator-prey relationships expressed as interaction overrides. When a wolf and a deer are in proximity, the deer's `fear_threshold` drops and `vigilance` rises, while the wolf's `persistence` rises. Not stored as permanent overrides but computed dynamically during behavior ticks. Extends the nature provider with context-aware resolution.

---

## Why Not Extend Species or Use Seeds?

This is not a genome service (dungeon creatures are pneuma echoes without DNA; character genetics belongs to Character-Lifecycle's Heritage Engine). This is not a personality service (personality is individual experience-driven temperament -- nurture; nature is species-defined behavioral tendency -- what you ARE by birth). This is not a seed type (seeds are progressive growth that starts empty; a wolf doesn't progressively earn being territorial, it IS territorial from the moment it exists).

The question arises: why not add behavioral defaults to Species, or model this as a Seed type?

**Species (L2) is deliberately game-agnostic.** Species stores `traitModifiers` as untyped JSON because different games implement different trait systems. Adding structured behavioral axes (aggression, territoriality, etc.) would impose game-specific semantics on a foundational service. Per the service hierarchy, L2 services remain generic; game-specific behavioral interpretation belongs at L4. Species answers "what is this type of creature?" Ethology answers "how does this type of creature behave?"

**Seeds are progressive growth, not static definitions.** Seeds start empty and accumulate growth over time via `RecordGrowthAsync`. A wolf doesn't progressively earn being territorial. It IS territorial from birth by species definition. Using seeds would require pre-loading growth domains to simulate "you were born this way" -- a hack that misuses the abstraction. Seeds answer "how much has this entity developed?" Nature answers "what IS this entity?" These are fundamentally different questions. A wolf has `${nature.territoriality} = 0.8` from the moment it exists; it doesn't need to "grow into" territoriality.

**Resource's ISeededResourceProvider is a file server, not a data system.** ISeededResourceProvider delivers read-only factory defaults as raw bytes (`byte[] Content` with MIME content types). It's designed for ABML templates and configuration files, not structured queryable data. You can't say "give me the aggression value for wolves in the Ironpeak Mountains" -- you'd get an entire YAML blob and need to parse it. No caching, no resolution hierarchy, no individual noise, no environmental overrides.

**Character-Lifecycle's Heritage Engine is character-specific by design.** Heritage handles Mendelian genetics for entities with "personality, drives, social relationships, and generational continuity." Creatures that don't breed, don't age, and don't have guardian spirits are explicitly outside Heritage's scope. Character-Lifecycle's deep dive states: "Animals, creatures, and other living entities that don't have guardian spirits, personality profiles, or generational continuity don't need this service." Ethology fills the gap Heritage intentionally leaves.

| Service | Question It Answers | Domain |
|---------|--------------------|--------------------|
| **Species** | "What type of creature is this?" | Type definitions (L2, game-agnostic) |
| **Character-Lifecycle Heritage** | "What did THIS character inherit genetically?" | Individual genetics (L4, character-specific) |
| **Seed** | "How much has this entity grown?" | Progressive capability (L2, growth-oriented) |
| **Ethology** | "How does this type of creature behave, in this environment, as this individual?" | Behavioral nature (L4, universal) |

The pipeline with Ethology:
```
Species (what type)  ──────────────────────────────────────────────────┐
Ethology (how it behaves by nature)  ──────────────────────────────────┤
Heritage (individual genetic variation, characters only)  ─────────────┤
                                                                       ▼
                                                          ACTOR BEHAVIOR SYSTEM
                                                          ${nature.*} available
                                                          for ALL entity types
                                                                       │
                        ┌──────────────────┬───────────────┬──────────┘
                        ▼                  ▼               ▼
                  Creature ABML       Character ABML   Dungeon ABML
                  (nature only)       (nature +        (nature +
                                       heritage +       dungeon
                                       personality +    genetic library)
                                       disposition)
```

Ethology provides the universal NATURE layer. Heritage refines it for characters. Personality/Disposition/History layer NURTURE on top. The result: creatures have species-appropriate individuality, characters have genetic individuality on top of that, and the same `${nature.*}` variables work in ABML expressions regardless of entity type.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Axis codes are opaque strings**: Not enums. Follows the same extensibility pattern as seed type codes, collection type codes, lifecycle stage codes, and organization type codes. `aggression`, `territoriality`, `curiosity` are Arcadia conventions. A different game defines entirely different axes. The service stores float values against string keys -- it doesn't care what the axes mean.

2. **One archetype per species per game service**: A species can have at most one behavioral archetype within a game scope. If a game needs subspecies differentiation (mountain wolf vs. forest wolf), it should define them as separate species in the Species service, not as separate archetypes for the same species. Environmental overrides handle location-based variation within a single species.

3. **Individual noise is NOT random**: Noise is deterministic from entity ID + axis code. The same entity always has the same noise. This is essential for behavioral consistency -- a wolf shouldn't be aggressive on one tick and docile on the next. If true randomness is needed for a specific mechanic, the behavior document should use ABML's random functions, not `${nature.*}`.

4. **Override stacking is additive, not multiplicative**: Realm override of +0.15 and location override of +0.10 produce a total modifier of +0.25 (additive). Multiplicative stacking would produce non-intuitive results at the extremes and make override magnitudes harder to reason about.

5. **Characters with Heritage skip noise on heritage-mapped axes**: This prevents double-individual-variation. Heritage already provides individual genetic variation per character. Adding ethology noise on top would create inconsistency between the Heritage phenotype value and the nature value for the same axis. Heritage is the authoritative source of individual variation for characters; ethology noise is only for entities without Heritage.

6. **No per-entity persistent overrides**: Individual entities do NOT get persistent overrides stored in MySQL. Individual variation comes exclusively from the deterministic noise function. If a specific creature needs a persistent behavioral change (tamed, cursed, blessed), that should be modeled as a Status effect or Seed capability, not an ethology override. Overrides are for environmental/ecological effects, not individual-entity modifications.

7. **Cache invalidation is eventually consistent**: When an archetype or override is modified, the cache is invalidated. But entities currently mid-behavior-tick will use their cached nature manifest until the next tick. At the default 60-second entity manifest TTL, behavioral changes propagate within ~1 minute. This is acceptable for ecological changes (which are gradual by nature) but means instant behavioral shifts require direct perception injection via the Actor API.

8. **Deprecated archetypes still resolve**: Deprecating an archetype prevents new entities from being warned (not blocked) about using it, but existing entities continue resolving nature values normally. An archetype is only truly inactive when deleted, which requires zero active entities referencing it. This prevents ecosystem disruption from administrative actions.

9. **Noise amplitude is per-archetype, not per-axis**: All axes in an archetype share the same noise amplitude. A wolf can't have high variation in aggression but low variation in sociality. If per-axis noise control is needed, it should be a future extension. The current design keeps the noise model simple and the archetype definition compact.

10. **Empty provider on missing archetype**: If no archetype exists for an entity's species, the nature provider returns an empty provider (all `${nature.*}` variables return null). Behavior documents must handle this gracefully with fallback defaults in their context block. This is consistent with how all other variable providers handle missing data in the Actor system.

### Design Considerations (Requires Planning)

1. **Scale of nature resolution at 100,000+ creatures**: With aggressive caching (resolved archetype TTL of 5 minutes, entity manifest TTL of 60 seconds), the actual MySQL queries are infrequent. The noise computation is pure CPU (single hash per axis) with no I/O. The bottleneck is cache memory, not computation. At 20 axes per archetype and 100,000 entities, the cache stores ~100,000 manifests of ~200 bytes each = ~20MB. Acceptable for Redis.

2. **Species code source for non-character entities**: Characters have a `speciesId` field. Actors have template metadata. But how does the nature provider know which species code to use for a given entity ID? The actor template's metadata must include a `speciesCode` field. This is an Actor/Puppetmaster configuration concern, not an Ethology concern, but it must be documented as a requirement.

3. **Heritage phenotype axis mapping**: The mapping between Heritage trait codes (e.g., `physical_strength`) and Ethology axis codes (e.g., `aggression`) needs to be data-driven. This mapping lives in the Heritage trait template's `personalityMapping` or `aptitudeMapping` fields, or in a new `natureMapping` field added to the `HeritableTraitTemplate`. Character-Lifecycle owns this mapping; Ethology consumes it.

4. **Override cleanup when locations are restructured**: If a location is deleted, reparented, or merged, its overrides need cleanup. The event handlers cover deletion, but reparenting and merging are more complex. Should overrides move with the location? Current design: location-scoped overrides are tied to a specific location ID. If the location is reparented, overrides remain (they describe the local environment, which doesn't change with administrative restructuring). If the location is merged into another, the source location's overrides are removed (deletion event).

5. **Interaction with existing CognitionDefaults**: The Actor service maps actor categories to cognition templates (`creature-cognition-base`, `humanoid-cognition-base`). This is separate from ethology -- CognitionDefaults determine the cognition pipeline (perception, appraisal, memory), while ethology provides data values. They're complementary, not competing. But the documentation should clarify the distinction: CognitionDefaults = how the brain works, Ethology = what values the brain uses.

6. **Warmup ordering**: The cache warmup worker should wait for Species data to be available (Species is L2, loads before L4 ethology). The `CacheWarmupDelaySeconds` configuration handles this, but the delay must be long enough for Species to be fully loaded and queryable. Default of 10 seconds should be sufficient for typical startup sequences.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Species (L2) is the primary prerequisite -- archetypes reference species codes that must exist. Actor's variable provider infrastructure is already operational, requiring no changes. creature_base.yaml exists and would benefit from ${nature.*} integration but is not a blocker. Phase 1 (archetype definitions) is self-contained. Phase 2 (nature resolution with noise) is self-contained. Phase 3 (environmental overrides) adds ecological richness. Phase 4 (character delegation) integrates with Character-Lifecycle when available. Phase 5 (cache optimization) tunes for production scale.*
