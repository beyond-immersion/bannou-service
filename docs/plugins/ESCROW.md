# Escrow Plugin Deep Dive

> **Plugin**: lib-escrow
> **Schema**: schemas/escrow-api.yaml
> **Version**: 1.0.0
> **State Stores**: escrow-agreements (MySQL), escrow-handler-registry (MySQL), escrow-tokens (Redis), escrow-idempotency (Redis), escrow-status-index (Redis), escrow-party-pending (Redis), escrow-active-validation (Redis)

---

## Overview

Full-custody orchestration layer for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes (full-consent requiring cryptographic tokens, initiator-trusted, single-party-trusted). Features a 13-state finite state machine, SHA-256-based token generation for deposit and release authorization, idempotent deposit handling, contract-bound conditional releases, per-party pending count tracking, custom asset type handler registration for extensibility, periodic validation with reaffirmation flow, and arbiter-mediated dispute resolution with split allocation support. Handles currency, items, item stacks, contracts, and custom asset types. Does NOT perform actual asset transfers itself - publishes events that downstream services (lib-currency, lib-inventory, lib-contract) consume to execute the physical movements.

**Release/Refund Modes**: Configurable confirmation flows via `ReleaseMode` and `RefundMode` enums. For unbound escrows, `ReleaseMode` controls whether releases complete immediately or require downstream service and/or party confirmations. Contract-bound escrows skip release mode logic entirely - they rely on contract fulfillment verification.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for agreements and handler registry; Redis for tokens, idempotency, status indexes, party pending counts, and validation tracking |
| lib-messaging (`IMessageBus`) | Publishing 13 escrow lifecycle events; error event publishing via `TryPublishErrorAsync` |
| lib-messaging (`IEventConsumer`) | Subscribing to `contract.fulfilled` and `contract.terminated` events for contract-bound escrows |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-contract | Guardian system locks/unlocks contracts as escrow assets; execution system checks asset requirement clauses; `BoundContractId` links contracts to escrows for conditional release |
| lib-currency | Provides `/currency/escrow/deposit`, `/currency/escrow/release`, `/currency/escrow/refund` endpoints consumed by escrow release/refund flows |
| lib-inventory | Provides escrow custody operations for item-type assets |

---

## State Storage

**Stores**: 7 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `escrow-agreements` | MySQL | Main escrow agreement records (queryable) |
| `escrow-handler-registry` | MySQL | Custom asset type handler registrations (queryable) |
| `escrow-tokens` | Redis | Token hash validation (hashed tokens to escrow/party info) |
| `escrow-idempotency` | Redis | Idempotency key deduplication cache (24h TTL) |
| `escrow-status-index` | Redis | Escrow IDs by status for lookup |
| `escrow-party-pending` | Redis | Count pending escrows per party for limits |
| `escrow-active-validation` | Redis | Track active escrows requiring periodic validation |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `agreement:{escrowId}` | `EscrowAgreementModel` | Full escrow agreement with parties, deposits, consents, allocations |
| `token:{sha256Hash}` | `TokenHashModel` | Token hash to escrow/party binding for auth validation |
| `idemp:{idempotencyKey}` | `IdempotencyRecord` | Cached deposit responses for deduplication |
| `handler:{assetType}` | `AssetHandlerModel` | Custom handler registration (plugin, endpoints) |
| `party:{partyType}:{partyId}` | `PartyPendingCount` | Number of active escrows per party |
| `status:{statusEnum}:{escrowId}` | `StatusIndexEntry` | Index of escrow IDs per status |
| `validate:{escrowId}` | `ValidationTrackingEntry` | Validation attempt history and failure count |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `escrow.created` | `EscrowCreatedEvent` | New escrow agreement created |
| `escrow.deposit.received` | `EscrowDepositReceivedEvent` | A deposit is received from a party |
| `escrow.funded` | `EscrowFundedEvent` | All required expected deposits fulfilled |
| `escrow.consent.received` | `EscrowConsentReceivedEvent` | A party records consent (release, refund, dispute, reaffirm) |
| `escrow.finalizing` | `EscrowFinalizingEvent` | Finalization begins (all consents met or condition verified) |
| `escrow.releasing` | `EscrowReleasingEvent` | Release initiated, waiting for confirmations (per ReleaseMode) |
| `escrow.released` | `EscrowReleasedEvent` | Assets released to recipients |
| `escrow.refunding` | `EscrowRefundingEvent` | Refund initiated, waiting for confirmations (per RefundMode) |
| `escrow.refunded` | `EscrowRefundedEvent` | Assets refunded to depositors |
| `escrow.disputed` | `EscrowDisputedEvent` | Party raises a dispute |
| `escrow.resolved` | `EscrowResolvedEvent` | Arbiter resolves a dispute |
| `escrow.expired` | `EscrowExpiredEvent` | Escrow times out (emitted by `EscrowExpirationService`) |
| `escrow.cancelled` | `EscrowCancelledEvent` | Escrow cancelled before full funding |
| `escrow.validation.failed` | `EscrowValidationFailedEvent` | Periodic validation detects asset discrepancy |
| `escrow.validation.reaffirmed` | `EscrowValidationReaffirmedEvent` | All affected parties reaffirm after validation failure |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `contract.fulfilled` | `ContractFulfilledEvent` | Queries for escrows with matching BoundContractId and transitions them to Finalizing (publishes EscrowFinalizingEvent). |
| `contract.terminated` | `ContractTerminatedEvent` | Queries for escrows with matching BoundContractId and transitions them directly to Refunded (publishes EscrowRefundedEvent). |

Both handlers use ETag-based optimistic concurrency with 3-attempt retry loops and respect the escrow state machine (only valid state transitions are applied).

---

## Configuration

| Property | Env Var | Default | Used | Purpose |
|----------|---------|---------|------|---------|
| `DefaultTimeout` | `ESCROW_DEFAULT_TIMEOUT` | `P7D` | ✓ | Default escrow expiration if not specified (ISO 8601 duration) - used in `CreateEscrowAsync` |
| `MaxTimeout` | `ESCROW_MAX_TIMEOUT` | `P30D` | ✗ | Maximum allowed escrow duration - NOT validated against requested timeout |
| `ExpirationGracePeriod` | `ESCROW_EXPIRATION_GRACE_PERIOD` | `PT1H` | ✓ | Grace period after expiration before auto-refund - used in `EscrowExpirationService` |
| `TokenLength` | `ESCROW_TOKEN_LENGTH` | `32` | ✓ | Token length in bytes - used in `GenerateToken` |
| `ExpirationCheckInterval` | `ESCROW_EXPIRATION_CHECK_INTERVAL` | `PT1M` | ✓ | How often to check for expired escrows - used in `EscrowExpirationService` |
| `ExpirationBatchSize` | `ESCROW_EXPIRATION_BATCH_SIZE` | `100` | ✓ | Batch size for expiration processing - used in `EscrowExpirationService` |
| `ValidationCheckInterval` | `ESCROW_VALIDATION_CHECK_INTERVAL` | `PT5M` | ✗ | How often to validate held assets (no background processor) |
| `MaxParties` | `ESCROW_MAX_PARTIES` | `10` | ✓ | Maximum parties per escrow - validated in `CreateEscrowAsync` |
| `MaxAssetsPerDeposit` | `ESCROW_MAX_ASSETS_PER_DEPOSIT` | `50` | ✗ | Maximum asset lines per deposit - NOT validated in `DepositAsync` |
| `MaxPendingPerParty` | `ESCROW_MAX_PENDING_PER_PARTY` | `100` | ✗ | Maximum concurrent pending escrows per party - NOT enforced in `CreateEscrowAsync` |
| `IdempotencyTtlHours` | `ESCROW_IDEMPOTENCY_TTL_HOURS` | `24` | ✓ | TTL in hours for idempotency key storage - used in `DepositAsync` |
| `MaxConcurrencyRetries` | `ESCROW_MAX_CONCURRENCY_RETRIES` | `3` | ✓ | Max retry attempts for optimistic concurrency operations - used throughout |
| `DefaultListLimit` | `ESCROW_DEFAULT_LIST_LIMIT` | `50` | ✓ | Default limit for listing escrows when not specified - used in `ListEscrowsAsync` |
| `DefaultReleaseMode` | `ESCROW_DEFAULT_RELEASE_MODE` | `service_only` | ✗ | Default release confirmation mode - defined but NOT used (escrows use request param) |
| `DefaultRefundMode` | `ESCROW_DEFAULT_REFUND_MODE` | `immediate` | ✗ | Default refund confirmation mode - defined but NOT used (escrows use request param) |
| `ConfirmationTimeoutSeconds` | `ESCROW_CONFIRMATION_TIMEOUT_SECONDS` | `300` | ✗ | Timeout for party confirmations - defined but NOT used to set deadlines |
| `ConfirmationTimeoutBehavior` | `ESCROW_CONFIRMATION_TIMEOUT_BEHAVIOR` | `auto_confirm` | ✓ | What happens when confirmation timeout expires |
| `ConfirmationTimeoutCheckIntervalSeconds` | `ESCROW_CONFIRMATION_TIMEOUT_CHECK_INTERVAL_SECONDS` | `30` | ✓ | How often the background service checks for expired confirmations |
| `ConfirmationTimeoutBatchSize` | `ESCROW_CONFIRMATION_TIMEOUT_BATCH_SIZE` | `100` | ✓ | Maximum escrows to process per timeout check cycle |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<EscrowService>` | Scoped | Structured logging |
| `EscrowServiceConfiguration` | Singleton | All 19 config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access (7 stores) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event subscription registration for contract events |

Service lifetime is **Scoped** (per-request). Two background services implemented:
- **`EscrowExpirationService`** - Checks for escrows past their `ExpiresAt + GracePeriod` in expirable states (PendingDeposits, PartiallyFunded, PendingConsent, PendingCondition) and transitions them to `Expired` status.
- **`EscrowConfirmationTimeoutService`** - Checks for escrows in `Releasing`/`Refunding` states with expired confirmation deadlines, applies configured timeout behavior (auto_confirm, dispute, or refund).

**Internal State Store Accessors** (lazy-initialized):
- `AgreementStore` - IQueryableStateStore for escrow agreements (MySQL)
- `TokenStore` - IStateStore for token hashes (Redis)
- `IdempotencyStore` - IStateStore for idempotency records (Redis)
- `HandlerStore` - IQueryableStateStore for asset handlers (MySQL)
- `PartyPendingStore` - IStateStore for party pending counts (Redis)
- `StatusIndexStore` - IStateStore for status index entries (Redis)
- `ValidationStore` - IStateStore for validation tracking (Redis)

---

## API Endpoints (Implementation Notes)

### Lifecycle Endpoints (4 endpoints)

- **CreateEscrow** (`/escrow/create`): Validates minimum 2 parties and at least 1 expected deposit. For `single_party_trusted` mode, requires `TrustedPartyId`. Generates SHA-256 deposit tokens for each party with expected deposits (full_consent mode only). Generates release tokens for consent-required parties. Saves token hashes to Redis. Initializes status as `Pending_deposits`. Tracks party pending counts. Updates status index. Publishes `escrow.created`. Returns deposit tokens in response.

- **GetEscrow** (`/escrow/get`): Simple key lookup by escrow ID from MySQL agreement store. Maps internal model to API model. Returns 404 if not found.

- **ListEscrows** (`/escrow/list`): Supports filtering by `PartyId`/`PartyType` and/or `Status` list. Uses `QueryAsync` with lambda predicates. Applies offset/limit pagination. TotalCount is post-filter count (not true total for the query).

- **GetMyToken** (`/escrow/get-my-token`): Looks up party within agreement by owner ID and type. Returns deposit or release token based on `TokenType` parameter. Includes used/usedAt status. Returns 404 if party not found or token not assigned.

### Consent Endpoints (2 endpoints)

- **RecordConsent** (`/escrow/consent`): Validates escrow is in `Funded`, `Pending_consent`, or `Pending_condition` state. Checks expiration. For `full_consent` mode with release consent, validates release token (hash lookup, escrow/party/type match, not previously used). Records consent model. Three consent types drive different transitions: `Release` counts toward `RequiredConsentsForRelease` threshold (triggers Finalizing or Pending_condition if bound contract exists); `Refund` from consent-required party triggers Refunding; `Dispute` triggers Disputed. Publishes `escrow.consent.received` plus secondary events (`escrow.finalizing`, `escrow.disputed`) based on state transition.

- **GetConsentStatus** (`/escrow/consent/status`): Returns list of parties requiring consent with their current consent status. Calculates release consent count vs required. Returns `CanRelease` (threshold met) and `CanRefund` (any refund consent exists) flags.

### Deposit Endpoints (3 endpoints)

- **Deposit** (`/escrow/deposit`): Idempotency-protected (returns cached response if key matches). Validates escrow in `Pending_deposits` or `Partially_funded`. Checks expiration. For `full_consent` mode, validates deposit token (hash lookup, marks as used). Creates deposit model with new bundle ID. Marks expected deposit as fulfilled. Transitions to `Funded` if all required deposits fulfilled (skipping optional), otherwise `Partially_funded`. On full funding, returns release tokens for all consent-required parties. Stores idempotency record with 24h expiry. Publishes `escrow.deposit.received` and `escrow.funded` (if fully funded).

- **ValidateDeposit** (`/escrow/deposit/validate`): Pre-flight validation without side effects. Checks escrow state, expiration, party existence, and whether party already deposited. Returns structured errors and warnings lists.

- **GetDepositStatus** (`/escrow/deposit/status`): Returns expected assets for a party, actual deposited assets (flattened from all deposits), fulfillment status, deposit token, and deposit deadline.

### Completion Endpoints (6 endpoints)

- **Release** (`/escrow/release`): Requires escrow in `Finalizing` state. For contract-bound escrows, verifies contract is fulfilled then immediately transitions to `Released`. For unbound escrows, checks `ReleaseMode`: `immediate` skips to `Released`; other modes transition to `Releasing`, set confirmation deadline, initialize confirmation tracking, and publish `escrow.releasing`. The released event is published when all required confirmations are received.

- **ConfirmRelease** (`/escrow/confirm-release`): Party confirmation for releases. Requires escrow in `Releasing` state. Validates release token, records party confirmation. If all required confirmations (per ReleaseMode) are met, transitions to `Released` and publishes `escrow.released`. Uses ETag-based optimistic concurrency.

- **ConfirmRefund** (`/escrow/confirm-refund`): Party confirmation for refunds. Requires escrow in `Refunding` state. Records party confirmation. If all required confirmations (per RefundMode) are met, transitions to `Refunded` and publishes `escrow.refunded`.

- **Refund** (`/escrow/refund`): Accepts escrow in `Refunding`, `Validation_failed`, `Disputed`, `Partially_funded`, or `Pending_deposits` states. For `immediate` RefundMode, transitions directly to `Refunded`. For other modes, transitions to `Refunding` and waits for confirmations. Builds refund results from actual deposits (returns each deposit's assets to depositor).

- **Cancel** (`/escrow/cancel`): Only for `Pending_deposits` or `Partially_funded` (not yet fully funded). Builds refund results for any partial deposits. Sets status to `Cancelled`, resolution to `Cancelled_refunded`. Publishes `escrow.cancelled`.

- **Dispute** (`/escrow/dispute`): Valid from `Funded`, `Pending_consent`, `Pending_condition`, or `Finalizing` states. Validates disputer is a party in the agreement (returns 403 if not). Adds dispute consent record. Sets status to `Disputed`. Publishes `escrow.disputed`.

### Arbiter Endpoint (1 endpoint)

- **Resolve** (`/escrow/resolve`): Only from `Disputed` state. Validates arbiter is a party with `Arbiter` role (returns 403 otherwise). Three resolution modes: `Released` (executes original release allocations), `Refunded` (returns deposits to depositors), `Split` (distributes assets per arbiter-defined `SplitAllocations`). Sets final status and resolution. Decrements pending counts. Publishes `escrow.resolved`.

### Condition Endpoint (1 endpoint)

- **VerifyCondition** (`/escrow/verify-condition`): For contract-bound escrows only (returns 400 if no `BoundContractId`). Valid from `Pending_condition` or `Validation_failed` states. If condition met: clears failures, transitions to `Finalizing`, publishes `escrow.finalizing`. If not met: increments failure count, adds validation failure record, transitions to `Validation_failed`, publishes `escrow.validation.failed`.

### Validation Endpoints (2 endpoints)

- **ValidateEscrow** (`/escrow/validate`): Rejects terminal-state escrows. Placeholder implementation - validation logic (checking with currency/inventory services) is not yet implemented. Tracks validation timestamp. On failure (if failures existed): records failure models, may transition to `Validation_failed`, publishes `escrow.validation.failed`. Currently always passes since no actual asset checks are performed.

- **Reaffirm** (`/escrow/reaffirm`): Only from `Validation_failed` state. Records `Reaffirm` consent for the party. Checks if all affected parties (those with validation failures) have reaffirmed. If all reaffirmed: clears failures, resets failure count, transitions back to `Pending_condition`, publishes `escrow.validation.reaffirmed`. Otherwise remains in `Validation_failed`.

### Handler Endpoints (3 endpoints)

- **RegisterHandler** (`/escrow/handler/register`): Registers custom asset type handler with deposit/release/refund/validate endpoints. One handler per asset type (returns 400 if already registered). Stores with `BuiltIn=false` flag.

- **ListHandlers** (`/escrow/handler/list`): Queries all registered handlers. Returns asset type, plugin ID, built-in flag, and all four endpoint URLs.

- **DeregisterHandler** (`/escrow/handler/deregister`): Removes custom handler registration. Cannot deregister built-in handlers (returns 400). Returns 404 if not found.

---

## Visual Aid

```
Escrow State Machine (13 states)
==================================

  ┌──────────────────┐
  │ Pending_deposits │──────────────┐
  └────────┬─────────┘              │
           │ deposit(s)             │ cancel/expire
           ▼                        ▼
  ┌──────────────────┐      ┌──────────────┐
  │ Partially_funded │─────►│  Cancelled   │ (terminal)
  └────────┬─────────┘      └──────────────┘
           │ all required deposits
           ▼                        ┌──────────────┐
  ┌──────────────────┐      ┌──────►│   Expired    │ (terminal)
  │     Funded       │──┐   │      └──────────────┘
  └────────┬─────────┘  │   │
           │             │   │ (from any non-terminal pre-release state)
           │ consent     │   │
           ▼             │   │
  ┌──────────────────┐   │   │
  │ Pending_consent  │───┤   │
  └────────┬─────────┘   │   │
           │ threshold    │   │
           │ met          │   │
           ▼              │   │
  ┌──────────────────┐    │ dispute
  │Pending_condition │────┤   │
  └────┬───────┬─────┘   │   │
       │       │          │   │
       │       │ fail     │   │
       │       ▼          │   │
       │  ┌──────────────┐│   │
       │  │Valid. failed  ││   │     ┌──────────────┐
       │  └───┬──────────┘│   │  ┌─►│   Refunded   │ (terminal)
       │      │ reaffirm  │   │  │  └──────────────┘
       │      └─(back)────┘   │  │
       │                      ▼  │
       │ condition    ┌──────────────┐
       │ met          │   Disputed   │──────────┐
       ▼              └──────────────┘          │ resolve
  ┌──────────────────┐          │               ▼
  │    Finalizing    │──────────┤      ┌──────────────────┐
  └────────┬─────────┘          │      │ Released/Refunded│
           │                    │      │  (per arbiter)   │
           ▼                    │      └──────────────────┘
  ┌──────────────────┐          │
  │    Releasing     │          │
  └────────┬─────────┘          │
           │                    ▼
           ▼              ┌──────────────┐
  ┌──────────────────┐    │   Refunding  │
  │    Released      │    └──────┬───────┘
  │   (terminal)     │           │
  └──────────────────┘           ▼
                          ┌──────────────┐
                          │   Refunded   │
                          │  (terminal)  │
                          └──────────────┘

  Terminal States: Released, Refunded, Expired, Cancelled


Token System (full_consent mode)
===================================

  CreateEscrow
       │
       ├── For each party with expected deposits:
       │    ├── Generate 32 random bytes
       │    ├── Combine with "{escrowId}:{partyId}:Deposit" context
       │    ├── SHA-256 hash → deposit token (Base64)
       │    ├── SHA-256(token) → token hash (stored in Redis)
       │    └── Return plain token to caller
       │
       ├── For each consent-required party:
       │    ├── Same process with TokenType.Release
       │    └── Release tokens stored, returned on full funding
       │
       └── Token Validation (on deposit/consent):
            ├── SHA-256(submitted token) → hash
            ├── Lookup hash in escrow-tokens store
            ├── Verify: escrowId, partyId, tokenType match
            ├── Verify: not already used
            ├── Mark as used + timestamp
            └── Proceed with operation


Consent Flow
==============

  After Funded state:
       │
       ├── Party A consents (Release) ────► ConsentsReceived = 1
       │    (validates release token in full_consent mode)
       │
       ├── Party B consents (Release) ────► ConsentsReceived = 2
       │
       ├── ConsentsReceived >= RequiredConsentsForRelease?
       │    ├── No BoundContractId → Finalizing
       │    └── Has BoundContractId → Pending_condition
       │
       ├── Party X consents (Refund) ─────► Refunding
       │    (any consent-required party can trigger)
       │
       └── Party Y consents (Dispute) ────► Disputed
            (any party can dispute)


Deposit/Release/Refund Lifecycle
==================================

  ┌─────────┐        ┌──────────────────┐        ┌─────────────┐
  │  Party  │ deposit│    Escrow Svc    │ event  │  Currency/  │
  │         │───────►│                  │───────►│  Inventory  │
  └─────────┘        │                  │        │  Service    │
                     │                  │        └─────────────┘
                     │  (holds assets   │
                     │   as records,    │
                     │   no physical    │
                     │   movement)      │
                     │                  │
  ┌─────────┐ release│                  │ event  ┌─────────────┐
  │Initiator│───────►│  Status→Released │───────►│  Currency/  │
  │ /System │        │                  │        │  Inventory  │
  └─────────┘        └──────────────────┘        │  (executes  │
                                                  │   transfer) │
                                                  └─────────────┘

  Note: Escrow service is a COORDINATION layer.
  It tracks what should move where, but downstream
  services execute the actual asset movements.


Dispute Resolution
=====================

  ┌──────────┐  dispute  ┌──────────────┐
  │  Party   │──────────►│   Disputed   │
  └──────────┘           └──────┬───────┘
                                │
                         ┌──────▼───────┐
                         │   Arbiter    │
                         │   resolves   │
                         └──────┬───────┘
                                │
              ┌─────────────────┼────────────────┐
              │                 │                 │
              ▼                 ▼                 ▼
     ┌────────────┐   ┌────────────┐   ┌────────────────┐
     │  Released  │   │  Refunded  │   │     Split      │
     │ (original  │   │ (return to │   │ (custom alloc  │
     │  allocat.) │   │ depositors)│   │  per arbiter)  │
     └────────────┘   └────────────┘   └────────────────┘
```

---

## Release Modes

Escrows support configurable release confirmation flows via `ReleaseMode`. This controls what must happen between `Finalizing` and `Released` states.

### Available Modes

| Mode | Behavior | Use Case |
|------|----------|----------|
| `immediate` | Skip `Releasing` state entirely; go directly to `Released` | Trusted/low-value scenarios (NPC vendors, system rewards) |
| `service_only` | Wait for downstream services (currency, inventory) to confirm transfers complete | **Default** - Most common production use |
| `party_required` | Wait for all parties to call `/escrow/confirm-release` | High-value trades requiring explicit acknowledgment |
| `service_and_party` | Wait for both service completion AND party confirmation | Maximum assurance |

> ⚠️ **WARNING: `immediate` Mode Risk**
>
> The `immediate` mode marks assets as released BEFORE downstream services confirm transfers. Use only for trusted/low-value scenarios (NPC vendors, automated rewards, system-initiated distributions). If downstream service calls fail, assets may be marked as released but not actually transferred, requiring manual intervention.

### Refund Modes

Refunds use `RefundMode` with similar options (excluding `service_and_party`). Default is `immediate` since refunds are less contentious - parties are getting their own assets back.

### Confirmation Timeout

When waiting for confirmations, a deadline is set based on `ConfirmationTimeoutSeconds`. When expired, the `ConfirmationTimeoutBehavior` determines what happens:

| Behavior | Action |
|----------|--------|
| `auto_confirm` | If services confirmed, auto-complete; otherwise escalate to `Disputed` |
| `dispute` | Transition to `Disputed`, require arbiter intervention |
| `refund` | Treat as failed, transition to `Refunding` |

---

## Contract-Bound vs Unbound Escrows

**Critical architectural distinction**: `ReleaseMode` only applies to **unbound escrows**. Contract-bound escrows follow a different flow.

| Scenario | Behavior | ReleaseMode Used? |
|----------|----------|-------------------|
| **Unbound escrow** | Escrow orchestrates release, publishes `escrow.releasing`, waits for confirmations per ReleaseMode | **YES** |
| **Contract-bound escrow** | Contract handles distribution via `ExecuteContract`, escrow verifies `contract.fulfilled` then immediate Released | **NO** |

Contract-bound escrows verify the contract status on release. Once the contract is fulfilled, the escrow immediately transitions to `Released` because the contract system has already coordinated the asset distribution.

---

## Stubs & Unimplemented Features

1. **Event consumer registration**: `RegisterEventConsumers()` registers handlers for `contract.fulfilled` (transitions bound escrows to Finalizing) and `contract.terminated` (transitions bound escrows to Refunded). Uses QueryAsync to find escrows by BoundContractId and ETag-based concurrency for state transitions. The `/escrow/verify-condition` endpoint remains available for manual verification.

2. **ValidateEscrow asset checking**: The `ValidateEscrowAsync` method contains a placeholder comment "Validate deposits (placeholder - real impl would check with currency/inventory services)". No actual cross-service validation is performed. The validation always passes (empty failure list).
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/213 -->

3. **Expiration background processing**: Implemented in `EscrowExpirationService` background worker that periodically scans for escrows past their `ExpiresAt + GracePeriod`, transitions them to `Expired` status, publishes `EscrowExpiredEvent`, and if deposits exist also publishes `EscrowRefundedEvent` for downstream services. Configuration properties `ExpirationCheckInterval`, `ExpirationBatchSize`, and `ExpirationGracePeriod` are wired up.

4. **Periodic validation loop**: Configuration defines `ValidationCheckInterval` (PT5M) but no background process triggers periodic validation. The `ValidationStore` tracks `NextValidationDue` but nothing reads it.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/250 -->

5. **Configuration properties not wired up**: Several configuration properties remain unused:
   - `MaxTimeout` - defined but NOT validated against requested timeout
   - `MaxAssetsPerDeposit` - defined but NOT validated in `DepositAsync`
   - `MaxPendingPerParty` - defined but NOT enforced in `CreateEscrowAsync`
   - `ValidationCheckInterval` - defined but no background validation processor exists
   - `DefaultReleaseMode` - defined but escrows use request parameter instead
   - `DefaultRefundMode` - defined but escrows use request parameter instead
   - `ConfirmationTimeoutSeconds` - defined but NOT used to set confirmation deadlines

6. **Custom handler invocation**: Handlers are registered with deposit/release/refund/validate endpoints, but the escrow service never actually invokes these endpoints during deposit or release flows. The handler registry is purely declarative.

7. **Asset transfer execution**: Release and refund operations set status and publish events but do not call currency/inventory services to execute actual transfers. The service is purely a coordination/tracking layer that assumes downstream consumers handle the physical movements.

8. **Releasing state**: Now used for event-driven confirmation flow. When `ReleaseMode` is not `immediate`, escrows transition to `Releasing` and wait for service/party confirmations before completing to `Released`. The `EscrowConfirmationTimeoutService` background service handles expired confirmation deadlines.

---

## Potential Extensions


1. **Cross-service asset validation**: Implement actual calls to currency/inventory services in `ValidateEscrowAsync` to verify deposited assets are still held and unchanged.

2. **Handler invocation pipeline**: During deposit/release/refund, look up registered handlers for each asset type and invoke their endpoints via mesh, enabling plug-and-play asset type support.

3. **Rate limiting via MaxPendingPerParty**: Enforce the configured limit during `CreateEscrow` by checking the party pending store before allowing new escrows.

4. **MaxTimeout validation**: Validate requested timeout against `MaxTimeout` configuration in `CreateEscrowAsync` to prevent excessively long escrow periods.

5. **MaxAssetsPerDeposit validation**: Validate asset count against `MaxAssetsPerDeposit` in `DepositAsync` to prevent oversized deposits.

6. **Distributed lock for concurrent modifications**: Add lock acquisition around agreement modifications to prevent race conditions when multiple parties deposit/consent simultaneously.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Status index key pattern uses individual keys**: Status index stores entries at `status:{status}:{escrowId}` as individual key-value pairs using `IStateStore.SaveAsync`/`DeleteAsync`. This is not a Redis Set - it's keyed storage. Scanning all escrows by status requires `QueryAsync` against the agreement store, not the status index.

2. **Party pending count race on failure**: Escrow creation increments party pending counts AFTER saving the agreement. If the save succeeds but subsequent operations (token save, status index save, event publish) fail, the pending counts remain incremented with no rollback. The catch block does NOT decrement counts.

### Intentional Quirks (Documented Behavior)

1. **Single refund consent triggers refund**: Any consent-required party submitting a `Refund` consent immediately transitions to `Refunding`. Unilateral refund right is a safety mechanism that can surprise developers expecting multi-party consensus.

2. **Release tokens returned on full funding**: When the last required deposit arrives, the `DepositResponse` includes all release tokens for consent-required parties. This is the only time release tokens are proactively delivered (otherwise use `GetMyToken`).

3. **Token hash double-hashing**: Tokens are first generated as SHA-256 hash of random bytes + context, then stored by hashing the token again. Validation requires SHA-256(submitted_token) lookup, providing one-way token storage.

4. **Escrow service is coordination-only**: The service publishes events but never invokes downstream services directly. Asset movements are event-driven: lib-currency and lib-inventory subscribe to `escrow.released`/`escrow.refunded` and execute transfers.

5. **Contract event handlers are best-effort**: `HandleContractFulfilledAsync` and `HandleContractTerminatedAsync` use try-catch with error event emission but don't retry or queue failed operations.

### Design Considerations

1. **POCO string defaults (`= string.Empty`)**: Internal POCO properties use `= string.Empty` as default to satisfy NRT. Consider making these nullable (`string?`) or adding validation at assignment time for fields that should always have values (e.g., `PartyType`, `CreatedByType`).


2. **Large agreement documents**: All parties, deposits, consents, allocations, and validation failures are stored in a single agreement document. Multi-party escrows with many deposits and consent records can grow large, impacting read/write performance.

3. **Token storage exposes timing**: Token hashes are stored with `ExpiresAt` from the escrow expiration. However, token expiration is not checked during validation - only the `Used` flag is verified. Expired tokens remain valid if the escrow has not expired.

4. **QueryAsync for listing**: `ListEscrowsAsync` uses `QueryAsync` with lambda predicates, which loads all agreements into memory for filtering. This does not scale for large datasets.

5. **Idempotency result caching stores full response**: The `IdempotencyRecord.Result` field stores the complete `DepositResponse` object including the full escrow state. This creates large Redis entries and may have serialization issues if the response model changes.

6. **Event ordering not guaranteed**: Multiple events can be published in a single operation (e.g., deposit + funded), but there is no transactional guarantee on event ordering or all-or-nothing delivery.

7. **Contract termination refund doesn't verify contract binding**: `RefundForContractTerminationAsync` validates the escrow is bound to the contract via query, but doesn't re-verify the `BoundContractId` matches after loading. The query result is trusted.

8. **Party pending count failures silently logged**: `IncrementPartyPendingCountAsync` and `DecrementPartyPendingCountAsync` log warnings but don't fail the operation when count updates fail after max retries. This can lead to stale pending counts.

---

## Work Tracking

### Recently Completed

1. **Event-driven confirmation flow** - [Issue #214](https://github.com/beyond-immersion/bannou-service/issues/214) (2026-02-01)
   - Implemented `ReleaseMode` and `RefundMode` enums for configurable confirmation flows
   - `Releasing` state now used for confirmation waiting when mode is not `immediate`
   - Added `/escrow/confirm-release` and `/escrow/confirm-refund` endpoints
   - Added `EscrowConfirmationTimeoutService` background worker
   - Added `EscrowReleasingEvent` and `EscrowRefundingEvent`

2. **Escrow Expiration Background Service** (2026-02-01)
   - Implemented `EscrowExpirationService` that scans for expired escrows and transitions them
   - Wired up `ExpirationCheckInterval`, `ExpirationBatchSize`, and `ExpirationGracePeriod` config
   - Publishes `EscrowExpiredEvent` and `EscrowRefundedEvent` (for auto-refund)

### Pending Design Review

1. **ValidateEscrow asset checking** - [Issue #213](https://github.com/beyond-immersion/bannou-service/issues/213) (2026-01-31)
   - `ValidateEscrowAsync` contains placeholder logic - validation always passes
   - Needs to call ICurrencyClient/IItemClient to verify deposited assets still held
   - Design questions: contract validation, custom handler invocation, graceful degradation policy
