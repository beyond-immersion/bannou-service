# Generated Events Reference

> **Auto-generated**: 2025-12-21 07:11:10
> **Source**: `schemas/*-events.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all events defined in Bannou's event schemas.

## Events by Service

### Common

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `FullServiceMappingsEvent` | Custom | `full-service-mappings` | Published periodically by Orchestrator as the auth... |
| `ServiceErrorEvent` | Error | `service.error` | Structured error event for unexpected service fail... |
| `ServiceHeartbeatEvent` | Health | `service.heartbeat` | Published periodically by each bannou instance to ... |
| `ServiceRegistrationEvent` | Custom | `service-registration` | Published by any service during startup to registe... |
| `SessionConnectedEvent` | Session | `session.connected` | Published by Connect service when a WebSocket conn... |
| `SessionDisconnectedEvent` | Session | `session.disconnected` | Published by Connect service when a WebSocket conn... |

### Accounts

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AccountCreatedEvent` | Lifecycle (Created) | `account.created` | Event published when a new account is created |
| `AccountCreatedEvent` | Lifecycle (Created) | `account.created` | Published to account.created when a account is cre... |
| `AccountDeletedEvent` | Lifecycle (Deleted) | `account.deleted` | Event published when an account is deleted |
| `AccountDeletedEvent` | Lifecycle (Deleted) | `account.deleted` | Published to account.deleted when a account is del... |
| `AccountUpdatedEvent` | Lifecycle (Updated) | `account.updated` | Event published when account properties change |
| `AccountUpdatedEvent` | Lifecycle (Updated) | `account.updated` | Published to account.updated when a account is upd... |
| `ServiceMappingEvent` | Custom | `service-mapping` |  |

### Character

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CharacterCreatedEvent` | Lifecycle (Created) | `character.created` | Published to character.created when a character is... |
| `CharacterDeletedEvent` | Lifecycle (Deleted) | `character.deleted` | Published to character.deleted when a character is... |
| `CharacterUpdatedEvent` | Lifecycle (Updated) | `character.updated` | Published to character.updated when a character is... |

### Common (client)

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BaseClientEvent` | Custom | `base-client` | Base schema for all server-to-client push events. |
| `CapabilityManifestEvent` | Custom | `capability-manifest` | Sent to client when their available API capabiliti... |
| `DisconnectNotificationEvent` | Custom | `disconnect-notification` | Sent to client before WebSocket connection is clos... |
| `SessionCapabilitiesEvent` | Custom | `session-capabilities` | Internal event carrying compiled capabilities from... |
| `SystemErrorEvent` | Error | `system.error` | Generic error notification sent to client. |
| `SystemNotificationEvent` | Custom | `system-notification` | Generic notification event for system-level messag... |

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

### Location

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `LocationCreatedEvent` | Lifecycle (Created) | `location.created` | Published to location.created when a location is c... |
| `LocationDeletedEvent` | Lifecycle (Deleted) | `location.deleted` | Published to location.deleted when a location is d... |
| `LocationUpdatedEvent` | Lifecycle (Updated) | `location.updated` | Published to location.updated when a location is u... |

### Permissions

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AuthSessionEvent` | Custom | `auth-session` | Authentication-related session events (login, logo... |
| `PermissionRecompileRequest` | Custom | `permission-recompile-request` | Request to trigger bulk permission recompilation. |
| `RedisOperationEvent` | Custom | `redis-operation` | Internal event for Redis operation tracking and mo... |
| `ServiceRegistrationEvent` | Custom | `service-registration` | Published by services on startup to register their... |
| `SessionStateChangeEvent` | Custom | `session-state-change` | Published by services when a session's state chang... |

### Realm

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RealmCreatedEvent` | Lifecycle (Created) | `realm.created` | Published to realm.created when a realm is created |
| `RealmDeletedEvent` | Lifecycle (Deleted) | `realm.deleted` | Published to realm.deleted when a realm is deleted |
| `RealmUpdatedEvent` | Lifecycle (Updated) | `realm.updated` | Published to realm.updated when a realm is updated |

### Relationship

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RelationshipCreatedEvent` | Lifecycle (Created) | `relationship.created` | Published to relationship.created when a relations... |
| `RelationshipDeletedEvent` | Lifecycle (Deleted) | `relationship.deleted` | Published to relationship.deleted when a relations... |
| `RelationshipUpdatedEvent` | Lifecycle (Updated) | `relationship.updated` | Published to relationship.updated when a relations... |

### Relationship Type

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RelationshipTypeCreatedEvent` | Lifecycle (Created) | `relationship-type.created` | Published to relationship-type.created when a rela... |
| `RelationshipTypeDeletedEvent` | Lifecycle (Deleted) | `relationship-type.deleted` | Published to relationship-type.deleted when a rela... |
| `RelationshipTypeUpdatedEvent` | Lifecycle (Updated) | `relationship-type.updated` | Published to relationship-type.updated when a rela... |

### Species

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SpeciesCreatedEvent` | Lifecycle (Created) | `species.created` | Published to species.created when a species is cre... |
| `SpeciesDeletedEvent` | Lifecycle (Deleted) | `species.deleted` | Published to species.deleted when a species is del... |
| `SpeciesUpdatedEvent` | Lifecycle (Updated) | `species.updated` | Published to species.updated when a species is upd... |

### Subscriptions

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SubscriptionUpdatedEvent` | Lifecycle (Updated) | `subscription.updated` | Published when a subscription changes state (creat... |

### Voice

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `VoiceParticipantJoinedEvent` | Custom | `voice-participant-joined` | Published when a participant joins a voice room. |
| `VoiceParticipantLeftEvent` | Custom | `voice-participant-left` | Published when a participant leaves a voice room. |
| `VoiceRoomCreatedEvent` | Lifecycle (Created) | `voice-room.created` | Published when a new voice room is created. |
| `VoiceRoomDeletedEvent` | Lifecycle (Deleted) | `voice-room.deleted` | Published when a voice room is deleted. |
| `VoiceTierUpgradeCompletedEvent` | Custom | `voice-tier-upgrade-completed` | Published when a voice room tier upgrade is comple... |
| `VoiceTierUpgradeRequestedEvent` | Custom | `voice-tier-upgrade-requested` | Published when a voice room needs to upgrade from ... |

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
