# Contract Implementation Map

> **Plugin**: lib-contract
> **Schema**: schemas/contract-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/CONTRACT.md](../plugins/CONTRACT.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-contract |
| Layer | L1 AppFoundation |
| Endpoints | 32 |
| State Stores | contract-statestore (Redis), contract-lock (Redis) |
| Events Published | 27 (`contract.template.created`, `contract.template.updated`, `contract.template.deleted`, `contract.instance.created`, `contract.instance.updated`, `contract.instance.deleted`, `contract.proposed`, `contract.consented`, `contract.accepted`, `contract.activated`, `contract.milestone.completed`, `contract.milestone.failed`, `contract.breach.detected`, `contract.breach.cured`, `contract.fulfilled`, `contract.terminated`, `contract.expired`, `contract.payment.due`, `contract.prebound-api.executed`, `contract.prebound-api.failed`, `contract.prebound-api.validation-failed`, `contract.locked`, `contract.unlocked`, `contract.party.transferred`, `contract.clause-type.registered`, `contract.template-values.set`, `contract.executed`) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 1 (ContractExpirationService) |

---

## State

**Store**: `contract-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `template:{templateId}` | `ContractTemplateModel` | Template definition and configuration |
| `template-code:{code}` | `string` | Template ID lookup by code |
| `all-templates` | `List<Guid>` | All template IDs for listing |
| `instance:{contractId}` | `ContractInstanceModel` | Contract instance state, parties, milestones, metadata |
| `breach:{breachId}` | `BreachModel` | Breach record with status and deadlines |
| `party-idx:{entityType}:{entityId}` | `List<Guid>` | Contract IDs where entity is a party |
| `template-idx:{templateId}` | `List<Guid>` | Contract IDs created from a template |
| `status-idx:{status}` | `List<Guid>` | Contract IDs in a given status |
| `clause-type:{typeCode}` | `ClauseTypeModel` | Clause type definition with handlers |
| `all-clause-types` | `List<string>` | All registered clause type codes |
| `idempotency:{operation}:{key}` | serialized response | Idempotent response cache (TTL: IdempotencyTtlSeconds) |

**Store**: `contract-lock` (Backend: Redis)

| Lock Key Pattern | Timeout | Purpose |
|-----------------|---------|---------|
| `contract:{contractId}` | ContractLockTimeoutSeconds (60s) | State transition serialization |
| `index:{listKey}` | IndexLockTimeoutSeconds (15s) | Index list mutation safety |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Redis persistence for all key patterns |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Contract-instance locks and index-level locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing all 27 contract event topics |
| lib-mesh (`IServiceNavigator`) | L0 | Hard | Executing prebound APIs (milestone callbacks, clause execution) |
| lib-mesh (`IEventConsumer`) | L0 | Hard | Event consumer registration (unused in v1) |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |

**Marker interface**: `ICleanDeprecatedEntity` — Category B deprecation lifecycle compliance (structural test validated).

No DI provider/listener interfaces implemented or consumed. No typed service clients injected — all cross-service calls go through `IServiceNavigator` at runtime (target service determined by prebound API definitions, not compile-time).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `contract.template.created` | `ContractTemplateCreatedEvent` | CreateContractTemplate |
| `contract.template.updated` | `ContractTemplateUpdatedEvent` | UpdateContractTemplate |
| `contract.template.deleted` | `ContractTemplateDeletedEvent` | CleanDeprecatedContractTemplates (per eligible template in sweep) |
| `contract.instance.created` | `ContractInstanceCreatedEvent` | CreateContractInstance |
| `contract.instance.updated` | `ContractInstanceUpdatedEvent` | ProposeContractInstance, ConsentToContract, TerminateContractInstance, GetContractInstanceStatus (lazy), CompleteMilestone, FailMilestone, GetMilestone (lazy), ReportBreach, UpdateContractMetadata, LockContract, UnlockContract, TransferContractParty, SetContractTemplateValues, ExecuteContract |
| `contract.instance.deleted` | `ContractInstanceDeletedEvent` | DeleteContractInstance |
| `contract.proposed` | `ContractProposedEvent` | ProposeContractInstance |
| `contract.consented` | `ContractConsentReceivedEvent` | ConsentToContract |
| `contract.accepted` | `ContractAcceptedEvent` | ConsentToContract (all consented) |
| `contract.activated` | `ContractActivatedEvent` | ConsentToContract (immediate), GetContractInstanceStatus (lazy), ContractExpirationService |
| `contract.milestone.completed` | `ContractMilestoneCompletedEvent` | CompleteMilestone |
| `contract.milestone.failed` | `ContractMilestoneFailedEvent` | FailMilestone, ProcessOverdueMilestone (deadline enforcement) |
| `contract.breach.detected` | `ContractBreachDetectedEvent` | ReportBreach, CheckBreachThreshold (auto-terminate) |
| `contract.breach.cured` | `ContractBreachCuredEvent` | CureBreach |
| `contract.fulfilled` | `ContractFulfilledEvent` | CompleteMilestone (all required complete), ConsentToContract (no-milestone path), GetContractInstanceStatus (lazy Pending→Fulfilled) |
| `contract.terminated` | `ContractTerminatedEvent` | TerminateContractInstance, CheckBreachThreshold |
| `contract.expired` | `ContractExpiredEvent` | ConsentToContract (lazy consent deadline), GetContractInstanceStatus (lazy), ContractExpirationService |
| `contract.payment.due` | `ContractPaymentDueEvent` | ContractExpirationService (payment schedule check) |
| `contract.prebound-api.executed` | `ContractPreboundApiExecutedEvent` | ExecutePreboundApi (success) |
| `contract.prebound-api.failed` | `ContractPreboundApiFailedEvent` | ExecutePreboundApi (failure), ExecuteSingleClause (failure) |
| `contract.prebound-api.validation-failed` | `ContractPreboundApiValidationFailedEvent` | ExecutePreboundApi (validation failure) |
| `contract.locked` | `ContractLockedEvent` | LockContract |
| `contract.unlocked` | `ContractUnlockedEvent` | UnlockContract |
| `contract.party.transferred` | `ContractPartyTransferredEvent` | TransferContractParty |
| `contract.clause-type.registered` | `ClauseTypeRegisteredEvent` | RegisterClauseType |
| `contract.template-values.set` | `ContractTemplateValuesSetEvent` | SetContractTemplateValues |
| `contract.executed` | `ContractExecutedEvent` | ExecuteContract |

---

## Events Consumed

This plugin does not consume external events. Schema declares `x-event-subscriptions: []`.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ContractService>` | Structured logging |
| `ContractServiceConfiguration` | All 16 config properties |
| `IStateStoreFactory` | State store access (contract-statestore) |
| `IDistributedLockProvider` | Contract-instance and index locks |
| `IMessageBus` | Event publishing |
| `IServiceNavigator` | Prebound API execution (milestone callbacks, clause execution) |
| `IEventConsumer` | Event consumer registration (unused in v1) |
| `ITelemetryProvider` | Span instrumentation |
| `ContractExpirationService` | Background worker for expiration and payment schedules |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateContractTemplate | POST /contract/template/create | admin | template, template-code, all-templates | contract.template.created |
| GetContractTemplate | POST /contract/template/get | user | - | - |
| ListContractTemplates | POST /contract/template/list | user | - | - |
| UpdateContractTemplate | POST /contract/template/update | admin | template, template-code | contract.template.updated |
| DeprecateContractTemplate | POST /contract/template/deprecate | admin | template | contract.template.updated |
| CreateContractInstance | POST /contract/instance/create | user | instance, party-idx, template-idx, status-idx | contract.instance.created |
| ProposeContractInstance | POST /contract/instance/propose | user | instance, status-idx | contract.proposed, contract.instance.updated |
| ConsentToContract | POST /contract/instance/consent | user | instance, status-idx | contract.consented, contract.accepted, contract.activated, contract.expired, contract.instance.updated |
| GetContractInstance | POST /contract/instance/get | user | - | - |
| QueryContractInstances | POST /contract/instance/query | user | - | - |
| TerminateContractInstance | POST /contract/instance/terminate | user | instance, status-idx | contract.terminated, contract.instance.updated |
| DeleteContractInstance | POST /contract/instance/delete | developer | instance, party-idx, template-idx, status-idx, breach | contract.instance.deleted |
| GetContractInstanceStatus | POST /contract/instance/get-status | user | instance, status-idx | contract.activated, contract.fulfilled, contract.expired, contract.milestone.failed, contract.breach.detected, contract.instance.updated |
| CompleteMilestone | POST /contract/milestone/complete | developer | instance, status-idx | contract.milestone.completed, contract.fulfilled, contract.instance.updated |
| FailMilestone | POST /contract/milestone/fail | developer | instance | contract.milestone.failed, contract.breach.detected, contract.instance.updated |
| GetMilestone | POST /contract/milestone/get | user | instance | contract.milestone.failed (lazy deadline), contract.instance.updated |
| ReportBreach | POST /contract/breach/report | user | instance, breach | contract.breach.detected, contract.terminated, contract.instance.updated |
| CureBreach | POST /contract/breach/cure | developer | instance, breach | contract.breach.cured |
| GetBreach | POST /contract/breach/get | user | - | - |
| UpdateContractMetadata | POST /contract/metadata/update | developer | instance | contract.instance.updated |
| GetContractMetadata | POST /contract/metadata/get | user | - | - |
| CheckContractConstraint | POST /contract/check-constraint | user | - | - |
| QueryActiveContracts | POST /contract/query-active | user | - | - |
| LockContract | POST /contract/lock | developer | instance, idempotency | contract.locked, contract.instance.updated |
| UnlockContract | POST /contract/unlock | developer | instance, idempotency | contract.unlocked, contract.instance.updated |
| TransferContractParty | POST /contract/transfer-party | developer | instance, party-idx, idempotency | contract.party.transferred, contract.instance.updated |
| RegisterClauseType | POST /contract/clause-type/register | admin | clause-type, all-clause-types | contract.clause-type.registered |
| ListClauseTypes | POST /contract/clause-type/list | developer | clause-type (lazy init) | - |
| SetContractTemplateValues | POST /contract/instance/set-template-values | developer | instance | contract.template-values.set, contract.instance.updated |
| CheckAssetRequirements | POST /contract/instance/check-asset-requirements | developer | - | - |
| ExecuteContract | POST /contract/instance/execute | developer | instance, idempotency | contract.executed, contract.prebound-api.*, contract.instance.updated |
| CleanDeprecatedContractTemplates | POST /contract/template/clean-deprecated | admin | template, template-code, all-templates, template-idx | contract.template.deleted |

---

## Methods

### CreateContractTemplate
POST /contract/template/create | Roles: [admin]

```
IF request.milestones.Count > config.MaxMilestonesPerTemplate -> 400
FOREACH milestone in request.milestones
 IF milestone.onComplete.Count > config.MaxPreboundApisPerMilestone -> 400
 IF milestone.onExpire.Count > config.MaxPreboundApisPerMilestone -> 400
READ contract-statestore:template-code:{code} -> 409 if exists
WRITE contract-statestore:template:{templateId} <- ContractTemplateModel from request
WRITE contract-statestore:template-code:{code} <- templateId
// AddToListAsync acquires index:{all-templates} lock (15s)
WRITE contract-statestore:all-templates <- append templateId
PUBLISH contract.template.created { templateId, code, name, createdAt }
RETURN (200, ContractTemplateResponse)
```

### GetContractTemplate
POST /contract/template/get | Roles: [user]

```
IF request.templateId is null AND request.code is null -> 400
IF request.code provided
 READ contract-statestore:template-code:{code} -> 404 if null
 // resolved templateId from code index
READ contract-statestore:template:{templateId} -> 404 if null
RETURN (200, ContractTemplateResponse)
```

### ListContractTemplates
POST /contract/template/list | Roles: [user]

```
READ contract-statestore:all-templates -> templateIds
FOREACH templateId in templateIds
 READ contract-statestore:template:{templateId}
 IF !request.includeDeprecated AND model.IsDeprecated -> skip
 IF request.realmId filter -> skip if mismatch
 IF request.searchTerm -> skip if name/description not matching (case-insensitive)
// Cursor-based pagination: cursor = base64({ offset })
// Page size defaults to config.DefaultPageSize
RETURN (200, ListContractTemplatesResponse { templates, nextCursor, hasMore })
```

### UpdateContractTemplate
POST /contract/template/update | Roles: [admin]

```
READ contract-statestore:template:{templateId} -> 404 if null
// Track changed fields for event
IF request.name provided -> update, track "name"
IF request.description provided -> update, track "description"
IF request.gameMetadata provided -> update, track "gameMetadata"
WRITE contract-statestore:template:{templateId} <- updated model
PUBLISH contract.template.updated { templateId, code, changedFields }
RETURN (200, ContractTemplateResponse)
```

### DeprecateContractTemplate
POST /contract/template/deprecate | Roles: [admin]

```
READ contract-statestore:template:{templateId} -> 404 if null
IF template.IsDeprecated == true -> 200 (idempotent, per IMPLEMENTATION TENETS)
// Sets IsDeprecated=true, DeprecatedAt=now, DeprecationReason from request.reason
WRITE contract-statestore:template:{templateId} <- updated ContractTemplateModel
PUBLISH contract.template.updated { templateId, code, changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason"] }
RETURN (200, ContractTemplateResponse)
```

### CreateContractInstance
POST /contract/instance/create | Roles: [user]

```
READ contract-statestore:template:{templateId} -> 404 if null
IF template.IsDeprecated -> 400 (Category B instance creation guard)
IF request.parties.Count > config.MaxPartiesPerContract -> 400
IF config.MaxActiveContractsPerEntity > 0
 FOREACH party in request.parties
 READ contract-statestore:party-idx:{entityType}:{entityId}
 // Count active contracts (Draft/Proposed/Pending/Active)
 IF activeCount >= config.MaxActiveContractsPerEntity -> 409
// Merge terms: instance terms override template defaults per config.TermsMergeMode
// Build milestone instances from template milestone definitions
WRITE contract-statestore:instance:{contractId} <- ContractInstanceModel (status=Draft)
WRITE contract-statestore:template-idx:{templateId} <- append contractId
WRITE contract-statestore:status-idx:Draft <- append contractId
FOREACH party in parties
 WRITE contract-statestore:party-idx:{entityType}:{entityId} <- append contractId
PUBLISH contract.instance.created { contractId, templateId, templateCode, status, createdAt }
RETURN (200, ContractInstanceResponse)
```

### ProposeContractInstance
POST /contract/instance/propose | Roles: [user]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF status != Draft -> 400
 // Set status=Proposed, ProposedAt=now, consent deadline
 WRITE contract-statestore:status-idx:Draft <- remove contractId
 WRITE contract-statestore:status-idx:Proposed <- append contractId
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.proposed { contractId, templateId, templateCode, parties }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["status","proposedAt"] }
RETURN (200, ContractInstanceResponse)
```

### ConsentToContract
POST /contract/instance/consent | Roles: [user]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF instance.GuardianId is set -> 403
 IF status != Proposed AND status != Pending -> 400
 // Lazy consent deadline check
 IF status == Proposed AND now > ProposedAt + DefaultConsentTimeoutDays
 // Transition to Expired
 WRITE contract-statestore:status-idx:Proposed <- remove contractId
 ETAG-WRITE contract-statestore:instance:{contractId} <- status=Expired
 PUBLISH contract.expired { contractId, templateCode }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["status"] }
 RETURN (400, null)
 // Find party by entityId+entityType
 IF party not found -> 400
 IF party already consented -> 400
 // Record consent with timestamp
 IF not all required parties consented
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.consented { contractId, role, remainingConsentsNeeded }
 ELSE // all consented
 PUBLISH contract.consented { contractId, role, remainingConsentsNeeded: 0 }
 PUBLISH contract.accepted { contractId, templateCode, parties }
 IF effectiveFrom is null OR effectiveFrom <= now
 // Immediate activation
 // Activate first milestone
 WRITE contract-statestore:status-idx:{oldStatus} <- remove contractId
 WRITE contract-statestore:status-idx:Active <- append contractId
 ETAG-WRITE contract-statestore:instance:{contractId} <- status=Active
 PUBLISH contract.activated { contractId, templateCode, parties }
 ELSE
 // Future activation
 WRITE contract-statestore:status-idx:{oldStatus} <- remove contractId
 WRITE contract-statestore:status-idx:Pending <- append contractId
 ETAG-WRITE contract-statestore:instance:{contractId} <- status=Pending
 PUBLISH contract.instance.updated { contractId, status, changedFields=["consentStatus","status","acceptedAt","effectiveFrom"] }
RETURN (200, ContractInstanceResponse)
```

### GetContractInstance
POST /contract/instance/get | Roles: [user]

```
READ contract-statestore:instance:{contractId} -> 404 if null
// Plain read -- no lazy state transitions
RETURN (200, ContractInstanceResponse)
```

### QueryContractInstances
POST /contract/instance/query | Roles: [user]

```
IF no filter provided (no partyEntityId, templateId, or statuses) -> 400
IF request.partyEntityId provided
 READ contract-statestore:party-idx:{entityType}:{entityId} -> contractIds
ELSE IF request.templateId provided
 READ contract-statestore:template-idx:{templateId} -> contractIds
ELSE
 // Union of status indexes
 FOREACH status in request.statuses
 READ contract-statestore:status-idx:{status} -> merge into contractIds
FOREACH contractId in contractIds
 READ contract-statestore:instance:{contractId}
 // Apply additional filters (status, templateCode) in memory
// Cursor-based pagination: cursor = base64({ offset })
// Page size defaults to config.DefaultPageSize
RETURN (200, QueryContractInstancesResponse { contracts, nextCursor, hasMore })
```

### TerminateContractInstance
POST /contract/instance/terminate | Roles: [user]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF instance.GuardianId is set -> 403
 IF status not in [Draft, Proposed, Pending, Active] -> 400
 IF requestingEntity not a party -> 400
 // Transition to Terminated
 WRITE contract-statestore:status-idx:{oldStatus} <- remove contractId
 ETAG-WRITE contract-statestore:instance:{contractId} <- status=Terminated, TerminatedAt=now
 PUBLISH contract.terminated { contractId, templateCode, requestingEntityId, reason }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["status","terminatedAt"] }
RETURN (200, ContractInstanceResponse)
```

### DeleteContractInstance
POST /contract/instance/delete | Roles: [developer]

```
READ contract-statestore:instance:{contractId} -> 404 if null
IF status not in [Fulfilled, Terminated, Expired, Declined] -> 400
DELETE contract-statestore:instance:{contractId}
WRITE contract-statestore:status-idx:{status} <- remove contractId
WRITE contract-statestore:template-idx:{templateId} <- remove contractId
FOREACH party in parties
 WRITE contract-statestore:party-idx:{entityType}:{entityId} <- remove contractId
FOREACH breachId in instance.BreachIds
 DELETE contract-statestore:breach:{breachId}
PUBLISH contract.instance.deleted { contractId, templateCode, status, deletedReason }
RETURN 200
```

### GetContractInstanceStatus
POST /contract/instance/get-status | Roles: [user]

```
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
// Lazy enforcement block
IF status == Pending AND effectiveFrom <= now
 // Activate contract
 WRITE contract-statestore:status-idx:Pending <- remove contractId
 PUBLISH contract.activated { contractId, templateCode, parties }
 IF no milestones OR all required milestones already complete
  // No-milestone path → straight to Fulfilled
  WRITE contract-statestore:status-idx:Fulfilled <- append contractId
  ETAG-WRITE contract-statestore:instance:{contractId} <- status=Fulfilled
  PUBLISH contract.fulfilled { contractId, templateCode, parties }
 ELSE
  // Activate first milestone
  WRITE contract-statestore:status-idx:Active <- append contractId
  ETAG-WRITE contract-statestore:instance:{contractId} <- status=Active
 PUBLISH contract.instance.updated { contractId, status, changedFields=["status"] }
IF status == Active AND effectiveUntil <= now
 // Expire contract
 WRITE contract-statestore:status-idx:Active <- remove contractId
 ETAG-WRITE contract-statestore:instance:{contractId} <- status=Expired
 PUBLISH contract.expired { contractId, templateCode, effectiveUntil }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["status"] }
IF status == Active
 FOREACH milestone in active milestones with deadline
 IF now > milestone.ActivatedAt + ParseIsoDuration(milestone.Deadline)
 // see helper: ProcessOverdueMilestone
 // Marks milestone Failed, publishes milestone.failed, may trigger breach
 IF anyProcessed
 PUBLISH contract.instance.updated { contractId, status, changedFields=["milestones","status"] }
// Build status response: milestone progress, pending consents, active breaches
FOREACH breachId in instance.BreachIds
 READ contract-statestore:breach:{breachId}
 // Filter to active breaches (Detected, CurePeriod)
RETURN (200, ContractInstanceStatusResponse)
```

### CompleteMilestone
POST /contract/milestone/complete | Roles: [developer]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF status != Active -> 400
 // Find milestone by code
 IF milestone not found -> 404
 IF milestone in terminal state (Completed/Failed/Skipped) -> 400
 // Mark milestone Completed
 // Execute onComplete prebound APIs in batches
 // see helper: ExecutePreboundApisBatched
 // Activate next milestone by sequence if exists
 IF all required milestones completed
 // Transition contract to Fulfilled
 WRITE contract-statestore:status-idx:Active <- remove contractId
 WRITE contract-statestore:status-idx:Fulfilled <- append contractId
 PUBLISH contract.fulfilled { contractId, templateCode, parties, milestonesCompleted }
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.milestone.completed { contractId, milestoneCode, preboundApisExecuted }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["milestones"(,"status")] }
RETURN (200, MilestoneResponse)
```

### FailMilestone
POST /contract/milestone/fail | Roles: [developer]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF status != Active -> 400
 IF milestone not found -> 404
 IF milestone in terminal state -> 400
 IF milestone.Required
 // Mark Failed, set triggeredBreach=true
 ELSE
 // Mark Skipped, triggeredBreach=false
 // Execute onExpire prebound APIs in batches
 // Check breach threshold -> auto-terminate if exceeded
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.milestone.failed { contractId, milestoneCode, wasRequired, triggeredBreach }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["milestones"] }
RETURN (200, MilestoneResponse)
```

### GetMilestone
POST /contract/milestone/get | Roles: [user]

```
READ contract-statestore:instance:{contractId} -> 404 if null
// Find milestone by code
IF milestone not found -> 404
// Lazy deadline enforcement
IF milestone is active AND has deadline AND deadline exceeded
 // see helper: ProcessOverdueMilestone
 PUBLISH contract.instance.updated { contractId, status, changedFields=["milestones","status"] }
RETURN (200, MilestoneResponse)
```

### ReportBreach
POST /contract/breach/report | Roles: [user]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF status != Active -> 400
 // Build BreachModel
 IF terms.GracePeriodForCure (parsed as ISO 8601 duration)
 // status=CurePeriod, CureDeadline=now + duration
 ELSE
 // status=Detected
 WRITE contract-statestore:breach:{breachId} <- BreachModel
 // Add breachId to instance.BreachIds, increment BreachCount
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.breach.detected { contractId, breachId, breachType, cureDeadline }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["breachIds"] }
 // Check breach threshold -> auto-terminate if exceeded
RETURN (200, BreachResponse)
```

### CureBreach
POST /contract/breach/cure | Roles: [developer]

```
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 READ contract-statestore:breach:{breachId} -> 404 if null
 IF breach not in curable state (already cured or past grace) -> 400
 // Mark breach Cured, set CuredAt
 WRITE contract-statestore:breach:{breachId} <- updated
 // Decrement instance BreachCount
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 PUBLISH contract.breach.cured { contractId, breachId, cureEvidence }
RETURN (200, BreachResponse)
```

### GetBreach
POST /contract/breach/get | Roles: [user]

```
READ contract-statestore:instance:{contractId} -> 404 if null
READ contract-statestore:breach:{breachId} -> 404 if null
IF breach does not belong to this contract -> 400
RETURN (200, BreachResponse)
```

### UpdateContractMetadata
POST /contract/metadata/update | Roles: [developer]

```
// No distributed lock -- relies on ETag only
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
IF status in terminal states (Fulfilled/Terminated/Expired) -> 400
IF request.metadataType == InstanceData
 // Update instance.GameMetadata.InstanceData
ELSE // RuntimeState
 // Update instance.GameMetadata.RuntimeState
ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
PUBLISH contract.instance.updated { contractId, status, changedFields=["gameMetadata"] }
RETURN (200, ContractMetadataResponse)
```

### GetContractMetadata
POST /contract/metadata/get | Roles: [user]

```
READ contract-statestore:instance:{contractId} -> 404 if null
RETURN (200, ContractMetadataResponse { instanceData, runtimeState })
```

### CheckContractConstraint
POST /contract/check-constraint | Roles: [user]

```
READ contract-statestore:party-idx:{entityType}:{entityId} -> contractIds
FOREACH contractId in contractIds
 READ contract-statestore:instance:{contractId}
 // Filter to Active status only
IF request.constraintType == Exclusivity
 // Check terms.Exclusivity boolean on active contracts
ELSE IF request.constraintType == NonCompete
 // Check terms.NonCompete boolean on active contracts
ELSE IF request.constraintType == TimeCommitment
 // Check date range overlaps for contracts with TimeCommitment=true
 // and TimeCommitmentType=Exclusive
RETURN (200, CheckConstraintResponse { allowed, conflictingContracts, reason })
```

### QueryActiveContracts
POST /contract/query-active | Roles: [user]

```
READ contract-statestore:party-idx:{entityType}:{entityId} -> contractIds
FOREACH contractId in contractIds
 READ contract-statestore:instance:{contractId}
 // Filter to Active status
 IF request.templateCodes provided
 // Wildcard prefix matching: TrimEnd('*') + StartsWith
// Cursor-based pagination: cursor = base64({ offset })
RETURN (200, QueryActiveContractsResponse { contracts })
```

### LockContract
POST /contract/lock | Roles: [developer]

```
IF request.idempotencyKey provided
 READ contract-statestore:idempotency:lock:{key}
 IF cached -> RETURN (200, cached response)
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
READ contract-statestore:template:{templateId}
IF template.Transferable != true -> 400
IF instance.GuardianId is set -> 409
// Set GuardianId, GuardianType, LockedAt
ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
IF request.idempotencyKey provided
 WRITE contract-statestore:idempotency:lock:{key} <- response (TTL: IdempotencyTtlSeconds)
PUBLISH contract.locked { contractId, guardianId, guardianType }
PUBLISH contract.instance.updated { contractId, status, changedFields=["guardianId","guardianType","lockedAt"] }
RETURN (200, LockContractResponse)
```

### UnlockContract
POST /contract/unlock | Roles: [developer]

```
IF request.idempotencyKey provided
 READ contract-statestore:idempotency:unlock:{key}
 IF cached -> RETURN (200, cached response)
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
IF not locked -> 400
IF request.guardianId != instance.GuardianId -> 403
// Clear GuardianId, GuardianType, LockedAt
ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
IF request.idempotencyKey provided
 WRITE contract-statestore:idempotency:unlock:{key} <- response (TTL: IdempotencyTtlSeconds)
PUBLISH contract.unlocked { contractId, previousGuardianId, previousGuardianType }
PUBLISH contract.instance.updated { contractId, status, changedFields=["guardianId","guardianType","lockedAt"] }
RETURN (200, UnlockContractResponse)
```

### TransferContractParty
POST /contract/transfer-party | Roles: [developer]

```
IF request.idempotencyKey provided
 READ contract-statestore:idempotency:transfer:{key}
 IF cached -> RETURN (200, cached response)
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
IF status != Active -> 400
IF locked AND request.guardianId != instance.GuardianId -> 403
READ contract-statestore:template:{templateId}
IF template.Transferable != true -> 400
// Find party by fromEntityId/fromEntityType
IF party not found -> 404
// Update party EntityId/EntityType to new entity
WRITE contract-statestore:party-idx:{oldType}:{oldId} <- remove contractId
WRITE contract-statestore:party-idx:{newType}:{newId} <- append contractId
ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
IF request.idempotencyKey provided
 WRITE contract-statestore:idempotency:transfer:{key} <- response (TTL: IdempotencyTtlSeconds)
PUBLISH contract.party.transferred { contractId, role, fromEntityId, toEntityId }
PUBLISH contract.instance.updated { contractId, status, changedFields=["parties"] }
RETURN (200, TransferContractPartyResponse)
```

### RegisterClauseType
POST /contract/clause-type/register | Roles: [admin]

```
READ contract-statestore:clause-type:{typeCode} -> 409 if exists
// Ensure built-in clause types (asset_requirement, currency_transfer, item_transfer, fee)
// see helper: EnsureBuiltInClauseTypes
WRITE contract-statestore:clause-type:{typeCode} <- ClauseTypeModel from request
WRITE contract-statestore:all-clause-types <- append typeCode
PUBLISH contract.clause-type.registered { typeCode, category }
RETURN (200, RegisterClauseTypeResponse)
```

### ListClauseTypes
POST /contract/clause-type/list | Roles: [developer]

```
// Ensure built-in clause types registered (lazy init)
// see helper: EnsureBuiltInClauseTypes
READ contract-statestore:all-clause-types -> typeCodes
FOREACH typeCode in typeCodes
 READ contract-statestore:clause-type:{typeCode}
 IF request.category filter -> skip if mismatch
 IF request.includeBuiltIn == false -> skip if isBuiltIn
RETURN (200, ListClauseTypesResponse { clauseTypes })
```

### SetContractTemplateValues
POST /contract/instance/set-template-values | Roles: [developer]

```
READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
IF status != Active -> 400
IF instance.GuardianId is set -> 400
FOREACH key in request.templateValues.Keys
 IF key does not match pattern ^[A-Za-z0-9_]+$ -> 400
// Merge provided values into instance.TemplateValues (additive)
ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
PUBLISH contract.template-values.set { contractId, keys }
PUBLISH contract.instance.updated { contractId, status, changedFields=["templateValues"] }
RETURN (200, SetTemplateValuesResponse { contractId, valueCount })
```

### CheckAssetRequirements
POST /contract/instance/check-asset-requirements | Roles: [developer]

```
READ contract-statestore:instance:{contractId} -> 404 if null
READ contract-statestore:template:{templateId} -> 404 if null
IF instance.TemplateValues is empty -> 400
// Parse clauses from template terms, filter for asset_requirement type
FOREACH clause in assetRequirementClauses
 // Resolve checkLocation via template value substitution
 // Query balance via IServiceNavigator -> clause type handler endpoint
 CALL _navigator.ExecutePreboundApiAsync(handler.endpoint, payload)
 // Compare current balance against required amount
RETURN (200, CheckAssetRequirementsResponse { allSatisfied, byParty })
```

### ExecuteContract
POST /contract/instance/execute | Roles: [developer]

```
IF request.idempotencyKey provided
 READ contract-statestore:idempotency:execute:{key}
 IF cached -> RETURN (200, cached response)
LOCK contract-lock:contract:{contractId} (ContractLockTimeoutSeconds) -> 409 if fails
 READ contract-statestore:instance:{contractId} [with ETag] -> 404 if null
 IF status != Fulfilled -> 400
 IF instance.ExecutedAt is set -> RETURN (200, already executed)
 IF instance.TemplateValues is empty -> 400
 READ contract-statestore:template:{templateId}
 // Parse clauses from template terms
 // Separate into fees (execute first) and distributions (execute second)
 FOREACH clause in feeClauses
 // Resolve amounts (flat/percentage/remainder)
 // Template value substitution for wallet IDs
 CALL _navigator.ExecutePreboundApiAsync(handler.endpoint, transferPayload)
 // Record DistributionRecordModel (succeeded or failed)
 FOREACH clause in distributionClauses
 // Same pattern: resolve, substitute, execute, record
 CALL _navigator.ExecutePreboundApiAsync(handler.endpoint, transferPayload)
 // Set ExecutedAt, ExecutionIdempotencyKey, distributions
 ETAG-WRITE contract-statestore:instance:{contractId} -> 409 if ETag mismatch
 IF request.idempotencyKey provided
 WRITE contract-statestore:idempotency:execute:{key} <- response (TTL: IdempotencyTtlSeconds)
 PUBLISH contract.executed { contractId, templateCode, distributionCount, distributionResults }
 PUBLISH contract.instance.updated { contractId, status, changedFields=["executedAt","executionIdempotencyKey","executionDistributions"] }
RETURN (200, ExecuteContractResponse { contractId, distributions })
```

### CleanDeprecatedContractTemplates
POST /contract/template/clean-deprecated | Roles: [admin]

```
READ contract-statestore:all-templates -> templateIds
IF empty -> RETURN (200, CleanDeprecatedResponse { cleaned=0 })
READ contract-statestore:template:{*} (bulk) -> all templates
// Filter to deprecated templates only
// Delegate to DeprecationCleanupHelper.ExecuteCleanupSweepAsync:
//   getEntityId: template.TemplateId
//   getDeprecatedAt: template.DeprecatedAt
//   hasActiveInstances: READ contract-statestore:template-idx:{templateId} -> has entries?
//   deleteAndPublish: (per eligible template)
FOREACH eligible deprecated template (via helper)
  DELETE contract-statestore:template:{templateId}
  DELETE contract-statestore:template-code:{code}
  WRITE contract-statestore:all-templates <- remove templateId
  DELETE contract-statestore:template-idx:{templateId}
  PUBLISH contract.template.deleted { templateId, code, name, deletedReason }
// Helper handles: grace period filtering, dry-run mode, per-item error isolation
RETURN (200, CleanDeprecatedResponse { cleaned, remaining, errors, cleanedIds })
```

---

## Background Services

### ContractExpirationService
**Interval**: config.MilestoneDeadlineCheckIntervalSeconds (default 300s)
**Startup Delay**: config.MilestoneDeadlineStartupDelaySeconds (default 30s)
**Purpose**: Drives lazy state transitions for contracts that are not actively queried, and checks payment schedules.

```
// Runs every MilestoneDeadlineCheckIntervalSeconds
// Creates service scope (ContractService is Scoped)

// Phase 1: Pending contracts
READ contract-statestore:status-idx:Pending -> contractIds
FOREACH contractId in contractIds
 // Calls GetContractInstanceStatusAsync which triggers Pending->Active lazy transition

// Phase 2: Active contracts
READ contract-statestore:status-idx:Active -> contractIds
FOREACH contractId in contractIds
 // Calls GetContractInstanceStatusAsync which triggers:
 // - Active->Expired (effectiveUntil passed)
 // - Overdue milestone enforcement
 // Check payment schedule
 IF instance has PaymentSchedule AND NextPaymentDue <= now
 // Advance payment: publish contract.payment.due, set NextPaymentDue to next interval
 PUBLISH contract.payment.due { contractId, templateCode, paymentSchedule, paymentNumber }
 ETAG-WRITE contract-statestore:instance:{contractId}
```
