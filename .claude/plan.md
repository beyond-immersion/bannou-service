# Issue #214: Escrow State Machine Bug Fixes - Implementation Plan

## Issue Summary

**Issue**: #214 - [HIGH PRIORITY] Escrow State Machine Bugs - 3 Critical Issues
**Plugin**: lib-escrow
**Schemas**: `schemas/escrow-api.yaml`, `schemas/escrow-events.yaml`, `schemas/escrow-configuration.yaml`, `schemas/state-stores.yaml`

---

## Decisions Made (Developer Approved)

| Bug | Decision |
|-----|----------|
| Bug 1: Releasing state unreachable | Keep `Releasing`, implement **event-driven confirmation flow** (not two-phase saves) |
| Bug 2: Status index not used | USE IT - Convert to Redis Sets, implement queries in ListEscrowsAsync |
| Bug 3: Party pending count race | Fix with try/finally pattern with rollback |
| Bug 4: Dead configuration | Wire up MaxTimeout, MaxAssetsPerDeposit, MaxPendingPerParty; remove TokenAlgorithm/TokenSecret |

### Additional Design Decisions (from architecture discussion)

| Feature | Decision |
|---------|----------|
| Release confirmation mode | Add `ReleaseMode` enum: `immediate`, `service_only`, `party_required`, `service_and_party` |
| Refund confirmation mode | Add `RefundMode` enum with same options, configurable separately |
| Confirmation timeout behavior | Add `ConfirmationTimeoutBehavior` enum: `auto_confirm`, `dispute`, `refund` |
| Default ReleaseMode | `service_only` (safe default - wait for downstream service confirmation) |
| Default RefundMode | `immediate` (refunds are less contentious) |
| Confirmation timeout | Configurable, default to auto-confirm if services confirmed |
| Event-driven flow | `escrow.releasing` event with confirmation shortcuts → parties call `/confirm-release` |

### Contract-Bound vs Unbound Escrow Behavior

**Critical architectural distinction**: `ReleaseMode` only applies to **unbound escrows**. Contract-bound escrows follow a different flow where the contract is the "brain" and escrow is just the "vault".

| Scenario | Behavior | ReleaseMode Used? |
|----------|----------|-------------------|
| **Unbound escrow** (P2P trades, simple exchanges) | Escrow orchestrates release, publishes `escrow.releasing`, waits for confirmations per ReleaseMode | **YES** |
| **Contract-bound escrow** | Contract handles distribution via `ExecuteContract`, escrow verifies `contract.fulfilled` status then immediately transitions to Released | **NO** (effectively `immediate` after contract validation) |

**Rationale**: When a contract is bound, the contract has already done all the work - milestone progression, consent gathering, clause execution. The escrow's job is just custody. Adding a separate confirmation flow would create the illusion of choice when the contract has already decided the outcome.

**Implementation**:
```csharp
// In ReleaseAsync
if (agreementModel.BoundContractId != null)
{
    // Contract-bound: Verify contract is fulfilled, then immediate release
    // Contract has already handled all distribution via ExecuteContract
    var contractStatus = await _contractClient.GetContractInstanceStatusAsync(
        new GetContractInstanceStatusRequest { ContractInstanceId = agreementModel.BoundContractId.Value },
        cancellationToken);

    if (contractStatus?.Status != ContractStatus.Fulfilled)
    {
        _logger.LogWarning("Cannot release contract-bound escrow {EscrowId}: contract {ContractId} not fulfilled",
            escrowId, agreementModel.BoundContractId);
        return (StatusCodes.Conflict, null);
    }

    // Skip Releasing state - contract already handled distribution
    agreementModel.Status = EscrowStatus.Released;
    agreementModel.CompletedAt = now;
    await SaveAndPublishReleasedAsync(agreementModel, cancellationToken);
    return (StatusCodes.OK, new ReleaseResponse { ... });
}
else
{
    // Unbound: Use configured ReleaseMode with confirmation flow
    // ... existing ReleaseMode logic
}
```

**Related Contract Issues** (prerequisites for full integration):
- #217: T5 violation - ContractExecutedEvent not schema-defined
- #218: ContractExecutedEvent needs per-party distribution details

For now, we implement assuming these will be completed. The contract-bound path verifies `Fulfilled` status and trusts that contract execution succeeded.

---

## Phase 1: Schema Changes (T1 - Schema-First)

**ALL schema changes must happen BEFORE any code changes per T1.**

### Step 1.1: Update escrow-api.yaml - Add Enums

**File**: `schemas/escrow-api.yaml`

**Add new enums to `components/schemas`**:

```yaml
ReleaseMode:
  type: string
  enum:
    - immediate
    - service_only
    - party_required
    - service_and_party
  description: |
    Controls how release confirmation is handled:
    - immediate: Finalizing → Released (skip Releasing state entirely).
      ⚠️ WARNING: Use only for trusted/low-value scenarios (NPC vendors, system rewards).
      Assets are marked as released BEFORE downstream services confirm transfers.
      If downstream services fail, manual intervention may be required.
    - service_only: Wait for downstream services (currency, inventory) to confirm transfers complete.
    - party_required: Wait for all parties to call /confirm-release.
    - service_and_party: Wait for both service completion AND party confirmation.

RefundMode:
  type: string
  enum:
    - immediate
    - service_only
    - party_required
  description: |
    Controls how refund confirmation is handled. Same semantics as ReleaseMode.
    Refunds typically use 'immediate' since parties are getting their own assets back.

ConfirmationTimeoutBehavior:
  type: string
  enum:
    - auto_confirm
    - dispute
    - refund
  description: |
    What happens when confirmation timeout expires:
    - auto_confirm: If service events received, auto-confirm parties (default).
    - dispute: Transition to Disputed state, require arbiter intervention.
    - refund: Treat as failed, transition to Refunding.
```

**Add fields to `CreateEscrowRequest`**:

```yaml
releaseMode:
  $ref: '#/components/schemas/ReleaseMode'
  nullable: true
  description: How release confirmation is handled. Defaults to service_only if not specified.

refundMode:
  $ref: '#/components/schemas/RefundMode'
  nullable: true
  description: How refund confirmation is handled. Defaults to immediate if not specified.
```

**Add fields to `EscrowAgreement` response model**:

```yaml
releaseMode:
  $ref: '#/components/schemas/ReleaseMode'
  description: How release confirmation is handled for this escrow.

refundMode:
  $ref: '#/components/schemas/RefundMode'
  description: How refund confirmation is handled for this escrow.
```

### Step 1.2: Update escrow-api.yaml - Add Confirm Endpoints

**Add `/escrow/confirm-release` endpoint**:

```yaml
/escrow/confirm-release:
  post:
    operationId: confirmRelease
    summary: Confirm receipt of released assets
    description: |
      Called by parties to confirm they received their released assets.
      Required when ReleaseMode is party_required or service_and_party.
    x-permissions:
      - role: user
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ConfirmReleaseRequest'
    responses:
      '200':
        description: Confirmation recorded
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ConfirmReleaseResponse'

/escrow/confirm-refund:
  post:
    operationId: confirmRefund
    summary: Confirm receipt of refunded assets
    description: |
      Called by parties to confirm they received their refunded deposits.
      Required when RefundMode is party_required.
    x-permissions:
      - role: user
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ConfirmRefundRequest'
    responses:
      '200':
        description: Confirmation recorded
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ConfirmRefundResponse'
```

**Add request/response models**:

```yaml
ConfirmReleaseRequest:
  type: object
  required:
    - escrowId
    - partyId
    - releaseToken
  properties:
    escrowId:
      type: string
      format: uuid
      description: The escrow being confirmed.
    partyId:
      type: string
      format: uuid
      description: The party confirming receipt.
    releaseToken:
      type: string
      description: The party's release token (received via confirmation shortcut).
    notes:
      type: string
      nullable: true
      description: Optional confirmation notes.

ConfirmReleaseResponse:
  type: object
  required:
    - escrowId
    - confirmed
    - allPartiesConfirmed
  properties:
    escrowId:
      type: string
      format: uuid
      description: The escrow ID.
    confirmed:
      type: boolean
      description: Whether this party's confirmation was recorded.
    allPartiesConfirmed:
      type: boolean
      description: Whether all parties have now confirmed (triggers Released transition).
    status:
      $ref: '#/components/schemas/EscrowStatus'
      description: Current escrow status after confirmation.

ConfirmRefundRequest:
  type: object
  required:
    - escrowId
    - partyId
  properties:
    escrowId:
      type: string
      format: uuid
      description: The escrow being confirmed.
    partyId:
      type: string
      format: uuid
      description: The party confirming receipt of refund.
    notes:
      type: string
      nullable: true
      description: Optional confirmation notes.

ConfirmRefundResponse:
  type: object
  required:
    - escrowId
    - confirmed
    - allPartiesConfirmed
  properties:
    escrowId:
      type: string
      format: uuid
      description: The escrow ID.
    confirmed:
      type: boolean
      description: Whether this party's confirmation was recorded.
    allPartiesConfirmed:
      type: boolean
      description: Whether all parties have now confirmed (triggers Refunded transition).
    status:
      $ref: '#/components/schemas/EscrowStatus'
      description: Current escrow status after confirmation.
```

### Step 1.3: Update escrow-events.yaml - Add Releasing/Refunding Events

**File**: `schemas/escrow-events.yaml`

**Add new events**:

```yaml
EscrowReleasingEvent:
  type: object
  required:
    - eventId
    - timestamp
    - escrowId
    - releaseMode
    - allocations
  properties:
    eventId:
      type: string
      format: uuid
      description: Unique event identifier.
    timestamp:
      type: string
      format: date-time
      description: When the release was initiated.
    escrowId:
      type: string
      format: uuid
      description: The escrow transitioning to Releasing.
    releaseMode:
      $ref: 'escrow-api.yaml#/components/schemas/ReleaseMode'
      description: The confirmation mode for this release.
    allocations:
      type: array
      items:
        $ref: '#/components/schemas/ReleaseAllocationWithConfirmation'
      description: Per-party allocations with confirmation details.
    confirmationDeadline:
      type: string
      format: date-time
      nullable: true
      description: Deadline for party confirmations (if applicable).
    boundContractId:
      type: string
      format: uuid
      nullable: true
      description: Associated contract ID if this is a contract-driven release.

ReleaseAllocationWithConfirmation:
  type: object
  required:
    - recipientPartyId
    - recipientPartyType
    - assets
  properties:
    recipientPartyId:
      type: string
      format: uuid
      description: Party receiving these assets.
    recipientPartyType:
      $ref: 'common-api.yaml#/components/schemas/EntityType'
      description: Type of the recipient party.
    assets:
      type: array
      items:
        $ref: 'escrow-api.yaml#/components/schemas/EscrowAsset'
      description: Assets being released to this party.
    destinationWalletId:
      type: string
      format: uuid
      nullable: true
      description: Target wallet for currency assets.
    destinationContainerId:
      type: string
      format: uuid
      nullable: true
      description: Target container for item assets.
    releaseToken:
      type: string
      nullable: true
      description: Token for this party to confirm release.
    confirmationShortcut:
      type: object
      nullable: true
      description: Prebound API shortcut for client confirmation (pushed via WebSocket).

EscrowRefundingEvent:
  type: object
  required:
    - eventId
    - timestamp
    - escrowId
    - refundMode
    - deposits
  properties:
    eventId:
      type: string
      format: uuid
      description: Unique event identifier.
    timestamp:
      type: string
      format: date-time
      description: When the refund was initiated.
    escrowId:
      type: string
      format: uuid
      description: The escrow transitioning to Refunding.
    refundMode:
      $ref: 'escrow-api.yaml#/components/schemas/RefundMode'
      description: The confirmation mode for this refund.
    deposits:
      type: array
      items:
        $ref: 'escrow-api.yaml#/components/schemas/EscrowDeposit'
      description: Deposits being refunded.
    reason:
      type: string
      nullable: true
      description: Reason for refund.
```

### Step 1.4: Update escrow-configuration.yaml

**File**: `schemas/escrow-configuration.yaml`

**Remove these properties** (dead code):
- `tokenAlgorithm`
- `tokenSecret`

**Add new properties**:

```yaml
DefaultReleaseMode:
  $ref: 'escrow-api.yaml#/components/schemas/ReleaseMode'
  env: ESCROW_DEFAULT_RELEASE_MODE
  default: service_only
  description: |
    Default release confirmation mode for new escrows.
    Recommended: service_only (wait for downstream service confirmation).

DefaultRefundMode:
  $ref: 'escrow-api.yaml#/components/schemas/RefundMode'
  env: ESCROW_DEFAULT_REFUND_MODE
  default: immediate
  description: Default refund confirmation mode. Immediate is typical since parties get their own assets back.

ConfirmationTimeoutSeconds:
  type: integer
  env: ESCROW_CONFIRMATION_TIMEOUT_SECONDS
  default: 300
  minimum: 60
  maximum: 86400
  description: Timeout for party confirmations in seconds (default 5 minutes).

ConfirmationTimeoutBehavior:
  $ref: 'escrow-api.yaml#/components/schemas/ConfirmationTimeoutBehavior'
  env: ESCROW_CONFIRMATION_TIMEOUT_BEHAVIOR
  default: auto_confirm
  description: What happens when confirmation timeout expires.

ConfirmationTimeoutCheckIntervalSeconds:
  type: integer
  env: ESCROW_CONFIRMATION_TIMEOUT_CHECK_INTERVAL_SECONDS
  default: 30
  minimum: 10
  maximum: 300
  description: How often the background service checks for expired confirmations.

ConfirmationTimeoutBatchSize:
  type: integer
  env: ESCROW_CONFIRMATION_TIMEOUT_BATCH_SIZE
  default: 100
  minimum: 10
  maximum: 1000
  description: Maximum escrows to process per timeout check cycle.
```

### Step 1.5: Regenerate All Escrow Code

```bash
cd scripts && ./generate-service.sh escrow
dotnet build
```

---

## Phase 2: Service Implementation

### Step 2.1: Update Internal Models

**File**: `plugins/lib-escrow/EscrowService.cs`

**Add fields to `EscrowAgreementModel`**:

```csharp
public ReleaseMode ReleaseMode { get; set; } = ReleaseMode.ServiceOnly;
public RefundMode RefundMode { get; set; } = RefundMode.Immediate;
```

**Add confirmation tracking model**:

```csharp
internal class ReleaseConfirmation
{
    public Guid PartyId { get; set; }
    public EntityType PartyType { get; set; }
    public bool ServiceConfirmed { get; set; }
    public bool PartyConfirmed { get; set; }
    public DateTimeOffset? ServiceConfirmedAt { get; set; }
    public DateTimeOffset? PartyConfirmedAt { get; set; }
}
```

**Add to `EscrowAgreementModel`**:

```csharp
public List<ReleaseConfirmation>? ReleaseConfirmations { get; set; }
public DateTimeOffset? ConfirmationDeadline { get; set; }
```

### Step 2.2: Wire Up Configuration Properties (T21)

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`

**In `CreateEscrowAsync`**:

```csharp
// Apply release/refund modes (use request values or configuration defaults)
var releaseMode = body.ReleaseMode ?? _configuration.DefaultReleaseMode;
var refundMode = body.RefundMode ?? _configuration.DefaultRefundMode;

// Wire up MaxTimeout validation
var maxTimeout = XmlConvert.ToTimeSpan(_configuration.MaxTimeout);
var requestedDuration = expiresAt - now;
if (requestedDuration > maxTimeout)
{
    _logger.LogWarning("Requested escrow timeout {Requested} exceeds maximum {Max}",
        requestedDuration, maxTimeout);
    return (StatusCodes.BadRequest, null);
}

// Wire up MaxPendingPerParty enforcement
foreach (var partyInput in body.Parties)
{
    var partyKey = GetPartyPendingKey(partyInput.PartyId, partyInput.PartyType);
    var pendingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
    if (pendingCount != null && pendingCount.PendingCount >= _configuration.MaxPendingPerParty)
    {
        _logger.LogWarning("Party {PartyId} has reached max pending escrows {Max}",
            partyInput.PartyId, _configuration.MaxPendingPerParty);
        return (StatusCodes.BadRequest, null);
    }
}
```

**In `DepositAsync`**:

```csharp
// Wire up MaxAssetsPerDeposit validation
var assetCount = body.Assets?.Assets?.Count ?? 0;
if (assetCount > _configuration.MaxAssetsPerDeposit)
{
    _logger.LogWarning("Deposit asset count {Count} exceeds maximum {Max}",
        assetCount, _configuration.MaxAssetsPerDeposit);
    return (StatusCodes.BadRequest, null);
}
```

### Step 2.3: Fix Party Pending Count Race Condition (T9)

**File**: `plugins/lib-escrow/EscrowServiceLifecycle.cs`

**Refactor `CreateEscrowAsync` to use try/finally with rollback**:

```csharp
var incrementedParties = new List<(Guid PartyId, EntityType PartyType)>();
try
{
    foreach (var party in partyModels)
    {
        await IncrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
        incrementedParties.Add((party.PartyId, party.PartyType));
    }

    // ... rest of creation logic (save agreement, status index, tokens, etc.) ...

    return (StatusCodes.OK, new CreateEscrowResponse { ... });
}
catch (Exception)
{
    // Rollback any incremented pending counts
    foreach (var (partyId, partyType) in incrementedParties)
    {
        try
        {
            await DecrementPartyPendingCountAsync(partyId, partyType, cancellationToken);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx,
                "Failed to rollback pending count for party {PartyId} during escrow creation failure",
                partyId);
        }
    }
    throw;
}
```

### Step 2.4: Implement Event-Driven Release Flow

**File**: `plugins/lib-escrow/EscrowServiceCompletion.cs`

**Replace current two-save logic with event-driven flow**:

```csharp
public async Task<(StatusCodes, ReleaseResponse?)> ReleaseAsync(
    ReleaseRequest body, CancellationToken cancellationToken = default)
{
    // ... validation, get agreement ...

    // Check release mode
    if (agreementModel.ReleaseMode == ReleaseMode.Immediate)
    {
        // IMMEDIATE MODE: Skip Releasing state, go straight to Released
        // ⚠️ This is only safe for trusted/low-value scenarios
        agreementModel.Status = EscrowStatus.Released;
        agreementModel.CompletedAt = now;
        await SaveAndPublishReleasedAsync(agreementModel, cancellationToken);
        return (StatusCodes.OK, new ReleaseResponse { ... });
    }

    // EVENT-DRIVEN MODE: Transition to Releasing and publish event
    agreementModel.Status = EscrowStatus.Releasing;
    agreementModel.ConfirmationDeadline = now.AddSeconds(_configuration.ConfirmationTimeoutSeconds);

    // Initialize confirmation tracking
    agreementModel.ReleaseConfirmations = agreementModel.Parties?
        .Where(p => p.ConsentRequired)
        .Select(p => new ReleaseConfirmation
        {
            PartyId = p.PartyId,
            PartyType = p.PartyType,
            ServiceConfirmed = false,
            PartyConfirmed = false
        })
        .ToList() ?? new List<ReleaseConfirmation>();

    await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken: cancellationToken);
    await UpdateStatusIndexAsync(previousStatus, EscrowStatus.Releasing, escrowId, cancellationToken);

    // Publish escrow.releasing event with confirmation shortcuts
    var releasingEvent = BuildReleasingEvent(agreementModel);
    await _messageBus.TryPublishAsync(EscrowTopics.EscrowReleasing, releasingEvent, cancellationToken);

    return (StatusCodes.OK, new ReleaseResponse
    {
        EscrowId = escrowId,
        Released = false,  // Not yet - waiting for confirmations
        ReleaseMode = agreementModel.ReleaseMode,
        ConfirmationDeadline = agreementModel.ConfirmationDeadline
    });
}
```

### Step 2.5: Implement Confirm Release Endpoint

**File**: `plugins/lib-escrow/EscrowServiceCompletion.cs` (or new file)

```csharp
public async Task<(StatusCodes, ConfirmReleaseResponse?)> ConfirmReleaseAsync(
    ConfirmReleaseRequest body, CancellationToken cancellationToken = default)
{
    var agreementKey = GetAgreementKey(body.EscrowId);
    var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

    if (agreementModel == null)
        return (StatusCodes.NotFound, null);

    if (agreementModel.Status != EscrowStatus.Releasing)
        return (StatusCodes.Conflict, null);

    // Validate release token
    var party = agreementModel.Parties?.FirstOrDefault(p => p.PartyId == body.PartyId);
    if (party == null || party.ReleaseToken != body.ReleaseToken)
        return (StatusCodes.Forbidden, null);

    // Record confirmation
    var confirmation = agreementModel.ReleaseConfirmations?
        .FirstOrDefault(c => c.PartyId == body.PartyId);
    if (confirmation == null)
        return (StatusCodes.BadRequest, null);

    confirmation.PartyConfirmed = true;
    confirmation.PartyConfirmedAt = DateTimeOffset.UtcNow;

    // Check if all required confirmations received
    var allConfirmed = CheckAllConfirmationsComplete(agreementModel);

    if (allConfirmed)
    {
        // Transition to Released
        agreementModel.Status = EscrowStatus.Released;
        agreementModel.CompletedAt = DateTimeOffset.UtcNow;
        agreementModel.Resolution = EscrowResolution.Released;
    }

    var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
    if (saveResult == null)
        return (StatusCodes.Conflict, null);

    if (allConfirmed)
    {
        await UpdateStatusIndexAsync(EscrowStatus.Releasing, EscrowStatus.Released, body.EscrowId, cancellationToken);
        await PublishReleasedEventAsync(agreementModel, cancellationToken);
    }

    return (StatusCodes.OK, new ConfirmReleaseResponse
    {
        EscrowId = body.EscrowId,
        Confirmed = true,
        AllPartiesConfirmed = allConfirmed,
        Status = agreementModel.Status
    });
}

private bool CheckAllConfirmationsComplete(EscrowAgreementModel agreement)
{
    if (agreement.ReleaseConfirmations == null || !agreement.ReleaseConfirmations.Any())
        return true;

    return agreement.ReleaseMode switch
    {
        ReleaseMode.ServiceOnly => agreement.ReleaseConfirmations.All(c => c.ServiceConfirmed),
        ReleaseMode.PartyRequired => agreement.ReleaseConfirmations.All(c => c.PartyConfirmed),
        ReleaseMode.ServiceAndParty => agreement.ReleaseConfirmations.All(c => c.ServiceConfirmed && c.PartyConfirmed),
        _ => true  // Immediate mode shouldn't reach here
    };
}
```

### Step 2.6: Convert Status Index to Redis Sets

**File**: `plugins/lib-escrow/EscrowService.cs`

**Update helper methods to use Set APIs**:

```csharp
internal async Task AddToStatusSetAsync(EscrowStatus status, Guid escrowId, CancellationToken ct = default)
{
    var key = GetStatusSetKey(status);
    await StatusIndexStore.AddToSetAsync(key, escrowId.ToString(), cancellationToken: ct);
}

internal async Task RemoveFromStatusSetAsync(EscrowStatus status, Guid escrowId, CancellationToken ct = default)
{
    var key = GetStatusSetKey(status);
    await StatusIndexStore.RemoveFromSetAsync(key, escrowId.ToString(), cancellationToken: ct);
}

internal async Task<List<Guid>> GetEscrowIdsByStatusAsync(EscrowStatus status, CancellationToken ct = default)
{
    var key = GetStatusSetKey(status);
    var members = await StatusIndexStore.GetSetMembersAsync<string>(key, cancellationToken: ct);
    return members?
        .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .ToList() ?? new List<Guid>();
}

internal async Task UpdateStatusIndexAsync(EscrowStatus oldStatus, EscrowStatus newStatus, Guid escrowId, CancellationToken ct = default)
{
    await RemoveFromStatusSetAsync(oldStatus, escrowId, ct);
    await AddToStatusSetAsync(newStatus, escrowId, ct);
}
```

**Update `ListEscrowsAsync` to use status index**:

```csharp
if (body.PartyId == null && body.Status != null && body.Status.Count > 0)
{
    // Use Redis status index for status-only queries (fast path)
    var escrowIds = new HashSet<Guid>();
    foreach (var status in body.Status)
    {
        var idsInStatus = await GetEscrowIdsByStatusAsync(status, cancellationToken);
        escrowIds.UnionWith(idsInStatus);
    }

    // Fetch full agreements for matched IDs
    var agreements = new List<EscrowAgreementModel>();
    foreach (var id in escrowIds.Skip(offset).Take(limit))
    {
        var agreement = await AgreementStore.GetAsync(GetAgreementKey(id), cancellationToken);
        if (agreement != null) agreements.Add(agreement);
    }

    totalCount = escrowIds.Count;
    results = agreements.Select(MapToApiModel).ToList();
}
```

---

## Phase 3: Event Subscriptions (T5)

### Step 3.1: Subscribe to Service Completion Events

**File**: `schemas/escrow-events.yaml`

**Add to x-event-subscriptions**:

```yaml
x-event-subscriptions:
  # ... existing subscriptions ...
  - topic: currency.escrow.transfer.completed
    event: CurrencyEscrowTransferCompletedEvent
    handler: HandleCurrencyTransferCompleted
  - topic: inventory.escrow.transfer.completed
    event: InventoryEscrowTransferCompletedEvent
    handler: HandleInventoryTransferCompleted
```

### Step 3.2: Implement Service Confirmation Handlers

**File**: `plugins/lib-escrow/EscrowServiceEvents.cs`

```csharp
public async Task HandleCurrencyTransferCompletedAsync(CurrencyEscrowTransferCompletedEvent evt)
{
    await RecordServiceConfirmationAsync(evt.EscrowId, evt.PartyId, evt.PartyType);
}

public async Task HandleInventoryTransferCompletedAsync(InventoryEscrowTransferCompletedEvent evt)
{
    await RecordServiceConfirmationAsync(evt.EscrowId, evt.PartyId, evt.PartyType);
}

private async Task RecordServiceConfirmationAsync(Guid escrowId, Guid partyId, EntityType partyType)
{
    var agreementKey = GetAgreementKey(escrowId);
    var (agreement, etag) = await AgreementStore.GetWithETagAsync(agreementKey);

    if (agreement == null || agreement.Status != EscrowStatus.Releasing)
        return;

    var confirmation = agreement.ReleaseConfirmations?
        .FirstOrDefault(c => c.PartyId == partyId);
    if (confirmation == null)
        return;

    confirmation.ServiceConfirmed = true;
    confirmation.ServiceConfirmedAt = DateTimeOffset.UtcNow;

    // Check if all confirmations complete (based on release mode)
    if (CheckAllConfirmationsComplete(agreement))
    {
        agreement.Status = EscrowStatus.Released;
        agreement.CompletedAt = DateTimeOffset.UtcNow;
        agreement.Resolution = EscrowResolution.Released;

        await AgreementStore.TrySaveAsync(agreementKey, agreement, etag ?? string.Empty);
        await UpdateStatusIndexAsync(EscrowStatus.Releasing, EscrowStatus.Released, escrowId);
        await PublishReleasedEventAsync(agreement);
    }
    else
    {
        await AgreementStore.TrySaveAsync(agreementKey, agreement, etag ?? string.Empty);
    }
}
```

---

## Phase 4: Background Timeout Service

### Step 4.1: Add Timeout Check Config

**File**: `schemas/escrow-configuration.yaml`

Add to configuration:

```yaml
ConfirmationTimeoutCheckIntervalSeconds:
  type: integer
  env: ESCROW_CONFIRMATION_TIMEOUT_CHECK_INTERVAL_SECONDS
  default: 30
  minimum: 10
  maximum: 300
  description: How often to check for expired confirmations (seconds).

ConfirmationTimeoutBatchSize:
  type: integer
  env: ESCROW_CONFIRMATION_TIMEOUT_BATCH_SIZE
  default: 100
  minimum: 10
  maximum: 1000
  description: Maximum escrows to process per timeout check cycle.
```

### Step 4.2: Add Background Service

**File**: `plugins/lib-escrow/Services/EscrowConfirmationTimeoutService.cs`

```csharp
/// <summary>
/// Background service that checks for expired confirmation deadlines
/// and applies the configured timeout behavior (auto_confirm, dispute, or refund).
/// </summary>
public class EscrowConfirmationTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EscrowServiceConfiguration _configuration;
    private readonly ILogger<EscrowConfirmationTimeoutService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_configuration.ConfirmationTimeoutCheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await ProcessExpiredConfirmationsAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing confirmation timeouts");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task ProcessExpiredConfirmationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var stateStoreFactory = services.GetRequiredService<IStateStoreFactory>();
        var messageBus = services.GetRequiredService<IMessageBus>();

        // Get escrows in Releasing or Refunding state from status index
        var releasingIds = await GetEscrowIdsByStatusAsync(stateStoreFactory, EscrowStatus.Releasing, ct);
        var refundingIds = await GetEscrowIdsByStatusAsync(stateStoreFactory, EscrowStatus.Refunding, ct);

        var now = DateTimeOffset.UtcNow;
        var processed = 0;

        foreach (var escrowId in releasingIds.Concat(refundingIds))
        {
            if (processed >= _configuration.ConfirmationTimeoutBatchSize) break;

            var wasProcessed = await ProcessSingleEscrowTimeoutAsync(
                stateStoreFactory, messageBus, escrowId, now, ct);

            if (wasProcessed) processed++;
        }

        if (processed > 0)
        {
            _logger.LogInformation("Processed {Count} confirmation timeouts", processed);
        }
    }

    private async Task<bool> ProcessSingleEscrowTimeoutAsync(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        Guid escrowId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var agreementStore = stateStoreFactory.GetStore<EscrowAgreementModel>(StateStoreDefinitions.Escrow);
        var agreementKey = $"agreement:{escrowId}";
        var (agreement, etag) = await agreementStore.GetWithETagAsync(agreementKey, ct);

        if (agreement == null) return false;
        if (agreement.ConfirmationDeadline == null || agreement.ConfirmationDeadline > now) return false;

        // Deadline has passed - apply configured timeout behavior
        var behavior = _configuration.ConfirmationTimeoutBehavior;

        switch (behavior)
        {
            case ConfirmationTimeoutBehavior.AutoConfirm:
                // Only auto-confirm if services have confirmed
                var allServicesConfirmed = agreement.ReleaseConfirmations?.All(c => c.ServiceConfirmed) ?? true;
                if (allServicesConfirmed)
                {
                    _logger.LogInformation("Auto-confirming escrow {EscrowId} after timeout", escrowId);
                    await TransitionToCompletedAsync(stateStoreFactory, messageBus, agreement, etag, ct);
                }
                else
                {
                    _logger.LogWarning("Cannot auto-confirm escrow {EscrowId} - services not confirmed, escalating to dispute", escrowId);
                    await TransitionToDisputedAsync(stateStoreFactory, messageBus, agreement, etag, ct);
                }
                break;

            case ConfirmationTimeoutBehavior.Dispute:
                _logger.LogInformation("Escalating escrow {EscrowId} to Disputed after timeout", escrowId);
                await TransitionToDisputedAsync(stateStoreFactory, messageBus, agreement, etag, ct);
                break;

            case ConfirmationTimeoutBehavior.Refund:
                _logger.LogInformation("Initiating refund for escrow {EscrowId} after timeout", escrowId);
                await TransitionToRefundingAsync(stateStoreFactory, messageBus, agreement, etag, ct);
                break;
        }

        return true;
    }

    // ... transition helper methods (similar pattern to EscrowServiceCompletion)
}
```

### Step 4.3: Register Background Service

**File**: `plugins/lib-escrow/lib-escrow.csproj` or plugin startup

Background services in Bannou plugins are registered via the `IHostedService` pattern:

```csharp
// The EscrowConfirmationTimeoutService will be discovered and registered
// by the plugin loader since it implements BackgroundService
```

---

## Phase 5: Documentation Updates

### Step 5.1: Update ESCROW.md

**File**: `docs/plugins/ESCROW.md`

**Updates**:
1. Configuration section: Remove TokenAlgorithm/TokenSecret, add new config properties
2. Add Release Modes section explaining `immediate`, `service_only`, `party_required`, `service_and_party`
3. Add warning callout for `immediate` mode risks
4. Bugs section: Mark bugs 1, 2, 3 as FIXED
5. Add Work Tracking entry for this issue

---

## Phase 6: Follow-up Issues

### Create HMAC Token Enhancement Issue

```bash
gh issue create --title "[ENHANCEMENT] Consider HMAC-signed escrow tokens" --body "..."
```

### Create Service Completion Event Issues

Currency and Inventory services need to publish `*.escrow.transfer.completed` events:

```bash
gh issue create --title "[FEATURE] lib-currency: Publish escrow transfer completion events" --body "..."
gh issue create --title "[FEATURE] lib-inventory: Publish escrow transfer completion events" --body "..."
```

---

## File Change Summary

| File | Change Type | Description |
|------|-------------|-------------|
| `schemas/escrow-api.yaml` | MODIFY | Add ReleaseMode, RefundMode, ConfirmationTimeoutBehavior enums; add /confirm-release, /confirm-refund endpoints; add fields to CreateEscrowRequest and EscrowAgreement |
| `schemas/escrow-events.yaml` | MODIFY | Add EscrowReleasingEvent, EscrowRefundingEvent; add x-event-subscriptions for service completion events |
| `schemas/escrow-configuration.yaml` | MODIFY | Remove tokenAlgorithm, tokenSecret; add DefaultReleaseMode, DefaultRefundMode, ConfirmationTimeoutSeconds, ConfirmationTimeoutBehavior, ConfirmationTimeoutCheckIntervalSeconds, ConfirmationTimeoutBatchSize |
| `plugins/lib-escrow/EscrowService.cs` | MODIFY | Add status set helpers; add ReleaseConfirmation model; update internal models |
| `plugins/lib-escrow/EscrowServiceLifecycle.cs` | MODIFY | MaxTimeout, MaxPendingPerParty, MaxAssetsPerDeposit validation; pending count race fix; release/refund mode handling |
| `plugins/lib-escrow/EscrowServiceCompletion.cs` | MODIFY | Event-driven release flow; implement ConfirmReleaseAsync, ConfirmRefundAsync |
| `plugins/lib-escrow/EscrowServiceEvents.cs` | MODIFY | Add service confirmation event handlers |
| `plugins/lib-escrow/Services/EscrowConfirmationTimeoutService.cs` | CREATE | Background service for confirmation timeout handling |
| `docs/plugins/ESCROW.md` | MODIFY | Update config table, add release modes docs, mark bugs fixed |

---

## Verification Steps

1. `cd scripts && ./generate-service.sh escrow` - Regenerate all escrow code
2. `dotnet build` - Must pass with no errors
3. `make test` - Unit tests must pass

---

## TENET Compliance Checklist

- [ ] **T1**: Schema changes before code (api.yaml, events.yaml, configuration.yaml updated first)
- [ ] **T4**: Using lib-state Set APIs for status index (AddToSetAsync, RemoveFromSetAsync, GetSetMembersAsync)
- [ ] **T5**: Events published for Releasing/Refunding state changes; typed events in schema
- [ ] **T7**: Error handling with proper logging; TryPublishErrorAsync for unexpected failures
- [ ] **T8**: Return pattern (StatusCodes, TResponse?) maintained for new endpoints
- [ ] **T9**: Multi-instance safety (ETag-based concurrency, distributed state, try/finally rollback)
- [ ] **T21**: All config properties wired up; no dead config; no hardcoded tunables
- [ ] **T22**: No warning suppressions added
- [ ] **T25**: ReleaseMode, RefundMode, ConfirmationTimeoutBehavior use proper enum types (not strings)

---

## Risk Callouts

### Immediate Mode Warning (CRITICAL)

The `immediate` release mode documentation MUST include this warning:

> ⚠️ **WARNING**: `immediate` mode marks assets as released BEFORE downstream services confirm transfers. Use only for trusted/low-value scenarios (NPC vendors, automated rewards, system-initiated distributions). If downstream service calls fail, assets may be marked as released but not actually transferred, requiring manual intervention.

### Downstream Service Dependencies

This design assumes Currency and Inventory services will publish completion events. If those services are not updated, `service_only` and `service_and_party` modes will never complete. Fallback: the confirmation timeout with `auto_confirm` behavior will eventually transition to Released.
