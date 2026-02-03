# Contract Plugin Deep Dive

> **Plugin**: lib-contract
> **Schema**: schemas/contract-api.yaml
> **Version**: 1.0.0
> **State Stores**: contract-statestore (Redis)

---

## Overview

Binding agreement management between entities with milestone-based progression, consent flows, prebound API execution, breach handling, guardian custody, and clause-type extensibility. Contracts follow a reactive design principle: external systems tell contracts when conditions are met or failed via API calls; contracts store state, emit events, and execute prebound APIs on state transitions. Templates define the structure (party roles, milestones, terms, enforcement mode), and instances are created from templates with merged terms, party consent tracking, and sequential milestone progression. Integrates with lib-escrow for asset-backed contracts through the guardian locking system, template value substitution, and clause execution. Supports four enforcement modes (advisory, event_only, consequence_based, community), configurable consent deadlines with lazy expiration, ISO 8601 duration-based cure periods for breaches, and batched prebound API execution with configurable parallelism and timeouts.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for templates, instances, breaches, indexes, clause types, and idempotency cache |
| lib-state (`IDistributedLockProvider`) | Contract-instance locks for state transition serialization (60-second TTL); index-level locks for concurrent list modification safety (15-second TTL); milestone-check locks for background service (30-second TTL) |
| lib-messaging (`IMessageBus`) | Publishing contract lifecycle events, prebound API execution events, error events |
| lib-mesh (`IServiceNavigator`) | Executing prebound APIs (milestone callbacks, clause validation, clause execution) |
| lib-mesh (`IEventConsumer`) | Event consumer registration (reserved for future event subscriptions) |
| lib-location (`ILocationClient`) | Territory constraint checking via location hierarchy ancestry queries |

> **⚠️ SERVICE HIERARCHY VIOLATION**: Contract (L1 App Foundation) depends on Location (L2 Game Foundation). This violates the service hierarchy - L1 services cannot depend on L2. The dependency exists for territory constraint checking (`CheckContractConstraint` with `Territory` constraint type). **Remediation options**: (A) Remove location validation entirely, (B) Move territory clause logic to an L4 "ContractExtensions" service that subscribes to contract events, or (C) invert things so that location provides its own validation defition to the contract service (recommended).

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | Uses guardian lock/unlock to take custody during exchanges; binds contract instances with template values; checks contract fulfillment status before release; deposits execute template value setting and asset requirement checking |

---

## State Storage

**Stores**: 1 state store (Redis-backed)

| Store | Backend | Purpose |
|-------|---------|---------|
| `contract-statestore` | Redis | Templates, instances, breaches, clause types, indexes, and idempotency cache |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `template:{templateId}` | `ContractTemplateModel` | Template definition and configuration |
| `template-code:{code}` | `string` | Template ID lookup by code |
| `all-templates` | `List<string>` | All template IDs for listing |
| `instance:{contractId}` | `ContractInstanceModel` | Contract instance state and parties |
| `breach:{breachId}` | `BreachModel` | Breach record with status and deadlines |
| `party-idx:{entityType}:{entityId}` | `List<string>` | Contract IDs for a party entity |
| `template-idx:{templateId}` | `List<string>` | Contract IDs created from a template |
| `status-idx:{status}` | `List<string>` | Contract IDs in a given status |
| `clause-type:{typeCode}` | `ClauseTypeModel` | Clause type definition with handlers |
| `all-clause-types` | `List<string>` | All registered clause type codes |
| `idempotency:lock:{key}` | `LockContractResponse` | Idempotent lock response cache (24h TTL) |
| `idempotency:unlock:{key}` | `UnlockContractResponse` | Idempotent unlock response cache (24h TTL) |
| `idempotency:transfer:{key}` | `TransferContractPartyResponse` | Idempotent transfer response cache (24h TTL) |
| `idempotency:execute:{key}` | `ExecuteContractResponse` | Idempotent execution response cache (24h TTL) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `contract-template.created` | `ContractTemplateCreatedEvent` | Template created |
| `contract-template.updated` | `ContractTemplateUpdatedEvent` | Template name/description/active/metadata changed |
| `contract-template.deleted` | `ContractTemplateDeletedEvent` | Template soft-deleted (marked inactive) |
| `contract-instance.created` | `ContractInstanceCreatedEvent` | Instance created in draft status |
| `contract.proposed` | `ContractProposedEvent` | Contract proposed to parties (awaiting consent) |
| `contract.consent-received` | `ContractConsentReceivedEvent` | One party consents; includes remaining count |
| `contract.accepted` | `ContractAcceptedEvent` | All required parties have consented |
| `contract.activated` | `ContractActivatedEvent` | Contract becomes active (effectiveFrom reached) |
| `contract.milestone.completed` | `ContractMilestoneCompletedEvent` | Milestone completed; includes API execution count |
| `contract.milestone.failed` | `ContractMilestoneFailedEvent` | Milestone failed; indicates if breach triggered |
| `contract.breach.detected` | `ContractBreachDetectedEvent` | Breach reported with cure deadline if applicable |
| `contract.breach.cured` | `ContractBreachCuredEvent` | Breach cured within grace period |
| `contract.fulfilled` | `ContractFulfilledEvent` | All required milestones complete |
| `contract.terminated` | `ContractTerminatedEvent` | Contract terminated early by a party |
| `contract.prebound-api.executed` | `ContractPreboundApiExecutedEvent` | Prebound API call succeeded |
| `contract.prebound-api.failed` | `ContractPreboundApiFailedEvent` | Prebound API call failed (substitution or HTTP error) |
| `contract.prebound-api.validation-failed` | `ContractPreboundApiValidationFailedEvent` | Prebound API response failed validation rules |
| `contract.locked` | `ContractLockedEvent` | Contract locked under guardian custody |
| `contract.unlocked` | `ContractUnlockedEvent` | Contract released from guardian custody |
| `contract.party.transferred` | `ContractPartyTransferredEvent` | Party role transferred to new entity |
| `contract.clausetype.registered` | `ClauseTypeRegisteredEvent` | New clause type registered |
| `contract.templatevalues.set` | `ContractTemplateValuesSetEvent` | Template values set on contract instance |
| `contract.executed` | `ContractExecutedEvent` | Contract clauses executed (distributions made) |

### Consumed Events

This plugin does not consume external events. The events schema explicitly declares `x-event-subscriptions: []` noting the service is reactive (no external event subscriptions for v1).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultEnforcementMode` | `CONTRACT_DEFAULT_ENFORCEMENT_MODE` | `event_only` | Enforcement mode for templates that do not specify one |
| `DefaultConsentTimeoutDays` | `CONTRACT_DEFAULT_CONSENT_TIMEOUT_DAYS` | `7` | Days for parties to consent before proposal lazily expires |
| `MaxPartiesPerContract` | `CONTRACT_MAX_PARTIES_PER_CONTRACT` | `20` | Hard cap on party count per instance (overrides template) |
| `MaxMilestonesPerTemplate` | `CONTRACT_MAX_MILESTONES_PER_TEMPLATE` | `50` | Maximum milestones allowed in a template definition |
| `MaxPreboundApisPerMilestone` | `CONTRACT_MAX_PREBOUND_APIS_PER_MILESTONE` | `10` | Cap on onComplete/onExpire API lists per milestone |
| `MaxActiveContractsPerEntity` | `CONTRACT_MAX_ACTIVE_CONTRACTS_PER_ENTITY` | `100` | Maximum active contracts (Draft/Proposed/Pending/Active) per entity; 0 for unlimited |
| `PreboundApiBatchSize` | `CONTRACT_PREBOUND_API_BATCH_SIZE` | `10` | APIs executed concurrently per batch (sequential between batches) |
| `PreboundApiTimeoutMs` | `CONTRACT_PREBOUND_API_TIMEOUT_MS` | `30000` | Per-API timeout in milliseconds (30s default) |
| `ContractLockTimeoutSeconds` | `CONTRACT_LOCK_TIMEOUT_SECONDS` | `60` | Lock timeout for contract-level distributed locks |
| `IndexLockTimeoutSeconds` | `CONTRACT_INDEX_LOCK_TIMEOUT_SECONDS` | `15` | Lock timeout for index update distributed locks |
| `IndexLockFailureMode` | `CONTRACT_INDEX_LOCK_FAILURE_MODE` | `warn` | Behavior on index lock failure: `warn` (continue) or `fail` (throw) |
| `TermsMergeMode` | `CONTRACT_TERMS_MERGE_MODE` | `shallow` | How instance terms merge with template: `shallow` (replace by key) or `deep` (recursive) |
| `IdempotencyTtlSeconds` | `CONTRACT_IDEMPOTENCY_TTL_SECONDS` | `86400` | TTL for idempotency key storage (24 hours) |
| `MilestoneDeadlineCheckIntervalSeconds` | `CONTRACT_MILESTONE_DEADLINE_CHECK_INTERVAL_SECONDS` | `300` | Interval between milestone deadline checks (5 minutes) |
| `MilestoneDeadlineStartupDelaySeconds` | `CONTRACT_MILESTONE_DEADLINE_STARTUP_DELAY_SECONDS` | `30` | Startup delay before first milestone deadline check |
| `DefaultPageSize` | `CONTRACT_DEFAULT_PAGE_SIZE` | `20` | Default page size for paginated endpoints (cursor-based pagination) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ContractService>` | Scoped | Structured logging |
| `ContractServiceConfiguration` | Singleton | All 14 config properties (see Configuration table) |
| `IStateStoreFactory` | Singleton | Redis state store access |
| `IDistributedLockProvider` | Singleton | Contract-instance locks (`contract-instance`, 60s TTL) for mutation serialization; index-level locks (15s TTL) for list operations; milestone-check locks (30s TTL) for background service |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IServiceNavigator` | Scoped | Prebound API execution (milestone callbacks, clause execution) |
| `IEventConsumer` | Scoped | Event consumer registration (unused in v1) |
| `ILocationClient` | Scoped | Territory constraint checking via ancestry queries |
| `ContractMilestoneExpirationService` | Singleton (BackgroundService) | Periodic milestone deadline enforcement |

Service lifetime is **Scoped** (per-request). Background service `ContractMilestoneExpirationService` runs continuously. Partial classes split into:
- `ContractService.cs` - Core operations (templates, instances, milestones, breaches, metadata, constraints, helpers, event publishing, internal models)
- `ContractServiceEscrowIntegration.cs` - Guardian system, clause type system, execution system

---

## API Endpoints (Implementation Notes)

### Template Operations (5 endpoints)

- **CreateContractTemplate** (`/contract/template/create`): Validates milestone count against `MaxMilestonesPerTemplate`. Validates prebound API count per milestone against `MaxPreboundApisPerMilestone` (checks both onComplete and onExpire lists). Validates deadline format (ISO 8601 duration via `XmlConvert.ToTimeSpan`). Checks template code uniqueness via code index. Saves template, code-to-id mapping, and adds to all-templates list. Publishes `contract-template.created`.
- **GetContractTemplate** (`/contract/template/get`): Supports lookup by template ID or code (via code index). Returns full template with party roles, milestones, default terms, and enforcement mode.
- **ListContractTemplates** (`/contract/template/list`): Cursor-based pagination. Filters by realmId, isActive, and search term (case-insensitive name/description substring match). Returns templates ordered by CreatedAt descending. Request accepts optional `cursor` (opaque, from previous response) and `pageSize` (defaults to `DefaultPageSize`). Response includes `templates`, `nextCursor` (null if no more results), and `hasMore`.
- **UpdateContractTemplate** (`/contract/template/update`): Mutable fields only: name, description, isActive, gameMetadata. Tracks changed fields for event. Does not update milestones, party roles, or terms. Publishes `contract-template.updated` with changedFields list.
- **DeleteContractTemplate** (`/contract/template/delete`): Soft-delete (marks inactive). Checks for active instances first (Draft/Proposed/Pending/Active statuses). Returns Conflict if active instances exist. Publishes `contract-template.deleted`.

### Instance Operations (7 endpoints)

- **CreateContractInstance** (`/contract/instance/create`): Loads template, validates active. Checks party count against template min/max AND config hard cap. Checks `MaxActiveContractsPerEntity` per party via party indexes. Creates parties with Pending consent. Merges template default terms with request terms (instance overrides template, custom terms merged). Creates milestone instances from template definitions. Saves with Draft status. Updates template, status, and party indexes.
- **ProposeContractInstance** (`/contract/instance/propose`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Transitions Draft to Proposed. Uses ETag-based optimistic concurrency. Persists state first, then updates status indexes, then publishes `contract.proposed`.
- **ConsentToContract** (`/contract/instance/consent`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Guardian enforcement (Forbidden if locked). Requires Proposed status. Lazy expiration check: computes deadline from ProposedAt + DefaultConsentTimeoutDays, transitions to Expired if past. Validates party exists and has not already consented. Records consent with timestamp. On all-consented: transitions to Active (immediate) or Pending (future effectiveFrom). Activates first milestone on activation. Persists state first, then publishes consent-received, and conditionally accepted/activated.
- **GetContractInstance** (`/contract/instance/get`): Simple key lookup, returns full instance response.
- **QueryContractInstances** (`/contract/instance/query`): Cursor-based pagination. Uses party index, template index, or status index union based on provided filters. Requires at least one filter (partyEntityId+type, templateId, or statuses). Bulk loads, applies additional status/template filters. Request accepts optional `cursor` (opaque) and `pageSize` (defaults to `DefaultPageSize`). Response includes `contracts`, `nextCursor` (null if no more results), and `hasMore`. Returns BadRequest if no filter criterion provided.
- **TerminateContractInstance** (`/contract/instance/terminate`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Guardian enforcement (Forbidden if locked). Validates requesting entity is a party. Checks `Terms.BreachThreshold` and auto-terminates if active breach count exceeds threshold. Transitions to Terminated. ETag concurrency. Persists state first, then updates indexes, then publishes `contract.terminated`.
- **GetContractInstanceStatus** (`/contract/instance/get-status`): Aggregates milestone progress, pending consents, active breaches, and days until expiration. Breach IDs loaded in bulk with status filtering. Triggers lazy milestone deadline enforcement.

### Milestone Operations (3 endpoints)

- **CompleteMilestone** (`/contract/milestone/complete`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates milestone in Active or Pending state. Marks Completed. Activates next milestone by sequence. If all required milestones complete: transitions contract from Active to Fulfilled. ETag concurrency. Persists state first, then updates indexes, then executes onComplete prebound APIs in configured batch size (parallel within batch, sequential between batches), then publishes milestone completed event.
- **FailMilestone** (`/contract/milestone/fail`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates Active or Pending state. Required milestones get Failed status (triggers breach flag); optional milestones get Skipped. ETag concurrency. Persists state first, then executes onExpire prebound APIs in batches, then publishes milestone failed event with wasRequired and triggeredBreach flags.
- **GetMilestone** (`/contract/milestone/get`): Returns single milestone status by code within a contract. Triggers lazy milestone deadline enforcement: checks if milestone is overdue (ActivatedAt + ParseIsoDuration(Deadline) < now), and if overdue applies DeadlineBehavior (skip/warn/breach for optional, breach for required).

### Breach Operations (3 endpoints)

- **ReportBreach** (`/contract/breach/report`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Creates breach record with Detected or Cure_period status based on terms.GracePeriodForCure (parsed as ISO 8601 via XmlConvert.ToTimeSpan). Links breach ID to contract instance. Checks breach threshold and auto-terminates if exceeded. ETag concurrency on instance. Publishes `contract.breach.detected`.
- **CureBreach** (`/contract/breach/cure`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates breach in Detected or Cure_period state. Marks Cured with timestamp. ETag concurrency on breach. Publishes `contract.breach.cured` with cure evidence.
- **GetBreach** (`/contract/breach/get`): Simple key lookup by breach ID.

### Metadata Operations (2 endpoints)

- **UpdateContractMetadata** (`/contract/metadata/update`): Two metadata types: Instance_data (static game data) and RuntimeState (dynamic game state). ETag concurrency. No event published.
- **GetContractMetadata** (`/contract/metadata/get`): Returns both InstanceData and RuntimeState from the contract's GameMetadata.

### Constraint Operations (2 endpoints)

- **CheckContractConstraint** (`/contract/check-constraint`): Loads entity's active contracts. Checks custom terms for constraint-related flags based on ConstraintType:
  - **Exclusivity**: checks "exclusivity" custom term
  - **Non_compete**: checks "nonCompete" custom term
  - **Territory**: validates location hierarchy overlap with exclusive/inclusive modes via `ILocationClient.GetLocationAncestorsAsync`. Custom terms `territoryLocationIds`, `territoryMode`, and proposedAction `locationId` drive the constraint logic.
  - **Time_commitment**: detects conflicting exclusive time commitments across active contracts by checking date range overlaps for contracts with `timeCommitment: true` and `timeCommitmentType: exclusive`.
  Returns allowed=true with no conflicts, or allowed=false with conflicting contract summaries and reason.
- **QueryActiveContracts** (`/contract/query-active`): Loads entity's contracts, filters to Active status only. Optional templateCodes filter with wildcard prefix matching (TrimEnd('*') + StartsWith). Returns contract summaries with roles.

### Guardian Operations (3 endpoints)

- **LockContract** (`/contract/lock`): Idempotency check via cache key. Loads instance, validates template is transferable. Rejects if already locked (Conflict). Sets GuardianId, GuardianType, LockedAt. Caches response for idempotency (24h TTL). Publishes `contract.locked`.
- **UnlockContract** (`/contract/unlock`): Idempotency check. Validates currently locked and caller is the guardian (Forbidden otherwise). Clears guardian fields. Publishes `contract.unlocked`.
- **TransferContractParty** (`/contract/transfer-party`): Idempotency check. Validates locked and caller is guardian. Finds party by fromEntityId/fromEntityType. Updates party index (removes old, adds new). Transfers party identity. Publishes `contract.party.transferred`.

### Clause Type Operations (2 endpoints)

- **RegisterClauseType** (`/contract/clause-type/register`): Validates uniqueness. Saves clause type with optional validation and execution handler definitions (service/endpoint/mappings). Adds to all-clause-types list. Publishes `contract.clausetype.registered`. Four built-in types auto-registered on first ListClauseTypes call: asset_requirement, currency_transfer, item_transfer, fee.
- **ListClauseTypes** (`/contract/clause-type/list`): Ensures built-in types registered (lazy init). Filters by category and includeBuiltIn flag. Returns summaries.

### Execution Operations (3 endpoints)

- **SetContractTemplateValues** (`/contract/instance/set-template-values`): Validates key format (alphanumeric + underscore regex). Merges with existing template values (additive). Publishes `contract.templatevalues.set`.
- **CheckAssetRequirements** (`/contract/instance/check-asset-requirements`): Requires template values set. Parses clauses from template's CustomTerms["clauses"] JSON array. Filters for asset_requirement type. For each requirement: resolves check_location via template substitution, queries balance via registered handler (currency/balance/get for currency, inventory for items). Returns per-party satisfaction with current/required/missing amounts.
- **ExecuteContract** (`/contract/instance/execute`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Uses ETag-based optimistic concurrency (`GetWithETagAsync` + `TrySaveAsync`). Idempotency via both instance's ExecutedAt field and explicit idempotency key cache. Requires Fulfilled status. Requires template values set. Parses clauses, separates into fees (execute first) and distributions (execute second). Each clause: loads clause type handler, builds transfer payload with template substitution, supports flat/percentage/remainder amount types. Remainder queries source wallet balance. Currency uses /currency/transfer; items use /inventory/transfer. Records distributions. Marks ExecutedAt. Publishes `contract.executed`.

---

## Visual Aid

```
Contract State Machine
========================

  CREATE         PROPOSE        CONSENT (all)
    │               │               │
    ▼               ▼               ▼
 ┌──────┐     ┌──────────┐    ┌─────────┐
 │ Draft │────►│ Proposed │───►│ Pending │ (if effectiveFrom > now)
 └──────┘     └──────────┘    └─────────┘
                    │               │
                    │ all consent   │ effectiveFrom reached
                    │ + no future   │
                    │ effectiveFrom │
                    ▼               ▼
               ┌─────────┐    ┌─────────┐
               │  Active  │◄───│ Pending │
               └─────────┘    └─────────┘
                    │
                    │ all required
                    │ milestones complete
                    ▼
               ┌───────────┐     EXECUTE
               │ Fulfilled │────────────► Distributions applied
               └───────────┘

    Consent timeout (lazy check):
    Proposed ──(DefaultConsentTimeoutDays expired)──► Expired

    Party request:
    Draft/Proposed/Active ──(TerminateContractInstance)──► Terminated


Milestone Progression
========================

  Template Milestones: [M1, M2, M3]  (M1.Required=true, M2.Required=true, M3.Required=false)

  Contract Activation
       │
       ▼
  M1: Pending ──(activate)──► Active
       │
       ├── CompleteMilestone(M1)
       │    ├── onComplete APIs executed (batched)
       │    ├── M1 → Completed
       │    └── M2 → Active (next activated)
       │
       └── FailMilestone(M1)
            ├── M1.Required=true → Failed (triggeredBreach=true)
            └── onExpire APIs executed

  M2: Active
       │
       ├── CompleteMilestone(M2)
       │    ├── All required complete? → Contract: Active → Fulfilled
       │    └── M3 → Active
       │
       └── FailMilestone(M2)
            └── M2.Required=true → Failed

  M3: Active (optional)
       │
       └── FailMilestone(M3)
            └── M3.Required=false → Skipped (no breach)


Milestone Deadline Enforcement
================================

  Background Service (ContractMilestoneExpirationService):
       │
       ├── Every MilestoneDeadlineCheckIntervalSeconds (default 300s)
       ├── Load all contracts from status-idx:active
       ├── For each contract:
       │    ├── Acquire milestone-check:{contractId} lock (30s)
       │    └── For each Active milestone with Deadline:
       │         ├── Compute absoluteDeadline = ActivatedAt + ParseIsoDuration(Deadline)
       │         └── If now > absoluteDeadline → invoke GetMilestoneAsync (triggers lazy enforcement)
       │
       └── Lazy enforcement also triggers on:
            ├── GetMilestoneAsync (per-milestone check)
            └── GetContractInstanceStatusAsync (all milestones)


Consent Flow
========================

  ProposeContractInstance
       │
       ▼
  Parties: [A: Pending, B: Pending, C: Pending]
       │
       ├── ConsentToContract(A) → [A: Consented, B: Pending, C: Pending]
       │    └── Publishes consent-received (remaining=2)
       │
       ├── ConsentToContract(B) → [A: Consented, B: Consented, C: Pending]
       │    └── Publishes consent-received (remaining=1)
       │
       └── ConsentToContract(C) → [A: Consented, B: Consented, C: Consented]
            ├── Publishes consent-received (remaining=0)
            ├── Publishes contract.accepted
            ├── If effectiveFrom <= now: Status → Active, publishes contract.activated
            └── If effectiveFrom > now: Status → Pending

  Lazy Expiration:
       Any consent attempt after (ProposedAt + DefaultConsentTimeoutDays)
       → Contract transitions to Expired, returns BadRequest

  Guardian Enforcement:
       If contract.GuardianId is set → ConsentToContract returns Forbidden


Guardian Custody & Escrow Integration
========================================

  Escrow creates agreement with boundContractId
       │
       ▼
  LockContract(guardianId=escrowId)
       ├── Validates template.Transferable=true
       ├── Validates not already locked (Conflict)
       ├── Sets GuardianId, GuardianType, LockedAt
       └── Locked contracts CANNOT be:
            • Consented to (Forbidden)
            • Terminated (Forbidden)

  SetContractTemplateValues(...)
       └── Provides runtime values for clause execution
            (e.g., wallet IDs, amounts, currency codes)

  CheckAssetRequirements(...)
       └── Queries actual balances via clause type handlers

  ExecuteContract(...)
       ├── Requires Fulfilled status
       ├── Fees execute FIRST (deducted from source wallets)
       ├── Distributions execute SECOND
       └── Idempotent (returns cached result on re-call)

  UnlockContract(guardianId=escrowId)
       └── Verifies caller is current guardian (Forbidden otherwise)


Clause Execution Pipeline
============================

  ExecuteContract(contractId)
       │
       ├── Load template → parse CustomTerms["clauses"] JSON array
       │
       ├── Separate by type:
       │    ├── fee clauses      → Execute first (order preserved)
       │    └── distribution/    → Execute second
       │        currency_transfer/
       │        item_transfer
       │
       ├── For each clause:
       │    │
       │    ├── Resolve clause type → find ExecutionHandler
       │    │    ├── "fee" → uses "fee" clause type (service=currency, endpoint=/currency/transfer)
       │    │    ├── "distribution" → infers currency_transfer or item_transfer
       │    │    │    based on presence of source_container property
       │    │    ├── "currency_transfer" → registered handler
       │    │    └── "item_transfer" → registered handler
       │    │
       │    ├── Build payload:
       │    │    ├── Resolve {{TemplateKey}} in source/destination wallets/containers
       │    │    ├── ParseClauseAmount:
       │    │    │    ├── "flat" → literal amount
       │    │    │    ├── "percentage" → floor(base_amount * amount / 100)
       │    │    │    └── "remainder" → queries source wallet balance at execution time
       │    │    └── Serialize payload for handler endpoint
       │    │
       │    ├── Execute via ServiceNavigator
       │    │    ├── Success (200) → record distribution (Succeeded=true)
       │    │    └── Failure → publish contract.prebound-api.failed, record failure (Succeeded=false)
       │    │
       │    └── Record DistributionRecordModel (always, including failures)
       │
       └── Save execution state (ExecutedAt, ExecutionIdempotencyKey, ExecutionDistributions)


Breach Handling
=================

  ReportBreach(contractId, breachingEntityId, breachType)
       │
       ├── Check terms.GracePeriodForCure (ISO 8601 via XmlConvert.ToTimeSpan)
       │    ├── Present → Status: Cure_period, CureDeadline = now + duration
       │    └── Absent → Status: Detected
       │
       ├── Save breach record (BREACH_PREFIX + breachId)
       ├── Link breachId to contract.BreachIds
       ├── Check breach threshold → auto-terminate if exceeded
       └── Publish contract.breach.detected

  CureBreach(breachId, cureEvidence)
       │
       ├── Validate Status: Detected or Cure_period
       ├── Mark Cured with timestamp
       └── Publish contract.breach.cured

  GetContractInstanceStatus includes:
       └── Active breaches = BreachIds where status in [Detected, Cure_period]


Prebound API Batched Execution
=================================

  ExecutePreboundApisBatchedAsync(apis, batchSize=10)
       │
       ├── Batch 1: apis[0..9] → Task.WhenAll (parallel)
       ├── Batch 2: apis[10..19] → Task.WhenAll (parallel)
       └── ...

  Each API:
       ├── CancellationTokenSource with PreboundApiTimeoutMs
       ├── BuildContractContext (contract.id, parties, terms, metadata)
       ├── ExecutePreboundApiAsync via ServiceNavigator
       │    ├── SubstitutionFailed → publish failed event
       │    ├── ResponseValidation rules → publish validation-failed event
       │    └── Success → publish executed event
       └── Exception → publish failed event (non-fatal, continues)
```

---

## Stubs & Unimplemented Features

1. **ContractSummary.TemplateName**: Always returned as null in QueryActiveContracts and CheckConstraint responses with comment "Would need to load template". Template name is not resolved for summary queries.
2. **Clause validation handler request/response mappings**: `ClauseHandlerModel.RequestMapping` and `ResponseMapping` are stored but never used in actual validation/execution logic.
3. **Contract expiration lifecycle**: The `ContractExpiredEvent` schema exists but no background job or scheduled check transitions Active contracts to Expired when effectiveUntil is reached. Expiration only occurs lazily on consent timeout.
4. **Payment schedule/frequency enforcement**: PaymentSchedule and PaymentFrequency terms are stored but no scheduled payment logic or enforcement exists.

---

## Potential Extensions

1. **Active expiration job**: Background service that periodically scans Active contracts for effectiveUntil < now and transitions them to Expired with event publication.
2. **Clause type handler chaining**: Allow clause types with both validation AND execution handlers to validate before executing, with configurable failure behavior.
3. **Template inheritance**: Allow templates to extend other templates, inheriting milestones, terms, and party roles with overrides.
4. **Bulk contract operations**: Batch creation from a single template for multi-entity scenarios (e.g., guild-wide contracts).

---

## Duration Format Reference

Contract service uses ISO 8601 durations for milestone deadlines and breach cure periods. Parsing is handled by `System.Xml.XmlConvert.ToTimeSpan`.

**Supported Formats**:
| Format | Example | Meaning |
|--------|---------|---------|
| `PnD` | `P10D` | 10 days |
| `PnM` | `P3M` | 3 months |
| `PnY` | `P1Y` | 1 year |
| `PTnH` | `PT12H` | 12 hours |
| `PTnM` | `PT30M` | 30 minutes |
| `PTnS` | `PT90S` | 90 seconds |
| Combined | `P1DT12H` | 1 day, 12 hours |
| Combined | `P1Y2M3DT4H5M6S` | 1 year, 2 months, 3 days, 4 hours, 5 minutes, 6 seconds |

**Error Handling**: Invalid formats silently return null, resulting in no deadline/cure period being set. The milestone or breach proceeds without time constraints.

---

## Milestone Deadline Architecture

The contract service uses a **hybrid lazy + background** approach for milestone deadline enforcement.

### Why This Design?

| Approach | Pros | Cons |
|----------|------|------|
| **Pure polling** | Guaranteed timing | Expensive (scans all contracts continuously) |
| **Pure lazy** | Zero overhead when idle | Stale milestones if never accessed |
| **Hybrid** (current) | Low overhead + eventual guarantee | Slight delay for unaccessed milestones |

### How It Works

1. **Primary: Lazy Enforcement** - When `GetMilestone` or `GetContractInstanceStatus` is called, the service checks if any active milestones are overdue and applies the configured `DeadlineBehavior`.

2. **Backup: Background Service** - `ContractMilestoneExpirationService` runs every `MilestoneDeadlineCheckIntervalSeconds` (default 5 minutes), scanning active contracts and triggering lazy enforcement for overdue milestones.

### Deadline Behavior

When a milestone deadline is exceeded:

| Milestone Type | DeadlineBehavior | Result |
|----------------|------------------|--------|
| Required | (always) | Milestone fails, breach triggered |
| Optional | `skip` | Milestone marked Skipped |
| Optional | `warn` | Milestone stays Active, warning logged |
| Optional | `breach` | Milestone fails, breach triggered |

---

## Partial Execution & Reconciliation

Contract clause execution is **intentionally non-transactional**. Cross-service transactions are avoided because:
- They require distributed coordination (2PC, sagas)
- They couple services together
- They reduce availability

### Failure Handling Pattern

When a clause execution fails:
1. Previous successful clauses are NOT rolled back
2. The failure is recorded in `ContractExecutedEvent.distributionResults` with `succeeded: false`
3. The contract is still marked as executed (ExecutedAt set)
4. Escrow can correlate failures via `clauseId` to its template value mappings

### Reconciliation Responsibility

**Escrow** handles reconciliation for contract-bound agreements:
- Receives `contract.executed` event with per-clause results
- Matches `clauseId` to the template values it set during deposit
- For partial failures: may hold funds, initiate refunds, or escalate to arbiter

**External systems** consuming contract events should:
- Check `distributionResults` for failures
- Implement compensating actions if needed
- Use idempotency keys to safely retry

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None currently tracked.*

### Intentional Quirks (Documented Behavior)

1. **Consent expiration is lazy**: Contracts do not automatically expire when `DefaultConsentTimeoutDays` passes. The check only triggers on the next `ConsentToContract` call. This avoids background job overhead for checking all Proposed contracts. Proposed contracts with no further consent attempts remain in Proposed status harmlessly until accessed.

2. **Index lock failure is configurable**: When `AddToListAsync`/`RemoveFromListAsync` cannot acquire the 15-second distributed lock, behavior is controlled by `IndexLockFailureMode` configuration. Default is `warn` (logs warning and continues), or `fail` (throws exception). See `CONTRACT_INDEX_LOCK_FAILURE_MODE` environment variable.

3. **Serial clause execution within type category**: Fee clauses and distribution clauses execute sequentially (clause-by-clause), not in parallel. This prevents race conditions on shared wallets. Parallel execution would require distributed locks per-wallet, adding complexity.

4. **Prebound API failures are non-blocking**: Failed prebound API executions (milestone callbacks) publish failure events but do not fail the milestone completion. The milestone is still marked complete. This is intentional to avoid coupling milestone state to external service availability.

5. **Template terms merge is configurable**: `MergeTerms` behavior is controlled by `TermsMergeMode` configuration. Default is `shallow` (single-level merge, dictionary values replaced by key) or `deep` (recursive merge of nested dictionaries). See `CONTRACT_TERMS_MERGE_MODE` environment variable.

### Design Considerations (Requires Planning)

1. **Per-milestone onApiFailure flag** ([#246](https://github.com/beyond-immersion/bannou-service/issues/246)): Currently prebound API failures are always non-blocking. Adding a per-milestone flag would require API schema changes (new field on MilestoneDefinition), model regeneration, and careful design of retry semantics. Requires design discussion before implementation.

2. ~~**Cursor-based pagination** ([#247](https://github.com/beyond-immersion/bannou-service/issues/247))~~: **IMPLEMENTED**. `ListContractTemplates` and `QueryContractInstances` now use cursor-based pagination with opaque cursor tokens. Cursors encode offset for forward compatibility. State store has Redis Search enabled for future filtered queries at scale. Configuration property `DefaultPageSize` controls default page size (20).

---

## Work Tracking

This section tracks active development work using AUDIT markers.

### Active Work

*None currently tracked.*

### Completed

- **2026-02-01**: Quirks analysis and issue creation. Created issues #241-#247. Reorganized documentation with new sections for Duration Format Reference, Milestone Deadline Architecture, and Partial Execution & Reconciliation.
- **2026-02-01**: Issue #218 - Added per-clause distribution details to `ContractExecutedEvent`.
- **2026-02-01**: Issue #217 - Moved 6 escrow integration events from inline definitions to schema.
- **2026-02-01**: Implemented territory constraint checking via `ILocationClient`.
- **2026-02-01**: Implemented time commitment constraint checking.
- **2026-02-01**: Implemented milestone deadline computation and enforcement with `ContractMilestoneExpirationService`.
- **2026-02-01**: Implemented breach threshold enforcement.
- **2026-01-31**: Fixed T25 violation for DistributionRecordModel (superseded by #218).
