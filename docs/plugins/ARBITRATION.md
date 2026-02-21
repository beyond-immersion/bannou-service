# Arbitration Plugin Deep Dive

> **Plugin**: lib-arbitration (not yet created)
> **Schema**: `schemas/arbitration-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: arbitration-cases (MySQL), arbitration-rulings (MySQL), arbitration-evidence (MySQL), arbitration-arbiters (Redis), arbitration-cache (Redis), arbitration-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [MORALITY-SYSTEM-NEXT-STEPS.md](../planning/MORALITY-SYSTEM-NEXT-STEPS.md)

## Overview

Authoritative dispute resolution service (L4 GameFeatures) for competing claims that need jurisdictional ruling and enforcement. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver adjudication game mechanics. Game-agnostic: procedural templates, arbiter selection rules, and cultural attitudes toward litigation are configured through contract templates and faction governance data at deployment time. Internal-only, never internet-facing.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Case records (MySQL), ruling records (MySQL), evidence records (MySQL), arbiter assignments (Redis), jurisdiction cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for case mutations, ruling issuance, jurisdiction resolution |
| lib-messaging (`IMessageBus`) | Publishing case lifecycle events, ruling events, enforcement events |
| lib-messaging (`IEventConsumer`) | Registering handlers for contract milestone completions and faction governance changes |
| lib-contract (`IContractClient`) | Creating arbitration contracts from procedural templates, milestone tracking, prebound API execution (L1) |
| lib-faction (`IFactionClient`) | Jurisdiction determination, sovereignty resolution, governance data queries, territory control queries (L4 -- see note) |
| lib-relationship (`IRelationshipClient`) | Executing relationship status changes from rulings (married -> divorced, member -> exiled) (L2) |
| lib-character (`ICharacterClient`) | Validating character existence for party roles, custody assignment (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service scope (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |
| lib-location (`ILocationClient`) | Resolving location hierarchy for jurisdiction determination (L2) |
| lib-currency (`ICurrencyClient`) | Executing monetary penalties (fines, reparations) (L2) |
| lib-inventory (`IInventoryClient`) | Identifying shared assets for division (L2) |
| lib-seed (`ISeedClient`) | Seed bond dissolution, sovereignty capability checks (L2) |

**Note on Faction dependency**: Faction is L4, same layer as Arbitration. L4-to-L4 dependencies must handle graceful degradation per the service hierarchy. However, Arbitration is fundamentally meaningless without Faction (no jurisdiction = no arbitration). This is a hard dependency in practice, documented as such. If Faction is disabled, Arbitration should also be disabled. This is analogous to how Quest is meaningless without Contract -- the orchestration layer requires its substrate.

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-escrow (`IEscrowClient`) | Asset division when rulings involve property | Asset division unavailable; rulings limited to non-asset consequences (relationship changes, fines, exile) |
| lib-obligation (`IObligationClient`) | Creating ongoing obligation contracts from rulings (alimony, probation) | Ongoing obligations not created; ruling is one-time consequence only |
| lib-puppetmaster (`IPuppetmasterClient`) | Divine arbiter requests, regional watcher notification | Divine arbitration unavailable; falls back to mortal arbiter only |
| lib-status (`IStatusClient`) | Applying status effects from rulings (imprisonment, probation restrictions) | Status effects not applied; ruling consequences limited to relationship/asset/monetary |
| lib-organization (`IOrganizationClient`) | Identifying shared organizational assets, organizational legal status changes | Organizational asset division unavailable; organization-level consequences disabled |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Arbitration is a new L4 service with no current consumers. Future dependents: lib-organization (charter disputes, succession contests), lib-faction (sovereignty disputes consume arbitration cases), Puppetmaster (divine actors interact with arbitration as arbiters), Gardener (player-facing arbitration UX -- presenting court proceedings as interactive gameplay), Storyline (arbitration rulings and disputes as narrative material for the content flywheel) |

---

## State Storage

### Case Store
**Store**: `arbitration-cases` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `case:{caseId}` | `ArbitrationCaseModel` | Primary lookup by case ID. Stores case type, jurisdiction (sovereign faction ID, location ID), petitioner/respondent party references (entity type + ID), arbiter reference, contract instance ID, status, governance parameters snapshot, filing timestamp, ruling deadline. |
| `case-code:{gameServiceId}:{caseCode}` | `ArbitrationCaseModel` | Human-readable case code lookup within game service scope (e.g., `ARC-DISS-00142`) |
| `case-contract:{contractId}` | `ArbitrationCaseModel` | Reverse lookup from contract instance ID to case (for contract event handling) |

### Ruling Store
**Store**: `arbitration-rulings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ruling:{rulingId}` | `ArbitrationRulingModel` | Primary lookup by ruling ID. Stores case reference, arbiter reference, ruling type (petitioner_favored, respondent_favored, split, dismissed), consequence manifest (list of consequence actions with downstream service references), reasoning text (optional -- NPC arbiters may produce reasoning from cognition), issuance timestamp, appeal deadline. |

### Evidence Store
**Store**: `arbitration-evidence` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `evidence:{evidenceId}` | `ArbitrationEvidenceModel` | Primary lookup by evidence ID. Stores case reference, submitting party, evidence type (testimony, document, witness, physical), content (structured data -- not free text), submission timestamp, relevance score (arbiter-assessed). |

### Arbiter Store
**Store**: `arbitration-arbiters` (Backend: Redis, prefix: `arb:arbiter`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `qualified:{gameServiceId}:{caseType}` | `QualifiedArbiterListModel` | Cached list of qualified arbiters for a case type within a game service. Rebuilt from faction governance data. TTL-based expiry. |
| `active:{arbiterId}` | `ArbiterCaseloadModel` | Active caseload for an arbiter. Tracks concurrent case count for load balancing. |

### Jurisdiction Cache
**Store**: `arbitration-cache` (Backend: Redis, prefix: `arb:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `juris:{locationId}:{caseType}` | `JurisdictionResolutionModel` | Cached jurisdiction resolution: sovereign faction, governance data, procedural template code. TTL-based expiry. Invalidated on faction territory or sovereignty changes. |

### Distributed Locks
**Store**: `arbitration-lock` (Backend: Redis, prefix: `arb:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `case:{caseId}` | Case mutation lock (state transitions, evidence submission, ruling issuance) |
| `ruling:{caseId}` | Ruling issuance lock (prevents duplicate rulings for same case) |
| `juris:{locationId}` | Jurisdiction resolution lock (serializes concurrent resolutions for same location) |
| `arbiter:{arbiterId}` | Arbiter assignment lock (prevents over-assignment) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `arbitration.case.filed` | `ArbitrationCaseFiledEvent` | New case created and jurisdiction accepted |
| `arbitration.case.updated` | `ArbitrationCaseUpdatedEvent` | Case metadata updated |
| `arbitration.case.closed` | `ArbitrationCaseClosedEvent` | Case reached terminal state (fulfilled, dismissed, or withdrawn) |
| `arbitration.case.defaulted` | `ArbitrationCaseDefaultedEvent` | Respondent failed to respond within deadline |
| `arbitration.service.confirmed` | `ArbitrationServiceConfirmedEvent` | Respondent formally notified of proceedings |
| `arbitration.evidence.submitted` | `ArbitrationEvidenceSubmittedEvent` | Evidence item submitted by a party |
| `arbitration.hearing.completed` | `ArbitrationHearingCompletedEvent` | Hearing milestone reached (if applicable) |
| `arbitration.ruling.issued` | `ArbitrationRulingIssuedEvent` | Arbiter issues ruling with consequence manifest |
| `arbitration.ruling.appealed` | `ArbitrationRulingAppealedEvent` | Party files appeal within appeal window |
| `arbitration.ruling.enforced` | `ArbitrationRulingEnforcedEvent` | Ruling consequences fully executed and verified |
| `arbitration.arbiter.assigned` | `ArbitrationArbiterAssignedEvent` | Arbiter assigned to case |
| `arbitration.arbiter.recused` | `ArbitrationArbiterRecusedEvent` | Arbiter removed from case (conflict of interest, corruption detected) |
| `arbitration.case.divine_requested` | `ArbitrationDivineRequestedEvent` | Party requests divine arbiter intervention |
| `arbitration.jurisdiction.challenged` | `ArbitrationJurisdictionChallengedEvent` | Party challenges the determined jurisdiction |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.milestone.completed` | `HandleMilestoneCompletedAsync` | For arbitration contract instances: advance case state to match milestone. Trigger next phase (e.g., evidence closed -> notify arbiter for hearing). |
| `contract.milestone.failed` | `HandleMilestoneFailedAsync` | For arbitration contract instances: handle deadline expiry (e.g., response deadline -> default proceeding). |
| `contract.fulfilled` | `HandleContractFulfilledAsync` | Arbitration contract completed: mark case as Closed. Archive case data. |
| `contract.terminated` | `HandleContractTerminatedAsync` | Arbitration contract terminated: handle abnormal case closure (withdrawal, dismissal). |
| `faction.territory.claimed` | `HandleTerritoryClaimedAsync` | Invalidate jurisdiction cache for affected location. Active cases at this location may need jurisdiction re-evaluation. |
| `faction.territory.released` | `HandleTerritoryReleasedAsync` | Invalidate jurisdiction cache. Active cases may fall through to realm baseline. |

### Resource Cleanup (FOUNDATION TENETS)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| character | arbitration | CASCADE | `/arbitration/cleanup-by-character` |
| realm | arbitration | CASCADE | `/arbitration/cleanup-by-realm` |

### DI Listener Patterns

| Pattern | Interface | Action |
|---------|-----------|--------|
| *(none)* | -- | Arbitration does not implement DI listener interfaces. It is a pure orchestration layer that reacts to events and API calls. |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultAppealWindowDays` | `ARBITRATION_DEFAULT_APPEAL_WINDOW_DAYS` | `14` | Default appeal window when governance parameters don't specify |
| `DefaultResponseDeadlineDays` | `ARBITRATION_DEFAULT_RESPONSE_DEADLINE_DAYS` | `7` | Default response deadline for service |
| `DefaultEvidenceWindowDays` | `ARBITRATION_DEFAULT_EVIDENCE_WINDOW_DAYS` | `14` | Default evidence submission window |
| `MaxEvidenceItemsPerParty` | `ARBITRATION_MAX_EVIDENCE_ITEMS_PER_PARTY` | `20` | Maximum evidence items per party per case |
| `MaxActiveCasesPerArbiter` | `ARBITRATION_MAX_ACTIVE_CASES_PER_ARBITER` | `5` | Maximum concurrent cases for a single arbiter |
| `JurisdictionCacheTtlMinutes` | `ARBITRATION_JURISDICTION_CACHE_TTL_MINUTES` | `30` | TTL for cached jurisdiction resolutions |
| `CaseCodePrefix` | `ARBITRATION_CASE_CODE_PREFIX` | `ARB` | Prefix for human-readable case codes |
| `DistributedLockTimeoutSeconds` | `ARBITRATION_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `DefaultDeadlineCheckIntervalMinutes` | `ARBITRATION_DEFAULT_DEADLINE_CHECK_INTERVAL_MINUTES` | `60` | How often the deadline worker checks for expired milestones |
| `DefaultDeadlineCheckDelayMinutes` | `ARBITRATION_DEFAULT_DEADLINE_CHECK_DELAY_MINUTES` | `5` | Initial delay before deadline worker starts |
| `DefaultDeadlineCheckBatchSize` | `ARBITRATION_DEFAULT_DEADLINE_CHECK_BATCH_SIZE` | `50` | Cases per batch in deadline worker |
| `QueryPageSize` | `ARBITRATION_QUERY_PAGE_SIZE` | `20` | Default page size for paged queries |
| `DivineArbitrationTimeoutDays` | `ARBITRATION_DIVINE_ARBITRATION_TIMEOUT_DAYS` | `7` | How long to wait for divine arbiter response before falling back to mortal |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<ArbitrationService>` | Structured logging |
| `ArbitrationServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Contract and faction event subscriptions |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IContractClient` | Arbitration contract lifecycle -- creation from template, milestone tracking (L1) |
| `IFactionClient` | Jurisdiction resolution, sovereignty queries, governance data (L4) |
| `IRelationshipClient` | Relationship status changes from rulings (L2) |
| `ICharacterClient` | Character validation for party roles (L2) |
| `IGameServiceClient` | Game service scope validation (L2) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1) |
| `ILocationClient` | Location hierarchy for jurisdiction resolution (L2) |
| `ICurrencyClient` | Monetary penalties from rulings (L2) |
| `IInventoryClient` | Shared asset identification for division (L2) |
| `ISeedClient` | Seed bond dissolution, sovereignty capability checks (L2) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Background Workers

| Worker | Interval Config | Lock Key | Purpose |
|--------|----------------|----------|---------|
| `ArbitrationDeadlineWorkerService` | `DefaultDeadlineCheckIntervalMinutes` | `arb:lock:deadline-worker` | Periodically checks for cases with expired milestones (response deadlines, evidence windows, appeal windows). Advances expired milestones to trigger default proceedings or case closure. |

---

## API Endpoints (Implementation Notes)

### Case Management (8 endpoints)

All endpoints require `developer` role.

- **FileCase** (`/arbitration/case/file`): Validates game service and party existence. Resolves jurisdiction: queries Faction for sovereign at location, retrieves governance data for case type. Validates sovereign has governance data for this case type (rejects if no procedural template exists). Creates Contract instance from procedural template code via `IContractClient`, passing governance parameters to `SetTemplateValues`. Acquires distributed lock on case creation. Saves case record under ID, code, and contract lookup keys. Publishes `arbitration.case.filed`. Returns case ID and procedural timeline (computed from governance parameters).

- **GetCase** (`/arbitration/case/get`): Load from MySQL by caseId. Enriches with current contract milestone status via `IContractClient`.

- **GetCaseByCode** (`/arbitration/case/get-by-code`): JSON query by gameServiceId + case code.

- **ListCases** (`/arbitration/case/list`): Paged JSON query with required gameServiceId filter, optional case type, status, petitioner, respondent, arbiter, and jurisdiction faction filters.

- **WithdrawCase** (`/arbitration/case/withdraw`): Petitioner-initiated. Acquires lock. Terminates contract. Marks case as Withdrawn. Publishes `arbitration.case.closed` with withdrawal reason. May carry costs (governance parameters may specify withdrawal penalties).

- **DismissCase** (`/arbitration/case/dismiss`): Arbiter-initiated. Acquires lock. Terminates contract. Marks case as Dismissed. Publishes `arbitration.case.closed` with dismissal reasoning.

- **ChallengeJurisdiction** (`/arbitration/case/challenge-jurisdiction`): Party-initiated. Records the challenge with reasoning. Arbiter (or higher sovereign) evaluates. May reassign jurisdiction or dismiss challenge. Publishes `arbitration.jurisdiction.challenged`.

- **GetTimeline** (`/arbitration/case/get-timeline`): Returns computed timeline for a case: deadline dates for each milestone, current phase, days remaining. Computed from governance parameters and case filing date.

### Evidence Management (3 endpoints)

- **SubmitEvidence** (`/arbitration/evidence/submit`): Validates case is in evidence-accepting phase (between filing and evidence window close). Validates submitting party is petitioner or respondent. Validates evidence count below `MaxEvidenceItemsPerParty`. Saves evidence record. Publishes `arbitration.evidence.submitted`.

- **ListEvidence** (`/arbitration/evidence/list`): Paged query by caseId, optional party filter.

- **GetEvidence** (`/arbitration/evidence/get`): Load by evidenceId.

### Arbiter Management (4 endpoints)

- **AssignArbiter** (`/arbitration/arbiter/assign`): Resolves arbiter selection mode from governance data. For `faction_leader` mode: queries faction for current leader. For `appointed_official` mode: queries governance data for designated arbiter. For `divine_arbiter` mode: publishes `arbitration.case.divine_requested` and sets timeout. Validates arbiter caseload below `MaxActiveCasesPerArbiter`. Updates arbiter assignment on case. Publishes `arbitration.arbiter.assigned`.

- **RecuseArbiter** (`/arbitration/arbiter/recuse`): Removes arbiter from case with reasoning (conflict of interest, corruption detected via obligation violation). Triggers re-assignment. Publishes `arbitration.arbiter.recused`.

- **ListQualifiedArbiters** (`/arbitration/arbiter/list-qualified`): Returns cached list of qualified arbiters for a game service + case type. Cache rebuilt from faction governance data.

- **GetArbiterCaseload** (`/arbitration/arbiter/get-caseload`): Returns active case count and case references for an arbiter.

### Ruling Management (4 endpoints)

- **IssueRuling** (`/arbitration/ruling/issue`): Arbiter-only. Acquires ruling lock (prevents duplicate). Validates case is in ruling-eligible phase. Records ruling with consequence manifest. Executes consequences via downstream service APIs (relationship change, escrow creation, obligation creation, fine, exile, imprisonment). Advances contract to ruling-issued milestone. Publishes `arbitration.ruling.issued`.

- **AppealRuling** (`/arbitration/ruling/appeal`): Party-initiated within appeal window. Validates appeal window is open. Records appeal. May escalate to higher sovereign or divine arbiter. Resets case to evidence phase for the appellate proceeding.

- **GetRuling** (`/arbitration/ruling/get`): Load ruling by rulingId or caseId.

- **EnforceRuling** (`/arbitration/ruling/enforce`): Verifies all ruling consequences have been executed (checks downstream service state). Marks case as enforcement-confirmed. Closes case. Publishes `arbitration.ruling.enforced`.

### Jurisdiction Resolution (2 endpoints)

- **ResolveJurisdiction** (`/arbitration/jurisdiction/resolve`): Given a location and case type, returns the jurisdictional sovereign faction, authority level, procedural template reference, and governance parameters. Uses jurisdiction cache with TTL.

- **GetJurisdictionHierarchy** (`/arbitration/jurisdiction/get-hierarchy`): Given a location, returns the full sovereignty hierarchy: immediate controlling faction, delegated authorities, sovereign, realm baseline. Useful for understanding the legal landscape at a location.

### Cleanup Endpoints (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/arbitration/cleanup-by-character`): Closes or withdraws active cases where the character is a party. Archives evidence. Preserves rulings for historical record.
- **CleanupByRealm** (`/arbitration/cleanup-by-realm`): Closes all active cases within the realm. Archives case data.

---

## Visual Aid

### Case Lifecycle State Machine

```
+-----------------------------------------------------------------------+
|                    ARBITRATION CASE LIFECYCLE                           |
|                                                                        |
|   FILED                                                                |
|   ┌─────────────┐                                                     |
|   │ Jurisdiction │─── rejected ───► DISMISSED (no jurisdiction)        |
|   │ validated    │                                                     |
|   │ Template     │─── sovereign has no ──► DISMISSED (no template)     |
|   │ selected     │    governance data                                  |
|   │ Contract     │                                                     |
|   │ created      │─── accepted ───► SERVICE                            |
|   └─────────────┘                    │                                 |
|                                      ▼                                 |
|   SERVICE                  RESPONSE                                    |
|   ┌─────────────┐         ┌─────────────┐                             |
|   │ Respondent   │────────►│ Respondent   │                            |
|   │ notified     │ timeout │ responds or  │                            |
|   │ Deadline set │────┐    │ defaults     │                            |
|   └─────────────┘    │    └──────┬───────┘                             |
|                       │           │                                     |
|                       ▼           ▼                                     |
|              DEFAULT PROC    EVIDENCE                                   |
|              (petitioner     ┌─────────────┐                           |
|               favored)       │ Both parties │                           |
|                    │         │ submit       │                           |
|                    │         │ evidence     │                           |
|                    │         └──────┬───────┘                           |
|                    │                │                                    |
|                    │         HEARING (if required)                       |
|                    │         ┌─────────────┐                           |
|                    │         │ Arbiter      │                           |
|                    │         │ reviews      │                           |
|                    │         └──────┬───────┘                           |
|                    │                │                                    |
|                    ▼                ▼                                    |
|              ┌─────────────────────────┐                               |
|              │        RULING           │                                |
|              │  Arbiter issues ruling  │                                |
|              │  Consequences executed  │                                |
|              └───────────┬─────────────┘                               |
|                          │                                              |
|                   APPEAL WINDOW                                         |
|                   ┌──────┴──────┐                                      |
|                   │             │                                       |
|                appeal       no appeal                                   |
|                   │             │                                       |
|                   ▼             ▼                                       |
|              APPELLATE     ENFORCEMENT                                  |
|              (restart at   ┌─────────────┐                             |
|               evidence     │ Consequences │                            |
|               phase with   │ verified     │                            |
|               higher       │ Obligations  │                            |
|               authority)   │ activated    │                            |
|                            └──────┬───────┘                            |
|                                   │                                     |
|                                   ▼                                     |
|                              ┌─────────┐                               |
|                              │ CLOSED  │                                |
|                              └─────────┘                               |
|                                                                        |
|   TERMINAL STATES:                                                     |
|   - Closed (ruling enforced -- normal completion)                      |
|   - Dismissed (no jurisdiction, no template, arbiter dismissal)        |
|   - Withdrawn (petitioner withdraws)                                   |
+-----------------------------------------------------------------------+
```

### Jurisdiction Resolution Flow

```
+-----------------------------------------------------------------------+
|                    JURISDICTION RESOLUTION                              |
|                                                                        |
|   Location: "Docks District" + Case Type: "dissolution"               |
|        │                                                               |
|        ▼                                                               |
|   Check cache: arb:cache:juris:{locationId}:{caseType}                |
|        │                                                               |
|        ├── HIT ──► Return cached resolution                            |
|        │                                                               |
|        └── MISS ──► Resolve from Faction                               |
|                       │                                                |
|                       ▼                                                |
|               Query: "Who controls this location?"                     |
|               ┌─────────────────────────────────┐                     |
|               │ Harbor Authority (Delegated)     │                     |
|               │ Controls Docks District          │                     |
|               │ Has governance for: trade_dispute │                    |
|               │ NO governance for: dissolution   │                     |
|               └─────────────┬───────────────────┘                     |
|                             │                                          |
|                  No governance for this case type                       |
|                  Walk up to sovereign                                   |
|                             │                                          |
|                             ▼                                          |
|               ┌─────────────────────────────────┐                     |
|               │ Kingdom of Arcadia (Sovereign)   │                    |
|               │ Sovereign over this territory    │                     |
|               │ Has governance for: dissolution  │                     |
|               │ Template: "dissolution-standard" │                     |
|               │ Params: { waitingPeriod: 30,     │                    |
|               │           division: "equal" }    │                     |
|               └─────────────────────────────────┘                     |
|                             │                                          |
|                             ▼                                          |
|               Cache result, return to caller                           |
|                                                                        |
|   EDGE CASE: Enclave sovereignty                                       |
|   If "Docks District" contains "Dwarven Quarter" (nested location)    |
|   AND "Dwarven Enclave" (Sovereign) controls "Dwarven Quarter"        |
|   THEN cases filed AT "Dwarven Quarter" use enclave's governance      |
|   (enclave sovereignty overrides outer sovereign within its boundary)  |
+-----------------------------------------------------------------------+
```

### Integration Orchestration

Case identity and lifecycle are owned here. Jurisdiction determination uses Faction (sovereignty, territory control, authority level). Procedural workflow is Contract (the arbitration case IS a contract instance created from a procedural template). Asset division is Escrow (when rulings involve property). Ongoing obligations from rulings are Obligation (alimony, probation, reparations feed into GOAP action costs). Relationship status changes from rulings (married -> divorced, member -> exiled) use Relationship. Sovereignty disputes may involve Seed (capability-gated claims). Divine arbitration uses Puppetmaster (regional watcher gods as arbiters).

```
+-----------------------------------------------------------------------+
|                    ARBITRATION ORCHESTRATION                            |
|                                                                        |
|   lib-arbitration orchestrates:                                        |
|                                                                        |
|   ┌──────────┐  jurisdiction   ┌──────────┐                          |
|   │ Faction  │◄───────────────│Arbitration│                           |
|   │ (L4)     │  sovereignty    │ (L4)     │                           |
|   │          │  governance     │          │                           |
|   └──────────┘  authority      │  CASE    │                           |
|                                │  RECORD  │                           |
|   ┌──────────┐  procedural    │  ───────  │                           |
|   │ Contract │◄───────────────│ caseId   │                           |
|   │ (L1)     │  template      │ type     │                           |
|   │          │  milestones     │ parties  │                           |
|   └──────────┘  prebound APIs  │ arbiter  │                           |
|                                │ contract │                           |
|   ┌──────────┐  asset         │ status   │                           |
|   │ Escrow   │◄───────────────│ ruling   │                           |
|   │ (L4)     │  division      └────┬─────┘                           |
|   └──────────┘                     │                                   |
|                                    │ ruling consequences               |
|   ┌──────────┐  ongoing           │                                   |
|   │Obligation│◄────────────────────┤                                   |
|   │ (L4)     │  obligations        │                                   |
|   └──────────┘                     │                                   |
|                                    │                                   |
|   ┌──────────┐  status             │                                   |
|   │Relation- │◄────────────────────┤                                   |
|   │ship (L2) │  changes            │                                   |
|   └──────────┘                     │                                   |
|                                    │                                   |
|   ┌──────────┐  fines              │                                   |
|   │ Currency │◄────────────────────┤                                   |
|   │ (L2)     │  penalties          │                                   |
|   └──────────┘                     │                                   |
|                                    │                                   |
|   ┌──────────┐  divine             │                                   |
|   │Puppet-   │◄────────────────────┘                                   |
|   │master(L4)│  arbitration                                            |
|   └──────────┘                                                         |
+-----------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Faction Sovereignty (Prerequisite -- changes to lib-faction)
- Add `authorityLevel` field to `faction-api.yaml` (enum: Influence, Delegated, Sovereign)
- Extend `DesignateRealmBaseline` to set Sovereign automatically
- Add delegation endpoint for sovereign-to-child authority grants
- Add `QueryGovernanceData` endpoint (case type + location -> procedural template + params)
- Extend `QueryApplicableNorms` to return authority source
- Add procedural norm type to governance data model
- Gate governance data behind `governance.arbitrate.*` seed capability + Sovereign/Delegated authority

### Phase 1: Obligation Multi-Channel Costs (Prerequisite -- changes to lib-obligation)
- Tag obligation costs as `legal` vs. `social` vs. `personal` based on source authority level
- Separate entries per violation type per authority channel
- Legal violations trigger sovereign enforcement events
- Social violations trigger reputation/encounter events
- Backward-compatible: no sovereign = all costs are social/personal (existing behavior)

### Phase 2: Core Arbitration Infrastructure
- Create `arbitration-api.yaml` schema with all endpoints
- Create `arbitration-events.yaml` schema
- Create `arbitration-configuration.yaml` schema
- Generate service code
- Implement case management CRUD (file creates contract from procedural template)
- Implement jurisdiction resolution with caching
- Implement evidence management
- Implement deadline worker background service

### Phase 3: Arbiter System
- Implement arbiter selection modes (faction leader, appointed official, peer panel)
- Implement arbiter assignment with caseload tracking
- Implement arbiter recusal
- Implement divine arbiter request flow (event publication, timeout, fallback)

### Phase 4: Ruling & Consequence Execution
- Implement ruling issuance with consequence manifest
- Implement consequence execution across downstream services (relationship, escrow, obligation, currency, status, faction)
- Implement appeal flow (escalation to higher sovereign)
- Implement enforcement verification

### Phase 5: Dissolution Case Type
- Author `dissolution-standard` contract template
- Author `dissolution-religious-annulment` contract template
- Wire dissolution-specific consequences (relationship change, asset division, custody, ongoing obligations)
- Integration test: NPC-initiated dissolution through full lifecycle

### Phase 6: Additional Case Types
- Author `criminal-trial-standard` template
- Author `trade-dispute-fast` template
- Author `exile-punitive` template
- Author `sovereignty-recognition` template
- Author `custody-standard` template

---

## Potential Extensions

1. **Precedent system**: Arbitration rulings for the same case type accumulate as precedent. Subsequent rulings can reference prior decisions. NPC arbiters use precedent as evidence in their cognition pipeline, biasing toward consistency. Over time, a jurisdiction develops a body of "case law" that emerges from accumulated rulings -- content flywheel material.

2. **Jurisdiction shopping**: NPCs evaluate which jurisdiction would be most favorable and may relocate before filing. The `evaluate_consequences` cognition stage compares costs across sovereigns: "If I file in the Kingdom, I get equal split. If I move to the Enclave first, I get petitioner-favored." This creates emergent legal strategy without any scripted behavior.

3. **Class action cases**: Multiple petitioners vs. one respondent (or one entity). Requires case consolidation mechanics -- multiple filing cases merged into one proceeding. Template-driven: only available when governance parameters include `allowClassAction: true`.

4. **Corruption detection**: When an arbiter issues a ruling that contradicts the evidence weight (bribery, personal bias), the system publishes events that witnesses may perceive. Other NPCs can file appeals or corruption complaints. A sovereign faction that detects systematic corruption in its arbiters may replace them -- emergent institutional reform.

5. **Inter-sovereign disputes**: When a case involves parties from different sovereign territories, which sovereign has jurisdiction? Requires a meta-arbitration protocol -- possibly defaulted to the realm baseline sovereign, or requiring agreement between the two sovereigns. Could escalate to divine arbitration when mortal sovereigns disagree.

6. **Arbitration variable provider**: `${arbitration.*}` namespace for ABML behavior expressions. Variables like `${arbitration.active_cases}`, `${arbitration.pending_ruling}`, `${arbitration.legal_risk.<case_type>}` would let NPC cognition reason about pending legal proceedings.

7. **Witness subpoena system**: Arbiter can compel witnesses to provide testimony. Witness NPCs evaluate the cost of compliance vs. defiance (obligation costs from contempt vs. cost of testifying). Creates emergent witness dynamics -- some characters refuse subpoenas if the sovereign's authority is weak.

8. **Client events**: `arbitration-client-events.yaml` for pushing case status updates (filing notifications, ruling announcements, deadline warnings) to parties' WebSocket clients for real-time player-facing arbitration UX.

9. **Arbitration as content flywheel input**: Completed cases with rulings, evidence, and consequences become rich narrative material. Storyline can generate quests from notable rulings ("avenge the unjust exile"), region-specific legal drama, and political intrigue from corruption patterns.

10. **Organization integration**: When lib-organization exists, arbitration cases can involve organizational parties (businesses suing businesses, sovereign revoking a charter, succession disputes). Organizational asset identification for division becomes possible.

---

## Why Not lib-contract Directly?

Arbitration follows the same structural pattern as Quest and Escrow -- a game-flavored API layer over Contract's state machine. Quest translates "complete this objective" into contract milestones. Escrow translates "exchange these assets" into contract-guarded custody. Arbitration translates "resolve this dispute" into contract-tracked procedural steps (filing, evidence, hearing, ruling, enforcement). They are parallel orchestration layers composing the same underlying primitive (Contract), not the same service.

Arbitration does not adjudicate -- it orchestrates the adjudication process. The arbiter (an NPC, a faction leader, a divine actor) makes the actual ruling decision. Arbitration provides the procedural framework, tracks the case state, enforces deadlines, and executes the ruling's consequences via prebound API calls. This is the same "orchestration not intelligence" principle that governs Quest (quest doesn't decide when objectives are complete -- the world does) and Escrow (escrow doesn't decide if conditions are met -- the arbiter does).

Contract provides the state machine, milestone progression, consent flows, and prebound API execution that arbitration cases need. The question arises: why not just create arbitration contracts directly via lib-contract's existing API?

**The answer: arbitration requires jurisdiction, procedure, and enforcement -- Contract provides none of these.**

| Concern | lib-contract Provides | lib-arbitration Adds |
|---------|----------------------|---------------------|
| **State machine** | Draft -> Proposed -> Active -> Fulfilled | Case-specific states (Filed -> Hearing -> Ruling -> Enforcement -> Closed) mapped onto Contract milestones |
| **Parties** | Named party roles with consent | Jurisdiction-aware party roles (petitioner, respondent, arbiter, sovereign) with authority validation |
| **Milestones** | Sequential progression with deadlines | Procedural steps (filing, evidence submission, hearing, ruling, appeal window, enforcement) selected from sovereign's template |
| **Prebound APIs** | Callbacks on milestone transitions | Ruling enforcement (relationship status change, escrow release, obligation creation, exile) as prebound APIs |
| **Procedures** | None -- Contract is procedure-agnostic | Jurisdiction determination, procedural template selection from sovereign's governance data, deadline computation |
| **Authority** | None -- any entity can create a contract | Sovereign/Delegated authority validation, arbiter qualification, jurisdictional challenges |
| **Enforcement** | Breach/cure mechanics | Legal vs. social consequence distinction, sovereign enforcement weight, guard NPC activation |

Contract is the engine. Arbitration is the legal system built on that engine.

---

## Faction Sovereignty Dependency

Arbitration is meaningful only when factions distinguish between legal authority (Sovereign/Delegated) and social influence. Without sovereignty, there is no principled way to determine who has jurisdiction, whose procedures apply, or what weight a ruling carries. The `authorityLevel` field on FactionModel (described in [Faction deep dive Design Consideration #6](FACTION.md#design-considerations-requires-planning)) must exist before arbitration can function.

Arbitration depends on a capability that does not yet exist in lib-faction: the `authorityLevel` field distinguishing Sovereign, Delegated, and Influence factions. This section documents the dependency and what it enables.

### What Sovereignty Provides

| Authority Level | Arbitration Capability |
|----------------|----------------------|
| **Sovereign** | Full jurisdiction. Defines procedural templates for case types. Appoints arbiters. Rulings carry legal weight (guard enforcement, imprisonment, asset seizure). |
| **Delegated** | Scoped jurisdiction granted by a sovereign. Can arbitrate within delegated scope (e.g., temple district handles religious matters, merchant court handles trade disputes). Inherits sovereign's law for gaps. |
| **Influence** | No jurisdiction. Cannot arbitrate. Can file cases (as petitioner), submit evidence, express preferences. Faction norms remain social cost modifiers only. |

### Revised Norm Resolution for Arbitration

When a case is filed, jurisdiction determination walks the authority hierarchy:

1. **Find the sovereign** in the territory hierarchy (walk up faction territory claims and parent chains until Sovereign is found)
2. **Check for delegated authority** -- a Delegated faction in the specific location may have jurisdiction for this case type
3. **Sovereign's governance data** provides the procedural template for this case type
4. **If sovereign has no position** (no governance data for this case type) -- fall through to realm baseline sovereign
5. **Enclave sovereignty** -- a nested sovereign within the outer sovereign's territory overrides completely within its location boundary

### What Must Change in lib-faction

The following changes to lib-faction are prerequisites for lib-arbitration (documented in [Faction deep dive Design Consideration #6](FACTION.md#design-considerations-requires-planning)):

- **Schema change**: Add `authorityLevel` enum field (`Influence`, `Delegated`, `Sovereign`) to `faction-api.yaml`
- **Model change**: Add field to `FactionModel`
- **DesignateRealmBaseline enhancement**: Automatically sets `AuthorityLevel = Sovereign`
- **New API**: Delegation endpoint for sovereign factions to grant Delegated authority to child factions
- **QueryApplicableNorms enhancement**: Return authority source alongside norm data
- **New governance data model**: Procedural norm type alongside cost norms -- `{ caseType, templateCode, governanceParameters }`, gated by `governance.arbitrate.*` seed capability and Sovereign/Delegated authority level
- **New API**: `QueryGovernanceData` -- given a location and case type, resolve the jurisdictional faction and return its procedural template reference and governance parameters

### What Must Change in lib-obligation

- **Cost tagging**: Tag violation costs as `legal` vs. `social` vs. `personal` based on source faction's authority level
- **Multi-channel costs**: Separate entries per violation type per authority channel (see [Obligation deep dive Design Considerations](OBLIGATION.md#design-considerations-requires-planning))

---

## The Arbitration Case Lifecycle

An arbitration case progresses through a defined lifecycle, mapped onto Contract milestones:

```
CASE LIFECYCLE                           CONTRACT MILESTONE MAPPING
=================                        ==========================

Filed ──────────────────────────────────► Milestone 1: filing_accepted
  │  Petitioner submits case              (prebound: validate jurisdiction,
  │  Jurisdiction determined              create case record, notify respondent)
  │  Procedural template selected
  │
  ▼
Service ────────────────────────────────► Milestone 2: service_confirmed
  │  Respondent formally notified          (prebound: record service timestamp,
  │  Response deadline set                 start response timer)
  │
  ▼
Response ───────────────────────────────► Milestone 3: response_received
  │  Respondent accepts, contests,         (prebound: record response type,
  │  or defaults (deadline expires)        update case state)
  │
  ▼
Evidence ───────────────────────────────► Milestone 4: evidence_closed
  │  Both parties submit evidence          (prebound: close evidence window,
  │  Witnesses recorded                    notify arbiter)
  │  Evidence window closes
  │
  ▼
Hearing (optional) ─────────────────────► Milestone 5: hearing_completed
  │  Arbiter reviews evidence              (prebound: record hearing outcome,
  │  Parties may present arguments         advance to ruling)
  │  (NPC arbiter uses GOAP to decide)
  │
  ▼
Ruling ─────────────────────────────────► Milestone 6: ruling_issued
  │  Arbiter issues ruling                 (prebound: execute ruling consequences
  │  Consequences executed                 via downstream service APIs)
  │  Appeal window opens
  │
  ▼
Appeal Window ──────────────────────────► Milestone 7: appeal_window_closed
  │  Parties may appeal to higher          (prebound: finalize or escalate)
  │  sovereign (if exists)
  │
  ▼
Enforcement ────────────────────────────► Milestone 8: enforcement_confirmed
  │  Ruling consequences verified          (prebound: verify downstream state,
  │  Ongoing obligations activated         close case)
  │
  ▼
Closed ─────────────────────────────────► Contract Fulfilled
```

Not all milestones are required for every case type. The procedural template determines which milestones are active. A simple trade dispute might skip the hearing (arbiter rules on evidence alone). A criminal proceeding might have additional milestones (sentencing, probation review). The milestone set is template-driven, not hardcoded.

### Default Proceedings

If the respondent fails to respond within the deadline (Milestone 3), the case proceeds as a **default proceeding**:
- The arbiter rules based on the petitioner's evidence alone
- Default rulings typically favor the petitioner (sovereign's template configures default behavior)
- The respondent can petition to reopen within an appeal window (if the template includes one)

This creates emergent gameplay: NPCs who ignore legal proceedings face default judgments. A character who flees jurisdiction to avoid a divorce filing may return to find they've been divorced in absentia with unfavorable terms.

---

## Procedural Templates

Arbitration does not contain case-type-specific logic. All case behavior comes from procedural templates stored as governance data on sovereign/delegated factions, referencing contract templates in lib-contract.

### The Flow

```
1. Case filed at Location X
        │
        ▼
2. lib-arbitration queries lib-faction:
   "Who is sovereign at Location X?"
   "What governance data does the sovereign
    have for case type 'dissolution'?"
        │
        ▼
3. Faction returns:
   {
     caseType: "dissolution",
     templateCode: "dissolution-standard",
     governanceParameters: {
       waitingPeriodDays: 30,
       defaultAssetDivision: "equal",
       appealWindowDays: 14,
       requiresHearing: false,
       defaultOnNoResponse: "petitioner_favored"
     }
   }
        │
        ▼
4. lib-arbitration creates Contract instance
   from template "dissolution-standard"
   with governance parameters merged into
   template values (via Contract's
   SetTemplateValues API)
        │
        ▼
5. Contract's milestone state machine
   drives the case lifecycle
   (prebound APIs execute consequences)
```

### Governance Parameters

Governance parameters are template-specific key-value pairs that configure how the procedural template behaves for this jurisdiction. They are passed to Contract's `SetTemplateValues` API, which merges them into the template's configurable slots.

| Parameter | Type | Used By | Description |
|-----------|------|---------|-------------|
| `waitingPeriodDays` | int | dissolution | Mandatory waiting period before ruling can be issued |
| `defaultAssetDivision` | string | dissolution | Division rule for shared assets: `equal`, `petitioner_favored`, `respondent_favored`, `arbiter_discretion` |
| `appealWindowDays` | int | all | Days after ruling before it becomes final |
| `requiresHearing` | bool | all | Whether an in-person hearing milestone is included |
| `defaultOnNoResponse` | string | all | Behavior on respondent default: `petitioner_favored`, `dismiss`, `continue` |
| `maxEvidenceItems` | int | all | Evidence submission limit per party |
| `sentencingRequired` | bool | criminal | Whether a separate sentencing milestone follows the guilty ruling |
| `custodyEvaluationRequired` | bool | dissolution, custody | Whether a custody evaluation milestone is included |
| `exileTerritory` | string | exile | Territory from which the subject is exiled (location code) |

These parameters are opaque to lib-arbitration -- it passes them through to Contract. The contract template interprets them via its milestone configuration and prebound API parameters.

### Example Procedural Templates

Templates are authored at deployment time and registered in lib-contract. Different sovereigns reference different templates for the same case type.

| Template Code | Case Type | Milestones | Description |
|--------------|-----------|------------|-------------|
| `dissolution-standard` | `dissolution` | filing, service, response, evidence, ruling, appeal, enforcement | Standard dissolution with waiting period and equal asset split |
| `dissolution-religious-annulment` | `dissolution` | filing, service, response, evidence, hearing, ruling, enforcement | Religious proceeding requiring a hearing before a temple arbiter |
| `criminal-trial-standard` | `criminal_proceeding` | filing, service, response, evidence, hearing, ruling, sentencing, appeal, enforcement | Full criminal trial with hearing and sentencing phases |
| `trade-dispute-fast` | `trade_dispute` | filing, response, evidence, ruling, enforcement | Expedited merchant court proceeding (no hearing, no appeal) |
| `exile-punitive` | `exile` | filing, ruling, enforcement | Sovereign-initiated, no response period -- immediate ruling and exile |
| `sovereignty-recognition` | `sovereignty_recognition` | filing, evidence, hearing, ruling, appeal, enforcement | Petition for sovereign recognition adjudicated by current sovereign or divine arbiter |
| `custody-standard` | `custody_inheritance` | filing, service, response, evidence, custody_evaluation, hearing, ruling, appeal, enforcement | Custody dispute with mandatory evaluation milestone |

---

## Ruling Consequences

When the arbiter issues a ruling (Milestone 6 prebound API execution), lib-arbitration orchestrates consequences across multiple downstream services. The specific consequences are determined by the ruling type and encoded in the contract template's prebound API configuration.

### Consequence Types

| Consequence | Downstream Service | Mechanism |
|-------------|-------------------|-----------|
| **Relationship status change** | Relationship (L2) | API call to update relationship type (married -> divorced, member -> exiled) |
| **Asset division** | Escrow (L4, soft) | Create escrow with division terms from ruling, release to parties |
| **Ongoing obligations** | Obligation (L4, soft) | Create new contracts with behavioral clauses (alimony, probation, reparations) that feed into GOAP costs |
| **Custody assignment** | Character (L2) | Update character household assignment, modify guardian/dependent relationships |
| **Exile enforcement** | Faction (L4) | Remove membership, mark character as exiled from territory. Obligation adds legal cost for re-entry. |
| **Sovereignty transfer** | Faction (L4) | Update authority level, reassign territory control |
| **Fine/penalty** | Currency (L2) | Debit from respondent's wallet, credit to petitioner or sovereign treasury |
| **Imprisonment** | Status (L4, soft) | Apply imprisonment status effect (restricts movement, disables actions) via Status Inventory |
| **Norm violation publication** | Events | Publish violation events consumed by encounter/reputation systems for social consequences |
| **Divine attention** | Puppetmaster (L4, soft) | Publish events that regional watcher gods may notice (divine justice interest) |

### The Escrow Integration

Asset division in arbitration follows the same pattern as Escrow's existing arbiter-resolved disputes:

```
Ruling includes asset division terms
        │
        ▼
lib-arbitration creates Escrow instance
  - Type: two-party (petitioner, respondent)
  - Trust mode: arbiter (the case arbiter)
  - Assets: from shared organization assets
    (via lib-organization) or directly held
  - Division rule: from governance parameters
        │
        ▼
Escrow collects deposits
  (assets moved into custody)
        │
        ▼
Arbiter resolves Escrow per ruling terms
  (API call from Contract prebound execution)
        │
        ▼
Escrow releases to parties per division
```

When lib-organization exists, shared organizational assets (household property, business inventory, treasury) are identified and moved into escrow as part of the filing process. Without lib-organization, only directly-held assets (character wallets, personal inventory) can be divided.

---

## Arbiter Selection

The arbiter for a case is determined by the jurisdictional faction's governance data and the case type. lib-arbitration does not select arbiters -- it resolves the selection rule and applies it.

### Selection Modes

| Mode | Mechanism | When Used |
|------|-----------|-----------|
| **Faction leader** | The sovereign/delegated faction's leader character is the arbiter | Default for most case types. Simple governance. |
| **Appointed official** | The sovereign designates specific characters as arbiters for case types (stored in governance data) | Specialized courts: merchant guild's trade arbiter, temple's religious judge |
| **Divine arbiter** | A regional watcher god serves as arbiter (via Puppetmaster actor) | Sovereignty disputes, religious matters, cases where mortal authority is contested |
| **Peer panel** | Multiple faction members serve as a jury (majority rules) | Complex cases requiring community judgment. Template configures panel size. |

### NPC Arbiter Decision-Making

When the arbiter is an NPC (the common case), the ruling decision is made through the NPC's own cognition pipeline:

1. The arbiter's Actor receives the case evidence as perceptions
2. The `evaluate_consequences` stage considers the arbiter's own obligations (loyalty to sovereign, personal biases, bribery costs)
3. The GOAP planner weighs the options (rule for petitioner, rule for respondent, split ruling, dismiss case)
4. The arbiter's personality traits influence the ruling (a compassionate arbiter favors lenient sentences; a strict arbiter favors harsh penalties)

This means arbiter corruption is emergent. An arbiter with low honesty and a bribe offer faces a GOAP evaluation: the cost of corruption vs. the cost of ruling honestly. The morality pipeline makes judicial corruption a natural consequence of character traits and social pressure, not a scripted event.

Beyond arbiters, any NPC with the `evaluate_consequences` cognition stage can autonomously decide to initiate, contest, or cooperate with arbitration proceedings. An unhappy NPC in a bad marriage evaluates the cost of continuing vs. filing for dissolution vs. fleeing to a permissive jurisdiction. A merchant NPC evaluates whether to contest a trade dispute ruling or accept the loss. This is emergent narrative from the intersection of sovereignty + arbitration + cognition.

### Divine Arbiter Pattern

For cases involving sovereignty disputes or where mortal authority is contested, a regional watcher god can serve as arbiter:

```
Case filed with divine arbitration request
        │
        ▼
lib-arbitration publishes arbitration.case.divine_requested event
        │
        ▼
Regional watcher (via Puppetmaster) perceives the request
  - God's aesthetic preferences evaluate the case
  - Domain relevance: god of justice more likely to intervene
    than god of war (unless it's a combat dispute)
        │
        ▼
God accepts or ignores
  - Accept: god actor issues ruling via Arbitration API
    (ruling carries divine authority weight)
  - Ignore: case falls back to mortal arbiter selection
```

Divine arbitration is never guaranteed. Gods have their own priorities, attention budgets, and domain preferences. Requesting divine arbitration is a petition, not a command.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified. Plugin is pre-implementation.*

### Intentional Quirks (Documented Behavior)

1. **Case type is opaque string**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, violation type codes. New case types require only a contract template and a faction governance entry. lib-arbitration stores whatever case type string is provided.

2. **Governance parameters are opaque key-value pairs**: lib-arbitration does not interpret governance parameters. It passes them through to Contract's `SetTemplateValues`. The contract template is responsible for interpreting them. This means lib-arbitration doesn't need code changes for new parameter types.

3. **Arbitration does not adjudicate**: The service tracks the process, not the decision. The arbiter (an NPC, a faction leader, a god actor) makes the ruling through their own cognition pipeline or via API call. lib-arbitration accepts the ruling and executes its consequences.

4. **Default proceedings favor the petitioner**: When the respondent fails to respond, the default behavior is configurable via governance parameters. The default default (meta-default) is `petitioner_favored`, which matches real-world legal systems where failure to appear typically results in a default judgment.

5. **Appeal resets to evidence phase**: An appeal is not a separate case -- it restarts the same case at the evidence phase with a higher authority arbiter. This simplifies the state machine (no separate appeal lifecycle) while allowing full re-adjudication.

6. **Jurisdiction is location-based, not party-based**: Jurisdiction is determined by where the case is filed (the location), not by the parties' faction memberships. A character filing in the Temple District gets the Temple's delegated authority regardless of their own guild memberships. This matches real-world jurisdictional principles.

7. **Case codes are human-readable**: Every case gets a sequential code (`ARB-DISS-00142`) within its game service scope, in addition to the GUID. This is for NPC dialogue, encounter memories, and player-facing UX. The code prefix is configurable.

8. **Ruling consequences are best-effort**: Consequences are executed via downstream service API calls. If a downstream service is unavailable (soft dependency), that consequence is skipped and logged. The ruling itself is still valid -- enforcement verification will detect the gap and retry. This follows the orchestration pattern where the orchestrator doesn't guarantee atomicity.

### Design Considerations (Requires Planning)

1. **Faction sovereignty implementation**: The `authorityLevel` field, governance data model, delegation endpoint, and enhanced `QueryApplicableNorms` are all prerequisites that must be designed and implemented in lib-faction before lib-arbitration can be built. This is the single largest prerequisite.

2. **Contract template authoring**: Procedural templates (dissolution-standard, criminal-trial-standard, etc.) must be authored and registered in lib-contract at deployment time. Template design is a game design task, not a service engineering task -- but the template structure must support the milestone patterns described in this document.

3. **NPC arbiter cognition**: NPC arbiters need behavior documents that support case evaluation -- reviewing evidence, weighing arguments, considering precedent. This is an ABML behavior authoring task. The arbiter actor receives case perceptions and must produce a ruling action.

4. **Divine arbiter coordination**: The flow where a divine actor accepts or ignores an arbitration request requires Puppetmaster integration. The regional watcher actor must have a perception handler for `arbitration.case.divine_requested` events and an action handler for issuing rulings via the Arbitration API.

5. **Deadline enforcement model**: The deadline worker checks for expired milestones periodically. This means deadlines are enforced with up to `DefaultDeadlineCheckIntervalMinutes` latency. For time-sensitive proceedings (criminal cases, emergency exile), the worker may need shorter intervals. This is configurable per deployment but not per case.

6. **Evidence model design**: Evidence is currently described as "structured data -- not free text." The exact structure needs design. Options: typed evidence models per case type (most structured, most rigid), generic key-value evidence (most flexible, least structured), or a hybrid where evidence has a type tag and a payload schema resolved from the type.

7. **Household split integration**: The dissolution case type's consequence includes household fragmentation, which is a cross-cutting concern involving Organization (if it exists), Relationship, Character, and Seed. The boundary between "arbitration ruling consequence" and "household split mechanic" needs clear delineation. Arbitration issues the ruling; the household split is executed by the downstream services in response.

8. **Multi-game template portability**: Different games within the same Bannou deployment may have radically different legal systems. The governance data + contract template architecture handles this (each sovereign faction references its own templates), but template portability and sharing across game services needs design.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. Prerequisites (Faction sovereignty, Obligation multi-channel costs) must be completed first. See [Faction deep dive Design Consideration #6](FACTION.md#design-considerations-requires-planning) and [Obligation deep dive Design Considerations](OBLIGATION.md#design-considerations-requires-planning) for the dependency chain.*
