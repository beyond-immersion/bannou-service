# Generated Events Reference

> **Source**: `schemas/*-events.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all events defined in Bannou's event schemas.

## Events by Service

### Common

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` | Base schema for all service-to-service events. |
| `CharacterStateUpdateEvent` | Custom | `character-state-update` | Published by Actor service when an actor updates a... |
| `FullServiceMappingsEvent` | Custom | `full-service-mappings` | Published periodically by Orchestrator as the auth... |
| `ServiceErrorEvent` | Error | `service.error` | Structured error event for unexpected service fail... |
| `ServiceHeartbeatEvent` | Health | `service.heartbeat` | Published periodically by each bannou instance to ... |
| `ServiceRegistrationEvent` | Custom | `service-registration` | Published by any service during startup to registe... |
| `SessionConnectedEvent` | Session | `session.connected` | Published by Connect service when a WebSocket conn... |
| `SessionDisconnectedEvent` | Session | `session.disconnected` | Published by Connect service when a WebSocket conn... |
| `SessionReconnectedEvent` | Session | `session-reconnected` | Published by Connect service when a WebSocket sess... |

### Achievement

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AchievementDefinitionCreatedEvent` | Lifecycle (Created) | `achievement-definition.created` | Published when a new achievement definition is cre... |
| `AchievementDefinitionDeletedEvent` | Lifecycle (Deleted) | `achievement-definition.deleted` | Published when an achievement definition is delete... |
| `AchievementDefinitionUpdatedEvent` | Lifecycle (Updated) | `achievement-definition.updated` | Published when an achievement definition is update... |
| `AchievementPlatformSyncedEvent` | Custom | `achievement-platform-synced` | Published when an achievement is synced to an exte... |
| `AchievementProgressUpdatedEvent` | Lifecycle (Updated) | `achievement-progress.updated` | Published when progress is made on a progressive a... |
| `AchievementUnlockedEvent` | Custom | `achievement-unlocked` | Published when an entity unlocks an achievement |

### Actor

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ActorCompletedEvent` | Custom | `actor-completed` | Published when an actor completes execution (self-... |
| `ActorStatusChangedEvent` | Custom | `actor-status-changed` | Published when an actor's status changes. |
| `CharacterPerceptionEvent` | Custom | `character-perception` | Perception event published by game servers for cha... |
| `PoolNodeDrainingEvent` | Custom | `pool-node-draining` | Published when a pool node begins graceful shutdow... |
| `PoolNodeHeartbeatEvent` | Health | `pool-node.heartbeat` | Periodic heartbeat from pool nodes to control plan... |
| `PoolNodeRegisteredEvent` | Registration | `pool-node.registered` | Published when a pool node starts and registers wi... |
| `PoolNodeUnhealthyEvent` | Custom | `pool-node-unhealthy` | Published by control plane when a pool node is det... |

### Analytics

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AnalyticsMilestoneReachedEvent` | Custom | `analytics-milestone-reached` | Published when an entity reaches a statistical mil... |
| `AnalyticsRatingUpdatedEvent` | Lifecycle (Updated) | `analytics-rating.updated` | Published when an entity's Glicko-2 skill rating c... |
| `AnalyticsScoreUpdatedEvent` | Lifecycle (Updated) | `analytics-score.updated` | Published when an entity's score or statistic chan... |

### Asset

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AssetProcessingCompletedEvent` | Custom | `asset-processing-completed` | Event published when asset processing completes |
| `AssetProcessingQueuedEvent` | Custom | `asset-processing-queued` | Event published when an asset is queued for proces... |
| `AssetReadyEvent` | Custom | `asset-ready` | Event published when an asset is fully processed a... |
| `AssetUploadCompletedEvent` | Custom | `asset-upload-completed` | Event published when an upload is completed and fi... |
| `AssetUploadRequestedEvent` | Custom | `asset-upload-requested` | Event published when a new upload is initiated via... |
| `BaseServiceEvent` | Custom | `base-service` |  |
| `BundleCreatedEvent` | Lifecycle (Created) | `bundle.created` | Event published when a bundle is successfully crea... |

### Asset (client)

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AssetProcessingCompleteEvent` | Custom | `asset-processing-complete` | Sent when asset processing (e.g., texture mipmaps,... |
| `AssetProcessingFailedEvent` | Custom | `asset-processing-failed` | Sent when asset processing fails. Includes retry i... |
| `AssetReadyEvent` | Custom | `asset-ready` | Final notification that an asset is ready for use. |
| `AssetUploadCompleteEvent` | Custom | `asset-upload-complete` | Sent when an asset upload has completed (success o... |
| `BundleCreationCompleteEvent` | Custom | `bundle-creation-complete` | Sent when bundle creation from asset_ids completes... |
| `BundleValidationCompleteEvent` | Custom | `bundle-validation-complete` | Sent when a bundle upload has been validated and p... |
| `BundleValidationFailedEvent` | Custom | `bundle-validation-failed` | Sent when bundle validation fails. Includes detail... |

### Auth

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AuthLoginFailedEvent` | Custom | `auth-login-failed` | Published when a login attempt fails (for brute fo... |
| `AuthLoginSuccessfulEvent` | Custom | `auth-login-successful` | Published when a user successfully authenticates w... |
| `AuthOAuthLoginSuccessfulEvent` | Custom | `auth-o-auth-login-successful` | Published when a user authenticates via OAuth prov... |
| `AuthPasswordResetSuccessfulEvent` | Custom | `auth-password-reset-successful` | Published when a password reset is successfully co... |
| `AuthRegistrationSuccessfulEvent` | Custom | `auth-registration-successful` | Published when a new user successfully registers |
| `AuthSteamLoginSuccessfulEvent` | Custom | `auth-steam-login-successful` | Published when a user authenticates via Steam |
| `BaseServiceEvent` | Custom | `base-service` |  |
| `SessionInvalidatedEvent` | Custom | `session.invalidated` | Event published when sessions are invalidated (log... |
| `SessionInvalidatedEventReason` | Custom | `session.invalidated-event-reason` | Reason for session invalidation |
| `SessionUpdatedEvent` | Lifecycle (Updated) | `session.updated` | Published when a session's roles or authorizations... |
| `SessionUpdatedEventReason` | Lifecycle (Updated) | `session.updated-event-reason` | Reason for session update |

### Behavior

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CinematicExtensionAvailableEvent` | Custom | `cinematic-extension-available` | Event published when a cinematic extension is avai... |

### Common (client)

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseClientEvent` | Custom | `base-client` | Base schema for all server-to-client push events. |
| `CapabilityManifestEvent` | Custom | `capability-manifest` | Sent to client when their available API capabiliti... |
| `DisconnectNotificationEvent` | Custom | `disconnect-notification` | Sent to client before WebSocket connection is clos... |
| `SessionCapabilitiesEvent` | Custom | `session-capabilities` | Internal event carrying compiled capabilities from... |
| `ShortcutPublishedEvent` | Custom | `shortcut-published` | Published by services to create or update a sessio... |
| `ShortcutRevokedEvent` | Expiration | `shortcut.revoked` | Published by services to remove shortcuts. |
| `SystemErrorEvent` | Error | `system.error` | Generic error notification sent to client. |
| `SystemNotificationEvent` | Custom | `system-notification` | Generic notification event for system-level messag... |

### Documentation

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `DocumentationArchiveCreatedEvent` | Lifecycle (Created) | `documentation-archive.created` | Published when a documentation archive is created |
| `DocumentationBindingCreatedEvent` | Lifecycle (Created) | `documentation-binding.created` | Published when a repository binding is created |
| `DocumentationBindingRemovedEvent` | Custom | `documentation-binding-removed` | Published when a repository binding is removed |
| `DocumentationQueriedEvent` | Custom | `documentation-queried` | Published when documentation is queried with natur... |
| `DocumentationSearchedEvent` | Custom | `documentation-searched` | Published when documentation is searched with keyw... |
| `DocumentationSyncCompletedEvent` | Custom | `documentation-sync-completed` | Published when a repository sync completes |
| `DocumentationSyncStartedEvent` | Custom | `documentation-sync-started` | Published when a repository sync starts |

### Game Session

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `GameSessionActionPerformedEvent` | Custom | `game-session-action-performed` | Published when a game action is performed in a ses... |
| `GameSessionPlayerJoinedEvent` | Custom | `game-session-player-joined` | Published when a player joins a game session |
| `GameSessionPlayerLeftEvent` | Custom | `game-session-player-left` | Published when a player leaves a game session |

### Game Session (client)

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ChatMessageReceivedEvent` | Custom | `chat-message-received` | Sent to recipients when a chat message is posted i... |
| `GameActionResultEvent` | Custom | `game-action-result` | Sent to relevant players when a game action produc... |
| `GameStateUpdatedEvent` | Lifecycle (Updated) | `game-state.updated` | Sent when game state changes that all players shou... |
| `PlayerJoinedEvent` | Custom | `player-joined` | Sent to all session participants when a new player... |
| `PlayerKickedEvent` | Custom | `player-kicked` | Sent to all session participants when a player is ... |
| `PlayerLeftEvent` | Custom | `player-left` | Sent to all session participants when a player lea... |
| `SessionStateChangedEvent` | Custom | `session-state-changed` | Sent to all session participants when the session ... |

### Leaderboard

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `LeaderboardEntryAddedEvent` | Custom | `leaderboard-entry-added` | Published when a new entity joins a leaderboard |
| `LeaderboardRankChangedEvent` | Custom | `leaderboard-rank-changed` | Published when an entity's rank changes on a leade... |
| `LeaderboardSeasonStartedEvent` | Custom | `leaderboard-season-started` | Published when a new leaderboard season begins |

### Mesh

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |
| `MeshEndpointDeregisteredEvent` | Registration | `mesh-endpoint-deregistered` | Published when an endpoint is removed from the ser... |
| `MeshEndpointRegisteredEvent` | Registration | `mesh-endpoint.registered` | Published when a new endpoint is registered in the... |

### Messaging

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |

### Orchestrator

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |
| `OrchestratorHealthPingEvent` | Custom | `orchestrator-health-ping` | Simple health ping event published to verify pub/s... |

### Permission

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |
| `SessionStateChangeEvent` | Custom | `session-state-change` | Published by services when a session's state chang... |

### Species

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |
| `SpeciesMergedEvent` | Custom | `species-merged` | Published when two species are merged, with the so... |

### State

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |

### Subscription

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |
| `SubscriptionUpdatedEvent` | Lifecycle (Updated) | `subscription.updated` | Published when a subscription changes state (creat... |

### Voice

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseServiceEvent` | Custom | `base-service` |  |

### Voice (client)

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `VoicePeerJoinedEvent` | Custom | `voice-peer-joined` | Sent to existing room participants when a new peer... |
| `VoicePeerLeftEvent` | Custom | `voice-peer-left` | Sent to remaining room participants when a peer le... |
| `VoicePeerUpdatedEvent` | Lifecycle (Updated) | `voice-peer.updated` | Sent when a peer updates their SIP endpoint (e.g.,... |
| `VoiceRoomClosedEvent` | Custom | `voice-room-closed` | Sent to all room participants when the voice room ... |
| `VoiceRoomStateEvent` | Custom | `voice-room-state` | Sent to a client when they join a voice room. |
| `VoiceTierUpgradeEvent` | Custom | `voice-tier-upgrade` | Sent to all room participants when the voice tier ... |

## Event Types

| Type | Description | Example |
|------|-------------|---------|
| Lifecycle (Created) | Entity creation events from `x-lifecycle` | `AccountCreatedEvent` |
| Lifecycle (Updated) | Entity update events from `x-lifecycle` | `CharacterUpdatedEvent` |
| Lifecycle (Deleted) | Entity deletion events from `x-lifecycle` | `RelationshipDeletedEvent` |
| Session | WebSocket connection events | `SessionConnectedEvent` |
| Registration | Service/capability registration | `ServiceRegistrationEvent` |
| Health | Heartbeat and health status | `ServiceHeartbeatEvent` |
| Error | Error reporting events | `ServiceErrorEvent` |
| Expiration | Subscription/token expiration | `SubscriptionExpiredEvent` |
| Custom | Service-specific events | Varies by service |

## Topic Naming Convention

Events are published to topics following the pattern: `{entity}.{action}`

Examples:
- `account.created` - Account was created
- `session.invalidated` - Session was invalidated
- `character.updated` - Character was updated

All events use the `bannou-pubsub` pub/sub component.

## Lifecycle Events (x-lifecycle)

Lifecycle events are auto-generated from `x-lifecycle` definitions in API schemas.
They follow a consistent pattern:

- **Created**: Full entity data on creation
- **Updated**: Full entity data + `changedFields` array
- **Deleted**: Entity ID + `deletedReason`

See [TENETS.md](../TENETS.md#lifecycle-events-x-lifecycle) for usage details.

---

*This file is auto-generated. See [TENETS.md](../TENETS.md) for architectural context.*
