# Subscription Plugin Deep Dive

> **Plugin**: lib-subscription
> **Schema**: schemas/subscription-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: subscription-statestore (MySQL), subscription-lock (Redis)

---

## Overview

The Subscription service (L2 GameFoundation) manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. Publishes `subscription.updated` events consumed by GameSession for real-time shortcut publishing, and pushes `subscription.status_changed` client events to connected players via WebSocket account-session routing. Includes a background expiration worker that periodically deactivates expired subscriptions. Internal-only, serves as the canonical source for subscription state.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for subscription records and indexes; constructor-cached store references |
| lib-state (`IDistributedLockProvider`) | Distributed locking for all mutating operations (create, update, cancel, renew, expire) |
| lib-messaging (`IMessageBus`) | Publishing `subscription.updated` events |
| lib-telemetry (`ITelemetryProvider`) | Telemetry span instrumentation for all async methods |
| lib-game-service (`IGameServiceClient`) | Resolving service metadata (stub name, display name) during subscription creation; validating service existence for stubName queries |
| lib-connect (`IEntitySessionRegistry`) | Publishing client events to all WebSocket sessions for an account via `"account"` entity type routing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-game-session (`GameSessionService`) | Calls `ISubscriptionClient` to validate subscriptions during session join |
| lib-game-session (`GameSessionStartupService`) | Calls `ISubscriptionClient` for subscription discovery at startup |
| lib-game-session (`GameSessionServiceEvents`) | Subscribes to `subscription.updated` event to update subscription cache and publish/revoke shortcuts |

---

## State Storage

**Primary Store**: `subscription-statestore` (Backend: MySQL, Constant: `StateStoreDefinitions.Subscription`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `subscription:{subscriptionId}` | `SubscriptionDataModel` | Full subscription record (internal model with Unix timestamps) |
| `account-subscriptions:{accountId}` | `List<Guid>` | Index of subscription IDs belonging to an account |
| `service-subscriptions:{serviceId}` | `List<Guid>` | Index of subscription IDs for a game service |
| `subscription-index` | `List<Guid>` | Global index of all subscription IDs (used by expiration worker) |

**Lock Store**: `subscription-lock` (Backend: Redis, Constant: `StateStoreDefinitions.SubscriptionLock`)

| Lock Resource Pattern | Purpose |
|-----------------------|---------|
| `account:{accountId}:service:{serviceId}` | Prevents duplicate active subscriptions during creation |
| `{subscriptionId}` | Serializes update, cancel, renew, and expire operations per subscription |

**Note**: The `SubscriptionDataModel` is an internal class using Unix timestamps to avoid serialization issues. It maps to the public `SubscriptionInfo` model via `MapToSubscriptionInfo()`. Store references (`_subscriptionStore`, `_indexStore`) are cached in the constructor per FOUNDATION TENETS.

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `subscription.updated` | `SubscriptionUpdatedEvent` | All subscription state changes (see actions below) |

**Action values in `SubscriptionUpdatedEvent`:**
| Action | Trigger |
|--------|---------|
| `Created` | New subscription created via `CreateSubscriptionAsync` |
| `Updated` | Expiration date or active status changed via `UpdateSubscriptionAsync` |
| `Cancelled` | User cancels subscription via `CancelSubscriptionAsync` |
| `Renewed` | Subscription extended/reactivated via `RenewSubscriptionAsync` |
| `Expired` | Background worker deactivates expired subscription |

### Consumed Events

This plugin does not consume external events. No `SubscriptionServiceEvents.cs` file exists.

### Client Events (WebSocket Push)

| Event Name | Event Type | Routing | Trigger |
|------------|-----------|---------|---------|
| `subscription.status_changed` | `SubscriptionStatusChangedEvent` | `IEntitySessionRegistry` → `"account"` entity type | All subscription state changes |

**Schema**: `schemas/subscription-client-events.yaml`
**Generated Model**: `plugins/lib-subscription/Generated/SubscriptionClientEventsModels.cs`

**Payload fields**: `accountId`, `serviceId`, `serviceName`, `action` (SubscriptionAction enum), `isActive`, `expiresAt` (nullable).

**Routing**: Uses `IEntitySessionRegistry.PublishToEntitySessionsAsync("account", accountId, event, ct)` to push to all WebSocket sessions for the affected account. The `"account"` entity type is maintained natively by Connect (no Gardener registration required).

**Why this matters**: Background expiration by the `SubscriptionExpirationService` deactivates subscriptions without player initiation. Without client events, the player would not know their subscription expired until their next API call fails. Admin renewals similarly change subscription state without the player's knowledge. The client event ensures immediate notification for all 5 action types: `created`, `updated`, `cancelled`, `expired`, `renewed`.

---

## Configuration

| Property | Env Var | Default | Min | Purpose |
|----------|---------|---------|-----|---------|
| `ExpirationCheckIntervalMinutes` | `SUBSCRIPTION_EXPIRATION_CHECK_INTERVAL_MINUTES` | `5` | `1` | Interval between expiration check cycles in the background worker |
| `ExpirationGracePeriodSeconds` | `SUBSCRIPTION_EXPIRATION_GRACE_PERIOD_SECONDS` | `30` | `0` | Grace period after expiration before marking inactive (prevents race conditions with last-second renewals) |
| `StartupDelaySeconds` | `SUBSCRIPTION_STARTUP_DELAY_SECONDS` | `30` | `0` | Delay before first expiration check after service start (allows dependencies to initialize) |
| `LockTimeoutSeconds` | `SUBSCRIPTION_LOCK_TIMEOUT_SECONDS` | `10` | `1` | Distributed lock expiry for all mutating subscription operations |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SubscriptionService>` | Singleton | Structured logging |
| `SubscriptionServiceConfiguration` | Singleton | Typed configuration access |
| `IStateStoreFactory` | Singleton | State store access; stores cached in constructor |
| `IMessageBus` | Singleton | Event publishing for `subscription.updated` |
| `IDistributedLockProvider` | Singleton | Distributed locking for all mutating operations |
| `ITelemetryProvider` | Singleton | Telemetry span instrumentation |
| `IGameServiceClient` | Singleton | Resolves service metadata during creation/query |
| `IEntitySessionRegistry` | Singleton | Publishes client events to account's WebSocket sessions |
| `SubscriptionExpirationService` | HostedService | Background worker for periodic expiration checks |

**Service Lifetime**: `SubscriptionService` is registered as **Singleton** (unusual for Bannou - see Quirk #7).

---

## API Endpoints (Implementation Notes)

All endpoints follow standard POST-only pattern with request bodies.

- **GetAccountSubscriptions** (`/subscription/account/list`): Loads account index, fetches each subscription by ID, filters by `includeInactive` and `includeExpired` flags. Returns empty list (not 404) for accounts with no subscriptions.

- **QueryCurrentSubscriptions** (`/subscription/query`): Returns only active, non-expired subscriptions. Requires at least one of `accountId` or `stubName`. Has **empty x-permissions** (`[]`) - designed for internal service-to-service calls without auth. When querying by `stubName` only, calls `IGameServiceClient` to resolve the service ID first.

- **GetSubscription** (`/subscription/get`): Direct lookup by subscription ID. Returns 404 if not found.

- **CreateSubscription** (`/subscription/create`): Admin-only. Validates service exists via `IGameServiceClient`, checks for existing active subscription (409 Conflict), denormalizes `StubName` and `DisplayName` into the subscription record. Adds to three indexes: account, service, and global.

- **UpdateSubscription** (`/subscription/update`): Admin-only. Updates expiration date and/or active status. `ExpirationDate = null` in request means "leave unchanged" (not "remove expiration"). Uses action "updated" in event.

- **CancelSubscription** (`/subscription/cancel`): User-accessible (role: user). Sets `IsActive=false`, records `CancelledAtUnix` and optional `Reason`. Does NOT remove from indexes.

- **RenewSubscription** (`/subscription/renew`): Admin-only. Extends from current expiration if still valid, or from "now" if already expired. Reactivates cancelled subscriptions by setting `IsActive=true` and clearing cancellation fields. Does NOT re-add to global index (already present).

---

## Visual Aid

```
State Key Relationships & Index Cleanup
========================================

  account-subscriptions:{accountId}     service-subscriptions:{serviceId}
           │                                      │
           │  (List<Guid>)                        │  (List<Guid>)
           ▼                                      ▼
     ┌─────────────────────────────────────────────────┐
     │              subscription:{id}                   │
     │           (SubscriptionDataModel)               │
     │                                                 │
     │  ┌─ SubscriptionId: Guid                        │
     │  ├─ AccountId: Guid                             │
     │  ├─ ServiceId: Guid                             │
     │  ├─ StubName: string (denormalized)             │
     │  ├─ DisplayName: string (denormalized)          │
     │  ├─ StartDateUnix: long                         │
     │  ├─ ExpirationDateUnix: long?                   │
     │  ├─ IsActive: bool                              │
     │  ├─ CancelledAtUnix: long?                      │
     │  ├─ CancellationReason: string?                 │
     │  ├─ CreatedAtUnix: long                         │
     │  └─ UpdatedAtUnix: long?                        │
     └─────────────────────────────────────────────────┘
                          ▲
                          │  (Guid in list)
                          │
              subscription-index (global)
                          │
      ┌───────────────────┼───────────────────┐
      │                   │                   │
      ▼                   ▼                   ▼
 [Expiration Worker]  [Deleted?]         [Inactive/No Expiry]
      │                   │                   │
      │  Marks expired    │  Remove from      │  Remove from
      │  subscriptions    │  index            │  index
      │  as inactive      │                   │
      └───────────────────┴───────────────────┘

  NOTE: account-subscriptions and service-subscriptions indexes
        are NEVER cleaned - they grow indefinitely with cancelled
        and expired entries. Only subscription-index is maintained
        by the expiration worker.
```

---

## Stubs & Unimplemented Features

None identified.

---

## Potential Extensions

1. **Subscription tiers**: Support multiple subscription levels (free, premium, enterprise) with different access grants and feature flags.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/511 -->

2. **Usage-based expiration**: Track API call counts or usage metrics and expire when quota exhausted.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/512 -->

3. **Batch subscription management**: Admin endpoint for bulk-creating subscriptions (e.g., game launch grants, promotional campaigns).
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/513 -->

4. **Subscription history**: Immutable audit trail of all state transitions with timestamps and actor IDs.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/514 -->

5. **Subscription transfer**: Allow transferring subscriptions between accounts (gift/resale scenarios).
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/515 -->

6. **Proration support**: Handle mid-cycle upgrades/downgrades with prorated billing periods.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/516 -->

7. ~~**Client events for real-time subscription status**~~: **FIXED** (2026-02-28) - Implemented `SubscriptionStatusChangedEvent` client event pushed via `IEntitySessionRegistry` account routing for all 5 subscription actions. See Client Events section above. ([#500](https://github.com/beyond-immersion/bannou-service/issues/500))

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Denormalized service metadata**: `StubName` and `DisplayName` are copied from GameService at creation time. If the game service's display name later changes, existing subscriptions retain the old name. This is intentional to provide stable display names for historical records.

2. **Null means "don't change" in updates**: `ExpirationDate = null` in an `UpdateSubscriptionRequest` means "leave unchanged", not "remove expiration". There is no API mechanism to convert a time-limited subscription to unlimited via the update endpoint (must cancel and recreate).

3. **Renewal extension base date logic**: `RenewSubscriptionAsync` with `extensionDays` extends from current expiration if still valid, but from "now" if already expired. A subscription that expired 5 days ago with 30-day extension expires 30 days from now (not 25 days). This prevents "stacking" lost time and ensures renewals always provide the full extension period.

4. **Global index cleanup only**: The expiration worker cleans the `subscription-index` by removing processed entries (expired, inactive, unlimited, or deleted subscriptions). However, `account-subscriptions` and `service-subscriptions` indexes are never cleaned - they accumulate cancelled/expired entries indefinitely.

5. **Grace period for expiration**: Subscriptions are only marked expired if `ExpirationDateUnix <= now - gracePeriodSeconds`. This 30-second default grace period prevents race conditions where a subscription expires while a renewal request is in flight.

6. **Expiration worker delegates to service**: The background worker scans for expired subscriptions but delegates actual expiration to `SubscriptionService.ExpireSubscriptionAsync` via the `ISubscriptionService` interface. This ensures locking and event publishing are handled consistently regardless of whether expiration is triggered by the worker or a future API call. The worker handles index scanning, eligibility filtering (inactive, unlimited, deleted subscriptions are removed from the index), and ETag-based index cleanup.

7. **Singleton service lifetime**: `SubscriptionService` is registered as `ServiceLifetime.Singleton` instead of the standard Bannou `Scoped` lifetime. This is safe because the service holds no in-memory state - all state goes through `IStateStoreFactory`. The singleton avoids unnecessary re-instantiation per request.

8. **Renewal always reactivates**: `RenewSubscriptionAsync` unconditionally sets `IsActive = true` and clears cancellation fields (`CancelledAtUnix`, `CancellationReason`). There is no way to extend a subscription's duration without also reactivating it - "extend but keep cancelled" is impossible.

9. **`ExpireSubscriptionAsync` via partial interface extension**: The `ExpireSubscriptionAsync` method is defined on `ISubscriptionService` via a partial interface extension in `ISubscriptionServiceExtensions.cs` (following the pattern established by lib-permission). This allows the background worker to call it through DI-resolved `ISubscriptionService` without it being an HTTP endpoint. The method takes a `Guid` parameter (not string) per IMPLEMENTATION TENETS type safety rules.

10. **Event publishing without transactional outbox**: State store update and event publish are separate operations (no transactional outbox pattern). This is the standard Bannou architecture used by all services (see MESSAGING.md Quirk #1). `TryPublishAsync` implements aggressive retry with in-memory buffering (5-second retry, crash-fast at 10,000 messages or 5 minutes). True event loss only occurs if the node dies before the buffer flushes. The expiration worker provides additional resilience for expired subscriptions by re-publishing events on each cycle.

### Design Considerations (Requires Planning)

1. **Index cleanup only applies to global index**: While the expiration worker uses ETag-based optimistic concurrency to clean `subscription-index`, there is no mechanism to clean `account-subscriptions:{accountId}` or `service-subscriptions:{serviceId}` indexes. These grow indefinitely with cancelled/expired entries, potentially impacting query performance over time.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/517 -->
2. **No subscription deletion endpoint**: There is no endpoint to permanently delete subscription records. The indexes grow indefinitely with cancelled/expired entries. This may be intentional (audit trail) but should be documented as a design decision.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/518 -->

3. ~~**First-write race on account/service indexes**~~: **FIXED** (2026-02-28) - Replaced the flawed optimistic concurrency approach in `AddToIndexAsync` with distributed locking per index key. The old code bypassed ETag-based concurrency when the index key didn't exist yet (`etag == null` path did unconditional `SaveAsync`), allowing concurrent first-writes to overwrite each other. The new implementation acquires a distributed lock on `index:{indexKey}` before reading/modifying, serializing all writes to each index key and eliminating the race.

---

## Work Tracking

*This section tracks active development work managed by the `/audit-plugin` workflow.*

### Completed
- **2026-02-28**: Issue #500 - Added `SubscriptionStatusChangedEvent` client event via `IEntitySessionRegistry` account routing for all 5 subscription actions (created, updated, cancelled, expired, renewed)
- **2026-02-28**: Fixed first-write race on account/service indexes in `AddToIndexAsync` by replacing optimistic concurrency with distributed locking per index key
