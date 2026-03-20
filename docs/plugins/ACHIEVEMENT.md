# Achievement Plugin Deep Dive

> **Plugin**: lib-achievement
> **Schema**: schemas/achievement-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Store**: achievement-definition (Redis), achievement-progress (Redis), achievement-sync (Redis)
> **Implementation Map**: [docs/maps/ACHIEVEMENT.md](../maps/ACHIEVEMENT.md)
> **Short**: Achievement/trophy system with progressive/binary types, rarity calculation, and platform sync

## Overview

The Achievement plugin (L4 GameFeatures) provides a multi-entity achievement and trophy system with progressive/binary unlock types, prerequisite chains, rarity calculations, and platform synchronization (Steam, Xbox, PlayStation). Achievements are scoped to game services, support event-driven auto-unlock from Analytics and Leaderboard events, and include a background service for periodic rarity recalculation.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| Game clients (via Connect) | Receive `achievement.progress.unlocked` and `achievement.progress.milestone-reached` client events via WebSocket push |
| lib-quest (via DI) | Discovers `AchievementPrerequisiteProviderFactory` via `IEnumerable<IPrerequisiteProviderFactory>` for dynamic prerequisite validation |

The Achievement plugin is primarily a leaf service — it reacts to external events. Game clients receive unlock and progress milestone notifications via the Connect service's client event push system (IEntitySessionRegistry). Achievement also participates in the Quest prerequisite system via `IPrerequisiteProviderFactory`, enabling quests to require specific achievements before acceptance.

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
| ProgressMilestonePercents | `ACHIEVEMENT_PROGRESS_MILESTONE_PERCENTS` | [25, 50, 75] | Progress percentage thresholds at which client milestone events are published (comma-separated in env var) |
| RarityCalculationStartupDelaySeconds | `ACHIEVEMENT_RARITY_CALCULATION_STARTUP_DELAY_SECONDS` | 30 | Delay before first rarity recalculation after startup |
| RarityCalculationIntervalMinutes | `ACHIEVEMENT_RARITY_CALCULATION_INTERVAL_MINUTES` | 60 | Interval between periodic rarity recalculations |
| RareThresholdPercent | `ACHIEVEMENT_RARE_THRESHOLD_PERCENT` | 5.0 | Percentage below which an achievement is flagged as rare |
| RarityThresholdEarnedCount | `ACHIEVEMENT_RARITY_THRESHOLD_EARNED_COUNT` | 100 | Earned count below which an achievement is considered rare regardless of percentage |
| SyncHistoryTtlSeconds | `ACHIEVEMENT_SYNC_HISTORY_TTL_SECONDS` | 0 | TTL for sync history data in Redis (0 = no expiry, sync history persists indefinitely) |
| SyncStatusRetryAttempts | `ACHIEVEMENT_SYNC_STATUS_RETRY_ATTEMPTS` | 3 | Retry attempts for ETag conflicts when updating sync status records |

## Visual Aid

```
 Event-Driven Achievement Flow

 ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
 │ Analytics │ │ Leaderboard │ │ Direct API │
 │score.updated │ │ rank.changed │ │ /unlock │
 └──────┬───────┘ └──────┬───────┘ └──────┬───────┘
 │ │ │
 ▼ ▼ ▼
 ┌─────────────────────────────────────────────────────────┐
 │ Achievement Service │
 │ │
 │ 1. Load definitions for gameServiceId (from index set) │
 │ 2. Match via typed fields: scoreType/leaderboardId/etc │
 │ 3. Acquire distributed lock on progress key │
 │ 4. Update progress or unlock │
 │ 5. Update EarnedCount on definition (ETag CAS) │
 │ 6. Save progress (permanent unless TTL configured) │
 └──────┬──────────────────┬───────────────┬───────────────┘
 │ │ │
 ▼ ▼ ▼
 ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐
 │ Service Evts │ │ Client Evts │ │ Platform Sync │
 │ • unlocked │ │ • unlocked │ │ • Skip !IsConfigured │
 │ • progress │ │ • milestone │ │ • Lookup mapping │
 │ .updated │ │ (25/50/75%)│ │ • Retry → Steam API │
 │ (IMessageBus)│ │ (WebSocket) │ │ • Publish sync event │
 └──────────────┘ └──────────────┘ └──────────────────────┘

 Background: RarityCalculationService
 ┌─────────────────────────────────────────────────────────┐
 │ Periodically (default 60min): │
 │ • Iterate game-services index → definitions index │
 │ • Recalculate: EarnedCount / TotalEligibleEntities │
 │ • Write RarityPercent + RarityCalculatedAt │
 └─────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

| Item | Status | Notes |
|------|--------|-------|
| Xbox sync provider | Stub | `XboxAchievementSync` exists but `IsConfigured=false` and returns "not implemented". Skipped by service layer. Configuration properties (`XboxClientId`, `XboxClientSecret`) are defined in schema but unused - scaffolding for future implementation <!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/585 --> |
| PlayStation sync provider | Stub | `PlayStationAchievementSync` exists but `IsConfigured=false` and returns "not implemented". Skipped by service layer. Configuration properties (`PlayStationClientId`, `PlayStationClientSecret`) are defined in schema but unused - scaffolding for future implementation <!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/587 --> |
| Internal sync provider | Active | `InternalAchievementSync` is a no-op provider (`IsConfigured=true`) for internal-only achievements |
| TotalEligibleEntities population | Not implemented | Field exists on definition but is never written to by any endpoint; rarity calc only works when manually populated <!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/581 --> |
| `SetProgressAsync` on platforms | Not called | IPlatformAchievementSync defines it, SteamAchievementSync implements it, but the service only calls `UnlockAsync` (never syncs incremental progress) <!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/592 --> |

## Potential Extensions

- **TotalEligibleEntities automation**: Subscribe to subscription/account lifecycle events to maintain accurate eligible entity counts for rarity <!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/581 -->
- **Progressive platform sync**: Call `SetProgressAsync` when progress updates occur (not just on unlock), enabling Steam stats to track incremental progress <!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/592 -->
- **Leaderboard integration on unlock**: Publish achievement points to a leaderboard for gamerscore-style rankings
 <!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/596 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Missing `account.deleted` cleanup handler**~~: **FIXED** (2026-03-15) - Added `HandleAccountDeletedAsync` in `AchievementServiceEvents.cs` that subscribes to `account.deleted` and deletes all Account-entity-type progress records and sync tracking records across all game services. Uses per-item error isolation per T7. Also added `account.deleted` to `x-event-subscriptions` in `achievement-service-events.yaml`.

### Intentional Quirks (Documented Behavior)

1. **Rarity dual-threshold logic**: An achievement is "rare" if EarnedCount < RarityThresholdEarnedCount (100) OR RarityPercent < RareThresholdPercent (5%). A brand-new achievement with 0 earned is always rare regardless of percentage.

2. **Category B deprecation lifecycle**: Achievement definitions follow Category B deprecation — deprecate-only, no undeprecate. Deprecated definitions with zero remaining progress records can be permanently removed via the `clean-deprecated` sweep endpoint. `includeDeprecated` parameter on all list endpoints defaults to `false`.

3. **Event handlers load all definitions per event**: Each analytics/leaderboard event triggers `LoadAchievementDefinitionsAsync` which iterates the definition index set and loads each definition individually. This is intentional to ensure freshness but creates N+1 query pattern per event.

4. **Progress TTL default is infinite**: ProgressTtlSeconds defaults to 0 meaning progress records never expire. This is intentional for persistent progress tracking but operators should be aware of storage growth.

5. **Orphaned progress data**: Progress records may reference deprecated or deleted definitions. Orphaned entries are filtered at read time by verifying each definition still exists. The `clean-deprecated` sweep removes deprecated definitions only when zero progress records remain, but direct deletion of progress records (e.g., via character cleanup) can leave index references to missing definitions.

6. **Client milestone events at configurable thresholds**: Progress milestone client events fire at configurable percentage thresholds (default: 25%, 50%, 75%) via `ProgressMilestonePercents` configuration. Schema defines it as a typed integer array (`type: array`, `items: type: integer`); the "(comma-separated in env var)" note refers to standard environment variable serialization, not a schema typing issue.

### Design Considerations (Requires Planning)

- **TotalEligibleEntities never populated**: The rarity calculation background worker depends on `TotalEligibleEntities > 0`, but this field is never written by any endpoint. The rarity percentage calculation branch will never execute, making the rarity system effectively dead code until this field is automated or manually populated.
 <!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/581 -->

- **Event handler N+1 query pattern**: Each analytics/leaderboard event triggers `LoadAchievementDefinitionsAsync` which loads every definition individually from Redis (one GetAsync per achievement in the index). No caching layer exists — high-frequency events could generate significant Redis traffic. A cache with invalidation on definition CRUD would improve this but requires careful design around coherency across service instances.
 <!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/582 -->

- **Platform sync is fire-and-forget on unlock**: When `AutoSyncOnUnlock=true`, platform syncs happen inline during unlock but failures don't prevent the local unlock from succeeding. Retry logic exists but if all retries fail, the sync is marked failed in the event and the achievement stays locally unlocked but not synced. No retry queue exists for permanently failed syncs.
 <!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/583 -->

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Completed

(All previously completed items verified and cleaned up on 2026-03-12)

### Active
- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#653](https://github.com/beyond-immersion/bannou-service/issues/653).
- [#705](https://github.com/beyond-immersion/bannou-service/issues/705) — **Migrate event-driven triggers off Analytics dependency.** Achievement's auto-unlock system depends entirely on `analytics.score.updated` and `analytics.milestone.reached` events — when Analytics (the most optional plugin) is disabled, all event-driven achievements are dead. Migration path: direct domain event subscriptions (`quest.completed`, `collection.unlocked`, `seed.phase.changed`), direct API progress updates from game engines, and `IAccumulatedDataProvider` (#703) for soft-dependency cross-session aggregation queries. Same anti-pattern dissolved by DIVINITY-GENERATION-ARCHITECTURE for Divine.
- [#581](https://github.com/beyond-immersion/bannou-service/issues/581) — TotalEligibleEntities automation for rarity calculation
- [#582](https://github.com/beyond-immersion/bannou-service/issues/582) — Event handler N+1 definition loading — add caching layer
- [#583](https://github.com/beyond-immersion/bannou-service/issues/583) — Platform sync permanent failure retry queue
- [#585](https://github.com/beyond-immersion/bannou-service/issues/585) — Xbox sync provider implementation (requires platform integration design)
- [#587](https://github.com/beyond-immersion/bannou-service/issues/587) — PlayStation sync provider implementation (requires platform integration design)
- [#592](https://github.com/beyond-immersion/bannou-service/issues/592) — SetProgressAsync never called for incremental platform progress sync
- [#596](https://github.com/beyond-immersion/bannou-service/issues/596) — Leaderboard integration on unlock for gamerscore-style rankings
