# Achievement Plugin Deep Dive

> **Plugin**: lib-achievement
> **Schema**: schemas/achievement-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: achievement-definition (Redis), achievement-progress (Redis)

## Overview

The Achievement plugin (L4 GameFeatures) provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service for periodic rarity recalculation.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | All persistence - definitions, progress records, index sets. Uses ETag-based optimistic concurrency for definition updates |
| lib-state (IDistributedLockProvider) | Distributed locks (configurable via LockExpirySeconds, default 30s) for compound progress/unlock operations |
| lib-messaging (IMessageBus) | Publishing lifecycle events (unlocked, progress, sync, definition CRUD) and error events |
| lib-messaging (IEventConsumer) | Subscribing to analytics.score.updated, analytics.milestone.reached, leaderboard.rank.changed for auto-unlock |
| Account service (IAccountClient) | SteamAchievementSync queries account auth methods to find Steam external IDs |
| Permission service (via AchievementPermissionRegistration) | Registers its endpoint permission matrix on startup via messaging event |
| IHttpClientFactory | SteamAchievementSync makes HTTP calls to Steam Partner API (partner.steam-api.com) |
| bannou-service (MetadataHelper) | Utility class for converting metadata objects between different representations |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (None currently) | No other service subscribes to achievement events or calls AchievementClient in the current codebase |

The Achievement plugin is currently a leaf consumer - it reacts to external events but nothing subscribes to its output events. Game clients receive unlock notifications via the Connect service's client event push system.

## State Storage

**Stores**: `achievement-definition` (Redis, prefix `ach:def`), `achievement-progress` (Redis, prefix `ach:prog`)

| Key Pattern | Store | Data Type | Purpose |
|-------------|-------|-----------|---------|
| `{gameServiceId}:{achievementId}` | achievement-definition | `AchievementDefinitionData` | Achievement definition including type, points, platforms, prerequisites, earned count, rarity stats |
| `achievement-definitions:{gameServiceId}` | achievement-definition | `Set<string>` | Index of all achievement IDs for a game service (used for listing and event handler lookups) |
| `achievement-game-services` | achievement-definition | `Set<string>` | Index of all game service IDs with definitions (used by RarityCalculationService to iterate all games) |
| `{gameServiceId}:{entityType}:{entityId}` | achievement-progress | `EntityProgressData` | All achievement progress for an entity - dictionary of achievementId to progress data, plus total points. Saved with configurable TTL |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `achievement.unlocked` | `AchievementUnlockedEvent` | Achievement unlocked via progress completion or direct unlock. Includes rarity information (IsRare, Rarity%) |
| `achievement.progress.updated` | `AchievementProgressUpdatedEvent` | Progress incremented on a progressive achievement. Includes previous/new/target progress and percent complete |
| `achievement.platform.synced` | `AchievementPlatformSyncedEvent` | After each platform sync attempt (success or failure). Includes platform-specific achievement ID |
| `achievement.definition.created` | `AchievementDefinitionCreatedEvent` | New achievement definition created. Includes type, points, platforms list |
| `achievement.definition.updated` | `AchievementDefinitionUpdatedEvent` | Definition fields updated (displayName, description, isActive, platformIds) |
| `achievement.definition.deleted` | `AchievementDefinitionDeletedEvent` | Definition removed from store and index |

### Consumed Events

| Topic | Event Type | Handler | Behavior |
|-------|-----------|---------|----------|
| `analytics.score.updated` | `AnalyticsScoreUpdatedEvent` | `HandleScoreUpdatedAsync` | Finds Progressive achievements with matching `scoreType` in metadata, increments progress by delta (must be positive integer) |
| `analytics.milestone.reached` | `AnalyticsMilestoneReachedEvent` | `HandleMilestoneReachedAsync` | Finds non-Progressive achievements with matching `milestoneType` metadata, optionally filters by `milestoneValue` and `milestoneName`, then unlocks |
| `leaderboard.rank.changed` | `LeaderboardRankChangedEvent` | `HandleRankChangedAsync` | Finds non-Progressive achievements with matching `leaderboardId` metadata, unlocks when `newRank <= rankThreshold` |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| SteamApiKey | `ACHIEVEMENT_STEAM_API_KEY` | null | Steam Web API publisher key (sync disabled if unset) |
| SteamAppId | `ACHIEVEMENT_STEAM_APP_ID` | null | Steam App ID for achievement mapping (sync disabled if unset) |
| XboxClientId | `ACHIEVEMENT_XBOX_CLIENT_ID` | null | Xbox Live client ID (not implemented) |
| XboxClientSecret | `ACHIEVEMENT_XBOX_CLIENT_SECRET` | null | Xbox Live client secret (not implemented) |
| PlayStationClientId | `ACHIEVEMENT_PLAYSTATION_CLIENT_ID` | null | PlayStation Network client ID (not implemented) |
| PlayStationClientSecret | `ACHIEVEMENT_PLAYSTATION_CLIENT_SECRET` | null | PlayStation Network client secret (not implemented) |
| MockPlatformSync | `ACHIEVEMENT_MOCK_PLATFORM_SYNC` | false | Returns success without API calls (service-level short-circuit in ExecutePlatformUnlockWithRetriesAsync) |
| AutoSyncOnUnlock | `ACHIEVEMENT_AUTO_SYNC_ON_UNLOCK` | true | Automatically sync to external platforms when an achievement is unlocked |
| SyncRetryAttempts | `ACHIEVEMENT_SYNC_RETRY_ATTEMPTS` | 3 | Number of retry attempts for failed platform API calls |
| SyncRetryDelaySeconds | `ACHIEVEMENT_SYNC_RETRY_DELAY_SECONDS` | 60 | Delay between retry attempts |
| LockExpirySeconds | `ACHIEVEMENT_LOCK_EXPIRY_SECONDS` | 30 | Expiry time for distributed locks on progress/unlock operations |
| EarnedCountRetryAttempts | `ACHIEVEMENT_EARNED_COUNT_RETRY_ATTEMPTS` | 3 | Retry attempts for ETag conflicts when incrementing earned count |
| ProgressTtlSeconds | `ACHIEVEMENT_PROGRESS_TTL_SECONDS` | 0 | TTL on progress records in Redis (0 = no expiry, progress persists indefinitely; positive value enables cache-like expiry) |
| RarityCalculationStartupDelaySeconds | `ACHIEVEMENT_RARITY_CALCULATION_STARTUP_DELAY_SECONDS` | 30 | Delay before first rarity recalculation after startup |
| RarityCalculationIntervalMinutes | `ACHIEVEMENT_RARITY_CALCULATION_INTERVAL_MINUTES` | 60 | Interval between periodic rarity recalculations |
| RareThresholdPercent | `ACHIEVEMENT_RARE_THRESHOLD_PERCENT` | 5.0 | Percentage below which an achievement is flagged as rare |
| RarityThresholdEarnedCount | `ACHIEVEMENT_RARITY_THRESHOLD_EARNED_COUNT` | 100 | Earned count below which an achievement is considered rare regardless of percentage |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `IMessageBus` | Publishes achievement events and error events via TryPublishAsync/TryPublishErrorAsync |
| `IStateStoreFactory` | Retrieves typed state stores for definitions and progress |
| `ILogger<AchievementService>` | Structured logging throughout |
| `AchievementServiceConfiguration` | All configurable thresholds, credentials, and feature flags |
| `IEventConsumer` | Registers handlers for analytics/leaderboard events in constructor |
| `IEnumerable<IPlatformAchievementSync>` | Collection of platform sync providers (Steam, Xbox, PlayStation, Internal). Service checks `IsConfigured` to skip unconfigured providers |
| `IDistributedLockProvider` | Distributed locks for progress updates and unlock operations (30s TTL) |
| `RarityCalculationService` (BackgroundService) | Periodically iterates all definitions and recalculates rarity percentages based on TotalEligibleEntities and EarnedCount |
| `MetadataHelper` (bannou-service) | Utility for safe reading of achievement metadata dictionaries (handles JsonElement, primitives, strings) |

## API Endpoints (Implementation Notes)

### Definitions group
Standard CRUD. Create checks for duplicates (409), maintains a set index per game service, and tracks the game service ID in a global index for rarity calculations. Update uses ETag-based optimistic concurrency and returns 409 on conflict. Delete removes from store and index but leaves historical progress records intact.

### Progress group
- **progress/get**: Returns progress for an entity. Filters out orphaned entries from deleted definitions (verifies each definition exists before including in response).
- **progress/update**: Progressive achievements only. Acquires distributed lock, increments progress, auto-unlocks at target, updates definition EarnedCount with optimistic concurrency retry (3 attempts). Already-unlocked achievements return OK with no change. Progress is stored permanently by default (ProgressTtlSeconds=0).
- **unlock**: Validates prerequisites (all must be unlocked), acquires distributed lock, sets progress to target. Returns `Unlocked=false` if already unlocked (idempotent). Increments EarnedCount with retry. Optionally triggers platform sync for configured platforms per definition.Platforms.
- **list-unlocked**: Filters from EntityProgressData, enriches with definition metadata. Filters orphaned entries. Supports platform filter.

### Platform Sync group
- **platform/sync**: Admin-only. Validates entity is Account type. Checks `IsConfigured` on provider (rejects unconfigured platforms with 400). Checks platform linkage via IPlatformAchievementSync. Iterates unlocked achievements, looks up platform-specific ID from PlatformIds map, executes with retry logic.
- **platform/status**: Iterates configured sync providers (skips `IsConfigured=false`), checks linkage status. Sync counts are hardcoded zeros (per-entity sync history not yet tracked).

## Visual Aid

```
                         Event-Driven Achievement Flow

  ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
  │   Analytics  │     │  Leaderboard │     │  Direct API  │
  │score.updated │     │ rank.changed │     │  /unlock     │
  └──────┬───────┘     └──────┬───────┘     └──────┬───────┘
         │                     │                    │
         ▼                     ▼                    ▼
  ┌─────────────────────────────────────────────────────────┐
  │              Achievement Service                        │
  │                                                         │
  │  1. Load definitions for gameServiceId (from index set) │
  │  2. Match via metadata: scoreType/leaderboardId/etc.    │
  │  3. Acquire distributed lock on progress key            │
  │  4. Update progress or unlock                           │
  │  5. Update EarnedCount on definition (ETag CAS)         │
  │  6. Save progress (permanent unless TTL configured)      │
  └────────────┬────────────────────────┬───────────────────┘
               │                        │
               ▼                        ▼
  ┌─────────────────────┐   ┌──────────────────────────────┐
  │  Publish Events     │   │  Platform Sync (if enabled)  │
  │  • unlocked         │   │  • Skip if !IsConfigured     │
  │  • progress.updated │   │  • Check linkage via Account │
  │  (includes rarity)  │   │  • Retry loop → Steam API    │
  └─────────────────────┘   │  • Publish sync event        │
                             └──────────────────────────────┘

           Background: RarityCalculationService
  ┌─────────────────────────────────────────────────────────┐
  │  Periodically (default 60min):                          │
  │  • Iterate game-services index → definitions index      │
  │  • Recalculate: EarnedCount / TotalEligibleEntities     │
  │  • Write RarityPercent + RarityCalculatedAt             │
  └─────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

| Item | Status | Notes |
|------|--------|-------|
| Xbox sync provider | Stub | `XboxAchievementSync` exists but `IsConfigured=false` and returns "not implemented". Skipped by service layer. Configuration properties (`XboxClientId`, `XboxClientSecret`) are defined in schema but unused - scaffolding for future implementation |
| PlayStation sync provider | Stub | `PlayStationAchievementSync` exists but `IsConfigured=false` and returns "not implemented". Skipped by service layer. Configuration properties (`PlayStationClientId`, `PlayStationClientSecret`) are defined in schema but unused - scaffolding for future implementation |
| Internal sync provider | Active | `InternalAchievementSync` is a no-op provider (`IsConfigured=true`) for internal-only achievements |
| Per-entity sync history tracking | Not implemented | `GetPlatformSyncStatusAsync` returns hardcoded zeros for synced/pending/failed counts |
| TotalEligibleEntities population | Not implemented | Field exists on definition but is never written to by any endpoint; rarity calc only works when manually populated |
| `SetProgressAsync` on platforms | Not called | IPlatformAchievementSync defines it, SteamAchievementSync implements it, but the service only calls `UnlockAsync` (never syncs incremental progress) |

## Potential Extensions

- **Sync history store**: Add a new state store to track per-entity platform sync history, enabling accurate status reporting instead of hardcoded zeros
- **TotalEligibleEntities automation**: Subscribe to subscription/account lifecycle events to maintain accurate eligible entity counts for rarity
- **Progressive platform sync**: Call `SetProgressAsync` when progress updates occur (not just on unlock), enabling Steam stats to track incremental progress
- **Achievement groups/categories**: Schema already has metadata field - could formalize grouping for UI presentation
- **Leaderboard integration on unlock**: Publish achievement points to a leaderboard for gamerscore-style rankings

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **T29 violation: definition `metadata` reads `scoreType`/`milestoneType`/`leaderboardId`/`rankThreshold` by convention**: `AchievementServiceEvents` reads `scoreType`, `milestoneType`, `milestoneValue`, `milestoneName`, `leaderboardId`, and `rankThreshold` keys from definition metadata via `MetadataHelper.TryGetString()` for analytics and leaderboard event matching. Schema claims "No Bannou plugin reads specific keys." These should be typed fields on the achievement definition schema.
   <!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/466 -->

2. **Dead code: `GetAchievementProgressKey` method is never called**
   - The method at `AchievementService.cs:95` generates keys in format `{gameServiceId}:{achievementId}:{entityType}:{entityId}` but is never invoked anywhere in the codebase.
   - Impact: No runtime issue, but represents code bloat/confusion. Should be removed or used if the key pattern was intended for a different purpose.

### Intentional Quirks (Documented Behavior)

1. **Rarity dual-threshold logic**: An achievement is "rare" if EarnedCount < RarityThresholdEarnedCount (100) OR RarityPercent < RareThresholdPercent (5%). A brand-new achievement with 0 earned is always rare regardless of percentage.

2. **Delete preserves progress data**: Deleting a definition removes it from store and index but leaves EntityProgressData intact. Orphaned entries filtered at read time.

3. **Event handlers load all definitions per event**: Each analytics/leaderboard event triggers `LoadAchievementDefinitionsAsync` which iterates the definition index set and loads each definition individually. This is intentional to ensure freshness but creates N+1 query pattern per event.

4. **Progress TTL default is infinite**: ProgressTtlSeconds defaults to 0 meaning progress records never expire. This is intentional for persistent progress tracking but operators should be aware of storage growth.

### Design Considerations (Requires Planning)

- **Event handler N+1 query pattern**: Each analytics/leaderboard event triggers `LoadAchievementDefinitionsAsync` which loads every definition individually from Redis (one GetAsync per achievement in the index). No caching layer exists - high-frequency events could generate significant Redis traffic. A cache with invalidation on definition CRUD would improve this but requires careful design around coherency across service instances.

- **Platform sync is fire-and-forget on unlock**: When `AutoSyncOnUnlock=true`, platform syncs happen inline during unlock but failures don't prevent the local unlock from succeeding. Retry logic exists but if all retries fail, the sync is marked failed in the event and the achievement stays locally unlocked but not synced. No retry queue exists for permanently failed syncs.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Active

- **2026-02-22**: T29 violation — definition metadata reads 6 keys by convention (`scoreType`, `milestoneType`, `milestoneValue`, `milestoneName`, `leaderboardId`, `rankThreshold`). Needs typed fields on achievement definition schema. [#466](https://github.com/beyond-immersion/bannou-service/issues/466)
