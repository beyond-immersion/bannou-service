# Contract Plugin Deep Dive

> **Plugin**: lib-contract
> **Schema**: schemas/contract-api.yaml
> **Version**: 1.0.0
> **Layer**: AppFoundation
> **State Store**: contract-statestore (Redis)
> **Implementation Map**: [docs/maps/CONTRACT.md](../maps/CONTRACT.md)
> **Short**: Binding agreements with milestone progression, consent flows, and prebound API execution

---

## Overview

Binding agreement management (L1 AppFoundation) between entities with milestone-based progression, consent flows, and prebound API execution on state transitions. Contracts are reactive: external systems report condition fulfillment via API calls; contracts store state, emit events, and execute callbacks. Templates define structure (party roles, milestones, terms, enforcement mode); instances track consent, sequential progression, and breach handling. Used as infrastructure by lib-quest (quest objectives map to contract milestones) and lib-escrow (asset-backed contracts via guardian locking).

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character (L2) | Queries contract instances for character-related contracts via `IContractClient.QueryContractInstancesAsync` |
| lib-quest (L2) | Quest objectives map 1:1 to contract milestones; creates templates/instances, manages milestone completion, sets template values for reward distribution, terminates contracts on quest abandonment |
| lib-item (L2) | Items with `useBehaviorContractTemplateId` delegate use-behavior to contracts; contract lifecycle orchestrates item consumption/transformation effects |
| lib-location (L2) | Registers `territory_constraint` clause type with Contract at plugin startup for territory-related contract enforcement |
| lib-chat (L1) | Validates contract instance existence via `IContractClient.GetContractInstanceAsync` for contract-governed rooms |
| lib-escrow (L4) | References `BoundContractId` for contract-backed escrow agreements; planned guardian lock/unlock integration |
| lib-status (L4) | Creates contract instances for statuses with `ContractTemplateId`; contract expiry triggers status removal via prebound APIs calling `/status/remove`; terminates contracts on manual status removal |
| lib-license (L4) | Creates contract instances for license node unlocks; sets template values, proposes/consents/completes milestones during unlock execution flow |
| lib-obligation (L4) | Queries active contracts per character to extract behavioral clauses; produces GOAP action cost modifiers from contractual obligations; reports breaches on knowing violations |
| lib-arbitration (L4) | *(aspirational — plugin not yet created)* Creates arbitration contracts from procedural templates with milestone tracking for dispute resolution phases |
| lib-craft (L4) | *(aspirational — plugin not yet created)* Models multi-step recipe execution sessions as contract-backed state machines |

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entityType` (on parties, consent, breach, termination, constraint, query) | A (Entity Reference) | `EntityType` enum (`$ref` to `common-api.yaml`) | Identifies which first-class Bannou entity is a contract party, breach reporter, or constraint subject |
| `guardianType` (on lock/unlock/transfer requests and events) | C (System State) | Opaque string (`maxLength: 64`) | System-mechanism identifier for the custody holder (e.g., `"escrow"`); not an entity type -- identifies the *kind of guardian system*, of which there may be only a few but they are not Bannou entities |
| `ContractStatus` | C (System State) | Service-specific enum | Finite state machine states (`draft`, `proposed`, `pending`, `active`, `fulfilled`, `terminated`, `expired`, `declined`) |
| `MilestoneStatus` | C (System State) | Service-specific enum | Finite milestone lifecycle states (`pending`, `active`, `completed`, `failed`, `skipped`) |
| `BreachType` | C (System State) | Service-specific enum | Finite breach categories (`term_violation`, `milestone_missed`, `milestone_deadline`, `non_payment`) |
| `BreachStatus` | C (System State) | Service-specific enum | Finite breach lifecycle states (`detected`, `cure_period`, `cured`, `escalated`, `forgiven`) |
| `ConsentStatus` | C (System State) | Service-specific enum | Finite consent states (`pending`, `consented`, `declined`, `implicit`) |
| `EnforcementMode` | C (System State) | Service-specific enum | Finite enforcement behavior modes (`advisory`, `event_only`, `consequence_based`, `community`) |
| `TerminationPolicy` | C (System State) | Service-specific enum | Finite termination rule modes (`mutual_consent`, `unilateral_with_notice`, `unilateral_immediate`, `non_terminable`) |
| `PaymentSchedule` | C (System State) | Service-specific enum | Finite payment timing modes (`one_time`, `recurring`, `milestone_based`) |
| `ConstraintType` | C (System State) | Service-specific enum | Finite constraint check types (`exclusivity`, `non_compete`, `time_commitment`) |
| `MetadataType` | C (System State) | Service-specific enum | Finite metadata partition types (`instance_data`, `runtime_state`) |
| `MilestoneDeadlineBehavior` | C (System State) | Service-specific enum | Finite deadline handling modes (`skip`, `warn`, `breach`) |
| `ValidationOutcome` | C (System State) | Service-specific enum | Finite prebound API validation results (`success`, `failure`, `transient_failure`) |
| `TimeCommitmentType` | C (System State) | Service-specific enum | Finite time commitment modes (`exclusive`, `shared`, `flexible`, `fire_and_forget`) |
| `ClauseCategory` | C (System State) | Service-specific enum | Finite clause handler categories (validation, execution, or both) |

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

## Visual Aid

```
Contract State Machine
========================

 CREATE PROPOSE CONSENT (all)
 │ │ │
 ▼ ▼ ▼
 ┌──────┐ ┌──────────┐ ┌─────────┐
 │ Draft │────►│ Proposed │───►│ Pending │ (if effectiveFrom > now)
 └──────┘ └──────────┘ └─────────┘
 │ │
 │ all consent │ effectiveFrom reached
 │ + no future │
 │ effectiveFrom │
 ▼ ▼
 ┌─────────┐ ┌─────────┐
 │ Active │◄───│ Pending │
 └─────────┘ └─────────┘
 │
 │ all required
 │ milestones complete
 ▼
 ┌───────────┐ EXECUTE
 │ Fulfilled │────────────► Distributions applied
 └───────────┘

 Consent timeout (lazy check):
 Proposed ──(DefaultConsentTimeoutDays expired)──► Expired

 Party request:
 Draft/Proposed/Active ──(TerminateContractInstance)──► Terminated


Milestone Progression
========================

 Template Milestones: [M1, M2, M3] (M1.Required=true, M2.Required=true, M3.Required=false)

 Contract Activation
 │
 ▼
 M1: Pending ──(activate)──► Active
 │
 ├── CompleteMilestone(M1)
 │ ├── onComplete APIs executed (batched)
 │ ├── M1 → Completed
 │ └── M2 → Active (next activated)
 │
 └── FailMilestone(M1)
 ├── M1.Required=true → Failed (triggeredBreach=true)
 └── onExpire APIs executed

 M2: Active
 │
 ├── CompleteMilestone(M2)
 │ ├── All required complete? → Contract: Active → Fulfilled
 │ └── M3 → Active
 │
 └── FailMilestone(M2)
 └── M2.Required=true → Failed

 M3: Active (optional)
 │
 └── FailMilestone(M3)
 └── M3.Required=false → Skipped (no breach)


Milestone Deadline Enforcement
================================

 Background Service (ContractExpirationService):
 │
 ├── Every MilestoneDeadlineCheckIntervalSeconds (default 300s)
 ├── Load all contracts from status-idx:active
 ├── For each contract:
 │ ├── Acquire milestone-check:{contractId} lock (30s)
 │ └── For each Active milestone with Deadline:
 │ ├── Compute absoluteDeadline = ActivatedAt + ParseIsoDuration(Deadline)
 │ └── If now > absoluteDeadline → invoke GetMilestoneAsync (triggers lazy enforcement)
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
 │ └── Publishes consent-received (remaining=2)
 │
 ├── ConsentToContract(B) → [A: Consented, B: Consented, C: Pending]
 │ └── Publishes consent-received (remaining=1)
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
 ├── Load template → parse typed Clauses property
 │
 ├── Separate by type:
 │ ├── fee clauses → Execute first (order preserved)
 │ └── distribution/ → Execute second
 │ currency_transfer/
 │ item_transfer
 │
 ├── For each clause:
 │ │
 │ ├── Resolve clause type → find ExecutionHandler
 │ │ ├── "fee" → uses "fee" clause type (service=currency, endpoint=/currency/transfer)
 │ │ ├── "distribution" → infers currency_transfer or item_transfer
 │ │ │ based on presence of source_container property
 │ │ ├── "currency_transfer" → registered handler
 │ │ └── "item_transfer" → registered handler
 │ │
 │ ├── Build payload:
 │ │ ├── Resolve {{TemplateKey}} in source/destination wallets/containers
 │ │ ├── ParseClauseAmount:
 │ │ │ ├── "flat" → literal amount
 │ │ │ ├── "percentage" → floor(base_amount * amount / 100)
 │ │ │ └── "remainder" → queries source wallet balance at execution time
 │ │ └── Serialize payload for handler endpoint
 │ │
 │ ├── Execute via ServiceNavigator
 │ │ ├── Success (200) → record distribution (Succeeded=true)
 │ │ └── Failure → publish contract.prebound-api.failed, record failure (Succeeded=false)
 │ │
 │ └── Record DistributionRecordModel (always, including failures)
 │
 └── Save execution state (ExecutedAt, ExecutionIdempotencyKey, ExecutionDistributions)


Breach Handling
=================

 ReportBreach(contractId, breachingEntityId, breachType)
 │
 ├── Check terms.GracePeriodForCure (ISO 8601 via XmlConvert.ToTimeSpan)
 │ ├── Present → Status: Cure_period, CureDeadline = now + duration
 │ └── Absent → Status: Detected
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
 │ ├── SubstitutionFailed → publish failed event
 │ ├── ResponseValidation rules → publish validation-failed event
 │ └── Success → publish executed event
 └── Exception → publish failed event (non-fatal, continues)
```

---

## Stubs & Unimplemented Features

1. **CleanDeprecatedContractTemplatesAsync** (`POST /contract/template/clean-deprecated`): Schema-defined and generated (controller + interface) but service implementation throws `NotImplementedException`. Sweeps deprecated contract templates with zero remaining contract instances. Uses shared `CleanDeprecatedRequest` (gracePeriodDays, dryRun) / `CleanDeprecatedResponse` (cleaned, remaining, errors, cleanedIds) from `common-api.yaml`. Permissions: `[role: admin]`. Implementation should use `DeprecationCleanupHelper.ExecuteCleanupSweepAsync` from `bannou-service/Helpers/DeprecationCleanupHelper.cs` per IMPLEMENTATION TENETS (Category B clean-deprecated, B20-B22).

---

## Potential Extensions

1. **Clause type handler chaining**: Allow clause types with both validation AND execution handlers to validate before executing, with configurable failure behavior. ([#458](https://github.com/beyond-immersion/bannou-service/issues/458))
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/458 -->
2. **Template inheritance**: Allow templates to extend other templates, inheriting milestones, terms, and party roles with overrides. ([#459](https://github.com/beyond-immersion/bannou-service/issues/459))
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/459 -->

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

## Contract Expiration Architecture

The contract service uses a **hybrid lazy + background** approach for both contract-level expiration (effectiveUntil) and milestone deadline enforcement.

### Why This Design?

| Approach | Pros | Cons |
|----------|------|------|
| **Pure polling** | Guaranteed timing | Expensive (scans all contracts continuously) |
| **Pure lazy** | Zero overhead when idle | Stale contracts/milestones if never accessed |
| **Hybrid** (current) | Low overhead + eventual guarantee | Slight delay for unaccessed contracts |

### How It Works

1. **Primary: Lazy Enforcement** - When `GetContractInstanceStatus` is called, the service checks if the contract has passed its `effectiveUntil` date (transitions to Expired) and if any active milestones are overdue (applies configured `DeadlineBehavior`). `GetMilestone` also triggers lazy milestone enforcement. `ConsentToContract` checks consent deadline expiration.

2. **Backup: Background Service** - `ContractExpirationService` runs every `MilestoneDeadlineCheckIntervalSeconds` (default 5 minutes), calling `GetContractInstanceStatusAsync` for each active contract which triggers lazy enforcement of both expiration types in a single pass.

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

6. **Template deprecation is Category B**: Contract templates use the full triple-field deprecation model (`IsDeprecated`/`DeprecatedAt`/`DeprecationReason`). Deprecation is one-way (no undeprecate), idempotent (returns OK when already deprecated), and prevents new instance creation (`ProposeContractInstance` rejects deprecated templates with BadRequest). No delete endpoint exists — templates persist forever for existing instances that reference them by `templateId`. The `DeprecateContractTemplate` endpoint accepts an optional `reason` parameter. Deprecation is communicated via `contract.template.updated` with `changedFields` containing the deprecation fields (no dedicated deprecation event per tenets). `ListContractTemplates` includes `includeDeprecated` parameter (default: `false`).

7. **Instance delete has no validation beyond developer role**: The `deleteContractInstance` endpoint only requires the `developer` role and verifies the contract is in a terminal state (Fulfilled, Terminated, Expired, or Declined). It does not validate party identity, check for active escrow references, or confirm that dependent systems have finished processing. This is intentional for now -- the developer role restriction limits access to administrative use cases where the caller is trusted.

### Design Considerations (Requires Planning)

1. **Per-milestone onApiFailure flag** ([#246](https://github.com/beyond-immersion/bannou-service/issues/246)): Currently prebound API failures are always non-blocking. Adding a per-milestone flag would require API schema changes (new field on MilestoneDefinition), model regeneration, and careful design of retry semantics. Requires design discussion before implementation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/246 -->


---

## Work Tracking

This section tracks active development work using AUDIT markers.

### Active Work

*None currently tracked.*

### Completed

*Historical entries cleared. See git history for audit trail.*
