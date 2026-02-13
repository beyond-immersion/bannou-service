# Dissolution, Sovereignty & Arbitration Design

> **Status**: Early design exploration
> **Date**: 2026-02-13
> **Related**: [#410](https://github.com/beyond-immersion/bannou-service/issues/410), [#362](https://github.com/beyond-immersion/bannou-service/issues/362)
> **Depends on**: [Morality System Next Steps](MORALITY-SYSTEM-NEXT-STEPS.md) (Phases 1-2 are prerequisites)

---

## Problem Statement

Characters in Arcadia form binding relationships -- marriages, guild memberships, business partnerships, household arrangements, divine covenants. These relationships are backed by contracts with behavioral clauses, and faction norms define cultural expectations around them.

**What happens when a character wants out?**

The current morality pipeline (Faction norms + Obligation costs + GOAP action modifiers) answers continuous questions well: "how costly is this action?" But dissolution is a **discrete, multi-step, multi-party process**. It's not "how guilty will I feel about divorcing?" -- it's "CAN I divorce, on WHOSE authority, under WHAT terms, with WHAT asset division, leaving WHAT ongoing obligations?"

The norm resolution hierarchy (guild > location > realm baseline, most specific wins) handles additive cost merging but breaks down when factions **contradict** each other on binary or procedural questions:

- Merchant Guild (character's guild): "Divorce permitted. Equal asset split."
- Temple of Solara (controls this location): "Divorce forbidden. Marriage is sacred."
- Arcadian Cultural Council (realm baseline): "Divorce permitted with arbitration."

"Most specific wins" says the guild norm prevails. But the Temple controls the territory the character lives in. A thief guild existing in a lawful city doesn't make theft free -- it just means the thief has a faction that doesn't penalize it. The city guard faction still does. Numerical merging cannot resolve procedural contradictions.

### The Scope of the Problem

The [Dungeon deep dive](../plugins/DUNGEON.md) identified that dissolution is not a single-use mechanic. It's a universal pattern:

| Scenario | Trigger | Contract Terms | Result |
|----------|---------|---------------|--------|
| **Branch family** | Household too large, members want independence | Asset division, territorial rights, trade agreements | Branch becomes NPC-managed, passive income/political benefits |
| **Divorce** | Character relationship dissolution | Property division, child custody, ongoing obligations | Characters split; personality/history/faction norms determine amicability |
| **Dungeon mastery** | Character bonds with dungeon core, player commits seed slot | Departure terms, household compensation, ongoing ties | Character leaves household; player gains dungeon_master account seed |
| **Exile/banishment** | Faction norm violation, family dishonor | Punitive terms, asset forfeiture, social stigma | Forced split; contentious by definition |
| **Religious vocation** | Character joins a divine order | Service terms, family tithe, visitation rights | Character serves deity; similar to dungeon Pattern A but with lib-divine integration |

All of these use the same underlying machinery: [Contract](../plugins/CONTRACT.md) (terms), [Faction](../plugins/FACTION.md) (cultural norms), [Obligation](../plugins/OBLIGATION.md) (ongoing moral costs), [Relationship](../plugins/RELATIONSHIP.md) (bond type changes), and [Seed](../plugins/SEED.md) (potential account seed creation if the player commits a slot). The specifics differ, but the separation mechanism is universal.

And dissolution is itself a special case of a broader pattern: **competing claims that need authoritative resolution**. Property disputes, criminal proceedings, trade disputes, custody/inheritance -- all follow the same shape: jurisdiction determination, procedural template selection, multi-party negotiation, authoritative ruling, enforcement.

---

## Current System Analysis

### What Exists

| System | Relevant Capability | Gap |
|--------|---------------------|-----|
| [**Contract**](../plugins/CONTRACT.md) (L1) | Template-based agreements with milestones, consent flows, breach handling, prebound API execution on state transitions, guardian locking, termination by parties | No concept of "dissolution procedure" -- termination is unilateral, not negotiated. No mechanism for one contract to spawn another (the dissolution contract from the marriage contract). |
| [**Escrow**](../plugins/ESCROW.md) (L4) | Multi-party asset division with 13-state FSM, arbiter-based dispute resolution, release modes (immediate/service_only/party_required), token-based authorization | Designed for exchanges, not separations. No integration with faction authority or norm-driven term constraints. Could serve as the asset division engine. |
| [**Faction**](../plugins/FACTION.md) (L4) | Seed-gated governance capabilities, norm definitions with violation types and base penalties, territory control, norm resolution hierarchy (guild > location > realm baseline) | Norms are pure cost modifiers. No concept of "procedures," "authority to arbitrate," or distinction between legal authority and social influence. Norm conflicts produce merged costs, not competing authority claims. |
| [**Obligation**](../plugins/OBLIGATION.md) (L4) | Contract-aware action cost modifiers, personality-weighted moral reasoning, violation reporting with breach feedback, `${obligations.*}` variable provider | Designed for per-tick GOAP cost modification. No concept of multi-step procedural obligations. Cannot distinguish legal penalties from social costs. The faction-to-obligation pipeline is still incomplete ([Integration Gap #1](MORALITY-SYSTEM-NEXT-STEPS.md)). |
| [**Relationship**](../plugins/RELATIONSHIP.md) (L2) | Entity-to-entity bonds with types, bidirectional uniqueness, soft-deletion with recreate capability | Status changes are instant. No lifecycle management for relationship transitions (married > separating > divorced). No integration with contracts or factions for state gating. |
| [**Seed**](../plugins/SEED.md) (L2) | Progressive growth, capability manifests, bonds between seeds, phase transitions | Seed bonds ([#362](https://github.com/beyond-immersion/bannou-service/issues/362)) needed for faction alliance mechanics. Bond *dissolution* mechanics not yet designed. |

### Critical Dependency: Morality Pipeline Completion

The dissolution system depends on the faction-to-obligation pipeline working. Per the [Morality System Next Steps](MORALITY-SYSTEM-NEXT-STEPS.md), the current integration gaps are:

1. **Critical**: Faction norms don't reach Obligation (faction knowledge is isolated)
2. **Critical**: Faction variable provider missing norm/territory data (`${faction.has_norm.<type>}`, `${faction.in_controlled_territory}`)
3. **Important**: No `evaluate_consequences` cognition stage implementation
4. **Moderate**: Guild charters lack contract backing
5. **Minor**: Personality trait mapping is static, not data-driven

Phases 1-2 of the morality roadmap (connecting faction norms to obligation, completing the faction variable provider) are prerequisites before dissolution mechanics can function.

---

## The Keystone: Faction Sovereignty

The open questions in the original analysis answer themselves once factions distinguish between **legal authority** and **social influence**. Today, all faction norms are equal -- they differ only in numerical weight and hierarchy position. But in the real world (and in any believable simulation), there is a categorical difference between "the city guard will arrest you" and "your neighbors will gossip about you."

### AuthorityLevel

A new field on FactionModel distinguishing three levels of authority:

| Level | Meaning | Enforcement Power |
|-------|---------|-------------------|
| **Influence** | Default. Social norms only. | Cannot arbitrate. Cannot enforce legally. Imposes GOAP cost modifiers and reputation damage. |
| **Delegated** | Authority granted by a sovereign. | Inherits sovereign's law. Can add local rules within scope of delegation. Can arbitrate within delegated jurisdiction. |
| **Sovereign** | Law-making authority. | Final word within controlled territory. Defines what is legal vs. illegal. Can arbitrate, exile, impose legal penalties. |

This is a schema change to `faction-api.yaml` -- a new field on the model, and an enhancement to `QueryApplicableNorms` to return authority source alongside norm data. [Obligation](../plugins/OBLIGATION.md) then tags costs as **legal** vs. **social** based on the source faction's authority level.

### Revised Norm Resolution

The norm resolution changes from "most specific wins" to an authority-aware hierarchy:

1. **Find the sovereign** in the territory hierarchy (walk up faction parents until Sovereign)
2. **Sovereign's norms = LAWS** (high enforcement weight, triggers guard/justice system)
3. **Delegated authority norms = LOCAL LAWS** (override sovereign on specifics, inherit on gaps)
4. **Influence norms = SOCIAL COSTS** (existing behavior -- GOAP cost modifiers, reputation damage)
5. **Guild membership norms = PERSONAL OBLIGATIONS** (existing behavior)
6. **A nested sovereign (enclave) overrides** the outer sovereign completely within its territory

The categorical distinction between legal and social is the key. When two factions contradict on dissolution rules, the answer isn't "most specific wins" -- it's "the sovereign's position is LAW, the influence faction's position is a social cost modifier." A thief guild that says "theft is fine" doesn't override the sovereign's law -- it just means the thief's guild membership doesn't add EXTRA cost for theft on top of the legal penalty.

### How Sovereignty Resolves the Open Questions

**Q3 (Jurisdiction)**: The sovereign authority in the territory has jurisdiction by default. A character can invoke a non-sovereign faction's procedures, but the sovereign's prohibitions still carry LEGAL weight (not just social cost). The distinction between a legal penalty and a social penalty is exactly the sovereignty flag.

**Q2 (Who Arbitrates)**: The sovereign authority arbitrates by default within its territory. `governance.arbitrate.*` capabilities are only meaningful for factions with Sovereign or Delegated authority level. An Influence-only faction can define dissolution preferences but can't arbitrate -- it can only impose social costs.

**Q4 (Forced Dissolution)**: Exile is inherently a sovereign act. Only a faction with Sovereign or Delegated authority can forcibly dissolve a membership. An Influence-only guild can kick you out (voluntary association), but only a sovereign can exile you from a territory with legal consequences.

**Q5 (Relationship Status Lifecycle)**: The intermediate states (`separating`, `on_probation`) are tracked by the dissolution contract, not by Relationship directly. The dissolution contract IS the state machine for the transition. This is consistent with how Contract already works -- Contract manages the lifecycle, Relationship records the final state change on fulfillment.

### IsRealmBaseline and Sovereignty

The existing `IsRealmBaseline` field on factions designates one faction per realm as the cultural baseline. With sovereignty, the realm baseline faction should automatically be Sovereign -- it's the realm-wide legal authority by definition:

- `DesignateRealmBaseline` should set `AuthorityLevel = Sovereign` (or validate that it's already Sovereign)
- A realm can have multiple Sovereign factions (the baseline + enclaves), but only one realm baseline
- The realm baseline is the **default sovereign** -- if no other sovereign controls a location, the realm baseline's laws apply
- This is already implicit in the norm resolution hierarchy (realm baseline is the fallback) but must be made explicit in the sovereignty model

### Enclave Sovereignty

Sovereignty applies at the location boundary. Nesting is naturally bounded by [lib-location](../plugins/LOCATION.md)'s hierarchy.

The real-world analogy is exact: step into an embassy compound and you're under different jurisdiction. Sovereignty follows territory, territory follows lib-location's parent/child hierarchy. A dwarven enclave controls a Location node within a human kingdom's Location node. When you cross into the enclave's location, the enclave's sovereignty applies.

Nesting depth is bounded by `MaxHierarchyDepth` on the faction hierarchy (currently 5), which is more than enough. In practice you'd rarely see more than 3 levels: realm sovereign > regional sovereign > enclave sovereign. The norm resolution algorithm walks up the faction parent chain looking for the first Sovereign, which is already a clean operation.

At sovereignty boundaries, the transition is instant -- same as location transitions already work for territory-controlling factions. `QueryApplicableNorms` already takes a `locationId` parameter. The sovereignty enhancement just changes how it interprets the controlling faction's authority level. No new spatial mechanics needed.

**Edge case**: A character standing at a sovereignty boundary (or moving through one) switches legal jurisdiction mid-action. This is fine -- the cognition pipeline re-evaluates costs per tick, and the variable provider re-resolves norms when location changes. A character who starts a theft in the kingdom and runs into the embassy mid-act faces the kingdom's legal consequences for the theft (it started in their jurisdiction) but the embassy's laws while inside. This naturally creates interesting gameplay around border mechanics.

### Sovereignty Acquisition

Multiple paths, all grounded in existing mechanics:

| Path | Mechanism | When |
|------|-----------|------|
| **Realm baseline** | `DesignateRealmBaseline` already exists. Extend it to automatically set `AuthorityLevel = Sovereign`. | Bootstrapping. Every realm needs at least one sovereign. |
| **Delegation** | A sovereign faction grants `Delegated` authority to a child faction or a faction that controls a sub-location. | A kingdom appoints a provincial governor; a temple district gets religious jurisdiction. |
| **Conquest/growth** | Seed-gated: requires a governance capability (e.g., `sovereignty.claim`) at a high growth phase (dominant). Requires controlling territory. | A faction that has grown powerful enough to assert independence. |
| **Treaty/recognition** | Via lib-arbitration: a case of type `sovereignty_recognition` adjudicated by the current sovereign or a divine arbiter. | Peaceful independence -- the enclave petitions for recognition. |
| **Divine mandate** | A regional watcher god (via [Puppetmaster](../plugins/PUPPETMASTER.md)) grants sovereignty as a divine act. | Theocratic sovereignty -- the god declares this faction the legitimate authority. |

The key principle from the [Vision](../../arcadia-kb/VISION.md): governance power is EARNED through member activity, not statically assigned. A nascent faction literally cannot claim sovereignty because it hasn't grown enough. A dominant faction with a `sovereignty.claim` capability CAN assert it, but doing so against an existing sovereign triggers... an arbitration case. Sovereignty disputes are themselves arbitrable. This is circular in exactly the right way -- it creates emergent political dynamics.

**For initial implementation**, only the first two paths matter (realm baseline = sovereign, delegation by a sovereign). Conquest and divine mandate are extensions that come naturally once the foundation exists.

### Implementation Scope

This is the smallest change that unlocks the most:

- Schema change: add `authorityLevel` field to `faction-api.yaml` (enum: `Influence`, `Delegated`, `Sovereign`)
- Model change: add field to `FactionModel`
- `DesignateRealmBaseline` change: automatically sets `AuthorityLevel = Sovereign`
- New API: delegation endpoint for sovereign factions to grant `Delegated` authority
- API enhancement: `QueryApplicableNorms` returns authority source alongside norm data
- Obligation enhancement: tag costs as `legal` vs. `social` based on source faction's authority level
- Seed capability gating: arbitration capabilities only meaningful for Sovereign/Delegated factions

---

## The Three Changes, Ordered

### 1. Faction Sovereignty (smallest, unlocks the most)

As described above. A schema change, a model field, and an enhancement to norm resolution and obligation cost tagging. This provides the principled foundation for everything that follows.

### 2. lib-arbitration (medium, enables dissolution and broader conflict resolution)

The original analysis's "Dissolution as Authority Contest" model is a specific case of a general pattern: **competing claims that need authoritative resolution**. Arbitration covers:

- **Dissolution/separation** (the original use case)
- **Contract conflicts** (guild charter vs. trade agreement)
- **Property disputes** (two factions claim the same territory)
- **Criminal proceedings** (sovereign authority vs. accused party)
- **Trade disputes** (buyer vs. seller, mediated by a merchant guild or sovereign)
- **Custody/inheritance** (who gets what when someone dies)

Option B from the original analysis is the right call, but scoped as **lib-arbitration** rather than lib-dissolution. Dissolution is one case type within the arbitration service, not a service unto itself.

#### Orchestration Pattern

```
lib-arbitration orchestrates:
  Faction (jurisdiction, authority level, procedural norms)
  + Contract (the thing being disputed, plus the arbitration contract itself)
  + Escrow (asset division when needed)
  + Obligation (ongoing obligations from rulings)
  + Relationship (status changes from rulings)
```

**Case types are opaque strings** (same pattern as violation types, collection types, seed types). `dissolution`, `property_dispute`, `criminal_proceeding`, `trade_dispute` are all just case types with different procedural templates. The arbitration service doesn't hardcode dissolution logic -- it provides the framework for any authoritative resolution process.

#### Procedural Templates: Faction References Contract Templates

Procedural templates follow the established pattern exactly. Faction norms already reference violation type codes (opaque strings) without embedding violation logic. Same principle: the sovereign faction stores governance data associating case types with contract template codes. The actual template (milestones, terms, prebound APIs) lives in [lib-contract](../plugins/CONTRACT.md) where it already belongs.

The flow:
1. Sovereign faction's governance data includes: `{ caseType: "dissolution", templateCode: "dissolution-standard", waitingPeriodDays: 30, ... }`
2. lib-arbitration reads the jurisdictional sovereign's governance data for the relevant case type
3. lib-arbitration instantiates the referenced contract template via lib-contract
4. The contract's milestones, consent flows, and prebound API execution handle the lifecycle

This means factions don't need a new data model for "procedures" -- they just need a way to associate case types with contract template codes, plus governance parameters (waiting period, division rules). That's a small extension to the existing norm data model: a new **procedural norm** type alongside cost norms, gated by `governance.arbitrate.*` seed capability and Sovereign/Delegated authority level.

The contract templates themselves are authored at deployment time (like dungeon-master-bond), not created dynamically. Different games configure different templates. Arcadia might have `dissolution-standard`, `dissolution-religious-annulment`, `dissolution-exile-punitive`. lib-arbitration doesn't know or care about the specifics -- it instantiates whatever template the jurisdiction references.

#### Layer & Dependencies

- **Layer**: L4 GameFeatures
- **Hard dependencies**: Contract (L1), Faction (L4), Relationship (L2)
- **Soft dependencies**: Escrow (L4, for asset division), Obligation (L4, for ongoing obligations), Currency (L2), Inventory (L2), Puppetmaster (L4, for divine arbitration), Seed (L2, for bond dissolution)

#### NPC Agency in Arbitration

An NPC with the `evaluate_consequences` cognition stage can autonomously decide to initiate arbitration. An unhappy NPC in a bad marriage evaluates:
- the cost of continuing vs.
- the cost of filing for dissolution under the local sovereign's authority vs.
- the cost of fleeing to a permissive jurisdiction

This is emergent narrative from the intersection of sovereignty + arbitration + cognition.

### 3. lib-organization (largest, enables living economy)

The biggest lift and the most independent. Organizations are entities that:

- **Own assets**: Currency wallets, Inventory containers, Locations
- **Enter contracts as parties**: Employment, trade agreements, partnerships
- **Have internal structure**: Roles, departments, reporting chains
- **Have succession rules**: Inheritance, elections, hostile takeover
- **Participate in the economy**: Revenue, expenses, payroll
- **Have legal status determined by the sovereign**: Chartered, licensed, outlawed

#### Why This Matters for the Vision

From the [Vision](../../arcadia-kb/VISION.md): the economy must be NPC-driven. NPCs running businesses -- shops, farms, workshops, trading companies -- is the substrate of the living economy. Without lib-organization, "NPC runs a shop" is just a behavior pattern with no structural backing. With lib-organization, the shop is a legal entity with inventory, a currency wallet, employees, and trade agreements -- and when the shopkeeper dies, succession rules determine what happens to it.

**Family-as-organization** is also critical: a household is an organization with shared assets, internal roles (parent/child/elder/heir), succession rules (primogeniture, equal division, matrilineal), and a legal status within the sovereign authority's framework. The [Dungeon deep dive](../plugins/DUNGEON.md)'s "household split" mechanic is really organization dissolution -- which flows through lib-arbitration under the sovereign's jurisdiction.

#### Legal Status from Sovereign

The sovereign determines an organization's legal standing. The mechanism:

- lib-organization stores legal status on each entity: `{ legalStatus, grantedBy: factionId, jurisdiction: locationId }`
- The sovereign (or delegated authority) sets this via an API call, probably through a contract (chartering is itself a legal act with terms and obligations)
- When sovereignty changes (new sovereign takes territory), the new sovereign can re-evaluate all organizations in its jurisdiction -- mass re-chartering, or mass outlawing

| Status | Meaning |
|--------|---------|
| **Chartered** | Officially recognized, protected by law |
| **Licensed** | Permitted to operate in specific domains |
| **Tolerated** | Not officially recognized but not outlawed |
| **Outlawed** | Operating illegally, subject to enforcement |

An Outlawed organization can still operate (it's a living world -- crime exists) but:
- Conducting business with it is itself a legal violation (adds to obligation costs)
- Its property is not protected by law (can be seized without legal consequence)
- Its members may face arrest
- It cannot enter contracts under the sovereign's jurisdiction (contracts require legal standing -- a design decision about whether lib-contract enforces this)

Legal status feeds into the organization's own seed growth: a Chartered organization grows faster (legitimate commerce feeds its seed), an Outlawed organization grows differently (underground economy, criminal reputation).

The chartering mechanism is itself a contract: the sovereign grants a charter (contract with milestones and behavioral clauses), the organization must maintain compliance (tax payments, regulatory adherence), failure to comply triggers breach > potential status downgrade from Chartered to Tolerated or Outlawed. This composes naturally with the existing Contract + Obligation pipeline.

#### Layer & Dependencies

- **Layer**: L4 GameFeatures
- **Hard dependencies**: Currency (L2), Inventory (L2), Contract (L1), Character (L2), Location (L2)
- **Soft dependencies**: Faction (L4, for legal status from sovereign), Seed (L2, for organizational growth), Relationship (L2, for membership bonds)

---

## How They Compose

```
NPC evaluates: "Should I divorce?"
    │
    ▼
Obligation: What are the costs? (existing pipeline)
    │
    ├── Sovereign faction LAWS: "Divorce permitted, 30-day wait, equal split"
    ├── Temple faction SOCIAL: "Divorce forbidden, violation cost: 50"
    ├── Marriage contract PERSONAL: Behavioral clauses from the wedding contract
    │
    ▼
NPC decides to proceed → initiates via lib-arbitration
    │
    ▼
Arbitration: jurisdiction = sovereign authority at character's location
    │
    ├── Creates dissolution contract from sovereign's template
    ├── Escrow for asset division (shared household assets via lib-organization)
    ├── Ongoing obligations (alimony, custody) as new contracts → obligation pipeline
    ├── Relationship status change (married → divorced)
    │
    ▼
Temple faction: violation event → reputation damage, potential exile from temple
Organization (household): succession/split per dissolution terms
Content flywheel: rich archive material from the entire process
```

The sovereignty flag is the keystone that makes the rest work. Without it, there's no principled way to distinguish "this faction disapproves" from "this faction will send guards." That distinction is what makes arbitration meaningful and what makes organizations operate within a legal framework.

---

## Detailed Design: Dissolution Flow (with Sovereignty + Arbitration)

### Step 1: Norm Discovery (Enhanced)

When a character initiates dissolution, the system queries all applicable factions. With sovereignty, the response is now categorized:

| Source | Authority | Response Type | Example |
|--------|-----------|--------------|---------|
| Sovereign faction at location | Sovereign | **LAW** (procedural template) | "Divorce permitted. 30-day waiting period. Equal split. File with magistrate." |
| Delegated authority (temple district) | Delegated | **LOCAL LAW** (procedural override or prohibition) | "Divorce requires temple annulment proceeding." |
| Guild membership (Merchant Guild) | Influence | **SOCIAL** (cost modifier + optional preference) | "Guild prefers mediated separation. No cost penalty." |
| Guild membership (Temple of Solara) | Influence | **SOCIAL** (prohibition as cost) | "Divorce forbidden. Violation cost: 50. Expulsion from temple." |
| Realm baseline | Sovereign (realm) | **DEFAULT LAW** (fallback) | "Divorce permitted with arbitration if no local sovereign rules." |

### Step 2: Authority Selection (Clarified by Sovereignty)

The sovereign's procedure is the **default jurisdiction**. Characters don't "choose a court" freely -- they file under the sovereign's authority unless:
- The sovereign has no position (silence) -- falls through to realm baseline
- A delegated authority has specific jurisdiction (temple district handles religious matters)
- The character flees to a different sovereign's territory (jurisdiction shopping, with consequences)

Invoking a non-sovereign faction's preferences is not "filing in their court" -- it's expressing a preference within the sovereign's framework. The Merchant Guild's preference for mediated separation is a request, not an authority.

### Step 3: The Arbitration Case

lib-arbitration creates a case:

- **Case type**: `dissolution` (opaque string)
- **Jurisdiction**: Sovereign (or delegated) faction at the location
- **Procedural template**: From the jurisdictional faction's governance data
- **Parties**: The entities being separated
- **Arbiter**: Determined by jurisdiction (sovereign-appointed NPC, faction leader, divine actor)
- **Contract**: A dissolution contract created from the procedural template
- **Escrow**: Created if asset division is required

### Step 4: Asset Division

If the parties share assets (via lib-organization or directly held):
- Assets enter escrow under the dissolution contract's terms
- Division rules come from the sovereign's procedural template
- Contested assets trigger the arbitration dispute flow
- The arbiter resolves per the sovereign's rules

### Step 5: Ruling and Enforcement

The arbitration produces a ruling (the dissolution contract reaching Fulfilled):
- Relationship status change
- Asset distribution via escrow release
- Ongoing obligation contracts created (custody, alimony)
- Norm violation events published (for factions that opposed the dissolution)
- Sovereign enforcement of the ruling (legal weight)
- Social consequences from opposing factions (influence weight)

---

## Resolved Design Decisions

### Q1: Dissolution Procedure Templates -- External Contract Templates Referenced by Code

Follows the established pattern exactly. Sovereign factions store governance data associating case types with contract template codes. The actual template lives in lib-contract. Factions gain a new "procedural norm" type alongside cost norms: `{ caseType, templateCode, governanceParameters }`, gated by `governance.arbitrate.*` capability and Sovereign/Delegated authority. Templates are authored at deployment time, not created dynamically. See the [Procedural Templates](#procedural-templates-faction-references-contract-templates) section under lib-arbitration for the full flow.

### Q9: Enclave Sovereignty -- Location Boundary, Naturally Bounded

Sovereignty applies at the location boundary, nesting bounded by lib-location's hierarchy and `MaxHierarchyDepth` (currently 5). Transition is instant at boundaries, same as existing territory-controlling faction transitions. No new spatial mechanics needed. See [Enclave Sovereignty](#enclave-sovereignty) under the Sovereignty section.

### Q10: Sovereignty Acquisition -- Multiple Paths

Realm baseline = automatic Sovereign. Delegation by a sovereign. Conquest via seed-gated capability. Treaty via lib-arbitration case. Divine mandate via Puppetmaster. Initial implementation: realm baseline + delegation only. See [Sovereignty Acquisition](#sovereignty-acquisition) under the Sovereignty section.

### Q11: Legal vs. Social -- Multi-Channel Obligation Costs

Multiple entries per violation type, tagged with authority level. Costs stack across channels; "most specific wins" applies within each channel.

| Channel | Source | Semantics | Consequence |
|---------|--------|-----------|-------------|
| **Legal** | Sovereign/Delegated faction norms | One per violation type (most specific sovereign wins within territory) | Guards, arrest, trial, imprisonment, fines |
| **Social** | Influence faction norms | One per violation type (most specific influence faction wins) | Reputation damage, gossip, encounter memories, relationship penalties |
| **Personal** | Direct contract behavioral clauses | Always stack (multiple contracts possible) | Contract breach, formal consequences per contract terms |

The obligation manifest entry becomes: `{ violationType, basePenalty, weightedPenalty, authorityLevel: Legal|Social|Personal, sourceFactionId?, sourceContractId? }`.

The GOAP planner sees the **total cost** (sum across channels) for action evaluation. But the `evaluate_consequences` cognition stage and post-violation feedback use the channel breakdown to trigger appropriate consequences:
- **Legal violation** > publishes event consumed by sovereign faction's enforcement system (guard NPCs react)
- **Social violation** > publishes event consumed by encounter/reputation systems (witnesses gossip)
- **Personal violation** > triggers contract breach via existing `BreachReportEnabled` mechanism

**Backward-compatible**: If no sovereign exists (faction sovereignty not yet implemented for a deployment), everything is social/personal as it is today. The legal channel only activates when a sovereign faction exists with norms for that violation type.

### Q12: Organization Legal Status -- Sovereign Determines It

The sovereign grants/revokes organizational legal status through a charter contract. See [Legal Status from Sovereign](#legal-status-from-sovereign) under the lib-organization section.

---

## Remaining Open Questions

### Q6: Service Hierarchy Placement for lib-arbitration

L4 GameFeatures seems right, alongside Escrow and Faction. Dependencies listed in the lib-arbitration section above.

### Q7: Does This Subsume Escrow?

Dissolution USES escrow as a component (like Quest uses Contract). Escrow's FSM handles asset division; arbitration handles the broader process (jurisdiction, procedural template, ruling, enforcement). They compose, they don't merge.

### Q8: NPC Agency in Arbitration

Covered in the lib-arbitration section. The `evaluate_consequences` cognition stage enables autonomous NPC decisions about initiating, contesting, or cooperating with arbitration proceedings.

### Q13: Contract Legal Standing

Can lib-contract enforce that an Outlawed organization cannot enter contracts under the sovereign's jurisdiction? This would mean contract creation validates the parties' legal standing via lib-organization + lib-faction. Implications: underground organizations can't use the formal legal system -- they need Escrow's full-custody mode or informal trust-based agreements. This creates a natural mechanical distinction between legal and illegal economies.

### Q14: Sovereignty Transfer on Conquest

When a new sovereign takes territory, what happens to:
- Existing arbitration cases in progress?
- Ongoing obligation contracts issued under the previous sovereign's authority?
- Organization charters granted by the previous sovereign?
- The deposed sovereign's Delegated authorities?

Mass re-evaluation could be a background process or an explicit administrative act by the new sovereign.

---

## Related Documents

### Deep Dives
- [Obligation](../plugins/OBLIGATION.md) -- Contract-aware action cost modifiers, violation reporting, `${obligations.*}` provider
- [Faction](../plugins/FACTION.md) -- Seed-based governance, norm hierarchy, territory control, `${faction.*}` provider
- [Contract](../plugins/CONTRACT.md) -- Template-based agreements, milestone progression, breach handling, prebound API execution
- [Escrow](../plugins/ESCROW.md) -- Multi-party asset exchanges, 13-state FSM, arbiter disputes, release modes
- [Relationship](../plugins/RELATIONSHIP.md) -- Entity-to-entity bonds, type taxonomy, soft-deletion
- [Dungeon](../plugins/DUNGEON.md) -- Household split mechanic table, Pattern A bonding as dissolution consumer
- [Seed](../plugins/SEED.md) -- Progressive growth, capability manifests, bond mechanics
- [Puppetmaster](../plugins/PUPPETMASTER.md) -- Regional watcher orchestration, divine actor coordination
- [Character](../plugins/CHARACTER.md) -- Foundational entity, household context

### Guides
- [Morality System](../guides/MORALITY-SYSTEM.md) -- Full pipeline architecture: Faction > Obligation > Actor cognition

### Planning
- [Morality System Next Steps](MORALITY-SYSTEM-NEXT-STEPS.md) -- Integration gaps and phased roadmap (Phases 1-2 are prerequisites)
- [Dungeon as Actor](DUNGEON-AS-ACTOR.md) -- Pattern A bonding and household departure mechanics
- [Economy/Currency Architecture](ECONOMY-CURRENCY-ARCHITECTURE.md) -- Asset management context

### Issues
- [#410](https://github.com/beyond-immersion/bannou-service/issues/410) -- Second Thoughts: Prospective Consequence Evaluation (parent feature for morality pipeline)
- [#362](https://github.com/beyond-immersion/bannou-service/issues/362) -- Seed Bond Dissolution (needed for faction alliance and relationship seed mechanics)

### Vision
- `arcadia-kb/VISION.md` -- Content Flywheel, emergent-over-authored principle, NPC-driven economies
- `arcadia-kb/PLAYER-VISION.md` -- Progressive agency, guardian spirit model, character co-pilot with moral resistance
