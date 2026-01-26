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
| lib-state (`IDistributedLockProvider`) | Contract-instance locks for state transition serialization (60-second TTL); index-level locks for concurrent list modification safety (15-second TTL) |
| lib-messaging (`IMessageBus`) | Publishing contract lifecycle events, prebound API execution events, error events |
| lib-mesh (`IServiceNavigator`) | Executing prebound APIs (milestone callbacks, clause validation, clause execution) |
| lib-mesh (`IEventConsumer`) | Event consumer registration (reserved for future event subscriptions) |

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
| `MaxActiveContractsPerEntity` | `CONTRACT_MAX_ACTIVE_CONTRACTS_PER_ENTITY` | `100` | Max active contracts per entity (0 = unlimited) |
| `PreboundApiBatchSize` | `CONTRACT_PREBOUND_API_BATCH_SIZE` | `10` | APIs executed concurrently per batch (sequential between batches) |
| `PreboundApiTimeoutMs` | `CONTRACT_PREBOUND_API_TIMEOUT_MS` | `30000` | Per-API timeout in milliseconds (30s default) |
| `ClauseValidationCacheStalenessSeconds` | `CONTRACT_CLAUSE_VALIDATION_CACHE_STALENESS_SECONDS` | `15` | How old a cached validation result can be before revalidation |
| `ContractLockTimeoutSeconds` | `CONTRACT_LOCK_TIMEOUT_SECONDS` | `60` | Lock timeout for contract-level distributed locks |
| `IndexLockTimeoutSeconds` | `CONTRACT_INDEX_LOCK_TIMEOUT_SECONDS` | `15` | Lock timeout for index update distributed locks |
| `IdempotencyTtlSeconds` | `CONTRACT_IDEMPOTENCY_TTL_SECONDS` | `86400` | TTL for idempotency key storage (24 hours) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ContractService>` | Scoped | Structured logging |
| `ContractServiceConfiguration` | Singleton | All 9 config properties |
| `IStateStoreFactory` | Singleton | Redis state store access |
| `IDistributedLockProvider` | Singleton | Contract-instance locks (`contract-instance`, 60s TTL) for mutation serialization; index-level locks (15s TTL) for list operations |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IServiceNavigator` | Scoped | Prebound API execution (milestone callbacks, clause execution) |
| `IEventConsumer` | Scoped | Event consumer registration (unused in v1) |

Service lifetime is **Scoped** (per-request). No background services. Partial classes split into:
- `ContractService.cs` - Core operations (templates, instances, milestones, breaches, metadata, constraints, helpers, event publishing, internal models)
- `ContractServiceClauseValidation.cs` - Clause validation with ConcurrentDictionary-based cache
- `ContractServiceEscrowIntegration.cs` - Guardian system, clause type system, execution system

---

## API Endpoints (Implementation Notes)

### Template Operations (5 endpoints)

- **CreateContractTemplate** (`/contract/template/create`): Validates milestone count against `MaxMilestonesPerTemplate`. Validates prebound API count per milestone against `MaxPreboundApisPerMilestone` (checks both onComplete and onExpire lists). Checks template code uniqueness via code index. Saves template, code-to-id mapping, and adds to all-templates list. Publishes `contract-template.created`.
- **GetContractTemplate** (`/contract/template/get`): Supports lookup by template ID or code (via code index). Returns full template with party roles, milestones, default terms, and enforcement mode.
- **ListContractTemplates** (`/contract/template/list`): Loads all templates via bulk get. Filters by realmId, isActive, and search term (case-insensitive name/description substring match). Paginated with OrderByDescending(CreatedAt).
- **UpdateContractTemplate** (`/contract/template/update`): Mutable fields only: name, description, isActive, gameMetadata. Tracks changed fields for event. Does not update milestones, party roles, or terms. Publishes `contract-template.updated` with changedFields list.
- **DeleteContractTemplate** (`/contract/template/delete`): Soft-delete (marks inactive). Checks for active instances first (Draft/Proposed/Pending/Active statuses). Returns Conflict if active instances exist. Publishes `contract-template.deleted`.

### Instance Operations (7 endpoints)

- **CreateContractInstance** (`/contract/instance/create`): Loads template, validates active. Checks party count against template min/max AND config hard cap. Checks `MaxActiveContractsPerEntity` per party via party indexes. Creates parties with Pending consent. Merges template default terms with request terms (instance overrides template, custom terms merged). Creates milestone instances from template definitions. Saves with Draft status. Updates template, status, and party indexes.
- **ProposeContractInstance** (`/contract/instance/propose`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Transitions Draft to Proposed. Uses ETag-based optimistic concurrency. Persists state first, then updates status indexes, then publishes `contract.proposed`.
- **ConsentToContract** (`/contract/instance/consent`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Guardian enforcement (Forbidden if locked). Requires Proposed status. Lazy expiration check: computes deadline from ProposedAt + DefaultConsentTimeoutDays, transitions to Expired if past. Validates party exists and has not already consented. Records consent with timestamp. On all-consented: transitions to Active (immediate) or Pending (future effectiveFrom). Activates first milestone on activation. Persists state first, then publishes consent-received, and conditionally accepted/activated.
- **GetContractInstance** (`/contract/instance/get`): Simple key lookup, returns full instance response.
- **QueryContractInstances** (`/contract/instance/query`): Uses party index, template index, or status index union based on provided filters. Requires at least one filter (partyEntityId+type, templateId, or statuses). Bulk loads, applies additional status/template filters, paginates. Requires at least one filter criterion or returns BadRequest.
- **TerminateContractInstance** (`/contract/instance/terminate`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Guardian enforcement (Forbidden if locked). Validates requesting entity is a party. Transitions to Terminated. ETag concurrency. Persists state first, then updates indexes, then publishes `contract.terminated`.
- **GetContractInstanceStatus** (`/contract/instance/get-status`): Aggregates milestone progress, pending consents, active breaches, and days until expiration. Breach IDs loaded in bulk with status filtering.

### Milestone Operations (3 endpoints)

- **CompleteMilestone** (`/contract/milestone/complete`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates milestone in Active or Pending state. Marks Completed. Activates next milestone by sequence. If all required milestones complete: transitions contract from Active to Fulfilled. ETag concurrency. Persists state first, then updates indexes, then executes onComplete prebound APIs in configured batch size (parallel within batch, sequential between batches), then publishes milestone completed event.
- **FailMilestone** (`/contract/milestone/fail`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates Active or Pending state. Required milestones get Failed status (triggers breach flag); optional milestones get Skipped. ETag concurrency. Persists state first, then executes onExpire prebound APIs in batches, then publishes milestone failed event with wasRequired and triggeredBreach flags.
- **GetMilestone** (`/contract/milestone/get`): Returns single milestone status by code within a contract.

### Breach Operations (3 endpoints)

- **ReportBreach** (`/contract/breach/report`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Creates breach record with Detected or Cure_period status based on terms.GracePeriodForCure (parsed as ISO 8601 "PnD" format). Links breach ID to contract instance. ETag concurrency on instance. Publishes `contract.breach.detected`.
- **CureBreach** (`/contract/breach/cure`): Acquires `contract-instance` distributed lock on `{contractId}` (60s TTL). Validates breach in Detected or Cure_period state. Marks Cured with timestamp. ETag concurrency on breach. Publishes `contract.breach.cured` with cure evidence.
- **GetBreach** (`/contract/breach/get`): Simple key lookup by breach ID.

### Metadata Operations (2 endpoints)

- **UpdateContractMetadata** (`/contract/metadata/update`): Two metadata types: Instance_data (static game data) and RuntimeState (dynamic game state). ETag concurrency. No event published.
- **GetContractMetadata** (`/contract/metadata/get`): Returns both InstanceData and RuntimeState from the contract's GameMetadata.

### Constraint Operations (2 endpoints)

- **CheckContractConstraint** (`/contract/check-constraint`): Loads entity's active contracts. Checks custom terms for constraint-related flags based on ConstraintType: Exclusivity (checks "exclusivity" custom term), Non_compete (checks "nonCompete" custom term), Territory and Time_commitment (stubs - not implemented). Returns allowed=true with no conflicts, or allowed=false with conflicting contract summaries and reason.
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
       │    │    ├── Success (200) → record distribution
       │    │    └── Failure → publish contract.prebound-api.failed, skip clause
       │    │
       │    └── Record DistributionRecordModel
       │
       └── Save execution state (ExecutedAt, ExecutionIdempotencyKey, ExecutionDistributions)


Breach Handling
=================

  ReportBreach(contractId, breachingEntityId, breachType)
       │
       ├── Check terms.GracePeriodForCure (ISO 8601 "PnD")
       │    ├── Present → Status: Cure_period, CureDeadline = now + days
       │    └── Absent → Status: Detected
       │
       ├── Save breach record (BREACH_PREFIX + breachId)
       ├── Link breachId to contract.BreachIds
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

1. **Territory constraint checking**: The `ConstraintType.Territory` case in CheckContractConstraint is a stub with only a comment "Would need to check territory overlap with proposed action". No actual territory logic exists.
2. **Time commitment constraint checking**: The `ConstraintType.Time_commitment` case is similarly stubbed with no implementation.
3. **Milestone deadline computation**: `MilestoneInstanceResponse.Deadline` is always returned as null with comment "Would need to compute absolute deadline". No deadline enforcement or expiration logic exists for milestones.
4. **ContractSummary.TemplateName**: Always returned as null in QueryActiveContracts and CheckConstraint responses with comment "Would need to load template". Template name is not resolved for summary queries.
5. **Clause validation handler request/response mappings**: `ClauseHandlerModel.RequestMapping` and `ResponseMapping` are stored but never used in actual validation/execution logic.
6. **Contract expiration lifecycle**: The `ContractExpiredEvent` schema exists but no background job or scheduled check transitions Active contracts to Expired when effectiveUntil is reached. Expiration only occurs lazily on consent timeout.
7. **Breach threshold enforcement**: `Terms.BreachThreshold` is stored but not used to automatically terminate contracts after N breaches.
8. **Payment schedule/frequency enforcement**: PaymentSchedule and PaymentFrequency terms are stored but no scheduled payment logic or enforcement exists.

---

## Potential Extensions

1. **Active expiration job**: Background service that periodically scans Active contracts for effectiveUntil < now and transitions them to Expired with event publication.
2. **Milestone deadline enforcement**: Compute absolute deadlines from ISO 8601 durations relative to contract activation or previous milestone completion. Fail milestones automatically when deadline passes.
3. **Territory and time constraint implementations**: Integrate with location/mapping service for territory overlap detection. Implement time commitment tracking for scheduling conflicts.
4. **Breach escalation pipeline**: Auto-terminate contracts when BreachThreshold is exceeded. Configurable escalation (cure_period -> consequence -> termination).
5. **Clause type handler chaining**: Allow clause types with both validation AND execution handlers to validate before executing, with configurable failure behavior.
6. **Template inheritance**: Allow templates to extend other templates, inheriting milestones, terms, and party roles with overrides.
7. **Bulk contract operations**: Batch creation from a single template for multi-entity scenarios (e.g., guild-wide contracts).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **T25 (Internal POCO uses string for enum)**: `DistributionRecordModel.AssetType` in ContractServiceEscrowIntegration uses string instead of the `AssetType` enum from escrow-api.yaml.

Note: `PartyModel.Role` and similar Role properties are intentionally strings - they represent user-defined template roles (e.g., "buyer", "seller", "guarantor") not a fixed enum.

### Intentional Quirks (Documented Behavior)

1. **Consent expiration is lazy**: Contracts do not automatically expire when DefaultConsentTimeoutDays passes. The check only triggers on the next ConsentToContract call. A proposed contract with no further consent attempts will remain in Proposed status indefinitely.

2. **Template deletion is soft-delete**: DeleteContractTemplate marks the template inactive rather than removing it. The template data remains accessible. Templates with active instances (Draft/Proposed/Pending/Active) cannot be deleted.

3. **Index lock failure is non-fatal**: When AddToListAsync/RemoveFromListAsync cannot acquire the 15-second distributed lock, the operation logs a warning but returns without error. The index may become stale.

4. **Distributed lock + optimistic concurrency**: All mutating methods (ProposeContract, Consent, Terminate, CompleteMilestone, FailMilestone, ReportBreach, CureBreach, ExecuteContract) acquire a `contract-instance` distributed lock (60s TTL) before reading state, and additionally use ETag-based optimistic concurrency on save. Lock acquisition failure returns Conflict (409).

5. **Guardian blocks consent and termination**: A locked contract returns Forbidden for both ConsentToContract and TerminateContractInstance. The guardian (typically an escrow) has exclusive control.

6. **Execution is idempotent via dual mechanisms**: ExecuteContract checks both the contract's ExecutedAt field (permanent) and an explicit idempotency key cache (24h TTL). A re-execution returns the cached distributions without re-calling external services.

7. **Fee clauses execute before distributions**: In ExecuteContract, fee-type clauses always execute first to ensure fees are deducted before distribution amounts are calculated. Distribution clauses that use "remainder" amount will see the post-fee balance.

8. **Remainder amount resolution queries at execution time**: When a clause specifies amount="remainder", the service queries the source wallet's current balance via the currency service at execution time. This means the remainder is dynamic, not pre-computed.

9. **Template value substitution uses {{Key}} syntax**: Values in clause definitions (source_wallet, destination_wallet, etc.) support `{{TemplateKey}}` patterns that are resolved from the contract's TemplateValues dictionary at execution time.

10. **GracePeriodForCure only supports "PnD" format**: The ISO 8601 duration parser is simplified to only handle "P" prefix and "D" suffix (days). Other duration formats (hours, months, etc.) are silently ignored, resulting in no cure deadline.

### Design Considerations (Requires Planning)

1. **N+1 pattern for listing**: ListContractTemplates and QueryContractInstances load all keys from an index list, then bulk-fetch all entries. For large deployments, these lists can grow unbounded.

2. **No pagination on index operations**: Party indexes and status indexes are unbounded lists. An entity with many contracts will have a large party-idx list that is loaded entirely on each query.

3. **In-memory clause validation cache is effectively request-scoped**: The `ConcurrentDictionary<string, CachedValidationResult>` in ContractServiceClauseValidation is an instance field on a Scoped service (`ServiceLifetime.Scoped`). Each HTTP request gets a new service instance with a fresh cache. The cache provides no cross-request benefit — it only helps within a single `ValidateAllClausesAsync` call if the same clause appears multiple times (unlikely). The staleness threshold configuration (`ClauseValidationCacheStalenessSeconds`) is effectively dead configuration since the cache never persists between requests.

4. **Clause execution is not transactional**: If one clause in ExecuteContract fails, previously executed clauses are NOT rolled back. The distributions list will be partial, and the contract is still marked as executed with whatever succeeded.

5. **Template terms merge is shallow**: MergeTerms performs a single-level merge. CustomTerms dictionary merge is also shallow (instance values override template values by key, no deep merge of nested objects).

6. **Prebound API failures are non-blocking**: Failed prebound API executions (milestone callbacks) publish failure events but do not fail the milestone completion. The milestone is still marked complete even if its onComplete APIs fail.

7. **Serial clause execution within type category**: Fee clauses and distribution clauses each execute sequentially (clause-by-clause), not in parallel. Only the APIs within a single milestone's callback list are batched in parallel.

8. **QueryActiveContracts wildcard matching**: Template code filtering uses `StartsWith` with `TrimEnd('*')`, meaning "trade*" matches "trade_goods", "trade_services", etc. The asterisk is only meaningful at the end of the pattern.

9. **MaxActiveContractsPerEntity counts all contracts, not just active ones**: The party index accumulates ALL contract IDs ever associated with an entity (including terminated, fulfilled, expired). Party indexes are never pruned. The effective limit is "max total contracts ever" rather than "max active". Fixing requires either pruning indexes on termination/fulfillment, or filtering by status at check time.

10. **ParseClauseAmount percentage mode returns 0 on missing base_amount**: When a clause uses `amount_type: "percentage"`, the calculation requires a numeric `base_amount` in the contract's template values. If `base_amount` is not set or not parseable as a double, the method logs a warning and returns 0. A percentage-typed fee clause executes as a zero-amount transfer. Whether this should fail the clause execution entirely (returning a failure status instead of 0) is a design decision.
