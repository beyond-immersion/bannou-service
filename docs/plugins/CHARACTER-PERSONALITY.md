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

## Tenet Violations (Fix Immediately)

1. **[IMPLEMENTATION TENETS - T25]** `CombatPreferencesData` internal model uses `string` for `Style`, `PreferredRange`, and `GroupRole` fields instead of the generated enum types (`CombatStyle`, `PreferredRange`, `GroupRole`). This causes `Enum.Parse` calls throughout business logic and string comparisons in `ApplyCombatEvolution`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 919-921
   - **What's wrong**: `public string Style { get; set; } = "BALANCED";`, `public string PreferredRange { get; set; } = "MEDIUM";`, `public string GroupRole { get; set; } = "FRONTLINE";`
   - **Should be**: `public CombatStyle Style { get; set; } = CombatStyle.BALANCED;`, `public PreferredRange PreferredRange { get; set; } = PreferredRange.MEDIUM;`, `public GroupRole GroupRole { get; set; } = GroupRole.FRONTLINE;`

2. **[IMPLEMENTATION TENETS - T25]** `CombatPreferencesData.CharacterId` and `PersonalityData.CharacterId` use `string` instead of `Guid`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 907, 919
   - **What's wrong**: `public string CharacterId { get; set; } = string.Empty;`
   - **Should be**: `public Guid CharacterId { get; set; }`

3. **[IMPLEMENTATION TENETS - T25]** `.ToString()` used to populate internal model fields from enums. `SetCombatPreferencesAsync` converts enum values to strings when creating `CombatPreferencesData`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 453-456
   - **What's wrong**: `CharacterId = body.CharacterId.ToString()`, `Style = body.Preferences.Style.ToString()`, `PreferredRange = body.Preferences.PreferredRange.ToString()`, `GroupRole = body.Preferences.GroupRole.ToString()`
   - **Should be**: Direct enum assignment once internal model uses proper types: `CharacterId = body.CharacterId`, `Style = body.Preferences.Style`, etc.

4. **[IMPLEMENTATION TENETS - T25]** `Enum.Parse` used in business logic (`MapToCombatPreferences`, `MapToPersonalityResponse`). These are not system boundary conversions - they convert internal model strings back to enums on every read.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 828-831, 860-862
   - **What's wrong**: `Guid.Parse(data.CharacterId)`, `Enum.Parse<TraitAxis>(t.Key)`, `Enum.Parse<CombatStyle>(data.Style)`, `Enum.Parse<PreferredRange>(data.PreferredRange)`, `Enum.Parse<GroupRole>(data.GroupRole)`
   - **Should be**: Once internal models use proper types, no parsing is needed - direct field access

5. **[IMPLEMENTATION TENETS - T25]** String comparisons for enum values throughout `ApplyCombatEvolution`. The method compares `data.Style` and `data.GroupRole` as strings instead of using enum equality.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 742-744, 757-760, 767-768, 773, 796-799, 808-810
   - **What's wrong**: `data.Style == "DEFENSIVE"`, `data.Style == "BALANCED"`, `data.Style == "AGGRESSIVE"`, `data.Style == "BERSERKER"`, `data.GroupRole == "SOLO"`, `data.GroupRole != "LEADER"`, etc.
   - **Should be**: `data.Style == CombatStyle.DEFENSIVE`, `data.GroupRole == GroupRole.SOLO`, etc.

6. **[IMPLEMENTATION TENETS - T25]** String assignment to enum fields in `ApplyCombatEvolution`. Style and GroupRole are assigned string literals instead of enum values.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 743-744, 758-760, 768, 774, 799, 809-810
   - **What's wrong**: `data.Style = "BALANCED"`, `data.Style = "AGGRESSIVE"`, `data.Style = "DEFENSIVE"`, `data.GroupRole = "SUPPORT"`, `data.GroupRole = "FLANKER"`, `data.Style = "TACTICAL"`
   - **Should be**: `data.Style = CombatStyle.BALANCED`, `data.GroupRole = GroupRole.SUPPORT`, etc.

7. **[QUALITY TENETS - T0]** Source code comments reference specific tenet numbers. Three comments use "(TENET 5)" which violates the rule against referencing tenet numbers in source code.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 125, 368, 469
   - **What's wrong**: `// Publish event using typed events per IMPLEMENTATION TENETS (TENET 5)`
   - **Should be**: `// Publish event using typed events per IMPLEMENTATION TENETS` (remove the parenthetical tenet number)

8. **[QUALITY TENETS - T10]** Operation entry logging uses `LogInformation` instead of `LogDebug`. Per T10, "Operation Entry" should be at Debug level, not Information. All CRUD entry points log at Information level.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 66, 103, 175, 293, 353, 405, 442, 518, 566
   - **What's wrong**: `_logger.LogInformation("Getting personality for character {CharacterId}", ...)` etc.
   - **Should be**: `_logger.LogDebug("Getting personality for character {CharacterId}", ...)` - use Debug for entry, Information only for significant state changes (the creation/update/delete confirmations after successful operations are correctly at Information)

9. **[IMPLEMENTATION TENETS - T21]** Hardcoded magic number `100` for batch limit. The batch size limit should be a configuration property.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, line 297
   - **What's wrong**: `if (body.CharacterIds.Count > 100)`
   - **Should be**: `if (body.CharacterIds.Count > _configuration.MaxBatchSize)` with `MaxBatchSize` defined in configuration schema

10. **[IMPLEMENTATION TENETS - T21]** Hardcoded magic number `3` for optimistic concurrency retry count. The retry limit should be a configuration property.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 208, 596
    - **What's wrong**: `for (var attempt = 0; attempt < 3; attempt++)`
    - **Should be**: `for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)` with the property defined in configuration schema

11. **[IMPLEMENTATION TENETS - T21]** Hardcoded combat evolution probability thresholds (0.3, 0.2, 0.4, 0.5, 1.5, 0.5, 0.3) for style/role transition probabilities and shift multipliers. All of these are tunable values that should be configuration properties.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 742, 744, 750, 755, 757, 759, 764-765, 767, 773, 779, 791, 796, 808
    - **What's wrong**: Magic numbers like `Random.Shared.NextDouble() < 0.3`, `shift * 0.5f`, `shift * 1.5f`, `shift * 0.3f`
    - **Should be**: Configuration properties for style transition probabilities and shift multipliers

12. **[IMPLEMENTATION TENETS - T25]** `PersonalityData.Traits` uses `Dictionary<string, float>` with string keys representing trait axes. The keys are TraitAxis enum values stored as strings, requiring `Enum.Parse<TraitAxis>` on read and `.ToString()` on write.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, line 908
    - **What's wrong**: `public Dictionary<string, float> Traits { get; set; } = new();`
    - **Should be**: `public Dictionary<TraitAxis, float> Traits { get; set; } = new();`

13. **[IMPLEMENTATION TENETS - T25]** `SetPersonalityAsync` converts `TraitAxis` enum to string when populating traits dictionary.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, line 115
    - **What's wrong**: `Traits = body.Traits.ToDictionary(t => t.Axis.ToString(), t => t.Value)`
    - **Should be**: `Traits = body.Traits.ToDictionary(t => t.Axis, t => t.Value)` (once dictionary uses TraitAxis keys)

14. **[IMPLEMENTATION TENETS - T25]** `GetAffectedTraits` returns `Dictionary<string, float>` with string keys for trait names instead of using `TraitAxis` enum keys.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 669-728
    - **What's wrong**: `private static Dictionary<string, float> GetAffectedTraits(...)` with entries like `{ "NEUROTICISM", 0.5f }`
    - **Should be**: `private static Dictionary<TraitAxis, float> GetAffectedTraits(...)` with entries like `{ TraitAxis.NEUROTICISM, 0.5f }`

15. **[FOUNDATION TENETS - T6]** Constructor does not perform null checks on injected dependencies. Per the standardized service pattern, each dependency should be validated with `?? throw new ArgumentNullException(nameof(...))`.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 49-52
    - **What's wrong**: `_logger = logger;` (no null check), `_configuration = configuration;`, `_stateStoreFactory = stateStoreFactory;`, `_messageBus = messageBus;`
    - **Should be**: `_logger = logger ?? throw new ArgumentNullException(nameof(logger));` etc.

16. **[IMPLEMENTATION TENETS - T7]** All catch blocks catch only `Exception` without distinguishing `ApiException`. Per T7, external calls should catch `ApiException` specifically to propagate status codes, then catch generic `Exception` for unexpected errors.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, lines 81, 152, 270, 331, 379, 420, 496, 545, 644
    - **What's wrong**: Only `catch (Exception ex)` is used
    - **Should be**: Add `catch (ApiException ex)` before generic Exception catch, log as warning, and return `(StatusCodes)ex.StatusCode`

17. **[QUALITY TENETS - Duplicate Assembly Attribute]** `InternalsVisibleTo("lib-character-personality.tests")` is declared twice - once in `AssemblyInfo.cs` and once at the top of `CharacterPersonalityService.cs`.
    - **Files**: `/home/lysander/repos/bannou/plugins/lib-character-personality/AssemblyInfo.cs` line 5, `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs` line 9
    - **What's wrong**: Duplicate `[assembly: InternalsVisibleTo("lib-character-personality.tests")]`
    - **Should be**: Remove the duplicate from `CharacterPersonalityService.cs` line 9 (keep it only in `AssemblyInfo.cs`)

18. **[IMPLEMENTATION TENETS - T25]** `SetPersonalityAsync` converts `body.CharacterId` (a Guid) to string using `.ToString()` to store in `PersonalityData.CharacterId`.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-character-personality/CharacterPersonalityService.cs`, line 114
    - **What's wrong**: `CharacterId = body.CharacterId.ToString()`
    - **Should be**: `CharacterId = body.CharacterId` (once internal model uses `Guid`)

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified beyond the tenet violations listed above.

### Intentional Quirks (Documented Behavior)

1. **Eight trait axes (not Big Five)**: Extends the psychological Big Five with Honesty, Aggression, and Loyalty for game-relevant behavioral dimensions. All on bipolar [-1.0, +1.0] scales.

2. **Direction weights are multipliers, not absolutes**: Experience type mappings use signed floats (e.g., -0.5, +0.3) that multiply the calculated shift magnitude. They represent relative importance, not actual shift amounts.

3. **Evolution uses `Random.Shared`**: Non-deterministic random number generation. No seed control possible for testing or replays. Each trait check and combat style transition uses independent random calls.

4. **UpdatedAt null if never updated**: `MapToPersonalityResponse` returns `UpdatedAt = null` when `CreatedAtUnix == UpdatedAtUnix`. First creation doesn't count as an "update".

5. **Batch get is sequential**: `BatchGetPersonalitiesAsync` iterates character IDs sequentially (not `Task.WhenAll`). Acceptable with 100-character limit but O(n) latency.

6. **Enum string storage in internal models**: `CombatPreferencesData` stores Style, PreferredRange, GroupRole as strings. Requires `Enum.Parse<T>()` on every read. Enables schema evolution but loses compile-time type safety.

7. **ALLY_LOST 50/50 random outcome**: When an ally is lost in combat, `ProtectAllies` is set to true OR false with equal probability. Models conflicting emotional responses (determination vs. self-preservation). Not influenced by current trait values or intensity.

8. **Evolution silently skips missing traits**: If a character's personality doesn't include a trait axis (e.g., no NEUROTICISM key), evolution operations that would modify it are silently skipped (line 210: `TryGetValue` returns false). Traits are NOT created by evolution—only pre-existing traits evolve.

9. **AMBUSH_SUCCESS DEFENSIVE→TACTICAL is guaranteed (100%)**: Unlike other style transitions that use probability checks (0.2-0.5), line 771 shows `data.Style = data.Style == "DEFENSIVE" ? "TACTICAL" : data.Style;` — a deterministic, unconditional transition.

10. **FAILED_RETREAT can downgrade BERSERKER to AGGRESSIVE**: The 50% "fight instead of flee" branch (line 760) directly sets `Style = "AGGRESSIVE"` regardless of current style. A BERSERKER becomes AGGRESSIVE, which is technically a de-escalation.

11. **BERSERKER only exits via DEFEAT**: The only code path leaving BERSERKER style is lines 720-721: `if (data.Style == "BERSERKER" && Random.Shared.NextDouble() < 0.4) data.Style = "AGGRESSIVE";`. No other experience type offers an exit.

12. **Combat preferences clamp to [0, 1], personality traits to [-1, +1]**: RiskTolerance/RetreatThreshold use `Math.Clamp(..., 0, 1)` (lines 701, 711, etc.) while personality traits use `Math.Clamp(..., -1.0f, 1.0f)` (line 213). Different semantic ranges for different data types.

13. **Combat shift multipliers are hardcoded per experience type**: NEAR_DEATH uses `shift * 1.5f` (maximum impact), NARROW_VICTORY uses `shift * 0.5f`, SUCCESSFUL_RETREAT uses `shift * 0.3f`. These multipliers can't be configured.

14. **Default combat preferences in internal model**: `CombatPreferencesData` (lines 881-886) has hardcoded field defaults: Style="BALANCED", PreferredRange="MEDIUM", GroupRole="FRONTLINE", RiskTolerance=0.5f, RetreatThreshold=0.3f, ProtectAllies=true. Used during JSON deserialization when fields are missing.

### Design Considerations (Requires Planning)

1. **Hardcoded combat RNG probabilities**: Style/role transition probabilities (0.2, 0.3, 0.4, 0.5) are hardcoded in switch cases. Not configurable. Would require schema extension to make tunable.

2. **Combat style transitions are limited**: Only specific transitions exist (DEFENSIVE->BALANCED->AGGRESSIVE->BERSERKER). TACTICAL is only reachable via ambush success. No general transition matrix.

3. **Trait direction weights embedded in code**: The experience-type-to-trait mapping table is hardcoded in a switch statement. Adding new experience types or changing weights requires code changes, not configuration.

4. **Actor service caches personalities**: `PersonalityCache` in lib-actor returns stale data if the personality client fails. If personality evolves while cached, the actor uses outdated traits until cache TTL expires.
