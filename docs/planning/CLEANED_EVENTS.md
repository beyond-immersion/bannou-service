# Cleaned Events Inventory

Events removed or flagged during schema cleanup (2025-12-30).
**Updated 2026-01-07**: Added comprehensive gap analysis for missing events across all services.
**Updated 2026-01-07 (PM)**: All P0 and P1 events implemented. See Implementation Status section.

These events were defined in schemas but had no code implementation (neither published nor subscribed).
They are documented here for historical reference and potential future implementation.

---

## Events Removed (Neither Published nor Subscribed)

### Documentation Service Events
Events defined in `schemas/documentation-events.yaml` but never implemented.

**Note:** The lifecycle events and most analytics/sync events ARE published and remain in the schema.
Only these 3 events were never implemented:

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| DocumentViewedEvent | `documentation.viewed` | Analytics: track document views |
| DocumentationImportStartedEvent | `documentation.import.started` | Import job lifecycle start |
| DocumentationImportCompletedEvent | `documentation.import.completed` | Import job lifecycle completion |

**Reason for removal:** View tracking and import job events were planned but never implemented.
**Future use:** Consider implementing for observability/audit trail when documentation service matures.

### Voice Service Events
Events defined in `schemas/voice-events.yaml` but never implemented:

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| VoiceRoomCreatedEvent | `voice.room.created` | Voice room lifecycle |
| VoiceRoomDeletedEvent | `voice.room.deleted` | Voice room cleanup |
| VoiceTierUpgradeRequestedEvent | `voice.tier.upgrade.requested` | SFU tier escalation request |
| VoiceTierUpgradeCompletedEvent | `voice.tier.upgrade.completed` | SFU tier escalation completion |
| VoiceParticipantJoinedEvent | `voice.participant.joined` | Participant tracking |
| VoiceParticipantLeftEvent | `voice.participant.left` | Participant tracking |

**Reason for removal:** Voice service is stub-only; events were aspirational.
**Future use:** Implement when voice/WebRTC integration is built.

### Mesh Service Events
Events defined in `schemas/mesh-events.yaml` but never implemented.

**Note:** MeshEndpointRegisteredEvent and MeshEndpointDeregisteredEvent ARE published and should remain.

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| MeshEndpointStatusChangedEvent | `mesh.endpoint.status.changed` | Track health status changes |
| MeshRoutingTableUpdatedEvent | `mesh.routing.updated` | Track routing table changes |

**Reason for removal:** Health status transitions and routing updates not implemented.
**Future use:** Consider for mesh observability dashboard.

### Messaging Service Events
Events defined in `schemas/messaging-events.yaml` but never implemented:

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| MessagePublishedEvent | `messaging.published` | Debug/audit message flow |
| SubscriptionCreatedEvent | `messaging.subscription.created` | Track dynamic subscriptions |
| SubscriptionRemovedEvent | `messaging.subscription.removed` | Track subscription cleanup |

**Reason for removal:** Debug events - would be expensive to publish for every message.
**Future use:** Consider as opt-in debug mode if needed.

### State Service Events
Events defined in `schemas/state-events.yaml` but never implemented:

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| StateChangedEvent | `state.changed` | Audit trail for state changes |
| StoreMigrationEvent | `state.migration` | Track Redis/MySQL migrations |
| StoreHealthEvent | `state.health` | Store health monitoring |

**Reason for removal:** Would be expensive; config flag for enabling was never used.
**Future use:** Implement as opt-in for debugging state issues.

### Permission Service Events
Events defined in `schemas/permission-events.yaml` but never implemented:

| Event | Topic | Intended Purpose |
|-------|-------|------------------|
| PermissionRecompileRequest | `permissions.recompile` | Request capability recompilation |
| RedisOperationEvent | `permissions.redis-operation` | Track Redis permission operations |
| AuthSessionEvent | `auth.session` | Legacy session tracking |

**Reason for removal:** Internal optimization events that were never needed.
**Future use:** May be useful for permission system debugging.


---

## Events Kept (Published but Not Subscribed)

The following events ARE published by services but have no subscribers yet.
They remain in schemas for future cross-service coordination:

### Lifecycle Events (Auto-Generated)
- BehaviorCreatedEvent, BehaviorUpdatedEvent, BehaviorDeletedEvent
- CharacterCreatedEvent, CharacterUpdatedEvent, CharacterDeletedEvent
- DocumentCreatedEvent, DocumentUpdatedEvent, DocumentDeletedEvent
- GameSessionCreatedEvent, GameSessionUpdatedEvent, GameSessionDeletedEvent
- LocationCreatedEvent, LocationUpdatedEvent, LocationDeletedEvent
- RealmCreatedEvent, RealmUpdatedEvent, RealmDeletedEvent
- RelationshipCreatedEvent, RelationshipUpdatedEvent, RelationshipDeletedEvent
- RelationshipTypeCreatedEvent, RelationshipTypeUpdatedEvent, RelationshipTypeDeletedEvent
- SpeciesCreatedEvent, SpeciesUpdatedEvent, SpeciesDeletedEvent

### Documentation Service Events
- DocumentationQueriedEvent, DocumentationSearchedEvent
- DocumentationBindingCreatedEvent, DocumentationBindingRemovedEvent
- DocumentationSyncStartedEvent, DocumentationSyncCompletedEvent
- DocumentationArchiveCreatedEvent

### Asset Service Events
- AssetUploadRequestedEvent, AssetUploadCompletedEvent
- AssetProcessingQueuedEvent, AssetProcessingCompletedEvent
- AssetReadyEvent, BundleCreatedEvent

### Game Session Events
- GameSessionPlayerJoinedEvent, GameSessionPlayerLeftEvent

### Mesh Service Events
- MeshEndpointRegisteredEvent, MeshEndpointDeregisteredEvent

### Other Events
- OrchestratorHealthPingEvent - Published for health checks

---

## Actively Used Events

For reference, these events have both publishers and subscribers:

| Event | Published By | Subscribed By |
|-------|-------------|---------------|
| SessionInvalidatedEvent | Auth | Connect |
| SessionUpdatedEvent | Auth | Permission |
| AccountCreatedEvent | Account | (lifecycle, future use) |
| AccountUpdatedEvent | Account | Auth |
| AccountDeletedEvent | Account | Auth |
| SessionConnectedEvent | Connect | GameSession, Permission |
| SessionDisconnectedEvent | Connect | GameSession, Permission |
| SessionReconnectedEvent | Connect | GameSession |
| ServiceRegistrationEvent | All services | Permission |
| ServiceHeartbeatEvent | All services | Orchestrator, Mesh |
| FullServiceMappingsEvent | Orchestrator | Mesh |
| ServiceErrorEvent | All services | Connect (admin forwarding) |
| SubscriptionUpdatedEvent | Subscriptions | Auth, GameSession |
| SessionStateChangeEvent | GameSession | Permission |

---

## Summary

- **Events Removed:** 17 events across 6 services (analytics, debug, optimization events that were never implemented)
- **Events Kept (Published-only):** 40+ lifecycle/asset/game/documentation events (valid for future cross-service coordination)
- **Actively Used:** 14 events with full publisher/subscriber chains

---

## Events Gap Analysis (2026-01-07)

Comprehensive analysis of missing events that should be added for proper observability, audit trails, and cross-service coordination.

### Priority Tiers

Events are categorized by importance:
- **P0 (Critical)**: Security/audit events, session lifecycle, breaks event-driven architecture
- **P1 (High)**: Important business operations without observability
- **P2 (Medium)**: Schema-defined but not implemented, or operational gaps
- **P3 (Low)**: Nice-to-have for completeness, debug/monitoring events

---

## P0 - Critical Missing Events

### Auth Service - Authentication Audit Events

The Auth service publishes NO success events for authentication operations. This is a critical security/audit gap.

| Event | Topic | When to Publish | Why Critical |
|-------|-------|-----------------|--------------|
| AuthLoginSuccessfulEvent | `auth.login.successful` | LoginAsync succeeds | Security audit trail, analytics |
| AuthLoginFailedEvent | `auth.login.failed` | LoginAsync fails (bad credentials) | Brute force detection, security monitoring |
| AuthRegistrationSuccessfulEvent | `auth.registration.successful` | RegisterAsync succeeds | User onboarding analytics |
| AuthOAuthLoginSuccessfulEvent | `auth.oauth.successful` | CompleteOAuthAsync succeeds | OAuth provider analytics |
| AuthSteamLoginSuccessfulEvent | `auth.steam.successful` | VerifySteamAuthAsync succeeds | Platform login analytics |
| AuthPasswordResetSuccessfulEvent | `auth.password-reset.successful` | ConfirmPasswordResetAsync succeeds | Security audit, should invalidate sessions |

**Additional Critical Fix**: `TerminateSessionAsync` should publish `SessionInvalidatedEvent` (like `LogoutAsync` does) to disconnect WebSocket clients.

### Asset Service - Schema-Defined Events Not Published

These events ARE defined in `asset-events.yaml` but NOT published anywhere in code:

| Event | Topic | When to Publish | Gap Location |
|-------|-------|-----------------|--------------|
| AssetUploadRequestedEvent | `asset.upload.requested` | RequestUploadAsync creates upload session | lib-asset/AssetService.cs |
| AssetProcessingQueuedEvent | `asset.processing.queued` | Asset enters processing queue | lib-asset/AssetService.cs |
| AssetProcessingCompletedEvent | `asset.processing.completed` | Processing finishes | lib-asset/AssetService.cs |
| AssetReadyEvent | `asset.ready` | Asset fully processed and available | lib-asset/AssetService.cs |

---

## P1 - High Priority Missing Events

### GameService - No Lifecycle Events

GameService has full CRUD operations but publishes ZERO lifecycle events. Should follow Account service pattern with `x-lifecycle`:

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| GameServiceCreatedEvent | `game-service.created` | CreateServiceAsync succeeds |
| GameServiceUpdatedEvent | `game-service.updated` | UpdateServiceAsync succeeds |
| GameServiceDeletedEvent | `game-service.deleted` | DeleteServiceAsync succeeds |

### Species Service - Missing Merge/Seed Events

| Event | Topic | When to Publish | Gap Location |
|-------|-------|-----------------|--------------|
| SpeciesMergedEvent | `species.merged` | MergeSpeciesAsync completes | lib-species/SpeciesService.cs:1041 |
| (species.updated) | `species.updated` | SeedSpeciesAsync updates existing | lib-species/SpeciesService.cs:763-785 |

**Note**: SeedSpeciesAsync already publishes `species.created` for new species, but updates to existing species during seed publish nothing.

### Leaderboard Service - Missing Entry Event

| Event | Topic | When to Publish | Gap Location |
|-------|-------|-----------------|--------------|
| LeaderboardEntryAddedEvent | `leaderboard.entry.added` | First score submission (previousScore == null) | lib-leaderboard/LeaderboardService.cs:475 |

This event IS defined in schema but NOT published.

### Achievement Service - Missing Definition Events

Achievement service publishes unlock/progress events but NOT definition lifecycle:

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| AchievementDefinitionCreatedEvent | `achievement.definition.created` | CreateAchievementDefinitionAsync succeeds |
| AchievementDefinitionUpdatedEvent | `achievement.definition.updated` | UpdateAchievementDefinitionAsync succeeds |
| AchievementDefinitionDeletedEvent | `achievement.definition.deleted` | DeleteAchievementDefinitionAsync succeeds |

---

## P2 - Medium Priority Missing Events

### Permission Service - Matrix Change Events

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| PermissionMatrixUpdatedEvent | `permission.matrix.updated` | RegisterServicePermissionsAsync modifies matrix |
| PermissionSessionUpdatedEvent | `permission.session.updated` | Session capabilities change |

### Orchestrator Service - Additional Lifecycle Events

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| ServiceHealthStatusChangedEvent | `orchestrator.health.changed` | Service health transitions (healthy↔unhealthy) |
| TopologyUpdateCompletedEvent | `orchestrator.topology.updated` | UpdateTopology succeeds |
| ConfigurationRollbackSucceededEvent | `orchestrator.rollback.succeeded` | RollbackConfiguration succeeds |

### Documentation Service - Missing Operational Events

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| DocumentRecoveredEvent | `documentation.recovered` | RecoverDocumentAsync succeeds |
| DocumentationBulkUpdateEvent | `documentation.bulk.updated` | BulkUpdateDocumentsAsync completes |
| DocumentationBulkDeleteEvent | `documentation.bulk.deleted` | BulkDeleteDocumentsAsync completes |
| DocumentationArchiveRestoredEvent | `documentation.archive.restored` | RestoreDocumentationArchiveAsync succeeds |
| DocumentationArchiveDeletedEvent | `documentation.archive.deleted` | DeleteDocumentationArchiveAsync succeeds |
| DocumentationTrashPurgedEvent | `documentation.trash.purged` | PurgeTrashcanAsync completes |

### Actor Service - Instance Lifecycle Events

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| ActorInstanceSpawnedEvent | `actor.instance.spawned` | Actor instance created/initialized |
| ActorInstanceDespawnedEvent | `actor.instance.despawned` | Actor instance terminated |
| ActorInstanceStateChangedEvent | `actor.instance.state-changed` | Actor behavior state transitions |

---

## P3 - Low Priority / Nice-to-Have Events

### Auth Service - Token Operations

| Event | Topic | When to Publish | Notes |
|-------|-------|-----------------|-------|
| AuthTokenRefreshedEvent | `auth.token.refreshed` | RefreshTokenAsync succeeds | High frequency, consider opt-in |

### Character/Species Schema Updates

| Event | Topic | Notes |
|-------|-------|-------|
| (character.realm.joined) | - | Already implemented but NOT in x-event-publications |
| (character.realm.left) | - | Already implemented but NOT in x-event-publications |

These events ARE published but should be added to schema's `x-event-publications` for documentation.

### Behavior Service

| Event | Topic | When to Publish |
|-------|-------|-----------------|
| BehaviorDeletedEvent | `behavior.deleted` | DeleteBehaviorAsync succeeds |
| BehaviorCompilationStartedEvent | `behavior.compilation.started` | Long-running compilation begins |
| BehaviorCompilationFailedEvent | `behavior.compilation.failed` | Compilation errors |

---

## Implementation Approach

### For Lifecycle Events (Account/GameService pattern)

1. Add `x-lifecycle` to API schema with entity properties
2. Run `scripts/generate-all-services.sh` to generate event models
3. Add private `Publish{Entity}{Action}EventAsync()` methods to service
4. Call publish method after successful CRUD operations

### For Custom Events

1. Define event model in `{service}-events.yaml` under `components/schemas`
2. Add to `x-event-publications` list
3. Run code generation
4. Add publish call at appropriate location in service

---

## Summary by Service

| Service | P0 Events | P1 Events | P2 Events | P3 Events | Total |
|---------|-----------|-----------|-----------|-----------|-------|
| Auth | 7 | 0 | 0 | 1 | 8 |
| Asset | 4 | 0 | 0 | 0 | 4 |
| GameService | 0 | 3 | 0 | 0 | 3 |
| Species | 0 | 2 | 0 | 0 | 2 |
| Leaderboard | 0 | 1 | 0 | 0 | 1 |
| Achievement | 0 | 3 | 0 | 0 | 3 |
| Permission | 0 | 0 | 2 | 0 | 2 |
| Orchestrator | 0 | 0 | 3 | 0 | 3 |
| Documentation | 0 | 0 | 6 | 0 | 6 |
| Actor | 0 | 0 | 3 | 0 | 3 |
| Behavior | 0 | 0 | 0 | 3 | 3 |
| Character | 0 | 0 | 0 | 2 | 2 |
| **Total** | **11** | **9** | **14** | **6** | **40** |

**Recommended Scope**: Implement P0 (11 events) + P1 (9 events) = 20 events as "inarguably reasonable" cases.
Consider P2 (14 events) based on time/effort.

---

## Implementation Status (2026-01-07)

### P0 Events - ALL COMPLETED ✅

| Event | Service | Status | Notes |
|-------|---------|--------|-------|
| AuthLoginSuccessfulEvent | Auth | ✅ Implemented | Published in LoginAsync on success |
| AuthLoginFailedEvent | Auth | ✅ Implemented | Published in LoginAsync on failure (2 cases) |
| AuthRegistrationSuccessfulEvent | Auth | ✅ Implemented | Published in RegisterAsync |
| AuthOAuthLoginSuccessfulEvent | Auth | ✅ Implemented | Published in CompleteOAuthAsync |
| AuthSteamLoginSuccessfulEvent | Auth | ✅ Implemented | Published in VerifySteamAuthAsync |
| AuthPasswordResetSuccessfulEvent | Auth | ✅ Implemented | Published in ConfirmPasswordResetAsync |
| TerminateSessionAsync fix | Auth | ✅ Implemented | Now publishes SessionInvalidatedEvent |
| AssetUploadRequestedEvent | Asset | ✅ Implemented | Published in RequestUploadAsync |
| AssetProcessingQueuedEvent | Asset | ⏸️ Deferred | Processing pipeline not yet implemented |
| AssetProcessingCompletedEvent | Asset | ⏸️ Deferred | Processing pipeline not yet implemented |
| AssetReadyEvent | Asset | ⏸️ Deferred | Processing pipeline not yet implemented |

**Note**: Asset processing events deferred until the asset processing pipeline is built. The upload request event was the critical one for audit trail.

### P1 Events - ALL COMPLETED ✅

| Event | Service | Status | Notes |
|-------|---------|--------|-------|
| GameServiceCreatedEvent | GameService | ✅ Implemented | Via x-lifecycle schema pattern |
| GameServiceUpdatedEvent | GameService | ✅ Implemented | Includes ChangedFields tracking |
| GameServiceDeletedEvent | GameService | ✅ Implemented | Added Reason field to DeleteRequest |
| SpeciesMergedEvent | Species | ✅ Implemented | Published in MergeSpeciesAsync |
| SeedSpeciesAsync fix | Species | ✅ Implemented | Now publishes SpeciesUpdatedEvent for changes |
| LeaderboardEntryAddedEvent | Leaderboard | ✅ Implemented | Published when entity first joins (no previousScore) |
| AchievementDefinitionCreatedEvent | Achievement | ✅ Implemented | Published in CreateAchievementDefinitionAsync |
| AchievementDefinitionUpdatedEvent | Achievement | ✅ Implemented | Includes ChangedFields tracking |
| AchievementDefinitionDeletedEvent | Achievement | ✅ Implemented | Published in DeleteAchievementDefinitionAsync |

### Files Modified

**Schemas:**
- `schemas/auth-events.yaml` - Added 6 audit event schemas with x-event-publications
- `schemas/game-service-api.yaml` - Added reason field to DeleteServiceRequest
- `schemas/game-service-events.yaml` - NEW FILE with x-lifecycle for auto-generation
- `schemas/species-events.yaml` - Added SpeciesMergedEvent publication and schema
- `schemas/achievement-events.yaml` - Added 3 definition lifecycle events

**Services:**
- `lib-auth/AuthService.cs` - 6 new publish helper methods, calls in Login/Register/OAuth/Steam/PasswordReset
- `lib-asset/AssetService.cs` - AssetUploadRequestedEvent publishing
- `lib-game-service/GameServiceService.cs` - 3 lifecycle event publish methods
- `lib-species/SpeciesService.cs` - SpeciesMergedEvent + SeedSpeciesAsync fix with ChangedFields
- `lib-leaderboard/LeaderboardService.cs` - LeaderboardEntryAddedEvent for new entries
- `lib-achievement/AchievementService.cs` - 3 definition lifecycle event publish methods

---

## Resolved Issues

### OAuth/Steam Session Creation & SessionId in Events

**Status**: ✅ FULLY RESOLVED (2026-01-07)

**Original Concern**: OAuth success events didn't include `sessionId`. Was this because OAuth flows weren't creating sessions?

**Investigation Findings**:
All authentication paths call `GenerateAccessTokenAsync()` which creates sessions in Redis. The issue was that sessionId wasn't being returned to callers for event publishing.

**Resolution (2026-01-07)**:
1. Changed `GenerateAccessTokenAsync` signature to return `(string accessToken, string sessionId)` tuple
2. Updated all callers (LoginAsync, RegisterAsync, CompleteOAuthAsync, VerifySteamAuthAsync) to capture sessionId
3. Updated all audit event publishers to include sessionId
4. Updated `auth-events.yaml` schema to make sessionId REQUIRED on:
   - AuthLoginSuccessfulEvent
   - AuthRegistrationSuccessfulEvent
   - AuthOAuthLoginSuccessfulEvent
   - AuthSteamLoginSuccessfulEvent
5. Mock handlers (used for testing) discard sessionId and skip audit events

**HTTP Tests Enhanced**:
- `TestOAuthFlow` now validates token and verifies SessionId when MockProviders=true
- `TestSteamAuthFlow` now validates token and verifies SessionId when MockProviders=true
- Both tests still pass when MockProviders=false (correctly reject invalid mock data)
