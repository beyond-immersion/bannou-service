# Character Personality Plugin Deep Dive

> **Plugin**: lib-character-personality
> **Schema**: schemas/character-personality-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: character-personality-statestore (MySQL)
> **Short**: Personality traits (bipolar axes) and combat preferences with probabilistic evolution
> **Implementation Map**: [docs/maps/CHARACTER-PERSONALITY.md](../maps/CHARACTER-PERSONALITY.md)

---

## Overview

Machine-readable personality traits and combat preferences (L4 GameFeatures) for NPC behavior decisions. Features probabilistic personality evolution based on character experiences and combat preference adaptation based on battle outcomes. Traits are floating-point values on bipolar axes that shift based on experience intensity. Provides `${personality.*}` and `${combat.*}` ABML variables to the Actor service via the Variable Provider Factory pattern.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `axis` (TraitAxis) | C (System State) | `TraitAxis` enum | Finite set of personality dimensions (OPENNESS, CONSCIENTIOUSNESS, EXTRAVERSION, AGREEABLENESS, NEUROTICISM, HONESTY, AGGRESSION, LOYALTY). System-owned; each axis has hardcoded evolution logic mapping experience types to trait shifts. |
| `experienceType` | C (System State) | `ExperienceType` enum | Finite set of experience categories (TRAUMA, BETRAYAL, LOSS, VICTORY, FRIENDSHIP, REDEMPTION, CORRUPTION, ENLIGHTENMENT, SACRIFICE). System-owned; each type maps to specific affected traits with signed direction weights. |
| `style` (CombatStyle) | C (System State) | `CombatStyle` enum | Finite set of combat approaches (DEFENSIVE, BALANCED, AGGRESSIVE, BERSERKER, TACTICAL). System-owned; transition logic between styles is hardcoded with probabilistic rules. |
| `preferredRange` | C (System State) | `PreferredRange` enum | Finite set of engagement distances (MELEE, CLOSE, MEDIUM, RANGED). System-owned operational mode for combat positioning. |
| `groupRole` | C (System State) | `GroupRole` enum | Finite set of group combat roles (FRONTLINE, SUPPORT, FLANKER, LEADER, SOLO). System-owned; transition logic tied to specific combat experience types. |
| `experienceType` (CombatExperienceType) | C (System State) | `CombatExperienceType` enum | Finite set of combat experience categories (DECISIVE_VICTORY, NARROW_VICTORY, DEFEAT, NEAR_DEATH, ALLY_SAVED, ALLY_LOST, SUCCESSFUL_RETREAT, FAILED_RETREAT, AMBUSH_SUCCESS, AMBUSH_SURVIVED). System-owned; each type has hardcoded preference evolution effects. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Discovers `PersonalityProviderFactory` and `CombatPreferencesProviderFactory` via `IEnumerable<IVariableProviderFactory>` DI injection for ABML expression evaluation. Providers use `IPersonalityDataCache` internally. |
| lib-resource | Receives direct API calls for reference registration; calls `/cleanup-by-character` endpoint on cascade delete |

**Note**: lib-character (L2) does NOT depend on lib-character-personality (L4) per SERVICE HIERARCHY. The enrichment query params (`includePersonality`, `includeCombatPreferences`) are logged but ignored; callers should aggregate from L4 services directly.

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

5. **Communication and information processing roles**: All eight trait axes modulate NPC information reception, inference (gap-filling), and retransmission in the Hearsay system. Traits are organized into three functional tiers: Tier 1 interpretation filters (Openness, Agreeableness, Neuroticism, Aggression), Tier 2 quality filters (Conscientiousness, Honesty), and Tier 3 distribution properties (Extraversion, Loyalty). Multi-axis personality vectors produce emergent interpretive archetypes (e.g., low Agreeableness + high Neuroticism = "paranoid" interpreter). See [Hearsay § Personality-Driven Information Processing](../plugins/HEARSAY.md#personality-driven-information-processing) and [CHARACTER-COMMUNICATION Guide § Distortion Rules](../guides/CHARACTER-COMMUNICATION.md#distortion-rules). No changes required to Character Personality itself — trait data is consumed by Hearsay via the existing `ICharacterPersonalityClient` soft dependency.
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

1. ~~**RestoreFromArchive does not publish lifecycle events**~~: **FIXED** (2026-03-16) - Added `PublishPersonalityCreatedAsync` and `PublishCombatPreferencesCreatedAsync` calls after state store writes in `RestoreFromArchiveAsync`. Events match the pattern in `SetPersonalityAsync`/`SetCombatPreferencesAsync`. Cache invalidation now occurs via the self-subscription to `personality.evolved`/`combat-preferences.evolved` (note: cache listens to `*.evolved`, not `*.created` — the created event primarily benefits downstream consumers and analytics, not the local cache).

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

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

See AUDIT markers in Potential Extensions and Design Considerations sections for items awaiting design decisions.

### Active

- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#648](https://github.com/beyond-immersion/bannou-service/issues/648).

### Completed

- **2026-03-16**: RestoreFromArchive now publishes `personality.created` and `combat-preferences.created` events per Foundation Tenets (Bugs #1)
- **2026-02-06**: Cache TTL now configurable via `CacheTtlMinutes` — verified and processed (2026-03-16)
- **2026-03-11**: Fixed inline key interpolation — verified and processed (2026-03-16)
