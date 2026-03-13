# Quest Implementation Map

> **Plugin**: lib-quest
> **Schema**: schemas/quest-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/QUEST.md](../plugins/QUEST.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-quest |
| Layer | L2 GameFoundation |
| Endpoints | 18 |
| State Stores | quest-definition-statestore (MySQL), quest-instance-statestore (MySQL), quest-objective-progress (Redis), quest-definition-cache (Redis), quest-character-index (Redis), quest-cooldown (Redis) |
| Events Published | 8 (quest.accepted, quest.objective.progressed, quest.completed, quest.failed, quest.abandoned, quest.instance.created, quest.instance.updated, quest.instance.deleted) |
| Events Consumed | 7 (3 contract, 4 self-subscription) |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `quest-definition-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `QuestDefinitionModel` | Quest definition with objectives, prerequisites, rewards |

**Store**: `quest-instance-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{questInstanceId}` | `QuestInstanceModel` | Active/completed quest instance with status, party, deadlines |
| `def-inst:{definitionId}` | `List<string>` (JSON) | Reverse index: definition → instance IDs (for O(1) clean-deprecated checks) |

**Store**: `quest-objective-progress` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `prog:{questInstanceId}:{objectiveCode}` | `ObjectiveProgressModel` | Real-time objective progress with entity deduplication (TTL: ProgressCacheTtlSeconds) |

**Store**: `quest-definition-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `QuestDefinitionModel` | Read-through cache for definitions (TTL: DefinitionCacheTtlSeconds) |

**Store**: `quest-character-index` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `char:{characterId}` | `CharacterQuestIndex` | Active quest IDs and completed quest codes per character |

**Store**: `quest-cooldown` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cd:{characterId}:{questCode}` | `CooldownEntry` | Per-character repeatable quest cooldown tracking |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 6 stores: definitions (MySQL), instances (MySQL), progress (Redis), cache (Redis), index (Redis), cooldowns (Redis) |
| lib-state (IDistributedLockProvider) | L0 | Hard | Character lock during quest acceptance |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 5 quest lifecycle events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers |
| lib-resource (IResourceClient) | L1 | Hard | Character reference registration/unregistration, cleanup callback registration |
| lib-contract (IContractClient) | L1 | Hard | Template/instance CRUD, consent, milestone completion, termination |
| lib-character (ICharacterClient) | L2 | Hard | Character existence validation on quest acceptance |
| lib-currency (ICurrencyClient) | L2 | Hard | CURRENCY_AMOUNT prerequisite validation, wallet resolution for rewards |
| lib-inventory (IInventoryClient) | L2 | Hard | ITEM_OWNED prerequisite validation, container resolution for rewards |
| lib-item (IItemClient) | L2 | Hard | Item template lookup by code for prerequisite validation |
| IEnumerable\<IPrerequisiteProviderFactory\> | L4 via DI | Collection | Dynamic prerequisite providers (character_level, reputation, etc.) |

**Notes:**
- `IResourceClient` (L1) is a hard constructor dependency: used for reference registration/unregistration (cleanup) and at startup for compression/cleanup callback registration.
- `IEventTemplateRegistry` resolved via `GetService<T>()` at startup for ABML event template registration; graceful degradation if absent.
- `QuestProviderFactory` implements `IVariableProviderFactory` for Actor `${quest.*}` ABML variables (L2-to-L2 provider pattern for consistency with L4 data sources).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `quest.accepted` | `QuestAcceptedEvent` | AcceptQuest |
| `quest.objective.progressed` | `QuestObjectiveProgressedEvent` | ReportObjectiveProgress, HandleContractMilestoneCompletedAsync (event handler) |
| `quest.completed` | `QuestCompletedEvent` | CompleteQuestAsync helper (via HandleQuestCompleted endpoint, HandleContractFulfilledAsync event handler) |
| `quest.failed` | `QuestFailedEvent` | FailOrAbandonQuestAsync helper (via HandleContractTerminatedAsync when reason lacks "abandoned"/"player") |
| `quest.abandoned` | `QuestAbandonedEvent` | AbandonQuest, FailOrAbandonQuestAsync helper (via HandleContractTerminatedAsync when reason contains "abandoned"/"player") |
| `quest.instance.created` | `QuestInstanceCreatedEvent` | AcceptQuest -- lifecycle event published after instance creation |
| `quest.instance.updated` | `QuestInstanceUpdatedEvent` | Status transitions (completed, failed, abandoned) -- lifecycle event with `changedFields` |
| `quest.instance.deleted` | `QuestInstanceDeletedEvent` | DeleteInstanceRecordAsync -- published when an instance is fully deleted (e.g., sole-questor cleanup) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.milestone.completed` | `HandleContractMilestoneCompletedAsync` | Finds quest by ContractId; marks matching objective complete via ETag write; publishes quest.objective.progressed |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Finds quest by ContractId; delegates to CompleteQuestAsync (ETag update, index update, cooldown write, publishes quest.completed) |
| `contract.terminated` | `HandleContractTerminatedAsync` | Finds quest by ContractId; delegates to FailOrAbandonQuestAsync (string heuristic on reason for fail vs abandon) |
| `quest.accepted` | `HandleQuestAcceptedForCacheAsync` | Self-subscription: invalidates QuestDataCache for all questor characters |
| `quest.completed` | `HandleQuestCompletedForCacheAsync` | Self-subscription: invalidates QuestDataCache for all questor characters |
| `quest.failed` | `HandleQuestFailedForCacheAsync` | Self-subscription: invalidates QuestDataCache for all questor characters |
| `quest.abandoned` | `HandleQuestAbandonedForCacheAsync` | Self-subscription: invalidates QuestDataCache for abandoning character |

### Contract Event Handler Details

#### HandleContractMilestoneCompletedAsync

```
QUERY _instanceStore WHERE ContractInstanceId == evt.ContractId
IF null -> return // not quest-related
READ _progressStore:"prog:{questInstanceId}:{milestoneCode}" [with ETag]
IF null or already complete -> return
// Set CurrentCount = RequiredCount, IsComplete = true
ETAG-WRITE _progressStore:"prog:{questInstanceId}:{milestoneCode}"
 -> retry up to MaxConcurrencyRetries
PUBLISH "quest.objective.progressed" { questInstanceId, questCode, objectiveCode, currentCount, requiredCount, isComplete: true }
```

#### HandleContractFulfilledAsync

```
QUERY _instanceStore WHERE ContractInstanceId == evt.ContractId
IF null or not Active -> return
// Delegates to CompleteQuestAsync (see HandleQuestCompleted endpoint)
```

#### HandleContractTerminatedAsync

```
QUERY _instanceStore WHERE ContractInstanceId == evt.ContractId
IF null or not Active -> return
// Delegates to FailOrAbandonQuestAsync
// Determines Abandoned vs Failed via string heuristic on evt.Reason
// ("abandoned" or "player" in reason -> Abandoned, else Failed)
```

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<QuestService>` | Structured logging |
| `QuestServiceConfiguration` | Typed configuration access (8 properties) |
| `IStateStoreFactory` | Acquires 6 state stores in constructor (not stored as field) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration (passed to RegisterEventConsumers) |
| `IResourceClient` | Character reference tracking (cleanup), compression/cleanup callback registration |
| `IContractClient` | Contract template/instance CRUD, consent, milestones |
| `ICharacterClient` | Character existence validation |
| `ICurrencyClient` | Currency prerequisite checks and wallet resolution |
| `IInventoryClient` | Item ownership checks and container resolution |
| `IItemClient` | Item template code-to-ID resolution |
| `IDistributedLockProvider` | Character lock during quest acceptance |
| `IEnumerable<IPrerequisiteProviderFactory>` | DI collection for L4 dynamic prerequisite providers |
| `IQuestDataCache` | In-memory TTL cache for actor variable provider |
| `ITelemetryProvider` | Distributed tracing spans |
| `QuestProviderFactory` | IVariableProviderFactory for Actor `${quest.*}` namespace |
| `QuestDataCache` | Singleton ConcurrentDictionary cache; loads via IQuestClient; invalidated by self-subscribed events |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateQuestDefinition | POST /quest/definition/create | developer | definition | - |
| GetQuestDefinition | POST /quest/definition/get | user | cache (read-through) | - |
| ListQuestDefinitions | POST /quest/definition/list | user | - | - |
| UpdateQuestDefinition | POST /quest/definition/update | developer | definition, cache | - |
| DeprecateQuestDefinition | POST /quest/definition/deprecate | developer | definition, cache | quest.definition.updated |
| AcceptQuest | POST /quest/accept | user | instance, progress, index, resource-ref, reverse-index | quest.accepted, quest.instance.created |
| AbandonQuest | POST /quest/abandon | user | instance, index | quest.abandoned |
| GetQuest | POST /quest/get | user | - | - |
| ListQuests | POST /quest/list | user | - | - |
| ListAvailableQuests | POST /quest/list-available | user | - | - |
| GetQuestLog | POST /quest/log | user | - | - |
| ReportObjectiveProgress | POST /quest/objective/progress | user | progress | quest.objective.progressed |
| ForceCompleteObjective | POST /quest/objective/complete | admin | progress | - |
| GetObjectiveProgress | POST /quest/objective/get | user | - | - |
| HandleMilestoneCompleted | POST /quest/internal/milestone-completed | [] | - | - |
| HandleQuestCompleted | POST /quest/internal/quest-completed | [] | instance, index, cooldown | quest.completed |
| GetCompressData | POST /quest/get-compress-data | developer | - | - |
| DeleteByCharacter | POST /quest/delete-by-character | [] | instance, progress, cooldown, index, resource-ref, reverse-index | quest.abandoned, quest.instance.updated, quest.instance.deleted (per instance) |
| CleanDeprecatedQuestDefinitions | POST /quest/definition/clean-deprecated | admin | definition, cache, reverse-index | quest.definition.deleted |

---

## Methods

### CreateQuestDefinition
POST /quest/definition/create | Roles: [developer]

```
// Normalize code to uppercase
-> 400 if code empty after normalization
QUERY _definitionStore WHERE Code == normalizedCode
-> 409 if definition with same code exists

// BuildContractTemplateRequest: milestones from objectives,
// reward prebound APIs attached to last required objective's OnComplete
CALL IContractClient.CreateContractTemplateAsync(templateRequest)
-> 409 if ApiException(409)
-> 503 if null response

WRITE _definitionStore:"def:{definitionId}" <- QuestDefinitionModel from request + contractTemplateId
RETURN (200, QuestDefinitionResponse)
```

### GetQuestDefinition
POST /quest/definition/get | Roles: [user]

```
IF body.definitionId provided
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 QUERY _definitionStore WHERE DefinitionId == definitionId
 IF found
 WRITE _definitionCache:"def:{definitionId}" <- definition (TTL: DefinitionCacheTtlSeconds)
ELSE IF body.code provided
 QUERY _definitionStore WHERE Code == normalizedCode
ELSE
 RETURN (400, null) // neither ID nor code

-> 404 if null
RETURN (200, QuestDefinitionResponse)
```

### ListQuestDefinitions
POST /quest/definition/list | Roles: [user]

```
QUERY _definitionStore WHERE
 (gameServiceId filter) AND (category filter) AND
 (difficulty filter) AND (includeDeprecated OR !IsDeprecated)
// In-memory tag filter: any-match semantics (at least one matching tag)
// In-memory pagination: Skip(offset).Take(limit)
RETURN (200, ListQuestDefinitionsResponse { definitions, total })
```

### UpdateQuestDefinition
POST /quest/definition/update | Roles: [developer]

```
READ _definitionStore:"def:{definitionId}" [with ETag] -> 404 if null
// Update mutable fields only: Name, Description, Category, Difficulty, Tags
// Structural fields (objectives, rewards, prerequisites) are immutable
ETAG-WRITE _definitionStore:"def:{definitionId}"
 -> retry up to MaxConcurrencyRetries -> 409 if exhausted
DELETE _definitionCache:"def:{definitionId}" // cache invalidation
RETURN (200, QuestDefinitionResponse)
```

### DeprecateQuestDefinition
POST /quest/definition/deprecate | Roles: [developer]

```
READ _definitionStore:"def:{definitionId}" [with ETag] -> 404 if null
IF already deprecated
 RETURN (200, QuestDefinitionResponse) // idempotent
// Set IsDeprecated = true, DeprecatedAt = UtcNow, DeprecationReason = body.reason
ETAG-WRITE _definitionStore:"def:{definitionId}"
 -> retry up to MaxConcurrencyRetries -> 409 if exhausted
DELETE _definitionCache:"def:{definitionId}" // cache invalidation
PUBLISH quest.definition.updated { definitionId, code, ..., changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, QuestDefinitionResponse)
```

### AcceptQuest
POST /quest/accept | Roles: [user]

```
// Resolve definition by ID (cache -> MySQL) or Code (MySQL only)
IF body.definitionId
 // GetDefinitionModelAsync: cache read-through
 READ _definitionCache:"def:{definitionId}"
 IF cache miss
 QUERY _definitionStore WHERE DefinitionId == definitionId
 IF found: WRITE _definitionCache:"def:{definitionId}" (TTL: DefinitionCacheTtlSeconds)
ELSE IF body.code
 QUERY _definitionStore WHERE Code == normalizedCode
-> 404 if definition null
-> 400 if definition.IsDeprecated

CALL ICharacterClient.GetCharacterAsync(questorCharacterId) -> 400 if not found

LOCK "quest:lock:char:{questorCharacterId}" (QuestInstance store, LockExpirySeconds)
 -> 409 if lock fails
 READ _characterIndex:"char:{questorCharacterId}"
 -> 400 if ActiveQuestIds.Count >= MaxActiveQuestsPerCharacter

 IF definition.Repeatable
 READ _cooldownStore:"cd:{characterId}:{questCode}" -> 409 if cooldown active

 FOREACH activeQuestId in index.ActiveQuestIds
 QUERY _instanceStore WHERE QuestInstanceId == activeQuestId
 AND DefinitionId == definition.DefinitionId -> 409 if duplicate found

 // CheckPrerequisitesAsync (per prerequisite in definition)
 FOREACH prerequisite in definition.Prerequisites
 IF QUEST_COMPLETED: check index.CompletedQuestCodes
 IF CURRENCY_AMOUNT: CALL ICurrencyClient (balance check)
 IF ITEM_OWNED: CALL IItemClient.GetItemTemplateAsync + CALL IInventoryClient.HasItemsAsync
 ELSE: CALL matching IPrerequisiteProviderFactory.CheckAsync
 -> 400 if any fail (FailFast stops on first; CheckAll evaluates all per config)

 CALL IContractClient.CreateContractInstanceAsync(...) -> 503 if fails
 CALL IContractClient.ConsentToContractAsync(...) -> 503 if fails

 // ResolveTemplateValuesAsync (best-effort, failures logged)
 IF currency rewards: CALL ICurrencyClient.GetOrCreateWalletAsync -> questor_wallet_id
 IF item rewards: CALL IInventoryClient.GetOrCreateContainerAsync -> questor_container_id
 CALL IContractClient.SetContractTemplateValuesAsync(...) // best-effort

 WRITE _instanceStore:"inst:{questInstanceId}" <- QuestInstanceModel
 CALL IResourceClient.RegisterReferenceAsync(character, questInstanceId) // cleanup tracking
 FOREACH objective in definition.Objectives
 WRITE _progressStore:"prog:{instanceId}:{objectiveCode}" <- ObjectiveProgressModel
 (TTL: ProgressCacheTtlSeconds)

 // UpdateCharacterIndexAsync (ETag retry)
 READ _characterIndex:"char:{questorCharacterId}" [with ETag]
 // Add questInstanceId to ActiveQuestIds
 ETAG-WRITE _characterIndex:"char:{questorCharacterId}"
 -> retry up to MaxConcurrencyRetries

 // Maintain reverse index (definition → instance list)
 ETAG-WRITE _instanceStringStore:"def-inst:{definitionId}" <- append questInstanceId

 PUBLISH "quest.accepted" { questInstanceId, definitionId, questCode, questorCharacterIds, gameServiceId }
 PUBLISH quest.instance.created (QuestInstanceCreatedEvent with all instance fields)

RETURN (200, QuestInstanceResponse)
```

### AbandonQuest
POST /quest/abandon | Roles: [user]

```
READ _instanceStore:"inst:{questInstanceId}" [with ETag] -> 404 if null
-> 409 if not Active
-> 400 if questorCharacterId not in QuestorCharacterIds
// Set Status = Abandoned, CompletedAt = UtcNow
ETAG-WRITE _instanceStore:"inst:{questInstanceId}"
 -> retry up to MaxConcurrencyRetries -> 409 if exhausted

// UpdateCharacterIndexAsync: remove from ActiveQuestIds
READ _characterIndex:"char:{questorCharacterId}" [with ETag]
ETAG-WRITE _characterIndex:"char:{questorCharacterId}"
 -> retry up to MaxConcurrencyRetries

CALL IContractClient.TerminateContractInstanceAsync(...) // best-effort
PUBLISH "quest.abandoned" { questInstanceId, questCode, abandoningCharacterId }
RETURN (200, QuestInstanceResponse)
```

### GetQuest
POST /quest/get | Roles: [user]

```
READ _instanceStore:"inst:{questInstanceId}" -> 404 if null
// MapToInstanceResponseAsync: loads definition via cache read-through,
// reads all objective progress from _progressStore
RETURN (200, QuestInstanceResponse)
```

### ListQuests
POST /quest/list | Roles: [user]

```
QUERY _instanceStore WHERE QuestorCharacterIds contains body.characterId
// In-memory status filter if body.statuses provided
// In-memory pagination: Skip(offset).Take(limit)
// Per result: load definition (cache read-through) + objective progress
RETURN (200, ListQuestsResponse { quests, total })
```

### ListAvailableQuests
POST /quest/list-available | Roles: [user]

```
QUERY _definitionStore WHERE !IsDeprecated
 AND (gameServiceId filter) AND (questGiverCharacterId filter)
READ _characterIndex:"char:{characterId}"

// Build activeDefinitionIds set from character index
FOREACH activeQuestId in index.ActiveQuestIds
 READ _instanceStore:"inst:{activeQuestId}"

FOREACH definition in definitions
 // Filter: not already active
 IF definition.DefinitionId in activeDefinitionIds -> skip
 // Filter: not completed (if non-repeatable)
 IF !definition.Repeatable AND questCode in index.CompletedQuestCodes -> skip
 // Filter: not on cooldown (if repeatable)
 IF definition.Repeatable
 READ _cooldownStore:"cd:{characterId}:{questCode}"
 IF cooldown active -> skip
 // Filter: prerequisites met (see AcceptQuest CheckPrerequisitesAsync)
 // May CALL ICurrencyClient, IItemClient, IInventoryClient, IPrerequisiteProviderFactory
 IF any prerequisite fails -> skip

RETURN (200, ListAvailableQuestsResponse { available })
```

### GetQuestLog
POST /quest/log | Roles: [user]

```
READ _characterIndex:"char:{characterId}"

FOREACH activeQuestId in index.ActiveQuestIds
 READ _instanceStore:"inst:{questId}"
 // Load definition via cache read-through
 // Apply optional category filter
 FOREACH objective in definition.Objectives
 READ _progressStore:"prog:{questId}:{objectiveCode}"
 // Apply RevealBehavior filter:
 // Always -> show, OnProgress -> show if currentCount > 0,
 // OnComplete -> show if isComplete, Never -> hide
 // Calculate overallProgress from required (non-optional) objectives

// CompletedCount from index.CompletedQuestCodes.Count
QUERY _instanceStore WHERE characterId AND Status == Failed // for failedCount

RETURN (200, QuestLogResponse { activeQuests, completedCount, failedCount })
```

### ReportObjectiveProgress
POST /quest/objective/progress | Roles: [user]

```
READ _instanceStore:"inst:{questInstanceId}" -> 404 if null
-> 400 if not Active

READ _progressStore:"prog:{questInstanceId}:{objectiveCode}" [with ETag]
 -> 404 if null
IF already complete
 RETURN (200, ObjectiveProgressResponse { milestoneCompleted: false })
IF body.trackedEntityId in progress.TrackedEntityIds // deduplication
 RETURN (200, ObjectiveProgressResponse { milestoneCompleted: false })

// Increment currentCount by body.incrementBy, cap at requiredCount
// Add trackedEntityId to TrackedEntityIds if provided
ETAG-WRITE _progressStore:"prog:{questInstanceId}:{objectiveCode}"
 -> retry up to MaxConcurrencyRetries -> 409 if exhausted

IF milestone just completed (transition from incomplete to complete)
 CALL IContractClient.CompleteMilestoneAsync(...) // best-effort

PUBLISH "quest.objective.progressed" { questInstanceId, questCode, objectiveCode, currentCount, requiredCount, isComplete }
RETURN (200, ObjectiveProgressResponse { milestoneCompleted })
```

### ForceCompleteObjective
POST /quest/objective/complete | Roles: [admin]

```
READ _instanceStore:"inst:{questInstanceId}" -> 404 if null
// Note: does NOT check Active status — can force-complete on any status

READ _progressStore:"prog:{questInstanceId}:{objectiveCode}" [with ETag]
 -> 404 if null
IF already complete
 RETURN (200, ObjectiveProgressResponse { milestoneCompleted: false })

// Set CurrentCount = RequiredCount, IsComplete = true
ETAG-WRITE _progressStore:"prog:{questInstanceId}:{objectiveCode}"
 -> retry up to MaxConcurrencyRetries -> 409 if exhausted

CALL IContractClient.CompleteMilestoneAsync(...) // best-effort
// Note: publishes NO event (unlike ReportObjectiveProgress)
RETURN (200, ObjectiveProgressResponse { milestoneCompleted: true })
```

### GetObjectiveProgress
POST /quest/objective/get | Roles: [user]

```
READ _instanceStore:"inst:{questInstanceId}" -> 404 if null
READ _progressStore:"prog:{questInstanceId}:{objectiveCode}" -> 404 if null
RETURN (200, ObjectiveProgressResponse { milestoneCompleted: false })
```

### HandleMilestoneCompleted
POST /quest/internal/milestone-completed | Roles: []

```
// Prebound API callback from Contract milestone completion
QUERY _instanceStore WHERE ContractInstanceId == body.contractInstanceId
// Near-stub: no functional processing beyond the query
// Real milestone processing occurs in contract.milestone.completed event handler
RETURN (200)
```

### HandleQuestCompleted
POST /quest/internal/quest-completed | Roles: []

```
// Prebound API callback from Contract fulfillment
QUERY _instanceStore WHERE ContractInstanceId == body.contractInstanceId
IF null -> RETURN (200) // not quest-related

// CompleteQuestAsync helper
READ _instanceStore:"inst:{questInstanceId}" [with ETag]
IF null or not Active -> RETURN (200)
// Set Status = Completed, CompletedAt = UtcNow
ETAG-WRITE _instanceStore:"inst:{questInstanceId}"
 -> retry up to MaxConcurrencyRetries

// UpdateCharacterIndexAsync
READ _characterIndex:"char:{characterId}" [with ETag]
// Remove from ActiveQuestIds, add questCode to CompletedQuestCodes
ETAG-WRITE _characterIndex:"char:{characterId}"
 -> retry up to MaxConcurrencyRetries

IF definition.Repeatable
 WRITE _cooldownStore:"cd:{characterId}:{questCode}" <- CooldownEntry
 (TTL: definition.CooldownSeconds)

PUBLISH "quest.completed" { questInstanceId, definitionId, questCode, questorCharacterIds, gameServiceId }
RETURN (200)
```

### GetCompressData
POST /quest/get-compress-data | Roles: [developer]

```
// Called by lib-resource during character compression/archival
READ _characterIndex:"char:{characterId}"

FOREACH activeQuestId in index.ActiveQuestIds
 READ _instanceStore:"inst:{questId}"
 // Load definition via cache read-through
 FOREACH objective in definition.Objectives
 READ _progressStore:"prog:{questId}:{objectiveCode}"

// Build category breakdown from completed quest codes
FOREACH questCode in index.CompletedQuestCodes
 QUERY _definitionStore WHERE Code == questCode // MySQL per code

RETURN (200, QuestArchive { characterId, activeQuests, completedQuests, questCategories })
```

### DeleteByCharacter
POST /quest/delete-by-character | Roles: [] (service-to-service only)

```
// Called by lib-resource during character deletion cleanup (CASCADE)
READ _characterIndex:"char:{characterId}"

// Query ALL instances by character (MySQL) — index only tracks active quests,
// but completed/abandoned instances also hold registered references
QUERY _instanceStore WHERE QuestorCharacterIds CONTAINS characterId

FOREACH instance in allInstances // per-item try-catch
  // Load definition for objective/event metadata
  definition = GetDefinitionModelAsync(instance.DefinitionId)

  IF instance.QuestorCharacterIds.Count <= 1 (sole questor)
    // Full instance deletion path
    IF status == Active
      // Abandon: set Status = Abandoned, terminate contract, publish quest.abandoned
      READ _instanceStore:"inst:{questInstanceId}" [with ETag]
      ETAG-WRITE (Status=Abandoned, CompletedAt=UtcNow)
      TRY CALL IContractClient.TerminateContractInstanceAsync(...)
      PUBLISH quest.abandoned { questInstanceId, questCode, abandoningCharacterId }
      PUBLISH quest.instance.updated { changedFields: [status, completedAt] }
    // Delete ALL objective progress
    FOREACH objective in definition.Objectives
      DELETE _progressStore:"prog:{questInstanceId}:{objectiveCode}"
    // Full deletion via DeleteInstanceRecordAsync
    //   Deletes instance record, removes from character index,
    //   removes from reverse index (def-inst:{definitionId}),
    //   publishes quest.instance.deleted lifecycle event
    CALL DeleteInstanceRecordAsync(instance, characterId)
  ELSE (multi-questor)
    // Partial cleanup: remove character without destroying quest
    IF status == Active
      READ _instanceStore:"inst:{questInstanceId}" [with ETag]
      // Remove characterId from QuestorCharacterIds
      ETAG-WRITE _instanceStore:"inst:{questInstanceId}"
      PUBLISH quest.instance.updated { changedFields: [questorCharacterIds] }
    // Delete character-specific progress records
    FOREACH objective in definition.Objectives
      DELETE _progressStore:"prog:{questInstanceId}:{objectiveCode}"
    // Update character index (remove quest from active list)
    READ _characterIndex:"char:{characterId}" [with ETag]
    ETAG-WRITE _characterIndex (remove from ActiveQuestIds)

  // Unregister character reference for ALL instances
  TRY CALL IResourceClient.UnregisterReferenceAsync(...)
  CATCH ApiException -> log warning, continue

FOREACH questCode in index.CompletedQuestCodes // per-item try-catch
  DELETE _cooldownStore:"cd:{characterId}:{questCode}"

DELETE _characterIndex:"char:{characterId}"

RETURN (200, DeleteByCharacterResponse { instancesAbandoned, progressRecordsDeleted, cooldownsDeleted })
```

### CleanDeprecatedQuestDefinitions
POST /quest/definition/clean-deprecated | Roles: [admin]

```
QUERY _definitionStore WHERE IsDeprecated == true
IF count == 0
  RETURN (200, CleanDeprecatedResponse { cleaned=0, remaining=0, errors=0, cleanedIds=[] })

result = DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
  deprecatedDefinitions,
  getEntityId: d => d.DefinitionId,
  getDeprecatedAt: d => d.DeprecatedAt,
  hasInstancesAsync: (d, ct) =>
    // O(1) reverse index check (replaces full MySQL query)
    _instanceStringStore.HasStringListEntriesAsync(BuildDefinitionInstanceIndexKey(d.DefinitionId), ct),
  deleteAndPublishAsync: (d, ct) =>
    DELETE _definitionStore:"def:{definitionId}"
    DELETE _definitionCache:"def:{definitionId}"
    // Defensive reverse index cleanup
    DELETE _instanceStringStore:BuildDefinitionInstanceIndexKey(definitionId)
    PUBLISH quest.definition.deleted (QuestDefinitionDeletedEvent with all fields + deleteReason),
  gracePeriodDays: body.GracePeriodDays,
  dryRun: body.DryRun
)

RETURN (200, CleanDeprecatedResponse { cleaned, remaining, errors, cleanedIds })
```

---

## Background Services

No background services.
