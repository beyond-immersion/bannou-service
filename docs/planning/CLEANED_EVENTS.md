# Cleaned Events Inventory

Events removed or flagged during schema cleanup (2025-12-30).

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
