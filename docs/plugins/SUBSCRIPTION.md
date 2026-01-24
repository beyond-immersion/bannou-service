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

2. **Service/account index maintenance on delete**: No endpoint exists to delete subscriptions. The `service-subscriptions:{serviceId}` and `account-subscriptions:{accountId}` indexes grow indefinitely with cancelled/expired entries. (The global `subscription-index` is cleaned by the expiration worker, but per-service and per-account indexes are not.)

3. **Event publishing without transactional guarantee**: State store update and event publish are separate operations. If the service crashes between saving and publishing, dependent services (Auth, GameSession) won't learn about the change until the next direct query.

4. **Internal model uses string types for GUIDs**: `SubscriptionDataModel` stores `SubscriptionId`, `AccountId`, and `ServiceId` as `string` rather than `Guid`. Requires `Guid.Parse()` at every boundary (event publishing, response mapping). Violates IMPLEMENTATION TENETS (type safety for internal models).

---

## Tenet Violations (Audit)

*Audit performed: 2026-01-24*

### Category: QUALITY TENETS

1. **Logging Standards (T10)** - SubscriptionService.cs:62, 130, 227, 256, 366, 417, 462, 529, 675 - Operation entry logged at Information level instead of Debug
   - What's wrong: Multiple methods log operation entry at `LogInformation` level (e.g., "Getting subscriptions for account...", "Querying current subscriptions...", "Getting subscription...", "Creating subscription...", etc.). Per T10, operation entry should be Debug level.
   - Fix: Change `_logger.LogInformation(...)` to `_logger.LogDebug(...)` for operation entry logging. Reserve Information for significant state changes (e.g., "Created subscription" is correct at Information).

2. **Logging Standards (T10)** - SubscriptionExpirationService.cs:50 - Startup log at Information may be acceptable
   - What's wrong: "Subscription expiration service starting, check interval: {Interval}" is logged at Information. This is a startup message which is typically acceptable at Information level for hosted services.
   - Fix: No fix needed - startup/shutdown logging at Information is acceptable for background services.

### Category: IMPLEMENTATION TENETS

3. **Internal Model Type Safety (T25)** - SubscriptionService.cs:710-724 - `SubscriptionDataModel` uses `string` for GUID fields
   - What's wrong: `SubscriptionDataModel` stores `SubscriptionId`, `AccountId`, and `ServiceId` as `string` instead of `Guid`. This violates T25 which mandates using proper C# types for internal models.
   - Fix: Change the internal model to use `Guid` types:
     ```csharp
     internal class SubscriptionDataModel
     {
         public Guid SubscriptionId { get; set; }
         public Guid AccountId { get; set; }
         public Guid ServiceId { get; set; }
         // ... other fields remain the same
     }
     ```
     Then remove all `Guid.Parse()` calls and `.ToString()` conversions for these fields throughout the service.

4. **Internal Model Type Safety (T25)** - SubscriptionService.cs:291, 318-320, 336-339, 609-611, 644-646 - `Guid.Parse()` in business logic and `.ToString()` when populating internal model
   - What's wrong: Multiple locations use `Guid.Parse()` to convert string GUIDs from the internal model, and `.ToString()` to populate the model. Per T25, `Enum.Parse` (and by extension `Guid.Parse`) belongs only at system boundaries, not in business logic.
   - Fix: Use `Guid` type in internal model (see violation #3), then assign directly without parsing/conversion.

5. **Configuration-First (T21)** - SubscriptionService.cs:54 - `AuthorizationSuffix` config property defined but never meaningfully used
   - What's wrong: The `AuthorizationSuffix` property is accessed at line 54 but the resulting value is never actually used in any business logic. This is dead configuration.
   - Fix: Either wire up `AuthorizationSuffix` to meaningful functionality or remove it from the configuration schema (`schemas/subscription-configuration.yaml`).

6. **Multi-Instance Safety (T9)** - SubscriptionService.cs:566-599 - Read-modify-write on indexes without distributed locks or optimistic concurrency
   - What's wrong: `AddToAccountIndexAsync`, `AddToServiceIndexAsync`, and `AddToSubscriptionIndexAsync` all perform read-modify-write operations (get list, check if contains, add, save) without distributed locks or optimistic concurrency (ETags). Two instances could race and lose an index entry.
   - Fix: Use optimistic concurrency with ETags in a retry loop:
     ```csharp
     private async Task AddToAccountIndexAsync(string accountId, string subscriptionId, CancellationToken ct)
     {
         for (var attempt = 0; attempt < 3; attempt++)
         {
             var (subscriptionIds, etag) = await listStore.GetWithETagAsync($"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{accountId}", ct);
             subscriptionIds ??= new List<string>();
             if (subscriptionIds.Contains(subscriptionId)) return;
             subscriptionIds.Add(subscriptionId);
             var result = await listStore.TrySaveAsync($"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{accountId}", subscriptionIds, etag ?? string.Empty, ct);
             if (result != null) return;
         }
         _logger.LogWarning("Failed to add {SubscriptionId} to account index after retries", subscriptionId);
     }
     ```

7. **Multi-Instance Safety (T9)** - SubscriptionService.cs:363-408, 414-453, 459-521 - Update/Cancel/Renew operations without distributed locks on read-modify-write
   - What's wrong: `UpdateSubscriptionAsync`, `CancelSubscriptionAsync`, `RenewSubscriptionAsync`, and `ExpireSubscriptionAsync` all perform read-modify-write operations on subscription records without distributed locks or optimistic concurrency. Concurrent operations could produce inconsistent state (as noted in Known Quirks).
   - Fix: Either use `IDistributedLockProvider` to acquire a lock on the subscription ID before modifying, or use ETag-based optimistic concurrency with retry logic.

8. **Error Handling (T7)** - SubscriptionService.cs:107-111, 213-218, 244-247, 353-357, 404-408, 450-453, 516-520 - Generic `catch (Exception ex)` without distinguishing ApiException
   - What's wrong: Multiple methods catch only `Exception` and return InternalServerError. Per T7, services should catch `ApiException` specifically (for expected API errors from downstream services) separately from generic `Exception`.
   - Fix: Add specific `ApiException` catch block before the generic catch:
     ```csharp
     catch (ApiException ex)
     {
         _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
         return ((StatusCodes)ex.StatusCode, null);
     }
     catch (Exception ex)
     {
         _logger.LogError(ex, "Failed operation {Operation}", operationName);
         await PublishErrorEventAsync(...);
         return (StatusCodes.InternalServerError, null);
     }
     ```
   - Note: `CreateSubscriptionAsync` (line 269) correctly catches `ApiException` for the `GetServiceAsync` call - this pattern should be applied consistently to all methods.

9. **Async Method Pattern (T23)** - SubscriptionExpirationService.cs:87-90 - Empty catch block swallows exceptions silently
   - What's wrong: The inner `catch` block at lines 87-90 is empty, silently swallowing any exception from error event publishing. While the comment explains the intent ("Don't let error publishing failures affect the loop"), this violates the principle of explicit error handling.
   - Fix: At minimum, log that error publishing failed:
     ```csharp
     catch (Exception pubEx)
     {
         _logger.LogDebug(pubEx, "Failed to publish error event - continuing expiration loop");
     }
     ```

### Category: FOUNDATION TENETS

10. **Service Implementation Pattern (T6)** - SubscriptionService.cs:35-51 - No null checks on constructor parameters
    - What's wrong: The constructor accepts multiple parameters but does not validate them with `ArgumentNullException.ThrowIfNull()`. While NRT provides compile-time safety, the tenet examples show explicit null checks for clarity and runtime safety at DI boundaries.
    - Fix: Add explicit null checks to the constructor:
      ```csharp
      public SubscriptionService(
          IStateStoreFactory stateStoreFactory,
          IMessageBus messageBus,
          ILogger<SubscriptionService> logger,
          SubscriptionServiceConfiguration configuration,
          IGameServiceClient serviceClient,
          IEventConsumer eventConsumer)
      {
          ArgumentNullException.ThrowIfNull(stateStoreFactory);
          ArgumentNullException.ThrowIfNull(messageBus);
          ArgumentNullException.ThrowIfNull(logger);
          ArgumentNullException.ThrowIfNull(configuration);
          ArgumentNullException.ThrowIfNull(serviceClient);
          ArgumentNullException.ThrowIfNull(eventConsumer);
          // ... rest of constructor
      }
      ```
    - Note: The tenet document states "NRT-protected parameters: no null checks needed" in the example, so this may be considered acceptable. However, consistency with other services that do include checks should be considered.

11. **Service Implementation Pattern (T6)** - SubscriptionExpirationService.cs:38-46 - No null checks on constructor parameters
    - What's wrong: Same issue as violation #10 - constructor parameters are not validated.
    - Fix: Add explicit null checks if consistency with other services requires it.

### Summary

| Category | Count | Severity |
|----------|-------|----------|
| QUALITY TENETS | 1 (+ 1 acceptable) | Low |
| IMPLEMENTATION TENETS | 7 | Medium-High |
| FOUNDATION TENETS | 2 | Low |

**Priority Fixes:**
1. **High**: T25 violations (#3, #4) - Change `SubscriptionDataModel` to use `Guid` types
2. **High**: T9 violations (#6, #7) - Add distributed locks or optimistic concurrency for read-modify-write operations
3. **Medium**: T7 violations (#8) - Add `ApiException` catch blocks
4. **Medium**: T21 violation (#5) - Remove dead `AuthorizationSuffix` configuration
5. **Low**: T10 violations (#1) - Change operation entry logs to Debug level
