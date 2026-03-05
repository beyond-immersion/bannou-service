# Quest Plugin Deep Dive

> **Plugin**: lib-quest
> **Schema**: schemas/quest-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: quest-definition-statestore (MySQL), quest-instance-statestore (MySQL), quest-objective-progress (Redis), quest-definition-cache (Redis), quest-character-index (Redis), quest-cooldown (Redis)
> **Implementation Map**: [docs/maps/QUEST.md](../maps/QUEST.md)

---

## Overview

The Quest service (L2 GameFoundation) provides objective-based gameplay progression as a thin orchestration layer over lib-contract. Translates game-flavored quest semantics (objectives, rewards, quest givers) into Contract infrastructure (milestones, prebound APIs, parties), leveraging Contract's state machine and cleanup orchestration while presenting a player-friendly API. Agnostic to prerequisite sources: L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` for validation without Quest depending on them. Exposes quest data to the Actor service via the Variable Provider Factory pattern for ABML behavior expressions.

**Deprecation Lifecycle (T31 Category B)**: Quest definitions are Category B entities — instances persist independently, so definitions must remain readable forever. Deprecation is one-way (no undeprecate), there is no delete endpoint, and acceptance of deprecated definitions is rejected. Contract templates are structurally immutable once created (trust guarantee); only quest metadata (name, description, category, difficulty, tags) can be updated.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Consumes `IVariableProviderFactory` implementation (QuestProviderFactory) to provide `${quest.*}` variables for ABML behavior expressions |
| lib-analytics | Subscribes to `quest.completed`, `quest.failed` events for statistics aggregation |
| lib-achievement | Subscribes to `quest.completed` events for achievement unlock triggers |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxActiveQuestsPerCharacter` | `QUEST_MAX_ACTIVE_QUESTS_PER_CHARACTER` | 25 | Maximum concurrent active quests per character |
| `DefaultRewardContainerMaxSlots` | `QUEST_DEFAULT_REWARD_CONTAINER_MAX_SLOTS` | 100 | Maximum slots in quest reward inventory containers |
| `DefinitionCacheTtlSeconds` | `QUEST_DEFINITION_CACHE_TTL_SECONDS` | 3600 | TTL for quest definition Redis cache |
| `ProgressCacheTtlSeconds` | `QUEST_PROGRESS_CACHE_TTL_SECONDS` | 300 | TTL for objective progress Redis cache |
| `QuestDataCacheTtlSeconds` | `QUEST_DATA_CACHE_TTL_SECONDS` | 120 | TTL for in-memory quest data cache used by actor behavior expressions |
| `LockExpirySeconds` | `QUEST_LOCK_EXPIRY_SECONDS` | 30 | Distributed lock expiry for quest mutations |
| `MaxConcurrencyRetries` | `QUEST_MAX_CONCURRENCY_RETRIES` | 5 | ETag concurrency retry attempts |
| `PrerequisiteValidationMode` | `QUEST_PREREQUISITE_VALIDATION_MODE` | CheckAll | Controls prerequisite validation behavior (FailFast stops on first failure, CheckAll evaluates all) |

---

## Visual Aid: Quest-Contract State Mapping

```
Quest Service State               Contract Service State
═══════════════════════           ══════════════════════════

┌─────────────────────┐          ┌─────────────────────────┐
│ QuestDefinitionModel│──creates─▶│ Contract Template       │
│ - objectives        │          │ - milestones (1:1)      │
│ - prerequisites     │          │ - prebound APIs         │
│ - rewards           │          │ - enforcement_mode      │
└─────────────────────┘          └─────────────────────────┘
          │                                  │
          │ accept                           │ create instance
          ▼                                  ▼
┌─────────────────────┐          ┌─────────────────────────┐
│ QuestInstanceModel  │──maps────▶│ Contract Instance       │
│ - ACTIVE            │          │ - ACTIVE                │
│ - COMPLETED         │◀─fulfilled│ - FULFILLED             │
│ - FAILED            │◀──breach──│ - BREACHED              │
│ - ABANDONED         │◀─terminate│ - TERMINATED            │
└─────────────────────┘          └─────────────────────────┘
          │
          │ per objective
          ▼
┌─────────────────────┐          ┌─────────────────────────┐
│ObjectiveProgressModel│──maps───▶│ Milestone               │
│ - currentCount      │          │ - completed flag        │
│ - requiredCount     │─complete─▶│ (via CompleteMilestone) │
│ - isComplete        │          │                         │
└─────────────────────┘          └─────────────────────────┘

Key: objectives ←→ milestones (1:1 mapping)
     rewards → prebound APIs (executed on contract fulfillment)
     Quest status ← Contract lifecycle events
```

---

## Variable Provider Integration (Actor Service)

Quest (L2) integrates with the Actor service (L2) via the Variable Provider Factory pattern. Since both services are L2, Actor could call `IQuestClient` directly, but the provider pattern is still used for consistency with L4 data sources (personality, encounters) and to support efficient batch loading with caching:

**QuestProviderFactory** (`IVariableProviderFactory`):
- Registered in DI by `QuestServicePlugin.ConfigureServices`
- Creates `QuestProvider` instances for behavior execution
- Uses `IQuestDataCache` for efficient per-character quest loading

**QuestProvider** (`IVariableProvider`):
- Namespace: `quest`
- Available variables:
  - `${quest.active_count}` - int: Number of active quests
  - `${quest.has_active}` - bool: Has any active quest?
  - `${quest.codes}` - List: Active quest codes
  - `${quest.active_quests}` - List: Active quest summaries
  - `${quest.by_code.CODE.*}` - Quest details by code

**QuestDataCache** (`IQuestDataCache`):
- Singleton with ConcurrentDictionary for thread-safety
- Configurable TTL via `QuestDataCacheTtlSeconds` (default: 120 seconds / 2 minutes)
- Event-driven invalidation via self-subscription to quest lifecycle events

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `questCode` | B (Content Code) | Opaque string | Game-configurable quest identifier, unique within game service. Extensible without schema changes. |
| `objectiveCode` | B (Content Code) | Opaque string | Game-configurable objective identifier, unique within a quest definition. |
| `targetEntityType` (on objectives) | B (Content Code) | Opaque string | Caller-supplied entity type for kill/collect objectives (e.g., `goblin`, `iron_ore`). Not constrained to Bannou entity types -- represents game content targets. |
| `status` | C (System State) | `QuestStatus` enum | Finite quest lifecycle states: `ACTIVE`, `COMPLETED`, `FAILED`, `ABANDONED`, `EXPIRED`. |
| `category` | C (System State) | `QuestCategory` enum | Finite quest organization categories: `MAIN`, `SIDE`, `BOUNTY`, `DAILY`, `WEEKLY`, `EVENT`, `TUTORIAL`. |
| `difficulty` | C (System State) | `QuestDifficulty` enum | Finite difficulty tiers: `TRIVIAL`, `EASY`, `NORMAL`, `HARD`, `HEROIC`, `LEGENDARY`. |
| `objectiveType` | C (System State) | `ObjectiveType` enum | Finite objective tracking modes: `KILL`, `COLLECT`, `DELIVER`, `TRAVEL`, `DISCOVER`, `TALK`, `CRAFT`, `ESCORT`, `DEFEND`, `CUSTOM`. |
| `revealBehavior` (on objectives) | C (System State) | `ObjectiveRevealBehavior` enum | Finite visibility modes for hidden objectives: `ALWAYS`, `ON_PROGRESS`, `ON_COMPLETE`, `NEVER`. |
| `prerequisiteType` | C (System State) | `PrerequisiteType` enum | Finite prerequisite types: `QUEST_COMPLETED`, `CHARACTER_LEVEL`, `REPUTATION`, `ITEM_OWNED`, `CURRENCY_AMOUNT`. Built-in (L2): QUEST_COMPLETED, CURRENCY_AMOUNT, ITEM_OWNED use direct service client calls. Dynamic: CHARACTER_LEVEL, REPUTATION, and unknown types route through `IPrerequisiteProviderFactory` DI pattern for L4 extensibility. |
| `reward.type` | C (System State) | `RewardType` enum | Finite reward categories: `CURRENCY`, `ITEM`, `EXPERIENCE`, `REPUTATION`. Determines which reward fields are relevant. |
| `validationMode` | C (System State) | `PrerequisiteValidationMode` enum | Finite validation strategies: `FAIL_FAST`, `CHECK_ALL`. |

---

## Stubs & Unimplemented Features

None — all previously identified stubs have been implemented.

---

## Potential Extensions

1. **Dynamic objectives** ([#503](https://github.com/beyond-immersion/bannou-service/issues/503)): Objectives that change based on game state or player choices. Contract milestones are structurally immutable by design (trust guarantee). The existing `CUSTOM` ObjectiveType with external progress reporting via `ReportObjectiveProgressAsync` likely covers most "dynamic" use cases — game code determines what counts as progress based on current state. See #503 comments for architectural guidance.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/503 -->

2. **Party quest acceptance flow** ([#506](https://github.com/beyond-immersion/bannou-service/issues/506)): The data model supports multi-character instances (`QuestorCharacterIds` list, configurable `MaxQuestors`) and progress is already per-instance (shared), but no API exists to add additional characters to an existing quest instance — `AcceptQuestAsync` always creates a new instance with a single questor. Requires designing party formation model, contract party management, per-objective-type progress semantics, and reward distribution.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/506 -->

3. **Localization support**: Quest names and descriptions are single-language. Could add localization key support for multi-language games.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/508 -->

4. **Client events for real-time objective tracking** ([#496](https://github.com/beyond-immersion/bannou-service/issues/496)): Push `QuestObjectiveProgressed` and `QuestStatusChanged` (consolidated lifecycle event with status discriminator for accepted/completed/failed/abandoned) client events via `IClientEventPublisher` using the Entity Session Registry (#426, now implemented). Sessions resolved via `character → session` bindings for all questor characters. Published from Quest's event handlers that process Contract state transitions. **Unblocked** — infrastructure dependency (#426) is closed.
<!-- AUDIT:NEEDS_DESIGN:2026-02-26:https://github.com/beyond-immersion/bannou-service/issues/496 -->

5. **T28 character deletion cleanup** ([#561](https://github.com/beyond-immersion/bannou-service/issues/561)): Quest stores data keyed by character IDs (instances, character indexes, cooldowns) but does not implement `ISeededResourceProvider` for cleanup when characters are deleted. Requires registering references with lib-resource and implementing cascade cleanup.
<!-- AUDIT:NEEDS_IMPLEMENTATION:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/561 -->

6. **Objective progress durability** ([#562](https://github.com/beyond-immersion/bannou-service/issues/562)): Redis-only progress storage with 5-minute TTL causes silent data loss for long-running quests. Contract milestones are binary and cannot store partial progress. Needs either durable storage or TTL aligned to quest deadline.
<!-- AUDIT:NEEDS_DESIGN:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/562 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks (Documented Behavior)

1. **Self-subscription for cache invalidation**: Quest service subscribes to its own events (`quest.accepted`, `quest.completed`, `quest.failed`, `quest.abandoned`) to invalidate the `QuestDataCache`. This ensures actors running on different instances see fresh data after quest state changes.

2. **Contract termination reason parsing for ABANDONED vs FAILED** ([#563](https://github.com/beyond-immersion/bannou-service/issues/563)): `HandleContractTerminatedAsync` uses string.Contains checks for "abandoned" and "player" to determine if termination is abandonment vs failure. This is fragile — Contract's `reason` field is a free-form nullable string, not a typed enum. If Contract callers or future changes alter reason string conventions, Quest silently misclassifies quest outcomes. #563 proposes a typed `TerminationCategory` enum on the Contract termination event.

3. **Objective progress TTL — durability concern** ([#562](https://github.com/beyond-immersion/bannou-service/issues/562)): Objective progress is stored in Redis with TTL from `ProgressCacheTtlSeconds` (default **5 minutes**). Progress is re-persisted on each update, refreshing the TTL. However, Contract milestones are **binary** (complete/not complete) and cannot store partial progress. If no progress is reported for longer than the TTL, partial progress (e.g., 7/10 goblins killed) and entity deduplication data (`TrackedEntityIds`) are silently lost. This is a real data loss risk for daily, weekly, exploration, and other long-running quest types. See #562 for proposed fixes.

4. **Entity deduplication via HashSet in ObjectiveProgressModel**: The `TrackedEntityIds` HashSet prevents counting the same killed enemy or collected item multiple times. This set persists in Redis with the progress record (subject to the same TTL concern as Quirk #3).

5. **First questor used for abandoned event**: In `FailOrAbandonQuestAsync`, when called from event handler (not direct API), uses `QuestorCharacterIds.FirstOrDefault()` as the abandoning character since the specific abandoner isn't known from contract termination event.

6. **Definition cache separate from MySQL store**: Definition cache (`quest-definition-cache`) is a Redis read-through cache of the MySQL `quest-definition-statestore`. Writes go to MySQL only; reads check cache first with fallback to MySQL.

### Design Considerations (Resolved)

1. **Prerequisite architecture (RESOLVED in #320)**: Quest uses a two-tier prerequisite system:
   - **Built-in (L2)**: `quest_completed`, `currency`, `item` - Quest calls L2 service clients directly with hard dependencies
   - **Dynamic (via IPrerequisiteProviderFactory)**: `character_level`, `reputation`, `skill`, `magic`, `achievement`, `status_effect`, etc. - L4 (or future L2) services implement `IPrerequisiteProviderFactory`, Quest discovers via `IEnumerable<IPrerequisiteProviderFactory>` DI collection injection, graceful degradation if provider missing
   - See `docs/planning/QUEST-PLUGIN-ARCHITECTURE.md` and `docs/reference/SERVICE-HIERARCHY.md` for full pattern

2. **Reward execution (RESOLVED in #320)**: Rewards execute via Contract prebound APIs:
   - Quest builds prebound API definitions from `RewardDefinitionModel` at definition creation
   - APIs attached to final milestone's `onComplete` array
   - Quest sets `TemplateValues` with resolved wallet/container IDs at quest acceptance (Quest is L2, can call Currency/Inventory directly)
   - Contract executes prebound APIs on milestone completion - Quest never calls Currency/Inventory for reward distribution

---

## Work Tracking

### Open Issues
- [#496](https://github.com/beyond-immersion/bannou-service/issues/496): Client events for real-time objective tracking (unblocked — #426 implemented)
- [#503](https://github.com/beyond-immersion/bannou-service/issues/503): Dynamic objectives — evaluate CUSTOM sufficiency before designing mutation
- [#506](https://github.com/beyond-immersion/bannou-service/issues/506): Party quest acceptance and shared progress mechanics
- [#508](https://github.com/beyond-immersion/bannou-service/issues/508): Localization support (cross-cutting, system-wide design)
- [#561](https://github.com/beyond-immersion/bannou-service/issues/561): T28 character deletion cleanup via lib-resource
- [#562](https://github.com/beyond-immersion/bannou-service/issues/562): Objective progress durability (Redis-only with 5-min TTL)
- [#563](https://github.com/beyond-immersion/bannou-service/issues/563): Contract typed termination reason enum (Contract-side, benefits Quest)
