# Character Personality Plugin Deep Dive

> **Plugin**: lib-character-personality
> **Schema**: schemas/character-personality-api.yaml
> **Version**: 1.0.0
> **State Store**: character-personality-statestore (MySQL)

---

## Overview

Machine-readable personality traits and combat preferences for NPC behavior decisions. Features probabilistic personality evolution based on character experiences (trauma, victory, corruption, etc.) and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes (e.g., -1.0 pacifist to +1.0 confrontational) that shift probabilistically based on experience intensity. Used by the Actor service's behavior system for decision-making.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for personality and combat preference data |
| lib-messaging (`IMessageBus`) | Publishing personality/combat lifecycle and evolution events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (`PersonalityCache`) | Caches personalities with configurable TTL; loads via `ICharacterPersonalityClient` on miss; returns stale data if load fails |
| lib-character | Fetches personality/combat prefs for enriched character response; deletes on character compression |

---

## State Storage

**Store**: `character-personality-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `personality-{characterId}` | `PersonalityData` | Trait axes with float values, version, timestamps |
| `combat-{characterId}` | `CombatPreferencesData` | Combat style, range, role, risk/retreat thresholds |

Both types share the same state store, distinguished by key prefix.

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `personality.created` | New personality created via Set |
| `personality.updated` | Existing personality modified via Set |
| `personality.evolved` | Experience causes probabilistic trait evolution |
| `personality.deleted` | Personality removed |
| `combat-preferences.created` | New combat preferences created via Set |
| `combat-preferences.updated` | Existing combat preferences modified via Set |
| `combat-preferences.evolved` | Combat experience causes preference evolution |
| `combat-preferences.deleted` | Combat preferences removed |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `BaseEvolutionProbability` | `CHARACTER_PERSONALITY_BASE_EVOLUTION_PROBABILITY` | `0.15` | Base chance for trait shift per evolution event |
| `MaxTraitShift` | `CHARACTER_PERSONALITY_MAX_TRAIT_SHIFT` | `0.1` | Maximum magnitude of trait change |
| `MinTraitShift` | `CHARACTER_PERSONALITY_MIN_TRAIT_SHIFT` | `0.02` | Minimum magnitude of trait change |

All three properties are actively referenced in both personality and combat evolution methods.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterPersonalityService>` | Scoped | Structured logging |
| `CharacterPersonalityServiceConfiguration` | Singleton | Evolution constants |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Personality CRUD

- **Get** (`/character-personality/get`): Simple key lookup. Returns NotFound if no personality exists.
- **Set** (`/character-personality/set`): Create-or-update with version tracking. Traits stored as `Dictionary<string, float>` keyed by axis name. Publishes created or updated event based on whether record existed.
- **BatchGet** (`/character-personality/batch-get`): Sequential iteration (not parallel) with 100-character limit. Returns partial results with notFound IDs list.
- **Delete** (`/character-personality/delete`): Simple removal with deletion event.

### Personality Evolution (`/character-personality/evolve`)

Probabilistic trait evolution based on experience type and intensity:

1. **Evolution probability**: `P = BaseEvolutionProbability * intensity` (max 15% at full intensity)
2. **Trait selection**: Experience type maps to 2-3 affected traits with signed direction weights
3. **Shift magnitude**: `shift = (MinTraitShift + (MaxTraitShift - MinTraitShift) * intensity) * direction`
4. **Clamping**: Result clamped to [-1.0, +1.0]

**Experience Type Effects**:

| Type | Affected Traits | Effect |
|------|----------------|--------|
| TRAUMA | Neuroticism+, Openness-, Extraversion- | Closes off, increases anxiety |
| BETRAYAL | Agreeableness-, Honesty-, Loyalty+ | Damages trust, deepens bonds |
| LOSS | Neuroticism+, Conscientiousness+ | Emotional sensitivity, duty |
| VICTORY | Extraversion+, Aggression+, Neuroticism- | Confidence boost |
| FRIENDSHIP | Agreeableness+, Extraversion+, Loyalty+ | Most beneficial |
| REDEMPTION | Honesty+, Conscientiousness+, Neuroticism- | Personal growth |
| CORRUPTION | Honesty-, Agreeableness-, Aggression+ | Moral decay |
| ENLIGHTENMENT | Openness+, Conscientiousness+, Neuroticism- | Maximum positive |
| SACRIFICE | Loyalty+, Conscientiousness+, Agreeableness+ | Heroic action |

### Combat CRUD

- **GetCombat** (`/character-personality/get-combat`): Returns combat preferences (style, range, role, risk/retreat thresholds).
- **SetCombat** (`/character-personality/set-combat`): Create-or-update. Enums stored as strings internally.
- **DeleteCombat** (`/character-personality/delete-combat`): Simple removal.

### Combat Evolution (`/character-personality/evolve-combat`)

Same probability formula as personality evolution. Combat experience types trigger style/role transitions and float adjustments:

| Type | Effects |
|------|---------|
| DECISIVE_VICTORY | RiskTolerance+, 30% DEFENSIVE->BALANCED, 20% BALANCED->AGGRESSIVE |
| NARROW_VICTORY | RiskTolerance + shift*0.5 |
| DEFEAT | RiskTolerance-, RetreatThreshold+, 30% AGGRESSIVE->BALANCED |
| NEAR_DEATH | RetreatThreshold+1.5x, RiskTolerance-1.5x, 50% any->DEFENSIVE |
| ALLY_SAVED | ProtectAllies=true, 40% SOLO->SUPPORT |
| ALLY_LOST | 50% random: ProtectAllies=true OR false |
| SUCCESSFUL_RETREAT | RetreatThreshold + shift*0.3 |
| FAILED_RETREAT | 50%: fight harder OR more cautious |
| AMBUSH_SUCCESS | 30% ->FLANKER, DEFENSIVE->TACTICAL |
| AMBUSH_SURVIVED | RiskTolerance- |

---

## Visual Aid

```
Personality Evolution Pipeline
================================

  RecordExperience(type=BETRAYAL, intensity=0.7)
       │
       ▼
  ┌─────────────────────────────────────┐
  │ Step 1: Roll for evolution          │
  │   P = 0.15 * 0.7 = 0.105 (10.5%)   │
  │   roll = Random.Shared.NextDouble() │
  │   evolved = roll < 0.105            │
  └──────────────┬──────────────────────┘
                 │ (if evolved)
                 ▼
  ┌─────────────────────────────────────┐
  │ Step 2: Get affected traits         │
  │   BETRAYAL →                        │
  │     AGREEABLENESS: -0.5 direction   │
  │     HONESTY: -0.3 direction         │
  │     LOYALTY: +0.2 direction         │
  └──────────────┬──────────────────────┘
                 │
                 ▼
  ┌─────────────────────────────────────┐
  │ Step 3: Calculate shift per trait   │
  │   shift = (0.02 + 0.08*0.7) * dir  │
  │   shift = 0.076 * direction         │
  │                                     │
  │   AGREEABLENESS: -0.076 * 0.5 = -0.038│
  │   HONESTY: -0.076 * 0.3 = -0.023   │
  │   LOYALTY: +0.076 * 0.2 = +0.015   │
  └──────────────┬──────────────────────┘
                 │
                 ▼
  ┌─────────────────────────────────────┐
  │ Step 4: Apply + clamp [-1.0, +1.0] │
  │   newValue = Clamp(current + shift) │
  └──────────────┬──────────────────────┘
                 │
                 ▼
  Publish personality.evolved event
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **Trait decay**: Gradual regression toward neutral (0.0) over time without reinforcing experiences.
2. **Personality archetypes**: Pre-defined trait combinations for quick character creation.
3. **Cross-trait interactions**: Evolution in one trait influences related traits (e.g., high aggression reduces agreeableness ceiling).
4. **Combat style transitions**: Currently limited paths (no TACTICAL reversion). Could add full transition matrix.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Eight trait axes (not Big Five)**: Extends the psychological Big Five with Honesty, Aggression, and Loyalty for game-relevant behavioral dimensions. All on bipolar [-1.0, +1.0] scales.

2. **Direction weights are multipliers, not absolutes**: Experience type mappings use signed floats (e.g., -0.5, +0.3) that multiply the calculated shift magnitude. They represent relative importance, not actual shift amounts.

3. **Evolution uses `Random.Shared`**: Non-deterministic random number generation. No seed control possible for testing or replays. Each trait check and combat style transition uses independent random calls.

4. **UpdatedAt null if never updated**: `MapToPersonalityResponse` returns `UpdatedAt = null` when `CreatedAtUnix == UpdatedAtUnix`. First creation doesn't count as an "update".

5. **Batch get is sequential**: `BatchGetPersonalitiesAsync` iterates character IDs sequentially (not `Task.WhenAll`). Acceptable with 100-character limit but O(n) latency.

6. **Enum string storage in internal models**: `CombatPreferencesData` stores Style, PreferredRange, GroupRole as strings. Requires `Enum.Parse<T>()` on every read. Enables schema evolution but loses compile-time type safety.

7. **ALLY_LOST 50/50 random outcome**: When an ally is lost in combat, `ProtectAllies` is set to true OR false with equal probability. Models conflicting emotional responses (determination vs. self-preservation). Not influenced by current trait values or intensity.

### Design Considerations (Requires Planning)

1. **Hardcoded combat RNG probabilities**: Style/role transition probabilities (0.2, 0.3, 0.4, 0.5) are hardcoded in switch cases. Not configurable. Would require schema extension to make tunable.

2. **Combat style transitions are limited**: Only specific transitions exist (DEFENSIVE->BALANCED->AGGRESSIVE->BERSERKER). TACTICAL is only reachable via ambush success. No general transition matrix.

3. **No concurrency on evolution**: Two simultaneous `RecordExperience` calls could both read version N, both evolve independently, and last-writer-wins at version N+1. One evolution is silently lost.

4. **Trait direction weights embedded in code**: The experience-type-to-trait mapping table is hardcoded in a switch statement. Adding new experience types or changing weights requires code changes, not configuration.

5. **Actor service caches personalities**: `PersonalityCache` in lib-actor returns stale data if the personality client fails. If personality evolves while cached, the actor uses outdated traits until cache TTL expires.
