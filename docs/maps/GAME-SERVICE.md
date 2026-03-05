# Game Service Implementation Map

> **Plugin**: lib-game-service
> **Schema**: schemas/game-service-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/GAME-SERVICE.md](../plugins/GAME-SERVICE.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-game-service |
| Layer | L2 GameFoundation |
| Endpoints | 5 |
| State Stores | game-service-statestore (MySQL), game-service-lock (Redis) |
| Events Published | 3 (game-service.created, game-service.updated, game-service.deleted) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `game-service-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `game-service:{serviceId}` | `GameServiceRegistryModel` | Service data (name, stub, description, active status, autoLobbyEnabled) |
| `game-service-stub:{stubName}` | `string` | Stub name (lowercased) to service ID lookup index |
| `game-service-list` | `List<Guid>` | Master list of all service IDs |

**Store**: `game-service-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `game-service-stub:{stubName}` | Distributed lock for stub name uniqueness during create |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for service registry, stub index, and service list |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed lock on stub name uniqueness during create |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle events |
| lib-messaging (IEventConsumer) | L0 | Hard | Event handler registration (no handlers registered) |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation for async helpers |
| lib-resource (IResourceClient) | L1 | Hard | Reference checking and cleanup coordination on delete |

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `game-service.created` | `GameServiceCreatedEvent` | CreateService — after all state writes complete |
| `game-service.updated` | `GameServiceUpdatedEvent` | UpdateService — only when at least one field changed; includes `changedFields` |
| `game-service.deleted` | `GameServiceDeletedEvent` | DeleteService — after all state deletions complete; includes optional `deletedReason` |

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<GameServiceService>` | Structured logging |
| `GameServiceServiceConfiguration` | Typed configuration (ServiceListRetryAttempts) |
| `IStateStoreFactory` | State store access (three stores constructor-cached as readonly fields: `_registryStore`, `_stringStore`, `_listStore`) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event registration (no handlers) |
| `IDistributedLockProvider` | Distributed locks for stub name uniqueness |
| `IResourceClient` | Reference checking and cleanup on delete |
| `ITelemetryProvider` | Telemetry span creation |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| ListServices | POST /game-service/services/list | user | - | - |
| GetService | POST /game-service/services/get | user | - | - |
| CreateService | POST /game-service/services/create | admin | service, stub-index, list | game-service.created |
| UpdateService | POST /game-service/services/update | admin | service | game-service.updated |
| DeleteService | POST /game-service/services/delete | admin | service, stub-index, list | game-service.deleted |

---

## Methods

### ListServices
POST /game-service/services/list | Roles: [user]

```
READ list-store:"game-service-list" -> serviceIds
IF serviceIds is null
  RETURN (200, { Services: [], TotalCount: 0 })
FOREACH serviceId in serviceIds
  READ model-store:"game-service:{serviceId}" -> model
  IF model is null -> skip
  IF body.ActiveOnly AND NOT model.IsActive -> skip
  // collect into results
// apply body.Skip / body.Take pagination in memory
RETURN (200, { Services: paginated, TotalCount: filtered count before pagination })
```

### GetService
POST /game-service/services/get | Roles: [user]

```
IF body.ServiceId has value
  READ model-store:"game-service:{body.ServiceId}" -> model
ELSE
  READ string-store:"game-service-stub:{body.StubName.ToLower()}" -> resolvedId
  IF resolvedId is empty                             -> 404
  READ model-store:"game-service:{resolvedId}" -> model
IF model is null                                     -> 404
RETURN (200, ServiceInfo from model)
```

### CreateService
POST /game-service/services/create | Roles: [admin]

```
normalizedStubName = body.StubName.ToLower()
LOCK game-service-lock:"game-service-stub:{normalizedStubName}"
                                                     -> 409 if lock fails
  READ string-store:"game-service-stub:{normalizedStubName}" -> existingId
  IF existingId exists                               -> 409 // duplicate stub name
  serviceId = new Guid
  WRITE model-store:"game-service:{serviceId}" <- GameServiceRegistryModel from request
  WRITE string-store:"game-service-stub:{normalizedStubName}" <- serviceId.ToString()
  // AddToServiceListAsync (ETag retry loop, up to config.ServiceListRetryAttempts)
  READ list-store:"game-service-list" [with ETag] -> (serviceIds, etag)
  IF serviceId already in list -> return // idempotent
  ETAG-WRITE list-store:"game-service-list" <- serviceIds + serviceId
  // retry on ETag conflict; log warning and continue if retries exhausted
PUBLISH game-service.created { gameServiceId, stubName, displayName, description, isActive, autoLobbyEnabled, createdAt }
RETURN (200, ServiceInfo)
```

### UpdateService
POST /game-service/services/update | Roles: [admin]

```
READ model-store:"game-service:{body.ServiceId}" -> model
IF model is null                                     -> 404
// track changed fields
IF body.DisplayName provided AND different -> update model, add "displayName" to changedFields
IF body.Description provided AND different -> update model, add "description" to changedFields
IF body.IsActive provided AND different -> update model, add "isActive" to changedFields
IF body.AutoLobbyEnabled provided AND different -> update model, add "autoLobbyEnabled" to changedFields
IF changedFields is empty
  RETURN (200, ServiceInfo from model)               // no write, no event
WRITE model-store:"game-service:{body.ServiceId}" <- updated model
PUBLISH game-service.updated { gameServiceId, stubName, displayName, description, isActive, autoLobbyEnabled, createdAt, updatedAt, changedFields }
RETURN (200, ServiceInfo)
```

### DeleteService
POST /game-service/services/delete | Roles: [admin]

```
READ model-store:"game-service:{body.ServiceId}" -> model
IF model is null                                     -> 404
// Resource cleanup check (T28 pattern)
CALL IResourceClient.CheckReferencesAsync({ resourceType: "game-service", resourceId: body.ServiceId }) -> check
IF check.RefCount > 0
  CALL IResourceClient.ExecuteCleanupAsync({ resourceType: "game-service", resourceId: body.ServiceId, policy: AllRequired }) -> result
  IF result is null OR !result.Success               -> 409
// catches ApiException from resource calls           -> 409
DELETE model-store:"game-service:{body.ServiceId}"
IF model.StubName non-empty
  DELETE string-store:"game-service-stub:{model.StubName}"
// RemoveFromServiceListAsync (ETag retry loop, up to config.ServiceListRetryAttempts)
READ list-store:"game-service-list" [with ETag] -> (serviceIds, etag)
IF serviceIds is null OR serviceId not in list -> return // idempotent
ETAG-WRITE list-store:"game-service-list" <- serviceIds - serviceId
// retry on ETag conflict; log warning and continue if retries exhausted
PUBLISH game-service.deleted { gameServiceId, stubName, displayName, description, isActive, autoLobbyEnabled, createdAt, updatedAt, deletedReason: body.Reason }
RETURN 200
```

---

## Background Services

No background services.
