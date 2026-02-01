# Subscription Plugin Deep Dive

> **Plugin**: lib-subscription
> **Schema**: schemas/subscription-api.yaml
> **Version**: 1.0.0
> **State Store**: subscription-statestore (MySQL)

---

## Overview

The Subscription service manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. It publishes `subscription.updated` events that Auth and GameSession services consume for real-time authorization updates. Includes a background expiration worker (`SubscriptionExpirationService`) that periodically checks for expired subscriptions and deactivates them. The service is internal-only (never internet-facing) and serves as the canonical source for subscription state.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for subscription records and indexes |
| lib-messaging (`IMessageBus`) | Publishing `subscription.updated` events |
| lib-messaging (`IEventConsumer`) | Event handler registration infrastructure (no actual handlers registered) |
| lib-game-service (`IGameServiceClient`) | Resolving service metadata (stub name, display name) during subscription creation; validating service existence for stubName queries |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth (`AuthService`) | Calls `QueryCurrentSubscriptionsAsync` via `ISubscriptionClient` during session creation |
| lib-auth (`TokenService`) | Calls `ISubscriptionClient` for subscription validation during token operations |
| lib-auth (`AuthServiceEvents`) | Subscribes to `subscription.updated` event to propagate authorization changes to active sessions |
| lib-game-session (`GameSessionService`) | Calls `ISubscriptionClient` to validate subscriptions during session join |
| lib-game-session (`GameSessionStartupService`) | Calls `ISubscriptionClient` for subscription discovery at startup |
| lib-game-session (`GameSessionServiceEvents`) | Subscribes to `subscription.updated` event to update subscription cache and publish/revoke shortcuts |

---

## State Storage

**Store**: `subscription-statestore` (Backend: MySQL, Constant: `StateStoreDefinitions.Subscription`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `subscription:{subscriptionId}` | `SubscriptionDataModel` | Full subscription record (internal model with Unix timestamps) |
| `account-subscriptions:{accountId}` | `List<Guid>` | Index of subscription IDs belonging to an account |
| `service-subscriptions:{serviceId}` | `List<Guid>` | Index of subscription IDs for a game service |
| `subscription-index` | `List<Guid>` | Global index of all subscription IDs (used by expiration worker) |

**Note**: The `SubscriptionDataModel` is an internal class using Unix timestamps to avoid serialization issues. It maps to the public `SubscriptionInfo` model via `MapToSubscriptionInfo()`.

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

This plugin does not consume external events. The `IEventConsumer` is injected but only the default no-op handler is registered (the comment in the constructor referencing "SubscriptionServiceEvents.cs" is outdated - no such file exists).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ExpirationCheckIntervalMinutes` | `SUBSCRIPTION_EXPIRATION_CHECK_INTERVAL_MINUTES` | `5` | Interval between expiration check cycles in the background worker |
| `ExpirationGracePeriodSeconds` | `SUBSCRIPTION_EXPIRATION_GRACE_PERIOD_SECONDS` | `30` | Grace period after expiration before marking inactive (prevents race conditions with last-second renewals) |
| `StartupDelaySeconds` | `SUBSCRIPTION_STARTUP_DELAY_SECONDS` | `30` | Delay before first expiration check after service start (allows dependencies to initialize) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SubscriptionService>` | Singleton | Structured logging |
| `SubscriptionServiceConfiguration` | Singleton | Typed configuration access |
| `IStateStoreFactory` | Singleton | State store access for subscription persistence |
| `IMessageBus` | Singleton | Event publishing for `subscription.updated` |
| `IGameServiceClient` | Scoped | Resolves service metadata during creation/query |
| `IEventConsumer` | Singleton | Event registration infrastructure (no handlers) |
| `SubscriptionExpirationService` | HostedService | Background worker for periodic expiration checks |

**Service Lifetime**: `SubscriptionService` is registered as **Singleton**.

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

1. ~~**`AuthorizationSuffix` config property**~~: **FIXED** (2026-01-31) - Removed the dead configuration property from schema, regenerated configuration class, and removed unused property accessor from service.

2. ~~**Misleading code comment**~~: **FIXED** (2026-02-01) - Updated the comment on line 49 in `SubscriptionService.cs` to accurately reflect that the service only publishes events, it doesn't consume them.

---

## Potential Extensions

1. **Subscription tiers**: Support multiple subscription levels (free, premium, enterprise) with different access grants and feature flags.

2. **Usage-based expiration**: Track API call counts or usage metrics and expire when quota exhausted.

3. **Batch subscription management**: Admin endpoint for bulk-creating subscriptions (e.g., game launch grants, promotional campaigns).

4. **Subscription history**: Immutable audit trail of all state transitions with timestamps and actor IDs.

5. **Subscription transfer**: Allow transferring subscriptions between accounts (gift/resale scenarios).

6. **Proration support**: Handle mid-cycle upgrades/downgrades with prorated billing periods.

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

6. **Data integrity validation in expiration worker**: The expiration worker explicitly checks for corrupted subscriptions with null/empty `StubName` and publishes error events for them rather than crashing. These subscriptions are skipped but not removed from the index.

### Design Considerations (Requires Planning)

1. **No optimistic concurrency on subscription operations**: `UpdateSubscriptionAsync`, `CancelSubscriptionAsync`, `RenewSubscriptionAsync`, and `ExpireSubscriptionAsync` perform read-modify-write operations without distributed locks or ETag-based optimistic concurrency. Two simultaneous operations could produce inconsistent state (e.g., cancel and renew arriving simultaneously could toggle `IsActive` unexpectedly).

2. **Read-modify-write on indexes without distributed locks**: `AddToAccountIndexAsync`, `AddToServiceIndexAsync`, and `AddToSubscriptionIndexAsync` perform read-modify-write operations without distributed locks. Two instances racing to add subscriptions could lose an index entry. The `Contains` check before adding provides some protection but is not atomic.

3. **Index cleanup only applies to global index**: While the expiration worker uses ETag-based optimistic concurrency to clean `subscription-index`, there is no mechanism to clean `account-subscriptions:{accountId}` or `service-subscriptions:{serviceId}` indexes. These grow indefinitely with cancelled/expired entries, potentially impacting query performance over time.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/223 -->

4. **Event publishing without transactional outbox**: State store update and event publish are separate operations (no transactional outbox pattern). However, lib-messaging's `TryPublishAsync` implements aggressive retry: if RabbitMQ is unavailable, messages are buffered in-memory and retried every 5 seconds. The node crashes if the buffer exceeds 10,000 messages or 5 minutes age - making failures visible rather than silent. True event loss only occurs if the node dies before the buffer flushes. The expiration worker provides additional resilience for expired subscriptions by re-publishing events on each cycle. This is the standard Bannou architecture used by all services (see MESSAGING.md Quirk #1).

5. **No subscription deletion endpoint**: There is no endpoint to permanently delete subscription records. The indexes grow indefinitely with cancelled/expired entries. This may be intentional (audit trail) but should be documented as a design decision.

6. ~~**Dead configuration property**~~: **FIXED** (2026-01-31) - Removed `AuthorizationSuffix` from schema, regenerated configuration, removed accessor from service.

---

## Work Tracking

*This section tracks active development work managed by the `/audit-plugin` workflow.*

### Completed

| Date | Gap | Action |
|------|-----|--------|
| 2026-02-01 | Misleading code comment referencing non-existent file | Fixed comment on line 49 to accurately reflect the service only publishes events |
| 2026-01-31 | Dead `AuthorizationSuffix` config property | Removed from schema, regenerated config, removed service accessor, removed test |
