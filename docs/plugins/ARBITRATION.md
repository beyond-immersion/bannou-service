# Arbitration Plugin Deep Dive

> **Plugin**: lib-arbitration (not yet created)
> **Schema**: `schemas/arbitration-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: arbitration-cases (MySQL), arbitration-rulings (MySQL), arbitration-evidence (MySQL), arbitration-arbiters (Redis), arbitration-cache (Redis), arbitration-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: *(Originally referenced MORALITY-SYSTEM-NEXT-STEPS.md; superseded by this deep dive and #410)*
> **Implementation Map**: [docs/maps/ARBITRATION.md](../maps/ARBITRATION.md)
> **Short**: Dispute resolution orchestration composing Contract/Faction primitives for jurisdictional rulings

## Overview

Authoritative dispute resolution service (L4 GameFeatures) for competing claims that need jurisdictional ruling and enforcement. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver adjudication game mechanics. Game-agnostic: procedural templates, arbiter selection rules, and cultural attitudes toward litigation are configured through contract templates and faction governance data at deployment time. Internal-only, never internet-facing.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Arbitration is a new L4 service with no current consumers. Future dependents: lib-organization (charter disputes, succession contests), lib-faction (sovereignty disputes consume arbitration cases), Puppetmaster (divine actors interact with arbitration as arbiters), Gardener (player-facing arbitration UX -- presenting court proceedings as interactive gameplay), Storyline (arbitration rulings and disputes as narrative material for the content flywheel) |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `caseType` | B (Content Code) | Opaque string | Game-configurable dispute types (dissolution, trade_dispute, succession_contest, criminal, property, etc.). Procedural templates are defined per case type by sovereign governance data. Extensible without schema changes. |
| `entityType` (on petitioner/respondent) | A (Entity Reference) | `EntityType` enum (`$ref: common-api.yaml`) | Identifies the type of party in a dispute (Character, Organization, Faction). All valid values are first-class Bannou entities. |
| `rulingType` | C (System State) | Service-specific enum | Finite set of ruling outcomes (PetitionerFavored, RespondentFavored, Split, Dismissed). System-owned; determines consequence execution logic. |
| `evidenceType` | C (System State) | Service-specific enum | Finite set of evidence categories (Testimony, Document, Witness, Physical). System-owned; affects evidence relevance scoring. |
| `status` (on case) | C (System State) | Service-specific enum | Finite set of case lifecycle states (Filed, AwaitingArbiter, Hearing, Deliberation, Ruled, Appealed, Closed). System-owned state machine managed via contract milestones. |
| `arbiterSelectionMode` | C (System State) | Service-specific enum | Finite set of arbiter selection strategies (FactionLeader, AppointedOfficial, ExternalRequest, PeerPanel). System-owned; determines how arbiters are assigned from governance data. |

---

## Visual Aid

### Case Lifecycle State Machine

```
+-----------------------------------------------------------------------+
| ARBITRATION CASE LIFECYCLE |
| |
| FILED |
| ┌─────────────┐ |
| │ Jurisdiction │─── rejected ───► DISMISSED (no jurisdiction) |
| │ validated │ |
| │ Template │─── sovereign has no ──► DISMISSED (no template) |
| │ selected │ governance data |
| │ Contract │ |
| │ created │─── accepted ───► SERVICE |
| └─────────────┘ │ |
| ▼ |
| SERVICE RESPONSE |
| ┌─────────────┐ ┌─────────────┐ |
| │ Respondent │────────►│ Respondent │ |
| │ notified │ timeout │ responds or │ |
| │ Deadline set │────┐ │ defaults │ |
| └─────────────┘ │ └──────┬───────┘ |
| │ │ |
| ▼ ▼ |
| DEFAULT PROC EVIDENCE |
| (petitioner ┌─────────────┐ |
| favored) │ Both parties │ |
| │ │ submit │ |
| │ │ evidence │ |
| │ └──────┬───────┘ |
| │ │ |
| │ HEARING (if required) |
| │ ┌─────────────┐ |
| │ │ Arbiter │ |
| │ │ reviews │ |
| │ └──────┬───────┘ |
| │ │ |
| ▼ ▼ |
| ┌─────────────────────────┐ |
| │ RULING │ |
| │ Arbiter issues ruling │ |
| │ Consequences executed │ |
| └───────────┬─────────────┘ |
| │ |
| APPEAL WINDOW |
| ┌──────┴──────┐ |
| │ │ |
| appeal no appeal |
| │ │ |
| ▼ ▼ |
| APPELLATE ENFORCEMENT |
| (restart at ┌─────────────┐ |
| evidence │ Consequences │ |
| phase with │ verified │ |
| higher │ Obligations │ |
| authority) │ activated │ |
| └──────┬───────┘ |
| │ |
| ▼ |
| ┌─────────┐ |
| │ CLOSED │ |
| └─────────┘ |
| |
| TERMINAL STATES: |
| - Closed (ruling enforced -- normal completion) |
| - Dismissed (no jurisdiction, no template, arbiter dismissal) |
| - Withdrawn (petitioner withdraws) |
+-----------------------------------------------------------------------+
```

### Jurisdiction Resolution Flow

```
+-----------------------------------------------------------------------+
| JURISDICTION RESOLUTION |
| |
| Location: "Docks District" + Case Type: "dissolution" |
| │ |
| ▼ |
| Check cache: arb:cache:juris:{locationId}:{caseType} |
| │ |
| ├── HIT ──► Return cached resolution |
| │ |
| └── MISS ──► Resolve from Faction |
| │ |
| ▼ |
| Query: "Who controls this location?" |
| ┌─────────────────────────────────┐ |
| │ Harbor Authority (Delegated) │ |
| │ Controls Docks District │ |
| │ Has governance for: trade_dispute │ |
| │ NO governance for: dissolution │ |
| └─────────────┬───────────────────┘ |
| │ |
| No governance for this case type |
| Walk up to sovereign |
| │ |
| ▼ |
| ┌─────────────────────────────────┐ |
| │ Kingdom of Arcadia (Sovereign) │ |
| │ Sovereign over this territory │ |
| │ Has governance for: dissolution │ |
| │ Template: "dissolution-standard" │ |
| │ Params: { waitingPeriod: 30, │ |
| │ division: "equal" } │ |
| └─────────────────────────────────┘ |
| │ |
| ▼ |
| Cache result, return to caller |
| |
| EDGE CASE: Enclave sovereignty |
| If "Docks District" contains "Dwarven Quarter" (nested location) |
| AND "Dwarven Enclave" (Sovereign) controls "Dwarven Quarter" |
| THEN cases filed AT "Dwarven Quarter" use enclave's governance |
| (enclave sovereignty overrides outer sovereign within its boundary) |
+-----------------------------------------------------------------------+
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Faction Sovereignty (Prerequisite -- changes to lib-faction)
**Status**: Schema designed (#601). Implementation pending.

Design decisions (resolved 2026-03-16):
- `AuthorityLevel` enum (`Influence`, `Delegated`, `Sovereign`) on `FactionResponse` — required, defaults to `Influence`
- Sovereignty is emergent (nullable concept): acquired via `DesignateRealmBaseline` (→ Sovereign) or delegation (→ Delegated); `authorityLevel` is NOT on `UpdateFactionRequest`
- Delegation is per-case-type via opaque `domain` string (game-configurable)
- `QueryGovernanceData` (`/faction/governance/query`) is a new endpoint, separate from `QueryApplicableNorms` — returns 404 when no sovereign has jurisdiction
- Governance data model: `{ domain, templateCode, governanceParameters }` — parameters are opaque pass-through (T29 compliant)
- 6 new governance endpoints: set, remove, list, query, delegate, revoke
- `ApplicableNormResponse` gains `authorityLevel` field for authority-aware cost tagging
- Enclave sovereignty is location-boundary-based

Schema changes:
- `faction-api.yaml`: `AuthorityLevel` enum, governance models, 6 new endpoints, enhanced `FactionResponse` and `ApplicableNormResponse`
- `faction-service-events.yaml`: `authorityLevel` in x-lifecycle, 4 new governance events (`faction.governance.defined`, `faction.governance.deleted`, `faction.authority.delegated`, `faction.authority.revoked`)
- `faction-configuration.yaml`: `MaxGovernanceEntriesPerFaction`, `GovernanceCacheTtlSeconds`
- `state-stores.yaml`: `faction-governance-statestore` (MySQL)

### Phase 1: Obligation Multi-Channel Costs (Prerequisite -- changes to lib-obligation)
- Tag obligation costs as `legal` vs. `social` vs. `personal` based on source authority level
- Separate entries per violation type per authority channel
- Legal violations trigger sovereign enforcement events
- Social violations trigger reputation/encounter events
- Backward-compatible: no sovereign = all costs are social/personal (existing behavior)

### Phase 2: Core Arbitration Infrastructure
- Create `arbitration-api.yaml` schema with all endpoints
- Create `arbitration-service-events.yaml` schema
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
- Implement external arbiter request flow (generic event publication, timeout, fallback)

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

Arbitration is meaningful only when factions distinguish between legal authority (Sovereign/Delegated) and social influence. Without sovereignty, there is no principled way to determine who has jurisdiction, whose procedures apply, or what weight a ruling carries.

**Status (2026-03-16)**: The sovereignty schema is designed (#601) — `AuthorityLevel` enum, governance data model, 6 governance endpoints, and enhanced `ApplicableNormResponse` with authority level. Schema changes to `faction-api.yaml` and `faction-service-events.yaml` are planned. Service code implementation is pending. See Phase 0 above for the full design summary.

This section documents the dependency and what it enables.

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

**Status (2026-03-16)**: Schema designed (#601), implementation pending. See Phase 0 above for the resolved design.

The following changes to lib-faction are prerequisites for lib-arbitration (documented in [Faction deep dive Design Consideration #6](FACTION.md#design-considerations-requires-planning)):

- **Schema change**: `AuthorityLevel` enum (`Influence`, `Delegated`, `Sovereign`) added to `faction-api.yaml` — required on `FactionResponse`, defaults to `Influence`
- **Model change**: `authorityLevel` field on `FactionModel`; NOT on `UpdateFactionRequest` — sovereignty only mutated via `DesignateRealmBaseline` (→ Sovereign) or delegation endpoints (→ Delegated / → Influence)
- **DesignateRealmBaseline enhancement**: Automatically sets `authorityLevel = Sovereign`
- **New governance endpoints** (6 total): `/faction/governance/set`, `remove`, `list`, `query`, `delegate`, `revoke`
- **Delegation**: Per-case-type via opaque `domain` string — sovereign can delegate specific case type jurisdictions to child factions
- **QueryApplicableNorms enhancement**: `ApplicableNormResponse` gains `authorityLevel` field for authority-aware cost tagging
- **New governance data model**: `{ domain, templateCode, governanceParameters }` — `governanceParameters` is opaque pass-through (T29 compliant)
- **QueryGovernanceData** (`/faction/governance/query`): Given location + domain, resolves jurisdictional faction by walking sovereignty hierarchy. Returns 404 when no sovereign has jurisdiction. This is the critical integration endpoint for lib-arbitration case filing.

### What Must Change in lib-obligation

- **Cost tagging**: Tag violation costs as `legal` vs. `social` vs. `personal` based on source faction's authority level
- **Multi-channel costs**: Separate entries per violation type per authority channel (see [Obligation deep dive Design Considerations](OBLIGATION.md#design-considerations-requires-planning))

---

## The Arbitration Case Lifecycle

An arbitration case progresses through a defined lifecycle, mapped onto Contract milestones:

```
CASE LIFECYCLE CONTRACT MILESTONE MAPPING
================= ==========================

Filed ──────────────────────────────────► Milestone 1: filing_accepted
 │ Petitioner submits case (prebound: validate jurisdiction,
 │ Jurisdiction determined create case record, notify respondent)
 │ Procedural template selected
 │
 ▼
Service ────────────────────────────────► Milestone 2: service_confirmed
 │ Respondent formally notified (prebound: record service timestamp,
 │ Response deadline set start response timer)
 │
 ▼
Response ───────────────────────────────► Milestone 3: response_received
 │ Respondent accepts, contests, (prebound: record response type,
 │ or defaults (deadline expires) update case state)
 │
 ▼
Evidence ───────────────────────────────► Milestone 4: evidence_closed
 │ Both parties submit evidence (prebound: close evidence window,
 │ Witnesses recorded notify arbiter)
 │ Evidence window closes
 │
 ▼
Hearing (optional) ─────────────────────► Milestone 5: hearing_completed
 │ Arbiter reviews evidence (prebound: record hearing outcome,
 │ Parties may present arguments advance to ruling)
 │ (NPC arbiter uses GOAP to decide)
 │
 ▼
Ruling ─────────────────────────────────► Milestone 6: ruling_issued
 │ Arbiter issues ruling (prebound: execute ruling consequences
 │ Consequences executed via downstream service APIs)
 │ Appeal window opens
 │
 ▼
Appeal Window ──────────────────────────► Milestone 7: appeal_window_closed
 │ Parties may appeal to higher (prebound: finalize or escalate)
 │ sovereign (if exists)
 │
 ▼
Enforcement ────────────────────────────► Milestone 8: enforcement_confirmed
 │ Ruling consequences verified (prebound: verify downstream state,
 │ Ongoing obligations activated close case)
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
 defaultOnNoResponse: "PetitionerFavored"
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

Governance parameters are an opaque blob owned by Faction's governance data and interpreted solely by Contract templates. **lib-arbitration does not read, validate, or interpret governance parameters** — it receives the governance data from Faction and passes it unchanged to Contract's `SetTemplateValues` API. This is a genuine opaque pass-through, not a cross-service data contract (per FOUNDATION TENETS, No Metadata Bag Contracts).

The parameter names and types below are **documentation for template authors** — they describe what game-specific contract templates expect to find in the governance data configured on sovereign factions at deployment time. They are NOT fields in Arbitration's schema:

| Parameter | Type | Used By | Description |
|-----------|------|---------|-------------|
| `waitingPeriodDays` | int | dissolution | Mandatory waiting period before ruling can be issued |
| `defaultAssetDivision` | string | dissolution | Division rule for shared assets (e.g., `Equal`, `PetitionerFavored`) |
| `appealWindowDays` | int | all | Days after ruling before it becomes final |
| `requiresHearing` | bool | all | Whether an in-person hearing milestone is included |
| `defaultOnNoResponse` | string | all | Behavior on respondent default (e.g., `PetitionerFavored`, `Dismiss`) |
| `maxEvidenceItems` | int | all | Evidence submission limit per party |
| `sentencingRequired` | bool | criminal | Whether a separate sentencing milestone follows the guilty ruling |
| `custodyEvaluationRequired` | bool | dissolution, custody | Whether a custody evaluation milestone is included |
| `exileTerritory` | string | exile | Territory from which the subject is exiled (location code) |

**Schema implication**: The governance data field on Arbitration's case model (if stored for reference) is `object?` — client-only metadata that the service stores and returns unchanged. Arbitration never inspects its structure.

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
| **External request** | An external actor (divine, cross-jurisdiction authority, player-controlled) is requested as arbiter via generic `arbitration.arbiter.requested` event. Timeout + fallback to mortal arbiter if no response. | Sovereignty disputes, religious matters, cases where mortal authority is contested, or any scenario requiring an arbiter outside the faction's governance hierarchy |
| **Peer panel** | Multiple faction members serve as a jury (majority rules) | Complex cases requiring community judgment. Template configures panel size. |

### NPC Arbiter Decision-Making

When the arbiter is an NPC (the common case), the ruling decision is made through the NPC's own cognition pipeline:

1. The arbiter's Actor receives the case evidence as perceptions
2. The `evaluate_consequences` stage considers the arbiter's own obligations (loyalty to sovereign, personal biases, bribery costs)
3. The GOAP planner weighs the options (rule for petitioner, rule for respondent, split ruling, dismiss case)
4. The arbiter's personality traits influence the ruling (a compassionate arbiter favors lenient sentences; a strict arbiter favors harsh penalties)

This means arbiter corruption is emergent. An arbiter with low honesty and a bribe offer faces a GOAP evaluation: the cost of corruption vs. the cost of ruling honestly. The morality pipeline makes judicial corruption a natural consequence of character traits and social pressure, not a scripted event.

Beyond arbiters, any NPC with the `evaluate_consequences` cognition stage can autonomously decide to initiate, contest, or cooperate with arbitration proceedings. An unhappy NPC in a bad marriage evaluates the cost of continuing vs. filing for dissolution vs. fleeing to a permissive jurisdiction. A merchant NPC evaluates whether to contest a trade dispute ruling or accept the loss. This is emergent narrative from the intersection of sovereignty + arbitration + cognition.

### External Arbiter Request Pattern

For cases requiring an arbiter outside the faction's governance hierarchy — sovereignty disputes, religious matters, cross-jurisdiction authority, or player-controlled arbitration — the generic external request flow applies:

```
Case filed with ExternalRequest selection mode
 │
 ▼
lib-arbitration publishes arbitration.arbiter.requested { requesterType, caseId, timeoutAt }
 │
 ▼
External actor perceives the request
 - Divine actors (via Puppetmaster): god's aesthetic preferences evaluate the case
 - Cross-jurisdiction authorities: evaluate whether to accept jurisdiction
 - Player-controlled arbiters: receive notification via client events (future)
 │
 ▼
Actor accepts or ignores
 - Accept: actor calls AssignArbiter with explicit arbiter ID
   (ruling carries the requester's authority weight)
 - Ignore/timeout: deadline worker falls back to FactionLeader selection mode
```

External arbiter assignment is never guaranteed. The request is a petition, not a command. The `requesterType` field (opaque string, e.g., `"divine"`, `"cross-jurisdiction"`, `"player"`) identifies the category of external arbiter being requested — consumers filter on this to decide whether the request is relevant to them.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**CASCADE cleanup behavior contradicts CASCADE policy**~~: **FIXED** (2026-03-16) — Policy changed to `onDelete: detach` for all four resource targets (character, realm, faction, location). Arbitration data (rulings, evidence, terminal cases) is preserved through resource cleanup with nullified references. Active cases are closed; terminal cases and rulings survive with the deleted entity's reference set to null. Resource IDs on case/ruling/evidence models are nullable to support detachment.

### Intentional Quirks (Documented Behavior)

1. **Case type is opaque string**: Not an enum. Follows the same extensibility pattern as seed type codes, collection type codes, violation type codes. New case types require only a contract template and a faction governance entry. lib-arbitration stores whatever case type string is provided.

2. **Governance parameters are genuinely opaque pass-through**: lib-arbitration receives governance data from Faction and passes it unchanged to Contract's `SetTemplateValues`. It never reads, validates, or branches on specific parameter keys. The contract template is solely responsible for interpreting them. This means lib-arbitration doesn't need code changes for new parameter types, and the governance data field is `object?` in the schema (per FOUNDATION TENETS, No Metadata Bag Contracts — this is legitimate opaque pass-through, not a cross-service data contract).

3. **Arbitration does not adjudicate**: The service tracks the process, not the decision. The arbiter (an NPC, a faction leader, a god actor) makes the ruling through their own cognition pipeline or via API call. lib-arbitration accepts the ruling and executes its consequences.

4. **Default proceedings favor the petitioner**: When the respondent fails to respond, the default behavior is configurable via governance parameters. The default default (meta-default) is `PetitionerFavored`, which matches real-world legal systems where failure to appear typically results in a default judgment.

5. **Appeal resets to evidence phase**: An appeal is not a separate case -- it restarts the same case at the evidence phase with a higher authority arbiter. This simplifies the state machine (no separate appeal lifecycle) while allowing full re-adjudication.

6. **Jurisdiction is location-based, not party-based**: Jurisdiction is determined by where the case is filed (the location), not by the parties' faction memberships. A character filing in the Temple District gets the Temple's delegated authority regardless of their own guild memberships. This matches real-world jurisdictional principles.

7. **Case codes are human-readable**: Every case gets a sequential code (`ARB-DISS-00142`) within its game service scope, in addition to the GUID. This is for NPC dialogue, encounter memories, and player-facing UX. The code prefix is configurable.

8. **Ruling consequences are best-effort with self-healing retry**: Consequences are executed via downstream service API calls. If a downstream service is unavailable (soft dependency), that consequence is skipped and logged with status `Failed` in the ruling's `consequenceResults`. The ruling itself is still valid. The self-healing mechanism: the contract's enforcement milestone has a deadline; when it expires without EnforceRuling closing the case, `HandleMilestoneFailedAsync` re-executes only the Failed consequences. This repeats until all succeed or the contract template's milestone structure is exhausted. Admin and divine actors can also call EnforceRuling manually at any time. Per Implementation Tenets — Multi-Service Call Compensation (Strategy 2: documented self-healing).

### Design Considerations (Requires Planning)

1. ~~**Faction sovereignty implementation**~~: **FIXED** (2026-03-16) — Faction sovereignty fully implemented (#601). `AuthorityLevel` enum, governance data model, 6 governance endpoints, 4 governance events, and enhanced `ApplicableNormResponse` all in place. Arbitration's Faction API dependency is now available.

2. **Contract template authoring**: Procedural templates (dissolution-standard, criminal-trial-standard, etc.) must be authored and registered in lib-contract at deployment time. Template design is a game design task, not a service engineering task -- but the template structure must support the milestone patterns described in this document.

3. **NPC arbiter cognition**: NPC arbiters need behavior documents that support case evaluation -- reviewing evidence, weighing arguments, considering precedent. This is an ABML behavior authoring task. The arbiter actor receives case perceptions and must produce a ruling action.

4. **External arbiter consumer integration**: The generic `arbitration.arbiter.requested` event needs at least one consumer. The primary consumer is Puppetmaster (divine actors filtering on `requesterType: "divine"`), which requires a perception handler for the event and an action handler for calling AssignArbiter with the god's character ID. Other consumers (cross-jurisdiction authorities, player-controlled arbiters) are future extensions that consume the same event with different `requesterType` filters.

5. ~~**Household split integration**~~: **FIXED** (2026-03-16) — The household split is a dissolution case type orchestrated through Arbitration's existing procedural template system. Boundary resolved: Arbitration calls downstream service APIs as consequences; Organization.Dissolve handles structural dissolution (member redistribution, asset identification for Escrow); Seed.TransferGrowth handles proportional growth splitting. Two new consequence types added to IssueRuling dispatch: `OrganizationDissolution` and `SeedGrowthTransfer`. Remaining implementation work tracked on the respective plugins: Organization (Dissolve endpoint, #436), Seed (TransferGrowth API, #436), Contract (Organization as party entity type, #436).

6. ~~**Ruling consequence retry mechanism**~~: **FIXED** (2026-03-16) — Self-healing mechanism designed using existing infrastructure: EnforceRuling is idempotent and re-executes only Failed consequences (skipping Succeeded). Contract's enforcement milestone deadline expiry triggers `contract.milestone.failed`, which `HandleMilestoneFailedAsync` receives and re-invokes consequence execution. No new background worker needed. Consequence results tracked per-item on the ruling record (`consequenceResults` list with status/attemptCount/lastError). Retry bounded by contract template milestone structure. Case stays in `Ruled` until all consequences succeed; admin/divine actors can also call EnforceRuling manually.

---

## Schema Creation Guidance (L4 Audit Notes, 2026-03-05)

When creating `arbitration-api.yaml`, `arbitration-service-events.yaml`, and `arbitration-configuration.yaml`, follow these requirements identified during the L4 audit:

### API Schema (`arbitration-api.yaml`)
- `x-service-layer: GameFeatures` at root level
- `servers: [{ url: http://localhost:5012 }]`
- Schema Modification Gate in `info.description`
- `x-permissions: []` on all endpoints (service-to-service only — pure orchestration service)
- `x-references` block for all 4 resource cleanup targets (character, realm, faction, location) with `sourceType: arbitration` and `onDelete: detach`
- Consider `x-compression-callback` for character archival (case participation history)
- All service-specific enums (`ArbitrationRulingType`, `ArbitrationEvidenceType`, `ArbitrationCaseStatus`, `ArbiterSelectionMode` with values `FactionLeader`, `AppointedOfficial`, `ExternalRequest`, `PeerPanel`) defined here, referenced via `$ref` from events
- `entityType` on petitioner/respondent uses `$ref: 'common-api.yaml#/components/schemas/EntityType'`
- Ruling `reasoning` field: `nullable: true` (not `required: false` with non-nullable type)
- Evidence `content` field: must be a typed model (per evidence type or discriminated union), NOT `additionalProperties: true`
- Governance data on case model: `object?` (genuine opaque pass-through)

### Events Schema (`arbitration-service-events.yaml`)
- `x-lifecycle` for `ArbitrationCase` entity with `topic_prefix: arbitration` (generates created/updated/deleted lifecycle events with Pattern C topics: `arbitration.case.created`, etc. — without `topic_prefix`, the entity `ArbitrationCase` would produce forbidden Pattern B topics like `arbitration-case.created`)
- `x-event-subscriptions` for all 6 consumed events (contract milestones, faction territory)
- `x-event-publications` for all domain-specific events
- All domain-specific events use `allOf` with `BaseServiceEvent` (per FOUNDATION TENETS — both Quest and Divine follow this pattern)
- All event topic strings use kebab-case (no underscores)

### Configuration Schema (`arbitration-configuration.yaml`)
- `x-service-configuration` with `envPrefix: ARBITRATION`
- All integer properties need `minimum: 1` validation bounds
- `QueryPageSize` needs `maximum` cap (e.g., 100)
- `CaseCodePrefix` needs `minLength: 1`, `maxLength: 10`

### Implementation Notes
- Helper service decomposition recommended for the 14 constructor dependencies: consider `JurisdictionResolver` (Faction + Location + cache), `ConsequenceExecutor` (Relationship + Currency + Escrow + Obligation + Status), reducing constructor to ~8 parameters
- `IStateStoreFactory` is constructor parameter only — resolve 6 stores in constructor, store as `readonly` fields, do not store factory as field
- `IFactionClient` resolved via `IServiceProvider` (soft L4 dependency), not constructor injection

---

## Work Tracking

Plugin is in pre-implementation phase. Prerequisites must be completed before schema creation can begin. L4-audited (2026-03-05), documentation audited (2026-03-08, 2026-03-11), maintained (2026-03-15).

### Prerequisites (blocking)

| Prerequisite | Blocker For | Reference |
|-------------|-------------|-----------|
| **Faction sovereignty** (`authorityLevel` field, governance data model, delegation, `QueryGovernanceData`) | All of Arbitration (no jurisdiction without sovereignty) | [#601](https://github.com/beyond-immersion/bannou-service/issues/601), [Faction deep dive DC #6](FACTION.md#design-considerations-requires-planning) |
| **Obligation multi-channel costs** (legal/social/personal authority tagging) | Phase 1, ruling consequence distinction | [#605](https://github.com/beyond-immersion/bannou-service/issues/605), [Obligation deep dive DCs](OBLIGATION.md#design-considerations-requires-planning) |

### Related Open Issues

| Issue | Relevance |
|-------|-----------|
| [#601](https://github.com/beyond-immersion/bannou-service/issues/601) | **P0 BLOCKER** — Faction sovereignty prerequisite (`authorityLevel`, governance data, delegation, `QueryGovernanceData`) |
| [#605](https://github.com/beyond-immersion/bannou-service/issues/605) | **P1 BLOCKER** — Obligation multi-channel costs prerequisite (legal/social/personal authority tagging) |
| [#435](https://github.com/beyond-immersion/bannou-service/issues/435) | Sovereignty transfer consequences — affects active case continuity during jurisdiction changes |
| [#436](https://github.com/beyond-immersion/bannou-service/issues/436) | Household split mechanic — blocks dissolution case type (Phase 5), cross-references DC #6 |
| [#410](https://github.com/beyond-immersion/bannou-service/issues/410) | Second Thoughts / Obligation + Faction — parent design issue for both prerequisites |
| [#362](https://github.com/beyond-immersion/bannou-service/issues/362) | Seed bond dissolution endpoint — needed for ruling consequences that dissolve seed bonds |
| [#560](https://github.com/beyond-immersion/bannou-service/issues/560) | Contract ILocationClient hierarchy violation — affects handler pattern Arbitration relies on |
| [#153](https://github.com/beyond-immersion/bannou-service/issues/153) | Escrow asset transfer integration — affects asset division capability |
| [#615](https://github.com/beyond-immersion/bannou-service/issues/615) | **Master tracking** — Arbitration implementation Phases 2-6 (blocked by #601, #605) |
| [#664](https://github.com/beyond-immersion/bannou-service/issues/664) | CASCADE vs DETACH policy mismatch — x-references cleanup behavior contradicts declared policy (Bug #1) |
| [#666](https://github.com/beyond-immersion/bannou-service/issues/666) | Ruling consequence retry mechanism — unspecified re-invocation path for enforcement (DC #7) |

