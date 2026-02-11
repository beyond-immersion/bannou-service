# Character Personality Plugin Deep Dive

> **Plugin**: lib-character-personality
> **Schema**: schemas/character-personality-api.yaml
> **Version**: 1.0.0
> **State Store**: character-personality-statestore (MySQL)

---

## Overview

Machine-readable personality traits and combat preferences (L4 GameFeatures) for NPC behavior decisions. Features probabilistic personality evolution based on character experiences and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes that shift based on experience intensity. Provides `${personality.*}` and `${combat.*}` ABML variables to the Actor service via the Variable Provider Factory pattern.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for personality and combat preference data |
| lib-messaging (`IMessageBus`) | Publishing personality/combat lifecycle events and evolution events |
| lib-messaging (`IEventConsumer`) | Event handler registration for cache invalidation (personality.evolved, combat-preferences.evolved) |
| lib-resource (`IResourceClient`) | Calls `RegisterReferenceAsync`/`UnregisterReferenceAsync` for character reference tracking |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Discovers `PersonalityProviderFactory` and `CombatPreferencesProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection for ABML expression evaluation. Providers use `IPersonalityDataCache` internally. |
| lib-resource | Receives direct API calls for reference registration; calls `/cleanup-by-character` endpoint on cascade delete |

**Note**: lib-character (L2) does NOT depend on lib-character-personality (L4) per SERVICE_HIERARCHY. The enrichment query params (`includePersonality`, `includeCombatPreferences`) are logged but ignored; callers should aggregate from L4 services directly.

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

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `personality.created` | `PersonalityCreatedEvent` | New personality created via Set |
| `personality.updated` | `PersonalityUpdatedEvent` | Existing personality modified via Set |
| `personality.evolved` | `PersonalityEvolvedEvent` | Experience causes probabilistic trait evolution |
| `personality.deleted` | `PersonalityDeletedEvent` | Personality removed (via Delete or CleanupByCharacter) |
| `combat-preferences.created` | `CombatPreferencesCreatedEvent` | New combat preferences created via Set |
| `combat-preferences.updated` | `CombatPreferencesUpdatedEvent` | Existing combat preferences modified via Set |
| `combat-preferences.evolved` | `CombatPreferencesEvolvedEvent` | Combat experience causes preference evolution |
| `combat-preferences.deleted` | `CombatPreferencesDeletedEvent` | Combat preferences removed (via Delete or CleanupByCharacter) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `personality.evolved` | `HandlePersonalityEvolvedAsync` | Invalidates `IPersonalityDataCache` for affected character (cache lives in this service) |
| `combat-preferences.evolved` | `HandleCombatPreferencesEvolvedAsync` | Invalidates `IPersonalityDataCache` for affected character |

**Note**: The service subscribes to its own events for internal cache invalidation. This ensures running actors using the `IVariableProviderFactory` instances get fresh data on their next behavior tick.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CacheTtlMinutes` | `CHARACTER_PERSONALITY_CACHE_TTL_MINUTES` | `5` | TTL in minutes for personality and combat preferences cache entries |
| `BaseEvolutionProbability` | `CHARACTER_PERSONALITY_BASE_EVOLUTION_PROBABILITY` | `0.15` | Base chance for trait shift per evolution event |
| `MaxTraitShift` | `CHARACTER_PERSONALITY_MAX_TRAIT_SHIFT` | `0.1` | Maximum magnitude of trait change |
| `MinTraitShift` | `CHARACTER_PERSONALITY_MIN_TRAIT_SHIFT` | `0.02` | Minimum magnitude of trait change |
| `MaxBatchSize` | `CHARACTER_PERSONALITY_MAX_BATCH_SIZE` | `100` | Maximum characters allowed in batch operations |
| `MaxConcurrencyRetries` | `CHARACTER_PERSONALITY_MAX_CONCURRENCY_RETRIES` | `3` | Retry attempts for optimistic concurrency conflicts |
| `CombatStyleTransitionProbability` | `CHARACTER_PERSONALITY_COMBAT_STYLE_TRANSITION_PROBABILITY` | `0.3` | Base probability for combat style transitions |
| `CombatDefeatStyleTransitionProbability` | `CHARACTER_PERSONALITY_COMBAT_DEFEAT_STYLE_TRANSITION_PROBABILITY` | `0.4` | Probability for style transitions after defeat |
| `CombatVictoryBalancedTransitionProbability` | `CHARACTER_PERSONALITY_COMBAT_VICTORY_BALANCED_TRANSITION_PROBABILITY` | `0.2` | Probability for balanced->aggressive after victory |
| `CombatRoleTransitionProbability` | `CHARACTER_PERSONALITY_COMBAT_ROLE_TRANSITION_PROBABILITY` | `0.4` | Base probability for combat role transitions |
| `CombatDefensiveShiftProbability` | `CHARACTER_PERSONALITY_COMBAT_DEFENSIVE_SHIFT_PROBABILITY` | `0.5` | Probability for defensive shift after injury |
| `CombatIntenseShiftMultiplier` | `CHARACTER_PERSONALITY_COMBAT_INTENSE_SHIFT_MULTIPLIER` | `1.5` | Multiplier for intense stat shifts (near-death) |
| `CombatMildShiftMultiplier` | `CHARACTER_PERSONALITY_COMBAT_MILD_SHIFT_MULTIPLIER` | `0.5` | Multiplier for mild stat shifts (standard) |
| `CombatMildestShiftMultiplier` | `CHARACTER_PERSONALITY_COMBAT_MILDEST_SHIFT_MULTIPLIER` | `0.3` | Multiplier for mildest stat shifts (minor) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterPersonalityService>` | Scoped | Structured logging |
| `CharacterPersonalityServiceConfiguration` | Singleton | Evolution constants and batch limits |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event handler registration (personality.evolved, combat-preferences.evolved) |
| `IPersonalityDataCache` | Singleton | Internal cache for personality and combat preferences data with TTL |

**Additional DI Registrations** (via Plugin):
| Service | Lifetime | Role |
|---------|----------|------|
| `PersonalityProviderFactory` | Singleton | `IVariableProviderFactory` for `${personality.*}` ABML expressions |
| `CombatPreferencesProviderFactory` | Singleton | `IVariableProviderFactory` for `${combat.*}` ABML expressions |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Personality CRUD

- **Get** (`/character-personality/get`): Simple key lookup. Returns NotFound if no personality exists.
- **Set** (`/character-personality/set`): Create-or-update with version tracking. Traits stored as `Dictionary<TraitAxis, float>` keyed by enum. Publishes created or updated event based on whether record existed.
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
- **SetCombat** (`/character-personality/set-combat`): Create-or-update. Uses proper enum types internally (`CombatStyle`, `PreferredRange`, `GroupRole`).
- **DeleteCombat** (`/character-personality/delete-combat`): Simple removal.

### Resource Cleanup

- **CleanupByCharacter** (`/character-personality/cleanup-by-character`): Called by lib-resource during cascading cleanup when a character is deleted. Removes BOTH personality traits AND combat preferences for the character. Returns `CleanupByCharacterResponse` indicating what was deleted. Does not return 404 if data doesn't exist—reports success with `personalityDeleted=false`/`combatPreferencesDeleted=false`.

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

### Compression Support

- **GetCompressData** (`/character-personality/get-compress-data`): Called by Resource service during hierarchical character compression. Returns `PersonalityCompressData` containing:
  - `hasPersonality`: Whether personality traits exist
  - `personality`: Full `PersonalityResponse` if exists
  - `hasCombatPreferences`: Whether combat preferences exist
  - `combatPreferences`: Full `CombatPreferencesResponse` if exists

  Returns NotFound only if BOTH personality and combat preferences are absent.

- **RestoreFromArchive** (`/character-personality/restore-from-archive`): Called by Resource service during decompression. Accepts Base64-encoded GZip JSON of `PersonalityCompressData`. Restores personality and combat preferences to state store if they don't already exist (idempotent).

**Compression Callback Registration** (in OnRunningAsync):
```csharp
await resourceClient.DefineCompressCallbackAsync(
    new DefineCompressCallbackRequest
    {
        ResourceType = "character",
        SourceType = "character-personality",
        CompressEndpoint = "/character-personality/get-compress-data",
        CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
        DecompressEndpoint = "/character-personality/restore-from-archive",
        DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
        Priority = 10  // After character base data (priority 0)
    }, ct);
```

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
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/201 -->
2. **Pre-defined archetype templates**: Template system that maps archetype codes (e.g., "guardian", "trickster") to pre-configured trait combinations for quick character creation. The `archetypeHint` field exists and is persisted, but no template system interprets it.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/256 -->
3. **Cross-trait interactions**: Evolution in one trait influences related traits (e.g., high aggression reduces agreeableness ceiling).
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/262 -->
4. **Combat style transitions**: Currently limited paths (no TACTICAL reversion). Could add full transition matrix.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/264 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None.

### Intentional Quirks

1. **Evolution uses `Random.Shared`**: Non-deterministic random number generation. No seed control possible for testing or replays. Each trait check and combat style transition uses independent random calls.

2. **ALLY_LOST 50/50 random outcome**: When an ally is lost in combat, `ProtectAllies` is set to true OR false with equal probability. Not influenced by current trait values or intensity—pure coin flip.

3. **Evolution silently skips missing traits**: If a character's personality doesn't include a trait axis (e.g., no NEUROTICISM key), evolution operations that would modify it are silently skipped. Traits are NOT created by evolution—only pre-existing traits evolve.

4. **AMBUSH_SUCCESS DEFENSIVE→TACTICAL is unconditional**: When `Style == DEFENSIVE`, the ambush success handler unconditionally sets `Style = TACTICAL` via ternary assignment (no probability roll). Other style changes roll against config probabilities, but this one is deterministic.

5. **FAILED_RETREAT can downgrade BERSERKER to AGGRESSIVE**: The 50% "fight instead of flee" branch directly sets `Style = "AGGRESSIVE"` regardless of current style, including BERSERKER (technically a de-escalation).

6. **BERSERKER only exits via DEFEAT**: The only code path leaving BERSERKER style is the 40% chance on DEFEAT experience. No other experience type offers an exit.

7. **Cache invalidation is event-driven with stale fallback**: `PersonalityDataCache` (in this service) subscribes to `personality.evolved` and `combat-preferences.evolved` events and invalidates entries immediately. If the service is unavailable when reloading after invalidation or cache miss, the cache returns stale data if available (graceful degradation). Actors using `IVariableProviderFactory` instances continue running with last-known personality rather than failing.

### Design Considerations (Requires Planning)

1. **Combat style transitions are limited**: Only specific transitions exist (DEFENSIVE->BALANCED->AGGRESSIVE->BERSERKER). TACTICAL is only reachable via ambush success. No general transition matrix.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/264 -->

2. **Trait direction weights embedded in code**: The experience-type-to-trait mapping table is hardcoded in a switch statement. Adding new experience types or changing weights requires code changes, not configuration.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/265 -->

3. **Combat style transitions are asymmetric**: Some styles (BERSERKER) have very few exit paths (only DEFEAT with 40% chance), while others (BALANCED) can transition in multiple directions. This may create style "traps" where characters get stuck in certain combat modes.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/264 -->

4. ~~**Cache TTL hardcoded**~~: **FIXED** (2026-02-06) - Added `CacheTtlMinutes` configuration property (default 5) to control cache TTL. `PersonalityDataCache` now injects `CharacterPersonalityServiceConfiguration` and uses the configured value.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

See AUDIT markers in Potential Extensions and Design Considerations sections for items awaiting design decisions.

### Completed

- **2026-02-06**: Cache TTL now configurable via `CacheTtlMinutes` (Design Considerations #4)
