# Resource Transactions: Durable Multi-Service Provisioning and Cleanup

> **Type**: Implementation Plan
> **Status**: Draft
> **Created**: 2026-03-19
> **Last Updated**: 2026-03-19
> **North Stars**: #1, #3, #4
> **Related Plugins**: Resource (L1), Genesis (L2), Craft (L4), Workshop (L4), Market (L4), Divine (L4), Dungeon (L4)
> **Related Issues**: #156, #366, #556, #593, #600, #626

## Summary

Extends lib-resource (L1) with transactional multi-service provisioning and cleanup coordination. When an orchestrating service (Genesis, Craft, Workshop) provisions resources across multiple services, Resource tracks the provisioning as a durable transaction with automatic compensation on failure and background recovery for crash scenarios. This eliminates the "best-effort" compensation pattern that currently produces silent orphaned resources with no recovery mechanism.

The design composes with a complementary schema-level change: all Create endpoints accept an optional caller-specified GUID, enabling idempotent retry of provisioning sequences. Together, these two changes guarantee that every multi-service operation either fully completes or fully rolls back — no silent orphans, no fabricated reconciliation workers, no swallowed exceptions.

---

## Context

### The Problem

Bannou services that orchestrate creation of resources across multiple services (Genesis creating seeds + wallets + inventories, Craft consuming materials + creating outputs + debiting currency, Workshop materializing production + placing items) face a fundamental problem: if provisioning fails mid-sequence, the already-provisioned resources are orphaned.

The current approach has three structural failures:

1. **Compensation failures are swallowed.** When provisioning fails, the catch block attempts to undo each provisioned resource. If *any* undo call also fails (service down, timeout, transient error), the failure is caught and logged as a Warning. No retry. No record. No recovery path.

2. **Destruction ordering violates T7.** `DestroyEntityCoreAsync` in Genesis calls `ExecuteCleanupAsync` best-effort, then deletes the entity record regardless of whether cleanup succeeded. If cleanup fails, the entity record is gone — and with it, the only record of what resources (wallets, inventories, seeds, actors, characters) were provisioned under it. T7 Strategy 3 says to put fallible calls before irreversible mutations; the current code does the opposite.

3. **Referenced safety mechanisms do not exist.** Genesis's `CompensateProvisioningAsync` XML doc claims "orphaned seeds owned by a nonexistent entity are cleaned up by Seed's owner reconciliation worker." No such worker exists in lib-seed. The only BackgroundService is `SeedDecayWorkerService` (growth decay). There is no individual seed delete API. Orphaned seeds persist forever. Per CLAUDE-PRACTICES.md: "A comment is not a mechanism."

### Scale of Exposure

This is not isolated to Genesis. Every planned L4 orchestration service faces the same problem:

| Service | Multi-Service Operations | Current/Planned Compensation |
|---------|-------------------------|------------------------------|
| **Genesis** | Seed + wallets + inventories + actor + character | Best-effort catch blocks, fabricated reconciliation reference |
| **Craft** | Consume materials + debit currency + create output + grant XP | Not yet implemented |
| **Workshop** | Materialize production + place items + update inventory | Issue #626 tracks this gap |
| **Market** | Escrow creation + inventory reservation + price locking | Not yet implemented |
| **Divine** | Blessing grants across Seed + Status + Collection | Not yet implemented |
| **Dungeon** | Monster spawning + trap activation + layout mutation | Not yet implemented |

### When Transactions Are NOT Needed

The vast majority of entity creation in Bannou is single-service: create a character, create a wallet, create a seed, create a container. One call, one store write, done. No transaction needed — the operation is atomic by nature.

Transactions are for the genuinely complex cases where one logical operation provisions resources across **3+ services** and needs guaranteed all-or-nothing semantics. Looking at the table above, these are significant lifecycle events — an entity awakening, a crafting operation completing, a marketplace listing being created — not high-throughput hot paths. A living weapon being forged from template is a major game event, not something happening thousands of times per minute.

**The design smell test**: If a service is hitting the transaction store at high frequency, that is a signal the service is orchestrating too much in a single operation, not a scaling problem with the transaction mechanism. The correct response is to decompose the service's operations, not to optimize the transaction store for throughput it should never see.

Once caller-specified IDs (Phase 2) are formalized across the codebase, many two-service orchestrations that might have seemed like transaction candidates become simple: provision with a pre-generated ID, and if you crash before saving the parent, retry the idempotent create on recovery. Transactions are the solution for cases too complex for idempotent retry alone — operations with 3+ services, conditional branching mid-provisioning, or resource types that cannot support idempotent creates.

### Why lib-resource Is the Right Home

Resource already owns multi-service lifecycle coordination:

- **Reference tracking**: `RegisterReference` / `UnregisterReference` — knows which services have data depending on which entities
- **Cleanup coordination**: `ExecuteCleanupAsync` — calls registered callbacks across multiple services when an entity is deleted (CASCADE/DETACH)
- **Migration coordination**: `ExecuteMigrateAsync` — calls registered callbacks to move references before deletion (RESTRICT)
- **Compression coordination**: `ExecuteCompressAsync` — calls registered callbacks to archive data before deletion

Adding transaction coordination extends the same pattern to the *creation* side of the lifecycle. Resource already answers "what happens when entity X dies?" — this makes it also answer "what happens when entity X is being born and the birth fails?"

No new plugin. No new infrastructure lib. No changes to downstream services. Resource gains new endpoints and a background worker; orchestrating services call Resource's transaction API instead of writing their own compensation logic.

---

## Design

### Transaction Lifecycle

```
BeginTransaction
    │
    ├── RegisterProvision (repeated for each provisioned resource)
    │
    ├── CommitTransaction  ──→  Provisions become permanent references
    │                           Transaction archived after retention period
    │
    └── AbortTransaction   ──→  Resource calls compensation endpoints
        │                       in reverse provisioning order
        │
        ├── All compensated  → Transaction status: Aborted
        │
        └── Some failed      → Transaction status: Aborting
                                Worker retries on interval
                                After max retries: error event + Aborted
```

If neither Commit nor Abort arrives within the transaction's TTL, the background worker does NOT blindly auto-abort. It first executes the transaction's **validation check** — a prebound API the requester registered at creation time — to determine whether the parent entity was successfully created despite the missing commit call. If validation succeeds (200), the worker auto-commits. If validation fails (non-200 or unreachable), the worker auto-aborts and compensates. See "TTL Recovery via Prebound Validation" below.

### Transaction States

| State | Meaning | Background Worker Action |
|-------|---------|--------------------------|
| `Active` | Provisioning in progress | If past TTL → execute validation check → auto-commit or auto-abort |
| `Committing` | Converting provisions to permanent references (may crash mid-loop) | Resume reference registration from last successful provision |
| `Committed` | All provisions confirmed, references registered | After retention period → purge metadata |
| `Aborting` | Compensation in progress, some provisions not yet compensated | Retry failed compensations with backoff |
| `Aborted` | All compensations completed or retries exhausted | After retention period → purge metadata |

The `Committing` state is the atomicity mechanism: CommitTransaction transitions to `Committing` first (single write), then registers references one by one (updating each provision's status to `ReferenceRegistered` as it goes), then transitions to `Committed`. If the process crashes during the loop, the worker sees `Committing` state and resumes from the first provision that isn't yet `ReferenceRegistered`.

### TTL Recovery via Prebound Validation

The most dangerous crash window is between "entity record saved" and "CommitTransaction called." Without validation, TTL expiry would auto-abort a transaction whose entity was actually created successfully — compensating (deleting) resources the entity references.

The solution uses the existing prebound API infrastructure (`PreboundApiDefinition` + `ServiceNavigator.ExecutePreboundApiAsync`). At `BeginTransaction` time, the requester provides:

1. **`completionValidation`**: A `PreboundApiDefinition` that returns 200 if the parent entity was successfully created. Resource executes this as an opaque HTTP call via `IServiceNavigator` — no hierarchy violation, no domain knowledge required.

2. **`expectedProvisionCount`**: How many provisions the requester intends to register. This lets the worker distinguish "orchestrator is still working" (Active, fewer provisions than expected, within TTL) from "orchestrator crashed mid-provisioning" (Active, fewer provisions than expected, past TTL) from "orchestrator finished provisioning but didn't call commit" (Active, all expected provisions registered, past TTL).

3. **Per-provision `verification`** (optional): A `PreboundApiDefinition` per provision that returns 200 if that specific resource still exists and is healthy. The worker can run these during recovery to skip compensating resources that were already cleaned up by other mechanisms.

**TTL expiry decision tree:**

```
Transaction Active, past TTL
│
├── expectedProvisionCount NOT met
│   └── Abort (orchestrator crashed before finishing provisioning)
│
└── expectedProvisionCount met (or no count specified)
    │
    ├── Execute completionValidation prebound API
    │   │
    │   ├── 200 OK → Auto-COMMIT (entity exists, orchestrator just didn't call commit)
    │   │
    │   ├── 404 / 4xx → Auto-ABORT (entity confirmed not to exist, compensate)
    │   │
    │   ├── Unreachable / timeout / 5xx → RETRY validation next cycle
    │   │   └── After N consecutive validation failures → error event, remain Active for admin
    │   │
    │   └── No completionValidation provided → Auto-ABORT (conservative default)
    │
    └── No completionValidation AND no expectedProvisionCount → Auto-ABORT
```

**Critical distinction**: "unreachable" is NOT "entity doesn't exist." A network partition or service restart that makes the validation endpoint temporarily unreachable must NOT trigger abort — that would destroy resources for a live entity. Only a definitive "entity not found" response (4xx) justifies abort. Transient failures retry until resolved or until a configurable `TransactionValidationMaxRetries` is exhausted, at which point the transaction remains Active with an error event for admin intervention.

**Example validation check registration:**

```csharp
var tx = await _resourceClient.BeginTransactionAsync(new BeginTransactionRequest
{
    OwnerService = "genesis",
    ParentResourceType = "genesis-entity",
    ParentResourceId = entityId,
    TtlSeconds = _configuration.ProvisioningTransactionTtlSeconds,
    ExpectedProvisionCount = 1 + template.Economy.Wallets.Count + template.Storage.Inventories.Count,
    CompletionValidation = new PreboundApiDefinition
    {
        ServiceName = "genesis",
        Endpoint = "/genesis/entity/get",
        PayloadTemplate = "{\"entityId\": \"{{parentResourceId}}\"}",
    },
}, ct);
```

Resource substitutes `{{parentResourceId}}` from the transaction's own `parentResourceId` field — the same template mechanism used for cleanup/migrate callbacks.

### Data Model

**Prerequisite**: `PreboundApiDefinition` must be moved to the Core SDK (`sdks/core/`) and referenced via `x-sdk-type` in schemas. This makes it a first-class schema type usable in request/response models. Prebound APIs that cannot be included in requests are prebound APIs that cannot be used — the current location in `bannou-service/ServiceClients/` is a bug, not a design constraint.

```yaml
# New state store: resource-transactions
# Backend: MySQL (durable, queryable for worker scans)
# For embedded/sidecar with SQLite: SQLite provides equivalent durability.
# In-memory backend provides structured compensation but NOT crash recovery — acceptable
# for embedded single-process mode where crash = process restart from save state anyway.

ResourceTransaction:
  transactionId: Guid (primary)
  ownerService: string           # "genesis", "craft", "workshop"
  parentResourceType: string     # "genesis-entity", "craft-output"
  parentResourceId: Guid         # Entity being created
  status: TransactionStatus      # Active, Committing, Committed, Aborting, Aborted
  createdAt: DateTimeOffset
  updatedAt: DateTimeOffset
  ttlSeconds: int                # Validation deadline (e.g., 120)
  expectedProvisionCount: int?   # How many provisions the requester intends to register
  validationAttempts: int        # How many times TTL validation has been attempted
  # Stored as serialized PreboundApiDefinition — preserves ALL fields at registration-time values.
  # If PreboundApiDefinition gains new fields (TimeoutSeconds, Headers, etc.), stored
  # transactions retain their registration-time values instead of silently inheriting new defaults.
  completionValidation: string?  # BannouJson.Serialize(PreboundApiDefinition) — null if not provided

ResourceProvision:
  provisionId: Guid (primary)
  transactionId: Guid (FK)
  sequenceNumber: int            # Provisioning order (for reverse compensation)
  resourceType: string           # "seed", "currency-wallet", "inventory-container"
  resourceId: Guid               # The pre-generated resource ID (known before resource creation)
  status: ProvisionStatus        # Pending, Provisioned, ReferenceRegistered, Compensated, CompensationFailed
  registeredAt: DateTimeOffset   # When the provision was registered (Pending)
  provisionedAt: DateTimeOffset? # When the resource was confirmed created
  compensatedAt: DateTimeOffset?
  compensationAttempts: int
  lastCompensationError: string?
  # Stored as serialized PreboundApiDefinition — same rationale as completionValidation
  compensation: string           # BannouJson.Serialize(PreboundApiDefinition) — the undo call
  verification: string?          # BannouJson.Serialize(PreboundApiDefinition) — optional health check
```

**Provision status lifecycle:**

| Status | Meaning |
|--------|---------|
| `Pending` | Registered with pre-generated ID; resource not yet created |
| `Provisioned` | Resource confirmed created at the pre-generated ID |
| `ReferenceRegistered` | Converted to permanent resource reference during commit |
| `Compensated` | Compensation endpoint called successfully (resource deleted/closed) |
| `CompensationFailed` | Compensation attempted but failed; worker will retry |

**Why Pending → Provisioned matters:** The orchestrating service registers each provision BEFORE creating the resource (with a pre-generated ID). If the process crashes between registration and creation, the provision is `Pending` — the transaction knows about it. On abort, the compensation endpoint is called; the resource doesn't exist; the endpoint returns 404; 404 is treated as successful compensation. No orphan.

This eliminates the gap where a resource exists without the transaction knowing about it. The reverse order (create-then-register) has an unrecoverable gap: if crash occurs between creation and registration, the resource is orphaned permanently because no mechanism can find it.

**Template placeholder semantics:** Transaction-level templates use `{{parentResourceId}}` (the entity being created). Provision-level templates use `{{provisionResourceId}}` (the specific provisioned resource). These are distinct from the `{{resourceId}}` used in x-references cleanup callbacks (which refers to the parent being deleted). The names are self-documenting and cannot be confused.

**Compensation endpoint requirements:**
1. **Idempotent**: Calling twice produces the same result as calling once. Concurrent abort attempts (orchestrator + TTL worker) may call the same endpoint simultaneously.
2. **404 = success**: If the resource doesn't exist (never created, or already compensated), return 404 or 200 — both are treated as successful compensation. The resource is gone; the goal is achieved.
3. **Standard deletion flow**: Compensation endpoints MUST use the entity's proper deletion logic (including `ExecuteCleanupAsync` for any dependent references), not bare store deletes. This ensures nested transaction cascading works correctly (R5).

### API Endpoints (resource-api.yaml additions)

**Implementation note**: The response patterns below (400/404 status codes) are shown for clarity. During implementation, these must be reconciled with Resource's existing response convention (the implementation map documents an "all-200" structured response pattern for existing endpoints). Either existing Resource moves to proper status codes, or new endpoints follow the existing convention. This is an implementation-time decision, not a design-time one.

**Schema enums required**: `TransactionStatus` (Active, Committing, Committed, Aborting, Aborted) and `ProvisionStatus` (Pending, Provisioned, ReferenceRegistered, Compensated, CompensationFailed) must be defined as proper enum types in `resource-api.yaml` per T25.

**Response models**: All endpoints need response models per T8 — RegisterProvision returns `provisionId`, ConfirmProvision returns updated status, CommitTransaction returns reference count registered.

```yaml
/resource/transaction/begin:
  post:
    operationId: beginTransaction
    summary: Begin a provisioning transaction
    description: >
      Creates a durable provisioning transaction with a TTL deadline.
      If the transaction is neither committed nor aborted within the TTL,
      the background worker auto-aborts it and compensates all provisions.
      Use this when an orchestrating service needs to provision resources
      across multiple services with guaranteed cleanup on failure.
    x-permissions: []
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/BeginTransactionRequest'
    responses:
      '200':
        description: Transaction created
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BeginTransactionResponse'

/resource/transaction/register-provision:
  post:
    operationId: registerProvision
    summary: Register a planned provision with pre-generated ID (status: Pending)
    description: >
      Records that a resource WILL BE provisioned as part of an active transaction.
      Called BEFORE creating the resource. The pre-generated resourceId and
      compensation definition are stored so that if the process crashes before
      the resource is created, the transaction can still compensate (compensation
      endpoint receives 404, which is treated as successful compensation).
      Provisions are compensated in reverse registration order.
    x-permissions: []
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/RegisterProvisionRequest'
    responses:
      '200':
        description: Provision registered (Pending)
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterProvisionResponse'
      '400':
        description: Transaction not in Active state
      '404':
        description: Transaction not found

/resource/transaction/confirm-provision:
  post:
    operationId: confirmProvision
    summary: Confirm a provision was successfully created (Pending → Provisioned)
    description: >
      Called AFTER the resource has been successfully created at the pre-generated ID.
      Transitions the provision from Pending to Provisioned. If the process crashes
      before this call, the provision remains Pending — on abort, the compensation
      endpoint is called and handles the resource if it exists (idempotent).
    x-permissions: []
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/ConfirmProvisionRequest'
    responses:
      '200':
        description: Provision confirmed (Provisioned)
      '400':
        description: Provision not in Pending state or transaction not Active
      '404':
        description: Transaction or provision not found

/resource/transaction/commit:
  post:
    operationId: commitTransaction
    summary: Commit a provisioning transaction
    description: >
      Marks a transaction as committed. All provisions become permanent
      resource references via the existing RegisterReference mechanism.
      After commit, provisioned resources are tracked by the standard
      x-references cleanup infrastructure — no separate tracking needed.
    x-permissions: []
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CommitTransactionRequest'
    responses:
      '200':
        description: Transaction committed, references registered
      '400':
        description: Transaction not in Active state
      '404':
        description: Transaction not found

/resource/transaction/abort:
  post:
    operationId: abortTransaction
    summary: Abort a provisioning transaction
    description: >
      Initiates compensation for all provisions in reverse order.
      Resource calls each provision's compensation endpoint using the
      same HTTP callback mechanism as cleanup callbacks. Provisions that
      compensate successfully are marked Compensated. Provisions that fail
      are marked CompensationFailed and retried by the background worker.
    x-permissions: []
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/AbortTransactionRequest'
    responses:
      '200':
        description: Abort initiated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AbortTransactionResponse'
      '400':
        description: Transaction not in abortable state
      '404':
        description: Transaction not found

/resource/transaction/status:
  post:
    operationId: getTransactionStatus
    summary: Query transaction status
    description: >
      Returns the current status of a transaction including per-provision
      status. Useful for admin tooling and debugging.
    x-permissions:
    - role: admin
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/GetTransactionStatusRequest'
    responses:
      '200':
        description: Transaction status
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/TransactionStatusResponse'
      '404':
        description: Transaction not found
```

### Orchestrating Service Usage Pattern

```csharp
// Genesis CreateEntityAsync — with Resource transactions
var entityId = Guid.NewGuid();
var expectedProvisions = 1 + template.Economy.Wallets.Count + template.Storage.Inventories.Count;

// Pre-generate all resource IDs (required for register-before-create order)
var seedId = Guid.NewGuid();
var walletIds = template.Economy.Wallets.ToDictionary(w => w.WalletCode, _ => Guid.NewGuid());
var containerIds = template.Storage.Inventories.ToDictionary(i => i.InventoryCode, _ => Guid.NewGuid());

var tx = await _resourceClient.BeginTransactionAsync(new BeginTransactionRequest
{
    OwnerService = "genesis",
    ParentResourceType = "genesis-entity",
    ParentResourceId = entityId,
    TtlSeconds = _configuration.ProvisioningTransactionTtlSeconds,
    ExpectedProvisionCount = expectedProvisions,
    CompletionValidation = new PreboundApiDefinition
    {
        ServiceName = "genesis",
        Endpoint = "/genesis/entity/get",
        PayloadTemplate = "{\"entityId\": \"{{parentResourceId}}\"}",
    },
}, ct);

try
{
    // Register provision FIRST (Pending), then create resource, then confirm (Provisioned).
    // If crash between register and create: provision is Pending, resource doesn't exist.
    // On abort: compensation called, 404 returned, treated as success. No orphan.

    // Seed
    await _resourceClient.RegisterProvisionAsync(new RegisterProvisionRequest
    {
        TransactionId = tx.TransactionId,
        ResourceType = "seed", ResourceId = seedId,
        Compensation = new PreboundApiDefinition
        {
            ServiceName = "seed", Endpoint = "/seed/delete",
            PayloadTemplate = "{\"seedId\": \"{{provisionResourceId}}\"}",
        },
    }, ct);
    await _seedClient.CreateSeedAsync(new CreateSeedRequest
    {
        SeedId = seedId,  // Caller-specified ID — idempotent create
        OwnerType = EntityType.Other, OwnerId = entityId,
        SeedTypeCode = template.Seed.SeedTypeCode, GameServiceId = body.GameServiceId,
    }, ct);
    await _resourceClient.ConfirmProvisionAsync(new ConfirmProvisionRequest
    {
        TransactionId = tx.TransactionId, ResourceId = seedId,
    }, ct);

    // Wallets
    foreach (var wallet in template.Economy.Wallets)
    {
        var walletId = walletIds[wallet.WalletCode];
        await _resourceClient.RegisterProvisionAsync(new RegisterProvisionRequest
        {
            TransactionId = tx.TransactionId,
            ResourceType = "currency-wallet", ResourceId = walletId,
            Compensation = new PreboundApiDefinition
            {
                ServiceName = "currency", Endpoint = "/currency/wallet/close",
                PayloadTemplate = "{\"walletId\": \"{{provisionResourceId}}\"}",
            },
        }, ct);
        await _currencyClient.CreateWalletAsync(new CreateWalletRequest
        {
            WalletId = walletId,
            OwnerId = entityId, OwnerType = EntityType.Other, RealmId = body.RealmId,
        }, ct);
        await _resourceClient.ConfirmProvisionAsync(new ConfirmProvisionRequest
        {
            TransactionId = tx.TransactionId, ResourceId = walletId,
        }, ct);
    }

    // All provisioning succeeded — save entity, commit transaction
    var entity = new GenesisEntityModel { EntityId = entityId, SeedId = seedId,
        WalletIds = walletIds, InventoryIds = containerIds, ... };
    await _entityStore.SaveAsync(BuildEntityKey(entityId), entity, ct);
    await _resourceClient.CommitTransactionAsync(
        new CommitTransactionRequest { TransactionId = tx.TransactionId }, ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // Graceful shutdown or request cancellation — let TTL + worker handle recovery
    throw;
}
catch (Exception)
{
    // Abort — Resource handles compensation durably
    try
    {
        await _resourceClient.AbortTransactionAsync(
            new AbortTransactionRequest { TransactionId = tx.TransactionId }, ct);
    }
    catch (ApiException)
    {
        // Even if abort call fails, TTL + validation guarantees the worker handles it
    }
    throw;
}
```

Note: The example above shows `ConfirmProvisionAsync` — a new endpoint that transitions a provision from `Pending` to `Provisioned`. This is the counterpart to `RegisterProvisionAsync` in the register-before-create flow. The API Endpoints section below includes both.

### Commit → Reference Bridge (Crash-Safe)

CommitTransaction uses a two-phase internal process to survive crashes mid-loop:

```csharp
// Inside ResourceService.CommitTransactionAsync (pseudocode)

// Phase 1: Transition to Committing (single atomic write — crash-safe checkpoint)
transaction.Status = TransactionStatus.Committing;
await _transactionStore.SaveAsync(BuildTransactionKey(transaction.TransactionId), transaction, ct);

// Phase 2: Register references one by one, marking each provision as it goes
foreach (var provision in transaction.Provisions.Where(p => p.Status == ProvisionStatus.Provisioned))
{
    await RegisterReferenceInternal(new RegisterReferenceRequest
    {
        ResourceType = transaction.ParentResourceType,
        ResourceId = transaction.ParentResourceId,
        SourceType = provision.ResourceType,
        SourceId = provision.ResourceId,
    }, ct);
    provision.Status = ProvisionStatus.ReferenceRegistered;
    await _provisionStore.SaveAsync(BuildProvisionKey(provision.ProvisionId), provision, ct);
}

// Phase 3: Transition to Committed (all provisions are now references)
transaction.Status = TransactionStatus.Committed;
await _transactionStore.SaveAsync(BuildTransactionKey(transaction.TransactionId), transaction, ct);
```

If the process crashes during Phase 2, the worker sees `Committing` state and resumes from the first provision that isn't `ReferenceRegistered`. Each provision's status update is individually persisted, so the resume point is precise.

After commit, the existing x-references cleanup infrastructure handles destruction. The transaction bridges the gap between "resources created" and "resources tracked by the reference system."

### Background Worker: TransactionRecoveryWorker

Standard BackgroundService following the canonical polling loop pattern (T6). One worker handles all recovery scenarios:

| Scan | Condition | Action |
|------|-----------|--------|
| **TTL validation** | `Active` past TTL, provision count not met | Auto-abort (orchestrator crashed before finishing) |
| **TTL validation** | `Active` past TTL, provision count met or unspecified | Execute `completionValidation` → 200 = auto-commit, 4xx = auto-abort, unreachable = increment `validationAttempts` and retry next cycle |
| **Validation exhausted** | `Active` past TTL, `validationAttempts >= TransactionValidationMaxRetries` | Publish `resource.transaction.validation-exhausted` error event, remain `Active` for admin |
| **Resume commit** | `Committing` with un-registered provisions, under max retries | Resume reference registration from last checkpoint |
| **Commit failed** | `Committing` with un-registered provisions, at max retries | Publish `resource.transaction.commit-failed` error event, transition to `Aborting` (compensate instead) |
| **Compensation retry** | `Aborting` with `CompensationFailed` provisions under max retries | Retry compensation with exponential backoff |
| **Compensation exhausted** | `Aborting` with `CompensationFailed` at max retries | Publish `resource.transaction.compensation-exhausted` error event, transition to `Aborted` |
| **Metadata retention** | `Committed`/`Aborted` past retention period | Purge transaction + provision records |

Configuration (resource-configuration.yaml):

```yaml
TransactionRecoveryWorkerIntervalSeconds:
  type: integer
  default: 30
  description: How often the worker scans for transactions needing recovery
TransactionRecoveryWorkerStartupDelaySeconds:
  type: integer
  default: 15
  description: Delay before first recovery cycle
TransactionCompensationMaxRetries:
  type: integer
  default: 10
  description: Maximum compensation retry attempts before giving up
TransactionCompensationBackoffBaseSeconds:
  type: integer
  default: 5
  description: Base delay for exponential backoff between compensation retries
TransactionCommitMaxRetries:
  type: integer
  default: 10
  description: Maximum retries for reference registration during Committing state
TransactionValidationMaxRetries:
  type: integer
  default: 5
  description: Maximum validation check attempts before escalating to admin
TransactionRetentionDays:
  type: integer
  default: 7
  description: Days to retain completed/aborted transaction metadata
TransactionDefaultTtlSeconds:
  type: integer
  default: 120
  description: Default TTL for transactions when caller does not specify
TransactionMaxTtlSeconds:
  type: integer
  default: 600
  description: Maximum allowed TTL — BeginTransaction clamps requested TTL to this value
```

### Destruction Side Fix

The destruction problem is simpler and does not require the full transaction mechanism. Two changes:

1. **T7 Strategy 3 reordering**: `ExecuteCleanupAsync` BEFORE `DeleteEntityRecordsAsync`. If cleanup fails, the entity still exists — return Conflict, caller retries. This is already mandated by T7 but Genesis does it backwards.

2. **Durable cleanup tracking**: Enhance `ExecuteCleanupAsync` to track which callbacks succeeded and which failed. Failed callbacks are retried by the same `TransactionRecoveryWorker` (or a separate but similar worker). Currently, `ExecuteCleanupAsync` swallows callback failures and logs warnings — the only change is making those failures durable and retryable instead of fire-and-forget.

---

## Required Change: Caller-Specified IDs on Create Endpoints

All Create endpoints for services that participate in transactions MUST accept an optional caller-specified GUID. This is structurally required by the register-before-create provision order — the ID must be known before the resource is created so the provision can be registered first.

### Why This Is Required (Not Optional)

The register-before-create flow (see "Why Pending → Provisioned matters" in Data Model) requires the resource ID to be known before the resource exists. The orchestrating service pre-generates the ID, registers the provision with that ID (status: Pending), then creates the resource using the pre-generated ID. Without caller-specified IDs, this flow is impossible — the service generates the ID during creation, which happens AFTER registration, creating a gap where the provision has no ID to compensate against.

### Why This Matters Beyond Transactions

- **Offline-to-online sync**: Sidecar/embedded mode creates entities with deterministic IDs. When syncing to cloud, idempotent creates prevent duplicates. This directly serves the deployment flexibility north star and the DEPLOYMENT-MODES planning doc.
- **Test determinism**: Tests can use known IDs for easier assertions.
- **Cross-service consistency**: Pre-generated IDs mean the orchestrating service knows all IDs before any provisioning call, enabling the entity record to be written first (with all IDs populated) if that pattern is ever preferred.

### Schema Pattern

Add to `common-api.yaml` as a shared pattern or per-service:

```yaml
# Each Create request model adds an optional GUID
CreateSeedRequest:
  properties:
    seedId:
      type: string
      format: uuid
      nullable: true
      description: >
        Optional caller-specified ID. If provided and a resource with this
        ID already exists, the existing resource is returned (idempotent).
        If not provided, the service generates a new ID.
```

### Structural Validation

A structural test validates that every Create endpoint's request model has an optional `Guid?` ID field and the service implementation handles both paths (use provided ID or generate new one).

---

## Resolved Design Decisions

### R1: Commit Failure Window → Resolved via Prebound Validation

**Problem**: If the orchestrator crashes between "entity saved" and "CommitTransaction called," TTL expiry would auto-abort a transaction whose entity was actually created successfully.

**Resolution**: The requester provides a `completionValidation` prebound API at `BeginTransaction` time. On TTL expiry, the worker executes the validation check before deciding. If the entity exists (200), auto-commit. If not, auto-abort. No hierarchy violation — Resource executes an opaque HTTP call via `IServiceNavigator`, same as cleanup/migrate callbacks. See "TTL Recovery via Prebound Validation" above.

### R2: Commit Atomicity → Resolved via `Committing` State

**Problem**: If the process crashes mid-loop while converting provisions to references during CommitTransaction, some provisions become references and others don't.

**Resolution**: CommitTransaction transitions to `Committing` first (single write), then registers references one by one (updating each provision to `ReferenceRegistered`), then transitions to `Committed`. The worker detects `Committing` and resumes from the first un-registered provision. See "Commit → Reference Bridge (Crash-Safe)" above.

### R3: Compensation Endpoint Availability → Prerequisites Before Phase 3

**Problem**: Seed has no individual delete API. Compensation requires an endpoint that actually removes the provisioned resource.

**Resolution**: Add a `DeleteSeedAsync` endpoint to Seed (hard delete of an individual seed and its growth/bond/cache data). This is a prerequisite for Phase 3 (Genesis migration), not a deferred concern. Currency already has `CloseWallet`. Inventory already has `DeleteContainer`. The Phase 1 implementation sequence is updated to include prerequisite endpoint additions.

### R4: Transaction Store Backend → MySQL/SQLite, Not In-Memory

**Problem**: In-memory provides zero crash recovery, which defeats the purpose.

**Resolution**: MySQL for cloud deployments. SQLite for sidecar/embedded (SQLite is durable and already supported by lib-state). In-memory mode provides structured compensation flow (immediate abort path) but explicitly does NOT provide crash recovery — acceptable because embedded mode crash = process restart from save state. The data model section documents this explicitly.

### R5: Nesting → Flat Only

Transactions are flat. No nesting. If a provisioning step itself needs to orchestrate sub-provisioning, it uses a separate transaction. The outer transaction's compensation for that step calls the entity's standard deletion flow (which triggers `ExecuteCleanupAsync` for any references registered by the inner transaction's commit). Compensation endpoints MUST NOT bypass `ExecuteCleanupAsync` with bare store deletes — this is what makes cascading cleanup work for nested orchestrations.

### R6: Compensation Endpoint Permissions → `x-permissions: []`

All compensation endpoints must be `x-permissions: []` (service-to-service only). Resource calls them via `IServiceNavigator` mesh routing. Most already have correct permissions. New compensation endpoints follow the same pattern.

### R7: T28 Account Deletion → No Interaction

Resource transactions are for provisioning, not deletion. Account-owned entities provisioned via transactions are tracked as normal references after commit; account deletion cleanup handlers handle them per T28 as usual.

### R8: Response Pattern → Match Existing Resource Convention

Resource currently uses a structured response pattern. New transaction endpoints follow the same convention used by existing Resource endpoints (per the implementation map) for consistency.

### R9: Transaction Lifecycle Events → Required by T5

Transaction state changes are meaningful. The events schema must include:
- `resource.transaction.created` — transaction begun (orchestration in progress)
- `resource.transaction.committed` — transaction successfully committed
- `resource.transaction.aborted` — transaction fully aborted (all compensations complete)
- `resource.transaction.auto-committed` — worker committed via validation check after TTL
- `resource.transaction.auto-aborted` — worker aborted after TTL validation failed
- `resource.transaction.commit-failed` — commit reference registration exhausted retries, falling back to abort
- `resource.transaction.compensation-exhausted` — compensation retries exhausted (error event)
- `resource.transaction.validation-exhausted` — TTL validation retries exhausted, requires admin intervention

### R10: Concurrency on State Transitions → Optimistic Concurrency via ETags

Concurrent requests to the same transaction (e.g., orchestrator calls Abort while worker auto-aborts) are handled by optimistic concurrency on the MySQL transaction record. `GetWithETagAsync` + `TrySaveAsync` on the transaction record — loser gets Conflict and retries or no-ops (state already transitioned). This is the same pattern used throughout the codebase for concurrent mutations.

### R11: Caller-Specified IDs Scope → Transaction-Participating Services First, Universal Eventually

Phase 2 adds optional IDs to services that participate in transactions (Seed, Currency, Inventory initially). The structural test is scoped to transaction-participating services in Phase 2. Universal rollout is a separate effort tracked independently, motivated by the offline-to-online sync requirement from DEPLOYMENT-MODES.md.

## Open Questions

1. **Scale profiling after implementation**: Transactions are used for complex multi-service orchestration (Genesis awakening, Craft completion, Market listing creation), not for every entity creation at scale. These are significant lifecycle events, not high-throughput hot paths. The transaction store should see low-to-moderate write volume even at 100K NPC scale. That said, actual load characteristics should be profiled after Phase 1 implementation to validate this assumption and establish baseline metrics. CREATE_ISSUE for load testing after Phase 1.

2. **Destruction durability scope**: Phase 4 proposes enhancing `ExecuteCleanupAsync` with durable callback tracking. The data model for tracking failed cleanup callbacks, the retry semantics, and whether the TransactionRecoveryWorker handles both responsibilities or a separate worker is needed — these require detailed design during Phase 4 planning, not Phase 1. The transaction infrastructure provides the pattern; destruction durability extends it.

---

## Implementation Sequence

### Phase 1: Resource Transaction Infrastructure

1. Add `resource-transactions` and `resource-provisions` to `state-stores.yaml`
2. Add transaction endpoints to `resource-api.yaml` (Begin, RegisterProvision, Commit, Abort, Status)
3. Add transaction models to `resource-api.yaml` (including `PreboundApiDefinition` fields for validation/verification)
4. Add transaction lifecycle events to `resource-service-events.yaml` (per R9)
5. Generate and implement in `ResourceService`
6. Implement `TransactionRecoveryWorker` with TTL validation, commit resume, compensation retry
7. Add configuration properties to `resource-configuration.yaml`
8. Unit tests for: transaction lifecycle, compensation, TTL expiry with validation, commit resume after crash, concurrent state transitions (ETag conflicts), retry with backoff

### Phase 1a: Prerequisite — Compensation Endpoints

Before transactions can be used by Genesis, the downstream services need actual compensation endpoints:

1. **Seed**: Add `POST /seed/delete` endpoint — hard delete of an individual seed plus its growth, bond, and capability cache data. This is the missing piece that makes seed compensation possible. (Issue #366 is related but focused on archive cleanup — the delete endpoint is a separate, simpler need.)
2. **Currency**: `CloseWallet` already exists — verify it's suitable as compensation (fully removes the wallet, idempotent on 404)
3. **Inventory**: `DeleteContainer` already exists — verify suitability and idempotency

All compensation endpoints must satisfy the three requirements in the Data Model section (idempotent, 404 = success, standard deletion flow).

### Phase 1b: Prerequisite — Caller-Specified IDs (Transaction-Participating Services)

The register-before-create provision order requires caller-specified IDs. This MUST precede Phase 3.

1. Add optional GUID field to Create request models for Seed, Currency, Inventory (Genesis's dependencies)
2. Implement idempotent create logic in each service (check-existing-by-ID-or-create)
3. Structural test: transaction-participating services' Create endpoints accept optional ID (scoped per R11)
4. Move `PreboundApiDefinition` to Core SDK (`sdks/core/`), add `x-sdk-type` reference support in schema generation

### Phase 2: Genesis Migration (Depends on Phase 1, 1a, 1b)

1. Replace `CompensateProvisioningAsync` with Resource transaction calls (BeginTransaction + RegisterProvision + ConfirmProvision + Commit/Abort)
2. Fix destruction ordering in `DestroyEntityCoreAsync`: `ExecuteCleanupAsync` BEFORE `DeleteEntityRecordsAsync` (per T7 Strategy 3)
3. Remove fabricated "owner reconciliation worker" XML documentation from `CompensateProvisioningAsync`
4. Update Genesis unit tests for transaction-based provisioning and corrected destruction ordering

### Phase 3: Destruction Durability (Detailed Design TBD)

1. Design the data model for tracking failed cleanup callbacks (may reuse provision store or separate)
2. Enhance `ExecuteCleanupAsync` to persist callback results — note that `success: false` semantics change from "final failure" to "failure will be retried," which affects existing callers
3. Implement retry for failed cleanup callbacks
4. Determine whether TransactionRecoveryWorker handles both or a dedicated worker is needed

### Phase 4: Helpers and Documentation (Human-Gated for Frozen Artifacts)

1. Shared `ResourceTransactionHelper` in `bannou-service/Services/` that trivializes the transaction pattern (similar to `UpdateWithRetryAsync` for state store operations)
2. *Human-gated*: Update HELPERS-AND-COMMON-PATTERNS.md with the canonical provisioning pattern
3. *Human-gated*: Update TENETS.md T7 multi-service compensation section to reference Resource transactions

---

## Affected Files

### New Files

| File | Purpose |
|------|---------|
| `schemas/state-stores.yaml` (additions) | `resource-transactions`, `resource-provisions` store definitions |
| `bannou-service/Services/ResourceTransactionHelper.cs` | Shared helper for trivial transaction usage |

### Modified Files

| File | Change |
|------|--------|
| `schemas/resource-api.yaml` | Transaction endpoints and models |
| `schemas/resource-configuration.yaml` | Worker configuration properties |
| `plugins/lib-resource/ResourceService.cs` | Transaction endpoint implementations |
| `plugins/lib-resource/ResourceServicePlugin.cs` | Worker registration |
| `plugins/lib-genesis/GenesisService.cs` | Replace compensation with transactions |
| `plugins/lib-genesis/GenesisService.Helpers.cs` | Remove `CompensateProvisioningAsync`, fix destruction ordering |
| `schemas/seed-api.yaml` | Optional `seedId` on CreateSeedRequest |
| `schemas/currency-api.yaml` | Optional `walletId` on CreateWalletRequest |
| `schemas/inventory-api.yaml` | Optional `containerId` on CreateContainerRequest |
| Each service with Create endpoints | Idempotent create logic (Phase 2, incremental) |

---

*This document is the design specification for Resource transactions. Updates require explicit approval.*
