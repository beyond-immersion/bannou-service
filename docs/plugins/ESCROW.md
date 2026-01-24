# Escrow Plugin Deep Dive

> **Plugin**: lib-escrow
> **Schema**: schemas/escrow-api.yaml
> **Version**: 1.0.0
> **State Stores**: escrow-agreements (MySQL), escrow-handler-registry (MySQL), escrow-tokens (Redis), escrow-idempotency (Redis), escrow-status-index (Redis), escrow-party-pending (Redis), escrow-active-validation (Redis)

---

## Overview

Full-custody orchestration layer for multi-party asset exchanges. Manages the complete escrow lifecycle from creation through deposit collection, consent gathering, condition verification, and final release or refund. Supports four escrow types (two-party, multi-party, conditional, auction) with three trust modes (full-consent requiring cryptographic tokens, initiator-trusted, single-party-trusted). Features a 13-state finite state machine, SHA-256-based token generation for deposit and release authorization, idempotent deposit handling, contract-bound conditional releases, per-party pending count tracking, custom asset type handler registration for extensibility, periodic validation with reaffirmation flow, and arbiter-mediated dispute resolution with split allocation support. Handles currency, items, item stacks, contracts, and custom asset types. Does NOT perform actual asset transfers itself - publishes events that downstream services (lib-currency, lib-inventory, lib-contract) consume to execute the physical movements.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for agreements and handler registry; Redis for tokens, idempotency, status indexes, party pending counts, and validation tracking |
| lib-messaging (`IMessageBus`) | Publishing 13 escrow lifecycle events; error event publishing via `TryPublishErrorAsync` |
| lib-mesh (`IServiceNavigator`) | Cross-service client access (injected but not currently invoked for asset transfers) |

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
| `escrow.released` | `EscrowReleasedEvent` | Assets released to recipients |
| `escrow.refunded` | `EscrowRefundedEvent` | Assets refunded to depositors |
| `escrow.disputed` | `EscrowDisputedEvent` | Party raises a dispute |
| `escrow.resolved` | `EscrowResolvedEvent` | Arbiter resolves a dispute |
| `escrow.expired` | `EscrowExpiredEvent` | Escrow times out (topic defined, not yet emitted in code) |
| `escrow.cancelled` | `EscrowCancelledEvent` | Escrow cancelled before full funding |
| `escrow.validation.failed` | `EscrowValidationFailedEvent` | Periodic validation detects asset discrepancy |
| `escrow.validation.reaffirmed` | `EscrowValidationReaffirmedEvent` | All affected parties reaffirm after validation failure |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `contract.fulfilled` | `ContractFulfilledEvent` | Triggers release when bound contract is fulfilled (schema-defined, handler not yet implemented) |
| `contract.terminated` | `ContractTerminatedEvent` | Triggers refund when bound contract is terminated (schema-defined, handler not yet implemented) |

Note: `RegisterEventConsumers()` is currently empty. The event subscriptions are defined in the schema but not yet wired up in the service implementation.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultTimeout` | `ESCROW_DEFAULT_TIMEOUT` | `P7D` | Default escrow expiration if not specified (ISO 8601 duration) |
| `MaxTimeout` | `ESCROW_MAX_TIMEOUT` | `P30D` | Maximum allowed escrow duration |
| `ExpirationGracePeriod` | `ESCROW_EXPIRATION_GRACE_PERIOD` | `PT1H` | Grace period after expiration before auto-refund |
| `TokenAlgorithm` | `ESCROW_TOKEN_ALGORITHM` | `hmac_sha256` | Algorithm used for token generation |
| `TokenLength` | `ESCROW_TOKEN_LENGTH` | `32` | Token length in bytes (before encoding) |
| `TokenSecret` | `ESCROW_TOKEN_SECRET` | `null` | Token secret for HMAC (must be set in production) |
| `ExpirationCheckInterval` | `ESCROW_EXPIRATION_CHECK_INTERVAL` | `PT1M` | How often to check for expired escrows |
| `ExpirationBatchSize` | `ESCROW_EXPIRATION_BATCH_SIZE` | `100` | Batch size for expiration processing |
| `ValidationCheckInterval` | `ESCROW_VALIDATION_CHECK_INTERVAL` | `PT5M` | How often to validate held assets |
| `MaxParties` | `ESCROW_MAX_PARTIES` | `10` | Maximum parties per escrow |
| `MaxAssetsPerDeposit` | `ESCROW_MAX_ASSETS_PER_DEPOSIT` | `50` | Maximum asset lines per deposit |
| `MaxPendingPerParty` | `ESCROW_MAX_PENDING_PER_PARTY` | `100` | Maximum concurrent pending escrows per party |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<EscrowService>` | Scoped | Structured logging |
| `EscrowServiceConfiguration` | Singleton | All 12 config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access (7 stores) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IServiceNavigator` | Scoped | Cross-service client access (for future asset handler invocation) |

Service lifetime is **Scoped** (per-request). No background services (expiration checking and periodic validation are defined in config but not yet implemented as background loops).

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

### Completion Endpoints (4 endpoints)

- **Release** (`/escrow/release`): Requires escrow in `Finalizing` or `Releasing` state. Builds release results from pre-configured `ReleaseAllocations`. Sets status to `Released`, resolution to `Released`. Decrements pending counts for all parties. Publishes `escrow.released`. Note: Does NOT invoke downstream services for actual asset movement - the event is consumed externally.

- **Refund** (`/escrow/refund`): Accepts escrow in `Refunding`, `Validation_failed`, `Disputed`, `Partially_funded`, or `Pending_deposits` states. Builds refund results from actual deposits (returns each deposit's assets to depositor). Sets status to `Refunded`. Decrements pending counts. Publishes `escrow.refunded`.

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

## Stubs & Unimplemented Features

1. **Event consumer registration empty**: `RegisterEventConsumers()` is a no-op. The schema defines subscriptions to `contract.fulfilled` and `contract.terminated` events for automatic release/refund of contract-bound escrows, but the handlers are not implemented. Currently, condition verification must be triggered externally via the `/escrow/verify-condition` endpoint.

2. **ValidateEscrow asset checking**: The `ValidateEscrowAsync` method contains a placeholder comment "Validate deposits (placeholder - real impl would check with currency/inventory services)". No actual cross-service validation is performed. The validation always passes (empty failure list).

3. **Expiration background processing**: Configuration defines `ExpirationCheckInterval` (PT1M), `ExpirationBatchSize` (100), and `ExpirationGracePeriod` (PT1H), but no background timer or hosted service scans for expired escrows. The `EscrowExpiredEvent` topic is defined but never published. Expired escrows remain in their current state until manually cancelled.

4. **Periodic validation loop**: Configuration defines `ValidationCheckInterval` (PT5M) but no background process triggers periodic validation. The `ValidationStore` tracks `NextValidationDue` but nothing reads it.

5. **Configuration properties not wired up**: Several configuration properties (`MaxParties`, `MaxAssetsPerDeposit`, `MaxPendingPerParty`, `DefaultTimeout`, `MaxTimeout`, `TokenAlgorithm`, `TokenLength`, `TokenSecret`) are defined but not referenced in the service implementation. Party count validation uses hardcoded `< 2` check. Token generation uses hardcoded 32-byte random regardless of `TokenLength`. No HMAC secret is used despite the `TokenSecret` config.

6. **Custom handler invocation**: Handlers are registered with deposit/release/refund/validate endpoints, but the escrow service never actually invokes these endpoints during deposit or release flows. The handler registry is purely declarative.

7. **Asset transfer execution**: Release and refund operations set status and publish events but do not call currency/inventory services to execute actual transfers. The service is purely a coordination/tracking layer that assumes downstream consumers handle the physical movements.

8. **Releasing state unused in transitions**: The `ValidTransitions` map includes `Finalizing -> Releasing -> Released`, but the release flow jumps directly from `Finalizing` to `Released` without passing through `Releasing`.

---

## Potential Extensions

1. **Background expiration processor**: Implement `IHostedService` that periodically scans the status index for escrows past their `ExpiresAt`, applies grace period, transitions to `Expired`, and auto-refunds deposits.

2. **Contract event consumers**: Wire up `contract.fulfilled` and `contract.terminated` event handlers to automatically trigger release or refund for contract-bound escrows without manual verification.

3. **Cross-service asset validation**: Implement actual calls to currency/inventory services in `ValidateEscrowAsync` to verify deposited assets are still held and unchanged.

4. **Handler invocation pipeline**: During deposit/release/refund, look up registered handlers for each asset type and invoke their endpoints via mesh, enabling plug-and-play asset type support.

5. **Rate limiting via MaxPendingPerParty**: Enforce the configured limit during `CreateEscrow` by checking the party pending store before allowing new escrows.

6. **Token secret HMAC integration**: Use the configured `TokenSecret` in token generation for deterministic, verifiable tokens rather than purely random tokens.

7. **Distributed lock for concurrent modifications**: Add lock acquisition around agreement modifications to prevent race conditions when multiple parties deposit/consent simultaneously.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Consent from Funded state transitions to Pending_consent**: The first release consent on a Funded escrow transitions to `Pending_consent` even if consent threshold is not yet met. This is deliberate state-tracking to distinguish "funded but no consents" from "funded with partial consents".

2. **Single refund consent triggers refund**: Any consent-required party submitting a `Refund` consent immediately transitions to `Refunding`. This is by design - unilateral refund right is a safety mechanism.

3. **Dispute available from Finalizing**: A party can dispute even after all consents are gathered and finalization has begun. This prevents irreversible release when a party discovers fraud during the finalization window.

4. **BoundContractId routes through Pending_condition**: When `BoundContractId` is set, achieving consent threshold goes to `Pending_condition` (awaiting contract verification) rather than directly to `Finalizing`. This two-phase gate ensures the bound contract's conditions are met.

5. **Deposit tokens only in full_consent mode**: In `initiator_trusted` or `single_party_trusted` modes, no cryptographic tokens are generated. Deposits are accepted based on party membership alone.

6. **Cancel only before full funding**: The cancel operation intentionally only works from `Pending_deposits` or `Partially_funded`. Once fully funded, the only paths out are consent-driven (release, refund, or dispute).

7. **Release tokens returned on full funding**: When the last required deposit arrives, the `DepositResponse` includes all release tokens for consent-required parties. This is the only time release tokens are proactively delivered (otherwise use `GetMyToken`).

8. **Arbiter does not need consent to resolve**: The arbiter bypasses the normal consent flow entirely. Resolution is a unilateral action requiring only the arbiter role.

### Design Considerations (Requires Planning)

1. **Party pending count race condition**: The `PartyPendingStore` increment/decrement in `CreateEscrowAsync` and completion operations uses `GetAsync` + modify + `SaveAsync` (last-write-wins). Concurrent escrow creation/completion for the same party can cause the counter to drift by 1. This is informational only (no financial impact) and self-corrects over time. The agreement model mutations (deposit, consent, dispute, release, refund, cancel, resolve, verify, validate, reaffirm) are all protected by ETag-based optimistic concurrency with 3-attempt retry loops. Token marking is deferred until after successful agreement save to prevent token consumption on retry.

2. **Status index not cleaned on expiration**: Since no expiration processor exists, status index entries for expired escrows accumulate indefinitely. If an escrow expires without being cancelled, its status index entry remains under the pre-expiration status forever. Needs either a periodic cleanup timer or expiration handling in query paths.

3. **Large agreement documents**: All parties, deposits, consents, allocations, and validation failures are stored in a single agreement document. Multi-party escrows with many deposits and consent records can grow large, impacting read/write performance.

4. **Token storage exposes timing**: Token hashes are stored with `ExpiresAt` from the escrow expiration. However, token expiration is not checked during validation - only the `Used` flag is verified. Expired tokens remain valid if the escrow has not expired.

5. **QueryAsync for listing**: `ListEscrowsAsync` uses `QueryAsync` with lambda predicates, which loads all agreements into memory for filtering. This does not scale for large datasets.

6. **Idempotency result caching stores full response**: The `IdempotencyRecord.Result` field stores the complete `DepositResponse` object including the full escrow state. This creates large Redis entries and may have serialization issues if the response model changes.

7. **Event ordering not guaranteed**: Multiple events can be published in a single operation (e.g., deposit + funded), but there is no transactional guarantee on event ordering or all-or-nothing delivery.
