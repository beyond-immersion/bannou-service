# Escrow Implementation Map

> **Plugin**: lib-escrow
> **Schema**: schemas/escrow-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/ESCROW.md](../plugins/ESCROW.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-escrow |
| Layer | L4 GameFeatures |
| Endpoints | 23 (23 generated) |
| State Stores | escrow-agreements (MySQL), escrow-handler-registry (MySQL), escrow-tokens (Redis), escrow-idempotency (Redis), escrow-status-index (Redis), escrow-party-pending (Redis), escrow-active-validation (Redis) |
| Events Published | 15 (escrow.created, escrow.deposit.received, escrow.funded, escrow.consent.received, escrow.finalizing, escrow.releasing, escrow.released, escrow.refunding, escrow.refunded, escrow.disputed, escrow.resolved, escrow.expired, escrow.cancelled, escrow.validation.failed, escrow.validation.reaffirmed) |
| Events Consumed | 3 |
| Client Events | 0 |
| Background Services | 3 |

---

## State

**Store**: `escrow-agreements` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `agreement:{escrowId}` | `EscrowAgreementModel` | Full escrow agreement with parties, deposits, consents, allocations, validation failures |

**Store**: `escrow-handler-registry` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `handler:{assetType}` | `AssetHandlerModel` | Custom asset type handler registration (plugin, endpoints) |

**Store**: `escrow-tokens` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `token:{sha256Hash}` | `TokenHashModel` | Token hash to escrow/party binding for deposit/release token validation |

**Store**: `escrow-idempotency` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `idemp:{idempotencyKey}` | `IdempotencyRecord` | Cached deposit responses for deduplication (TTL: IdempotencyTtlHours) |

**Store**: `escrow-status-index` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `status:{statusEnum}:{escrowId}` | `StatusIndexEntry` | Index of escrow IDs per status for background worker scanning |

**Store**: `escrow-party-pending` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `party:{partyType}:{partyId}` | `PartyPendingCount` | Number of active escrows per party for MaxPendingPerParty rate limiting |

**Store**: `escrow-active-validation` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `validate:{escrowId}` | `ValidationTrackingEntry` | Validation attempt history and failure count |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 7 state stores: agreements (MySQL), handlers (MySQL), tokens, idempotency, status index, party pending, validation (Redis) |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 15 escrow lifecycle events; error events via TryPublishErrorAsync |
| lib-messaging (IEventConsumer) | L0 | Hard | Subscribing to account.deleted, contract.fulfilled, contract.terminated |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helper methods and event handlers |
| lib-resource (IResourceClient) | L1 | Hard | x-references character cleanup: register/unregister character-escrow references, CleanupByCharacter callback |
| lib-currency (ICurrencyClient) | L2 | Hard | ValidateEscrowAsync: verify currency deposits (wallet/balance existence) |
| lib-item (IItemClient) | L2 | Hard | ValidateEscrowAsync: verify item/item-stack deposits (instance existence) |
| lib-contract (IContractClient) | L1 | Hard | ValidateEscrowAsync: verify contract deposits; DepositAsync: lock contracts as escrow assets |
| lib-inventory (IInventoryClient) | L2 | Hard | DepositAsync: transfer items to escrow container (and future release/refund transfers) |
| lib-mesh (IMeshInvocationClient) | L0 | Hard | Custom handler endpoint invocation for validation and deposits (and future release/refund) |

**Notes**:
- Account cleanup uses event subscription (`account.deleted`) per T28 Account Deletion Cleanup Obligation — not lib-resource
- Character cleanup uses lib-resource with CASCADE policy via `x-references`
- ICurrencyClient, IItemClient, IContractClient are used for asset validation in ValidateEscrowAsync (and future asset transfer in deposit/release/refund flows per #153)
- IStateStoreFactory is used only in constructor to acquire stores; not stored as a field

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `escrow.created` | `EscrowCreatedEvent` | CreateEscrow success |
| `escrow.deposit.received` | `EscrowDepositReceivedEvent` | Deposit success |
| `escrow.funded` | `EscrowFundedEvent` | Deposit when all required deposits fulfilled |
| `escrow.consent.received` | `EscrowConsentReceivedEvent` | RecordConsent success |
| `escrow.finalizing` | `EscrowFinalizingEvent` | RecordConsent (all consents met), VerifyCondition (condition met), HandleContractFulfilledAsync |
| `escrow.releasing` | `EscrowReleasingEvent` | Release (party confirmation required path) |
| `escrow.released` | `EscrowReleasedEvent` | Release (direct path), ConfirmRelease (all confirmed), EscrowConfirmationTimeoutService (auto-confirm) |
| `escrow.refunding` | `EscrowRefundingEvent` | RecordConsent (Refund consent from consent-required party) |
| `escrow.refunded` | `EscrowRefundedEvent` | Refund, HandleContractTerminatedAsync, ConfirmRefund (all confirmed), EscrowExpirationService (auto-refund), EscrowConfirmationTimeoutService (timeout refund) |
| `escrow.disputed` | `EscrowDisputedEvent` | Dispute, RecordConsent (Dispute consent), EscrowConfirmationTimeoutService (timeout dispute) |
| `escrow.resolved` | `EscrowResolvedEvent` | Resolve success |
| `escrow.expired` | `EscrowExpiredEvent` | EscrowExpirationService (past expiry + grace period) |
| `escrow.cancelled` | `EscrowCancelledEvent` | Cancel success |
| `escrow.validation.failed` | `EscrowValidationFailedEvent` | VerifyCondition (condition not met), ValidateEscrow (failures found — currently stubbed) |
| `escrow.validation.reaffirmed` | `EscrowValidationReaffirmedEvent` | Reaffirm (all affected parties reaffirmed) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Queries agreements where account is a party, calls CleanupSingleAgreementAsync per agreement: deletes tokens, status index, validation tracking, party pending counts, agreement record. Per-agreement error isolation. |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Queries agreements by BoundContractId, transitions from PendingCondition to Finalizing via ETag retry, publishes escrow.finalizing |
| `contract.terminated` | `HandleContractTerminatedAsync` | Queries agreements by BoundContractId, transitions from PendingCondition/ValidationFailed/PendingConsent to Refunded via ETag retry, publishes escrow.refunded |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<EscrowService>` | Structured logging |
| `EscrowServiceConfiguration` | All 21 config properties |
| `IStateStoreFactory` | Constructor-only: acquires 7 state stores |
| `IMessageBus` | Event publishing (15 topics) and error events |
| `IEventConsumer` | Registers 3 event handlers (account.deleted, contract.fulfilled, contract.terminated) |
| `ITelemetryProvider` | Span instrumentation for async helpers and event handlers |
| `IResourceClient` | Character reference tracking via x-references |
| `ICurrencyClient` | Asset validation: verify currency deposits (wallet/balance existence) |
| `IItemClient` | Asset validation: verify item/item-stack deposits (instance existence) |
| `IContractClient` | Asset validation + contract locking during deposits |
| `IInventoryClient` | Item transfers to escrow container during deposits |
| `IMeshInvocationClient` | Custom handler endpoint invocation via mesh |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateEscrow | POST /escrow/create | generated | [] | agreement, tokens, status-index, party-pending | escrow.created |
| GetEscrow | POST /escrow/get | generated | user | - | - |
| ListEscrows | POST /escrow/list | generated | user | - | - |
| GetMyToken | POST /escrow/get-my-token | generated | [] | - | - |
| Deposit | POST /escrow/deposit | generated | user | agreement, tokens, idempotency, status-index | escrow.deposit.received, escrow.funded |
| ValidateDeposit | POST /escrow/deposit/validate | generated | user | - | - |
| GetDepositStatus | POST /escrow/deposit/status | generated | user | - | - |
| RecordConsent | POST /escrow/consent | generated | user | agreement, tokens, status-index | escrow.consent.received, escrow.finalizing, escrow.disputed, escrow.refunding |
| GetConsentStatus | POST /escrow/consent/status | generated | user | - | - |
| Release | POST /escrow/release | generated | [] | agreement, status-index, party-pending | escrow.releasing, escrow.released |
| Refund | POST /escrow/refund | generated | [] | agreement, status-index, party-pending | escrow.refunded |
| Cancel | POST /escrow/cancel | generated | [] | agreement, status-index, party-pending | escrow.cancelled |
| Dispute | POST /escrow/dispute | generated | user | agreement, status-index | escrow.disputed |
| ConfirmRelease | POST /escrow/confirm-release | generated | user | agreement, status-index, party-pending | escrow.released |
| ConfirmRefund | POST /escrow/confirm-refund | generated | user | agreement, status-index, party-pending | escrow.refunded |
| Resolve | POST /escrow/resolve | generated | [] | agreement, status-index, party-pending | escrow.resolved |
| VerifyCondition | POST /escrow/verify-condition | generated | [] | agreement, validation, status-index | escrow.finalizing, escrow.validation.failed |
| ValidateEscrow | POST /escrow/validate | generated | [] | agreement, validation, status-index | escrow.validation.failed |
| Reaffirm | POST /escrow/reaffirm | generated | user | agreement, validation, status-index | escrow.validation.reaffirmed |
| RegisterHandler | POST /escrow/handler/register | generated | [] | handler | - |
| ListHandlers | POST /escrow/handler/list | generated | [] | - | - |
| DeregisterHandler | POST /escrow/handler/deregister | generated | [] | handler | - |
| CleanupByCharacter | POST /escrow/cleanup-by-character | generated | [] | agreement, tokens, status-index, party-pending, validation | - |

---

## Methods

### CreateEscrow
POST /escrow/create | Roles: []

```
// Validate request
IF parties.Count < 2 OR parties.Count > config.MaxParties          -> 400
IF expectedDeposits is empty                                        -> 400
IF trustMode == SinglePartyTrusted AND trustedPartyId is null       -> 400
IF requested duration > config.MaxTimeout                           -> 400

// Check party pending limits
FOREACH party in request.Parties
  READ party-pending:party:{partyType}:{partyId}
  IF count >= config.MaxPendingPerParty                             -> 400

// Build agreement model
// ExpiresAt defaults to now + config.DefaultTimeout if not specified
// ReleaseMode/RefundMode default from config if not in request

WRITE escrow-agreements:agreement:{escrowId} <- EscrowAgreementModel (status: PendingDeposits)

// Generate tokens (FullConsent mode only)
IF trustMode == FullConsent
  FOREACH party with expected deposits
    // Generate deposit token: SHA-256(random bytes + context)
    WRITE escrow-tokens:token:{depositTokenHash} <- TokenHashModel
  FOREACH party where consentRequired
    // Generate release token: same process
    WRITE escrow-tokens:token:{releaseTokenHash} <- TokenHashModel

WRITE escrow-status-index:status:PendingDeposits:{escrowId} <- StatusIndexEntry

// Increment pending counts (with compensation on failure)
FOREACH party
  ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}  // IncrementPartyPendingCountAsync, retry up to MaxConcurrencyRetries
  // On failure: decrement all already-incremented parties, re-throw

PUBLISH escrow.created { escrowId, escrowType, trustMode, parties, expectedDepositCount, expiresAt, boundContractId, referenceType, referenceId, createdAt }
RETURN (200, CreateEscrowResponse { escrow, depositTokens })
```

### GetEscrow
POST /escrow/get | Roles: [user]

```
READ escrow-agreements:agreement:{escrowId}                         -> 404 if null
RETURN (200, GetEscrowResponse { escrow })
```

### ListEscrows
POST /escrow/list | Roles: [user]

```
// pageSize defaults to config.DefaultListLimit, offset defaults to 0
QUERY escrow-agreements WHERE combined predicates ORDER BY CreatedAt DESC PAGED(page, pageSize)
  // Filter branches: partyId+status, partyId-only, status-only, unfiltered
RETURN (200, ListEscrowsResponse { escrows, totalCount })
```

### GetMyToken
POST /escrow/get-my-token | Roles: []

```
READ escrow-agreements:agreement:{escrowId}                         -> 404 if null
// Find party by ownerId + ownerType                                -> 404 if not found
// Switch on tokenType: Deposit → party.DepositToken, Release → party.ReleaseToken
IF token is null                                                    -> 404
RETURN (200, GetMyTokenResponse { token, tokenUsed, tokenUsedAt })
```

### Deposit
POST /escrow/deposit | Roles: [user]

```
// Idempotency check (outside retry loop)
READ escrow-idempotency:idemp:{idempotencyKey}
IF exists AND same escrowId+partyId
  RETURN (200, cached DepositResponse)
IF exists AND different escrowId or partyId                         -> 400

// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not PendingDeposits or PartiallyFunded                    -> 400
IF expired                                                          -> 400
// Find party                                                       -> 404 if not found

IF trustMode == FullConsent
  IF depositToken not provided                                      -> 400
  IF party already deposited                                        -> 400
  READ escrow-tokens:token:{hash(depositToken)}
  IF not found or wrong escrow/party/type                           -> 401
  IF already used                                                   -> 400

IF assets.Count > config.MaxAssetsPerDeposit                        -> 400

// Execute asset transfers BEFORE recording deposit (reject cleanly on failure)
FOREACH asset in deposit.Assets
  SWITCH asset.AssetType
    Currency: CALL ICurrencyClient.EscrowDepositAsync(walletId, currencyDefId, amount, escrowId, idempotencyKey)
    Item/ItemStack: CALL IInventoryClient.TransferItemAsync(instanceId, escrowContainerId, quantity?)
    Contract: CALL IContractClient.LockContractAsync(contractInstanceId, guardianId=escrowId, guardianType="escrow")
    Custom: CALL handler.DepositEndpoint via IMeshInvocationClient
  IF any transfer fails → 400 (deposit rejected)

// Create deposit, mark expected deposit as fulfilled
// Determine new status: all required fulfilled → Funded, else PartiallyFunded
ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

// Deferred token marking (after agreement save)
IF FullConsent mode
  WRITE escrow-tokens:token:{tokenHash} <- mark as used

// Update status index (if status changed)
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:{newStatus}:{escrowId} <- StatusIndexEntry

PUBLISH escrow.deposit.received { escrowId, partyId, partyType, depositId, assetSummary, depositsReceived, depositsExpected, fullyFunded, depositedAt }

IF fullyFunded
  PUBLISH escrow.funded { escrowId, totalDeposits, fundedAt }
  // Initialize periodic validation tracking
  WRITE escrow-active-validation:validate:{escrowId} <- { NextValidationDue: now + config.ValidationCheckInterval, LastValidatedAt: null }

// Cache result for idempotent replay
WRITE escrow-idempotency:idemp:{idempotencyKey} <- IdempotencyRecord (TTL: IdempotencyTtlHours)

RETURN (200, DepositResponse { escrow, deposit, fullyFunded, releaseTokens })
// releaseTokens populated only when fullyFunded
```

### ValidateDeposit
POST /escrow/deposit/validate | Roles: [user]

```
READ escrow-agreements:agreement:{escrowId}                         -> 404 if null
// Validate: status, expiration, party existence, already deposited
// Collect errors and warnings (no state writes)
RETURN (200, ValidateDepositResponse { valid, errors, warnings })
```

### GetDepositStatus
POST /escrow/deposit/status | Roles: [user]

```
READ escrow-agreements:agreement:{escrowId}                         -> 404 if null
// Find party, collect expected/deposited assets, fulfillment status
RETURN (200, GetDepositStatusResponse { expectedAssets, depositedAssets, fulfilled, depositToken, depositDeadline })
```

### RecordConsent
POST /escrow/consent | Roles: [user]

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not Funded/PendingConsent/PendingCondition                -> 400
IF expired                                                          -> 400
// Find party                                                       -> 404 if not found

IF trustMode == FullConsent AND consentType == Release
  IF releaseToken not provided                                      -> 400
  IF party.ReleaseTokenUsed                                         -> 400
  READ escrow-tokens:token:{hash(releaseToken)}
  IF not found or wrong escrow/party/type                           -> 401
  IF already used                                                   -> 400

IF party already consented with same consentType                    -> 400

// Record consent, determine state transition
IF consentType == Release
  // Count release consents; if >= requiredConsentsForRelease:
  //   BoundContractId → PendingCondition; else → Finalizing
  // If not yet at threshold and status was Funded → PendingConsent
IF consentType == Refund AND party.ConsentRequired
  // → Refunding; set ConfirmationDeadline if RefundMode != Immediate
IF consentType == Dispute
  // → Disputed

ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

// Deferred token marking (FullConsent + Release only)
IF FullConsent AND Release
  WRITE escrow-tokens:token:{tokenHash} <- mark as used

// Update status index (if status changed)
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:{newStatus}:{escrowId} <- StatusIndexEntry

PUBLISH escrow.consent.received { escrowId, partyId, partyType, consentType, consentsReceived, consentsRequired, consentedAt }

IF transitioned to Finalizing
  PUBLISH escrow.finalizing { escrowId, boundContractId, finalizerCount: 0, startedAt }
IF transitioned to Disputed
  PUBLISH escrow.disputed { escrowId, disputedBy, disputedByType, reason, disputedAt }
IF transitioned to Refunding
  PUBLISH escrow.refunding { escrowId, refundMode, deposits, reason }

RETURN (200, ConsentResponse { escrow, triggered, newStatus })
```

### GetConsentStatus
POST /escrow/consent/status | Roles: [user]

```
READ escrow-agreements:agreement:{escrowId}                         -> 404 if null
// Build consent status for parties where consentRequired == true
// Count release consents vs required
RETURN (200, GetConsentStatusResponse { partiesRequiringConsent, consentsReceived, consentsRequired, canRelease, canRefund })
```

### Release
POST /escrow/release | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not Finalizing and not Releasing                          -> 400

// Path A: Finalizing → Releasing (party confirmation required)
IF status == Finalizing AND releaseMode in [PartyRequired, ServiceAndParty]
  // Initialize ReleaseConfirmations for each allocation recipient
  // Set ConfirmationDeadline = now + config.ConfirmationTimeoutSeconds
  ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Releasing)
  DELETE escrow-status-index:status:Finalizing:{escrowId}
  WRITE escrow-status-index:status:Releasing:{escrowId} <- StatusIndexEntry
  PUBLISH escrow.releasing { escrowId, releaseMode, allocations, confirmationDeadline, boundContractId }
  RETURN (200, ReleaseResponse { escrow, finalizerResults: [], releases: [] })

// Path B: Finalizing → Released (immediate/service-only) or Releasing → Released
ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Released, resolution: Released, completedAt)
                                                                    -> 409 if retries exhausted
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:Released:{escrowId} <- StatusIndexEntry

FOREACH party in agreement
  ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}      // DecrementPartyPendingCountAsync

PUBLISH escrow.released { escrowId, recipients, resolution: Released, completedAt }
RETURN (200, ReleaseResponse { escrow, finalizerResults: [], releases })
```

### Refund
POST /escrow/refund | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not in [Refunding, ValidationFailed, Disputed, PartiallyFunded, PendingDeposits] -> 400

// Build refund results from actual deposits
ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Refunded, resolution, completedAt)
                                                                    -> 409 if retries exhausted
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:Refunded:{escrowId} <- StatusIndexEntry

FOREACH party in agreement
  ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}      // DecrementPartyPendingCountAsync

PUBLISH escrow.refunded { escrowId, depositors, reason, resolution: Refunded, completedAt }
RETURN (200, RefundResponse { escrow, refunds })
```

### Cancel
POST /escrow/cancel | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not PendingDeposits and not PartiallyFunded               -> 400

// Build refund results from any partial deposits
ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Cancelled, resolution: CancelledRefunded, completedAt)
                                                                    -> 409 if retries exhausted
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:Cancelled:{escrowId} <- StatusIndexEntry

FOREACH party in agreement
  ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}      // DecrementPartyPendingCountAsync

PUBLISH escrow.cancelled { escrowId, reason, depositsRefunded, cancelledAt }
RETURN (200, CancelResponse { escrow, refunds })
```

### Dispute
POST /escrow/dispute | Roles: [user]

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status not in [Funded, PendingConsent, PendingCondition, Finalizing] -> 400
IF partyId not found in agreement parties                           -> 403

// Add Dispute consent record
ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Disputed)
                                                                    -> 409 if retries exhausted
DELETE escrow-status-index:status:{previousStatus}:{escrowId}
WRITE escrow-status-index:status:Disputed:{escrowId} <- StatusIndexEntry

PUBLISH escrow.disputed { escrowId, disputedBy, disputedByType, reason, disputedAt }
RETURN (200, DisputeResponse { escrow })
```

### ConfirmRelease
POST /escrow/confirm-release | Roles: [user]

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status != Releasing                                              -> 409
// Find party                                                       -> 404 if not found
IF party.ReleaseToken != body.ReleaseToken                          -> 403
// Find ReleaseConfirmation for party                               -> 400 if not found
IF already confirmed                                                -> 200 (idempotent)

// Mark PartyConfirmed = true
// Check if all confirmations complete (per ReleaseMode)
ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

IF allPartiesConfirmed
  // Transition to Released
  DELETE escrow-status-index:status:Releasing:{escrowId}
  WRITE escrow-status-index:status:Released:{escrowId} <- StatusIndexEntry
  FOREACH party
    ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}    // DecrementPartyPendingCountAsync
  PUBLISH escrow.released { escrowId, recipients, resolution: Released, completedAt }

RETURN (200, ConfirmReleaseResponse { escrowId, confirmed: true, allPartiesConfirmed, status })
```

### ConfirmRefund
POST /escrow/confirm-refund | Roles: [user]

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status != Refunding                                              -> 409
// Find party                                                       -> 404 if not found
// Find ReleaseConfirmation for party                               -> 400 if not found
IF already confirmed                                                -> 200 (idempotent)

// Mark PartyConfirmed = true
// Check if all refund confirmations complete (per RefundMode)
ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

IF allPartiesConfirmed
  // Transition to Refunded
  DELETE escrow-status-index:status:Refunding:{escrowId}
  WRITE escrow-status-index:status:Refunded:{escrowId} <- StatusIndexEntry
  FOREACH party
    ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}    // DecrementPartyPendingCountAsync
  PUBLISH escrow.refunded { escrowId, depositors, resolution: Refunded, completedAt }

RETURN (200, ConfirmRefundResponse { escrowId, confirmed: true, allPartiesConfirmed, status })
```

### Resolve
POST /escrow/resolve | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status != Disputed                                               -> 400
IF arbiterId not a party with Arbiter role                          -> 403

// Resolution switch:
//   Released → status=Released, transfers from ReleaseAllocations
//   Refunded → status=Refunded, transfers from Deposits
//   Split → status=Released, transfers from body.SplitAllocations
ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

DELETE escrow-status-index:status:Disputed:{escrowId}
WRITE escrow-status-index:status:{resolvedStatus}:{escrowId} <- StatusIndexEntry

FOREACH party
  ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}      // DecrementPartyPendingCountAsync

PUBLISH escrow.resolved { escrowId, arbiterId, arbiterType, resolution, notes, resolvedAt }
RETURN (200, ResolveResponse { escrow, transfers })
```

### VerifyCondition
POST /escrow/verify-condition | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF no BoundContractId                                               -> 400
IF status not PendingCondition and not ValidationFailed             -> 400

IF conditionMet
  // Clear validation failures, transition to Finalizing
  ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Finalizing)
  READ escrow-active-validation:validate:{escrowId}
  WRITE escrow-active-validation:validate:{escrowId} <- reset tracking
  DELETE escrow-status-index:status:{previousStatus}:{escrowId}
  WRITE escrow-status-index:status:Finalizing:{escrowId} <- StatusIndexEntry
  PUBLISH escrow.finalizing { escrowId, boundContractId, finalizerCount: 0, startedAt }
ELSE
  // Append validation failure, transition to ValidationFailed
  ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: ValidationFailed)
  READ escrow-active-validation:validate:{escrowId}
  WRITE escrow-active-validation:validate:{escrowId} <- increment failure count
  DELETE escrow-status-index:status:{previousStatus}:{escrowId}
  WRITE escrow-status-index:status:ValidationFailed:{escrowId} <- StatusIndexEntry
  PUBLISH escrow.validation.failed { escrowId, failures, detectedAt }

RETURN (200, VerifyConditionResponse { escrow, triggered })
```

### ValidateEscrow
POST /escrow/validate | Roles: []

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status is terminal                                               -> 400

// Per-asset-type validation via direct service calls
FOREACH deposit in agreement.Deposits
  FOREACH asset in deposit.Assets
    TRY
      SWITCH asset.AssetType
        Currency:
          CALL ICurrencyClient.GetBalanceAsync(deposit party WalletId, asset.CurrencyDefinitionId)
          IF ApiException(404) → failure (wallet or currency missing)
        Item / ItemStack:
          CALL IItemClient.GetItemInstanceAsync(asset.ItemInstanceId)
          IF ApiException(404) → failure (item no longer exists)
        Contract:
          CALL IContractClient.GetContractInstanceAsync(asset.ContractInstanceId)
          IF ApiException(404) → failure (contract no longer exists)
          IF contract status is terminal (Fulfilled/Terminated/Expired) → failure
        Custom:
          READ handler from escrow-handler-registry:handler:{asset.CustomAssetType}
          IF handler exists → invoke handler.ValidateEndpoint via lib-mesh (skip if fails)
          IF no handler → skip (log Warning)
    CATCH ApiException (non-404) → skip asset (service error, retry next cycle)
    CATCH Exception → skip asset (service unavailable, retry next cycle)

ETAG-WRITE escrow-agreements:agreement:{escrowId} (LastValidatedAt updated)
                                                                    -> 409 if retries exhausted
READ escrow-active-validation:validate:{escrowId}
WRITE escrow-active-validation:validate:{escrowId} <- update tracking

IF failures not empty  // (currently never true)
  DELETE escrow-status-index:status:{previousStatus}:{escrowId}
  WRITE escrow-status-index:status:ValidationFailed:{escrowId} <- StatusIndexEntry
  PUBLISH escrow.validation.failed { escrowId, failures, detectedAt }

RETURN (200, ValidateEscrowResponse { valid, escrow, failures })
```

### Reaffirm
POST /escrow/reaffirm | Roles: [user]

```
// ETag retry loop (up to MaxConcurrencyRetries)
READ escrow-agreements:agreement:{escrowId} [with ETag]             -> 404 if null
IF status != ValidationFailed                                       -> 400
// Find party                                                       -> 404 if not found

// Add Reaffirm consent record
// Check if all affected parties (from ValidationFailures) have reaffirmed
ETAG-WRITE escrow-agreements:agreement:{escrowId}                   -> 409 if retries exhausted

IF allReaffirmed
  // Clear validation failures, transition to PendingCondition
  READ escrow-active-validation:validate:{escrowId}
  WRITE escrow-active-validation:validate:{escrowId} <- reset failure count
  DELETE escrow-status-index:status:ValidationFailed:{escrowId}
  WRITE escrow-status-index:status:PendingCondition:{escrowId} <- StatusIndexEntry
  PUBLISH escrow.validation.reaffirmed { escrowId, reaffirmedBy, reaffirmedByType, allReaffirmed: true, reaffirmedAt }

RETURN (200, ReaffirmResponse { escrow, allReaffirmed })
```

### RegisterHandler
POST /escrow/handler/register | Roles: []

```
READ escrow-handler-registry:handler:{assetType}
IF exists                                                           -> 400
WRITE escrow-handler-registry:handler:{assetType} <- AssetHandlerModel (builtIn: false)
RETURN (200, RegisterHandlerResponse {})
```

### ListHandlers
POST /escrow/handler/list | Roles: []

```
QUERY escrow-handler-registry WHERE true  // all handlers, no pagination
RETURN (200, ListHandlersResponse { handlers })
```

### DeregisterHandler
POST /escrow/handler/deregister | Roles: []

```
READ escrow-handler-registry:handler:{assetType}                    -> 404 if null
IF handler.BuiltIn                                                  -> 400
DELETE escrow-handler-registry:handler:{assetType}
RETURN (200, DeregisterHandlerResponse {})
```

### CleanupByCharacter
POST /escrow/cleanup-by-character | Roles: []

```
QUERY escrow-agreements WHERE parties contains (characterId, EntityType.Character)

FOREACH agreement (per-item error isolation)
  // CleanupSingleAgreementAsync:
  FOREACH party with deposit token
    DELETE escrow-tokens:token:{depositTokenHash}
  FOREACH party with release token
    DELETE escrow-tokens:token:{releaseTokenHash}
  FOREACH party
    ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}    // DecrementPartyPendingCountAsync
  DELETE escrow-status-index:status:{status}:{escrowId}
  DELETE escrow-active-validation:validate:{escrowId}
  DELETE escrow-agreements:agreement:{escrowId}

RETURN (200, CleanupByCharacterResponse { agreementsDeleted })
```

### Internal Helper: ExecuteReleaseTransfersAsync
Called AFTER agreement transitions to Released. Per-allocation, per-asset error isolation.

```
FOREACH allocation in agreement.ReleaseAllocations (per-item error isolation)
  FOREACH asset in allocation.Assets
    TRY
      SWITCH asset.AssetType
        Currency: CALL ICurrencyClient.EscrowReleaseAsync(recipientWalletId, currencyDefId, amount, escrowId, idempotencyKey)
        Item/ItemStack: CALL IInventoryClient.TransferItemAsync(instanceId, destinationContainerId, quantity?)
        Contract: CALL IContractClient.UnlockContractAsync(contractInstanceId, guardianId=escrowId)
        Custom: CALL handler.ReleaseEndpoint via IMeshInvocationClient
    CATCH → log Warning, continue (FSM state is authoritative; Disputed handles failures)
```

### Internal Helper: ExecuteRefundTransfersAsync
Called AFTER agreement transitions to Refunded/Cancelled/Expired. Per-deposit, per-asset error isolation.

```
FOREACH deposit in agreement.Deposits (per-item error isolation)
  // Find the depositing party to get source wallet/container
  FOREACH asset in deposit.Assets
    TRY
      SWITCH asset.AssetType
        Currency: CALL ICurrencyClient.EscrowRefundAsync(partyWalletId, currencyDefId, amount, escrowId, idempotencyKey)
        Item/ItemStack: CALL IInventoryClient.TransferItemAsync(instanceId, sourceContainerId, quantity?)
        Contract: CALL IContractClient.UnlockContractAsync(contractInstanceId, guardianId=escrowId)
        Custom: CALL handler.RefundEndpoint via IMeshInvocationClient
    CATCH → log Warning, continue (FSM state is authoritative)
```

---

## Background Services

### EscrowExpirationService
**Interval**: `config.ExpirationCheckInterval` (ISO 8601 duration, default PT1M)
**Startup delay**: `config.ExpirationStartupDelaySeconds` (default 20s)
**Purpose**: Finds escrows past their expiry + grace period and transitions them to Expired

```
QUERY escrow-agreements WHERE status in [PendingDeposits, PartiallyFunded, PendingConsent, PendingCondition]
  AND ExpiresAt <= now - config.ExpirationGracePeriod
  PAGED(1, config.ExpirationBatchSize)

FOREACH agreement (per-item error isolation)
  READ escrow-agreements:agreement:{escrowId} [with ETag]
  // Verify still in expirable state (double-check after re-read)
  ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Expired, resolution: hasDeposits ? ExpiredRefunded : Expired)
  DELETE escrow-status-index:status:{previousStatus}:{escrowId}
  WRITE escrow-status-index:status:Expired:{escrowId} <- StatusIndexEntry
  FOREACH party
    ETAG-WRITE escrow-party-pending:party:{partyType}:{partyId}    // DecrementPartyPendingCountAsync
  PUBLISH escrow.expired { escrowId, status, autoRefunded, expiredAt }
  IF hasDeposits
    PUBLISH escrow.refunded { escrowId, depositors, reason: "Expired", resolution: ExpiredRefunded, completedAt }
```

### EscrowConfirmationTimeoutService
**Interval**: `config.ConfirmationTimeoutCheckIntervalSeconds` (seconds, default 30s)
**Startup delay**: `config.ConfirmationStartupDelaySeconds` (default 15s)
**Purpose**: Finds Releasing/Refunding escrows with expired confirmation deadlines and applies timeout behavior

```
QUERY escrow-agreements WHERE status in [Releasing, Refunding]
  AND ConfirmationDeadline <= now
  PAGED(1, config.ConfirmationTimeoutBatchSize)

FOREACH agreement (per-item error isolation)
  READ escrow-agreements:agreement:{escrowId} [with ETag]
  // Verify still in Releasing/Refunding with expired deadline

  IF status == Releasing
    IF config.ConfirmationTimeoutBehavior == AutoConfirm
      IF all ServiceConfirmed
        // Auto-complete: transition to Released
        ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Released, resolution: Released)
        DELETE/WRITE status-index
        PUBLISH escrow.released { escrowId, recipients, resolution: Released, completedAt }
      ELSE
        // Services not confirmed: escalate to Disputed
        ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Disputed)
        DELETE/WRITE status-index
        PUBLISH escrow.disputed { escrowId, reason: "Confirmation timeout", disputedAt }
    ELSE IF config.ConfirmationTimeoutBehavior == Dispute
      ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Disputed)
      DELETE/WRITE status-index
      PUBLISH escrow.disputed { escrowId, reason: "Confirmation timeout", disputedAt }
    ELSE IF config.ConfirmationTimeoutBehavior == Refund
      ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Refunded, resolution: Refunded)
      DELETE/WRITE status-index
      PUBLISH escrow.refunded { escrowId, depositors, reason: "Confirmation timeout refund", completedAt }

  IF status == Refunding
    IF config.ConfirmationTimeoutBehavior == AutoConfirm
      // Auto-complete refund
      ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Refunded, resolution: Refunded)
      DELETE/WRITE status-index
      PUBLISH escrow.refunded { escrowId, depositors, completedAt }
    ELSE
      // Dispute or Refund behavior: complete the refund
      ETAG-WRITE escrow-agreements:agreement:{escrowId} (status: Refunded, resolution: Refunded)
      DELETE/WRITE status-index
      PUBLISH escrow.refunded { escrowId, depositors, completedAt }
```

### EscrowValidationService
**Interval**: `config.ValidationCheckInterval` (ISO 8601 duration, default PT5M)
**Startup delay**: `config.ValidationStartupDelaySeconds` (default 25s)
**Purpose**: Periodically validates that deposited assets still exist and are in expected state

```
QUERY escrow-agreements WHERE status in [Funded, PendingConsent, PendingCondition, Finalizing, Releasing]
  PAGED(1, config.ValidationBatchSize)

// Filter in-memory: only process where NextValidationDue <= now
// (NextValidationDue is on the ValidationTrackingEntry in escrow-active-validation store)

FOREACH agreement (per-item error isolation)
  READ escrow-active-validation:validate:{escrowId}
  IF tracking.NextValidationDue > now → skip

  READ escrow-agreements:agreement:{escrowId} [with ETag]
  // Verify still in validation-eligible state

  // Per-asset-type validation (same logic as ValidateEscrowAsync)
  FOREACH deposit in agreement.Deposits
    FOREACH asset in deposit.Assets
      TRY validate via ICurrencyClient / IItemClient / IContractClient / handler registry
      CATCH → skip asset (service unavailable, retry next cycle)

  IF failures found AND status != Releasing
    SET agreement.PreFailureStatus = current status
    SET agreement.Status = ValidationFailed
    ADD failures to agreement.ValidationFailures
    ETAG-WRITE escrow-agreements:agreement:{escrowId}
    DELETE/WRITE status-index
    PUBLISH escrow.validation.failed { escrowId, failures, detectedAt }

  // Update NextValidationDue regardless of outcome
  WRITE escrow-active-validation:validate:{escrowId} <- { NextValidationDue: now + interval, LastValidatedAt: now }
```

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 23 endpoints are schema-generated on the standard interface. Plugin lifecycle is trivial (`StandardServicePlugin<IEscrowService>`). No manual controllers, no MapPost/MapGet registrations, no custom IBannouService overrides.
