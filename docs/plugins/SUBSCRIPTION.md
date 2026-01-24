# Subscription Plugin Deep Dive

> **Plugin**: lib-subscription
> **Schema**: schemas/subscription-api.yaml
> **Version**: 1.0.0
> **State Store**: subscription-statestore (MySQL)

---

## Overview

The Subscription service manages user subscriptions to game services, controlling which accounts have access to which games/applications with time-limited access. It publishes `subscription.updated` events that Auth and GameSession services consume for real-time authorization updates. Includes a background expiration worker that automatically deactivates expired subscriptions.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for subscription records and indexes |
| lib-messaging (`IMessageBus`) | Publishing subscription lifecycle events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no handlers currently) |
| lib-game-service (`IGameServiceClient`) | Resolving stub names and fetching service display names during creation |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth (`AuthService`) | Calls `QueryCurrentSubscriptionsAsync` via `ISubscriptionClient` during session creation and token refresh |
| lib-auth (`TokenService`) | Validates active subscriptions for token generation |
| lib-auth (`AuthServiceEvents`) | Subscribes to `subscription.updated` to update session authorizations |
| lib-game-session (`GameSessionService`) | Calls `ISubscriptionClient` to validate subscriptions during session join |
| lib-game-session (`GameSessionServiceEvents`) | Subscribes to `subscription.updated` to update subscriber discovery |

---

## State Storage

**Store**: `subscription-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `subscription:{subscriptionId}` | `SubscriptionDataModel` | Full subscription record |
| `account-subscriptions:{accountId}` | `List<string>` | Subscription IDs for an account |
| `service-subscriptions:{serviceId}` | `List<string>` | Subscription IDs for a game service |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `subscription.updated` (action: `Created`) | New subscription created |
| `subscription.updated` (action: `Updated`) | Expiration or active status changed |
| `subscription.updated` (action: `Cancelled`) | User cancels subscription |
| `subscription.updated` (action: `Renewed`) | Subscription extended or reactivated |
| `subscription.updated` (action: `Expired`) | Background worker deactivates expired subscription |

All actions publish the same `SubscriptionUpdatedEvent` type with an `Action` enum discriminator.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ExpirationCheckIntervalMinutes` | `SUBSCRIPTION_EXPIRATION_CHECK_INTERVAL_MINUTES` | `5` | How often the background worker checks for expired subscriptions |
| `ExpirationGracePeriodSeconds` | `SUBSCRIPTION_EXPIRATION_GRACE_PERIOD_SECONDS` | `30` | Grace period before marking as expired (prevents race conditions) |
| `StartupDelaySeconds` | `SUBSCRIPTION_STARTUP_DELAY_SECONDS` | `30` | Delay before first expiration check after service start |
| `AuthorizationSuffix` | `SUBSCRIPTION_AUTHORIZATION_SUFFIX` | `authorized` | Unused — property exists but is never referenced in implementation |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SubscriptionService>` | Singleton | Structured logging |
| `SubscriptionServiceConfiguration` | Singleton | Typed configuration |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Singleton | Event publishing |
| `IGameServiceClient` | Scoped | Resolves stub names for service metadata |
| `IEventConsumer` | Singleton | Event registration (no handlers) |
| `SubscriptionExpirationService` | HostedService | Background worker for expiration checks |

Service lifetime is **Singleton**.

---

## API Endpoints (Implementation Notes)

- **GetAccountSubscriptions** (`/subscription/account/list`): Fetches account index, loads all subscriptions, filters by `includeInactive` and `includeExpired` flags.
- **QueryCurrentSubscriptions** (`/subscription/query`): Returns only active, non-expired subscriptions. Supports query by `accountId` OR `stubName` OR both. Has **empty x-permissions** (no auth required) — designed for internal service-to-service calls.
- **CreateSubscription**: Calls `IGameServiceClient.GetServiceAsync()` to resolve stub name and display name. Checks for existing active subscription (409 Conflict). Denormalizes service metadata into subscription record.
- **CancelSubscription**: Sets `IsActive=false`, records `CancelledAtUnix` and reason. User-accessible (not admin-only).
- **RenewSubscription**: Extends from current expiration date (or from "now" if already expired). Reactivates cancelled subscriptions.

---

## Visual Aid

```
Subscription Lifecycle & Event Flow
=====================================

  CreateSubscription ──► subscription.updated (Created)
         │                         │
         ▼                         ▼
  [Active subscription]      Auth: updates session
         │                   GameSession: updates discovery
         │
    ┌────┴─────────┐
    │              │
 Cancel         Expire (background)
    │              │
    ▼              ▼
subscription.   subscription.
updated         updated
(Cancelled)     (Expired)
    │              │
    ▼              ▼
 [Inactive]     [Inactive]
    │
 Renew
    │
    ▼
subscription.updated (Renewed)
    │
    ▼
 [Active again]
```

---

## Stubs & Unimplemented Features

1. **`AuthorizationSuffix` config property**: Defined in schema and generated configuration class but never referenced in service implementation. Should be either wired up or removed.

2. **Missing subscription-index maintenance**: The `SubscriptionExpirationService` reads a `"subscription-index"` key to find subscriptions to check for expiration, but the main service never populates this index. The background worker will find no subscriptions to expire.

---

## Potential Extensions

1. **Subscription tiers**: Support multiple subscription levels (free, premium, enterprise) with different access grants.
2. **Usage-based expiration**: Track API call counts and expire when quota exhausted.
3. **Batch subscription management**: Admin endpoint for bulk-creating subscriptions (e.g., game launch grants).
4. **Subscription history**: Immutable audit trail of all state transitions.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Denormalized service metadata**: `StubName` and `DisplayName` are copied from GameService at creation time. If the game service's display name later changes, existing subscriptions retain the old name. This is intentional — subscriptions reference the state at creation time.

2. **Null means "don't change" in updates**: `ExpirationDate = null` in an update request means "leave unchanged", not "remove expiration". There is no way to convert a time-limited subscription to unlimited via the update endpoint.

3. **Grace period on expiration**: The background worker only expires subscriptions that passed their expiration by `ExpirationGracePeriodSeconds` (default 30s). This prevents race conditions where multiple instances expire the same subscription simultaneously.

4. **Query endpoint has no permissions**: `/subscription/query` has an empty `x-permissions` array, meaning any authenticated caller can query. This is intentional for service-to-service internal calls (Auth and GameSession need to call this without admin elevation).

5. **Renewal logic depends on expiration state**: `RenewSubscriptionAsync` with `extensionDays` extends from current expiration if still valid, but from "now" if already expired. Specifically: if `currentExpiration > now`, extends from `currentExpiration`; otherwise extends from `now`. A subscription that expired 5 days ago with 30-day extension expires 30 days from now (not 25).

### Design Considerations (Requires Planning)

1. **No optimistic concurrency**: Two simultaneous cancellations or renewals could produce inconsistent state. The `IsActive` flag could be toggled unexpectedly if a cancel and renew arrive simultaneously.

2. **Service index maintenance on delete**: No endpoint exists to delete subscriptions. The `service-subscriptions:{serviceId}` and `account-subscriptions:{accountId}` indexes grow indefinitely with cancelled/expired entries.

3. **Event publishing without transactional guarantee**: State store update and event publish are separate operations. If the service crashes between saving and publishing, dependent services (Auth, GameSession) won't learn about the change until the next direct query.
