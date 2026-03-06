# Subscription Implementation Map

> **Plugin**: lib-subscription
> **Schema**: schemas/subscription-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](../plugins/SUBSCRIPTION.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-subscription |
| Layer | L2 GameFoundation |
| Endpoints | 7 |
| State Stores | subscription-statestore (MySQL), subscription-lock (Redis) |
| Events Published | 1 (`subscription.updated`) |
| Events Consumed | 0 |
| Client Events | 1 (`subscription.status-changed`) |
| Background Services | 1 (SubscriptionExpirationService) |

---

## State

**Store**: `subscription-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `subscription:{subscriptionId}` | `SubscriptionDataModel` | Full subscription record (internal model with Unix timestamps) |
| `account-subscriptions:{accountId}` | `List<Guid>` | Index of subscription IDs belonging to an account |
| `service-subscriptions:{serviceId}` | `List<Guid>` | Index of subscription IDs for a game service |
| `subscription-index` | `List<Guid>` | Global index of all subscription IDs (used by expiration worker) |

**Store**: `subscription-lock` (Backend: Redis)

| Lock Key Pattern | Purpose |
|------------------|---------|
| `account:{accountId}:service:{serviceId}` | Prevents duplicate active subscriptions during creation |
| `{subscriptionId}` | Serializes update, cancel, renew, and expire operations per subscription |
| `index:{indexKey}` | Serializes concurrent writes to each index key |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for subscription records and indexes |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locking for all mutating operations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing `subscription.updated` events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation |
| lib-connect (IEntitySessionRegistry) | L1 | Hard | Push client events to all WebSocket sessions for an account |
| lib-game-service (IGameServiceClient) | L2 | Hard | Validate service existence and retrieve stub/display names |

No soft dependencies. No DI provider/listener interfaces implemented or consumed.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `subscription.updated` | `SubscriptionUpdatedEvent` | All subscription state changes (action: Created, Updated, Cancelled, Renewed, Expired) |

---

## Events Consumed

This plugin does not consume external events.

---

## Client Events

| Event Name | Event Type | Target |
|------------|-----------|--------|
| `subscription.status-changed` | `SubscriptionStatusChangedClientEvent` | All WebSocket sessions for the affected account via IEntitySessionRegistry |

Published best-effort alongside every `subscription.updated` event. Failure to push does not roll back the mutation.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<SubscriptionService>` | Structured logging |
| `SubscriptionServiceConfiguration` | Typed configuration (lock timeout, expiration intervals) |
| `IStateStoreFactory` | Acquires `_subscriptionStore` and `_indexStore` in constructor (not stored as field) |
| `IDistributedLockProvider` | Distributed locking for mutations and index writes |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Span instrumentation |
| `IGameServiceClient` | Game service metadata resolution |
| `IEntitySessionRegistry` | Account-session client event routing |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetAccountSubscriptions | POST /subscription/account/list | user | - | - |
| QueryCurrentSubscriptions | POST /subscription/query | [] | - | - |
| GetSubscription | POST /subscription/get | user | - | - |
| CreateSubscription | POST /subscription/create | admin | subscription, account-index, service-index, global-index | subscription.updated |
| UpdateSubscription | POST /subscription/update | admin | subscription | subscription.updated |
| CancelSubscription | POST /subscription/cancel | user | subscription | subscription.updated |
| RenewSubscription | POST /subscription/renew | admin | subscription | subscription.updated |

---

## Methods

### GetAccountSubscriptions
POST /subscription/account/list | Roles: [] (service-to-service)

```
READ _indexStore:"account-subscriptions:{accountId}"            -> empty list if null
FOREACH subscriptionId in index
  READ _subscriptionStore:"subscription:{subscriptionId}"
  IF !includeInactive AND !model.IsActive -> skip
  IF !includeExpired AND model has expiration AND expired -> skip
  // MapToSubscriptionInfo for each passing record
RETURN (200, SubscriptionListResponse { subscriptions })
```

---

### QueryCurrentSubscriptions
POST /subscription/query | Roles: []

```
IF accountId is null AND stubName is null/empty -> RETURN (400, null)

IF accountId provided
  READ _indexStore:"account-subscriptions:{accountId}"          -> candidateIds
ELSE IF stubName provided
  CALL IGameServiceClient.GetServiceAsync({ stubName })         -> 200 with serviceId, or empty if 404
  READ _indexStore:"service-subscriptions:{serviceId}"          -> candidateIds

FOREACH subscriptionId in candidateIds
  READ _subscriptionStore:"subscription:{subscriptionId}"
  IF !model.IsActive -> skip
  IF model has expiration AND expired -> skip
  IF stubName provided AND model.StubName != stubName (case-insensitive) -> skip
  // Collect matching subscriptions and distinct accountIds
RETURN (200, QuerySubscriptionsResponse { subscriptions, accountIds })
```

// When both accountId and stubName provided, accountId branch executes;
// stubName filtering happens post-fetch via string comparison.

---

### GetSubscription
POST /subscription/get | Roles: [] (service-to-service)

```
READ _subscriptionStore:"subscription:{subscriptionId}"        -> 404 if null
RETURN (200, SubscriptionInfo)
```

---

### CreateSubscription
POST /subscription/create | Roles: [] (service-to-service)

```
CALL IGameServiceClient.GetServiceAsync({ serviceId })         -> 404 if not found

LOCK subscription-lock:"account:{accountId}:service:{serviceId}" -> 409 if fails
  READ _indexStore:"account-subscriptions:{accountId}"
  FOREACH existingId in index
    READ _subscriptionStore:"subscription:{existingId}"
    IF existing is active -> RETURN (409, null)                 // duplicate active subscription

  // Compute startDate (default: now), expirationDate (from request or durationDays or null)
  WRITE _subscriptionStore:"subscription:{newId}" <- SubscriptionDataModel from request + service metadata

  // AddToIndexAsync for each index (each acquires its own inner lock)
  LOCK subscription-lock:"index:account-subscriptions:{accountId}"
    READ + append + WRITE _indexStore:"account-subscriptions:{accountId}"
  LOCK subscription-lock:"index:service-subscriptions:{serviceId}"
    READ + append + WRITE _indexStore:"service-subscriptions:{serviceId}"
  LOCK subscription-lock:"index:subscription-index"
    READ + append + WRITE _indexStore:"subscription-index"

  PUBLISH subscription.updated { subscriptionId, accountId, serviceId, stubName, displayName, action: Created, isActive: true, expirationDate }
  PUSH subscription.status-changed to account sessions (best-effort)
RETURN (200, SubscriptionInfo)
```

// Inner index locks throw InvalidOperationException if they fail (fatal, consistent with Location pattern).
// StubName and DisplayName are denormalized from GameService at creation time.

---

### UpdateSubscription
POST /subscription/update | Roles: [] (service-to-service)

```
LOCK subscription-lock:"{subscriptionId}"                      -> 409 if fails
  READ _subscriptionStore:"subscription:{subscriptionId}"      -> 404 if null
  IF expirationDate provided -> update model.ExpirationDateUnix
  IF isActive provided -> update model.IsActive
  // Always set UpdatedAtUnix = now
  WRITE _subscriptionStore:"subscription:{subscriptionId}" <- updated model
  PUBLISH subscription.updated { ..., action: Updated }
  PUSH subscription.status-changed to account sessions (best-effort)
RETURN (200, SubscriptionInfo)
```

---

### CancelSubscription
POST /subscription/cancel | Roles: [user]

```
LOCK subscription-lock:"{subscriptionId}"                      -> 409 if fails
  READ _subscriptionStore:"subscription:{subscriptionId}"      -> 404 if null
  IF model.AccountId != body.AccountId                         -> 403 (ownership check)
  // Set IsActive=false, CancelledAtUnix=now, CancellationReason=reason, UpdatedAtUnix=now
  WRITE _subscriptionStore:"subscription:{subscriptionId}" <- updated model
  PUBLISH subscription.updated { ..., action: Cancelled, isActive: false }
  PUSH subscription.status-changed to account sessions (best-effort)
RETURN (200, SubscriptionInfo)
```

// Not idempotent: re-cancelling a cancelled subscription re-writes and re-publishes.
// accountId is required in the request body for ownership verification.

---

### RenewSubscription
POST /subscription/renew | Roles: [] (service-to-service)

```
LOCK subscription-lock:"{subscriptionId}"                      -> 409 if fails
  READ _subscriptionStore:"subscription:{subscriptionId}"      -> 404 if null

  IF newExpirationDate provided -> use it
  ELSE IF extensionDays provided
    IF current expiration exists AND not yet expired -> extend from current expiration
    ELSE -> extend from now
  // If neither provided, expiration unchanged

  // Always: IsActive=true, CancelledAtUnix=null, CancellationReason=null, UpdatedAtUnix=now
  WRITE _subscriptionStore:"subscription:{subscriptionId}" <- updated model
  PUBLISH subscription.updated { ..., action: Renewed, isActive: true }
  PUSH subscription.status-changed to account sessions (best-effort)
RETURN (200, SubscriptionInfo)
```

// Renewal always reactivates and clears cancellation fields.

---

## Background Services

### SubscriptionExpirationService
**Interval**: `config.ExpirationCheckIntervalMinutes` (default: 5 minutes)
**Startup Delay**: `config.StartupDelaySeconds` (default: 30 seconds)
**Purpose**: Periodically scans the global subscription index, expires past-grace-period subscriptions, and prunes the index.

```
// Wait StartupDelay before first cycle
// Each cycle:
READ _indexStore:"subscription-index"                          -> return if null/empty

FOREACH subscriptionId in index
  READ _subscriptionStore:"subscription:{subscriptionId}"
  IF null -> mark for index removal
  IF !IsActive -> mark for index removal
  IF no expiration date -> mark for index removal (unlimited, no need to monitor)
  IF expirationDateUnix <= (now - gracePeriodSeconds)
    CALL SubscriptionService.ExpireSubscriptionAsync(subscriptionId)
    // ExpireSubscription acquires its own lock, sets IsActive=false, publishes events
    mark for index removal

IF any marked for removal
  // CleanupSubscriptionIndexAsync with ETag-based optimistic concurrency (up to 3 retries)
  READ _indexStore:"subscription-index" [with ETag]
  ETAG-WRITE _indexStore:"subscription-index" <- index minus removed IDs
  // Retries on ETag mismatch; defers to next cycle after 3 failures
```
