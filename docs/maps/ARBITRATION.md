# Arbitration Implementation Map

> **Plugin**: lib-arbitration
> **Schema**: schemas/arbitration-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/ARBITRATION.md](../plugins/ARBITRATION.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-arbitration |
| Layer | L4 GameFeatures |
| Endpoints | 25 |
| State Stores | arbitration-cases (MySQL), arbitration-rulings (MySQL), arbitration-evidence (MySQL), arbitration-arbiters (Redis), arbitration-cache (Redis), arbitration-lock (Redis) |
| Events Published | 16 (`arbitration.case.created`, `arbitration.case.updated`, `arbitration.case.deleted`, `arbitration.case.filed`, `arbitration.case.closed`, `arbitration.case.defaulted`, `arbitration.notice.confirmed`, `arbitration.evidence.submitted`, `arbitration.hearing.completed`, `arbitration.ruling.issued`, `arbitration.ruling.appealed`, `arbitration.ruling.enforced`, `arbitration.arbiter.assigned`, `arbitration.arbiter.recused`, `arbitration.case.divine-requested`, `arbitration.jurisdiction.challenged`) |
| Events Consumed | 6 |
| Client Events | 0 |
| Background Services | 1 |

---

## State

**Store**: `arbitration-cases` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `case:{caseId}` | `ArbitrationCaseModel` | Primary case lookup by ID |
| `case-code:{gameServiceId}:{caseCode}` | `ArbitrationCaseModel` | Human-readable case code lookup within game service scope |
| `case-contract:{contractId}` | `ArbitrationCaseModel` | Reverse lookup from contract instance ID (for event handling) |

**Store**: `arbitration-rulings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ruling:{rulingId}` | `ArbitrationRulingModel` | Primary ruling lookup by ID |

**Store**: `arbitration-evidence` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `evidence:{evidenceId}` | `ArbitrationEvidenceModel` | Primary evidence lookup by ID |

**Store**: `arbitration-arbiters` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `qualified:{gameServiceId}:{caseType}` | `QualifiedArbiterListModel` | Cached qualified arbiters for case type (TTL-based) |
| `active:{arbiterId}` | `ArbiterCaseloadModel` | Active caseload tracking per arbiter |

**Store**: `arbitration-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `juris:{locationId}:{caseType}` | `JurisdictionResolutionModel` | Cached jurisdiction resolution (TTL-based, invalidated on territory changes) |

**Store**: `arbitration-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `arb:lock:case:{caseId}` | Case mutation lock |
| `arb:lock:ruling:{caseId}` | Ruling issuance lock (prevents duplicates) |
| `arb:lock:juris:{locationId}` | Jurisdiction resolution lock (serializes concurrent resolutions) |
| `arb:lock:arbiter:{arbiterId}` | Arbiter assignment lock (prevents over-assignment) |
| `arb:lock:deadline-worker` | Background worker singleton lock |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 6 stores: cases, rulings, evidence (MySQL); arbiters, cache, lock (Redis) |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Case mutation, ruling issuance, jurisdiction resolution, arbiter assignment locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 16 event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registering 6 consumed event handlers |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation (NullTelemetryProvider fallback when telemetry disabled) |
| lib-contract (`IContractClient`) | L1 | Hard | Creating arbitration contracts, milestone tracking, prebound API execution |
| lib-resource (`IResourceClient`) | L1 | Hard | Reference tracking, cleanup callback registration (character, realm, faction, location) |
| lib-character (`ICharacterClient`) | L2 | Hard | Validating character existence for party roles, custody assignment |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Validating game service scope |
| lib-location (`ILocationClient`) | L2 | Hard | Location hierarchy for jurisdiction determination |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | Relationship status changes from rulings |
| lib-currency (`ICurrencyClient`) | L2 | Hard | Monetary penalties (fines, reparations) |
| lib-inventory (`IInventoryClient`) | L2 | Hard | Shared asset identification for division |
| lib-seed (`ISeedClient`) | L2 | Hard | Seed bond dissolution for ruling consequences involving seed-bound relationships |
| lib-faction (`IFactionClient`) | L4 | Soft | Jurisdiction determination, sovereignty resolution, governance data queries. Functionally prerequisite but architecturally soft (L4→L4). All jurisdiction-dependent endpoints return error when absent. |
| lib-escrow (`IEscrowClient`) | L4 | Soft | Asset division when rulings involve property. Unavailable: rulings limited to non-asset consequences. |
| lib-obligation (`IObligationClient`) | L4 | Soft | Creating ongoing obligations from rulings (alimony, probation). Unavailable: one-time consequences only. |
| lib-puppetmaster (`IPuppetmasterClient`) | L4 | Soft | Divine arbiter requests, regional watcher notification. Unavailable: mortal arbiter only. |
| lib-status (`IStatusClient`) | L4 | Soft | Status effects from rulings (imprisonment, probation). Unavailable: consequences limited to relationship/asset/monetary. |
| lib-organization (`IOrganizationClient`) | L4 | Soft | Shared organizational assets, organizational legal status changes. Unavailable: organizational consequences disabled. |

**Notes**:
- `IStateStoreFactory` is a constructor parameter only (not stored as field); stores are resolved in constructor and cached as typed fields.
- `IServiceProvider` stored as field for runtime resolution of 6 soft L4 dependencies via `GetService<T>()`.
- Arbitration is a leaf node — no current consumers of `IArbitrationClient`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `arbitration.case.created` | `ArbitrationCaseCreatedEvent` | x-lifecycle: FileCase |
| `arbitration.case.updated` | `ArbitrationCaseUpdatedEvent` | x-lifecycle: WithdrawCase, DismissCase, ChallengeJurisdiction, AssignArbiter, RecuseArbiter, AppealRuling, EnforceRuling, event handlers |
| `arbitration.case.deleted` | `ArbitrationCaseDeletedEvent` | x-lifecycle: CleanupBy* endpoints |
| `arbitration.case.filed` | `ArbitrationCaseFiledEvent` | FileCase — jurisdiction accepted, contract created |
| `arbitration.case.closed` | `ArbitrationCaseClosedEvent` | WithdrawCase, DismissCase, EnforceRuling, HandleContractFulfilledAsync, HandleContractTerminatedAsync, CleanupBy* |
| `arbitration.case.defaulted` | `ArbitrationCaseDefaultedEvent` | HandleMilestoneFailedAsync (response deadline expiry), ArbitrationDeadlineWorkerService |
| `arbitration.notice.confirmed` | `ArbitrationNoticeConfirmedEvent` | HandleMilestoneCompletedAsync (service-confirmed milestone) |
| `arbitration.evidence.submitted` | `ArbitrationEvidenceSubmittedEvent` | SubmitEvidence |
| `arbitration.hearing.completed` | `ArbitrationHearingCompletedEvent` | HandleMilestoneCompletedAsync (hearing milestone) |
| `arbitration.ruling.issued` | `ArbitrationRulingIssuedEvent` | IssueRuling |
| `arbitration.ruling.appealed` | `ArbitrationRulingAppealedEvent` | AppealRuling |
| `arbitration.ruling.enforced` | `ArbitrationRulingEnforcedEvent` | EnforceRuling |
| `arbitration.arbiter.assigned` | `ArbitrationArbiterAssignedEvent` | AssignArbiter |
| `arbitration.arbiter.recused` | `ArbitrationArbiterRecusedEvent` | RecuseArbiter |
| `arbitration.case.divine-requested` | `ArbitrationDivineRequestedEvent` | AssignArbiter (DivineArbiter selection mode) |
| `arbitration.jurisdiction.challenged` | `ArbitrationJurisdictionChallengedEvent` | ChallengeJurisdiction |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.milestone.completed` | `HandleMilestoneCompletedAsync` | Advances case state for arbitration contract milestones. Publishes `arbitration.notice.confirmed` or `arbitration.hearing.completed` per milestone type. |
| `contract.milestone.failed` | `HandleMilestoneFailedAsync` | Handles deadline expiry (response deadline → default proceeding, publishes `arbitration.case.defaulted`). |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Marks case Closed, publishes `arbitration.case.closed`. |
| `contract.terminated` | `HandleContractTerminatedAsync` | Handles abnormal closure (withdrawal, dismissal), publishes `arbitration.case.closed`. |
| `faction.territory.claimed` | `HandleTerritoryClaimedAsync` | Invalidates jurisdiction cache for affected location. |
| `faction.territory.released` | `HandleTerritoryReleasedAsync` | Invalidates jurisdiction cache for affected location. |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<ArbitrationService>` | Structured logging |
| `ArbitrationServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (constructor parameter only — 6 stores resolved and cached) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Contract and faction event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ITelemetryProvider` | Span instrumentation |
| `IContractClient` | Arbitration contract lifecycle (L1) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `ICharacterClient` | Character validation for party roles (L2) |
| `IGameServiceClient` | Game service scope validation (L2) |
| `ILocationClient` | Location hierarchy for jurisdiction (L2) |
| `IRelationshipClient` | Relationship changes from rulings (L2) |
| `ICurrencyClient` | Monetary penalties from rulings (L2) |
| `IInventoryClient` | Shared asset identification (L2) |
| `ISeedClient` | Seed bond dissolution, sovereignty checks (L2) |
| `IServiceProvider` | Runtime resolution of 6 soft L4 dependencies |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| FileCase | POST /arbitration/case/file | generated | [] | case, case-code, case-contract, juris-cache | arbitration.case.created, arbitration.case.filed |
| GetCase | POST /arbitration/case/get | generated | [] | - | - |
| GetCaseByCode | POST /arbitration/case/get-by-code | generated | [] | - | - |
| ListCases | POST /arbitration/case/list | generated | [] | - | - |
| WithdrawCase | POST /arbitration/case/withdraw | generated | [] | case, case-code, case-contract | arbitration.case.updated, arbitration.case.closed |
| DismissCase | POST /arbitration/case/dismiss | generated | [] | case, case-code, case-contract | arbitration.case.updated, arbitration.case.closed |
| ChallengeJurisdiction | POST /arbitration/case/challenge-jurisdiction | generated | [] | case, case-code, case-contract | arbitration.case.updated, arbitration.jurisdiction.challenged |
| GetTimeline | POST /arbitration/case/get-timeline | generated | [] | - | - |
| SubmitEvidence | POST /arbitration/evidence/submit | generated | [] | evidence | arbitration.evidence.submitted |
| ListEvidence | POST /arbitration/evidence/list | generated | [] | - | - |
| GetEvidence | POST /arbitration/evidence/get | generated | [] | - | - |
| AssignArbiter | POST /arbitration/arbiter/assign | generated | [] | case, active-arbiter | arbitration.case.updated, arbitration.arbiter.assigned |
| RecuseArbiter | POST /arbitration/arbiter/recuse | generated | [] | case, active-arbiter | arbitration.case.updated, arbitration.arbiter.recused |
| ListQualifiedArbiters | POST /arbitration/arbiter/list-qualified | generated | [] | qualified-cache | - |
| GetArbiterCaseload | POST /arbitration/arbiter/get-caseload | generated | [] | - | - |
| IssueRuling | POST /arbitration/ruling/issue | generated | [] | ruling, case | arbitration.case.updated, arbitration.ruling.issued |
| AppealRuling | POST /arbitration/ruling/appeal | generated | [] | case | arbitration.case.updated, arbitration.ruling.appealed |
| GetRuling | POST /arbitration/ruling/get | generated | [] | - | - |
| EnforceRuling | POST /arbitration/ruling/enforce | generated | [] | case | arbitration.case.updated, arbitration.ruling.enforced, arbitration.case.closed |
| ResolveJurisdiction | POST /arbitration/jurisdiction/resolve | generated | [] | juris-cache | - |
| GetJurisdictionHierarchy | POST /arbitration/jurisdiction/get-hierarchy | generated | [] | - | - |
| CleanupByCharacter | POST /arbitration/cleanup-by-character | generated | [] | case | arbitration.case.closed |
| CleanupByRealm | POST /arbitration/cleanup-by-realm | generated | [] | case | arbitration.case.closed |
| CleanupByFaction | POST /arbitration/cleanup-by-faction | generated | [] | case, juris-cache | arbitration.case.closed |
| CleanupByLocation | POST /arbitration/cleanup-by-location | generated | [] | case, juris-cache | arbitration.case.closed |

---

## Methods

### FileCase
POST /arbitration/case/file | Roles: []

```
CALL _gameServiceClient.GetGameServiceAsync(gameServiceId)       -> 404 if null
CALL _characterClient.GetCharacterAsync(petitionerId)            -> 404 if null (when entityType == Character)
CALL _characterClient.GetCharacterAsync(respondentId)            -> 404 if null (when entityType == Character)

// Jurisdiction resolution (cache-first)
READ _cacheStore:juris:{locationId}:{caseType}                   -> jurisdictionResolution
IF jurisdictionResolution == null
  LOCK _lockStore:arb:lock:juris:{locationId}
    CALL _locationClient.GetLocationAsync(locationId)            -> 404 if null
    CALL _factionClient.QueryGovernanceDataAsync(locationId, domain: caseType) -> 400 if Faction unavailable
    // QueryGovernanceData resolves jurisdiction by walking sovereignty hierarchy
    // Returns { domain, templateCode, governanceParameters } or 404 if no sovereign has jurisdiction
    IF no governance data for domain                              -> 400
    WRITE _cacheStore:juris:{locationId}:{caseType} <- JurisdictionResolutionModel (TTL: config.JurisdictionCacheTtlMinutes)

// Create case
LOCK _lockStore:arb:lock:case:{caseId}
  CALL _contractClient.CreateContractAsync(templateCode, governanceParameters)
  // governanceParameters passed unchanged (opaque pass-through per FOUNDATION TENETS)
  WRITE _caseStore:case:{caseId} <- ArbitrationCaseModel
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- ArbitrationCaseModel
  WRITE _caseStore:case-contract:{contractId} <- ArbitrationCaseModel

PUBLISH arbitration.case.created { full case model }
PUBLISH arbitration.case.filed { caseId, caseCode, caseType, locationId, sovereignFactionId, petitioner, respondent, templateCode }
RETURN (200, FileCaseResponse { caseId, caseCode, proceduralTimeline })
```

### GetCase
POST /arbitration/case/get | Roles: []

```
READ _caseStore:case:{caseId}                                    -> 404 if null
CALL _contractClient.GetContractMilestonesAsync(case.contractId)
// Enrich with current milestone status
RETURN (200, GetCaseResponse)
```

### GetCaseByCode
POST /arbitration/case/get-by-code | Roles: []

```
READ _caseStore:case-code:{gameServiceId}:{caseCode}             -> 404 if null
RETURN (200, GetCaseResponse)
```

### ListCases
POST /arbitration/case/list | Roles: []

```
QUERY _caseStore WHERE $.gameServiceId == gameServiceId          // required
  [AND $.caseType == caseType]                                   // optional
  [AND $.status == status]                                       // optional
  [AND $.petitionerEntityId == petitionerEntityId]               // optional
  [AND $.respondentEntityId == respondentEntityId]               // optional
  [AND $.arbiterEntityId == arbiterEntityId]                     // optional
  [AND $.sovereignFactionId == jurisdictionFactionId]            // optional
  ORDER BY $.filingTimestamp DESC
  PAGED(page, pageSize)
RETURN (200, ListCasesResponse { cases, totalCount, page, pageSize })
```

### WithdrawCase
POST /arbitration/case/withdraw | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status is terminal                                       -> 409
LOCK _lockStore:arb:lock:case:{caseId}
  CALL _contractClient.TerminateContractAsync(case.contractId, reason: "Withdrawal")
  WRITE _caseStore:case:{caseId} <- status: Withdrawn
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated
PUBLISH arbitration.case.updated { changedFields: [status] }
PUBLISH arbitration.case.closed { caseId, closedReason: Withdrawal }
RETURN (200, WithdrawCaseResponse)
```

### DismissCase
POST /arbitration/case/dismiss | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status is terminal                                       -> 409
LOCK _lockStore:arb:lock:case:{caseId}
  CALL _contractClient.TerminateContractAsync(case.contractId, reason: "Dismissal")
  WRITE _caseStore:case:{caseId} <- status: Dismissed, dismissalReasoning
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated
PUBLISH arbitration.case.updated { changedFields: [status] }
PUBLISH arbitration.case.closed { caseId, closedReason: Dismissal }
RETURN (200, DismissCaseResponse)
```

### ChallengeJurisdiction
POST /arbitration/case/challenge-jurisdiction | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status is terminal                                       -> 409
LOCK _lockStore:arb:lock:case:{caseId}
  WRITE _caseStore:case:{caseId} <- jurisdictionChallenged: true, challengeReasoning, challengingParty
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated
PUBLISH arbitration.case.updated { changedFields: [jurisdictionChallenged] }
PUBLISH arbitration.jurisdiction.challenged { caseId, challengingParty, reasoning }
RETURN (200, ChallengeJurisdictionResponse)
```

### GetTimeline
POST /arbitration/case/get-timeline | Roles: []

```
READ _caseStore:case:{caseId}                                    -> 404 if null
CALL _contractClient.GetContractMilestonesAsync(case.contractId)
// Compute deadline dates, current phase, days remaining from Contract milestones
RETURN (200, GetTimelineResponse { milestones, currentPhase, daysRemaining })
```

### SubmitEvidence
POST /arbitration/evidence/submit | Roles: []

```
READ _caseStore:case:{caseId}                                    -> 404 if null
IF case.status not in evidence-accepting phase                   -> 409
IF submittingParty not in {petitioner, respondent}               -> 400
COUNT _evidenceStore WHERE $.caseId == caseId AND $.submittingPartyEntityId == submittingPartyEntityId
IF count >= config.MaxEvidenceItemsPerParty                      -> 409
WRITE _evidenceStore:evidence:{evidenceId} <- ArbitrationEvidenceModel
PUBLISH arbitration.evidence.submitted { evidenceId, caseId, submittingParty, evidenceType }
RETURN (200, SubmitEvidenceResponse { evidenceId })
```

### ListEvidence
POST /arbitration/evidence/list | Roles: []

```
READ _caseStore:case:{caseId}                                    -> 404 if null
QUERY _evidenceStore WHERE $.caseId == caseId
  [AND $.submittingPartyEntityId == partyEntityId]               // optional
  ORDER BY $.submissionTimestamp ASC
  PAGED(page, pageSize)
RETURN (200, ListEvidenceResponse { evidence, totalCount, page, pageSize })
```

### GetEvidence
POST /arbitration/evidence/get | Roles: []

```
READ _evidenceStore:evidence:{evidenceId}                        -> 404 if null
RETURN (200, GetEvidenceResponse)
```

### AssignArbiter
POST /arbitration/arbiter/assign | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.arbiterEntityId != null                                  -> 409 (already assigned)

IF selectionMode == FactionLeader
  CALL _factionClient.GetFactionLeaderAsync(sovereignFactionId)  -> 400 if Faction unavailable
ELSE IF selectionMode == AppointedOfficial
  // Read designated arbiter from governance data
ELSE IF selectionMode == DivineArbiter
  PUBLISH arbitration.case.divine-requested { caseId, timeoutAt }
  // Deferred assignment — divine actor responds via API
  RETURN (200, AssignArbiterResponse { caseId, pending: true })
ELSE IF selectionMode == PeerPanel
  // Peer panel mechanics — not yet specified

// Validate caseload
LOCK _lockStore:arb:lock:arbiter:{arbiterId}
  READ _arbiterStore:active:{arbiterId}
  IF caseload.activeCaseCount >= config.MaxActiveCasesPerArbiter -> 409
  WRITE _arbiterStore:active:{arbiterId} <- activeCaseCount++

  LOCK _lockStore:arb:lock:case:{caseId}
    WRITE _caseStore:case:{caseId} <- arbiterEntityId, arbiterEntityType, status update
    WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
    WRITE _caseStore:case-contract:{contractId} <- updated

PUBLISH arbitration.case.updated { changedFields: [arbiterEntityId, status] }
PUBLISH arbitration.arbiter.assigned { caseId, arbiterId, selectionMode }
RETURN (200, AssignArbiterResponse { caseId, arbiterId })
```

### RecuseArbiter
POST /arbitration/arbiter/recuse | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.arbiterEntityId == null                                  -> 404 (no arbiter assigned)
IF case.status is terminal                                       -> 409

LOCK _lockStore:arb:lock:case:{caseId}
  LOCK _lockStore:arb:lock:arbiter:{case.arbiterEntityId}
    READ _arbiterStore:active:{case.arbiterEntityId}
    WRITE _arbiterStore:active:{case.arbiterEntityId} <- activeCaseCount--
  WRITE _caseStore:case:{caseId} <- arbiterEntityId: null
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated

PUBLISH arbitration.case.updated { changedFields: [arbiterEntityId] }
PUBLISH arbitration.arbiter.recused { caseId, recusedArbiterId, reason }
// Triggers re-assignment (mechanism not yet specified)
RETURN (200, RecuseArbiterResponse)
```

### ListQualifiedArbiters
POST /arbitration/arbiter/list-qualified | Roles: []

```
READ _arbiterStore:qualified:{gameServiceId}:{caseType}
IF null (cache miss)
  // Resolve qualified arbiters from governance data for this domain
  // No dedicated Faction endpoint — derive from governance records for the game service scope
  CALL _factionClient.QueryGovernanceDataAsync(locationId, domain: caseType) -> 400 if Faction unavailable
  // Extract arbiter designations from governanceParameters (template-defined arbiter roles)
  WRITE _arbiterStore:qualified:{gameServiceId}:{caseType} <- QualifiedArbiterListModel (TTL)

RETURN (200, ListQualifiedArbitersResponse { arbiters })
```

### GetArbiterCaseload
POST /arbitration/arbiter/get-caseload | Roles: []

```
READ _arbiterStore:active:{arbiterId}
// No arbiter caseload record = zero active cases (not 404)
IF null -> return empty caseload
RETURN (200, GetArbiterCaseloadResponse { arbiterId, activeCaseCount, caseIds })
```

### IssueRuling
POST /arbitration/ruling/issue | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status not in ruling-eligible phase                      -> 409

LOCK _lockStore:arb:lock:ruling:{caseId}                         -> 409 if fails (duplicate prevention)
  LOCK _lockStore:arb:lock:case:{caseId}
    // Check for existing ruling (idempotency guard)
    QUERY _rulingStore WHERE $.caseId == caseId                  -> 409 if exists

    WRITE _rulingStore:ruling:{rulingId} <- ArbitrationRulingModel { rulingType, consequenceManifest, reasoning, appealDeadline }

    // Execute consequences (best-effort, per-item isolation)
    FOREACH consequence in consequenceManifest
      IF consequence.type == RelationshipChange
        CALL _relationshipClient.UpdateRelationshipAsync(...)
      ELSE IF consequence.type == AssetDivision
        CALL _escrowClient?.CreateEscrowAsync(...)               // soft dep, skip if absent
      ELSE IF consequence.type == OngoingObligation
        CALL _obligationClient?.CreateObligationAsync(...)       // soft dep, skip if absent
      ELSE IF consequence.type == CustodyAssignment
        CALL _characterClient.UpdateCharacterAsync(...)
      ELSE IF consequence.type == Exile
        CALL _factionClient?.ExileMemberAsync(...)               // soft dep, skip if absent
      ELSE IF consequence.type == Fine
        CALL _currencyClient.TransferAsync(respondent, sovereign, amount)
      ELSE IF consequence.type == Imprisonment
        CALL _statusClient?.ApplyStatusAsync(...)                // soft dep, skip if absent
      ELSE IF consequence.type == SovereigntyTransfer
        CALL _factionClient?.TransferSovereigntyAsync(...)       // soft dep, skip if absent
      // Per-item: catch ApiException, log warning, continue

    CALL _contractClient.AdvanceMilestoneAsync(case.contractId, "ruling_issued")
    WRITE _caseStore:case:{caseId} <- status: Ruled, rulingId
    WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
    WRITE _caseStore:case-contract:{contractId} <- updated

PUBLISH arbitration.case.updated { changedFields: [status, rulingId] }
PUBLISH arbitration.ruling.issued { rulingId, caseId, arbiterId, rulingType, consequenceManifest }
RETURN (200, IssueRulingResponse { rulingId })
```

### AppealRuling
POST /arbitration/ruling/appeal | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status != Ruled                                          -> 409
READ _rulingStore:ruling:{case.rulingId}                         -> 404 if null
IF now > ruling.appealDeadline                                   -> 409 (appeal window expired)

LOCK _lockStore:arb:lock:case:{caseId}
  WRITE _caseStore:case:{caseId} <- status: Appealed, reset to evidence phase for appellate
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated

PUBLISH arbitration.case.updated { changedFields: [status] }
PUBLISH arbitration.ruling.appealed { rulingId, caseId, appealingParty }
RETURN (200, AppealRulingResponse)
```

### GetRuling
POST /arbitration/ruling/get | Roles: []

```
IF rulingId provided
  READ _rulingStore:ruling:{rulingId}                            -> 404 if null
ELSE IF caseId provided
  READ _caseStore:case:{caseId}                                  -> 404 if null
  IF case.rulingId == null                                       -> 404
  READ _rulingStore:ruling:{case.rulingId}                       -> 404 if null
RETURN (200, GetRulingResponse)
```

### EnforceRuling
POST /arbitration/ruling/enforce | Roles: []

```
READ _caseStore:case:{caseId} [with ETag]                        -> 404 if null
IF case.status != Ruled                                          -> 409
READ _rulingStore:ruling:{case.rulingId}                         -> 404 if null

LOCK _lockStore:arb:lock:case:{caseId}
  // Verify all consequences executed (check downstream service state)
  FOREACH consequence in ruling.consequenceManifest
    // Re-execute any missed consequences (best-effort)

  CALL _contractClient.AdvanceMilestoneAsync(case.contractId, "enforcement_confirmed")
  WRITE _caseStore:case:{caseId} <- status: Closed
  WRITE _caseStore:case-code:{gameServiceId}:{caseCode} <- updated
  WRITE _caseStore:case-contract:{contractId} <- updated

PUBLISH arbitration.case.updated { changedFields: [status] }
PUBLISH arbitration.ruling.enforced { rulingId, caseId }
PUBLISH arbitration.case.closed { caseId, closedReason: RulingEnforced }
RETURN (200, EnforceRulingResponse)
```

### ResolveJurisdiction
POST /arbitration/jurisdiction/resolve | Roles: []

```
READ _cacheStore:juris:{locationId}:{caseType}
IF cached -> RETURN (200, ResolveJurisdictionResponse { from cache })

CALL _factionClient (soft)                                       -> 400 if unavailable
LOCK _lockStore:arb:lock:juris:{locationId}
  // Double-check cache after lock
  READ _cacheStore:juris:{locationId}:{caseType}
  IF cached -> RETURN (200, ...)

  CALL _locationClient.GetLocationAsync(locationId)              -> 404 if null
  CALL _factionClient.QueryGovernanceDataAsync(locationId, domain: caseType)
  // QueryGovernanceData walks sovereignty hierarchy: delegated -> sovereign -> realm baseline
  // Enclave sovereignty: nested sovereign overrides within its boundary
  // Returns { domain, templateCode, governanceParameters } or 404
  IF no governance data found at any level                       -> 404

  WRITE _cacheStore:juris:{locationId}:{caseType} <- JurisdictionResolutionModel (TTL: config.JurisdictionCacheTtlMinutes)

RETURN (200, ResolveJurisdictionResponse { sovereignFactionId, authorityLevel, templateCode, governanceParameters })
```

### GetJurisdictionHierarchy
POST /arbitration/jurisdiction/get-hierarchy | Roles: []

```
CALL _locationClient.GetLocationAsync(locationId)                -> 404 if null
CALL _factionClient (soft)                                       -> 400 if unavailable
CALL _factionClient.GetSovereigntyHierarchyAsync(locationId)
RETURN (200, GetJurisdictionHierarchyResponse { hierarchy })
```

### CleanupByCharacter
POST /arbitration/cleanup-by-character | Roles: []

```
// Resource-managed CASCADE cleanup
QUERY _caseStore WHERE ($.petitionerEntityId == characterId OR $.respondentEntityId == characterId)
  AND $.status NOT IN [Closed, Dismissed, Withdrawn]

FOREACH case in activeCases
  TRY
    LOCK _lockStore:arb:lock:case:{case.caseId}
      CALL _contractClient.TerminateContractAsync(case.contractId, "PartyDeleted")
      WRITE _caseStore:case:{case.caseId} <- status: Closed, closedReason: PartyCharacterDeleted
      WRITE _caseStore:case-code:{...} <- updated
      WRITE _caseStore:case-contract:{...} <- updated
    PUBLISH arbitration.case.closed { caseId, closedReason: PartyCharacterDeleted }
  CATCH -> LogWarning, continue  // Per-item isolation

RETURN (200, CleanupResponse)
```

### CleanupByRealm
POST /arbitration/cleanup-by-realm | Roles: []

```
// Resource-managed cleanup
QUERY _caseStore WHERE $.locationId IN realm locations
  AND $.status NOT IN [Closed, Dismissed, Withdrawn]

FOREACH case in activeCases
  TRY
    LOCK _lockStore:arb:lock:case:{case.caseId}
      CALL _contractClient.TerminateContractAsync(case.contractId, "RealmDeleted")
      WRITE _caseStore:case:{case.caseId} <- status: Closed, closedReason: RealmDeleted
      WRITE _caseStore:case-code:{...} <- updated
      WRITE _caseStore:case-contract:{...} <- updated
    PUBLISH arbitration.case.closed { caseId, closedReason: RealmDeleted }
  CATCH -> LogWarning, continue  // Per-item isolation

RETURN (200, CleanupResponse)
```

### CleanupByFaction
POST /arbitration/cleanup-by-faction | Roles: []

```
// Resource-managed cleanup
QUERY _caseStore WHERE $.sovereignFactionId == factionId
  AND $.status NOT IN [Closed, Dismissed, Withdrawn]

FOREACH case in activeCases
  TRY
    LOCK _lockStore:arb:lock:case:{case.caseId}
      CALL _contractClient.TerminateContractAsync(case.contractId, "SovereignFactionDeleted")
      WRITE _caseStore:case:{case.caseId} <- status: Closed, closedReason: JurisdictionVoid
      WRITE _caseStore:case-code:{...} <- updated
      WRITE _caseStore:case-contract:{...} <- updated
    PUBLISH arbitration.case.closed { caseId, closedReason: JurisdictionVoid }
  CATCH -> LogWarning, continue  // Per-item isolation

// Invalidate jurisdiction cache entries referencing this faction
DELETE _cacheStore entries WHERE sovereignFactionId == factionId

RETURN (200, CleanupResponse)
```

### CleanupByLocation
POST /arbitration/cleanup-by-location | Roles: []

```
// Resource-managed cleanup
QUERY _caseStore WHERE $.locationId == locationId
  AND $.status NOT IN [Closed, Dismissed, Withdrawn]

FOREACH case in activeCases
  TRY
    LOCK _lockStore:arb:lock:case:{case.caseId}
      CALL _contractClient.TerminateContractAsync(case.contractId, "LocationDeleted")
      WRITE _caseStore:case:{case.caseId} <- status: Closed, closedReason: JurisdictionVoid
      WRITE _caseStore:case-code:{...} <- updated
      WRITE _caseStore:case-contract:{...} <- updated
    PUBLISH arbitration.case.closed { caseId, closedReason: JurisdictionVoid }
  CATCH -> LogWarning, continue  // Per-item isolation

DELETE _cacheStore:juris:{locationId}:*  // all case types

RETURN (200, CleanupResponse)
```

---

## Background Services

### ArbitrationDeadlineWorkerService
**Interval**: `config.DeadlineCheckIntervalMinutes` (default: 60 min)
**Initial Delay**: `config.DeadlineCheckDelayMinutes` (default: 5 min)
**Purpose**: Reconciliation safety net for cases with expired milestones not caught by Contract milestone events.

```
LOCK _lockStore:arb:lock:deadline-worker                         // singleton lock
IF lock not acquired -> return

QUERY _caseStore WHERE $.status NOT IN [Closed, Dismissed, Withdrawn]
  AND $.nextMilestoneDeadline < now
  ORDER BY $.nextMilestoneDeadline ASC
  PAGED(1, config.DeadlineCheckBatchSize)

FOREACH case in expiredCases
  TRY
    CALL _contractClient.GetContractMilestonesAsync(case.contractId)
    IF milestone already failed -> skip (event already handled)
    CALL _contractClient.FailMilestoneAsync(case.contractId, expiredMilestone)
    // This fires contract.milestone.failed which HandleMilestoneFailedAsync processes
  CATCH -> LogWarning, continue  // Per-item isolation
```

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 25 endpoints are standard generated-interface endpoints. No `x-manual-implementation`, `x-controller-only`, or manually-registered routes. Plugin lifecycle limited to standard resource cleanup callback registration and event consumer registration.
