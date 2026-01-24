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

### Tenet Violations (Fix Immediately)

#### 1. FOUNDATION TENETS (T6) - Missing Constructor Null Checks and IEventConsumer Registration

**File**: `plugins/lib-escrow/EscrowService.cs`, lines 197-209

The constructor stores all dependencies directly without `?? throw new ArgumentNullException(nameof(...))` guards. Per T6, all injected dependencies must have explicit null checks. Additionally, the constructor does not accept `IEventConsumer` and does not call `RegisterEventConsumers(eventConsumer)`, even though the `EscrowServiceEvents.cs` partial class defines the method.

**Fix**: Add null-check guards for all constructor parameters. Add `IEventConsumer eventConsumer` parameter, call `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer))`, and call `RegisterEventConsumers(eventConsumer)`.

---

#### 2. IMPLEMENTATION TENETS (T21) - All 12 Configuration Properties Are Dead (Never Referenced)

**Files**: All service files in `plugins/lib-escrow/`

The `_configuration` field is assigned in the constructor but **never referenced anywhere** in the entire service implementation. All 12 configuration properties (`DefaultTimeout`, `MaxTimeout`, `ExpirationGracePeriod`, `TokenAlgorithm`, `TokenLength`, `TokenSecret`, `ExpirationCheckInterval`, `ExpirationBatchSize`, `ValidationCheckInterval`, `MaxParties`, `MaxAssetsPerDeposit`, `MaxPendingPerParty`) are dead configuration. Per T21, every defined config property MUST be referenced in service code.

**Fix**: Wire up all configuration properties OR remove them from `schemas/escrow-configuration.yaml`:
- Use `_configuration.MaxParties` in `CreateEscrowAsync` party count validation (currently hardcoded `< 2` only)
- Use `_configuration.MaxAssetsPerDeposit` in `DepositAsync` asset count validation
- Use `_configuration.MaxPendingPerParty` in `CreateEscrowAsync` pending count check
- Use `_configuration.DefaultTimeout` for the expiration calculation (currently hardcoded `now.AddDays(7)`)
- Use `_configuration.MaxTimeout` to cap requested expirations
- Use `_configuration.TokenLength` in `GenerateToken` (currently hardcoded `new byte[32]`)
- Use `_configuration.TokenSecret` for HMAC token generation
- Use `_configuration.TokenAlgorithm` to determine token generation strategy

---

#### 3. IMPLEMENTATION TENETS (T21) - Hardcoded Tunables

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`, line 39
```csharp
var expiresAt = body.ExpiresAt ?? now.AddDays(7);
```
Hardcoded 7-day default expiration. Should use `_configuration.DefaultTimeout` (which is defined as `"P7D"`).

**File**: `plugins/lib-escrow/EscrowServiceDeposits.cs`, line 235
```csharp
ExpiresAt = now.AddHours(24),
```
Hardcoded 24-hour idempotency TTL. Should be a configuration property.

**File**: `plugins/lib-escrow/EscrowService.cs`, line 277
```csharp
var randomBytes = new byte[32];
```
Hardcoded 32-byte token length. Should use `_configuration.TokenLength`.

**File**: `plugins/lib-escrow/EscrowServiceDeposits.cs`, line 44; `EscrowServiceConsent.cs`, line 24; `EscrowServiceCompletion.cs`, lines 24, 163, 295, 415, 527; `EscrowServiceValidation.cs`, lines 23, 190, 327
```csharp
for (var attempt = 0; attempt < 3; attempt++)
```
Hardcoded retry count of 3 across all ETag retry loops. Should be a configuration property.

**Fix**: Define additional configuration properties (`IdempotencyTtlHours`, `MaxConcurrencyRetries`) in the schema, or use the existing properties for values that already have them.

---

#### 4. IMPLEMENTATION TENETS (T9) - Static Dictionary for State Machine (Not ConcurrentDictionary)

**File**: `plugins/lib-escrow/EscrowService.cs`, line 108
```csharp
private static readonly Dictionary<EscrowStatus, HashSet<EscrowStatus>> ValidTransitions = new()
```

Per T9, use `ConcurrentDictionary` for local caches, never plain `Dictionary`. While this is a read-only static, it is initialized without `FrozenDictionary` or `ImmutableDictionary` and technically could be modified. The tenet is explicit: "Use ConcurrentDictionary for local caches, never plain Dictionary."

**Fix**: Change to a `static readonly IReadOnlyDictionary<EscrowStatus, HashSet<EscrowStatus>>` or `FrozenDictionary` to make immutability explicit, or use `ConcurrentDictionary`.

---

#### 5. IMPLEMENTATION TENETS (T9) - PartyPendingStore Non-Atomic Read-Modify-Write (No Distributed Lock)

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`, lines 183-195
```csharp
var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
var newCount = new PartyPendingCount { PendingCount = (existingCount?.PendingCount ?? 0) + 1 ... };
await PartyPendingStore.SaveAsync(partyKey, newCount, ...);
```

Also in `EscrowServiceCompletion.cs` lines 99-106, 229-238, 358-367, 644-653.

This is a non-atomic read-modify-write without ETag concurrency or distributed lock protection. Multiple instances can read the same count, increment, and write back, causing counter drift. Per T9, use `IDistributedLockProvider` for cross-instance coordination or ETag-based optimistic concurrency for state that requires consistency.

**Fix**: Either use `GetWithETagAsync`/`TrySaveAsync` for the party pending store, or acquire a distributed lock around these operations.

---

#### 6. IMPLEMENTATION TENETS (T7) - Missing ApiException Catch Blocks

**Files**: All endpoint methods across all service files

No endpoint method distinguishes between `ApiException` (expected API error from downstream) and `Exception` (unexpected failure). T7 requires:
```csharp
catch (ApiException ex) { _logger.LogWarning(...); return ((StatusCodes)ex.StatusCode, null); }
catch (Exception ex) { _logger.LogError(...); await EmitErrorAsync(...); return (StatusCodes.InternalServerError, null); }
```

Currently all methods only catch `Exception`. While the service does not currently make downstream service calls, state store operations can throw `ApiException` and should be handled distinctly.

**Fix**: Add `catch (ApiException ex)` blocks before the generic `catch (Exception ex)` in every endpoint method.

---

#### 7. QUALITY TENETS (T10) - Hardcoded Service ID in EmitErrorAsync

**File**: `plugins/lib-escrow/EscrowService.cs`, line 580
```csharp
await _messageBus.TryPublishErrorAsync("escrow", ...);
```

Hardcoded `"escrow"` service ID string. Per T21, configuration-first pattern should use `_configuration.ServiceId ?? "escrow"` or a similar pattern from the configuration class (`ForceServiceId`).

**Fix**: Use `_configuration.ForceServiceId ?? "escrow"` to respect the configuration override pattern.

---

#### 8. IMPLEMENTATION TENETS (T25) - ToString() on Enums When Populating Event Models

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`, lines 202-203, 208
```csharp
EscrowType = agreementModel.EscrowType.ToString(),
TrustMode = agreementModel.TrustMode.ToString(),
Role = p.Role.ToString()
```

**File**: `plugins/lib-escrow/EscrowServiceConsent.cs`, line 216
```csharp
ConsentType = body.ConsentType.ToString(),
```

**File**: `plugins/lib-escrow/EscrowServiceCompletion.cs`, line 664
```csharp
Resolution = body.Resolution.ToString(),
```

**File**: `plugins/lib-escrow/EscrowServiceValidation.cs`, line 284
```csharp
FailureType = f.FailureType.ToString(),
```

Per T25, `.ToString()` should only be used when populating event models that have `string` fields for cross-language compatibility. This is the acceptable boundary conversion pattern documented in T25 under "Event Model Conversions". **These are borderline acceptable** if the event schema genuinely requires string types for these fields, but should be verified against the event schemas. If the event models could use enum types instead, they should.

**Note**: This is the weakest violation -- T25 explicitly allows `.ToString()` at event boundaries. Verify that the event schema intentionally uses string types for these fields rather than enum references.

---

#### 9. IMPLEMENTATION TENETS (T25) - Enum.TryParse in Business Logic

**File**: `plugins/lib-escrow/EscrowServiceValidation.cs`, line 220
```csharp
AssetType = Enum.TryParse<AssetType>(f.AssetType, out var at) ? at : AssetType.Custom,
```

This parses a string `f.AssetType` (from a `ValidationFailure` API model) into an enum within business logic. Per T25, enum parsing belongs only at system boundaries. The `ValidationFailure` model's `AssetType` field should already be an enum type if it is an internal model, or this conversion should happen at the deserialization boundary.

**Fix**: If `ValidationFailure.AssetType` is a generated API model with string type, this is acceptable as a boundary conversion. If it could be changed to use the enum type in the schema, prefer that. Verify the schema definition.

---

#### 10. CLAUDE.md - Multiple `?? string.Empty` Without Justifying Comments

**File**: `plugins/lib-escrow/EscrowServiceCompletion.cs`, lines 119, 251
```csharp
.FirstOrDefault(a => a.RecipientPartyId == r.RecipientPartyId)?.RecipientPartyType ?? string.Empty,
.FirstOrDefault(d => d.PartyId == r.DepositorPartyId)?.PartyType ?? string.Empty,
```

These use `?? string.Empty` without the required explanatory comment. Per CLAUDE.md, this pattern is only acceptable in two documented scenarios (compiler satisfaction where null can never execute, or external service defensive coding with error logging). Here the `FirstOrDefault` can genuinely return null (making the coalesce reachable), so this silently converts a potentially-missing party type to empty string, hiding a potential data integrity issue.

**Fix**: Either throw an exception if the party is expected to always exist (programming error if not found), or add error logging when null is encountered, or restructure to avoid the coalesce.

---

#### 11. CLAUDE.md - `= string.Empty` on String Properties in Internal POCOs

**File**: `plugins/lib-escrow/EscrowService.cs`, lines 648, 666, 688, 703, 707, 744, 753, 764, 781, 784, 793, 838

Multiple internal POCO properties use `= string.Empty` as default:
```csharp
public string CreatedByType { get; set; } = string.Empty;
public string PartyType { get; set; } = string.Empty;
public string SourceOwnerType { get; set; } = string.Empty;
// etc.
```

Per CLAUDE.md general rule: "Avoid `?? string.Empty` as it hides bugs by silently coercing null to empty string." The same principle applies to `= string.Empty` defaults on properties that represent meaningful values. If these types can legitimately be empty, they should be nullable (`string?`). If they should always have a value, they should throw on access or be validated at population time.

**Note**: This is a judgment call -- `= string.Empty` on POCO properties is a common C# pattern to satisfy NRT, but per the project's strict null-safety philosophy, these fields likely represent required data (e.g., `PartyType` should always be set). Consider making them `string?` or validating at assignment.

---

#### 12. IMPLEMENTATION TENETS (T3) - Event Consumer Registration Not Called From Constructor

**File**: `plugins/lib-escrow/EscrowService.cs`, constructor (lines 197-209)

The constructor does not inject `IEventConsumer` or call `RegisterEventConsumers()`. While the current `RegisterEventConsumers()` body is empty, the schema defines subscriptions to `contract.fulfilled` and `contract.terminated` events. Per T3/T6, the constructor MUST accept `IEventConsumer`, null-check it, and call `RegisterEventConsumers(eventConsumer)` -- even if the method body is currently empty -- to maintain the pattern for when handlers are implemented.

**Fix**: Add `IEventConsumer eventConsumer` to constructor, add null check, call `RegisterEventConsumers(eventConsumer)`.

---

#### 13. IMPLEMENTATION TENETS (T21) - Hardcoded ListEscrows Default Limit

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`, line 277
```csharp
var limit = body.Limit ?? 50;
```

Hardcoded default limit of 50 for pagination. Per T21, tunables must be configuration properties.

**Fix**: Add a configuration property like `DefaultListLimit` and use `_configuration.DefaultListLimit` here.

---

#### 14. FOUNDATION TENETS (T6) - Missing IDistributedLockProvider Dependency

**File**: `plugins/lib-escrow/EscrowService.cs`, constructor

The service does not inject `IDistributedLockProvider` despite having multiple operations that perform non-atomic read-modify-write on the `PartyPendingStore`. Per T9, services with cross-instance coordination requirements should use `IDistributedLockProvider`.

**Fix**: Add `IDistributedLockProvider` to the constructor and use it to protect party pending count modifications.

---

#### 15. IMPLEMENTATION TENETS (T5) - EscrowExpiredEvent Never Published

The `EscrowTopics.EscrowExpired` constant is defined and the event type exists in the schema, but no code ever publishes this event. While this is noted in "Stubs & Unimplemented Features", T5 requires that all meaningful state changes publish events. The `Expired` state exists in the state machine but no code path transitions to it or publishes the corresponding event.

**Fix**: Implement the expiration background processor that transitions expired escrows and publishes `EscrowExpiredEvent`, or remove the expired state/event from the schema if not yet needed.

---

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
