# Quest Plugin Deep Dive

> **Plugin**: lib-quest
> **Schema**: schemas/quest-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: quest-definition-statestore (MySQL), quest-instance-statestore (MySQL), quest-objective-progress (Redis), quest-definition-cache (Redis), quest-character-index (Redis), quest-cooldown (Redis), quest-idempotency (Redis)

---

## Overview

The Quest service (L2 GameFoundation) provides objective-based gameplay progression as a thin orchestration layer over lib-contract. Translates game-flavored quest semantics (objectives, rewards, quest givers) into Contract infrastructure (milestones, prebound APIs, parties), leveraging Contract's state machine and cleanup orchestration while presenting a player-friendly API. Agnostic to prerequisite sources: L4 services (skills, magic, achievements) implement `IPrerequisiteProviderFactory` for validation without Quest depending on them. Exposes quest data to the Actor service via the Variable Provider Factory pattern for ABML behavior expressions.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | Quest definitions (MySQL), instances (MySQL), progress (Redis), cooldowns (Redis), indexes (Redis), idempotency (Redis) |
| lib-messaging (IMessageBus) | Publishing quest lifecycle events (accepted, progressed, completed, failed, abandoned) |
| lib-contract (IContractClient) | Creating contract templates/instances, managing milestones, setting template values, terminating contracts |
| lib-character (ICharacterClient) | Validating character existence for quest acceptance |
| lib-currency (ICurrencyClient) | CURRENCY_AMOUNT prerequisite validation, resolving wallet IDs for reward template values |
| lib-inventory (IInventoryClient) | ITEM_OWNED prerequisite validation, resolving container IDs for reward template values |
| lib-item (IItemClient) | Resolving item template IDs from codes for prerequisite validation |
| lib-resource (IResourceClient) | Registering compression callback for character archival |
| IEventConsumer | Subscribing to contract lifecycle events for state synchronization |
| IEventTemplateRegistry | Registering event templates for ABML `emit_event:` action |
| IEnumerable\<IPrerequisiteProviderFactory\> | DI collection injection for dynamic prerequisite providers (L4 services register implementations) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Consumes `IVariableProviderFactory` implementation (QuestProviderFactory) to provide `${quest.*}` variables for ABML behavior expressions |
| lib-analytics | Subscribes to `quest.completed`, `quest.failed` events for statistics aggregation |
| lib-achievement | Subscribes to `quest.completed` events for achievement unlock triggers |

---

## State Storage

### MySQL Stores (Durable)

**Store**: `quest-definition-statestore`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:def:{definitionId}` | `QuestDefinitionModel` | Quest definition with objectives, prerequisites, rewards |

**Store**: `quest-instance-statestore`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:inst:{questInstanceId}` | `QuestInstanceModel` | Active/completed quest instance with status, party, deadlines |

### Redis Stores (Ephemeral/Cache)

**Store**: `quest-objective-progress`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:prog:{questInstanceId}:{objectiveCode}` | `ObjectiveProgressModel` | Real-time objective progress tracking with entity deduplication |

**Store**: `quest-definition-cache`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:def:{definitionId}` | `QuestDefinitionModel` | Read-through cache for frequently accessed definitions (TTL: 1 hour) |

**Store**: `quest-character-index`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:char:{characterId}` | `CharacterQuestIndex` | Active quest IDs and completed quest codes per character |

**Store**: `quest-cooldown`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:cd:{characterId}:{questCode}` | `CooldownEntry` | Per-character quest cooldown tracking for repeatable quests (TTL: 24 hours) |

**Store**: `quest-idempotency`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `quest:idem:{key}` | `IdempotencyRecord` | Idempotency keys for accept/complete operations (TTL: 24 hours) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `quest.accepted` | `QuestAcceptedEvent` | Character accepts a quest via AcceptQuestAsync |
| `quest.objective.progressed` | `QuestObjectiveProgressedEvent` | Objective progress updated via ReportObjectiveProgressAsync or contract milestone events |
| `quest.completed` | `QuestCompletedEvent` | All required objectives complete, quest status → COMPLETED |
| `quest.failed` | `QuestFailedEvent` | Quest fails due to deadline, breach, or contract termination |
| `quest.abandoned` | `QuestAbandonedEvent` | Player voluntarily abandons quest via AbandonQuestAsync |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.milestone.completed` | `HandleContractMilestoneCompletedAsync` | Updates objective progress when Contract confirms milestone completion |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Marks quest as COMPLETED when all required milestones done |
| `contract.terminated` | `HandleContractTerminatedAsync` | Marks quest as FAILED or ABANDONED based on termination reason |
| `quest.accepted` | `HandleQuestAcceptedForCacheAsync` | Self-subscribe: invalidates quest data cache for affected characters |
| `quest.completed` | `HandleQuestCompletedForCacheAsync` | Self-subscribe: invalidates quest data cache for affected characters |
| `quest.failed` | `HandleQuestFailedForCacheAsync` | Self-subscribe: invalidates quest data cache for affected characters |
| `quest.abandoned` | `HandleQuestAbandonedForCacheAsync` | Self-subscribe: invalidates quest data cache for affected character |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxActiveQuestsPerCharacter` | `QUEST_MAX_ACTIVE_QUESTS_PER_CHARACTER` | 25 | Maximum concurrent active quests per character |
| `MaxQuestorsPerQuest` | `QUEST_MAX_QUESTORS_PER_QUEST` | 5 | Maximum party members per quest instance |
| `DefaultDeadlineSeconds` | `QUEST_DEFAULT_DEADLINE_SECONDS` | 604800 (7 days) | Default quest deadline when definition doesn't specify one |
| `DefinitionCacheTtlSeconds` | `QUEST_DEFINITION_CACHE_TTL_SECONDS` | 3600 | TTL for quest definition Redis cache |
| `ProgressCacheTtlSeconds` | `QUEST_PROGRESS_CACHE_TTL_SECONDS` | 300 | TTL for objective progress Redis cache |
| `QuestDataCacheTtlSeconds` | `QUEST_DATA_CACHE_TTL_SECONDS` | 120 | TTL for in-memory quest data cache used by actor behavior expressions |
| `CooldownCacheTtlSeconds` | `QUEST_COOLDOWN_CACHE_TTL_SECONDS` | 86400 | TTL for quest cooldown tracking |
| `LockExpirySeconds` | `QUEST_LOCK_EXPIRY_SECONDS` | 30 | Distributed lock expiry for quest mutations |
| `LockRetryAttempts` | `QUEST_LOCK_RETRY_ATTEMPTS` | 3 | Retry attempts when lock acquisition fails |
| `MaxConcurrencyRetries` | `QUEST_MAX_CONCURRENCY_RETRIES` | 5 | ETag concurrency retry attempts |
| `IdempotencyTtlSeconds` | `QUEST_IDEMPOTENCY_TTL_SECONDS` | 86400 | TTL for idempotency keys (24 hours) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<QuestService>` | Structured logging |
| `QuestServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for all quest stores |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration |
| `IContractClient` | Contract service integration for template/instance management |
| `ICharacterClient` | Character validation during quest acceptance |
| `IQuestDataCache` | In-memory TTL cache for actor behavior expressions |
| `QuestProviderFactory` | IVariableProviderFactory implementation for Actor integration |
| `QuestProvider` | IVariableProvider exposing `${quest.*}` variables |

---

## API Endpoints (Implementation Notes)

### Definition Endpoints (developer role)

Standard CRUD operations on quest definitions with contract template creation. `CreateQuestDefinitionAsync` validates objectives and prerequisites, then creates a contract template via `IContractClient` before storing the definition. Contract templates are immutable once created; only quest metadata (name, description, category, difficulty, tags) can be updated via `UpdateQuestDefinitionAsync`. Deprecated definitions cannot be used for new instances.

### Instance Endpoints (user role)

**AcceptQuestAsync**: The core acceptance flow:
1. Validates character exists via `ICharacterClient`
2. Checks prerequisites (completed quests, level, reputation, items, currency)
3. Verifies not on cooldown for repeatable quests
4. Checks active quest count against `MaxActiveQuestsPerCharacter`
5. Creates contract instance via `IContractClient` with auto-consent for questor
6. Initializes objective progress records
7. Updates character index with new active quest
8. Publishes `quest.accepted` event

**AbandonQuestAsync**: Terminates underlying contract, updates status to ABANDONED, removes from character index.

**ListQuestsAsync/ListAvailableQuestsAsync**: Filter by status, character, quest giver, game service. Available quests check prerequisites and cooldowns.

**GetQuestLogAsync**: Player-facing UI endpoint returning progress summaries with hidden objective filtering based on reveal behavior.

### Objective Endpoints

**ReportObjectiveProgressAsync**: Increments progress, supports entity deduplication via `TrackedEntityIds` HashSet, triggers milestone completion in Contract when objective completes. Uses ETag-based optimistic concurrency with configurable retries.

**ForceCompleteObjectiveAsync** (admin role): Debug endpoint that immediately completes an objective regardless of current progress.

### Internal Endpoints (service-to-service)

**HandleMilestoneCompletedAsync/HandleQuestCompletedAsync**: Prebound API callbacks from Contract service (empty x-permissions - internal only).

### Compression Endpoint (developer role)

**GetCompressDataAsync**: Returns `QuestArchive` with active quests, completed count, and category breakdown for character archival via lib-resource.

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

## Stubs & Unimplemented Features

1. ~~**Prerequisite validation for non-QUEST_COMPLETED types**~~: **IMPLEMENTED** (2026-02-07) in #320 - Full prerequisite validation now implemented:
   - **Built-in (L2)**: QUEST_COMPLETED (direct check), CURRENCY_AMOUNT (via ICurrencyClient), ITEM_OWNED (via IInventoryClient/IItemClient)
   - **Dynamic (L4)**: REPUTATION and unknown types (via IPrerequisiteProviderFactory DI collection)
   - **Stub**: CHARACTER_LEVEL logs and skips until Character service tracks levels
   - Configurable validation mode: `CHECK_ALL` (rich error info) or `FAIL_FAST` (early exit)

2. ~~**Reward distribution via prebound APIs**~~: **IMPLEMENTED** (2026-02-07) in #320 - Rewards now execute via Contract prebound APIs:
   - `BuildRewardPreboundApis` generates API definitions from CURRENCY and ITEM rewards
   - APIs attached to final required milestone's `onComplete` array
   - `ResolveTemplateValuesAsync` resolves wallet/container IDs at quest acceptance
   - EXPERIENCE and REPUTATION rewards log warnings (L4 services not yet implemented)

3. ~~**QuestDataCache TTL configuration**~~: **FIXED** (2026-02-07) - Added `QuestDataCacheTtlSeconds` config property (env: `QUEST_DATA_CACHE_TTL_SECONDS`, default: 120). Cache now reads from configuration.

---

## Potential Extensions

1. **Quest chains**: Support for sequential quest chains where completing one quest unlocks the next. Currently would require manual prerequisite management.

2. **Dynamic objectives**: Objectives that change based on game state or player choices. Current model has static objective definitions.

3. **Shared party progress**: Currently each character in a party quest has individual objective progress. Could add shared progress tracking for cooperative objectives.

4. **Quest log categories**: The quest log returns all active quests. Could add category-based filtering (main story, side, daily, etc.) for UI organization.

5. **Localization support**: Quest names and descriptions are single-language. Could add localization key support for multi-language games.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Hardcoded QuestDataCache TTL**~~: **FIXED** (2026-02-07) - Added `QuestDataCacheTtlSeconds` property to configuration schema (default 120 seconds). `QuestDataCache` now injects `QuestServiceConfiguration` and uses this property instead of hardcoded value.

### Intentional Quirks (Documented Behavior)

1. **Self-subscription for cache invalidation**: Quest service subscribes to its own events (`quest.accepted`, `quest.completed`, `quest.failed`, `quest.abandoned`) to invalidate the `QuestDataCache`. This ensures actors running on different instances see fresh data after quest state changes.

2. **Contract termination reason parsing for ABANDONED vs FAILED**: `HandleContractTerminatedAsync` uses string.Contains checks for "abandoned" and "player" to determine if termination is abandonment vs failure. This is intentionally loose matching to handle various termination reasons.

3. **Objective progress TTL**: Objective progress is stored in Redis with TTL from `ProgressCacheTtlSeconds` (default 5 minutes). For long-running quests, progress is re-persisted on each update which refreshes the TTL. If no progress happens for longer than TTL, progress data may expire - this is acceptable as the source of truth is Contract milestones.

4. **Entity deduplication via HashSet in ObjectiveProgressModel**: The `TrackedEntityIds` HashSet prevents counting the same killed enemy or collected item multiple times. This set persists in Redis with the progress record.

5. **First questor used for abandoned event**: In `FailOrAbandonQuestAsync`, when called from event handler (not direct API), uses `QuestorCharacterIds.FirstOrDefault()` as the abandoning character since the specific abandoner isn't known from contract termination event.

6. **Definition cache separate from MySQL store**: Definition cache (`quest-definition-cache`) is a Redis read-through cache of the MySQL `quest-definition-statestore`. Writes go to MySQL only; reads check cache first with fallback to MySQL.

### Design Considerations (Resolved)

1. **Prerequisite architecture (RESOLVED in #320)**: Quest uses a two-tier prerequisite system:
   - **Built-in (L2)**: `quest_completed`, `currency`, `item`, `character_level`, `relationship` - Quest calls L2 service clients directly with hard dependencies
   - **Dynamic (L4)**: `skill`, `magic`, `achievement`, `status_effect`, etc. - L4 services implement `IPrerequisiteProviderFactory`, Quest discovers via `IEnumerable<IPrerequisiteProviderFactory>` DI collection injection, graceful degradation if provider missing
   - See `docs/planning/QUEST-PLUGIN-ARCHITECTURE.md` and `docs/reference/SERVICE-HIERARCHY.md` for full pattern

2. **Reward execution (RESOLVED in #320)**: Rewards execute via Contract prebound APIs:
   - Quest builds prebound API definitions from `RewardDefinitionModel` at definition creation
   - APIs attached to final milestone's `onComplete` array
   - Quest sets `TemplateValues` with resolved wallet/container IDs at quest acceptance (Quest is L2, can call Currency/Inventory directly)
   - Contract executes prebound APIs on milestone completion - Quest never calls Currency/Inventory for reward distribution

---

## Work Tracking

### Completed
- **2026-02-07**: Moved Quest to L2 (GameFoundation), implemented prerequisite validation and reward prebound APIs (#320):
  - Changed service layer from GameFeatures to GameFoundation in schema
  - Added ICurrencyClient, IInventoryClient, IItemClient dependencies for built-in prerequisites
  - Created IPrerequisiteProviderFactory interface for dynamic L4 prerequisites
  - Implemented CheckCurrencyPrerequisiteAsync and CheckItemPrerequisiteAsync
  - Added BuildRewardPreboundApis for CURRENCY and ITEM rewards via Contract
  - Added ResolveTemplateValuesAsync for wallet/container ID resolution
  - Added PrerequisiteValidationMode configuration (CHECK_ALL vs FAIL_FAST)
  - Added FailedPrerequisite model and AcceptQuestErrorResponse for detailed failure info
- **2026-02-07**: Fixed T21 violation - `QuestDataCache` TTL is now configurable via `QuestDataCacheTtlSeconds` (env: `QUEST_DATA_CACHE_TTL_SECONDS`, default: 120)
