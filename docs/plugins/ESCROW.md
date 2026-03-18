# Escrow Plugin Deep Dive

> **Plugin**: lib-escrow
> **Schema**: schemas/escrow-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: escrow-agreements (MySQL), escrow-handler-registry (MySQL), escrow-tokens (Redis), escrow-idempotency (Redis), escrow-status-index (Redis), escrow-party-pending (Redis), escrow-active-validation (Redis)
> **Short**: Full-custody multi-party asset exchange with 13-state FSM (currency/items/contracts)
> **Implementation Map**: [docs/maps/ESCROW.md](../maps/ESCROW.md)

---

## Overview

Full-custody orchestration layer (L4 GameFeatures) for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes and a 13-state finite state machine. Handles currency, items, contracts, and extensible custom asset types — designed to call lib-currency and lib-inventory directly for asset movements (not yet implemented; see stub #5). Integrates with lib-contract for conditional releases where contract fulfillment triggers escrow completion. See Release Modes section below for configurable confirmation flows.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for agreements and handler registry; Redis for tokens, idempotency, status indexes, party pending counts, and validation tracking |
| lib-messaging (`IMessageBus`) | Publishing 15 escrow lifecycle events; error event publishing via `TryPublishErrorAsync` |
| lib-messaging (`IEventConsumer`) | Subscribing to `account.deleted` (cleanup obligation), `contract.fulfilled` and `contract.terminated` events for contract-bound escrows |
| lib-contract (`IContractClient`, L1) | Constructor-injected. Consumes `contract.fulfilled` and `contract.terminated` events; contract-bound escrows verify contract status for conditional release; asset validation checks contract instance existence and terminal status |
| lib-currency (`ICurrencyClient`, L2) | Constructor-injected. Asset validation checks wallet/balance existence. **Planned** for asset movements during deposit/release/refund (see stub #5) |
| lib-item (`IItemClient`, L2) | Constructor-injected. Asset validation checks item instance existence. |
| lib-inventory (`IInventoryClient`, L2) | Constructor-injected. Deposit transfers items to escrow container via `TransferItemAsync`. **Planned** for release/refund transfers (see #153). |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-contract | Guardian system locks/unlocks contracts as escrow assets; execution system checks asset requirement clauses; `BoundContractId` links contracts to escrows for conditional release |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `partyType` | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies the type of entity acting as a party (account, character, guild, etc.); all valid values are first-class Bannou entities |
| `ownerType` | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type for token ownership lookup; same entity reference pattern as `partyType` |
| `disputedByType` (events) | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type of the disputing party |
| `arbiterType` (events) | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type of the resolving arbiter |
| `cancelledByType` (events) | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type that cancelled the escrow |
| `reaffirmedByType` (events) | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type of the reaffirming party |
| `recipientPartyType` (events) | A (Entity Reference) | `EntityType` enum (from `common-api.yaml`) | Identifies entity type of the release recipient |
| `escrowType` | C (System State/Mode) | `EscrowType` enum (`TwoParty`, `MultiParty`, `Conditional`, `Auction`) | Structural agreement type determining deposit/consent rules; service-specific classification |
| `trustMode` | C (System State/Mode) | `EscrowTrustMode` enum (`FullConsent`, `InitiatorTrusted`, `SinglePartyTrusted`) | Trust model governing consent requirements; service-specific classification |
| `status` | C (System State/Mode) | `EscrowStatus` enum (13 values) | Position in the 13-state escrow finite state machine |
| `role` | C (System State/Mode) | `EscrowPartyRole` enum (`Depositor`, `Recipient`, `DepositorRecipient`, `Arbiter`, `Observer`) | Party's functional role within the escrow; service-specific classification |
| `consentType` | C (System State/Mode) | `EscrowConsentType` enum (`Release`, `Refund`, `Dispute`, `Reaffirm`) | Type of consent action being taken; service-specific state transition trigger |
| `resolution` | C (System State/Mode) | `EscrowResolution` enum (6 values) | How the escrow was resolved; terminal state classification |
| `assetType` | C (System State/Mode) | `AssetType` enum (`Currency`, `Item`, `ItemStack`, `Contract`, `Custom`) | Classifies the kind of asset held in escrow; determines which downstream service handles the asset (Currency, Inventory, Contract, or custom handler) |
| `releaseMode` | C (System State/Mode) | `ReleaseMode` enum (`Immediate`, `ServiceOnly`, `PartyRequired`, `ServiceAndParty`) | Confirmation flow configuration for releases |
| `refundMode` | C (System State/Mode) | `RefundMode` enum (`Immediate`, `ServiceOnly`, `PartyRequired`) | Confirmation flow configuration for refunds |
| `failureType` | C (System State/Mode) | `ValidationFailureType` enum (`AssetMissing`, `AssetMutated`, `AssetExpired`, `BalanceMismatch`) | Classifies what went wrong during periodic validation |
| `tokenType` | C (System State/Mode) | `TokenType` enum (`Deposit`, `Release`) | Distinguishes deposit tokens from release tokens; system authentication classification |
| `confirmationTimeoutBehavior` | C (System State/Mode) | `ConfirmationTimeoutBehavior` enum (`AutoConfirm`, `Dispute`, `Refund`) | Configured behavior when confirmation deadline expires |
| `referenceType` | B (Game Content Type) | Opaque string | What this escrow is for (e.g., `"trade"`, `"auction"`, `"contract"`); callers provide context-specific labels without schema changes |
| `customAssetType` | B (Game Content Type) | Opaque string | Registered handler type for `assetType=custom`; plugin-extensible asset types via handler registry |

**Notes**:
- Escrow has the most `EntityType` usages of any service due to its multi-party, polymorphic nature -- every party, depositor, recipient, arbiter, and disputer is identified by `entityId` + `EntityType`.
- `assetType` (Escrow's own enum) is Category C, not Category B, because the values map to specific downstream service integrations (Currency, Inventory, Contract) rather than being game-configurable. The `custom` value bridges to Category B via the handler registry pattern.
- `referenceType` is a plain string with no enum constraint, used as a caller-provided label for contextual categorization.

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
| `account.deleted` | `AccountDeletedEvent` | Cleans up all escrow data for the deleted account — agreements, tokens, status indexes, party pending counts, and validation tracking. Per FOUNDATION TENETS (Account Deletion Cleanup Obligation). |
| `contract.fulfilled` | `ContractFulfilledEvent` | Queries for escrows with matching BoundContractId and transitions them to Finalizing (publishes EscrowFinalizingEvent). |
| `contract.terminated` | `ContractTerminatedEvent` | Queries for escrows with matching BoundContractId and transitions them directly to Refunded (publishes EscrowRefundedEvent). |

Contract handlers use ETag-based optimistic concurrency with 3-attempt retry loops and respect the escrow state machine (only valid state transitions are applied). Account deletion handler uses per-agreement error isolation with telemetry spans.

---

## Configuration

| Property | Env Var | Default | Used | Purpose |
|----------|---------|---------|------|---------|
| `DefaultTimeout` | `ESCROW_DEFAULT_TIMEOUT` | `P7D` | ✓ | Default escrow expiration if not specified (ISO 8601 duration) - used in `CreateEscrowAsync` |
| `MaxTimeout` | `ESCROW_MAX_TIMEOUT` | `P30D` | ✓ | Maximum allowed escrow duration - validated in `CreateEscrowAsync` |
| `ExpirationGracePeriod` | `ESCROW_EXPIRATION_GRACE_PERIOD` | `PT1H` | ✓ | Grace period after expiration before auto-refund - used in `EscrowExpirationService` |
| `TokenLength` | `ESCROW_TOKEN_LENGTH` | `32` | ✓ | Token length in bytes - used in `GenerateToken` |
| `ExpirationCheckInterval` | `ESCROW_EXPIRATION_CHECK_INTERVAL` | `PT1M` | ✓ | How often to check for expired escrows - used in `EscrowExpirationService` |
| `ExpirationBatchSize` | `ESCROW_EXPIRATION_BATCH_SIZE` | `100` | ✓ | Batch size for expiration processing - used in `EscrowExpirationService` |
| `ValidationCheckInterval` | `ESCROW_VALIDATION_CHECK_INTERVAL` | `PT5M` | ✓ | How often to validate held assets - used in `EscrowValidationService` |
| `ValidationStartupDelaySeconds` | `ESCROW_VALIDATION_STARTUP_DELAY_SECONDS` | `25` | ✓ | Startup delay before first validation check - used in `EscrowValidationService` |
| `ValidationBatchSize` | `ESCROW_VALIDATION_BATCH_SIZE` | `50` | ✓ | Maximum escrows to process per validation cycle - used in `EscrowValidationService` |
| `MaxParties` | `ESCROW_MAX_PARTIES` | `10` | ✓ | Maximum parties per escrow - validated in `CreateEscrowAsync` |
| `MaxAssetsPerDeposit` | `ESCROW_MAX_ASSETS_PER_DEPOSIT` | `50` | ✓ | Maximum asset lines per deposit - validated in `DepositAsync` |
| `MaxPendingPerParty` | `ESCROW_MAX_PENDING_PER_PARTY` | `100` | ✓ | Maximum concurrent pending escrows per party - validated in `CreateEscrowAsync` |
| `IdempotencyTtlHours` | `ESCROW_IDEMPOTENCY_TTL_HOURS` | `24` | ✓ | TTL in hours for idempotency key storage - used in `DepositAsync` |
| `MaxConcurrencyRetries` | `ESCROW_MAX_CONCURRENCY_RETRIES` | `3` | ✓ | Max retry attempts for optimistic concurrency operations - used throughout |
| `DefaultListLimit` | `ESCROW_DEFAULT_LIST_LIMIT` | `50` | ✓ | Default limit for listing escrows when not specified - used in `ListEscrowsAsync` |
| `DefaultReleaseMode` | `ESCROW_DEFAULT_RELEASE_MODE` | `ServiceOnly` | ✓ | Default release confirmation mode - used when request param is null |
| `DefaultRefundMode` | `ESCROW_DEFAULT_REFUND_MODE` | `Immediate` | ✓ | Default refund confirmation mode - used when request param is null |
| `ConfirmationTimeoutSeconds` | `ESCROW_CONFIRMATION_TIMEOUT_SECONDS` | `300` | ✓ | Timeout for party confirmations - used in `ReleaseAsync` and `RecordConsentAsync` to set `ConfirmationDeadline` |
| `ConfirmationTimeoutBehavior` | `ESCROW_CONFIRMATION_TIMEOUT_BEHAVIOR` | `AutoConfirm` | ✓ | What happens when confirmation timeout expires |
| `ConfirmationTimeoutCheckIntervalSeconds` | `ESCROW_CONFIRMATION_TIMEOUT_CHECK_INTERVAL_SECONDS` | `30` | ✓ | How often the background service checks for expired confirmations |
| `ConfirmationTimeoutBatchSize` | `ESCROW_CONFIRMATION_TIMEOUT_BATCH_SIZE` | `100` | ✓ | Maximum escrows to process per timeout check cycle |
| `ExpirationStartupDelaySeconds` | `ESCROW_EXPIRATION_STARTUP_DELAY_SECONDS` | `20` | ✓ | Startup delay before first expiration check - used in `EscrowExpirationService` |
| `ConfirmationStartupDelaySeconds` | `ESCROW_CONFIRMATION_STARTUP_DELAY_SECONDS` | `15` | ✓ | Startup delay before first confirmation timeout check - used in `EscrowConfirmationTimeoutService` |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<EscrowService>` | Scoped | Structured logging |
| `EscrowServiceConfiguration` | Singleton | All 21 config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access (7 stores) |
| `ITelemetryProvider` | Singleton | Telemetry span instrumentation for async helpers and background services |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event subscription registration for contract events |

Service lifetime is **Scoped** (per-request). Three background services implemented:
- **`EscrowExpirationService`** - Checks for escrows past their `ExpiresAt + GracePeriod` in expirable states (PendingDeposits, PartiallyFunded, PendingConsent, PendingCondition) and transitions them to `Expired` status.
- **`EscrowConfirmationTimeoutService`** - Checks for escrows in `Releasing`/`Refunding` states with expired confirmation deadlines, applies configured timeout behavior (AutoConfirm, Dispute, or Refund).
- **`EscrowValidationService`** - Periodically validates deposited assets still exist and are in expected state by calling downstream services (Currency, Item, Contract). Transitions to `ValidationFailed` with `PreFailureStatus` for restoration after reaffirmation.

**Internal State Store Accessors** (constructor-cached):
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

- **Refund** (`/escrow/refund`): Accepts escrow in `Refunding`, `Validation_failed`, `Disputed`, `Partially_funded`, or `Pending_deposits` states. Always transitions directly to `Refunded` terminal state (this endpoint is the administrative/service-initiated refund path). The `Refunding` intermediate state with confirmation flow is only reached via `RecordConsent` when a party submits a Refund consent with non-Immediate RefundMode. Builds refund results from actual deposits (returns each deposit's assets to depositor).

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
 └────────┬─────────┘ │
 │ deposit(s) │ cancel/expire
 ▼ ▼
 ┌──────────────────┐ ┌──────────────┐
 │ Partially_funded │─────►│ Cancelled │ (terminal)
 └────────┬─────────┘ └──────────────┘
 │ all required deposits
 ▼ ┌──────────────┐
 ┌──────────────────┐ ┌──────►│ Expired │ (terminal)
 │ Funded │──┐ │ └──────────────┘
 └────────┬─────────┘ │ │
 │ │ │ (from any non-terminal pre-release state)
 │ consent │ │
 ▼ │ │
 ┌──────────────────┐ │ │
 │ Pending_consent │───┤ │
 └────────┬─────────┘ │ │
 │ threshold │ │
 │ met │ │
 ▼ │ │
 ┌──────────────────┐ │ dispute
 │Pending_condition │────┤ │
 └────┬───────┬─────┘ │ │
 │ │ │ │
 │ │ fail │ │
 │ ▼ │ │
 │ ┌──────────────┐│ │
 │ │Valid. failed ││ │ ┌──────────────┐
 │ └───┬──────────┘│ │ ┌─►│ Refunded │ (terminal)
 │ │ reaffirm │ │ │ └──────────────┘
 │ └─(back)────┘ │ │
 │ ▼ │
 │ condition ┌──────────────┐
 │ met │ Disputed │──────────┐
 ▼ └──────────────┘ │ resolve
 ┌──────────────────┐ │ ▼
 │ Finalizing │──────────┤ ┌──────────────────┐
 └────────┬─────────┘ │ │ Released/Refunded│
 │ │ │ (per arbiter) │
 ▼ │ └──────────────────┘
 ┌──────────────────┐ │
 │ Releasing │ │
 └────────┬─────────┘ │
 │ ▼
 ▼ ┌──────────────┐
 ┌──────────────────┐ │ Refunding │
 │ Released │ └──────┬───────┘
 │ (terminal) │ │
 └──────────────────┘ ▼
 ┌──────────────┐
 │ Refunded │
 │ (terminal) │
 └──────────────┘

 Terminal States: Released, Refunded, Expired, Cancelled


Token System (full_consent mode)
===================================

 CreateEscrow
 │
 ├── For each party with expected deposits:
 │ ├── Generate 32 random bytes
 │ ├── Combine with "{escrowId}:{partyId}:Deposit" context
 │ ├── SHA-256 hash → deposit token (Base64)
 │ ├── SHA-256(token) → token hash (stored in Redis)
 │ └── Return plain token to caller
 │
 ├── For each consent-required party:
 │ ├── Same process with TokenType.Release
 │ └── Release tokens stored, returned on full funding
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
 │ (validates release token in full_consent mode)
 │
 ├── Party B consents (Release) ────► ConsentsReceived = 2
 │
 ├── ConsentsReceived >= RequiredConsentsForRelease?
 │ ├── No BoundContractId → Finalizing
 │ └── Has BoundContractId → Pending_condition
 │
 ├── Party X consents (Refund) ─────► Refunding
 │ (any consent-required party can trigger)
 │
 └── Party Y consents (Dispute) ────► Disputed
 (any party can dispute)


Deposit/Release/Refund Lifecycle
==================================

 ┌─────────┐ ┌──────────────────┐ ┌─────────────┐
 │ Party │ deposit│ Escrow Svc │ event │ Currency/ │
 │ │───────►│ │───────►│ Inventory │
 └─────────┘ │ │ │ Service │
 │ │ └─────────────┘
 │ (holds assets │
 │ as records, │
 │ no physical │
 │ movement) │
 │ │
 ┌─────────┐ release│ │ event ┌─────────────┐
 │Initiator│───────►│ Status→Released │───────►│ Currency/ │
 │ /System │ │ │ │ Inventory │
 └─────────┘ └──────────────────┘ │ (executes │
 │ transfer) │
 └─────────────┘

 Note: Escrow service is a COORDINATION layer.
 It tracks what should move where, but downstream
 services execute the actual asset movements.


Dispute Resolution
=====================

 ┌──────────┐ dispute ┌──────────────┐
 │ Party │──────────►│ Disputed │
 └──────────┘ └──────┬───────┘
 │
 ┌──────▼───────┐
 │ Arbiter │
 │ resolves │
 └──────┬───────┘
 │
 ┌─────────────────┼────────────────┐
 │ │ │
 ▼ ▼ ▼
 ┌────────────┐ ┌────────────┐ ┌────────────────┐
 │ Released │ │ Refunded │ │ Split │
 │ (original │ │ (return to │ │ (custom alloc │
 │ allocat.) │ │ depositors)│ │ per arbiter) │
 └────────────┘ └────────────┘ └────────────────┘
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

1. ~~**ValidateEscrow asset checking**~~: **FIXED** (2026-03-18) - Implemented per-asset-type validation via direct service calls. Added `ICurrencyClient`, `IItemClient`, `IContractClient` as constructor dependencies (L4→L2/L1 hard). Currency validates wallet/balance existence via `GetBalanceAsync`. Items validate instance existence via `GetItemInstanceAsync`. Contracts validate instance existence and reject terminal states (Fulfilled/Terminated/Expired) via `GetContractInstanceAsync`. Custom assets look up handler from registry (mesh invocation deferred to #153). Service unavailability is NOT validation failure — assets are skipped and retried next cycle. Only confirmed discrepancies (404, terminal contract status) produce `ValidationFailure` entries. Closes [#213](https://github.com/beyond-immersion/bannou-service/issues/213).

2. ~~**Periodic validation loop**~~: **FIXED** (2026-03-18) - Implemented `EscrowValidationService` background worker following `EscrowExpirationService` pattern. Runs at `ValidationCheckInterval` (PT5M default). Queries escrows in funded states (Funded, PendingConsent, PendingCondition, Finalizing, Releasing) where `NextValidationDue <= now`. `NextValidationDue` initialized on Funded transition in `DepositAsync`. Validation failures from any funded state (except Releasing) transition to `ValidationFailed` with `PreFailureStatus` stored for restoration after reaffirmation. `ReaffirmAsync` now restores to `PreFailureStatus` instead of hardcoded `PendingCondition`. Added `ValidationStartupDelaySeconds` and `ValidationBatchSize` config properties. Closes [#250](https://github.com/beyond-immersion/bannou-service/issues/250).

3. ~~**Configuration property not wired up**~~: **FIXED** (2026-03-16) - Duplicate of stub #2. `ValidationCheckInterval` is the interval for the periodic validation background service tracked by Issue #250. No separate fix needed — resolving #250 resolves this.

4. ~~**Custom handler invocation (validation path)**~~: **FIXED** (2026-03-18) - Implemented custom handler validation invocation via `IMeshInvocationClient`. Added `CustomHandlerValidateRequest`/`CustomHandlerValidateResponse` schema types defining the standardized handler validation protocol. `ValidateCustomAssetAsync` now calls the handler's `ValidateEndpoint` via mesh with escrow context and custom asset data. `MeshInvocationException` caught for service unavailability (skip, not failure). Handler responses with `valid: false` produce `ValidationFailure` entries. Deposit/release/refund handler invocation remains with [#153](https://github.com/beyond-immersion/bannou-service/issues/153).

5. ~~**Asset transfer execution (deposit path)**~~: **FIXED** (2026-03-18) - Implemented deposit-time asset transfers via `ExecuteDepositTransfersAsync`. Currency deposits call `ICurrencyClient.EscrowDepositAsync` with idempotency key. Items/ItemStacks call `IInventoryClient.TransferItemAsync` to escrow container. Contracts call `IContractClient.LockContractAsync` with escrow as guardian. Custom assets call handler's DepositEndpoint via mesh. Transfers execute BEFORE agreement mutation — failure cleanly rejects the deposit (T7). Added `IInventoryClient` as constructor dependency. Release/refund/cancel/expire asset transfer execution remains with [#153](https://github.com/beyond-immersion/bannou-service/issues/153).
<!-- AUDIT:IMPLEMENTATION:2026-03-18:https://github.com/beyond-immersion/bannou-service/issues/153 -->

---

## Potential Extensions

1. ~~**Handler invocation pipeline**~~: **FIXED** (2026-03-18) - Custom handler endpoints (deposit/release/refund/validate) now invoked via `IMeshInvocationClient` during all escrow flows. Deposit calls handler's `DepositEndpoint`, release calls `ReleaseEndpoint`, refund calls `RefundEndpoint`, validation calls `ValidateEndpoint`. Standardized protocol types defined in schema (`CustomHandlerValidateRequest/Response`, `CustomHandlerReleaseRequest/Response`, `CustomHandlerRefundRequest/Response`). Handler lookup from `escrow-handler-registry` store. Closes [#153](https://github.com/beyond-immersion/bannou-service/issues/153).

2. **Deposit commitment window and forfeiture policy**: Progressive deposit hardening where deposits become non-refundable after a configurable `commitmentWindow` duration. Pre-agreed `forfeiturePolicy` (Refund, ForfeitToCounterparty, Proportional, Arbiter) enables automatic breach resolution without arbiter involvement. Inspired by real-world earnest money, liquidated damages, and construction progress payment patterns. When combined with typed `TerminationCategory` from [#563](https://github.com/beyond-immersion/bannou-service/issues/563), enables nuanced breach handling: breach within commitment window → refund (deposits still soft); breach after window → apply forfeiture policy. Includes potential for auto-generated return contracts when deposited assets have already been transferred/consumed. See [#698](https://github.com/beyond-immersion/bannou-service/issues/698).
<!-- AUDIT:NEEDS_DESIGN:2026-03-18:https://github.com/beyond-immersion/bannou-service/issues/698 -->

3. **Typed contract termination category consumption**: When Contract implements `TerminationCategory` enum ([#563](https://github.com/beyond-immersion/bannou-service/issues/563)), Escrow's `HandleContractTerminatedAsync` will differentiate behavior: `Breach` → `Disputed` (if arbiter exists) or `Refunded` (no arbiter); all other categories → `Refunded` (current behavior). Future commitment window feature (#698) enhances this with automatic forfeiture based on commitment status.
<!-- AUDIT:NEEDS_DESIGN:2026-03-18:https://github.com/beyond-immersion/bannou-service/issues/563 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Missing `x-references` for lib-resource cleanup**~~: **FIXED** (2026-03-16) - Added `x-references` declaration in `escrow-api.yaml` targeting `character` with CASCADE policy. Added `/escrow/cleanup-by-character` endpoint with `CleanupByCharacterRequest`/`CleanupByCharacterResponse` models. Generated `EscrowReferenceTracking.cs` with `RegisterCharacterReferenceAsync`/`UnregisterCharacterReferenceAsync` helpers and `RegisterResourceCleanupCallbacksAsync` callback registration. Implemented `CleanupByCharacterAsync` in `EscrowService.cs` following the same pattern as account cleanup — queries agreements where character is a party, per-agreement error isolation via `CleanupSingleAgreementAsync`. Account cleanup remains event-based per T28 Account Deletion Cleanup Obligation (not x-references).

2. ~~**Missing `account.deleted` event handler**~~: **FIXED** (2026-03-16) - Added `HandleAccountDeletedAsync` handler in `EscrowServiceEvents.cs` with `CleanupEscrowsForAccountAsync` cleanup logic. Queries agreements where account is a party, then removes agreement records, tokens, status index entries, party pending counts, and validation tracking. Per-agreement error isolation with telemetry spans.

3. ~~**No multi-service call compensation in release/refund flows**~~: **FIXED** (2026-03-16) - Reclassified: no multi-service calls currently exist in release/refund flows (asset transfer execution is unimplemented — stub #5). T7 Multi-Service Call Compensation applies only when actual inter-service calls are made. Compensation strategy must be designed as part of the asset transfer integration tracked by [#153](https://github.com/beyond-immersion/bannou-service/issues/153). Added explicit requirement to Issue #153 scope in Work Tracking.

4. ~~**Background service `ExecuteAsync` has telemetry span**~~: **FIXED** (2026-03-16) - Removed `StartActivity` spans from `EscrowExpirationService.ExecuteAsync` and `EscrowConfirmationTimeoutService.ExecuteAsync`. Per-cycle methods retain their own spans correctly.

### Intentional Quirks (Documented Behavior)

1. **Status index key pattern uses individual keys**: Status index stores entries at `status:{status}:{escrowId}` as individual key-value pairs using `IStateStore.SaveAsync`/`DeleteAsync`. This is not a Redis Set — it's keyed storage. Scanning all escrows by status requires `QueryAsync` against the agreement store, not the status index.

2. **Single refund consent triggers refund**: Any consent-required party submitting a `Refund` consent immediately transitions to `Refunding`. Unilateral refund right is a safety mechanism that can surprise developers expecting multi-party consensus.

3. **Release tokens returned on full funding**: When the last required deposit arrives, the `DepositResponse` includes all release tokens for consent-required parties. This is the only time release tokens are proactively delivered (otherwise use `GetMyToken`).

4. **Token hash double-hashing**: Tokens are first generated as SHA-256 hash of random bytes + context, then stored by hashing the token again. Validation requires SHA-256(submitted_token) lookup, providing one-way token storage.

5. **Asset transfer execution**: Deposit transfers implemented (2026-03-18). Release and refund transfers now also implemented — `ExecuteReleaseTransfersAsync` credits recipients via `ICurrencyClient.EscrowReleaseAsync`, transfers items via `IInventoryClient.TransferItemAsync`, unlocks contracts via `IContractClient.UnlockContractAsync`, and calls custom handler ReleaseEndpoints. `ExecuteRefundTransfersAsync` does the same for refunds using `EscrowRefundAsync` and refund endpoints. All terminal paths (Release, Refund, Cancel, Resolve, ConfirmRelease, ConfirmRefund, Expiration, ConfirmationTimeout) call the appropriate transfer helper AFTER the agreement status transition succeeds. Transfer failures are logged with per-asset error isolation but do NOT block the terminal transition — the FSM state is authoritative and the `Disputed` escalation path handles unresolved transfer failures. **Original design resolved 2026-03-18**: Escrow will call `ICurrencyClient` and `IInventoryClient` directly (L4→L2 hard dependencies via constructor injection). Currency already has 3 escrow endpoints (`/currency/escrow/deposit`, `/currency/escrow/release`, `/currency/escrow/refund`). Inventory needs 3 matching endpoints using the existing `ownerType: escrow` value. Events like `escrow.released` and `escrow.refunded` will remain for observability and analytics, NOT for triggering asset movements. T7 compensation via `Releasing`/`Refunding` intermediate FSM states; `Disputed` as escalation path. For contract-bound escrows, Contract's `ExecuteContract` handles distribution — Escrow verifies and transitions. For custom asset types, registered handler endpoints invoked via lib-mesh. Implementation tracked by [#153](https://github.com/beyond-immersion/bannou-service/issues/153).

6. **Contract event handlers are best-effort**: `HandleContractFulfilledAsync` and `HandleContractTerminatedAsync` use try-catch with error event emission but don't retry or queue failed operations.

7. **Single-document agreement model**: All parties, deposits, consents, allocations, and validation failures are stored in a single `EscrowAgreementModel` document in MySQL. Document size is bounded by configuration: `MaxParties` (default 10) and `MaxAssetsPerDeposit` (default 50) cap the nested list growth. For typical 2-party escrows the document is small; even worst-case (10 parties, 50 assets each, full consent/validation history) remains within MySQL JSON column performance characteristics.

8. **Token ExpiresAt stored but not checked during validation**: `TokenHashModel.ExpiresAt` is set to the escrow's expiration when tokens are created, but token validation only checks the `Used` flag (not `ExpiresAt`). This is safe because each method validates the escrow agreement's `ExpiresAt` before reaching token validation — so an expired escrow's tokens are unreachable. The token-level `ExpiresAt` exists as metadata (useful for diagnostics/cleanup) but is not a security boundary.

9. **Party pending count failures silently logged**: `IncrementPartyPendingCountAsync` and `DecrementPartyPendingCountAsync` log warnings but don't fail the operation when count updates fail after max retries. This is intentional — the pending count is a soft rate-limit (`MaxPendingPerParty`), not a data integrity mechanism. Authoritative escrow agreements live in MySQL with proper concurrency. A stale count self-corrects on the next successful operation, and worst-case failure modes (slightly permissive or restrictive limits) are bounded and non-catastrophic.

10. **Idempotency records not cleaned up during character/account cleanup**: `CleanupSingleAgreementAsync` deletes tokens, status index entries, validation tracking, and agreement records, but does not delete `idemp:{key}` entries for deposits made by the cleaned-up entity. This is acceptable because idempotency records have a bounded TTL (`IdempotencyTtlHours`, default 24h) and contain no PII — they store the escrow response for deduplication replay. The TTL is the designed cleanup mechanism for these ephemeral Redis records.

11. ~~**EscrowResolution enum has no distinct value for expired-with-no-deposits**~~: **FIXED** (2026-03-18) - Added `Expired` value to `EscrowResolution` enum in schema. `EscrowExpirationService` now uses `Expired` when no deposits exist and `ExpiredRefunded` when deposits need refunding. Closes [#699](https://github.com/beyond-immersion/bannou-service/issues/699).

### Design Considerations

1. ~~**QueryAsync for listing**~~: **FIXED** (2026-03-16) - Replaced `QueryAsync` + in-memory `.Skip().Take()` with `QueryPagedAsync` for server-side MySQL pagination. All three filter paths (by party+status, by party, by status, unfiltered) now use a single `QueryPagedAsync` call with combined predicates. Results ordered by `CreatedAt` descending.

2. ~~**Idempotency result caching stores full response**~~: **FIXED** (2026-03-16) - Changed `IdempotencyRecord.Result` from `object?` to `DepositResponse?`. The `object?` type caused a deserialization bug: BannouJson cannot roundtrip `object?` back to the concrete type, so the `is DepositResponse` pattern match always failed, making the cached response unreachable on idempotent retries. The typed field ensures correct deserialization. Large Redis entries remain inherent to caching full responses but are bounded by `MaxAssetsPerDeposit` (50) and TTL (24h).

3. ~~**Event ordering not guaranteed**~~: **FIXED** (2026-03-16) - Events ARE published in deterministic order via sequential `await` calls (e.g., `PublishEscrowDepositReceivedAsync` before `PublishEscrowFundedAsync` in `DepositAsync`). RabbitMQ preserves FIFO within a single channel. The remaining concern — if the process crashes between two sequential publishes, the second event is lost — is a platform-wide infrastructure characteristic of non-transactional event publishing, not an Escrow-specific gap. All Bannou services that publish multiple events in a single operation share this characteristic.

4. ~~**Contract termination refund doesn't verify contract binding**~~: **FIXED** (2026-03-16) - Added `BoundContractId` re-verification guard in both `RefundForContractTerminationAsync` and `TransitionToFinalizingForContractAsync`. After re-loading the agreement via `GetWithETagAsync`, both methods now verify the agreement is still bound to the expected contract before mutating state. Closes the TOCTOU window between the initial query and the ETag-protected mutation.

5. ~~**Party pending count failures silently logged**~~: **FIXED** (2026-03-17) - Reclassified as intentional behavior. The party pending count is a soft rate-limit (enforcing `MaxPendingPerParty`), not a data integrity mechanism. Silent failure with Warning logging is correct: (1) authoritative escrow agreements are stored in MySQL with proper concurrency, (2) a stale count self-corrects when the next successful operation updates the party's count, (3) worst-case failure modes are bounded (slightly permissive or restrictive limits) and non-catastrophic. Moved to Intentional Quirks.

6. ~~**No distributed lock for concurrent agreement modifications**~~: **RESOLVED** (2026-03-18) — ETag-based optimistic concurrency with `MaxConcurrencyRetries` (default 3) is sufficient at 100K NPC scale. The dominant escrow pattern (2-party NPC vendor trades) has near-zero contention due to inherently sequential GOAP-driven operations. Multi-party escrows are rare and bounded by `MaxParties` (default 10). MySQL sub-millisecond read-modify-write resolves retry loops within microseconds. Distributed locks would add latency to every operation for zero benefit on the dominant case. If load testing reveals problems: (1) increase `MaxConcurrencyRetries` via config, (2) add retry backoff with jitter, (3) last resort: configurable per-agreement lock above a party count threshold. Closed [#697](https://github.com/beyond-immersion/bannou-service/issues/697).

---

## Work Tracking

### Pending Design Review

*No implementation work remaining. PE #2 and PE #3 are NEEDS_DESIGN pending external dependencies.*

### Completed

1. **Full release/refund asset transfer integration** — [Issue #153](https://github.com/beyond-immersion/bannou-service/issues/153) (2026-03-18)
   - Added `ExecuteReleaseTransfersAsync` (Currency.EscrowRelease, Inventory.TransferItem, Contract.Unlock, Custom handler release via mesh)
   - Added `ExecuteRefundTransfersAsync` (Currency.EscrowRefund, Inventory.TransferItem, Contract.Unlock, Custom handler refund via mesh)
   - Wired into ALL terminal paths: Release, Refund, Cancel, Resolve, ConfirmRelease, ConfirmRefund, EscrowExpirationService, EscrowConfirmationTimeoutService
   - Added `CustomHandlerReleaseRequest/Response` and `CustomHandlerRefundRequest/Response` schema types
   - Transfer failures use per-asset error isolation (T7) — FSM state is authoritative, Disputed is escalation path
   - Added `IInventoryClient` constructor dependency

1. **Deposit-time asset transfers** — (2026-03-18)
   - Added `IInventoryClient` as constructor dependency
   - Implemented `ExecuteDepositTransfersAsync` in helpers: Currency (`EscrowDepositAsync`), Items (`TransferItemAsync`), Contracts (`LockContractAsync`), Custom (handler DepositEndpoint via mesh)
   - Transfers execute BEFORE agreement mutation — failure rejects deposit cleanly (T7)
   - Release/refund/cancel/expire asset transfer remains with #153

1. **Custom handler validation invocation** — (2026-03-18)
   - Added `CustomHandlerValidateRequest`/`CustomHandlerValidateResponse` schema types
   - Added `IMeshInvocationClient` to EscrowService constructor
   - `ValidateCustomAssetAsync` now calls handler's ValidateEndpoint via mesh (both endpoint and worker)
   - Deposit/release/refund handler invocation remains with #153

1. **Periodic validation background service** — [Issue #250](https://github.com/beyond-immersion/bannou-service/issues/250) (2026-03-18)
   - Created `EscrowValidationService` background worker following `EscrowExpirationService` pattern
   - Queries funded states where `NextValidationDue <= now`, validates assets via Currency/Item/Contract clients
   - `NextValidationDue` initialized on `Funded` transition in `DepositAsync`
   - `PreFailureStatus` stored on `ValidationFailed` transition, restored by `ReaffirmAsync`
   - Broadened `ValidateEscrowAsync` endpoint to transition from any funded state (not just PendingCondition)
   - Added `ValidationStartupDelaySeconds` and `ValidationBatchSize` config properties

1. **ValidateEscrow asset checking** — [Issue #213](https://github.com/beyond-immersion/bannou-service/issues/213) (2026-03-18)
   - Added `ICurrencyClient`, `IItemClient`, `IContractClient` as constructor dependencies
   - Implemented per-asset-type validation: Currency (wallet/balance existence), Item/ItemStack (instance existence), Contract (instance existence + terminal status check), Custom (handler lookup, mesh invocation deferred to #153)
   - Service unavailability = skip (not failure); only confirmed discrepancies produce `ValidationFailure` entries
   - Updated implementation map pseudocode and dependency tables

1. **EscrowResolution `Expired` enum value** — [Issue #699](https://github.com/beyond-immersion/bannou-service/issues/699) (2026-03-18)
   - Added `Expired` value to `EscrowResolution` enum in `escrow-api.yaml`
   - `EscrowExpirationService` now uses `Expired` (no deposits) vs `ExpiredRefunded` (has deposits)
   - Updated implementation map pseudocode

1. **Four code fixes from implementation map cross-reference** — (2026-03-18)
   - Fixed dead ternary in `EscrowExpirationService` (both branches identical `ExpiredRefunded`); created [#699](https://github.com/beyond-immersion/bannou-service/issues/699) for schema enum gap
   - Fixed `EscrowConfirmationTimeoutService` missing party pending count decrements on all terminal-state paths (T9 violation — `HandleRefundAsync` and `CompleteEscrowAsync` now decrement like every other terminal path)
   - Fixed `RecordConsent` Refund path not initializing `ReleaseConfirmations` for `PartyRequired`/`ServiceOnly` refund modes (made `ConfirmRefund` always return 400 for non-Immediate refund flows)
   - Fixed hardcoded key prefix strings in both background services to use `EscrowService.BuildAgreementKey()`/`BuildStatusIndexKey()`/`BuildPartyPendingKey()` (T6 violation)
   - Updated deep dive: Quirk #5 language (was present-tense for unimplemented feature), Refund endpoint description (was conflating two code paths), added Quirks #10-11

2. **Periodic validation background service design resolved** — [Issue #250](https://github.com/beyond-immersion/bannou-service/issues/250) (2026-03-18)
   - `EscrowValidationService` follows `EscrowExpirationService` pattern
   - Validates funded states (`Funded`, `PendingConsent`, `PendingCondition`, `Finalizing`, `Releasing`) where `NextValidationDue <= now`
   - `NextValidationDue` initialized on `Funded` transition
   - Validation failures transition to `Validation_failed` with `PreFailureStatus` for restoration after reaffirmation (except `Releasing` — handled by release compensation)
   - Implementation merges with #213; both issues remain open for implementation

2. **ValidateEscrow asset checking design resolved** — [Issue #213](https://github.com/beyond-immersion/bannou-service/issues/213) (2026-03-18)
   - Per-asset-type validation: Currency (wallet/transactions), Items (escrow container), Contracts (guardian lock), Custom (handler ValidateEndpoint via mesh)
   - Service unavailability = skip + retry next cycle (NOT validation failure)
   - Only confirmed asset discrepancies trigger `Validation_failed` state and reaffirmation flow
   - Implementation work remains open on Issue #213

2. **Asset transfer integration design resolved** — [Issue #153](https://github.com/beyond-immersion/bannou-service/issues/153) (2026-03-18)
   - Direct invocation (Option B) confirmed as sole tenet-compliant approach per T27, T2, T4
   - Escrow calls `ICurrencyClient` + `IInventoryClient` via constructor injection (hard L4→L2)
   - Currency already has 3 escrow endpoints; Inventory needs 3 matching endpoints (`ownerType: escrow`)
   - T7 compensation via `Releasing`/`Refunding` intermediate states; `Disputed` as escalation path
   - Contract-bound escrows: Contract's `ExecuteContract` handles distribution; Escrow verifies and transitions
   - Custom handlers: same direct invocation via mesh using registered handler endpoints
   - Events remain for observability (Analytics, Regional Watchers), NOT for triggering asset movements
   - Implementation work remains open on Issue #153

2. **x-references for lib-resource cleanup** — (2026-03-16)
   - Added `x-references` targeting character with CASCADE policy
   - Added `/escrow/cleanup-by-character` endpoint and `CleanupByCharacterAsync` implementation
   - Generated `EscrowReferenceTracking.cs` with register/unregister helpers and callback registration

2. **Bug #3 reclassified: multi-service call compensation** — (2026-03-16)
   - No T7 violation exists today — release/refund flows make zero inter-service calls (asset transfer is unimplemented)
   - Compensation strategy requirement added to Issue #153 (asset transfer integration) scope

3. **DC #2: Idempotency result caching deserialization** — (2026-03-16)
   - Changed `IdempotencyRecord.Result` from `object?` to `DepositResponse?`
   - Fixed deserialization bug where BannouJson could not roundtrip `object?` to concrete type
   - Idempotent deposit retries now correctly return cached response

4. **DC #3: Event ordering clarification** — (2026-03-16)
   - Events ARE published in deterministic order via sequential awaits
   - "Crash between publishes" concern is a platform-wide characteristic, not Escrow-specific
   - Documentation updated to reflect actual code behavior

5. **DC #4: Contract binding TOCTOU guard** — (2026-03-16)
   - Added `BoundContractId` re-verification in `RefundForContractTerminationAsync` and `TransitionToFinalizingForContractAsync`
   - Closes TOCTOU window between query-by-contract and ETag-protected mutation

6. **DC #5: Party pending count reclassification** — (2026-03-17)
   - Reclassified from Design Consideration to Intentional Quirk
   - Pending count is a soft rate-limit, not a data integrity mechanism; silent failure is correct
   - Moved to Intentional Quirks #9
