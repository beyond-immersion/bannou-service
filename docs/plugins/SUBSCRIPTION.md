# Subscription Plugin Deep Dive

> **Plugin**: lib-subscription
> **Schema**: schemas/subscription-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: subscription-statestore (MySQL), subscription-lock (Redis)
> **Implementation Map**: [docs/maps/SUBSCRIPTION.md](../maps/SUBSCRIPTION.md)
> **Short**: Account-to-game access mapping with time-limited subscriptions and expiration worker

---

## Overview

The Subscription service (L2 GameFoundation) manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. Publishes `subscription.updated` events consumed by GameSession for real-time shortcut publishing, and pushes `subscription.status-changed` client events to connected players via WebSocket account-session routing. Includes a background expiration worker that periodically deactivates expired subscriptions. Serves as the canonical source for subscription state. Most mutating endpoints are service-to-service (admin/internal), but account listing, subscription get, and cancel are user-facing.

Client events are routed via `IEntitySessionRegistry.PublishToEntitySessionsAsync("account", accountId, ...)` to all WebSocket sessions for the affected account. This is especially important for background expiration (the player didn't initiate the state change) and admin renewals.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-game-session (`GameSessionService`) | Calls `ISubscriptionClient` to validate subscriptions during session join |
| lib-game-session (`GameSessionStartupService`) | Calls `ISubscriptionClient` for subscription discovery at startup |
| lib-game-session (`GameSessionServiceEvents`) | Subscribes to `subscription.updated` event to update subscription cache and publish/revoke shortcuts |

---

## Configuration

| Property | Env Var | Default | Min | Purpose |
|----------|---------|---------|-----|---------|
| `ExpirationCheckIntervalMinutes` | `SUBSCRIPTION_EXPIRATION_CHECK_INTERVAL_MINUTES` | `5` | `1` | Interval between expiration check cycles in the background worker |
| `ExpirationGracePeriodSeconds` | `SUBSCRIPTION_EXPIRATION_GRACE_PERIOD_SECONDS` | `30` | `0` | Grace period after expiration before marking inactive (prevents race conditions with last-second renewals) |
| `StartupDelaySeconds` | `SUBSCRIPTION_STARTUP_DELAY_SECONDS` | `30` | `0` | Delay before first expiration check after service start (allows dependencies to initialize) |
| `LockTimeoutSeconds` | `SUBSCRIPTION_LOCK_TIMEOUT_SECONDS` | `10` | `1` | Distributed lock expiry for all mutating subscription operations |

---

## Visual Aid

```
State Key Relationships & Index Cleanup
========================================

 account-subscriptions:{accountId} service-subscriptions:{serviceId}
 │ │
 │ (List<Guid>) │ (List<Guid>)
 ▼ ▼
 ┌─────────────────────────────────────────────────┐
 │ subscription:{id} │
 │ (SubscriptionDataModel) │
 │ │
 │ ┌─ SubscriptionId: Guid │
 │ ├─ AccountId: Guid │
 │ ├─ ServiceId: Guid │
 │ ├─ StubName: string (denormalized) │
 │ ├─ DisplayName: string (denormalized) │
 │ ├─ StartDateUnix: long │
 │ ├─ ExpirationDateUnix: long? │
 │ ├─ IsActive: bool │
 │ ├─ CancelledAtUnix: long? │
 │ ├─ CancellationReason: string? │
 │ ├─ CreatedAtUnix: long │
 │ └─ UpdatedAtUnix: long? │
 └─────────────────────────────────────────────────┘
 ▲
 │ (Guid in list)
 │
 subscription-index (global)
 │
 ┌───────────────────┼───────────────────┐
 │ │ │
 ▼ ▼ ▼
 [Expiration Worker] [Deleted?] [Inactive/No Expiry]
 │ │ │
 │ Marks expired │ Remove from │ Remove from
 │ subscriptions │ index │ index
 │ as inactive │ │
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

1. **Batch subscription management**: Admin endpoint for bulk-creating subscriptions (e.g., game launch grants, promotional campaigns).
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/513 -->

2. **Subscription transfer**: Allow transferring subscriptions between accounts (gift/resale scenarios).
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/515 -->

### Closed Extensions (Resolved by Architecture)

- **Subscription tiers** (#511): Tiers map to Permission states via `UpdateSessionState`. Subscription optionally carries a `tier` string field; Permission's existing `service × state × role → endpoints` matrix handles differentiated access. No Subscription redesign needed.
- **Usage-based expiration** (#512): Usage metering/quotas belong in Connect middleware or a dedicated metering service, not in an access-control primitive.
- **Subscription history** (#514): Analytics (L4) is the canonical event aggregation service. Subscription already publishes `subscription.updated` events for every state change — Analytics consumes these for audit trails.
- **Proration support** (#516): Billing/monetary concern belonging in Currency (L2) or external billing integration, not in access-control.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Missing account deletion cleanup handler (Foundation Tenets — T28 Account Deletion Cleanup Obligation)**: Subscription stores data with account ownership (`accountId` is a direct ownership key) but does not subscribe to `account.deleted` and clean up subscriptions for deleted accounts. Per Foundation Tenets, every service with account-owned data MUST implement `HandleAccountDeletedAsync` in `*ServiceEvents.cs`. This is not a design question — the pattern is unambiguous. Reference implementation: `lib-collection/CollectionServiceEvents.cs`. See [#566](https://github.com/beyond-immersion/bannou-service/issues/566).
<!-- AUDIT:TODO:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/566 -->

2. **Missing lib-resource registration for GameService references (Foundation Tenets — T28 Resource-Managed Cleanup)**: Subscription records reference `serviceId` (a GameService entity at L2), but no `x-references` declaration exists in the schema and no cleanup endpoint is implemented. Per Foundation Tenets, dependent data cleanup for non-account entities MUST use lib-resource — declare `x-references` in the API schema, implement a `delete-by-service` cleanup endpoint, and register via generated `RegisterResourceCleanupCallbacksAsync()`. See [#567](https://github.com/beyond-immersion/bannou-service/issues/567).
<!-- AUDIT:NEEDS_DESIGN:2026-03-05:https://github.com/beyond-immersion/bannou-service/issues/567 -->

3. **CancelSubscription accepts accountId from user-facing WebSocket (Foundation Tenets — T32 Account Identity Boundary)**: The `CancelSubscription` endpoint has `x-permissions: [role: user]` (user-facing via WebSocket) but accepts `accountId` in the request body for ownership verification. Per Foundation Tenets Rule 1, client-facing endpoints MUST NOT accept `accountId` in request bodies. The correct pattern is server-injected `accountId` via the shortcut system (Rule 2), consistent with how GameSession handles join/leave.

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

11. **Index lock failure is fatal**: `AddToIndexAsync` throws `InvalidOperationException` if it cannot acquire the distributed lock on `index:{indexKey}`. This means a `CreateSubscription` call will fail entirely if any index lock cannot be acquired, consistent with the Location service's THROW pattern. The subscription record is saved before index updates, so a partial failure (record saved, index throw) can leave an orphaned record visible only via direct ID lookup.

### Design Considerations (Requires Planning)

1. **Index cleanup only applies to global index**: While the expiration worker uses ETag-based optimistic concurrency to clean `subscription-index`, there is no mechanism to clean `account-subscriptions:{accountId}` or `service-subscriptions:{serviceId}` indexes. These grow indefinitely with cancelled/expired entries, potentially impacting query performance over time.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/517 -->
2. **No subscription deletion endpoint**: There is no endpoint to permanently delete subscription records. The indexes grow indefinitely with cancelled/expired entries. This may be intentional (audit trail) but should be documented as a design decision.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/518 -->

---

## Work Tracking

*This section tracks active development work managed by the `/audit-plugin` workflow.*

*No active work items.*
