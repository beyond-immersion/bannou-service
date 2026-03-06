# Achievement Implementation Map

> **Plugin**: lib-achievement
> **Schema**: schemas/achievement-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](../plugins/ACHIEVEMENT.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-achievement |
| Layer | L4 GameFeatures |
| Endpoints | 12 |
| State Stores | achievement-definition (Redis), achievement-progress (Redis), achievement-lock (Redis) |
| Events Published | 5 (achievement.definition.created, achievement.definition.updated, achievement.progress.unlocked, achievement.progress.updated, achievement.platform.synced) |
| Events Consumed | 3 (analytics.score.updated, analytics.milestone.reached, leaderboard.rank.changed) |
| Client Events | 2 (achievement.progress.unlocked, achievement.progress.milestone-reached) |
| Background Services | 1 (RarityCalculationService) |

---

## State

**Store**: `achievement-definition` (Backend: Redis, ICacheableStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{gameServiceId}:{achievementId}` | `AchievementDefinitionData` | Achievement definition including type, points, platforms, prerequisites, earned count, rarity stats |
| `achievement-definitions:{gameServiceId}` | `Set<string>` | Index of all achievement IDs for a game service |
| `achievement-game-services` | `Set<string>` | Index of all game service IDs with definitions (for rarity worker) |

**Store**: `achievement-progress` (Backend: Redis, IStateStore)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{gameServiceId}:{entityType}:{entityId}` | `EntityProgressData` | All achievement progress for an entity — dictionary of achievementId to progress data, plus total points |

**Store**: `achievement-lock` (Backend: Redis) — used via `IDistributedLockProvider` only

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{gameServiceId}:{entityType}:{entityId}` | lock | Mutual exclusion for compound progress/unlock operations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for definitions (ICacheableStateStore) and progress (IStateStore) |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks on progress keys for progress/unlock operations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing service events (definition CRUD, unlock, progress, platform sync) |
| lib-messaging (IEventConsumer) | L0 | Hard | Subscribing to analytics and leaderboard events for auto-unlock |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers and event handlers |
| lib-connect (IEntitySessionRegistry) | L1 | Hard | Push client events (unlock, progress milestone) to entity WebSocket sessions |
| lib-resource (IResourceClient) | L1 | Hard | Reference tracking for character entities; cleanup callback registration |
| lib-account (IAccountClient) | L1 | Hard | SteamAchievementSync queries account auth methods for Steam external IDs |

**Notes**:
- `IAccountClient` is injected into `SteamAchievementSync` (not directly into `AchievementService`). DI resolution fails at startup if missing.
- Achievement participates in Quest's prerequisite system via `AchievementPrerequisiteProviderFactory` (`IPrerequisiteProviderFactory`), discovered by Quest via DI collection.
- Achievement registers `x-references` targeting `character` with sourceType `achievement-progress`. Cleanup endpoint `/achievement/cleanup-by-character` deletes progress across all game services when a character is deleted.
- `IEnumerable<IPlatformAchievementSync>` is an achievement-internal collection pattern (Internal, Steam, Xbox stub, PlayStation stub), not a `bannou-service/Providers/` interface.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `achievement.definition.created` | `AchievementDefinitionCreatedEvent` | CreateAchievementDefinition — full definition snapshot |
| `achievement.definition.updated` | `AchievementDefinitionUpdatedEvent` | UpdateAchievementDefinition, DeprecateAchievementDefinition — includes `changedFields` array |
| `achievement.progress.unlocked` | `AchievementUnlockedEvent` | UpdateAchievementProgress (on auto-unlock at target), UnlockAchievement — includes isRare, rarity% |
| `achievement.progress.updated` | `AchievementProgressUpdatedEvent` | UpdateAchievementProgress — includes previous/new/target progress, percent complete |
| `achievement.platform.synced` | `AchievementPlatformSyncedEvent` | UnlockAchievement (auto-sync), SyncPlatformAchievements — includes platform, success, errorMessage |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `analytics.score.updated` | `HandleScoreUpdatedAsync` | Loads all definitions for gameServiceId, filters to Progressive with matching scoreType, calls UpdateAchievementProgress with delta as increment |
| `analytics.milestone.reached` | `HandleMilestoneReachedAsync` | Loads all definitions for gameServiceId, filters to non-Progressive with matching milestoneType/value/name, calls UnlockAchievement for each match |
| `leaderboard.rank.changed` | `HandleRankChangedAsync` | Loads all definitions for gameServiceId, filters to non-Progressive with matching leaderboardId and newRank <= rankThreshold, calls UnlockAchievement for each match |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AchievementService>` | Structured logging |
| `AchievementServiceConfiguration` | All configurable thresholds, credentials, feature flags |
| `IStateStoreFactory` | Constructor-consumed to acquire `_definitionStore` and `_progressStore` |
| `IMessageBus` | Service event and error event publishing |
| `IEventConsumer` | Registers handlers for analytics/leaderboard events |
| `IDistributedLockProvider` | Distributed locks on progress keys |
| `ITelemetryProvider` | Telemetry spans |
| `IEntitySessionRegistry` | Client event push to entity WebSocket sessions |
| `IEnumerable<IPlatformAchievementSync>` | Platform sync providers (Internal, Steam, Xbox stub, PlayStation stub) |
| `RarityCalculationService` | Background worker for periodic rarity recalculation |
| `AchievementPrerequisiteProviderFactory` | `IPrerequisiteProviderFactory` singleton — enables Quest (L2) to validate achievement-based prerequisites via DI collection |

---

## Method Index

> **Roles column**: `[]` = service-to-service only (not exposed via WebSocket). See SCHEMA-RULES.md § x-permissions.

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateAchievementDefinition | POST /achievement/definition/create | [developer] | definition, definition-index, game-service-index | achievement.definition.created |
| GetAchievementDefinition | POST /achievement/definition/get | [] | - | - |
| ListAchievementDefinitions | POST /achievement/definition/list | [user] | - | - |
| UpdateAchievementDefinition | POST /achievement/definition/update | [developer] | definition | achievement.definition.updated |
| DeprecateAchievementDefinition | POST /achievement/definition/deprecate | [developer] | definition | achievement.definition.updated |
| GetAchievementProgress | POST /achievement/progress/get | [user] | - | - |
| UpdateAchievementProgress | POST /achievement/progress/update | [] | progress, definition (earned count) | achievement.progress.updated, achievement.progress.unlocked |
| UnlockAchievement | POST /achievement/unlock | [] | progress, definition (earned count) | achievement.progress.unlocked, achievement.platform.synced |
| ListUnlockedAchievements | POST /achievement/list-unlocked | [user] | - | - |
| SyncPlatformAchievements | POST /achievement/platform/sync | [admin] | - | achievement.platform.synced |
| GetPlatformSyncStatus | POST /achievement/platform/status | [] | - | - |
| CleanupByCharacter | POST /achievement/cleanup-by-character | [] | progress | - |

---

## Methods

### CreateAchievementDefinition
POST /achievement/definition/create | Roles: [developer]

```
READ _definitionStore:{gameServiceId}:{achievementId}          -> 409 if exists
WRITE _definitionStore:{gameServiceId}:{achievementId}         <- AchievementDefinitionData from request
  // EntityTypes defaults to [Account] if not provided
  // Platforms defaults to [Internal] if not provided
WRITE _definitionStore (set):achievement-definitions:{gameServiceId} <- achievementId
WRITE _definitionStore (set):achievement-game-services         <- gameServiceId
PUBLISH achievement.definition.created { full definition snapshot }
RETURN (200, AchievementDefinitionResponse)
```

### GetAchievementDefinition
POST /achievement/definition/get | Roles: []

```
READ _definitionStore:{gameServiceId}:{achievementId}          -> 404 if null
RETURN (200, AchievementDefinitionResponse)
```

### ListAchievementDefinitions
POST /achievement/definition/list | Roles: [user]

```
READ _definitionStore (set):achievement-definitions:{gameServiceId}
  // Returns empty list (not 404) if set is empty
FOREACH achievementId in set
  READ _definitionStore:{gameServiceId}:{achievementId}
    // Skip if null (orphaned index entry)
  IF body.Platform set AND definition.Platforms !contains it -> skip
  IF body.AchievementType set AND doesn't match -> skip
  IF body.IsActive set AND doesn't match -> skip
  IF !body.IncludeHidden AND type is Hidden or Secret -> skip
  IF !body.IncludeDeprecated AND definition.IsDeprecated -> skip
// Sort alphabetically by achievementId (case-insensitive)
RETURN (200, ListAchievementDefinitionsResponse)
```

### UpdateAchievementDefinition
POST /achievement/definition/update | Roles: [developer]

```
READ _definitionStore:{gameServiceId}:{achievementId} [with ETag]  -> 404 if null
// Updateable: DisplayName, Description, IsActive, PlatformMappings,
//   ScoreType, MilestoneType, MilestoneValue, MilestoneName, LeaderboardId, RankThreshold
// Dirty-tracking: only apply fields that differ from current values
IF changedFields is empty
  RETURN (200, AchievementDefinitionResponse)  // no-op, no write
ETAG-WRITE _definitionStore:{gameServiceId}:{achievementId}    -> 409 if conflict
PUBLISH achievement.definition.updated { definition snapshot, changedFields }
RETURN (200, AchievementDefinitionResponse)
```

### DeprecateAchievementDefinition
POST /achievement/definition/deprecate | Roles: [developer]

```
READ _definitionStore:{gameServiceId}:{achievementId} [with ETag]  -> 404 if null
IF definition.IsDeprecated
  RETURN (200, AchievementDefinitionResponse)  // idempotent, no write
// Set IsDeprecated=true, DeprecatedAt=now, DeprecationReason from request
ETAG-WRITE _definitionStore:{gameServiceId}:{achievementId}    -> 409 if conflict
PUBLISH achievement.definition.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, AchievementDefinitionResponse)
```

### GetAchievementProgress
POST /achievement/progress/get | Roles: [user]

```
READ _progressStore:{gameServiceId}:{entityType}:{entityId}
  // Null-coalesces to empty EntityProgressData (returns 200, not 404)
IF body.AchievementId specified
  // Single achievement lookup
  READ _definitionStore:{gameServiceId}:{achievementId}
    // Skip if definition missing
ELSE
  // All achievements for entity
  FOREACH achievementId in entityProgress.Achievements
    READ _definitionStore:{gameServiceId}:{achievementId}
      // Skip entries where definition is missing (orphan cleanup)
RETURN (200, AchievementProgressResponse { progress, totalPoints, unlockedCount })
```

### UpdateAchievementProgress
POST /achievement/progress/update | Roles: []

```
READ _definitionStore:{gameServiceId}:{achievementId}          -> 404 if null
IF definition.IsDeprecated                                     -> 400
IF body.EntityType not in definition.EntityTypes               -> 400
IF definition.AchievementType != Progressive OR !ProgressTarget -> 400
LOCK achievement-lock:{gameServiceId}:{entityType}:{entityId}  -> 409 if fails
  READ _progressStore:{gameServiceId}:{entityType}:{entityId}
    // Null-coalesces to empty EntityProgressData
  IF achievement already unlocked
    RETURN (200, UpdateAchievementProgressResponse { unlocked=false })  // short-circuit
  // Increment progress, clamp to ProgressTarget
  IF newProgress >= targetProgress
    // Auto-unlock: set UnlockedAt, IsUnlocked, clamp progress
    WRITE _progressStore:{gameServiceId}:{entityType}:{entityId} <- updated progress [with TTL if configured]
    // IncrementEarnedCountAsync: ETag-retry loop (config.EarnedCountRetryAttempts)
    READ _definitionStore:{gameServiceId}:{achievementId} [with ETag]
    ETAG-WRITE _definitionStore:{gameServiceId}:{achievementId}  // EarnedCount++
    PUBLISH achievement.progress.unlocked { gameServiceId, achievementId, entityId, entityType, points, totalPoints, isRare, rarity }
    PUSH achievement.progress.unlocked -> entity sessions
  ELSE
    WRITE _progressStore:{gameServiceId}:{entityType}:{entityId} <- updated progress [with TTL if configured]
  PUBLISH achievement.progress.updated { previousProgress, newProgress, targetProgress, percentComplete }
  // Milestone client events at configurable thresholds (default: 25%, 50%, 75%)
  FOREACH milestone threshold crossed by this increment
    PUSH achievement.progress.milestone-reached -> entity sessions
RETURN (200, UpdateAchievementProgressResponse { previousProgress, newProgress, targetProgress, unlocked, unlockedAt })
```

### UnlockAchievement
POST /achievement/unlock | Roles: []

```
READ _definitionStore:{gameServiceId}:{achievementId}          -> 404 if null
IF definition.IsDeprecated                                     -> 400
IF body.EntityType not in definition.EntityTypes               -> 400
// Prerequisite check (before lock)
IF definition.Prerequisites not empty
  READ _progressStore:{gameServiceId}:{entityType}:{entityId}
  FOREACH prerequisite in definition.Prerequisites
    IF not unlocked in progress                                -> 400 "prerequisites not met"
LOCK achievement-lock:{gameServiceId}:{entityType}:{entityId}  -> 409 if fails
  READ _progressStore:{gameServiceId}:{entityType}:{entityId}
    // Null-coalesces to empty EntityProgressData
  IF achievement already unlocked
    RETURN (200, UnlockAchievementResponse { unlockedAt })     // idempotent
  // Set IsUnlocked, UnlockedAt, CurrentProgress = ProgressTarget ?? 1
  WRITE _progressStore:{gameServiceId}:{entityType}:{entityId} <- updated progress [with TTL if configured]
  // IncrementEarnedCountAsync: ETag-retry loop
  READ _definitionStore:{gameServiceId}:{achievementId} [with ETag]
  ETAG-WRITE _definitionStore:{gameServiceId}:{achievementId}  // EarnedCount++
  PUBLISH achievement.progress.unlocked { gameServiceId, achievementId, entityId, entityType, points, totalPoints, isRare, rarity }
  PUSH achievement.progress.unlocked -> entity sessions
  // Platform sync (if AutoSyncOnUnlock AND !body.SkipPlatformSync)
  IF config.AutoSyncOnUnlock AND !body.SkipPlatformSync
    FOREACH platform in definition.Platforms (excluding Internal)
      // SyncAchievementToPlatformAsync:
      CALL syncProvider.IsLinkedAsync(entityId)
      IF linked
        CALL syncProvider.GetExternalIdAsync(entityId)
        // ExecutePlatformUnlockWithRetriesAsync:
        CALL syncProvider.UnlockAsync(externalId, platformAchievementId)
          // Retries config.SyncRetryAttempts times with config.SyncRetryDelaySeconds delay
        PUBLISH achievement.platform.synced { platform, success, errorMessage }
RETURN (200, UnlockAchievementResponse { unlockedAt, platformSyncStatus })
```

### ListUnlockedAchievements
POST /achievement/list-unlocked | Roles: [user]

```
READ _progressStore:{gameServiceId}:{entityType}:{entityId}
  // Returns 200 with empty list (not 404) if null
FOREACH achievement in entityProgress.Achievements WHERE IsUnlocked
  IF UnlockedAt is null -> skip (data inconsistency, logged as error)
  READ _definitionStore:{gameServiceId}:{achievementId}
    // Skip if definition missing (orphan)
  IF !body.IncludeDeprecated AND definition.IsDeprecated -> skip
  IF body.Platform set AND definition.Platforms !contains it -> skip
// TotalPoints recomputed from definition Points values (not from stored total)
RETURN (200, ListUnlockedAchievementsResponse { achievements, totalPoints })
```

### SyncPlatformAchievements
POST /achievement/platform/sync | Roles: [admin]

```
IF body.EntityType != Account                                  -> 400
// Find sync provider for requested platform
IF no provider registered for body.Platform                    -> 400
IF !syncProvider.IsConfigured                                  -> 400
CALL syncProvider.IsLinkedAsync(body.EntityId)
IF not linked
  RETURN (200, SyncPlatformAchievementsResponse { synced=0, failed=0 })
CALL syncProvider.GetExternalIdAsync(body.EntityId)
IF externalId is null/empty
  RETURN (200, SyncPlatformAchievementsResponse { synced=0, failed=0 })
READ _progressStore:{gameServiceId}:{entityType}:{entityId}
  // Returns synced=0 if null or empty
FOREACH achievement in entityProgress.Achievements WHERE IsUnlocked
  READ _definitionStore:{gameServiceId}:{achievementId}
  // Look up platformAchievementId from definition.PlatformMappings
  // ExecutePlatformUnlockWithRetriesAsync with configurable retries
  CALL syncProvider.UnlockAsync(externalId, platformAchievementId)
  PUBLISH achievement.platform.synced { platform, success, errorMessage }
RETURN (200, SyncPlatformAchievementsResponse { platform, synced, failed, errors })
```

### GetPlatformSyncStatus
POST /achievement/platform/status | Roles: []

```
IF body.EntityType != Account                                  -> 400
FOREACH syncProvider in _platformSyncs
  IF body.Platform set AND doesn't match -> skip
  IF !syncProvider.IsConfigured -> skip
  CALL syncProvider.IsLinkedAsync(body.EntityId)
  IF linked
    CALL syncProvider.GetExternalIdAsync(body.EntityId)
  // SyncedCount, PendingCount, FailedCount are all hardcoded 0
  // LastSyncAt, LastError are null (per-entity sync history not tracked)
RETURN (200, PlatformSyncStatusResponse { entityId, entityType, platforms })
```

### CleanupByCharacter
POST /achievement/cleanup-by-character | Roles: []

```
// Called by lib-resource during character deletion cleanup
READ _definitionStore (set):achievement-game-services
FOREACH gameServiceId in set
  READ _progressStore:{gameServiceId}:Character:{characterId}
  IF exists
    DELETE _progressStore:{gameServiceId}:Character:{characterId}
    progressRecordsDeleted++
RETURN (200, CleanupByCharacterResponse { progressRecordsDeleted })
```

---

## Background Services

### RarityCalculationService
**Interval**: `config.RarityCalculationIntervalMinutes` (default: 60 minutes)
**Startup Delay**: `config.RarityCalculationStartupDelaySeconds` (default: 30 seconds)
**Purpose**: Periodically recalculate `RarityPercent` on all achievement definitions

```
// Creates a new DI scope per cycle
READ _definitionStore (set):achievement-game-services
FOREACH gameServiceId in set
  READ _definitionStore (set):achievement-definitions:{gameServiceId}
  FOREACH achievementId in set
    READ _definitionStore:{gameServiceId}:{achievementId} [with ETag]
    IF definition.TotalEligibleEntities > 0 AND definition.EarnedCount >= 0
      // RarityPercent = EarnedCount / TotalEligibleEntities * 100.0
      ETAG-WRITE _definitionStore:{gameServiceId}:{achievementId}
        // Silently skip on ETag conflict (retries next interval)
```

**Note**: `TotalEligibleEntities` is never populated by any service endpoint. The rarity calculation branch (`TotalEligibleEntities > 0`) will never execute until this field is manually set or an automation is implemented.
