# Achievement Plugin Deep Dive

> **Plugin**: lib-achievement
> **Schema**: schemas/achievement-api.yaml
> **Version**: 1.0.0
> **State Stores**: achievement-definition (Redis), achievement-progress (Redis)

## Overview

The Achievement plugin provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service that periodically recalculates rarity percentages.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | All persistence - definitions, progress records, index sets. Uses ETag-based optimistic concurrency for definition updates |
| lib-state (IDistributedLockProvider) | 30-second distributed locks for compound progress/unlock operations |
| lib-messaging (IMessageBus) | Publishing lifecycle events (unlocked, progress, sync, definition CRUD) and error events |
| lib-messaging (IEventConsumer) | Subscribing to analytics.score.updated, analytics.milestone.reached, leaderboard.rank.changed for auto-unlock |
| Account service (IAccountClient) | SteamAchievementSync queries account auth methods to find Steam external IDs |
| Permission service (via AchievementPermissionRegistration) | Registers its endpoint permission matrix on startup via messaging event |
| IHttpClientFactory | SteamAchievementSync makes HTTP calls to Steam Partner API (partner.steam-api.com) |

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
| MockPlatformSync | `ACHIEVEMENT_MOCK_PLATFORM_SYNC` | false | Returns success without API calls (both at service level and within SteamAchievementSync) |
| AutoSyncOnUnlock | `ACHIEVEMENT_AUTO_SYNC_ON_UNLOCK` | true | Automatically sync to external platforms when an achievement is unlocked |
| SyncRetryAttempts | `ACHIEVEMENT_SYNC_RETRY_ATTEMPTS` | 3 | Number of retry attempts for failed platform API calls |
| SyncRetryDelaySeconds | `ACHIEVEMENT_SYNC_RETRY_DELAY_SECONDS` | 60 | Delay between retry attempts |
| ProgressCacheTtlSeconds | `ACHIEVEMENT_PROGRESS_CACHE_TTL_SECONDS` | 300 | TTL on progress records in Redis (5 minutes default) |
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
| `IEnumerable<IPlatformAchievementSync>` | Collection of platform sync providers (Steam, Xbox, PlayStation, Internal) |
| `IDistributedLockProvider` | Distributed locks for progress updates and unlock operations (30s TTL) |
| `RarityCalculationService` (BackgroundService) | Periodically iterates all definitions and recalculates rarity percentages based on TotalEligibleEntities and EarnedCount |
| `MetadataHelper` | Utility for safe reading of achievement metadata dictionaries (handles JsonElement, primitives, strings) |

## API Endpoints (Implementation Notes)

### Definitions group
Standard CRUD. Create checks for duplicates (409), maintains a set index per game service, and tracks the game service ID in a global index for rarity calculations. Update uses ETag-based optimistic concurrency and returns 409 on conflict. Delete removes from store and index but leaves historical progress records intact.

### Progress group
- **progress/update**: Progressive achievements only. Acquires distributed lock, increments progress, auto-unlocks at target, updates definition EarnedCount via ETag. Already-unlocked achievements return OK with no change.
- **unlock**: Validates prerequisites (all must be unlocked), acquires distributed lock, sets progress to target. Returns `Unlocked=false` if already unlocked (idempotent). Optionally triggers platform sync per definition.Platforms.
- **list-unlocked**: Filters from EntityProgressData, enriches with definition metadata. Supports platform filter.

### Platform Sync group
- **platform/sync**: Admin-only. Validates entity is Account type. Checks platform linkage via IPlatformAchievementSync. Iterates unlocked achievements, looks up platform-specific ID from PlatformIds map, executes with retry logic.
- **platform/status**: Iterates all registered sync providers, checks linkage status. Sync counts are hardcoded zeros (per-entity sync history not yet tracked).

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
  │  6. Save progress with TTL                              │
  └────────────┬────────────────────────┬───────────────────┘
               │                        │
               ▼                        ▼
  ┌─────────────────────┐   ┌──────────────────────────────┐
  │  Publish Events     │   │  Platform Sync (if enabled)  │
  │  • unlocked         │   │  • Check linkage via Account │
  │  • progress.updated │   │  • Retry loop → Steam API    │
  │  (includes rarity)  │   │  • Publish sync event        │
  └─────────────────────┘   └──────────────────────────────┘

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
| Xbox sync provider | Stub | `XboxAchievementSync` exists but returns "not implemented" / placeholder behavior |
| PlayStation sync provider | Stub | `PlayStationAchievementSync` exists but returns "not implemented" / placeholder behavior |
| Internal sync provider | Stub | `InternalAchievementSync` exists as a no-op provider for internal-only achievements |
| Per-entity sync history tracking | Not implemented | `GetPlatformSyncStatusAsync` returns hardcoded zeros for synced/pending/failed counts |
| `achievement-unlock` state store | Defined but unused | Exists in state-stores.yaml and StateStoreDefinitions but never referenced in service code |
| TotalEligibleEntities population | Not implemented | Field exists on definition but is never written to by any endpoint; rarity calc only works when manually populated |
| `SetProgressAsync` on platforms | Not called | IPlatformAchievementSync defines it, SteamAchievementSync implements it, but the service only calls `UnlockAsync` (never syncs incremental progress) |

## Potential Extensions

- **Sync history store**: Use the defined-but-unused `achievement-unlock` store to track per-entity platform sync history, enabling accurate status reporting
- **TotalEligibleEntities automation**: Subscribe to subscription/account lifecycle events to maintain accurate eligible entity counts for rarity
- **Progressive platform sync**: Call `SetProgressAsync` when progress updates occur (not just on unlock), enabling Steam stats to track incremental progress
- **Achievement groups/categories**: Schema already has metadata field - could formalize grouping for UI presentation
- **Leaderboard integration on unlock**: Publish achievement points to a leaderboard for gamerscore-style rankings

## Known Quirks & Caveats

- **Progress TTL**: EntityProgressData is stored with a configurable TTL (default 5 minutes). If progress expires between updates, the entity starts from zero. This is a cache pattern - permanent progress requires the TTL to be set high enough or the store backend to persist through eviction.
- **Duplicate mock-mode checks**: MockPlatformSync is checked both in `ExecutePlatformUnlockWithRetriesAsync` (service level) and within `SteamAchievementSync.UnlockAsync` (provider level). The service-level check short-circuits before the provider is ever called.
- **EarnedCount increment is best-effort**: The ETag-based TrySaveAsync on definition after unlock does not retry on conflict. If two concurrent unlocks race, one EarnedCount increment may be silently lost. The periodic rarity recalculation does not fix this since it only recalculates percentage, not earned count.
- **Event handler loads all definitions**: Each analytics/leaderboard event triggers `LoadAchievementDefinitionsAsync` which iterates the full index set and loads every definition individually. No caching layer exists - high-frequency events could generate significant Redis traffic.
- **Rarity dual-threshold logic**: An achievement is "rare" if EarnedCount < RarityThresholdEarnedCount (100) OR RarityPercent < RareThresholdPercent (5%). A brand-new achievement with 0 earned is always rare regardless of percentage.
- **Platform sync is account-only**: Both `SyncPlatformAchievementsAsync` and `GetPlatformSyncStatusAsync` reject non-Account entity types with 400. Character/guild achievements cannot be synced to platforms.
- **Delete preserves progress**: Deleting a definition removes it from the store and index but leaves EntityProgressData intact. The unlocked achievement remains in the entity's progress dictionary with no corresponding definition.
- **Steam sync returns success when disabled**: If Steam credentials are not configured, `SteamAchievementSync.UnlockAsync` returns `Success=true` with SyncId "disabled" - it's treated as a graceful no-op, not an error.
- **Metadata key alternatives**: HandleScoreUpdatedAsync accepts both `scoreType` and `eventType` as metadata keys for matching analytics events (legacy compatibility).
